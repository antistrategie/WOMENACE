"""Close a doll's right thumb the right way, in her finished model.

The converter bakes the trigger hand's pose into the mesh and then folds the finger bones
into the hand, so the pose that ships is the mesh. It used to turn the right thumb about
each thumb bone's own X axis, which closes it on a source whose thumb bones happen to lie
that way and swings it outwards on one where they do not. The converter now measures which
way the thumb closes. This script gives a model converted before that the same thumb,
without converting her again:

1. The source model is posed twice, the old way and the new, and each vertex of the right
   hand has a place in both.
2. The finished model's right hand (the vertices that follow Hand_R) is the old pose under
   one turn, scale and shift, found from vertices of the two that share a place on the
   texture. How well they then agree says whether the old pose is the one that was baked: a
   model they do not agree on is left alone.
3. Each vertex of the finished hand moves by what its nearest source vertex moves between
   the two poses, and its normal and tangent turn with it. Every level of detail is done.

    blender --background --python scripts/doll/fix_trigger_thumb.py -- \\
        --config scripts/.config/<doll>-<outfit>.json [--dry-run]
"""

import argparse
import json
import math
import sys
from pathlib import Path

import bpy  # type: ignore
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import pmx_to_menace as conv  # noqa: E402

FINGERS = ["中指", "人指", "小指", "薬指"]
FINGER_ANGLES = [55, 75, 30]
OLD_THUMB_ANGLES = [20, 30, 40]
COMPONENTS = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
WIDTH = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}
# How far the finished hand may sit from the old pose and still be taken as that pose.
AGREE_METRES = 0.0015


def pose(armature, thumb, keep_index):
    """Pose the right hand: the fingers as the converter always has, the thumb the old way or the new."""
    bones = armature.pose.bones
    for pb in bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0.0, 0.0, 0.0)
        pb.location = (0.0, 0.0, 0.0)
    if thumb is None:
        bpy.context.view_layer.update()
        return
    for finger in FINGERS:
        if keep_index and finger == "人指":
            continue
        for joint, degrees in zip("１２３", FINGER_ANGLES):
            pb = bones.get(f"{finger}{joint}.R")
            if pb is not None:
                pb.rotation_euler.x = math.radians(degrees)
    if thumb == "old":
        for joint, degrees in zip("０１２", OLD_THUMB_ANGLES):
            pb = bones.get(f"親指{joint}.R")
            if pb is not None:
                pb.rotation_euler.x = math.radians(degrees)
        bpy.context.view_layer.update()
    else:
        bpy.context.view_layer.update()
        conv.curl_trigger_thumb(armature, [])
        bpy.context.view_layer.update()


def posed(meshes):
    """Every source vertex's place and normal in the world, as the pose leaves them."""
    graph = bpy.context.evaluated_depsgraph_get()
    places, normals = [], []
    for obj in meshes:
        mesh = obj.evaluated_get(graph).to_mesh()
        world = np.array(obj.matrix_world)
        co = np.empty(len(mesh.vertices) * 3, np.float32)
        mesh.vertices.foreach_get("co", co)
        no = np.empty(len(mesh.vertices) * 3, np.float32)
        mesh.vertices.foreach_get("normal", no)
        places.append(co.reshape(-1, 3) @ world[:3, :3].T + world[:3, 3])
        normals.append(no.reshape(-1, 3) @ world[:3, :3].T)
        obj.evaluated_get(graph).to_mesh_clear()
    return np.concatenate(places).astype(np.float64), np.concatenate(normals).astype(np.float64)


def source_uvs(meshes):
    """Each source vertex's places on the texture, glTF's way up, as {place: [vertex]}."""
    table, first = {}, 0
    for obj in meshes:
        mesh = obj.data
        layer = mesh.uv_layers.active
        if layer is not None:
            uv = np.empty(len(mesh.loops) * 2, np.float32)
            layer.data.foreach_get("uv", uv)
            uv = uv.reshape(-1, 2)
            vertex = np.empty(len(mesh.loops), np.int32)
            mesh.loops.foreach_get("vertex_index", vertex)
            for (u, v), index in zip(uv, vertex):
                table.setdefault((round(float(u), 4), round(1.0 - float(v), 4)), set()).add(first + int(index))
        first += len(mesh.vertices)
    return table


class Model:
    def __init__(self, path):
        self.path = Path(path)
        self.doc = json.loads(self.path.read_text())
        self.bin_path = self.path.parent / self.doc["buffers"][0]["uri"]
        self.data = bytearray(self.bin_path.read_bytes())

    def view(self, accessor):
        acc = self.doc["accessors"][accessor]
        bv = self.doc["bufferViews"][acc["bufferView"]]
        if "byteStride" in bv:
            raise SystemExit("an interleaved buffer view cannot be written in place")
        kind, width = COMPONENTS[acc["componentType"]], WIDTH[acc["type"]]
        start = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
        return np.frombuffer(self.data, kind, acc["count"] * width, start).reshape(acc["count"], width)

    def save(self):
        self.bin_path.write_bytes(self.data)
        self.path.write_text(json.dumps(self.doc, ensure_ascii=False, separators=(",", ":")))


def similarity(source, target):
    """The scale and turn and shift that take source points onto target points (Umeyama)."""
    mu_s, mu_t = source.mean(axis=0), target.mean(axis=0)
    s, t = source - mu_s, target - mu_t
    u, d, vt = np.linalg.svd(t.T @ s / len(source))
    sign = np.sign(np.linalg.det(u) * np.linalg.det(vt))
    fix = np.diag([1.0, 1.0, sign])
    turn = u @ fix @ vt
    scale = (d * np.diag(fix)).sum() / (s ** 2).sum() * len(source)
    return scale, turn, mu_t - scale * turn @ mu_s


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args(argv)
    cfg = json.loads(Path(args.config).read_text())
    name = Path(args.config).stem
    if cfg.get("fist_rotations") or not cfg.get("fist_pose", True):
        print(f"THUMB {name}: skipped, its hand is posed from the config's own list")
        return
    if not str(cfg.get("pmx_path", "")).lower().endswith(".pmx"):
        print(f"THUMB {name}: skipped, not a PMX source")
        return

    bpy.ops.wm.read_factory_settings(use_empty=True)
    objects = conv.import_pmx(Path(cfg["pmx_path"]))
    armature = conv.find_pmx_armature(objects)
    meshes = conv.find_pmx_meshes(objects, cfg.get("source_mesh_names", []))
    for obj in meshes:
        conv.remove_shape_keys(obj)
        conv.ensure_armature_modifier(obj, armature)
        for mod in obj.modifiers:
            if mod.type == "ARMATURE":
                mod.use_deform_preserve_volume = True
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="POSE")
    keep_index = bool(cfg.get("keep_right_index_extended", True))
    pose(armature, None, keep_index)
    rest, _ = posed(meshes)
    pose(armature, "old", keep_index)
    old, old_normals = posed(meshes)
    pose(armature, "new", keep_index)
    new, new_normals = posed(meshes)
    bpy.ops.object.mode_set(mode="OBJECT")

    # The right hand in the source: what either pose moved, which is the fingers and thumb.
    hand = (np.linalg.norm(old - rest, axis=1) > 1e-5) | (np.linalg.norm(new - rest, axis=1) > 1e-5)
    moved = np.linalg.norm(new - old, axis=1)
    if hand.sum() < 50:
        print(f"THUMB {name}: skipped, the source has no right finger bones to pose")
        return
    # How many of the source's own edges the new pose pulls long, for judging the finished one's.
    source_torn, first = 0, 0
    for obj in meshes:
        pairs = np.empty(len(obj.data.edges) * 2, np.int32)
        obj.data.edges.foreach_get("vertices", pairs)
        pairs = pairs.reshape(-1, 2) + first
        was = np.linalg.norm(old[pairs[:, 0]] - old[pairs[:, 1]], axis=1)
        now = np.linalg.norm(new[pairs[:, 0]] - new[pairs[:, 1]], axis=1)
        source_torn += int(((now - was > 0.004 / 1.2) & (now > was * 2.0)).sum())
        first += len(obj.data.vertices)
    uvs = source_uvs(meshes)
    # Every texture place a source vertex has (one on a seam has several), for telling apart
    # two parts of the hand that touch.
    source_uv = {}
    for place, vertices in uvs.items():
        for v in vertices:
            source_uv.setdefault(v, []).append(place)

    model = Model(cfg["output_path"])
    doc = model.doc
    skin = doc["skins"][0]
    joint = next(i for i, node in enumerate(skin["joints"]) if doc["nodes"][node].get("name") == "Hand_R")

    def hand_of(prim):
        joints, weights = model.view(prim["attributes"]["JOINTS_0"]), model.view(prim["attributes"]["WEIGHTS_0"])
        return ((joints == joint) * weights).sum(axis=1)

    # The fit, on the first level of detail: finished vertices wholly on the hand whose
    # place on the texture is one source vertex's of the hand.
    pairs_source, pairs_done, pairs_target = [], [], []
    for prim in doc["meshes"][0]["primitives"]:
        share = hand_of(prim)
        if not (share > 0.99).any():
            continue
        places, uv = model.view(prim["attributes"]["POSITION"]), model.view(prim["attributes"]["TEXCOORD_0"])
        for i in np.nonzero(share > 0.99)[0]:
            found = [v for v in uvs.get((round(float(uv[i][0]), 4), round(float(uv[i][1]), 4)), ()) if hand[v]]
            # A source that doubles its vertices has several at one place on the texture, and
            # in one place: any of them is the vertex.
            if len(found) == 1 or (len(found) > 1 and np.ptp(old[found], axis=0).max() < 1e-4):
                pairs_source.append(old[found[0]])
                pairs_done.append(new[found[0]])
                pairs_target.append(places[i].astype(np.float64))
    if len(pairs_source) < 50:
        print(f"THUMB {name}: skipped, only {len(pairs_source)} vertices of the finished hand found in the source")
        return
    pairs_source, pairs_done, pairs_target = np.array(pairs_source), np.array(pairs_done), np.array(pairs_target)
    # A hand that is already the new pose has been done before. The two poses differ only at
    # the thumb, so the rest of the hand gives the fit and the thumb says which pose it is.
    differ = np.linalg.norm(pairs_done - pairs_source, axis=1) > 0.001
    if differ.any() and (~differ).sum() >= 50:
        same_source, same_target = pairs_source[~differ], pairs_target[~differ]
        fit = similarity(same_source, same_target)
        # A pair that shares a texture place with another part is dropped and the fit made again.
        apart = np.linalg.norm(same_source @ (fit[0] * fit[1]).T + fit[2] - same_target, axis=1)
        good = apart < max(np.median(apart) * 4, 0.0005)
        fit = similarity(same_source[good], same_target[good])
        done_off = np.linalg.norm(pairs_done[differ] @ (fit[0] * fit[1]).T + fit[2] - pairs_target[differ], axis=1)
    else:
        done_off = np.array([np.inf])
    if float(np.median(done_off)) <= AGREE_METRES:
        print(f"THUMB {name}: already done")
        return
    scale, turn, shift = similarity(pairs_source, pairs_target)
    off = np.linalg.norm(pairs_source @ (scale * turn).T + shift - pairs_target, axis=1)
    # A pair that shares a texture place with another part is dropped and the fit made again.
    keep = off < max(np.median(off) * 4, 0.0005)
    scale, turn, shift = similarity(pairs_source[keep], pairs_target[keep])
    off = np.linalg.norm(pairs_source[keep] @ (scale * turn).T + shift - pairs_target[keep], axis=1)
    agree = float(np.percentile(off, 95))
    worst_move = float(moved[hand].max() * scale)
    print(f"THUMB {name}: {keep.sum()} of {len(keep)} pairs, scale {scale:.4f}, 95% within {agree * 1000:.2f} mm, thumb moves up to {worst_move * 1000:.1f} mm")
    if agree > AGREE_METRES:
        print(f"THUMB {name}: LEFT ALONE, the finished hand is not the old pose")
        return

    # Every source vertex about the hand, moved or not: a finished vertex of her cuff or wrist
    # has to find its own, which does not move, and not the nearest finger's.
    low, high = old[hand].min(axis=0) - 0.03, old[hand].max(axis=0) + 0.03
    source = np.nonzero(((old >= low) & (old <= high)).all(axis=1))[0]
    placed = old[source] @ (scale * turn).T + shift
    step = (new[source] - old[source]) @ (scale * turn).T
    normal_step = (new_normals[source] - old_normals[source]) @ turn.T
    placed_uv = [np.array(source_uv.get(int(v), [(np.inf, np.inf)])) for v in source]
    changed, edges_seen, torn_by_level, stranded = 0, 0, [], 0
    # The outline is drawn from the same vertices as the parts it goes round, so a set of
    # vertices is met once for each primitive that draws it and moved the first time only.
    met = set()
    # The first level of detail's hand, as it was, and how each of its vertices moved: the
    # coarser levels are cut down from it, and each of their vertices moves as the first
    # level's vertex it came from does.
    first_places, first_uv, first_step, first_normal_step = [], [], [], []
    for level, mesh in enumerate(doc["meshes"]):
        torn = 0
        if level == 1:
            first_places, first_uv = np.array(first_places), np.array(first_uv)
            first_step, first_normal_step = np.array(first_step), np.array(first_normal_step)
        for prim in mesh["primitives"]:
            share = hand_of(prim)
            which = np.nonzero(share > 0.0)[0]
            if len(which) == 0:
                continue
            attrs = prim["attributes"]
            if attrs["POSITION"] in met:
                continue
            met.add(attrs["POSITION"])
            places, normals = model.view(attrs["POSITION"]), model.view(attrs["NORMAL"])
            tangents = model.view(attrs["TANGENT"]) if "TANGENT" in attrs else None
            uv = model.view(attrs["TEXCOORD_0"])
            before_places = places.astype(np.float64).copy()
            for i in which:
                here = uv[i].astype(np.float64)
                if level == 0:
                    near = np.linalg.norm(placed - places[i], axis=1)
                    # Where the thumb lies against the palm, vertices of the two are as good
                    # as in one place: of the source vertices there, the one at the nearest
                    # place on the texture is the vertex this one came from. A vertex with no
                    # source vertex at its place is of a part the source does not pose.
                    close = np.nonzero(near <= 0.0005)[0]
                    move, turn_by = np.zeros(3), np.zeros(3)
                    if len(close) > 0:
                        apart = np.array([np.linalg.norm(placed_uv[c] - here, axis=1).min() for c in close])
                        j = int(close[apart.argmin()]) if np.isfinite(apart).any() else int(close[near[close].argmin()])
                        move, turn_by = step[j], normal_step[j]
                    elif near.min() < 0.01 and np.linalg.norm(step[int(near.argmin())]) > 0.001:
                        # Of no part the source poses, yet beside one that moves.
                        stranded += 1
                    first_places.append(places[i].astype(np.float64))
                    first_uv.append(here)
                    first_step.append(move)
                    first_normal_step.append(turn_by)
                else:
                    near = np.linalg.norm(first_places - places[i], axis=1)
                    close = np.nonzero(near <= max(near.min() * 2.0, 0.0005) + 0.002)[0]
                    j = int(close[np.linalg.norm(first_uv[close] - here, axis=1).argmin()])
                    if near[j] > 0.01:
                        continue
                    move, turn_by = first_step[j], first_normal_step[j]
                if np.linalg.norm(move) < 1e-6:
                    continue
                before = normals[i].astype(np.float64)
                after = before + turn_by
                length = np.linalg.norm(after)
                if length > 1e-6:
                    after /= length
                    if tangents is not None:
                        # The tangent takes the turn that carries the old normal to the new.
                        axis = np.cross(before, after)
                        sine, cosine = np.linalg.norm(axis), float(before @ after)
                        if sine > 1e-6:
                            axis /= sine
                            t = tangents[i, :3].astype(np.float64)
                            tangents[i, :3] = t * cosine + np.cross(axis, t) * sine + axis * (axis @ t) * (1 - cosine)
                    normals[i] = after
                # The whole step, whatever share of the vertex follows the hand: this is the
                # shape she is bound in, which every bone's share starts from.
                places[i] = places[i] + move
                changed += 1
            # A vertex left behind, or taken with the wrong part, shows as an edge pulled long.
            if "indices" in prim:
                corners = model.view(prim["indices"]).reshape(-1, 3).astype(np.int64)
                for a, b in ((0, 1), (1, 2), (2, 0)):
                    was = np.linalg.norm(before_places[corners[:, a]] - before_places[corners[:, b]], axis=1)
                    now = np.linalg.norm(places[corners[:, a]].astype(np.float64) - places[corners[:, b]], axis=1)
                    touched = np.abs(now - was) > 1e-7
                    edges_seen += int(touched.sum())
                    torn += int(((now - was > 0.004) & (now > was * 2.0)).sum())
            acc = doc["accessors"][attrs["POSITION"]]
            acc["min"], acc["max"] = [float(x) for x in places.min(axis=0)], [float(x) for x in places.max(axis=0)]
        torn_by_level.append(torn)
    print(f"THUMB {name}: {'would move' if args.dry_run else 'moved'} {changed} vertices over {len(doc['meshes'])} level(s) of detail, edges pulled long a level {torn_by_level} of {edges_seen} changed (the source's own pose pulls {source_torn}), {stranded} beside the thumb left where they were")
    if not args.dry_run:
        model.save()


main()
