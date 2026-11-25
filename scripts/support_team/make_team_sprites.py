#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy", "opencv-python-headless"]
# ///
"""Make an Armed Echelon's portrait, badge, weapon, skill and item sprites from its GFL2 art.

A team is its crew of Dolls in one piece of art (the client's `Support_Whole_GuildWar_<Team>`,
2048x2048, transparent background), with one --heads box per Doll from left to right, --lead
naming the team's leader among them and a --front-outline round her, so the portraits differ from
a Doll's. Where a portrait shows one Doll it is the leader.

    sprites/<team>/bark.png               460x480, the leader's face, cut as a Doll's bark is
    sprites/<team>/slot.png               the same face on a transparent background
    sprites/<team>/slot_inactive.png      greyscale with the scanline layer
    textures/<team>/stand_look_right.png  2192x3668, the leader in front, the others behind
    textures/<team>/stand_look_left.png   mirror
    textures/<team>/stand_look_right_inactive.png  greyscale

The emblem is the team's active skill icon over its faction's logo, the logo drawn white with a
black outline (--logo, --icon), and the badge sprites are made from it as make_doll_sprites.py
makes a Doll's. The badge_dragged portrait shows the leader.

    sprites/<team>/unit_type.png          the skill icon in white with black edges, for the squad type row
    sprites/<team>/badge_mini.png, slot_badge.png, badge_unit_window.png, big_badge.png,
        badge_white.png, badge_dragged.png

The skill bar icon is the skill icon itself, and each --item is an accessory's icon:

    sprites/weapon/<team>/SkillIcon.png             64x64
    sprites/weapon/<team>/SkillIconDisabled.png     flat grey
    sprites/<team>/items/<name>.png                 186x186
    sprites/<team>/items/<name>_disabled.png        flat grey

The weapon icons come from a side render of the team's weapon (--weapon-render, from
scripts/weapon/render_weapon.py as prepare_team.py's render step runs it), straightened by the
weapon's measured angle:

    sprites/weapon/<team>/IconEquipment.png         912x318
    sprites/weapon/<team>/IconEquipmentDisabled.png flat grey
    sprites/weapon/<team>/IconSkillBar.png          465x111
    sprites/weapon/<team>/IconSkillBarDisabled.png  flat grey
    sprites/weapon/<team>/Icon.png                  144x144

prepare_team.py runs this as its `sprites` step with the team's team.json. By hand:
    uv run scripts/support_team/make_team_sprites.py --team <team> \\
        --whole Support_Whole_GuildWar_<Team>.png --heads <x,y,size> ... --lead <n> --front-outline <x,y> ... \\
        --logo Img_Guild_Faction_Logo_<n>.png --icon Skill_<Team>_Active.png \\
        --weapon-render weapon_render.png [--item <name>=<art.png> ...]
"""

import argparse
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "doll"))
import make_doll_sprites as doll  # noqa: E402

# The bark's face: head box width, centre x and top, the middle of make_doll_sprites.py's ranges
# for a Doll's, so the leader's face fills the roster slot as a Doll's does.
BARK_HEAD = tuple(sum(r) / 2 for r in (doll.BARK_HEAD_SIZE, doll.BARK_HEAD_CX, doll.BARK_HEAD_TOP))
# Behind the bark's face, as a Doll's bark has her art's own backdrop: a dark olive fall-off.
BACKDROP_TOP = (52, 58, 54)
BACKDROP_BOTTOM = (24, 28, 27)
# The whole bark, backdrop included, is drawn at this alpha rather than fully opaque.
BARK_ALPHA = 251

# The stand image: the frames that show it crop it (vanilla's UXML, scale-and-crop). The unit
# window's LeaderMask shows x 562 to 1594 and y 0 to 1922 of it, and hiring's LeaderBackground
# x 393 to 1798 and y 336 to 1904. The crew fill a box inside both, as wide as the unit window's
# crop, the art's lowest point this far above its foot, and the rest of the image is clear.
STAND_BOX = (562, 390, 1032, 1440)
STAND_BOX_FOOT_MARGIN = 10
# A Doll's stand is one figure, so the team's puts the leader in front, her head this far
# below the box's top and the art this much larger than it fits the box. She is cut from the art
# along --front-outline, a polygon round her and what she holds, its edge feathered by this many
# source pixels. Behind her is the rest of the art as one scene, this much smaller, centred on the
# others with their heads this far below the box's top on average, and dimmed to this gain. Her
# cut is taken out of it grown by this many source pixels, so none of her edge is left in it.
STAND_FRONT_HEAD_TOP = 140
STAND_FRONT_ZOOM = 1.05
STAND_FRONT_FEATHER = 6
STAND_BACK_SCALE = 0.70
STAND_BACK_HEAD_TOP = 10
STAND_BACK_GAIN = 0.72
STAND_BACK_HOLE = 30

# The faction logo is drawn white with a black outline, the skill icon over its middle at this
# share of its width.
LOGO_COLOUR = (255, 255, 255)
LOGO_OUTLINE = 6
ICON_SHARE = 0.62
ICON_HALO = 10
# The halo's colour (BGR): near black, a shade darker than the bark's backdrop, so the icon's edge
# reads on the white logo without a hard black ring.
ICON_HALO_COLOUR = (14, 18, 16)

# badge_dragged finds the bark inside the stand image and cuts its portrait around it at a Doll's
# proportions, so it is given the leader alone, framed as make_doll_sprites.py frames a Doll.
# Stand: figure top, figure height and head centre x, each inside that script's
# STAND_FIGURE_TOP, STAND_FIGURE_HEIGHT (the shortest class) and STAND_HEAD_CX. Bark: head box
# size, centre x and top, inside its BARK_HEAD_SIZE, BARK_HEAD_CX and BARK_HEAD_TOP.
DRAGGED_STAND = (430, 3100, 1070)
DRAGGED_BARK = (500, 236, -20)

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

# A skill's icon on the skill bar, drawn as the mod's other skill icons are: the art's light in
# cream on a dark brown plate inside a one-pixel cream border, the art inside this margin. The
# disabled icon is the same in greys. Colours are BGR, as OpenCV writes them.
SKILL_ICON_SIZE = (64, 64)
SKILL_ICON_MARGIN = 4
SKILL_PLATE = (9, 34, 68)
SKILL_LINE = (126, 213, 253)
SKILL_PLATE_DISABLED = (43, 43, 43)
SKILL_LINE_DISABLED = (219, 219, 219)
SKILL_DARK = 50
SKILL_LIGHT = 120

# An accessory's icon, the size of vanilla's (accessory_21_186x186), the art filling this share.
ITEM_ICON_SIZE = (186, 186)
ITEM_ICON_SHARE = 0.92


def backdrop(w, h):
    t = np.linspace(0, 1, h, dtype=np.float32)[:, None, None]
    top = np.array(BACKDROP_TOP, np.float32)
    bottom = np.array(BACKDROP_BOTTOM, np.float32)
    rgb = np.broadcast_to(top + (bottom - top) * t, (h, w, 3))
    return np.dstack([rgb, np.full((h, w), 255, np.float32)]).astype(np.uint8)


def bark(whole, lead):
    """The leader's face, cut as a Doll's bark is."""
    w, h = doll.BARK_SIZE
    out = doll.over(backdrop(w, h), slot(whole, lead), w / 2, h / 2)
    out[..., 3] = BARK_ALPHA
    return out


def slot(whole, lead):
    """The leader's face on nothing, so the roster slot shows the faction disc behind her."""
    return doll.place(whole, lead, doll.BARK_SIZE, *BARK_HEAD)


def stand(whole, heads, lead, outline):
    """The leader in front and the rest of the crew behind her as the art draws them, inside the
    part of the stand image the roster and recruitment frames show."""
    others = [head for i, head in enumerate(heads) if i != lead]
    x, y, w, h = STAND_BOX
    size = doll.STAND_SIZE
    cx = x + w / 2
    lowest = np.where(whole[..., 3] > doll.FIGURE_ALPHA)[0].max()
    # The leader is fitted by her own outline, the others by the crew's middle head over the whole
    # art, so the ones behind keep one size whoever leads.
    span = h - STAND_BOX_FOOT_MARGIN - STAND_FRONT_HEAD_TOP
    s_front = span / (max(py for _, py in outline) - heads[lead][1]) * STAND_FRONT_ZOOM
    s_back = span / (lowest - sorted(hy for _, hy, _ in heads)[len(heads) // 2]) * STAND_FRONT_ZOOM * STAND_BACK_SCALE
    shape = np.zeros(whole.shape[:2], np.uint8)
    cv2.fillPoly(shape, [np.array(outline, np.int32)], 255)
    grown = cv2.dilate(shape, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (STAND_BACK_HOLE * 2 + 1,) * 2))
    cut = cv2.GaussianBlur(shape.astype(np.float32) / 255, (0, 0), STAND_FRONT_FEATHER)
    hole = cv2.GaussianBlur(grown.astype(np.float32) / 255, (0, 0), STAND_FRONT_FEATHER)
    front_art, back_art = whole.copy(), whole.copy()
    front_art[..., 3] = (whole[..., 3] * cut).astype(np.uint8)
    back_art[..., 3] = (whole[..., 3] * (1 - hole)).astype(np.uint8)

    def view(art, head, s, head_cx, head_top):
        hx, hy, hs = head
        return premultiplied(doll.warp(art, s, head_cx - (hx + hs / 2) * s, y + head_top - hy * s, size))

    middle = (np.mean([hx + hs / 2 for hx, _, hs in others]), np.mean([hy for _, hy, _ in others]))
    back = view(back_art, (middle[0], middle[1], 0), s_back, cx, STAND_BACK_HEAD_TOP)
    back[..., :3] *= STAND_BACK_GAIN
    front = view(front_art, heads[lead], s_front, cx, STAND_FRONT_HEAD_TOP)
    return straight(front + back * (1 - front[..., 3:4] / 255))


def premultiplied(im):
    out = im.astype(np.float32)
    out[..., :3] *= out[..., 3:4] / 255
    return out


def straight(pre):
    """Back from premultiplied colour, so the transparent background leaves no dark fringe."""
    alpha = pre[..., 3:4]
    rgb = np.where(alpha > 0, pre[..., :3] / np.maximum(alpha / 255, 1e-6), 0)
    return np.clip(np.dstack([rgb, alpha]), 0, 255).astype(np.uint8)


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
    halo[..., :3] = ICON_HALO_COLOUR
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
    w, h = SKILL_ICON_SIZE
    # Cut to the art first: GFL2's icon sits off centre in its own square.
    ys, xs = np.where(icon[..., 3] > 12)
    icon = icon[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    fitted = doll.fit(icon, w - 2 * SKILL_ICON_MARGIN, h - 2 * SKILL_ICON_MARGIN)
    art = doll.over(np.zeros((h, w, 4), np.uint8), fitted, w / 2, h / 2).astype(np.float32)
    # The art's light, so its dark outlines fall to the plate and every fill comes up in full line
    # colour, as the other icons are flat: luminance between these two is ramped from one to the other.
    lum = doll.luminance(art)[..., None]
    light = np.clip((lum - SKILL_DARK) / (SKILL_LIGHT - SKILL_DARK), 0, 1) * art[..., 3:4] / 255
    border = np.zeros((h, w, 1), np.float32)
    border[0, :] = border[-1, :] = border[:, 0] = border[:, -1] = 1

    def drawn(plate, line):
        plate, line = np.array(plate, np.float32), np.array(line, np.float32)
        rgb = plate + (line - plate) * np.maximum(light, border)
        return np.dstack([np.clip(rgb, 0, 255), np.full((h, w), 255, np.float32)]).astype(np.uint8)

    return drawn(SKILL_PLATE, SKILL_LINE), drawn(SKILL_PLATE_DISABLED, SKILL_LINE_DISABLED)


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
    ap.add_argument("--front-outline", nargs="+", required=True, metavar="X,Y",
                    help="polygon round the leader and what she holds, in the whole art's pixels")
    ap.add_argument("--lead", type=int, required=True, help="the leader's place in --heads, from 0")
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

    lead = heads[args.lead]
    portrait = bark(whole, lead)
    roster = slot(whole, lead)
    outline = [tuple(int(v) for v in point.split(",")) for point in args.front_outline]
    standing = stand(whole, heads, args.lead, outline)
    write(sprites / "bark.png", portrait)
    write(sprites / "slot.png", roster)
    write(sprites / "slot_inactive.png", doll.slot_inactive(roster))
    write(textures / "stand_look_right.png", standing)
    write(textures / "stand_look_left.png", standing[:, ::-1])
    write(textures / "stand_look_right_inactive.png", doll.greyscale(standing))

    write(sprites / "unit_type.png", unit_type_icon(doll.load_rgba(args.icon)))
    badge = emblem(doll.load_rgba(args.logo), doll.load_rgba(args.icon))
    if args.emblem_out:
        cv2.imwrite(args.emblem_out, badge)
    figure = doll.place_figure(whole, lead, doll.STAND_SIZE, *DRAGGED_STAND)
    for name, im in doll.badges(badge, doll.place(whole, lead, doll.BARK_SIZE, *DRAGGED_BARK), figure).items():
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
