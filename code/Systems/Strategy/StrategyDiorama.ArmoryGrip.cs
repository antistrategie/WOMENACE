using System;
using System.Collections.Generic;
using Quat = System.Numerics.Quaternion;
using Vec = System.Numerics.Vector3;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace WOMENACE.Code;

// The armoury's preview units carry only an Animator: no left-hand IK, so the support hand goes
// where the preview idle puts it, which suits a vanilla soldier's arms and misses a doll's rifle.
// Tactical units grip through the game's IK onto the weapon's weapon_hand_l empty. Here each
// staged squaddie gets the same grip after its Animator has posed it: a two-bone solve that brings
// the left hand to weapon_hand_l, keeps the elbow on the side the animation bent it, and turns
// the hand to the empty's rotation.
public sealed partial class StrategyDiorama
{
    private sealed class Grip
    {
        public Transform Upper, Lower, Hand, Target;
    }

    private static readonly Dictionary<IntPtr, Grip> Grips = new();
    private static GameObject _gripDriver;
    private static bool _gripDriverRegistered;

    // Finds the grip for every staged unit that has a humanoid rig and a weapon_hand_l empty.
    private void TrackGrips()
    {
        foreach (var staged in _stagedRoots)
        {
            if (staged.Root == null || !staged.Root.gameObject.activeInHierarchy)
                continue;
            foreach (var animator in staged.Root.GetComponentsInChildren<Animator>())
            {
                if (animator == null || Grips.ContainsKey(animator.Pointer))
                    continue;
                if (!animator.isHuman)
                    continue;
                // The squad leader stands in the squad's first slot and keeps the pose her own
                // idle gives her: only her squaddies' support hands are brought to their rifles.
                if (InLeaderSlot(animator.transform, staged.Root))
                    continue;
                var target = FindDescendant(animator.transform.parent ?? animator.transform, "weapon_hand_l");
                var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                var hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                if (target == null || upper == null || lower == null || hand == null)
                    continue;
                Grips[animator.Pointer] = new Grip { Upper = upper, Lower = lower, Hand = hand, Target = target };
            }
        }
        if (Grips.Count > 0)
            EnsureGripDriver();
    }

    private const string LeaderSlot = "Unit_01";

    private static bool InLeaderSlot(Transform unit, Transform squad)
    {
        for (var t = unit; t != null && t != squad; t = t.parent)
            if (t.name == LeaderSlot)
                return true;
        return false;
    }

    private static void ClearGrips() => Grips.Clear();

    private static Transform FindDescendant(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDescendant(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    internal static void SolveGrips()
    {
        List<IntPtr> dead = null;
        foreach (var (key, grip) in Grips)
        {
            if (grip.Upper == null || grip.Lower == null || grip.Hand == null || grip.Target == null)
            {
                (dead ??= new()).Add(key);
                continue;
            }
            if (grip.Hand.gameObject.activeInHierarchy)
                Solve(grip);
        }
        if (dead != null)
            foreach (var key in dead)
                Grips.Remove(key);
    }

    // The maths runs in System.Numerics (Unity's is an interop call per operation, every frame):
    // the transforms are read once and the three rotations written back.
    private static void Solve(Grip grip)
    {
        var s = N(grip.Upper.position);
        var e = N(grip.Lower.position);
        var h = N(grip.Hand.position);
        var t = N(grip.Target.position);
        var a = Vec.Distance(s, e);
        var b = Vec.Distance(e, h);
        // A grip on the shoulder itself gives no direction to reach in.
        if (a < 1e-4f || b < 1e-4f || Vec.DistanceSquared(s, t) < 1e-8f)
            return;
        var c = Math.Clamp(Vec.Distance(s, t), Math.Abs(a - b) + 1e-3f, a + b - 1e-3f);

        // Elbow: open or close it to the angle that puts the hand at the target's distance, about
        // the axis the animation already bends it around (turning about cross(shoulder, hand), both
        // from the elbow, by a positive angle opens it).
        var want = MathF.Acos(Math.Clamp((a * a + b * b - c * c) / (2f * a * b), -1f, 1f));
        var have = Angle(s - e, h - e);
        var axis = Vec.Cross(s - e, h - e);
        if (axis.LengthSquared() < 1e-8f)
            axis = Vec.Cross(s - e, -Vec.UnitY);
        // An arm hanging straight down has neither axis, and is left as the animation has it.
        var turn = axis.LengthSquared() < 1e-8f
            ? Quat.Identity
            : Quat.CreateFromAxisAngle(Vec.Normalize(axis), want - have);
        h = e + Vec.Transform(h - e, turn);

        // Shoulder: swing the arm so the hand reaches the target, then roll it about the shoulder-to-
        // target line to put the elbow back where the animation had it.
        var swing = FromTo(h - s, t - s);
        var swungElbow = s + Vec.Transform(e - s, swing);
        var line = Vec.Normalize(t - s);
        var was = e - s - line * Vec.Dot(e - s, line);
        var now = swungElbow - s - line * Vec.Dot(swungElbow - s, line);
        var roll = Quat.Identity;
        if (was.LengthSquared() > 1e-8f && now.LengthSquared() > 1e-8f)
        {
            var angle = Angle(now, was);
            roll = Quat.CreateFromAxisAngle(line, Vec.Dot(Vec.Cross(now, was), line) < 0f ? -angle : angle);
        }

        grip.Lower.rotation = U(turn * N(grip.Lower.rotation));
        grip.Upper.rotation = U(roll * swing * N(grip.Upper.rotation));
        grip.Hand.rotation = grip.Target.rotation;
    }

    private static float Angle(Vec from, Vec to)
    {
        var d = MathF.Sqrt(from.LengthSquared() * to.LengthSquared());
        return d < 1e-12f ? 0f : MathF.Acos(Math.Clamp(Vec.Dot(from, to) / d, -1f, 1f));
    }

    private static Quat FromTo(Vec from, Vec to)
    {
        if (from.LengthSquared() < 1e-12f || to.LengthSquared() < 1e-12f)
            return Quat.Identity;
        from = Vec.Normalize(from);
        to = Vec.Normalize(to);
        var d = Vec.Dot(from, to);
        if (d > 0.999999f)
            return Quat.Identity;
        if (d < -0.999999f)
        {
            var side = Vec.Cross(Vec.UnitX, from);
            if (side.LengthSquared() < 1e-6f)
                side = Vec.Cross(Vec.UnitY, from);
            return Quat.CreateFromAxisAngle(Vec.Normalize(side), MathF.PI);
        }
        var cross = Vec.Cross(from, to);
        return Quat.Normalize(new Quat(cross.X, cross.Y, cross.Z, 1f + d));
    }

    private static Vec N(Vector3 v) => new(v.x, v.y, v.z);
    private static Quat N(Quaternion q) => new(q.x, q.y, q.z, q.w);
    private static Quaternion U(Quat q) => new(q.X, q.Y, q.Z, q.W);

    private void EnsureGripDriver()
    {
        if (_gripDriver != null)
            return;
        try
        {
            if (!_gripDriverRegistered)
            {
                ClassInjector.RegisterTypeInIl2Cpp<ArmoryGripDriver>();
                _gripDriverRegistered = true;
            }
            _gripDriver = new GameObject("WOMENACE.ArmoryGrip");
            UnityEngine.Object.DontDestroyOnLoad(_gripDriver);
            _gripDriver.AddComponent(Il2CppType.Of<ArmoryGripDriver>());
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: grip driver failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// Solves the armoury units' left-hand grips in LateUpdate, after their Animators have posed them.
public sealed class ArmoryGripDriver : MonoBehaviour
{
    public ArmoryGripDriver(IntPtr pointer) : base(pointer) { }

    private static bool _reported;

    public void LateUpdate()
    {
        try { StrategyDiorama.SolveGrips(); }
        catch (Exception ex)
        {
            if (!_reported)
            {
                _reported = true;
                StrategyDiorama.SystemContext?.Log.Warn($"strategy diorama: grip solve failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
