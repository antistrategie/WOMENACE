#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy", "opencv-python-headless"]
# ///
"""Make a doll's portrait and badge sprites: portraits from her GFL2 full-body art, badges
from her emblem.

Source: the client's `Avatar_Whole_<Name>SSR` texture (2048x2048, transparent background).
The head is located by matching the client's own `Avatar_Head_<Name>SSR` icon into it, or
given by hand with --head-box when that match fails.

Outputs, all from the one source image:

    sprites/<doll>/bark.png                    460x480 head crop
    sprites/<doll>/slot.png                    same image as bark
    sprites/<doll>/slot_inactive.png           greyscale with the scanline layer
    textures/<doll>/stand_look_right.png       2192x3668 full body
    textures/<doll>/stand_look_left.png        mirror of the right-facing image
    textures/<doll>/stand_look_right_inactive.png  greyscale

Placement copies the hand-made sprites of the existing roster. The stand image is placed by
the figure's whole outline, raised hands, weapons and props above the head included: its top
edge sits near the same line for every doll and its height follows her size class, so short
dolls come out smaller and end higher. The bark crop is placed by the head box. The ranges
below are the spread measured across those sprites, and each doll draws her own values from
them (seeded by her name), so the set keeps the roster's slightly uneven hand placement.

The head box comes from matching the client's Avatar_Head icon, which is a render of the 3D
model rather than a crop of the art, so it lands near the face but can sit off on a tilted or
unusual pose. Check the bark crop: on a good match the box runs from the top of the hair to
just below the chin (chin about three quarters down). Pass --head-box to correct it.

Badges, from an emblem image (--emblem, with --cut-background for one photographed on paper,
--emblem-scale for one that reads too large at the full fit):

    sprites/<doll>/badge_mini.png              48x48
    sprites/<doll>/slot_badge.png              54x54
    sprites/<doll>/badge_unit_window.png       234x234, also the unit's Badge
    sprites/<doll>/badge_white.png             234x234 white silhouette of it, the Badge's state overlay
    sprites/<doll>/big_badge.png               720x270, sewn onto the patch fabric
    sprites/<doll>/badge_dragged.png           90x165, head and shoulders over the emblem in the drag frame

Outfit icons, from that outfit's own art (--outfit <folder> with --whole), cropped around the
head the same way:

    sprites/<doll>/armor/<outfit>/IconEquipment.png   912x318
    sprites/<doll>/armor/<outfit>/Icon.png            144x144
    sprites/<doll>/armor/<outfit>/IconSkillBar.png    261x117

Outfit art often has scenery and odd poses, where the head match fails: check, use --head-box.

The fabric is rebuilt from the roster's sprites, the empty drag frame from vanilla's
(data/badge_*.png). Emblems are centred on their outline's bounding box. badge_dragged cuts its
portrait from stand_look_right.png around the bark crop, so it needs both of those first.

Run:
    uv run scripts/doll/make_doll_sprites.py --doll suomi \\
        --whole Avatar_Whole_SuomiSSR.png --head Avatar_Head_SuomiSSR.png --span 1.104 \\
        --emblem emblem.png --cut-background
"""

import argparse
import hashlib
import json
from pathlib import Path

import cv2
import numpy as np

REPO = Path(__file__).resolve().parents[2]
ASSETS = REPO / "assets" / "additions"
DATA = Path(__file__).resolve().parent / "data"
SCANLINES = DATA / "slot_inactive_scanlines.json"

STAND_SIZE = (2192, 3668)
BARK_SIZE = (460, 480)

# Stand image: the figure outline's top edge, its height by size class (canon span), and the
# head box centre x.
STAND_FIGURE_TOP = (416, 452)
STAND_FIGURE_HEIGHT = ((1.15, (3030, 3110)), (1.40, (3170, 3240)), (9.99, (3330, 3380)))
STAND_HEAD_CX = (1002, 1145)
FIGURE_ALPHA = 40
# With --figure-top, anything raised above that line (a hand, a gun) fades out over this band,
# measured upwards from the drawn figure top, as the hand-made Mechty stand image does.
RAISED_FADE = (110, 190)
# Outfit icons (armor/<outfit>/), head box as fractions of the sprite: size and centre x of
# the width, top of the height. Measured on the outfits whose icons match their art.
OUTFIT_ICONS = {
    "IconEquipment": ((912, 318), (0.165, 0.207), (0.684, 0.697), (-0.062, 0.043)),
    "Icon": ((144, 144), (1.03, 1.21), (0.47, 0.55), (-0.083, -0.031)),
    "IconSkillBar": ((261, 117), (0.337, 0.402), (0.48, 0.573), (-0.088, -0.021)),
}
# Bark crop: head box (the Avatar_Head icon's footprint) size, centre x, top y.
BARK_HEAD_SIZE = (500, 536)
BARK_HEAD_CX = (211, 262)
BARK_HEAD_TOP = (-31, -10)
# Emblem fit boxes (width, height, centre x, centre y), measured on the roster's free-form emblems.
EMBLEM_MINI = (44, 44, 24, 24)
EMBLEM_SLOT = (52, 52, 27, 27)
EMBLEM_UNIT_WINDOW = (218, 200, 117, 117)
EMBLEM_BIG = (286, 240, 360, 133)
EMBLEM_DRAGGED = (78, 56, 45, 116)
# Drag frame pentagon interior: left/top corner, right/top corner, shoulder (bottom of the
# straight sides), point. Fitting keeps PENTAGON_MARGIN px clear of the gold line.
PENTAGON = ((1, 84), (88, 84), (0, 130), (44.5, 155))
PENTAGON_MARGIN = 3
# Drag frame portrait panel (w, h, x, y) over the stripes. The portrait is cut from the stand
# image around the bark crop: DRAGGED_CROP[0] bark widths wide, its top DRAGGED_CROP[1] bark
# pixels above the bark's top, which gives vanilla's head size.
DRAGGED_PORTRAIT = (88, 80, 1, 4)
DRAGGED_CROP = (1.4, 10)


def load_rgba(path):
    im = cv2.imread(str(path), cv2.IMREAD_UNCHANGED)
    if im is None:
        raise SystemExit(f"cannot read {path}")
    if im.shape[2] == 3:
        im = np.dstack([im, np.full(im.shape[:2], 255, np.uint8)])
    return im


def grey(im):
    a = im[..., 3:4].astype(np.float32) / 255
    return cv2.cvtColor((im[..., :3] * a).astype(np.uint8), cv2.COLOR_BGR2GRAY).astype(np.float32)


def find_head(whole, head):
    """Box (x, y, size) of the head icon inside the full art, by multi-scale template match."""
    tg = grey(head)

    def search(scales):
        best = None
        for s in scales:
            sr = cv2.resize(whole, (int(whole.shape[1] * s), int(whole.shape[0] * s)), interpolation=cv2.INTER_AREA)
            if sr.shape[0] < tg.shape[0] or sr.shape[1] < tg.shape[1]:
                continue
            _, mv, _, ml = cv2.minMaxLoc(cv2.matchTemplate(grey(sr), tg, cv2.TM_CCOEFF_NORMED))
            if best is None or mv > best[0]:
                best = (mv, s, ml)
        return best

    mv, s, _ = search(np.geomspace(0.1, 1.2, 40))
    mv, s, (x, y) = search(np.linspace(s * 0.95, s * 1.05, 11))
    return mv, (x / s, y / s, head.shape[1] / s)


def rng_for(*parts):
    """Placement randomness seeded by the doll (and outfit), so reruns give the same sprites."""
    key = "/".join(parts).encode()
    return np.random.default_rng(int.from_bytes(hashlib.sha256(key).digest()[:8], "little"))


def draw(rng, lo_hi):
    return rng.uniform(*lo_hi)


def place(whole, box, size, head_size, head_cx, head_top):
    """Scale the art so the head box is head_size wide and put it at (head_cx, head_top)."""
    hx, hy, hs = box
    s = head_size / hs
    return warp(whole, s, head_cx - (hx + hs / 2) * s, head_top - hy * s, size)


def place_figure(whole, box, size, top, height, head_cx, figure_top=None):
    """Scale the art so the figure outline is `height` tall, its top edge at `top`.

    figure_top (source pixels) overrides the outline's top, for a doll with a hand or weapon
    raised high above her head: she is sized and placed from that line, and what rises above
    it fades out instead of pushing her down.
    """
    ys, _ = np.where(whole[..., 3] > FIGURE_ALPHA)
    first = ys.min() if figure_top is None else figure_top
    hx, _, hs = box
    s = height / (ys.max() - first + 1)
    out = warp(whole, s, head_cx - (hx + hs / 2) * s, top - first * s, size)
    if figure_top is not None:
        near, far = RAISED_FADE
        rows = np.arange(size[1], dtype=np.float32)
        fade = np.clip((rows - (top - far)) / (far - near), 0, 1)[:, None]
        out[..., 3] = (out[..., 3] * fade).astype(np.uint8)
    return out


def warp(whole, s, tx, ty, size):
    m = np.float32([[s, 0, tx], [0, s, ty]])
    # Premultiplied resampling so transparent edges carry no dark fringe.
    a = whole[..., 3:4].astype(np.float32) / 255
    pre = np.dstack([whole[..., :3].astype(np.float32) * a, whole[..., 3:4].astype(np.float32)])
    out = cv2.warpAffine(pre, m, size, flags=cv2.INTER_LANCZOS4, borderMode=cv2.BORDER_CONSTANT, borderValue=0)
    out = np.clip(out, 0, 255)
    alpha = out[..., 3:4]
    rgb = np.where(alpha > 0, out[..., :3] / np.maximum(alpha / 255, 1e-6), 0)
    return np.dstack([np.clip(rgb, 0, 255), alpha]).astype(np.uint8)


def luminance(im):
    return 0.299 * im[..., 2] + 0.587 * im[..., 1] + 0.114 * im[..., 0]


def greyscale(im):
    lum = np.clip(luminance(im.astype(np.float32)), 0, 255).astype(np.uint8)
    return np.dstack([lum, lum, lum, im[..., 3]])


def slot_inactive(slot):
    """Greyscale under the scanline layer the hand-made slot_inactive sprites share."""
    sl = json.loads(SCANLINES.read_text())
    gain = np.array(sl["gain"], np.float32)[:, None]
    offset = np.array(sl["offset"], np.float32)[:, None]
    overlay = np.array(sl["overlay"], np.float32)[:, None]
    a = slot[..., 3].astype(np.float32) / 255
    lined = luminance(slot.astype(np.float32)) * gain + offset
    a_out = (sl["alpha_gain"] * a * 255 + sl["alpha_offset"]) / 255
    layer = sl["alpha_offset"] / 255
    value = (lined * a + overlay * layer * (1 - a)) / np.maximum(a + layer * (1 - a), 1e-6)
    value = np.clip(value, 0, 255).astype(np.uint8)
    return np.dstack([value, value, value, np.clip(a_out * 255, 0, 255).astype(np.uint8)])


def cut_background(im):
    """Drop a light, low-saturation backdrop (paper, a photo table) around an outlined emblem.

    The backdrop is every bright grey pixel connected to the image border, so the emblem needs
    a closed darker outline. Holes inside the emblem stay opaque.
    """
    bgr = im[..., :3].astype(np.float32)
    lum = bgr.mean(-1)
    sat = bgr.max(-1) - bgr.min(-1)
    cand = ((lum > 150) & (sat < 40)).astype(np.uint8)
    _, lbl = cv2.connectedComponents(cand, connectivity=4)
    border = set(np.unique(np.concatenate([lbl[0], lbl[-1], lbl[:, 0], lbl[:, -1]]))) - {0}
    fg = (~np.isin(lbl, list(border))).astype(np.uint8)
    _, comp, stats, _ = cv2.connectedComponentsWithStats(fg, 8)
    fg = (comp == 1 + np.argmax(stats[1:, cv2.CC_STAT_AREA])).astype(np.uint8)
    inv = 1 - fg
    _, holes = cv2.connectedComponents(inv, connectivity=4)
    outside = set(np.unique(np.concatenate([holes[0], holes[-1], holes[:, 0], holes[:, -1]])))
    fg[(inv == 1) & ~np.isin(holes, list(outside))] = 1
    alpha = cv2.GaussianBlur(cv2.erode(fg, np.ones((3, 3), np.uint8)).astype(np.float32), (0, 0), 0.9)
    out = np.dstack([im[..., :3], (alpha * 255).astype(np.uint8)])
    ys, xs = np.where(alpha > 0.05)
    return out[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def fit(emblem, box_w, box_h):
    """Resize the emblem to fit inside box_w x box_h, keeping its aspect."""
    h, w = emblem.shape[:2]
    s = min(box_w / w, box_h / h)
    size = (max(1, round(w * s)), max(1, round(h * s)))
    a = emblem[..., 3:4].astype(np.float32) / 255
    pre = np.dstack([emblem[..., :3].astype(np.float32) * a, emblem[..., 3:4].astype(np.float32)])
    pre = cv2.resize(pre, size, interpolation=cv2.INTER_AREA)
    alpha = pre[..., 3:4]
    rgb = np.where(alpha > 0, pre[..., :3] / np.maximum(alpha / 255, 1e-6), 0)
    return np.dstack([np.clip(rgb, 0, 255), alpha]).astype(np.uint8)


def over(base, top, cx, cy):
    """Alpha-composite top onto base (both BGRA uint8), centred at (cx, cy)."""
    out = base.astype(np.float32)
    h, w = top.shape[:2]
    x0, y0 = round(cx - w / 2), round(cy - h / 2)
    bx0, by0, bx1, by1 = max(0, x0), max(0, y0), min(base.shape[1], x0 + w), min(base.shape[0], y0 + h)
    t = top[by0 - y0:by1 - y0, bx0 - x0:bx1 - x0].astype(np.float32)
    b = out[by0:by1, bx0:bx1]
    ta, ba = t[..., 3:4] / 255, b[..., 3:4] / 255
    oa = ta + ba * (1 - ta)
    rgb = (t[..., :3] * ta + b[..., :3] * ba * (1 - ta)) / np.maximum(oa, 1e-6)
    out[by0:by1, bx0:bx1] = np.dstack([rgb, oa * 255])
    return np.clip(out, 0, 255).astype(np.uint8)


def fit_pentagon(emblem):
    """Largest fit of the emblem whose outline stays inside the drag frame's gold pentagon.

    A plain box fit lets square corners poke through where the pentagon narrows, so the
    emblem's own alpha is tested against the frame interior (inset by PENTAGON_MARGIN), trying
    sizes from the full box down and centres nearest the preferred one first.
    """
    bw, bh, cx, cy = EMBLEM_DRAGGED
    yy, xx = np.mgrid[:165, :90]
    (l, t), (r, _), (_, b), (px, py) = PENTAGON
    m = PENTAGON_MARGIN
    # Below the shoulders the sides run straight to the point.
    slope = (py - b) / (px - l)
    edge = np.where(xx <= px, b + slope * (xx - l), b + slope * (r - xx))
    inside = (xx >= l + m) & (xx <= r - m) & (yy >= t + m) & (yy <= np.maximum(b, edge) - m * 1.4)
    for k in np.linspace(1.0, 0.5, 26):
        im = fit(emblem, bw * k, bh * k)
        h, w = im.shape[:2]
        solid = im[..., 3] > 64
        for dy in sorted(range(-20, 21), key=abs):
            for dx in sorted(range(-6, 7), key=abs):
                x0, y0 = round(cx + dx - w / 2), round(cy + dy - h / 2)
                if x0 < 0 or y0 < 0 or x0 + w > 90 or y0 + h > 165:
                    continue
                if inside[y0:y0 + h, x0:x0 + w][solid].all():
                    return im, cx + dx, cy + dy
    raise SystemExit("emblem does not fit the drag frame")


def find_bark(stand, bark):
    """(scale, x, y) of the bark crop inside the stand image: both are cut from the same art."""
    k = 4
    sg = grey(cv2.resize(stand, (stand.shape[1] // k, stand.shape[0] // k), interpolation=cv2.INTER_AREA))
    sg = sg[:sg.shape[0] * 2 // 5]
    best = None
    for s in np.geomspace(0.4, 2.0, 40):
        w, h = round(BARK_SIZE[0] * s / k), round(BARK_SIZE[1] * s / k)
        if h >= sg.shape[0]:
            continue
        tg = grey(cv2.resize(bark, (w, h), interpolation=cv2.INTER_AREA))
        _, mv, _, ml = cv2.minMaxLoc(cv2.matchTemplate(sg, tg, cv2.TM_CCOEFF_NORMED))
        if best is None or mv > best[0]:
            best = (mv, s, ml[0] * k, ml[1] * k)
    return best[1:]


def dragged_portrait(stand, bark):
    """Head and shoulders for the drag frame, cut from the stand image so the stripes show round her."""
    pw, ph, _, _ = DRAGGED_PORTRAIT
    s, x, y = find_bark(stand, bark)
    width, top = DRAGGED_CROP
    w = width * BARK_SIZE[0] * s
    h = w * ph / pw
    x0, y0 = x + BARK_SIZE[0] * s / 2 - w / 2, y - top * s
    m = np.float32([[pw / w, 0, -x0 * pw / w], [0, ph / h, -y0 * ph / h]])
    a = stand[..., 3:4].astype(np.float32) / 255
    pre = np.dstack([stand[..., :3] * a, stand[..., 3:4]]).astype(np.float32)
    pre = cv2.warpAffine(pre, m, (pw, ph), flags=cv2.INTER_AREA, borderValue=0)
    alpha = pre[..., 3:4]
    rgb = np.where(alpha > 0, pre[..., :3] / np.maximum(alpha / 255, 1e-6), 0)
    return np.dstack([np.clip(rgb, 0, 255), alpha]).astype(np.uint8)


def badge_dragged(emblem, stand, bark):
    """The drag frame: portrait over the striped panel, emblem inside the pentagon."""
    frame = cv2.imread(str(DATA / "badge_dragged_frame_90x165.png"), cv2.IMREAD_UNCHANGED)
    inner, cx, cy = fit_pentagon(emblem)
    out = over(frame, inner, cx, cy)
    pw, ph, px, py = DRAGGED_PORTRAIT
    return over(out, dragged_portrait(stand, bark), px + pw / 2, py + ph / 2)


def badge_white(badge):
    """The badge's flat white silhouette, holes filled. The unit badge draws it over the Badge,
    tinted by state (white pulse when wavering or fleeing, grey when the turn is done, red when
    stabilised or dead), so it has to cover the same shape."""
    a = badge[..., 3]
    # Close gaps first (see-through panels, open lettering) so the fill finds them enclosed.
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15))
    solid = cv2.morphologyEx((a > 12).astype(np.uint8), cv2.MORPH_CLOSE, k)
    _, lbl = cv2.connectedComponents(1 - solid, connectivity=4)
    outside = set(np.unique(np.concatenate([lbl[0], lbl[-1], lbl[:, 0], lbl[:, -1]])))
    filled = ((solid == 1) | ~np.isin(lbl, list(outside))) & (cv2.erode(solid, np.ones((3, 3), np.uint8)) == 1)
    out = np.full(badge.shape, 255, np.uint8)
    out[..., 3] = np.maximum(a, filled.astype(np.uint8) * 255)
    return out


def badges(emblem, bark, stand):
    """Emblem sprites: plain at three sizes, on the patch fabric, and in the drag frame."""
    out = {}
    for name, (bw, bh, cx, cy), size in (("badge_mini", EMBLEM_MINI, 48), ("slot_badge", EMBLEM_SLOT, 54),
                                         ("badge_unit_window", EMBLEM_UNIT_WINDOW, 234)):
        out[name] = over(np.zeros((size, size, 4), np.uint8), fit(emblem, bw, bh), cx, cy)

    fabric = cv2.imread(str(DATA / "badge_fabric_720x270.png"))
    fabric = np.dstack([fabric, np.full(fabric.shape[:2], 255, np.uint8)])
    bw, bh, cx, cy = EMBLEM_BIG
    patch = fit(emblem, bw, bh)
    # A sewn-on patch: soft shadow under it, and the weave showing faintly through the print.
    shadow = np.zeros_like(patch)
    shadow[..., 3] = cv2.GaussianBlur(patch[..., 3], (0, 0), 4) * 0.6
    big = over(fabric, shadow, cx + 3, cy + 4)
    weave = fabric[..., :3].astype(np.float32)
    weave = weave - cv2.GaussianBlur(weave, (0, 0), 6)
    h, w = patch.shape[:2]
    x0, y0 = round(cx - w / 2), round(cy - h / 2)
    textured = patch.copy()
    textured[..., :3] = np.clip(patch[..., :3].astype(np.float32) + 0.8 * weave[y0:y0 + h, x0:x0 + w], 0, 255)
    out["big_badge"] = over(big, textured, cx, cy)

    out["badge_white"] = badge_white(out["badge_unit_window"])
    out["badge_dragged"] = badge_dragged(emblem, stand, bark)
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--doll", required=True, help="asset folder name, e.g. suomi")
    ap.add_argument("--whole", help="Avatar_Whole_<Name>SSR.png, makes the portrait sprites")
    ap.add_argument("--head", help="Avatar_Head_<Name>SSR.png, used to find the head")
    ap.add_argument("--head-box", help="x,y,size of the head box in the full art, skips the match")
    ap.add_argument("--span", type=float, help="canon span in metres (see the height policy)")
    ap.add_argument("--seed", help="placement seed, defaults to the doll name")
    ap.add_argument("--figure-top", type=float, help="source y to treat as the figure's top (usually the "
                    "top of the head) when a raised hand or weapon reaches far above it: that part fades out")
    ap.add_argument("--outfit", help="outfit folder name: --whole is then that outfit's art and the "
                    "script makes armor/<outfit>/ Icon, IconEquipment and IconSkillBar")
    ap.add_argument("--emblem", help="the doll's emblem image, makes the badge sprites (needs bark.png and stand_look_right.png)")
    ap.add_argument("--cut-background", action="store_true", help="remove a light backdrop around the emblem")
    ap.add_argument("--emblem-scale", type=float, default=1.0, help="shrink the emblem inside its fit boxes, "
                    "for a wide or text-heavy emblem that reads larger than the roster's at the full fit")
    args = ap.parse_args()
    if not 0 < args.emblem_scale <= 1:
        ap.error("--emblem-scale shrinks the emblem: give a value above 0 and at most 1")

    if args.whole and args.outfit:
        outfit_icons(args)
    elif args.whole:
        portraits(args)
    if args.emblem:
        emblem = load_rgba(args.emblem)
        if args.cut_background:
            emblem = cut_background(emblem)
        ys, xs = np.where(emblem[..., 3] > 12)
        emblem = emblem[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
        if args.emblem_scale < 1.0:
            # A transparent margin, so every box fit lands at this fraction of its full size.
            h, w = emblem.shape[:2]
            ph = round(h * (1 / args.emblem_scale - 1) / 2)
            pw = round(w * (1 / args.emblem_scale - 1) / 2)
            emblem = cv2.copyMakeBorder(emblem, ph, ph, pw, pw, cv2.BORDER_CONSTANT, value=(0, 0, 0, 0))
        sprites = ASSETS / "sprites" / args.doll
        bark = load_rgba(sprites / "bark.png")
        stand = load_rgba(ASSETS / "textures" / args.doll / "stand_look_right.png")
        for name, im in badges(emblem, bark, stand).items():
            path = sprites / f"{name}.png"
            cv2.imwrite(str(path), im)
            print(f"wrote {path.relative_to(REPO)}")


def head_box(args, whole):
    if args.head_box:
        return tuple(float(v) for v in args.head_box.split(","))
    if args.head:
        score, box = find_head(whole, load_rgba(args.head))
        print(f"head match {score:.3f}: box x={box[0]:.0f} y={box[1]:.0f} size={box[2]:.0f}")
        if score < 0.6:
            raise SystemExit("head match is weak, pass --head-box by hand")
        return box
    raise SystemExit("pass --head or --head-box")


def outfit_icons(args):
    """The outfit's three item icons, cropped from its art around the head like the roster's."""
    whole = load_rgba(args.whole)
    box = head_box(args, whole)
    rng = rng_for(args.seed or args.doll, args.outfit)
    folder = ASSETS / "sprites" / args.doll / "armor" / args.outfit
    folder.mkdir(parents=True, exist_ok=True)
    for name, ((w, h), size, cx, top) in OUTFIT_ICONS.items():
        im = place(whole, box, (w, h), w * draw(rng, size), w * draw(rng, cx), h * draw(rng, top))
        path = folder / f"{name}.png"
        cv2.imwrite(str(path), im)
        print(f"wrote {path.relative_to(REPO)}")


def portraits(args):
    if args.span is None:
        raise SystemExit("--span is needed with --whole")
    whole = load_rgba(args.whole)
    box = head_box(args, whole)

    rng = rng_for(args.seed or args.doll)
    height = next(rng_ for limit, rng_ in STAND_FIGURE_HEIGHT if args.span < limit)
    stand = place_figure(whole, box, STAND_SIZE, draw(rng, STAND_FIGURE_TOP), draw(rng, height), draw(rng, STAND_HEAD_CX),
                         args.figure_top)
    bark = place(whole, box, BARK_SIZE, draw(rng, BARK_HEAD_SIZE), draw(rng, BARK_HEAD_CX), draw(rng, BARK_HEAD_TOP))

    sprites = ASSETS / "sprites" / args.doll
    textures = ASSETS / "textures" / args.doll
    sprites.mkdir(parents=True, exist_ok=True)
    textures.mkdir(parents=True, exist_ok=True)
    outputs = {
        sprites / "bark.png": bark,
        sprites / "slot.png": bark,
        sprites / "slot_inactive.png": slot_inactive(bark),
        textures / "stand_look_right.png": stand,
        textures / "stand_look_left.png": stand[:, ::-1],
        textures / "stand_look_right_inactive.png": greyscale(stand),
    }
    for path, im in outputs.items():
        cv2.imwrite(str(path), np.ascontiguousarray(im))
        print(f"wrote {path.relative_to(REPO)}")


if __name__ == "__main__":
    main()
