#!/usr/bin/env python3
"""Bake the face SDF lookup coordinates into a doll's glTF, as TEXCOORD_2.

GFL2 samples its face shadow map with the head mesh's *second* UV set, which is
not an unwrap but a hand-authored mapping of the face onto the map's painted
island. PMX 2.0 has no way to carry that set, so a port from PMX has to
reconstruct it. The game's own values are recoverable: the capture of the game's
face draw carries POSITION and that UV set per vertex, and GFL2's face UV layout
is shared across its characters to about a pixel, so one captured face transfers
to every doll.

data/face_sdf_uv_ref.npz holds that capture: 2,455 face vertices, positions and
their UV, dumped from the game's face draw (frame 9535, eid 948). The bake aligns
the doll's face-part vertices to the reference cloud with a similarity transform,
uniform scale included because the conversion scales a doll to MENACE's reference
soldier, then gives each vertex the inverse-distance-weighted UV of its nearest
reference vertices. The same face under a similarity transform aligns to under
two millimetres mean, and the fit is trimmed nearest-neighbour rounds, so no
landmark needs finding.

A planar projection fitted to the same capture reaches about 25 texels of
residual over the face's interior, but a projection is linear everywhere: past
the painted island's edge it keeps going, and a perimeter vertex samples off the
paint, where the threshold channel reads zero and zero reads as full shadow.
That is a dark rim around the face. The transfer cannot leave the game's own
value range by construction, and it carries the mapping's nonlinearity that no
fitted plane can, which the capture locates at the cheeks and jaw: exactly where
a rim shows.

The transferred coordinates are written as (1 - u, 1 - v): the glTF's X runs
opposite the capture's, so the alignment mirrors it and U flips with it, and the
glTF V origin is the top of the image. Both flips are pinned by agreement with
the in-game-verified planar values over the face's interior, where the two
constructions must and do coincide, to a median of 28 texels in U and 15 in V,
which is the projection's own residual.

This has to be baked rather than computed in the shader. On a skinned renderer
the vertex program sees posed positions, so a lookup built there would slide
across the face as she moves. Baked from rest positions it is fixed to the
surface, which is what a texture coordinate has to be.

TEXCOORD_2 is free: a source that ships it ships it as a constant (0, 1),
carrying nothing, and a source that does not is given one here. Only face-part
primitives take values, because only the face material reads them. Re-running
recomputes from POSITION and is idempotent.

    python3 scripts/doll/bake_face_sdf_uv.py unity/Assets/Authored/makiatto/default
"""

import json
import struct
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
from doll_shading import part_for                                     # noqa: E402

REFERENCE = Path(__file__).parent / "data" / "face_sdf_uv_ref.npz"

# The part whose materials sample the SDF. The welded face material covers the
# face, mouth, teeth, tongue and eye whites, and it is the only material the
# bake gives _UseBlendTex.
SDF_PART = "face"

# Neighbours per vertex for the UV transfer. One neighbour quantises the lookup
# to the reference's vertex sites, which puts a vertex-density wobble on the
# shadow's edge; a handful smooths it below the map's own penumbra.
NEIGHBOURS = 6

# Alignment quality gate, in the capture's metre-scale units. The clouds are the
# same face, so a converged fit sits near 1.5mm mean; a fit that cannot get
# under this has aligned the wrong geometry and the transfer would be nonsense.
MAX_MEAN_DISTANCE = 0.005


def read_vec(doc, buf, accessor_index, comps):
    acc = doc["accessors"][accessor_index]
    view = doc["bufferViews"][acc["bufferView"]]
    off = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = view.get("byteStride") or 4 * comps
    rows = [struct.unpack_from("<%df" % comps, buf, off + i * stride)
            for i in range(acc["count"])]
    return np.array(rows, dtype=np.float64)


def nearest(points, cloud, k=1):
    """Indices and distances of each point's k nearest cloud vertices."""
    idx = np.empty((len(points), k), dtype=int)
    dist = np.empty((len(points), k))
    for i in range(0, len(points), 512):
        d2 = ((points[i:i + 512, None, :] - cloud[None, :, :]) ** 2).sum(-1)
        part = np.argsort(d2, axis=1)[:, :k]
        idx[i:i + 512] = part
        dist[i:i + 512] = np.sqrt(np.take_along_axis(d2, part, 1))
    return idx, dist


def similarity(src, dst):
    """Least-squares similarity transform src -> dst: scale, rotation, translation."""
    ms, md = src.mean(0), dst.mean(0)
    a, b = src - ms, dst - md
    cov = b.T @ a / len(src)
    u, s, vt = np.linalg.svd(cov)
    sign = np.eye(3)
    sign[2, 2] = np.sign(np.linalg.det(u @ vt))
    rot = u @ sign @ vt
    scale = (s * sign.diagonal()).sum() * len(src) / (a ** 2).sum()
    return scale, rot, md - scale * rot @ ms


def align(face_pos, ref_pos):
    """The doll's face vertices carried into the reference cloud's frame.

    Initialised by matching X extents and centroids, then refined with trimmed
    nearest-neighbour rounds: the pairing improves as the fit does, and trimming
    the worst decile keeps regions one mesh has and the other lacks, the mouth
    interior mostly, from steering the transform.
    """
    p = face_pos.copy()
    p[:, 0] *= -1.0
    p *= (ref_pos[:, 0].max() - ref_pos[:, 0].min()) / (p[:, 0].max() - p[:, 0].min())
    p += ref_pos.mean(0) - p.mean(0)
    for _ in range(6):
        idx, dist = nearest(p, ref_pos)
        keep = dist[:, 0] < np.percentile(dist[:, 0], 90)
        scale, rot, t = similarity(p[keep], ref_pos[idx[keep, 0]])
        p = scale * p @ rot.T + t
    return p


def transfer(face_pos):
    """Face rest positions -> the game's lookup coordinates, plus the fit's
    mean nearest-reference distance. The alignment gate lives here so every
    caller, glTF and FBX alike, fails loudly on geometry that is not a face.
    """
    ref = np.load(REFERENCE)
    ref_pos, ref_uv = ref["pos"].astype(np.float64), ref["uv"].astype(np.float64)
    aligned = align(face_pos, ref_pos)
    idx, dist = nearest(aligned, ref_pos, NEIGHBOURS)
    if dist[:, 0].mean() > MAX_MEAN_DISTANCE:
        raise SystemExit(
            f"face alignment did not converge: mean nearest-reference distance "
            f"{dist[:, 0].mean() * 1000:.1f}mm against a {MAX_MEAN_DISTANCE * 1000:.0f}mm gate")
    weights = 1.0 / (dist + 1e-6) ** 2
    weights /= weights.sum(1, keepdims=True)
    uv = (ref_uv[idx] * weights[..., None]).sum(1)
    return np.column_stack([1.0 - uv[:, 0], 1.0 - uv[:, 1]]), dist[:, 0].mean()


def add_uv2(doc, buf):
    """Give every primitive the UV sets up to TEXCOORD_2, where it has none.

    New sets are a constant (0, 1), which is what a source that ships
    TEXCOORD_2 already ships in it. The face bake overwrites the face's, and
    transfer_hair_uv the hair's TEXCOORD_1.

    On every primitive rather than only the ones read, because glTFast clusters
    a mesh's primitives by vertex layout and splits the ones that disagree onto
    separate meshes. A face carrying a UV set the rest of the body does not
    would leave the model as two renderers per LOD instead of one. And up from
    TEXCOORD_1 rather than TEXCOORD_2 alone, because a source with a gap in the
    run is a source whose second set the importer reads as its third.

    Keyed by POSITION so primitives sharing vertex data share the coordinates
    too, which is what the outline submesh does: it points at the accessors of
    the primitives it copies.
    """
    made = {}
    for mesh in doc["meshes"]:
        for prim in mesh["primitives"]:
            attrs = prim["attributes"]
            for level in (1, 2):
                name = f"TEXCOORD_{level}"
                if name in attrs:
                    continue
                key = (attrs["POSITION"], level)
                if key not in made:
                    count = doc["accessors"][attrs["POSITION"]]["count"]
                    buf.extend(b"\0" * (-len(buf) % 4))
                    doc["bufferViews"].append({
                        "buffer": 0, "byteOffset": len(buf),
                        "byteLength": count * 8, "target": 34962,
                    })
                    buf.extend(struct.pack("<%df" % (count * 2), *([0.0, 1.0] * count)))
                    doc["accessors"].append({
                        "bufferView": len(doc["bufferViews"]) - 1,
                        "componentType": 5126, "count": count, "type": "VEC2",
                    })
                    made[key] = len(doc["accessors"]) - 1
                attrs[name] = made[key]
    if made:
        doc["buffers"][0]["byteLength"] = len(buf)
    return len(made)


def bake(doll):
    """Write the transferred lookup into TEXCOORD_2. Returns the vertex count."""
    doll = Path(doll)
    gltf_path = doll / "model.gltf"
    doc = json.loads(gltf_path.read_text())
    if len(doc["buffers"]) != 1:
        raise SystemExit("expected a single .bin buffer")
    bin_path = doll / doc["buffers"][0]["uri"]
    buf = bytearray(bin_path.read_bytes())

    if add_uv2(doc, buf):
        gltf_path.write_text(json.dumps(doc, indent=2))

    # Every face-part primitive, deduplicated by accessor: the outline submesh
    # points at the same vertex data as the surface it copies, and writing one
    # accessor twice is harmless but counting it twice makes the report a lie.
    names = [m.get("name") for m in doc.get("materials", [])]
    targets = {}
    for mesh in doc["meshes"]:
        for prim in mesh["primitives"]:
            if part_for(names[prim["material"]] or "") != SDF_PART:
                continue
            attrs = prim["attributes"]
            if "TEXCOORD_2" in attrs and attrs["TEXCOORD_2"] not in targets:
                targets[attrs["TEXCOORD_2"]] = attrs["POSITION"]
    if not targets:
        raise SystemExit(f"no primitives resolve to part '{SDF_PART}'")

    positions = {uv_acc: read_vec(doc, buf, pos_acc, 3)
                 for uv_acc, pos_acc in targets.items()}
    transferred, mean_distance = transfer(np.vstack(list(positions.values())))

    written = 0
    row = 0
    for uv_acc, pos in positions.items():
        acc = doc["accessors"][uv_acc]
        view = doc["bufferViews"][acc["bufferView"]]
        if view.get("byteStride") not in (None, 8):
            raise SystemExit("TEXCOORD_2 is interleaved, cannot write in place")
        base = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        if acc["count"] != len(pos):
            raise SystemExit("TEXCOORD_2 count does not match POSITION")
        for i in range(acc["count"]):
            struct.pack_into("<2f", buf, base + i * 8, *transferred[row])
            row += 1
        written += acc["count"]

    bin_path.write_bytes(bytes(buf))
    print(f"  face SDF UV: transferred from the captured face draw, {written} vertices, "
          f"alignment {mean_distance * 1000:.1f}mm mean", file=sys.stderr)
    return written


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    bake(sys.argv[1])


if __name__ == "__main__":
    main()
