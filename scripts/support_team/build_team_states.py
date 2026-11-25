"""Build a support team's per-state takes from its per-object AssetStudio exports.

Every crew member was exported with the same set of clips (her native idle and the donor Doll
clips). For each state the crew play their clip a little out of step, the props either play
their native idle or ride their carrier, and the result is baked into one take.

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
    markers          empties hung on a prop's bone that ride every take, so the Unity bake can
                     hang a skill point on one: name, prop, bone, and "at" (the prop armature's
                     space at rest)
    states           the states, in order

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
    crew             per member, what she plays:
        clip         the clip
        range        [first, last] frames of it, or the whole clip
        offset       frames she runs ahead (loop) or starts late, so the crew are out of step
        place        "formation" (default unless native): her root on her formation spot plus
                     the clip's own travel unless in_place, facing the team's way. "shift": the
                     clip's own motion and facing, moved to start on her formation spot (a take
                     from another scene). "native": as the clip has it
        upper        {clip, frame, from}: the bones under "from" (one bone or a list, Spine1_M
                     by default) hold that clip's pose at that frame for the whole take
    props            per prop, one of:
        "idle"       it plays its native idle, in step with a pingpong when there is one
        follow       {follow: member, prop_ref, range}: keyed frame by frame where the extra
                     source <prop_ref> has it over that range, moved as the member's "shift"
                     moved her
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
            idle_pose       prop bones posed as its native idle has them at frame 1
            grips           per side ("L", "R"), where her hand goes, kept in the prop's space:
                            a point from "at" in her frame, or {at, fingers, thumb | palm} with
                            the way the knuckles point ("forearm" carries on along the forearm)
                            and the way the thumb side or the palm faces
            grips_ref       {prop_ref, carrier_ref, frame, shift, elbow_push}: each wrist's
                            whole placement and elbow read from a reference scene at a frame.
                            "shift" moves a hand along the prop (its own space), "elbow_push"
                            moves an elbow's bend target (the team's frame)

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
def load(src, name):
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
    return kept, next(o for o in kept if o.type == "ARMATURE")


def stem_of(src):
    return src.replace("\\", "/").rsplit("/", 1)[-1].rsplit(".", 1)[0]


for src in spec["members"] + spec["props"]:
    load(src, f"{team}_{stem_of(src)}")
for o in list(bpy.data.objects):
    if o.type == "MESH" and o.name.endswith("_lod1"):
        bpy.data.objects.remove(o)

# Takes from other GFL2 scenes (the ult). The crew's rest comes from the skin's bind pose, the
# same in every scene, so a crew member's take from there plays on the team's own armature. A
# prop's rest is wherever that scene had it, so a prop's take is read off the scene's own
# objects instead (see track), which stay loaded as references until the tracks are read.
extras, extra_objects = {}, []
for src in spec.get("extra_sources", []):
    before_actions = set(bpy.data.actions)
    added, arm = load(src, f"ref_{stem_of(src)}")
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

    if "steady" in how:
        steady(obj, how, arm, bone, desired, centre)
    else:
        rigid(obj, arm, bone, desired)
    # Bones of the prop posed as its native idle has them at frame 1 (the laptop's lid, closed).
    for bname, basis in how.get("_idle_pose", {}).items():
        pb = obj.pose.bones[bname]
        pb.location, pb.rotation_quaternion, pb.scale = basis
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
        if isinstance(g, dict):
            # A grip with a hand direction: where the knuckles point ("fingers") and which way
            # across the palm the thumb lies ("thumb") or which way the palm faces ("palm"), in the carrier's frame.
            fingers = g["fingers"]
            grips[side] = {"at": desired.inverted() @ (spot + Vector(g["at"])),
                           "fingers": fingers if isinstance(fingers, str) else back @ Vector(fingers)}
            for k in ("thumb", "palm"):
                if k in g:
                    grips[side][k] = back @ Vector(g[k])
        else:
            grips[side] = desired.inverted() @ (spot + Vector(g))
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
    ult), moved with its carrier from her spot in that scene to her spot in the team."""
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
        placed = Matrix.Translation(offset) @ placements[min(i, n - 1)]
        loc, rot, _ = placed.decompose()
        prev = rot.to_euler("XYZ", prev) if prev is not None else rot.to_euler("XYZ")
        obj.location, obj.rotation_euler, obj.scale = loc, prev, (1, 1, 1)
        obj.keyframe_insert("location", frame=f)
        obj.keyframe_insert("rotation_euler", frame=f)
        obj.keyframe_insert("scale", frame=f)


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


def reach(arm, side, target, pole=None):
    """Two-bone IK in armature space: bend the arm (Shoulder, Elbow) so the wrist reaches <target>,
    with the elbow towards <pole>, or on the side the clip already bends it to. The rig's bone
    tails do not point at their children, so Blender's own IK, which reaches with tails, does
    not fit it."""
    sh, el, wr = (arm.pose.bones[f"{n}_{side}"] for n in ("Shoulder", "Elbow", "Wrist"))
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
            else:
                target = to_carrier @ local
            reach(arm, side, target, pole)
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


def place(arm, mname, mode, in_place):
    """Put a member where she stands in the team.

    "formation" (a donor clip, whose root starts at rest): her root takes her idle's location,
    plus the clip's own root travel unless the state is in place. Her idle's facing is left
    out: in the idle each member turns to the camera, while a borrowed state faces the team's
    way. "shift" (a take from another GFL2 scene, the ult): her root keeps the take's motion
    and facing, moved so that she starts on her formation spot. "native": left as it is."""
    if mode == "native":
        return Vector((0, 0, 0))
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
        return Vector((0, 0, 0))
    scene.frame_set(1)
    return (arm.matrix_world @ arm.pose.bones["root"].matrix).translation - before


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
        action = action_for(arm, part["clip"])
        if action is None:
            raise SystemExit(f"{name}: no clip {part['clip']} on {member}")
        if is_donor(action):
            donor.add(member)
        if "upper" in part:
            up = part["upper"]
            up_action = action_for(arm, up["clip"])
            if up_action is None:
                raise SystemExit(f"{name}: no clip {up['clip']} on {member}")
            tops = up.get("from", "Spine1_M")
            names = set().union(*(subtree(arm, t) for t in ([tops] if isinstance(tops, str) else tops)))
            uppers[member] = (pose_at(arm, up_action, up["frame"]), names)
        if state.get("idle_hair"):
            # A donor clip leaves her hair in its bind pose, where the fringe cuts into her face.
            # Her own idle lifts it clear, so her hair chains hold its pose at frame 1.
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
    for mname, arm in members.items():
        part = state["crew"].get(mname, {})
        moved[mname] = place(arm, mname, part.get("place", default_place), state.get("in_place"))
        if mname in uppers:
            hold_bones(arm, *uppers[mname])
        if mname in hair:
            hold_bones(arm, *hair[mname])


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
    # Props bake object and pose together: their armatures sit at scale 1 under the team root,
    # so the object bake is sound for them, and a held prop's placement varies by state.
    bake(list(props.values()), length, {"POSE", "OBJECT"})

    if spec.get("preview"):
        preview(name, length)
    if spec.get("review"):
        keep = {}
        for o in list(members.values()) + list(props.values()) + [motion]:
            act = o.animation_data.action if o.animation_data else None
            if act is not None:
                act = act.copy()
                act.name = f"{o.name}|{name}"
                act.use_fake_user = True
            keep[o.name] = act
        review.append((name, length, keep))

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
        for how in state["props"].values():
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
for state in spec["states"]:
    if not only or state["name"] in only:
        build(state)
if spec.get("review"):
    write_review(spec["review"])
