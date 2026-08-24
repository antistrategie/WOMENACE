#!/usr/bin/env python3
"""Build Asteria's railgun (and its back bracket) from her GFL2 battle rig.

GFL2 has no weapon prefab for the railgun: `c_AsteriaSSR01_slg_Gun_lod0` and
`c_AsteriaSSR01_slg_Bracket_lod0` are skinned children of her character rig,
and the gun's moving parts are driven by her own combat clips through 26
`Weapon_*` bones under `Weapon_L020`. This script lifts that subtree out into a
standalone weapon rig, re-expresses the mesh and the bone rest poses in the
gun's own frame, and bakes gun-local clips out of the ultimate-skill clip,
which is the only clip that deploys the gun.

Two stages, with a .blend checkpoint between them for inspection:

  blender -b --python scripts/weapon/build_asteria_railgun.py -- --stage build
      Asteria_SSR01_raw.blend (the VoyExport FBX, imported once) ->
      asteria_railgun_authored.blend + diagnostic renders.

  blender -b --python scripts/weapon/build_asteria_railgun.py -- --stage export
      asteria_railgun_authored.blend ->
      unity/Assets/Authored/weapon/asteria_railgun/{raw.glb,controller.json,textures/}
      unity/Assets/Authored/weapon/asteria_railgun_bracket/{raw.glb,textures/}
  then bake both through the usual weapon route, which picks the controller up:
      python3 scripts/weapon/shade_weapons.py --bake asteria_railgun asteria_railgun_bracket

The authored blend carries the weapon-pipeline objects: a `<name>_root` empty
with the mesh, the rig and the `muzzle` / `weapon_hand_l` attach-point empties
as siblings. Nudge the empties (or the mesh, for the right-hand grip) in the
authored blend and re-run the export stage; the build stage regenerates
everything from the raw rig.

Frame of reference (Blender, before the glTF Y-up swap): muzzle along -Y, up
along +Z, the right hand at the origin. The gun's own frame is `Weapon_L020`
at bind: +X runs from the rear block to the muzzle, +Y is up as she holds it
(measured off the ult: +Y points at the sky through the whole deploy and
beam, so the jaws open up and down), +Z is the side away from her body, the
bracket mounting on the -Z face against her back.

Clips, sampled off `c_AsteriaSSR01_slg_UltraSkill` at 60 fps. The ult is 6.6 s
of body motion around three short bursts of gun-local motion; the bursts are
concatenated and the body motion (the root bone's swing) dropped:
  stow      the closed box, cables latched (frame 1), held.
  deploy    cables unlatch and vanish (36-49), the front assembly slides out
            of the rear block (126-146), the cover flips and the side plates
            and rails spread (166-206).
  deployed  the open configuration (frame 206), held.
  stowing   deploy, reversed.
  fire      deployed plus a recoil of the front assembly along its own slide
            axis; the ult has no per-shot gun motion, so this is synthesised.
The four cable chains are hidden by scaling their root bones to zero where
GFL2 teleports them 40 m away.
"""

import argparse
import json
import math
import shutil
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

DOWNLOADS = Path.home() / "Downloads" / "Asteria_Animations"
RAW_BLEND = DOWNLOADS / "Asteria_SSR01_raw.blend"
AUTHORED_BLEND = DOWNLOADS / "asteria_railgun_authored.blend"
RENDER_DIR = DOWNLOADS / "renders"
PMX_DIR = Path.home() / "Downloads" / "阿斯缇亚原皮"

REPO = Path(__file__).resolve().parents[2]
UNITY = REPO / "unity"
AUTHORED = UNITY / "Assets" / "Authored" / "weapon"

SRC_ARMATURE = "root"
SRC_TOP = "c_AsteriaSSR01_slg_skin"
SRC_GUN = "c_AsteriaSSR01_slg_Gun_lod0"
SRC_BRACKET = "c_AsteriaSSR01_slg_Bracket_lod0"
SRC_ULT = "root|c_AsteriaSSR01_slg_UltraSkill|Base Layer"
GUN_ROOT_BONE = "Weapon_L020"
BRACKET_BONES = ("Weapon_MB01", "Weapon_MB02", "Weapon_MB03",
                 "Weapon_L010", "Weapon_L011", "Weapon_L012", "Weapon_L013",
                 "Weapon_R010", "Weapon_R011", "Weapon_R012", "Weapon_R013")
CHAIN_ROOTS = ("Weapon_MB035", "Weapon_MB038", "Weapon_MB041", "Weapon_MB045")

GUN = "asteria_railgun"
BRACKET = "asteria_railgun_bracket"
STOWED = "railgun_stowed"
GUN_MATERIAL = "railgun"
BRACKET_MATERIAL = "railgun_bracket"

FPS = 60
# The deploy bursts are detected from the clip (see detect_segments): frames
# where any non-root gun bone moves, merged across gaps shorter than this.
SEGMENT_MERGE_GAP = 8
SEGMENT_PADDING = 1
MOTION_EPS_M = 0.0015
MOTION_EPS_DEG = 0.4
DEPLOYED_FRAME = 206
STOW_FRAME = 1
# Source translations beyond this are GFL2's hide-by-teleport.
TELEPORT_DISTANCE = 5.0
RECOIL_FRAMES = 18
RECOIL_PEAK_FRAME = 3
RECOIL_FRACTION = 0.4  # of the deploy slide, back along it

# Where the hands hold the gun, along its own axis: the right hand just ahead
# of the rear block so the block rides behind the shoulder like the vanilla
# rocket launcher's tube (its LOD0 runs 0.9 m behind the grip), the left hand
# further out, which is where her own hands are in the ult (rear hand at
# x 0.2-0.4, front hand at 0.55-0.7). The gun has no grips, so each palm sits
# on the underside of the body at that station (read off the mesh, see
# hand_height), the slab rising above the hand the way the launcher tube does.
HAND_X = 0.45
LEFT_HAND_X = 0.80
PALM_OFFSET = 0.02
# The muzzle is the deployed front of the rails.
MUZZLE_IN_GUN = Vector((1.41, 0.0, 0.0))
# Left-hand grip orientation, copied from rocket_launcher_t1's weapon_hand_l in
# its glTF (x, y, z, w). Round-trips through the same Y-up swap bake_weapon.py
# uses, and is the usual suspect when the off-hand sits wrong in game.
LEFT_HAND_GLTF_QUAT = (-0.13420387, 0.45497218, 0.48862037, -0.73228395)
MUZZLE_SCALE = 1.2  # rocket_launcher_t1 scales its muzzle flash up too

# Gun frame (Weapon_L020 at bind) -> Blender weapon frame.
#   gun +X (muzzle) -> Blender -Y, gun +Y (up) -> Blender +Z,
#   gun +Z (outboard, away from the body) -> Blender -X (the soldier's right:
#   the weapon frame's +X is the soldier's left, where weapon_hand_l sits)
GUN_TO_WEAPON = Matrix(((0, 0, -1, 0),
                        (-1, 0, 0, 0),
                        (0, 1, 0, 0),
                        (0, 0, 0, 1)))
# Bracket: a back item is authored in the body's frame as worn, then yawed
# half a turn: the rig at bind faces Blender +Y, while the Back_Special
# socket's forward matches the soldier's (measured live: a +z nudge moved the
# assembly towards her front), so the worn pose lands outward only after the
# flip. The vanilla rocket backpack straddles its socket and rises mostly
# above it.
BRACKET_CENTRE_HEIGHT = 0.2
BACK_YAW = Matrix.Rotation(math.pi, 4, "Z")


def log(*a):
    print("[railgun]", *a, flush=True)


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


def subtree(arm, root_name):
    root = arm.data.bones[root_name]
    out = [root]
    i = 0
    while i < len(out):
        out.extend(out[i].children)
        i += 1
    return out


def link(obj):
    bpy.context.scene.collection.objects.link(obj)
    return obj


# --------------------------------------------------------------------------
# build stage
# --------------------------------------------------------------------------

def transform_mesh_copy(src_obj, name, material_name, world_to_new):
    """A standalone copy of src_obj's mesh with its bind-pose world geometry
    re-expressed through world_to_new, custom normals included."""
    me = src_obj.data.copy()
    me.name = name
    obj = bpy.data.objects.new(name, me)
    link(obj)
    mw = src_obj.matrix_world.copy()
    full = world_to_new @ mw
    rot = full.to_3x3()
    normals = [Vector(n.vector) for n in me.corner_normals]
    for v in me.vertices:
        v.co = full @ v.co
    me.normals_split_custom_set([(rot @ n).normalized() for n in normals])
    me.materials.clear()
    me.materials.append(bpy.data.materials.get(material_name)
                        or bpy.data.materials.new(material_name))
    for g in list(obj.vertex_groups):
        pass  # keep the names: the new rig's bones share them
    obj.matrix_world = Matrix.Identity(4)
    return obj


def build_rig(src_arm, bones, name, world_to_new):
    """An armature holding copies of `bones` (a subtree, root first) with rest
    matrices re-expressed through world_to_new."""
    data = bpy.data.armatures.new(name)
    rig = bpy.data.objects.new(name, data)
    link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    rest = {}
    for b in bones:
        eb = data.edit_bones.new(b.name)
        m = world_to_new @ src_arm.matrix_world @ b.matrix_local
        eb.head = (0, 0, 0)
        eb.tail = (0, b.length, 0)
        eb.matrix = m
        rest[b.name] = m.copy()
        if b.parent is not None and b.parent.name in data.edit_bones:
            eb.parent = data.edit_bones[b.parent.name]
            eb.use_connect = False
    # verify the roll survived the matrix assignment
    worst = 0.0
    for b in bones:
        eb = data.edit_bones[b.name]
        diff = max(abs(x) for row in (eb.matrix - rest[b.name]) for x in row)
        worst = max(worst, diff)
    bpy.ops.object.mode_set(mode="OBJECT")
    log(f"{name}: {len(bones)} bones, max rest-matrix error {worst:.2e}")
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return rig


def sample_source(src_arm, src_act, bone_names, frame):
    """Bone-local pose deltas of the source rig at a frame."""
    src_arm.animation_data.action = src_act
    try:
        src_arm.animation_data.action_slot = src_act.slots[0]
    except (AttributeError, IndexError):
        pass
    bpy.context.scene.frame_set(frame)
    return {n: src_arm.pose.bones[n].matrix_basis.copy() for n in bone_names}


def basis_to_trs(m):
    loc, rot, scale = m.decompose()
    return loc, rot, scale


class PoseWriter:
    """Keyframe writer with quaternion hemisphere continuity: adjacent samples
    of a rotation crossing 180 degrees land on opposite quaternion signs, and
    the glTF round-trip interpolates the two through zero, whipping the
    joint. Every key is aligned against the bone's previous one."""

    def __init__(self, rig):
        self.rig = rig
        self.last = {}

    def write(self, pose, frame):
        for n, (loc, rot, scale) in pose.items():
            prev = self.last.get(n)
            if prev is not None and prev.dot(rot) < 0:
                rot = -rot
            self.last[n] = rot.copy()
            set_key(self.rig, n, frame, loc, rot, scale)


def set_key(rig, name, frame, loc, rot, scale):
    pb = rig.pose.bones[name]
    pb.location = loc
    pb.rotation_quaternion = rot
    pb.scale = scale
    pb.keyframe_insert("location", frame=frame)
    pb.keyframe_insert("rotation_quaternion", frame=frame)
    pb.keyframe_insert("scale", frame=frame)


def new_action(rig, name):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    if rig.animation_data is None:
        rig.animation_data_create()
    rig.animation_data.action = act
    return act


def gun_local_pose(src_basis, names):
    """The source deltas made gun-local: the root bone stays put (its swing is
    body motion), a teleported chain root becomes a zero-scale hide."""
    pose = {}
    for n in names:
        if n == GUN_ROOT_BONE:
            pose[n] = (Vector((0, 0, 0)), Quaternion((1, 0, 0, 0)), Vector((1, 1, 1)))
            continue
        loc, rot, scale = basis_to_trs(src_basis[n])
        if n in CHAIN_ROOTS and loc.length > TELEPORT_DISTANCE:
            pose[n] = (Vector((0, 0, 0)), Quaternion((1, 0, 0, 0)), Vector((0, 0, 0)))
        else:
            pose[n] = (loc, rot, scale)
    return pose


def finish_action(act):
    for fc in fcurves(act):
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"


def pose_distance(a, b):
    """Largest change between two poses, in metres or degrees (whichever is
    numerically larger), ignoring the orientation of hidden bones."""
    worst = 0.0
    for n in a:
        la, ra, sa = a[n]
        lb, rb, sb = b[n]
        worst = max(worst, (sa - sb).length)
        if sa.length < 1e-6 and sb.length < 1e-6:
            continue
        worst = max(worst, (la - lb).length,
                    math.degrees(ra.rotation_difference(rb).angle))
    return worst


def pose_moved(a, b):
    for n in a:
        la, ra, sa = a[n]
        lb, rb, sb = b[n]
        if (sa - sb).length > 1e-6:
            return True
        if sa.length < 1e-6:
            continue
        if (la - lb).length > MOTION_EPS_M:
            return True
        if math.degrees(ra.rotation_difference(rb).angle) > MOTION_EPS_DEG:
            return True
    return False


def detect_segments(src_arm, src_act, names):
    """Frame windows in which the gun's own parts move. The ult is mostly
    body motion with the gun rigid between three bursts."""
    f0, f1 = int(src_act.frame_range[0]), int(src_act.frame_range[1])
    prev = None
    moving = []
    for f in range(f0, f1 + 1):
        pose = gun_local_pose(sample_source(src_arm, src_act, names, f), names)
        if prev is not None and pose_moved(prev, pose):
            moving.append(f)
        prev = pose
    runs = []
    for f in moving:
        if runs and f - runs[-1][1] <= SEGMENT_MERGE_GAP:
            runs[-1][1] = f
        else:
            runs.append([f, f])
    segments = [(max(f0, a - SEGMENT_PADDING), min(f1, b + SEGMENT_PADDING)) for a, b in runs]
    log(f"motion windows in {src_act.name}: {segments}")
    return segments


def bake_gun_clips(src_arm, rig, names):
    src_act = bpy.data.actions[SRC_ULT]
    scene = bpy.context.scene
    scene.render.fps = FPS

    stow = gun_local_pose(sample_source(src_arm, src_act, names, STOW_FRAME), names)
    deployed = gun_local_pose(sample_source(src_arm, src_act, names, DEPLOYED_FRAME), names)
    segments = detect_segments(src_arm, src_act, names)
    if not segments:
        raise SystemExit("no gun motion found in the ult clip")
    last_pose = gun_local_pose(sample_source(src_arm, src_act, names, segments[-1][1]), names)
    log(f"deploy end vs deployed frame {DEPLOYED_FRAME}: max gap {pose_distance(last_pose, deployed):.4f}")

    # deploy: the bursts back to back. Continuity between bursts is checked
    # rather than assumed: the bones that move in one burst sit still through
    # the others.
    act = new_action(rig, "deploy")
    writer = PoseWriter(rig)
    t = 1
    deploy_frames = []
    prev = None
    for (a, b) in segments:
        for f in range(a, b + 1):
            pose = gun_local_pose(sample_source(src_arm, src_act, names, f), names)
            if prev is not None and f == a:
                gap = pose_distance(prev, pose)
                log(f"deploy: seam {a} joins with max pose gap {gap:.4f}")
            writer.write(pose, t)
            deploy_frames.append((t, pose))
            prev = pose
            t += 1
    deploy_len = t - 1
    finish_action(act)
    log(f"deploy: {deploy_len} frames ({deploy_len / FPS:.2f} s)")

    act = new_action(rig, "stowing")
    writer = PoseWriter(rig)
    for i, (_, pose) in enumerate(reversed(deploy_frames)):
        writer.write(pose, i + 1)
    finish_action(act)

    act = new_action(rig, "stow")
    writer = PoseWriter(rig)
    writer.write(stow, 1)
    writer.write(stow, 2)
    finish_action(act)

    act = new_action(rig, "deployed")
    writer = PoseWriter(rig)
    writer.write(deployed, 1)
    writer.write(deployed, 2)
    finish_action(act)

    # fire: recoil along each part's own slide axis, read off the slide burst
    # so no bone-space axis is guessed.
    # the slide burst is the one with the largest translation of a rail bone
    def burst_slide(seg):
        a = sample_source(src_arm, src_act, names, seg[0])
        b = sample_source(src_arm, src_act, names, seg[1])
        return max((b[n].to_translation() - a[n].to_translation()).length
                   for n in names if n != GUN_ROOT_BONE and n not in CHAIN_ROOTS
                   and not n.startswith("Weapon_MB"))
    slide_seg = max(segments, key=burst_slide)
    before = sample_source(src_arm, src_act, names, slide_seg[0])
    after = sample_source(src_arm, src_act, names, slide_seg[1])
    slide = {}
    for n in names:
        if n == GUN_ROOT_BONE or n.startswith("Weapon_MB"):
            continue
        d = after[n].to_translation() - before[n].to_translation()
        if d.length > 0.05:
            slide[n] = d
    log(f"fire: recoil on {len(slide)} sliding parts, slide {next(iter(slide.values())).length:.3f} m")
    act = new_action(rig, "fire")
    writer = PoseWriter(rig)
    for i in range(RECOIL_FRAMES + 1):
        if i <= RECOIL_PEAK_FRAME:
            k = i / RECOIL_PEAK_FRAME
        else:
            u = (i - RECOIL_PEAK_FRAME) / (RECOIL_FRAMES - RECOIL_PEAK_FRAME)
            k = 1.0 - (1.0 - math.cos(u * math.pi)) / 2.0
        pose = {}
        for n, (loc, rot, scale) in deployed.items():
            if n in slide:
                loc = loc - slide[n] * (RECOIL_FRACTION * k)
            pose[n] = (loc, rot, scale)
        writer.write(pose, i + 1)
    finish_action(act)

    rig.animation_data.action = None
    # one NLA track per clip, muted, so the glTF exporter sees them all by name
    for act_name in ("stow", "deploy", "deployed", "stowing", "fire"):
        act = bpy.data.actions[act_name]
        track = rig.animation_data.nla_tracks.new()
        track.name = act_name
        strip = track.strips.new(act_name, 1, act)
        try:
            strip.action_slot = act.slots[0]
        except (AttributeError, IndexError):
            pass
        track.mute = True
    return deploy_len


def verify_transfer(src_arm, src_gun, rig, gun, world_to_new, bind_root, names):
    """The new rig must deform the gun exactly as the source rig does: compare
    the deployed pose vertex by vertex."""
    scene = bpy.context.scene
    src_act = bpy.data.actions[SRC_ULT]
    src_arm.animation_data.action = src_act
    try:
        src_arm.animation_data.action_slot = src_act.slots[0]
    except (AttributeError, IndexError):
        pass
    rig.animation_data.action = bpy.data.actions["deployed"]
    try:
        rig.animation_data.action_slot = bpy.data.actions["deployed"].slots[0]
    except (AttributeError, IndexError):
        pass
    scene.frame_set(DEPLOYED_FRAME)
    deps = bpy.context.evaluated_depsgraph_get()
    src_ev = src_gun.evaluated_get(deps).data
    new_ev = gun.evaluated_get(deps).data
    # the source root bone has swung with her body by now: compare in the
    # root's frame at this instant, which is what the new rig holds still
    # (the armature object itself carries root motion in the clip, so its
    # matrix_world here is not the bind-time one: bind_root comes from the caller)
    posed_root = src_arm.matrix_world @ src_arm.pose.bones[GUN_ROOT_BONE].matrix
    world_to_new = world_to_new @ bind_root @ posed_root.inverted()
    # bone level first: the rig must agree before the skin can
    worst_bone = 0.0
    for n in names:
        if n.startswith("Weapon_MB"):
            continue
        a = world_to_new @ (src_arm.matrix_world @ src_arm.pose.bones[n].matrix)
        b = rig.matrix_world @ rig.pose.bones[n].matrix
        worst_bone = max(worst_bone, (a.to_translation() - b.to_translation()).length)
    log(f"transfer check: root swing {(posed_root.to_translation() - bind_root.to_translation()).length:.3f} m, "
        f"max bone head error {worst_bone * 1000:.2f} mm, "
        f"rig L021 loc {tuple(round(v, 3) for v in rig.pose.bones['Weapon_L021'].location)}")
    worst = 0.0
    chain_verts = set()
    for v in gun.data.vertices:
        if any(gun.vertex_groups[g.group].name in CHAIN_ROOTS
               or gun.vertex_groups[g.group].name.startswith("Weapon_MB") for g in v.groups):
            chain_verts.add(v.index)
    for i in range(len(src_ev.vertices)):
        if i in chain_verts:
            continue
        a = world_to_new @ (src_gun.matrix_world @ src_ev.vertices[i].co)
        b = gun.matrix_world @ new_ev.vertices[i].co
        worst = max(worst, (a - b).length)
    log(f"transfer check at frame {DEPLOYED_FRAME}: max vertex error {worst * 1000:.2f} mm "
        f"({len(chain_verts)} cable verts skipped)")
    # frame 1 of deployed on the new rig is the source's frame 206; put the
    # scene back on frame 1 so the blend opens on the stowed look
    rig.animation_data.action = None
    src_arm.animation_data.action = None
    scene.frame_set(1)
    return worst


def assign_action(rig, act):
    rig.animation_data.action = act
    try:
        rig.animation_data.action_slot = act.slots[0]
    except (AttributeError, IndexError):
        pass


def present_for_inspection(rig):
    """The blend opens on the rig with `deploy` as its active action, so the
    Dope Sheet and the timeline show the clip straight away. The other clips
    sit in the (muted) NLA tracks and the Action Editor's dropdown; the
    export stage reads the tracks, not the active action."""
    for o in bpy.data.objects:
        o.select_set(o is rig)
    bpy.context.view_layer.objects.active = rig
    act = bpy.data.actions["deploy"]
    assign_action(rig, act)
    scene = bpy.context.scene
    scene.frame_start = int(act.frame_range[0])
    scene.frame_end = int(act.frame_range[1])
    scene.frame_set(scene.frame_start)


def make_root(name):
    root = bpy.data.objects.new(name, None)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.1
    return link(root)


def make_empty(name, parent, location, quat=None, scale=1.0):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "ARROWS"
    e.empty_display_size = 0.05
    e.location = location
    e.rotation_mode = "QUATERNION"
    if quat is not None:
        e.rotation_quaternion = quat
    e.scale = (scale, scale, scale)
    e.parent = parent
    return link(e)


def render_diagnostics(rig, gun, bracket, stowed):
    """Workbench renders of the stowed and deployed gun and the bracket."""
    RENDER_DIR.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "SINGLE"
    scene.display.shading.single_color = (0.55, 0.55, 0.6)
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 700
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("diag_world")
    world.color = (0.9, 0.9, 0.9)
    scene.world = world
    cam_data = bpy.data.cameras.new("diag_cam")
    cam_data.type = "ORTHO"
    cam = link(bpy.data.objects.new("diag_cam", cam_data))
    scene.camera = cam

    def bounds(objs):
        deps = bpy.context.evaluated_depsgraph_get()
        pts = []
        for o in objs:
            ev = o.evaluated_get(deps)
            for v in ev.data.vertices:
                p = o.matrix_world @ v.co
                if p.length < 5:
                    pts.append(p)
        mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        return mn, mx

    # attach points and the grip origin as small spheres, so the renders show
    # where the hands and the muzzle flash land
    markers = []
    for o in bpy.data.objects:
        if o.type == "EMPTY" and o.name in ("muzzle", "weapon_hand_l"):
            bpy.ops.mesh.primitive_uv_sphere_add(radius=0.03, location=o.matrix_world.to_translation())
            m = bpy.context.active_object
            m.name = "marker_" + o.name
            markers.append(m)
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.03, location=(0, 0, 0))
    origin = bpy.context.active_object
    origin.name = "marker_hand_r"
    markers.append(origin)

    def shot(name, objs, axis):
        shown = list(objs) + (markers if gun in objs else [])
        for o in bpy.data.objects:
            if o.type == "MESH":
                o.hide_render = o not in shown
        mn, mx = bounds(objs)
        c = (mn + mx) / 2
        # ortho_scale spans the wider image axis; the frame is 2:1, so the
        # vertical extent needs twice the room
        ext = mx - mn
        horizontal = {"side": ext.y, "top": ext.y, "front": ext.x}[axis]
        vertical = {"side": ext.z, "top": ext.x, "front": ext.z}[axis]
        size = max(horizontal, vertical * 2.0)
        # the long axis (Blender Y) runs across the landscape frame in every view
        offs = {"side": (Vector((4, 0, 0)), Vector((0, 0, 1))),
                "top": (Vector((0, 0, 4)), Vector((1, 0, 0))),
                "front": (Vector((0, -4, 0)), Vector((0, 0, 1)))}[axis]
        off, up = offs
        z = (-off).normalized()
        x = up.cross(z).normalized()
        y = z.cross(x)
        cam.matrix_world = Matrix(((x.x, y.x, -z.x, (c + off).x),
                                   (x.y, y.y, -z.y, (c + off).y),
                                   (x.z, y.z, -z.z, (c + off).z),
                                   (0, 0, 0, 1)))
        cam_data.ortho_scale = size * 1.15
        scene.render.filepath = str(RENDER_DIR / f"{name}.png")
        bpy.ops.render.render(write_still=True)

    for state in ("stow", "deployed"):
        act = bpy.data.actions[state]
        rig.animation_data.action = act
        try:
            rig.animation_data.action_slot = act.slots[0]
        except (AttributeError, IndexError):
            pass
        scene.frame_set(1)
        for axis in ("side", "top", "front"):
            shot(f"{GUN}_{state}_{axis}", [gun], axis)
    rig.animation_data.action = None
    for axis in ("side", "top", "front"):
        shot(f"{BRACKET}_{axis}", [bracket, stowed], axis)
    bpy.data.objects.remove(cam)
    bpy.data.cameras.remove(cam_data)
    for m in markers:
        bpy.data.objects.remove(m)
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = False
    log(f"renders in {RENDER_DIR}")


NUDGEABLE = (GUN, GUN + "_rig", "muzzle", "weapon_hand_l")


def capture_nudges():
    """The user's hand adjustments in the authored blend are the local
    transforms of the gun root's children (all created at identity by the
    build). Read them before a rebuild so they survive it."""
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
    top = bpy.data.objects[SRC_TOP]
    # The FBX importer's 0.01 sits on top of ModelConverter's own /100: the
    # data is in metres already.
    top.scale = (1, 1, 1)
    bpy.context.view_layer.update()
    src_gun = bpy.data.objects[SRC_GUN]
    src_bracket = bpy.data.objects[SRC_BRACKET]

    # rest pose for the bind-frame maths
    src_arm.animation_data.action = None
    scene.frame_set(1)
    bpy.context.view_layer.update()

    gun_bones = subtree(src_arm, GUN_ROOT_BONE)
    gun_names = [b.name for b in gun_bones]
    bind = src_arm.matrix_world @ src_arm.data.bones[GUN_ROOT_BONE].matrix_local
    world_to_gun = bind.inverted()
    # the rigid body only: the cover's cloth hangs below the slab at the rear
    group_names = [g.name for g in src_gun.vertex_groups]
    gun_pts = []
    for v in src_gun.data.vertices:
        dominant = group_names[max(v.groups, key=lambda g: g.weight).group]
        if not dominant.startswith("Weapon_MB"):
            gun_pts.append(world_to_gun @ (src_gun.matrix_world @ v.co))

    def hand_height(x):
        """The gun's underside (its -Y face) around station x, less a palm's
        thickness so the hand cups the slab instead of sitting inside it. The
        front half is ribbed, so the band is wide enough to catch a rib."""
        band = [p.y for p in gun_pts if abs(p.x - x) < 0.1 and abs(p.z) < 0.07]
        return min(band) - PALM_OFFSET

    hand_in_gun = Vector((HAND_X, hand_height(HAND_X), 0.0))
    left_hand_in_gun = Vector((LEFT_HAND_X, hand_height(LEFT_HAND_X), 0.0))
    log(f"hands in the gun frame: right {tuple(round(v, 3) for v in hand_in_gun)}, "
        f"left {tuple(round(v, 3) for v in left_hand_in_gun)}")
    gun_to_weapon = Matrix.Translation(-(GUN_TO_WEAPON @ hand_in_gun)) @ GUN_TO_WEAPON
    world_to_weapon = gun_to_weapon @ world_to_gun

    # --- gun ---------------------------------------------------------------
    gun_root = make_root(GUN + "_root")
    rig = build_rig(src_arm, gun_bones, GUN + "_rig", world_to_weapon)
    rig.parent = gun_root
    gun = transform_mesh_copy(src_gun, GUN, GUN_MATERIAL, world_to_weapon)
    gun.parent = gun_root
    mod = gun.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    make_empty("muzzle", gun_root, gun_to_weapon @ MUZZLE_IN_GUN, scale=MUZZLE_SCALE)
    make_empty("weapon_hand_l", gun_root, gun_to_weapon @ left_hand_in_gun,
               gltf_quat_to_blender(LEFT_HAND_GLTF_QUAT))

    bake_gun_clips(src_arm, rig, gun_names)
    err = verify_transfer(src_arm, src_gun, rig, gun, world_to_weapon, bind, gun_names)
    if err > 0.002:
        raise SystemExit(f"rig transfer error {err * 1000:.1f} mm, refusing to continue")

    # --- bracket -----------------------------------------------------------
    # The mount is rigid on the back; its bones only sway with her spine. It
    # ships as a static back item in the body's frame as worn, centred on the
    # back socket.
    brk_root = make_root(BRACKET + "_root")
    pts = [BACK_YAW @ (src_bracket.matrix_world @ v.co) for v in src_bracket.data.vertices]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre = (mn + mx) / 2
    world_to_back = Matrix.Translation(Vector((0, 0, BRACKET_CENTRE_HEIGHT)) - centre) @ BACK_YAW
    bracket = transform_mesh_copy(src_bracket, BRACKET, BRACKET_MATERIAL, world_to_back)
    bracket.parent = brk_root
    for g in list(bracket.vertex_groups):
        bracket.vertex_groups.remove(g)
    # the gun as worn: a static bind-pose copy (closed, cover on) resting on
    # the bracket, its own object so the runtime can hide just the gun while
    # the squad stands and reveal the hand-held one on deploy
    stowed = transform_mesh_copy(src_gun, STOWED, GUN_MATERIAL, world_to_back)
    stowed.parent = brk_root
    for g in list(stowed.vertex_groups):
        stowed.vertex_groups.remove(g)
    brk_root.location = (2.0, 0.0, 0.0)  # parked beside the gun in the blend
    log(f"bracket: {len(bracket.data.vertices)} verts, size "
        f"{tuple(round(v, 3) for v in (mx - mn))} m (x across, y depth, z up)")

    # --- tidy: drop the source rig and everything that is not ours ----------
    keep = {gun_root, rig, gun, brk_root, bracket, stowed} | {
        o for o in bpy.data.objects if o.parent in (gun_root,)}
    for o in list(bpy.data.objects):
        if o not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    for act in list(bpy.data.actions):
        if act.name not in ("stow", "deploy", "deployed", "stowing", "fire"):
            bpy.data.actions.remove(act)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials):
        for datablock in list(block):
            if datablock.users == 0:
                block.remove(datablock)
    scene.frame_start = 1
    scene.frame_end = max(int(a.frame_range[1]) for a in bpy.data.actions)

    for name, basis in nudges.items():
        bpy.data.objects[name].matrix_basis = basis

    render_diagnostics(rig, gun, bracket, stowed)
    present_for_inspection(rig)
    AUTHORED_BLEND.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(AUTHORED_BLEND))
    log(f"saved {AUTHORED_BLEND}")


# --------------------------------------------------------------------------
# export stage
# --------------------------------------------------------------------------

GUN_TRIO = (PMX_DIR / "Textures" / "c_AsteriaSSR01_slg_Gun_d.png",
            PMX_DIR / "normalmap" / "c_AsteriaSSR01_slg_Gun_n.png",
            PMX_DIR / "normalmap" / "c_AsteriaSSR01_slg_Gun_rmo.png")
BRACKET_TRIO = (PMX_DIR / "Textures" / "c_AsteriaSSR01_slg_Bracket_d.png",
                PMX_DIR / "normalmap" / "c_AsteriaSSR01_slg_Bracket_n.png",
                PMX_DIR / "normalmap" / "c_AsteriaSSR01_slg_Bracket_rmo.png")
# The gun dir has one material, so its trio sits flat in textures/. The back
# item carries two (bracket + stowed gun), so each material's trio lives in
# textures/<material>/, the layout shade_weapons.py reads for a multi-material
# weapon.
TEXTURES = {
    GUN: {None: GUN_TRIO},
    BRACKET: {BRACKET_MATERIAL: BRACKET_TRIO, GUN_MATERIAL: GUN_TRIO},
}

# The weapon's Animator contract, consumed by BakeWeapon -controller. MENACE
# forwards every soldier animator parameter to each attachment Animator that
# declares it, so Stance (0 default, 1 deployed, 2 pinned) arrives with no mod
# code. The fire clip triggers on CustomSkillEffect, which only the railgun
# skill raises (a SetAnimatorTrigger handler): the squad's rifle volleys
# forward Shoot_Single to every attachment, and the railgun must not cycle
# its fire animation on those.
CONTROLLER = {
    "parameters": [
        {"name": "Stance", "type": "Int"},
        {"name": "CustomSkillEffect", "type": "Trigger"},
    ],
    "states": [
        {"name": "Stowed", "clip": "stow", "loop": True, "default": True},
        {"name": "Deploying", "clip": "deploy"},
        {"name": "Deployed", "clip": "deployed", "loop": True},
        {"name": "Fire", "clip": "fire"},
        {"name": "Stowing", "clip": "stowing"},
    ],
    "transitions": [
        {"from": "Stowed", "to": "Deploying", "conditions": [["Stance", "Equals", 1]]},
        {"from": "Deploying", "to": "Deployed", "exitTime": 1.0},
        {"from": "Deploying", "to": "Stowing", "conditions": [["Stance", "NotEqual", 1]]},
        {"from": "Deployed", "to": "Fire", "conditions": [["CustomSkillEffect", "Trigger"]]},
        {"from": "Fire", "to": "Deployed", "exitTime": 1.0},
        {"from": "Deployed", "to": "Stowing", "conditions": [["Stance", "NotEqual", 1]]},
        {"from": "Stowing", "to": "Stowed", "exitTime": 1.0},
        {"from": "Stowing", "to": "Deploying", "conditions": [["Stance", "Equals", 1]]},
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
    kwargs = dict(
        filepath=str(path),
        export_format="GLB",
        use_selection=True,
        export_image_format="NONE",
        export_apply=True,
        export_attributes=False,
        export_yup=True,
        export_extras=False,
        export_skins=animations,
        export_morph=False,
        export_animations=animations,
    )
    if animations:
        kwargs.update(
            export_animation_mode="NLA_TRACKS",
            export_frame_range=False,
            export_force_sampling=True,
            export_bake_animation=False,
            export_optimize_animation_size=False,
            export_anim_single_armature=True,
            export_reset_pose_bones=True,
        )
    bpy.ops.export_scene.gltf(**kwargs)
    log(f"wrote {path}")


def pin_node_rotation(glb_path, node_name, quat):
    """Write the intended glTF quaternion onto a node. Blender's exporter
    negates the x and w of an empty's rotation on some round-trips (the
    off-hand grip bug), so the authored value is pinned rather than trusted."""
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import fix_hand_grip
    gltf, binary = fix_hand_grip.read_glb(str(glb_path))
    hit = [n for n in gltf["nodes"] if n.get("name") == node_name]
    if len(hit) != 1:
        raise SystemExit(f"{glb_path}: expected one node named {node_name}, found {len(hit)}")
    before = hit[0].get("rotation", [0, 0, 0, 1])
    hit[0]["rotation"] = [float(v) for v in quat]
    fix_hand_grip.write_glb(str(glb_path), gltf, binary)
    log(f"{node_name}: rotation {[round(v, 4) for v in before]} -> {[round(v, 4) for v in quat]}")


def copy_textures(name):
    """The Sunborn d / n / rmo trios beside the GLB, where shade_weapons.py
    finds them by suffix: flat for one material, one subdir per material
    otherwise."""
    multi = None not in TEXTURES[name]
    for material, trio in TEXTURES[name].items():
        tex = AUTHORED / name / "textures"
        if material is not None:
            tex = tex / material
        tex.mkdir(parents=True, exist_ok=True)
        for src in trio:
            shutil.copyfile(src, tex / src.name)
    if multi:
        # a flat trio left over from a single-material layout would shadow
        # the per-material subdirs in shade_weapons' suffix search
        for stray in (AUTHORED / name / "textures").glob("*.*"):
            stray.unlink()
    log(f"textures in {AUTHORED / name / 'textures'}")


def stage_export():
    bpy.ops.wm.open_mainfile(filepath=str(AUTHORED_BLEND))
    gun_root = bpy.data.objects[GUN + "_root"]
    brk_root = bpy.data.objects[BRACKET + "_root"]
    rig = bpy.data.objects[GUN + "_rig"]
    rig.animation_data.action = None
    saved = brk_root.location.copy()
    brk_root.location = (0, 0, 0)
    export_glb(gun_root, AUTHORED / GUN / "raw.glb", animations=True)
    pin_node_rotation(AUTHORED / GUN / "raw.glb", "weapon_hand_l", LEFT_HAND_GLTF_QUAT)
    export_glb(brk_root, AUTHORED / BRACKET / "raw.glb", animations=False)
    brk_root.location = saved
    copy_textures(GUN)
    copy_textures(BRACKET)
    (AUTHORED / GUN / "controller.json").write_text(json.dumps(CONTROLLER, indent=2) + "\n")
    log(f"wrote {AUTHORED / GUN / 'controller.json'}")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--stage", choices=("build", "export"), required=True)
    args = parser.parse_args(argv)
    if args.stage == "build":
        stage_build()
    else:
        stage_export()


if __name__ == "__main__":
    main()
