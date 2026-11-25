"""Render a Doll weapon's icon set from its model, as the other weapons' are.

Each icon is a flat side render from render_weapon.py. A disabled icon is its
enabled one in grey at the brightness the game's own disabled icons have.

Run as:
    uv run --with pillow --with numpy python scripts/weapon/weapon_icons.py <name>...

Each <name> is a folder under unity/Assets/Authored/weapon/ with a raw.glb, and
the icons are written to assets/sprites/weapon/<name>/.
"""

import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
# name: (width, height, padding, whether it has a disabled twin)
ICONS = {
    "IconEquipment": (912, 318, 0.08, True),
    "IconSkillBar": (465, 111, 0.08, True),
}
# The square catalogue icon is rendered large and scaled down, as its edges
# would otherwise stair-step at 144 pixels.
SQUARE = 144
SQUARE_UP = 8
DISABLED_BRIGHTNESS = 0.55


def render(glb: Path, out: Path, width: int, height: int, padding: float | None = None) -> Image.Image:
    cmd = ["blender", "--background", "--python", str(ROOT / "scripts/weapon/render_weapon.py"), "--",
           "--glb", str(glb), "--out", str(out), "--width", str(width), "--height", str(height),
           "--view", "side", "--mode", "flat"]
    if padding is not None:
        cmd += ["--padding", str(padding)]
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL)
    return Image.open(out).convert("RGBA")


def disabled(icon: Image.Image) -> Image.Image:
    rgba = np.asarray(icon).astype(np.float64)
    grey = (rgba[:, :, :3] @ [0.299, 0.587, 0.114]) * DISABLED_BRIGHTNESS
    out = np.dstack([grey, grey, grey, rgba[:, :, 3]])
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA")


def main(names: list[str]) -> None:
    with tempfile.TemporaryDirectory() as tmp:
        for name in names:
            glb = ROOT / "unity/Assets/Authored/weapon" / name / "raw.glb"
            dest = ROOT / "assets/sprites/weapon" / name
            for icon, (w, h, padding, has_disabled) in ICONS.items():
                image = render(glb, Path(tmp) / f"{name}_{icon}.png", w, h, padding)
                image.save(dest / f"{icon}.png")
                if has_disabled:
                    disabled(image).save(dest / f"{icon}Disabled.png")
            square = render(glb, Path(tmp) / f"{name}_Icon.png", SQUARE * SQUARE_UP, SQUARE * SQUARE_UP)
            square.resize((SQUARE, SQUARE), Image.LANCZOS).save(dest / "Icon.png")
            print(dest.relative_to(ROOT))


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    main(sys.argv[1:])
