---
name: pmx-to-menace
description: Convert an MMD PMX character into an addition-prefab glTF for MENACE via Jiangyu. Use when adding a new character, troubleshooting an existing conversion, or asking about material / skinning / LOD behaviour in the PMX pipeline.
---

# PMX to MENACE conversion

## What this pipeline does

Shading the converted model is [`doll-shading`](../doll-shading/SKILL.md). Run this conversion **before** that: it regenerates the glTF and discards anything added afterwards, including hand-painted weights if they were painted on the glTF rather than kept in the `.blend`.

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
  -gltfFolder Assets/Authored/<character>/<variant> \
  -referencePrefab Assets/Imported/rmc_default_female_soldier_2/GameObject/rmc_default_female_soldier_2.prefab \
  -outputDir Assets/Prefabs \
  -outputName <character>/<variant>

# 3. Jiangyu: compile + deploy
mise run compile   # compiled/ bundles, one addition bundle per prefab
mise run deploy    # into the game's Mods/<modname>/
```

The `.config/` directory is gitignored. Configs hold absolute PMX paths on local disk.

A model that needs hand edits runs in two stages instead of `full`: `--stage prep --blend <path>` writes a full-resolution LOD0 `.blend` to edit, and `--stage finish --blend <path>` decimates the edited LOD0 and exports. Re-prepping an outfit whose approved `.blend` carries hand-painted weights takes `--keep-body-weights <approved.blend>`: every vertex not on a spring chain keeps the approved weights, matched by position, and the prep refuses when the meshes do not match. Pass Blender `--python-exit-code 1`, since it otherwise exits 0 when the script throws. After any re-prep, compare per-material LOD0 vertex counts against the committed bake: a strip pattern that silently matches nothing shows up there and nowhere else.

## Config file structure

One JSON per character in `scripts/.config/`. Fields:

- `pmx_path`. Absolute path to the source `.pmx`.
- `reference_prefab_path`. Absolute path to a vanilla MENACE soldier's exported `model.gltf` (under `exported/<soldier>/`). Used for armature shape and attachment-bone landmarks.
- `reference_avatar_path`. Absolute path to the matching reference soldier's `*Avatar.asset` (under `unity/Assets/Imported/<soldier>/Avatar/`). Used for T-pose muscle-zero calibration.
- `output_path`. Where Blender writes the authored `model.gltf` (typically `unity/Assets/Authored/<character>/<variant>/model.gltf`).
- `source_mesh_names`. Names of the PMX mesh objects to transfer.
- `bone_map`. PMX bone to MENACE humanoid bone mapping. Drives both the bone rename and vertex-group remap.
- `ignore_bones`. PMX bones to drop entirely (typically MMD IK control bones).
- `target_height_metres`. Absolute character height in metres (Foot to Head bone span). Optional. Defaults to matching the reference soldier's height. Policy: GFL2 canon span x1.2, where canon span is the raw PMX's own Foot to Head distance at the standard 0.08 import scale. `scripts/doll/measure_pmx_height.py` prints the config-ready number for a PMX.
- `height_scale_override`. Explicit multiplicative scale, overrides `target_height_metres` if set.
- `skip_palm_calibration`. Disables the right-hand palm-down mesh roll. Only for rigs whose combat animations are their own captured clips rather than retargeted vanilla holds: the clips are self-consistent with the rig, so recalibrating the palm would break the grips they were captured with. The LEFT hand is never palm-calibrated for anyone, it is IK-slaved to each weapon's `weapon_hand_l` empty.
- `hip_leg_weight_blend`. Fraction of crotch-vert weight moved from Hips onto UpperLeg_L/R. `0.3` is a good default for MMD rigs that weight the whole pelvis pure-Hips.
- `dress_leg_prefixes`. Vertex-group prefixes of a physics skirt, cape or coat grid. Their weights are rewritten onto the humanoid rig before the bone rename: pelvis at the waistband, the legs below the crotch, split left and right by the vertex's X. Without this the grid folds into one torso bone and the legs animate through the cloth. List only the rows that hang over the thighs: a cape's back rows stay on the pelvis or they drag forward with the leg swing.
- `dress_leg_blend_top` and `dress_leg_blend_depth`. The pelvis-to-leg ramp in metres, top relative to the crotch and depth measured down from the top (defaults 0.02 and 0.22). A long skirt clearing the thigh takes the default. Cloth sitting on the thigh needs a shallow ramp, around 0.05 and 0.06, or the half-weighted panel rotates half as far as the leg under it.
- `dress_leg_split_width`. Half-width in metres of the centreline strip that blends between the two legs. Defaults to the hip half-width, which averages both legs across the whole front. Narrow it, around 0.03, so each panel welds to the thigh it covers.
- `dress_leg_front_band` and `dress_leg_front_pivot`. Half-width in metres of the front-to-back band that follows the legs, centred at the pivot offset from the hip joint (negative is in front). Vanilla locomotion only swings a leg forward, so only the front of a skirt should follow. Unset keeps the whole grid leg-following. A pleated skirt takes around 0.03 at 0.09.
- `strip_material_patterns`. Materials whose name contains any of these are deleted first. MMD rigs ship alternate-state submeshes such as bare feet under shoes, named `(Hide)` or `Unused`, which MMD viewers hide and a straight conversion renders through the clothing. Also used to drop props the doll should not carry, such as display guns.
- `strip_bone_patterns`. Vertices weighted entirely to PMX bones matching any of these are deleted. For an accessory riding a chain the humanoid rig has no counterpart for, which would otherwise hang in mid-air off the nearest humanoid bone.
- `spring_bones`. Keep the PMX's physics chains as their own bones for the runtime spring solver and write the outfit's spring profile (see Spring bones). Skips the `dress_leg_prefixes` rewrite for those chains.
- `hang_down_chain_prefixes`. Chains that hang straight down. With `spring_bones` they become draped ribbons in the generated profile.
- `lod_decimate_ratios`. Polygon ratio per LOD. Default `[1.0, 0.5, 0.25, 0.1]`.
- `lod_mesh_basename`. Prefix for output LOD mesh names. `BakeHumanoid` auto-detects this from mesh naming, so the value only matters for glTF inspection.

## Spring bones

With `spring_bones` on, the converter reads the PMX's MMD physics rigid bodies, splits the simulated bones into root-to-leaf chains, keeps them off `bone_map` so they bake as their own skinned bones, and writes `code/Systems/SpringBones/Profiles/<doll>_<variant>.cs`. A chain's preset comes from its bone names and rest shape: hair, stiff cloth, ribbon, draped ribbon, skirt, gown, split gown or accessory. Body jiggle bones are left out.

The profile is written once and never overwritten. After that it is tuned in place. An unedited profile can be deleted to regenerate it. The runtime is `code/Systems/SpringBones/`: `SpringBody` is the one frame driver shared by the live rig and the offline replay, so they cannot diverge.

- Tune live with the `Springs.Status` and `Springs.Set` dev verbs, then copy settled values into the profile.
- Check the whole roster offline with the replay harness in `tests/SpringBones/` (its README covers recording and the report). It runs the real solver against each outfit's baked skeleton.
- MENACE's deployed stance is a kneel with one shin flat behind her. A floor-length skirt too slim to swallow that leg lets the shin out through its back on the generated `Gown`. Switch it to `SpringPresets.SlimGown` and the profile's colliders to `SlimGownColliders`, which follow the legs with the back only while a knee is bent deep and widen the thigh capsules. A full ball gown stays on `Gown`.
- Regenerating the Asteria and Alva default profiles must reproduce the committed ones exactly. Use that as the regression check after changing the converter's chain rules.

## Pipeline stages

1. Parse the reference soldier glTF for armature shape and bone landmarks.
2. Import PMX (mmd_tools), strip shape keys, compute uniform scale.
3. Rename PMX bones to MENACE humanoid names via `bone_map`.
4. Rebuild PMX materials as glTF-compatible Principled BSDFs.
5. Remap vertex groups, drop unmapped ones (spring chain bones are kept), rebind meshes to the armature.
6. Pose arm and foot chains to the reference avatar's T-pose (rotations from the avatar's `m_SkeletonPose`). Bake the mesh so the rest pose is T-pose. Foot bones get edit-mode head/tail changes only (no mesh bake) to preserve the PMX visual against Unity's toe-anchor convention.
7. Graft reference attachment bones (sockets) onto the PMX armature for weapons and equipment.
8. Blend hip-to-leg weights for crotch verts (optional, controlled by `hip_leg_weight_blend`), and rewrite the `dress_leg_prefixes` groups onto pelvis and legs. Check the result with the thigh raised in the prep blend before `--stage finish`.
9. Conform mesh names to `{lod_mesh_basename}_LOD0..LODN`.
10. Per-LOD Decimate at the configured ratios.
11. Export glTF. Source PMX textures pass through unchanged, one Principled BSDF material per source texture.

## Key files

- `scripts/pmx_to_menace.py`. The entire Blender conversion pipeline.
- `scripts/.config/<character>.json`. Per-model config (gitignored).
- `unity/Assets/Authored/<character>/<variant>/`. Blender writes `model.gltf`, `model.bin`, and one PNG per PMX source texture here.
- `unity/Assets/Prefabs/<character>/<variant>/`. Unity-side `BakeHumanoid` writes `main.prefab`, `avatar.asset`, and `baked_<source>.mat` per unique source texture here.
- `compiled/bundles/<character>__<variant>__main.bundle`. Jiangyu's compile output for the addition prefab.
- `code/Systems/SpringBones/Profiles/<doll>_<variant>.cs`. The outfit's spring profile, written once by the converter.

## Invariants

- The authored armature exports as Blender Z-up. The glTF exporter converts to Y-up with `export_yup=True`. No post-export root rotation.
- Every output LOD mesh name matches `{lod_mesh_basename}_LOD<N>` so `BakeHumanoid` can pick them up automatically.
- LOD0 ratio is `1.0`. Only LOD1..N are decimated.
- Vertex-colour attributes are stripped on export (`export_attributes=False`) so mmd_tools' AO-style vertex colour data doesn't multiply against texture colour at runtime.
