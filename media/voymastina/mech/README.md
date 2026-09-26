# Sinbreaker perk artwork

The source images are Titanfall 2 artwork from the Titanfall Wiki:

| Perk | Source | Foreground treatment |
|---|---|---|
| Monarch | [MonarchIcon.png](https://titanfall.wiki.gg/wiki/File:MonarchIcon.png) | Mech silhouette only, without the surrounding emblem, in warm white |
| Arc Rounds | [Arcrounds.png](https://titanfall.wiki.gg/wiki/File:Arcrounds.png) | Rounds and electrical arc only, without the badge, in warm greyscale |
| Breach Guard | [Tempplating.png](https://titanfall.wiki.gg/wiki/File:Tempplating.png) | Three complete armour plates, without flames or cropped edge plates, in muted ivory and bronze |

`*_source.png` retains the downloaded artwork. `*_foreground.png` is the cleaned,
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
