# Sinbreaker verification

Run from the repository root:

```sh
JIANGYU_DIR=/home/justin/dev/github.com/antistrategie/jiangyu mise compile
dotnet run --project tests/Sinbreaker
python -m unittest tests/test_sinbreaker_templates.py
```

The console tests execute the combat ledger used by the runtime without loading
Unity. They cover non-stacking marks, read-only previews, committed multi-element
damage, owner-turn expiry and badge counts, refreshable guard, execution gating,
once-per-turn repairs, percentage caps and gun-only Arc Rounds. Commitment checks
exercise skill and usage identity, duplicate callbacks and nested damage-build
scopes through the same ledger methods the runtime calls.

These are not interop integration tests. They do not execute native hook dispatch,
the nested receive-damage prefix/postfix stack, manager lifecycle callbacks,
vehicle classification or UI refresh. The live checklist covers those boundaries.

The Python tests check the drill's inherited airborne exclusion, replacement of
inherited perk handlers and description links against the native glossary and
Sinbreaker keywords. They require a current successful compile. Attribute editing
and asset-reference validation belong to Jiangyu, not this suite.

## Native contracts

- `Skill.Use` assigns `UsageId` at `0x714DC0`, before `OnUse`. Only a real
  carrying-skill `OnUse` commits a bunker action.
- `ApplyToTile` schedules damage rather than completing it synchronously.
  `FillDamageInfo` builds actual hit properties at `0x70A3D6`.
  `GetExpectedDamage` builds preview properties separately at `0x70C51E`.
- `DamageSustainedMult` enters hull damage at `0x70A651`. Armour durability is
  calculated separately at `0x70AAA9`. The preview uses the same split.
- The knife's only damage exclusion is `hovering`. The drill clears that inherited
  list so `AttackHandler.OnBeforeAnySkillUsed` (`0x748DA0`) does not zero its damage
  multiplier in actual hits or previews. `OnVerifyTarget` (`0x749050`) checks
  `EntityFlagsRequired`, not this list, so target permissions are unchanged.
  The drill bypasses the shared melee damage restoration fallback so legitimate
  zeroes from normal mitigation are preserved.
- Execution classification uses the victim's native `Actor.IsVehicle()`, not a
  machine tag. Classification is captured before the actor dies.

## Live-game acceptance

These checks require MENACE and are not claimed by the offline tests:

1. Start a new campaign. Sinbreaker is unavailable at affinity 9 and available
   at 10 on the existing 5,500-point curve. Save, reload and swap both forms.
   Confirm Monarch is immediate and promotions survive a normal campaign reload.
2. Check default and Erwin chassis at 320 HP, 900 armour durability, 170 armour on all sides,
   vision 11, four accessory slots and unchanged supply costs. Pilot agility 113
   derives 130 AP before other modifiers through the shared over-cap conversion
   (130.4 float, 130 rounded integer), without a Monarch AP bonus. Check preview,
   armoury and tactical AP after first unlock, both form-swap directions and a
   save/reload with each form active. Infantry Voymastina keeps her own attributes.
   Pilot vitality must not override chassis HP. Pilot growth stays native,
   leaving over-cap agility at its authored base while other stats can grow to
   100. Solo Dolls retain their existing caps and growth. Ordinary movement costs
   22 AP per tile. Expert Pilot and Dash together cost 12, or 9 with Booster
   Injection. Check the existing fast movement, dash and landing behaviour.
3. Check gun 40 AP, three 37-damage rounds, 30 penetration and 7 armour wear.
   Check rocket 50 AP, 130 damage, 80 penetration, 50 armour wear and three elements hit.
   Check bunker 60 AP, 150 damage, 160 penetration and 160 armour wear.
   Confirm six-tile guaranteed, cover-ignoring bunker hits all inherited elements.
4. Gun-hit several enemies, including an armour-stopped hit. Repeated rounds and
   bursts leave one mark each. Hover and cancel repeatedly without consuming it.
   The marked bunker previews and deals a 1.25 damage multiplier on every affected
   element. Check ground, flying and hovering targets. Rocket remains independent.
5. Check mark badges and expiry across other actors' turns, Sinbreaker's current
   turn end, next turn start and next turn end. An off-turn gun hit lasts until
   the next Sinbreaker turn end. Bunker consumes only its target's mark. The
   marked enemy's overhead bar shows one Target Lock icon, including when its
   HUD appears after the mark was applied. Consumption and expiry clear the icon
   immediately, and pooled HUDs must not carry it over to another unit.
6. Kill an ordinary hostile unit with an unmarked bunker and restore 30% of the
   current maximum armour durability. Killing only a squad element pays nothing.
   An actual vehicle restores full durability, a non-vehicle machine does not.
   Check repeated callbacks, collateral cook-off deaths, a second execution,
   full-armour executions, changed maximum durability and a dead Sinbreaker.
   No hull HP, defects or revival should change. Verify both the selected-unit
   window and overhead armour bar refresh immediately, without reselecting the
   unit, taking damage or ending the turn. Check partial and full repairs.
7. Learn Arc Rounds. Gun penetration becomes 50 and armour wear 14 per round,
   with unchanged HP damage and AP cost. Check accessories and other perks combine
   in the hover and hit path. Rocket and bunker receive no Arc Rounds bonus.
8. Learn Breach Guard and use an unmarked, non-lethal bunker. Incoming hull damage
   is multiplied by 0.75 with unchanged armour wear. Combine with other mitigation,
   use bunker twice and confirm no stacking. Guard survives her turn end and ends
   at the start of her next turn. Check the status and ordinary cook-off immunity.
9. Review perk cards, inactive cards, status icons, duration badges, tooltips and
   promotion ordering in the actual UI.
10. Equip one Vehicle Ammo Cases accessory and deploy both chassis variants. After
    mission initialisation, gun uses should be 36 and rocket uses 16, both full.
    Two cases should give 54 and 24. Repeat across missions and with different
    accessory slot ordering. Pile Bunker stays unlimited. Consume gun and rocket
    ammo and verify a supply drop refills both without changing their maximum uses.
    The native weapon reset and pouch callbacks must not lose or duplicate a bonus.

The artwork uses isolated Titanfall 2 motifs on the existing WOMENACE backings.
Monarch uses `media/promotion_unique.png`, and both learned perks use
`media/promotion.png`. Source images, cleaned foregrounds and composition details
are in [the artwork directory](../../media/voymastina/mech/README.md).
Target Lock uses the unmodified GFL2 CN `Buff_Target` status icon.
