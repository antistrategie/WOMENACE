using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace WOMENACE.Code;

// The spring solver, free of the engine: plain System.Numerics state in, local
// rotations out. SpringBoneRig feeds it from a live body's transforms, and the
// offline replay under tests/SpringBones feeds it recorded motion, so both run
// the same code.
//
// Each spring bone owns one particle, the world position of its child. Every
// step builds the chain's TARGET shape, where the particles would sit if the
// chain held its rest pose on the animated body, and springs each particle
// toward its own target. Springs run stiff at the root and soft at the tip, so
// motion travels down a chain as a wave. The particles are then laid back out
// at their link lengths root to tip, pushed out of the body colliders, and
// every bone is turned to point at its particle.
//
// Targets do not depend on the simulated links above them, so one link's
// swing never drags the rest into a limit, and a long chain stays calm. Every
// correction for the body (following the legs, clearing them, layering over
// another garment) is made to the target shape first, which the springs then
// follow smoothly, and the particles only meet the colliders as a backstop.
internal sealed class SpringJoint
{
    public NQuaternion RestLocalRotation;
    public NVector3 RestAxis;
    public float LocalLength;
    public NVector3 Tail;
    public NVector3 PrevTail;
    public NQuaternion LastLocal;
    public NVector3 Carried;
    public NVector3 LastCarried;

    // The bone's world rotation and position as last aimed or posed, worked
    // out down the chain from its parent, for the chains hanging off it.
    public NQuaternion World;
    public NVector3 Head;
}

// Where a chain hangs from: the point whose motion carries it, the rotation of
// the bone its root hangs from, and its root bone's position.
internal struct SpringAttach
{
    public NVector3 Carry;
    public NQuaternion Rotation;
    public NVector3 Origin;

    internal static SpringAttach Lerp(in SpringAttach a, in SpringAttach b, float t) => new()
    {
        Carry = NVector3.Lerp(a.Carry, b.Carry, t),
        Rotation = NQuaternion.Slerp(a.Rotation, b.Rotation, t),
        Origin = NVector3.Lerp(a.Origin, b.Origin, t),
    };
}

internal sealed class SpringChain
{
    public SpringChainSpec Spec;
    public string Root;
    public SpringJoint[] Joints;
    public NVector3[] Targets;
    public int Hits;

    // Last step's carry point and parent rotation (SpringAttach), whose
    // change since carries the particles along.
    public NVector3 LastParentPosition;
    public NQuaternion LastParentRotation;

    // The bones the chain turns, root first, the bone below the last one,
    // the bone the root hangs from, and the root's position in it. Bones that
    // are not animated keep their bind local position, so the root's world
    // position follows from its parent's, unless the chain is mounted.
    public string[] Bones;
    public string Tip;
    public string Parent;
    public NVector3 RootLocalPosition;
    public int Depth;

    // SpringChainSpec.Anchor when it resolved, and the root's bind position
    // in the anchor's frame, where a hanging chain's root is pinned.
    public string Anchor;
    public NVector3 MountOffset;

    // What the chain hangs from, as SpringBody reads it. A chain whose parent
    // is an animated bone reads it (ParentBone, and AnchorBone when mounted,
    // index SpringBody.Bones). A branch, whose parent lies under another
    // chain's bone, hangs from that bone of its Trunk, through the parent's
    // bind pose in the bone's frame (TrunkOffset, TrunkRotation), since the
    // bones between hold their bind pose.
    public int ParentBone = -1;
    public int AnchorBone = -1;
    public SpringChain Trunk;
    public int TrunkJoint;
    public NVector3 TrunkOffset;
    public NQuaternion TrunkRotation = NQuaternion.Identity;

    // Where the chain hangs from at the end of this frame and, for a chain
    // whose parent is animated, the last, to interpolate a split frame. A
    // branch takes its trunk's pose step by step instead, and its Attach is
    // set only by Reset and Hang.
    public SpringAttach Attach;
    public SpringAttach LastAttach;

    public bool Mounted => AnchorBone >= 0;

    // Each particle's rest position in the hips' frame, unscaled, and how far
    // it hangs from the hips rather than from the chain's parent, 0 at the
    // waist to 1 at the hip joints. Null when no particle hangs below the
    // waist.
    public NVector3[] HipsRest;
    public float[] HipsWeight;

    // Leg-following chains of other garments that hang inside and outside
    // this one, from the bind pose (LinkLayers), and the order layers step
    // in, inner first. Null when none.
    public SpringChain[] Inner;
    public SpringChain[] Outer;
    public int Layer;

    // Collider groups this chain leaves out although its spec names them:
    // the panels either side of a split up the front of a long garment let
    // the legs stride between them (FindSplits).
    public SpringColliderGroup Excluded;

    // For SpringChainSpec.HangDown: the way the body pushes the chain, in the
    // hips' frame, forward and out on its root's side in the bind pose, and
    // the way it hangs this step.
    public NVector3 Outward;
    public NVector3 HangDirection = new(0f, -1f, 0f);

    // Each particle's height and radial offset about the body's axis as of
    // its last step, for the layers over and under it. LayerCached is false
    // until the chain has stepped since it was last reset.
    public float[] LayerHeights;
    public NVector3[] LayerRadials;
    public bool LayerCached;

    // Working state for clearing the legs, and the swing that clears them,
    // eased from step to step.
    public NVector3[] Probe;
    public NQuaternion Clear = NQuaternion.Identity;

    public SpringColliderGroup Colliders => Spec.Colliders & ~Excluded;
}

// Where the body's hips and legs are this frame. All world space.
internal struct SpringBodyPose
{
    public bool Valid;
    public NVector3 Hips;
    public NQuaternion HipsRotation;
    public NVector3 Spine;
    public NVector3 HipLeft;
    public NVector3 KneeLeft;
    public NVector3 HipRight;
    public NVector3 KneeRight;
    public NVector3 FootLeft;
    public NVector3 FootRight;

    public NVector3 Up => SpringSolver.Direction(Spine - Hips);
    public NVector3 HipCentre => (HipLeft + HipRight) * 0.5f;
    public NVector3 Forward => SpringSolver.Direction(NVector3.Cross(HipRight - HipLeft, Up));
}

internal sealed class SpringCollider
{
    public SpringColliderSpec Spec;
    public NVector3 A;
    public NVector3 B;
    public float Radius;
}

internal static class SpringSolver
{
    internal const float MaxStepSeconds = 1f / 60f;

    // At most this many steps a frame. Below 30 fps the steps lengthen instead
    // of multiplying, so a struggling machine pays no more per frame for the
    // hair than a fast one. The springs stay stable at these step lengths.
    private const int MaxStepsPerFrame = 2;

    // Share of a step a frame may run over before it is split into two. A
    // frame a hair over 1/60 s is the common case at a vsynced 60 fps, and
    // splitting it would double the work for nothing.
    private const float StepSlack = 0.25f;

    // Longest frame the solver integrates. A longer hitch is simulated as this
    // much time, so a stall cannot fling the chains.
    private const float MaxFrameSeconds = 0.1f;

    // A body faster than this was placed, not moved. The game snaps a unit
    // along its path at the start of a move, a metre and more per frame.
    private const float TeleportMetresPerSecond = 25f;

    internal static float ClampFrame(float deltaTime) => Math.Min(deltaTime, MaxFrameSeconds);

    internal static int StepsFor(float deltaTime) => Math.Clamp((int)Math.Ceiling(deltaTime / MaxStepSeconds - StepSlack), 1, MaxStepsPerFrame);

    internal static bool IsTeleport(NVector3 from, NVector3 to, float deltaTime)
    {
        var jump = TeleportMetresPerSecond * Math.Max(deltaTime, MaxStepSeconds);
        return NVector3.DistanceSquared(from, to) > jump * jump;
    }

    private static readonly NVector3 Down = new(0f, -1f, 0f);

    // Fraction of a limit where its soft cap starts easing.
    private const float SoftLimitStart = 0.5f;

    // Leg drive: where along the body's down axis, relative to the hip
    // joints, a point starts to follow the leg and where it follows fully, in
    // metres. Nothing above the joints follows: turned about a hip, a point
    // above it moves the opposite way to the thigh, into the belly.
    private const float LegDriveFull = 0.1f;

    // How far from the leg's axis the thigh's surface carries cloth, in
    // metres at reference height: a slim thigh's radius under the cloth.
    // Cloth this close to the axis turns with the thigh, and cloth hanging
    // further out is carried by the surface point under it rather than turned
    // on a longer lever. Turned on the longer lever about the hip joint, a
    // panel hanging in front of the thigh lifts faster down its length than
    // it descends where the drive fades in, and its links fold up over the
    // thigh.
    private const float LegSurfaceRadius = 0.05f;

    // Carries the chain's target shape with each thigh's swing away from
    // straight down along the body (Carried), by a share of it, the front of
    // a garment following fully and the back by SpringChainSpec.LegDriveBack,
    // or by LegDriveBackKneeling while a knee is bent deep. The rest pose is the T-pose, with the legs straight down, so a skirt
    // shaped by it runs through the thighs as soon as they move. A target
    // that already moves with the legs keeps the springs from fighting the
    // leg colliders.
    private static void DriveWithLegs(SpringChain chain, in SpringBodyPose pose, NVector3 origin, float scale, float drive)
    {
        var up = pose.Up;
        var lateral = pose.HipRight - pose.HipLeft;
        var halfWidth = lateral.Length() * 0.5f;
        lateral = Direction(lateral);
        if (up.LengthSquared() < 0.5f || lateral.LengthSquared() < 0.5f || halfWidth < 1e-4f)
            return;
        var forward = pose.Forward;
        var centre = pose.HipCentre;
        var swingLeft = FromTo(-up, pose.KneeLeft - pose.HipLeft);
        var swingRight = FromTo(-up, pose.KneeRight - pose.HipRight);

        // Which leg the column hangs over, and how far to the front, from
        // where its target sits around the hips.
        var tip = chain.Targets[^1] - centre;
        var side = Math.Clamp(NVector3.Dot(tip, lateral) / halfWidth, -1f, 1f);
        var rightShare = 0.5f + 0.5f * side;
        var front = NVector3.Dot(tip, forward);
        var across = NVector3.Dot(tip, lateral);
        var around = MathF.Sqrt(Math.Max(front * front + across * across, 1e-8f));
        var frontness = 0.5f + 0.5f * Math.Clamp(front / around, -1f, 1f);
        var back = Math.Max(chain.Spec.LegDriveBack, chain.Spec.LegDriveBackKneeling * Kneeling(pose));
        var share = drive * (back + (1f - back) * frontness);

        for (var i = 0; i < chain.Targets.Length; i++)
        {
            var target = chain.Targets[i];
            var depth = Math.Clamp(-NVector3.Dot(target - centre, up) / (LegDriveFull * scale), 0f, 1f);
            if (depth <= 0f)
                continue;
            var weight = share * depth;
            var left = Carried(target, pose.HipLeft, pose.KneeLeft, up, NQuaternion.Slerp(NQuaternion.Identity, swingLeft, weight), LegSurfaceRadius * scale);
            var right = Carried(target, pose.HipRight, pose.KneeRight, up, NQuaternion.Slerp(NQuaternion.Identity, swingRight, weight), LegSurfaceRadius * scale);
            chain.Targets[i] = NVector3.Lerp(left, right, rightShare);
        }
        Relay(chain, origin, scale);
    }

    // How far into a kneel the deeper-bent knee is, by the angle between
    // thigh and shin. The combat stance bends the knees about 50 degrees and
    // a stride mostly up to 95, while the deployed kneel folds one to 135.
    private const float KneelBendStart = 100f;
    private const float KneelBendFull = 125f;

    private static float Kneeling(in SpringBodyPose pose)
    {
        var bend = Math.Max(
            AngleBetween(pose.KneeLeft - pose.HipLeft, pose.FootLeft - pose.KneeLeft),
            AngleBetween(pose.KneeRight - pose.HipRight, pose.FootRight - pose.KneeRight));
        return Math.Clamp((bend - KneelBendStart) / (KneelBendFull - KneelBendStart), 0f, 1f);
    }

    private static float AngleBetween(NVector3 a, NVector3 b)
    {
        var la = a.Length();
        var lb = b.Length();
        if (la < 1e-6f || lb < 1e-6f)
            return 0f;
        return MathF.Acos(Math.Clamp(NVector3.Dot(a, b) / (la * lb), -1f, 1f)) * (180f / MathF.PI);
    }

    // A target point carried by a thigh: moved as far as the point of the
    // thigh's surface under it, within `radius` of the leg's axis, moves
    // with the thigh's swing (LegSurfaceRadius).
    private static NVector3 Carried(NVector3 target, NVector3 hip, NVector3 knee, NVector3 up, NQuaternion swing, float radius)
    {
        var depth = -NVector3.Dot(target - hip, up);
        var axis = hip - up * depth;
        var radial = target - axis;
        var length = radial.Length();
        var surface = length > radius ? axis + radial * (radius / length) : target;
        return target + Swung(surface, hip, knee, up, swing) - surface;
    }

    // A target point swung with a thigh: turned about the hip down to knee
    // level, and below the knee only shifted as far as the knee level moved,
    // so the cloth hangs from there. Turned about the hip all the way down, a
    // floor-length hem would swing out as far as the leg is long.
    private static NVector3 Swung(NVector3 target, NVector3 hip, NVector3 knee, NVector3 up, NQuaternion swing)
    {
        var offset = target - hip;
        var depth = -NVector3.Dot(offset, up);
        var thigh = (knee - hip).Length();
        if (depth <= thigh)
            return hip + NVector3.Transform(offset, swing);
        var level = offset + up * (depth - thigh);
        return target + NVector3.Transform(level, swing) - level;
    }

    // Lays the target shape back out at the link lengths from the root, after
    // a correction that moved its points by different amounts.
    private static void Relay(SpringChain chain, NVector3 origin, float scale)
    {
        var above = origin;
        for (var i = 0; i < chain.Targets.Length; i++)
        {
            var target = above + Direction(chain.Targets[i] - above) * (chain.Joints[i].LocalLength * scale);
            chain.Targets[i] = target;
            above = target;
        }
    }

    // Hanging cloth does not hang plumb: it is tipped toward the body's front
    // by HangTilt, the hips' facing laid flat, so it turns with her but
    // ignores her lean.
    private static void UpdateHang(SpringChain chain, in SpringBodyPose pose)
    {
        if (!chain.Spec.HangDown)
            return;
        var tilt = chain.Spec.HangTilt * (MathF.PI / 180f);
        var forward = pose.Valid ? Direction(NVector3.Cross(pose.HipRight - pose.HipLeft, NVector3.UnitY)) : NVector3.Zero;
        forward.Y = 0f;
        forward = Direction(forward);
        chain.HangDirection = tilt == 0f || forward.LengthSquared() < 0.5f ? Down : Down * MathF.Cos(tilt) + forward * MathF.Sin(tilt);
    }

    // The way the body pushes a hanging chain: forward and out on its root's
    // side in the bind pose, turned with the hips and laid flat. Cloth tied at
    // the front of the shoulders is held to the front even when a forward lean
    // puts the shoulder behind the hips.
    private static NVector3 OutwardOf(SpringChain chain, in SpringBodyPose pose)
    {
        if (!pose.Valid || chain.Outward.LengthSquared() < 1e-8f)
            return NVector3.Zero;
        var outward = NVector3.Transform(chain.Outward, pose.HipsRotation);
        outward.Y = 0f;
        return Direction(outward);
    }

    // How far along `outward` a point must move to clear every capsule of the
    // given groups by `reach`. Along the outward direction a point leaves a
    // capsule less directly the more that runs along its surface.
    private static float OutwardPush(List<SpringCollider> colliders, NVector3 point, NVector3 outward, float reach, SpringColliderGroup groups)
    {
        var push = 0f;
        foreach (var c in colliders)
        {
            if ((c.Spec.Group & groups) == 0)
                continue;
            var away = point - ClosestOnSegment(c.A, c.B, point);
            var depth = c.Radius + reach - away.Length();
            if (depth <= 0f)
                continue;
            var facing = away.LengthSquared() < 1e-12f ? 1f : Math.Max(NVector3.Dot(Direction(away), outward), 0.3f);
            push = Math.Max(push, depth / facing);
        }
        return push;
    }

    // The torso groups a hanging chain is pushed out of along its outward
    // direction, target and particles alike. They overlap in front of the
    // hips, and pushed out of whichever is deepest, a particle there would be
    // shoved a different way every step.
    private const SpringColliderGroup TorsoGroups = SpringColliderGroup.Body | SpringColliderGroup.Pelvis | SpringColliderGroup.Seat;

    // Clears a hanging chain's target shape of the torso, point by point, out
    // along its one outward direction. The torso moves with the chain's root,
    // so the push changes smoothly. The legs are cleared like any chain's
    // (ClearLegs).
    private static void ClearTorso(SpringChain chain, List<SpringCollider> colliders, in SpringBodyPose pose, NVector3 origin, float scale)
    {
        var outward = OutwardOf(chain, pose);
        if (outward.LengthSquared() < 0.5f)
            return;
        var reach = chain.Spec.HitRadius * scale * LegTargetMargin;
        for (var i = 0; i < chain.Targets.Length; i++)
            chain.Targets[i] += outward * OutwardPush(colliders, chain.Targets[i], outward, reach, TorsoGroups);
        Relay(chain, origin, scale);
    }

    // Re-hangs the part of the target shape below the waist from the hips.
    // Many skirts are rigged from a spine bone or the chest, and hung whole
    // from it a run's forward lean would swing the hem back between the legs.
    // Below the waist cloth follows the pelvis.
    private static void HangFromHips(SpringChain chain, in SpringBodyPose pose, float scale)
    {
        if (chain.HipsRest == null)
            return;
        for (var i = 0; i < chain.Targets.Length; i++)
        {
            var weight = chain.HipsWeight[i];
            if (weight <= 0f)
                continue;
            var hung = pose.Hips + NVector3.Transform(chain.HipsRest[i] * scale, pose.HipsRotation);
            chain.Targets[i] = NVector3.Lerp(chain.Targets[i], hung, weight);
        }
    }

    // Each particle's rest position in the hips' frame and its hips weight,
    // from the bind pose: `waist` and `hipJoints` are heights along the body's
    // up axis, `restHeights` the particles' heights.
    internal static void SetHipsRest(SpringChain chain, NVector3[] restInHips, float[] restHeights, float waist, float hipJoints)
    {
        var weights = new float[restHeights.Length];
        var any = false;
        for (var i = 0; i < weights.Length; i++)
        {
            weights[i] = waist - hipJoints < 1e-4f ? 1f : Math.Clamp((waist - restHeights[i]) / (waist - hipJoints), 0f, 1f);
            any |= weights[i] > 0f;
        }
        chain.HipsRest = any ? restInHips : null;
        chain.HipsWeight = any ? weights : null;
    }

    // How near in height an inner layer's particle must be to hold an outer
    // one out, how far around the body it may sit from it (about a column's
    // spacing), and the gap kept between them, in metres. Wider than a
    // column's spacing, the panels in a coat's opening would count as under
    // the coat's front edge and shove it out.
    private const float LayerReach = 0.1f;
    private const float LayerAcross = 0.05f;
    private const float LayerGap = 0.015f;

    // Least difference in rest radius, in metres, for one chain to count as
    // hanging outside another, and the widest angle around the body between
    // two chains that can overlap.
    private const float LayerMinSeparation = 0.01f;
    private const float LayerMaxAngle = 45f;

    // Moves a point of one layer to the right side of the layers near it,
    // straight out from the body's axis or in toward it. Each other particle's
    // push fades out over the outer half of its reach, so a garment is never
    // snapped out and back as the one beside it swings, while between
    // particles it is still held out in full.
    private static NVector3 Layered(SpringChain[] others, NVector3 position, in SpringBodyPose pose, float scale, bool outside)
    {
        var up = pose.Up;
        var relative = position - pose.Hips;
        var height = NVector3.Dot(relative, up);
        var radial = relative - up * height;
        var radius = radial.Length();
        if (radius < 1e-4f)
            return position;
        var outward = radial / radius;
        var reach = LayerReach * scale;
        var width = LayerAcross * scale;
        var gap = LayerGap * scale;
        var shift = 0f;
        foreach (var layer in others)
        {
            if (!layer.LayerCached)
                continue;
            var heights = layer.LayerHeights;
            var radials = layer.LayerRadials;
            for (var k = 0; k < heights.Length; k++)
            {
                var apart = Math.Abs(heights[k] - height);
                if (apart >= reach)
                    continue;
                var along = NVector3.Dot(radials[k], outward);
                if (along <= 0f)
                    continue;
                var across = (radials[k] - outward * along).Length();
                if (across >= width)
                    continue;
                var weight = Math.Min(1f, 2f * (1f - apart / reach)) * Math.Min(1f, 2f * (1f - across / width));
                shift = outside
                    ? Math.Max(shift, (along + gap - radius) * weight)
                    : Math.Max(shift, (radius - along + gap) * weight);
            }
        }
        if (shift <= 0f)
            return position;
        return position + outward * (outside ? shift : -Math.Min(shift, radius * 0.5f));
    }

    // Records each particle's height and radial offset about the body's axis,
    // for the layers over and under this chain.
    private static void CacheLayer(SpringChain chain, in SpringBodyPose pose)
    {
        if (chain.Inner == null && chain.Outer == null)
            return;
        var count = chain.Joints.Length;
        chain.LayerHeights ??= new float[count];
        chain.LayerRadials ??= new NVector3[count];
        var up = pose.Up;
        for (var k = 0; k < count; k++)
        {
            var relative = chain.Joints[k].Tail - pose.Hips;
            var height = NVector3.Dot(relative, up);
            chain.LayerHeights[k] = height;
            chain.LayerRadials[k] = relative - up * height;
        }
        chain.LayerCached = true;
    }

    // How far a particle of an outer layer sits inside the layers under it,
    // in metres, for the replay's report.
    internal static float LayerClip(SpringChain[] inner, NVector3 position, in SpringBodyPose pose, float scale)
    {
        var lifted = Layered(inner, position, pose, scale, outside: true);
        return Math.Max(0f, (lifted - position).Length() - LayerGap * scale);
    }

    // A chain's garment: its spec, which lists one garment's chains. Columns
    // of one garment overlap where it is pleated, and they are not layers of
    // each other.
    private static SpringChainSpec GarmentOf(SpringChain chain) => chain.Spec;

    // Works out which leg-following chains hang inside which, from their rest
    // positions in the hips' frame (SetHipsRest), and gives each its inner and
    // outer layers. SpringChainBuilder numbers the layers from these to step
    // inner first. `upInHips` is the body's up axis in the hips' frame.
    internal static void LinkLayers(IReadOnlyList<SpringChain> chains, NVector3 upInHips)
    {
        var up = Direction(upInHips);
        foreach (var outer in chains)
        {
            outer.Inner = null;
            if (outer.HipsRest == null || outer.Spec.LegDrive <= 0f)
                continue;
            var count = outer.HipsRest.Length;
            var heights = new float[count];
            var radii = new float[count];
            for (var i = 0; i < count; i++)
            {
                heights[i] = NVector3.Dot(outer.HipsRest[i], up);
                radii[i] = Radial(outer.HipsRest[i], up).Length();
            }
            var tip = Radial(outer.HipsRest[count - 1], up);
            List<SpringChain> inner = null;
            foreach (var candidate in chains)
            {
                if (candidate == outer || candidate.HipsRest == null || candidate.Spec.LegDrive <= 0f
                    || GarmentOf(candidate) == GarmentOf(outer)
                    || AngleDegrees(tip, Radial(candidate.HipsRest[^1], up)) > LayerMaxAngle)
                    continue;
                var sum = 0f;
                var overlapping = 0;
                foreach (var p in candidate.HipsRest)
                {
                    var height = NVector3.Dot(p, up);
                    var outerRadius = RadiusAt(heights, radii, height);
                    if (float.IsNaN(outerRadius))
                        continue;
                    sum += outerRadius - Radial(p, up).Length();
                    overlapping++;
                }
                if (overlapping > 0 && sum / overlapping > LayerMinSeparation)
                    (inner ??= new List<SpringChain>()).Add(candidate);
            }
            outer.Inner = inner?.ToArray();
        }
        foreach (var chain in chains)
        {
            List<SpringChain> over = null;
            foreach (var other in chains)
                if (other.Inner != null && Array.IndexOf(other.Inner, chain) >= 0)
                    (over ??= new List<SpringChain>()).Add(other);
            chain.Outer = over?.ToArray();
        }
    }

    private static NVector3 Radial(NVector3 p, NVector3 up) => p - up * NVector3.Dot(p, up);

    // A chain's rest radius at a height, interpolated between its particles,
    // or NaN outside the heights it spans.
    private static float RadiusAt(float[] heights, float[] radii, float height)
    {
        for (var i = 0; i + 1 < heights.Length; i++)
        {
            var a = heights[i];
            var b = heights[i + 1];
            if ((height - a) * (height - b) > 0f)
                continue;
            var t = Math.Abs(b - a) < 1e-6f ? 0f : (height - a) / (b - a);
            return radii[i] + (radii[i + 1] - radii[i]) * t;
        }
        return float.NaN;
    }

    // A split up the front of a long garment: the largest gap between its
    // columns around the body, when it faces forward and is well wider than
    // the columns' spacing. The columns within two spacings of either edge
    // let the legs stride between them rather than catching on a thigh every
    // step. Only garments reaching past the knee are split this way: over a
    // short skirt or coat the legs never pass between the panels.
    private const float SplitMaxFacing = 45f;
    private const float SplitMinGap = 1.5f;
    private const float SplitEdgeSpacings = 2f;

    // Fewest columns a garment needs for a gap between them to read as a
    // split: fewer than this ring the body too sparsely for any gap to stand
    // out from the spacing.
    private const int SplitMinColumns = 4;

    internal static void FindSplits(IReadOnlyList<SpringChain> chains, NVector3 upInHips, NVector3 forwardInHips, NVector3 kneeInHips)
    {
        var up = Direction(upInHips);
        var kneeHeight = NVector3.Dot(kneeInHips, up);
        var forward = Direction(Radial(forwardInHips, up));
        var lateral = NVector3.Cross(up, forward);
        var garments = new Dictionary<object, List<SpringChain>>();
        foreach (var chain in chains)
        {
            chain.Excluded = SpringColliderGroup.None;
            if (chain.HipsRest == null || chain.Spec.LegDrive <= 0f)
                continue;
            var key = GarmentOf(chain);
            if (!garments.TryGetValue(key, out var list))
                garments[key] = list = new List<SpringChain>();
            list.Add(chain);
        }
        foreach (var columns in garments.Values)
        {
            if (columns.Count < SplitMinColumns)
                continue;
            var lowest = float.MaxValue;
            var around = new (float Angle, SpringChain Chain)[columns.Count];
            for (var c = 0; c < columns.Count; c++)
            {
                var tip = columns[c].HipsRest[^1];
                lowest = Math.Min(lowest, NVector3.Dot(tip, up));
                var radial = Radial(tip, up);
                around[c] = (MathF.Atan2(NVector3.Dot(radial, lateral), NVector3.Dot(radial, forward)) * (180f / MathF.PI), columns[c]);
            }
            if (lowest >= kneeHeight)
                continue;
            Array.Sort(around, (a, b) => a.Angle.CompareTo(b.Angle));
            var gaps = new float[around.Length];
            for (var c = 0; c < around.Length; c++)
            {
                var next = around[(c + 1) % around.Length].Angle;
                gaps[c] = (next - around[c].Angle + 360f) % 360f;
            }
            var widest = 0;
            for (var c = 1; c < gaps.Length; c++)
                if (gaps[c] > gaps[widest])
                    widest = c;
            var sorted = (float[])gaps.Clone();
            Array.Sort(sorted);
            var spacing = sorted[sorted.Length / 2];
            var start = around[widest].Angle;
            var end = start + gaps[widest];
            var centre = Wrap(start + gaps[widest] * 0.5f);
            if (Math.Abs(centre) > SplitMaxFacing || gaps[widest] < SplitMinGap * spacing)
                continue;
            var edge = SplitEdgeSpacings * spacing;
            foreach (var (angle, chain) in around)
            {
                var fromStart = Wrap(start - angle);
                var fromEnd = Wrap(angle - end);
                if ((fromStart >= 0f && fromStart <= edge) || (fromEnd >= 0f && fromEnd <= edge))
                    chain.Excluded = SpringColliderGroup.Legs | SpringColliderGroup.Pelvis;
            }
        }
    }

    private static float Wrap(float degrees) => (degrees % 360f + 540f) % 360f - 180f;

    // Times a target shape is swung clear of the legs per step. Clearing the
    // deepest point can bring another into contact, and a few passes settle it.
    private const int LegClearPasses = 3;

    // Time constants the leg-clearing swing eases at, out toward a pressing
    // leg and back once it leaves. Between the legs the deepest contact flips
    // from one thigh to the other through a stride, and a swing applied
    // outright would flip with it and throw the shape sideways.
    private const float LegClearOutSeconds = 0.03f;
    private const float LegClearBackSeconds = 0.15f;

    // Multiple of the chain's HitRadius that targets keep from the body.
    private const float LegTargetMargin = 2f;

    // Swings the chain's target shape clear of the legs before the springs
    // pull. The shape turns as a whole about its root, just far enough that
    // its deepest point above knee level leaves the leg, a few times over,
    // and below knee level it only shifts as far as the knee level moved, as
    // the leg drive does. Turned whole, the shape swings out like a panel and
    // stays smooth: pushed out point by point, or bent at the hips, it would
    // kink where it met the leg and jump as the push flipped round the end of
    // a thigh capsule. The swing is capped at SpringChainSpec.LegClearAngle.
    private static void ClearLegs(SpringChain chain, List<SpringCollider> colliders, in SpringBodyPose pose, NVector3 origin, float scale, float step)
    {
        var count = chain.Targets.Length;
        var up = pose.Up;
        var centre = pose.HipCentre;
        var kneeDepth = 0.5f * ((pose.KneeLeft - pose.HipLeft).Length() + (pose.KneeRight - pose.HipRight).Length());

        var reach = chain.Spec.HitRadius * scale * LegTargetMargin;
        var needed = NQuaternion.Identity;
        var probe = chain.Probe ??= new NVector3[count];
        Array.Copy(chain.Targets, probe, count);
        for (var pass = 0; pass < LegClearPasses; pass++)
        {
            var deepest = 0f;
            var from = NVector3.Zero;
            var to = NVector3.Zero;
            for (var i = 0; i < count; i++)
            {
                var target = probe[i];
                if (Below(target, centre, up) > kneeDepth)
                    continue;
                foreach (var c in colliders)
                {
                    if ((c.Spec.Group & SpringColliderGroup.Legs) == 0)
                        continue;
                    var closest = ClosestOnSegment(c.A, c.B, target);
                    var away = target - closest;
                    var limit = c.Radius + reach;
                    var depth = limit - away.Length();
                    if (depth <= deepest || away.LengthSquared() < 1e-12f)
                        continue;
                    deepest = depth;
                    from = target - origin;
                    to = closest + NVector3.Normalize(away) * limit - origin;
                }
            }
            if (deepest <= 0f)
                break;
            var swing = FromTo(from, to);
            needed = swing * needed;
            for (var i = 0; i < count; i++)
                probe[i] = origin + NVector3.Transform(probe[i] - origin, swing);
        }

        var angle = AngleDegrees(needed, NQuaternion.Identity);
        var cap = chain.Spec.LegClearAngle;
        if (angle > cap)
        {
            needed = NQuaternion.Slerp(NQuaternion.Identity, needed, cap / angle);
            angle = cap;
        }
        var easeOut = angle > AngleDegrees(chain.Clear, NQuaternion.Identity);
        var blend = 1f - MathF.Exp(-step / (easeOut ? LegClearOutSeconds : LegClearBackSeconds));
        chain.Clear = NQuaternion.Normalize(NQuaternion.Slerp(chain.Clear, needed, blend));

        for (var i = 0; i < count; i++)
        {
            var target = chain.Targets[i];
            var belowKnee = Below(target, centre, up) - kneeDepth;
            if (belowKnee <= 0f)
            {
                chain.Targets[i] = origin + NVector3.Transform(target - origin, chain.Clear);
                continue;
            }
            var level = target + up * belowKnee;
            chain.Targets[i] = target + origin + NVector3.Transform(level - origin, chain.Clear) - level;
        }
    }

    // How far a point sits below the hip joints along the body.
    private static float Below(NVector3 point, NVector3 hipCentre, NVector3 up) => -NVector3.Dot(point - hipCentre, up);

    // Places a collider on the segment between two bone positions, moved
    // toward the body's back and down by the spec's offsets. Without a body
    // pose the offsets cannot be oriented and the capsule stays on the line.
    internal static void PlaceCollider(SpringCollider c, NVector3 from, NVector3 to, float scale, in SpringBodyPose pose)
    {
        var shift = NVector3.Zero;
        if ((c.Spec.Back != 0f || c.Spec.Down != 0f) && pose.Valid)
            shift = (-pose.Forward * c.Spec.Back - pose.Up * c.Spec.Down) * scale;
        c.A = from + (to - from) * c.Spec.T0 + shift;
        c.B = from + (to - from) * c.Spec.T1 + shift;
        c.Radius = c.Spec.Radius * scale;
    }

    // Steps a freshly reset body holds still for, so it starts where its
    // chains settle rather than at the rest pose: from a skirt's modelled
    // flare, gravity and the legs would visibly drop it into place every time
    // a body appeared.
    private const int SettleSteps = 30;

    // Puts the chain on its rest shape (a HangDown chain on its hang line) for
    // the pose given, with no velocity.
    internal static void Reset(SpringChain chain, in SpringBodyPose pose, in SpringAttach attach, float scale)
    {
        var carryPosition = attach.Carry;
        var parentRotation = attach.Rotation;
        var origin = attach.Origin;
        UpdateHang(chain, pose);
        var rotation = parentRotation;
        var cursor = origin;
        foreach (var j in chain.Joints)
        {
            rotation *= j.RestLocalRotation;
            cursor += (chain.Spec.HangDown ? chain.HangDirection : NVector3.Transform(j.RestAxis, rotation)) * (j.LocalLength * scale);
            j.Tail = cursor;
            j.PrevTail = cursor;
            j.LastCarried = NVector3.Zero;
        }
        chain.LastParentPosition = carryPosition;
        chain.LastParentRotation = parentRotation;
        chain.Clear = NQuaternion.Identity;
        Aim(chain, parentRotation, origin, scale);
    }

    // Resets the chain, then lets it settle on the body as posed.
    internal static void ResetSettled(SpringChain chain, List<SpringCollider> colliders, in SpringBodyPose pose, in SpringAttach attach, float scale)
    {
        Reset(chain, pose, attach, scale);
        for (var i = 0; i < SettleSteps; i++)
            Step(chain, colliders, pose, attach, scale, MaxStepSeconds, MaxStepSeconds);
        foreach (var j in chain.Joints)
        {
            j.PrevTail = j.Tail;
            j.LastCarried = NVector3.Zero;
        }
        chain.Hits = 0;
    }

    // Moves the chain's whole state with a body that was placed rather than
    // moved, so it carries on swinging from where it was, without the cost or
    // the pop of a reset.
    internal static void Shift(SpringChain chain, NVector3 delta)
    {
        foreach (var j in chain.Joints)
        {
            j.Tail += delta;
            j.PrevTail += delta;
            j.Head += delta;
        }
        chain.LastParentPosition += delta;
    }

    internal static void Step(SpringChain chain, List<SpringCollider> colliders, in SpringBodyPose pose, in SpringAttach attach, float scale, float step, float lastStep)
    {
        var carryPosition = attach.Carry;
        var parentRotation = attach.Rotation;
        var origin = attach.Origin;
        var spec = chain.Spec;
        var joints = chain.Joints;
        var count = joints.Length;
        var groups = chain.Colliders;
        UpdateHang(chain, pose);

        // Carry every particle along by a share of the motion of the body
        // part the chain hangs from. Walking, turning, aim twists and stance
        // snaps all reach the chain through that part, and at full world
        // inertia each of them jolts it. Only the share not carried shows. A
        // hanging chain is carried by its anchor's travel alone: it hangs from
        // the world, and turned with the body part it would swing round with
        // it.
        //
        // With RelativeDrag the carry is not applied to positions: each
        // particle instead moves with the body part's motion at its position,
        // drag acts only on its motion relative to that, and Follow carries a
        // share of each CHANGE in the body's motion straight through.
        var turn = spec.HangDown ? NQuaternion.Identity : parentRotation * NQuaternion.Inverse(chain.LastParentRotation);
        if (spec.RelativeDrag)
        {
            foreach (var j in joints)
                j.Carried = carryPosition + NVector3.Transform(j.Tail - chain.LastParentPosition, turn) - j.Tail;
        }
        else if (spec.Follow > 0f)
        {
            foreach (var j in joints)
            {
                j.Tail = NVector3.Lerp(j.Tail, carryPosition + NVector3.Transform(j.Tail - chain.LastParentPosition, turn), spec.Follow);
                j.PrevTail = NVector3.Lerp(j.PrevTail, carryPosition + NVector3.Transform(j.PrevTail - chain.LastParentPosition, turn), spec.Follow);
            }
        }
        chain.LastParentPosition = carryPosition;
        chain.LastParentRotation = parentRotation;

        // The target shape: the rest pose hung off the animated parent, or
        // straight down for a hanging chain.
        var rotation = parentRotation;
        var cursor = origin;
        for (var i = 0; i < count; i++)
        {
            var j = joints[i];
            rotation *= j.RestLocalRotation;
            cursor += (spec.HangDown ? chain.HangDirection : NVector3.Transform(j.RestAxis, rotation)) * (j.LocalLength * scale);
            chain.Targets[i] = cursor;
        }

        if (pose.Valid)
        {
            if (spec.LegDrive > 0f)
            {
                HangFromHips(chain, pose, scale);
                DriveWithLegs(chain, pose, origin, scale, spec.LegDrive);
            }

            // Layered garments keep their target shapes apart: a chain under
            // another keeps inside where the one over it stood last step, and
            // a chain over another keeps outside where the one under it
            // stands this step. Kept apart one way only, a skirt flared by a
            // wide stance would come out through the side of the coat.
            if (chain.Outer != null)
            {
                for (var i = 0; i < count; i++)
                    chain.Targets[i] = Layered(chain.Outer, chain.Targets[i], pose, scale, outside: false);
                Relay(chain, origin, scale);
            }
            if (chain.Inner != null)
            {
                for (var i = 0; i < count; i++)
                    chain.Targets[i] = Layered(chain.Inner, chain.Targets[i], pose, scale, outside: true);
                Relay(chain, origin, scale);
            }

            // The legs are cleared first and the torso last: the torso does
            // not move against the chain's root, so the shape it leaves is the
            // one the springs see, and the legs' swing cannot turn it back in.
            if (spec.HangDown || (groups & SpringColliderGroup.Legs) != 0)
                ClearLegs(chain, colliders, pose, origin, scale, step);
            if (spec.HangDown)
                ClearTorso(chain, colliders, pose, origin, scale);
        }

        // Drag is rated per 60 Hz step and rescaled to the step taken, so the
        // feel holds at any frame rate. Stiffness is a spring frequency in Hz,
        // eased from the root's value to the tip's. Forces are accelerations
        // scaled by the step squared. The carried motion is rescaled by the
        // step ratio because it is a displacement over the previous step.
        // Hanging cloth falls the way it hangs, tilt and all, so gravity does
        // not drag it back to plumb against its tipped target.
        var keep = MathF.Pow(1f - Math.Clamp(spec.Drag, 0f, 1f), step * 60f) * (step / lastStep);
        var squared = step * step;
        var gravity = (spec.HangDown ? chain.HangDirection : Down) * (9.81f * spec.Gravity * squared);
        for (var i = 0; i < count; i++)
        {
            var j = joints[i];
            var t = count == 1 ? 0f : i / (float)(count - 1);
            var omega = 2f * MathF.PI * (spec.Stiffness + (spec.TipStiffness - spec.Stiffness) * t);
            var pull = (chain.Targets[i] - j.Tail) * (omega * omega * squared);
            NVector3 next;
            if (spec.RelativeDrag)
            {
                var ratio = step / lastStep;
                var change = j.Carried - j.LastCarried * ratio;
                var own = (j.Tail - j.PrevTail) * ratio - j.Carried + change * spec.Follow;
                next = j.Tail + j.Carried + own * (keep / ratio) + pull + gravity;
                j.LastCarried = j.Carried;
            }
            else
            {
                next = j.Tail + (j.Tail - j.PrevTail) * keep + pull + gravity;
            }
            j.PrevTail = j.Tail;
            j.Tail = next;
        }

        // The links laid back out at their lengths root to tip, the caps
        // applied, and the colliders last, so a push out of the body always
        // wins: capped after the push, a panel a lifted thigh had shoved aside
        // would be pulled straight back into the leg.
        var hit = spec.HitRadius * scale;
        var strayStart = MathF.Cos(spec.MaxAngle * SoftLimitStart * (MathF.PI / 180f));
        var foldStart = MathF.Cos(spec.FoldGuard * SoftLimitStart * (MathF.PI / 180f));
        var outward = spec.HangDown ? OutwardOf(chain, pose) : NVector3.Zero;
        var previous = origin;
        for (var i = 0; i < count; i++)
        {
            var j = joints[i];
            var length = j.LocalLength * scale;
            var direction = Direction(j.Tail - previous);

            // A soft cap on how far the chain may stray from its target,
            // measured as an angle at the chain's root. Past half of MaxAngle
            // the stray eases toward the cap instead of stopping dead at it,
            // since a particle slammed to a halt reads as a twitch. It pulls
            // toward the targets, the way the springs do, so the two never
            // fight, where a limit against the neighbouring link would.
            var toTarget = chain.Targets[i] - origin;
            var toParticle = previous + direction * length - origin;
            if (Cosine(toTarget, toParticle) < strayStart)
            {
                var stray = AngleDegrees(toTarget, toParticle);
                var capped = origin + TurnToward(Direction(toTarget), toParticle, SoftCap(stray, spec.MaxAngle)) * toParticle.Length();
                direction = Direction(capped - previous);
            }

            var targetDirection = chain.Targets[i] - (i == 0 ? origin : chain.Targets[i - 1]);
            direction = GuardFold(targetDirection, direction, spec.FoldGuard, foldStart);
            var position = previous + direction * length;

            var contact = NVector3.Zero;
            var pushed = false;
            var touching = groups;
            if (touching != SpringColliderGroup.None)
            {
                if (spec.HangDown && outward.LengthSquared() > 0.5f)
                {
                    // Out of the torso along the chain's one outward direction,
                    // past every capsule at once, as its target was.
                    var push = OutwardPush(colliders, position, outward, hit, touching & TorsoGroups);
                    if (push > 0f)
                    {
                        position += outward * push;
                        contact = outward;
                        pushed = true;
                    }
                    touching &= ~TorsoGroups;
                }
                // Each particle is resolved against its deepest collision
                // only. Where two capsules overlap, pushing out of each in
                // turn would shove the particle two ways in one step.
                var deepest = 0f;
                var resolved = position;
                foreach (var c in colliders)
                {
                    if ((c.Spec.Group & touching) == 0)
                        continue;
                    var closest = ClosestOnSegment(c.A, c.B, position);
                    var away = position - closest;
                    var reach = c.Radius + hit;
                    // A torso or hip capsule never pushes further out than the
                    // particle's own target sits. A capsule is rounder than the
                    // body it stands in for, and where the rest shape dips
                    // inside one, a skirt waistband hugging the hips, the
                    // spring pulls in while the push shoves out. The legs push
                    // fully: they stride into where a skirt rests, and
                    // yielding there lets it pass through them.
                    if ((c.Spec.Group & SpringColliderGroup.Legs) == 0)
                    {
                        var target = chain.Targets[i];
                        reach = Math.Min(reach, (target - ClosestOnSegment(c.A, c.B, target)).Length());
                    }
                    var distance = away.Length();
                    if (distance >= reach || reach - distance <= deepest)
                        continue;
                    // Out on the side the particle's target is on, not the
                    // side it happens to be on. A fast swing can carry a
                    // particle clean through a capsule in one step, and pushed
                    // out where it landed it would stay trapped on the wrong
                    // side of the body.
                    var home = chain.Targets[i] - closest;
                    if (NVector3.Dot(away, home) < 0f || away.LengthSquared() < 1e-12f)
                        away = home;
                    if (away.LengthSquared() < 1e-12f)
                        continue;
                    deepest = reach - distance;
                    contact = NVector3.Normalize(away);
                    resolved = closest + contact * reach;
                }
                if (deepest > 0f)
                {
                    position = resolved;
                    pushed = true;
                }
                if (pushed)
                {
                    chain.Hits++;
                    // The fold guard again: a leg sweeping through a coattail
                    // shoves single particles sideways while their neighbours
                    // stay put, and the link between them would flip into a
                    // zigzag. A brief clip beats a folded, flailing panel.
                    position = previous + GuardFold(targetDirection, Direction(position - previous), spec.FoldGuard, foldStart) * length;
                    // An inelastic contact: the motion into the surface stops
                    // there. Left in, the push would become outward speed and
                    // a skirt resting on a leg would buzz.
                    var velocity = position - j.PrevTail;
                    var inward = NVector3.Dot(velocity, contact);
                    if (inward < 0f)
                        j.PrevTail = position - (velocity - contact * inward);
                }
            }

            // A garment over another is held outside it particle by particle
            // too, after the body has pushed: the legs shove a skirt out
            // directly while the coat over it only follows its target on soft
            // springs, so a held-out target alone would leave the skirt
            // standing through the coat. Outward only, so it never pushes into
            // the body, and inelastic, as against the body.
            if (chain.Inner != null && pose.Valid)
            {
                var held = Layered(chain.Inner, position, pose, scale, outside: true);
                var lift = held - position;
                if (lift.LengthSquared() > 1e-12f)
                {
                    var lifted = Direction(lift);
                    position = held;
                    var velocity = position - j.PrevTail;
                    var inward = NVector3.Dot(velocity, lifted);
                    if (inward < 0f)
                        j.PrevTail = position - (velocity - lifted * inward);
                }
            }

            j.Tail = position;
            previous = position;
        }

        if (pose.Valid)
            CacheLayer(chain, pose);
        Aim(chain, parentRotation, origin, scale);
    }

    // Turns each bone to point at its particle, root to tip, each composed on
    // its parent's new rotation, kept as the local rotation to write.
    private static void Aim(SpringChain chain, NQuaternion parentRotation, NVector3 origin, float scale)
    {
        var parentWorld = parentRotation;
        var head = origin;
        foreach (var j in chain.Joints)
        {
            var restRotation = parentWorld * j.RestLocalRotation;
            var restDirection = NVector3.Transform(j.RestAxis, restRotation);
            var world = FromTo(restDirection, j.Tail - head) * restRotation;
            j.LastLocal = NQuaternion.Inverse(parentWorld) * world;
            j.World = world;
            j.Head = head;
            head += NVector3.Transform(j.RestAxis * (j.LocalLength * scale), world);
            parentWorld = world;
        }
    }

    // The bones' world poses from their local rotations as they stand, for a
    // chain eased toward rest rather than aimed.
    internal static void PoseJoints(SpringChain chain, in SpringAttach attach, float scale)
    {
        var world = attach.Rotation;
        var head = attach.Origin;
        foreach (var j in chain.Joints)
        {
            world *= j.LastLocal;
            j.World = world;
            j.Head = head;
            head += NVector3.Transform(j.RestAxis * (j.LocalLength * scale), world);
        }
    }

    internal static NVector3 Direction(NVector3 v)
    {
        var length = v.Length();
        return length < 1e-12f ? NVector3.Zero : v / length;
    }

    // The cosine of the angle between two vectors, 1 when either is zero.
    private static float Cosine(NVector3 a, NVector3 b)
    {
        var lengths = MathF.Sqrt(a.LengthSquared() * b.LengthSquared());
        return lengths < 1e-12f ? 1f : NVector3.Dot(a, b) / lengths;
    }

    private static float AngleDegrees(NVector3 a, NVector3 b)
    {
        var la = a.Length();
        var lb = b.Length();
        if (la < 1e-12f || lb < 1e-12f)
            return 0f;
        return MathF.Acos(Math.Clamp(NVector3.Dot(a, b) / (la * lb), -1f, 1f)) * (180f / MathF.PI);
    }

    internal static float AngleDegrees(NQuaternion a, NQuaternion b)
    {
        var dot = Math.Min(Math.Abs(NQuaternion.Dot(a, b)), 1f);
        return 2f * MathF.Acos(dot) * (180f / MathF.PI);
    }

    // The shortest rotation taking direction a onto direction b.
    private static NQuaternion FromTo(NVector3 a, NVector3 b)
    {
        a = Direction(a);
        b = Direction(b);
        var dot = NVector3.Dot(a, b);
        if (dot < -0.999999f)
        {
            var axis = NVector3.Cross(NVector3.UnitX, a);
            if (axis.LengthSquared() < 1e-6f)
                axis = NVector3.Cross(NVector3.UnitY, a);
            return NQuaternion.CreateFromAxisAngle(NVector3.Normalize(axis), MathF.PI);
        }
        var cross = NVector3.Cross(a, b);
        return NQuaternion.Normalize(new NQuaternion(cross.X, cross.Y, cross.Z, 1f + dot));
    }

    // Unit direction `from` turned toward `to` by at most `degrees`.
    private static NVector3 TurnToward(NVector3 from, NVector3 to, float degrees)
    {
        var angle = AngleDegrees(from, to);
        if (angle <= degrees || angle < 1e-6f)
            return Direction(to);
        var axis = NVector3.Cross(from, to);
        if (axis.LengthSquared() < 1e-12f)
            return from;
        return NVector3.Transform(from, NQuaternion.CreateFromAxisAngle(NVector3.Normalize(axis), degrees * (MathF.PI / 180f)));
    }

    // Holds a link within `limit` degrees of its target direction, eased past
    // half (`startCosine` is the cosine of that half). It catches a link
    // flipped into a hairpin, which otherwise holds itself folded: each
    // particle is laid out along the line from the one above, and once that
    // line points backwards the springs cannot pull it through. It measures
    // against the target, as the springs pull, so it never fights them.
    private static NVector3 GuardFold(NVector3 targetDirection, NVector3 direction, float limit, float startCosine)
    {
        if (targetDirection.LengthSquared() < 1e-12f || Cosine(targetDirection, direction) >= startCosine)
            return direction;
        var flip = AngleDegrees(targetDirection, direction);
        return TurnToward(Direction(targetDirection), direction, SoftCap(flip, limit));
    }

    // Eases an angle past half the limit toward the limit, never reaching
    // it, so a capped particle decelerates instead of stopping dead.
    private static float SoftCap(float angle, float limit)
    {
        var softStart = limit * SoftLimitStart;
        if (angle <= softStart)
            return angle;
        var range = limit - softStart;
        return range <= 0f ? limit : softStart + range * MathF.Tanh((angle - softStart) / range);
    }

    internal static NVector3 ClosestOnSegment(NVector3 a, NVector3 b, NVector3 p)
    {
        var ab = b - a;
        var denom = ab.LengthSquared();
        if (denom < 1e-12f)
            return a;
        return a + ab * Math.Clamp(NVector3.Dot(p - a, ab) / denom, 0f, 1f);
    }
}
