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
// the hand as a mission's IK turns it at the empty.
public sealed partial class StrategyDiorama
{
    private sealed class Grip
    {
        public Transform Upper, Lower, Hand, Target;
        // The hand keeps the turn the animation gave it, where a support hand takes its empty's.
        public bool KeepsTurn;
        // A right hand held clear of the head with what it carries, which has no empty to go to.
        public Carried Carried;
    }

    // Every grip is kept by the hand bone it solves, a support hand's and a carrying hand's alike.
    private static readonly Dictionary<IntPtr, Grip> Grips = new();
    // Mission carriers' hands, by hand. The armoury clears its own grips as it closes, and these
    // go when their units do.
    private static readonly Dictionary<IntPtr, Grip> MissionGrips = new();
    private static GameObject _gripDriver;
    private static bool _gripDriverRegistered;

    // Finds the grip for every staged unit that has a humanoid rig and a weapon_hand_l empty.
    private void TrackGrips()
    {
        foreach (var staged in _staging.StagedRoots)
        {
            if (staged.Root == null || !staged.Root.gameObject.activeInHierarchy)
                continue;
            foreach (var animator in staged.Root.GetComponentsInChildren<Animator>())
            {
                if (animator == null || !animator.isHuman)
                    continue;
                // The squad leader stands in the squad's first slot and keeps the pose her own
                // idle gives her: only her squaddies' support hands are brought to their rifles.
                if (InLeaderSlot(animator.transform, staged.Root))
                    continue;
                var target = FindDescendant(animator.transform.parent ?? animator.transform, "weapon_hand_l");
                var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                var hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                if (target == null || upper == null || lower == null || hand == null || Grips.ContainsKey(hand.Pointer))
                    continue;
                Grips[hand.Pointer] = new Grip { Upper = upper, Lower = lower, Hand = hand, Target = target };
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
        SolveGrips(Grips);
        SolveGrips(MissionGrips);
    }

    private static bool _gripFailReported;

    // A grip that throws is let go, so the others are still held on that frame and the frames after.
    private static void SolveGrips(Dictionary<IntPtr, Grip> grips)
    {
        List<IntPtr> dead = null;
        foreach (var (key, grip) in grips)
        {
            try
            {
                if (grip.Upper == null || grip.Lower == null || grip.Hand == null
                    || (grip.Carried == null ? grip.Target == null : grip.Carried.Unit == null || grip.Carried.Head == null || grip.Carried.Socket == null))
                {
                    (dead ??= new()).Add(key);
                    continue;
                }
                if (!grip.Hand.gameObject.activeInHierarchy)
                    continue;
                if (grip.Carried == null)
                    Solve(grip, N(grip.Target.position));
                else if (CarriedPlace(grip, out var place))
                {
                    // A support hand on the weapon is read before the weapon moves, and brought back
                    // to it after.
                    var support = grip.Carried.Support;
                    var gripping = support != null && support.Upper != null && support.Lower != null && support.Hand != null && support.Target != null
                        && Vec.Distance(N(support.Hand.position), N(support.Target.position)) < SupportGripReach * grip.Carried.Scale;
                    Solve(grip, place);
                    if (gripping)
                        Solve(support, N(support.Target.position));
                }
            }
            catch (Exception ex)
            {
                (dead ??= new()).Add(key);
                if (!_gripFailReported)
                {
                    _gripFailReported = true;
                    SystemContext?.Log.Warn($"strategy diorama: a grip failed and was let go: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        if (dead != null)
            foreach (var key in dead)
                grips.Remove(key);
    }

    // How near its weapon's weapon_hand_l a support hand is to count as on it, in metres at a
    // Doll's own scale.
    private const float SupportGripReach = 0.06f;

    // The maths runs in System.Numerics (Unity's is an interop call per operation, every frame):
    // the transforms are read once and the three rotations written back.
    private static void Solve(Grip grip, Vec t)
    {
        var s = N(grip.Upper.position);
        var e = N(grip.Lower.position);
        var h = N(grip.Hand.position);
        var held = grip.KeepsTurn ? grip.Hand.rotation : default;
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
        if (grip.KeepsTurn)
            grip.Hand.rotation = held;
        else
            grip.Hand.rotation = U(N(grip.Target.rotation) * GripTurn);
    }

    // A mission's IK turns the hand to the empty's rotation and then by this turn of its own,
    // the same for every unit and weapon (measured off units in a mission with Rig.Record).
    // The empties are placed against that, so the hand here takes it too.
    private static readonly Quat GripTurn = Quat.Normalize(new Quat(0.4912f, 0.5087f, 0.5087f, 0.4912f));

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
    private static Vector3 U(Vec v) => new(v.X, v.Y, v.Z);

    private static void EnsureGripDriver()
    {
        if (_gripDriver != null)
            return;
        try
        {
            if (!_gripDriverRegistered)
            {
                ClassInjector.RegisterTypeInIl2Cpp<GripDriver>();
                _gripDriverRegistered = true;
            }
            _gripDriver = new GameObject("WOMENACE.Grip");
            UnityEngine.Object.DontDestroyOnLoad(_gripDriver);
            _gripDriver.AddComponent(Il2CppType.Of<GripDriver>());
        }
        catch (Exception ex)
        {
            SystemContext?.Log.Warn($"strategy diorama: grip driver failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// Solves every grip in LateUpdate, after the Animators have posed their units: the armoury
// squaddies' support hands, the right hands that carry a tripod weapon clear of a Doll's head in
// the armoury, and the mission carriers' hands (TripodCarrySystem).
public sealed class GripDriver : MonoBehaviour
{
    public GripDriver(IntPtr pointer) : base(pointer) { }

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
