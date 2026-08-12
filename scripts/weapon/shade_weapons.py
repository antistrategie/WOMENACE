#!/usr/bin/env python3
"""Put every equipped weapon on the doll shader, and bake it.

A doll's glTF contains a weapon mesh and doll_shading shades it, but that is not
the weapon the game equips. The equipped one is the standalone weapon/<name>
prefab the template points at, baked by BakeWeapon from raw.glb, so without this
a cel-shaded doll holds a physically shaded gun.

BakeWeapon takes the binding as a materials.json manifest keyed by the glTF's own
material name, which this reads from raw.glb rather than guessing. Three things go
in it and each is easy to get wrong:

  base    the _d or _da the source ships.
  normal  the _n, or the neutral stand-in where a weapon has no ripped normal.
  mask    the GFL2 _rmo. DollToon reads that packing natively, R as roughness,
          G as metallic and B as occlusion, so it binds straight through.

The ramp is the one doll_shading gives the weapon part, so the standalone prefab
and the weapon mesh inside the doll model agree. It is the game's own weapon
ramp: the capture's binding map puts ResourceId 6550 on the weapon draws, and
ramp_weapon.png is that atlas reoriented.

The reference prefab donates a material for BakeWeapon to clone, and nothing
else. A manifest that names a shader and all four maps leaves none of it, so one
reference serves every weapon.

    python3 scripts/weapon/shade_weapons.py
    python3 scripts/weapon/shade_weapons.py --bake
    python3 scripts/weapon/shade_weapons.py --bake makiatto vector_ssr
"""

import argparse
import json
import struct
import subprocess
import sys
from pathlib import Path

WEAPONS = Path("unity/Assets/Authored/weapon")
SHADER = "Womenace/DollToon"
RAMP = "Assets/Authored/shared/ramps/ramp_weapon.png"
REFERENCE_PREFAB = ("Assets/Imported/arc_assault_rifle_t1/GameObject/"
                    "arc_assault_rifle_t1.prefab")
OUTPUT_DIR = "Assets/Prefabs"

# Suffix that identifies each map in a weapon's textures folder. First match
# wins, so _da is tried before _d and the ripped _n before a neutral stand-in.
MAP_SUFFIXES = {
    "base": ("_da", "_d"),
    "normal": ("_n", "_normal"),
    "mask": ("_rmo",),
}


def material_name(glb_path):
    """The glTF material name the manifest keys on, read from the GLB's JSON chunk."""
    data = glb_path.read_bytes()
    if data[:4] != b"glTF":
        raise SystemExit(f"{glb_path} is not a GLB")
    offset = 12
    while offset < len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        if kind == 0x4E4F534A:
            doc = json.loads(data[offset + 8: offset + 8 + length])
            break
        offset += 8 + length
    else:
        raise SystemExit(f"{glb_path} has no JSON chunk")
    names = [m.get("name") for m in doc.get("materials", []) if m.get("name")]
    if len(names) != 1:
        raise SystemExit(f"{glb_path} has {len(names)} materials, expected one: {names}")
    return names[0]


def textures(weapon_dir):
    """base, normal and mask paths, project-relative, by suffix."""
    folder = weapon_dir / "textures"
    files = sorted(p for p in folder.iterdir() if p.suffix in (".png", ".tga"))
    found = {}
    for key, suffixes in MAP_SUFFIXES.items():
        for suffix in suffixes:
            match = next((p for p in files if p.stem.endswith(suffix)), None)
            if match is not None:
                found[key] = str(match).split("unity/", 1)[-1]
                break
        else:
            raise SystemExit(f"{folder}: no {key} map, looked for "
                             + " or ".join(f"*{s}" for s in suffixes))
    return found


def manifest(weapon_dir):
    """Write the weapon's materials.json. Returns its path."""
    maps = textures(weapon_dir)
    document = {
        "materials": [{
            "name": material_name(weapon_dir / "raw.glb"),
            "shader": SHADER,
            "base": maps["base"],
            "normal": maps["normal"],
            "mask": maps["mask"],
            "extras": [{"property": "_RampMap", "path": RAMP}],
        }],
    }
    path = weapon_dir / "materials.json"
    path.write_text(json.dumps(document, indent=2) + "\n")
    return path


def unity_editor(project_root):
    version_file = project_root / "ProjectSettings" / "ProjectVersion.txt"
    version = None
    for line in version_file.read_text().splitlines():
        if line.startswith("m_EditorVersion:"):
            version = line.split(":", 1)[1].strip()
            break
    if version is None:
        raise SystemExit(f"could not read the editor version from {version_file}")
    editor = Path.home() / "Unity" / "Hub" / "Editor" / version / "Editor" / "Unity"
    if not editor.is_file():
        raise SystemExit(f"Unity {version} not found at {editor}")
    return editor, version


def bake(weapon_dir, manifest_path, editor):
    name = weapon_dir.name
    command = [
        str(editor), "-batchmode", "-nographics", "-quit",
        "-buildTarget", "StandaloneWindows64",
        "-projectPath", "unity",
        "-executeMethod", "Jiangyu.Mod.BakeWeapon.BakeBatch",
        "-gltfPath", f"Assets/Authored/weapon/{name}/raw.glb",
        "-referencePrefab", REFERENCE_PREFAB,
        "-outputDir", OUTPUT_DIR,
        "-outputName", f"weapon/{name}",
        "-materialManifest", str(manifest_path).split("unity/", 1)[-1],
    ]
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"bake failed for {name} ({result.returncode}). "
                         f"Unity's own log is the place to look, not this output.")


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("weapons", nargs="*",
                        help="folder names under Authored/weapon (default: all)")
    parser.add_argument("--bake", action="store_true",
                        help="run BakeWeapon too, instead of only writing manifests")
    args = parser.parse_args()

    names = args.weapons or sorted(p.name for p in WEAPONS.iterdir()
                                   if (p / "raw.glb").is_file())
    editor = None
    if args.bake:
        editor, version = unity_editor(Path("unity"))
        print(f"baking with Unity {version}", file=sys.stderr)

    for name in names:
        weapon_dir = WEAPONS / name
        path = manifest(weapon_dir)
        print(f"  {name}: {path}", file=sys.stderr)
        if editor is not None:
            bake(weapon_dir, path, editor)
            print(f"  {name}: baked -> {OUTPUT_DIR}/weapon/{name}", file=sys.stderr)


if __name__ == "__main__":
    main()
