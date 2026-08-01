#!/usr/bin/env python3
"""Render The Sinner's item icons from the authored .blend.

Produces the three chassis-item sprites (transparent background, shaded):
  Icon           144x144   three-quarter front
  IconEquipment  912x318   side profile
  IconSkillBar   261x117   side profile

Run under Blender:
  blender -b --python scripts/vehicle/render_koleda_car.py
"""

import math
import os

import bpy
from mathutils import Vector

BLEND = "/home/justin/Downloads/Koleda_Supercar/Koleda_Supercar_base.blend"
OUT = "/home/justin/dev/github.com/antistrategie/WOMENACE/assets/additions/sprites/sinner"

bpy.ops.wm.open_mainfile(filepath=BLEND)
os.makedirs(OUT, exist_ok=True)

# neutral rest pose: no scene staging, doors closed
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
arm.animation_data.action = None
for tr in arm.animation_data.nla_tracks:
    tr.mute = True
for pb in arm.pose.bones:
    pb.location = (0, 0, 0)
    pb.rotation_quaternion = (1, 0, 0, 0)
    pb.scale = (1, 1, 1)
node = arm
while node:
    node.location = (0, 0, 0)
    node = node.parent
bpy.context.view_layer.update()

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.film_transparent = True

# bounds of the visible car, and hide excluded meshes from the renders
# exclude the same junk meshes the bake drops (-dropMeshes plane) plus LODs
meshes = [o for o in bpy.data.objects if o.type == "MESH"
          and "lod1" not in o.name and "plane" not in o.name.lower()]
for o in bpy.data.objects:
    if o.type == "MESH" and o not in meshes:
        o.hide_render = True
deps = bpy.context.evaluated_depsgraph_get()
deps.update()
lo = Vector((1e9, 1e9, 1e9))
hi = Vector((-1e9, -1e9, -1e9))
for o in meshes:
    ev = o.evaluated_get(deps)
    for v in ev.data.vertices:
        w = ev.matrix_world @ v.co
        lo = Vector(map(min, lo, w))
        hi = Vector(map(max, hi, w))
centre = (lo + hi) / 2
size = max(hi - lo)

# light rig
key = bpy.data.objects.new("key", bpy.data.lights.new("key", "SUN"))
key.data.energy = 4.0
key.rotation_euler = (math.radians(55), 0, math.radians(-35))
scene.collection.objects.link(key)
fill = bpy.data.objects.new("fill", bpy.data.lights.new("fill", "SUN"))
fill.data.energy = 1.5
fill.rotation_euler = (math.radians(60), 0, math.radians(140))
scene.collection.objects.link(fill)

target = bpy.data.objects.new("target", None)
target.location = centre
scene.collection.objects.link(target)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
track = cam.constraints.new("TRACK_TO")
track.target = target
cam.data.clip_start = size * 0.01
scene.camera = cam


def shot(name, width, height, offset, ortho_scale=None):
    cam.location = centre + offset
    if ortho_scale:
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = ortho_scale
    else:
        cam.data.type = "PERSP"
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.filepath = f"{OUT}/{name}.png"
    bpy.ops.render.render(write_still=True)
    print("rendered", name)


# three-quarter front for the square icon (nose towards camera-left)
shot("Icon", 144, 144, Vector((size * 0.72, size * 0.72, size * 0.45)))
# side profile for the wide cards, orthographic so the car fills the strip
shot("IconEquipment", 912, 318, Vector((size * 2.0, 0, size * 0.28)), ortho_scale=size * 1.15)
shot("IconSkillBar", 261, 117, Vector((size * 2.0, 0, size * 0.28)), ortho_scale=size * 1.2)
print("DONE")
