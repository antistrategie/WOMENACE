#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy", "opencv-python-headless"]
# ///
"""Make an Armed Echelon's portrait, badge and weapon sprites from its GFL2 art.

A team is three Dolls in one piece of art (the client's `Support_Whole_GuildWar_<Team>`,
2048x2048, transparent background), so the portraits differ from a Doll's:

    sprites/<team>/bark.png               460x480, the crew's faces in three panels split by "||"
    sprites/<team>/slot.png               same image as bark
    sprites/<team>/slot_inactive.png      greyscale with the scanline layer
    textures/<team>/stand_look_right.png  2192x3668, the crew in the same panels, feet level as drawn
    textures/<team>/stand_look_left.png   mirror
    textures/<team>/stand_look_right_inactive.png  greyscale

The emblem is the team's active skill icon over its faction's logo in black and white (--logo, --icon), and the
badge sprites are made from it as make_doll_sprites.py makes a Doll's. The badge_dragged
portrait is cut around the middle panel's face.

The weapon icons come from a side render of the team's weapon (--weapon-render, from
scripts/support_team/render_weapon_side.py), straightened by the tube's measured angle:

    sprites/weapon/<team>/IconEquipment.png         912x318
    sprites/weapon/<team>/IconEquipmentDisabled.png flat grey
    sprites/weapon/<team>/IconSkillBar.png          465x111
    sprites/weapon/<team>/IconSkillBarDisabled.png  flat grey
    sprites/weapon/<team>/Icon.png                  144x144

Run:
    uv run scripts/support_team/make_team_sprites.py --team limonad \\
        --whole Support_Whole_GuildWar_ATG01.png \\
        --heads 625,840,130 1005,890,165 1445,995,150 \\
        --logo Img_Guild_Faction_Logo_1.png --icon Skill_ATG01_Active.png \\
        --weapon-render launcher_raw.png
"""

import argparse
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "doll"))
import make_doll_sprites as doll  # noqa: E402

PANEL_GAP = 8
# Each face: the head box (the helmet) this wide in its panel, its top this far down.
PANEL_HEAD_WIDTH = 0.84
PANEL_HEAD_TOP = 34
# The "||" divider: two light lines in the gap between panels.
DIVIDER_COLOUR = (170, 205, 222)
DIVIDER_LINE = 2
# Behind the faces, as a Doll's bark has her art's own backdrop: a dark olive fall-off.
BACKDROP_TOP = (52, 58, 54)
BACKDROP_BOTTOM = (24, 28, 27)

# The stand image: the frames that show it crop it (vanilla's UXML, scale-and-crop). The unit
# window's LeaderMask shows x 562 to 1594 and y 0 to 1922 of it, and hiring's LeaderBackground
# x 393 to 1798 and y 336 to 1904. The panels fill a box inside both, as wide as the unit
# window's crop so each panel shows as much of its Doll as it can, the highest head this far
# below its top, the art's lowest point this far above its foot, and the rest of the image is clear.
STAND_BOX = (562, 390, 1032, 1440)
STAND_GAP = 16
STAND_BOX_HEAD_TOP = 40
STAND_BOX_FOOT_MARGIN = 10

# The faction logo is drawn white with a black outline, the skill icon over its middle at this
# share of its width.
LOGO_COLOUR = (255, 255, 255)
LOGO_OUTLINE = 6
ICON_SHARE = 0.62
ICON_HALO = 10

# The squad type icon: the skill icon in white, its weapon edged in black where it lies over the
# rest, and a black outline. A pixel this little saturated is the weapon's grey.
TYPE_ICON_GREY_SATURATION = 60
TYPE_ICON_EDGE = 2
TYPE_ICON_OUTLINE = 3

WEAPON_ICONS = {
    # name: (size, (x0, x1, y0, y1) box the weapon fits inside, measured on the roster's icons)
    "IconEquipment": ((912, 318), (41, 870, 57, 260)),
    "IconSkillBar": ((465, 111), (26, 438, 4, 106)),
    "Icon": ((144, 144), (0, 143, 30, 113)),
}
# A disabled icon is the weapon in flat grey, this share of its luminance.
DISABLED_GAIN = 0.54

# A skill's icon on the skill bar, the size of the mod's other skill icons.
SKILL_ICON_SIZE = (64, 64)

# An accessory's icon, the size of vanilla's (accessory_21_186x186), the art filling this share.
ITEM_ICON_SIZE = (186, 186)
ITEM_ICON_SHARE = 0.92


def backdrop(w, h):
    t = np.linspace(0, 1, h, dtype=np.float32)[:, None, None]
    top = np.array(BACKDROP_TOP, np.float32)
    bottom = np.array(BACKDROP_BOTTOM, np.float32)
    rgb = np.broadcast_to(top + (bottom - top) * t, (h, w, 3))
    return np.dstack([rgb, np.full((h, w), 255, np.float32)]).astype(np.uint8)


def panels(whole, heads, size, gap, head_width, head_top, backdrop_on, foot=None, bleed=(0, 0)):
    """The crew side by side in equal panels of `size`, split by a "||" divider in each gap.
    Each head box is `head_width` of its panel wide with its top at `head_top`. With `as_drawn`
    the crew keep the art's own proportions instead when `foot` is given: one scale puts the
    highest head's top at `head_top` and the art's lowest point on `foot`, and every panel is
    cut at that scale and height, so their feet stay level as drawn. `bleed` widens the first
    panel to the left and the last to the right by that much, showing more of the art beside them
    where a frame is wider than the panels."""
    w, h = size
    panel_w = (w - gap * (len(heads) - 1)) // len(heads)
    w += bleed[0] + bleed[1]
    out = backdrop(w, h) if backdrop_on else np.zeros((h, w, 4), np.uint8)
    line = max(DIVIDER_LINE, round(gap * DIVIDER_LINE / PANEL_GAP))
    x = 0
    top_head = min(heads, key=lambda head: head[1])
    as_drawn = foot is not None
    if as_drawn:
        lowest = np.where(whole[..., 3] > doll.FIGURE_ALPHA)[0].max()
        drawn = (foot - head_top) / (lowest - top_head[1])
    for i, (hx, hy, hs) in enumerate(heads):
        s = drawn if as_drawn else panel_w * head_width / hs
        y = top_head[1] if as_drawn else hy
        left = bleed[0] if i == 0 else 0
        slice_w = panel_w + left + (bleed[1] if i == len(heads) - 1 else 0)
        face = doll.warp(whole, s, left + panel_w / 2 - (hx + hs / 2) * s, head_top - y * s, (slice_w, h))
        out[:, x:x + slice_w] = doll.over(out[:, x:x + slice_w].copy(), face, slice_w / 2, h / 2)
        x += slice_w
        if i < len(heads) - 1:
            divider = out[:, x:x + gap]
            divider[..., :3] = (10, 12, 12)
            divider[..., 3] = 255
            for line_x in (round(gap / 8), gap - round(gap / 8) - line):
                divider[:, line_x:line_x + line, :3] = DIVIDER_COLOUR[::-1]
            x += gap
    return out


def bark(whole, heads):
    out = panels(whole, heads, doll.BARK_SIZE, PANEL_GAP, PANEL_HEAD_WIDTH, PANEL_HEAD_TOP, True)
    out[..., 3] = 251
    return out


def stand(whole, heads):
    """The crew in the same panels, standing as the art draws them, feet level, inside the part of
    the stand image the roster and recruitment frames show. The outer panels run on to the image's
    edges with the art beside them, which the wider recruitment frame shows."""
    x, y, w, h = STAND_BOX
    bleed = (x, doll.STAND_SIZE[0] - x - w)
    box = panels(whole, heads, (w, h), STAND_GAP, 0, STAND_BOX_HEAD_TOP, False,
                 foot=h - STAND_BOX_FOOT_MARGIN, bleed=bleed)
    out = np.zeros((doll.STAND_SIZE[1], doll.STAND_SIZE[0], 4), np.uint8)
    out[y:y + h] = box
    return out


def emblem(logo, icon):
    """The skill icon over the faction's logo, with a dark halo so it reads on the logo."""
    logo = logo.copy()
    logo[..., :3] = LOGO_COLOUR[::-1]
    ys, xs = np.where(logo[..., 3] > 12)
    logo = logo[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    pad = LOGO_OUTLINE + 2
    logo = cv2.copyMakeBorder(logo, pad, pad, pad, pad, cv2.BORDER_CONSTANT, value=(0, 0, 0, 0))
    outline = np.zeros_like(logo)
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (LOGO_OUTLINE * 2 + 1,) * 2)
    outline[..., 3] = cv2.dilate(logo[..., 3], k)
    logo = doll.over(outline, logo, logo.shape[1] / 2, logo.shape[0] / 2)
    h, w = logo.shape[:2]
    size = round(w * ICON_SHARE)
    icon = cv2.resize(icon, (size, size), interpolation=cv2.INTER_LANCZOS4)
    pad = ICON_HALO * 2
    icon = cv2.copyMakeBorder(icon, pad, pad, pad, pad, cv2.BORDER_CONSTANT, value=(0, 0, 0, 0))
    halo = np.zeros_like(icon)
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (ICON_HALO * 2 + 1,) * 2)
    halo[..., 3] = cv2.GaussianBlur(cv2.dilate(icon[..., 3], k), (0, 0), ICON_HALO / 3)
    halo[..., :3] = (14, 18, 16)
    out = doll.over(logo, halo, w / 2, h / 2)
    return doll.over(out, icon, w / 2, h / 2)


def unit_type_icon(icon):
    """The skill icon in black and white, for the squad type row: every part white, the weapon
    kept apart from what is behind it by a black edge, and a black outline round the whole."""
    icon = icon.astype(np.float32)
    alpha = icon[..., 3]
    hsv = cv2.cvtColor(icon[..., :3].astype(np.uint8), cv2.COLOR_BGR2HSV)
    weapon = ((hsv[..., 1] < TYPE_ICON_GREY_SATURATION) & (alpha > 40)).astype(np.uint8) * 255
    # The weapon keeps its own shading, from its lightest part down.
    shade = cv2.cvtColor(icon[..., :3].astype(np.uint8), cv2.COLOR_BGR2GRAY).astype(np.float32)
    lit = np.where(weapon > 0, shade / max(1.0, shade[weapon > 0].max()), 1.0)
    pad = TYPE_ICON_OUTLINE + 2
    out = np.zeros((icon.shape[0] + 2 * pad, icon.shape[1] + 2 * pad, 4), np.float32)
    inner = out[pad:-pad, pad:-pad]
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (TYPE_ICON_OUTLINE * 2 + 1,) * 2)
    out[..., 3] = cv2.dilate(cv2.copyMakeBorder(alpha, pad, pad, pad, pad, cv2.BORDER_CONSTANT, value=0), k)
    edge = cv2.dilate(weapon, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (TYPE_ICON_EDGE * 2 + 1,) * 2))
    white = np.clip(lit * 255, 0, 255)
    inner[..., 0] = inner[..., 1] = inner[..., 2] = np.where((edge > 0) & (weapon == 0), 0, white) * (alpha / 255)
    return np.clip(out, 0, 255).astype(np.uint8)


def straighten(render):
    """Turn the weapon so its long axis is level, and cut it to its outline."""
    a = render[..., 3]
    ys, xs = np.where(a > 40)
    pts = np.column_stack([xs, ys]).astype(np.float32)
    mean = pts.mean(0)
    _, _, vt = np.linalg.svd(pts - mean, full_matrices=False)
    angle = np.degrees(np.arctan2(vt[0][1], vt[0][0]))
    if angle > 90:
        angle -= 180
    elif angle < -90:
        angle += 180
    m = cv2.getRotationMatrix2D((float(mean[0]), float(mean[1])), angle, 1.0)
    pre = render.astype(np.float32)
    pre[..., :3] *= pre[..., 3:4] / 255
    pre = cv2.warpAffine(pre, m, (render.shape[1], render.shape[0]), flags=cv2.INTER_LANCZOS4, borderValue=0)
    pre = np.clip(pre, 0, 255)
    alpha = pre[..., 3:4]
    rgb = np.where(alpha > 0, pre[..., :3] / np.maximum(alpha / 255, 1e-6), 0)
    out = np.dstack([np.clip(rgb, 0, 255), alpha]).astype(np.uint8)
    ys, xs = np.where(out[..., 3] > 12)
    print(f"weapon straightened by {angle:.2f} degrees")
    return out[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def weapon_icons(weapon):
    out = {}
    for name, ((w, h), (x0, x1, y0, y1)) in WEAPON_ICONS.items():
        fitted = doll.fit(weapon, x1 - x0, y1 - y0)
        icon = doll.over(np.zeros((h, w, 4), np.uint8), fitted, (x0 + x1) / 2, (y0 + y1) / 2)
        out[name] = icon
        if name != "Icon":
            lum = np.clip(doll.luminance(icon.astype(np.float32)) * DISABLED_GAIN, 0, 255).astype(np.uint8)
            out[name + "Disabled"] = np.dstack([lum, lum, lum, icon[..., 3]])
    return out


def skill_icons(icon):
    """The team's skill icon as its fire skill's icon on the skill bar, and the disabled one."""
    icon = cv2.resize(icon, SKILL_ICON_SIZE, interpolation=cv2.INTER_AREA)
    lum = np.clip(doll.luminance(icon.astype(np.float32)) * DISABLED_GAIN, 0, 255).astype(np.uint8)
    return icon, np.dstack([lum, lum, lum, icon[..., 3]])


def item_icons(art):
    """An accessory's icon and its disabled one, the art fitted into vanilla's square."""
    w, h = ITEM_ICON_SIZE
    fitted = doll.fit(art, round(w * ITEM_ICON_SHARE), round(h * ITEM_ICON_SHARE))
    icon = doll.over(np.zeros((h, w, 4), np.uint8), fitted, w / 2, h / 2)
    lum = np.clip(doll.luminance(icon.astype(np.float32)) * DISABLED_GAIN, 0, 255).astype(np.uint8)
    return icon, np.dstack([lum, lum, lum, icon[..., 3]])


def write(path, im):
    path.parent.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(path), np.ascontiguousarray(im))
    print(f"wrote {path.relative_to(doll.REPO)}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--team", required=True, help="asset folder name, e.g. limonad")
    ap.add_argument("--whole", required=True, help="Support_Whole_GuildWar_<Team>.png")
    ap.add_argument("--heads", nargs="+", required=True, help="x,y,size of each crew member's head box, left to right")
    ap.add_argument("--logo", required=True, help="the faction's logo (Img_Guild_Faction_Logo_<n>.png)")
    ap.add_argument("--icon", required=True, help="the team's active skill icon (Skill_<Team>_Active.png)")
    ap.add_argument("--weapon-render", help="side render of the team's weapon on a transparent background")
    ap.add_argument("--emblem-out", help="also save the composed emblem here, for review")
    ap.add_argument("--item", nargs="+", default=[], metavar="NAME=PATH",
                    help="an accessory's art, written as sprites/<team>/items/<NAME>.png and <NAME>_disabled.png")
    args = ap.parse_args()

    whole = doll.clear_stray_alpha(doll.load_rgba(args.whole))
    heads = [tuple(float(v) for v in h.split(",")) for h in args.heads]
    sprites = doll.ASSETS / "sprites" / args.team
    textures = doll.ASSETS / "textures" / args.team

    portrait = bark(whole, heads)
    standing = stand(whole, heads)
    write(sprites / "bark.png", portrait)
    write(sprites / "slot.png", portrait)
    write(sprites / "slot_inactive.png", doll.slot_inactive(portrait))
    write(textures / "stand_look_right.png", standing)
    write(textures / "stand_look_left.png", standing[:, ::-1])
    write(textures / "stand_look_right_inactive.png", doll.greyscale(standing))

    write(sprites / "unit_type.png", unit_type_icon(doll.load_rgba(args.icon)))
    badge = emblem(doll.load_rgba(args.logo), doll.load_rgba(args.icon))
    if args.emblem_out:
        cv2.imwrite(args.emblem_out, badge)
    # badge_dragged cuts its portrait around a bark crop found in the stand image: the middle
    # face, cut as a Doll's bark is, stands in for the crew.
    hx, hy, hs = heads[len(heads) // 2]
    middle = doll.place(whole, (hx, hy, hs), doll.BARK_SIZE, 500, 236, -20)
    for name, im in doll.badges(badge, middle, doll.place_figure(whole, (hx, hy, hs), doll.STAND_SIZE, 430, 3100, 1070)).items():
        write(sprites / f"{name}.png", im)

    skill, skill_disabled = skill_icons(doll.load_rgba(args.icon))
    write(doll.ASSETS / "sprites" / "weapon" / args.team / "SkillIcon.png", skill)
    write(doll.ASSETS / "sprites" / "weapon" / args.team / "SkillIconDisabled.png", skill_disabled)

    for item in args.item:
        name, path = item.split("=", 1)
        icon, disabled = item_icons(doll.load_rgba(path))
        write(sprites / "items" / f"{name}.png", icon)
        write(sprites / "items" / f"{name}_disabled.png", disabled)

    if args.weapon_render:
        for name, im in weapon_icons(straighten(doll.load_rgba(args.weapon_render))).items():
            write(doll.ASSETS / "sprites" / "weapon" / args.team / f"{name}.png", im)


if __name__ == "__main__":
    main()
