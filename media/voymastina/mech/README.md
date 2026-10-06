# Sinbreaker artwork

The perk foregrounds are Titanfall 2 artwork from the Titanfall Wiki:

| Perk | Source | Foreground treatment |
|---|---|---|
| Monarch | [MonarchIcon.png](https://titanfall.wiki.gg/wiki/File:MonarchIcon.png) | Mech silhouette only, without the surrounding emblem, in warm white |
| Arc Rounds | [Arcrounds.png](https://titanfall.wiki.gg/wiki/File:Arcrounds.png) | Rounds and electrical arc only, without the badge, in warm greyscale |
| Breach Guard | [Tempplating.png](https://titanfall.wiki.gg/wiki/File:Tempplating.png) | Three complete armour plates, without flames or cropped edge plates, in muted ivory and bronze |

`*_source.png` retains the exported or downloaded artwork. `*_foreground.png` is the cleaned,
transparent motif used for both the perk card and the smaller status icon.

## Composition

Cards are 192 × 230 pixels. Monarch uses `media/promotion_unique.png`, while the
two promotion perks use `media/promotion.png`. Foregrounds preserve their aspect
ratio within these bounds and are centred with the listed vertical offset:

| Foreground | Maximum size | Vertical offset |
|---|---|---|
| Monarch | 124 × 148 | -2 |
| Arc Rounds | 122 × 132 | -3 |
| Breach Guard | 116 × 132 | -3 |

Inactive cards are greyscale versions of the complete card, including its backing.
Status icons fit the same foreground within 56 × 56 pixels, centred on a transparent
64 × 64 canvas.

The final sprites live in `assets/additions/sprites/voymastina/mech/perks/` and
`assets/additions/sprites/voymastina/mech/effects/`.

## Target Lock

`effects/target_lock.png` is an unchanged 64 × 64 export of the GFL2 CN client's
`Buff_Target` Texture2D, retaining its red debuff colour and backing. The same
sprite serves the status tooltip and enemy overhead icon.

Source bundle: `6030f8a2f6f0042324ebd077e5586e15.bundle` under the CN client's
`AssetBundles_Windows` directory. Texture path ID: `7648786188359493073`.
Exported with the existing AssetStudio.CLI using `--game GirlsFrontline`,
`--types Texture2D` and `--names '^Buff_Target$'`.

## Pile Bunker

`pile_bunker_source.png` retains the unchanged 128 × 128 CN export of
`Skill_Voymastina_Active_3`, identified as Pile Bunker in
[Voymastina's skill table](https://iopwiki.com/wiki/Voymastina).

Source bundle: `9405364c5f0c1f4a8bcb6fd5fd6e4113.bundle` under the CN client's
`AssetBundles_Windows` directory. Texture path ID: `6634566320509739342`.
Exported with AssetStudio.CLI using `--game GirlsFrontline`, `--types Texture2D`
and `--names '^Skill_Voymastina_Active_3$'`.

The enabled and disabled sprites live in
`assets/additions/sprites/voymastina/mech/skills/`. Both use a 56 × 56 motif centred
on a 64 × 64 canvas with a one-pixel border, matching the existing Sextans skill
icons. The enabled palette is cream `#FBE2AA` and gold `#FFD67E` on brown
`#412108`. The disabled palette is grey `#E5E5E5` and `#DCDCDC` on charcoal
`#2A2A2A`. Source brightness and transparency preserve the original linework and
faint background shapes, with the orange impact accents mapped to the accent colour.

## Sirius Fall projectile

`unity/Assets/Prefabs/voymastina_mech/sirius_fall_rocket/main.prefab` is a variant
of the imported vanilla `Rmc_Rocket_rpg_Launcher`. Its only overrides are the
root's three scale components, set to 2. The source hierarchy, meshes, materials
and particle settings remain inherited.

The source is declared in `jiangyu.json` imports. Only Sirius Fall references the
variant, through a nested `ProjectileData.Prefab` edit that retains the native
flight and despawn settings. This is a MENACE projectile, not a GFL2 asset.
