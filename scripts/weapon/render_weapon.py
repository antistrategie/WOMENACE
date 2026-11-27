"""Render a weapon GLB or FBX to a transparent PNG for icon use.

Three shading modes:

* `--mode flat` (default) uses Blender's Workbench render engine with the
  Texture shading mode so the output shows just the BaseColor texture with
  no PBR shininess. Fastest path, best for clean inventory icons.
* `--mode shaded` uses Eevee with the GLB's Principled BSDF material plus a
  three-point light rig. Produces a PBR-shaded image with rim highlights.
* `--mode studio` uses Workbench with the Texture colour type under studio
  light with cavity shading, so a flat diffuse still shows the model's form.

`--diffuse` replaces every material with one showing that texture, for a model
whose own materials do not import (a GFL2 FBX export). `--lod0-only` drops
every mesh whose name lacks `lod0`, which keeps a model's lower LODs out of
the render. `--axis` lays the model's longest dimension along X about its
centre and looks at it from that axis in place of `--view`, for a model whose
import orientation is not known in advance: `minus_y` and `plus_y` look from
either side, `plus_z` from above.

`--hide-groups` leaves out the pieces whose strongest vertex group is named (a
mortar's rounds, which lie beside it and hang above it at rest). `--fold`
folds a weapon that stands at rest for carrying, as vanilla's mortar icons
show it, from a JSON of the `fold` block of a support team's `weapon_render`
and the `split` rules of its states.json, which part the prop's shared bones as
the animation states do. The legs swing on their hinge at the top until their
feet come in beside the tube short of its base, the strut between them halfway
between their two turns, and the whole is framed with the base to the left, the
tube rising `tilt` degrees to the right, turned `yaw` degrees to show the base's
face. `roll` turns it about the tube first: at 90 degrees the folded legs lie
straight behind the barrel, which hides them and the bipod's joint that stands
off the tube. A folded weapon is framed already and seen from above.

In both modes the background renders as alpha=0 (transparent PNG).

Run as:
    blender --background --python render_weapon.py -- \\
        (--glb <path.glb> | --fbx <path.fbx>) --out <path.png> \\
        [--width 912] [--height 318] [--padding 0.05] \\
        [--view side|front|back|top|three_quarter | --axis minus_y|plus_y|plus_z] \\
        [--mode flat|shaded|studio] [--diffuse <texture.png>] [--lod0-only] \\
        [--hide-groups <group,...>] [--fold <fold.json>]
"""

import argparse
import json
import math
import sys
from pathlib import Path

try:
    import bpy  # type: ignore
    import mathutils  # type: ignore
    from mathutils import Matrix, Vector  # type: ignore
except ImportError as exc:  # pragma: no cover - Blender-only entry point
    raise SystemExit("This script must be run inside Blender.") from exc

# A folded support-team prop is parted by the same cuts its states are.
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "support_team"))
from prop_cuts import cut_takes, pieces_of  # noqa: E402


# -----------------------------------------------------------------------------
# Scene reset + GLB import
# -----------------------------------------------------------------------------


def reset_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)


def import_model(path: Path) -> list[bpy.types.Object]:
    if not path.exists():
        raise SystemExit(f"Model not found: {path}")
    pre = set(bpy.data.objects)
    if path.suffix.lower() == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(path))
    else:
        bpy.ops.import_scene.gltf(filepath=str(path))
    return [o for o in bpy.data.objects if o not in pre]


def keep_lod0(objects: list[bpy.types.Object]) -> list[bpy.types.Object]:
    for obj in [o for o in objects if o.type == "MESH" and "lod0" not in o.name]:
        bpy.data.objects.remove(obj, do_unlink=True)
    return [o for o in bpy.data.objects if o in set(objects)]


def force_diffuse(objects: list[bpy.types.Object], texture: Path) -> None:
    mat = bpy.data.materials.new("diffuse")
    mat.use_nodes = True
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(str(texture))
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    for obj in objects:
        if obj.type == "MESH":
            obj.data.materials.clear()
            obj.data.materials.append(mat)


def hide_groups(meshes: list, groups: set) -> None:
    """Leave out every vertex whose strongest group is named (a mortar's rounds, which lie on the
    ground beside it and hang in the air above it in its rest pose)."""
    import bmesh
    for o in meshes:
        names = {g.index: g.name for g in o.vertex_groups}
        gone = {v.index for v in o.data.vertices
                if v.groups and names[max(v.groups, key=lambda g: g.weight).group] in groups}
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in gone], context="VERTS")
        bm.to_mesh(o.data)
        bm.free()
        print(f"[info] left out {len(gone)} vertices of {sorted(groups)} from {o.name}")


def fold(meshes: list, spec: dict) -> None:
    """Bake the folded pose into the meshes, in framed space, and drop the armature."""
    f = spec["fold"]
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    co, part = {}, {}
    for mesh in meshes:
        rel = arm.matrix_world.inverted() @ mesh.matrix_world
        names = {g.index: g.name for g in mesh.vertex_groups}
        for v in mesh.data.vertices:
            co[mesh, v.index] = rel @ v.co
            if v.groups:
                part[mesh, v.index] = names[max(v.groups, key=lambda g: g.weight).group]
        # The cuts build_team_states.py parts the prop with, on the prop's rest.
        pieces = pieces_of(mesh)
        for cut in (c for c in spec["split"] if c["prop"] == f["prop"]):
            for piece in pieces:
                if not all(part.get((mesh, i)) == cut["from"] for i in piece) \
                        or not cut_takes(cut, [co[mesh, i] for i in piece]):
                    continue
                for i in piece:
                    part[mesh, i] = cut["bone"]

    def pts(name):
        return [co[k] for k, p in part.items() if p == name]

    base = sum(pts(f["base"]), Vector()) / len(pts(f["base"]))
    tube = pts(f["tube"])
    muzzle = max(tube, key=lambda p: (p - base).length)
    axis = (base - muzzle).normalized()
    breech = max(tube, key=lambda p: (p - muzzle).dot(axis))
    turns, hinges, sides = {}, [], []
    for leg in f["legs"]:
        ps = pts(leg)
        top = max(p.y for p in ps)
        crown = [p for p in ps if p.y > top - 0.03]
        hinge = sum(crown, Vector()) / len(crown)
        foot = max(ps, key=lambda p: (p - hinge).length)
        off = hinge - muzzle
        side = (off - axis * off.dot(axis)).normalized()
        # `spread` keeps that share of the leg's own distance out to the side (0 closes the legs).
        target = breech - axis * f.get("short", 0.25) + side * f.get("gap", 0.06) \
            + Vector((off.x * f.get("spread", 1.0), 0.0, 0.0))
        q = (foot - hinge).normalized().rotation_difference((target - hinge).normalized())
        turns[leg] = Matrix.Translation(hinge) @ q.to_matrix().to_4x4() @ Matrix.Translation(-hinge)
        hinges.append((hinge, q))
        sides.append(side)
    # The base turns in its socket about the tube, `base_turn` degrees: a quarter turn stands a
    # long, narrow base plate edge on where the roll would show its whole breadth.
    if f.get("base_turn"):
        q = Matrix.Rotation(math.radians(f["base_turn"]), 4, axis)
        turns[f["base"]] = Matrix.Translation(breech) @ q @ Matrix.Translation(-breech)
    if f.get("strut"):
        hinge = (hinges[0][0] + hinges[-1][0]) / 2
        q = hinges[0][1].slerp(hinges[-1][1], 0.5)
        turns[f["strut"]] = Matrix.Translation(hinge) @ q.to_matrix().to_4x4() @ Matrix.Translation(-hinge)
    right = -axis
    down = sum(sides, Vector()).normalized()
    up = -(down - right * down.dot(right)).normalized()
    framed = Matrix((right, up, right.cross(up))).to_4x4() @ Matrix.Translation(-(muzzle + base) / 2)
    frame = Matrix.Rotation(math.radians(f.get("tilt", 20)), 4, "Z") \
        @ Matrix.Rotation(math.radians(f.get("yaw", 20)), 4, "Y") \
        @ Matrix.Rotation(math.radians(f.get("roll", 0)), 4, "X") @ framed
    # Seen from +Z the base goes to the left: half a turn in the picture keeps the tube's slope.
    if (frame @ base).x > (frame @ muzzle).x:
        frame = Matrix.Rotation(math.pi, 4, "Z") @ frame
    # `base_bend` hinges the base at the breech in the picture's plane, so a base that stands
    # square across the tube leans back along it and the weapon is drawn longer and lower.
    bent = {}
    if f.get("base_bend"):
        at = frame @ breech
        bent[f["base"]] = Matrix.Translation(at) @ Matrix.Rotation(math.radians(f["base_bend"]), 4, "Z") \
            @ Matrix.Translation(-at)
    for mesh in meshes:
        for v in mesh.data.vertices:
            p = part.get((mesh, v.index))
            v.co = bent.get(p, Matrix.Identity(4)) @ frame @ (turns.get(p, Matrix.Identity(4)) @ co[mesh, v.index])
        mesh.parent = None
        mesh.matrix_world = Matrix.Identity(4)
        for m in list(mesh.modifiers):
            mesh.modifiers.remove(m)
    bpy.data.objects.remove(arm, do_unlink=True)
    print("folded", sorted(turns), "about the tube from", tuple(round(v, 3) for v in muzzle))


def lay_along_x(objects: list[bpy.types.Object]) -> None:
    """Turn the model about its centre so its longest dimension runs along X."""
    lo, hi = _mesh_bounds_world(objects)
    size = hi - lo
    longest = max(range(3), key=lambda i: size[i])
    pivot = bpy.data.objects.new("pivot", None)
    bpy.context.scene.collection.objects.link(pivot)
    pivot.location = (lo + hi) * 0.5
    for obj in [o for o in bpy.data.objects if o.parent is None and o is not pivot]:
        obj.parent = pivot
        obj.matrix_parent_inverse = pivot.matrix_world.inverted()
    pivot.rotation_euler = {0: (0, 0, 0), 1: (0, 0, -1.5708), 2: (0, 1.5708, 0)}[longest]
    bpy.context.view_layer.update()


def _mesh_bounds_world(objects: list[bpy.types.Object]) -> tuple[mathutils.Vector, mathutils.Vector]:
    INF = float("inf")
    lo = mathutils.Vector((INF, INF, INF))
    hi = mathutils.Vector((-INF, -INF, -INF))
    # The evaluated mesh, so a skinned model is measured in the pose it renders in.
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for obj in objects:
        if obj.type != "MESH" or obj.data is None:
            continue
        m = obj.matrix_world
        for v in obj.evaluated_get(depsgraph).data.vertices:
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

# --axis name -> camera direction, for a model laid along X by lay_along_x.
_AXES = {
    "minus_y": mathutils.Vector((0.0, -1.0, 0.0)),
    "plus_y":  mathutils.Vector((0.0,  1.0, 0.0)),
    "plus_z":  mathutils.Vector((0.0,  0.0, 1.0)),
}


Basis = tuple[mathutils.Vector, mathutils.Matrix, mathutils.Vector]


def _view_basis(view: str) -> Basis:
    """Camera direction, rotation and the up the lights are placed by, for a --view name."""
    if view not in _VIEWS:
        raise SystemExit(f"Unknown view {view!r}, valid: {list(_VIEWS)}")
    direction, up = _VIEWS[view]
    direction = direction.normalized()
    # Build a rotation that maps Blender's camera-forward (-Z) onto
    # `-direction`, with `up` aligned to camera-up (+Y).
    forward = -direction
    cam_z = -forward                # camera looks down -Z, so Z basis is -forward
    cam_x = up.cross(forward).normalized()
    if cam_x.length == 0:
        # Up and forward are parallel, so pick an alternate up.
        cam_x = mathutils.Vector((1.0, 0.0, 0.0)) if abs(forward.z) > 0.9 else mathutils.Vector((0.0, 0.0, 1.0))
        cam_x = cam_x.cross(forward).normalized()
    cam_y = forward.cross(cam_x).normalized()
    return direction, mathutils.Matrix((
        (cam_x.x, cam_y.x, cam_z.x),
        (cam_x.y, cam_y.y, cam_z.y),
        (cam_x.z, cam_y.z, cam_z.z),
    )), up


def _axis_basis(axis: str) -> Basis:
    """Camera direction, rotation and up for an --axis name. The image's up is +Z, or +Y from above."""
    direction = _AXES[axis]
    if axis == "plus_z":
        rotation = mathutils.Matrix.Identity(3)
    else:
        rotation = (-direction).to_track_quat("-Z", "Y").to_matrix()
    return direction, rotation, rotation.col[1].copy()


def add_orthographic_camera(
    objects: list[bpy.types.Object],
    basis: Basis,
    width_px: int,
    height_px: int,
    padding: float,
) -> bpy.types.Object:
    direction, rotation, _ = basis
    cam_x = rotation.col[0].copy()
    cam_y = rotation.col[1].copy()

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

    cam.matrix_world = mathutils.Matrix.Translation(camera_pos) @ rotation.to_4x4()

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


def add_three_point_lights(objects: list[bpy.types.Object], basis: Basis) -> None:
    """Three-point rig in world space, sized by the mesh's bounding sphere.
    Key light from upper-front, fill from opposite-lower for ambient lift,
    rim from behind for silhouette pop."""
    lo, hi = _mesh_bounds_world(objects)
    centre = (lo + hi) * 0.5
    radius = max((hi - lo).length, 0.3)

    view_dir = basis[0].normalized()
    up = basis[2].normalized()
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


def configure_workbench_render(width_px: int, height_px: int, studio: bool) -> None:
    """Workbench engine in Texture shading mode shows the BaseColor texture
    with no PBR shininess. Flat film + transparent background output as a
    clean RGBA PNG suitable for UI icons. `studio` lights it with studio light
    and cavity shading instead of flat light."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"

    workbench = scene.display
    if studio:
        workbench.shading.light = "STUDIO"
        workbench.shading.color_type = "TEXTURE"
        workbench.shading.show_cavity = True
        workbench.shading.cavity_type = "BOTH"
        scene.view_settings.view_transform = "Standard"
    elif hasattr(workbench, "shading"):
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
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--glb", type=Path)
    source.add_argument("--fbx", type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--width", type=int, default=912)
    parser.add_argument("--height", type=int, default=318)
    parser.add_argument("--padding", type=float, default=0.05,
                        help="Fraction of bbox added as margin on each side.")
    parser.add_argument("--view", choices=list(_VIEWS.keys()), default="side")
    parser.add_argument("--axis", choices=list(_AXES.keys()),
                        help="Lay the longest dimension along X and look from this axis, in place of --view.")
    parser.add_argument("--mode", choices=["flat", "shaded", "studio"], default="flat",
                        help="flat = Workbench/no lighting, shaded = Eevee + three-point rig, "
                             "studio = Workbench with studio light and cavity.")
    parser.add_argument("--diffuse", type=Path, help="Texture to show on every mesh in place of its materials.")
    parser.add_argument("--lod0-only", action="store_true", help="Drop meshes whose name lacks lod0.")
    parser.add_argument("--hide-groups", default="", help="Comma-separated vertex groups whose pieces are left out.")
    parser.add_argument("--fold", type=Path, help="Fold the weapon for carrying (a JSON of its fold block and split rules).")
    return parser.parse_args(argv)


def main() -> None:
    args = parse_args()
    reset_scene()
    model = args.glb or args.fbx
    objects = import_model(model)
    print(f"[info] imported {len(objects)} object(s) from {model.name}")
    if args.lod0_only:
        objects = keep_lod0(objects)
    meshes = [o for o in objects if o.type == "MESH"]
    if args.fold:
        fold(meshes, json.loads(args.fold.read_text()))
        objects = [o for o in bpy.data.objects if o in set(objects)]
    groups = set(filter(None, args.hide_groups.split(",")))
    if groups:
        hide_groups(meshes, groups)
    if args.diffuse:
        force_diffuse(objects, args.diffuse)
    if args.fold:
        basis = _axis_basis("plus_z")
    elif args.axis:
        lay_along_x(objects)
        basis = _axis_basis(args.axis)
    else:
        basis = _view_basis(args.view)

    add_orthographic_camera(objects, basis, args.width, args.height, args.padding)
    if args.mode == "shaded":
        add_three_point_lights(objects, basis)
        configure_eevee_render(args.width, args.height)
    else:
        configure_workbench_render(args.width, args.height, args.mode == "studio")

    args.out.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.scene.render.filepath = str(args.out)
    bpy.ops.render.render(write_still=True)
    print(f"[done] {args.mode} render, {'folded' if args.fold else args.axis or args.view} view ({args.width}×{args.height}) -> {args.out}")


if __name__ == "__main__":
    main()
