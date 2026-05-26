# WOMENACE

A Girls' Frontline mod for MENACE, built with [Jiangyu](https://github.com/antistrategie/jiangyu). Characters are authored from PMX (MMD) sources, converted to Unity-native prefabs via a Blender + Unity pipeline, and shipped to MENACE through Jiangyu's KDL template system as squad-leader clones, armor clones, entity clones, and perk trees.

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
├── templates/              KDL template patches and clones, one file per character
├── scripts/                Authoring + asset pipelines (Python)
│   ├── pmx_to_menace.py    PMX → glTF (humanoid characters)
│   ├── bake_weapon.py      OBJ → glTF (weapons + attach-point empties)
│   ├── render_weapon.py    glTF → transparent PNG (icon prep)
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

1. **KDL spine** — TagTemplate, SpeakerTemplate, EntityTemplate, UnitLeaderTemplate, PerkTreeTemplate, ArmorTemplate clones at `templates/<character>/`. See [`skills/character-authoring/SKILL.md`](skills/character-authoring/SKILL.md).
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

- British English in code, comments, docs (analyse, colour, organisation).
- No em dashes. No semicolons in prose, comments, or string literals. Use periods, commas, colons.
- Docs describe the current working state only. No past-tense framing ("used to", "previously", "earlier attempts"). No future-tense framing ("not yet", "TODO", "in progress"). If something doesn't work, fix it or leave it out.
- Run `mise format` (or `jiangyu templates format`) before committing template edits. It rewrites every `templates/*.kdl` through the same parse → validate → normalise → serialise pipeline Studio uses on save, so diffs only show real authoring changes — not the kind of churn that creeps in from hand-edits (redundant `composite=` attributes, stale shorthand forms, blank-line drift). `mise format --check` is the CI-equivalent: exits non-zero if anything would change.
- Bundle build target is `StandaloneWindows64` so the bundle ships D3D11 shader variants matching MENACE's Proton/DXVK runtime.
- gltfast is pinned in `unity/Packages/manifest.json` to a version known to import multi-primitive skinned meshes without the bone-weights Jobs race.
- The dumped `Menace_character.shader` stub in `unity/Assets/Imported/<reference soldier>/Shader/` is essential. The Editor renders it magenta, but bundled materials carry its shader name and `Jiangyu.Loader.dll` rebinds the name to MENACE's vanilla shader at load time.
