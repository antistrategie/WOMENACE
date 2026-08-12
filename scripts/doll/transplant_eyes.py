#!/usr/bin/env python3
"""Transplant a doll's eye stack onto an FBX-sourced model of the same character.

GFL2's battle rips carry no eye geometry: the game attaches its eye meshes at
runtime, so an FBX export shows painted sockets. The character's PMX-sourced
doll has the full four-layer stack as real geometry (EyeWhite backing, Eyes
eyeball, EyeShadow multiply, Eyes+ additive), and both models are the same
face, so the layers carry over with the same trimmed-ICP similarity fit the
face SDF transfer uses: doll face aligned onto the FBX face's rest positions,
the resulting transform applied to the eye primitives.

Writes one OBJ per layer, with UVs, in the FBX face mesh's own bind space, so
the build can attach them as skinned renderers sharing the face's bones. A
textures.json beside them maps each layer to the doll texture its material
binds, read from the doll's glTF.

    python3 scripts/doll/transplant_eyes.py \\
        unity/Assets/Authored/voymastina/erwin \\
        unity/Assets/Authored/voymastina_mech/face_sdf/c_VoymastinaSSR01_slg_face_lod0.pos.txt \\
        unity/Assets/Authored/voymastina_mech/eyes
"""

import json
import struct
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
from bake_face_sdf_uv import nearest, similarity                      # noqa: E402
from doll_shading import part_for                                     # noqa: E402

# Doll material name -> emitted layer, in draw order.
LAYERS = ["EyeWhite", "Eyes", "EyeShadow", "Eyes+"]


def read_accessor(doc, buf, index, comps):
    acc = doc["accessors"][index]
    view = doc["bufferViews"][acc["bufferView"]]
    off = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = view.get("byteStride") or 4 * comps
    return np.array([struct.unpack_from("<%df" % comps, buf, off + i * stride)
                     for i in range(acc["count"])], dtype=np.float64)


def read_indices(doc, buf, index):
    acc = doc["accessors"][index]
    view = doc["bufferViews"][acc["bufferView"]]
    off = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    fmt = {5121: "B", 5123: "H", 5125: "I"}[acc["componentType"]]
    size = struct.calcsize(fmt)
    return np.array([struct.unpack_from("<" + fmt, buf, off + i * size)[0]
                     for i in range(acc["count"])], dtype=np.int64)


def fit(src, dst):
    """Similarity carrying src onto dst, tried both-handed, better residual wins.

    The doll glTF's and the FBX's X axes have no fixed relation, and a face is
    symmetric enough that both fits converge; the residual picks the honest one.
    """
    best = None
    for mirror in (1.0, -1.0):
        p = src.copy()
        p[:, 0] *= mirror
        p *= (dst[:, 0].max() - dst[:, 0].min()) / (p[:, 0].max() - p[:, 0].min())
        p += dst.mean(0) - p.mean(0)
        transform = [mirror]
        for _ in range(6):
            idx, dist = nearest(p, dst)
            keep = dist[:, 0] < np.percentile(dist[:, 0], 90)
            s, rot, t = similarity(p[keep], dst[idx[keep, 0]])
            p = s * p @ rot.T + t
            transform.append((s, rot, t))
        _, dist = nearest(p, dst)
        if best is None or dist[:, 0].mean() < best[0]:
            best = (dist[:, 0].mean(), transform)
    return best


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    doll_dir, face_pos_path, out_dir = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
    out_dir.mkdir(parents=True, exist_ok=True)

    doc = json.loads((doll_dir / "model.gltf").read_text())
    buf = (doll_dir / doc["buffers"][0]["uri"]).read_bytes()
    names = [m.get("name") for m in doc.get("materials", [])]
    images = [i.get("uri", "") for i in doc.get("images", [])]
    textures = [t.get("source") for t in doc.get("textures", [])]

    # The doll's face cloud for the alignment: every face-part primitive except
    # the eye backing, which is geometry the FBX face does not have.
    face_points, prims = [], {}
    for mesh in doc["meshes"]:
        for prim in mesh["primitives"]:
            name = names[prim["material"]] or ""
            if name in LAYERS:
                if name not in prims:
                    prims[name] = prim
            elif part_for(name) == "face":
                face_points.append(read_accessor(doc, buf, prim["attributes"]["POSITION"], 3))
    missing = [l for l in LAYERS if l not in prims]
    if missing:
        raise SystemExit(f"doll lacks eye layers: {missing}")
    doll_face = np.vstack(face_points)
    fbx_face = np.loadtxt(face_pos_path, dtype=np.float64).reshape(-1, 3)

    # Fit on the faces, then carry each eye layer through the same transform.
    residual, transform = fit(doll_face, fbx_face)
    if residual > 0.02 * (fbx_face[:, 1].max() - fbx_face[:, 1].min()):
        raise SystemExit(f"face alignment did not converge: {residual:.4f} mean")
    print(f"  face alignment residual: {residual * 1000:.2f} (fbx units x1000)", file=sys.stderr)

    manifest = {}
    for layer in LAYERS:
        prim = prims[layer]
        pos = read_accessor(doc, buf, prim["attributes"]["POSITION"], 3)
        uv = read_accessor(doc, buf, prim["attributes"]["TEXCOORD_0"], 2)
        tris = read_indices(doc, buf, prim["indices"]).reshape(-1, 3)

        p = pos.copy()
        p[:, 0] *= transform[0]
        p *= (fbx_face[:, 0].max() - fbx_face[:, 0].min()) / (
            (doll_face[:, 0].max() - doll_face[:, 0].min()))
        p += fbx_face.mean(0) - doll_face_scaled_mean(doll_face, transform[0], fbx_face)
        for s, rot, t in transform[1:]:
            p = s * p @ rot.T + t

        safe = layer.replace("+", "_hl")
        obj_path = out_dir / f"eye_{safe}.obj"
        with open(obj_path, "w") as f:
            f.write(f"# {layer} transplanted from {doll_dir.name}\n")
            for v in p:
                f.write(f"v {v[0]:.6f} {v[1]:.6f} {v[2]:.6f}\n")
            for t in uv:
                f.write(f"vt {t[0]:.6f} {1.0 - t[1]:.6f}\n")
            # Mirrored X flips the winding; emit faces to match.
            flip = transform[0] < 0
            for a, b, c in tris:
                i, j, k = (a, c, b) if flip else (a, b, c)
                f.write(f"f {i+1}/{i+1} {j+1}/{j+1} {k+1}/{k+1}\n")

        tex_index = doc["materials"][[i for i, n in enumerate(names) if n == layer][0]] \
            .get("pbrMetallicRoughness", {}).get("baseColorTexture", {}).get("index")
        manifest[layer] = {
            "obj": obj_path.name,
            "texture": images[textures[tex_index]] if tex_index is not None else None,
        }
        print(f"  {layer}: {len(p)} verts, {len(tris)} tris -> {obj_path.name} "
              f"tex={manifest[layer]['texture']}", file=sys.stderr)

    (out_dir / "textures.json").write_text(json.dumps(manifest, indent=2))


def doll_face_scaled_mean(doll_face, mirror, fbx_face):
    p = doll_face.copy()
    p[:, 0] *= mirror
    p *= (fbx_face[:, 0].max() - fbx_face[:, 0].min()) / (p[:, 0].max() - p[:, 0].min())
    return p.mean(0)


if __name__ == "__main__":
    main()
