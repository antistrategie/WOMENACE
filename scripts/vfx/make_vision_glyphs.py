"""Build the signed distance glyph atlas for Divination's foresight labels.

The labels are a plain mesh of letter quads (DivinationSystem.TextMesh) read through the
Womenace/VisionText shader, which wants the distance field in alpha with 0.5 on the glyph edge.
Only the characters the labels use are drawn, one per cell of a GRID x GRID atlas, in the order
of GLYPHS; the runtime holds the same string.

    uv run --with pillow --with numpy --with scipy scripts/vfx/make_vision_glyphs.py
"""
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

GLYPHS = "0123456789/ ABDEGHIMNOPRST%"
GRID = 6
CELL = 64
SCALE = 4
SPREAD = 10  # distance in cell pixels mapped across the 0..1 range
FONT = "/usr/share/fonts/noto/NotoSansMono-CondensedBold.ttf"
OUT = Path(__file__).resolve().parents[2] / "unity/Assets/Authored/effects/vision_ghost/textures/glyphs.png"


def main():
    big = CELL * SCALE
    font = ImageFont.truetype(FONT, int(big * 0.78))
    atlas = np.zeros((GRID * CELL, GRID * CELL), np.float32)
    for i, ch in enumerate(GLYPHS):
        img = Image.new("L", (big, big), 0)
        draw = ImageDraw.Draw(img)
        left, top, right, bottom = draw.textbbox((0, 0), ch, font=font)
        draw.text(((big - (right - left)) / 2 - left, (big - (bottom - top)) / 2 - top), ch, font=font, fill=255)
        inside = np.array(img) > 127
        # signed distance in big pixels, positive inside
        dist = np.where(inside, ndimage.distance_transform_edt(inside), -ndimage.distance_transform_edt(~inside))
        field = np.clip(0.5 + dist / (2 * SPREAD * SCALE), 0, 1)
        small = np.array(Image.fromarray(field).resize((CELL, CELL), Image.BILINEAR))
        row, col = divmod(i, GRID)
        atlas[row * CELL:(row + 1) * CELL, col * CELL:(col + 1) * CELL] = small
    a = (atlas * 255).astype(np.uint8)
    rgba = np.dstack([np.full_like(a, 255)] * 3 + [a])
    OUT.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(rgba, "RGBA").save(OUT)
    print(f"wrote {OUT} ({len(GLYPHS)} glyphs)")


if __name__ == "__main__":
    main()
