using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace WOMENACE.Code;

// A body's bind pose, free of the engine: every transform by name, with its
// parent, its first child, its local pose and its pose in the root's space.
// The live rig fills it from the body prefab and the offline replay from the
// prefab's YAML, so both build their chains from the same data.
internal sealed class SpringBindPose
{
    public string[] Names;
    public int[] Parents;
    public int[] FirstChildren;
    public NVector3[] LocalPositions;
    public NQuaternion[] LocalRotations;
    public NVector3[] Positions;
    public NQuaternion[] Rotations;
    public Dictionary<string, int> Index;

    // Transforms listed parents before children, the root first.
    internal static SpringBindPose Create(string[] names, int[] parents, int[] firstChildren, NVector3[] localPositions, NQuaternion[] localRotations)
    {
        var count = names.Length;
        var bind = new SpringBindPose
        {
            Names = names,
            Parents = parents,
            FirstChildren = firstChildren,
            LocalPositions = localPositions,
            LocalRotations = localRotations,
            Positions = new NVector3[count],
            Rotations = new NQuaternion[count],
            Index = new Dictionary<string, int>(StringComparer.Ordinal),
        };
        for (var i = 0; i < count; i++)
        {
            bind.Index.TryAdd(names[i], i);
            var parent = parents[i];
            if (parent < 0)
            {
                bind.Rotations[i] = NQuaternion.Identity;
                continue;
            }
            bind.Rotations[i] = bind.Rotations[parent] * localRotations[i];
            bind.Positions[i] = bind.Positions[parent] + NVector3.Transform(localPositions[i], bind.Rotations[parent]);
        }
        return bind;
    }

    internal int Find(string name) => name != null && Index.TryGetValue(name, out var i) ? i : -1;
}

// Builds a profile's solver chains against a body's bind pose, in the order
// they step. The live rig and the offline replay both build through here.
internal static class SpringChainBuilder
{
    // The bones the body pose is read from (SpringBodyPose).
    internal static readonly string[] PoseBones = { "Hips", "Spine", "UpperLeg_L", "LowerLeg_L", "UpperLeg_R", "LowerLeg_R", "Foot_L", "Foot_R" };

    // The MENACE humanoid bones, recorded as a full-body pose by the flight
    // recorder so the offline replay can drive every outfit's skeleton.
    internal static readonly string[] HumanoidBones =
    {
        "Hips", "Spine", "Spine2", "Neck", "Head",
        "Shoulder_L", "UpperArm_L", "LowerArm_L", "Hand_L",
        "Shoulder_R", "UpperArm_R", "LowerArm_R", "Hand_R",
        "UpperLeg_L", "LowerLeg_L", "Foot_L",
        "UpperLeg_R", "LowerLeg_R", "Foot_R",
    };

    // `present` says whether the live body carries a bone. The bind pose comes
    // from the body's prefab, and a chain the instance lacks is left out.
    internal static List<SpringChain> Build(SpringBodyProfile profile, SpringBindPose bind, Predicate<string> present, List<string> missing)
    {
        var chains = new List<SpringChain>();
        var hips = bind.Find("Hips");
        var spine = bind.Find("Spine");
        var thigh = bind.Find("UpperLeg_L");
        var knee = bind.Find("LowerLeg_L");
        foreach (var spec in profile.Chains)
        {
            foreach (var entry in spec.Roots)
            {
                var path = ChainPath(bind, entry, out var rootName);
                if (path == null)
                {
                    missing.Add(entry);
                    continue;
                }
                if (!path.TrueForAll(bone => present(bind.Names[bone])) || !present(bind.Names[bind.Parents[path[0]]]))
                {
                    missing.Add(entry + " (not on the instance)");
                    continue;
                }
                var joints = new List<SpringJoint>();
                for (var k = 0; k + 1 < path.Count; k++)
                {
                    var local = bind.LocalPositions[path[k + 1]];
                    if (local.LengthSquared() < 1e-10f)
                        break;
                    joints.Add(new SpringJoint
                    {
                        RestLocalRotation = bind.LocalRotations[path[k]],
                        LastLocal = bind.LocalRotations[path[k]],
                        RestAxis = NVector3.Normalize(local),
                        LocalLength = local.Length(),
                    });
                }
                if (joints.Count == 0)
                {
                    missing.Add(entry + " (no link)");
                    continue;
                }
                var root = path[0];
                var chain = new SpringChain
                {
                    Spec = spec,
                    Root = rootName,
                    Joints = joints.ToArray(),
                    Targets = new NVector3[joints.Count],
                    Bones = new string[joints.Count],
                    Tip = bind.Names[path[joints.Count]],
                    Parent = bind.Names[bind.Parents[root]],
                    RootLocalPosition = bind.LocalPositions[root],
                    Depth = Depth(bind, root),
                };
                for (var k = 0; k < joints.Count; k++)
                    chain.Bones[k] = bind.Names[path[k]];

                var anchor = spec.HangDown ? bind.Find(spec.Anchor) : -1;
                if (spec.HangDown && spec.Anchor != null && (anchor < 0 || !present(spec.Anchor)))
                    missing.Add($"{entry} anchor {spec.Anchor}");
                else if (anchor >= 0)
                {
                    chain.Anchor = spec.Anchor;
                    chain.MountOffset = NVector3.Transform(bind.Positions[root] - bind.Positions[anchor], NQuaternion.Inverse(bind.Rotations[anchor]));
                }

                if (hips >= 0)
                {
                    var inverseHips = NQuaternion.Inverse(bind.Rotations[hips]);
                    var restInHips = new NVector3[joints.Count];
                    var heights = new float[joints.Count];
                    for (var k = 0; k < joints.Count; k++)
                    {
                        var particle = path[k + 1];
                        restInHips[k] = NVector3.Transform(bind.Positions[particle] - bind.Positions[hips], inverseHips);
                        heights[k] = bind.Positions[particle].Y;
                    }
                    if (spine >= 0 && thigh >= 0)
                        SpringSolver.SetHipsRest(chain, restInHips, heights, bind.Positions[spine].Y, bind.Positions[thigh].Y);

                    // Out on the root's side, flat in the root's space, and
                    // forward too for a root in front of the hips, then into
                    // the hips' frame. A root behind the hips is pushed
                    // straight out, since forward would push it through the
                    // body, and one on the body's centre line straight to
                    // its own side of it.
                    var side = bind.Positions[root] - bind.Positions[hips];
                    side.Y = 0f;
                    var lateral = SpringSolver.Direction(new NVector3(side.X, 0f, 0f));
                    var outward = side.Z >= 0f
                        ? SpringSolver.Direction(lateral + NVector3.UnitZ)
                        : lateral.LengthSquared() > 0f ? lateral : -NVector3.UnitZ;
                    chain.Outward = NVector3.Transform(outward, inverseHips);
                }
                chains.Add(chain);
            }
        }

        if (hips >= 0 && spine >= 0)
        {
            var upInHips = NVector3.Transform(bind.Positions[spine] - bind.Positions[hips], NQuaternion.Inverse(bind.Rotations[hips]));
            SpringSolver.LinkLayers(chains, upInHips);
            if (knee >= 0)
            {
                var inverseHips = NQuaternion.Inverse(bind.Rotations[hips]);
                SpringSolver.FindSplits(chains, upInHips, NVector3.Transform(NVector3.UnitZ, inverseHips), NVector3.Transform(bind.Positions[knee] - bind.Positions[hips], inverseHips));
            }
        }
        FindTrunks(chains, bind, missing);
        OrderLayers(chains);

        // Inner layers before the layers over them, so a coat is held out by
        // where the skirt under it is this step, and trunks before branches
        // within a layer, so a chain hanging off another chain's bone reads
        // that bone after its own chain has turned it.
        var order = chains.ToArray();
        Array.Sort(order, (x, y) => x.Layer != y.Layer ? x.Layer.CompareTo(y.Layer) : x.Depth.CompareTo(y.Depth));
        return order.ToList();
    }

    // A chain whose parent lies under another chain's bone hangs from that
    // bone: the nearest ancestor of the parent, itself included, that any
    // chain turns. The bones between hold their bind pose, so the parent's
    // pose follows from the trunk's bone through its bind offset. A branch
    // hangs from its trunk, so an anchor on it is dropped.
    private static void FindTrunks(List<SpringChain> chains, SpringBindPose bind, List<string> missing)
    {
        var owner = new Dictionary<string, (SpringChain Chain, int Joint)>(StringComparer.Ordinal);
        foreach (var chain in chains)
            for (var k = 0; k < chain.Bones.Length; k++)
                owner.TryAdd(chain.Bones[k], (chain, k));
        foreach (var chain in chains)
        {
            var parent = bind.Find(chain.Parent);
            for (var bone = parent; bone >= 0; bone = bind.Parents[bone])
            {
                if (!owner.TryGetValue(bind.Names[bone], out var trunk) || trunk.Chain == chain)
                    continue;
                var inverse = NQuaternion.Inverse(bind.Rotations[bone]);
                chain.Trunk = trunk.Chain;
                chain.TrunkJoint = trunk.Joint;
                chain.TrunkOffset = NVector3.Transform(bind.Positions[parent] - bind.Positions[bone], inverse);
                chain.TrunkRotation = inverse * bind.Rotations[parent];
                if (chain.Anchor != null)
                {
                    missing.Add($"{chain.Root} anchor {chain.Anchor} (hangs from {trunk.Chain.Root})");
                    chain.Anchor = null;
                }
                break;
            }
        }
    }

    // A chain steps after the chains it reads this step: its trunk, and the
    // layers under it (LinkLayers). A branch lifted to its trunk's layer lifts
    // the layers over it in turn. No honest layering runs deeper than there
    // are chains, so the climb stops there should the links ever loop.
    private static void OrderLayers(List<SpringChain> chains)
    {
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var chain in chains)
            {
                var layer = chain.Trunk?.Layer ?? 0;
                if (chain.Inner != null)
                    foreach (var inner in chain.Inner)
                        layer = Math.Max(layer, inner.Layer + 1);
                if (layer > chain.Layer && layer <= chains.Count)
                {
                    chain.Layer = layer;
                    changed = true;
                }
            }
        }
    }

    // The bones of one chain entry, root first. "Root>Leaf" is the path
    // between the two, found by walking up from the leaf. A bare root walks
    // down first children until a leaf.
    private static List<int> ChainPath(SpringBindPose bind, string entry, out string rootName)
    {
        var split = entry.IndexOf('>');
        rootName = split < 0 ? entry : entry[..split];
        var root = bind.Find(rootName);
        if (root < 0)
            return null;
        var path = new List<int>();
        if (split < 0)
        {
            for (var bone = root; bone >= 0; bone = bind.FirstChildren[bone])
                path.Add(bone);
            return path;
        }
        var leaf = bind.Find(entry[(split + 1)..]);
        if (leaf < 0)
            return null;
        for (var bone = leaf; bone >= 0; bone = bind.Parents[bone])
        {
            path.Add(bone);
            if (bone == root)
            {
                path.Reverse();
                return path;
            }
        }
        return null;
    }

    private static int Depth(SpringBindPose bind, int bone)
    {
        var depth = 0;
        for (var p = bind.Parents[bone]; p >= 0; p = bind.Parents[p])
            depth++;
        return depth;
    }
}
