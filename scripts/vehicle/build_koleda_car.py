#!/usr/bin/env python3
"""Build the Koleda supercar bake inputs from the authored .blend.

Owns everything car-specific and Sunborn-specific, keeping the generic
Jiangyu BakeVehicle free of model flavour:

- exports unity/Assets/Authored/koleda_car/raw.fbx in a neutral state
  (stored pose wiped, object locations zeroed, axis-correction rotations
  KEPT, animation tracks muted, per-action takes)
- repacks each Sunborn `_rmo` (R=Roughness, G=Metallic, B=AO) into an HDRP
  MaskMap (R=Metallic, G=AO, B=0, A=1-Roughness) next to the textures
- writes the material manifest mapping each authored material to its
  base / normal / mask paths

Run under Blender:
  blender -b --python scripts/vehicle/build_koleda_car.py

Then bake:
  Unity -batchmode -nographics -quit -projectPath unity \
    -executeMethod Jiangyu.Mod.BakeVehicle.BakeBatch \
    -fbxPath Assets/Authored/koleda_car/raw.fbx \
    -outputName koleda_car/default -targetLength 4.8 \
    -moveClip drive_wheelspin -doorOpenClip door_open -doorCloseClip door_close \
    -dropMeshes plane \
    -materialManifest Assets/Authored/koleda_car/materials.json \
    -muzzleAnchors Gun_L:muzzle,Gun_R:muzzle2 \
    -graftNodes "Assets/Imported/el.carrier_open_transport/GameObject/el.carrier_open_transport.prefab@rotator/mesh/carrier_chassis/Root/Chassis/lights/FrontLights@-0.66;0.55;2.2,Assets/Imported/el.carrier_open_transport/GameObject/el.carrier_open_transport.prefab@rotator/mesh/carrier_chassis/Root/Chassis/lights/FrontLights@0.66;0.55;2.2"

The graft copies the carrier's FrontLights node twice, once per headlight
lens (x offsets measured from the CarLight vertex groups). The carrier's
orange Area Light is deliberately left behind: the car carries no cabin
or brake glow. The game wraps any child Light components in a
NightLightsComp at element init, so night missions light up with no extra
runtime code.
"""

import json
import os
import sys
from pathlib import Path

import bpy

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "weapon"))
from bake_weapon import repack_rmo_to_mask_map

BLEND = "/home/justin/Downloads/Koleda_Supercar/Koleda_Supercar_base.blend"
AUTHORED = "/home/justin/dev/github.com/antistrategie/WOMENACE/unity/Assets/Authored/koleda_car"
FBX_OUT = AUTHORED + "/raw.fbx"
TEX = AUTHORED + "/textures"
UNITY_ROOT = "/home/justin/dev/github.com/antistrategie/WOMENACE/unity"
CLIPS = {"drive_wheelspin", "door_open", "door_close"}

# Material -> Sunborn texture set. sub0..sub4 are the lod0 submesh slots in
# source order. The glass mesh (headlight covers, the windshield is cut from
# the model) has no entry here: the whole sheet binds the koleda_lights
# material appended below, so a glass texture-set entry would match no slot.
SETS = {
    "supercar_sub0": "Koleda_Supercar_01",
    "supercar_sub1": "Koleda_Supercar_01menban",
    "supercar_sub2": "Koleda_Supercar_02",
    "supercar_sub3": "Koleda_Supercar_03",
    "supercar_sub4": "Koleda_Supercar_04",
}


def find_tex(stem_suffixes):
    for suffix in stem_suffixes:
        for ext in (".png", ".tga"):
            p = f"{TEX}/{suffix}{ext}"
            if os.path.exists(p):
                return p
    return None


def build_masks_and_manifest():
    manifest = {"materials": []}
    for mat, prefix in SETS.items():
        base = find_tex([prefix + "_d", prefix + "_da"])
        normal = find_tex([prefix + "_n"])
        rmo = find_tex([prefix + "_rmo"])
        entry = {"name": mat}
        if base:
            entry["base"] = os.path.relpath(base, UNITY_ROOT).replace("\\", "/")
        if normal:
            entry["normal"] = os.path.relpath(normal, UNITY_ROOT).replace("\\", "/")
        if rmo:
            mask_path = f"{TEX}/{prefix}_mask.png"
            if not os.path.exists(mask_path) or os.path.getmtime(mask_path) < os.path.getmtime(rmo):
                repack_rmo_to_mask_map(Path(rmo), Path(mask_path))
                print("repacked mask:", mask_path)
            entry["mask"] = os.path.relpath(mask_path, UNITY_ROOT).replace("\\", "/")
        manifest["materials"].append(entry)
    if os.environ.get("WOMENACE_DEBUG_LIGHTS") == "1":
        manifest["materials"].append({
            "name": "debug_lights",
            "base": os.path.relpath(TEX + "/debug_red.png", UNITY_ROOT).replace("\\", "/"),
        })
    else:
        manifest["materials"].append({
            "name": "koleda_lights",
            "base": os.path.relpath(TEX + "/Koleda_Supercar_trans_da.png", UNITY_ROOT).replace("\\", "/"),
        })
    out = AUTHORED + "/materials.json"
    with open(out, "w") as fh:
        json.dump(manifest, fh, indent=2)
    print("manifest:", out)


def export_fbx():
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    arm.animation_data.action = None
    for tr in list(arm.animation_data.nla_tracks):
        if tr.name in CLIPS:
            tr.mute = True
        else:
            arm.animation_data.nla_tracks.remove(tr)
    for act in list(bpy.data.actions):
        if act.name.split("|")[0] not in CLIPS and act.name not in CLIPS:
            bpy.data.actions.remove(act)
    for act in bpy.data.actions:
        act.use_fake_user = True
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.scale = (1, 1, 1)
    freeze_headlights(arm)
    # Zero LOCATIONS only. The chain's rotations are the source FBX's axis
    # correction. Zeroing them stands the car on its end in Unity.
    node = arm
    top = arm
    while node:
        node.location = (0, 0, 0)
        top = node
        node = node.parent
    # The source model faces the opposite way to MENACE's +Z-forward
    # convention: spin the whole export 180 degrees about world up on top of
    # the kept axis correction.
    from mathutils import Matrix
    bpy.context.view_layer.update()
    top.matrix_world = Matrix.Rotation(3.14159265, 4, "Z") @ top.matrix_world
    top.location = (0, 0, 0)
    bpy.context.view_layer.update()
    sel = [o for o in bpy.data.objects if o.type in ("ARMATURE", "MESH", "EMPTY")]
    for o in bpy.data.objects:
        o.select_set(o in sel)
    bpy.ops.export_scene.fbx(
        filepath=FBX_OUT,
        use_selection=True,
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,
        object_types={"ARMATURE", "MESH", "EMPTY"},
        path_mode="STRIP",
        embed_textures=False,
    )
    print("exported:", FBX_OUT)


LIGHT_BONES = ("CarLight_L", "CarLight_R")


def action_fcurves(act):
    try:
        return [fc for layer in act.layers for strip in layer.strips
                for cb in strip.channelbags for fc in cb.fcurves]
    except AttributeError:
        return list(act.fcurves)


def light_lens_pass():
    """The visible lens strips are the glass mesh's CarLight cover faces.
    Their mesh data measures mirror-identical per side, yet the engine
    renders one lit and one dead on the original glass material, and every
    build that overrides the glass renders them equal. The CarLight faces
    are painted onto a dedicated flat material (the glass sheet's own
    diffuse with its amber lens texels, no normal or mask map). In the
    baked prefab the WHOLE glass sheet ends up on that material: the glass
    mesh is only the headlight covers (the windshield is cut from the
    model), and the manifest carries no texture set for the original glass
    slot. The body mesh stays fully original. WOMENACE_DEBUG_LIGHTS=1
    paints the whole units red instead (pipeline channel test)."""
    debug = os.environ.get("WOMENACE_DEBUG_LIGHTS") == "1"
    targets = [("Koleda_Supercar_glass_lod0", True)]
    if debug:
        targets.append(("Koleda_Supercar_lod0", True))
    for objname, _ in targets:
        o = bpy.data.objects[objname]
        me = o.data
        gi = {g.index: g.name for g in o.vertex_groups}
        mat = bpy.data.materials.new("debug_lights" if debug else "koleda_lights")
        me.materials.append(mat)
        ridx = len(me.materials) - 1
        n = 0
        for poly in me.polygons:
            hit = False
            for vi in poly.vertices:
                if any("CarLight" in gi.get(g.group, "") and g.weight > 0.3
                       for g in me.vertices[vi].groups):
                    hit = True
                    break
            if hit:
                poly.material_index = ridx
                n += 1
        print(f"lens paint: {n} faces -> {mat.name} on {objname}")


def freeze_headlights(arm):
    """The pop-up light units cannot be trusted to the rig: the rip's
    per-side skin weights differ, so any posed state renders the two units
    at different world angles (the dead-headlight look), and the baked takes
    re-key the bones so the units also move with clips. Freeze them instead:
    pose the LEFT unit raised (door_close's end state, the one that renders
    lit), bake its evaluated geometry and normals into the bind mesh, mirror
    them exactly onto the right unit, then hand every unit vertex rigidly to
    the chassis bone. After this no bone, weight, or clip can affect the
    lights, and the two sides are mirror-identical by construction."""
    from mathutils import Vector

    close = bpy.data.actions["door_close"]
    end = max(fc.range()[1] for fc in action_fcurves(close))
    path = 'pose.bones["CarLight_L"].rotation_quaternion'
    q = [1.0, 0.0, 0.0, 0.0]
    for fc in action_fcurves(close):
        if fc.data_path == path:
            q[fc.array_index] = fc.evaluate(end)
    for name in LIGHT_BONES:
        arm.pose.bones[name].rotation_quaternion = q
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()

    for obj_name in ("Koleda_Supercar_lod0", "Koleda_Supercar_glass_lod0"):
        o = bpy.data.objects[obj_name]
        me = o.data
        groups = {s: o.vertex_groups.get("CarLight_" + s) for s in "LR"}
        if groups["L"] is None or groups["R"] is None:
            continue
        gidx = {s: g.index for s, g in groups.items()}

        def weight(vert, gi):
            return sum(g.weight for g in vert.groups if g.group == gi)

        unit_verts = {"L": [], "R": []}
        for v in me.vertices:
            wl, wr = weight(v, gidx["L"]), weight(v, gidx["R"])
            if wl <= 0 and wr <= 0:
                continue
            unit_verts["L" if wl >= wr else "R"].append(v.index)

        ev = o.evaluated_get(deps)
        posed = [v.co.copy() for v in ev.data.vertices]
        posed_normals = [Vector(cn.vector) for cn in ev.data.corner_normals]

        # left unit: bake its own posed state; right unit: bake the exact
        # mirror of the left's posed state, matched by mirrored bind position
        left_bind = [(me.vertices[vi].co.copy(), vi) for vi in unit_verts["L"]]
        match = {}
        for vi in unit_verts["R"]:
            co = me.vertices[vi].co
            src, svi = min(left_bind, key=lambda t: (t[0].x + co.x) ** 2
                           + (t[0].y - co.y) ** 2 + (t[0].z - co.z) ** 2)
            match[vi] = svi
        for vi in unit_verts["L"]:
            me.vertices[vi].co = posed[vi]
        for vi, svi in match.items():
            src = posed[svi]
            me.vertices[vi].co = Vector((-src.x, src.y, src.z))

        # normals: left loops keep their posed normals, right loops mirror
        # the matched left vertex's posed normal
        left_loop_normal = {}
        left_set = set(unit_verts["L"])
        for poly in me.polygons:
            for li in poly.loop_indices:
                vi = me.loops[li].vertex_index
                if vi in left_set:
                    left_loop_normal.setdefault(vi, posed_normals[li])
        normals = [Vector(cn.vector) for cn in me.corner_normals]
        right_set = set(unit_verts["R"])
        for poly in me.polygons:
            for li in poly.loop_indices:
                vi = me.loops[li].vertex_index
                if vi in left_set:
                    normals[li] = posed_normals[li]
                elif vi in right_set:
                    n = left_loop_normal[match[vi]]
                    normals[li] = Vector((-n.x, n.y, n.z))
        me.normals_split_custom_set([n.normalized() for n in normals])

        # vertex colours are packed shader data on ripped meshes and the two
        # units' channels differ (the lit look rides on them), so mirror
        # the left unit's loop colours onto the right as well
        for ca in me.color_attributes:
            if ca.domain != "CORNER":
                continue
            left_col = {}
            for poly in me.polygons:
                for li in poly.loop_indices:
                    vi = me.loops[li].vertex_index
                    if vi in left_set:
                        left_col.setdefault(vi, tuple(ca.data[li].color[:]))
            for poly in me.polygons:
                for li in poly.loop_indices:
                    vi = me.loops[li].vertex_index
                    if vi in right_set and match[vi] in left_col:
                        ca.data[li].color = left_col[match[vi]]

        # rigid to the chassis: the light bones lose all influence
        body = o.vertex_groups.get("Body_M") or o.vertex_groups.new(name="Body_M")
        frozen = unit_verts["L"] + unit_verts["R"]
        for s in "LR":
            groups[s].remove(frozen)
        body.add(frozen, 1.0, "REPLACE")
        print(f"headlights frozen on {obj_name}: {len(frozen)} verts rigid to Body_M")

    for name in LIGHT_BONES:
        arm.pose.bones[name].rotation_quaternion = (1, 0, 0, 0)
    for act_name in ("door_open", "door_close"):
        act = bpy.data.actions[act_name]
        try:
            bags = [cb for layer in act.layers for strip in layer.strips
                    for cb in strip.channelbags]
        except AttributeError:
            bags = [act]
        for bag in bags:
            for fc in [f for f in bag.fcurves if "CarLight" in f.data_path]:
                bag.fcurves.remove(fc)
    bpy.context.view_layer.update()


bpy.ops.wm.open_mainfile(filepath=BLEND)
light_lens_pass()
export_fbx()
build_masks_and_manifest()
print("DONE")
