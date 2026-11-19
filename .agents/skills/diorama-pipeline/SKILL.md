---
name: diorama-pipeline
description: Building the strategy diorama's assets from GFL2 scenes: the scene exporters, BakeRoom, vfx_to_unity.py and BuildRoomVfx, BakeAnimatedProp, the doll poses, the colour grade and the hologram builders, in the order they run. Use when rebaking a room or its effects, adding a room or a prop to the diorama, or tracing which tool writes an environment/* prefab.
---

# Diorama pipeline

## What this covers

The strategy diorama (`code/Systems/Strategy/`) loads its rooms and props as prefabs under `unity/Assets/Prefabs/environment/` and `unity/Assets/Prefabs/effects/`. Every one of them is written by a Unity batch tool under `unity/Assets/Editor/` from source files under `unity/Assets/Authored/`. Those source files come from the GFL2 client through the scene exporters in `~/gfl2-extract/scenes/` (outside this repository, see its `README.md`) and the scripts in `scripts/diorama/`.

Every Unity tool runs in batch mode against `unity/`:

```bash
Unity -batchmode -nographics -quit -buildTarget StandaloneWindows64 -projectPath unity \
  -executeMethod Womenace.EditorTools.<Tool>.<Method> <arguments> -logFile <tool>.log
```

Each tool's header comment carries its exact arguments. After any of them, `jiangyu compile` bundles the prefabs it wrote.

| Asset | Tool | Source |
|-------|------|--------|
| `environment/<room>/main` | `BakeRoom.Run -room <room>` | `Authored/environment/<room>/` (`scene.gltf`, `bake.json`, textures, lightmap, shadowmask, probe cubes) |
| `environment/<name>_vfx/main` and the machine's `gacha_machine_vfx*` | `BuildRoomVfx.Run -name <name>` | `Authored/environment/<name>/` (the text `vfx_to_unity.py` writes, plus `overrides.txt`) |
| `environment/dinergate/main` | `BakeAnimatedProp.BakeBatch` | a skinned GLB with its clips and textures |
| `environment/doll_poses/main` | `BuildDioramaPoses.Run` | `Authored/diorama/_donor/<doll>.fbx` and `.prefix` |
| `environment/ops_room_grade/main` | `BuildRoomGrade.Run` | `Authored/environment/ops_room_grade/gfl2_lut.png` |
| `environment/elmo/main` | `BuildElmoHologram.Run` | `Authored/environment/elmo/elmo_<part>.obj` and `.png` |
| `environment/gacha_machine/main` | `BuildGachaHologram.Run` | `Authored/environment/gacha_machine/gacha_machine.glb` |
| `effects/doll_hologram/main` | `BuildDollHologram.Run` | `Authored/effects/doll_hologram/hologram.mat` |

The runtime names these assets in `DioramaScene.cs` (the three rooms and their effects) and in the feature that uses each prop.

## Run order

### 1. Export the scene

Follow `~/gfl2-extract/scenes/README.md`: `deps.py`, `scene_dump.py`, `scene_probes_post.py`, `scene_gltf.py`, then `scene_bake_sidecar.py`. Copy the resulting `gltf/` folder to `unity/Assets/Authored/environment/<room>/`. `scene_probes_post.py` also writes `post.json`, the scene's post-processing volumes, whose values `StrategyDiorama.Rooms.cs` carries by hand in the room's `RoomLook`.

### 2. Bake the room

`BakeRoom.Run -room <room>` writes `Prefabs/environment/<room>/main.prefab` with:

- every material on `Womenace/BakedScene` with GFL2's textures and values, a material per reflection probe a renderer takes, and one per light-probe-lit renderer, all under `materials/`, which the bake deletes and writes afresh each run. A material on one of GFL2's effect shaders listed in `EffectShaders` goes on `Womenace/GfVfx` instead.
- GFL2's shadow casting per renderer, the room's geometry on its own rendering layer.
- only the lights GFL2 renders live (its sun and its Realtime and Mixed lights), under `_Lights`.
- disabled holder renderers the runtime reads: `_LightProbes` (a float texture of the probes), `_ReflectionProbes` (each probe's box and cube), `_MirrorBox` (the floor mirror's cube, `Womenace/PrebakedCube`), and an inactive `_HoloScreen` quad that carries `Womenace/HoloScreen` into the bundle.
- `_CameraAnchor` at GFL2's camera, when the sidecar has one.

The probe cubes under `probes/` are written over in place, so their GUIDs survive a rebake and a prop baked apart from the room keeps its reflection. A cube the room no longer has is deleted. The log reports how many renderers each probe reflects on and the share of tangents that run along +u: a share near zero means the glTF's handedness conversion failed and every normal map shades mirrored.

`RunOnRows` lengthens a row of scenery GFL2 laid only as wide as its own camera sees, for a room whose diorama camera turns further.

### 3. Effects

Dump a scene's or prefab's effects with `vfx_dump.py` from `~/gfl2-extract/scenes/`, then turn the dump into text the editor reads:

```bash
uv run scripts/diorama/vfx_to_unity.py <dump dir with vfx.json> <name>
```

It writes `textures/`, `textures.txt`, `meshes/`, `materials.txt` and `nodes.txt` under `unity/Assets/Authored/environment/<name>/`. `BuildRoomVfx.Run -name <name>` then rebuilds the hierarchy, meshes, `Womenace/GfVfx` materials and every particle system property, and writes `Prefabs/environment/<name>/main.prefab`. Its `meshes/` and `materials/` folders are written afresh each run. The prefab is in the source scene's space, so a room's effects line up with its baked room with no offset. The log lists the particle properties it could not set (`MISSED`).

The mod's own changes to GFL2's effect materials go in `overrides.txt` beside the dump (`TEX <material> <property> <texture>`), never in `materials.txt`, so a fresh dump keeps them.

### 4. Animated props

`BakeAnimatedProp.BakeBatch` bakes a skinned GLB with its clips into a clickable prop for a baked room: `Womenace/BakedScene` lit by a flat ambient level, one of the room's reflection probe cubes (`-reflection Assets/Prefabs/environment/<room>/probes/<probe>.asset`), an AnimatorController whose default clip loops and whose other clips play once and return to it, and a box collider. The probe cube must exist, so bake the room first. A rebake of the room keeps the cube's GUID, so the prop needs no rebake after one. Its effects build with `BuildRoomVfx` like a room's.

### 5. Doll poses

`scripts/diorama/export_lobby_clips.py [doll ...]` exports each doll's GFL2 lobby animations on her own rig as an FBX donor in `unity/Assets/Authored/diorama/_donor/` (gitignored), with her take prefix in `<doll>.prefix`. It needs the CN asset map and the staged VoyExport build.

`BuildDioramaPoses.Run [-dolls a,b] [-skipExtract]` imports each donor as Humanoid through `Authored/diorama/humanmap.txt`, extracts the lobby loops as muscle clips into `Authored/diorama/clips/<doll>/`, and wires every doll's clips into one controller whose states are `<doll>-<pose>`. `-skipExtract` rebuilds the controller from the clips already extracted, which is all a fresh checkout without the donors can do. The prefab also carries the contact shadow quad (`Womenace/BlobShadow`) the runtime clones under each doll.

### 6. Colour grade and holograms

These depend on nothing above and run in any order.

- `BuildRoomGrade.Run` carries GFL2's ops room colour lookup strip into the bundle as an uncompressed texture, so the runtime's HDRP tonemapping LUT keeps its exact 8-bit values.
- `BuildElmoHologram.Run`, `BuildGachaHologram.Run` and `BuildDollHologram.Run` put their models on `Womenace/DollHologram`. Each loads its material if it exists and creates it only if it does not, so values tuned on the `.mat` survive a rebuild. `BuildGachaHologram` also builds the machine's controller from the GLB's two pull clips and keeps its `Camera` node, whose x scale is the pull camera's focal length.

## Checking a bake

- Read each tool's log for its summary line and warnings.
- `jiangyu compile` after the bake, then open the room in game. The diorama's dev verbs (`StrategyDiorama.Dev.cs`) report a room's live lights, shadow light and nearest light probes.
- A material that renders magenta in the Editor is usually an extracted `Menace/*` stub, which the game rebinds by name at runtime (see `AGENTS.md`). A `Womenace/*` shader that renders magenta is a real error.
