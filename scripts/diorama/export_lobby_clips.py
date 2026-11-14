#!/usr/bin/env python3
"""Export each doll's GFL2 lobby animations as an FBX donor for the strategy diorama.

GFL2 binds clips in code, so VoyExport bakes them against the doll's own rig
(`c_<Name>SSR01_slg_skin`, whose bundle also carries the meshes the humanoid
calibration reads its bindposes from). Clip bone paths differ per doll, so every
doll is exported on her own rig. The FBX lands in
unity/Assets/Authored/diorama/_donor/<doll>.fbx (gitignored), where
Womenace.EditorTools.BuildDioramaPoses turns the lobby loops into muscle clips.

Usage: export_lobby_clips.py [doll ...]   (default: every doll in DOLLS)
Needs the CN asset map and the staged VoyExport build (see ~/gfl2-extract/README.md).
"""
import json
import re
import subprocess
import sys
from collections import Counter
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
EXTRACT = Path.home() / "gfl2-extract"
ASSET_MAP = EXTRACT / "maps/gfl2_cn_assets.json"
VOYEXPORT_DIR = EXTRACT / "bin/Release/win-x64"
BUNDLES = Path.home() / "Games/GF2-CN/prefix/drive_c/GF2/GF2 Game/GF2_Exilium_Data/LocalCache/Data/AssetBundles_Windows"
OUT = REPO / "unity/Assets/Authored/diorama/_donor"

# Body asset name (Transmog's first path segment) -> GFL2 internal name.
DOLLS = {
    "alva": "Alva", "asteria": "Asteria", "cheyanne": "Cheyanne", "helen": "Helen",
    "jiangyu": "Jiangyu", "klukai": "Clukay", "koleda": "Koleda", "lenna": "Lenna",
    "leva": "Leva", "lewis": "Lewis", "makiatto": "Macqiato", "nemesis": "NemesisGnosis",
    "papasha": "Papasha", "robella": "Robella", "sextans": "Sextans", "soppo": "Soppo",
    "springfield": "Springfield", "suomi": "Suomi", "vector": "Vector",
    "voymastina": "Voymastina", "welrod": "Welrod",
}
# The loops the diorama plays.
TAKES = ("Idle_0_Loop", "LongIdle_0_Loop", "Sit_0_Loop")


def bundle(asset):
    return asset["Source"].replace("\\", "/").rsplit("/", 1)[-1]


def plan(assets, name):
    rig = None
    for grade in ("SSR01", "SR01"):
        hits = [a for a in assets if a["Type"] == "Animator" and a["Name"] == f"c_{name}{grade}_slg_skin"]
        if hits:
            rig, prefix = bundle(hits[0]), f"Lobby_{name}{grade}_"
            break
    if rig is None:
        return None
    wanted = {prefix + t for t in TAKES}
    clips = Counter(bundle(a) for a in assets if a["Type"] == "AnimationClip" and a["Name"] in wanted)
    if not clips:
        return None
    # One clip bundle: overlapping bundles give duplicate takes. Take the one holding the most loops.
    return rig, clips.most_common(1)[0][0], prefix


def main():
    dolls = sys.argv[1:] or list(DOLLS)
    assets = json.loads(ASSET_MAP.read_text())
    OUT.mkdir(parents=True, exist_ok=True)
    for doll in dolls:
        name = DOLLS[doll]
        p = plan(assets, name)
        if p is None:
            print(f"{doll}: no rig or lobby clips for {name}, skipped")
            continue
        rig, clips, prefix = p
        model = f"c_{prefix[len('Lobby_'):-1]}_slg_skin"
        target = OUT / f"{doll}.fbx"
        win = lambda path: "Z:" + str(path)
        cmd = ["wine", "VoyExport.exe", win(target), "GirlsFrontline", model, prefix,
               win(BUNDLES / rig), win(BUNDLES / clips)]
        result = subprocess.run(cmd, cwd=VOYEXPORT_DIR, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, stdin=subprocess.DEVNULL)
        summary = [l for l in result.stdout.splitlines() if re.match(r"(model|ModelConverter|Exported|ERROR)", l)]
        print(f"{doll}: " + " | ".join(summary))
        (OUT / f"{doll}.prefix").write_text(prefix)


if __name__ == "__main__":
    main()
