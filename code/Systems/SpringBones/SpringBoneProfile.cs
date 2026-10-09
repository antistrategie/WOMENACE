using System.Reflection;

namespace WOMENACE.Code;

// Which colliders a chain collides with. Chains that hang from a limb or the
// chest leave that body part's group out: a chain whose root already sits
// inside a collider is shoved out of it every step and jitters.
[Flags]
internal enum SpringColliderGroup
{
    None = 0,
    Body = 2,
    // Kept apart from the torso because the legs sweep through anything
    // hanging behind them at a run, and a chain shoved by a leg every stride
    // kinks.
    Legs = 4,
    // The capsule across the hip joints. Short skirts and coats leave it out:
    // their waistbands hug the hips inside it, and the legs and the seat
    // already hold their panels. Gowns, whose backs mostly hang still while
    // the legs move, take it.
    Pelvis = 8,
    // The buttocks, behind the hip joints, which the pelvis capsule does not
    // reach, so the back of a skirt cannot sag into them.
    Seat = 16,
}

// Tuning for a set of chains that share one material, and the garment they
// make up: chains of one spec are one garment, for layering one garment over
// another (a coat over a skirt). The fields are mutable so the Springs dev
// verbs can retune a live rig without a rebuild.
internal sealed class SpringChainSpec
{
    public string Name;

    // One entry per chain: the first bone it ROTATES, optionally followed by
    // ">" and the leaf bone it ends at ("HairD1>HairD21"). With a leaf named,
    // the chain is the path between the two, which stays right on a rig that
    // branches. Without one it walks down first children to a leaf. The leaf
    // is only the last particle, so a chain needs a bone below its root.
    public string[] Roots;

    // Spring frequency in Hz pulling each particle toward where the rest pose
    // would put it, at the root link and at the tip, eased between. A soft
    // tip under a stiff root is what makes motion travel down the chain.
    // Gravity only wins against a spring well under 1 Hz.
    public float Stiffness;
    public float TipStiffness;

    // Fraction of each 60 Hz step's velocity lost, 0 to 1.
    public float Drag;

    // Multiple of real gravity.
    public float Gravity;

    // Collision radius of each particle, in metres.
    public float HitRadius;

    // Largest stray from the rest shape, as an angle at the chain's root, in
    // degrees. Soft: the stray eases toward it past half the value.
    public float MaxAngle;

    // How much the chain's target shape follows the legs' swing, 0 to 1. For
    // skirts and coats, whose T-pose rest shape runs through the thighs as
    // soon as the legs move.
    public float LegDrive;

    // Share of the legs' swing the back of the chain's garment follows, the
    // front following fully. The legs swing mostly forward in a stride, so
    // the back of a skirt has little to follow.
    public float LegDriveBack = 0.25f;

    // Share of the leg drive the back of the garment takes while a knee is
    // bent deep, as in the deployed kneel with one shin flat behind her. A
    // floor-length back that hangs still lets that shin straight through it,
    // while one that always follows the legs stands off a wide stance like a
    // bell. The larger of this and LegDriveBack applies.
    public float LegDriveBackKneeling;

    // Largest swing, in degrees about the chain's root, its target shape
    // takes to clear the legs. Uncapped, a skirt kept wide of both legs in a
    // stance stretches as wide as the stance and fans out, and past the cap
    // the legs meet the cloth's own collisions instead.
    public float LegClearAngle = 180f;

    // Drag on the chain's motion relative to the body part it hangs from,
    // rather than relative to the world. World drag blows trailing cloth back
    // through a steady run as if in a gale. Relative to the body, a steady
    // run makes no wind and only changes in the body's motion swing it.
    public bool RelativeDrag;

    // Largest angle any link may point away from its own target direction,
    // in degrees, eased past half. It only exists to stop a chain folding
    // into a hairpin, so it sits well past any honest swing. Defaults to 100.
    public float FoldGuard = 100f;

    // Share of the motion of the body part the chain hangs from that carries
    // the particles straight along, 0 to 1. Walking, turning and aiming reach
    // the chain through that part, so this sets how hard they swing it.
    public float Follow;

    // The target shape hangs straight down from the root, tipped forward by
    // HangTilt, whatever the rest pose: for long cloth the rip leaves lying
    // along the T-posed arm, which only gravity was meant to drop.
    public bool HangDown;

    // Degrees a HangDown chain is tipped from plumb toward the body's front.
    // Ribbons are styled, not left to hang where real gravity would put
    // them.
    public float HangTilt;

    // For a HangDown chain, the bone its root is pinned to, where the bind
    // pose puts it, and whose travel carries it: cloth tied at the shoulder
    // but rigged off the arm hangs from the torso, not from the arm. Null
    // hangs it from the bone it is rigged to.
    public string Anchor;

    public SpringColliderGroup Colliders;
}

// A capsule between two points on the segment from bone From to bone To, at
// fractions T0 and T1 along it. Values outside 0 to 1 extend past either bone.
// Defining colliders by bone POSITIONS rather than an offset in a bone's local
// frame keeps them independent of the per-rig bone axes the PMX conversion
// produces.
internal sealed class SpringColliderSpec
{
    public string From;
    public string To;
    public float T0;
    public float T1;
    public float Radius;
    public SpringColliderGroup Group;

    // Offsets toward the body's back and down, in metres, for a capsule that
    // sits off the line between two bones.
    public float Back;
    public float Down;
}

// Spring setup for one baked body. A body is matched to its profile by its
// LOD0 mesh node and its chain roots (SpringBodyProfiles.Match), since every
// outfit of one doll can share the LOD0 name.
internal sealed class SpringBodyProfile
{
    public string LodMesh;
    public SpringChainSpec[] Chains;
    public SpringColliderSpec[] Colliders;
}

internal static partial class SpringBodyProfiles
{
    // Shared body colliders on the MENACE humanoid bone names, sized from the
    // MMD static rigid bodies of a 1.566 m Doll rip and scaled to the doll's
    // own height.
    private const float ColliderReferenceHeight = 1.566f;

    internal static SpringColliderSpec[] HumanoidColliders(float heightMetres)
    {
        var colliders = ReferenceColliders();
        var scale = heightMetres / ColliderReferenceHeight;
        foreach (var c in colliders)
        {
            c.Radius *= scale;
            c.Back *= scale;
            c.Down *= scale;
        }
        return colliders;
    }

    // Leg capsules for a slim floor-length skirt. The shared thigh capsules
    // start a tenth of the way down the thigh, so a raised thigh comes through
    // the top rows of a skirt hung off the hips. These start just above the
    // hip joint and are wider.
    internal static SpringColliderSpec[] SlimGownColliders(float heightMetres)
    {
        var colliders = HumanoidColliders(heightMetres);
        foreach (var c in colliders)
            if (c.From.StartsWith("UpperLeg_", StringComparison.Ordinal) && c.To.StartsWith("LowerLeg_", StringComparison.Ordinal))
            {
                c.T0 = -0.1f;
                c.Radius *= 1.3f;
            }
        return colliders;
    }

    private static SpringColliderSpec[] ReferenceColliders() => new[]
    {
        new SpringColliderSpec { From = "Spine2", To = "Neck", T0 = 0.15f, T1 = 0.75f, Radius = 0.1f, Group = SpringColliderGroup.Body },
        new SpringColliderSpec { From = "Spine", To = "Spine2", T0 = 0f, T1 = 1f, Radius = 0.085f, Group = SpringColliderGroup.Body },
        new SpringColliderSpec { From = "UpperLeg_L", To = "UpperLeg_R", T0 = -0.1f, T1 = 1.1f, Radius = 0.1f, Group = SpringColliderGroup.Pelvis },
        // Measured on the meshes: buttocks reach 12 to 16 cm behind the hip
        // joints, centred a little below them.
        new SpringColliderSpec { From = "UpperLeg_L", To = "UpperLeg_R", T0 = 0.1f, T1 = 0.9f, Radius = 0.085f, Back = 0.06f, Down = 0.03f, Group = SpringColliderGroup.Seat },
        new SpringColliderSpec { From = "UpperLeg_L", To = "LowerLeg_L", T0 = 0.1f, T1 = 1f, Radius = 0.075f, Group = SpringColliderGroup.Legs },
        new SpringColliderSpec { From = "UpperLeg_R", To = "LowerLeg_R", T0 = 0.1f, T1 = 1f, Radius = 0.075f, Group = SpringColliderGroup.Legs },
        new SpringColliderSpec { From = "LowerLeg_L", To = "Foot_L", T0 = 0f, T1 = 1f, Radius = 0.06f, Group = SpringColliderGroup.Legs },
        new SpringColliderSpec { From = "LowerLeg_R", To = "Foot_R", T0 = 0f, T1 = 1f, Radius = 0.06f, Group = SpringColliderGroup.Legs },
    };

    // Every profile is a static field of this partial class, one file per
    // outfit under Profiles/, gathered on first use. The converter writes
    // those files, so a new outfit needs no registration here.
    private static SpringBodyProfile[] _all;

    internal static SpringBodyProfile[] All => _all ??= typeof(SpringBodyProfiles)
        .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        .Where(f => f.FieldType == typeof(SpringBodyProfile))
        .Select(f => (SpringBodyProfile)f.GetValue(null))
        .Where(p => p != null)
        .ToArray();

    // Least share of a profile's chain roots a body must carry for the profile
    // to apply. An outfit converted without spring bones shares its LOD0 name
    // and a few bone names with a converted sibling, but not its chains.
    private const float MatchMinShare = 0.9f;

    // The profile for a body, from the names of its transforms: of the
    // profiles whose LOD0 node it has, the one with the largest share of its
    // chain roots present, and of equal shares the one with more chains, the
    // more specific. Outfits of one doll share the LOD0 name, so the roots
    // tell them apart.
    internal static SpringBodyProfile Match(ISet<string> names)
    {
        SpringBodyProfile best = null;
        var bestShare = MatchMinShare - 1e-4f;
        var bestRoots = 0;
        foreach (var profile in All)
        {
            if (!names.Contains(profile.LodMesh))
                continue;
            var roots = 0;
            var present = 0;
            foreach (var spec in profile.Chains)
            {
                foreach (var entry in spec.Roots)
                {
                    roots++;
                    var split = entry.IndexOf('>');
                    if (names.Contains(split < 0 ? entry : entry[..split]))
                        present++;
                }
            }
            var share = roots == 0 ? 0f : present / (float)roots;
            if (share > bestShare || (share == bestShare && roots > bestRoots))
            {
                best = profile;
                bestShare = share;
                bestRoots = roots;
            }
        }
        return best;
    }
}
