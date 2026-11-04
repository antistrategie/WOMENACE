#!/usr/bin/env python3
"""Build Welrod's umbrella (hand-held and back-stowed) from her GFL2 battle rig.

GFL2 has no prefab for the umbrella: `c_WelrodSSR01_slg_weapon_open_lod0` is a
skinned child of her character rig `c_WelrodSSR01_slg_skin`, held in her right
hand through `Weapon_02` (the shaft, a child of `Wrist_R`), `weapon_16` (the
runner that slides along it) and twelve ribs `weapon_04..15` hanging off the
runner. The bind pose is the open umbrella. Her clips fold it by turning the
ribs about 75 degrees and sliding the runner 0.40 m down the shaft, and hide it
by scaling `Weapon_02` to 0.01. The separate `..._weapon_close` mesh exports
with no skin at all, so the folded open mesh stands in for the closed umbrella.

This script lifts the shaft subtree out into a standalone weapon rig,
re-expresses mesh and rest poses in the umbrella's own frame, and bakes
umbrella-local clips out of `UltraSkill_God_Main`, which opens it (frames ~1-11)
and folds it again (~66-86) with nothing else in the way.

Three stages:

  blender -b --python scripts/weapon/build_welrod_umbrella.py -- --stage raw
      the VoyExport FBX -> Welrod_SSR01_raw.blend (imported once)

  blender -b --python scripts/weapon/build_welrod_umbrella.py -- --stage build
      Welrod_SSR01_raw.blend -> welrod_umbrella_authored.blend + renders

  blender -b --python scripts/weapon/build_welrod_umbrella.py -- --stage export
      welrod_umbrella_authored.blend ->
      unity/Assets/Authored/weapon/welrod_umbrella/{raw.glb,controller.json,textures/}
      unity/Assets/Authored/weapon/welrod_umbrella_back/{raw.glb,textures/}
  then bake both through the usual weapon route, which picks the controller up:
      python3 scripts/weapon/shade_weapons.py --bake welrod_umbrella welrod_umbrella_back
  (the umbrella's post_bake.txt then rebuilds the carrier controller into its prefab).

The carrier's own body animation (drawing, opening, holding up, folding, stowing) is cut
from the same GFL2 ultimate as humanoid muscle clips, in Unity batch mode:
  ExtractHumanoidClips -ranges Assets/Authored/welrod/_donor/umbrella_ranges.txt
      (the donor FBX at Assets/Authored/welrod/_donor/WelrodSSR01_combat.fbx is this
      export's VoyExport FBX, gitignored) -> Authored/weapon/welrod_umbrella/carrier/*.anim
  TuneUmbrellaHold      raises the arm so the umbrella stands up in Raise and Hold
  BuildUmbrellaCarrier  the carrier controller, into the umbrella prefab
MeasureUmbrellaGrip measures the grip UmbrellaCarrySystem uses while those clips play.

The authored blend carries the weapon-pipeline objects: a `<name>_root` empty
with the mesh, the rig and the `muzzle` / `weapon_hand_l` empties as siblings.
Nudge them (or the mesh, for the right-hand grip) and re-run the export stage;
a rebuild carries the nudges over.

Frame of reference (Blender, before the glTF Y-up swap): forward along -Y,
up along +Z, the right hand at the origin. The shaft stands up out of the
fist, leaning forward (see HOLD_LEAN_DEG), canopy on top. The umbrella's own frame is `Weapon_02` at bind: its
+Y runs from the handle up the shaft to the tip.
"""

import argparse
import json
import math
import shutil
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

FBX = Path.home() / "gfl2-extract" / "out" / "welrod_umbrella" / "fbx.fbx"
TEX_SRC = Path.home() / "gfl2-extract" / "out" / "welrod_umbrella" / "tex" / "Texture2D"
WORK = Path.home() / "Downloads" / "Welrod_Umbrella"
RAW_BLEND = WORK / "Welrod_SSR01_raw.blend"
AUTHORED_BLEND = WORK / "welrod_umbrella_authored.blend"
RENDER_DIR = WORK / "renders"

REPO = Path(__file__).resolve().parents[2]
AUTHORED = REPO / "unity" / "Assets" / "Authored" / "weapon"

SRC_ARMATURE = "c_WelrodSSR01_slg_skin"
SRC_MESH = "c_WelrodSSR01_slg_weapon_open_lod0"
SRC_CLIP = SRC_ARMATURE + "|c_WelrodSSR01_slg_UltraSkill_God_Main|Base Layer"
ROOT_BONE = "Weapon_02"
RUNNER = "weapon_16"

UMBRELLA = "welrod_umbrella"
BACK = "welrod_umbrella_back"
MATERIAL = "umbrella"
CLIPS = ("stow", "deploy", "deployed", "stowing")

# Detection of the open/fold windows: frames where any non-root bone moves.
MOTION_EPS_M = 0.001
MOTION_EPS_DEG = 0.3
SEGMENT_MERGE_GAP = 4
SEGMENT_PADDING = 1

# Umbrella frame (Weapon_02 at bind) -> Blender weapon frame: shaft +Y to
# Blender -Y (forward, canopy leading), bone +X kept, bone +Z flipped to stay
# right-handed (a half turn about X).
UMB_TO_WEAPON = Matrix(((1, 0, 0, 0),
                        (0, -1, 0, 0),
                        (0, 0, -1, 0),
                        (0, 0, 0, 1)))
# The hold: held like an umbrella, not aimed like a rifle. The shaft stands up
# out of the right fist, leaning HOLD_LEAN_DEG forward from vertical so the
# canopy shields her front, and the left hand takes the shaft higher up.
# GRIP is where the shaft passes through the fist: below and slightly behind
# the right-hand origin, which sits at the top of a rifle's pistol grip.
HOLD_LEAN_DEG = 30.0
GRIP = Vector((0.0, 0.015, -0.05))
# The forward axis (-Y) turned up onto the shaft: a rotation about X.
HOLD_ROT = Matrix.Rotation(-math.radians(90.0 - HOLD_LEAN_DEG), 4, "X")
# The left hand is the vanilla ARC rifle's grip turned with the shaft. Its
# weapon_hand_l sits at Unity (-0.099, -0.054, 0.392) on a handguard 0.09 m
# above the origin; here it takes the same hold LEFT_HAND_STATION up the shaft.
LEFT_HAND_STATION = 0.20
ARC_HAND_FROM_HANDGUARD = Vector((0.099, -LEFT_HAND_STATION, -0.054 - 0.09))
# The ARC's weapon_hand_l rotation as glTF (x, y, z, w).
ARC_HAND_GLTF_QUAT = (-0.11617741, 0.49007735, 0.5628476, -0.65538514)

# Back item: the folded umbrella hangs along the spine, handle up and tip
# down, centred this far above the Back_Special socket. Authored in the body's
# frame as worn, then yawed half a turn like the railgun bracket (the socket's
# forward matches the soldier's).
BACK_CENTRE_HEIGHT = 0.05
BACK_YAW = Matrix.Rotation(math.pi, 4, "Z")
# leaned across the back like a slung sword, handle over the right shoulder
BACK_LEAN = Matrix.Rotation(math.radians(20), 4, "Y")


def log(*a):
    print("[umbrella]", *a, flush=True)


def fcurves(act):
    try:
        return [fc for layer in act.layers for strip in layer.strips
                for cb in strip.channelbags for fc in cb.fcurves]
    except AttributeError:
        return list(act.fcurves)


def gltf_quat_to_blender(q):
    qx, qy, qz, qw = q
    change = Quaternion((1.0, 0.0, 0.0), math.radians(-90.0))
    return change @ Quaternion((qw, qx, qy, qz)) @ change.inverted()


def blender_quat_to_gltf(q):
    change = Quaternion((1.0, 0.0, 0.0), math.radians(-90.0))
    g = change.inverted() @ q @ change
    return (g.x, g.y, g.z, g.w)


LEFT_HAND = GRIP + HOLD_ROT @ ARC_HAND_FROM_HANDGUARD
LEFT_HAND_QUAT = HOLD_ROT.to_quaternion() @ gltf_quat_to_blender(ARC_HAND_GLTF_QUAT)
# pinned on export (see pin_node_rotation)
LEFT_HAND_GLTF_QUAT = blender_quat_to_gltf(LEFT_HAND_QUAT)


def subtree(arm, root_name):
    out = [arm.data.bones[root_name]]
    i = 0
    while i < len(out):
        out.extend(out[i].children)
        i += 1
    return out


def link(obj):
    bpy.context.scene.collection.objects.link(obj)
    return obj


def assign_action(obj, act):
    if obj.animation_data is None:
        obj.animation_data_create()
    obj.animation_data.action = act
    if act is not None:
        try:
            obj.animation_data.action_slot = act.slots[0]
        except (AttributeError, IndexError):
            pass


# --------------------------------------------------------------------------
# raw stage
# --------------------------------------------------------------------------

def stage_raw():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(FBX))
    log(f"imported {FBX}: {len(bpy.data.actions)} actions, scene fps {bpy.context.scene.render.fps}")
    WORK.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(RAW_BLEND))
    log(f"saved {RAW_BLEND}")


# --------------------------------------------------------------------------
# build stage
# --------------------------------------------------------------------------

def transform_mesh_copy(src_obj, name, world_to_new, keep_groups=True):
    """A standalone copy of src_obj's mesh with its bind-pose world geometry
    re-expressed through world_to_new, custom normals included."""
    me = src_obj.data.copy()
    me.name = name
    obj = link(bpy.data.objects.new(name, me))
    full = world_to_new @ src_obj.matrix_world
    rot = full.to_3x3()
    normals = [Vector(n.vector) for n in me.corner_normals]
    for v in me.vertices:
        v.co = full @ v.co
    me.normals_split_custom_set([(rot @ n).normalized() for n in normals])
    me.materials.clear()
    me.materials.append(bpy.data.materials.get(MATERIAL) or bpy.data.materials.new(MATERIAL))
    if keep_groups:
        for g in src_obj.vertex_groups:
            obj.vertex_groups.new(name=g.name)
        for v in src_obj.data.vertices:
            for g in v.groups:
                obj.vertex_groups[g.group].add([v.index], g.weight, "REPLACE")
    return obj


def build_rig(src_arm, bones, name, world_to_new):
    data = bpy.data.armatures.new(name)
    rig = link(bpy.data.objects.new(name, data))
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    rest = {}
    for b in bones:
        eb = data.edit_bones.new(b.name)
        m = world_to_new @ src_arm.matrix_world @ b.matrix_local
        eb.head = (0, 0, 0)
        eb.tail = (0, max(b.length, 0.01), 0)
        eb.matrix = m
        rest[b.name] = m.copy()
        if b.parent is not None and b.parent.name in data.edit_bones:
            eb.parent = data.edit_bones[b.parent.name]
            eb.use_connect = False
    worst = max(max(abs(x) for row in (data.edit_bones[b.name].matrix - rest[b.name]) for x in row)
                for b in bones)
    bpy.ops.object.mode_set(mode="OBJECT")
    log(f"{name}: {len(bones)} bones, max rest-matrix error {worst:.2e}")
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return rig


def local_pose(src_arm, names, frame):
    """Bone-local deltas at a frame; the root shaft is held still (its swing
    is her arm) and at unit scale (GFL2 hides the umbrella by shrinking it)."""
    bpy.context.scene.frame_set(frame)
    pose = {}
    for n in names:
        if n == ROOT_BONE:
            pose[n] = (Vector((0, 0, 0)), Quaternion((1, 0, 0, 0)), Vector((1, 1, 1)))
        else:
            loc, rot, scale = src_arm.pose.bones[n].matrix_basis.decompose()
            pose[n] = (loc, rot, scale)
    return pose


def pose_moved(a, b):
    for n in a:
        la, ra, _ = a[n]
        lb, rb, _ = b[n]
        if (la - lb).length > MOTION_EPS_M or math.degrees(ra.rotation_difference(rb).angle) > MOTION_EPS_DEG:
            return True
    return False


def detect_segments(src_arm, names, f0, f1):
    prev = None
    moving = []
    for f in range(f0, f1 + 1):
        pose = local_pose(src_arm, names, f)
        if prev is not None and pose_moved(prev, pose):
            moving.append(f)
        prev = pose
    runs = []
    for f in moving:
        if runs and f - runs[-1][1] <= SEGMENT_MERGE_GAP:
            runs[-1][1] = f
        else:
            runs.append([f, f])
    return [(max(f0, a - 1 - SEGMENT_PADDING), min(f1, b + SEGMENT_PADDING)) for a, b in runs]


def runner_slide(src_arm, frame):
    bpy.context.scene.frame_set(frame)
    return src_arm.pose.bones[RUNNER].matrix_basis.to_translation().length


class PoseWriter:
    """Keyframes with quaternion hemisphere continuity."""

    def __init__(self, rig):
        self.rig = rig
        self.last = {}

    def write(self, pose, frame):
        for n, (loc, rot, scale) in pose.items():
            prev = self.last.get(n)
            if prev is not None and prev.dot(rot) < 0:
                rot = -rot
            self.last[n] = rot.copy()
            pb = self.rig.pose.bones[n]
            pb.location = loc
            pb.rotation_quaternion = rot
            pb.scale = scale
            for path in ("location", "rotation_quaternion", "scale"):
                pb.keyframe_insert(path, frame=frame)


def new_action(rig, name):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    assign_action(rig, act)
    return act


def finish_action(act):
    for fc in fcurves(act):
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"


def bake_clips(src_arm, rig, names):
    src_act = bpy.data.actions[SRC_CLIP]
    assign_action(src_arm, src_act)
    f0, f1 = int(src_act.frame_range[0]), int(src_act.frame_range[1])
    segments = detect_segments(src_arm, names, f0, f1)
    log(f"motion windows in {src_act.name}: {segments}")
    if len(segments) != 2:
        raise SystemExit(f"expected an open and a fold window, found {segments}")
    (oa, ob), (fa, fb) = segments
    # the open window starts folded and ends open, the fold window the reverse
    log(f"runner slide: open {runner_slide(src_arm, oa):.3f} -> {runner_slide(src_arm, ob):.3f} m, "
        f"fold {runner_slide(src_arm, fa):.3f} -> {runner_slide(src_arm, fb):.3f} m")
    folded = local_pose(src_arm, names, oa)
    opened = local_pose(src_arm, names, ob)

    def window(name, a, b):
        act = new_action(rig, name)
        w = PoseWriter(rig)
        for i, f in enumerate(range(a, b + 1)):
            w.write(local_pose(src_arm, names, f), i + 1)
        finish_action(act)
        log(f"{name}: {b - a + 1} frames ({(b - a + 1) / bpy.context.scene.render.fps:.2f} s)")

    window("deploy", oa, ob)
    window("stowing", fa, fb)
    for name, pose in (("stow", folded), ("deployed", opened)):
        act = new_action(rig, name)
        w = PoseWriter(rig)
        w.write(pose, 1)
        w.write(pose, 2)
        finish_action(act)

    assign_action(rig, None)
    for name in CLIPS:
        act = bpy.data.actions[name]
        track = rig.animation_data.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 1, act)
        try:
            strip.action_slot = act.slots[0]
        except (AttributeError, IndexError):
            pass
        track.mute = True
    return ob


def verify_transfer(src_arm, src_mesh, rig, mesh, world_to_weapon, bind_root, names, frame):
    """The new rig must deform the umbrella as the source rig does, compared
    in the shaft's frame at the given (open) frame."""
    scene = bpy.context.scene
    assign_action(src_arm, bpy.data.actions[SRC_CLIP])
    assign_action(rig, bpy.data.actions["deployed"])
    scene.frame_set(frame)
    deps = bpy.context.evaluated_depsgraph_get()
    src_ev = src_mesh.evaluated_get(deps).data
    new_ev = mesh.evaluated_get(deps).data
    posed_root = src_arm.matrix_world @ src_arm.pose.bones[ROOT_BONE].matrix
    # undo the root's posed scale too: the source shaft may be scaled in the clip
    loc, rot, _ = posed_root.decompose()
    posed_root = Matrix.LocRotScale(loc, rot, bind_root.to_scale())
    to_new = world_to_weapon @ bind_root @ posed_root.inverted()
    worst = 0.0
    for i in range(len(src_ev.vertices)):
        a = to_new @ (src_mesh.matrix_world @ src_ev.vertices[i].co)
        b = mesh.matrix_world @ new_ev.vertices[i].co
        worst = max(worst, (a - b).length)
    log(f"transfer check at frame {frame}: max vertex error {worst * 1000:.2f} mm")
    assign_action(rig, None)
    assign_action(src_arm, None)
    scene.frame_set(1)
    return worst


def make_empty(name, parent, location, quat=None, display="ARROWS"):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = display
    e.empty_display_size = 0.05 if display == "ARROWS" else 0.1
    e.location = location
    e.rotation_mode = "QUATERNION"
    if quat is not None:
        e.rotation_quaternion = quat
    e.parent = parent
    return link(e)


def render_diagnostics(rig, mesh, back):
    RENDER_DIR.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "SINGLE"
    scene.display.shading.single_color = (0.55, 0.55, 0.6)
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 700
    world = bpy.data.worlds.new("diag_world")
    world.color = (0.9, 0.9, 0.9)
    scene.world = world
    cam_data = bpy.data.cameras.new("diag_cam")
    cam_data.type = "ORTHO"
    cam = link(bpy.data.objects.new("diag_cam", cam_data))
    scene.camera = cam

    markers = []
    for o in list(bpy.data.objects):
        if o.type == "EMPTY" and o.name in ("muzzle", "weapon_hand_l"):
            bpy.ops.mesh.primitive_uv_sphere_add(radius=0.025, location=o.matrix_world.to_translation())
            bpy.context.active_object.name = "marker_" + o.name
            markers.append(bpy.context.active_object)
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.025, location=(0, 0, 0))
    bpy.context.active_object.name = "marker_hand_r"
    markers.append(bpy.context.active_object)

    def shot(name, objs, axis):
        shown = list(objs) + (markers if mesh in objs else [])
        for o in bpy.data.objects:
            if o.type == "MESH":
                o.hide_render = o not in shown
        deps = bpy.context.evaluated_depsgraph_get()
        pts = [o.matrix_world @ v.co for o in objs for v in o.evaluated_get(deps).data.vertices]
        mn = Vector([min(p[i] for p in pts) for i in range(3)])
        mx = Vector([max(p[i] for p in pts) for i in range(3)])
        c = (mn + mx) / 2
        ext = mx - mn
        horizontal = {"side": ext.y, "top": ext.y, "front": ext.x}[axis]
        vertical = {"side": ext.z, "top": ext.x, "front": ext.z}[axis]
        off, up = {"side": (Vector((4, 0, 0)), Vector((0, 0, 1))),
                   "top": (Vector((0, 0, 4)), Vector((1, 0, 0))),
                   "front": (Vector((0, -4, 0)), Vector((0, 0, 1)))}[axis]
        z = (-off).normalized()
        x = up.cross(z).normalized()
        y = z.cross(x)
        cam.matrix_world = Matrix(((x.x, y.x, -z.x, (c + off).x),
                                   (x.y, y.y, -z.y, (c + off).y),
                                   (x.z, y.z, -z.z, (c + off).z),
                                   (0, 0, 0, 1)))
        cam_data.ortho_scale = max(horizontal, vertical * 2.0, 0.2) * 1.15
        scene.render.filepath = str(RENDER_DIR / f"{name}.png")
        bpy.ops.render.render(write_still=True)

    for state in ("stow", "deployed"):
        assign_action(rig, bpy.data.actions[state])
        scene.frame_set(1)
        for axis in ("side", "top", "front"):
            shot(f"{UMBRELLA}_{state}_{axis}", [mesh], axis)
    assign_action(rig, None)
    for axis in ("side", "front"):
        shot(f"{BACK}_{axis}", [back], axis)
    bpy.data.objects.remove(cam)
    bpy.data.cameras.remove(cam_data)
    for m in markers:
        bpy.data.objects.remove(m)
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = False
    log(f"renders in {RENDER_DIR}")


NUDGEABLE = (UMBRELLA, UMBRELLA + "_rig", "muzzle", "weapon_hand_l", BACK)


def capture_nudges():
    if not AUTHORED_BLEND.exists():
        return {}
    bpy.ops.wm.open_mainfile(filepath=str(AUTHORED_BLEND))
    nudges = {}
    for name in NUDGEABLE:
        o = bpy.data.objects.get(name)
        if o is not None and o.matrix_basis != Matrix.Identity(4):
            nudges[name] = o.matrix_basis.copy()
    if nudges:
        log(f"carrying nudges for {sorted(nudges)}")
    return nudges


def stage_build():
    nudges = capture_nudges()
    bpy.ops.wm.open_mainfile(filepath=str(RAW_BLEND))
    scene = bpy.context.scene
    src_arm = bpy.data.objects[SRC_ARMATURE]
    # the FBX importer's 0.01 sits on top of ModelConverter's own /100: the
    # data is in metres already
    src_arm.scale = (1, 1, 1)
    assign_action(src_arm, None)
    for pb in src_arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    scene.frame_set(1)
    bpy.context.view_layer.update()
    src_mesh = bpy.data.objects[SRC_MESH]

    bones = subtree(src_arm, ROOT_BONE)
    names = [b.name for b in bones]
    bind = src_arm.matrix_world @ src_arm.data.bones[ROOT_BONE].matrix_local
    world_to_umb = bind.inverted()

    # extent along the shaft, from the open mesh at bind
    ys = [(world_to_umb @ (src_mesh.matrix_world @ v.co)).y for v in src_mesh.data.vertices]
    shaft_lo, shaft_hi = min(ys), max(ys)
    log(f"umbrella along its shaft: {shaft_lo:.3f} .. {shaft_hi:.3f} m from the Weapon_02 head")

    # The right hand: where her palm sits on the handle in the open hold of
    # the ult, read in the shaft's frame.
    src_act = bpy.data.actions[SRC_CLIP]
    assign_action(src_arm, src_act)
    segments = detect_segments(src_arm, names, int(src_act.frame_range[0]), int(src_act.frame_range[1]))
    hold = segments[0][1] + 5
    scene.frame_set(hold)
    pb = src_arm.pose.bones
    shaft = src_arm.matrix_world @ pb[ROOT_BONE].matrix
    loc, rot, _ = shaft.decompose()
    shaft = Matrix.LocRotScale(loc, rot, Vector((1, 1, 1)))
    palm_world = src_arm.matrix_world @ ((pb["Wrist_R"].head + pb["MiddleFinger1_R"].head) / 2)
    palm = shaft.inverted() @ palm_world
    # the grip lies on the shaft: keep the palm's station along it, drop the
    # sideways offset of the palm's centre from the shaft line
    hand = Vector((0.0, palm.y, 0.0))
    log(f"palm in the shaft frame at frame {hold}: {tuple(round(v, 3) for v in palm)}; grip at y {hand.y:.3f}")
    assign_action(src_arm, None)
    for p in src_arm.pose.bones:
        p.matrix_basis = Matrix.Identity(4)
    scene.frame_set(1)
    bpy.context.view_layer.update()

    umb_to_weapon = (Matrix.Translation(GRIP) @ HOLD_ROT
                     @ Matrix.Translation(-(UMB_TO_WEAPON @ hand)) @ UMB_TO_WEAPON)
    world_to_weapon = umb_to_weapon @ world_to_umb

    root = make_empty(UMBRELLA + "_root", None, (0, 0, 0), display="PLAIN_AXES")
    rig = build_rig(src_arm, bones, UMBRELLA + "_rig", world_to_weapon)
    rig.parent = root
    mesh = transform_mesh_copy(src_mesh, UMBRELLA, world_to_weapon)
    mesh.parent = root
    mesh.modifiers.new("Armature", "ARMATURE").object = rig
    make_empty("muzzle", root, umb_to_weapon @ Vector((0.0, shaft_hi, 0.0)))
    make_empty("weapon_hand_l", root, LEFT_HAND, LEFT_HAND_QUAT)

    open_frame = bake_clips(src_arm, rig, names)
    err = verify_transfer(src_arm, src_mesh, rig, mesh, world_to_weapon, bind, names, open_frame)
    if err > 0.002:
        raise SystemExit(f"rig transfer error {err * 1000:.1f} mm, refusing to continue")

    # Back item: the folded umbrella as a static mesh, handle up and tip down
    # along the spine, centred on the socket.
    back_root = make_empty(BACK + "_root", None, (0, 0, 0), display="PLAIN_AXES")
    assign_action(rig, bpy.data.actions["stow"])
    scene.frame_set(1)
    deps = bpy.context.evaluated_depsgraph_get()
    folded = bpy.data.meshes.new_from_object(mesh.evaluated_get(deps))
    assign_action(rig, None)
    # weapon frame -> worn: undo the hold, then the shaft (-Y forward before
    # the hold, so +Y is the handle end) stands up along +Z with the handle on top
    stand = BACK_LEAN @ Matrix.Rotation(math.radians(90), 4, "X") @ HOLD_ROT.inverted()
    pts = [stand @ v.co for v in folded.vertices]
    mn = Vector([min(p[i] for p in pts) for i in range(3)])
    mx = Vector([max(p[i] for p in pts) for i in range(3)])
    centre = (mn + mx) / 2
    to_back = Matrix.Translation(Vector((0, 0, BACK_CENTRE_HEIGHT)) - BACK_YAW @ centre) @ BACK_YAW @ stand
    folded.transform(to_back)
    folded.name = BACK
    back = link(bpy.data.objects.new(BACK, folded))
    back.parent = back_root
    back_root.location = (1.5, 0.0, 0.0)  # parked beside the umbrella in the blend
    log(f"back item: {len(folded.vertices)} verts, folded length {(mx - mn).z:.3f} m")

    keep = {root, rig, mesh, back_root, back} | {o for o in bpy.data.objects if o.parent is root}
    for o in list(bpy.data.objects):
        if o not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    for act in list(bpy.data.actions):
        if act.name not in CLIPS:
            bpy.data.actions.remove(act)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials):
        for datablock in list(block):
            if datablock.users == 0:
                block.remove(datablock)

    for name, basis in nudges.items():
        if name in bpy.data.objects:
            bpy.data.objects[name].matrix_basis = basis

    render_diagnostics(rig, mesh, back)
    for o in bpy.data.objects:
        o.select_set(o is rig)
    bpy.context.view_layer.objects.active = rig
    act = bpy.data.actions["deploy"]
    assign_action(rig, act)
    scene.frame_start, scene.frame_end = int(act.frame_range[0]), int(act.frame_range[1])
    scene.frame_set(scene.frame_start)
    bpy.ops.wm.save_as_mainfile(filepath=str(AUTHORED_BLEND))
    log(f"saved {AUTHORED_BLEND}")


# --------------------------------------------------------------------------
# export stage
# --------------------------------------------------------------------------

TRIO = tuple(TEX_SRC / f"c_WelrodSSR01_slg_weapon_{s}.png" for s in ("d", "n", "rmo"))

# The weapon's Animator contract, consumed by BakeWeapon -controller. MENACE
# forwards every soldier animator parameter to each attachment Animator that
# declares it. The umbrella is its own state, not the squad's Deployed stance
# (it cannot be opened while deployed), so it keys off a bool of its own that
# the open and close skills set.
OPEN_PARAM = "UmbrellaOpen"
CONTROLLER = {
    "parameters": [{"name": OPEN_PARAM, "type": "Bool"}],
    "states": [
        {"name": "Stowed", "clip": "stow", "loop": True, "default": True},
        {"name": "Deploying", "clip": "deploy"},
        {"name": "Deployed", "clip": "deployed", "loop": True},
        {"name": "Stowing", "clip": "stowing"},
    ],
    "transitions": [
        {"from": "Stowed", "to": "Deploying", "conditions": [[OPEN_PARAM, "If"]]},
        {"from": "Deploying", "to": "Deployed", "exitTime": 1.0},
        {"from": "Deploying", "to": "Stowing", "conditions": [[OPEN_PARAM, "IfNot"]]},
        {"from": "Deployed", "to": "Stowing", "conditions": [[OPEN_PARAM, "IfNot"]]},
        {"from": "Stowing", "to": "Stowed", "exitTime": 1.0},
        {"from": "Stowing", "to": "Deploying", "conditions": [[OPEN_PARAM, "If"]]},
    ],
}


def export_glb(root, path, animations):
    for o in bpy.data.objects:
        o.select_set(False)
    stack = [root]
    while stack:
        o = stack.pop()
        o.select_set(True)
        stack.extend(o.children)
    path.parent.mkdir(parents=True, exist_ok=True)
    kwargs = dict(filepath=str(path), export_format="GLB", use_selection=True,
                  export_image_format="NONE", export_apply=True, export_attributes=False,
                  export_yup=True, export_extras=False, export_skins=animations,
                  export_morph=False, export_animations=animations)
    if animations:
        kwargs.update(export_animation_mode="NLA_TRACKS", export_frame_range=False,
                      export_force_sampling=True, export_bake_animation=False,
                      export_optimize_animation_size=False, export_anim_single_armature=True,
                      export_reset_pose_bones=True)
    bpy.ops.export_scene.gltf(**kwargs)
    log(f"wrote {path}")


def pin_node_rotation(glb_path, node_name, quat):
    """Blender's exporter can negate an empty's x and w on the way out (the
    off-hand grip bug), so the intended glTF quaternion is written back."""
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import fix_hand_grip
    gltf, binary = fix_hand_grip.read_glb(str(glb_path))
    hit = [n for n in gltf["nodes"] if n.get("name") == node_name]
    if len(hit) != 1:
        raise SystemExit(f"{glb_path}: expected one node named {node_name}, found {len(hit)}")
    hit[0]["rotation"] = [float(v) for v in quat]
    fix_hand_grip.write_glb(str(glb_path), gltf, binary)


def stage_export():
    bpy.ops.wm.open_mainfile(filepath=str(AUTHORED_BLEND))
    root = bpy.data.objects[UMBRELLA + "_root"]
    back_root = bpy.data.objects[BACK + "_root"]
    assign_action(bpy.data.objects[UMBRELLA + "_rig"], None)
    saved = back_root.location.copy()
    back_root.location = (0, 0, 0)
    export_glb(root, AUTHORED / UMBRELLA / "raw.glb", animations=True)
    pin_node_rotation(AUTHORED / UMBRELLA / "raw.glb", "weapon_hand_l", LEFT_HAND_GLTF_QUAT)
    export_glb(back_root, AUTHORED / BACK / "raw.glb", animations=False)
    back_root.location = saved
    for name in (UMBRELLA, BACK):
        tex = AUTHORED / name / "textures"
        tex.mkdir(parents=True, exist_ok=True)
        for src in TRIO:
            shutil.copyfile(src, tex / src.name)
    (AUTHORED / UMBRELLA / "controller.json").write_text(json.dumps(CONTROLLER, indent=2) + "\n")
    log(f"wrote {AUTHORED / UMBRELLA / 'controller.json'}")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--stage", choices=("raw", "build", "export"), required=True)
    args = parser.parse_args(argv)
    {"raw": stage_raw, "build": stage_build, "export": stage_export}[args.stage]()


if __name__ == "__main__":
    main()
