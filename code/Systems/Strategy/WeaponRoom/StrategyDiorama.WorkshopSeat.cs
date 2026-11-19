using Il2CppMenace.Items;
using Jiangyu.Game.Audio;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using UnityEngine;

namespace WOMENACE.Code;

// The doll whose weapon is being calibrated, seated in the weapon room's lounge with it: where she
// sits, how her feet are found and how she leans back to keep them off the floor.
public sealed partial class StrategyDiorama
{
    // The armchair on the left of the calibration shot, which the side panels leave in view: the
    // middle of its seat and the seat's height, in the weapon room's space. It faces the table,
    // along -z.
    private static readonly Vector3 LoungeSeat = new(-24.75f, 19.69f, -93.95f);
    private static readonly Quaternion LoungeSeatFacing = Quaternion.Euler(0f, 180f, 0f);
    // From the seat's middle to its front edge, and how far past that edge a sitter's knees are.
    private const float LoungeSeatHalfDepth = 0.47f;
    private const float KneesPastSeat = 0.05f;
    private const float AnklesPastSeat = 0.07f;
    // How far over the seat the middle of a thigh is where it crosses the seat's front edge.
    private const float ThighOverSeat = 0.045f;
    // Where a doll with no sitting loop stands instead: between the table and the other
    // armchair, turned to the hall.
    private static readonly Vector3 LoungeStand = new(-24.3f, 19.19f, -96.6f);
    // The lounge's floor height, in the weapon room's space.
    private const float LoungeFloor = 19.19f;
    // How far a seated doll's soles stay above it, and how far back she may lean to keep them there.
    private const float SoleClearance = 0.005f;
    private const float MaxSeatedLean = 15f;
    // How far a sitter's hip joints are above what she sits on.
    private const float SeatedHipHeight = 0.09f;
    private const int CalibrationDollRelightFrames = 30;

    // The doll whose weapon is being calibrated sits in the lounge with it.
    private void ShowCalibrationDoll()
    {
        var lit = false;
        var room = _weaponSet.Root;
        var instance = WorkshopStation == CalibrateStation ? CalibrationUISystem.Instance?.Picked : null;
        var leader = instance?.Leader;
        // The doll who carries the weapon, or for one in stock the doll whose weapon it is.
        // Her tag is read when the leader or the weapon picked changes, and not on every look.
        var picked = leader != null ? leader.Pointer : IntPtr.Zero;
        var weapon = leader == null ? instance?.BaseWeaponId : null;
        if (picked != _weaponRoom.CalibrationLeader || weapon != _weaponRoom.CalibrationWeapon)
            (_weaponRoom.CalibrationLeader, _weaponRoom.CalibrationWeapon, _weaponRoom.CalibrationTag) = (picked, weapon,
                leader != null ? Affinity.CharacterTag(leader) : weapon != null ? Calibration.OwnerTagFor(weapon) : null);
        var tag = _weaponRoom.CalibrationTag;
        if (tag != _weaponRoom.CalibrationDollTag || (tag != null && _weaponRoom.CalibrationDoll == null))
        {
            _weaponRoom.CalibrationDollTag = tag;
            if (_weaponRoom.CalibrationDoll != null)
            {
                _life.CrewFidgets.Remove(_weaponRoom.CalibrationDoll);
                _life.CrewSeatShifts.Remove(_weaponRoom.CalibrationDoll);
                _life.DollRigs.Remove(_weaponRoom.CalibrationDoll.Pointer);
                UnityEngine.Object.Destroy(_weaponRoom.CalibrationDoll);
            }
            _weaponRoom.CalibrationDoll = null;
            var doll = tag != null && room != null ? SpawnDoll(tag, "WOMENACE_CalibrationDoll") : null;
            if (doll == null)
                return;
            _weaponRoom.CalibrationDoll = doll;
            doll.transform.SetParent(room.transform, false);
            doll.transform.localScale = Vector3.one * RoomDollScale;
            var body = doll.name.Substring(doll.name.IndexOf(':') + 1);
            // She sits in her own sitting loop or her stand-in's, which Pose finds. A doll with
            // neither sits in the loop every doll falls back on, where Pose would stand her: the
            // armchair is for sitting in. Her own animator is asked which she has, since the ops
            // room's pose probe is off with its room.
            Pose(doll, "Sit");
            var animator = doll.GetComponentInChildren<Animator>();
            var sitter = PoseStandIns.GetValueOrDefault(body) ?? body;
            bool Has(string who) => animator != null && animator.HasState(0, Animator.StringToHash($"Base Layer.{who}-Sit"));
            var sits = Has(body) || Has(sitter);
            if (!sits && Has(PoseFallback))
            {
                _life.CrewFidgets.Remove(doll);
                animator.Play(Animator.StringToHash($"Base Layer.{PoseFallback}-Sit"), 0, UnityEngine.Random.value);
                sits = true;
            }
            if (sits)
            {
                doll.transform.localRotation = LoungeSeatFacing;
                doll.transform.localPosition = LoungeSeat;
                // Her round shadow is for a floor under her feet. Seated it would hang in the
                // chair, which the room's ambient occlusion seats her in.
                foreach (var renderer in doll.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name == ContactShadowName)
                        renderer.enabled = false;
                // She is seated off her own pose: her hip joints at their height over the seat,
                // and as far forward as her legs need. The seat is deeper than her thighs are
                // long and her loop tucks a foot back, so her knees and her ankles both go past
                // the seat's front edge, and her thighs clear the cushion where they cross it.
                var legs = new[]
                {
                    (animator?.GetBoneTransform(HumanBodyBones.LeftUpperLeg), animator?.GetBoneTransform(HumanBodyBones.LeftLowerLeg), animator?.GetBoneTransform(HumanBodyBones.LeftFoot)),
                    (animator?.GetBoneTransform(HumanBodyBones.RightUpperLeg), animator?.GetBoneTransform(HumanBodyBones.RightLowerLeg), animator?.GetBoneTransform(HumanBodyBones.RightFoot)),
                };
                if (animator != null && animator.isHuman && legs.All(leg => leg.Item1 != null && leg.Item2 != null && leg.Item3 != null))
                {
                    animator.Update(0f);
                    Vector3 Own(Transform joint) => doll.transform.InverseTransformPoint(joint.position) * RoomDollScale;
                    var hips = (Own(legs[0].Item1) + Own(legs[1].Item1)) * 0.5f;
                    var forward = float.NegativeInfinity;
                    foreach (var (_, knee, ankle) in legs)
                        forward = Mathf.Max(forward, Mathf.Max(LoungeSeatHalfDepth + KneesPastSeat - Own(knee).z, LoungeSeatHalfDepth + AnklesPastSeat - Own(ankle).z));
                    var lift = 0f;
                    foreach (var (hip, knee, _) in legs)
                    {
                        var (from, to) = (Own(hip), Own(knee));
                        var along = Mathf.Clamp01((LoungeSeatHalfDepth - (forward + from.z)) / Mathf.Max(to.z - from.z, 0.01f));
                        var over = Mathf.Lerp(from.y, to.y, along) - hips.y + SeatedHipHeight;
                        lift = Mathf.Max(lift, ThighOverSeat - over);
                    }
                    doll.transform.localPosition = LoungeSeat + LoungeSeatFacing * new Vector3(-hips.x, SeatedHipHeight + lift - hips.y, forward);
                    ClearOfFloor(doll, doll.transform.localPosition + LoungeSeatFacing * hips);
                }
            }
            else
            {
                doll.transform.localRotation = Quaternion.Euler(0f, -70f, 0f);
                doll.transform.localPosition = LoungeStand;
            }
            // The room's own light on her, as on every doll in a room, and her rig at once.
            ProbeLight(doll, _weaponSet);
            lit = true;
        }
        if (_weaponRoom.CalibrationDoll != null && (lit || Time.frameCount % CalibrationDollRelightFrames == 0))
            LightDoll(_weaponRoom.CalibrationDoll, SquadLightScale);
    }

    // The lowest point of a doll's feet as they are posed now, in the weapon room's space: her full
    // detail meshes baked in their pose, counting only the vertices near her feet. A long skirt
    // hangs below a seated doll's soles, and the floor is under her shoes, not her hem. A vertex
    // is near a foot within a part of her shin's length of the line from her ankle to her toes,
    // which takes in a shoe's sole and heel. Only the humanoid joints are read: a rig can hang
    // other chains under a foot that reach far from it. A rig without humanoid feet is measured
    // whole.
    private Vector3 Lowest(GameObject doll) => LowestPoints(doll).feet;

    // How far from the line through a foot a vertex still counts as the foot's, in shin lengths.
    private const float FootReach = 0.3f;

    // The lowest point of her feet, and of her whole body for the dev verb.
    private (Vector3 feet, Vector3 whole) LowestPoints(GameObject doll)
    {
        var room = _weaponSet.Root.transform;
        var feet = new List<(Vector3 ankle, Vector3 toes, float reach)>();
        var animator = doll.GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
            foreach (var (shinBone, footBone, toesBone) in new[]
            {
                (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes),
                (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes),
            })
            {
                var (knee, foot, toes) = (animator.GetBoneTransform(shinBone), animator.GetBoneTransform(footBone), animator.GetBoneTransform(toesBone));
                if (knee == null || foot == null)
                    continue;
                var ankle = room.InverseTransformPoint(foot.position);
                var shin = (room.InverseTransformPoint(knee.position) - ankle).magnitude;
                feet.Add((ankle, toes != null ? room.InverseTransformPoint(toes.position) : ankle, shin * FootReach));
            }
        var lowest = new Vector3(0f, float.PositiveInfinity, 0f);
        var lowestAny = lowest;
        foreach (var renderer in doll.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (renderer.name.Contains("_LOD") && !renderer.name.EndsWith("_LOD0"))
                continue;
            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked, false);
                var m = room.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                // Transformed by hand: Matrix4x4's own methods are calls into the game, once a vertex.
                foreach (var vertex in baked.vertices)
                {
                    var at = new Vector3
                    {
                        x = m.m00 * vertex.x + m.m01 * vertex.y + m.m02 * vertex.z + m.m03,
                        y = m.m10 * vertex.x + m.m11 * vertex.y + m.m12 * vertex.z + m.m13,
                        z = m.m20 * vertex.x + m.m21 * vertex.y + m.m22 * vertex.z + m.m23,
                    };
                    if (at.y < lowestAny.y)
                        lowestAny = at;
                    if (at.y >= lowest.y)
                        continue;
                    foreach (var (ankle, toes, reach) in feet)
                        if (NearSegment(at, ankle, toes, reach))
                        {
                            lowest = at;
                            break;
                        }
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(baked);
            }
        }
        return (float.IsPositiveInfinity(lowest.y) ? lowestAny : lowest, lowestAny);
    }

    // Whether a point is within reach of the segment from a to b. Plain arithmetic: it runs for
    // every vertex of a doll.
    private static bool NearSegment(Vector3 p, Vector3 a, Vector3 b, float reach)
    {
        float abx = b.x - a.x, aby = b.y - a.y, abz = b.z - a.z;
        float apx = p.x - a.x, apy = p.y - a.y, apz = p.z - a.z;
        var lengthSq = abx * abx + aby * aby + abz * abz;
        var t = lengthSq > 0f ? Math.Clamp((apx * abx + apy * aby + apz * abz) / lengthSq, 0f, 1f) : 0f;
        float dx = apx - abx * t, dy = apy - aby * t, dz = apz - abz * t;
        return dx * dx + dy * dy + dz * dz <= reach * reach;
    }

    // A long-shinned doll seated on the cushion has her soles through the floor (Voymastina's by
    // 8 cm). She leans back about her hips until they are clear of it, as a lounging sitter does,
    // which swings her feet forward and up, and is lifted for what the furthest lean leaves. Her
    // lowest point is read again after each step, as the heel may give way to the toe.
    private void ClearOfFloor(GameObject doll, Vector3 hips)
    {
        var room = _weaponSet.Root.transform;
        var leant = 0f;
        for (var step = 0; step < 4; step++)
        {
            var low = Lowest(doll);
            var under = LoungeFloor + SoleClearance - low.y;
            if (under <= 0f)
                return;
            // The point's place about her hips, along the way she faces and up.
            var facing = room.InverseTransformDirection(doll.transform.forward);
            var from = low - hips;
            var along = facing.x * from.x + facing.z * from.z;
            var reach = Mathf.Sqrt(along * along + from.y * from.y);
            var now = Mathf.Atan2(from.y, along);
            var wanted = Mathf.Asin(Mathf.Clamp((from.y + under) / Mathf.Max(reach, 1e-4f), -1f, 1f));
            var lean = Mathf.Min((wanted - now) * Mathf.Rad2Deg, MaxSeatedLean - leant);
            if (lean <= 0.01f)
            {
                doll.transform.localPosition += Vector3.up * under;
                return;
            }
            doll.transform.RotateAround(room.TransformPoint(hips), doll.transform.right, -lean);
            leant += lean;
        }
    }
}
