#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy", "opencv-python-headless"]
# ///
"""Make the faction backgrounds a doll's portrait sits on, one set per GFL2 faction.

A UnitLeaderTemplate carries three of them, each copying the structure of vanilla's
background_<faction>_* set (pirates, marines, mercenaries, other):

    background_<faction>_1180x1040   FactionBackground      flat faction colour, the shape in alpha:
                                                           a disc fading from bottom-left to top-right
                                                           with the logo cut out of it
    background_<faction>_972x906     BigBackground          opaque: a centred disc fading towards its
                                                           edge over a dark tint, the logo darkened in
    background_<faction>_162x162     SlotFactionBackground  flat colour, alpha fading top to bottom,
                                                           logo cut out

Logos are GFL2's Img_CampLogo_* (white silhouettes) in data/faction_logos/. The disc, fade and
brightness constants were fitted on the vanilla textures.

Run:
    uv run scripts/doll/make_faction_backgrounds.py [faction ...]
"""

import sys
from pathlib import Path

import cv2
import numpy as np

REPO = Path(__file__).resolve().parents[2]
LOGOS = Path(__file__).resolve().parent / "data" / "faction_logos"
OUT = REPO / "assets" / "additions" / "textures" / "factions"

# faction: (logo file, colour as RGB[, logo scale[, logo shift]]). Shift moves the logo sideways by
# a fraction of the disc radius (negative = left), for a narrow logo the portrait would hide. The scale enlarges a logo whose shape reads
# small behind a portrait (a thin triangle leaves most of the disc empty).
FACTIONS = {
    "404": ("404.png", (150, 158, 172)),
    "404_team_a": ("404t1.png", (150, 158, 172)),
    "404_team_b": ("404t2.png", (150, 158, 172)),
    "defy": ("DEFY.png", (228, 228, 224), 1.3),
    "ar_team": ("AR.png", (220, 45, 50)),
    "elmo_ce": ("ElmoG.png", (215, 95, 25)),
    "elmo_3rd_gen": ("3RD.png", (215, 175, 55)),
    "monsoon": ("Monsoon.png", (140, 80, 205), 1.6),
    "zucchero": ("Zucchero.png", (245, 165, 90)),
    "doll_community": ("DollCom.png", (95, 175, 232)),
    "pol03": ("POL03.png", (60, 175, 95)),
    "42lab": ("42LAB.png", (40, 185, 190)),
    "groza": ("Groza.png", (236, 230, 70)),
}

# Vanilla 1180x1040: disc centre and radius, alpha plane a*x + b*y + c (normalised), strength
# such that colour * peak alpha lands where vanilla's does.
BIG_DISC = (573.9, 636.0, 613.6)
BIG_PLANE = (-0.0010232, 0.00089794, 0.65758)
BIG_LOGO_BOX = 0.82  # logo fitted in this fraction of the disc diameter
BIG_EFFECTIVE = 52.0
# Vanilla 162x162: whole square, alpha plane, brighter than the big one.
SLOT_PLANE = (-0.00057856, 0.0044666, 0.18004)
SLOT_LOGO_BOX = 0.98
SLOT_EFFECTIVE = 150.0
# Vanilla 972x906: disc centre, radius, soft edge, falloff, logo darkening, backdrop tint.
PANEL_DISC = (486.0, 453.0, 512.0)
PANEL_EDGE = 14.0
PANEL_FALLOFF = (0.26, 480.0, 1.6)
PANEL_LOGO_BOX = 0.90
PANEL_LOGO_DARKEN = 0.6
PANEL_BACKDROP = (0.15, 18.0)


def logo_alpha(name):
    im = cv2.imread(str(LOGOS / name), cv2.IMREAD_UNCHANGED)
    # Some logos are white badges with dark artwork inside (Elmo, POL-03): only the white parts
    # are cut, so the cut-out is alpha times brightness.
    a = im[..., 3].astype(np.float32) / 255 * (im[..., :3].astype(np.float32).max(-1) / 255)
    ys, xs = np.where(a > 0.05)
    return a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def paste_logo(logo, shape, cx, cy, box):
    """The logo alpha scaled to fit a box x box square, centred at (cx, cy) on a canvas."""
    h, w = logo.shape
    s = box / max(h, w)
    small = cv2.resize(logo, (max(1, round(w * s)), max(1, round(h * s))), interpolation=cv2.INTER_AREA)
    out = np.zeros(shape, np.float32)
    x0, y0 = round(cx - small.shape[1] / 2), round(cy - small.shape[0] / 2)
    # The big disc runs off the canvas, so a large logo can too: clip it.
    bx0, by0 = max(0, x0), max(0, y0)
    bx1, by1 = min(shape[1], x0 + small.shape[1]), min(shape[0], y0 + small.shape[0])
    out[by0:by1, bx0:bx1] = small[by0 - y0:by1 - y0, bx0 - x0:bx1 - x0]
    return out


def plane(shape, coef):
    yy, xx = np.mgrid[:shape[0], :shape[1]]
    return np.clip(coef[0] * xx + coef[1] * yy + coef[2], 0, 1)


def flat(colour, alpha):
    h, w = alpha.shape
    bgr = np.empty((h, w, 4), np.uint8)
    bgr[..., :3] = colour[::-1]
    bgr[..., 3] = np.clip(alpha * 255, 0, 255).astype(np.uint8)
    return bgr


def big(logo, colour, scale=1.0, shift=0.0):
    shape = (1040, 1180)
    cx, cy, r = BIG_DISC
    yy, xx = np.mgrid[:shape[0], :shape[1]]
    disc = np.clip(r - np.hypot(xx - cx, yy - cy) + 0.5, 0, 1)
    peak = min(1.0, BIG_EFFECTIVE / max(colour))
    knock = paste_logo(logo, shape, cx + shift * r, cy - 0.04 * r, 2 * r * BIG_LOGO_BOX * scale)
    return flat(colour, disc * plane(shape, BIG_PLANE) * (1 - knock) * peak)


def slot(logo, colour, scale=1.0, shift=0.0):
    shape = (162, 162)
    peak = min(0.9, SLOT_EFFECTIVE / max(colour))
    knock = paste_logo(logo, shape, 81 + shift * 81, 81, 162 * SLOT_LOGO_BOX * scale)
    return flat(colour, plane(shape, SLOT_PLANE) / 0.9 * (1 - knock) * peak)


def panel(logo, colour, scale=1.0, shift=0.0):
    shape = (906, 972)
    cx, cy, r = PANEL_DISC
    yy, xx = np.mgrid[:shape[0], :shape[1]]
    d = np.hypot(xx - cx, yy - cy)
    c = np.array(colour, np.float32)
    k, ref, p = PANEL_FALLOFF
    lit = c * (1 - k * (d / ref) ** p)[..., None]
    gain, lift = PANEL_BACKDROP
    shade = np.clip(1 - 0.3 * yy / shape[0], 0.7, 1)[..., None]
    backdrop = (c * gain + lift) * shade
    knock = cv2.GaussianBlur(paste_logo(logo, shape, cx + shift * r, cy, 2 * r * PANEL_LOGO_BOX * scale), (0, 0), 1.2)
    lit = lit * (1 - PANEL_LOGO_DARKEN * knock[..., None]) + backdrop * PANEL_LOGO_DARKEN * knock[..., None]
    inside = np.clip((r - d) / PANEL_EDGE + 0.5, 0, 1)[..., None]
    rgb = lit * inside + backdrop * (1 - inside)
    out = np.dstack([np.clip(rgb[..., ::-1], 0, 255), np.full(shape, 255, np.float32)])
    return out.astype(np.uint8)


def main():
    wanted = sys.argv[1:] or list(FACTIONS)
    OUT.mkdir(parents=True, exist_ok=True)
    for key in wanted:
        logo_file, colour, *rest = FACTIONS[key]
        scale = rest[0] if rest else 1.0
        shift = rest[1] if len(rest) > 1 else 0.0
        logo = logo_alpha(logo_file)
        for size, im in (("1180x1040", big(logo, colour, scale, shift)), ("972x906", panel(logo, colour, scale, shift)),
                         ("162x162", slot(logo, colour, scale, shift))):
            path = OUT / f"background_{key}_{size}.png"
            cv2.imwrite(str(path), im)
            print(f"wrote {path.relative_to(REPO)}")


if __name__ == "__main__":
    main()
