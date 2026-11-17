using System;
using System.Collections.Generic;
using Mat = System.Numerics.Matrix4x4;
using Vec = System.Numerics.Vector3;
using UnityEngine;

namespace WOMENACE.Code;

// The armoury shows a tripod weapon in its carrier's right hand with no tripod under it, where
// vanilla's soldier holds it beside his head. A Doll's head is larger than his, and the model
// passes through it. Her right hand is moved, with the weapon in it, until none of the weapon
// is in her head. It goes level and square to the weapon's length, to the side of her head the
// weapon's middle is already on, which is the shortest way a long thing leaves a ball: her head as a ball at its bone, the model as the triangles
// of its meshes, a rigged one's as its bones have posed them. The hand is held there by the
// solve that brings a squaddie's support hand to her rifle, so the weapon stays in it. Any
// other weapon is carried as the animation has it.
//
// How far the hand goes can only be worked out once she and the weapon have taken their pose,
// which is after they are first seen. The first time a Doll in an outfit is seen with a weapon
// the hand goes out to it smoothly, and what was worked out is kept, so from then on the hand
// is there from the frame she appears.
public sealed partial class StrategyDiorama
{
    private const string CarrySocket = "Hand_R_Socket";
    // A Doll's head at her own scale, in metres: the ball's middle above the head bone, and its
    // radius with her hair.
    private const float CarryHeadRise = 0.09f;
    private const float CarryHeadRadius = 0.12f;
    private const float CarryStep = 0.01f;
    private const float CarryReach = 0.25f;
    private const float CarrySettleSeconds = 0.8f;
    // A rigged model unfolds as it appears, and is read again once it has.
    private const float CarryRereadSeconds = 0.3f;
    private const float CarryEasing = 10f;
    // Vanilla's tripod weapons, by the part their templates' names share.
    private const string CarryTripodId = "tripod_";
    // How far out the hand was found to go, by Doll, outfit and weapon.
    private static readonly Dictionary<string, (float Out, Vec Way)> CarryKnown = new(StringComparer.Ordinal);

    private sealed class Carried
    {
        public Transform Unit, Head, Socket;
        public float Scale, Until, RereadAt, Out, Wanted;
        // The way the hand goes, as her right, up and forward.
        public Vec Way = Vec.UnitX;
        public string Model, Key;
        // Each model's triangles, middle and length's direction, in the model's own space.
        public readonly List<(Transform Model, Vec[] Corners, Vec Middle, Vec Along)> Models = new();
        public readonly List<string> Read = new();
    }

    // Holds the right hand of every staged Doll that has something in it.
    private void HoldCarried()
    {
        try
        {
            var leader = _squadWindow != null ? Affinity.LeaderOf(_squadWindow) : null;
            if (leader == null)
                return;
            var tag = Affinity.CharacterTag(leader);
            var weapon = leader.GetItems()?.GetItemAtSlot(Il2CppMenace.Items.ItemSlot.InfantrySpecial)?.GetTemplate();
            var id = weapon?.GetID();
            var shown = weapon?.TryCast<Il2CppMenace.Items.WeaponTemplate>()?.Model;
            if (tag == null || id == null || shown == null || !id.Contains(CarryTripodId, StringComparison.Ordinal))
                return;
            var key = $"{tag}|{Transmog.SelectionFor(Context, tag)}|{id}";
            var known = CarryKnown.TryGetValue(key, out var found);
            var held = false;
            foreach (var staged in _stagedRoots)
            {
                if (staged.Root == null || !staged.Root.gameObject.activeInHierarchy)
                    continue;
                foreach (var animator in staged.Root.GetComponentsInChildren<Animator>())
                {
                    if (animator == null || !animator.isHuman)
                        continue;
                    var head = animator.GetBoneTransform(HumanBodyBones.Head);
                    var upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    var lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                    var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    var socket = hand != null ? FindDescendant(hand, CarrySocket) : null;
                    if (head == null || upper == null || lower == null || socket == null || Grips.ContainsKey(hand.Pointer))
                        continue;
                    var carries = false;
                    for (var i = 0; i < socket.childCount && !carries; i++)
                        carries = ModelName(socket.GetChild(i)) == shown.name;
                    if (!carries)
                        continue;
                    var carried = new Carried
                    {
                        Unit = animator.transform,
                        Head = head,
                        Socket = socket,
                        Scale = animator.transform.lossyScale.y,
                        Until = known ? 0f : Time.unscaledTime + CarrySettleSeconds,
                        RereadAt = known ? 0f : Time.unscaledTime + CarryRereadSeconds,
                        Out = found.Out,
                        Wanted = found.Out,
                        Way = known ? found.Way : Vec.UnitX,
                        Model = shown.name,
                        Key = key,
                    };
                    if (!known)
                        ReadCarried(carried);
                    Grips[hand.Pointer] = new Grip { Upper = upper, Lower = lower, Hand = hand, KeepsTurn = true, Carried = carried };
                    held = true;
                }
            }
            if (held)
                EnsureGripDriver();
        }
        catch (Exception ex) { Context.Log.Warn($"strategy diorama: carried weapons not held: {ex.GetType().Name}: {ex.Message}"); }
    }

    // Where the hand is to be this frame, from where the animation has just put it. False where
    // it stays there.
    private static bool CarriedPlace(Grip grip, out Vec place)
    {
        var carried = grip.Carried;
        // A hand that has settled where the animation has it is asked nothing more.
        if (carried.Until == 0f && carried.Out == 0f && carried.Wanted == 0f)
        {
            place = default;
            return false;
        }
        var (right, up, forward) = (Vec.Normalize(N(carried.Unit.right)), Vec.Normalize(N(carried.Unit.up)), Vec.Normalize(N(carried.Unit.forward)));
        var now = Time.unscaledTime;
        if (carried.Until > 0f && now >= carried.Until)
        {
            carried.Until = 0f;
            CarryKnown[carried.Key] = (carried.Wanted, carried.Way);
            carried.Models.Clear();
        }
        if (carried.Until > 0f)
        {
            if (carried.RereadAt > 0f && now >= carried.RereadAt)
            {
                carried.RereadAt = 0f;
                ReadCarried(carried);
            }
            var centre = N(carried.Head.position) + up * (CarryHeadRise * carried.Scale);
            var way = CarriedWay(carried, centre, right, up);
            carried.Way = new Vec(Vec.Dot(way, right), Vec.Dot(way, up), Vec.Dot(way, forward));
            var furthest = 0f;
            foreach (var (model, corners, _, _) in carried.Models)
                if (model != null)
                    furthest = MathF.Max(furthest, Clearance(model, corners, centre, CarryHeadRadius * carried.Scale, way, carried.Scale));
            carried.Wanted = furthest;
        }
        var outward = right * carried.Way.X + up * carried.Way.Y + forward * carried.Way.Z;
        if (carried.Out != carried.Wanted)
        {
            carried.Out += (carried.Wanted - carried.Out) * (1f - MathF.Exp(-CarryEasing * Time.unscaledDeltaTime));
            if (MathF.Abs(carried.Wanted - carried.Out) < 0.0005f)
                carried.Out = carried.Wanted;
        }
        place = N(grip.Hand.position) + outward * carried.Out;
        return carried.Out > 0f;
    }

    // The way out of her head for what the hand carries: from her head's middle to the biggest
    // model's, with what is up and what is along the model's length taken out of it. Her right
    // where the model's length passes through the middle of her head and gives no side.
    private static Vec CarriedWay(Carried carried, Vec centre, Vec right, Vec up)
    {
        var (way, most) = (right, 0);
        foreach (var (model, corners, middle, along) in carried.Models)
        {
            if (model == null || corners.Length <= most)
                continue;
            most = corners.Length;
            var toWorld = ToMat(model.localToWorldMatrix);
            var aside = Vec.Transform(middle, toWorld) - centre;
            aside -= up * Vec.Dot(aside, up);
            var length = Vec.TransformNormal(along, toWorld);
            length -= up * Vec.Dot(length, up);
            if (length.LengthSquared() > 1e-8f)
            {
                length = Vec.Normalize(length);
                aside -= length * Vec.Dot(aside, length);
            }
            way = aside.Length() > 0.01f * carried.Scale ? Vec.Normalize(aside) : right;
        }
        return way;
    }

    // The direction a set of points is longest in: the one their spread is greatest along,
    // found by turning a direction through the spread until it stops changing.
    private static (Vec Middle, Vec Along) Lengthways(List<Vec> points)
    {
        var middle = Vec.Zero;
        foreach (var point in points)
            middle += point;
        middle /= points.Count;
        float xx = 0f, xy = 0f, xz = 0f, yy = 0f, yz = 0f, zz = 0f;
        foreach (var point in points)
        {
            var d = point - middle;
            xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z;
            yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
        }
        var along = Vec.Normalize(new Vec(1f, 0.7f, 0.4f));
        for (var i = 0; i < 24; i++)
        {
            var turned = new Vec(xx * along.X + xy * along.Y + xz * along.Z, xy * along.X + yy * along.Y + yz * along.Z, xz * along.X + yz * along.Y + zz * along.Z);
            if (turned.LengthSquared() < 1e-20f)
                break;
            along = Vec.Normalize(turned);
        }
        return (middle, along);
    }

    // The triangles of everything in the hand, each model's in its own space, where they keep
    // their places as the hand moves.
    private static void ReadCarried(Carried carried)
    {
        carried.Models.Clear();
        carried.Read.Clear();
        for (var i = 0; i < carried.Socket.childCount; i++)
        {
            var model = carried.Socket.GetChild(i);
            if (ModelName(model) != carried.Model)
                continue;
            // One level of detail of each part is enough, and the coarsest is the least to go through.
            var coarsest = new Dictionary<string, (Renderer Renderer, int Level)>(StringComparer.Ordinal);
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer.transform.lossyScale.sqrMagnitude < 1e-6f
                    || (renderer.TryCast<MeshRenderer>() == null && renderer.TryCast<SkinnedMeshRenderer>() == null))
                    continue;
                var (part, level) = (renderer.name, 0);
                var at = part.LastIndexOf("_LOD", StringComparison.Ordinal);
                if (at >= 0 && int.TryParse(part.AsSpan(at + 4), out var parsed))
                    (part, level) = (part[..at], parsed);
                if (!coarsest.TryGetValue(part, out var known) || level > known.Level)
                    coarsest[part] = (renderer, level);
            }
            var corners = new List<Vec>();
            var toModel = ToMat(model.worldToLocalMatrix);
            foreach (var (part, (renderer, _)) in coarsest)
                ReadTriangles(part, renderer, toModel, corners, carried.Read);
            if (corners.Count > 0)
            {
                var (middle, along) = Lengthways(corners);
                carried.Models.Add((model, corners.ToArray(), middle, along));
            }
        }
    }

    // A rigged part is baked as its bones hold it now, and what its bones have folded away to
    // nothing is left out: such triangles have no area, and are where they are for no reason of
    // the weapon's shape. A part whose mesh the game does not keep readable is taken as the box
    // round it.
    private static void ReadTriangles(string part, Renderer renderer, Mat toModel, List<Vec> into, List<string> read)
    {
        Mesh mesh = null, baked = null;
        try
        {
            if (renderer.TryCast<SkinnedMeshRenderer>() is { } rigged)
            {
                baked = new Mesh();
                rigged.BakeMesh(baked, true);
                mesh = baked;
            }
            else
                mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null)
            {
                read.Add($"{part} has no mesh");
                return;
            }
            var place = ToMat(renderer.transform.localToWorldMatrix) * toModel;
            if (!mesh.isReadable)
            {
                var (low, high) = (N(mesh.bounds.min), N(mesh.bounds.max));
                var box = new Vec[8];
                for (var i = 0; i < 8; i++)
                    box[i] = Vec.Transform(new Vec((i & 1) == 0 ? low.X : high.X, (i & 2) == 0 ? low.Y : high.Y, (i & 4) == 0 ? low.Z : high.Z), place);
                foreach (var corner in BoxCorners)
                    into.Add(box[corner]);
                read.Add($"{part} as its box");
                return;
            }
            var vertices = mesh.vertices;
            var placed = new Vec[vertices.Length];
            for (var i = 0; i < placed.Length; i++)
                placed[i] = Vec.Transform(N(vertices[i]), place);
            var corners = mesh.triangles;
            var kept = 0;
            for (var i = 0; i + 2 < corners.Length; i += 3)
            {
                var (a, b, c) = (placed[corners[i]], placed[corners[i + 1]], placed[corners[i + 2]]);
                if (Vec.Cross(b - a, c - a).LengthSquared() < FoldedAway)
                    continue;
                into.Add(a);
                into.Add(b);
                into.Add(c);
                kept++;
            }
            read.Add($"{part} {(baked != null ? "rigged" : "fixed")} {kept} of {corners.Length / 3} triangles");
        }
        catch (Exception ex) { read.Add($"{part} failed {ex.GetType().Name}"); }
        finally
        {
            if (baked != null)
                UnityEngine.Object.Destroy(baked);
        }
    }

    // How far the model has to go along the way given for none of its triangles to reach into
    // the ball. The ball is taken into the model's space, where the triangles are, and goes the
    // other way in place of the model going this way. A model is one size in every direction,
    // so a length in its space is the world's length by one factor.
    private static float Clearance(Transform model, Vec[] corners, Vec centre, float radius, Vec outward, float scale)
    {
        var toModel = ToMat(model.worldToLocalMatrix);
        var start = Vec.Transform(centre, toModel);
        var way = Vec.TransformNormal(outward, toModel);
        var factor = way.Length();
        var reach = radius * factor;
        var (step, limit) = (CarryStep * scale, CarryReach * scale);
        for (var slid = 0f; slid <= limit; slid += step)
        {
            var at = start - way * slid;
            var clear = true;
            for (var i = 0; i + 2 < corners.Length && clear; i += 3)
                clear = Vec.DistanceSquared(NearestOnTriangle(at, corners[i], corners[i + 1], corners[i + 2]), at) >= reach * reach;
            if (clear)
                return slid;
        }
        return limit;
    }

    // Twice a triangle's area, squared, under which it is one folded away.
    private const float FoldedAway = 1e-12f;

    // A box's twelve triangles, by its corners as ReadTriangles numbers them.
    private static readonly int[] BoxCorners =
    {
        0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1,
        2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3,
    };

    // The point of a triangle nearest to a point (Ericson, Real-Time Collision Detection 5.1.5).
    private static Vec NearestOnTriangle(Vec p, Vec a, Vec b, Vec c)
    {
        var (ab, ac, ap) = (b - a, c - a, p - a);
        var (d1, d2) = (Vec.Dot(ab, ap), Vec.Dot(ac, ap));
        if (d1 <= 0f && d2 <= 0f)
            return a;
        var bp = p - b;
        var (d3, d4) = (Vec.Dot(ab, bp), Vec.Dot(ac, bp));
        if (d3 >= 0f && d4 <= d3)
            return b;
        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            return a + ab * (d1 / (d1 - d3));
        var cp = p - c;
        var (d5, d6) = (Vec.Dot(ab, cp), Vec.Dot(ac, cp));
        if (d6 >= 0f && d5 <= d6)
            return c;
        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            return a + ac * (d2 / (d2 - d6));
        var va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        var denominator = 1f / (va + vb + vc);
        return a + ab * (vb * denominator) + ac * (vc * denominator);
    }

    private static string ModelName(Transform model)
    {
        var name = model.name;
        return name.EndsWith("(Clone)", StringComparison.Ordinal) ? name[..^"(Clone)".Length] : name;
    }

    // Unity's matrices multiply a column on their right and System.Numerics' a row on their
    // left, so the one is the other turned over.
    private static Mat ToMat(Matrix4x4 m) => new(
        m.m00, m.m10, m.m20, m.m30,
        m.m01, m.m11, m.m21, m.m31,
        m.m02, m.m12, m.m22, m.m32,
        m.m03, m.m13, m.m23, m.m33);
}
