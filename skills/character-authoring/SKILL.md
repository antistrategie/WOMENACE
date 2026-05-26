---
name: character-authoring
description: Author KDL templates for a new playable squad leader in MENACE. Use when adding a new character (tag, speaker, entity, perk tree, armor, squad leader), figuring out the right vanilla parent to clone from, or debugging tag-restriction (OnlyEquipableBy) gating. Sound and 3D-model authoring have their own skills.
---

# Character authoring

## What this covers

The KDL "spine" of a new character: the TagTemplate / SpeakerTemplate / EntityTemplate / UnitLeaderTemplate / PerkTreeTemplate / ArmorTemplate clones that hook a name + portraits + stats + perks + equipment into MENACE's squad-leader system. PMX/Unity model side lives in [`pmx-to-menace`](../pmx-to-menace/SKILL.md), voice + audio in [`voice-pipeline`](../voice-pipeline/SKILL.md), weapon model + audio in [`weapon-pipeline`](../weapon-pipeline/SKILL.md).

## Pick a parent character to clone from

Every clone needs a vanilla MENACE leader as its starting point. The parent dictates:

- **Class archetype** — what kind of unit (assault, sniper, heavy, medic, etc.). Their default perks, stats, and weapon class flow through.
- **SpeakerTemplate role name** — different speakers use different role names in their ConversationTemplate clones. JeanSy templates' speaker role is "JeanSy"; Carda's combat templates use "SL"; arrival templates use "Carda". See [voice-pipeline](../voice-pipeline/SKILL.md) for the per-template-role mapping.
- **Conversation namespace** — JeanSy's barks live at `JeanSy/<event>`; Carda's at `Carda_Early/<event>`. The voice pipeline relies on this.

Find candidates via Jiangyu CLI:

```bash
dotnet ../jiangyu/src/Jiangyu.Cli/bin/Debug/net10.0/jiangyu.dll templates inspect --type UnitLeaderTemplate --name squad_leader.<name>
```

For an existing-archetype clone (e.g. another sniper from carda), pick the vanilla leader with the closest class. The clone inherits everything; you override only what differs.

## File layout

Per character:

```
templates/<character>/
├── squad_leader.kdl   tag + speaker + entity + unit-leader templates
├── armor.kdl           one ArmorTemplate per visual variant
├── perk_tree.kdl       PerkTreeTemplate clone with the character's perk grid
└── voice/              (sound + conversation, see voice-pipeline skill)
    ├── soundbank.kdl
    ├── arrivals.kdl
    └── ...
```

Sprite/texture assets live separately under `assets/additions/sprites/<character>/` and `assets/additions/textures/<character>/`. KDL refs use `asset="<character>/<basename>"` regardless of source category.

## squad_leader.kdl (the spine)

Five clones in one file:

```kdl
clone "TagTemplate" from="unique" id="wmgfl_<character>"

clone "SpeakerTemplate" from="<parent_speaker>" id="wmgfl_<character>_speaker" { ... }

clone "EntityTemplate" from="player_squad.<parent>" id="player_squad.<character>" {
    append "Tags" "wmgfl_<character>"
    append "Tags" "armor_restricted"
    ...
    clear "Items"
    append "Items" "armor.<character>_default"
    append "Items" "weapon.<character>_<weapon_name>"   // optional
}

clone "UnitLeaderTemplate" from="squad_leader.<parent>" id="squad_leader.<character>" {
    set "InfantryUnitTemplate" "player_squad.<character>"
    set "SpeakerTemplate" "wmgfl_<character>_speaker"
    set "InitialAttributes" index=0 ...   // 7 stats
    set "InitialPerk" "perk.<starting_perk>"
    set "PerkTrees" index=0 "perk_tree.<character>"
    set "Slot" asset="<character>/slot"
    set "BadgeMini" asset="<character>/badge_mini"
    ...
    set "HiringSelectBarkSound" {
        set "bankId" "wmgfl_tactical_barks_<character>_va"
        set "itemId" "<filename_of_chosen_clip>"   // see voice-pipeline
    }
    ...
}
```

The five clones depend on each other by ID:

- `UnitLeaderTemplate.InfantryUnitTemplate` → `player_squad.<character>` (the EntityTemplate)
- `UnitLeaderTemplate.SpeakerTemplate` → `<character>_speaker` (the SpeakerTemplate)
- `UnitLeaderTemplate.PerkTrees[0]` → `perk_tree.<character>` (the PerkTreeTemplate, in a different file)
- `EntityTemplate.Items[]` → `armor.<character>_*` and optionally `weapon.<character>_*`
- `EntityTemplate.Tags[]` includes the `<character>` TagTemplate

## Tag and equipment restriction

Tags gate which items each unit can equip. Two tags do the work:

- **`<character>`** — unique per character. The character's `EntityTemplate.Tags` adds it; their armor/weapon `OnlyEquipableBy` references it.
- **`armor_restricted` / `weapon_restricted`** — slot-restriction tags. When the unit has one of these, Jiangyu's `InventoryFilterPatch` Harmony hook filters the loadout-UI dropdown so only items with a matching `OnlyEquipableBy` show.

Concrete: a unit tagged `cheyanne` + `armor_restricted` opens her armor slot. The filter runs because `armor_restricted` is on her. For each candidate item, it checks `OnlyEquipableBy ∩ unit.Tags`. `armor.cheyanne_default` has `OnlyEquipableBy=[cheyanne]`, which intersects, so it shows. `armor.voymastina_default` has `OnlyEquipableBy=[voymastina]`, no intersection, hidden.

**The filter only runs in the strategy-mode loadout dropdown** (`UnitWindowEquipment.UpdateEquipmentAlternatives` → `SortedFilteredItemList.GetSortedAndFilteredItems`). Other UI paths (blackmarket, debug menus) bypass it; `OnlyEquipableBy` is documentation-only there.

## Sprite slots

Several portrait/badge fields exist on both `UnitLeaderTemplate` and `EntityTemplate`. MENACE's UI is inconsistent about which one it reads. For an Infantry leader clone (`UnitActorType == 0`):

- `EntityTemplate.Badge` / `BadgeWhite` — in-mission badge above units and the turn-bar squad list (via `InfantryUnitTemplate.Badge`).
- `UnitLeaderTemplate.BadgeMini` — mission-prep tactical preview small badge. Read in `MissionPrepDeployedEntity.Init`.
- `UnitLeaderTemplate.Slot` / `SlotInactive` — turn-bar portrait.
- `UnitLeaderTemplate.BadgeUnitWindow` — unit-info window header (read in `UnitLeaderUIExtensions.InitUnitWindowHeader`).
- `UnitLeaderTemplate.BigBadge` — hiring info-panel banner.
- `UnitLeaderTemplate.SlotBadge` / `BadgeDragged` — hire-slot + drag visuals.
- `UnitLeaderTemplate.Badge` / `BadgeWhite` — dead for Infantry. Don't bother setting.
- `EntityTemplate.PreviewMapIcon` — also dead in tested screens.

The full bestiary lives in [`../../AGENTS.md`](../../AGENTS.md) under "Sprite slots".

## armor.kdl

One `ArmorTemplate` clone per visual variant:

```kdl
clone "ArmorTemplate" from="armor.player_fatigues" id="armor.<character>_<variant>" {
    set "Title" { set "m_DefaultTranslation" "<armor name>" }
    set "ShortName" { set "m_DefaultTranslation" "<short name>" }
    set "Description" { set "m_DefaultTranslation" "<flavour>" }
    set "Icon" asset="<character>/armor/<variant>/Icon"
    set "IconEquipment" asset="<character>/armor/<variant>/IconEquipment"
    set "IconSkillBar" asset="<character>/armor/<variant>/IconSkillBar"
    clear "MaleModels"
    append "MaleModels" asset="<character>/<variant>/main"
    clear "FemaleModels"
    append "FemaleModels" asset="<character>/<variant>/main"
    set "SquadLeaderMode" enum="SquadLeaderModelMode" "SameAsOthers"
    append "OnlyEquipableBy" "wmgfl_<character>"
}
```

The model `asset=` ref points at the per-variant subdir under `unity/Assets/Prefabs/<character>/<variant>/main.prefab`. The PMX-to-MENACE pipeline produces that prefab (see [`../pmx-to-menace/SKILL.md`](../pmx-to-menace/SKILL.md)).

`SquadLeaderMode = SameAsOthers` means everyone in the squad wearing this armor uses the same 3D model (the character's). Other options exist on the enum but are rarely useful for character-locked armor.

## perk_tree.kdl

```kdl
clone "PerkTreeTemplate" from="perk_tree.<parent>" id="perk_tree.<character>" {
    clear "Perks"
    append "Perks" { set "Skill" "perk.<perk_id>"; set "Tier" 1 }
    append "Perks" { set "Skill" "perk.<perk_id>"; set "Tier" 2 }
    ...
}
```

Tiers gate when a perk unlocks during levelling. Vanilla characters mostly have 4-5 tier-1, 4-5 tier-2, 4 tier-3, 2 tier-4. Match that distribution or the perk panel UI looks sparse.

Verify each perk exists before referencing — Jiangyu rejects unknown perk IDs at compile:

```bash
dotnet ../jiangyu/src/Jiangyu.Cli/bin/Debug/net10.0/jiangyu.dll templates search "perk.<name>"
```

## Conventions

- **British English** in titles, descriptions, comments (analyse, colour). Only use American spelling for external library APIs.
- **No em dashes**, **no semicolons** in prose / Title / Description / KDL string literals. Periods, commas, colons only.
- **`mise run format`** before committing. Rewrites KDL through Jiangyu's parse → validate → normalise → serialise pipeline so diffs only show real authoring changes. `mise run format --check` exits non-zero in CI when files would change.
- **KDL composite-over-dotted** — never `set "Type.field" v`. Always `set "Type" composite="X" { set "field" v }` or the bare-child-block form for monomorphic destinations.
- **`wmgfl_` prefix** on collision-prone clone IDs: SoundBank names, character Tags (`wmgfl_cheyanne`), SpeakerTemplate IDs (`wmgfl_cheyanne_speaker`). Already-namespaced IDs like `armor.cheyanne_default` skip it. See `AGENTS.md` for the full rule and rationale.

## What inherits, what you override

A `clone` deep-copies the parent's typed state, then applies the patches in your block. Anything you don't `set` keeps the parent's value. This matters because:

- `Triggers`, `Condition`, `EventSettings`, `Priority`, `PlayChance`, `Repeatable`, `Repetitions` on cloned ConversationTemplates flow through. You almost never need to set these.
- The parent's other Roles (the ones whose `m_SerializedRequirements` you don't patch) flow through. Voymastina's KDL pattern of `set "Roles" index=N` modifies one role; the others stay parent-defined.
- The parent's `Nodes` (m_SerializedNodes) get REPLACED if you do `set "Nodes" { ... }` (not `append`). The voice-pipeline skill explains the implication.

## Common shape mistakes

- **Cloning the wrong parent class** — e.g. cloning from `specialweapon.X` if you don't want the unit to consume the specialweapon slot. For a sniper-style weapon in the normal slot, pick `weapon.generic_battle_rifle_tier1_crowbar_marksman` or similar.
- **Forgetting `append "Tags" "wmgfl_<character>"`** on the EntityTemplate. The armor restriction silently fails (no match in `OnlyEquipableBy ∩ unit.Tags`).
- **Forgetting `append "Tags" "armor_restricted"`** — the filter doesn't activate and the unit sees ALL armor.
- **Wrong RoleGuid in cloned ConversationTemplates** — must match the role NAME in the actual parent template, which differs per-template (see [voice-pipeline](../voice-pipeline/SKILL.md)).

## Cross-references

- [`pmx-to-menace`](../pmx-to-menace/SKILL.md) for the 3D mesh / prefab side.
- [`voice-pipeline`](../voice-pipeline/SKILL.md) for SoundBank + ConversationTemplate clone authoring.
- [`weapon-pipeline`](../weapon-pipeline/SKILL.md) for adding a character-specific weapon model + custom gunshot audio.
- [`../../AGENTS.md`](../../AGENTS.md) for the cross-cutting MENACE internals (sprite-slot bestiary, audio-bank routing model, etc).
