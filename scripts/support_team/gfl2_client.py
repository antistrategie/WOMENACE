#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10,<3.13"
# dependencies = ["UnityPy"]
# ///
"""Read the GFL2 client's packed asset archives: index them, cut one out, export textures.

Most of the client's archives are stored end to end inside other `.bundle` files in
`GF2_Exilium_Data/LocalCache/Data/AssetBundles_Windows`, each one lightly encrypted. An archive is
named here as `<pack>:<offset>`, the pack file it sits in and its byte offset there. Cut out on its
own, an archive is a plain UnityFS bundle that AssetStudio and UnityPy read normally.

Four subcommands:

    gfl2_client.py index <bundles_dir> <index.jsonl>
        One line per archive: {"pack", "offset", "size", "names": {type: [object names]}}.
        Every archive in the client, about 61,000, so it takes a while. Search it for an object
        name to find the archive that holds it.

    gfl2_client.py cut <bundles_dir> <pack:offset> <out.bundle>
        Writes one archive, decrypted, as a standalone bundle.

    gfl2_client.py texture <bundles_dir> <pack:offset> <name> <out.png>
        Exports one named texture from one archive.

    gfl2_client.py textures <bundles_dir> <index.jsonl> <regex> <out_dir>
        Exports every Texture2D whose name matches, as PNG, finding them through the index.

UnityPy needs Python 3.12 or older here (its lz4 dependency does not build on newer ones), which
the script header pins for `uv run`.
"""
import argparse
import json
import os
import re
import struct
import sys
from concurrent.futures import ProcessPoolExecutor, as_completed
from pathlib import Path

import UnityPy

UnityPy.config.FALLBACK_UNITY_VERSION = "2019.4.29f1"
# Every archive starts with this header. The client XORs the first 32 KiB of each with a 16 byte
# key, so the key is the first 16 bytes read against it.
SIGNATURE = b"UnityFS\0\0\0\0\x075.x."
SPAN = 32768
INDEXED = {"GameObject", "Mesh", "AnimationClip", "Avatar", "AnimatorController", "AnimatorOverrideController",
           "TextAsset", "Texture2D", "Material", "Sprite", "AudioClip", "MonoScript"}


def decrypt(data):
    if data.startswith(b"UnityFS\0"):
        return data
    key = bytes(a ^ b for a, b in zip(data[:16], SIGNATURE))
    n = min(len(data), SPAN)
    return bytes(data[i] ^ key[i % 16] for i in range(n)) + data[n:]


def archives(path):
    """Every archive in one pack file, as (offset, decrypted bytes)."""
    size = path.stat().st_size
    offset = 0
    with path.open("rb") as s:
        while offset + 64 < size:
            s.seek(offset)
            header = decrypt(s.read(256))
            if not header.startswith(b"UnityFS\0"):
                return
            # Past the signature and two version strings sits the archive's total length.
            p = 12
            for _ in range(2):
                p = header.index(b"\0", p) + 1
            length = struct.unpack_from(">q", header, p)[0]
            if not 0 < length <= size - offset:
                return
            s.seek(offset)
            yield offset, decrypt(s.read(length))
            offset += length


def archive(bundles, name):
    """One archive's bytes, by `<pack>:<offset>`."""
    pack, offset = name.rsplit(":", 1)
    for at, data in archives(Path(bundles) / pack):
        if at == int(offset):
            return data
    raise SystemExit(f"no archive at {name}")


def scan(path):
    out = []
    try:
        for offset, data in archives(path):
            rec = {"pack": path.name, "offset": offset, "size": len(data)}
            try:
                names = {}
                for obj in UnityPy.load(data).objects:
                    kind = obj.type.name
                    if kind not in INDEXED:
                        continue
                    try:
                        name = obj.peek_name()
                    except Exception:
                        try:
                            name = obj.read().m_Name
                        except Exception:
                            continue
                    if name:
                        names.setdefault(kind, set()).add(name)
                rec["names"] = {kind: sorted(v) for kind, v in names.items()}
            except Exception as e:
                rec["error"] = str(e)[:200]
            out.append(rec)
    except Exception as e:
        out.append({"pack": path.name, "error": str(e)[:200]})
    return out


def index(bundles, dest):
    files = sorted(p for p in Path(bundles).iterdir() if p.suffix == ".bundle")
    with ProcessPoolExecutor(max(2, (os.cpu_count() or 4) - 2)) as pool, Path(dest).open("w", encoding="utf-8") as f:
        futures = [pool.submit(scan, p) for p in files]
        for i, future in enumerate(as_completed(futures), 1):
            for rec in future.result():
                f.write(json.dumps(rec) + "\n")
            if i % 1000 == 0 or i == len(files):
                print(f"{i}/{len(files)}", file=sys.stderr, flush=True)


def cut(bundles, name, out):
    out = Path(out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(archive(bundles, name))
    print(f"{name} -> {out}")


def textures(bundles, index_path, pattern, out_dir):
    pattern = re.compile(pattern, re.I)
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    wanted = {}
    for line in open(index_path, encoding="utf-8"):
        rec = json.loads(line)
        names = [n for n in rec.get("names", {}).get("Texture2D", []) if pattern.search(n)]
        if names:
            wanted.setdefault(rec["pack"], {})[rec["offset"]] = set(names)
    done = set()
    for pack, offsets in wanted.items():
        for offset, data in archives(Path(bundles) / pack):
            if offset not in offsets:
                continue
            for obj in UnityPy.load(data).objects:
                if obj.type.name != "Texture2D":
                    continue
                tex = obj.read()
                if tex.m_Name in offsets[offset] and tex.m_Name not in done:
                    try:
                        tex.image.save(out_dir / f"{tex.m_Name}.png")
                        done.add(tex.m_Name)
                    except Exception as e:
                        print(f"failed {tex.m_Name}: {e}", file=sys.stderr)
    print(f"{len(done)} texture(s) -> {out_dir}")
    return done


def texture(bundles, name, texture_name, out):
    """One named texture from one archive, as PNG: how a team's art is exported without the index."""
    for obj in UnityPy.load(archive(bundles, name)).objects:
        if obj.type.name == "Texture2D" and obj.peek_name() == texture_name:
            out = Path(out)
            out.parent.mkdir(parents=True, exist_ok=True)
            obj.read().image.save(out)
            print(f"{texture_name} -> {out}")
            return
    raise SystemExit(f"no texture {texture_name} in {name}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="command", required=True)
    p = sub.add_parser("index")
    p.add_argument("bundles")
    p.add_argument("index")
    p = sub.add_parser("cut")
    p.add_argument("bundles")
    p.add_argument("archive", help="<pack>:<offset>")
    p.add_argument("out")
    p = sub.add_parser("texture")
    p.add_argument("bundles")
    p.add_argument("archive", help="<pack>:<offset>")
    p.add_argument("name")
    p.add_argument("out")
    p = sub.add_parser("textures")
    p.add_argument("bundles")
    p.add_argument("index")
    p.add_argument("pattern")
    p.add_argument("out_dir")
    args = ap.parse_args()
    if args.command == "index":
        index(args.bundles, args.index)
    elif args.command == "cut":
        cut(args.bundles, args.archive, args.out)
    elif args.command == "texture":
        texture(args.bundles, args.archive, args.name, args.out)
    else:
        textures(args.bundles, args.index, args.pattern, args.out_dir)


if __name__ == "__main__":
    main()
