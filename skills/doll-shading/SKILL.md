---
name: doll-shading
description: Shade a doll with the GFL2 character shader in MENACE. Use when adding or re-baking a doll's materials, porting ramp atlases, wiring the face SDF or eye layers, or debugging how a doll renders (flat, too bright, seams, missing outline). The PMX-to-glTF conversion has its own skill; this is everything after it.
---

# Doll shading

## What this covers

Turning a converted doll glTF into a shaded MENACE prefab: the GFL2 shader set, the
ramp atlases, the face SDF, the eye layers, the outline, and the bake that wires them
together. Model conversion is [`pmx-to-menace`](../pmx-to-menace/SKILL.md).

The shading model is GFL2's, not HDRP's. The spec it is built against lives outside
this repo, in the tdollhouse project's `docs/gfl2-shader-spec.md`, and the shaders
carry the reasoning inline where a line departs from it.

## One command

```bash
python3 scripts/doll/prepare_doll.py unity/Assets/Authored/<doll>/<outfit> --bake
mise run compile && mise run deploy
```

`prepare_doll.py` runs the whole sequence in the order it has to happen: reconstruct
the face SDF coordinates, append the outline submesh, resolve every material, invoke
`BakeHumanoid`. Without `--bake` it prepares and prints the bake invocation.

**Order is the reason it exists.** Two steps mutate the glTF and one reads it, so
resolving materials first bakes a doll with no outline and no SDF coordinates. Both
mutating steps are idempotent, so re-running is safe.

## A bake drops every post-pass on that prefab

`BakeHumanoid` rewrites `main.prefab` whole. Anything an Editor pass attached to it
afterwards is gone, and nothing warns: re-baking Sextans for the shading rollout
reverted both her outfits to the vanilla soldier animator, which surfaced only because
someone watched her move.

So `prepare_doll.py` carries a `POST_PASSES` table, re-runs the passes that own the
prefab it just baked, and then checks that they took, by looking for the guid of an
asset each pass must leave referenced. Adding a doll that needs one means adding an
entry:

```python
{
    "outfits": ("sextans/default", "sextans/nocte"),
    "method": "Womenace.EditorTools.BuildSextansController.Build",
    "describes": "Sextans' animator controller and its GFL2 clip swaps",
    "expects": "Assets/Prefabs/sextans/_bake/sextans.controller",
}
```

The `expects` check is the point. A pass that silently no-ops leaves exactly the
failure it exists to prevent, so running it is not evidence that it worked.

This covers passes over prefabs `prepare_doll.py` bakes. Two others sit outside it and
are re-run by hand: `VehicleOutlineHulls` after `BakeVehicle`, and
`BuildVoymastinaMech`, which owns the mech's prefabs end to end.

## Never convert after preparing

`pmx_to_menace.py` rebuilds the glTF from the PMX and discards anything added
afterwards. **Hand-painted weights included**, if those were painted on the glTF
rather than in the `.blend`.

Nothing in `scripts/doll/` reads or writes `JOINTS` or `WEIGHTS`, and nothing there
regenerates the mesh, so preparing a doll cannot disturb weighting. The hazard runs
one way only: Blender first, prepare second, never the reverse. `prepare_doll.py`
warns when a sibling `.blend` is newer than `model.gltf`, which is the signature of
unexported Blender edits about to be baked over.

## What each script does

| script | reads | writes |
|---|---|---|
| `prepare_doll.py` | — | orchestrates the four below, then bakes |
| `bake_face_sdf_uv.py` | rest positions, `data/face_sdf_uv_ref.npz` | `TEXCOORD_2` in `model.bin` |
| `transfer_hair_uv.py` | rest positions, `<doll>/hair_uv1_ref.npz` | `TEXCOORD_1` in `model.bin` |
| `add_outline_submesh.py` | `doll_shading` part rules | an `Outline` material + primitives |
| `doll_shading.py` | `model.gltf`, texture folder | nothing — prints bake arguments |

Three more sit beside them, run once per character rather than per bake:

```bash
python3 scripts/doll/extract_character_refs.py <doll> <name in the client>
```

pulls that character's ramp atlases and hair UV reference out of the GFL2
client, which is where both come from and neither is derivable from the PMX.
`bake_ramp_png.py` is the decoder it uses, callable on its own for a gradient
the ranking picks wrong, and `transplant_eyes.py` fits a doll's four-layer eye
stack onto a face that has none.

**A source without a `TEXCOORD_2` is given one.** Most exports carry it as a
constant `(0, 1)` holding nothing, which the SDF bake overwrites. Where it is
absent the bake appends one, on every primitive and not only the face's:
glTFast clusters a mesh's primitives by vertex layout and splits the ones that
disagree onto separate meshes, so a face carrying a UV set the body lacks would
land as two renderers per LOD instead of one.

## Per-part routing

`doll_shading.py` maps material names to parts by substring, then parts to shaders and
ramps. GFL2 names parts conventionally, so this is shared across every doll and no
doll needs its own list. Adding a doll usually means adding no rules at all.

| part | shader | notes |
|---|---|---|
| hair, cloth, skin, silkstock, weapon | `DollToon` | ramp per part |
| face, mouth, teeth, tongue, eyewhite | `DollToon` + face SDF | one material; they are welded together |
| eyes, iris, pupil | `DollEye` | opaque eyeball, view-based UV parallax |
| eyeshadow | `DollEyeShadow` | multiply layer, `Blend DstColor Zero` |
| eyes+ | `DollEyeHighlight` | additive layer, `Blend One One` |
| outline | `DollOutline` | inverted hull on its own submesh |
| emotions | game's shader | inert overlays parked behind the face |

The three shaders that own their pixels (`DollToon`, `DollEye`, `DollOutline`) each carry
a `MotionVectors` pass out of the shared `DollMotionVectors.hlsl`. The two eye blend
layers deliberately do not: they are `ZWrite Off` layers over the eyeball, and the
eyeball already carries the depth and the motion for those pixels.

## Transparency: the texture name decides, not the alpha

A surface goes on `DollToonTrans` when its base map's name carries `ubertrans`,
and otherwise it does not. GFL2 names a texture after the material that binds it
and its material names carry the shader (`uber`, `ubertrans`, `faceuber`,
`eyelashuber`), so that name is the game stating which of its two surface
shaders draws the material.

**Do not route from measured alpha.** Silk stockings, jacket zips, sunglass
lenses and lace all measure partial alpha across most of their footprint, some
of them at a flat 60% over 100% of it, and the game draws every one of them
opaque. Routing the car's wheels from an alpha histogram put solid geometry on a
blended shader. Across this project's dolls exactly one costume trips the real
test: Soppo's Redline sticker sheet, on
`c_SoppoSSR0101_slg_cloth_trans_ubertrans_da`.

A translucent material also drops out of the outline submesh, because the game
draws no contour through transparency and a hull around a decal sheet is a rim
in mid-air around the sheet rather than around anything visible on it.

## The weapon she carries is a separate prefab

A doll's glTF contains a weapon mesh, and `doll_shading` shades it. That is **not**
the weapon the game equips. The equipped one is the standalone `weapon/<doll>` prefab
the template names (`set "Model" asset="weapon/<doll>/main"`), and it is baked by
`BakeWeapon` from its own `raw.glb`. Shading only the doll leaves a cel-shaded doll
holding a physically shaded gun.

One command writes every weapon's manifest and bakes it:

```bash
python3 scripts/weapon/shade_weapons.py --bake            # all of them
python3 scripts/weapon/shade_weapons.py --bake makiatto   # or some of them
```

It reads the manifest key from `raw.glb`, finds the three maps by suffix in the
weapon's `textures/`, and binds the shared weapon ramp. A manifest rather than
the blanket `-overrideShader`, because the ramp has to be bound per material:

```bash
Unity -batchmode -nographics -quit -buildTarget StandaloneWindows64 \
  -projectPath unity -executeMethod Jiangyu.Mod.BakeWeapon.BakeBatch \
  -gltfPath Assets/Authored/weapon/<doll>/raw.glb \
  -referencePrefab Assets/Imported/<reference weapon>/GameObject/<reference weapon>.prefab \
  -outputDir Assets/Prefabs -outputName weapon/<doll> \
  -materialManifest Assets/Authored/weapon/<doll>/materials.json
```

The same thing is authorable in the window: `Jiangyu → Bake weapon prefab from glTF…`,
"Fill from source glTF" for the slot names, then a shader, extra textures and values
per slot.

- **The reference prefab donates a material to clone, and nothing else.** A
  manifest naming a shader and all four maps leaves none of it, so one reference
  serves every weapon.
- **The mask is the Sunborn `_rmo`.** `DollToon` reads GFL2's packing natively,
  R rough / G metal / B occlusion. Any HDRP-convention repack, which holds metallic
  in R and smoothness in alpha, reads metallic as roughness and inverts the gloss.
- **The manifest key is the glTF material name**, which is the exporter's preview
  material (`wa2000_preview`), not the mesh name. Read it from the glb or press
  "Fill from source glTF".
- **Check the mesh has tangents.** `DollToon` builds its tangent frame from `TANGENT`
  and a glTF exporter often omits it; the importer generates them here, but a mesh
  without them shades off a zero vector and now writes that into the normal buffer.
  `MeshAttributeCheck.Run -assetPath <asset>` reports normals, tangents and UV sets.
- The weapon takes no outline yet: the contour needs a duplicated submesh, and
  `add_outline_submesh.py` works on `model.gltf` plus `model.bin`, not on a `.glb`.

## Ramp atlases: check the orientation

Ramps live in two places, split the way the game splits them. `Authored/<doll>/ramps/`
carries hair, cloth and silkstock, which ship as per-character gradient assets and
genuinely differ per character: Groza's hair floor is a warm brown where Makiatto's is
deep maroon. `Authored/shared/ramps/` carries skin and weapon, which no character
ships a gradient asset for: those atlases are global, dumped from the capture.
`doll_shading` looks in the doll's own ramps first, then shared, so a doll overrides a
shared ramp by shipping its own.

Each character's set comes from the client's own `RampAtlasRGBA` assets, pulled
by `extract_character_refs.py`, with `scripts/doll/data/<doll>_ramp_gradients.json`
keeping the dump they were built from. Three things to know when adding a
character:

- **The part is not always in the name.** Cheyanne's and Sextans' costumes name
  their main gradient `body_ramp`, and Sextans ships a `cloth_ramp` beside a
  `cloth3_Baoshi_ramp` for its gemstones. The extractor ranks an unqualified
  name over a qualified one and the base costume over an alternate, which is a
  ranking, not a fact: check what it picked before believing it.
- **One set per character serves every outfit.** Alternate costumes ship their
  own ramps, split across `P1`/`P2`/`P3` parts and several cloth atlases that a
  single `ramp_cloth_main` cannot express, so an outfit takes its character's
  base-costume set.
- **Not every character ships every part.** Leva and Voymastina ship no
  silkstock gradient at all, so their stockings fall back to the character's own
  cloth atlas rather than to a global one: the game's silkstock gradients sit
  close to their character's cloth and nowhere near each other.
- **A character can be missing from a client.** Lenna is in the global client
  and not in CN, which is what `--client` is for.

A ramp is a 256x16 atlas of four bands, and **binding it upside down is silent**. The
main-diffuse band at V=0.125 must carry the warm per-part gradient; if it carries a
neutral grey that is identical across parts, the atlas is flipped and every surface is
shading through a linear grey curve with no stylisation at all. That reads as
"plastic" and nothing else looks wrong.

Two checks that catch it:

- Sample V=0.125 on two different parts. Cloth and silkstock must differ, and
  silkstock must read lighter and cooler. If they are byte-identical, it is flipped.
- The hair band's shadow end should be a warm maroon near `#250000`, not black.

`DollToon` mixes the band's dark end halfway toward grey of the same luminance,
weighted toward U=0, in `RampDiffuse`. The floors are authored for GFL2's scene
ambient, which MENACE's scenes lack, so as authored the strongly red skin and hair
floors land shadow on pink where the game reads cream. The half mix is tuned in
game and preserves the ramp's brightness.

Ramps and the face SDF import **uncompressed, linear, no mips**. They are data read at
exact coordinates, not images: compression quantises the values the shader thresholds
against, and filtering across rows blends bands that mean different things.

## Things that cost a day if you do not know them

- **The spec's main-diffuse facing scale reads black in MENACE.** The spec scales
  the ramp sample by `min(1, NoL/max(eps, NoL))`, zero for any surface facing away
  from the key, leaving that side to the scene's ambient. GFL2's authored ambient
  carries it, but a MENACE scene's probe measures near one percent of the key, so
  the faithful transcription renders the whole away side black. `DollToon` drops
  the scale and every surface the key does not reach lands on the ramp's warm dark
  end instead, exactly as the face SDF path does.
- **A second pass with the same `LightMode` tag never draws.** HDRP draws only the
  first. That is why the outline and the eye layers are separate materials on
  duplicated submeshes rather than extra passes. A dead pass compiles, ships, and
  reports itself in `passCount` — it just never executes.
- **A `MotionVectors` pass that does not write stencil bit 32 achieves nothing.**
  HDRP's full-screen camera motion pass runs straight afterwards with
  `Comp NotEqual` against `StencilUsage.ObjectMotionVector`, so untagged pixels have
  their object motion overwritten by camera-only motion. The bit is internal to HDRP,
  hence the bare 32.
- **`disabledShaderPasses: MOTIONVECTORS` on an HDRP Lit material is the default and
  is not a bug.** In HDRP's own words it "doesn't disable motion vector, it just mean
  that the material don't do any vertex deformation but we can still have skinning /
  morph target". MENACE's own character materials carry it. Do not read it as the game
  opting out.
- **A renderer with motion vectors is dropped from the depth prepass.**
  `excludeObjectMotionVectors` is set on that renderer list, so the `MotionVectors`
  pass is the only prepass record of her depth and has to write it — matching the
  forward pass's cull, its depth bias and, for the outline, its clip-space expansion.
- **Verify shaders against D3D11.** A shader can compile for the editor's API and fail
  for the one the bundle ships. Run `ShaderCheck` with
  `-buildTarget StandaloneWindows64`, then read the build log.
- **The log that matters for shaders is `unity/build.log`.** That is the prefab bundle
  build, which is what a doll's shaders compile into. `.jiangyu/unity_build_mesh.log`
  is the raw-GLB mesh stage and is only rewritten when that stage reruns, so it goes
  stale across incremental builds and reports a clean shader that was never rebuilt.
  Check its timestamp against the bundle's before believing either.

  ```bash
  grep -E "Shader (error|warning)" unity/build.log
  # and confirm the keyword variants exist rather than being stripped away:
  grep -A5 'Compiling shader "Womenace/DollToon" pass "MotionVectors" (fp)' unity/build.log
  ```

  `Full variant space` counts what the pragmas declare; `After scriptable stripping`
  is what ships. A pass reporting a full space of 1 when it declares `multi_compile`
  keywords is reading a stale log.
- **`mise run compile` refreshes the Editor scripts from the *Debug* CLI build.** Edit
  a bake template and compile will quietly revert it unless jiangyu's Debug
  configuration is rebuilt.
- **The retarget rig turns the object frame.** Its root bone sits at Euler
  (0, 180, 180), a half turn about X, and the glTF import mirrors X on top of that.
  Anything resolving a direction into object space has to undo both.
- **Unity crashes with a file-descriptor assertion** when the shell's limit is very
  high. `ulimit -n 8192` before invoking it.

## Known gaps

The hair-to-face shadow band is parked: depth cannot separate "fringe in front of
face" from "fringe in front of hair", both being the same few centimetres, and the
stencil route did not take in either pass context. The face carries no outline until a
per-vertex width is baked to zero at its open boundary edges. The face glint is
unbuilt: the SDF map's G and B channels carry its landmark masks and nothing reads
them yet.

An alternate costume takes its character's base-costume ramps, because its own set
splits across parts a single `ramp_cloth_main` cannot express. Doing better means a
per-material ramp binding rather than a per-part one.

`DollToonTrans` carries neither a `TransparentBackface` pass nor a transparent depth
prepass, which is right for the decals, livery and glass it currently draws and wrong
for the first genuinely sheer garment: a skirt would show its own far side through its
near one in draw order. `DollToon` has no `clip()` cutout path either, so a fabric
with binary holes has to go through the blended shader and pay its sorting.
