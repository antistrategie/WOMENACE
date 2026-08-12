#!/usr/bin/env python3
"""Transfer the game's hair strip UV onto a doll's glTF, as TEXCOORD_1.

GFL2's hair specular samples the _spc map with the hair mesh's second UV set, a
per-strand strip layout no PMX source carries: the PMX additional-UV slot is
noise, which is why the hair path ships disabled on a bare conversion. The
game's own hair mesh has the real set, and it sits in the client's bundles in
rest pose, so it transfers the same way the face SDF set does: align the doll's
hair to the game's with a trimmed-ICP similarity fit, give each doll vertex the
inverse-distance-weighted UV of its nearest game vertices.

The reference is <doll>/hair_uv1_ref.npz (pos + uv), dumped from the game's
hair mesh by tdollhouse's shaderdump (Mesh mode decodes the channels). When the
file exists, prepare_doll runs this and doll_shading switches the hair
materials onto the specular path.

The fit is tried both-handed and the residual picks: hair is asymmetric enough
to discriminate, unlike a face. Fringe and falls diverge from the game mesh by
design (the PMX authors resculpted them), so the report prints the worst decile
distance: strands far off the game surface take their nearest strip's
coordinates, which is an approximation the streak has to be judged on in game.

    python3 scripts/doll/transfer_hair_uv.py unity/Assets/Authored/makiatto/default
"""

import json
import struct
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
from bake_face_sdf_uv import nearest, read_vec                        # noqa: E402
from doll_shading import part_for                                     # noqa: E402
from transplant_eyes import fit                                       # noqa: E402

NEIGHBOURS = 6


def reference_for(doll_dir):
    """The character's game-hair reference, sitting beside the outfit folders."""
    return Path(doll_dir).parent / "hair_uv1_ref.npz"


def bake(doll):
    doll = Path(doll)
    ref_path = reference_for(doll)
    if not ref_path.is_file():
        raise SystemExit(f"no hair reference at {ref_path}")
    ref = np.load(ref_path)
    ref_pos, ref_uv = ref["pos"].astype(np.float64), ref["uv"].astype(np.float64)

    gltf_path = doll / "model.gltf"
    doc = json.loads(gltf_path.read_text())
    bin_path = doll / doc["buffers"][0]["uri"]
    buf = bytearray(bin_path.read_bytes())
    names = [m.get("name") for m in doc.get("materials", [])]

    targets = {}
    for mesh in doc["meshes"]:
        for prim in mesh["primitives"]:
            if part_for(names[prim["material"]] or "") != "hair":
                continue
            attrs = prim["attributes"]
            if "TEXCOORD_1" in attrs and attrs["TEXCOORD_1"] not in targets:
                targets[attrs["TEXCOORD_1"]] = attrs["POSITION"]
    if not targets:
        raise SystemExit("no hair primitives with a TEXCOORD_1 to overwrite")

    positions = {acc: read_vec(doc, buf, pos_acc, 3) for acc, pos_acc in targets.items()}
    hair = np.vstack(list(positions.values()))

    residual, transform = fit(hair, ref_pos)
    p = hair.copy()
    p[:, 0] *= transform[0]
    p *= (ref_pos[:, 0].max() - ref_pos[:, 0].min()) / (p[:, 0].max() - p[:, 0].min())
    p += ref_pos.mean(0) - p.mean(0)
    for s, rot, t in transform[1:]:
        p = s * p @ rot.T + t

    idx, dist = nearest(p, ref_pos, NEIGHBOURS)
    weights = 1.0 / (dist + 1e-6) ** 2
    weights /= weights.sum(1, keepdims=True)
    uv = (ref_uv[idx] * weights[..., None]).sum(1)
    # The reference is in Unity's V convention; the glTF importer flips V, so
    # the file stores the complement, the same convention the face bake pins.
    uv[:, 1] = 1.0 - uv[:, 1]

    written = 0
    row = 0
    for acc_index, pos in positions.items():
        acc = doc["accessors"][acc_index]
        view = doc["bufferViews"][acc["bufferView"]]
        if view.get("byteStride") not in (None, 8):
            raise SystemExit("TEXCOORD_1 is interleaved, cannot write in place")
        base = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        if acc["count"] != len(pos):
            raise SystemExit("TEXCOORD_1 count does not match POSITION")
        for i in range(acc["count"]):
            struct.pack_into("<2f", buf, base + i * 8, *uv[row])
            row += 1
        written += acc["count"]

    bin_path.write_bytes(bytes(buf))
    print(f"  hair strip UV: transferred from the game hair mesh, {written} vertices, "
          f"alignment {residual * 1000:.1f}mm mean, worst decile "
          f"{np.percentile(dist[:, 0], 90) * 1000:.1f}mm", file=sys.stderr)
    return written


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    bake(sys.argv[1])


if __name__ == "__main__":
    main()
