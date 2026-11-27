"""Build a support team's per-state takes from its per-object AssetStudio exports.

Every crew member was exported with the same set of clips (her native idle and the donor Doll
clips). For each state the crew play their clip a little out of step, the props either play
their native idle or ride their carrier, and the result is baked into one take. A transition
(a state with "between") is built instead from the poses of the two states it joins.

    blender --background --factory-startup --python build_team_states.py --
        <spec.json> <work_dir> [<state>...]

prepare_team.py runs it as its `states` step. Naming states after <work_dir> bakes only those. The
Idle state is written with the meshes as <team>_default.fbx, the body the Unity bake builds the
prefab from. Every other state is written as <team>_<state>.fbx with the armatures only, so its
clip binds to the same bone paths. A "motion" empty under the team root carries the team's
travel (see "travel").

The spec is teams/<team>/states.json. Its paths are relative to <work_dir>, so the spec in the
repository holds no machine's paths. Bones are named as on the GFL2 crew rig (Root_M, Hip_L and
so on). Every option:

    team             the asset name, which prefixes every output and the props' armatures
    out_dir          where the FBXs go
    review           a .blend written at the end with every state back to back, for checking
    preview          true renders side and front PNGs of each state into <out_dir>/preview
    members          the crew's exports, one FBX each
    props            the props' exports, whose armatures are named <team>_<file stem>
    extra_sources    exports from other GFL2 scenes (the ult), read as references by file stem
    texture_dirs     folders whose PNGs colour the review's materials
    crew             the members' armature names (their file stems)
    prop_names       the props' armature names
    formation_clip   per member, her native idle: its first frame gives her place in the team
    prop_idle        per prop, its native idle
    spine_bone       the bone a slung prop rides unless it names one, and the bone whose
                     direction gives a member's facing for "facing" and "ground"
    prop_materials   name prefixes of the props' materials, coloured in the review from the
                     texture named "<material>_d"
    coat             a long coat that donor clips leave still:
        chains       per side, [bone, "front" | "side" | "back"] for each chain's root bone. A
                     clip that leaves the first chain's bone unkeyed is a donor clip
        follow       per position, [share of the thigh's swing taken going forward, going back]
        parent       the bone the chains hang from, in whose frame the swing is measured
    split            cuts that move loose pieces of a prop's mesh off a bone they share onto a
                     bone of their own, so a state can place them apart. GFL2 builds a weapon
                     from loose pieces, each on one bone, and several pieces can share one.
                     prop_cuts.py holds the rule, which render_weapon.py's fold reads too:
        prop, from, bone   the prop armature, the bone the pieces are weighted to, and the bone
                     they move onto: new, a child of "from" at rest, so it plays every GFL2 take
                     as "from" does, or one the prop already has, which takes them as they are
        below, above        pieces lying wholly below or above this, in metres along "axis"
        mid_below, mid_above  pieces whose centre lies below or above it
        axis         the prop's own axis at rest the cut measures along (0, 1, 2 for X, Y, Z,
                     Y by default)
        y_below      leaves out any piece reaching this high on Y
        head         "top" puts a new bone's head at the middle of the pieces' top 3 cm on Y, a
                     hinge for a part that folds, and otherwise at their centre
    markers          empties hung on a prop's bone that ride every take, so the Unity bake can
                     hang a skill point on one: name, prop, bone, and "at" (the prop armature's
                     space at rest)
    states           the states, in order. The cuts are made first, so a state names the new
                     bones as any other

A state:

    name             the state, and the FBX's suffix in lower case
    native           the crew keep their clips' own root placement ("place": "native") by default
    loop             the clips repeat, each member running "offset" frames ahead
    length           the take's length in frames, unless "pingpong" or "length_from" sets it
    length_from      the member whose clip span (and offset) gives the length
    pingpong         with "loop", every clip plays the length_from member's span forwards and
                     then backwards, so a held pose that is not a cycle loops without a jump
    in_place         every member's root bone stays on her formation spot (a donor run travels
                     on it)
    travel           a member whose Root_M ground travel moves onto the motion empty, every
                     member held in place relative to it, so the Unity import takes the travel
                     as root motion from the motion node
    idle_hair        each member's hair chains (under Head_M) hold her native idle's first
                     frame, where a donor clip leaves them in a bind pose that cuts the face
    lower_knees      a share (0 to 1) of each donor-clip thigh's forward swing taken back, so
                     the knees rise no higher than the coat can follow
    anchor           {prop, ref, frame}: the turn and shift that take the prop from where the
                     extra source "ref" has it at "frame" (1 by default) to where its native
                     idle has it at frame 1. A member placed "anchor" is carried by it, so a
                     weapon that stands on the ground keeps its place between states and the
                     crew keep theirs round it
    crew             per member, what she plays:
        clip         the clip
        of           the member whose clip it is (the crew share one rig), herself by default
        range        [first, last] frames of it, or the whole clip
        offset       frames she runs ahead (loop) or starts late, so the crew are out of step
        place        "formation" (default unless native): her root on her formation spot plus
                     the clip's own travel unless in_place, facing the team's way. "shift": the
                     clip's own motion and facing, moved to start on her formation spot (a take
                     from another scene). "anchor": the whole take carried by the state's
                     anchor. "native": as the clip has it
        nudge        with "anchor", [x, y] metres in the team's frame added to the carry, for a
                     member who touches no anchored prop
        upper        {clip, frame, from, of}: the bones under "from" (one bone or a list,
                     Spine1_M by default) hold that clip's pose at that frame for the whole
                     take, "of" taking the clip from another member. A list of holds is applied
                     in order, so a later hold wins on the bones both name
        sway         {bone, child, hand, frame, hang, scale | swing, bend, bend_swing}: an arm
                     held by "upper" swung forward and back with the donor's run. "bone" to
                     "child" is the upper arm, turned about her side axis (read off "frame",
                     Chest_M by default) to "hang" degrees forward of straight down (10) plus
                     "scale" (0.25) of the donor arm's swing about its mean, or the donor's
                     swing paced to an amplitude of "swing" degrees either way. With "hand" the
                     elbow keeps "bend" degrees (20), deeper by up to "bend_swing" (10) as the
                     arm swings forward
        raise        {shoulder bone: degrees}: upper arms swung forward about her side axis
        open         {elbow bone: degrees}: elbows opened, so held hands move out from the body
        breathe      {bones, scale}: a held pose laid over with her native idle, each named bone
                     turning from the held pose as it turns from the idle's first frame, by
                     "scale" (1). Rotation only, so she keeps her spot. The state's "length"
                     sets how much of the idle plays
    props            per prop, one of:
        "idle"       it plays its native idle, in step with a pingpong when there is one
        follow       {follow: member, prop_ref, range, bones}: keyed frame by frame where the
                     extra source <prop_ref> has it over that range, moved as the member's
                     placement moved her. The bones named in "bones" play the reference's own
                     take too, and the rest hold the prop's native idle at frame 1
        match        {carrier, bone (Wrist_R by default), match: {prop_ref, carrier_ref,
                     frame}}: held on the carrier's bone as the reference holds it at that
                     frame. "idle:<name>" names the team's own prop or member in its native idle
        ground       {carrier, ground: {distance, angle, pivot, lid, lid_bone}}: stood on the
                     ground "distance" metres from her root, "angle" degrees round from her
                     facing (positive to her left), its +Z towards her and +Y up, with "pivot"
                     (its own space, the origin by default) on that spot. "lid" opens
                     "lid_bone" that many degrees about the bone's head along X. Placed once,
                     at the first state that grounds it, and kept there in later ones
        otherwise    slung on the carrier, placed at frame 1 in her frame (x right, y back,
                     z up, the team facing -Y):
            carrier, bone   who carries it and on which bone (spine_bone by default)
            at              where it goes, from her root or from "from_bone"'s head
            from_bone       the bone "at" is measured from
            pivot           the point of the prop (its own space) that goes to "at", in place
                            of its mesh's centre
            rot             a full turn (degrees, XYZ) from its own axes, for a prop whose long
                            axis alone does not fix how it is held
            dir, flip       without "rot", the way its long axis points, and that reversed
            facing          "at" and the turn are taken in the carrier's own facing at frame 1
                            rather than the team's
            steady          keyed frame by frame, taking only this share of the bone's turn
                            since frame 1, so a donor run's chest sway is not copied in full.
                            Without it a Child Of constraint holds the prop rigidly
            tilt            degrees (XYZ, her frame) turning the placed prop and its grips about
                            "at", so a lean can be tried without working the grips out again
            ride            {at, rot}: placed in the ride bone's own frame at frame 1 instead,
                            so a carry worked out in one state sits the same on her in a state
                            whose pose differs. Takes no "tilt", and its grips are in the
                            prop's space
            idle_pose       prop bones posed as its native idle has them at frame 1
            fold            [{bone, about, angle, axis, keep}]: a bone turned "angle" degrees
                            about "axis" (X by default) through "about" (the prop's space at
                            rest), the bones in "keep" held where they were, so a part folds
                            for the carry while its child stays put
            grips           per side ("L", "R"), where her hand goes, kept in the prop's space:
                            a point from "at" in her frame, or {at, fingers, thumb | palm} with
                            the way the knuckles point ("forearm" carries on along the forearm)
                            and the way the thumb side or the palm faces. "pole" is where the
                            elbow bends towards, from "at"'s origin. "hinge": true rolls the
                            upper arm first, so the forearm folds the way the rig's elbow folds
                            at rest, where the IK's shortest turn leaves the roll to chance.
                            "twist_limit" (degrees) turns the hand about its knuckles by the
                            least that keeps the forearm's twist within it, and gives the
                            forearm's twist bone half the roll, so the wrist is not wrung
            grip_space      "prop": the grips are given in the prop's own space, as read off
                            another state's carry
            grips_ref       {prop_ref, carrier_ref, frame, shift, elbow_push}: each wrist's
                            whole placement and elbow read from a reference scene at a frame.
                            "shift" moves a hand along the prop (its own space), "elbow_push"
                            moves an elbow's bend target (the team's frame)
        with any rule but "idle", single bones of the prop can go their own way after it is
        placed:
            bone_sling      {bone: how}: the bone carried on a crew member's bone as a sling
                            carries a whole prop: "carrier", "bone" (spine_bone by default),
                            "at" from "from_bone"'s head (root by default) in the team's frame
                            at frame 1, "rot" (degrees, XYZ) from the bone's own axes and
                            "pivot" in the prop's space. "along" [low, high] tilts it with the
                            line between two of her bones, and "at_spine" [across, out, up]
                            measures "at" along that line instead, so it keeps its place on her
                            back however far a state leans her. "local" takes "at" and "rot" in
                            the ride bone's own frame, read off the rig. "as" {bone, holder,
                            wrist, frame, from, from_axis, to, to_axis} holds it as the team's
                            idle has "holder" hold another bone of the prop, the line "from"
                            "from_axis" set along "to" "to_axis". "turn" (degrees, XYZ, its rest
                            axes) with "shift" (metres, the prop's rest frame) is a constant
                            pose on its parent instead, and "stay" holds it at its rest on the
                            prop while its parent is carried elsewhere
            bone_match      {bone: {carrier, bone, frame}}: the prop's bone held on the
                            carrier's bone as the team's idle holds it at that frame
            hide            {bone: frame}: the bone shrunk away from that frame of the state

A transition's take runs from one built state's last frame to the next one's first, so a
cross-fade does not slide the crew or fly the props. build_transition's docstring describes how
each option moves her. The states it reads are built in the same run, and naming it builds them:

    name, length     as for a state, and the length must be given
    between          [<from>, <to>], the two states it joins
    crew             per member:
        keys         [{at, pose, turn, shift, steps}]: poses at shares of the take, "pose"
                     "from", "to" or [<state>, 1 | "end"], turned "turn" degrees about her feet
                     or moved "shift" metres across the ground. "steps" counts the steps that
                     bring her to the key
        move         [<start>, <end>]: short for keys from the one end to the other
        stride, steps, still, overlap   key-to-key steps: as many as strides of "stride"
                     metres (0.5) cover, or "steps" per foot, a foot with less than "still"
                     metres (0.03) to go left planted, each step's slot overlapping the next by
                     "overlap" (0.25)
        lift, crouch metres a key-to-key step lifts its foot (0.08) and her hips sink while she
                     steps (0.05)
        coat         true turns the coat's chains with how far the leg IK moved her thighs
        walks        [{from, to, steps, ...}]: a planned walk from one key to another, which
                     needs a key at each end. Its timing and shape: "settle" (0.6), "accel"
                     (0.3), "turn_in", "turn_out" (0.35 each), "turn_end" (1.0), "face" (false
                     keeps her own facing), "lead" (the first foot), "first" (0.8) and "closing"
                     (0.6) for the first and last steps' time in the air, "turn" (0.25 metres a
                     radian), "vary" (0.1), "stride" (0.42), "lift" (0.06), "track" (0.04),
                     "roll" ([25, 15] degrees toe-off and heel strike) and "pivot" (20 degrees)
        bend         {degrees, window}: her spine bent forward from the waist
        look         {at: [<prop>, <bone>], window, limit, level}: neck and head turned towards
                     where the second state has that bone, up to "limit" degrees (70), about the
                     vertical only with "level"
        twist        {degrees, window, side | toward}: her spine turned about the vertical,
                     "side" 1 or -1, or towards a [<prop>, <bone>] of the second state, the side
                     chosen once as the turn begins
        hands        {prop, bone, until, release, sides, slide, wrap, fingers}: her hands kept
                     on a prop's bone as the first state holds it, letting go over [until,
                     release] or per side ("sides"), sliding along the part over the "slide"
                     window and closing round it over "wrap". "fingers": "from" keeps the first
                     state's closed fingers
        grab         {prop, hands: {<side>: [<bone>, <share>]}, window, fingers}: her hands put
                     on points of the placed prop, a share of the way from the bone's head to
                     the part's far end
        A window of bend, look, twist or grab is two shares (rising and settling again) or
        four (rising, holding and falling). Every other window is a start and an end share,
        eased from the one end to the other.
    props            per prop, {window, bones, <rule>}: the object goes from end to end over
                     "window", and "bones" gives a bone (and those under it) its own rule. A
                     rule:
        carrier, bone  rides her bone (Chest_M by default), its hold changing over "window"
        path         [[<share of the take>, <share of the way>], ...] in place of "window"
        lands        with a carrier, goes from her hold to where the second state has it
        clear        {of, by, fade}: a landing part pushed out flat from her torso until its
                     long axis keeps "by" metres from her spine, faded in over "fade"
        phases       [{until, ...}]: rules in turn, each until its "until". "place" "from" or
                     "to" holds an end's placement, "hide" shrinks it away, "opens" goes from
                     the phase before to the second state's placement, a "carrier" ("bone")
                     holds it as it was when the phase began, and with "upright" ("lift"
                     metres) it goes whole on her bone, stood as the second state stands it
        hide_between true shows it only on the first and last frames
        With no carrier or phases it goes straight from the one placement to the other.

swing_coat is not an option. With "coat" set, every member on a donor clip has her coat chains
swung with her thighs frame by frame: each chain turns about its root by its "follow" share of
the thigh's swing from rest, the forward share while the thigh is ahead of rest and the back share
while it is behind, so the thighs do not pass through the coat.
"""
import json
import math
import os
import re
import sys

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from prop_cuts import cut_takes, pieces_of  # noqa: E402

_args = sys.argv[sys.argv.index("--") + 1:]
spec = json.load(open(_args[0], encoding="utf-8"))
# The spec's paths are relative to the team's work folder (prepare_team.py's --work), passed after
# the spec, so the spec in the repository holds no machine's paths.
_work = _args[1] if len(_args) > 1 else os.path.dirname(os.path.abspath(_args[0]))


def _in_work(path):
    return path if os.path.isabs(path) else os.path.join(_work, path).replace("\\", "/")


for _key in ("out_dir", "review"):
    if spec.get(_key):
        spec[_key] = _in_work(spec[_key])
for _key in ("members", "props", "extra_sources", "texture_dirs"):
    spec[_key] = [_in_work(p) for p in spec.get(_key, [])]
os.makedirs(spec["out_dir"], exist_ok=True)
team = spec["team"]
out_dir = spec["out_dir"]
spine_bone = spec["spine_bone"]
# The bone a transition carries a prop on unless its rule names one. A carry in a state rides the
# spine (spine_bone) like a sling, but a prop passed from hand to hand during a transition goes
# with the chest, which the arms reach from.
transition_carry_bone = "Chest_M"
coat = spec.get("coat")

# Built up as the script runs: each member's formation spot, each grounded prop's placement,
# the reference placements read from the extra sources, and each baked state for the review.
places = {}
grounded = {}
tracks = {}
review = []

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = 60

root = bpy.data.objects.new(team, None)
scene.collection.objects.link(root)
motion = bpy.data.objects.new("motion", None)
scene.collection.objects.link(motion)
motion.parent = root


def fcurves(action):
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                yield from bag.fcurves


# ---- fold the per-object exports into one team ----
def load(src, name, free=False):
    """Import one AssetStudio export and fold its 0.01 scene root into the team root. Returns the
    objects it added and its armature."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=src, use_anim=True, automatic_bone_orientation=False, ignore_leaf_bones=False)
    added = [o for o in bpy.data.objects if o not in before]
    old_roots = [o for o in added if o.parent is None and o.type == "EMPTY" and abs(o.scale.x - 0.01) < 1e-6]
    for arm in [o for o in added if o.parent is None and o.type == "ARMATURE" and abs(o.scale.x - 0.01) < 1e-6]:
        arm.scale = (1.0, 1.0, 1.0)
        arm.parent = root
        arm.name = name
    for old in old_roots:
        for child in list(old.children):
            # The child keeps the root's axis conversion and gains metres: the 0.01 root only
            # converted AssetStudio's centimetres.
            world = child.matrix_world.copy()
            child.parent = root
            child.matrix_world = world
            child.scale = child.scale * 100.0
            child.location = child.location * 100.0
    kept = [o for o in added if o not in old_roots]
    for old in old_roots:
        bpy.data.objects.remove(old)
    arm = next(o for o in kept if o.type == "ARMATURE")
    # The importer connects a bone whose head sits on its parent's tail, and a connected bone
    # ignores its location keys. GFL2 prop takes move a prop's bones by location, so a prop's
    # bones are freed (<free>). Its rest stays where it was. A crew rig stays connected: donor
    # clips key its arms at the donor's bone lengths, which the connection ignores.
    if free and any(b.use_connect for b in arm.data.bones):
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode="EDIT")
        for eb in arm.data.edit_bones:
            eb.use_connect = False
        bpy.ops.object.mode_set(mode="OBJECT")
    return kept, arm


def stem_of(src):
    return src.replace("\\", "/").rsplit("/", 1)[-1].rsplit(".", 1)[0]


for src in spec["members"]:
    load(src, f"{team}_{stem_of(src)}")
for src in spec["props"]:
    load(src, f"{team}_{stem_of(src)}", free=True)
for o in list(bpy.data.objects):
    if o.type == "MESH" and o.name.endswith("_lod1"):
        bpy.data.objects.remove(o)


def split_part(cut):
    """Give part of a prop its own bone: the loose pieces weighted to "from" that lie wholly
    below "below" or above "above", or whose centres lie below "mid_below" or above
    "mid_above" (metres along the prop's own "axis" at rest, Y by default), move onto a new
    bone "bone", a child of "from". Its head is the pieces' centre, or with "head": "top" the
    middle of their top three centimetres along Y (a bipod leg's hinge). GFL2 builds Fabric's
    mortar from loose pieces, each on one bone, but its barrel and baseplate share one, and so
    do the bipod's legs, and the carry parts them. A child of "from" at rest plays every GFL2
    take as the shared bone did, so only a state that places it moves it. "y_below" also leaves
    out any piece reaching that high on Y (the traverse handwheels, out at the bipod's sides
    above its legs, which must not fold away with a leg)."""
    arm = bpy.data.objects[cut["prop"]]
    picked = {}
    for mesh in (o for o in arm.children_recursive if o.type == "MESH"):
        source = mesh.vertex_groups.get(cut["from"])
        if source is None:
            continue
        rel = arm.matrix_world.inverted() @ mesh.matrix_world
        owner = {v.index: max(v.groups, key=lambda g: g.weight).group for v in mesh.data.vertices if v.groups}
        chosen = []
        for piece in pieces_of(mesh):
            if all(owner.get(i) == source.index for i in piece) \
                    and cut_takes(cut, [rel @ mesh.data.vertices[i].co for i in piece]):
                chosen += piece
        picked[mesh] = (rel, chosen)
    pts = [rel @ m.data.vertices[i].co for m, (rel, ids) in picked.items() for i in ids]
    centre = sum(pts, Vector()) / len(pts)
    if cut.get("head") == "top":
        top = max(p.y for p in pts)
        crown = [p for p in pts if p.y > top - 0.03]
        centre = sum(crown, Vector()) / len(crown)
    # A bone the prop already has takes the pieces as they are (the barrel's collar, which
    # clamps it to the bipod's traverse bar, given to the bipod so its handwheel stays joined
    # when the barrel goes to another carrier).
    if cut["bone"] not in arm.data.bones:
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode="EDIT")
        eb = arm.data.edit_bones.new(cut["bone"])
        eb.head, eb.tail = centre, centre + Vector((0.0, 0.1, 0.0))
        eb.parent = arm.data.edit_bones[cut["from"]]
        bpy.ops.object.mode_set(mode="OBJECT")
    for mesh, (rel, ids) in picked.items():
        group = mesh.vertex_groups.get(cut["bone"]) or mesh.vertex_groups.new(name=cut["bone"])
        mesh.vertex_groups[cut["from"]].remove(ids)
        group.add(ids, 1.0, "REPLACE")
    print(f"split {cut['prop']}: {len(pts)} vertices from {cut['from']} onto {cut['bone']} at {tuple(round(v, 3) for v in centre)}")


for cut in spec.get("split", []):
    split_part(cut)

# Takes from other GFL2 scenes (the ult). The crew's rest comes from the skin's bind pose, the
# same in every scene, so a crew member's take from there plays on the team's own armature. A
# prop's rest is wherever that scene had it, so a prop's take is read off the scene's own
# objects instead (see track), which stay loaded as references until the tracks are read.
extras, extra_objects = {}, []
for src in spec.get("extra_sources", []):
    before_actions = set(bpy.data.actions)
    added, arm = load(src, f"ref_{stem_of(src)}", free=True)
    for a in set(bpy.data.actions) - before_actions:
        a.use_fake_user = True
    extras[stem_of(src)] = arm
    extra_objects += added

arms = {o.name: o for o in bpy.data.objects if o.type == "ARMATURE" and o not in extra_objects}
members = {name: arms[name] for name in spec["crew"]}
props = {name: arms[name] for name in spec["prop_names"]}
# Each prop armature's own placement from the import: it carries the FBX axis conversion as a
# rotation on the object, which a prop playing its native take needs back.
prop_base = {name: (obj.matrix_parent_inverse.copy(), obj.matrix_basis.copy()) for name, obj in props.items()}
# Actions per armature, by clip name: AssetStudio names a take "<armature>|<clip>|Base Layer",
# and Blender suffixes the armature part when several imports share a name.
clips = {}
for a in bpy.data.actions:
    parts = a.name.split("|")
    if len(parts) >= 2:
        clips.setdefault(parts[1], []).append(a)


def action_for(arm, clip):
    """The import of <clip> made for this armature: the one whose bones it animates."""
    for a in clips.get(clip, []):
        if any('pose.bones["' in c.data_path for c in fcurves(a)) and a.name.startswith(arm.name.split(".")[0]):
            return a
    # Props share the scene root's name, so fall back to the clip's only import.
    found = clips.get(clip, [])
    return found[0] if len(found) == 1 else None


def find(name, under):
    """An object named <name> below <under> (its weapon socket or holder)."""
    stack = list(under.children)
    while stack:
        o = stack.pop()
        if o.name.split(".")[0] == name:
            return o
        stack.extend(o.children)
    return None


def formation(arm):
    """A member's place in the team: her root bone's location and rotation at the first frame of
    her native idle. The donor clips start their root at rest, so they lose it."""
    idle = action_for(arm, spec["formation_clip"][arm.name])
    loc, rot = [0.0, 0.0, 0.0], [1.0, 0.0, 0.0, 0.0]
    for fc in fcurves(idle):
        if fc.data_path == 'pose.bones["root"].location':
            loc[fc.array_index] = fc.keyframe_points[0].co.y
        elif fc.data_path == 'pose.bones["root"].rotation_quaternion':
            rot[fc.array_index] = fc.keyframe_points[0].co.y
    return Vector(loc), rot


# Leaf empties carry their own copies of every take. They deform nothing, so they stay still.
for o in bpy.data.objects:
    if o.type == "EMPTY" and o.animation_data:
        o.animation_data_clear()

# Markers on a prop's bone (the launcher's muzzle), "at" in the prop armature's own space at rest.
# They ride every take on the same path, so the Unity bake can hang a skill point on one.
for mark in spec.get("markers", []):
    host = props[mark["prop"]]
    empty = bpy.data.objects.new(mark["name"], None)
    scene.collection.objects.link(empty)
    empty.parent, empty.parent_type, empty.parent_bone = host, "BONE", mark["bone"]
    # A bone child hangs from the bone's tail. Placed from the bone's rest frame, so whatever pose
    # the import left the bone in does not move the marker off the prop.
    bone = host.data.bones[mark["bone"]]
    empty.matrix_parent_inverse = Matrix.Identity(4)
    empty.matrix_basis = (bone.matrix_local @ Matrix.Translation((0.0, bone.length, 0.0))).inverted() @ Matrix.Translation(Vector(mark["at"]))


def reset(obj):
    ad = obj.animation_data_create()
    ad.action = None
    for track in list(ad.nla_tracks):
        ad.nla_tracks.remove(track)
    for c in list(obj.constraints):
        obj.constraints.remove(c)
    # Taking an action away leaves the bones in its last pose, so they go back to rest.
    if obj.type == "ARMATURE":
        for pb in obj.pose.bones:
            pb.matrix_basis = Matrix.Identity(4)
    return ad


def assign(ad, action):
    """Give an object an action. Blender ties an action's slot to the object it was imported
    with, so an action from another import plays on a differently named object only once a
    slot is chosen for it."""
    ad.action = action
    if action is None or not len(action.slots):
        return
    own = {s.identifier for s in action.slots}
    if ad.action_slot is None or ad.action_slot.identifier not in own:
        ad.action_slot = action.slots[0]


def play(obj, action, start, loop):
    ad = reset(obj)
    track = ad.nla_tracks.new()
    strip = track.strips.new(action.name, int(start), action)
    if strip.action_slot is None and len(action.slots):
        strip.action_slot = action.slots[0]
    strip.extrapolation = "HOLD"
    if loop:
        strip.repeat = 4.0
    return strip


def play_pingpong(obj, action, first, last):
    """Play frames <first> to <last> of an action forwards from frame 1 and then backwards."""
    strip = play(obj, action, 1, False)
    strip.action_frame_start, strip.action_frame_end = first, last
    strip.frame_end = 1 + (last - first)
    back = strip.id_data.animation_data.nla_tracks[0].strips.new(f"{action.name} back", int(strip.frame_end), action)
    if back.action_slot is None and len(action.slots):
        back.action_slot = action.slots[0]
    back.action_frame_start, back.action_frame_end = first, last
    back.frame_end = back.frame_start + (last - first)
    back.use_reverse = True
    back.extrapolation = "HOLD_FORWARD"


def bake(objs, end, types):
    """Bake the objects' NLA and constraints into a fresh action each. The crew bake their poses
    only: their object transforms carry the fold's scale and placement and never move, and an
    object bake re-derives them wrongly through the 0.01 scene root."""
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.nla.bake(frame_start=1, frame_end=end, only_selected=False, visual_keying=True,
                     clear_constraints=True, use_current_action=False, bake_types=types)
    for o in objs:
        for track in list(o.animation_data.nla_tracks):
            o.animation_data.nla_tracks.remove(track)


def key_location(obj, data_path, frame, value):
    if data_path:
        pb = obj.pose.bones[data_path]
        pb.location = value
        pb.keyframe_insert("location", frame=frame)
    else:
        obj.location = value
        obj.keyframe_insert("location", frame=frame)


def ground(v):
    return Vector((v.x, v.y, 0.0))


def preview(name, length):
    """Side and front views at four points of the state, one PNG per view per point, for checking."""
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.color_type = "OBJECT"
    scene.render.resolution_x, scene.render.resolution_y = 420, 360
    cam = bpy.data.objects.get("preview_cam") or bpy.data.objects.new("preview_cam", bpy.data.cameras.new("preview_cam"))
    if cam.name not in scene.collection.objects:
        scene.collection.objects.link(cam)
    scene.camera = cam
    for i, o in enumerate(o for o in scene.objects if o.type == "MESH"):
        o.color = (0.35 + (i * 0.37) % 0.6, 0.35 + (i * 0.61) % 0.6, 0.35 + (i * 0.83) % 0.6, 1)
    for k, f in enumerate(sorted({1, max(1, length // 3), max(1, 2 * length // 3), length})):
        scene.frame_set(f)
        meshes = [o for o in scene.objects if o.type == "MESH"]
        pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        print(f"preview {name} f{f}: bounds {tuple(round(v, 2) for v in lo)} {tuple(round(v, 2) for v in hi)}")
        if k == 0:
            for o in meshes:
                b = [o.matrix_world @ Vector(c) for c in o.bound_box]
                size = Vector((max(p.x for p in b) - min(p.x for p in b), max(p.y for p in b) - min(p.y for p in b), max(p.z for p in b) - min(p.z for p in b)))
                print(f"   mesh {o.name:36} centre {tuple(round(v, 2) for v in sum(b, Vector()) / 8)} size {tuple(round(v, 2) for v in size)}")
        for view, offset in (("side", Vector((-4.5, 0, 0.9))), ("front", Vector((0, -4.5, 0.9)))):
            cam.location = Vector((0, 0, 0.8)) + offset
            cam.rotation_euler = (Vector((0, 0, 0.8)) - cam.location).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = f"{out_dir}/preview/{name.lower()}_{view}_{k}_f{f}.png"
            bpy.ops.render.render(write_still=True)


def sling(obj, prop, how):
    """Carry a prop on a crew member's bone (her spine, by default), the way a launcher is slung.

    The prop is placed by its mesh at frame 1, in the carrier's own frame (x right, y back, z up
    from her root, the team facing -Y): its centre goes to "at" (metres), and its long axis
    points along "dir". A Child Of constraint on the bone then holds that relation, so the prop
    rides her body."""
    arm = members[how["carrier"]]
    bone = how.get("bone", spine_bone)
    scene.frame_set(1)
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.location, obj.rotation_mode, obj.rotation_euler = (0, 0, 0), "XYZ", (0, 0, 0)
    obj.scale = (1, 1, 1)

    # The prop's mesh in its armature's space: its centre and its longest axis.
    pts = []
    for mesh in (o for o in obj.children_recursive if o.type == "MESH"):
        rel = obj.matrix_world.inverted() @ mesh.matrix_world
        pts += [rel @ Vector(c) for c in mesh.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre, extent = (lo + hi) / 2, hi - lo
    axis = Vector([1.0 if i == max(range(3), key=lambda k: extent[k]) else 0.0 for i in range(3)])
    # "pivot" names the point of the prop that goes to "at" (the laptop's hinge), in its own space.
    if "pivot" in how:
        centre = Vector(how["pivot"])

    # "at" is measured from her root, or from the head of "from_bone" (a shoulder for a prop
    # resting on it), at frame 1.
    origin = (arm.matrix_world @ arm.pose.bones[how.get("from_bone", "root")].matrix).translation
    at = Vector(how.get("at", (0.0, 0.2, 1.0)))
    if "rot" in how:
        # A full turn (degrees, XYZ) from the prop's own axes, for a prop whose long axis alone
        # does not fix how it is held (the laptop lying flat).
        turn = Euler([math.radians(v) for v in how["rot"]], "XYZ").to_matrix().to_4x4()
    else:
        direction = Vector(how.get("dir", (0.3, 0.0, 1.0))).normalized()
        if how.get("flip"):
            direction = -direction
        turn = axis.rotation_difference(direction).to_matrix().to_4x4()
    if how.get("facing"):
        # "at" and the turn are taken in her own facing at frame 1 rather than the team's, for a
        # member whose take turns her: the way the bone it rides points (the spine bones point
        # forward at rest), level. Her hips can face elsewhere while she points.
        ride = arm.pose.bones[bone]
        ahead = arm.matrix_world.to_3x3() @ (ride.tail - ride.head)
        ahead.z = 0.0
        face = Vector((0.0, -1.0, 0.0)).rotation_difference(ahead.normalized()).to_matrix().to_4x4()
        at = face.to_3x3() @ at
        turn = face @ turn
    spot = origin + at
    desired = Matrix.Translation(spot - (turn @ centre)) @ turn
    # "tilt" (degrees, XYZ, the carrier's frame) turns the placed prop about its contact point
    # "at", with the grips (the barrel leaned out off the gunner's shoulder, her hands going
    # with it), so a lean can be tried without working the grips out again.
    tilt = Euler([math.radians(v) for v in how.get("tilt", (0.0, 0.0, 0.0))], "XYZ").to_matrix()
    desired = Matrix.Translation(spot) @ tilt.to_4x4() @ Matrix.Translation(-spot) @ desired
    if "ride" in how:
        # Tilt and grips in the carrier's frame are worked out from "at", which a ride replaces.
        if "tilt" in how or (how.get("grips") and how.get("grip_space") != "prop"):
            raise SystemExit(f"{how['carrier']}: a ride takes no tilt, and its grips are in the prop's space")
        # "ride" places the prop on the ride bone itself ("at" metres and "rot" degrees XYZ in
        # the bone's frame at frame 1), so a carry worked out in one state sits the same on her
        # body in another whose pose differs (the barrel from the run, on her chest as she stands).
        r = how["ride"]
        on = no_scale(arm.matrix_world @ arm.pose.bones[bone].matrix)
        desired = on @ Matrix.Translation(Vector(r["at"])) @ Euler([math.radians(v) for v in r["rot"]], "XYZ").to_matrix().to_4x4()

    if "steady" in how:
        steady(obj, how, arm, bone, desired, centre)
    else:
        rigid(obj, arm, bone, desired)
    # Bones of the prop posed as its native idle has them at frame 1 (the laptop's lid, closed).
    for bname, basis in how.get("_idle_pose", {}).items():
        pb = obj.pose.bones[bname]
        pb.location, pb.rotation_quaternion, pb.scale = basis
    # Folded parts for the carry: a bone turned "angle" degrees about "axis" through "about" (the
    # prop's own space at rest), with the bones in "keep" held where they were, so a part can
    # fold although its child must not move (the mortar's bipod, whose child is the tube).
    for fold in how.get("fold", []):
        bpy.context.view_layer.update()
        kept = {n: obj.pose.bones[n].matrix.copy() for n in fold.get("keep", [])}
        pb = obj.pose.bones[fold["bone"]]
        about = Vector(fold["about"])
        turn = Quaternion(Vector(fold.get("axis", (1.0, 0.0, 0.0))), math.radians(fold["angle"])).to_matrix().to_4x4()
        pb.matrix = Matrix.Translation(about) @ turn @ Matrix.Translation(-about) @ pb.matrix
        bpy.context.view_layer.update()
        for n, m in kept.items():
            obj.pose.bones[n].matrix = m
            bpy.context.view_layer.update()
        for n in [fold["bone"]] + list(kept):
            key_pose(obj.pose.bones[n], 1)
    # Grip points, kept in the prop's own space so they ride it: hand placements read from a
    # reference scene ("grips_ref", the wrist's whole placement, so the hand turns as it does
    # there), or points given in the carrier's frame from "at".
    if "grips_ref" in how:
        # The elbow comes along as the arm's bend: the wrist's turn is the reference's, so the
        # forearm has to come in from where the reference has it or the wrist bends back.
        # "shift" moves a hand along the prop (in its own space) from where the reference has it,
        # for a hand the carrier cannot reach there (a front grip, with her chest square on).
        g = how["grips_ref"]
        out = {}
        for side in ("L", "R"):
            move = Matrix.Translation(Vector(g.get("shift", {}).get(side, (0.0, 0.0, 0.0))))
            out[side] = {"whole": move @ tracks[("grip", g["prop_ref"], g["carrier_ref"], side, g["frame"])],
                         "elbow": move @ tracks[("grip_elbow", g["prop_ref"], g["carrier_ref"], side, g["frame"])]}
            # "elbow_push" moves an elbow's bend target in the team's frame (x her left, z up),
            # to swing that arm's elbow out from her body where it would cut through it.
            if side in g.get("elbow_push", {}):
                out[side]["push"] = Vector(g["elbow_push"][side])
        return out
    grips = {}
    back = desired.to_3x3().inverted()
    for side, g in how.get("grips", {}).items():
        if isinstance(g, dict) and how.get("grip_space") == "prop":
            # Grips already in the prop's own space (read off another state's carry).
            grips[side] = {k: (Vector(v) if isinstance(v, list) else v) for k, v in g.items()}
        elif isinstance(g, dict):
            # A grip with a hand direction: where the knuckles point ("fingers") and which way
            # across the palm the thumb lies ("thumb") or which way the palm faces ("palm"), in the carrier's frame.
            fingers = g["fingers"]
            grips[side] = {"at": desired.inverted() @ (spot + tilt @ Vector(g["at"])),
                           "fingers": fingers if isinstance(fingers, str) else back @ (tilt @ Vector(fingers))}
            for k in ("thumb", "palm"):
                if k in g:
                    grips[side][k] = back @ (tilt @ Vector(g[k]))
            for k in ("hinge", "twist_limit"):
                if k in g:
                    grips[side][k] = g[k]
            if "pole" in g:
                # Where the elbow bends towards, from "at"'s origin in the carrier's frame (the
                # gunner's elbow under the barrel, her forearm wrapped round it).
                grips[side]["pole"] = desired.inverted() @ (spot + tilt @ Vector(g["pole"]))
        else:
            grips[side] = desired.inverted() @ (spot + tilt @ Vector(g))
    return grips


def on_ground(obj, how):
    """Stand a prop on the ground beside a crew member, facing her: "distance" metres from her
    root, "angle" degrees round from the way her spine points at frame 1 (positive to her left),
    its own +Z turned towards her and its +Y up. "lid" opens "lid_bone" that many degrees past
    its rest about the hinge."""
    g = how["ground"]
    arm = members[how["carrier"]]
    scene.frame_set(1)
    reset(obj)
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.rotation_mode = "XYZ"
    # Placed once, where the first state that sets it down has her at its first frame, and kept
    # there in every later state: a prop on the ground does not move between states.
    key = (how["carrier"], json.dumps(g, sort_keys=True))
    if key in grounded:
        obj.matrix_basis = grounded[key][0].copy()
        if grounded[key][1] is not None:
            obj.pose.bones[g["lid_bone"]].matrix_basis = grounded[key][1].copy()
        bpy.context.view_layer.update()
        return
    spine = arm.pose.bones[spine_bone]
    ahead = arm.matrix_world.to_3x3() @ (spine.tail - spine.head)
    ahead.z = 0.0
    ahead.normalize()
    out = Matrix.Rotation(math.radians(g.get("angle", 0.0)), 3, "Z") @ ahead
    base = (arm.matrix_world @ arm.pose.bones["root"].matrix).translation
    spot = Vector((base.x, base.y, 0.0)) + out * g.get("distance", 0.5)
    up, toward = Vector((0, 0, 1)), -out
    turn = Matrix((up.cross(toward), up, toward)).transposed().to_4x4()
    pivot = Vector(g.get("pivot", (0.0, 0.0, 0.0)))
    obj.matrix_basis = Matrix.Translation(spot) @ turn @ Matrix.Translation(-pivot)
    if "lid" in g:
        # The hinge runs along the prop's X through the lid bone's head.
        lid = obj.pose.bones[g["lid_bone"]]
        hinge = lid.head.copy()
        lid.matrix = Matrix.Translation(hinge) @ Matrix.Rotation(math.radians(-g["lid"]), 4, "X") @ Matrix.Translation(-hinge) @ lid.matrix
    bpy.context.view_layer.update()
    grounded[key] = (obj.matrix_basis.copy(), obj.pose.bones[g["lid_bone"]].matrix_basis.copy() if "lid" in g else None)


def steady(obj, how, arm, bone, desired, pivot):
    """Key a carried prop frame by frame: its pivot stays at "at" from "from_bone", and it takes
    only a share ("steady") of the bone's turn since frame 1. A donor run sways the chest from
    side to side, which a launcher resting on the shoulder should not copy in full."""
    share = float(how["steady"])
    lead = how.get("from_bone", "root")
    at = Vector(how.get("at", (0.0, 0.2, 1.0)))
    first = no_scale(arm.matrix_world @ arm.pose.bones[bone].matrix)
    rest_rot = desired.to_quaternion()
    obj.rotation_mode = "XYZ"
    prev = None
    for f in range(1, scene.frame_end + 1):
        scene.frame_set(f)
        now = no_scale(arm.matrix_world @ arm.pose.bones[bone].matrix)
        turned = (now @ first.inverted()).to_quaternion() @ rest_rot
        rot = rest_rot.slerp(turned, share)
        spot = (arm.matrix_world @ arm.pose.bones[lead].matrix).translation + at
        placed = Matrix.Translation(spot) @ rot.to_matrix().to_4x4() @ Matrix.Translation(-pivot)
        loc, q, _ = placed.decompose()
        prev = q.to_euler("XYZ", prev) if prev is not None else q.to_euler("XYZ")
        obj.location, obj.rotation_euler = loc, prev
        obj.keyframe_insert("location", frame=f)
        obj.keyframe_insert("rotation_euler", frame=f)


def rigid(obj, arm, bone, desired):
    """Hold a carried prop on the bone with a Child Of constraint, as placed at frame 1."""
    c = obj.constraints.new("CHILD_OF")
    c.target, c.subtarget = arm, bone
    c.use_scale_x = c.use_scale_y = c.use_scale_z = False
    target = arm.matrix_world @ arm.pose.bones[bone].matrix
    loc, rot, _ = target.decompose()
    target = Matrix.Translation(loc) @ rot.to_matrix().to_4x4()
    # world(armature) = target @ inverse.
    c.inverse_matrix = target.inverted() @ desired


def clip_span(action, part):
    """The frames of an action a part plays: its "range", or the whole action."""
    if "range" in part:
        return float(part["range"][0]), float(part["range"][1])
    return float(action.frame_range[0]), float(action.frame_range[1])


def pose_at(arm, action, frame):
    """Every pose bone's basis on an armature when it plays <action> at <frame>."""
    ad = reset(arm)
    assign(ad, action)
    scene.frame_set(int(frame))
    basis = {pb.name: (pb.location.copy(), pb.rotation_quaternion.copy(), pb.scale.copy()) for pb in arm.pose.bones}
    ad.action = None
    return basis


def subtree(arm, top):
    names, stack = set(), [arm.data.bones[top]]
    while stack:
        b = stack.pop()
        names.add(b.name)
        stack.extend(b.children)
    return names


def hold_bones(arm, basis, names):
    """Set the named bones of a baked action to one pose for its whole length."""
    for fc in fcurves(arm.animation_data.action):
        if not fc.data_path.startswith('pose.bones["'):
            continue
        bone = fc.data_path.split('"')[1]
        if bone not in names:
            continue
        loc, rot, scl = basis[bone]
        source = {"location": loc, "rotation_quaternion": rot, "scale": scl}.get(fc.data_path.rsplit(".", 1)[-1])
        if source is None:
            continue
        value = source[fc.array_index]
        for kp in fc.keyframe_points:
            kp.co.y = kp.handle_left.y = kp.handle_right.y = value
        fc.update()


def no_scale(m):
    loc, rot, _ = m.decompose()
    return Matrix.Translation(loc) @ rot.to_matrix().to_4x4()


def match(obj, prop, how):
    """Hold a prop on a carrier's bone exactly as the reference scene holds it at one frame (the
    launcher on the gunner's shoulder in the ult): a Child Of constraint on the bone keeps the
    relation read_tracks found there."""
    arm = members[how["carrier"]]
    bone = how.get("bone", "Wrist_R")
    m = how["match"]
    reset(obj)
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.location, obj.rotation_mode, obj.rotation_euler, obj.scale = (0, 0, 0), "XYZ", (0, 0, 0), (1, 1, 1)
    c = obj.constraints.new("CHILD_OF")
    c.target, c.subtarget = arm, bone
    c.use_scale_x = c.use_scale_y = c.use_scale_z = False
    c.inverse_matrix = tracks[("match", m["prop_ref"], m["carrier_ref"], bone, m["frame"])]


def follow(obj, how, offset, length):
    """Key a prop frame by frame where the reference scene has it (the launcher through the
    ult), moved with its carrier from her spot in that scene to her spot in the team (<offset>,
    a placement). "bones" names the prop's own bones that play the reference take too (the
    mortar's shell down the tube), and every other bone holds the prop's native idle at frame 1
    (the spare shells where the idle lays them)."""
    idle_basis = None
    if how.get("bones"):
        idle_basis = pose_at(obj, action_for(obj, spec["prop_idle"][obj.name]), 1)
    reset(obj)
    obj.matrix_parent_inverse = Matrix.Identity(4)
    # Euler keys, as every other prop placement bakes to: the review plays all the states on
    # one object, which reads only the rotation channels of its own rotation mode.
    obj.rotation_mode = "XYZ"
    placements = tracks[("follow", how["prop_ref"], tuple(how["range"]))]
    prev = None
    n = len(placements)
    for f in range(1, length + 1):
        i = f - 1
        if length == 2 * n - 1 and i >= n:
            # A ping-pong state plays the track back down as its crew play their clips backwards.
            i = 2 * (n - 1) - i
        placed = offset @ placements[min(i, n - 1)]
        loc, rot, _ = placed.decompose()
        prev = rot.to_euler("XYZ", prev) if prev is not None else rot.to_euler("XYZ")
        obj.location, obj.rotation_euler, obj.scale = loc, prev, (1, 1, 1)
        obj.keyframe_insert("location", frame=f)
        obj.keyframe_insert("rotation_euler", frame=f)
        obj.keyframe_insert("scale", frame=f)
        if idle_basis is not None:
            own = tracks[("follow_bones", how["prop_ref"], tuple(how["range"]))][min(i, n - 1)]
            for pb in obj.pose.bones:
                if pb.name in own:
                    pb.matrix_basis = own[pb.name]
                elif f == 1 and pb.name != "Prop":
                    pb.location, pb.rotation_quaternion, pb.scale = idle_basis[pb.name]
                else:
                    continue
                key_pose(pb, f)
                pb.keyframe_insert("scale", frame=f)


def carry_bones(obj, how, length):
    """Key single bones of a prop onto a crew member's bone frame by frame, held as the team's
    idle holds them ("bone_match": the mortar's loaded shell in the loader's hands while the
    mortar rides the gunner's back), then shrink the bones named in "hide" to nothing from the
    state frame given (the spare shells while the mortar is carried, the shell once it leaves
    the tube). "bone_sling" carries a single bone of a prop on a crew member's bone as `sling`
    carries a whole prop ("at" from "from_bone" in the team's frame at frame 1, "rot" from the
    prop's own axes, "pivot" in its own space): the mortar's baseplate on the loader's back
    while the barrel rides the gunner's. Runs after the prop is placed, so the prop's own
    placement is undone."""
    for bname, s in how.get("bone_sling", {}).items():
        arm = members.get(s.get("carrier"))
        pb = obj.pose.bones[bname]
        rest = obj.data.bones[bname].matrix_local
        if "turn" in s:
            # A constant turn about the bone's own head, in its rest axes (a bipod leg folded
            # against the other about its hinge), riding wherever its parent is carried.
            # "shift" (metres in the prop's rest frame) also moves it (the bipod's traverse head,
            # off its top beside the loader's head and down alongside the folded legs).
            q = Euler([math.radians(v) for v in s["turn"]], "XYZ").to_quaternion()
            loc = rest.to_3x3().inverted() @ Vector(s.get("shift", (0.0, 0.0, 0.0)))
            for f in range(1, length + 1):
                pb.location, pb.rotation_quaternion = loc, q
                key_pose(pb, f)
            continue
        if s.get("stay"):
            # Held at its rest on the prop while a parent bone is carried elsewhere (the barrel in
            # the gunner's hands, where the prop is placed, while its parent the bipod rides the
            # loader's back).
            for f in range(1, length + 1):
                scene.frame_set(f)
                pb.matrix = rest
                bpy.context.view_layer.update()
                key_pose(pb, f)
            continue
        scene.frame_set(1)
        ride = s.get("bone", spine_bone)
        if "as" in s:
            # Held as another bone is held in the team's idle ("as": the loaded shell in the
            # loader's hands): the part's axis "from_axis" through "from" (its own space at rest)
            # goes along the held bone's "to_axis" through "to", so the barrel's foot sits in the
            # hand under the shell's tail. The carrier rides her own "bone" in that relation.
            a = s["as"]
            rel = tracks[("bone_match", obj.name, a["bone"], a["holder"], a.get("wrist", "Wrist_R"), a.get("frame", 1))]
            onto = Vector(a["from_axis"]).normalized().rotation_difference(Vector(a["to_axis"]).normalized())
            line = Matrix.Translation(Vector(a["to"])) @ onto.to_matrix().to_4x4() @ Matrix.Translation(-Vector(a["from"]))
            held = rel @ obj.data.bones[a["bone"]].matrix_local.inverted() @ line
        elif s.get("local"):
            # "at" and "rot" in the ride bone's own frame, read off the carrier's rig (the gunner's
            # backpack, rigid on Chest_M, whose outer face the baseplate lies flat against), so the
            # part keeps its place on her whatever pose a state starts in.
            turn = Euler([math.radians(v) for v in s.get("rot", (0.0, 0.0, 0.0))], "XYZ").to_matrix().to_4x4()
            pivot = Vector(s.get("pivot", (0.0, 0.0, 0.0)))
            held = Matrix.Translation(Vector(s["at"]) - turn.to_3x3() @ pivot) @ turn
        else:
            origin = (arm.matrix_world @ arm.pose.bones[s.get("from_bone", "root")].matrix).translation
            turn = Euler([math.radians(v) for v in s.get("rot", (0.0, 0.0, 0.0))], "XYZ").to_matrix().to_4x4()
            if "along" in s:
                # "rot" stands the part upright, and the line from the first named bone's head
                # to the second's then tilts it with the carrier's back at frame 1 (the bipod
                # flat along the loader's spine however far her run leans her).
                low, high = (arm.matrix_world @ arm.pose.bones[n].head for n in s["along"])
                spine = (high - low).normalized()
                tilt = Vector((0.0, 0.0, 1.0)).rotation_difference(spine)
                turn = tilt.to_matrix().to_4x4() @ turn
            if "at_spine" in s:
                # "at" measured across, out from and up along her back (the gunner's backpack face,
                # which keeps its place on her back however far each state leans her).
                across = Vector((1.0, 0.0, 0.0))
                out = spine.cross(across).normalized()
                if out.y < 0:
                    out = -out
                a = s["at_spine"]
                offset = across * a[0] + out * a[1] + spine * a[2]
            else:
                offset = Vector(s["at"])
            pivot = Vector(s.get("pivot", (0.0, 0.0, 0.0)))
            desired = Matrix.Translation(origin + offset - turn @ pivot) @ turn
            held = no_scale(arm.matrix_world @ arm.pose.bones[ride].matrix).inverted() @ desired
        for f in range(1, length + 1):
            scene.frame_set(f)
            now = no_scale(arm.matrix_world @ arm.pose.bones[ride].matrix) @ held
            pb.matrix = obj.matrix_world.inverted() @ now @ rest
            bpy.context.view_layer.update()
            key_pose(pb, f)
    for bname, m in how.get("bone_match", {}).items():
        arm = members[m["carrier"]]
        rel = tracks[("bone_match", obj.name, bname, m["carrier"], m["bone"], m["frame"])]
        pb = obj.pose.bones[bname]
        for f in range(1, length + 1):
            scene.frame_set(f)
            wrist = no_scale(arm.matrix_world @ arm.pose.bones[m["bone"]].matrix)
            pb.matrix = obj.matrix_world.inverted() @ wrist @ rel
            bpy.context.view_layer.update()
            key_pose(pb, f)
    for bname, start in how.get("hide", {}).items():
        pb = obj.pose.bones[bname]
        for f in range(max(1, int(start) - 1), length + 1):
            scene.frame_set(f)
            # A bone at no scale makes its matrix singular for the visual bake, so a thousandth.
            pb.scale = (1.0, 1.0, 1.0) if f < int(start) else (0.001, 0.001, 0.001)
            pb.keyframe_insert("scale", frame=f)


def animates(action, bone):
    return any(fc.data_path.startswith(f'pose.bones["{bone}"]') for fc in fcurves(action))


def key_pose(pb, frame):
    pb.keyframe_insert("location", frame=frame)
    pb.keyframe_insert("rotation_quaternion", frame=frame)


def turn_about(pb, q, pivot):
    """Turn a pose bone (armature space) by <q> about <pivot>, and its children with it."""
    pb.matrix = Matrix.Translation(pivot) @ q.to_matrix().to_4x4() @ Matrix.Translation(-pivot) @ pb.matrix
    bpy.context.view_layer.update()


def is_donor(action):
    """A donor clip animates the donor's own cloth, not the coat (spec "coat"), so the thighs go
    through the coat unless swing_coat moves it."""
    return coat is not None and not animates(action, coat["chains"]["L"][0][0])


def swing_coat(arm, length):
    """Swing each coat chain about its root with its thigh, frame by frame, in the coat parent's
    frame: the thigh's swing from rest, a share of it by the chain's position. A flap the leg
    pushes into follows it closely, the one it swings away from only a little."""
    bones = arm.data.bones
    parent = coat["parent"]
    rest_s1 = bones[parent].matrix_local.to_3x3()
    forward = (rest_s1.inverted() @ Vector((0, 0, 1))).normalized()
    for f in range(1, length + 1):
        scene.frame_set(f)
        s1 = arm.pose.bones[parent].matrix.to_quaternion()
        to_s1 = s1.to_matrix().inverted()
        for side, chains in coat["chains"].items():
            hip, knee = arm.pose.bones[f"Hip_{side}"], arm.pose.bones[f"Knee_{side}"]
            now = (to_s1 @ (knee.head - hip.head)).normalized()
            rest = (rest_s1.inverted() @ (bones[f"Knee_{side}"].head_local - bones[f"Hip_{side}"].head_local)).normalized()
            swing = rest.rotation_difference(now)
            ahead = (now - rest).dot(forward) > 0
            for name, where in chains:
                pb = arm.pose.bones.get(name)
                if pb is None:
                    continue
                q_s1 = Quaternion().slerp(swing, coat["follow"][where][0 if ahead else 1])
                # The turn in Spine1's frame, carried to armature space.
                turn_about(pb, s1 @ q_s1 @ s1.inverted(), pb.head.copy())
                key_pose(pb, f)


def reach(arm, side, target, pole=None, hinge=False, limb=("Shoulder", "Elbow", "Wrist")):
    """Two-bone IK in armature space: bend the arm (Shoulder, Elbow) so the wrist reaches <target>,
    with the elbow towards <pole>, or on the side the clip already bends it to. The rig's bone
    tails do not point at their children, so Blender's own IK, which reaches with tails, does
    not fit it. Each bone takes the shortest turn, which leaves the upper arm's roll to chance and
    can fold the forearm against the elbow. <hinge> rolls the upper arm about itself first, so
    the forearm folds the way the rig's elbow folds at rest. <limb> names another chain of
    two (Hip, Knee, Ankle bends a leg to plant its foot)."""
    sh, el, wr = (arm.pose.bones[f"{n}_{side}"] for n in limb)
    s, e, w = sh.head.copy(), el.head.copy(), wr.head.copy()
    a, b = (e - s).length, (w - e).length
    d = target - s
    dist = min(max(d.length, abs(a - b) + 1e-4), a + b - 1e-4)
    axis = d.normalized()
    hint = (pole if pole is not None else e) - s
    bend = hint - axis * hint.dot(axis)
    if bend.length < 1e-5:
        bend = Vector((0, -1, 0)) - axis * axis.dot(Vector((0, -1, 0)))
    bend.normalize()
    x = (a * a - b * b + dist * dist) / (2 * dist)
    elbow = s + axis * x + bend * math.sqrt(max(a * a - x * x, 0.0))
    turn_about(sh, (e - s).rotation_difference(elbow - s), s)
    if hinge:
        bones = arm.data.bones
        rest = bones[sh.name].matrix_local.to_3x3().inverted()
        upper = rest @ (bones[el.name].head_local - bones[sh.name].head_local)
        fore = rest @ (bones[wr.name].head_local - bones[el.name].head_local)
        fold = fore - upper * (fore.dot(upper) / upper.length_squared)
        u = (elbow - s).normalized()
        now = sh.matrix.to_3x3().normalized() @ fold
        now -= u * now.dot(u)
        want = s + axis * dist - elbow
        want -= u * want.dot(u)
        if now.length > 1e-6 and want.length > 1e-6:
            sign = 1.0 if now.cross(want).dot(u) > 0 else -1.0
            turn_about(sh, Quaternion(u, sign * now.angle(want)), s)
    e, w = el.head.copy(), wr.head.copy()
    turn_about(el, (w - e).rotation_difference(s + axis * dist - e), e)


def frame_of(a, b):
    """An orthonormal turn whose x is <a> and whose y is <b> made square to it."""
    x = a.normalized()
    y = (b - x * x.dot(b)).normalized()
    return Matrix((x, y, x.cross(y))).transposed()


def rest_palm(arm, side):
    """The way the palm faces at rest: square to the knuckles and the thumb side, on the side the
    middle finger curls to."""
    bones = arm.data.bones
    w, mid, idx = (bones[f"{n}_{side}"].head_local for n in ("Wrist", "MiddleFinger1", "IndexFinger1"))
    fingers, across = (mid - w).normalized(), idx - mid
    normal = fingers.cross(across - fingers * fingers.dot(across)).normalized()
    tip = bones[f"MiddleFinger1_{side}"]
    while tip.children:
        tip = tip.children[0]
    curl = (tip.tail_local if tip.tail_local != tip.head_local else tip.head_local) - mid
    return normal if (curl - fingers * fingers.dot(curl)).dot(normal) >= 0 else -normal


def hand_turn(arm, side, fingers, thumb=None, palm=None):
    """The wrist's turn in armature space that points the knuckles along <fingers>, with the thumb
    side of the palm towards <thumb> or the palm facing <palm>, measured from the hand at rest
    (wrist to middle knuckle, middle knuckle to index knuckle)."""
    bones = arm.data.bones
    w, mid, idx = (bones[f"{n}_{side}"].head_local for n in ("Wrist", "MiddleFinger1", "IndexFinger1"))
    if palm is not None:
        turn = frame_of(fingers, palm) @ frame_of(mid - w, rest_palm(arm, side)).inverted()
    else:
        turn = frame_of(fingers, thumb) @ frame_of(mid - w, idx - mid).inverted()
    return (turn @ bones[f"Wrist_{side}"].matrix_local.to_3x3()).to_quaternion()


def forearm_twist(arm, side, turn):
    """How far a wrist turn <turn> (armature space) rolls the hand about the forearm against the
    elbow bone, in degrees from how the rig holds them at rest."""
    el, wr = arm.pose.bones[f"Elbow_{side}"], arm.pose.bones[f"Wrist_{side}"]
    bones = arm.data.bones
    rest = bones[el.name].matrix_local.to_3x3().inverted() @ bones[wr.name].matrix_local.to_3x3()
    frame = el.matrix.to_3x3().normalized()
    d = (rest.inverted() @ frame.inverted() @ turn.to_matrix()).to_quaternion()
    axis = (frame.inverted() @ (wr.head - el.head)).normalized()
    deg = math.degrees(2.0 * math.atan2(Vector((d.x, d.y, d.z)).dot(axis), d.w))
    return (deg + 180.0) % 360.0 - 180.0


def within_twist(arm, side, turn, limit):
    """Turn the hand about its own knuckle line, by as little as will do, until the forearm's
    twist is within <limit> degrees. A hand set to a grip's palm direction alone can ask for a
    forearm turned half round, which the skin shows as a wrung wrist."""
    bones = arm.data.bones
    w, mid = (bones[f"{n}_{side}"].head_local for n in ("Wrist", "MiddleFinger1"))
    knuckles = turn.to_matrix() @ (bones[f"Wrist_{side}"].matrix_local.to_3x3().inverted() @ (mid - w))
    best = None
    for step in sorted(range(-180, 181, 5), key=abs):
        q = Quaternion(knuckles.normalized(), math.radians(step)) @ turn
        t = abs(forearm_twist(arm, side, q))
        if t <= limit:
            return q
        if best is None or t < best[0]:
            best = (t, q)
    return best[1]


def hold(arm, prop_obj, grips, length):
    """Bring the carrier's hands onto a prop's grip points, frame by frame. A grip given as a
    whole placement (a Matrix) also turns the hand to it, and so does one with a hand direction."""
    to_arm = arm.matrix_world.inverted()
    miss = {side: 0.0 for side in grips}
    for f in range(1, length + 1):
        scene.frame_set(f)
        to_carrier = to_arm @ prop_obj.matrix_world
        for side, local in grips.items():
            turn, pole = None, None
            if isinstance(local, Matrix):
                placed = to_carrier @ local
                target, turn = placed.translation, placed.to_quaternion()
            elif isinstance(local, dict) and "whole" in local:
                placed = to_carrier @ local["whole"]
                target, turn, pole = placed.translation, placed.to_quaternion(), to_carrier @ local["elbow"]
                if "push" in local:
                    pole = pole + to_arm.to_3x3() @ local["push"]
            elif isinstance(local, dict):
                target = to_carrier @ local["at"]
                if "pole" in local:
                    pole = to_carrier @ local["pole"]
            else:
                target = to_carrier @ local
            reach(arm, side, target, pole, hinge=isinstance(local, dict) and local.get("hinge", False))
            if isinstance(local, dict) and "whole" not in local:
                rot = to_carrier.to_3x3()
                if isinstance(local["fingers"], str):
                    # "forearm": the knuckles carry on along the forearm and the hand only
                    # twists to bring the palm round, so the wrist does not bend.
                    pb = arm.pose.bones
                    fingers = pb[f"Wrist_{side}"].head - pb[f"Elbow_{side}"].head
                else:
                    fingers = rot @ local["fingers"]
                turn = hand_turn(arm, side, fingers,
                                 thumb=rot @ local["thumb"] if "thumb" in local else None,
                                 palm=rot @ local["palm"] if "palm" in local else None)
            miss[side] = max(miss[side], (arm.pose.bones[f"Wrist_{side}"].head - target).length)
            names = ["Shoulder", "Elbow"]
            if turn is not None and isinstance(local, dict) and "twist_limit" in local:
                turn = within_twist(arm, side, turn, local["twist_limit"])
                # The forearm's twist bone takes half the roll, so the skin turns along the
                # forearm rather than all at the wrist.
                el, part = arm.pose.bones[f"Elbow_{side}"], arm.pose.bones[f"ElbowPart1_{side}"]
                along = (arm.pose.bones[f"Wrist_{side}"].head - el.head).normalized()
                turn_about(part, Quaternion(along, math.radians(forearm_twist(arm, side, turn) / 2.0)), el.head)
                names.append("ElbowPart1")
            if turn is not None:
                wr = arm.pose.bones[f"Wrist_{side}"]
                wr.matrix = Matrix.LocRotScale(wr.head.copy(), turn, wr.matrix.to_scale())
                bpy.context.view_layer.update()
                names.append("Wrist")
            for n in names:
                key_pose(arm.pose.bones[f"{n}_{side}"], f)
    # How far a hand fell short of its grip at worst (out of the arm's reach).
    return miss


def lower_knees(arm, share, length):
    """Take back a share of each thigh's forward swing from rest, frame by frame, so the knees
    rise less. The donor's run lifts the thighs higher than the coat's skirt can follow."""
    bones = arm.data.bones
    forward = Vector((0, 0, 1))  # the rig faces +Z in armature space
    for f in range(1, length + 1):
        scene.frame_set(f)
        pelvis = arm.pose.bones["Root_M"].matrix.to_quaternion()
        rest_pelvis = bones["Root_M"].matrix_local.to_quaternion()
        for side in ("L", "R"):
            hip, knee = arm.pose.bones[f"Hip_{side}"], arm.pose.bones[f"Knee_{side}"]
            now = knee.head - hip.head
            rest = pelvis @ rest_pelvis.inverted() @ (bones[f"Knee_{side}"].head_local - bones[f"Hip_{side}"].head_local)
            if (now.normalized() - rest.normalized()).dot(pelvis @ rest_pelvis.inverted() @ forward) <= 0:
                continue
            swing = rest.rotation_difference(now)
            turn_about(hip, Quaternion().slerp(swing, share).inverted(), hip.head.copy())
            key_pose(hip, f)


def anchor_root(arm, turn):
    """Carry a member's whole take by a placement in the team's frame: every key of her root
    bone, location and rotation together."""
    act = arm.animation_data.action
    rest = arm.data.bones["root"].matrix_local
    world = arm.matrix_world.copy()
    into = rest.inverted() @ world.inverted() @ turn @ world @ rest
    locs = {fc.array_index: fc for fc in fcurves(act) if fc.data_path == 'pose.bones["root"].location'}
    rots = {fc.array_index: fc for fc in fcurves(act) if fc.data_path == 'pose.bones["root"].rotation_quaternion'}
    frames = sorted({kp.co.x for fc in list(locs.values()) + list(rots.values()) for kp in fc.keyframe_points})
    prev = None
    for f in frames:
        loc = Vector([locs[i].evaluate(f) if i in locs else 0.0 for i in range(3)])
        rot = Quaternion([rots[i].evaluate(f) if i in rots else (1.0 if i == 0 else 0.0) for i in range(4)])
        nloc, nrot, _ = (into @ Matrix.LocRotScale(loc, rot, None)).decompose()
        if prev is not None:
            nrot.make_compatible(prev)
        prev = nrot
        for curves, values in ((locs, nloc), (rots, nrot)):
            for i, fc in curves.items():
                kp = next(k for k in fc.keyframe_points if k.co.x == f)
                kp.co.y = kp.handle_left.y = kp.handle_right.y = values[i]
    for fc in list(locs.values()) + list(rots.values()):
        fc.update()


def torso_axes(arm, sway):
    """The body's up, forward and left in armature space, read off the "frame" bone (Chest_M:
    its X points up the spine, its Y forward and its Z to her left), so a swing is measured and
    made about her own side axis however far the run leans or turns her."""
    m = arm.pose.bones[sway.get("frame", "Chest_M")].matrix.to_3x3().normalized()
    return m.col[0].normalized(), m.col[1].normalized(), m.col[2].normalized()


def fore_angle(d, up, ahead):
    """How far a limb line points forward of straight down, in the body's own frame."""
    return math.atan2(d.dot(ahead), -d.dot(up))


def sway_angles(arm, sway, length):
    """The donor clip's swing of an upper arm, frame by frame: the angle, about her side axis,
    of the line from "bone" to "child" forward of straight down."""
    out = []
    for f in range(1, length + 1):
        scene.frame_set(f)
        up, ahead, _ = torso_axes(arm, sway)
        d = arm.pose.bones[sway["child"]].head - arm.pose.bones[sway["bone"]].head
        out.append(fore_angle(d, up, ahead))
    return out


def apply_sway(arm, sway, angles, length):
    """Turn an arm held in another pose (the spotter's idle arm, holding the flag) so it swings
    forward and back about her side axis as a running arm does: about "hang" degrees forward of
    straight down, by a share ("scale") of the donor's swing about its mean, in step with the
    run. Held as it is, an idle arm on a body the run leans forward points out behind her."""
    mean = sum(angles) / len(angles)
    spread = max(abs(a - mean) for a in angles) or 1.0
    pb = arm.pose.bones[sway["bone"]]
    child = arm.pose.bones[sway["child"]]
    hand = arm.pose.bones[sway["hand"]] if "hand" in sway else None
    for f, angle in enumerate(angles, start=1):
        scene.frame_set(f)
        up, ahead, side = torso_axes(arm, sway)
        held = fore_angle(child.head - pb.head, up, ahead)
        if "swing" in sway:
            # The donor's swing paced to an amplitude of its own ("swing", degrees either way):
            # Lind's run pumps her forearms more than her upper arms, which barely swing.
            want = math.radians(sway.get("hang", 10.0) + sway["swing"] * (angle - mean) / spread)
        else:
            want = math.radians(sway.get("hang", 10.0)) + sway.get("scale", 0.25) * (angle - mean)
        # A turn about her left by a positive angle swings a hanging arm back, so forward is -.
        turn_about(pb, Quaternion(side, -(want - held)), pb.head.copy())
        key_pose(pb, f)
        if hand is None:
            continue
        # A running arm is never locked straight: the elbow keeps a bend ("bend", degrees of
        # forearm forward of the upper arm) that deepens as the arm swings forward and opens as
        # it swings back ("bend_swing", degrees at the donor's furthest swing either way), as in
        # a run cycle's arm pump.
        fore = fore_angle(hand.head - child.head, up, ahead)
        bend = sway.get("bend", 20.0) + sway.get("bend_swing", 10.0) * (angle - mean) / spread
        turn_about(child, Quaternion(side, -(want + math.radians(bend) - fore)), child.head.copy())
        key_pose(child, f)


def open_elbows(arm, opening, length):
    """Open elbows held in another pose by a set angle each ("open", degrees per elbow bone,
    "Elbow_L": 15), about the axis square to the upper arm and forearm, so the hands move out
    from the body (the loader's idle hold of the shell, whose wrists and forearms go into her
    coat once the run leans her forward)."""
    for name, deg in opening.items():
        pb = arm.pose.bones[name]
        upper = pb.parent
        hand = pb.children[0] if pb.children else None
        for f in range(1, length + 1):
            scene.frame_set(f)
            a = pb.head - upper.head
            b = (hand.head if hand else pb.tail) - pb.head
            axis = a.cross(b)
            if axis.length < 1e-6:
                continue
            # Turning the forearm about a x b by a positive angle closes the elbow, so open by -deg.
            turn_about(pb, Quaternion(axis.normalized(), -math.radians(deg)), pb.head.copy())
            key_pose(pb, f)


def raise_arms(arm, raising, length):
    """Swing upper arms forward by a set angle each ("raise", degrees per shoulder bone) about
    her side axis (see torso_axes), so hands held low in front come up and out from the coat
    they hang into once the run leans the body forward."""
    for name, deg in raising.items():
        pb = arm.pose.bones[name]
        for f in range(1, length + 1):
            scene.frame_set(f)
            side = torso_axes(arm, {})[2]
            turn_about(pb, Quaternion(side, -math.radians(deg)), pb.head.copy())
            key_pose(pb, f)


def place(arm, mname, mode, in_place, part=None, anchor=None):
    """Put a member where she stands in the team, and return how far her take moved (a
    placement, which a prop following her takes too).

    "formation" (a donor clip, whose root starts at rest): her root takes her idle's location,
    plus the clip's own root travel unless the state is in place. Her idle's facing is left
    out: in the idle each member turns to the camera, while a borrowed state faces the team's
    way. "shift" (a take from another GFL2 scene, the ult): her root keeps the take's motion
    and facing, moved so that she starts on her formation spot. "anchor": the take is carried
    whole, turned and moved, by the placement that sets the state's anchor prop where the
    team's idle has it (the mortar, which stands on the ground and must not move between
    states), so the crew keep their places around it. "nudge" (metres, team frame) then moves a
    member who does not touch the anchor prop (the spotter) nearer the team. "native": left
    as it is."""
    if mode == "native":
        return Matrix.Identity(4)
    if mode == "anchor":
        turn = Matrix.Translation(Vector(tuple(part.get("nudge", (0.0, 0.0))) + (0.0,))) @ anchor
        anchor_root(arm, turn)
        return turn
    loc = places[mname][0]
    curves = [fc for fc in fcurves(arm.animation_data.action) if fc.data_path == 'pose.bones["root"].location']
    before = (arm.matrix_world @ arm.pose.bones["root"].matrix).translation.copy() if mode == "shift" else None
    for fc in curves:
        base = loc[fc.array_index]
        first = fc.keyframe_points[0].co.y
        for kp in fc.keyframe_points:
            if mode == "shift":
                value = kp.co.y + base - first
            else:
                value = base if in_place else base + kp.co.y
            shift = value - kp.co.y
            kp.co.y = value
            kp.handle_left.y += shift
            kp.handle_right.y += shift
        fc.update()
    if mode != "shift":
        return Matrix.Identity(4)
    scene.frame_set(1)
    return Matrix.Translation((arm.matrix_world @ arm.pose.bones["root"].matrix).translation - before)


baked = {}


def breathe(arm, member, how):
    """Lay the member's own idle over a held pose: each named bone turns from the held pose as
    it turns from the idle's first frame, by <scale>. A set-up crew holds its places and only
    breathes and looks about. Rotation only, so nobody shifts off her spot."""
    idle = action_for(arm, spec["formation_clip"][member])
    f0, f1 = idle.frame_range
    span = max(1, int(f1 - f0))
    scale = float(how.get("scale", 1.0))
    def curves(action, bone):
        found = [None] * 4
        for fc in fcurves(action):
            if fc.data_path == f'pose.bones["{bone}"].rotation_quaternion':
                found[fc.array_index] = fc
        return found if all(found) else None
    posed = arm.animation_data.action
    for bone in how["bones"]:
        own, still = curves(idle, bone), curves(posed, bone)
        if own is None or still is None:
            continue
        first = Quaternion([fc.evaluate(f0) for fc in own]).inverted()
        frames = sorted({kp.co.x for fc in still for kp in fc.keyframe_points})
        for frame in frames:
            at = f0 + (int(frame) - 1) % span
            turn = Quaternion().slerp(first @ Quaternion([fc.evaluate(at) for fc in own]), scale)
            q = Quaternion([fc.evaluate(frame) for fc in still]) @ turn
            for i, fc in enumerate(still):
                for kp in fc.keyframe_points:
                    if kp.co.x == frame:
                        kp.co.y = kp.handle_left.y = kp.handle_right.y = q[i]
        for fc in still:
            fc.update()
    print(f"breathe: {member} on her idle")


def build(state):
    name, loop = state["name"], state.get("loop", False)
    default_place = "native" if state.get("native") else "formation"
    length = 1
    uppers, donor, hair = {}, set(), {}
    # A held pose that is not a cycle (the aim hold, a stretch of the ult) loops as a ping-pong:
    # each clip plays its span forwards and then backwards, so the last frame is the first and
    # the loop has no jump. The span is the lead member's ("length_from").
    pingpong = None
    if state.get("pingpong"):
        lead = state["crew"][state["length_from"]]
        lf, ll = clip_span(action_for(members[state["length_from"]], lead["clip"]), lead)
        pingpong = int(ll - lf)
    for member, part in state["crew"].items():
        arm = members[member]
        # "of" plays another member's clip (the crew share one rig): the gunner stands as the
        # spotter does while she carries the barrel.
        action = action_for(members[part["of"]] if "of" in part else arm, part["clip"])
        if action is None:
            raise SystemExit(f"{name}: no clip {part['clip']} on {member}")
        if is_donor(action):
            donor.add(member)
        # "upper" may be a list, held in order, so a later hold wins on the bones both name (the
        # gunner's arms from Lind's idle, at the lengths Lind's clips give them in every other
        # state, then her fingers from the loader's).
        for up in (part["upper"] if isinstance(part.get("upper"), list) else [part["upper"]] if "upper" in part else []):
            # "of" borrows another member's clip (the crew share one rig): the gunner holds the
            # barrel with the loader's arms from the loader's idle.
            up_action = action_for(members[up["of"]] if "of" in up else arm, up["clip"])
            if up_action is None:
                raise SystemExit(f"{name}: no clip {up['clip']} on {member}")
            tops = up.get("from", "Spine1_M")
            names = set().union(*(subtree(arm, t) for t in ([tops] if isinstance(tops, str) else tops)))
            uppers.setdefault(member, []).append((pose_at(arm, up_action, up["frame"]), names))
        if state.get("idle_hair") and member in donor:
            # A donor clip leaves her hair in its bind pose, where the fringe cuts into her face.
            # Her own idle lifts it clear, so her hair chains hold its pose at frame 1. A member
            # on her own clip keeps its hair.
            idle = action_for(arm, spec["formation_clip"][member])
            chains = set().union(*(subtree(arm, b.name) for b in arm.data.bones["Head_M"].children if b.name.startswith("Hair_")))
            hair[member] = (pose_at(arm, idle, 1), chains)
        first, last = clip_span(action, part)
        if pingpong is not None:
            play_pingpong(arm, action, first, first + pingpong)
            continue
        clip_len = int(last - first)
        offset = int(part.get("offset", 0))
        start = 1 - offset if loop else 1 + offset
        strip = play(arm, action, start, loop)
        strip.action_frame_start, strip.action_frame_end = first, last
        strip.frame_end = strip.frame_start + (last - first) * strip.repeat
        length = max(length, clip_len + 1 if loop else clip_len + 1 + offset)
    if "length" in state:
        length = state["length"]
    if pingpong is not None:
        length = 2 * pingpong + 1
    elif "length_from" in state:
        # A state that is one member's action (the gunner's shot) lasts as long as her clip.
        part = state["crew"][state["length_from"]]
        first, last = clip_span(action_for(members[state["length_from"]], part["clip"]), part)
        length = int(last - first) + 1 + int(part.get("offset", 0))
    for prop, how in state["props"].items():
        obj = props[prop]
        reset(obj)
        if how == "idle":
            obj.matrix_parent_inverse, obj.matrix_basis = (m.copy() for m in prop_base[prop])
            idle = action_for(obj, spec["prop_idle"][prop])
            if pingpong is not None:
                # In step with its carrier's idle, which ping-pongs from its first frame too.
                play_pingpong(obj, idle, idle.frame_range[0], idle.frame_range[0] + pingpong)
            else:
                play(obj, idle, 1, True)
        else:
            reset(obj)
    scene.frame_start, scene.frame_end = 1, length
    motion.animation_data_clear()
    motion.location = (0, 0, 0)

    # The crew bake first and are put in place (formation, travel). The props bake last, so a
    # carried prop follows its carrier where she finally stands.
    bake(list(members.values()), length, {"POSE"})

    moved = {}
    anchor = None
    if "anchor" in state:
        a = state["anchor"]
        anchor = tracks[("anchor", a["prop"], a["ref"], a.get("frame", 1))]
    for mname, arm in members.items():
        part = state["crew"].get(mname, {})
        moved[mname] = place(arm, mname, part.get("place", default_place), state.get("in_place"), part, anchor)
        swing = sway_angles(arm, part["sway"], length) if "sway" in part else None
        if mname in uppers:
            for held in uppers[mname]:
                hold_bones(arm, *held)
        if swing is not None:
            apply_sway(arm, part["sway"], swing, length)
        if "raise" in part:
            raise_arms(arm, part["raise"], length)
        if "open" in part:
            open_elbows(arm, part["open"], length)
        if mname in hair:
            hold_bones(arm, *hair[mname])
        if "breathe" in part:
            breathe(arm, mname, part["breathe"])


    if "travel" in state:
        lead = members[state["travel"]]
        pts = []
        for f in range(1, length + 1):
            scene.frame_set(f)
            pts.append(ground((lead.matrix_world @ lead.pose.bones["Root_M"].matrix).translation))
        to_root = root.matrix_world.inverted().to_3x3()
        for f, p in enumerate(pts, start=1):
            delta = p - pts[0]
            key_location(motion, None, f, to_root @ delta)
            for mname, arm in members.items():
                bone = arm.data.bones["root"]
                back = bone.matrix_local.to_3x3().inverted() @ (arm.matrix_world.inverted().to_3x3() @ -delta)
                key_location(arm, "root", f, places[mname][0] + back)
        print(f"{name}: travel {pts[-1] - pts[0]} over {length} frames from {state['travel']}")

    for mname in sorted(donor):
        if "lower_knees" in state:
            lower_knees(members[mname], float(state["lower_knees"]), length)
        swing_coat(members[mname], length)
        print(f"{name}: coat follows the thighs on {mname}")

    for prop, how in state["props"].items():
        obj = props[prop]
        if how == "idle":
            continue
        if "follow" in how:
            follow(obj, how, moved[how["follow"]], length)
        elif "match" in how:
            match(obj, prop, how)
        elif "ground" in how:
            on_ground(obj, how)
        else:
            if "idle_pose" in how:
                basis = pose_at(obj, action_for(obj, spec["prop_idle"][prop]), 1)
                how["_idle_pose"] = {b: basis[b] for b in how["idle_pose"]}
                reset(obj)
            grips = sling(obj, prop, how)
            if grips:
                bpy.context.view_layer.update()
                miss = hold(members[how["carrier"]], obj, grips, length)
                print(f"{name}: {how['carrier']} holds {prop}, worst miss " + ", ".join(f"{k} {v:.3f}" for k, v in sorted(miss.items())))
        carry_bones(obj, how, length)
    # Props bake object and pose together: their armatures sit at scale 1 under the team root,
    # so the object bake is sound for them, and a held prop's placement varies by state.
    bake(list(props.values()), length, {"POSE", "OBJECT"})

    if spec.get("preview"):
        preview(name, length)
    keep(name, length)
    export(name, length)


def keep(name, length):
    """Hold on to a state's baked actions: the review file plays them, and a transition reads
    the poses its states meet it on."""
    held = {}
    for o in list(members.values()) + list(props.values()) + [motion]:
        act = o.animation_data.action if o.animation_data else None
        if act is not None:
            act = act.copy()
            act.name = f"{o.name}|{name}"
            act.use_fake_user = True
        held[o.name] = act
    baked[name] = (length, held)
    if spec.get("review"):
        review.append((name, length, held))


def export(name, length):
    # The exporter takes the channels a take leaves unkeyed from the pose the scene stands in, so
    # every state is exported from its last frame, with or without the preview renders (which
    # leave the scene there too).
    scene.frame_set(length)
    with_meshes = name == "Idle"
    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.data.objects:
        if o.type in ({"ARMATURE", "EMPTY", "MESH"} if with_meshes else {"ARMATURE", "EMPTY"}):
            o.select_set(True)
    path = f"{out_dir}/{team}_default.fbx" if with_meshes else f"{out_dir}/{team}_{name.lower()}.fbx"
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, add_leaf_bones=False, bake_anim=True,
                             bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
                             bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
                             # Unsimplified: a prop held still (the launcher in the deployed
                             # stance) has constant curves, which simplification drops, and the
                             # prop then falls back to its rest spot on the ground.
                             bake_anim_simplify_factor=0.0,
                             object_types={"ARMATURE", "MESH", "EMPTY"}, path_mode="STRIP", embed_textures=False)
    print(f"wrote {path}: {length} frames")


def ease(t, window):
    """How far through <window> (a start and an end, shares of the take) <t> is, eased in and
    out: 0 before it, 1 after it."""
    a, b = window
    if t <= a:
        return 0.0
    if t >= b:
        return 1.0
    s = (t - a) / (b - a)
    return s * s * (3.0 - 2.0 * s)


def mix(m0, m1, s):
    """A placement <s> of the way from <m0> to <m1>: location and scale in a line, turn by slerp.
    A bone shrunk to nothing (a hidden round) has no turn to slerp, so it keeps its own end's
    placement and grows or vanishes at the end of the way."""
    l0, r0, k0 = m0.decompose()
    l1, r1, k1 = m1.decompose()
    if min(k0) < 1e-4 or min(k1) < 1e-4:
        return m1.copy() if s >= 1.0 else m0.copy()
    r1.make_compatible(r0)
    return Matrix.LocRotScale(l0.lerp(l1, s), r0.slerp(r1, s), k0.lerp(k1, s))


def sample(name, end):
    """Every crew member's and prop's pose at a built state's last frame (<end>) or its first:
    bone bases, armature-space bone placements, world bone placements and the object's own."""
    length, held = baked[name]
    objs = list(members.values()) + list(props.values())
    for o in objs:
        assign(reset(o), held[o.name])
    scene.frame_set(length if end else 1)
    out = {}
    for o in objs:
        out[o.name] = {
            "basis": {pb.name: pb.matrix_basis.copy() for pb in o.pose.bones},
            "pose": {pb.name: pb.matrix.copy() for pb in o.pose.bones},
            "world": {pb.name: o.matrix_world @ pb.matrix for pb in o.pose.bones},
            "object": o.matrix_world.copy(),
        }
    for o in objs:
        o.animation_data.action = None
    return out


def basis_of(pb, pose, parent_pose):
    """The basis that puts a pose bone at <pose> (armature space) under its parent at
    <parent_pose>."""
    b = pb.bone
    if b.parent is None:
        return b.matrix_local.inverted() @ pose
    rest = b.parent.matrix_local.inverted() @ b.matrix_local
    return rest.inverted() @ parent_pose.inverted_safe() @ pose


def key_basis(pb, m, frame, hidden=False):
    """Key a pose bone's basis, shrunk to a thousandth if <hidden>: a bone at no scale makes its
    matrix singular for the visual bake."""
    loc, rot, scl = m.decompose()
    pb.location, pb.rotation_quaternion, pb.scale = loc, rot, Vector((0.001, 0.001, 0.001)) if hidden else scl
    pb.keyframe_insert("location", frame=frame)
    pb.keyframe_insert("rotation_quaternion", frame=frame)
    pb.keyframe_insert("scale", frame=frame)


def build_transition(state):
    """A take that carries the team from one state's last frame to the next state's first
    ("between"), where a cross-fade would slide the crew and fly the props. The way back plays it
    reversed. Both states, and any state a key pose names, are built first in the same run.

    A crew member goes through her "keys" in turn, each a pose at a share of the take ("at"):
    "from" (the first state's last frame), "to" (the second state's first) or [<state>, <frame>]
    (1, or "end" for its last). A key can turn her about her feet ("turn", degrees, positive
    anticlockwise seen from above), move her across the ground ("shift", metres in the team's
    frame, clear of a crewmate who stands where the pose would put her). Between keys her bones
    turn from the one pose to the other and her feet step the ground between, in as many steps
    as strides of "stride" metres cover it, the farther foot first and the two in turn, each
    lifted "lift" metres and planted between steps by leg IK. Her body goes as far between the
    keys as her two feet have on average, so it never runs ahead of a planted foot, and her hips
    sink "crouch" metres (0.05) at the middle of a stretch she steps, so legs straight at both
    keys have the reach to stay planted. "steps" sets how many steps each foot takes outright. A
    foot with less than "still" metres to go is not stepped, and a key's own "steps" counts the
    steps that bring her to it. "coat" turns her coat's chains with how far the IK moved her
    thighs off the blended pose (follow_thighs). "move": [<start>, <end>] is short for keys from
    the one end to the other.

    "walks" ([{"from", "to", "steps"}], each from a key to a key) walks her a planned path
    instead. Her shape blends from the one key's pose to the other's over the walk's last part
    (from "settle", 0.6, of the way), her hips go a straight line at a speed that rises over the
    first "accel" share and falls over the last (0.3), and she turns to face the way she goes
    over the first "turn_in" share and to the end key's facing over the "turn_out" share (0.35
    each) that ends at "turn_end" (1.0), so the turns overlap the walking ("face": false keeps
    her facing her own way, for back steps). A walk that ends in a kneel finishes turning before
    she goes down ("turn_end" before 1, "settle" after the turn), so the knees do not follow a
    turning body over feet already set for the kneel. "steps" footfalls land along that path in
    turn, half a step's share of her journey (hips' travel and turning, "turn" metres a radian,
    0.25) before the first and whole steps after, off the foot on the side she turns to ("lead"
    to choose), each beside where her hips will be in the middle of that foot's time alone on
    the ground, so she passes over it, but never past where her hips stop nor more than "stride"
    metres (0.42) ahead of the other foot, and turned as she will have turned then, the last two
    on the end key's feet, the very last a short closing step ("closing" share of its step, 0.6)
    that lands as she starts to settle, the first in the air for "first" of its step (0.8, less
    so her weight moves over the other foot before it lifts), and a swinging foot bows out
    "track" metres (0.04) to pass beside the planted one. Their timing varies by "vary" (0.1 of
    a step, the same each bake). A foot rolls up on its toes before it lifts, eases off the
    ground, swings "lift" metres up (0.06), comes down softly from toes pointing down to toes
    up, and rolls down off its heel ("roll", degrees: [25, 15]). Her pelvis turns with the
    stride (the forward foot's hip forward, up to 10 degrees) and her spine turns back, so her
    chest stays steady and her legs reach further. The last two footfalls land flat on the end
    key's feet. The standing leg stays straight: her hips sit at the height it reaches, rising
    over the standing foot and falling as her feet part, and only the swinging leg bends (not in
    the settle). A walking knee hinges over the way its foot points more than the way her hips
    face, and follows the pose's own knee only as she settles into the end pose (knee_aim). A
    planted foot pivots on its ball once her body has turned more than "pivot" degrees (20) from
    the way it was set down facing, so turns need no steps of their own. A foot leaves the
    ground at least 4 frames from any crewmate's walk lift (its swing takes a little more or
    less of the step), so the crew never step off together.

    "bend" turns her spine "degrees" forward from the waist over its "window" (the gunner
    bending to set the barrel down). "look" turns her neck and head towards where the second
    state has a prop's bone ("at", up to "limit" degrees, about the vertical only with "level").
    "grab" puts her hands on points of a placed prop ("hands": {<side>: [<bone>, <share>]}, a
    share of the way from the bone's head to the part's far end) over its "window", after the
    props are placed, so they follow the prop (the loader's hands on the bipod's legs as they
    open). A window of two shares rises and settles again, and of four rises, holds and falls. A
    grab's hands close round the parts (wrap_hand) with the elbows down and out, so the forearms
    come to the part from outside it.

    "twist" turns her spine "degrees" about the vertical ("side" 1 anticlockwise seen from
    above, -1 clockwise, or else whichever way is towards where the second state has a prop's
    bone ("toward"): over a "window" of two shares it rises and settles again, and of four it
    rises over the first two, holds, and turns back over the last two. "hands" keeps her hands
    on a prop's bone as the first state holds it ("prop", "bone") until "until" and lets go by
    "release": the prop moves by its own rule and her arms follow it. "fingers": "from" keeps
    the fingers of a holding hand closed as the first state has them, on a grab too. "sides" can
    give each hand its own [<until>, <release>] (the gunner's left hand letting go as her torso
    turns back). "slide" (a window) moves both grips along the part's long axis towards the
    stretch nearest her shoulders, so hands set where a carry held a barrel still reach it on
    its plate, and "wrap" (a window) closes each hand round the part there instead of keeping
    the carry's hand angle (wrap_hand), so the wrist stays in line with the forearm as the
    barrel turns.

    A prop's rule: with a "carrier" it rides one of her bones ("bone", Chest_M by default) and
    changes its hold on her over its "window", or with "lands" goes from where she holds it to
    where the second state has it. With no carrier it goes straight from the one placement to
    the other. "phases" runs rules in turn, each until its "until" (the last for the rest of the
    take): "place" "from" or "to" holds it at either end's placement, "hide" shrinks it away,
    and "clear" ({"of": <member>, "by": <metres>}) pushes a landing part out flat from her torso
    until its long axis keeps that far from her spine, faded in over "fade" and out as it lands.
    A "path" of [<share of the take>, <share of the way>] points stands in for the "window" and
    takes the prop along its way in stages (the barrel lowered and held, then set on its plate).
    "opens" goes from where the phase before would hold it to where the second state has it,
    over its own span (the bipod's legs swinging open as she sets it down), and a "carrier"
    holds it on her bone as it was when the phase began. With "upright" the part goes whole in
    its first state's shape, its own bone's head on her bone ("lift" metres up), turned as the
    second state stands it (the folded bipod upright in the loader's hand). "hide_between" shows
    it only on the first and last frames, each at its own end. "bones" gives a bone of the prop
    (and the bones under it) a rule of its own."""
    name, length = state["name"], int(state["length"])
    first, second = state["between"]
    for needed in (first, second):
        if needed not in baked:
            raise SystemExit(f"{name}: build {needed} before it in the same run")
    a, b = sample(first, True), sample(second, False)
    extra = {}
    for part in state.get("crew", {}).values():
        for key in part.get("keys", []):
            src = key.get("pose", "from")
            if isinstance(src, list) and tuple(src) not in extra:
                if src[0] not in baked:
                    raise SystemExit(f"{name}: build {src[0]} before it in the same run")
                extra[tuple(src)] = sample(src[0], src[1] == "end")
    for o in list(members.values()) + list(props.values()):
        ad = reset(o)
        ad.action = bpy.data.actions.new(f"{o.name}|{name}|bake")
    motion.animation_data_clear()
    motion.location = (0, 0, 0)
    scene.frame_start, scene.frame_end = 1, length
    span = max(1, length - 1)
    ends = {"from": a, "to": b}

    def rule_for(pname, bone):
        """A prop bone's rule, and the bone that owns it (itself or the nearest bone above it
        with a rule of its own)."""
        how = state.get("props", {}).get(pname, {})
        node = props[pname].data.bones[bone]
        while node is not None:
            if node.name in how.get("bones", {}):
                return how["bones"][node.name], node.name
            node = node.parent
        return how, None

    def carried(pname, bone, rule, t):
        """A prop bone's world placement under a rule that keeps no state."""
        w0, w1 = a[pname]["world"][bone], b[pname]["world"][bone]
        s = along(rule["path"], t) if "path" in rule else ease(t, rule.get("window", (0.0, 1.0)))
        if "carrier" not in rule:
            return mix(w0, w1, s)
        carrier = members[rule["carrier"]]
        cb = rule.get("bone", transition_carry_bone)
        c_now = carrier.matrix_world @ carrier.pose.bones[cb].matrix
        c0 = carrier.matrix_world @ a[carrier.name]["pose"][cb]
        c1 = carrier.matrix_world @ b[carrier.name]["pose"][cb]
        if rule.get("lands"):
            world = mix(c_now @ c0.inverted() @ w0, w1, s)
            if "clear" in rule:
                world = Matrix.Translation(clearance(pname, bone, world, rule["clear"], t, s)) @ world
            return world
        return c_now @ mix(c0.inverted() @ w0, c1.inverted() @ w1, s)

    axes = {}

    def clearance(pname, bone, world, clear, t, s):
        """How far to push a carried part out from a crew member's torso, flat across the ground:
        enough that its long axis keeps "by" metres from her spine (hips to neck), faded in over
        "fade" and out as the part reaches the second state's placement, so neither end moves.
        A barrel lowered across her chest otherwise leaves her hands nowhere to close on it but
        inside her body."""
        if (pname, bone) not in axes:
            axes[pname, bone] = part_axis(props[pname], bone)
        axis, lo, hi, base, radius = axes[pname, bone]
        who = members[clear["of"]]
        spine0 = who.matrix_world @ who.pose.bones["Spine1_M"].head
        spine1 = who.matrix_world @ who.pose.bones["Neck_M"].head
        a, b_ = world @ (base + axis * lo), world @ (base + axis * hi)
        best = None
        for i in range(21):
            x = a.lerp(b_, i / 20)
            d = spine1 - spine0
            on = spine0.lerp(spine1, max(0.0, min(1.0, (x - spine0).dot(d) / d.length_squared)))
            gap = x - on
            if best is None or gap.length < best.length:
                best = gap
        flat = Vector((best.x, best.y, 0.0))
        short = float(clear["by"]) - best.length
        if short <= 0.0 or flat.length < 1e-6:
            return Vector()
        weight = ease(t, clear.get("fade", (0.0, 0.1))) * (1.0 - s)
        return flat.normalized() * short * weight

    def up_of(arm):
        return (arm.matrix_world.inverted().to_3x3() @ Vector((0.0, 0.0, 1.0))).normalized()

    def plan_steps(f0, f1, metres, window, part):
        """Each step a slot of the window, overlapping its neighbours by "overlap", the feet
        taking the slots in turn, the farther foot first. "steps" sets how many steps each foot
        takes, or else there are as many as strides of "stride" metres cover. A foot's steps each
        cover an equal share of its way."""
        feet = {s: (f1[s].translation - f0[s].translation).length * metres for s in ("L", "R")}
        stepping = sorted((s for s in ("L", "R") if feet[s] > float(part.get("still", 0.03))),
                          key=lambda s: -feet[s])
        w0, w1 = window
        steps = {}
        if not stepping:
            return steps
        if "steps" in part:
            n = len(stepping) * max(1, int(part["steps"]))
        else:
            n = max(len(stepping), math.ceil(feet[stepping[0]] / float(part.get("stride", 0.5))))
            if len(stepping) == 2 and n % 2:
                n += 1
        d = (w1 - w0) / n
        # "overlap" (a share of a slot, a quarter by default) lets one foot lift before the other
        # lands. At 0 one leg moves at a time.
        over = d * float(part.get("overlap", 0.25))
        order = [stepping[i % len(stepping)] for i in range(n)]
        count = {s: order.count(s) for s in stepping}
        fracs = {s: [(j + 1) / count[s] for j in range(count[s])] for s in stepping}
        for s in stepping:
            slots = [i for i, o in enumerate(order) if o == s]
            prev = 0.0
            steps[s] = []
            for i, f in zip(slots, fracs[s]):
                steps[s].append(((max(w0, w0 + i * d - over), min(w1, w0 + (i + 1) * d + over)), prev, f))
                prev = f
        return steps

    def stride(hops, t):
        """How far along its way a stepping foot is at <t>, and how high it is lifted."""
        done, raised = 0.0, 0.0
        for win, f0, f1 in hops:
            if t >= win[0]:
                g = ease(t, win)
                done, raised = f0 + (f1 - f0) * g, math.sin(math.pi * g)
        return done, raised

    def key_bases(mname, arm, key):
        """A key's bone bases and its ankles' armature-space placements."""
        src = key.get("pose", "from")
        bases = (ends[src] if isinstance(src, str) else extra[tuple(src)])[mname]["basis"]
        for pb in arm.pose.bones:
            pb.matrix_basis = bases[pb.name].copy()
        bpy.context.view_layer.update()
        root_pb = arm.pose.bones["root"]
        if key.get("turn"):
            turn_about(root_pb, Quaternion(up_of(arm), math.radians(float(key["turn"]))), root_pb.head.copy())
        if key.get("shift"):
            move = arm.matrix_world.inverted().to_3x3() @ Vector((float(key["shift"][0]), float(key["shift"][1]), 0.0))
            root_pb.matrix = Matrix.Translation(move) @ root_pb.matrix
            bpy.context.view_layer.update()
        bpy.context.view_layer.update()
        return ({pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones},
                {s: arm.pose.bones[f"Ankle_{s}"].matrix.copy() for s in ("L", "R")})

    # The crew: each bone's basis from key to key, then the legs, the spine and the hands.
    team_lifts = []
    for mname, arm in members.items():
        part = state.get("crew", {}).get(mname, {})
        keys = part.get("keys") or [{"at": part.get("move", (0.0, 1.0))[0], "pose": "from"},
                                    {"at": part.get("move", (0.0, 1.0))[1], "pose": "to"}]
        poses = [(float(k["at"]),) + key_bases(mname, arm, k) for k in keys]
        metres = arm.matrix_world.to_scale().x
        segments = []
        for (t0, _, f0), (t1, _, f1), key in zip(poses, poses[1:], keys[1:]):
            if any(w["from"] <= t0 and t1 <= w["to"] for w in part.get("walks", [])):
                segments.append(((t0, t1), {}))
                continue
            # A key's own "steps" counts the steps that bring her to it.
            stretch = dict(part, steps=key["steps"]) if "steps" in key else part
            segments.append(((t0, t1), plan_steps(f0, f1, metres, (t0, t1), stretch)))
        # Lifts of key-to-key steps, so a crewmate's walk keeps clear of them too.
        my_lifts = [win[0] for _, steps in segments for hops in steps.values() for win, _, _ in hops]
        team_lifts.extend(my_lifts)
        up = up_of(arm)
        lift = float(part.get("lift", 0.08))
        crouch = float(part.get("crouch", 0.05)) / metres
        # A leg's reach held a little short of its full length, so the IK keeps its knee's way.
        rest = arm.data.bones
        leg = {sd: 0.998 * ((rest[f"Knee_{sd}"].head_local - rest[f"Hip_{sd}"].head_local).length
                            + (rest[f"Ankle_{sd}"].head_local - rest[f"Knee_{sd}"].head_local).length) for sd in ("L", "R")}

        # Walks: stretches where she walks a planned path of footsteps instead of stepping key
        # to key. The keys at a walk's ends give her pose there. Between them her shape blends
        # from the one to the other over the walk's last part ("settle"), her hips travel a
        # straight line at a speed that rises as she sets off and falls as she arrives, and she
        # turns smoothly to face the way she goes and, towards the end, the way the end key faces,
        # so the turns overlap the walking. Her feet land along that path in turn.
        walks = part.get("walks", [])

        def walk_at(t):
            return next((w for w in walks if w["from"] <= t <= w["to"]), None)

        def segment_at(u):
            i = 0
            while i < len(segments) - 1 and u > segments[i][0][1]:
                i += 1
            return i, ease(u, segments[i][0])

        def flat(v):
            return v - up * v.dot(up)

        def wrap_angle(v):
            return (v + math.pi) % (2.0 * math.pi) - math.pi

        walk_body, walk_feet, walk_info = {}, {}, {}
        for w in walks:
            w0, w1 = float(w["from"]), float(w["to"])
            ends_k = [next((j for j, k in enumerate(keys) if abs(k["at"] - edge) < 1e-6), None) for edge in (w0, w1)]
            if None in ends_k:
                raise SystemExit(f"{name}: {mname}'s walk {w0}-{w1} needs a key at each end")
            b0, b1 = poses[ends_k[0]][1], poses[ends_k[1]][1]
            for pb in arm.pose.bones:
                pb.matrix_basis = b1[pb.name].copy()
            bpy.context.view_layer.update()
            p1, f1_ = flat(arm.pose.bones["Root_M"].head), facing_of(arm, up)
            for pb in arm.pose.bones:
                pb.matrix_basis = b0[pb.name].copy()
            bpy.context.view_layer.update()
            p0, f0_ = flat(arm.pose.bones["Root_M"].head), facing_of(arm, up)
            start = {sd: arm.pose.bones[f"Ankle_{sd}"].matrix.copy() for sd in ("L", "R")}
            toes = {sd: start[sd].inverted() @ arm.pose.bones[f"Toes_{sd}"].head for sd in ("L", "R")}
            heel = {}
            for sd in ("L", "R"):
                # The heel's pivot: on the floor below the ankle, a little behind it.
                at = arm.matrix_world @ start[sd].translation
                floor = arm.matrix_world.inverted() @ Vector((at.x, at.y, 0.0))
                back = flat(start[sd].translation - start[sd] @ toes[sd])
                heel[sd] = start[sd].inverted() @ (floor + back.normalized() * 0.02 / metres)
            end = {sd: poses[ends_k[1]][2][sd].copy() for sd in ("L", "R")}
            e1, e2 = f0_, up.cross(f0_).normalized()

            def angle_of(v, e1=e1, e2=e2):
                return math.atan2(v.dot(e2), v.dot(e1))

            way = p1 - p0
            th0, th1 = angle_of(f0_), angle_of(f1_)
            faces_way = w.get("face", True) and way.length * metres > 0.05
            thw = angle_of(way) if faces_way else th0
            turn_in, turn_out = wrap_angle(thw - th0), wrap_angle(th1 - thw)
            accel, settle = float(w.get("accel", 0.3)), float(w.get("settle", 0.6))
            into, outof = float(w.get("turn_in", 0.35)), float(w.get("turn_out", 0.35))
            done = float(w.get("turn_end", 1.0))

            def heading(x, th0=th0, turn_in=turn_in, turn_out=turn_out, into=into, outof=outof, done=done, e1=e1, e2=e2):
                th = th0 + turn_in * ease(x, (0.0, into)) + turn_out * ease(x, (done - outof, done))
                return e1 * math.cos(th) + e2 * math.sin(th), th

            def path_at(t, w0=w0, w1=w1, p0=p0, way=way, accel=accel):
                x = (t - w0) / (w1 - w0)
                return p0 + way * glide(x, accel), heading(x)[0]

            settle_at = [settle]

            def body(t, w0=w0, w1=w1, b0=b0, b1=b1, settle_at=settle_at, path_at=path_at, heading=heading, angle_of=angle_of):
                """Her pose at <t> in the walk: her shape, then her hips put on the path and turned
                to its heading about themselves."""
                x = (t - w0) / (w1 - w0)
                shape = ease(x, (settle_at[0], 1.0))
                for pb in arm.pose.bones:
                    pb.matrix_basis = mix(b0[pb.name], b1[pb.name], shape)
                bpy.context.view_layer.update()
                hips_now = flat(arm.pose.bones["Root_M"].head)
                want, _ = path_at(t)
                turn = wrap_angle(heading(x)[1] - angle_of(facing_of(arm, up)))
                root_pb = arm.pose.bones["root"]
                root_pb.matrix = (Matrix.Translation(want - hips_now) @ Matrix.Translation(hips_now)
                                  @ Quaternion(up, turn).to_matrix().to_4x4() @ Matrix.Translation(-hips_now) @ root_pb.matrix)
                bpy.context.view_layer.update()

            walk_body[id(w)] = body
            left0 = e2
            # She steps off with the foot on the side she turns to.
            lead = w.get("lead") or ("L" if turn_in > 0 else "R")
            other = "R" if lead == "L" else "L"
            gap = flat(start["L"].translation - start["R"].translation)
            sign = {"L": 1.0 if gap.dot(left0) > 0 else -1.0}
            sign["R"] = -sign["L"]
            half = max(0.06 / metres, min(0.12 / metres, 0.5 * abs(gap.dot(left0))))
            n = max(2, int(w["steps"]))
            pre, post, swing, closing = 0.3, 0.2, 0.8, float(w.get("closing", 0.6))
            beat = (w1 - w0) / (n + post)
            seed = sum(map(ord, mname)) % 11
            # Footfalls fall at even shares of her journey, her hips' travel and her turning
            # ("turn" metres a radian, 0.25), not of the time: a turn made while she slows gets
            # steps of its own, so her feet keep up with her hips.
            # The walking steps fit before she settles. The closing step follows.
            t_stop = min(w1 - post * beat, w0 + (w1 - w0) * settle)
            ts_ = [w0 + (t_stop - w0) * j / 200 for j in range(201)]
            cost, before = [], None
            for t_ in ts_:
                here = path_at(t_)
                cost.append(0.0 if before is None else cost[-1] + (here[0] - before[0]).length * metres
                            + float(w.get("turn", 0.25)) * before[1].angle(here[1]))
                before = here

            def at_cost(c, ts_=ts_, cost=cost):
                for j in range(1, len(ts_)):
                    if cost[j] >= c:
                        gap_ = cost[j] - cost[j - 1]
                        return ts_[j - 1] + (ts_[j] - ts_[j - 1]) * ((c - cost[j - 1]) / gap_ if gap_ > 1e-12 else 1.0)
                return ts_[-1]

            vary = float(w.get("vary", 0.1))
            # Half a step's travel before the first footfall (from a standstill the body has
            # only half a step on the standing foot), whole steps after, the last but one at the
            # stop and the last as she settles (below). Landing later lets her body run so far past
            # the standing foot that the leg stretches out behind her.
            span_k = max(1.0, n - 1.5)
            taus = [at_cost(cost[-1] * min(1.0, max(0.0, (k + 0.5 + (vary * math.sin(k * 2.39996 + seed) if k < n - 2 else 0.0)) / span_k)))
                    for k in range(n)]
            # The closing step lands as she starts to settle, so she goes down into a kneel or
            # into her end stance with both feet already placed, not on one foot.
            taus[-1] = min(w1 - post * beat, t_stop + max(0.6 * beat, 0.12 * (w1 - w0)))
            for k in range(1, n):
                taus[k] = max(taus[k], taus[k - 1] + 0.3 * beat)
            lift_m = float(w.get("lift", 0.06)) / metres
            step_max = float(w.get("stride", 0.42)) / metres
            track_m = float(w.get("track", 0.04)) / metres
            toe_off, heel_on = (math.radians(v) for v in w.get("roll", (25.0, 15.0)))
            events = {"L": [], "R": []}
            last = dict(start)
            last_faces = {"L": th0, "R": th0}
            limit = math.radians(float(w.get("pivot", 20.0)))

            def spun(m, faces, t, sd, toes=toes, heading=heading, w0=w0, w1=w1, limit=limit):
                """A planted foot turned on its ball once her body has turned more than <limit> from
                the way the foot was set down facing: a body turning over a planted foot twists the
                leg, where a person pivots the foot in place."""
                d = wrap_angle(heading((t - w0) / (w1 - w0))[1] - faces)
                over = math.copysign(max(0.0, abs(d) - limit), d)
                if abs(over) < 1e-6:
                    return m
                at = m @ toes[sd]
                return Matrix.Translation(at) @ Quaternion(up, over).to_matrix().to_4x4() @ Matrix.Translation(-at) @ m
            for k in range(n):
                sd = lead if k % 2 == 0 else other
                # Her foot leaves the ground at least 4 frames from any crewmate's lift: the swing
                # takes a little more or less of the step (0.66 to 0.9 of it) so two never step off
                # together. The footfall stays put.
                since = taus[k] - (taus[k - 1] if k else w0)
                # The closing step (onto the end key's feet) is short and quick: a long swing there
                # leaves her balanced on one foot set for the end pose while she is still moving.
                # The first step can be quicker too ("first", the share of its time in the air,
                # as "swing"), so her weight is over the other foot before this one lifts.
                base = closing if k == n - 1 else (float(w.get("first", swing)) if k == 0 else swing)
                options = [taus[k] - sw * since for sw in (base, base - 0.06, base + 0.06, base - 0.12, base + 0.1, base - 0.14)]

                def clear_of(l):
                    return min((abs(l - other_lift) for other_lift in team_lifts), default=1.0)

                lam = next((l for l in options if clear_of(l) >= 4.0 / span), max(options, key=clear_of))
                team_lifts.append(lam)
                my_lifts.append(lam)
                if k >= n - 2:
                    land, rolls, faces = end[sd], False, th0 + turn_in + turn_out
                else:
                    # A foot lands beside where her hips will be half a step on, turned as far as
                    # she will have turned by then.
                    # Where her body will be in the middle of this foot's time alone on the ground
                    # (from the other foot's lift to its landing), so she passes over it.
                    nxt = taus[k + 1]
                    hm, fm = path_at(min(w1, nxt - 0.5 * swing * (nxt - taus[k])))
                    # Never past where her hips stop: a step into a stop that lands beyond it
                    # leaves her body behind both feet.
                    if way.length > 1e-6:
                        unit = way.normalized()
                        stop = way.length - 0.15 / metres
                        gone = (hm - p0).dot(unit)
                        if gone > stop:
                            hm = hm - unit * (gone - stop)
                    left = up.cross(fm).normalized()
                    place = hm + up * start[sd].translation.dot(up) + left * sign[sd] * half
                    # No further than "stride" metres (0.42) ahead of the other foot: a longer step
                    # (a first step from a standstill, most often) spreads her feet so far apart
                    # that her hips must drop to reach both.
                    planted_other = last[other if sd == lead else lead].translation
                    ahead_of = (place - planted_other).dot(fm)
                    if ahead_of > step_max:
                        place = place - fm * (ahead_of - step_max)
                    turn = f0_.rotation_difference(fm)
                    land, rolls = Matrix.LocRotScale(place, turn @ start[sd].to_quaternion(), start[sd].to_scale()), True
                    faces = heading((min(w1, nxt - 0.5 * swing * (nxt - taus[k])) - w0) / (w1 - w0))[1]
                events[sd].append((lam, taus[k], last[sd], land, rolls, last_faces[sd], faces))
                last[sd], last_faces[sd] = land, faces

            def rolled(m, r, pivot, sd, toes=toes, heel=heel):
                if abs(r) < 1e-6:
                    return m
                back = flat(m.translation - m @ toes[sd])
                axis = back.normalized().cross(up).normalized()
                at = {"toe": m @ toes[sd], "heel": m @ heel[sd]}.get(pivot, m.translation)
                return Matrix.Translation(at) @ Quaternion(axis, r).to_matrix().to_4x4() @ Matrix.Translation(-at) @ m

            def foot(sd, t, events=events, rolled=rolled, beat=beat, start=start, lift_m=lift_m,
                     toe_off=toe_off, heel_on=heel_on, pre=pre, post=post, track_m=track_m, sign=sign, path_at=path_at,
                     spun=spun, start_t=w0):
                """Where a foot is at <t>: planted, rolling up on its toes before it lifts, swinging
                (eased off and down, toes from pointing down to up), or rolling down off its
                heel after it lands. + raises the heel, - the toes."""
                prev_tau, prev_rolls, here, here_faces = None, False, start[sd], events[sd][0][5] if events[sd] else 0.0
                for lam, tau, m0, m1, rolls, faces0, faces1 in events[sd]:
                    if t < lam:
                        m0 = spun(m0, faces0, t, sd)
                        r, pivot = 0.0, "toe"
                        if prev_rolls and t < prev_tau + post * beat:
                            r, pivot = -heel_on * (1.0 - ease(t, (prev_tau, prev_tau + post * beat))), "heel"
                        rise_from = max(lam - pre * beat, start_t)
                        if t > rise_from:
                            r, pivot = toe_off * ease(t, (rise_from, lam)), "toe"
                        return rolled(m0, r, pivot, sd)
                    if t <= tau:
                        # It lifts turned as far as it had pivoted, and lands turned to agree with
                        # her body then, pivoting on round to its own way as she finishes turning.
                        m0 = spun(m0, faces0, lam, sd)
                        m1 = spun(m1, faces1, tau, sd)
                        x = (t - lam) / (tau - lam)
                        # Eased off the ground and down again: the toe-off roll before it gives the
                        # lift its briskness, and a foot leaving at full speed jerks the leg.
                        go = x * x * (3.0 - 2.0 * x)
                        place = m0.translation.lerp(m1.translation, go) + up * lift_m * math.sin(math.pi * x ** 0.7)
                        # Out to her own side mid-swing ("track", 0.04), so the swinging foot
                        # passes beside the planted one.
                        place += up.cross(path_at(t)[1]).normalized() * sign[sd] * track_m * math.sin(math.pi * x)
                        q0, q1 = m0.to_quaternion(), m1.to_quaternion()
                        q1.make_compatible(q0)
                        smooth = x * x * (3.0 - 2.0 * x)
                        r = toe_off + ((-heel_on if rolls else 0.0) - toe_off) * smooth
                        # In the air the foot rolls about its toe while the toes point down and about
                        # its heel once they point up, the pivots it lifts and lands on, so the ankle
                        # does not jump at lift-off or landing.
                        return rolled(Matrix.LocRotScale(place, q0.slerp(q1, smooth), m0.to_scale()), r, "toe" if r > 0 else "heel", sd)
                    prev_tau, prev_rolls, here, here_faces = tau, rolls, m1, faces1
                r = -heel_on * (1.0 - ease(t, (prev_tau, prev_tau + post * beat))) if prev_rolls else 0.0
                return rolled(spun(here, here_faces, t, sd), r, "heel", sd)

            def swinging(sd, t, events=events):
                """How grounded a foot is: 1 planted, easing to 0 over the first part of a swing
                and back to 1 between 35% and 85% of it."""
                for lam, tau, *_ in events[sd]:
                    if lam <= t <= tau:
                        x = (t - lam) / (tau - lam)
                        # Counted fully again well before it lands, so the hips are low enough for
                        # the leg to reach the ground: counted late, the foot hangs short and snaps
                        # down as it lands.
                        return max(1.0 - ease(x, (0.0, 0.3)), ease(x, (0.35, 0.85)))
                return 1.0

            # She settles once the closing step is down, not on one foot.
            settle_at[0] = min(0.95, max(settle, (taus[-1] - w0) / (w1 - w0)))

            def shape_at(t, w0=w0, w1=w1, settle_at=settle_at):
                return ease((t - w0) / (w1 - w0), (settle_at[0], 1.0))

            walk_feet[id(w)] = foot
            walk_info[id(w)] = (foot, swinging, shape_at, dict(sign))
            print(f"{name}: {mname} walks {w0:.2f}-{w1:.2f}, {n} steps off the {lead} foot, turning "
                  f"{math.degrees(turn_in):+.0f} then {math.degrees(turn_out):+.0f} degrees, lands at "
                  + ", ".join(f"{1 + t_ * span:.0f}" for t_ in taus))

        twist, hands = part.get("twist"), part.get("hands")
        bend, look = part.get("bend"), part.get("look")
        twist_sign = None
        last_rise = None
        knees = {}
        palms = {}
        coat_turn = 0.0
        slide_axis = part_axis(props[hands["prop"]], hands["bone"]) if hands and ("slide" in hands or "wrap" in hands) else None
        grips = {}
        if hands:
            w0 = a[hands["prop"]]["world"][hands["bone"]]
            sides = hands.get("sides", {"L": None, "R": None})
            for side in sides:
                span_ = sides[side] if isinstance(sides, dict) and sides[side] else (hands["until"], hands["release"])
                grips[side] = (w0.inverted() @ arm.matrix_world @ a[mname]["pose"][f"Wrist_{side}"], span_)
        for f in range(1, length + 1):
            t = (f - 1) / span
            walking = walk_at(t)
            i, s = segment_at(t)
            (t0, t1), steps = segments[i]
            gait = {side: stride(steps[side], t) if side in steps else (s, 0.0) for side in ("L", "R")}
            # The body goes as far as its feet have, on average: a body gliding on at a steady rate
            # runs ahead of a planted foot until the leg cannot reach it and drags it along.
            if steps:
                s = (gait["L"][0] + gait["R"][0]) / 2
            for pb in arm.pose.bones:
                pb.matrix_basis = mix(poses[i][1][pb.name], poses[i + 1][1][pb.name], s)
            bpy.context.view_layer.update()
            if walking is not None:
                walk_body[id(walking)](t)
            else:
                last_rise = None
            thighs = thigh_lines(arm) if part.get("coat") else None
            if steps and t0 < t < t1:
                # Knees a little bent while she steps: poses that stand on straight legs leave no
                # reach for a planted foot as the hips pass over it.
                sink = crouch * math.sin(math.pi * (t - t0) / (t1 - t0))
                hips = arm.pose.bones["Root_M"]
                hips.matrix = Matrix.Translation(-up * sink) @ hips.matrix
                bpy.context.view_layer.update()
            if walking is not None:
                # The standing leg straight: her hips sit at the height that leg reaches (a little
                # short of full length), so they rise over the standing foot and fall as her feet
                # part, and only the swinging leg bends. Faded in from the walk's start and out at
                # its end, and kept out of a settle into a crouch or kneel, which bends the legs
                # on purpose. A leg that cannot reach always brings the hips down.
                foot_at, swinging, shape_at, side_sign = walk_info[id(walking)]
                x = (t - walking["from"]) / (walking["to"] - walking["from"])
                fade = min(1.0, 6.0 * math.sin(math.pi * x))
                # The pelvis turns with the stride, the forward foot's hip forward (up to 10
                # degrees), and the spine turns back so her chest stays steady: the legs reach
                # further, so the hips need not drop so far between her feet.
                apart = (foot_at("L", t).translation - foot_at("R", t).translation).dot(facing_of(arm, up))
                yaw = -side_sign["L"] * max(-1.0, min(1.0, apart * metres / 0.5)) * math.radians(10.0) * fade * (1.0 - shape_at(t))
                if abs(yaw) > 1e-5:
                    hips = arm.pose.bones["Root_M"]
                    turn_about(hips, Quaternion(up, yaw), hips.head.copy())
                    spine = arm.pose.bones["Spine1_M"]
                    turn_about(spine, Quaternion(up, -yaw), spine.head.copy())
                # Each foot counts as far as it is grounded: easing out early in its swing and back
                # in before it lands, so the height never jumps as a foot lifts or lands.
                room = min(math.sqrt(max(leg[sd] ** 2 - flat(arm.pose.bones[f"Hip_{sd}"].head - foot_at(sd, t).translation).length ** 2, 0.0))
                           - (arm.pose.bones[f"Hip_{sd}"].head - foot_at(sd, t).translation).dot(up)
                           + (1.0 - swinging(sd, t)) * 0.15 / metres for sd in ("L", "R"))
                rise = room * fade * (1.0 - shape_at(t)) if room > 0.0 else room * fade
                # The hips rise at most 0.8 cm a frame and fall at most 1.5, so a foot taking or
                # giving its weight does not throw them up or drop them.
                if last_rise is not None:
                    rise = max(last_rise - 0.015 / metres, min(rise, last_rise + 0.008 / metres))
                last_rise = rise
                hips = arm.pose.bones["Root_M"]
                hips.matrix = Matrix.Translation(up * rise) @ hips.matrix
                bpy.context.view_layer.update()
            for side in ("L", "R"):
                if walking is not None:
                    placed = walk_feet[id(walking)](side, t)
                    rest_b = arm.data.bones
                    toes_way = placed.to_3x3() @ (rest_b[f"Ankle_{side}"].matrix_local.to_3x3().inverted()
                                                  @ (rest_b[f"Toes_{side}"].head_local - rest_b[f"Ankle_{side}"].head_local))
                    toes_way -= up * toes_way.dot(up)
                    reach(arm, side, placed.translation, limb=("Hip", "Knee", "Ankle"),
                          pole=knee_aim(arm, side, placed.translation, metres, knees.setdefault(side, {}), toes_way,
                                        walk_info[id(walking)][2](t)))
                    ankle = arm.pose.bones[f"Ankle_{side}"]
                    ankle.matrix = Matrix.LocRotScale(ankle.matrix.translation, placed.to_quaternion(), ankle.matrix.to_scale())
                    bpy.context.view_layer.update()
                    continue
                p0, p1 = poses[i][2][side], poses[i + 1][2][side]
                g, raised = gait[side]
                target = p0.translation.lerp(p1.translation, g) + up * lift * raised
                pole = knee_aim(arm, side, target, metres, knees.setdefault(side, {}))
                reach(arm, side, target, pole=pole, limb=("Hip", "Knee", "Ankle"))
                ankle = arm.pose.bones[f"Ankle_{side}"]
                ankle.matrix = Matrix.LocRotScale(ankle.matrix.translation, mix(p0, p1, g).to_quaternion(), ankle.matrix.to_scale())
                bpy.context.view_layer.update()
            if thighs is not None:
                coat_turn = max(coat_turn, follow_thighs(arm, thighs))
            if twist:
                win = twist["window"]
                if len(win) == 4:
                    k = ease(t, win[:2]) * (1.0 - ease(t, win[2:]))
                else:
                    k = 0.0 if t <= win[0] or t >= win[1] else math.sin(math.pi * (t - win[0]) / (win[1] - win[0]))
                if k > 0.0:
                    # The side is chosen once, when the turn begins: chosen afresh each frame, it
                    # flips when the prop comes straight ahead and the torso snaps across.
                    if twist_sign is None and "side" in twist:
                        twist_sign = 1.0 if float(twist["side"]) > 0 else -1.0
                    if twist_sign is None:
                        hips = arm.pose.bones["Root_M"]
                        ahead = arm.matrix_world.to_3x3() @ (hips.matrix.to_3x3() @ arm.data.bones["Root_M"].matrix_local.to_3x3().inverted() @ Vector((0, 0, 1)))
                        goal = b[twist["toward"][0]]["world"][twist["toward"][1]].translation - arm.matrix_world @ hips.head
                        twist_sign = 1.0 if ahead.x * goal.y - ahead.y * goal.x > 0 else -1.0
                    sign = twist_sign
                    spine = ("Spine1_M", "Spine2_M", "Chest_M")
                    q = Quaternion(up, sign * math.radians(float(twist["degrees"])) * k / len(spine))
                    for bone in spine:
                        turn_about(arm.pose.bones[bone], q, arm.pose.bones[bone].head.copy())
            if bend:
                k = window_weight(bend["window"], t)
                if k > 0.0:
                    # Forward from the waist: the spine turns about her side axis, so her chest
                    # and arms go down and ahead (the gunner bending to set the barrel down).
                    hips = arm.pose.bones["Root_M"]
                    ahead = (hips.matrix.to_3x3() @ arm.data.bones["Root_M"].matrix_local.to_3x3().inverted() @ Vector((0, 0, 1))).normalized()
                    spine = ("Spine1_M", "Spine2_M", "Chest_M")
                    q = Quaternion(up.cross(ahead).normalized(), math.radians(float(bend["degrees"])) * k / len(spine))
                    for bone in spine:
                        turn_about(arm.pose.bones[bone], q, arm.pose.bones[bone].head.copy())
            if look:
                k = window_weight(look["window"], t)
                if k > 0.0:
                    # Neck and head turn her face towards where the second state has a prop's
                    # bone, the neck taking half the turn.
                    goal = arm.matrix_world.inverted() @ b[look["at"][0]]["world"][look["at"][1]].translation
                    rest_head = arm.data.bones["Head_M"].matrix_local.to_3x3().inverted()
                    for bone, share in (("Neck_M", 0.5), ("Head_M", 1.0)):
                        head = arm.pose.bones["Head_M"]
                        face = (head.matrix.to_3x3() @ rest_head @ Vector((0, 0, 1))).normalized()
                        want = (goal - head.head).normalized()
                        if look.get("level"):
                            # Only the way she faces turns, about the vertical, so she looks
                            # towards the prop without dropping her head to it.
                            face = (face - up * face.dot(up)).normalized()
                            want = (want - up * want.dot(up)).normalized()
                        turn = face.rotation_difference(want)
                        limit = math.radians(float(look.get("limit", 70)))
                        if turn.angle > limit:
                            turn = Quaternion().slerp(turn, limit / turn.angle)
                        pb = arm.pose.bones[bone]
                        turn_about(pb, Quaternion().slerp(turn, k * share), pb.head.copy())
            if hands:
                pw = carried(hands["prop"], hands["bone"], rule_for(hands["prop"], hands["bone"])[0], t)
                shift = Vector()
                if slide_axis is not None:
                    # The grips slide along the part (a barrel's tube) towards the stretch nearest
                    # her shoulders, the same distance apart: a grip fixed where the carry held it
                    # goes out of reach once the barrel stands up on its plate.
                    axis, lo, hi = slide_axis[:3]
                    shoulders = arm.matrix_world @ ((arm.pose.bones["Shoulder_L"].head + arm.pose.bones["Shoulder_R"].head) / 2)
                    margin = 0.1 * (hi - lo)
                    near = min(max((pw.inverted() @ shoulders).dot(axis), lo + margin), hi - margin)
                    middle = sum(g.translation.dot(axis) for g, _ in grips.values()) / len(grips)
                    shift = axis * (near - middle) * ease(t, hands["slide"])
                for side, (grip, (until, release)) in grips.items():
                    hold = 1.0 - ease(t, (until, release))
                    if hold > 0.0:
                        goal = arm.matrix_world.inverted() @ pw @ Matrix.Translation(shift) @ grip
                        wrist = arm.pose.bones[f"Wrist_{side}"]
                        now = wrist.matrix.copy()
                        if "wrap" in hands:
                            # From the carry's grip into a hand closed round the part where that
                            # grip sits along it, so the wrist does not bend as the barrel turns.
                            axis, lo, hi, base, radius = slide_axis
                            to_arm = arm.matrix_world.inverted() @ pw
                            u = (Matrix.Translation(shift) @ grip).translation.dot(axis)
                            rod = (to_arm @ (base + axis * u), (to_arm.to_3x3() @ axis).normalized())
                            closed = ease(t, hands["wrap"])
                            reach(arm, side, now.translation.lerp(goal.translation, hold))
                            bpy.context.view_layer.update()
                            # The elbow swings from where the pose has it to the grip's aim with
                            # the grip, so the take starts and ends on the state's own arm.
                            held_at, aim = wrap_hand(arm, side, rod[0], rod[1], radius, memo=palms.setdefault(side, {}))
                            goal = Matrix.LocRotScale(goal.translation.lerp(held_at.translation, closed),
                                                      goal.to_quaternion().slerp(held_at.to_quaternion(), closed), goal.to_scale())
                        own = wrist.rotation_quaternion.copy()
                        target = now.translation.lerp(goal.translation, hold)
                        pole = swing_pole(arm, side, target, aim, closed * hold, palms.setdefault(side, {})) if "wrap" in hands else None
                        reach(arm, side, target, pole=pole)
                        settle_hand(wrist, own, goal.to_quaternion(), hold)
                        if hands.get("fingers") == "from":
                            close_fingers(arm, side, a[mname]["basis"], hold)
            for pb in arm.pose.bones:
                key_basis(pb, pb.matrix_basis.copy(), f)
        if my_lifts:
            print(f"{name}: {mname} lifts a foot at " + ", ".join(f"{1 + l * span:.0f}" for l in sorted(my_lifts)))
        if part.get("coat"):
            print(f"{name}: {mname} coat follows her legs, turned up to {math.degrees(coat_turn):.0f} degrees")
        if twist_sign is not None:
            print(f"{name}: {mname} twists {'anticlockwise' if twist_sign > 0 else 'clockwise'} seen from above")
        print(f"{name}: {mname} through {len(keys)} keys, " + "; ".join(
            f"{t0:.2f}-{t1:.2f} " + (", ".join(f"{len(v)} steps {k}" for k, v in st.items()) or "no steps")
            for (t0, t1), st in segments) + (", twists" if twist else "") + (", hands on " + hands["bone"] if hands else ""))

    # The props, by world placement bone by bone, so each part of a prop can go its own way.
    for pname, obj in props.items():
        how = state.get("props", {}).get(pname, {})
        saved_inverse = obj.matrix_parent_inverse.copy()
        obj.matrix_parent_inverse = Matrix.Identity(4)
        rules = {pb.name: rule_for(pname, pb.name) for pb in obj.pose.bones}
        order = sorted(obj.pose.bones, key=lambda pb: len(pb.bone.parent_recursive))
        rot_path = "rotation_quaternion" if obj.rotation_mode == "QUATERNION" else "rotation_euler"
        last = {pb.name: a[pname]["world"][pb.name] for pb in obj.pose.bones}
        phase = {}
        for f in range(1, length + 1):
            t = (f - 1) / span
            scene.frame_set(f)
            whole = mix(a[pname]["object"], b[pname]["object"], ease(t, how.get("window", (0.0, 1.0))))
            obj.matrix_world = whole
            obj.keyframe_insert("location", frame=f)
            obj.keyframe_insert(rot_path, frame=f)
            obj.keyframe_insert("scale", frame=f)
            poses = {}
            for pb in order:
                rule, owner = rules[pb.name]
                w0, w1 = a[pname]["world"][pb.name], b[pname]["world"][pb.name]
                hidden = False
                if rule.get("hide_between"):
                    world = w1 if f == length else w0
                    hidden = 1 < f < length
                elif "phases" in rule:
                    phases = rule["phases"]
                    k = next((j for j, ph in enumerate(phases) if t <= ph.get("until", 2.0)), len(phases) - 1)

                    def phase_world(k):
                        ph = phases[k]
                        if ph.get("place") == "to":
                            return w1, False
                        if ph.get("place") == "from" or ph.get("hide"):
                            return w0, bool(ph.get("hide"))
                        if ph.get("opens") and k > 0:
                            # From where the phase before would hold it to where the second state
                            # has it, over this phase: the bipod's legs swing open as it is set down.
                            before, _ = phase_world(k - 1)
                            return mix(before, w1, ease(t, (phases[k - 1].get("until", 0.0), ph.get("until", 1.0)))), False
                        carrier = members[ph["carrier"]]
                        c_now = carrier.matrix_world @ carrier.pose.bones[ph.get("bone", transition_carry_bone)].matrix
                        if ph.get("upright"):
                            # The part goes whole, in its first state's shape, its owning bone's
                            # head on the carrier's bone (and "lift" metres up), turned as the
                            # second state stands it: the folded bipod upright in the loader's hand.
                            own0, own1 = a[pname]["world"][owner], b[pname]["world"][owner]
                            at = Matrix.LocRotScale(c_now.translation + Vector((0.0, 0.0, float(ph.get("lift", 0.0)))),
                                                    own1.to_quaternion(), own0.to_scale())
                            return at @ own0.inverted() @ w0, False
                        if (pb.name, k) not in phase:
                            phase[pb.name, k] = c_now.inverted() @ last[pb.name]
                        return c_now @ phase[pb.name, k], False

                    world, hidden = phase_world(k)
                else:
                    world = carried(pname, pb.name, rule, t)
                last[pb.name] = world
                poses[pb.name] = whole.inverted_safe() @ world
                parent = pb.bone.parent
                key_basis(pb, basis_of(pb, poses[pb.name], poses[parent.name] if parent else None), f, hidden)
        obj.matrix_parent_inverse = saved_inverse
        print(f"{name}: {pname} placed bone by bone, own rules for {sorted(how.get('bones', {}))}")

    # Hands that take hold of a prop once it is placed: a pass over the baked props, which bends
    # each arm to its point on the prop and keys the arm again (the loader's hands on the
    # bipod's legs as they swing open).
    for mname, arm in members.items():
        grab = state.get("crew", {}).get(mname, {}).get("grab")
        if not grab:
            continue
        obj = props[grab["prop"]]
        points = {}
        for side, (bone, share) in grab["hands"].items():
            axis, lo, hi, base, radius = part_axis(obj, bone)
            # The share runs from the end at the bone's head (a bipod leg's hinge).
            top, foot = (lo, hi) if abs(lo) < abs(hi) else (hi, lo)
            points[side] = (bone, base + axis * (top + float(share) * (foot - top)), axis, radius)
        chain = [f"{n}_{sd}" for sd in grab["hands"] for n in ("Shoulder", "Elbow", "Wrist")]
        palms, extra = {}, set()
        worst = 0.0
        for f in range(1, length + 1):
            t = (f - 1) / span
            k = window_weight(grab["window"], t)
            if k <= 0.0:
                continue
            scene.frame_set(f)
            for side, (bone, local, axis, radius) in points.items():
                to_arm = arm.matrix_world.inverted() @ obj.matrix_world @ obj.pose.bones[bone].matrix
                wrist = arm.pose.bones[f"Wrist_{side}"]
                now = wrist.matrix.copy()
                pole = elbow_out(arm, side)
                goal, aim = wrap_hand(arm, side, to_arm @ local, (to_arm.to_3x3() @ axis).normalized(), radius, pole, palms.setdefault(side, {}))
                own = wrist.rotation_quaternion.copy()
                target = now.translation.lerp(goal.translation, k)
                # The elbow's aim comes and goes with the grab, swung round the arm.
                reach(arm, side, target, pole=swing_pole(arm, side, target, aim, k, palms[side]))
                settle_hand(wrist, own, goal.to_quaternion(), k)
                if grab.get("fingers") == "from":
                    extra.update(close_fingers(arm, side, a[mname]["basis"], k))
                if k >= 1.0:
                    worst = max(worst, (arm.pose.bones[f"Wrist_{side}"].head - goal.translation).length * arm.matrix_world.to_scale().x)
            for bone in chain + sorted(extra):
                key_basis(arm.pose.bones[bone], arm.pose.bones[bone].matrix_basis.copy(), f)
        print(f"{name}: {mname} takes hold of {grab['prop']} at {sorted(grab['hands'].items())}, worst miss {worst:.3f} m")

    if spec.get("preview"):
        preview(name, length)
    keep(name, length)
    export(name, length)


def along(path, t):
    """How far along its way a prop is at <t>, from [<share of the take>, <share of the way>]
    points, eased between each pair: the barrel lowered and held, then set down."""
    if t <= path[0][0]:
        return float(path[0][1])
    for (t0, s0), (t1, s1) in zip(path, path[1:]):
        if t <= t1:
            return s0 + (s1 - s0) * ease(t, (t0, t1))
    return float(path[-1][1])


def glide(x, accel):
    """How far along a walk she is at <x> (both shares), her speed rising evenly over the first
    <accel> of it, steady, then falling evenly over the last <accel>."""
    x = max(0.0, min(1.0, x))
    top = 1.0 / (1.0 - accel)
    if x < accel:
        return top * x * x / (2.0 * accel)
    if x < 1.0 - accel:
        return top * (accel / 2.0 + x - accel)
    return 1.0 - top * (1.0 - x) ** 2 / (2.0 * accel)


def window_weight(window, t):
    """A weight over a window of shares: two rise and settle again, four rise over the first two,
    hold, and fall over the last two."""
    if len(window) == 4:
        return ease(t, window[:2]) * (1.0 - ease(t, window[2:]))
    return 0.0 if t <= window[0] or t >= window[1] else math.sin(math.pi * (t - window[0]) / (window[1] - window[0]))


def part_axis(obj, bone):
    """The long axis of a prop's part in its bone's own frame, read off the vertices weighted to
    the bone at rest (a GFL2 bone need not run along its part), how far the part reaches along
    it each way, a point its axis passes through and its radius: (axis, low, high, base,
    radius)."""
    data = obj.data.bones[bone]
    to_bone = data.matrix_local.inverted()
    pts = []
    for mesh in (o for o in obj.children_recursive if o.type == "MESH"):
        group = mesh.vertex_groups.get(bone)
        if group is None:
            continue
        rel = obj.matrix_world.inverted() @ mesh.matrix_world
        pts += [to_bone @ (rel @ v.co) for v in mesh.data.vertices
                if v.groups and max(v.groups, key=lambda g: g.weight).group == group.index]
    centre = sum(pts, Vector()) / len(pts)
    end = max(pts, key=lambda p: (p - centre).length)
    other = max(pts, key=lambda p: (p - end).length)
    axis = (other - end).normalized()
    along = [p.dot(axis) for p in pts]
    base = centre - axis * centre.dot(axis)
    radius = sorted((p - base - axis * p.dot(axis)).length for p in pts)[len(pts) // 2]
    return axis, min(along), max(along), base, radius


def swing_pole(arm, side, target, aim, weight, memo):
    """The point an arm's elbow bends towards: its bend direction swung about the line from
    shoulder to hand, <weight> of the way from where the pose's elbow is to the side of <aim>.
    A point slid straight between the two can cross that line, where the bend is undecided and
    the elbow turns over. The way round is chosen once (kept in <memo>) for when the two sides
    are near opposite."""
    shoulder = arm.pose.bones[f"Shoulder_{side}"].head
    elbow = arm.pose.bones[f"Elbow_{side}"].head
    line = (target - shoulder).normalized()

    def across(p):
        v = p - shoulder
        return (v - line * v.dot(line)).normalized()

    if aim is None or weight <= 0.0:
        return None
    a, b = across(elbow), across(aim)
    angle = math.atan2(line.dot(a.cross(b)), a.dot(b))
    if "sense" not in memo:
        memo["sense"] = 1.0 if angle >= 0 else -1.0
    if abs(angle) > math.radians(150) and angle * memo["sense"] < 0:
        angle += memo["sense"] * 2.0 * math.pi
    d = Quaternion(line, angle * weight) @ a
    return shoulder + line * (elbow - shoulder).dot(line) + d * 0.3


def close_fingers(arm, side, basis, weight):
    """Turn a hand's fingers <weight> of the way to their bases in <basis> (a pose's, where the
    hand is closed round something), so a hand holding a part grips it."""
    hand = arm.pose.bones[f"Wrist_{side}"]
    for pb in hand.children_recursive:
        pb.matrix_basis = mix(pb.matrix_basis.copy(), basis[pb.name], weight)
    bpy.context.view_layer.update()
    return [pb.name for pb in hand.children_recursive]


def settle_hand(wrist, own, turn, weight):
    """Turn a wrist <weight> of the way from its own basis (<own>, the pose's hand on its forearm)
    to the armature-space <turn>, blended on the forearm: the pose's hand and a grip can face
    near opposite ways in armature space, where a blend there flips its way round mid-way. The
    hand bends at most 45 degrees off its forearm."""
    wrist.matrix = Matrix.LocRotScale(wrist.matrix.translation, turn, wrist.matrix.to_scale())
    bpy.context.view_layer.update()
    held = wrist.rotation_quaternion.copy()
    held.make_compatible(own)
    wrist.rotation_quaternion = own.slerp(held, weight)
    bpy.context.view_layer.update()
    # The hand bends at most 45 degrees off its forearm, measured from the forearm's line to the
    # line from the wrist to the middle knuckle: a rod lying along the forearm would bend it
    # further to close on it. Only the bend is taken back, about the axis square to both lines.
    arm, side = wrist.id_data, wrist.name.rsplit("_", 1)[1]
    fore = wrist.head - arm.pose.bones[f"Elbow_{side}"].head
    hand = arm.pose.bones[f"MiddleFinger1_{side}"].head - wrist.head
    over = fore.angle(hand) - math.radians(45)
    if over > 0.0:
        turn_about(wrist, Quaternion(hand.cross(fore).normalized(), over), wrist.head.copy())


def elbow_out(arm, side):
    """A point down and out from her side for an elbow to bend towards, so a hand reaching for a
    part brings its forearm to it from outside her body: an elbow aimed from the part back to
    her shoulder ends up in her chest when the part is in front of her."""
    other = "R" if side == "L" else "L"
    out = (arm.pose.bones[f"Hip_{side}"].head - arm.pose.bones[f"Hip_{other}"].head).normalized()
    down = -(arm.matrix_world.inverted().to_3x3() @ Vector((0, 0, 1))).normalized()
    return arm.pose.bones[f"Elbow_{side}"].head + down * 0.25 + out * 0.2


def thigh_lines(arm):
    """Each thigh's line (hip to knee) in the pelvis's frame, from the blended pose."""
    pelvis = arm.pose.bones["Root_M"].matrix.to_3x3().inverted()
    return {sd: (pelvis @ (arm.pose.bones[f"Knee_{sd}"].head - arm.pose.bones[f"Hip_{sd}"].head)).normalized() for sd in ("L", "R")}


def follow_thighs(arm, before):
    """Turn the coat's chains with how far the legs moved off the blended pose's thighs (stepping,
    kneeling, the hips sinking), in the pelvis's frame: the coat is blended with the pose's
    thighs, so legs the IK moves otherwise go through it. A chain takes a share of its thigh's
    turn by its position (the spec's "coat"), as swing_coat shares a donor thigh's swing. Returns
    the largest turn."""
    pelvis = arm.pose.bones["Root_M"].matrix.to_3x3()
    rest_pelvis = arm.data.bones["Root_M"].matrix_local.to_3x3()
    forward = (pelvis @ rest_pelvis.inverted() @ Vector((0, 0, 1))).normalized()
    largest = 0.0
    for side, chains in coat["chains"].items():
        now = (pelvis.inverted() @ (arm.pose.bones[f"Knee_{side}"].head - arm.pose.bones[f"Hip_{side}"].head)).normalized()
        turn_local = before[side].rotation_difference(now)
        largest = max(largest, turn_local.angle)
        turn = pelvis.to_quaternion() @ turn_local @ pelvis.to_quaternion().inverted()
        ahead = (pelvis @ (now - before[side])).dot(forward) > 0
        for chain, where in chains:
            pb = arm.pose.bones.get(chain)
            if pb is None:
                continue
            turn_about(pb, Quaternion().slerp(turn, coat["follow"][where][0 if ahead else 1]), pb.head.copy())
    return largest


def knee_aim(arm, side, target, metres, memo=None, toes=None, pose=1.0):
    """The point a leg's knee bends towards, for a foot going to <target>: the pose's own knee
    where it stands forward of the line from hip to foot (a bent leg, a kneel, which keep their
    pose), turning to straight ahead as it comes back to the line or behind it, where it gives no
    side to bend to (a straight leg) or the wrong one (a pose blended between two stances), and
    the knee would flip over. Forward by 5 cm or more is the pose's knee alone. With <toes> (the
    way the foot points, armature space) straight ahead lies mostly the way the foot points: the
    knee hinges over the foot, and a foot turned off her hips (planted while she turns, or
    pivoting on its ball) turns the knee with it rather than twisting under a knee kept to the
    hips. <pose> (0 to 1) is how far the pose's knee counts at all: a walking leg whose foot is
    far from where the pose has it (behind her, out to the side) takes a pose knee that lies off
    to the side of its line, so a walk counts it only as she settles into her end pose."""
    hip = arm.pose.bones[f"Hip_{side}"].head
    knee = arm.pose.bones[f"Knee_{side}"].head
    line = (target - hip).normalized()
    across = (knee - hip) - line * (knee - hip).dot(line)
    hips = arm.pose.bones["Root_M"]
    ahead = hips.matrix.to_3x3() @ arm.data.bones["Root_M"].matrix_local.to_3x3().inverted() @ Vector((0, 0, 1))
    if toes is not None:
        ahead = ahead.normalized() * 0.3 + toes.normalized() * 0.7
    ahead = (ahead - line * ahead.dot(line)).normalized()
    clear = pose * max(0.0, min(1.0, across.dot(ahead) * metres / 0.05))
    way = ((across.normalized() * clear + ahead * (1.0 - clear)) if across.length > 1e-6 else ahead).normalized()
    if memo is not None:
        # At most 45 degrees a frame: a leg near straight barely decides which way its knee goes,
        # and the knee rolls over in a frame where the choice swings.
        before = memo.get("way")
        if before is not None and way.dot(before) < 0.7071:
            axis = before.cross(way)
            axis = axis.normalized() if axis.length > 1e-6 else line
            way = Quaternion(axis, math.radians(45)) @ before
        memo["way"] = way.copy()
    return hip + line * (knee - hip).dot(line) + way * 0.3 / metres


def facing_of(arm, up):
    """The way her hips face (the rig faces +Z at rest), flat across the ground, armature space."""
    hips = arm.pose.bones["Root_M"]
    ahead = hips.matrix.to_3x3() @ arm.data.bones["Root_M"].matrix_local.to_3x3().inverted() @ Vector((0, 0, 1))
    return (ahead - up * ahead.dot(up)).normalized()


def wrap_hand(arm, side, at, axis, radius, pole=None, memo=None):
    """Where a wrist goes, and how it turns (armature space), for a hand closed round a rod at
    <at> running along <axis>: the knuckles go on in the line of the forearm, squared to the rod,
    the palm faces across it to whichever side turns the wrist least, and the rod lies in the
    hollow of the palm. Solved twice, since the forearm's line follows the wrist. With no <pole>
    the elbow goes down and out from her side (elbow_out). <memo>
    (a dict kept from frame to frame) holds the palm's side, which is chosen once and then kept
    nearest the frame before: chosen afresh it flips where the two sides are near even. Returns
    the wrist's placement and the pole used."""
    wrist = arm.pose.bones[f"Wrist_{side}"]
    rest = arm.data.bones
    half = 0.5 * (rest[f"MiddleFinger1_{side}"].head_local - rest[f"Wrist_{side}"].head_local).length
    palm_rest = rest[f"Wrist_{side}"].matrix_local.to_3x3().inverted() @ rest_palm(arm, side)
    chain = [arm.pose.bones[f"{n}_{side}"] for n in ("Shoulder", "Elbow", "Wrist")]
    saved = [pb.matrix_basis.copy() for pb in chain]
    goal = wrist.matrix.copy()
    if pole is None:
        pole = elbow_out(arm, side)
    for _ in range(2):
        fore = wrist.head - arm.pose.bones[f"Elbow_{side}"].head
        knuckles = fore - axis * fore.dot(axis)
        if knuckles.length < 1e-6:
            knuckles = axis.orthogonal()
        knuckles.normalize()
        palm = axis.cross(knuckles).normalized()
        before = memo.get("palm") if memo is not None else None
        if (before if before is not None else wrist.matrix.to_3x3() @ palm_rest).dot(palm) < 0:
            palm = -palm
        if memo is not None:
            memo["next"] = palm.copy()
        place = at - palm * (radius + 0.02) - knuckles * half
        goal = Matrix.LocRotScale(place, hand_turn(arm, side, knuckles, palm=palm), wrist.matrix.to_scale())
        reach(arm, side, place, pole=pole)
        bpy.context.view_layer.update()
    # Leave the arm as it was: the caller bends it to the goal at its own weight.
    for pb, m in zip(chain, saved):
        pb.matrix_basis = m
    bpy.context.view_layer.update()
    if memo is not None:
        memo["palm"] = memo.pop("next")
    return goal, pole


def grip_point(obj, bone, share):
    """A point on a prop's part, in its bone's own frame: <share> of the way from the bone's head
    to the part's farthest vertex at rest (a bipod leg's hinge to its foot)."""
    data = obj.data.bones[bone]
    head = data.head_local
    far = head
    for mesh in (o for o in obj.children_recursive if o.type == "MESH"):
        group = mesh.vertex_groups.get(bone)
        if group is None:
            continue
        rel = obj.matrix_world.inverted() @ mesh.matrix_world
        for v in mesh.data.vertices:
            if v.groups and max(v.groups, key=lambda g: g.weight).group == group.index:
                p = rel @ v.co
                if (p - head).length > (far - head).length:
                    far = p
    return data.matrix_local.inverted() @ head.lerp(far, share)


def prop_placement(ref, frame):
    """Where a reference prop's mesh sits at <frame> of its own take: the placement of its
    "Prop" bone (which the GFL2 prop takes move, and the mesh follows) undone from rest. A team
    prop at rest placed there shows the mesh where the scene had it."""
    scene.frame_set(int(frame))
    moving = ref.data.bones.get("Prop") or ref.data.bones[0]
    return ref.matrix_world @ ref.pose.bones[moving.name].matrix @ moving.matrix_local.inverted()


def read_tracks():
    """Read every reference placement the states ask for, then drop the reference objects."""
    for state in spec["states"]:
        for pname, how in state["props"].items():
            if not isinstance(how, dict):
                continue
            if "grips_ref" in how:
                g = how["grips_ref"]
                ref, carrier = extras[g["prop_ref"]], extras[g["carrier_ref"]]
                for side in ("L", "R"):
                    key = ("grip", g["prop_ref"], g["carrier_ref"], side, g["frame"])
                    if key not in tracks:
                        placed = prop_placement(ref, g["frame"])
                        wrist = no_scale(carrier.matrix_world @ carrier.pose.bones[f"Wrist_{side}"].matrix)
                        tracks[key] = no_scale(placed).inverted() @ wrist
                        elbow = carrier.matrix_world @ carrier.pose.bones[f"Elbow_{side}"].head
                        tracks[("grip_elbow",) + key[1:]] = no_scale(placed).inverted() @ elbow
            if "match" in how:
                m = how["match"]
                key = ("match", m["prop_ref"], m["carrier_ref"], how.get("bone", "Wrist_R"), m["frame"])
                if key not in tracks and m["prop_ref"].startswith("idle:"):
                    # Held as the team's own idle holds it ("idle:<prop>" in "idle:<member>"'s
                    # hand): both play their native idle and the relation is read at <frame>.
                    pobj, carm = props[m["prop_ref"][5:]], members[m["carrier_ref"][5:]]
                    assign(reset(carm), action_for(carm, spec["formation_clip"][carm.name]))
                    pad = reset(pobj)
                    pobj.matrix_parent_inverse, pobj.matrix_basis = (x.copy() for x in prop_base[pobj.name])
                    assign(pad, action_for(pobj, spec["prop_idle"][pobj.name]))
                    placed = prop_placement(pobj, m["frame"])
                    wrist = no_scale(carm.matrix_world @ carm.pose.bones[key[3]].matrix)
                    tracks[key] = wrist.inverted() @ placed
                    reset(carm)
                    reset(pobj)
                if key not in tracks:
                    ref, carrier = extras[m["prop_ref"]], extras[m["carrier_ref"]]
                    placed = prop_placement(ref, m["frame"])
                    wrist = no_scale(carrier.matrix_world @ carrier.pose.bones[key[3]].matrix)
                    tracks[key] = wrist.inverted() @ placed
            elif "follow" in how:
                key = ("follow", how["prop_ref"], tuple(how["range"]))
                if key not in tracks:
                    first, last = how["range"]
                    tracks[key] = [prop_placement(extras[how["prop_ref"]], f) for f in range(int(first), int(last) + 1)]
                if how.get("bones"):
                    # The named bones' own poses through the same frames, as bases.
                    ref = extras[how["prop_ref"]]
                    own = []
                    for f in range(int(how["range"][0]), int(how["range"][1]) + 1):
                        scene.frame_set(f)
                        own.append({b: ref.pose.bones[b].matrix_basis.copy() for b in how["bones"]})
                    tracks[("follow_bones", how["prop_ref"], tuple(how["range"]))] = own
            held_as = [(x["as"]["bone"], {"carrier": x["as"]["holder"], "bone": x["as"].get("wrist", "Wrist_R"),
                                          "frame": x["as"].get("frame", 1)})
                       for x in how.get("bone_sling", {}).values() if "as" in x]
            for bname, m in list(how.get("bone_match", {}).items()) + held_as:
                # A single bone of a prop held by a crew member as the team's idle has it: both
                # play their native idle and the bone's placement is read off her bone at <frame>.
                pobj = props[pname]
                key = ("bone_match", pobj.name, bname, m["carrier"], m["bone"], m["frame"])
                if key not in tracks:
                    carm = members[m["carrier"]]
                    assign(reset(carm), action_for(carm, spec["formation_clip"][carm.name]))
                    pad = reset(pobj)
                    pobj.matrix_parent_inverse, pobj.matrix_basis = (x.copy() for x in prop_base[pobj.name])
                    assign(pad, action_for(pobj, spec["prop_idle"][pobj.name]))
                    scene.frame_set(int(m["frame"]))
                    held = no_scale(pobj.matrix_world @ pobj.pose.bones[bname].matrix)
                    wrist = no_scale(carm.matrix_world @ carm.pose.bones[m["bone"]].matrix)
                    tracks[key] = wrist.inverted() @ held
                    reset(carm)
                    reset(pobj)
        if "anchor" in state:
            # The placement that takes the anchor prop from where the reference scene has it to
            # where the team's own idle has it at its first frame.
            a = state["anchor"]
            key = ("anchor", a["prop"], a["ref"], a.get("frame", 1))
            if key not in tracks:
                pobj = props[a["prop"]]
                pad = reset(pobj)
                pobj.matrix_parent_inverse, pobj.matrix_basis = (x.copy() for x in prop_base[pobj.name])
                assign(pad, action_for(pobj, spec["prop_idle"][pobj.name]))
                home = no_scale(prop_placement(pobj, 1))
                there = no_scale(prop_placement(extras[a["ref"]], a.get("frame", 1)))
                tracks[key] = home @ there.inverted()
                reset(pobj)
                print(f"anchor {a['prop']}: turned {math.degrees(tracks[key].to_euler().z):.1f} deg, moved {tuple(round(v, 3) for v in tracks[key].translation)}")
    for o in extra_objects:
        if o.name in bpy.data.objects:
            bpy.data.objects.remove(o)
    print(f"read {len(tracks)} reference track(s)")


read_tracks()

for mname, arm in members.items():
    places[mname] = formation(arm)
    print(f"formation {mname}: root at {tuple(round(v, 3) for v in places[mname][0])} turned {tuple(round(v, 3) for v in places[mname][1])}")


def colour_maps():
    """Wire each material's colour map from the exported textures, by the material's name
    (member, part), the way the Unity bake maps them."""
    files = {}
    for d in spec.get("texture_dirs", []):
        for f in os.listdir(d):
            if f.lower().endswith(".png"):
                files.setdefault(f[:-4].lower(), os.path.join(d, f))
    parts = (("eyelash", "face_d"), ("eye", "eye_d"), ("face", "face_d"), ("fringe", "hair_d"),
             ("hair", "hair_d"), ("helmet", "helmet_d"), ("upper", "upper_d"), ("lower", "lower_d"))
    for mat in bpy.data.materials:
        base = re.sub(r"\.\d+$", "", mat.name).lower()
        m = re.match(r"(c_.+?_slg)_(.+)$", base)
        path = None
        if m:
            path = next((files.get(f"{m.group(1)}_{tex}") for key, tex in parts if m.group(2).startswith(key)), None)
        elif base.startswith(tuple(spec.get("prop_materials", []))):
            stem = base.replace("_uber", "")
            path = files.get(f"{stem}_d") or next((p for k, p in files.items() if k.startswith(stem.split("_lod")[0]) and k.endswith("_d")), None)
        if not path or not mat.node_tree:
            continue
        bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf is None:
            continue
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(path, check_existing=True)
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        # Solid shading coloured by texture shows the material's active image node.
        mat.node_tree.nodes.active = tex
        if "eyenewadd" in base or "eyenewmul" in base:
            bsdf.inputs["Alpha"].default_value = 0.0


def write_review(path):
    """One .blend with every state back to back on the timeline, each marked by name: the crew
    and props play each state's baked action in turn, ready to watch with Space."""
    objs = {o.name: o for o in list(members.values()) + list(props.values()) + [motion]}
    for o in objs.values():
        reset(o)
    scene.timeline_markers.clear()
    start = 1
    for name, length, keep in review:
        scene.timeline_markers.new(name, frame=start)
        for oname, act in keep.items():
            if act is None:
                continue
            ad = objs[oname].animation_data_create()
            track = ad.nla_tracks[0] if ad.nla_tracks else ad.nla_tracks.new()
            strip = track.strips.new(f"{name}", start, act)
            strip.extrapolation = "NOTHING"
        start += length
    scene.frame_start, scene.frame_end = 1, start - 1
    cam = bpy.data.objects.get("preview_cam")
    if cam:
        bpy.data.objects.remove(cam)
    colour_maps()
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == "VIEW_3D":
                space = area.spaces[0]
                # Material preview is not offered in a background session, so the file opens
                # in solid shading coloured by each material's texture.
                space.shading.type = "SOLID"
                space.shading.color_type = "TEXTURE"
                space.region_3d.view_location = Vector((0, 0, 0.8))
                space.region_3d.view_distance = 5.0
                space.region_3d.view_perspective = "PERSP"
    scene.frame_set(1)
    bpy.ops.wm.save_as_mainfile(filepath=path)
    print(f"review {path}: {len(review)} states, {start - 1} frames, markers at {[m.frame for m in scene.timeline_markers]}")


only = set(_args[2:])
# A transition reads the states it joins and the states its key poses come from, so naming one
# builds them too.
for state in spec["states"]:
    if "between" in state and state["name"] in only:
        only.update(state["between"])
        for part in state.get("crew", {}).values():
            only.update(k["pose"][0] for k in part.get("keys", []) if isinstance(k.get("pose"), list))
for state in spec["states"]:
    if not only or state["name"] in only:
        (build_transition if "between" in state else build)(state)
if spec.get("review"):
    write_review(spec["review"])
