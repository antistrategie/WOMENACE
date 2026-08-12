#!/usr/bin/env python3
"""Turn dumped face-mesh positions into face SDF lookup coordinates.

The FBX half of the sidecar bake FaceSdfUv.cs describes: Unity's
FaceSdfUv.Dump writes <dir>/<mesh>.pos.txt per face mesh, this writes
<mesh>.uv2.txt beside each, and the model's build swaps the coordinates in as
UV channel 2. The transfer itself is bake_face_sdf_uv.transfer, the same
capture-cloud alignment the glTF dolls go through.

    python3 scripts/doll/transfer_face_sdf_uv.py unity/Assets/Authored/voymastina_mech/face_sdf
"""

import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
from bake_face_sdf_uv import transfer                                 # noqa: E402


def run(directory):
    directory = Path(directory)
    dumps = sorted(directory.glob("*.pos.txt"))
    if not dumps:
        raise SystemExit(f"no *.pos.txt dumps in {directory}")
    for dump in dumps:
        positions = np.loadtxt(dump, dtype=np.float64).reshape(-1, 3)
        uv, mean_distance = transfer(positions)
        out = dump.with_name(dump.name.replace(".pos.txt", ".uv2.txt"))
        np.savetxt(out, uv, fmt="%.8f")
        print(f"  {dump.stem.replace('.pos', '')}: {len(uv)} coordinates, "
              f"alignment {mean_distance * 1000:.1f}mm mean", file=sys.stderr)


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    run(sys.argv[1])


if __name__ == "__main__":
    main()
