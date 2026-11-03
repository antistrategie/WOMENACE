---
name: character-authoring
description: Author KDL templates for a new playable squad leader in MENACE. Use when adding a new character (tag, speaker, entity, perk tree, armor, squad leader), figuring out the right vanilla parent to clone from, or debugging tag-restriction (OnlyEquipableBy) gating. Sound and 3D-model authoring have their own skills.
---

# Character authoring

## What this covers

The KDL "spine" of a new character: the TagTemplate / SpeakerTemplate / EntityTemplate / UnitLeaderTemplate / PerkTreeTemplate / ArmorTemplate clones that hook a name + portraits + stats + perks + equipment into MENACE's squad-leader system. PMX/Unity model side lives in [`pmx-to-menace`](../pmx-to-menace/SKILL.md), voice + audio in [`voice-pipeline`](../voice-pipeline/SKILL.md), weapon model + audio in [`weapon-pipeline`](../weapon-pipeline/SKILL.md).

## Pick a parent character to clone from

Every clone needs a vanilla MENACE leader as its starting point. The parent dictates:

- **Class archetype**: what kind of unit (assault, sniper, heavy, medic, etc.). Their default perks, stats, and weapon class flow through.
- **SpeakerTemplate role name**: different speakers use different role names in their ConversationTemplate clones. JeanSy templates' speaker role is "JeanSy". Carda's combat templates use "SL", and arrival templates use "Carda". See [voice-pipeline](../voice-pipeline/SKILL.md) for the per-template-role mapping.
- **Conversation namespace**: JeanSy's barks live at `JeanSy/<event>`, and Carda's at `Carda_Early/<event>`. The voice pipeline relies on this.

Find candidates via Jiangyu CLI:

```bash
dotnet ../jiangyu/src/Jiangyu.Cli/bin/Debug/net10.0/jiangyu.dll templates inspect --type UnitLeaderTemplate --name squad_leader.<name>
```

For an existing-archetype clone (e.g. another sniper from carda), pick the vanilla leader with the closest class. The clone inherits everything, and you override only what differs.

## File layout

Per character:

```
templates/dolls/<character>/
├── squad_leader.kdl   tag + speaker + entity + unit-leader templates
├── armor.kdl           one ArmorTemplate per visual variant
├── perk_tree.kdl       PerkTreeTemplate clone with the character's perk grid
├── perks/              the character's own perks, one file each (optional)
└── voice/              (sound + conversation, see voice-pipeline skill)
    ├── soundbank.kdl
    ├── arrivals.kdl
    └── ...
```

A perk that several characters share lives in `templates/perks/<perk>.kdl` instead. Every KDL file starts with its first `clone` or `patch` node, never with a comment: the formatter drops the first node of a comment-led file.

Sprite/texture assets live separately under `assets/additions/sprites/<character>/` and `assets/additions/textures/<character>/`. KDL refs use `asset="<character>/<basename>"` regardless of source category.

## squad_leader.kdl (the spine)

Five clones in one file:

```kdl
clone "TagTemplate" from="unique" id="wmgfl_<character>"

clone "SpeakerTemplate" from="<parent_speaker>" id="wmgfl_<character>_speaker" { ... }

clone "EntityTemplate" from="player_squad.<parent>" id="player_squad.<character>" {
    append "Tags" "wmgfl_<character>"
    ...
    clear "Items"
    append "Items" "armor.player_fatigues"
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
    set "FactionBackground" asset="factions/background_<faction>_1180x1040"
    set "BigBackground" asset="factions/background_<faction>_972x906"
    set "SlotFactionBackground" asset="factions/background_<faction>_162x162"
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
- `EntityTemplate.Items[]` → vanilla starting armour (`armor.player_fatigues`) and optionally `weapon.<character>_*`
- `EntityTemplate.Tags[]` includes the `wmgfl_<character>` TagTemplate. The transmog system reads this
  tag off the spawning EntityTemplate to decide whose outfit to render, and weapon `OnlyEquipableBy`
  gating matches against it.

## Tag and equipment restriction

Tags gate which items each unit can equip.

- **`wmgfl_<character>`**: unique per character. The character's `EntityTemplate.Tags` adds it, and their weapon `OnlyEquipableBy` references it, so the weapon shows only in that character's dropdown (vanilla `OnlyEquipableBy` filtering hides it from everyone else).
- **`jy_weapon_restricted` / `jy_special_restricted`**: slot-restriction tags on the EntityTemplate for a doll locked TO her own gear (a melee-only doll carries both). When the unit has one, Jiangyu's `InventoryFilterPatch` Harmony hook filters that slot's loadout-UI dropdown to items with a matching `OnlyEquipableBy`. Without it the character equips vanilla items in that slot too.
- **`wmgfl_transmog`**: carried by no unit. An outfit `ArmorTemplate` whose `OnlyEquipableBy` names only this tag never appears in any equip dropdown: outfits are cosmetic carriers for the transmog picker, never equippable armour. Dolls equip vanilla armour for stats.

**The `InventoryFilterPatch` filter only runs in the strategy-mode loadout dropdown** (`UnitWindowEquipment.UpdateEquipmentAlternatives` → `SortedFilteredItemList.GetSortedAndFilteredItems`). Other UI paths (blackmarket, debug menus) bypass it, so `OnlyEquipableBy` is documentation-only there.

## Sprite slots

Several portrait/badge fields exist on both `UnitLeaderTemplate` and `EntityTemplate`. MENACE's UI is inconsistent about which one it reads. For an Infantry leader clone (`UnitActorType == 0`):

- `EntityTemplate.Badge` / `BadgeWhite`: in-mission badge above units and the turn-bar squad list (via `InfantryUnitTemplate.Badge`).
- `UnitLeaderTemplate.BadgeMini`: mission-prep tactical preview small badge. Read in `MissionPrepDeployedEntity.Init`.
- `UnitLeaderTemplate.Slot` / `SlotInactive`: turn-bar portrait.
- `UnitLeaderTemplate.BadgeUnitWindow`: unit-info window header (read in `UnitLeaderUIExtensions.InitUnitWindowHeader`).
- `UnitLeaderTemplate.BigBadge`: hiring info-panel banner.
- `UnitLeaderTemplate.SlotBadge` / `BadgeDragged`: hire-slot + drag visuals.
- `UnitLeaderTemplate.Badge` / `BadgeWhite`: dead for Infantry. Don't bother setting.
- `EntityTemplate.PreviewMapIcon`: also dead in tested screens.
- `UnitLeaderTemplate.FactionBackground` (1180x1040), `BigBackground` (972x906) and `SlotFactionBackground` (162x162): the faction disc behind the portraits. A clone inherits its parent's (`sy` gives the pirate skull), so every doll sets her own faction's set from `factions/background_<faction>_<size>`.

The full bestiary lives in [`../../AGENTS.md`](../../AGENTS.md) under "Sprite slots".

## Making the sprites

`scripts/doll/make_doll_sprites.py` builds a doll's sprites from GFL2 client art, copying how the hand-made roster sprites were cut. Export the art from the CN client with AssetStudio (see `AGENTS.md` "Sourcing from the GFL2 client"): `Avatar_Whole_<Name>SSR` is the full-body art (`SSR01`, `SSR02` and so on are her outfits) and `Avatar_Head_<Name>SSR` is the head icon used to find the head.

- `--whole --head --span` writes `bark`, `slot`, `slot_inactive` and the three `stand_look_*` textures. `--span` is her canon span from the height policy, which picks her size class in the stand image.
- `--outfit <folder> --whole <that outfit's art>` writes the outfit's `Icon`, `IconEquipment` and `IconSkillBar` under `sprites/<character>/armor/<folder>/`.
- `--emblem <image>` writes the five emblem sprites. `--cut-background` removes a light paper or table backdrop around an outlined emblem. `badge_dragged` carries a small copy of `bark`, so run the portraits first.

Placement is drawn from the spread measured across the roster, seeded by the doll's name, so reruns are stable and the set keeps the roster's slightly uneven hand placement. Two things need a look on every doll:

- **The head box.** `Avatar_Head` is a render of the 3D model, not a crop of the art, so the match lands near the face but can sit off on a tilted pose, and it usually fails on outfit art with scenery. On a good box the bark crop shows the whole head with the chin clear of the bottom edge. Pass `--head-box x,y,size` (source pixels, hair top to just below the chin, chin about three quarters down) to correct it.
- **Anything raised above the head.** The stand image lines up the top of the figure across the roster. A hand, lamp or weapon held high would push the doll down, so pass `--figure-top <source y>` at the top of her head: she is placed from there and the raised part fades out above.

`scripts/doll/make_faction_backgrounds.py` builds the three faction backgrounds per faction from GFL2's `Img_CampLogo_*` faction logos (`data/faction_logos/`, faction names in the `CharacterBelongData` table). Its table maps each faction to a logo and a colour, with an optional logo scale and sideways shift for a narrow logo the portraits would hide. A doll in a faction without a set needs a new table row.

## armor.kdl (transmog outfits)

One `ArmorTemplate` clone per outfit. Outfits carry the character's look (model, icons, name) for the transmog picker. They hold no combat stats and no unit can equip them. The character wears vanilla armour for stats and always renders her selected outfit:

```kdl
clone "ArmorTemplate" from="armor.player_fatigues" id="armor.<character>_<variant>" {
    set "Title" { set "m_DefaultTranslation" "<outfit name>" }
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
    append "OnlyEquipableBy" "wmgfl_transmog"
    append "Tags" "jy_no_sell"
}
```

The model `asset=` ref points at the per-variant subdir under `unity/Assets/Prefabs/<character>/<variant>/main.prefab`. The PMX-to-MENACE pipeline produces that prefab (see [`../pmx-to-menace/SKILL.md`](../pmx-to-menace/SKILL.md)).

Code-side registration, both in `code/`:

- `Transmog.DefaultFor` (`Systems/Transmog/Transmog.cs`) derives `armor.<character>_default` from the `wmgfl_<character>` tag and probes the template registry for it. A character is a transmog character exactly when that template exists, so the default outfit needs no code-side registration, but its id MUST follow the `armor.<character>_default` convention or the character renders whatever armour she wears.
- Extra outfits (skins) are `Unlocks` entries (`Feature.Skins`) with the affinity level that unlocks them, and the picker lists them greyed until then.

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

Verify each perk exists before referencing, as Jiangyu rejects unknown perk IDs at compile:

```bash
dotnet ../jiangyu/src/Jiangyu.Cli/bin/Debug/net10.0/jiangyu.dll templates search "perk.<name>"
```

## Conventions

- **British English** in titles, descriptions, comments (analyse, colour). Only use American spelling for external library APIs.
- **No em dashes**, **no semicolons** in prose / Title / Description / KDL string literals. Periods, commas, colons only.
- **`mise run format`** before committing. Rewrites KDL through Jiangyu's parse → validate → normalise → serialise pipeline so diffs only show real authoring changes. `mise run format --check` exits non-zero in CI when files would change.
- **KDL composite-over-dotted**: never `set "Type.field" v`. Always `set "Type" composite="X" { set "field" v }` or the bare-child-block form for monomorphic destinations.
- **`wmgfl_` prefix** on collision-prone clone IDs: SoundBank names, character Tags (`wmgfl_<character>`), SpeakerTemplate IDs (`wmgfl_<character>_speaker`). Already-namespaced IDs like `armor.<character>_default` skip it. See `AGENTS.md` for the full rule and rationale.

## What inherits, what you override

A `clone` deep-copies the parent's typed state, then applies the patches in your block. Anything you don't `set` keeps the parent's value. This matters because:

- `Triggers`, `Condition`, `EventSettings`, `Priority`, `PlayChance`, `Repeatable`, `Repetitions` on cloned ConversationTemplates flow through. You almost never need to set these.
- The parent's other Roles (the ones whose `m_SerializedRequirements` you don't patch) flow through. `set "Roles" index=N` modifies one role, and the others stay parent-defined.
- The parent's `Nodes` (m_SerializedNodes) get REPLACED if you do `set "Nodes" { ... }` (not `append`). The voice-pipeline skill explains the implication.

## Common shape mistakes

- **Cloning the wrong parent class**: e.g. cloning from `specialweapon.X` if you don't want the unit to consume the specialweapon slot. For a sniper-style weapon in the normal slot, pick `weapon.generic_battle_rifle_tier1_crowbar_marksman` or similar.
- **Forgetting `append "Tags" "wmgfl_<character>"`** on the EntityTemplate. The transmog swap never matches (the character renders her equipped vanilla armour as a vanilla soldier body) and weapon `OnlyEquipableBy` gating fails.
- **Naming the default outfit off-convention**: `Transmog.DefaultFor` derives `armor.<character>_default` from the character tag, so an id like `armor.<character>_base` means the picker tile never appears and nothing renders the outfits.
- **Wrong RoleGuid in cloned ConversationTemplates**: must match the role NAME in the actual parent template, which differs per-template (see [voice-pipeline](../voice-pipeline/SKILL.md)).
- **Two UnitLeaderTemplates sharing an id segment**: the game's `GameConditionVars` static ctor builds a `LEADER_STATUS_<SEGMENT>` conversation var per leader template from the id segment after the dot. `pilot.<character>` plus `squad_leader.<character>` both yield `LEADER_STATUS_<CHARACTER>`, which throws a duplicate-key `TypeInitializationException` and crashes new-game creation. A character with two forms needs a unique segment per form (`squad_leader.<character>` plus `pilot.<character>_mech`, or `pilot.<character>` plus `squad_leader.<character>_foot`).

## Cross-references

- [`pmx-to-menace`](../pmx-to-menace/SKILL.md) for the 3D mesh / prefab side.
- [`voice-pipeline`](../voice-pipeline/SKILL.md) for SoundBank + ConversationTemplate clone authoring.
- [`weapon-pipeline`](../weapon-pipeline/SKILL.md) for adding a character-specific weapon model + custom gunshot audio.
- [`../../AGENTS.md`](../../AGENTS.md) for the cross-cutting MENACE internals (sprite-slot bestiary, audio-bank routing model, etc).
