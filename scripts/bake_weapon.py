"""Weapon-mesh OBJ-to-GLB preprocessor for Jiangyu.

Produces a single GLB carrying the weapon's mesh, a Principled BSDF material
wired up with the source textures (for Blender visual preview only), and two
empty objects (`muzzle`, `weapon_hand_l`) seeded from a reference vanilla
MENACE weapon's attach-point transforms. The downstream Unity-side
`Jiangyu.Mod.BakeWeapon` Editor utility consumes this GLB and bakes a prefab
with the Menace/* shader cloned from the reference weapon's material.

The intermediate GLB is intended to be opened in Blender so the modder can
nudge the `muzzle` and `weapon_hand_l` empties to match their weapon's actual
muzzle and left-hand grip positions before handing back to Unity.

Pipeline:
  1. Import OBJ.
  2. Apply axis fixup so the mesh's forward axis matches the reference's
     convention (+Z forward, +Y up, in Unity-style coordinates).
  3. Centre the mesh origin at (0, 0, 0). The weapon root parents under the
     soldier's Hand_R bone so the mesh origin determines where the gun sits
     in the hand.
  4. Build a Principled BSDF material with the three textures wired into
     BaseColor / Normal / Roughness+Metallic. This is for Blender preview
     only; the Unity bake replaces the material with a Menace-shader clone.
  5. Read the reference weapon glTF, extract `muzzle` and `weapon_hand_l`
     node translations + rotations.
  6. Create two Empty objects at those positions, parented to the mesh root.
  7. Export as GLB with textures embedded.

Run as:
    blender --background --python bake_weapon.py -- --config <config.json>
"""

import argparse
import json
import math
import sys
from dataclasses import dataclass
from pathlib import Path

try:
    import bpy  # type: ignore
    import mathutils  # type: ignore
except ImportError as exc:  # pragma: no cover - Blender-only entry point
    raise SystemExit("This script must be run inside Blender.") from exc

try:
    import numpy as np  # type: ignore
except ImportError as exc:  # pragma: no cover - Blender bundles numpy
    raise SystemExit("numpy is required (Blender bundles numpy by default).") from exc


# -----------------------------------------------------------------------------
# Config
# -----------------------------------------------------------------------------


@dataclass
class WeaponConfig:
    obj_path: Path
    texture_base_map: Path
    texture_normal_map: Path
    texture_mask_map: Path
    reference_weapon_gltf: Path
    output_path: Path
    obj_forward_axis: str
    obj_up_axis: str
    mesh_basename: str
    centre_axes: list[str]
    flip_uv_v: bool

    @staticmethod
    def load(path: Path) -> "WeaponConfig":
        data = json.loads(path.read_text(encoding="utf-8"))
        textures = data["textures"]
        # Default: centre only the X (left/right) axis. Y (up) and Z (length)
        # need manual positioning in Blender because the right-hand grip and
        # receiver axis aren't at the mesh's bbox midpoint for any rig that
        # has a dangling magazine or asymmetric stock/barrel split.
        return WeaponConfig(
            obj_path=Path(data["obj_path"]),
            texture_base_map=Path(textures["base_map"]),
            texture_normal_map=Path(textures["normal_map"]),
            texture_mask_map=Path(textures["mask_map"]),
            reference_weapon_gltf=Path(data["reference_weapon_gltf"]),
            output_path=Path(data["output_path"]),
            obj_forward_axis=str(data.get("obj_forward_axis", "-Z")),
            obj_up_axis=str(data.get("obj_up_axis", "Y")),
            mesh_basename=str(data["mesh_basename"]),
            centre_axes=[a.upper() for a in data.get("centre_axes", ["X"])],
            flip_uv_v=bool(data.get("flip_uv_v", True)),
        )


# -----------------------------------------------------------------------------
# Scene reset
# -----------------------------------------------------------------------------


def reset_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)


# -----------------------------------------------------------------------------
# OBJ import
# -----------------------------------------------------------------------------


_AXIS_ENUM = {
    "X": "X", "+X": "X",
    "Y": "Y", "+Y": "Y",
    "Z": "Z", "+Z": "Z",
    "-X": "NEGATIVE_X", "NEGATIVE_X": "NEGATIVE_X",
    "-Y": "NEGATIVE_Y", "NEGATIVE_Y": "NEGATIVE_Y",
    "-Z": "NEGATIVE_Z", "NEGATIVE_Z": "NEGATIVE_Z",
}


def import_obj(obj_path: Path, forward_axis: str, up_axis: str) -> bpy.types.Object:
    if not obj_path.exists():
        raise SystemExit(f"OBJ not found: {obj_path}")

    fwd = _AXIS_ENUM.get(forward_axis)
    up = _AXIS_ENUM.get(up_axis)
    if fwd is None or up is None:
        raise SystemExit(f"Invalid axis config: forward={forward_axis!r}, up={up_axis!r}")

    pre_objs = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=str(obj_path), forward_axis=fwd, up_axis=up)
    new_objs = [o for o in bpy.data.objects if o not in pre_objs and o.type == "MESH"]
    if not new_objs:
        raise SystemExit(f"OBJ import produced no mesh: {obj_path}")
    if len(new_objs) > 1:
        print(f"[warn] OBJ produced {len(new_objs)} meshes; joining into one.")
        bpy.ops.object.select_all(action="DESELECT")
        for obj in new_objs:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = new_objs[0]
        bpy.ops.object.join()
    mesh_obj = bpy.context.view_layer.objects.active

    # Bake the importer's axis re-orientation (location/rotation/scale) into
    # the mesh data. Without this, the glTF exporter writes node-level TRS
    # instead of in-mesh coordinates, and our attach-point empties get
    # parented relative to a transformed root rather than the mesh's own
    # local frame.
    bpy.ops.object.select_all(action="DESELECT")
    mesh_obj.select_set(True)
    bpy.context.view_layer.objects.active = mesh_obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return mesh_obj


def flip_uv_v(mesh_obj: bpy.types.Object) -> None:
    """Invert V on every UV-loop. Sunborn-rip OBJs ship UVs authored against
    Unity's V=0-at-top convention; Blender (and the glTF spec) expect
    V=0-at-bottom, so without this the texture renders upside-down."""
    mesh = mesh_obj.data
    for uv_layer in mesh.uv_layers:
        for loop_uv in uv_layer.data:
            u, v = loop_uv.uv
            loop_uv.uv = (u, 1.0 - v)


def centre_origin(mesh_obj: bpy.types.Object, axes: list[str]) -> None:
    """Shift the mesh so its bounding-box midpoint sits at 0 on the named
    axes. Axis names are in **glTF space** so the modder thinks in the
    export convention (X=right, Y=up, Z=forward), not Blender's swapped
    Z-up frame. The glTF Y-up export will swap Blender Y/Z, so we map:

        glTF X  ->  Blender X
        glTF Y  ->  Blender Z   (height)
        glTF Z  ->  Blender Y   (length / forward)
    """
    if not axes:
        return
    mesh = mesh_obj.data
    if not mesh.vertices:
        return
    xs = [v.co.x for v in mesh.vertices]
    ys = [v.co.y for v in mesh.vertices]
    zs = [v.co.z for v in mesh.vertices]
    midpoint = [
        (min(xs) + max(xs)) / 2.0,
        (min(ys) + max(ys)) / 2.0,
        (min(zs) + max(zs)) / 2.0,
    ]
    gltf_to_blender_idx = {"X": 0, "Y": 2, "Z": 1}
    shift = [0.0, 0.0, 0.0]
    for a in axes:
        idx = gltf_to_blender_idx.get(a)
        if idx is None:
            print(f"[warn] unknown centre axis {a!r}, skipping")
            continue
        shift[idx] = -midpoint[idx]
    for v in mesh.vertices:
        v.co.x += shift[0]
        v.co.y += shift[1]
        v.co.z += shift[2]


def rename_mesh(mesh_obj: bpy.types.Object, basename: str) -> None:
    mesh_obj.name = f"{basename}_LOD0"
    if mesh_obj.data is not None:
        mesh_obj.data.name = f"{basename}_LOD0"


# -----------------------------------------------------------------------------
# Material (Blender preview)
# -----------------------------------------------------------------------------


def _load_image(path: Path, non_colour: bool) -> bpy.types.Image:
    if not path.exists():
        raise SystemExit(f"Texture not found: {path}")
    img = bpy.data.images.load(str(path), check_existing=True)
    if non_colour:
        img.colorspace_settings.name = "Non-Color"
    return img


def build_preview_material(cfg: WeaponConfig, mesh_obj: bpy.types.Object) -> None:
    """Sets up a Principled BSDF using BaseMap / NormalMap / MaskMap for the
    Blender preview only. The Unity bake replaces this with a clone of the
    reference weapon's Menace/* shader material."""
    mat = bpy.data.materials.new(name=f"{cfg.mesh_basename}_preview")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()

    output = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    output.location = (600, 0)
    bsdf.location = (300, 0)
    links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])

    # Base colour (sRGB).
    base_tex = nodes.new("ShaderNodeTexImage")
    base_tex.image = _load_image(cfg.texture_base_map, non_colour=False)
    base_tex.location = (-300, 200)
    links.new(base_tex.outputs["Color"], bsdf.inputs["Base Color"])

    # Normal map (non-colour, with Normal Map node).
    normal_tex = nodes.new("ShaderNodeTexImage")
    normal_tex.image = _load_image(cfg.texture_normal_map, non_colour=True)
    normal_tex.location = (-300, -100)
    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.location = (0, -100)
    links.new(normal_tex.outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], bsdf.inputs["Normal"])

    # Mask map (non-colour). Assume R=Roughness, G=Metallic, B=AO for preview
    # only — Unity-side bake passes the texture through to whatever channel
    # mapping the Menace/* shader expects.
    mask_tex = nodes.new("ShaderNodeTexImage")
    mask_tex.image = _load_image(cfg.texture_mask_map, non_colour=True)
    mask_tex.location = (-300, -400)
    separate = nodes.new("ShaderNodeSeparateColor")
    separate.location = (0, -400)
    links.new(mask_tex.outputs["Color"], separate.inputs["Color"])
    links.new(separate.outputs["Red"], bsdf.inputs["Roughness"])
    links.new(separate.outputs["Green"], bsdf.inputs["Metallic"])

    if mesh_obj.data.materials:
        mesh_obj.data.materials[0] = mat
    else:
        mesh_obj.data.materials.append(mat)


# -----------------------------------------------------------------------------
# Reference glTF attach-point extraction
# -----------------------------------------------------------------------------


@dataclass
class AttachPoint:
    name: str
    translation: tuple[float, float, float]
    rotation: tuple[float, float, float, float]  # quaternion (x, y, z, w)


def read_reference_attach_points(gltf_path: Path) -> list[AttachPoint]:
    """Extract `muzzle` and `weapon_hand_l` node TRS from the reference glTF.
    These are the attach points the MENACE runtime queries on weapon prefabs
    for muzzle-flash spawn position and left-hand IK target."""
    if not gltf_path.exists():
        raise SystemExit(f"Reference glTF not found: {gltf_path}")
    data = json.loads(gltf_path.read_text(encoding="utf-8"))
    wanted = ("muzzle", "weapon_hand_l")
    found: list[AttachPoint] = []
    for node in data.get("nodes", []):
        name = node.get("name", "")
        if name in wanted:
            t = tuple(node.get("translation", [0.0, 0.0, 0.0]))
            r = tuple(node.get("rotation", [0.0, 0.0, 0.0, 1.0]))
            found.append(AttachPoint(name=name, translation=t, rotation=r))
    missing = [n for n in wanted if not any(p.name == n for p in found)]
    if missing:
        print(f"[warn] reference glTF missing attach point(s): {missing}")
    return found


def _gltf_quat_to_blender(q_gltf: tuple[float, float, float, float]) -> mathutils.Quaternion:
    """Convert a glTF quaternion (x, y, z, w) in Y-up space to a Blender
    quaternion in Z-up space. Same axis-swap Blender's glTF importer applies
    for orientation: rotate by -90° around X to map Y-up → Z-up, then
    conjugate the source rotation by that change-of-basis."""
    qx, qy, qz, qw = q_gltf
    q = mathutils.Quaternion((qw, qx, qy, qz))
    q_change = mathutils.Quaternion((1.0, 0.0, 0.0), math.radians(-90.0))
    return q_change @ q @ q_change.inverted()


def create_attach_point_empties(parent: bpy.types.Object, points: list[AttachPoint]) -> None:
    """Add `muzzle` and `weapon_hand_l` empties as siblings of the mesh under
    a shared root. The empties must be parented to the prefab root (not to
    the mesh) so the modder can move the mesh independently — the mesh's
    position relative to the root determines where the soldier's right hand
    grips the gun, while the empties stay at their authored muzzle and
    left-hand-grip positions.

    Translation and rotation are both converted from the reference's glTF
    Y-up frame into Blender's Z-up frame so they round-trip back to the
    reference's values on export."""
    for p in points:
        empty = bpy.data.objects.new(p.name, None)
        empty.empty_display_type = "ARROWS"
        empty.empty_display_size = 0.05
        gx, gy, gz = p.translation
        empty.location = mathutils.Vector((gx, -gz, gy))
        empty.rotation_mode = "QUATERNION"
        empty.rotation_quaternion = _gltf_quat_to_blender(p.rotation)
        empty.parent = parent
        bpy.context.collection.objects.link(empty)


def create_prefab_root(basename: str) -> bpy.types.Object:
    """The prefab root is where the soldier's Hand_R bone attaches. Mesh +
    attach-point empties hang off this as siblings so each can be repositioned
    without dragging the others."""
    root = bpy.data.objects.new(basename, None)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.1
    bpy.context.collection.objects.link(root)
    return root


# -----------------------------------------------------------------------------
# GLB export
# -----------------------------------------------------------------------------


def export_glb(output_path: Path) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(output_path),
        export_format="GLB",
        export_image_format="AUTO",
        export_apply=True,
        export_attributes=False,
        export_yup=True,
        export_extras=False,
        export_animations=False,
        export_skins=False,
        export_morph=False,
    )


def repack_rmo_to_mask_map(rmo_path: Path, out_path: Path) -> None:
    """Convert Sunborn-rip `_rmo` (R=Roughness, G=Metallic, B=AO) to HDRP
    `_MaskMap` convention (R=Metallic, G=AO, B=Detail=0, A=Smoothness=1-R).
    Dodges the chrome-blue rendering bug from slotting the raw `_rmo` into
    Menace's MaskMap slot, where the shader reads channel R as Metallic."""
    if not rmo_path.exists():
        raise SystemExit(f"RMO texture not found: {rmo_path}")
    out_path.parent.mkdir(parents=True, exist_ok=True)

    img = bpy.data.images.load(str(rmo_path), check_existing=False)
    try:
        img.colorspace_settings.name = "Non-Color"
        w, h = img.size
        if w == 0 or h == 0:
            raise SystemExit(f"RMO texture has zero dimensions: {rmo_path}")

        # Read pixels into numpy for vectorised channel swap. Blender's
        # img.pixels is a flat RGBA float32 array in row-major bottom-up
        # order — we don't care about row order since we don't transpose.
        pixels = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(pixels)
        pixels = pixels.reshape(-1, 4)

        repacked = np.zeros_like(pixels)
        repacked[:, 0] = pixels[:, 1]            # R = Metallic (from rmo.G)
        repacked[:, 1] = pixels[:, 2]            # G = AO       (from rmo.B)
        repacked[:, 2] = 0.0                     # B = Detail   (no detail mask)
        repacked[:, 3] = 1.0 - pixels[:, 0]      # A = Smoothness (1 - rmo.R)

        new_img = bpy.data.images.new(
            name=out_path.stem, width=w, height=h, alpha=True, float_buffer=True)
        try:
            new_img.colorspace_settings.name = "Non-Color"
            new_img.pixels.foreach_set(repacked.flatten())
            new_img.filepath_raw = str(out_path)
            new_img.file_format = "PNG"
            new_img.save()
        finally:
            bpy.data.images.remove(new_img)
    finally:
        bpy.data.images.remove(img)


def copy_textures_to_output(cfg: WeaponConfig) -> dict[str, Path]:
    """Copy the three source textures into the GLB's output directory so the
    Unity-side BakeWeapon utility can pick them up as standalone Texture2D
    assets (with proper sRGB / normal map / linear import settings) rather
    than relying on glTF-embedded textures."""
    import shutil
    out_dir = cfg.output_path.parent / "textures"
    out_dir.mkdir(parents=True, exist_ok=True)
    paths = {}
    for key, src in (
        ("base_map", cfg.texture_base_map),
        ("normal_map", cfg.texture_normal_map),
        ("mask_map", cfg.texture_mask_map),
    ):
        dst = out_dir / src.name
        shutil.copy2(src, dst)
        paths[key] = dst
    return paths


# -----------------------------------------------------------------------------
# Entry point
# -----------------------------------------------------------------------------


def parse_args() -> argparse.Namespace:
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1 :]
    else:
        argv = []
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True, type=Path)
    parser.add_argument(
        "--mode",
        choices=["full", "repack-mask"],
        default="full",
        help="full: GLB + textures + repacked mask (default). "
        "repack-mask: regenerate just the HDRP MaskMap PNG from _rmo (preserves a hand-edited GLB).",
    )
    return parser.parse_args(argv)


def _mask_output_path(cfg: WeaponConfig) -> Path:
    """Filename for the HDRP-repacked mask: `<rmo-stem-with-_rmo-stripped>_mask.png`
    next to the copied textures."""
    src = cfg.texture_mask_map
    stem = src.stem
    if stem.endswith("_rmo"):
        stem = stem[: -len("_rmo")]
    return cfg.output_path.parent / "textures" / f"{stem}_mask.png"


def main() -> None:
    args = parse_args()
    cfg = WeaponConfig.load(args.config)

    if args.mode == "repack-mask":
        reset_scene()
        out_path = _mask_output_path(cfg)
        repack_rmo_to_mask_map(cfg.texture_mask_map, out_path)
        print(f"[done] HDRP mask map written to: {out_path}")
        return

    reset_scene()
    mesh_obj = import_obj(cfg.obj_path, cfg.obj_forward_axis, cfg.obj_up_axis)
    print(f"[info] imported mesh: {mesh_obj.name} ({len(mesh_obj.data.vertices)} verts)")
    print(f"[info] OBJ axes -> forward={cfg.obj_forward_axis}, up={cfg.obj_up_axis}")

    if cfg.flip_uv_v:
        flip_uv_v(mesh_obj)
        print(f"[info] flipped UV V (Unity V=0-top -> glTF V=0-bottom)")

    if cfg.centre_axes:
        centre_origin(mesh_obj, cfg.centre_axes)
        print(f"[info] centred mesh on axes: {cfg.centre_axes}")
    rename_mesh(mesh_obj, cfg.mesh_basename)

    build_preview_material(cfg, mesh_obj)

    # Build the prefab structure: a root empty at origin (the Hand_R attach
    # point), with mesh + attach-point empties as siblings underneath. Moving
    # the mesh in Blender repositions the right-hand grip without dragging
    # the muzzle / left-hand empties along.
    root = create_prefab_root(cfg.mesh_basename + "_root")
    mesh_obj.parent = root

    points = read_reference_attach_points(cfg.reference_weapon_gltf)
    print(f"[info] reference attach points: {[p.name for p in points]}")
    create_attach_point_empties(root, points)

    export_glb(cfg.output_path)
    tex_paths = copy_textures_to_output(cfg)
    mask_path = _mask_output_path(cfg)
    repack_rmo_to_mask_map(cfg.texture_mask_map, mask_path)
    print(f"\n[done] weapon GLB written to: {cfg.output_path}")
    print(f"       copied textures to:    {cfg.output_path.parent / 'textures'}")
    print(f"       repacked HDRP mask:    {mask_path}")
    print(f"       open the GLB in Blender to nudge the 'muzzle' and 'weapon_hand_l'")
    print(f"       empties (and the mesh, to set the right-hand grip), then re-export")
    print(f"       over the same path before passing to Unity BakeWeapon.")


if __name__ == "__main__":
    main()
