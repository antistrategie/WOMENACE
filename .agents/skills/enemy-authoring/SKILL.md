---
name: enemy-authoring
description: End-to-end authoring of a new enemy unit for MENACE from a GFL2 boss or monster rig: extraction, Unity bake, entity and skill KDL, runtime AoE shapes and AI weighting, sound, army-list placement, and in-mission testing. Use when adding an enemy unit or debugging why an enemy's AI will not move, attack or use a skill.
---

# Enemy authoring

## What this covers

A new enemy unit is a cloned vanilla vehicle or infantry `EntityTemplate` with a mod-owned body, its own skills, and an entry in a faction's army lists. The pieces:

1. **Model**: GFL2 rig and clips to a clean FBX, then a Unity bake to `unity/Assets/Prefabs/<enemy>/<variant>/main.prefab`.
2. **KDL**: `templates/enemies/<enemy>/` holds the animator, entity, skills, sound bank and army-list patch.
3. **Code**: `code/Systems/Enemies/<Enemy>/` holds what KDL cannot author, chiefly AoE shapes and skill AI configs.
4. **Testing**: a dev spawn verb plus the Unity player log.

## 1. Extract the rig

GFL2 bosses ship as real prefabs with an Animator, skinned renderers and their own clips, so the VoyExport host exports them directly (see the GFL2 extraction notes in `AGENTS.md`). Rigid parts such as hulls and weapons are single-influence skins, which the stock export collapses to no vertex groups. Export the renderer weights with `VoyExport --smrweights` (name-keyed per renderer, safe when one mesh is shared across prefabs with different bone orders) and rebuild the groups in Blender before writing the clean FBX: lod0 only, one take per clip.

GFL2 clips carry `PlayBattleSound(int)` animation events. `VoyExport --clipevents` lists them, and each id resolves through `AudioData.bytes` in the client's `Table/` directory to a `bank/cue` name. That gives the true sound for each clip. Sounds triggered by a skill timeline rather than a clip do not appear there and have to be matched by ear.

## 2. Bake

Write a per-enemy `Build<Enemy>.cs` under `unity/Assets/Authored/<enemy>/Editor/`, modelled on the existing Titan bake. It must:

- Solve the import scale from bind-pose bounds to the GFL2 canon height times the 1.2 world factor (the Doll height policy).
- Build its own controller on the construct driver contract with a Generic avatar, `applyRootMotion` off, and every parameter the construct driver writes (`Speed`, `Movement_Initialized`, `Aiming`, `Hit`, `Vault`, `Overloaded`, `Rotation`, plus the attack triggers). The walker animator drives locomotion partly in code against the vanilla walker rig, so a non-vanilla rig cannot use it.
- Strip planar travel from the top skeleton bone only, so MENACE owns the tile move.
- Map attacks to native triggers: `AnimationType` `ShootSingle` sets `Shoot_Single`, `SpecialAttack1` sets `Special_Attack_1`, `SpecialAttack2` sets `Special_Attack_2`.
- Stamp `OnAnimationEvent` with int 4 (`MovementStep`) on each foot plant of the walk clip, or the unit's `SoundOnMovementStep` never plays.
- Add the named transforms skills resolve: `muzzle` (and `muzzle2`, `muzzle3`) at the barrel tip facing +Z, and `damage` for self-centred skills with `MuzzleType Damage`. A missing `damage` child logs "Required muzzle 'damage'" and the skill applies nothing.
- Add a solid `BoxCollider` named `ColliderDestroyProps` under the root, sized to the body, for vehicle prop crushing.

Body loading goes through `VehicleModels.AssetFor`, so the entity carries no eager bundle reference.

## 3. Entity KDL

Clone the closest vanilla enemy. For a vehicle, clone its `ModularVehicleTemplate` with `clear "Slots"`, clear `Items`, `Loot` and `AttachedPrefabs`, and remove the extra `Prefabs` skins so the swapped body always wins. The loader logs a swallowed `VisuallyMountWeapons` exception for a weaponless modular vehicle, which is expected.

Stats live in `Properties`: `HitpointsPerElement`, `Armor`, `ArmorSide`, `ArmorBack`, `ArmorDurabilityPerElement` (the armour's own pool that `DamageToArmorDurability` wears down), `Accuracy`, `Vision`. `ArmyPointCost` sits on the entity itself. Vanilla enemy walkers run 150 to 250 HP, 100 to 140 armour and 250 to 400 durability. A boss sits a step above that.

## 4. Skills

Clone a vanilla skill whose handler chain already does the job, then retune. Skill handlers are separate objects, readable offline: inspect the skill as JSON for the handler `pathId`, then `jiangyu templates inspect --collection resources.assets --path-id <id>`. The Attack handler carries `Damage`, `ArmorPenetration`, `DamageToArmorDurability`, `Suppression`, `ElementsHit`, `ElementsHitPercentage` (a fixed count or a share of the squad), `AccuracyBonus` and `AccuracyDropoff`. A gun skill's Attack handler has `Damage` 0 because weapon damage comes from its weapon item, so a weaponless clone deals only what KDL sets on the handler.

- Grenade and AoE clones roll to hit and can scatter. `IsAlwaysHitting`, `CanHitAnotherTile #false`, `ScatterChance 0` and `AoEChanceToHitCenter 100` make a blast land where aimed.
- `IsLimitedUses` with `Uses N` refills every turn on an entity-granted skill. Item-granted uses last the mission, so a limited skill rides a cloned vehicle accessory in `Items` (`SkillsGranted`).
- A turn cooldown is a hidden marker effect with a `LifetimeLimit` handler (`OnRoundStart`), planted through the tag-plus-`ApplySkillToSelf` charge pattern, and gated on the skill with `LimitUsability`.
- AP cost sets attacks per turn against the unit's 100 AP. Movement costs 16 AP a tile for a medium walker, so 60 AP attacks leave room for one attack plus a two-tile walk.
- A burst skill's `OnAfterSkillUse` fires after the first repetition, so an AI turn ends mid-burst and the remaining rounds play during the next unit's turn. `DelayAfterSkillUse` long enough to cover the burst holds the turn.

## 5. What KDL cannot author

`CustomAoEShape` and `AIConfig` are Odin-serialised. A system assigns both in `OnTemplatesApplied` and `OnSceneLoaded`.

- **AoE shapes**: an `ICustomAoEShape` injected with `[JiangyuType(Interfaces=...)]`, `GetAoERadius` returning 0. During AI tile scoring the origin tile can be empty, so resolve the attacker from `TacticalManager.GetActiveActor()` when the origin holds no entity, or the shape counts no hostiles from any tile the unit has not reached.
- **AI configs**: assign a fresh `Il2CppMenace.Tactical.AI.Behaviors.Data.InflictDamage` (base `Il2CppMenace.Tactical.AI.Data.SkillBehavior`) with `ScoreMult` and `InfluencesPositioning`, leaving the vanilla skill's own config untouched.

## 6. How the AI decides

The AI scores every move plus attack it can afford this turn and takes the best. It never plans a walk this turn to attack next turn.

- It positions only from skills whose `AIConfig` has `InfluencesPositioning`, and it reads the skill's targeting range, not its blast radius. A self-centred pulse with range 0 never pulls a unit in. Make such a skill targeted (`IsTargeted`, `TargetsAllowed Attack`, a real range) and let the shape recentre the blast on the user.
- A melee-range skill out of reach this turn contributes nothing. Pair it with a ranged skill whose range brings the unit within one turn's walk of it.
- The entity's `AIRole` weighs the rest. `ThreatFromOpponents` must stay on for a ranged unit, since it is how the AI finds tiles to attack from. Melee bosses can turn it off. `SafetyScale`, `IsAllowedToEvadeEnemies` and `CoverAgainstOpponents` govern kiting and cover.

## 7. Sound

Unpack the boss's CriWare `.acb` with `vgmstream-cli` (names collide, so index the output), and the large-mech foley from the common foley bank. Clone `weapons_soundbank` into the enemy's bank under `templates/enemies/<enemy>/sound.kdl`. A burst skill plays its sound once through `SoundsOnAttackPerElement` with `IsSoundOnAttackPerElementPlayingAfterAnimationDelay`, and a self-centred skill's impact goes there too so it lands on the delay rather than at the animation start. Null `SoundOnMovementStepOverrides2` so the walker's surface sounds do not replace the unit's footstep.

## 8. Army lists

A faction's `ArmyTemplate` lists (`army.<faction>_list_*`) hold `PossibleUnits` entries of `Template`, `Weight`, `WeightMultAfterPick` and `MaxConcurrentPctOfArmyPoints`, each list gated by `ReqCampaignProgress`. A mission fills its army-point budget by weighted picks. Append the enemy with a `patch "ArmyTemplate"` block. Bosses use a low weight, a small repeat multiplier and a budget cap so they always arrive with escorts. `WeightMultAfterPick 0` limits a unit to one per army. Each starting list has a `_reinforcements` twin with the same units, which supplies the enemies that arrive mid-mission. A unit appears there only if it is appended to that list as well.

A fixed weight dilutes when another mod adds units to the same list. `ArmyShareSystem` holds an entry at a target share of picks instead: list the army, the unit and the fraction in its `Shares` table, and it solves the weight from the other entries in a prefix on `Army.CreateArmy`, after every template mod has finished. The KDL weight only applies until then.

## 9. Testing

A dev spawn verb (`code/Dev/<Enemy>.Dev.cs`) spawns the unit near the squad on the mission's enemy side, or on the player side to fire its skills by hand. The Unity player log at `<Proton prefix>/AppData/LocalLow/Overhype Studios/Menace/Player.log` records every `OnTurnStart`, `OnMovementFinished`, `OnSkillUse` and `OnDeath` per actor, which is the trace for what the AI actually did each turn. The bridge `aiscore` command reports skill scores only during the AI's own turn and reads 0 otherwise.
