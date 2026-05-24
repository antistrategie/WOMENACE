"""Render a weapon GLB to a transparent PNG for icon use.

Two shading modes:

* `--mode flat` (default) uses Blender's Workbench render engine with the
  Texture shading mode so the output shows just the BaseColor texture with
  no PBR shininess. Fastest path; best for clean inventory icons.
* `--mode shaded` uses Eevee with the GLB's Principled BSDF material plus a
  three-point light rig. Produces a PBR-shaded image with rim highlights.

In both modes the background renders as alpha=0 (transparent PNG).

Run as:
    blender --background --python render_weapon.py -- \\
        --glb <path.glb> --out <path.png> \\
        [--width 912] [--height 318] [--padding 0.05] \\
        [--view side|front|back|top|three_quarter] \\
        [--mode flat|shaded]
"""

import argparse
import sys
from pathlib import Path

try:
    import bpy  # type: ignore
    import mathutils  # type: ignore
except ImportError as exc:  # pragma: no cover - Blender-only entry point
    raise SystemExit("This script must be run inside Blender.") from exc


# -----------------------------------------------------------------------------
# Scene reset + GLB import
# -----------------------------------------------------------------------------


def reset_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)


def import_glb(glb_path: Path) -> list[bpy.types.Object]:
    if not glb_path.exists():
        raise SystemExit(f"GLB not found: {glb_path}")
    pre = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(glb_path))
    return [o for o in bpy.data.objects if o not in pre]


def _mesh_bounds_world(objects: list[bpy.types.Object]) -> tuple[mathutils.Vector, mathutils.Vector]:
    INF = float("inf")
    lo = mathutils.Vector((INF, INF, INF))
    hi = mathutils.Vector((-INF, -INF, -INF))
    for obj in objects:
        if obj.type != "MESH" or obj.data is None:
            continue
        m = obj.matrix_world
        for v in obj.data.vertices:
            w = m @ v.co
            lo.x = min(lo.x, w.x); hi.x = max(hi.x, w.x)
            lo.y = min(lo.y, w.y); hi.y = max(hi.y, w.y)
            lo.z = min(lo.z, w.z); hi.z = max(hi.z, w.z)
    return lo, hi


# -----------------------------------------------------------------------------
# Camera setup
# -----------------------------------------------------------------------------


# View name → (camera_direction_in_blender, up_axis). Blender's glTF importer
# converts gltf Y-up → Blender Z-up, so post-import the weapon extends along
# Blender's -Y axis (forward) with up along +Z.
_VIEWS = {
    "side":          (mathutils.Vector(( 1.0,  0.0,  0.0)), mathutils.Vector((0.0, 0.0, 1.0))),
    "front":         (mathutils.Vector(( 0.0, -1.0,  0.0)), mathutils.Vector((0.0, 0.0, 1.0))),
    "back":          (mathutils.Vector(( 0.0,  1.0,  0.0)), mathutils.Vector((0.0, 0.0, 1.0))),
    "top":           (mathutils.Vector(( 0.0,  0.0,  1.0)), mathutils.Vector((0.0, -1.0, 0.0))),
    "three_quarter": (mathutils.Vector(( 1.0, -0.7,  0.5)), mathutils.Vector((0.0, 0.0, 1.0))),
}


def add_orthographic_camera(
    objects: list[bpy.types.Object],
    view: str,
    width_px: int,
    height_px: int,
    padding: float,
) -> bpy.types.Object:
    if view not in _VIEWS:
        raise SystemExit(f"Unknown view {view!r}; valid: {list(_VIEWS)}")
    direction, up = _VIEWS[view]
    direction = direction.normalized()

    lo, hi = _mesh_bounds_world(objects)
    centre = (lo + hi) * 0.5
    extents = hi - lo

    # Camera distance: along the view direction, far enough that the camera
    # sits outside the mesh's bounding sphere. For an orthographic camera the
    # exact distance doesn't matter for framing, only for clip planes.
    radius = max(extents.length, 0.1)
    camera_pos = centre + direction * (radius * 3.0)

    cam_data = bpy.data.cameras.new(name="render_cam")
    cam = bpy.data.objects.new(name="render_cam", object_data=cam_data)
    bpy.context.collection.objects.link(cam)

    cam.location = camera_pos
    # Aim the camera at the mesh centre using a track-to constraint pattern:
    # build a rotation that maps Blender's camera-forward (-Z) onto
    # `-direction`, with `up` aligned to camera-up (+Y).
    forward = -direction
    cam_z = -forward                # camera looks down -Z; so Z basis is -forward
    cam_x = up.cross(forward).normalized()
    if cam_x.length == 0:
        # Up and forward are parallel; pick an alternate up.
        cam_x = mathutils.Vector((1.0, 0.0, 0.0)) if abs(forward.z) > 0.9 else mathutils.Vector((0.0, 0.0, 1.0))
        cam_x = cam_x.cross(forward).normalized()
    cam_y = forward.cross(cam_x).normalized()
    rot_matrix = mathutils.Matrix((
        (cam_x.x, cam_y.x, cam_z.x, 0.0),
        (cam_x.y, cam_y.y, cam_z.y, 0.0),
        (cam_x.z, cam_y.z, cam_z.z, 0.0),
        (0.0,     0.0,     0.0,     1.0),
    ))
    cam.matrix_world = mathutils.Matrix.Translation(camera_pos) @ rot_matrix

    cam_data.type = "ORTHO"
    # Project the bbox onto the camera plane to figure out width/height in
    # camera-local units. cam_x is the in-image right axis, cam_y is up.
    half = extents * 0.5
    bbox_corners = [
        centre + mathutils.Vector((half.x * sx, half.y * sy, half.z * sz))
        for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)
    ]
    projected_x = [(c - camera_pos).dot(cam_x) for c in bbox_corners]
    projected_y = [(c - camera_pos).dot(cam_y) for c in bbox_corners]
    width_units = max(projected_x) - min(projected_x)
    height_units = max(projected_y) - min(projected_y)

    aspect = width_px / height_px
    # ortho_scale spans the longer of width/height in world units. Blender's
    # ortho_scale is the horizontal size when width >= height, otherwise
    # the framing breaks. Compute the bigger side accounting for aspect ratio:
    side = max(width_units, height_units * aspect)
    cam_data.ortho_scale = side * (1.0 + padding * 2.0)

    cam_data.clip_start = 0.001
    cam_data.clip_end = radius * 10.0

    bpy.context.scene.camera = cam
    return cam


# -----------------------------------------------------------------------------
# Render setup
# -----------------------------------------------------------------------------


def add_three_point_lights(objects: list[bpy.types.Object], view: str) -> None:
    """Three-point rig in world space, sized by the mesh's bounding sphere.
    Key light from upper-front, fill from opposite-lower for ambient lift,
    rim from behind for silhouette pop."""
    lo, hi = _mesh_bounds_world(objects)
    centre = (lo + hi) * 0.5
    radius = max((hi - lo).length, 0.3)

    view_dir = _VIEWS[view][0].normalized()
    up = _VIEWS[view][1].normalized()
    side = up.cross(view_dir).normalized()
    if side.length == 0:
        side = mathutils.Vector((1.0, 0.0, 0.0))

    rig = [
        ("key",  view_dir * 1.5 + up * 1.5 + side * 0.8,  3.5, 0.18),
        ("fill", view_dir * 1.5 - up * 0.8 - side * 1.2,  1.4, 0.30),
        ("rim", -view_dir * 1.8 + up * 0.5,               2.6, 0.12),
    ]
    for name, offset, energy, radius_size in rig:
        light_data = bpy.data.lights.new(name=f"render_light_{name}", type="AREA")
        light_data.energy = energy * (radius * 4.0)
        light_data.size = radius * radius_size
        light = bpy.data.objects.new(name=f"render_light_{name}", object_data=light_data)
        bpy.context.collection.objects.link(light)
        light.location = centre + offset * radius
        # Point each light at the mesh centre by reusing the camera-aim logic.
        forward = (centre - light.location).normalized()
        cam_z = -forward
        cam_x = mathutils.Vector((0, 0, 1)).cross(forward).normalized()
        if cam_x.length == 0:
            cam_x = mathutils.Vector((1, 0, 0))
        cam_y = forward.cross(cam_x).normalized()
        light.matrix_world = mathutils.Matrix.Translation(light.location) @ mathutils.Matrix((
            (cam_x.x, cam_y.x, cam_z.x, 0.0),
            (cam_x.y, cam_y.y, cam_z.y, 0.0),
            (cam_x.z, cam_y.z, cam_z.z, 0.0),
            (0.0,     0.0,     0.0,     1.0),
        ))


def configure_eevee_render(width_px: int, height_px: int) -> None:
    """Eevee Next + transparent film + RGBA output. Keeps the Principled BSDF
    material from the GLB intact so the BaseColor / Normal / Roughness /
    Metallic textures all contribute as PBR data."""
    scene = bpy.context.scene
    # Blender 5.x default engine name. Fall back gracefully if it's not
    # registered.
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue

    scene.render.film_transparent = True
    scene.render.resolution_x = width_px
    scene.render.resolution_y = height_px
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    scene.render.image_settings.compression = 50

    # Neutral world so the only light hitting the mesh is the three-point rig.
    world = scene.world or bpy.data.worlds.new("render_world")
    scene.world = world
    world.use_nodes = True
    world.color = (0.02, 0.02, 0.02)
    for node in list(world.node_tree.nodes):
        world.node_tree.nodes.remove(node)
    bg = world.node_tree.nodes.new("ShaderNodeBackground")
    bg.inputs["Color"].default_value = (0.02, 0.02, 0.02, 1.0)
    bg.inputs["Strength"].default_value = 0.3
    out = world.node_tree.nodes.new("ShaderNodeOutputWorld")
    world.node_tree.links.new(bg.outputs["Background"], out.inputs["Surface"])


def configure_workbench_render(width_px: int, height_px: int) -> None:
    """Workbench engine in Texture shading mode shows the BaseColor texture
    with no PBR shininess. Flat film + transparent background output as a
    clean RGBA PNG suitable for UI icons."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"

    workbench = scene.display
    if hasattr(workbench, "shading"):
        workbench.shading.light = "FLAT"
        workbench.shading.color_type = "TEXTURE"
        workbench.shading.show_specular_highlight = False
        if hasattr(workbench.shading, "show_xray"):
            workbench.shading.show_xray = False

    scene.render.film_transparent = True
    scene.render.resolution_x = width_px
    scene.render.resolution_y = height_px
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    scene.render.image_settings.compression = 50


# -----------------------------------------------------------------------------
# Entry point
# -----------------------------------------------------------------------------


def parse_args() -> argparse.Namespace:
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--glb", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--width", type=int, default=912)
    parser.add_argument("--height", type=int, default=318)
    parser.add_argument("--padding", type=float, default=0.05,
                        help="Fraction of bbox added as margin on each side.")
    parser.add_argument("--view", choices=list(_VIEWS.keys()), default="side")
    parser.add_argument("--mode", choices=["flat", "shaded"], default="flat",
                        help="flat = Workbench/no lighting; shaded = Eevee + three-point rig.")
    return parser.parse_args(argv)


def main() -> None:
    args = parse_args()
    reset_scene()
    objects = import_glb(args.glb)
    print(f"[info] imported {len(objects)} object(s) from {args.glb.name}")

    add_orthographic_camera(objects, args.view, args.width, args.height, args.padding)
    if args.mode == "shaded":
        add_three_point_lights(objects, args.view)
        configure_eevee_render(args.width, args.height)
    else:
        configure_workbench_render(args.width, args.height)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.scene.render.filepath = str(args.out)
    bpy.ops.render.render(write_still=True)
    print(f"[done] {args.mode} render, {args.view} view ({args.width}×{args.height}) -> {args.out}")


if __name__ == "__main__":
    main()
