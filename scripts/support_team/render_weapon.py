"""Side render of a support team's weapon on a transparent background, flat textured, for its
equipment and skill bar icons (make_team_sprites.py --weapon-render straightens and fits it).

    blender --background --factory-startup --python render_weapon.py -- <fbx> <diffuse> <out.png> <w> <h> [<view>]

prepare_team.py runs it as its `render` step. Only the lod0 meshes are kept.
"""
import sys
import bpy
import mathutils

fbx, diffuse, out, w, h = sys.argv[sys.argv.index("--") + 1:][:5]
w, h = int(w), int(h)
view = (sys.argv[sys.argv.index("--") + 1:] + ["x"])[5]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
for o in [m for m in meshes if "lod0" not in m.name]:
    bpy.data.objects.remove(o, do_unlink=True)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]

img = bpy.data.images.load(diffuse)
mat = bpy.data.materials.new("launcher")
mat.use_nodes = True
tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
tex.image = img
bsdf = mat.node_tree.nodes["Principled BSDF"]
mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
for o in meshes:
    o.data.materials.clear()
    o.data.materials.append(mat)
    print("mesh", o.name, [round(v, 3) for v in o.dimensions])

lo = mathutils.Vector((1e9,) * 3)
hi = mathutils.Vector((-1e9,) * 3)
for o in meshes:
    for c in o.bound_box:
        p = o.matrix_world @ mathutils.Vector(c)
        lo = mathutils.Vector(map(min, lo, p))
        hi = mathutils.Vector(map(max, hi, p))
centre, size = (lo + hi) / 2, hi - lo
print("bounds", [round(v, 3) for v in size])

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
scene.display.shading.show_cavity = True
scene.display.shading.cavity_type = "BOTH"
scene.render.film_transparent = True
scene.render.resolution_x, scene.render.resolution_y = w, h
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"

# Lay the launcher's long axis along world X: a parent at its centre is turned so it is.
longest = max(range(3), key=lambda i: size[i])
pivot = bpy.data.objects.new("pivot", None)
scene.collection.objects.link(pivot)
pivot.location = centre
for o in [o for o in bpy.data.objects if o.parent is None and o is not pivot]:
    o.parent = pivot
    o.matrix_parent_inverse = pivot.matrix_world.inverted()
pivot.rotation_euler = {0: (0, 0, 0), 1: (0, 0, -1.5708), 2: (0, 1.5708, 0)}[longest]
bpy.context.view_layer.update()
# A weapon can run at a slight angle to the mesh axes. The render keeps a margin so the 2D step
# (make_team_sprites.py) can straighten it by the tube's measured angle.
lo = mathutils.Vector((1e9,) * 3)
hi = mathutils.Vector((-1e9,) * 3)
for o in meshes:
    for c in o.bound_box:
        p = o.matrix_world @ mathutils.Vector(c)
        lo = mathutils.Vector(map(min, lo, p))
        hi = mathutils.Vector(map(max, hi, p))
centre, size = (lo + hi) / 2, hi - lo
print("laid bounds", [round(v, 4) for v in size])

cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.type = "ORTHO"
# `view` picks the side: y looks from -Y, -y from +Y, z from above.
side = {"y": (0, -1, 0), "-y": (0, 1, 0), "z": (0, 0, 1), "x": (0, -1, 0), "-x": (0, 1, 0)}[view]
d = mathutils.Vector(side)
cam.location = centre + d * size.length * 4
cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler() if view != "z" else (0, 0, 0)
span_h = size[2] if view != "z" else size[1]
cam.data.ortho_scale = max(size[0], span_h * w / h) * 1.25
cam.data.clip_start = size.length * 0.01
cam.data.clip_end = size.length * 10
bpy.ops.render.render(write_still=False)
bpy.data.images["Render Result"].save_render(out)
print("wrote", out)
