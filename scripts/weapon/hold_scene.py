"""Open a weapon's source file in Blender with a doll holding it, to place it by eye.

The scene is the weapon's raw.glb (its mesh and its muzzle and weapon_hand_l empties under
their root) and three reference objects made from a pose written by WeaponHoldCheck, which
stands the doll as the game stood a unit in a mission:

  ref_body    the doll, still. Her right hand is where the game holds the weapon's root.
  ref_hand_r  her right hand again, in blue, so it reads against the weapon.
  ref_hand_l  her left hand, in green, a child of weapon_hand_l: it goes where the empty goes,
              turned as the game turns it. In a stance the game leaves that hand out of (a
              blade held in one hand) it stays where the stance has it.

Move the root to move the whole weapon in her hand, or the mesh and the empties one by one.
The reference objects cannot be picked. "Save placement" in the sidebar's WOMENACE tab writes
where the root's children now stand into raw.glb, and nothing else of the file changes.

The doll stands upright. The weapon's root hangs from hold_frame, which stands where her
hand holds the weapon, and a placement is saved as it stands in that frame.

Run through hold_scene.sh, which makes the pose first.
"""

import argparse
import json
import struct
import sys
from pathlib import Path

import bpy  # type: ignore
from mathutils import Matrix, Vector  # type: ignore


def read_pose(pose):
    data = json.loads(pose.with_suffix(".json").read_text())
    nodes = {k: {a: Vector(v) for a, v in n.items()} for k, n in data["nodes"].items()}
    vertices, faces, current = [], {}, None
    for line in pose.with_suffix(".obj").read_text().splitlines():
        if line.startswith("v "):
            vertices.append(Vector([float(x) for x in line.split()[1:4]]))
        elif line.startswith("o "):
            current = line[2:].strip()
            faces[current] = []
        elif line.startswith("f "):
            faces[current].append([int(x.split("/")[0]) - 1 for x in line.split()[1:4]])
    weapon = nodes["weapon"]

    def place(point):
        # The pose is Y up with its front along -Z for Blender, which stands Z up.
        return Vector((point.x, -point.z, point.y))

    # Where the weapon's own frame stands in that world. A point of the weapon's file, in
    # Blender's axes (a, b, c), is Unity's (-a, c, -b) in the weapon's frame: the pose's points
    # and axes are mirrored alike, so the weapon's axes carry it straight into the pose.
    frame = Matrix.Identity(4)
    for column, axis in enumerate((-place(weapon["x"]), -place(weapon["z"]), place(weapon["y"]))):
        for row in range(3):
            frame[row][column] = axis[row]
    for row in range(3):
        frame[row][3] = place(weapon["p"])[row]
    return [place(v) for v in vertices], faces, {k: place(n["p"]) for k, n in nodes.items()}, frame


def make(name, vertices, faces, colour):
    used = sorted({i for face in faces for i in face})
    index = {old: new for new, old in enumerate(used)}
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([vertices[i] for i in used], [], [[index[i] for i in face] for face in faces])
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    obj.color = colour
    obj.hide_select = True
    bpy.context.scene.collection.objects.link(obj)
    return obj


def to_gltf(obj, frame):
    # Where the object stands in the weapon's own frame, which the doll's hand holds.
    location, rotation, scale = (frame.matrix_world.inverted() @ obj.matrix_world).decompose()
    return (
        [location.x, location.z, -location.y],
        [rotation.x, rotation.z, -rotation.y, rotation.w],
        [scale.x, scale.z, scale.y],
    )


def save(path, root, frame):
    data = Path(path).read_bytes()
    chunks, offset = [], 12
    while offset < len(data):
        length = struct.unpack("<I", data[offset:offset + 4])[0]
        chunks.append((data[offset + 4:offset + 8], data[offset + 8:offset + 8 + length]))
        offset += 8 + length
    gltf = json.loads(next(body for kind, body in chunks if kind == b"JSON").decode("utf-8"))
    by_name = {node.get("name"): node for node in gltf["nodes"]}
    written = []
    for child in root.children:
        node = by_name.get(child.name)
        if node is None or child.name.startswith("ref_"):
            continue
        translation, rotation, scale = to_gltf(child, frame)
        node.pop("matrix", None)
        if max(abs(t) for t in translation) > 1e-7:
            node["translation"] = translation
        else:
            node.pop("translation", None)
        if abs(abs(rotation[3]) - 1) > 1e-6:
            # A rotation and its negative are the same turn: the file keeps the sign it had.
            was = node.get("rotation")
            if was is not None and sum(a * b for a, b in zip(was, rotation)) < 0:
                rotation = [-c for c in rotation]
            node["rotation"] = rotation
        else:
            node.pop("rotation", None)
        if max(abs(s - 1) for s in scale) > 1e-6:
            node["scale"] = scale
        else:
            node.pop("scale", None)
        written.append(child.name)
    for key in ("translation", "rotation", "scale", "matrix"):
        by_name[root.name].pop(key, None)
    text = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    text += b" " * ((4 - len(text) % 4) % 4)
    body = b""
    for kind, chunk in chunks:
        chunk = text if kind == b"JSON" else chunk + b"\x00" * ((4 - len(chunk) % 4) % 4)
        body += struct.pack("<I", len(chunk)) + kind + chunk
    Path(path).write_bytes(b"glTF" + struct.pack("<II", 2, 12 + len(body)) + body)
    return written


class WOMENACE_OT_save_placement(bpy.types.Operator):
    bl_idname = "womenace.save_placement"
    bl_label = "Save placement"
    bl_description = "Write where the weapon's mesh and empties stand into raw.glb"

    def execute(self, context):
        scene = context.scene
        root = bpy.data.objects.get(scene["womenace_root"])
        written = save(scene["womenace_glb"], root, bpy.data.objects.get(scene["womenace_frame"]))
        self.report({"INFO"}, f"Wrote {', '.join(written)} to {scene['womenace_glb']}. Bake with: {scene['womenace_bake']}")
        return {"FINISHED"}


class WOMENACE_PT_hold(bpy.types.Panel):
    bl_label = "Weapon hold"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "WOMENACE"

    def draw(self, context):
        column = self.layout.column()
        column.label(text=f"Weapon: {context.scene.get('womenace_weapon', '?')}")
        column.label(text="Move the root for the whole weapon,")
        column.label(text="or the mesh and empties one by one.")
        column.operator("womenace.save_placement", icon="FILE_TICK")


def frame_hand():
    try:
        look_at_hand()
    except Exception as error:  # The scene is usable without the view being set up.
        print(f"hold_scene: view not set up: {error}")
    return None


def look_at_hand():
    for window in bpy.context.window_manager.windows:
        for area in window.screen.areas:
            if area.type != "VIEW_3D":
                continue
            space = area.spaces.active
            space.shading.type = "SOLID"
            space.shading.color_type = "OBJECT"
            space.show_region_ui = True
            region = next(r for r in area.regions if r.type == "WINDOW")
            target = bpy.data.objects.get("ref_hand_r")
            if target is not None:
                space.region_3d.view_location = sum((Vector(c) for c in target.bound_box), Vector()) / 8
                space.region_3d.view_distance = 0.6


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    parser = argparse.ArgumentParser()
    parser.add_argument("--weapon", required=True)
    parser.add_argument("--glb", required=True)
    parser.add_argument("--pose", required=True)
    args = parser.parse_args(argv)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=args.glb)
    root = next(o for o in bpy.data.objects if o.parent is None and o.children)
    if any(abs(c) > 1e-6 for c in (*root.location, *root.rotation_euler)):
        raise SystemExit("the weapon's root is moved in its file: it is expected at its file's origin")
    for obj in root.children:
        if obj.type == "EMPTY":
            obj.empty_display_type = "ARROWS"
            obj.empty_display_size = 0.04
            obj.show_in_front = True
            obj.show_name = True
        else:
            obj.color = (0.95, 0.5, 0.12, 1.0)
    root.empty_display_size = 0.08
    root.show_name = True

    vertices, faces, nodes, stands = read_pose(Path(args.pose))
    # The weapon hangs from a frame that stands where the doll's hand holds it, so she stands
    # upright in the scene whatever way her hand has the weapon turned.
    frame = bpy.data.objects.new("hold_frame", None)
    frame.empty_display_size = 0.02
    frame.hide_select = True
    bpy.context.scene.collection.objects.link(frame)
    frame.matrix_world = stands
    root.parent = frame
    root.matrix_parent_inverse = Matrix.Identity(4)
    bpy.context.view_layer.update()

    def middles(tris):
        return {tuple(round(c, 4) for c in (vertices[a] + vertices[b] + vertices[c]) / 3) for a, b, c in tris}

    hands = middles(faces.get("hand_r", [])) | middles(faces.get("hand_l", []))
    body = [f for f in faces["body"] if tuple(round(c, 4) for c in (vertices[f[0]] + vertices[f[1]] + vertices[f[2]]) / 3) not in hands]
    make("ref_body", vertices, body, (0.75, 0.77, 0.82, 1.0))
    make("ref_hand_r", vertices, faces.get("hand_r", []), (0.2, 0.45, 0.95, 1.0))
    left = make("ref_hand_l", vertices, faces.get("hand_l", []), (0.2, 0.75, 0.45, 1.0))
    grip = bpy.data.objects.get("weapon_hand_l")
    data = json.loads(Path(args.pose).with_suffix(".json").read_text())
    if grip is not None and data.get("leftHandFollows", True):
        bpy.context.view_layer.update()
        off = (grip.matrix_world.translation - nodes["grip"]).length if "grip" in nodes else 0
        if off > 0.002:
            print(f"hold_scene: the pose's weapon_hand_l is {off * 1000:.0f} mm from raw.glb's: the prefab is not baked from this file")
        left.parent = grip
        left.matrix_parent_inverse = grip.matrix_world.inverted()

    scene = bpy.context.scene
    scene["womenace_weapon"] = args.weapon
    scene["womenace_glb"] = args.glb
    scene["womenace_root"] = root.name
    scene["womenace_frame"] = frame.name
    scene["womenace_bake"] = f"python3 scripts/weapon/shade_weapons.py --bake {args.weapon}"
    bpy.utils.register_class(WOMENACE_OT_save_placement)
    bpy.utils.register_class(WOMENACE_PT_hold)
    bpy.app.timers.register(frame_hand, first_interval=0.5)


main()
