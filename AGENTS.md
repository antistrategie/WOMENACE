# WOMENACE

A Girls' Frontline mod for MENACE, built with [Jiangyu](https://github.com/antistrategie/jiangyu). Characters are authored from PMX (MMD) sources, converted to Unity-native prefabs via a Blender + Unity pipeline, and shipped to MENACE through Jiangyu's KDL template system as squad-leader clones, armor clones, entity clones, and perk trees.

This file is the agent-onboarding briefing. For SDK-level concepts (KDL templates, clone/patch grammar, addition vs replacement, asset bundle pipeline, loader hooks), read Jiangyu's own [AGENTS.md](https://github.com/antistrategie/jiangyu/blob/main/AGENTS.md) first. For the PMX-to-Unity pipeline (running the converter, config schema, stage-by-stage detail, gotchas), read [`skills/pmx-to-menace/SKILL.md`](skills/pmx-to-menace/SKILL.md).

## What belongs in this file

Things an agent can't easily find by reading the code. If the answer lives in `templates/`, `mise.toml`, Jiangyu's source, or anywhere else in the repo, leave it out. If finding it requires disassembling MENACE, reading Jiangyu loader internals, or trial-and-error, write it down here so the next agent doesn't repeat the work. Expensive-to-derive findings (cached disassembly results, behaviour quirks) count too. Jiangyu's AGENTS.md owns SDK-level concepts. Cross-reference, don't duplicate.

## Layout

```
WOMENACE/
├── jiangyu.json            mod manifest (name + Jiangyu version pin)
├── mise.toml               task runner: compile, deploy, unity-init, unity-open
├── templates/              KDL template patches and clones, one file per character
├── scripts/                Blender pipeline (Python)
│   ├── pmx_to_menace.py    PMX → glTF converter
│   └── .config/            per-character pipeline configs (gitignored)
├── skills/                 Claude Code skills used during authoring
│   └── pmx-to-menace/
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

Three stages, end-to-end via `mise compile && mise deploy`:

1. **PMX → glTF** (Blender headless). `scripts/pmx_to_menace.py` converts an MMD PMX into a humanoid-renamed, T-pose-calibrated glTF under `unity/Assets/Authored/<character>/`.
2. **glTF → Unity prefab** (Unity Editor batchmode). `unity/Assets/Jiangyu/Editor/BakeHumanoid.cs` consumes the glTF + a reference vanilla soldier prefab and writes `unity/Assets/Prefabs/<character>/main.prefab` + per-source-texture baked materials + a humanoid avatar.
3. **Mod → MENACE** (Jiangyu CLI). `mise compile` parses `templates/`, builds each `Assets/Prefabs/<character>/main.prefab` into its own asset bundle, writes `compiled/`. `mise deploy` copies into `~/.steam/.../Menace/Mods/WOMENACE/`. At MENACE startup, Jiangyu's loader rebinds bundled materials' shader names to MENACE's loaded shader catalogue via `Shader.Find`.

Stage 1 + 2 commands and config schema live in [`skills/pmx-to-menace/SKILL.md`](skills/pmx-to-menace/SKILL.md).

## Asset paths

Jiangyu picks the Unity import type based on the directory under `assets/additions/`:

- `sprites/<character>/` becomes `Sprite` assets (Badge, SlotBadge, BadgeUnitWindow, etc.).
- `textures/<character>/` becomes `Texture2D` assets (StandLookLeftImage, BigBackground, etc.).

KDL references use `asset="<character>/<basename>"` regardless of source directory. Character prefabs ship in a separate per-character bundle built from `unity/Assets/Prefabs/<character>/main.prefab` and are referenced the same way (`asset="<character>/main"`).

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

## Conventions

- British English in code, comments, docs (analyse, colour, organisation).
- No em dashes. No semicolons in prose, comments, or string literals. Use periods, commas, colons.
- Docs describe the current working state only. No past-tense framing ("used to", "previously", "earlier attempts"). No future-tense framing ("not yet", "TODO", "in progress"). If something doesn't work, fix it or leave it out.
- Run `mise format` (or `jiangyu templates format`) before committing template edits. It rewrites every `templates/*.kdl` through the same parse → validate → normalise → serialise pipeline Studio uses on save, so diffs only show real authoring changes — not the kind of churn that creeps in from hand-edits (redundant `composite=` attributes, stale shorthand forms, blank-line drift). `mise format --check` is the CI-equivalent: exits non-zero if anything would change.
- Bundle build target is `StandaloneWindows64` so the bundle ships D3D11 shader variants matching MENACE's Proton/DXVK runtime.
- gltfast is pinned in `unity/Packages/manifest.json` to a version known to import multi-primitive skinned meshes without the bone-weights Jobs race.
- The dumped `Menace_character.shader` stub in `unity/Assets/Imported/<reference soldier>/Shader/` is essential. The Editor renders it magenta, but bundled materials carry its shader name and `Jiangyu.Loader.dll` rebinds the name to MENACE's vanilla shader at load time.
