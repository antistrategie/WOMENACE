# WOMENACE

A Girls' Frontline mod for MENACE, built with [Jiangyu](https://github.com/antistrategie/jiangyu). Characters are authored from PMX (MMD) sources, converted to Unity-native prefabs via a Blender + Unity pipeline, and shipped to MENACE through Jiangyu's KDL template system as squad-leader clones, armor clones, entity clones, and perk trees. Runtime behaviour the templates cannot express (affinity progression, gift drops, SSR-weapon imprint bonuses, the mech form swap) lives in a C# code mod under `code/`.

This file is the agent-onboarding briefing. For SDK-level concepts (KDL templates, clone/patch grammar, addition vs replacement, asset bundle pipeline, loader hooks), read Jiangyu's own [AGENTS.md](https://github.com/antistrategie/jiangyu/blob/main/AGENTS.md) first. For per-pipeline detail:

- [`skills/character-authoring/SKILL.md`](skills/character-authoring/SKILL.md) — KDL spine (tag, speaker, entity, perk tree, armor, squad leader).
- [`skills/pmx-to-menace/SKILL.md`](skills/pmx-to-menace/SKILL.md) — PMX → addition-prefab glTF.
- [`skills/voice-pipeline/SKILL.md`](skills/voice-pipeline/SKILL.md) — voice transcription, SoundBank, ConversationTemplate clones.
- [`skills/weapon-pipeline/SKILL.md`](skills/weapon-pipeline/SKILL.md) — weapon OBJ → prefab + custom gunshot SoundBank + Skill clones.

## What belongs in this file

Things an agent can't easily find by reading the code. If the answer lives in `templates/`, `mise.toml`, Jiangyu's source, or anywhere else in the repo, leave it out. If finding it requires disassembling MENACE, reading Jiangyu loader internals, or trial-and-error, write it down here so the next agent doesn't repeat the work. Expensive-to-derive findings (cached disassembly results, behaviour quirks) count too. Jiangyu's AGENTS.md owns SDK-level concepts. Cross-reference, don't duplicate.

## Layout

```
WOMENACE/
├── jiangyu.json            mod manifest (name + Jiangyu version pin)
├── mise.toml               task runner: compile, deploy, unity-init, unity-open
├── templates/              KDL template patches and clones
│   ├── common.kdl          shared registrations (pickable leaders, dossier roster)
│   ├── dolls/<character>/  per-character spine (tag, speaker, entity, leader, perks, armor) + voice/
│   ├── weapons/            weapon + fire-skill clones, one file per weapon (+ shared soundbank.kdl)
│   └── gifts/              affinity-gift item clones, one file per rarity
├── code/                   C# code mod: JiangyuSystem subclasses (auto-discovered) + Harmony patches
│   ├── Templates.cs        shared id→template lookup + caching resolve
│   ├── Systems/Affinity/   affinity model + unlock grants + badge popover
│   ├── Systems/Transmog/   outfit transmog: shared model, body-prefab swap, picker UI, save migration
│   ├── Systems/Ssr/        SSR "Imprint Boost" weapons (owner-only combat bonuses + tooltip section)
│   ├── Systems/Dolls/      per-doll systems (e.g. Voymastina mech form swap)
│   ├── Systems/Gifts/      affinity-gift drops + catalogue
│   ├── Perks/              custom perk behaviours
│   └── Dev/                dev-only verbs (*.Dev.cs, excluded from release builds)
├── scripts/                Authoring + asset pipelines (Python)
│   ├── pmx_to_menace.py    PMX → glTF (humanoid characters)
│   ├── weapon/
│   │   ├── bake_weapon.py    OBJ → glTF (weapons + attach-point empties)
│   │   └── render_weapon.py  glTF → transparent PNG (icon prep)
│   ├── voice/
│   │   ├── transcribe.py   OpenAI ASR + MT → per-character .trans.csv
│   │   ├── serve.py        Local web utility: browse + play character voice lines
│   │   └── normalize_audio.py  LUFS-normalise voice clips to vanilla MENACE
│   └── .config/            per-character/weapon pipeline configs (gitignored)
├── skills/                 Per-pipeline SKILL.md docs
│   ├── character-authoring/
│   ├── pmx-to-menace/
│   ├── voice-pipeline/
│   └── weapon-pipeline/
├── unity/                  Unity 6000.0.72f1 Editor project (URP)
│   ├── Assets/
│   │   ├── Authored/       PMX/OBJ-derived character + weapon assets (committed, one subdir per character or weapon)
│   │   ├── Imported/       vanilla MENACE prefab rips (gitignored, repopulated by `jiangyu compile` from `importedPrefabs` in jiangyu.json)
│   │   ├── Jiangyu/Editor/ Jiangyu-managed Editor scripts (BuildBundles, BakeHumanoid, etc.)
│   │   └── Prefabs/        modder-authored prefab outputs (one subdir per character or weapon)
│   └── Packages/manifest.json
├── compiled/               build output, gitignored (jiangyu.json + bundles)
├── exported/               persistent AssetRipper exports referenced by scripts/.config/ (gitignored)
└── .jiangyu/               Jiangyu cache, gitignored (unity_build, exports, glb_staging, etc.)
```

## Per-character pipeline

A full character ships four kinds of content end-to-end via `mise compile && mise deploy`:

1. **KDL spine** — TagTemplate, SpeakerTemplate, EntityTemplate, UnitLeaderTemplate, PerkTreeTemplate, ArmorTemplate clones at `templates/dolls/<character>/`. See [`skills/character-authoring/SKILL.md`](skills/character-authoring/SKILL.md).
2. **3D model** — PMX source → glTF (Blender) → addition prefab (Unity). See [`skills/pmx-to-menace/SKILL.md`](skills/pmx-to-menace/SKILL.md).
3. **Voice** — rip dir → normalised + transcribed WAVs at `assets/additions/audio/<character>/` → SoundBank + ConversationTemplate clones. See [`skills/voice-pipeline/SKILL.md`](skills/voice-pipeline/SKILL.md).
4. **Weapon (optional)** — OBJ source → glTF (Blender) → addition prefab (Unity) + WeaponTemplate clone + custom gunshot SoundBank + Skill clones. See [`skills/weapon-pipeline/SKILL.md`](skills/weapon-pipeline/SKILL.md).

`mise compile` parses `templates/`, builds each `Assets/Prefabs/<...>/main.prefab` into the mod bundle, writes `compiled/`. `mise deploy` copies into `~/.steam/.../Menace/Mods/WOMENACE/`. At MENACE startup, Jiangyu's loader rebinds bundled materials' shader names to MENACE's loaded shader catalogue via `Shader.Find`.

Voymastina is the reference end-to-end example covering all four. Cheyanne is a second reference with a different parent character (carda vs sy), which exposes more of the parent-namespace + role-mapping decisions.

## Asset paths

Jiangyu picks the Unity import type based on the directory under `assets/additions/`:

- `sprites/<character>/` becomes `Sprite` assets (Badge, SlotBadge, BadgeUnitWindow, etc.).
- `textures/<character>/` becomes `Texture2D` assets (StandLookLeftImage, BigBackground, etc.).
- `audio/<character>/` becomes `AudioClip` assets, force-imported as PCM + DecompressOnLoad (Vorbis defaults would smear transients on percussive content like gunshots).

Logical asset names preserve nested subdirectories: `assets/additions/audio/weapons/rf/rf_shot_01.wav` → asset `weapons/rf/rf_shot_01`. KDL refs use the full nested path: `asset="weapons/rf/rf_shot_01"`. The first-level subdir under `audio/sprites/textures` is the modder's organising convention (per character, per weapon class, etc.); Jiangyu doesn't impose a layout, only preserves what's there.

Character prefabs ship in the mod bundle built from `unity/Assets/Prefabs/<character>/<variant>/main.prefab` and are referenced as `asset="<character>/<variant>/main"`. Weapon prefabs at `unity/Assets/Prefabs/weapon/<name>/main.prefab` are referenced as `asset="weapon/<name>/main"`.

## Inspecting MENACE internals

Three layers, use in order:

1. **Jiangyu CLI** (`jiangyu templates …`). `search <substring>` finds types and vanilla instances. `query <Type>[.Member]` lists fields with their types, writability, and the JSON patch shape. `inspect --type X --name id [--with-mod .]` shows actual values, optionally with your KDL applied. First stop for "what fields exist, what does this vanilla template already set".
2. **cpp2il_out** at `~/.local/share/Steam/.../MelonLoader/Dependencies/Il2CppAssemblyGenerator/Cpp2IL/cpp2il_out/Assembly-CSharp.dll`. For constraints the CLI doesn't surface (`[Range]`, `[NamedArray]`, field offsets, `[Address(RVA=…)]`). Method bodies are stubbed to `return null`, so this is type metadata only.
3. **GameAssembly.dll** via `objdump -d --start-address=0x<RVA>`. Last resort, for behaviour such as which field a UI slot actually reads or which branch a method takes. RVAs come from cpp2il's `[Address(RVA="…")]` attributes.

## Sprite slots

Several badge/portrait fields exist on both `UnitLeaderTemplate` and `EntityTemplate`, and MENACE's UI is inconsistent about which one it reads. For an Infantry leader clone (`UnitActorType == 0`, the default for any sy-derived character):

- `EntityTemplate.Badge` / `BadgeWhite` drive the in-mission badge above units and the turn-bar squad list, via `UnitLeaderTemplate.GetBadge()` → `InfantryUnitTemplate.Badge` branch for Infantry.
- `UnitLeaderTemplate.BadgeMini` drives the small squad badge drawn on the mission preparation tactical preview (`MissionPrepDeployedEntity.m_SmallImage`). Read directly in `MissionPrepDeployedEntity.Init` (RVA 0x7F5010, `mov rdx,[rdx+0x130]`) via DeployedEntity → BaseUnitLeader.LeaderTemplate, not via `GetBadge()`. The same method also reads `BadgeDragged` (offset 0x138) into `m_DraggedImage`. This is the one direct reader of `BadgeMini` in the binary, so without setting it Infantry clones show sy's badge on the mission prep map.
- `UnitLeaderTemplate.Badge` / `BadgeWhite` are dead for Infantry. The only reader is `UnitLeaderTemplate.GetBadge()`, which always takes the `InfantryUnitTemplate.Badge` branch for `UnitActorType == 0`.
- `EntityTemplate.PreviewMapIcon` is a Sprite slot but is not read by the mission preparation preview. Vanilla `player_squad.sy.PreviewMapIcon` is null and setting it on a clone has no observable effect on screens checked so far.
- `UnitLeaderTemplate.Slot` / `SlotInactive` drive the portrait in the turn-bar.
- `UnitLeaderTemplate.BadgeUnitWindow` drives the unit info window header. Read in `UnitLeaderUIExtensions.InitUnitWindowHeader`, an extension method on `BaseUnitLeader` that's easy to miss when sweeping UI class methods.
- `UnitLeaderTemplate.BigBadge` drives the hiring info panel banner.
- `UnitLeaderTemplate.SlotBadge` / `BadgeDragged` drive the hire-slot and drag visuals (BadgeDragged also serves as the mission-prep drag preview).

## Affinity and unlocks

The affinity feature is a shared model (persisted state + a static read API + a declarative rules table) that its `JiangyuSystem`s coordinate through, never by calling each other (the SDK's only cross-system channel: `Context.State.Get<T>()` returns the same live instance to every system of the mod).

- `AffinityState` (in `code/Systems/Affinity/Affinity.cs`) is the only persisted store of points, keyed by a stable FNV-1a hash of the character's own tag (`wmgfl_<name>`, parsed out of the speaker `Tags`) so a character's forms share one total (Voymastina's squad-leader and pilot share a speaker) and the key does not move when unrelated speaker tags change. `AffinitySystem` is its only writer.
- `Affinity` (static) is the shared read API: level maths (`StepThresholds`, `LevelForPoints`), the leader key (`KeyFor`), and the character tag (`CharacterTag`, e.g. `wmgfl_voymastina`). Any system gates on affinity through this.
- `Unlocks` (`code/Systems/Affinity/Unlocks.cs`) is the per-character map of `level -> Feature`. Both the gameplay gates and the badge popover read it, so the level a feature unlocks at and the level the popover advertises cannot drift. A character absent from the map has no unlocks. Add unlocks here and every consumer follows.

Three gate mechanisms, chosen to fit how MENACE surfaces each:

- **Outfits are gated in the transmog picker** (`Systems/Transmog/`). Dolls equip vanilla armour for stats; the outfit `ArmorTemplate`s (`armor.<doll>_*`) are pure cosmetic carriers no unit can equip (their `OnlyEquipableBy` names only the `wmgfl_transmog` marker tag, which no unit carries, so vanilla filtering hides them from every equip dropdown). `TransmogSystem` postfixes `EntityVisuals.DetermineArmorPrefab` (a static with exactly two callers: the tactical spawn and the armoury preview) and swaps the returned body prefab to the selected outfit's model for any unit whose `EntityTemplate` carries a `wmgfl_<name>` tag. Selections persist in `TransmogState` keyed like affinity (`Affinity.KeyForTag`), defaulting to the convention-derived `armor.<name>_default` (`Transmog.DefaultFor` probes the registry for it, existence is the doll test). `TransmogPickerSystem` renders the picker: an `IconSkillBar` tile over the armour slot's bottom-right corner (a SIBLING of the slot, not a child: the game resolves clicks from element ancestry, so a child would open the armour dropdown too) opening the `transmog/outfit-modal` UXML, one card per outfit, locked cards greyed until `Unlocks` says the level is reached. After a selection the armoury's 3D stage respawns by raising the container's `OnVisualAlterationChanged` event, the same path a real armour equip refreshes through (neither `UnitWindow.SetLeader` nor the unit selector's `Refresh` rebuilds the stage).
- **The mech form is gated at the swap button.** The Pilot form is reachable only through `VoymastinaFormSwapSystem`'s swap (the pilot template is not pickable or dossier-unlocked), so locking that one affordance fully locks the form. Below the unlock level the button is greyed and non-clickable (`SetEnabled(false)` blocks the click, the `.wm-locked` USS class supplies the look since the game theme is not relied on to style `:disabled` for `.text-button`) and `DoSwap` re-checks defensively.
- **SSR weapons are granted at the unlock level, then boosted in code** (`SsrImprintSystem`). A `Feature.Weapon` unlock adds a `special_weapon` anyone can equip to the shared inventory (`AffinitySystem.ApplyUnlocks`, `OwnedItems.AddItem` idempotent on `GetInstanceCount`). The owner-only combat bonus (extra damage, extra shots, on-hit effects like +1 freeze) and the green-for-owner / greyed-otherwise "<Doll> Imprint Boost" tooltip section live in `SsrImprintSystem`, one `Entry` per weapon. Base stats stay authored in KDL. Only the owner overrides live in code.

Two non-obvious constraints shaped `SsrImprintSystem`, both expensive to rediscover:

- **Owner identity in a mission comes from the speaker, not tags.** A combat entity DROPS its `EntityTemplate` tags but keeps its `SpeakerTemplate`, so "is the owning doll firing this?" must be read via `Entity.GetSpeakerTemplate()` (wrapped as `Affinity.CharacterTag(Entity)`), never `HasAnyOfTheseTagsAnywhere("wmgfl_…")` or the tags on `Skill.GetActor()` (that actor exists but carries none of the doll's template tags).
- **The boost never leaves the shared template mutated at rest.** Damage is added per shot to the `DamageInfo` in a `Skill.FillDamageInfo` postfix, so `WeaponTemplate.Damage` stays at its authored base and a buffed value can never leak into another wielder's loadout/preview UI. Extra shots have no per-hit lever, so `SkillTemplate.Repetitions` must be set on the shared template before the shot loop reads it, and that loop reads asynchronously AFTER `Use` returns, so it cannot be restored in a postfix (doing so fires a single shot). The tooltip sets the viewer's numbers in the Pre hook and restores base in the Post hook so the resting template is never left buffed.

The badge hover popover is the game's **native tooltip**, via the SDK `Jiangyu.Game.Ui.Components.Tooltip` wrapper (`UIManager.ShowTooltip`, sticks to the mouse). `AffinitySystem` builds it from `Unlocks.RowsFor` on each hover (`Tooltip.OnHover`): a subheading then one row per level, every reached level (current and below) in `Positive` and every level not yet reached in `Disabled`. Each label is the zero-padded level (`01`, `02`, ...) followed by that level's reward text, or a `·` for a level that grants nothing.

## Campaign map (GFL1 reskin)

`CampaignMapSystem` (`code/Systems/CampaignMap/`) reskins the mission-select board into the GFL1 look: each node becomes a circular spot carrying its state, and a Voymastina chibi idles on the selected node and walks to a newly picked one. The expensive-to-derive MENACE internals:

- The board is `Menace.UI.Strategy.MissionSelectUIScreen` (active even though the scene tag is `MissionPreparation`). Its `MissionPoisContainer` (named `MissionPois`, spanning the 1280x720 panel) and the `MissionPoi` nodes sit on the screen's own `UIDocument` but OUTSIDE the subtree `GetActiveScreen().GetRootElement()` returns (that returns only the nav + `MissionWindow` subtree; the board is a sibling of it on the same document). So the default `bridge command ui` probe cannot see them (it walks only `GetRootElement()`), `{allPanels:true}` (which walks each full `UIDocument` root) surfaces them, and the system reaches nodes via Harmony postfixes on `MissionPoi.SetMission`/`SetSelected` and `MissionPoisContainer.Init`/`SetSelectedMission` (`__instance`), never screen-tree injection.
- **Node state is `Mission.GetStatus()`** (`MissionStatus { Playable, Locked, Played, Unplayable }`, `Played` = cleared), reached via `poi.TryCast<MissionPoi>().GetMission().GetStatus()`. The `mission_icon_played` SPRITE is NOT completion, it is the "next-to-play" marker on the selected playable node. Final/boss = a `FinalAssetIcon` child (`enemy_asset_icon_final`). So final to red, Played to blue, else the hollow ring.
- Each `MissionPoi` has `MissionIcon` (the glyph, tinted per state), `MissionIconBorder` and `InfoBorder` (both light up on the selected node, so detect selection via `InfoBorder`), `IconPos`, `InfoBG`/`MissionName`, and `Assets`/`OperationAssetIcon`. The reskin keeps the glyph and asset icons, sets `MissionIcon` to `display:none` (so the chibi's foot anchor must use a still-visible element like `IconPos`, not the hidden icon), and hides `MissionIconBorder` (the box over the node) on both SetMission and SetSelected since the game re-shows it.
- Chibi frames ship on a **power-of-two square (256x256) canvas** so Unity's non-power-of-two import rescale never distorts the aspect (which made the running frames look skinnier). All frames of both animations share one scale and are placed bottom-centre so the character is a consistent size with aligned feet. The walk loop tracks the chibi's live foot position and always heads to the latest selected node, so rapid selection changes retarget in flight rather than stranding it.
- The chibi frames + spot sprites are grouped into a single `Icons__campaign.bundle` via the Jiangyu bundler convention: textures in a **subfolder of `Assets/UI/Icons/`** ship as one bundle keyed `<Icons>__<subfolder>` (a texture directly in `Icons/` still gets its own leaf-named bundle). The bundle file name does not matter to loads: assets resolve by leaf name (`wait_000`, `spot_complete`).

## Loader + CLI version coupling

The deployed `Jiangyu.Loader.dll` (under `~/.steam/.../Menace/Mods/`) and the Jiangyu CLI used by `mise compile` must come from the same Jiangyu commit. A stale loader silently misses pipeline changes — e.g. the addition-prefab build was folded into the mesh-replacement Unity batchmode pass at commit `bfd02ee`, so a pre-`bfd02ee` loader fails to load addition prefabs from the new combined bundle and the in-game models render as fallback.

Deploy: build the loader release dll and copy to the Mods dir:

```bash
cd ~/dev/github.com/antistrategie/jiangyu
dotnet build src/Jiangyu.Loader/Jiangyu.Loader.csproj -c Release
cp src/Jiangyu.Loader/bin/Release/net6.0/Jiangyu.Loader.dll ~/.steam/steam/steamapps/common/Menace/Mods/Jiangyu.Loader.dll
```

Symptom of mismatch: `MelonLoader/Latest.log` shows `Template patch '…': AssetReference 'X/Y/main': no asset of type GameObject found in the mod bundle catalog or the live game-asset registry` even though the bundle exists in the Mods dir.

## Editor-script drift check

`jiangyu compile` checks whether the per-mod `unity/Assets/Jiangyu/Editor/*.cs` files match the embedded templates in the Jiangyu CLI build. When they drift (e.g. you've upgraded Jiangyu but haven't run `jiangyu unity sync`), compile emits a warning naming the drifted files. The compile still proceeds, but bundles may build wrong if the new CLI passes args (e.g. `-runPrefabs true`) that the stale Editor script doesn't understand.

Run `jiangyu unity sync` from the repo root to refresh the managed scripts. The CLI command writes only to `unity/Assets/Jiangyu/Editor/` and `.gitignore` — modder content (under `Assets/Prefabs/`, etc.) is untouched.

## Conventions

- **Mod ID prefix `wmgfl_`** on collision-prone clone IDs: SoundBank names (`wmgfl_tactical_barks_<char>_va`, `wmgfl_weapons_ar_addition_bank`, `wmgfl_weapons_rf_addition_bank`), character TagTemplate IDs (`wmgfl_cheyanne`, `wmgfl_voymastina`), SpeakerTemplate IDs (`wmgfl_cheyanne_speaker`). Already-namespaced IDs (`armor.cheyanne_default`, `weapon.voymastina_ak15`, `Cheyanne/arrival_cheyanne` etc.) skip the prefix because the character segment carries equivalent namespacing. Jiangyu-contract tags (`jy_weapon_restricted`, `jy_vehicle_restricted`, `jy_no_sell`) skip it because they're a cross-mod protocol.
- British English in code, comments, docs (analyse, colour, organisation).
- No em dashes. No semicolons in prose, comments, or string literals. Use periods, commas, colons.
- Docs describe the current working state only. No past-tense framing ("used to", "previously", "earlier attempts"). No future-tense framing ("not yet", "TODO", "in progress"). If something doesn't work, fix it or leave it out.
- Run `mise format` (or `jiangyu templates format`) before committing template edits. It rewrites every `templates/*.kdl` through the same parse → validate → normalise → serialise pipeline Studio uses on save, so diffs only show real authoring changes — not the kind of churn that creeps in from hand-edits (redundant `composite=` attributes, stale shorthand forms, blank-line drift). `mise format --check` is the CI-equivalent: exits non-zero if anything would change.
- Bundle build target is `StandaloneWindows64` so the bundle ships D3D11 shader variants matching MENACE's Proton/DXVK runtime.
- gltfast is pinned in `unity/Packages/manifest.json` to a version known to import multi-primitive skinned meshes without the bone-weights Jobs race.
- The dumped `Menace_character.shader` stub in `unity/Assets/Imported/<reference soldier>/Shader/` is essential. The Editor renders it magenta, but bundled materials carry its shader name and `Jiangyu.Loader.dll` rebinds the name to MENACE's vanilla shader at load time.
