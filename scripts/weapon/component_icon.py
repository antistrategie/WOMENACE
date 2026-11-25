"""Draw a Doll weapon's component icon: the weapon's outline as line art.

The weapon's model is rendered side-on in Blender (render_weapon.py), and its
silhouette is traced and scaled down to the 144px icon. Normal weapons are
drawn in the workshop icons' cream and SSR weapons in the interface's gold.

Run as:
    uv run --with opencv-python-headless --with numpy python scripts/weapon/component_icon.py <name>...

Each <name> is a folder under unity/Assets/Authored/weapon/ with a raw.glb, and
the icon is written to assets/sprites/weapon/<name>/component.png.
"""

import subprocess
import sys
import tempfile
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SIZE = 144
# Rendered at a whole multiple of the icon, so every icon pixel averages the same number.
UP = 8
STROKE = 1.5  # in icon pixels
CREAM = (200, 196, 181)  # the workshop icons' ink
GOLD = (215, 192, 116)  # --gold in wui.uss


def render(glb: Path, out: Path) -> None:
    subprocess.run(
        ["blender", "--background", "--python", str(ROOT / "scripts/weapon/render_weapon.py"), "--",
         "--glb", str(glb), "--out", str(out), "--width", str(SIZE * UP), "--height", str(SIZE * UP),
         "--view", "side", "--mode", "flat"],
        check=True, stdout=subprocess.DEVNULL)


def lay_along(alpha: np.ndarray) -> np.ndarray:
    """A weapon modelled upright, such as a blade, laid along its length as the
    others are, with its heavier end (the hilt) to the left."""
    ys, xs = np.nonzero(alpha > 110)
    if np.ptp(ys) <= np.ptp(xs):
        return alpha
    points = np.column_stack([xs, ys]).astype(np.float64)
    centre = points.mean(axis=0)
    _, vectors = np.linalg.eigh(np.cov((points - centre).T))
    axis = vectors[:, -1]
    angle = np.degrees(np.arctan2(axis[1], axis[0]))
    n = alpha.shape[0]
    turned = cv2.warpAffine(alpha, cv2.getRotationMatrix2D((n / 2, n / 2), angle, 1.0), (n, n))
    if np.count_nonzero(turned[:, : n // 2] > 110) < np.count_nonzero(turned[:, n // 2:] > 110):
        turned = turned[::-1, ::-1]
    # Fitted again to the render's own margin, which is 6 of the icon's 144 pixels.
    ys, xs = np.nonzero(turned > 0)
    crop = turned[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    margin = round(n * 6 / 144)
    scale = (n - 2 * margin) / max(crop.shape)
    crop = cv2.resize(crop, (round(crop.shape[1] * scale), round(crop.shape[0] * scale)), interpolation=cv2.INTER_AREA)
    out = np.zeros_like(alpha)
    top, left = (n - crop.shape[0]) // 2, (n - crop.shape[1]) // 2
    out[top:top + crop.shape[0], left:left + crop.shape[1]] = crop
    return out


def trace(rendered: Path, ink: tuple[int, int, int]) -> np.ndarray:
    alpha = lay_along(cv2.imread(str(rendered), cv2.IMREAD_UNCHANGED)[:, :, 3])
    alpha = cv2.GaussianBlur(alpha, (0, 0), UP * 0.3)
    _, mask = cv2.threshold(alpha, 110, 255, cv2.THRESH_BINARY)
    contours, _ = cv2.findContours(mask, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_NONE)
    # A hole or speck of a few icon pixels would only blur into the line around it.
    contours = [cv2.approxPolyDP(c, UP * 0.3, True) for c in contours if cv2.contourArea(c) > UP * UP * 4]
    lines = np.zeros_like(mask)
    cv2.drawContours(lines, contours, -1, 255, round(STROKE * UP), cv2.LINE_AA)
    icon = np.zeros((SIZE, SIZE, 4), np.uint8)
    icon[:, :, :3] = ink[::-1]
    icon[:, :, 3] = cv2.resize(lines, (SIZE, SIZE), interpolation=cv2.INTER_AREA)
    return icon


def main(names: list[str]) -> None:
    with tempfile.TemporaryDirectory() as tmp:
        for name in names:
            glb = ROOT / "unity/Assets/Authored/weapon" / name / "raw.glb"
            rendered = Path(tmp) / f"{name}.png"
            render(glb, rendered)
            out = ROOT / "assets/sprites/weapon" / name / "component.png"
            cv2.imwrite(str(out), trace(rendered, GOLD if name.endswith("_ssr") else CREAM))
            print(out.relative_to(ROOT))


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    main(sys.argv[1:])
