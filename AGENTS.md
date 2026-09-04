# WOMENACE

WOMENACE is a Girls' Frontline overhaul mod for MENACE, built with [Jiangyu](https://github.com/antistrategie/jiangyu). It adds Dolls as squad leaders and pilots with custom models, voices, perk trees, weapons, outfits, alternate forms, affinity progression, weapon proficiency and calibration, SSR imprint mechanics, elemental Phase damage, Fairy O.C.I. modules, and other Doll-specific systems.

Most content is authored as KDL in `templates/`. Runtime behaviour that templates cannot express lives in the C# mod under `code/`. Models and other bundled visuals are authored in the Unity project under `unity/`.

## Start here

Read Jiangyu's own [AGENTS.md](https://github.com/antistrategie/jiangyu/blob/main/AGENTS.md) for SDK concepts, KDL grammar, asset bundles, loader hooks, and current CLI behaviour. Do not duplicate those contracts here.

Use the pipeline skill that matches the work:

- [`skills/character-authoring/SKILL.md`](skills/character-authoring/SKILL.md) for a Doll's KDL spine.
- [`skills/pmx-to-menace/SKILL.md`](skills/pmx-to-menace/SKILL.md) for PMX to humanoid prefab conversion.
- [`skills/doll-shading/SKILL.md`](skills/doll-shading/SKILL.md) for GFL-style materials, face SDFs, hair UVs, and outlines.
- [`skills/voice-pipeline/SKILL.md`](skills/voice-pipeline/SKILL.md) for voice audio, subtitles, SoundBanks, and conversations.
- [`skills/weapon-pipeline/SKILL.md`](skills/weapon-pipeline/SKILL.md) for weapon models, audio, templates, and skills.

[`docs/ONBOARDING.md`](docs/ONBOARDING.md) is the longer human-oriented introduction.

## Repository map

- `templates/dolls/<name>/` contains each Doll's leader, entity, armour, perk tree, weapon, calibration ranks, voice, and any Doll-specific KDL.
- `templates/fairies/`, `templates/gifts/`, `templates/vehicles/`, and the root KDL files contain shared or non-Doll content.
- `code/Systems/` contains runtime systems grouped by feature. `code/Perks/` contains custom perk behaviour. `code/Dev/` contains development-only verbs excluded from release builds.
- `assets/additions/` contains added sprites, textures, and audio. Logical asset names preserve the path below the asset-type directory.
- `unity/Assets/Authored/` contains source assets. `unity/Assets/Prefabs/` contains bundle-ready prefabs. `unity/Assets/Shaders/` contains the mod's `Womenace/` shaders.
- `scripts/` contains the Blender, asset-preparation, shading, voice, and weapon pipelines. Local pipeline configuration under `scripts/.config/` is gitignored.
- `compiled/`, `.jiangyu/`, and exported game data are generated or local working state and are not source.

## Runtime system routing

Use the implementation and its adjacent comments as the authority for current behaviour. The main cross-cutting systems are:

- `Systems/Affinity/` and `Systems/Gifts/` for affinity, gift drops, rewards, and unlock presentation.
- `Systems/Calibration/` for six-rank Doll weapon progression, affinity-earned components, workshop duplicates, and calibration UI.
- `Systems/Proficiency/` for affinity-scaled accuracy with a Doll's trained weapon class.
- `Systems/Transmog/` and `Systems/Dolls/FormSwapSystem.cs` for outfits and infantry, pilot, or mech form changes.
- `Systems/Ssr/` and `Systems/Elements/` for imprint bonuses, elemental build-up, Phase effects, and HUD gauges.
- `Systems/Dolls/` for bespoke kits such as OTs-14's weapons bay, Sextans' solo melee kit, Cheyanne's aim trainer and ricochet, Soppo's stances, Vector's Overburn, and Voymastina's Sinbreaker form.
- `Systems/Fairies/` for Fairy Lodge unlocks and off-map abilities.
- `Systems/NewGame/` for WOMENACE campaign options, Doll roster selection, vanilla leader filtering, and dummy-link limits.
- `Systems/Vehicles/` and `Systems/Weapons/` for special vehicles and weapon presentation such as The Sinner and Asteria's particle cannon.
- `Systems/CampaignMap/` for the GFL1-inspired mission-board reskin.

Persisted cross-system state belongs in `Context.State.Get<T>()`. Reusable rules and ID conventions belong in small shared models rather than being copied between systems.

## Content and asset rules

- WOMENACE adds content instead of replacing vanilla assets. Additions belong under `assets/additions/` or in mod-owned Unity bundles, then templates point to them.
- Character prefabs use `unity/Assets/Prefabs/<character>/<variant>/main.prefab` and KDL asset names such as `<character>/<variant>/main`.
- Weapon prefabs use `unity/Assets/Prefabs/weapon/<name>/main.prefab` and KDL asset names such as `weapon/<name>/main`.
- Asset references preserve nested paths. For example, `assets/additions/audio/weapons/rf/rf_shot_01.wav` is `asset="weapons/rf/rf_shot_01"`.
- Audio under `assets/additions/audio/` compiles to Vorbis held compressed in memory. Clips above 48 kHz keep PCM, which is how the 96 kHz weapon effects stay uncompressed without a per-asset setting. Portraits compile to DXT5 and sprites to BC7 when their dimensions divide by four.
- Run `scripts/pmx_to_menace.py` before the scripts under `scripts/doll/`. The PMX conversion regenerates the mesh and discards later doll-preparation work.
- Doll squads normally pin `EntityTemplate.Scale` to `(1, 1)` and disable the squad-leader scale override. Every element uses the same Doll body, so vanilla random scale variation looks like inconsistent character height.
- Collision-prone IDs use the `wmgfl_` prefix. IDs already namespaced by a Doll or Jiangyu's cross-mod contract tags do not need another prefix.

## Commands

- `mise compile` builds templates, code, and bundles into `compiled/`.
- `mise deploy` deploys the compiled mod to MENACE.
- `mise format` formats KDL and C#.
- `mise lint` verifies the C# project without modifying it.
- `jiangyu unity sync` refreshes Jiangyu-managed Unity Editor scripts.

Run verification in proportion to the change. At minimum, compile after KDL, C#, prefab, shader, or asset-reference changes. Build and exercise the relevant in-game path for runtime patches or UI changes.

## Inspecting MENACE

Use the least expensive source that can answer the question:

1. Use `jiangyu templates search`, `query`, and `inspect` for template types, members, and live template values.
2. Use the generated `cpp2il_out/Assembly-CSharp.dll` for metadata, attributes, offsets, and method RVAs. Its method bodies are stubs.
3. Disassemble `GameAssembly.dll` at the recorded RVA only when behaviour cannot be established from templates, metadata, or a live probe.

Record expensive findings next to the code they constrain. Add a short repository-level note here only when the finding applies across features or is difficult to locate from the implementation.

## Hard-won constraints

- Infantry badge fields are split across templates. `EntityTemplate.Badge` and `BadgeWhite` drive the mission badge and turn-bar squad list. `UnitLeaderTemplate.BadgeMini` and `BadgeDragged` drive mission-preparation previews. `BadgeUnitWindow`, `BigBadge`, `Slot`, `SlotInactive`, and `SlotBadge` each feed distinct UI surfaces. `EntityTemplate.PreviewMapIcon` does not drive the mission-preparation preview.
- A combat entity drops its `EntityTemplate` tags but keeps its speaker. Doll identity during missions must come from `Entity.GetSpeakerTemplate()`, normally through `Affinity.CharacterTag(Entity)`.
- `SkillTemplate.Repetitions` is read asynchronously after `Skill.Use` returns. An owner-specific repetition override cannot be restored in a `Use` postfix without collapsing the attack to one shot.
- Attachment animators are collected when the element is created. Animators mounted later are not included. Mounted weapon clips can receive the soldier's declared animator parameters, and heavy-weapon clips can raise MENACE animation events through `AnimatorEventRelay`.
- `ShipUpgradeTemplate` with `ParentUnlocked` checks whether any parent is unlocked, not installed. Installation-gated Fairy children use `EventOnly` plus the runtime gate. A second-level O.C.I. row needs exactly one `ChildUpgrades` entry or the authored UXML sample slot appears.
- The mission board is outside `GetActiveScreen().GetRootElement()`. Reach `MissionPoi` and `MissionPoisContainer` through their instances or inspect the complete `UIDocument` panels. Mission completion is `Mission.GetStatus() == Played`, not the `mission_icon_played` sprite.
- The deployed `Jiangyu.Loader.dll` and the CLI used to compile must come from the same Jiangyu commit. A mismatch can make valid addition prefabs fail asset lookup at runtime.
- `mise` uses the Jiangyu CLI from `${JIANGYU_BUILD:-Debug}`. Treat Unity Editor script drift warnings as actionable. Build the configuration `mise` will use, then run `jiangyu unity sync` when managed scripts differ.
- Bundles target `StandaloneWindows64` with D3D11 shader variants for MENACE under Proton and DXVK. After installing missing Windows build support or correcting the target, run one clean compile so bad cached bundles are replaced.
- Extracted `Menace/*` shader stubs may render magenta in the Editor but are rebound to the game's shaders by name at runtime. Mod-owned shaders live under `Womenace/` and must retain needed runtime keyword variants with `multi_compile`.
- `BakeVehicle -targetLength` scales from measured renderer bounds. Check the logged measured length because a stray or double-transformed renderer silently rescales the whole vehicle.

## Writing conventions

- Use British English in code, comments, and documentation.
- Do not use em dashes or semicolons in prose, comments, or string literals.
- Describe only the current working state. Avoid historical narration, future promises, and stale plans.
- Prefer comments that explain why a constraint exists. Let names and nearby code explain what ordinary logic does.
