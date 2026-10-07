namespace WOMENACE.Code;

internal static partial class SpringBodyProfiles
{
    // Tuned live, so it spells out every value instead of using the presets.
    // Chains follow the PMX's own physics bodies. HeadFrontCloth link 01 is a
    // bone-following body in the rip, so those strands start swinging at
    // link 02. The ropes stay folded onto the torso: their bones point up
    // and each is a single link.
    internal static readonly SpringBodyProfile Ots14Default = new()
    {
        LodMesh = "ots14_LOD0",
        Chains = new[]
        {
            // The fringe and veil are short and stiff, with too little
            // travel to reach the head, so they collide with nothing.
            new SpringChainSpec
            {
                Name = "fringe",
                Roots = new[] { "HairA1", "HairB1", "HairC1" },
                Stiffness = 6f, TipStiffness = 4f, Drag = 0.3f, Gravity = 0.05f, HitRadius = 0.01f, MaxAngle = 10f, Follow = 0.7f,
                Colliders = SpringColliderGroup.None,
            },
            // The tails already hang in the rest pose, so gravity only
            // adds a little weight. A soft tip under a firmer root lets
            // a turn travel down them.
            new SpringChainSpec
            {
                Name = "twintails",
                Roots = new[] { "HairD1", "HairE1" },
                Stiffness = 3f, TipStiffness = 1.5f, Drag = 0.2f, Gravity = 0.3f, HitRadius = 0.025f, MaxAngle = 25f, Follow = 0.8f,
                Colliders = SpringColliderGroup.Body | SpringColliderGroup.Pelvis,
            },
            new SpringChainSpec
            {
                Name = "veil",
                Roots = new[]
                {
                    "HeadFrontCloth_L02", "HeadFrontCloth_LM02", "HeadFrontCloth_RM02",
                    "HeadFrontCloth_R02", "HeadFrontCloth_RR02", "HeadBackCloth_M01",
                },
                Stiffness = 5f, TipStiffness = 4f, Drag = 0.2f, Gravity = 0.2f, HitRadius = 0.01f, MaxAngle = 12f, Follow = 0.7f,
                Colliders = SpringColliderGroup.None,
            },
            // The ribbons' rest is the T-pose, hanging straight down off a
            // level arm. They run on the ribbon preset's values with a tight
            // fold guard, since softer, gravity-hung springs fold the elbow
            // ribbon over itself as the arm moves.
            new SpringChainSpec
            {
                Name = "sleeves",
                Roots = new[]
                {
                    "UpperArmCloth_L01", "ForeArmCloth_L01", "ElbowCloth_L01",
                    "UpperArmCloth_R01", "ForeArmCloth_R01", "ElbowCloth_R01",
                },
                Stiffness = 1.5f, TipStiffness = 1f, Drag = 0.2f, Gravity = 0.5f, HitRadius = 0.015f, MaxAngle = 60f, Follow = 0.6f,
                RelativeDrag = true, FoldGuard = 60f,
                Colliders = SpringColliderGroup.Body | SpringColliderGroup.Pelvis,
            },
            new SpringChainSpec
            {
                Name = "chest",
                Roots = new[] { "ChestCloth_L05", "ChestCloth_R05" },
                Stiffness = 5f, TipStiffness = 5f, Drag = 0.3f, Gravity = 0.3f, HitRadius = 0.01f, MaxAngle = 15f, Follow = 0.7f,
                Colliders = SpringColliderGroup.None,
            },
        },
        Colliders = HumanoidColliders(1.566f),
    };
}
