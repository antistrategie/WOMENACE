#!/usr/bin/env python3
"""Turn a GFL2 scene effects dump (vfx_dump.py in ~/gfl2-extract/scenes) into plain text the Unity
editor rebuilds from (Womenace.EditorTools.BuildRoomVfx), under
unity/Assets/Authored/environment/<name>/:

  textures/<texture name>.png   every texture the effects use
  textures.txt                  per texture: name, GFL2 wrap mode (0 repeat, 1 clamp), sRGB (1 or 0)
  meshes/<mesh name>.txt        "v x y z", "n x y z", "u x y", "c r g b a", "s i i i ..." per submesh
  materials.txt                 MAT name <tab> shader, then TEX/F/C/KW lines
  nodes.txt                     NODE/COMP lines, and P lines setting each particle system and
                                particle renderer property by its SerializedProperty path

Particle system typetree field names are Unity's serialized names, so a path built from them is
what SerializedObject.FindProperty takes. Values: plain numbers, "curve" (pre;post;keys), "gradient"
(mode;colour keys;alpha keys), "colour", "vector", "ref" (a material or mesh by name, or a Unity built-in mesh by id), and
"size" for an array's length ahead of its elements.

usage: vfx_to_unity.py <dump dir with vfx.json> <out name>
"""
import json
import shutil
import sys
from pathlib import Path

import numpy as np

REPO = Path(__file__).resolve().parents[2]
dump = Path(sys.argv[1])
out = REPO / "unity/Assets/Authored/environment" / sys.argv[2]
(out / "textures").mkdir(parents=True, exist_ok=True)
(out / "meshes").mkdir(parents=True, exist_ok=True)
data = json.loads((dump / "vfx.json").read_text())

# Textures and meshes by readable, unique names.
texture_names, mesh_names, material_names = {}, {}, {}
texture_lines = []


def unique(name, taken):
    base, n = name, 1
    while name in taken:
        n += 1
        name = f"{base}_{n}"
    return name


for key, info in data["textures"].items():
    if "error" in info or not (dump / "textures" / f"{key}.png").exists():
        continue
    name = unique(info["name"], set(texture_names.values()))
    texture_names[key] = name
    shutil.copy(dump / "textures" / f"{key}.png", out / "textures" / f"{name}.png")
    texture_lines.append(f"{name}\t{info.get('wrap', 0)}\t{int(info.get('srgb', True))}")

for key, info in data["meshes"].items():
    if "error" in info:
        continue
    name = unique(info["name"], set(mesh_names.values()))
    mesh_names[key] = name
    arrays = np.load(dump / "meshes" / f"{key}.npz")
    lines = [f"v {x:.6f} {y:.6f} {z:.6f}" for x, y, z in arrays["positions"]]
    if "normals" in arrays:
        lines += [f"n {x:.6f} {y:.6f} {z:.6f}" for x, y, z in arrays["normals"]]
    if "uv0" in arrays:
        lines += [f"u {r[0]:.6f} {r[1]:.6f}" for r in arrays["uv0"]]
    if "colours" in arrays:
        lines += [f"c {r[0]:.4f} {r[1]:.4f} {r[2]:.4f} {r[3]:.4f}" for r in arrays["colours"]]
    i = 0
    while f"sub{i}" in arrays:
        lines.append("s " + " ".join(str(int(v)) for v in arrays[f"sub{i}"]))
        i += 1
    (out / "meshes" / f"{name}.txt").write_text("\n".join(lines) + "\n")

# Each material's lines, written once the nodes show which ones something draws with.
mat_blocks = {}
for key, m in data["materials"].items():
    if "error" in m:
        continue
    name = unique(m["name"], set(material_names.values()))
    material_names[key] = name
    mat_lines = mat_blocks[name] = []
    mat_lines.append(f"MAT\t{name}\t{m['shader']}")
    for slot, t in m["textures"].items():
        if t["texture"] in texture_names:
            mat_lines.append(f"TEX\t{slot}\t{texture_names[t['texture']]}\t{t['scale'][0]}\t{t['scale'][1]}\t{t['offset'][0]}\t{t['offset'][1]}")
    for k, v in m["floats"].items():
        mat_lines.append(f"F\t{k}\t{v}")
    for k, v in m["colours"].items():
        mat_lines.append(f"C\t{k}\t" + "\t".join(str(x) for x in v))
    kw = m["keywords"] or []
    if isinstance(kw, str):
        kw = kw.split()
    mat_lines.append("KW\t" + " ".join(kw))


def is_curve(v):
    return isinstance(v, dict) and "m_Curve" in v and "m_PreInfinity" in v


def is_gradient(v):
    return isinstance(v, dict) and "key0" in v and "ctime0" in v


def is_colour(v):
    return isinstance(v, dict) and set(v) == {"r", "g", "b", "a"}


def is_vector(v):
    return isinstance(v, dict) and set(v) in ({"x", "y"}, {"x", "y", "z"}, {"x", "y", "z", "w"})


def curve_text(c):
    keys = []
    for k in c["m_Curve"]:
        keys.append(",".join(str(k.get(f, 0)) for f in ("time", "value", "inSlope", "outSlope", "weightedMode", "inWeight", "outWeight")))
    return f"{c['m_PreInfinity']};{c['m_PostInfinity']};" + "|".join(keys)


def gradient_text(g):
    colours = "|".join(f"{g[f'key{i}']['r']},{g[f'key{i}']['g']},{g[f'key{i}']['b']},{g[f'ctime{i}'] / 65535.0}" for i in range(g["m_NumColorKeys"]))
    alphas = "|".join(f"{g[f'key{i}']['a']},{g[f'atime{i}'] / 65535.0}" for i in range(g["m_NumAlphaKeys"]))
    return f"{g.get('m_Mode', 0)};{colours};{alphas}"


def flatten(prefix, value, emit):
    if value is None:
        return
    if is_curve(value):
        emit(prefix, "curve", curve_text(value))
    elif is_gradient(value):
        emit(prefix, "gradient", gradient_text(value))
    elif is_colour(value):
        emit(prefix, "colour", f"{value['r']},{value['g']},{value['b']},{value['a']}")
    elif is_vector(value):
        emit(prefix, "vector", ",".join(str(value[a]) for a in "xyzw" if a in value))
    elif isinstance(value, dict) and "kind" in value:
        if "builtin" in value:
            emit(prefix, "ref", f"builtin:{value['builtin']}")
        elif value.get("ref") in material_names:
            emit(prefix, "ref", "material:" + material_names[value["ref"]])
        elif value.get("ref") in mesh_names:
            emit(prefix, "ref", "mesh:" + mesh_names[value["ref"]])
    elif isinstance(value, dict):
        for k, v in value.items():
            if k == "m_GameObject":
                continue
            flatten(f"{prefix}.{k}" if prefix else k, v, emit)
    elif isinstance(value, list):
        emit(prefix, "size", str(len(value)))
        for i, v in enumerate(value):
            flatten(f"{prefix}.Array.data[{i}]", v, emit)
    elif isinstance(value, bool):
        emit(prefix, "number", "1" if value else "0")
    elif isinstance(value, (int, float)):
        emit(prefix, "number", repr(value))


node_lines = []
for key, n in data["nodes"].items():
    p, r, s = n["position"], n["rotation"], n["scale"]
    node_lines.append("\t".join(["NODE", key, n["parent"] or "", n["name"], "1" if n["active"] else "0",
                                 *map(str, p), *map(str, r), *map(str, s)]))
    # A mesh effect draws only if its renderer is enabled, and a scenery card (env_unlit) is the
    # room bake's to draw.
    renderer = next((c for c in n["components"] if c["type"] == "MeshRenderer"), None)
    drawn = renderer is not None and renderer.get("enabled", True) and not all(
        (data["materials"].get(m) or {}).get("shader", "").endswith("env_unlit") for m in renderer.get("materials") or [None])
    for c in n["components"]:
        kind = c["type"]
        if kind in ("MeshFilter", "MeshRenderer") and not drawn:
            continue
        if kind == "MeshFilter" and c.get("mesh") in mesh_names:
            node_lines.append(f"MESH\t{key}\t{mesh_names[c['mesh']]}")
        elif kind == "MeshFilter" and c.get("builtin"):
            node_lines.append(f"MESH\t{key}\tbuiltin:{c['builtin']}")
        elif kind == "MeshRenderer":
            node_lines.append(f"MATS\t{key}\t" + ",".join(material_names.get(m, "") for m in c.get("materials") or []))
        elif kind in ("ParticleSystem", "ParticleSystemRenderer"):
            node_lines.append(f"COMP\t{key}\t{kind}")

            def emit(path, kind_, value, key=key, comp=kind):
                node_lines.append(f"P\t{key}\t{comp}\t{path}\t{kind_}\t{value}")
            flatten("", c["tree"], emit)
(out / "nodes.txt").write_text("\n".join(node_lines) + "\n")

# Only the materials a drawn renderer or a particle system uses: the rest (scenery cards the room
# bake draws, the disabled holotable mesh's) would become material assets nothing references.
used = set()
for line in node_lines:
    fields = line.split("\t")
    if fields[0] == "MATS":
        used.update(n for n in fields[2].split(",") if n)
    elif fields[0] == "P" and fields[5].startswith("material:"):
        used.add(fields[5][len("material:"):])
(out / "materials.txt").write_text(
    "\n".join(l for name, block in mat_blocks.items() if name in used for l in block) + "\n")
(out / "textures.txt").write_text("\n".join(texture_lines) + "\n")
print(f"{len(texture_names)} textures, {len(mesh_names)} meshes, {len(used)} of {len(material_names)} materials, "
      f"{sum(1 for l in node_lines if l.startswith('NODE'))} nodes, {sum(1 for l in node_lines if l.startswith('P'))} properties -> {out}")
