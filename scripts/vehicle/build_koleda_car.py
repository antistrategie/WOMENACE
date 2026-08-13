#!/usr/bin/env python3
"""Build the Koleda supercar bake inputs from the authored .blend.

Owns everything car-specific and Sunborn-specific, keeping the generic
Jiangyu BakeVehicle free of model flavour:

- exports unity/Assets/Authored/koleda_car/raw.fbx in a neutral state
  (stored pose wiped, object locations zeroed, axis-correction rotations
  KEPT, animation tracks muted, per-action takes)
- writes the material manifest mapping each authored material to its
  base / normal / mask paths. The masks are the Sunborn `_rmo`, which is the
  packing DollToon reads natively

Run under Blender:
  blender -b --python scripts/vehicle/build_koleda_car.py

Then bake:
  Unity -batchmode -nographics -quit -projectPath unity \
    -executeMethod Jiangyu.Mod.BakeVehicle.BakeBatch \
    -fbxPath Assets/Authored/koleda_car/raw.fbx \
    -outputName koleda_car/default -targetLength 5.0 \
    -moveClip drive_wheelspin -doorOpenClip door_open -doorCloseClip door_close \
    -materialManifest Assets/Authored/koleda_car/materials.json \
    -muzzleAnchors Gun_L:muzzle,Gun_R:muzzle2 \
    -graftNodes "Assets/Imported/el.carrier_open_transport/GameObject/el.carrier_open_transport.prefab@rotator/mesh/carrier_chassis/Root/Chassis/lights/FrontLights@-0.688;0.573;2.452,Assets/Imported/el.carrier_open_transport/GameObject/el.carrier_open_transport.prefab@rotator/mesh/carrier_chassis/Root/Chassis/lights/FrontLights@0.688;0.573;2.452"

The graft copies the carrier's FrontLights node twice, once per headlight
lens (x offsets measured from the CarLight vertex groups). The carrier's
orange Area Light is deliberately left behind: the car carries no cabin
or brake glow. The game wraps any child Light components in a
NightLightsComp at element init, so night missions light up with no extra
runtime code.

Those offsets are post-scale metres, set straight onto the grafted node's
local position, so they do not ride the `-targetLength` solve. Change the
target and they have to be multiplied by the same ratio or the headlights
stay where the smaller car's lenses were.

The forward offset is read off the baked prefab rather than guessed: the
body's nose sits at 2.541 and the lens strips at 2.333 to 2.352, so 2.452
puts the emitters just ahead of the glass and still inside the bodywork.

MENACE's world runs about 1.2 times life size, its own reference soldier
measuring 2.13m, so the car is baked past the 4.77m the game's own
full-size Koleda_Supercar asset carries. The source mesh is the 3.758m
battle lod0, which is smaller again.
"""

import json
import os
import sys
from pathlib import Path

import bpy


BLEND = "/home/justin/Downloads/Koleda_Supercar/Koleda_Supercar_base.blend"
AUTHORED = "/home/justin/dev/github.com/antistrategie/WOMENACE/unity/Assets/Authored/koleda_car"
FBX_OUT = AUTHORED + "/raw.fbx"
TEX = AUTHORED + "/textures"
UNITY_ROOT = "/home/justin/dev/github.com/antistrategie/WOMENACE/unity"
CLIPS = {"drive_wheelspin", "door_open", "door_close"}

# Material -> Sunborn texture set. sub0..sub4 are the lod0 submesh slots in
# source order. The glass and livery meshes have no entry here: their sets are
# appended below by hand, because neither takes a Sunborn <prefix>_d/_n/_rmo
# trio the way a body slot does.
#
# The pairing is not positional. AssetStudio exports the car with no materials
# at all (its Materials folder comes out empty, and the FBX carries no material
# names), and the asset map holds exactly one Material for the whole car, so
# there is nothing to read the binding off. It is recovered from the UV layouts
# instead, which is decisive because a Sunborn sheet is painted island by island:
#
# - sub0 and sub2 share Koleda_Supercar_01. Their islands have precisely zero
#   overlap, interlocking across the sheet, while every other pair of submeshes
#   collides over 68% of the smaller mask. sub0 lands on the white bodywork
#   artwork, sub2 on the dark trim between it.
# - sub1 is Koleda_Supercar_01menban. Thirty-four quads whose outlines trace the
#   door panel silhouettes on that sheet, orange strips and locating dots
#   included. menban is 门板, door panel.
# - sub3 is Koleda_Supercar_03, the cockpit sheet.
# - sub4 is Koleda_Supercar_02. This is the wheels: every wheel bone
#   (front/back Wheel and WheelBase, left and right) weights into this slot and
#   no other, and its spoke islands sit exactly on the sheet's gold five-spoke
#   artwork, the rim arcs on the rim rings.
#
# Koleda_Supercar_04 is not a body set. It is a flat 512 gradient, 219 unique
# colours in a 32..95 band with a featureless normal and a uniform RMO, and lod0
# does not sample it. Binding it to sub4 is what renders the wheels untextured.
SETS = {
    "supercar_sub0": "Koleda_Supercar_01",
    "supercar_sub1": "Koleda_Supercar_01menban",
    "supercar_sub2": "Koleda_Supercar_01",
    "supercar_sub3": "Koleda_Supercar_03",
    "supercar_sub4": "Koleda_Supercar_02",
}


def find_tex(stem_suffixes):
    for suffix in stem_suffixes:
        for ext in (".png", ".tga"):
            p = f"{TEX}/{suffix}{ext}"
            if os.path.exists(p):
                return p
    return None


# The GFL2 character shading, as the mech and the dolls run it. Body slots and
# the lens strips take DollToon with the shared weapon ramp, which is what the
# capture's binding map puts on the game's large draws. The livery decals blend
# through DollToonTrans off the trans sheet's alpha, and the glass blends
# through it off a flat tint.
# The masks bind the Sunborn _rmo, the packing DollToon reads natively.
RAMP = "Assets/Authored/shared/ramps/ramp_weapon.png"
TOON = "Womenace/DollToon"
TRANS = "Womenace/DollToonTrans"

# Every body slot is opaque, as the game's own recovered material is
# (Koleda_Supercar_01_uber_VFX: _Surface 0, _UseRampMap 1). Where a Sunborn set
# names its albedo _da the alpha carries data, not coverage, so a slot must
# never be routed to the transparent shader off an alpha histogram: doing that
# renders the wheels and the diffuser see-through.
TRANS_SLOTS = set()


def build_manifest():
    manifest = {"materials": []}
    for mat, prefix in SETS.items():
        base = find_tex([prefix + "_d", prefix + "_da"])
        normal = find_tex([prefix + "_n"])
        rmo = find_tex([prefix + "_rmo"])
        entry = {
            "name": mat,
            "shader": TRANS if mat in TRANS_SLOTS else TOON,
            "extras": [{"property": "_RampMap", "path": RAMP}],
        }
        if base:
            entry["base"] = os.path.relpath(base, UNITY_ROOT).replace("\\", "/")
        if normal:
            entry["normal"] = os.path.relpath(normal, UNITY_ROOT).replace("\\", "/")
        if rmo:
            entry["mask"] = os.path.relpath(rmo, UNITY_ROOT).replace("\\", "/")
        manifest["materials"].append(entry)
    trans_set = {
        "base": os.path.relpath(TEX + "/Koleda_Supercar_trans_da.png", UNITY_ROOT).replace("\\", "/"),
        "normal": os.path.relpath(TEX + "/Koleda_Supercar_trans_n.png", UNITY_ROOT).replace("\\", "/"),
        "mask": os.path.relpath(TEX + "/Koleda_Supercar_trans_rmo.png", UNITY_ROOT).replace("\\", "/"),
        "shader": TRANS,
        "extras": [{"property": "_RampMap", "path": RAMP}],
    }
    # The lens strips keep the flat base-only material on the game's default
    # shader. The two lamp units carry mismatched split normals, so any lit
    # shader renders one lens dark and one bright; the flat material is the fix
    # that made them equal, and a light cover reads better bright than
    # cel-shaded anyway.
    manifest["materials"].append({
        "name": "koleda_lights",
        "base": os.path.relpath(TEX + "/Koleda_Supercar_trans_da.png", UNITY_ROOT).replace("\\", "/"),
    })
    # The livery. plane_lod0 is the decal shell, nineteen small quads sitting on
    # the bodywork in mirrored pairs, each mapped to its own rectangle of the
    # trans sheet: NIGHT RUNNER across the nose, OVERDRIVE PIONEER on the deck,
    # VENTILATION and the warning labels down the flanks. The name is why it was
    # dropped for so long. It is not a ground-shadow quad, and dropping it is
    # what takes every decal off the car.
    manifest["materials"].append(dict(trans_set, name="supercar_livery"))
    # Two more patches on the flanks, the same sheet, their own small mesh.
    manifest["materials"].append(dict(trans_set, name="supercar_decals"))
    # The glass takes none of it. The trans sheet is a decal sheet, 92%
    # transparent with scattered artwork and no glass region on it anywhere,
    # and the glass mesh is unwrapped across the whole of it, so binding the
    # sheet paints every decal onto the windscreen and the door vent panes, at
    # glass scale, clipped by the glass outline, and mirrored left to right
    # because the two vent panes share their UV space. Those coordinates are
    # leftovers the game's own glass material never reads.
    #
    # So the albedo is a uniform tint, which reads the same wherever the
    # coordinates land, and the trans normal and RMO come off for the same
    # reason: sampled across a decal sheet they are noise.
    manifest["materials"].append({
        "name": "supercar_glass",
        "shader": TRANS,
        "base": os.path.relpath(TEX + "/glass_tint.png", UNITY_ROOT).replace("\\", "/"),
        "extras": [{"property": "_RampMap", "path": RAMP}],
    })
    out = AUTHORED + "/materials.json"
    with open(out, "w") as fh:
        json.dump(manifest, fh, indent=2)
    print("manifest:", out)


def restore_game_meshes():
    """Geometry the authored blend lacks, back on the car. The blend's glass
    object holds only the headlight lens strips, and the two flank decal
    patches (the game's fgj mesh) are absent from it entirely. Both come back
    from data/ JSON dumped straight out of the game's meshes, bound rigid to
    Body_M exactly like the lens strips, each on its own material so the
    manifest routes them to the transparent shader. koleda_glass.json is the
    windscreen pane and the two door vent panes, which is the game's glass
    mesh less the lens strips the blend already carries.

    The placement needs nothing solved. The game authors its car parts in one
    shared model space, where the glass and the decal band already sit inside
    the body's box, and the blend keeps its mesh data in that same space with
    the whole conversion (0.01 scale, the axis correction, the root offset)
    living in the object matrix. So these carry the body's matrix and land
    where the game puts them.

    Which is why the data is mesh coordinates and not a world-space OBJ. An OBJ
    exported after the object matrix has been applied, then re-parented, takes
    that matrix twice: the part lands a hundred times too small and nearly three
    units off the nose, invisible, and drags the bake's bounding box out with
    it so `-targetLength` shrinks the whole car to fit."""
    arm = next(a for a in bpy.data.objects if a.type == "ARMATURE")
    body = bpy.data.objects["Koleda_Supercar_lod0"]

    # The livery shell's own slot, renamed off the source's plane_lod0_mat so
    # the manifest can key on it like every other slot.
    shell = bpy.data.objects.get("Koleda_Supercar_plane_lod0")
    if shell is not None and shell.data.materials:
        shell.data.materials[0].name = "supercar_livery"

    for data_file, name in [("koleda_glass.json", "glass"),
                            ("koleda_decals.json", "decals")]:
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "data", data_file)
        with open(path) as handle:
            doc = json.load(handle)

        mesh = bpy.data.meshes.new("Koleda_Supercar_" + name)
        mesh.from_pydata(doc["vertices"], [], doc["triangles"])
        mesh.update()
        uv = mesh.uv_layers.new(name="UVMap")
        source = doc["uv"]
        for loop in mesh.loops:
            uv.data[loop.index].uv = source[loop.vertex_index]
        mesh.normals_split_custom_set_from_vertices(
            [tuple(n) for n in doc["normals"]])

        o = bpy.data.objects.new("Koleda_Supercar_" + name, mesh)
        bpy.context.collection.objects.link(o)
        mat = bpy.data.materials.new("supercar_" + name)
        mesh.materials.append(mat)

        group = o.vertex_groups.new(name="Body_M")
        group.add(list(range(len(mesh.vertices))), 1.0, "REPLACE")
        mod = o.modifiers.new("Armature", "ARMATURE")
        mod.object = arm
        o.parent = body.parent
        o.matrix_world = body.matrix_world.copy()
        print(f"{name} restored: {len(mesh.vertices)} verts, "
              f"{len(mesh.polygons)} faces")


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
    baked prefab the WHOLE of that sheet ends up on that material, which is
    what is wanted: the blend's glass object holds only the headlight covers,
    the panes coming back separately through restore_game_meshes. The body
    mesh stays fully original."""
    for objname in ("Koleda_Supercar_glass_lod0",):
        o = bpy.data.objects[objname]
        me = o.data
        gi = {g.index: g.name for g in o.vertex_groups}
        mat = bpy.data.materials.new("koleda_lights")
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
restore_game_meshes()
export_fbx()
build_manifest()
print("DONE")
