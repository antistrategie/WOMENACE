"""Draw a hold as flat views in the weapon's own frame, on a centimetre grid.

Reads a pose written by WeaponHoldCheck. The side view looks along the weapon's X with its
muzzle to the right, and the view from above looks down its Y, so a distance read off the
grid is a distance to move the weapon's parts in its source file. The weapon is orange and
the doll's arm and hand blue, both see-through, so the grip shows inside the hand.

    uv run --with numpy --with matplotlib python3 hold_profile.py <dir>/<name> <out.png>
"""

import json
import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.collections import PolyCollection


def main():
    pose, out = Path(sys.argv[1]), sys.argv[2]
    data = json.loads(pose.with_suffix(".json").read_text())
    nodes = {k: {a: np.array(v) for a, v in n.items()} for k, n in data["nodes"].items()}
    vertices, faces, current = [], {}, None
    for line in pose.with_suffix(".obj").read_text().splitlines():
        if line.startswith("v "):
            vertices.append([float(x) for x in line.split()[1:4]])
        elif line.startswith("o "):
            current = line[2:].strip()
            faces[current] = []
        elif line.startswith("f "):
            faces[current].append([int(x.split("/")[0]) - 1 for x in line.split()[1:4]])
    vertices = np.array(vertices)
    weapon = nodes["weapon"]
    # The pose's points and axes are mirrored alike, which a dot product does not see: the
    # numbers read off are offsets in the weapon's own frame as Unity has it.
    basis = np.stack([weapon["x"], weapon["y"], weapon["z"]])
    local = (vertices - weapon["p"]) @ basis.T
    place = {k: (n["p"] - weapon["p"]) @ basis.T for k, n in nodes.items()}

    # A hand is the body's faces past its wrist, the way its fingers run, and no further than
    # a hand's length: the sleeve and forearm behind the wrist are left out.
    def hand(side, tris, reach):
        tris = np.array(tris)
        middle = local[tris].mean(axis=1)
        knuckle = next((place[n] for n in (f"{side}MiddleProximal", f"{side}IndexProximal") if n in place), None)
        wrist = place[f"{side}Hand"]
        keep = np.linalg.norm(middle - wrist, axis=1) < reach
        if knuckle is not None:
            along = (knuckle - wrist) / np.linalg.norm(knuckle - wrist)
            keep &= (middle - wrist) @ along > -0.005
        return tris[keep]

    gun = np.array(faces["weapon"])
    right = np.array(faces["hand_r"]) if faces.get("hand_r") else hand("Right", faces["body"], 0.13)
    left = np.array(faces["hand_l"]) if faces.get("hand_l") else hand("Left", faces["body"], 0.13)
    fig, axes = plt.subplots(3, 1, figsize=(16, 22))
    for ax, (h, v, title) in zip(axes, ((2, 1, "side: forward (Z) across, up (Y) up"), (2, 0, "from above: forward (Z) across, X up"), (0, 1, "from behind: X across, up (Y) up"))):
        for tris, colour, alpha in ((gun, "#e8862a", 0.35), (right, "#2a6fe8", 0.35), (left, "#2ab36f", 0.35)):
            ax.add_collection(PolyCollection(local[tris][:, :, [h, v]] * 100, facecolors=colour, edgecolors="none", alpha=alpha))
        for name, mark in (("RightHand", "ko"), ("grip", "g^"), ("muzzle", "bs")):
            if name in place:
                ax.plot(place[name][h] * 100, place[name][v] * 100, mark, markersize=7)
        for name, point in place.items():
            if name.startswith("Right") and ("Proximal" in name or "Distal" in name or "Intermediate" in name):
                ax.plot(point[h] * 100, point[v] * 100, "k.", markersize=4)
        across = (-25, 45) if h == 2 else (-14, 14)
        ax.set_xlim(*across)
        ax.set_ylim(-18, 12) if v == 1 else ax.set_ylim(-12, 12)
        ax.set_aspect("equal")
        ax.set_xticks(range(across[0], across[1] + 1, 5 if h == 2 else 2))
        ax.set_xticks(range(across[0], across[1] + 1), minor=True)
        ax.set_yticks(range(-18, 13, 2) if v == 1 else range(-12, 13, 2))
        ax.grid(True, which="major", linewidth=0.6)
        ax.grid(True, which="minor", linewidth=0.2)
        ax.set_title(f"{pose.name}  {title}  (cm, weapon origin at 0,0)")
    fig.tight_layout()
    fig.savefig(out, dpi=70)


main()
