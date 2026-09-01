#!/usr/bin/env python3
"""Bake a ramp atlas PNG from a dumped RampAtlasRGBA gradient asset.

GFL2 ships most characters' ramps as gradient assets, not textures: a
RampAtlasRGBA MonoBehaviour holding four Unity Gradients, baked to a 256x16
atlas at runtime. The gradients are the source of truth. tdollhouse's
shaderdump tool dumps them raw from a character's texture bundles:

    dotnet bin/ShaderDump.dll GirlsFrontline <out> MonoBehaviour ramp <bundle...>

This decodes that dump and writes the atlas the shaders bind: 256x16, four
bands of four rows, gradient 0 in the bottom quarter where the shader's main
diffuse band (V=0.125) reads, gradient 3 at the top. Colour keys are linear
floats with 16-bit fixed-point times (divide by 65535), key count limited by
m_NumColorKeys, piecewise-linear between keys, sampled at texel centres.
Bytes are the linear values directly: the atlas must import linear (the
sibling metas carry the settings, new files copy them with a fresh guid).

Validated against the frozen baseline: baking Makiatto's cloth gradient
reproduces the shipped ramp_cloth_main.png artist band within 1/255.

    python3 scripts/doll/bake_ramp_png.py <monobehaviours.json> <asset name> <out.png>
"""

import json
import re
import struct
import sys
import uuid
import zlib
from pathlib import Path


def decode_gradient(ramp):
    """A raw dumped Unity Gradient -> sorted [(t, (r, g, b)), ...]."""
    n = int(ramp.get("m_NumColorKeys", 8))
    keys = []
    for i in range(n):
        c = ramp[f"key{i}"]
        keys.append((ramp[f"ctime{i}"] / 65535.0, (c["r"], c["g"], c["b"])))
    keys.sort(key=lambda k: k[0])
    return keys


def sample(keys, t):
    if t <= keys[0][0]:
        return keys[0][1]
    for (t0, c0), (t1, c1) in zip(keys, keys[1:]):
        if t <= t1:
            f = (t - t0) / (t1 - t0) if t1 > t0 else 0.0
            return tuple(a + (b - a) * f for a, b in zip(c0, c1))
    return keys[-1][1]


def bake(entry):
    """One dumped asset -> 16 rows of 256 linear RGB byte triples."""
    bands = []
    for ramp in entry["fields"]["ramps"]:
        keys = decode_gradient(ramp)
        bands.append([sample(keys, (x + 0.5) / 256.0) for x in range(256)])
    rows = []
    # Gradient 0 fills the bottom quarter of the image, which is where the
    # shader's main-diffuse band lands after Unity's V flip.
    for band in reversed(bands):
        row = bytes(min(255, max(0, round(v * 255.0))) for c in band for v in c)
        rows.extend([row] * 4)
    return rows


def write_png(rows, path):
    raw = b"".join(b"\x00" + r for r in rows)

    def chunk(tag, data):
        c = tag + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c))

    path.write_bytes(
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", 256, 16, 8, 2, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b""))


def write_meta(png_path):
    """Clone a sibling ramp's Unity import settings under a fresh guid.

    The settings are what make the texture readable as data: uncompressed,
    linear, no mips. A ramp dropped in without them imports sRGB-compressed
    and every threshold the shader reads is quantised.
    """
    meta_path = Path(str(png_path) + ".meta")
    if meta_path.exists():
        return
    # Any already-imported ramp will do, so search outward rather than at a fixed
    # depth: a ramps/ directory sits beside a character or beside a single outfit,
    # and the shared dump sits at the Authored root above both.
    candidates = [png_path.parent]
    for parent in png_path.parents:
        candidates.append(parent / "ramps")
        candidates.append(parent / "shared" / "ramps")
    template = None
    for directory in candidates:
        for sibling in sorted(directory.glob("ramp_*.png.meta")) if directory.is_dir() else []:
            template = sibling.read_text()
            break
        if template is not None:
            break
    if template is None:
        raise SystemExit(f"no sibling ramp meta to copy import settings from for {png_path}")
    meta_path.write_text(re.sub(r"guid: [0-9a-f]{32}", "guid: " + uuid.uuid4().hex, template))


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    dump, name, out = sys.argv[1], sys.argv[2], Path(sys.argv[3])
    entries = json.loads(Path(dump).read_text())
    entry = next((e for e in entries if e.get("Name") == name), None)
    if entry is None:
        known = ", ".join(e.get("Name", "?") for e in entries)
        raise SystemExit(f"no asset '{name}' in {dump} (has: {known})")
    write_png(bake(entry), out)
    write_meta(out)
    print(f"  ramp: {name} -> {out}", file=sys.stderr)


if __name__ == "__main__":
    main()
