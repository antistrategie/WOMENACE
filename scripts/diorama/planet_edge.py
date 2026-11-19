#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["numpy", "pillow", "UnityPy"]
# ///
"""Fit the edge of a planet in MENACE's mission select video, for the strategy diorama.

Mission select shows the operation's planet as a part of a globe over the holotable
(code/Systems/Strategy/Missions/StrategyDiorama.Planet.cs). The picture is vanilla's video of the planet
from orbit, and each pixel is placed on a sphere from the circle the planet's edge is a part of.
That circle is the one thing recorded per planet: its centre and radius in pixels of a 1600 by 900
frame, in the PlanetEdges table. A planet without a line there keeps the land view.

Three steps, each a subcommand:

    list     the mission select clips in the game's data, with their sizes
    frame    writes one frame of a clip as a 1600 by 900 PNG (needs ffmpeg)
    fit      fits a circle to points picked by eye along the planet's edge in that frame,
             prints the line for PlanetEdges and writes the frame with the circle drawn on it

Pick the points in any image viewer that shows pixel coordinates: five or more, spread along as
much of the edge as the frame shows, on the planet's surface where it meets space and not on the
glow of its air. The circle's centre is usually far outside the frame. Look at the overlay: the
circle should lie on the edge for its whole length. A planet's day and night clips have shared
one framing so far. Check one frame of each against the same circle.

Usage:
    uv run scripts/diorama/planet_edge.py list --data <Menace_Data>
    uv run scripts/diorama/planet_edge.py frame --data <Menace_Data> --clip <name> --out frame.png
    uv run scripts/diorama/planet_edge.py fit frame.png 212,640 480,410 800,270 1150,190 1500,170
"""
import argparse
import subprocess
import sys
import tempfile
from pathlib import Path

FRAME = (1600, 900)


def clips(data: Path):
    """Each VideoClip in resources.assets: its name, size, length and where its bytes are."""
    import UnityPy

    for obj in UnityPy.load(str(data / "resources.assets")).objects:
        if obj.type.name != "VideoClip":
            continue
        tree = obj.read_typetree()
        source = tree["m_ExternalResources"]
        seconds = tree.get("m_FrameCount", 0) / max(1e-6, tree.get("m_FrameRate", 1))
        yield tree.get("m_Name", ""), tree.get("Width"), tree.get("Height"), seconds, source


def list_clips(args):
    for name, width, height, seconds, _ in sorted(clips(args.data)):
        print(f"{name}  {width}x{height}  {seconds:.1f}s")


def write_frame(args):
    for name, _, _, seconds, source in clips(args.data):
        if name != args.clip:
            continue
        with open(args.data / Path(source["m_Source"]).name, "rb") as file:
            file.seek(source["m_Offset"])
            video = file.read(source["m_Size"])
        with tempfile.NamedTemporaryFile(suffix=".mp4") as held:
            held.write(video)
            held.flush()
            at = seconds * args.at
            subprocess.run(
                ["ffmpeg", "-y", "-loglevel", "error", "-ss", f"{at:.2f}", "-i", held.name, "-frames:v", "1",
                 "-vf", f"scale={FRAME[0]}:{FRAME[1]}", str(args.out)],
                check=True,
            )
        print(f"wrote {args.out} ({name} at {at:.1f}s)")
        return
    sys.exit(f"no clip named {args.clip}: see the list subcommand")


def fit(args):
    import numpy as np
    from PIL import Image, ImageDraw

    points = np.array([[float(v) for v in point.split(",")] for point in args.points])
    if len(points) < 3:
        sys.exit("a circle needs three points, and five or more make a fit worth trusting")
    x, y = points[:, 0], points[:, 1]
    # The circle through the points by least squares: x^2 + y^2 = 2 cx x + 2 cy y + c.
    (cx, cy, c), *_ = np.linalg.lstsq(np.c_[2 * x, 2 * y, np.ones_like(x)], x * x + y * y, rcond=None)
    radius = float(np.sqrt(c + cx * cx + cy * cy))
    off = np.abs(np.hypot(x - cx, y - cy) - radius)
    print(f"centre ({cx:.0f}, {cy:.0f}) radius {radius:.0f}, the points lie {off.mean():.1f} px off it (worst {off.max():.1f})")
    print(f'        ["planet.<id>"] = new({cx:.0f}f, {cy:.0f}f, {radius:.0f}f),')

    image = Image.open(args.frame).convert("RGB")
    if image.size != FRAME:
        sys.exit(f"{args.frame} is {image.size[0]}x{image.size[1]}: the fit is in pixels of a {FRAME[0]}x{FRAME[1]} frame")
    draw = ImageDraw.Draw(image)
    draw.ellipse([cx - radius, cy - radius, cx + radius, cy + radius], outline=(255, 60, 60), width=2)
    for px, py in points:
        draw.ellipse([px - 5, py - 5, px + 5, py + 5], outline=(255, 230, 60), width=2)
    out = args.frame.with_name(args.frame.stem + "_edge.png")
    image.save(out)
    print(f"wrote {out}")


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    commands = parser.add_subparsers(dest="command", required=True)
    listing = commands.add_parser("list", help="the video clips in the game's data")
    listing.add_argument("--data", type=Path, required=True, help="the game's Menace_Data folder")
    listing.set_defaults(run=list_clips)
    frame = commands.add_parser("frame", help="one frame of a clip as a 1600 by 900 PNG")
    frame.add_argument("--data", type=Path, required=True, help="the game's Menace_Data folder")
    frame.add_argument("--clip", required=True, help="the clip's name, as list prints it")
    frame.add_argument("--out", type=Path, required=True)
    frame.add_argument("--at", type=float, default=0.5, help="how far through the clip, 0 to 1")
    frame.set_defaults(run=write_frame)
    fitting = commands.add_parser("fit", help="the circle through points picked along the planet's edge")
    fitting.add_argument("frame", type=Path)
    fitting.add_argument("points", nargs="+", help="x,y in pixels of the frame")
    fitting.set_defaults(run=fit)
    args = parser.parse_args()
    args.run(args)


if __name__ == "__main__":
    main()
