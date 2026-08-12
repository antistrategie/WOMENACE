#!/usr/bin/env python3
"""Add an outline submesh to a doll's glTF: the outlined parts, drawn a second time.

An inverted-hull outline is a second draw of the same geometry with front faces
culled, so the back faces show only where they fall outside the silhouette. That is
a second draw, and in HDRP a second draw means a second material: only the first
pass carrying a given LightMode tag is ever drawn, so an extra pass on the surface
shader is silently dead code. Ours was, from the day it was written.

So this appends a primitive per outlined primitive, pointing at the *same* vertex
accessors and index accessor under one new material. No vertex data is copied.

Which parts get an outline comes from doll_shading, so there is one source of truth.
The face is excluded on purpose: its mesh is open at the mouth and eyes, and an
inverted hull draws a line along every open boundary edge. The game solves that with
a per-vertex width in vertex colour that tapers to zero at those rims, which a PMX
source does not carry, so the face waits until that width is baked.

Idempotent: re-running strips the previously added primitives and material first.

    python3 scripts/doll/add_outline_submesh.py unity/Assets/Authored/makiatto/default
"""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from doll_shading import part_for, translucent_materials             # noqa: E402

OUTLINE_MATERIAL = "Outline"

# Parts whose silhouette carries a contour. Weapons, eyes and the blend layers are
# excluded because the game draws no outline on them either, and the face because of
# its open boundary edges.
OUTLINED_PARTS = {"hair", "cloth", "silkstock", "skin"}


def add(doll):
    """Append the outline submesh. Returns the number of primitives added."""
    gltf_path = Path(doll) / "model.gltf"
    doc = json.loads(gltf_path.read_text())
    materials = doc["materials"]

    old = next((i for i, m in enumerate(materials) if m.get("name") == OUTLINE_MATERIAL), None)
    if old is not None:
        for mesh in doc["meshes"]:
            mesh["primitives"] = [p for p in mesh["primitives"] if p.get("material") != old]
        materials.pop(old)
        for mesh in doc["meshes"]:
            for prim in mesh["primitives"]:
                if prim.get("material", 0) > old:
                    prim["material"] -= 1

    # The game draws no contour through transparency, and a hull around a decal
    # sheet is a rim in mid-air around the shape of the sheet rather than of
    # anything visible on it.
    blended = translucent_materials(gltf_path)
    sources = [i for i, m in enumerate(materials)
               if part_for(m.get("name") or "") in OUTLINED_PARTS
               and m.get("name") not in blended]
    if not sources:
        raise SystemExit("no material resolved to an outlined part")

    template = json.loads(json.dumps(materials[sources[0]]))
    template["name"] = OUTLINE_MATERIAL
    template["doubleSided"] = True
    template["alphaMode"] = "OPAQUE"
    materials.append(template)
    outline_index = len(materials) - 1

    added = 0
    for mesh in doc["meshes"]:
        duplicates = []
        for prim in mesh["primitives"]:
            if prim.get("material") not in sources:
                continue
            copy = dict(prim)                       # same accessors, by reference
            copy["attributes"] = dict(prim["attributes"])
            copy["material"] = outline_index
            duplicates.append(copy)
        mesh["primitives"].extend(duplicates)
        added += len(duplicates)

    gltf_path.write_text(json.dumps(doc, indent=2))
    outlined = sorted({materials[i].get("name") for i in sources})
    print(f"  outline submesh: {added} primitive(s) over {len(outlined)} material(s) "
          f"({', '.join(outlined)})", file=sys.stderr)
    return added


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    add(sys.argv[1])


if __name__ == "__main__":
    main()
