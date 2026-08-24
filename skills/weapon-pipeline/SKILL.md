---
name: weapon-pipeline
description: End-to-end weapon authoring for MENACE — OBJ + textures → Blender preprocessor → manual IK nudge → Unity bake → KDL WeaponTemplate clone, plus optional custom gunshot SoundBank + Skill clones. Use when adding a character-specific weapon model with bespoke audio or troubleshooting attach-point / IK / audio routing.
---

# Weapon pipeline

## What this covers

Adding a new weapon model + audio that a character (or anyone tag-allowed) can equip. Three sub-pipelines that chain together:

1. **Model**: OBJ → `bake_weapon.py` (Blender) → `raw.glb` with attach-point empties → manual nudge → `BakeWeapon.cs` (Unity) → addition prefab.
2. **Audio**: source WAV(s) → `bake_*_audio.py` (DSP) → close + distant variants under `assets/additions/audio/weapons/<class>/` → SoundBank clone.
3. **KDL**: WeaponTemplate clone (model + icons + skills + tag gate) + per-fire-mode Skill clones (audio routing).

## Pick a parent weapon

Find a vanilla weapon in the right class as the clone source. Class matters because it gates which slot consumes the weapon, what fire skills are inherited, and what icon scale templates use.

```bash
dotnet ../jiangyu/src/Jiangyu.Cli/bin/Debug/net10.0/jiangyu.dll templates inspect \
  --type WeaponTemplate --name <parent_id>
```

**`weapon.*` vs `specialweapon.*`** — specialweapons consume a precious squad slot (1 per squad in most setups). For a character-locked weapon that should feel like normal equipment, prefer `weapon.*`. Vanilla MENACE has no `weapon.*` snipers — the closest normal-class equivalent is `weapon.generic_battle_rifle_tier1_crowbar_marksman` (a battle rifle with a marksman barrel + scope).

Inspect `Model` (the reference 3D prefab the parent uses), `SkillsGranted[]` (the fire skills), and `Icon`/`IconEquipment`/`IconSkillBar` to know what you'll be overriding.

## 1. Blender preprocessor

`scripts/bake_weapon.py` driven by `scripts/.config/<weapon>.json`:

```bash
blender --background --python scripts/bake_weapon.py -- --config scripts/.config/<weapon>.json
```

What it does:

- Imports the source OBJ.
- Applies axis fixup so the gun's length lies along Blender −Y (forward) after the glTF Y-up→Z-up convention. For Sunborn-rip OBJs, use `obj_forward_axis: "X"` + `obj_up_axis: "Y"`. Counter-intuitive: `"X"` (not `"-X"`) lands the muzzle at glTF +Z post-export.
- Centres the mesh on X+Y (left/right symmetry + bbox-vertical centring). Z preserves grip-at-origin.
- Builds a Principled BSDF material with BaseColor/Normal/Roughness+Metallic wired up (Blender preview only, since Unity replaces it with the material the manifest names).
- Reads the **reference vanilla weapon's glTF**, extracts the `muzzle` and `weapon_hand_l` empty positions + rotations, and seeds two empties at those positions in the authored armature.
- Exports `raw.glb` + a `textures/` subdir, the RMO copied across untouched because `DollToon` reads its Roughness/Metallic/AO packing natively.

Config file shape (one JSON per weapon under `scripts/.config/`):

```json
{
  "obj_path": "/path/to/Gun.obj",
  "textures": {
    "base_map":   "/path/to/Gun_d.tga",
    "normal_map": "/path/to/Gun_n.tga",
    "mask_map":   "/path/to/Gun_rmo.tga"
  },
  "reference_weapon_gltf": "/tmp/<vanilla_weapon>/model.gltf",
  "output_path": "unity/Assets/Authored/weapon/<name>/raw.glb",
  "obj_forward_axis": "X",
  "obj_up_axis": "Y",
  "centre_axes": ["X", "Y"],
  "flip_uv_v": false,
  "mesh_basename": "<name>"
}
```

The reference glTF is the vanilla weapon's mesh + attach-points, exported once via:

```bash
dotnet ../jiangyu/src/Jiangyu.Cli/bin/Debug/net10.0/jiangyu.dll \
  unity import-prefab <vanilla_weapon>
dotnet ../jiangyu/src/Jiangyu.Cli/bin/Debug/net10.0/jiangyu.dll \
  assets export model <vanilla_weapon> --output /tmp/<vanilla_weapon> --path-id <id>
```

The `--path-id` comes from a search; `import-prefab` is what makes the reference available to `BakeWeapon` in the Unity step.

## 2. Manual IK nudge in Blender

`raw.glb` lands with seeded attach-point empties at the REFERENCE weapon's positions — i.e. they fit the parent gun's geometry, not yours. Open in Blender, nudge `muzzle` and `weapon_hand_l` to the right places on the new mesh:

- `muzzle` — where bullet trace lines start + muzzle flash spawns. Set to the front face of the barrel exit, oriented along the barrel axis (empty Y axis → out the barrel).
- `weapon_hand_l` — where the soldier's left hand grips the foregrip. Position is the palm centre; rotation defines the wrist orientation.

**The mesh and empties are siblings under a `<name>_root` empty**, so moving the mesh shifts the right-hand grip (the prefab parents under Hand_R at runtime) without dragging the muzzle / left-hand IK targets. Move the empties; leave the mesh alone unless the right-hand grip is also wrong.

**Don't apply transform to the empties** — they have no geometry, so apply zeroes the position. Save the GLB and re-bake without applying. The mesh transform can be applied if you moved the mesh; the empties cannot.

**Mirrored rotation gotcha**: glTF round-trips can sometimes flip the empty's rotation around the X axis (e.g. quaternion `(+0.597, +0.253, +0.761, +0.025)` instead of the reference's `(-0.597, +0.253, +0.761, -0.025)`). Manifests in-game as the left hand grip rotated 180° around the gun barrel. Patch the rotation directly in the GLB (modify the JSON chunk, fix the chunk length headers) before re-baking; see `scripts/render_weapon.py` for a working GLB-mutation example.

## 3. Unity bake

```bash
python3 scripts/weapon/shade_weapons.py --bake <name>
```

That writes the weapon's `materials.json` and runs `Jiangyu.Mod.BakeWeapon.BakeBatch` over it, putting the weapon on `Womenace/DollToon` with the shared weapon ramp so the gun a doll carries is shaded like the doll. The mask it binds is the Sunborn `_rmo`, which is the packing `DollToon` reads natively.

A single-material weapon keeps its d / n / rmo trio flat in `textures/`. A weapon whose GLB carries several materials keeps one trio per material in `textures/<material name>/`, and the manifest gets one entry per material.

`BakeWeapon.cs` (in `unity/Assets/Jiangyu/Editor/`) clones a material from the reference vanilla weapon and applies the manifest over it, so a manifest naming a shader and all four maps leaves nothing of the reference behind and one reference serves every weapon. Unset texture slots fall back to neutral 1×1 defaults, which matters for `_MaskMap` because Unity's default white would read as Metallic=1 and turn the gun chrome-blue.

Output:

- `unity/Assets/Prefabs/weapon/<name>/main.prefab` — the authored prefab
- `unity/Assets/Prefabs/weapon/<name>/baked.mat` — the cloned material with new textures

The `asset="weapon/<name>/main"` KDL ref points at this prefab.

## 4. Icons

Three sprite icons feed the WeaponTemplate: `Icon` (912×320 catalogue tile), `IconEquipment` (smaller inventory thumbnail), `IconSkillBar` (skill-row glyph). MENACE's vanilla icons are dark matte gunmetal, soft painterly rendering, on pure white. Match that or it'll stand out.

Generate via Gemini's image model, fed the source render plus four vanilla MENACE icons as style anchors:

1. Render the weapon in Blender with `scripts/weapon/render_weapon.py --mode flat --view side` to produce a clean 912×320 side profile of the actual model.
2. Hand Gemini the source render + 4 vanilla MENACE icons (any 912×318 weapon sprite extracted from MENACE's `resources.assets`).
3. Prompt: repaint the source in the reference art style, preserve geometry exactly, side profile, white background, same dimensions.
4. Crop/resize the result for `IconEquipment` and `IconSkillBar`.

Save the three PNGs to `assets/additions/sprites/weapon/<name>/{Icon,IconEquipment,IconSkillBar}.png`. The asset names become `weapon/<name>/Icon` etc in KDL.

## 5. WeaponTemplate clone

```kdl
clone "WeaponTemplate" from="<parent_weapon>" id="weapon.<character>_<name>" {
    set "Title" { set "m_DefaultTranslation" "<weapon name>" }
    set "ShortName" { set "m_DefaultTranslation" "<class label>" }   // e.g. "Sniper Rifle"
    set "Description" { set "m_DefaultTranslation" "<flavour>" }
    set "Model" asset="weapon/<name>/main"
    set "SkillsGranted" index=0 "active.fire_<character>_<name>"           // primary fire
    set "SkillsGranted" index=1 "active.fire_<character>_<name>_<mode>"   // aimed/sustained/burst
    set "Icon" asset="weapon/<name>/Icon"
    set "IconEquipment" asset="weapon/<name>/IconEquipment"
    set "IconSkillBar" asset="weapon/<name>/IconSkillBar"
}
```

`SkillsGranted` indices match the parent's positions — typically [0] = primary semi-auto/burst and [1] = aimed/sustained/marksman. Inspecting the parent reveals what's at each index.

## 6. Custom gunshot audio (optional)

Skip if you want the weapon to use the vanilla parent's gunshot SFX — `SkillsGranted` already inherits the parent's `SoundsOnAttack`.

For custom audio:

### Source clips

One clip is enough; two gives a bit of variety. For a bolt-action sniper, you only need single-shot variants (no burst concatenation). For an automatic weapon, see the burst recipe in [`project_weapon_audio_pipeline`](../../) memory — concatenate 4/3/2 shots offset by `60s/RPM` per round.

Bake script (per-weapon): converts source WAV(s) to 96 kHz mono, derives a distant variant via lowpass(4 kHz) + 5 early reflections at 38/75/135/220/310 ms. Pattern in `/tmp/bake_m200_audio.py` for the M200 / `rf` class.

Output lands at `assets/additions/audio/weapons/<class>/<name>_NN.wav`. The `weapons/` subdir keeps weapon SFX grouped separately from character voice barks.

### SoundBank clone

`templates/weapon/soundbank.kdl` collects all weapon-class banks side by side:

```kdl
clone "SoundBank" from="weapons_soundbank" id="weapons_<class>_addition_bank" {
    clear "sounds"
    clear "busIndices"

    append "sounds" {
        set "name" "<class>_shot"
        set "fixedPitch" 1.0
        append "variations" {
            set "clip" asset="weapons/<class>/<class>_shot_01"
        }
        append "variations" {
            set "clip" asset="weapons/<class>/<class>_shot_02"
        }
    }
    append "busIndices" 0

    append "sounds" {
        set "name" "<class>_shot_distant"
        set "fixedPitch" 1.0
        append "variations" {
            set "clip" asset="weapons/<class>/<class>_shot_distant_01"
        }
        append "variations" {
            set "clip" asset="weapons/<class>/<class>_shot_distant_02"
        }
    }
    append "busIndices" 0
}
```

**Variations-per-Sound** is the weapon-bank convention (different from voice banks' filename-as-name). The engine picks a random variation per fire, so back-to-back shots don't sound identical. The Sound name is the logical event (`<class>_shot`), the variations are the file refs.

### Skill clone(s)

Per fire mode in `templates/weapon/<weapon>.kdl`:

```kdl
clone "SkillTemplate" from="<parent_fire_skill>" id="active.fire_<character>_<name>" {
    clear "SoundsOnAttack"
    append "SoundsOnAttack" {
        set "bankId" "weapons_<class>_addition_bank"
        set "itemId" "<class>_shot"
    }
    append "SoundsOnAttack" {
        set "bankId" "weapons_<class>_addition_bank"
        set "itemId" "<class>_shot_distant"
    }
    clear "SoundsOnAttackFar"
    append "SoundsOnAttackFar" {
        set "bankId" "weapons_<class>_addition_bank"
        set "itemId" "<class>_shot_distant"
    }
}
```

`SoundsOnAttack` is the layered close-range audio (close + distant tail mixed; the engine attenuates the distant layer for nearby observers). `SoundsOnAttackFar` is what distant observers hear — single layer, just the distant variant.

Optionally keep a vanilla brass layer (`bankId "weapons_soundbank"; itemId "small_caliber_brass_burst"`) in the close-range stack for character — the AK-15 setup does this; the M200 (bolt-action) skips it since the click is part of the recorded shot.

`SoundsOnAttack` and `SoundsOnAttackFar` are inherited from the parent until you `clear` + `append`. If the parent's other entries (e.g. magazine clatter) are what you want to keep, don't `clear` — just `set "SoundsOnAttack" index=N {...}` to override a specific layer.

Update the WeaponTemplate's `SkillsGranted` index to reference your new skill clone IDs.

## Animated weapons (moving parts)

A weapon whose parts move (rails that unfold, a rocket that shows and hides) ships a skinned mesh with its own Animator. No runtime code: MENACE's `ElementAnimator` collects every Animator under the element at creation, the mounted weapon prefab included, and forwards each parameter it sets on the soldier to every attachment Animator declaring the same name. The vanilla `rpg_t1` prefab hides its rocket on `Shoot_Single` this way, and every tripod weapon is a skinned rig on the same contract. Parameters worth declaring:

- `Shoot_Single` / `Shoot_Burst` (Trigger): the skill's `AnimationType` (ShootSingle, ShootBurst, ThrowGrenade, SpecialAttack1..4 map to `Throw_Grenade`, `Special_Attack_1`, ...).
- `Stance` (Int): `ActorStance` Default 0, Deployed 1, PinnedDown 2. A skill with `IsDeploymentRequired` fires from stance 1, so an unfold driven by `Stance == 1` opens the weapon as the soldier deploys.
- `AmmoCount` (Int): from a `ReportAmmoToAnimator` handler on the skill. `CustomSkillEffect` (Trigger): from a `SetAnimatorTrigger { Parameter = TriggerCustomSkillEffect }` handler, for a clip one particular skill should play.

Authoring, worked in `scripts/weapon/build_asteria_railgun.py`:

1. Build the rig in Blender with the same object layout as a rigid weapon: `<name>_root` empty holding the armature, the skinned mesh (armature modifier) and the `muzzle` / `weapon_hand_l` empties as siblings. One action per clip, each on its own muted NLA track named after the clip (the exporter writes one glTF animation per track). Every clip keys the full pose.
2. Export with `export_animation_mode="NLA_TRACKS"`, `export_skins=True`, `export_force_sampling=True`. The exporter can flip the sign of x and w on an empty's rotation, so pin `weapon_hand_l` to its intended glTF quaternion after export (`pin_node_rotation` there does this with `fix_hand_grip.py`'s GLB reader).
3. Write `controller.json` beside `raw.glb`: parameters, states (each naming a clip, `loop`, one `default`) and transitions (`conditions` as `[parameter, mode, value]` with Equals / NotEqual / Greater / Less / If / IfNot / Trigger, or an `exitTime` in normalised clip time, optional `duration`).
4. `python3 scripts/weapon/shade_weapons.py --bake <name>` picks the controller up and passes `-controller` to `BakeWeapon`, which copies the clips into a `weapon.controller` beside the prefab, builds the state machine and puts the Animator on the prefab root. glTFast wraps an animated scene in one extra root node, and the bake lifts the authored root back out (clip paths lose that segment) so `muzzle` and `weapon_hand_l` sit directly under the prefab root like every rigid weapon.

The bake tool is generic: the parameter names, the state graph and the clips all come from the weapon's own `controller.json`.

## Laser and beam visuals

A skill's visuals are plain prefab references on the SkillTemplate, so a beam weapon is data: `ProjectileData` set with `type="LaserProjectileData"` (a `Prefab` the engine stretches from muzzle to impact with `LaserProjectile`, fading over 0.5 s), `SecondaryProjectileData` for sparks, `MuzzleEffect`, and the surface-indexed `ImpactOnSurface` table. `asset="<vanilla prefab name>"` resolves a vanilla prefab by name. `active.tripod.fire_laser_lance` is the red large-beam reference set (`LaserTracerCapsule`, `laser_sparks`, `laser_muzzle_flash_large_01`, `impact_laser_large_*`); `templates/dolls/asteria/railgun.kdl` carries it onto a rocket-launcher skill. `DecalCollection` references have no `asset=` route, so a clone keeps its parent's decals.

## File layout

```
scripts/
├── bake_weapon.py                    Blender preprocessor (shared across weapons)
├── render_weapon.py                  Optional: render the weapon to a transparent PNG (icon prep)
└── .config/<weapon>.json             Per-weapon config (gitignored)

unity/Assets/
├── Authored/weapon/<name>/           Blender output: raw.glb + textures/ (+ controller.json for an animated weapon)
└── Prefabs/weapon/<name>/            BakeWeapon output: main.prefab + baked.mat (+ weapon.controller)

assets/additions/
├── audio/weapons/<class>/            Custom gunshot WAVs (96 kHz, close + distant)
└── sprites/weapon/<name>/            Icon / IconEquipment / IconSkillBar PNGs

templates/weapon/
├── <weapon>.kdl                      WeaponTemplate + Skill clones
└── soundbank.kdl                     All weapon SoundBank clones in one file
```

## Common shape mistakes

- **Cloning from `specialweapon.*`** when you wanted a normal-class weapon — consumes the squad's precious specialweapon slot. Swap to `weapon.generic_battle_rifle_*` or similar.
- **Wrong attach-point rotation** on `weapon_hand_l` — left hand floats 180° around the barrel. Patch the rotation in `raw.glb` (see the IK nudge gotcha).
- **Skipping `set "fixedPitch" 1.0`** on SoundBank sound entries — the engine may treat `fixedPitch=0` as muted. Vanilla weapons set it explicitly.
- **Bank/skill name mismatch** — the SkillTemplate's `bankId` string must match the SoundBank's clone-ID exactly. The loader hashes (FNV-1a) the string at runtime; a typo silently misroutes.
- **`SoundsOnAttack` populated, but `SoundsOnAttackFar` left empty** after a `clear` — distant observers hear nothing. Always populate both, or skip both clears.

## Cross-references

- [`voice-pipeline`](../voice-pipeline/SKILL.md) for the voice-bank-vs-weapon-bank distinction (filename-as-name vs variations-per-Sound).
- [`../../AGENTS.md`](../../AGENTS.md) for audio-bank routing, sprite slots, and the burst-concatenation recipe for full-auto weapons.
