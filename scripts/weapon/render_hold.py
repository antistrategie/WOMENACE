"""Render how a doll holds a weapon, from a pose written by WeaponHoldCheck.

Takes the OBJ (objects "body" and "weapon") and the JSON beside it, and writes one PNG a
view: the whole hold from her right and from in front, and each hand close up from two
sides. Workbench with flat colours, the weapon in orange against a pale body, so where a
hand meets the weapon reads at a glance.

Run as:
    blender --background --python render_hold.py -- --pose <dir>/<name> --out <dir>
"""

import argparse
import json
import sys
from pathlib import Path

import bpy  # type: ignore
from mathutils import Vector  # type: ignore


def to_blender(p):
    # The OBJ is Y up with -Z forward for the importer, which maps (x, y, z) to (x, -z, y).
    return Vector((p[0], -p[2], p[1]))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    parser = argparse.ArgumentParser()
    parser.add_argument("--pose", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--size", type=int, default=640)
    args = parser.parse_args(argv)

    pose = Path(args.pose)
    data = json.loads(pose.with_suffix(".json").read_text())
    nodes = {k: {a: to_blender(v) for a, v in n.items()} for k, n in data["nodes"].items()}

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.obj_import(filepath=str(pose.with_suffix(".obj")), forward_axis="NEGATIVE_Z", up_axis="Y")
    colours = {"body": (0.78, 0.80, 0.86, 1.0), "weapon": (0.95, 0.45, 0.10, 1.0)}
    for obj in bpy.data.objects:
        obj.color = colours.get(obj.name.split(".")[0], (0.5, 0.5, 0.5, 1.0))

    # The empties as small markers: the left grip in green, the muzzle in blue.
    for label, colour in (("grip", (0.1, 0.9, 0.2, 1.0)), ("muzzle", (0.2, 0.4, 1.0, 1.0))):
        if label in nodes:
            bpy.ops.mesh.primitive_uv_sphere_add(radius=0.012, location=nodes[label]["p"])
            bpy.context.object.color = colour

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "OBJECT"
    scene.display.shading.show_cavity = True
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x = scene.render.resolution_y = args.size
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.color = (0.05, 0.05, 0.06)

    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    def shot(name, target, direction, scale):
        direction = Vector(direction).normalized()
        cam.location = target + direction * 3.0
        cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
        cam_data.ortho_scale = scale
        cam_data.clip_start, cam_data.clip_end = 0.01, 10.0
        scene.render.filepath = str(Path(args.out) / f"{pose.name}_{name}.png")
        bpy.ops.render.render(write_still=True)

    # Her right is -x and her front is -y here.
    right, left = nodes["RightHand"]["p"], nodes["LeftHand"]["p"]
    middle = (right + left) / 2
    if "muzzle" in nodes:
        middle = (middle + nodes["muzzle"]["p"]) / 2
    shot("1_right", middle, (-1, -0.15, 0.1), 1.3)
    shot("2_front", middle, (-0.35, -1, 0.15), 1.3)
    shot("3_top", middle, (0, -0.05, 1), 1.3)
    shot("4_rhand_out", right, (-1, 0, 0.1), 0.36)
    shot("5_rhand_in", right, (1, 0, 0.1), 0.36)
    shot("6_rhand_under", right, (0, -0.2, -1), 0.36)
    shot("7_lhand_out", left, (1, 0, 0.1), 0.36)
    shot("8_lhand_in", left, (-1, 0, 0.1), 0.36)
    shot("9_lhand_under", left, (0, -0.2, -1), 0.36)


main()
