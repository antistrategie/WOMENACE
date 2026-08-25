"""Copy hand-painted weights from a donor .blend onto a freshly re-prepped
checkpoint blend, LOWER BODY ONLY. This is the maintained recovery path for
carrying a doll's painting through any future from-PMX rebuild.

Only vertices whose donor weights are dominated by the lower body (Spine and
below) take the donor's painted values, wholesale per vertex so normalisation
survives. Everything above (Spine2, neck, head, arms, hands) keeps the fresh
rebuild's auto weights, so a weight-borne upper-body fault cannot ride back in
through the transfer. Matching is nearest-vertex; a fresh mesh from the same
PMX at the same scale matches effectively exactly. Snapshot the painted blend
as the donor BEFORE re-prepping (backups/weight-donors-* is the convention).

Run inside Blender:
    blender --background --python scripts/doll/transfer_weights.py -- \
        --fresh <checkpoint.blend> --donor <donor.blend>
"""
import argparse
import sys
from pathlib import Path

import bpy  # type: ignore
import mathutils  # type: ignore

LOWER_GROUPS = {
    "Spine", "Hips",
    "UpperLeg_L", "LowerLeg_L", "Foot_L",
    "UpperLeg_R", "LowerLeg_R", "Foot_R",
}


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--fresh", required=True)
    ap.add_argument("--donor", required=True)
    ap.add_argument("--dominance", type=float, default=0.5)
    return ap.parse_args(argv)


def main() -> None:
    args = parse_args()
    bpy.ops.wm.open_mainfile(filepath=args.fresh)
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    fresh = next(o for o in bpy.data.objects if o.type == "MESH")

    with bpy.data.libraries.load(args.donor) as (data_from, data_to):
        data_to.objects = list(data_from.objects)
    donor = next((o for o in data_to.objects if o is not None and o.type == "MESH"), None)
    if donor is None:
        raise SystemExit(f"no mesh in donor {args.donor}")
    bpy.context.scene.collection.objects.link(donor)

    donor_groups = {g.index: g.name for g in donor.vertex_groups}
    tree = mathutils.kdtree.KDTree(len(donor.data.vertices))
    dm = donor.matrix_world
    for i, v in enumerate(donor.data.vertices):
        tree.insert(dm @ v.co, i)
    tree.balance()

    fresh_group_by_name = {g.name: g for g in fresh.vertex_groups}
    fm = fresh.matrix_world
    replaced = 0
    far = 0
    for v in fresh.data.vertices:
        _, di, dist = tree.find(fm @ v.co)
        dv = donor.data.vertices[di]
        weights = {}
        lower = 0.0
        total = 0.0
        for gw in dv.groups:
            name = donor_groups.get(gw.group)
            if name is None or gw.weight <= 0.0:
                continue
            weights[name] = gw.weight
            total += gw.weight
            if name in LOWER_GROUPS:
                lower += gw.weight
        if total <= 0.0 or lower / total < args.dominance:
            continue
        if dist > 0.01:
            far += 1
        for g in fresh.vertex_groups:
            g.remove([v.index])
        for name, w in weights.items():
            g = fresh_group_by_name.get(name)
            if g is None:
                g = fresh.vertex_groups.new(name=name)
                fresh_group_by_name[name] = g
            g.add([v.index], w, "REPLACE")
        replaced += 1

    bpy.data.objects.remove(donor, do_unlink=True)
    for block in list(bpy.data.meshes):
        if block.users == 0:
            bpy.data.meshes.remove(block)

    print(f"[done] {replaced}/{len(fresh.data.vertices)} vertices took donor lower-body weights"
          + (f", {far} matched farther than 1cm" if far else ""))
    bpy.ops.wm.save_as_mainfile(filepath=args.fresh)


main()
