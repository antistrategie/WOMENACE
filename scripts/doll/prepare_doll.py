#!/usr/bin/env python3
"""Prepare a doll's glTF for baking, and bake it. One command per doll.

Everything between "the model is exported from Blender" and "the prefab exists" in
the right order:

  1. Reconstruct the face SDF lookup coordinates into TEXCOORD_2.
  2. Append the outline submesh.
  3. Resolve every material to a shader, ramp, texture and flag.
  4. Invoke BakeHumanoid with all of that.

Order matters, which is the reason this exists rather than four commands. Steps 1
and 2 mutate the glTF and step 3 reads it, so running 3 first silently bakes a doll
with no outline material and no SDF coordinates. Both mutating steps are idempotent
and recompute from the mesh, so re-running this is safe.

What it will not do is touch skin weights. Nothing here reads or writes JOINTS or
WEIGHTS, and nothing here regenerates the mesh. The only script that does is
pmx_to_menace.py, which rebuilds the glTF from the PMX and discards anything added
afterwards, hand-painted weights included. So the order across the whole pipeline is
Blender first, this second, and never the reverse. The staleness check below exists
to catch exactly that mistake.

    python3 scripts/doll/prepare_doll.py unity/Assets/Authored/makiatto/default
    python3 scripts/doll/prepare_doll.py <doll dir> --bake
"""

import argparse
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import add_outline_submesh                                           # noqa: E402
import bake_face_sdf_uv                                              # noqa: E402
import doll_shading                                                  # noqa: E402
import transfer_hair_uv                                              # noqa: E402

REFERENCE_PREFAB = ("Assets/Imported/rmc_default_female_soldier_2/GameObject/"
                    "rmc_default_female_soldier_2.prefab")
OUTPUT_DIR = "Assets/Prefabs"


def unity_editor(project_root):
    """The editor matching the project, so a bake cannot run on the wrong version."""
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


def check_not_stale(doll):
    """Warn if Blender has unexported edits, which this would otherwise bake over."""
    gltf = doll / "model.gltf"
    newer = [b for b in sorted(doll.glob("*.blend"))
             if b.stat().st_mtime > gltf.stat().st_mtime]
    if not newer:
        return
    names = ", ".join(b.name for b in newer)
    print(f"  WARNING: {names} is newer than model.gltf. If that .blend holds edits "
          f"you have not exported, this will prepare and bake the older mesh.",
          file=sys.stderr)


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("doll", help="e.g. unity/Assets/Authored/makiatto/default")
    parser.add_argument("--bake", action="store_true",
                        help="run BakeHumanoid too, instead of printing its invocation")
    parser.add_argument("--output-name", default=None,
                        help="prefab path under Assets/Prefabs (default: <doll>/<outfit>)")
    args = parser.parse_args()

    doll = Path(args.doll)
    if not (doll / "model.gltf").is_file():
        raise SystemExit(f"no model.gltf in {doll}")
    # The project root is the folder holding Assets/, since the bake takes
    # project-relative paths and Unity takes a project path.
    project_root = next((p for p in doll.resolve().parents if (p / "Assets").is_dir()), None)
    if project_root is None:
        raise SystemExit(f"{doll} is not inside a Unity project")
    gltf_folder = str(doll.resolve().relative_to(project_root))
    output_name = args.output_name or "/".join(doll.parts[-2:])

    print(f"preparing {doll}", file=sys.stderr)
    check_not_stale(doll)
    bake_face_sdf_uv.bake(doll)
    # The hair strip UV, for characters whose game hair mesh has been dumped.
    # Without the reference the hair keeps the disabled specular path.
    if transfer_hair_uv.reference_for(doll).is_file():
        transfer_hair_uv.bake(doll)
    add_outline_submesh.add(doll)
    shaders, textures, floats, skipped, inverted = doll_shading.resolve(doll)
    for part, names in sorted(skipped.items()):
        print(f"  no ramp for part '{part}', keeping the game shader: {', '.join(names)}",
              file=sys.stderr)
    if inverted:
        print(f"  WIRED FROM _smo, SET _MaskRoughnessInverted=1 ON THESE OR THEIR "
              f"ROUGHNESS READS BACKWARDS: {', '.join(sorted(set(inverted)))}", file=sys.stderr)
    print(f"  materials: {len(shaders)} shaded", file=sys.stderr)

    editor, version = unity_editor(project_root)
    command = [
        str(editor), "-batchmode", "-nographics", "-quit",
        "-buildTarget", "StandaloneWindows64",
        "-projectPath", str(project_root),
        "-executeMethod", "Jiangyu.Mod.BakeHumanoid.BakeBatch",
        "-gltfFolder", gltf_folder,
        "-referencePrefab", REFERENCE_PREFAB,
        "-outputDir", OUTPUT_DIR,
        "-outputName", output_name,
        "-overrideShaderFor", ",".join(shaders),
        "-setTextureFor", ",".join(textures),
        "-setFloatFor", ",".join(floats),
    ]

    if not args.bake:
        print(f"\nprepared. bake with --bake, or run this (Unity {version}):\n", file=sys.stderr)
        print(" ".join(f"'{c}'" if " " in c or "," in c else c for c in command))
        return

    print(f"  baking with Unity {version} -> {OUTPUT_DIR}/{output_name}", file=sys.stderr)
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"bake failed ({result.returncode}). "
                         f"Unity's own log is the place to look, not this output.")
    print("  baked. run `mise run compile && mise run deploy` next.", file=sys.stderr)


if __name__ == "__main__":
    main()
