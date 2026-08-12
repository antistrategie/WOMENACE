#!/usr/bin/env python3
"""Pull a character's ramp atlases and hair UV reference out of the GFL2 client.

Two things a doll needs that no PMX carries, both sitting in the client's own
bundles:

  ramps       the character's hair, cloth and silkstock gradients, shipped as
              RampAtlasRGBA MonoBehaviours. Written to Authored/<doll>/ramps/,
              with the raw gradient dump kept in data/<doll>_ramp_gradients.json
              so a re-bake needs no second trip to the client.
  hair UV     the game hair mesh's strip layout, which the specular streak reads.
              Written to Authored/<doll>/hair_uv1_ref.npz, from where
              transfer_hair_uv.py transfers it onto the doll's own hair.

Reading the bundles is tdollhouse's ShaderDump, which needs ASSET_STUDIO_DIR set
to that project's extract/AS. The bundle a given asset lives in comes from the
asset map under ~/gfl2-extract/maps/.

Two clients hold different content and a character missing from one may be in
the other, so --client picks: Lenna ships in the global client and not in CN.

    python3 scripts/doll/extract_character_refs.py lenna Lenna --client global
    python3 scripts/doll/extract_character_refs.py cheyanne Cheyanne

The character's internal name is not always its display name (Makiatto is
Macqiato, Helen is Hailunna in places), and one character can be inconsistent
with itself: Lenna's face texture is c_Lanna_face_d where everything else is
Lenna. Grep the asset map when a run finds nothing.
"""

import argparse
import json
import subprocess
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
from bake_ramp_png import bake, write_meta, write_png                 # noqa: E402

TDOLLHOUSE = Path.home() / "dev/github.com/antistrategie/tdollhouse"
SHADERDUMP = TDOLLHOUSE / "tools/shaderdump/bin/ShaderDump.dll"
MAPS = Path.home() / "gfl2-extract/maps"

CLIENTS = {
    "cn": (Path.home() / "Games/GF2-CN/prefix/drive_c/GF2/GF2 Game/GF2_Exilium_Data"
           / "LocalCache/Data/AssetBundles_Windows",
           MAPS / "gfl2_cn_assets.json"),
    "global": (Path.home() / ".steam/steam/steamapps/common/GIRLS' FRONTLINE 2 EXILIUM"
               / "GF2_Exilium_Data/LocalCache/Data/AssetBundles_Windows",
               MAPS / "gfl2_assets.json"),
}

# Ramp part to the filename doll_shading resolves. Ordered most specific first:
# a silkstock gradient is often named for the cloth slot it belongs to, as in
# c_SextansSSR01_slg_cloth2_silkstock_ramp, and matching cloth first would file
# the stockings' atlas as the costume's.
RAMP_NAMES = [
    ("silkstock", "ramp_silkstock.png"),
    ("hair", "ramp_hair.png"),
    ("cloth", "ramp_cloth_main.png"),
    # Some characters name the costume's main gradient for the body it covers
    # rather than for the cloth, and ship nothing named cloth at all.
    ("body", "ramp_cloth_main.png"),
]


def shaderdump(out_dir, kind, needle, bundles, bundle_root):
    command = ["dotnet", str(SHADERDUMP), "GirlsFrontline", str(out_dir), kind, needle]
    command += [str(bundle_root / b) for b in bundles]
    result = subprocess.run(command, capture_output=True, text=True)
    return result.returncode == 0


def bundles_holding(asset_map, kind, predicate):
    """The bundles carrying assets of a type whose name the predicate accepts."""
    return sorted({entry["Source"].split("\\")[-1] for entry in asset_map
                   if entry.get("Type") == kind and predicate(str(entry.get("Name", "")))})


def base_costume_first(name, internal):
    """0 for the character's base costume, 1 for an alternate.

    SSR01 is the default and SSR0101 upward are alternates, and the substring
    test distinguishes them because SSR01's underscore cannot match inside
    SSR0101.
    """
    lowered = name.lower()
    return 0 if ("ssr01_" in lowered or f"c_{internal.lower()}_" in lowered) else 1


def extract_ramps(doll_dir, internal, asset_map, bundle_root, scratch):
    low = internal.lower()
    bundles = bundles_holding(asset_map, "Texture2D", lambda n: low in n.lower() and any(
        part in n.lower() for part in ("cloth", "hair", "silkstock")))
    if not bundles:
        print(f"  ramps: no texture bundle carries '{internal}'", file=sys.stderr)
        return
    dump = scratch / f"{doll_dir.name}-ramps"
    if not shaderdump(dump, "MonoBehaviour", "ramp", bundles, bundle_root):
        print(f"  ramps: dump failed over {len(bundles)} bundle(s)", file=sys.stderr)
        return
    source = dump / "monobehaviours.json"
    entries = json.loads(source.read_text()) if source.is_file() else []
    if not entries:
        print(f"  ramps: none in {len(bundles)} bundle(s)", file=sys.stderr)
        return

    (Path(__file__).parent / "data" / f"{doll_dir.name}_ramp_gradients.json"
     ).write_text(source.read_text())

    # Ranked by how the asset is named first and by costume second, so a
    # character shipping both a cloth and a body gradient takes the cloth one
    # whichever sorts first, and takes the base costume's of the two.
    # Ranked by the word matched, then by costume, then by whether the word is
    # the last thing in the name. That last test separates a costume's main
    # gradient from its accents: Sextans ships cloth_ramp beside
    # cloth3_Baoshi_ramp, the gemstones', and the unqualified one is the cloth.
    chosen = {}
    for name in sorted({e["Name"] for e in entries}):
        lowered = name.lower()
        word = next((i for i, (w, _) in enumerate(RAMP_NAMES) if w in lowered), None)
        if word is None:
            continue
        filename = RAMP_NAMES[word][1]
        rank = (word,
                base_costume_first(name, internal),
                0 if lowered.endswith(RAMP_NAMES[word][0] + "_ramp") else 1,
                len(name))
        if filename not in chosen or rank < chosen[filename][0]:
            chosen[filename] = (rank, name)

    ramps = doll_dir / "ramps"
    ramps.mkdir(exist_ok=True)
    for filename, (_, name) in sorted(chosen.items()):
        entry = next(e for e in entries if e["Name"] == name)
        out = ramps / filename
        write_png(bake(entry), out)
        write_meta(out)
        print(f"  ramp: {name} -> {filename}", file=sys.stderr)


def extract_hair_uv(doll_dir, internal, asset_map, bundle_root, scratch):
    low = internal.lower()

    def is_hair_mesh(name):
        lowered = name.lower()
        return low in lowered and "hair" in lowered and lowered.endswith("_lod0")

    candidates = sorted(
        ((str(e["Name"]), e["Source"].split("\\")[-1]) for e in asset_map
         if e.get("Type") == "Mesh" and is_hair_mesh(str(e.get("Name", "")))),
        key=lambda c: base_costume_first(c[0], internal))
    if not candidates:
        print(f"  hair: no game hair mesh for '{internal}'", file=sys.stderr)
        return
    name, bundle = candidates[0]

    dump = scratch / f"{doll_dir.name}-hair"
    if not shaderdump(dump, "Mesh", name.lower(), [bundle], bundle_root):
        print(f"  hair: dump failed for {name}", file=sys.stderr)
        return
    meshes = json.loads((dump / "meshs.json").read_text())
    mesh = next((m for m in meshes if m["Name"] == name), None)
    if not mesh or not mesh.get("m_Vertices") or not mesh.get("m_UV1"):
        print(f"  hair: {name} carries no UV1", file=sys.stderr)
        return
    reference = doll_dir / "hair_uv1_ref.npz"
    np.savez_compressed(
        reference,
        pos=np.array(mesh["m_Vertices"], dtype=np.float32).reshape(-1, 3),
        uv=np.array(mesh["m_UV1"], dtype=np.float32).reshape(-1, 2))
    print(f"  hair: {name}, {len(mesh['m_Vertices']) // 3} vertices "
          f"-> {reference.name}", file=sys.stderr)


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("doll", help="the Authored/ folder name, e.g. cheyanne")
    parser.add_argument("internal", help="the character's name in the client, e.g. Cheyanne")
    parser.add_argument("--client", choices=sorted(CLIENTS), default="cn")
    parser.add_argument("--scratch", type=Path, default=Path("/tmp/gfl2-refs"),
                        help="where the raw dumps land")
    args = parser.parse_args()

    if not SHADERDUMP.is_file():
        raise SystemExit(f"ShaderDump not built at {SHADERDUMP}. It needs "
                         f"ASSET_STUDIO_DIR set to {TDOLLHOUSE}/extract/AS, and a "
                         f"build without it leaves the previous binary in place.")
    bundle_root, asset_map_path = CLIENTS[args.client]
    if not bundle_root.is_dir():
        raise SystemExit(f"{args.client} client bundles not at {bundle_root}")

    doll_dir = Path("unity/Assets/Authored") / args.doll
    if not doll_dir.is_dir():
        raise SystemExit(f"no character folder at {doll_dir}")
    args.scratch.mkdir(parents=True, exist_ok=True)

    print(f"{args.doll} ({args.internal}) from the {args.client} client", file=sys.stderr)
    asset_map = json.loads(asset_map_path.read_text())
    extract_ramps(doll_dir, args.internal, asset_map, bundle_root, args.scratch)
    extract_hair_uv(doll_dir, args.internal, asset_map, bundle_root, args.scratch)


if __name__ == "__main__":
    main()
