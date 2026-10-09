using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace WOMENACE.Code;

// One body's spring simulation, free of the engine, frame by frame. The live
// rig and the offline replay both drive a body through here, so they step it
// the same way. Each frame the caller fills Positions and Rotations with the
// world poses of the animated bones named in Bones, calls Read, then Reset,
// Advance or Hang, and writes each chain's local rotations back, with a
// mounted chain's root at its Attach.Origin. Relax needs no Read.
internal sealed class SpringBody
{
    internal readonly SpringChain[] Chains;
    internal readonly List<SpringCollider> Colliders = new();
    internal readonly string[] Bones;
    internal readonly NVector3[] Positions;
    internal readonly NQuaternion[] Rotations;
    internal readonly float Scale;

    // The bones a hanging chain needs while the rest of the body is at rest:
    // the body pose bones and the parents and anchors of the hanging chains
    // and of the chains they branch off (Hang).
    internal readonly int[] HangBones;
    internal readonly bool HasHanging;

    internal SpringBodyPose Pose { get; private set; }

    private readonly int[] _poseBones;
    private readonly int[] _colliderFrom;
    private readonly int[] _colliderTo;
    private float _lastStep = SpringSolver.MaxStepSeconds;

    // The hanging chains and the chains they branch off, in step order.
    private readonly SpringChain[] _hangChains;

    // `present` says whether the live body carries a bone (SpringChainBuilder).
    internal SpringBody(SpringBodyProfile profile, SpringBindPose bind, float scale, Predicate<string> present, List<string> missing)
    {
        Scale = scale;
        Chains = SpringChainBuilder.Build(profile, bind, present, missing).ToArray();
        var bones = new BoneList(bind, present);
        var hang = new List<int>();
        var pose = new int[SpringChainBuilder.PoseBones.Length];
        var posed = true;
        for (var i = 0; i < pose.Length; i++)
        {
            posed &= (pose[i] = bones.Add(SpringChainBuilder.PoseBones[i])) >= 0;
            if (pose[i] >= 0)
                hang.Add(pose[i]);
        }
        _poseBones = posed ? pose : null;
        var hangs = new HashSet<SpringChain>();
        foreach (var chain in Chains)
            if (chain.Spec.HangDown)
                for (var c = chain; c != null; c = c.Trunk)
                    hangs.Add(c);
        var hangChains = new List<SpringChain>();
        foreach (var chain in Chains)
        {
            if (hangs.Contains(chain))
                hangChains.Add(chain);
            if (chain.Trunk != null)
                continue;
            chain.ParentBone = bones.Add(chain.Parent);
            chain.AnchorBone = bones.Add(chain.Anchor);
            if (!hangs.Contains(chain))
                continue;
            hang.Add(chain.ParentBone);
            if (chain.AnchorBone >= 0)
                hang.Add(chain.AnchorBone);
        }
        _hangChains = hangChains.ToArray();
        var from = new List<int>();
        var to = new List<int>();
        foreach (var spec in profile.Colliders)
        {
            var a = bones.Add(spec.From);
            var b = bones.Add(spec.To);
            if (a < 0 || b < 0)
            {
                missing.Add($"collider {spec.From}-{spec.To}");
                continue;
            }
            Colliders.Add(new SpringCollider { Spec = spec });
            from.Add(a);
            to.Add(b);
        }
        _colliderFrom = from.ToArray();
        _colliderTo = to.ToArray();
        Bones = bones.Names.ToArray();
        Positions = new NVector3[Bones.Length];
        Rotations = new NQuaternion[Bones.Length];
        HangBones = hang.Distinct().ToArray();
        foreach (var chain in Chains)
            HasHanging |= chain.Spec.HangDown;
    }

    // The animated bones read each frame, each named once, by index.
    private sealed class BoneList
    {
        private readonly SpringBindPose _bind;
        private readonly Predicate<string> _present;
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
        public readonly List<string> Names = new();

        public BoneList(SpringBindPose bind, Predicate<string> present)
        {
            _bind = bind;
            _present = present;
        }

        // The bone's index, or -1 when the body has no such bone.
        public int Add(string name)
        {
            if (name == null || _bind.Find(name) < 0 || !_present(name))
                return -1;
            if (!_index.TryGetValue(name, out var i))
            {
                _index[name] = i = Names.Count;
                Names.Add(name);
            }
            return i;
        }
    }

    // Takes in this frame's bone poses: the body pose, the colliders, and
    // where each chain whose parent is animated hangs from at the frame's end.
    // `colliders` false skips placing the colliders, for Hang, which reads
    // only HangBones and meets no collider.
    internal void Read(bool colliders = true)
    {
        Pose = _poseBones == null ? default : new SpringBodyPose
        {
            Valid = true,
            Hips = Positions[_poseBones[0]],
            HipsRotation = Rotations[_poseBones[0]],
            Spine = Positions[_poseBones[1]],
            HipLeft = Positions[_poseBones[2]],
            KneeLeft = Positions[_poseBones[3]],
            HipRight = Positions[_poseBones[4]],
            KneeRight = Positions[_poseBones[5]],
            FootLeft = Positions[_poseBones[6]],
            FootRight = Positions[_poseBones[7]],
        };
        var pose = Pose;
        for (var c = 0; colliders && c < Colliders.Count; c++)
            SpringSolver.PlaceCollider(Colliders[c], Positions[_colliderFrom[c]], Positions[_colliderTo[c]], Scale, pose);
        foreach (var chain in Chains)
        {
            if (chain.Trunk != null)
                continue;
            chain.LastAttach = chain.Attach;
            chain.Attach = Animated(chain);
        }
    }

    // Restarts every chain from rest and lets it settle on the body as posed
    // this frame, with no velocity. Every layer forgets where it stood first,
    // so no garment settles against another's place from before.
    internal void Reset()
    {
        foreach (var chain in Chains)
            chain.LayerCached = false;
        var pose = Pose;
        foreach (var chain in Chains)
        {
            if (chain.Trunk != null)
                chain.Attach = FromTrunk(chain);
            SpringSolver.ResetSettled(chain, Colliders, pose, chain.Attach, Scale);
        }
        _lastStep = SpringSolver.MaxStepSeconds;
    }

    // Carries every chain along with a body that was placed rather than moved.
    internal void Shift(NVector3 delta)
    {
        foreach (var chain in Chains)
        {
            SpringSolver.Shift(chain, delta);
            if (chain.Trunk != null)
                continue;
            chain.LastAttach.Carry += delta;
            chain.LastAttach.Origin += delta;
        }
    }

    // Steps the chains through a frame, splitting it into steps no longer
    // than about MaxStepSeconds. A fixed-rate clock that skips frames updates
    // the hair in bursts against a body that animates every frame, which
    // reads as twitching at the tips. Across a split frame, what each chain
    // hangs from moves part of the way per step, so neither step takes the
    // whole frame's travel. A hitch longer than the solver integrates
    // carries the chains straight along by the travel beyond that, so a
    // stall cannot cram the whole frame's travel into a short simulation and
    // fling them. The frame must be longer than zero. Returns the steps taken.
    internal int Advance(float deltaTime)
    {
        var clamped = SpringSolver.ClampFrame(deltaTime);
        if (clamped < deltaTime)
        {
            // A branch goes with the chain at the base of its trunks.
            var excess = 1f - clamped / deltaTime;
            foreach (var chain in Chains)
                SpringSolver.Shift(chain, Travel(chain) * excess);
            foreach (var chain in Chains)
            {
                if (chain.Trunk != null)
                    continue;
                var travel = Travel(chain) * excess;
                chain.LastAttach.Carry += travel;
                chain.LastAttach.Origin += travel;
            }
        }
        deltaTime = clamped;
        var steps = SpringSolver.StepsFor(deltaTime);
        var step = deltaTime / steps;
        var pose = Pose;
        for (var s = 1; s <= steps; s++)
        {
            var t = s / (float)steps;
            foreach (var chain in Chains)
            {
                var attach = chain.Trunk != null ? FromTrunk(chain)
                    : s == steps ? chain.Attach
                    : SpringAttach.Lerp(chain.LastAttach, chain.Attach, t);
                SpringSolver.Step(chain, Colliders, pose, attach, Scale, step, _lastStep);
            }
            _lastStep = step;
        }
        return steps;
    }

    // Eases every chain that is not hanging toward its rest rotation, by
    // `blend`, 0 to 1. Called while the body is out of range, without Read.
    internal void Relax(float blend)
    {
        foreach (var chain in Chains)
        {
            if (chain.Spec.HangDown)
                continue;
            foreach (var j in chain.Joints)
                j.LastLocal = NQuaternion.Slerp(j.LastLocal, j.RestLocalRotation, blend);
        }
    }

    // Puts every hanging chain straight onto its hanging shape for this
    // frame's pose. Their rest pose lies along the T-posed arm, so a body at
    // rest keeps them hanging instead. A chain at rest that a hanging chain
    // branches off is re-posed on this frame's parent too.
    // Needs Read with HangBones filled.
    internal void Hang()
    {
        var pose = Pose;
        foreach (var chain in _hangChains)
        {
            if (chain.Trunk != null)
                chain.Attach = FromTrunk(chain);
            if (chain.Spec.HangDown)
                SpringSolver.Reset(chain, pose, chain.Attach, Scale);
            else
                SpringSolver.PoseJoints(chain, chain.Attach, Scale);
        }
    }

    // A mounted chain's root is pinned to its place on the anchor and carried
    // by the anchor's travel. Any other hangs from its parent.
    private SpringAttach Animated(SpringChain chain)
    {
        var rotation = Rotations[chain.ParentBone];
        if (chain.Mounted)
        {
            var anchor = Positions[chain.AnchorBone];
            return new SpringAttach
            {
                Carry = anchor,
                Rotation = rotation,
                Origin = anchor + NVector3.Transform(chain.MountOffset * Scale, Rotations[chain.AnchorBone]),
            };
        }
        var parent = Positions[chain.ParentBone];
        return new SpringAttach
        {
            Carry = parent,
            Rotation = rotation,
            Origin = parent + NVector3.Transform(chain.RootLocalPosition * Scale, rotation),
        };
    }

    // How far a chain's carry point moved this frame: its own for a chain
    // whose parent is animated, the chain's at the base of its trunks for a
    // branch.
    private static NVector3 Travel(SpringChain chain)
    {
        while (chain.Trunk != null)
            chain = chain.Trunk;
        return chain.Attach.Carry - chain.LastAttach.Carry;
    }

    private SpringAttach FromTrunk(SpringChain chain)
    {
        var bone = chain.Trunk.Joints[chain.TrunkJoint];
        var rotation = bone.World * chain.TrunkRotation;
        var parent = bone.Head + NVector3.Transform(chain.TrunkOffset * Scale, bone.World);
        return new SpringAttach
        {
            Carry = parent,
            Rotation = rotation,
            Origin = parent + NVector3.Transform(chain.RootLocalPosition * Scale, rotation),
        };
    }
}
