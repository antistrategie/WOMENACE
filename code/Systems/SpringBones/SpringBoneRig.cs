using UnityEngine;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace WOMENACE.Code;

// The spring simulation for one live body instance: reads its transforms
// into a SpringBody, which steps the chains, and writes the bones back.
//
// The bones the solver writes have no animation curves, so the Animator never
// overwrites them and the result persists until the next write. Their rest
// pose comes from the body prefab, through the same chain builder the offline
// replay uses.
//
// Every call into a UnityEngine type from mod code is a call into native code
// in this IL2CPP game, and a value it returns is boxed on the game's heap. So
// the game is touched as little as possible: each animated bone the chains
// need is read once a frame, everything below them is worked out from the
// bind pose, and each spring bone's local rotation is written once a frame.
internal sealed partial class SpringBoneRig
{
    // How long the body may stay past the simulated LODs, or off screen,
    // before its chains ease to rest. A body hovering at the LOD switch as
    // the camera zooms would otherwise reset and settle on every crossing.
    private const float OutOfRangeGraceSeconds = 0.25f;

    // How long the chains take to ease back to rest once the body has left
    // range, and the rate they ease at.
    private const float RelaxSeconds = 0.4f;
    private const float RelaxTimeConstant = 0.1f;

    // A solver chain with the transforms it writes, and the ones the dev
    // probes read.
    private sealed class RigChain
    {
        public SpringChain Chain;
        public Transform[] Bones;
        public Transform Tip;
        public Transform ParentTransform;
    }

    internal readonly Animator Animator;
    internal readonly Transform Root;
    internal readonly SpringBodyProfile Profile;
    private readonly Renderer[] _lods;
    private readonly SpringBody _body;
    private readonly RigChain[] _chains;

    // The transforms behind SpringBody.Bones, read each frame.
    private readonly Transform[] _reads;

    // A rig starts out of range, so a body built far away takes no reset
    // until it is drawn close.
    private float _awaySeconds = OutOfRangeGraceSeconds;
    // A rig starts at its bind pose, which is rest, so it has nothing to ease.
    private float _relaxSeconds = RelaxSeconds;
    private NVector3 _lastRootPosition;
    private bool _needsReset = true;

    internal int ChainCount => _chains.Length;
    internal int JointCount { get; }
    internal bool Simulating { get; private set; }

    // The LOD the body is drawn at, or -1 when off screen.
    internal int Lod { get; private set; }

    private SpringBoneRig(Animator animator, SpringBodyProfile profile, Renderer[] lods, SpringBody body, RigChain[] chains, Transform[] reads)
    {
        Animator = animator;
        Root = animator.transform;
        Profile = profile;
        _lods = lods;
        _body = body;
        _chains = chains;
        _reads = reads;
        foreach (var rc in chains)
            JointCount += rc.Bones.Length;
    }

    // Builds the rig from the instance's hierarchy, or returns null with the
    // reason when the instance does not carry the profile's bones. Every name
    // resolves to the FIRST transform carrying it, matching how the baked
    // hierarchy holds each bone once.
    internal static SpringBoneRig Build(Animator animator, GameObject prefab, SpringBodyProfile profile, out string error)
    {
        error = null;
        var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        foreach (var t in animator.GetComponentsInChildren<Transform>(includeInactive: true))
            byName.TryAdd(t.name, t);

        if (!byName.ContainsKey(profile.LodMesh))
        {
            error = $"no '{profile.LodMesh}' node";
            return null;
        }
        // The body's LOD renderers by index, found by the shared naming
        // "<basename>_LOD<n>" the profile's LOD0 name carries.
        var lodPrefix = profile.LodMesh[..^1];
        var lods = new List<Renderer>();
        for (var n = 0; byName.TryGetValue(lodPrefix + n, out var node); n++)
        {
            var renderer = node.GetComponent<Renderer>();
            if (renderer != null)
                lods.Add(renderer);
        }

        var missing = new List<string>();
        var body = new SpringBody(profile, BindPose(prefab), animator.transform.lossyScale.x, byName.ContainsKey, missing);
        var chains = new RigChain[body.Chains.Length];
        for (var c = 0; c < chains.Length; c++)
        {
            var chain = body.Chains[c];
            var bones = new Transform[chain.Bones.Length];
            for (var k = 0; k < bones.Length; k++)
                bones[k] = byName[chain.Bones[k]];
            chains[c] = new RigChain
            {
                Chain = chain,
                Bones = bones,
                Tip = byName.GetValueOrDefault(chain.Tip),
                ParentTransform = byName[chain.Parent],
            };
        }
        var reads = new Transform[body.Bones.Length];
        for (var i = 0; i < reads.Length; i++)
            reads[i] = byName[body.Bones[i]];

        if (chains.Length == 0)
        {
            error = "no spring chains resolved: " + string.Join(", ", missing);
            return null;
        }
        if (missing.Count > 0)
            error = "unresolved: " + string.Join(", ", missing);
        PairedReadsWork(reads[0]);
        var rig = new SpringBoneRig(animator, profile, lods.ToArray(), body, chains, reads);
        rig._head = byName.GetValueOrDefault("Head");
        rig._humanoid = HumanoidBones.Select(name => byName.GetValueOrDefault(name)).ToArray();
        return rig;
    }

    // The prefab's hierarchy as a bind pose. The prefab is never animated, so
    // its transforms hold the bind pose the instance's animated bones left.
    // The baked bodies carry no scale below the root, so none is read.
    private static SpringBindPose BindPose(GameObject prefab)
    {
        var transforms = prefab.GetComponentsInChildren<Transform>(includeInactive: true);
        var index = new Dictionary<IntPtr, int>();
        for (var i = 0; i < transforms.Length; i++)
            index[transforms[i].Pointer] = i;
        var count = transforms.Length;
        var names = new string[count];
        var parents = new int[count];
        var firstChildren = new int[count];
        var positions = new NVector3[count];
        var rotations = new NQuaternion[count];
        for (var i = 0; i < count; i++)
        {
            var t = transforms[i];
            names[i] = t.name;
            parents[i] = i == 0 || t.parent == null ? -1 : index.GetValueOrDefault(t.parent.Pointer, -1);
            firstChildren[i] = t.childCount > 0 ? index.GetValueOrDefault(t.GetChild(0).Pointer, -1) : -1;
            positions[i] = N(t.localPosition);
            rotations[i] = N(t.localRotation);
        }
        return SpringBindPose.Create(names, parents, firstChildren, positions, rotations);
    }

    internal bool IsDead
    {
        get
        {
            try { return Animator == null || Animator.WasCollected || Root == null; }
            catch { return true; }
        }
    }

    // `resets` is how many rigs may still reset this frame. A reset settles
    // every chain over many steps at once, so bodies coming into range
    // together (a squad on a zoom) reset on successive frames, each holding
    // its rest pose until its turn.
    internal void Tick(float deltaTime, ref int resets)
    {
        // Only the closer LODs simulate (SpringBoneSystem.MaxSimulatedLod).
        // Further out the motion does not read, and the cost is per joint per
        // body. Once out of range for a moment, the chains ease back to rest
        // rather than freezing mid-swing (every LOD shares these bones, so a
        // frozen swing stays on screen), and coming back starts from rest.
        Lod = DrawnLod();
        var inRange = Lod >= 0 && Lod <= SpringBoneSystem.MaxSimulatedLod;
        _awaySeconds = inRange ? 0f : _awaySeconds + deltaTime;
        Simulating = _awaySeconds < OutOfRangeGraceSeconds;
        if (!Simulating)
        {
            Relax(deltaTime);
            return;
        }

        if (_needsReset)
        {
            if (resets <= 0)
            {
                Hang();
                return;
            }
            resets--;
        }
        else if (deltaTime <= 0f)
            return;
        var rootPosition = N(Root.position);
        _relaxSeconds = 0f;
        SampleRoot(rootPosition);
        ReadBones(null);
        _body.Read();

        int steps;
        if (_needsReset)
        {
            _body.Reset();
            _needsReset = false;
            steps = 0;
        }
        else
        {
            if (SpringSolver.IsTeleport(_lastRootPosition, rootPosition, deltaTime))
                _body.Shift(rootPosition - _lastRootPosition);
            steps = _body.Advance(deltaTime);
        }
        _lastRootPosition = rootPosition;
        Write(hanging: null);

        Sample(deltaTime);
        Record(deltaTime, steps);
    }

    // Whether Transform.GetPositionAndRotation is callable, which reads both
    // in one native call with nothing boxed. Probed once on the first rig.
    private static bool? _pairedReads;

    private static void PairedReadsWork(Transform probe)
    {
        if (_pairedReads.HasValue)
            return;
        try
        {
            probe.GetPositionAndRotation(out _, out _);
            _pairedReads = true;
        }
        catch
        {
            _pairedReads = false;
        }
    }

    // Reads the animated bones into the body: all of them, or those listed.
    private void ReadBones(int[] only)
    {
        var count = only?.Length ?? _reads.Length;
        var paired = _pairedReads == true;
        for (var n = 0; n < count; n++)
        {
            var i = only?[n] ?? n;
            var bone = _reads[i];
            if (paired)
            {
                bone.GetPositionAndRotation(out var position, out var rotation);
                _body.Positions[i] = N(position);
                _body.Rotations[i] = N(rotation);
            }
            else
            {
                _body.Positions[i] = N(bone.position);
                _body.Rotations[i] = N(bone.rotation);
            }
        }
    }

    // Writes the chains' rotations, and a mounted chain's root as a world
    // position, so the hierarchy, and with it the Animator's bindings, stays
    // as baked. `hanging` limits the write to hanging chains or the rest.
    private void Write(bool? hanging)
    {
        foreach (var rc in _chains)
        {
            if (hanging.HasValue && rc.Chain.Spec.HangDown != hanging.Value)
                continue;
            if (rc.Chain.Mounted)
                rc.Bones[0].position = U(rc.Chain.Attach.Origin);
            var joints = rc.Chain.Joints;
            for (var k = 0; k < joints.Length; k++)
                rc.Bones[k].localRotation = U(joints[k].LastLocal);
        }
    }

    private int DrawnLod()
    {
        if (_lods.Length == 0)
            return 0;
        for (var n = 0; n < _lods.Length; n++)
            if (_lods[n].isVisible)
                return n;
        return -1;
    }

    // Eases every bone toward its rest rotation for RelaxSeconds after the
    // body leaves range, then leaves the bones alone until it comes back.
    // Hanging chains are not eased toward their rest pose, which lies along
    // the T-posed arm: while the body is drawn they hang where they are
    // pinned, and off screen they are left alone.
    private void Relax(float deltaTime)
    {
        _needsReset = true;
        // Paused, the Animator holds the body still too.
        if (deltaTime <= 0f)
            return;
        if (_relaxSeconds < RelaxSeconds)
        {
            _relaxSeconds += deltaTime;
            var blend = _relaxSeconds >= RelaxSeconds ? 1f : 1f - MathF.Exp(-deltaTime / RelaxTimeConstant);
            _body.Relax(blend);
            Write(hanging: false);
        }
        if (Lod >= 0)
            Hang();
    }

    // Keeps the hanging chains hanging, and mounted roots on their anchors,
    // on a drawn body that is not simulating.
    private void Hang()
    {
        if (!_body.HasHanging)
            return;
        ReadBones(_body.HangBones);
        _body.Read(colliders: false);
        _body.Hang();
        Write(hanging: true);
    }

    // Shoves every particle sideways, for checking the motion by eye.
    internal void Kick(Vector3 velocityPerStep)
    {
        var kick = N(velocityPerStep);
        foreach (var rc in _chains)
            foreach (var j in rc.Chain.Joints)
                j.PrevTail = j.Tail - kick;
    }

    private static NVector3 N(Vector3 v) => new(v.x, v.y, v.z);

    private static NQuaternion N(Quaternion q) => new(q.x, q.y, q.z, q.w);

    private static Quaternion U(NQuaternion q) => new(q.X, q.Y, q.Z, q.W);

    private static Vector3 U(NVector3 v) => new(v.X, v.Y, v.Z);

    // Jitter probe: each chain tip's position in its root's parent space, so
    // the body's own motion drops out, and the frame-to-frame change in its
    // velocity. Smooth swinging reads a few millimetres, twitching reads tens.
    private int _sampleFramesLeft;
    private int _sampleFrames;
    private float _sampleSeconds;
    private NVector3[] _tipPrev;
    private NVector3[] _tipPrevPrev;
    private float[] _jerkSum;
    private float[] _jerkMax;
    private NVector3 _rootPrev;
    private NVector3 _rootPrevPrev;
    private int _rootFrames;
    private int _rootStillFrames;
    private float _rootJerkSum;
    private float _rootJerkMax;
    private float _rootTravel;

    internal void StartSampling(int frames)
    {
        _sampleFramesLeft = frames;
        _sampleFrames = 0;
        _sampleSeconds = 0f;
        _tipPrev = new NVector3[_chains.Length];
        _tipPrevPrev = new NVector3[_chains.Length];
        _jerkSum = new float[_chains.Length];
        _jerkMax = new float[_chains.Length];
        _rootFrames = 0;
        _rootStillFrames = 0;
        _rootJerkSum = 0f;
        _rootJerkMax = 0f;
        _rootTravel = 0f;
    }

    // The body's own motion over the same frames: how far it travelled, how
    // jerky its path was, and how many frames it stood still. Stepped movement
    // (a body that only advances on some frames) shows as still frames while
    // travelling.
    private void SampleRoot(NVector3 root)
    {
        if (_sampleFramesLeft <= 0)
            return;
        if (_rootFrames >= 1)
        {
            var moved = (root - _rootPrev).Length();
            _rootTravel += moved;
            if (moved < 1e-6f)
                _rootStillFrames++;
        }
        if (_rootFrames >= 2)
        {
            var jerk = (root - 2f * _rootPrev + _rootPrevPrev).Length() * 1000f;
            _rootJerkSum += jerk;
            _rootJerkMax = Math.Max(_rootJerkMax, jerk);
        }
        _rootPrevPrev = _rootPrev;
        _rootPrev = root;
        _rootFrames++;
    }

    private void Sample(float deltaTime)
    {
        if (_sampleFramesLeft <= 0)
            return;
        _sampleFramesLeft--;
        for (var c = 0; c < _chains.Length; c++)
        {
            var rc = _chains[c];
            var tip = N(rc.ParentTransform.InverseTransformPoint(U(rc.Chain.Joints[^1].Tail)));
            if (_sampleFrames >= 2)
            {
                var jerk = (tip - 2f * _tipPrev[c] + _tipPrevPrev[c]).Length() * 1000f;
                _jerkSum[c] += jerk;
                _jerkMax[c] = Math.Max(_jerkMax[c], jerk);
            }
            _tipPrevPrev[c] = _tipPrev[c];
            _tipPrev[c] = tip;
        }
        _sampleFrames++;
        _sampleSeconds += deltaTime;
    }

    internal string SampleReport()
    {
        if (_jerkSum == null)
            return null;
        var counted = Mathf.Max(1, _sampleFrames - 2);
        var fps = _sampleSeconds > 0f ? _sampleFrames / _sampleSeconds : 0f;
        var lines = new List<string> { $"jitter over {_sampleFrames} frames at {fps:F0} fps{(_sampleFramesLeft > 0 ? " (still sampling)" : "")}:" };
        lines.Add($"  body: travelled {_rootTravel:F2} m, still on {_rootStillFrames} frames, path jerk mean {_rootJerkSum / Mathf.Max(1, _rootFrames - 2):F1} mm, max {_rootJerkMax:F1} mm");
        for (var c = 0; c < _chains.Length; c++)
            lines.Add($"  {_chains[c].Chain.Spec.Name}/{_chains[c].Chain.Root}: mean {_jerkSum[c] / counted:F1} mm, max {_jerkMax[c]:F1} mm");
        return string.Join("\n", lines);
    }

    // Per chain: how far its root link has swung from rest, and the largest
    // bend of any link against its parent. A rig that never moves reads 0.
    internal IEnumerable<string> Describe()
    {
        foreach (var rc in _chains)
        {
            var chain = rc.Chain;
            var swing = 0f;
            var maxBend = 0f;
            for (var i = 0; i < chain.Joints.Length; i++)
            {
                var bend = SpringSolver.AngleDegrees(N(rc.Bones[i].localRotation), chain.Joints[i].RestLocalRotation);
                if (i == 0)
                    swing = bend;
                maxBend = Mathf.Max(maxBend, bend);
            }
            yield return $"{chain.Spec.Name}/{chain.Root}: {chain.Joints.Length} links, swing {swing:F1} deg, max bend {maxBend:F1} deg";
        }
    }
}
