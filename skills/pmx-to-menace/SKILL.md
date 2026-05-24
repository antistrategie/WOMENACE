---
name: pmx-to-menace
description: Convert an MMD PMX character into an addition-prefab glTF for MENACE via Jiangyu. Use when adding a new character, troubleshooting an existing conversion, or asking about material / skinning / LOD behaviour in the PMX pipeline.
---

# PMX to MENACE conversion

## What this pipeline does

`scripts/pmx_to_menace.py` takes a PMX model (MMD format) plus a reference MENACE soldier glTF and avatar, and emits a single glTF carrying the PMX character's mesh and skeleton renamed to MENACE's humanoid bone convention, T-pose-calibrated against the reference, with attachment bones grafted in. Runs headless in Blender via `mmd_tools` for the PMX import.

The output glTF is the input to Jiangyu's Unity-side `BakeHumanoid` Editor utility, which bakes it into an addition soldier prefab (avatar + per-source-texture materials + LODGroup + animator). Jiangyu then compiles the prefab into an AssetBundle that MENACE loads at runtime.

## Running the full pipeline

```bash
# 1. Blender: PMX → glTF
blender --background --python scripts/pmx_to_menace.py -- --config scripts/.config/<character>.json

# 2. Unity: glTF → addition soldier prefab
/path/to/Unity -batchmode -nographics -quit -buildTarget StandaloneWindows64 \
  -projectPath unity \
  -executeMethod Jiangyu.Mod.BakeHumanoid.BakeBatch \
  -gltfFolder Assets/Authored/<character> \
  -referencePrefab Assets/Imported/rmc_default_female_soldier_2/GameObject/rmc_default_female_soldier_2.prefab \
  -outputDir Assets/Prefabs \
  -outputName <character>

# 3. Jiangyu: compile + deploy
mise run compile   # → compiled/<modname>.bundle + per-prefab addition bundles
mise run deploy    # → ~/.steam/.../Menace/Mods/<modname>/
```

The `.config/` directory is gitignored. Configs hold absolute PMX paths on local disk.

## Config file structure

One JSON per character in `scripts/.config/`. Fields:

- `pmx_path`. Absolute path to the source `.pmx`.
- `reference_prefab_path`. Absolute path to a vanilla MENACE soldier's exported `model.gltf` (under `exported/<soldier>/`). Used for armature shape and attachment-bone landmarks.
- `reference_avatar_path`. Absolute path to the matching reference soldier's `*Avatar.asset` (under `unity/Assets/Imported/<soldier>/Avatar/`). Used for T-pose muscle-zero calibration.
- `output_path`. Where Blender writes the authored `model.gltf` (typically `unity/Assets/Authored/<character>/model.gltf`).
- `source_mesh_names`. Names of the PMX mesh objects to transfer.
- `bone_map`. PMX bone to MENACE humanoid bone mapping. Drives both the bone rename and vertex-group remap.
- `ignore_bones`. PMX bones to drop entirely (typically MMD IK control bones).
- `target_height_metres`. Absolute character height in metres (e.g. `1.8` for a 180cm character). Optional. Defaults to matching the reference soldier's height.
- `height_scale_override`. Explicit multiplicative scale, overrides `target_height_metres` if set.
- `hip_leg_weight_blend`. Fraction of crotch-vert weight moved from Hips onto UpperLeg_L/R. `0.3` is a good default for MMD rigs that weight the whole pelvis pure-Hips.
- `lod_decimate_ratios`. Polygon ratio per LOD. Default `[1.0, 0.5, 0.25, 0.1]`.
- `lod_mesh_basename`. Prefix for output LOD mesh names. `BakeHumanoid` auto-detects this from mesh naming, so the value only matters for glTF inspection.

## Pipeline stages

1. Parse the reference soldier glTF for armature shape and bone landmarks.
2. Import PMX (mmd_tools), strip shape keys, compute uniform scale.
3. Rename PMX bones to MENACE humanoid names via `bone_map`.
4. Rebuild PMX materials as glTF-compatible Principled BSDFs.
5. Remap vertex groups, drop unmapped, rebind meshes to the armature.
6. Pose arm and foot chains to the reference avatar's T-pose (rotations from the avatar's `m_SkeletonPose`). Bake the mesh so the rest pose is T-pose. Foot bones get edit-mode head/tail changes only (no mesh bake) to preserve the PMX visual against Unity's toe-anchor convention.
7. Graft reference attachment bones (sockets) onto the PMX armature for weapons and equipment.
8. Blend hip-to-leg weights for crotch verts (optional, controlled by `hip_leg_weight_blend`).
9. Conform mesh names to `{lod_mesh_basename}_LOD0..LODN`.
10. Per-LOD Decimate at the configured ratios.
11. Export glTF. Source PMX textures pass through unchanged, one Principled BSDF material per source texture.

## Key files

- `scripts/pmx_to_menace.py`. The entire Blender conversion pipeline.
- `scripts/.config/<character>.json`. Per-model config (gitignored).
- `unity/Assets/Authored/<character>/`. Blender writes `model.gltf`, `model.bin`, and one PNG per PMX source texture here.
- `unity/Assets/Prefabs/<character>/`. Unity-side `BakeHumanoid` writes `main.prefab`, `avatar.asset`, and `baked_<source>.mat` per unique source texture here.
- `compiled/<character>__main.bundle`. Jiangyu's compile output for the addition prefab.

## Invariants

- The authored armature exports as Blender Z-up. The glTF exporter converts to Y-up with `export_yup=True`. No post-export root rotation.
- Every output LOD mesh name matches `{lod_mesh_basename}_LOD<N>` so `BakeHumanoid` can pick them up automatically.
- LOD0 ratio is `1.0`. Only LOD1..N are decimated.
- Vertex-colour attributes are stripped on export (`export_attributes=False`) so mmd_tools' AO-style vertex colour data doesn't multiply against texture colour at runtime.
