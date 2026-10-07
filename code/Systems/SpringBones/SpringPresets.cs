namespace WOMENACE.Code;

// Chain tuning by kind, so a generated profile only has to sort its chains
// into categories. A profile that needs something else spells out its own
// SpringChainSpec instead.
internal static class SpringPresets
{
    // Short strands near the face: stiff and light, with too little travel
    // to reach the head.
    internal static SpringChainSpec ShortHair(params string[] roots) => new()
    {
        Name = "short_hair",
        Roots = roots,
        Stiffness = 3.5f,
        TipStiffness = 2f,
        Drag = 0.2f,
        Gravity = 0.05f,
        HitRadius = 0.01f,
        MaxAngle = 15f,
        Follow = 0.4f,
        Colliders = SpringColliderGroup.None,
    };

    // Tails and long locks. They already hang in the rest pose, so gravity
    // only adds weight, and a soft tip under a firmer root lets a turn travel
    // down them. Drag and Follow keep a long tail from folding on a sprint
    // without going stiff.
    internal static SpringChainSpec LongHair(params string[] roots) => new()
    {
        Name = "long_hair",
        Roots = roots,
        Stiffness = 3f,
        TipStiffness = 1.5f,
        Drag = 0.2f,
        Gravity = 0.3f,
        HitRadius = 0.025f,
        MaxAngle = 25f,
        Follow = 0.8f,
        Colliders = SpringColliderGroup.Body | SpringColliderGroup.Pelvis,
    };

    // Short cloth that holds its shape: veils, collars, flaps. Cloth folds
    // less sharply than hair, so its fold guard sits tighter.
    internal static SpringChainSpec StiffCloth(params string[] roots) => new()
    {
        Name = "stiff_cloth",
        Roots = roots,
        Stiffness = 3f,
        TipStiffness = 2f,
        Drag = 0.2f,
        Gravity = 0.2f,
        HitRadius = 0.01f,
        MaxAngle = 18f,
        Follow = 0.4f,
        FoldGuard = 60f,
        Colliders = SpringColliderGroup.None,
    };

    // Cloth that hangs: ribbons, sashes, ties. Firm enough to sway rather
    // than flop, and its drag is relative to the body, since world drag
    // flutters a tie in a run's wind until it whips into the body.
    internal static SpringChainSpec Ribbon(params string[] roots) => new()
    {
        Name = "ribbon",
        Roots = roots,
        Stiffness = 1.5f,
        TipStiffness = 1f,
        Drag = 0.2f,
        Gravity = 0.5f,
        HitRadius = 0.015f,
        MaxAngle = 45f,
        Follow = 0.6f,
        RelativeDrag = true,
        FoldGuard = 60f,
        Colliders = SpringColliderGroup.Body | SpringColliderGroup.Pelvis,
    };

    // Long cloth tied round a limb or at the shoulder that the rip leaves
    // lying along the T-posed arm. It hangs straight down, tipped toward the
    // body's front, rather than holding the arm-shaped rest pose, which a
    // lowered or raised arm swings out sideways. With an anchor its root is
    // pinned to that bone and carried by it, so cloth tied at the shoulder
    // but rigged off the arm hangs from the torso. The torso pushes it out
    // and the legs move its target aside rather than kicking it. Soft, heavy
    // and lightly carried, so it swings by its own weight.
    internal static SpringChainSpec DrapedRibbon(string anchor, params string[] roots) => new()
    {
        Name = "draped_ribbon",
        Roots = roots,
        Stiffness = 0.6f,
        TipStiffness = 0.4f,
        Drag = 0.4f,
        Gravity = 1.5f,
        HitRadius = 0.02f,
        MaxAngle = 70f,
        Follow = 0.3f,
        RelativeDrag = true,
        FoldGuard = 60f,
        Anchor = anchor,
        HangDown = true,
        HangTilt = 10f,
        Colliders = SpringColliderGroup.Body | SpringColliderGroup.Pelvis,
    };

    // A rigid accessory that swings as a whole: a case, a bag, a holster, a
    // pendant. Usually one link, firm, with a modest swing.
    internal static SpringChainSpec Accessory(params string[] roots) => new()
    {
        Name = "accessory",
        Roots = roots,
        Stiffness = 1.2f,
        TipStiffness = 1.2f,
        Drag = 0.2f,
        Gravity = 0.2f,
        HitRadius = 0.02f,
        MaxAngle = 25f,
        Follow = 0.4f,
        Colliders = SpringColliderGroup.Body | SpringColliderGroup.Pelvis,
    };

    // The columns of one skirt or coat. They keep their flare, follow the
    // legs, and let the legs push them aside, so they collide with the legs
    // and the seat as well as the torso. A thigh lifted on a running stride
    // swings the front panels far off their rest, so the stray cap sits wide
    // and the particles carry a thicker margin over the legs. Panels never
    // fold sharply, so their fold guard sits tight, which keeps a leg-swept
    // coattail from flipping into a zigzag.
    internal static SpringChainSpec Skirt(params string[] roots) => new()
    {
        Name = "skirt",
        Roots = roots,
        Stiffness = 1.5f,
        TipStiffness = 1f,
        Drag = 0.4f,
        Gravity = 0.5f,
        HitRadius = 0.03f,
        MaxAngle = 60f,
        Follow = 0.8f,
        FoldGuard = 60f,
        LegDrive = 1f,
        LegClearAngle = 30f,
        Colliders = SpringColliderGroup.Body | SpringColliderGroup.Legs | SpringColliderGroup.Seat,
    };

    // A skirt reaching well past the knee. Its margin over the legs is thin
    // and its swing clear of them small, since below the knee the cloth
    // carries the swing all the way to the hem, and a slim floor-length skirt
    // kept wide of both legs stretches as wide as the stance. Its back does
    // not follow the legs and the hips hold it out: turned with a thigh
    // coming forward, cloth behind and below the hip joints swings into the
    // buttocks. The converter picks it over Skirt for a garment whose hem
    // falls a quarter of the shin below the knee.
    internal static SpringChainSpec Gown(params string[] roots)
    {
        var spec = Skirt(roots);
        spec.Name = "gown";
        spec.HitRadius = 0.015f;
        spec.LegClearAngle = 20f;
        spec.LegDriveBack = 0f;
        spec.Colliders |= SpringColliderGroup.Pelvis;
        return spec;
    }
}
