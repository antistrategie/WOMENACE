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
- `templates/perks/` contains perks shared by several Dolls, one file per perk. `templates/fairies/`, `templates/gifts/`, `templates/vehicles/`, and the root KDL files contain other shared or non-Doll content.
- `code/Systems/` contains runtime systems grouped by feature. `code/Perks/` contains custom perk behaviour. `code/Dev/` contains development-only verbs excluded from release builds.
- `assets/additions/` contains added sprites, textures, and audio. Logical asset names preserve the path below the asset-type directory.
- `unity/Assets/Authored/` contains source assets. `unity/Assets/Prefabs/` contains bundle-ready prefabs. `unity/Assets/Shaders/` contains the mod's `Womenace/` shaders. `unity/Assets/Editor/` holds the mod's own batch-mode tools (asset checks, clip extraction, prefab splitting) next to the Jiangyu-managed scripts.
- `scripts/` contains the Blender, asset-preparation, shading, voice, and weapon pipelines. Local pipeline configuration under `scripts/.config/` is gitignored.
- `compiled/`, `.jiangyu/`, and exported game data are generated or local working state and are not source.

## Runtime system routing

Use the implementation and its adjacent comments as the authority for current behaviour. The main cross-cutting systems are:

- `Systems/Affinity/` and `Systems/Gifts/` for affinity, gift drops, rewards, and unlock presentation.
- `Systems/Calibration/` for six-rank Doll weapon progression, affinity-earned components, workshop duplicates, and calibration UI.
- `Systems/Proficiency/` for affinity-scaled accuracy with a Doll's trained weapon class.
- `Systems/Transmog/` for outfits and the shared outfit and weapon skin picker. `Systems/Dolls/FormSwapSystem.cs` handles infantry, pilot, or mech form changes.
- `Systems/Procurement/` for reward pools, pity, limited claims, and permanent Curios unlocks.
- `Systems/Ssr/` and `Systems/Elements/` for imprint bonuses, elemental build-up, Phase effects, and HUD gauges. A kit that makes a victim take more damage from an element registers the multiplier with `ElementsSystem.RegisterVictimDamageMult`, which both the hit path and the hover preview read.
- `Systems/Dolls/` for bespoke per-Doll kits, one folder per Doll, plus the form-swap system they share.
- `Systems/Fairies/` for Fairy Lodge unlocks and off-map abilities.
- `Systems/NewGame/` for WOMENACE campaign options, Doll roster selection, vanilla leader filtering, and dummy-link limits.
- `Systems/Vehicles/` and `Systems/Weapons/` for signature vehicles, weapon skins, and weapon presentation.
- `Systems/CampaignMap/` for the GFL1-inspired mission-board reskin.

Persisted cross-system state belongs in `Context.State.Get<T>()`. Reusable rules and ID conventions belong in small shared models rather than being copied between systems.

## Content and asset rules

- WOMENACE adds content instead of replacing vanilla assets. Additions belong under `assets/additions/` or in mod-owned Unity bundles, then templates point to them.
- Character prefabs use `unity/Assets/Prefabs/<character>/<variant>/main.prefab` and KDL asset names such as `<character>/<variant>/main`. Outfit templates (`armor.<character>_<variant>`) carry no body: `TransmogSystem` loads `<character>/<variant>/main` the first time a doll wears the outfit, so a doll nobody fields costs no memory.
- Weapon prefabs use `unity/Assets/Prefabs/weapon/<name>/main.prefab` and KDL asset names such as `weapon/<name>/main`.
- `VehicleModelSystem` loads the signature vehicle bodies on first preview or tactical spawn. Their entity templates inherit a fallback prefab and carry no eager bundle reference.
- Asset references preserve nested paths. For example, `assets/additions/audio/weapons/rf/rf_shot_01.wav` is `asset="weapons/rf/rf_shot_01"`.
- The camouflage `media/promotion_unique.png` template is for a Doll's starting `UnitLeaderTemplate.InitialPerk`. Perks learned through promotion use `media/promotion.png`, including unique and active perks. Vanilla Command: Rally's tapered tan backing is an artwork exception.
- Audio under `assets/additions/audio/` compiles to Vorbis held compressed in memory. Clips above 48 kHz keep PCM, which is how the 96 kHz weapon effects stay uncompressed without a per-asset setting. Portraits compile to DXT5 and sprites to BC7 when their dimensions divide by four.
- Run `scripts/pmx_to_menace.py` before the scripts under `scripts/doll/`. The PMX conversion regenerates the mesh and discards later doll-preparation work.
- Doll squads normally pin `EntityTemplate.Scale` to `(1, 1)` and disable the squad-leader scale override. Every element uses the same Doll body, so vanilla random scale variation looks like inconsistent character height.
- Collision-prone IDs use the `wmgfl_` prefix. IDs already namespaced by a Doll or Jiangyu's cross-mod contract tags do not need another prefix.
- A KDL file never starts with a comment. The formatter drops the first node of such a file and everything after it, and format and compile both pass. Put the comment inside the first block.
- Generated KDL (voice conversations, a spine from a CSV) is generated once. After that it is hand-edited in place, never regenerated.

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
- Tactical element creation checks renderer materials through `BuildingShaderDamageState.NeedsMaterialDamageInfo` (RVA `0x6356C0`), which throws for null materials. Weapon attachments need valid material slots even on disabled particle renderers or ones that render only trails.
- `ShipUpgradeTemplate` with `ParentUnlocked` checks whether any parent is unlocked, not installed. Installation-gated Fairy children use `EventOnly` plus the runtime gate. A second-level O.C.I. row needs exactly one `ChildUpgrades` entry or the authored UXML sample slot appears.
- Offmap ability uses are one pool per operation on `OffmapAbilityInstance.m_RemainingUses`, refilled only by `Operation.StartOperation`, `EndOperation` and the ship-upgrades dialog. Fairy abilities are a per-mission budget through `FairyUsesSystem` (refill on `Operation.EndMission`, skill-card usage line reworded). Fairy `OffmapAbilityTemplate.UpgradeType` carries the mod-owned fairy type so `ChangeOffmapAbilityUsesEffect` (Arsenal) never matches them. Vanilla module cards never state uses, the skill card does.
- The mission board is outside `GetActiveScreen().GetRootElement()`. Reach `MissionPoi` and `MissionPoisContainer` through their instances or inspect the complete `UIDocument` panels. Mission completion is `Mission.GetStatus() == Played`, not the `mission_icon_played` sprite.
- The deployed `Jiangyu.Loader.dll` and the CLI used to compile must come from the same Jiangyu commit. A mismatch can make valid addition prefabs fail asset lookup at runtime.
- `mise` uses the Jiangyu CLI from `${JIANGYU_BUILD:-Debug}`. Treat Unity Editor script drift warnings as actionable. Build the configuration `mise` will use, then run `jiangyu unity sync` when managed scripts differ.
- Bundles target `StandaloneWindows64` with D3D11 shader variants for MENACE under Proton and DXVK. After installing missing Windows build support or correcting the target, run one clean compile so bad cached bundles are replaced.
- Extracted `Menace/*` shader stubs may render magenta in the Editor but are rebound to the game's shaders by name at runtime. Mod-owned shaders live under `Womenace/` and must retain needed runtime keyword variants with `multi_compile`.
- `BakeVehicle -targetLength` scales from measured renderer bounds. Check the logged measured length because a stray or double-transformed renderer silently rescales the whole vehicle.
- `SkillContainer` defers removals while it is dispatching to handlers (`m_UpdateStack` is raised for every OnMovementFinished, OnTurnEnd and OnUpdate). In that state `Remove(SkillTemplate)` only flags the first match as garbage and returns true, and the next call finds the same flagged skill again, so `while (container.Remove(template))` hangs the game with a clean log. Remove through `SkillEffects.RemoveInstances`, never in a loop on the return value.
- `TacticalManager.InvokeOnTurnEnd` fires from `Actor.SetTurnDone`, before the actor's `SkillContainer.OnTurnEnd` runs the `LifetimeLimit` countdown. An effect refreshed from an `InvokeOnTurnEnd` postfix is counted down straight after and expires a turn early. Refresh from an `Actor.OnTurnEnd` postfix instead.
- `TacticalPreloader.PreloadSkill` indexes the 14-entry `ImpactOnSurface` table of every fire skill during map generation. `clear "ImpactOnSurface"` on a weapon-fire clone leaves the mission stuck loading. Override the entries by index.
- `SkillTemplate.CustomAoEShape` and `AOETiles` are Odin-serialised and cannot be authored in KDL. A footprint other than a radius needs an `ICustomAoEShape` assigned by a system, with `GetAoERadius` returning 0 or the generic radius ring is drawn too. A handler template's fields read through a fresh interop wrapper come back as their initialisers, so read authored numbers off the handler instance the skill carries.
- `SpawnTileEffect.ChancePerTileFromCenter` is a per-tile falloff (-100 spawns on the aimed tile only, 0 spawns on every tile). A tile holds one spawned object effect, so when two handlers spawn on the same tile the later handler wins.
- `IsLimitedUses` with `Uses N` is a per-turn budget, on a perk-granted skill too. A per-mission charge is a hidden marker effect added on first use with `LimitUsability` on the skill.

## Writing conventions

- Use British English in code, comments, and documentation.
- Do not use em dashes or semicolons in prose, comments, or string literals.
- Describe only the current working state. Avoid historical narration, future promises, and stale plans.
- Prefer comments that explain why a constraint exists. Let names and nearby code explain what ordinary logic does.
