# Melee cook-off regression harness

Run with the .NET 10 SDK, without the game or extra packages:

```sh
dotnet run --project tests/MeleeCookOff
```

Development diagnostics are enabled by default. Run the non-dev code path with
`dotnet run --project tests/MeleeCookOff -p:JiangyuDev=false`.
`mise test` runs both configurations and the probe-cleanup harness after compiling
the mod.

The console harness links the production `MeleeCookOffSystem` and `MeleeSkills`
sources. Minimal game, SDK and Harmony shims dispatch the registered callbacks,
with a callback representing the native damage body and nested calls. The damage
prefix and finalizer share per-call state. No cook-off policy is copied into the
harness. Every scenario gets a fresh system and unloads its owned patch afterwards.

The checks cover nested critical damage attribution, independent damage and victim
boundaries, native attacker pointer identity, blast instance identity, descendant
shrapnel, repeated impacts, non-attack payloads and scene reset. Only hitpoint and
armour damage may change. Hook warnings fail the scenario rather than being hidden.
The critical-damage fixture passes a null attacker and retains the melee killer in
the defect skill's `Source`, matching `DamageHandler.ApplyDamage`.
The simulated ordinary postfix runs only when the preceding callbacks succeed.
The registered finalizer also runs on exceptions, including when an earlier prefix
prevented this system's prefix from running. Regressions check that original
exceptions survive and aborted calls cannot leave inherited damage depth behind.

These shims test managed policy, not IL2CPP execution. Their flag values, pointers
and damage fields are stand-ins, not a native ABI model. A live-game path remains
necessary to verify patch binding, native critical-defect nesting, SourceSkill
attribution, asynchronous repetitions and the actual damage side effects. The
exception-safe damage patch uses Harmony directly because the SDK exposes no
finalizer registration, and is removed in `OnUnload`.

## In-game check

Use a disposable tactical mission with a dev build and an expendable witness.
Do not use the mission's only player actor. Control explosions can kill it.

Use `Melee.Spawn` with the witness, `targetId` set to
`enemy.vehicle_rogue_army_auto_autocannon_walker` and `grantBlast=false`.
Attack normally with Pile Bunker, then inspect `Melee.Report` and `Melee.Probe`.
The critical defect is chance gated. A death without that defect does not exercise
the nested-damage regression. The walker must survive the initial hit below its
heavy-defect threshold, so soften a full-health walker before the Pile Bunker hit.

When a melee hit triggers the critical defect, `inheritedDefectHits` increases and
the resulting blast increases `damageZeroed` without reducing the witness's
hitpoints. Repeat with her gun as a control, and with another unit in the blast
radius to check that protection applies only to the melee killer. Check armour
durability as well as hitpoints in game. Ordinary incoming attacks, later fire
damage and explosions from other wrecks must still hurt.

`Melee.CriticalHit(witness, target, melee)` provides a forced native-defect probe
on a separate, fresh test vehicle. It makes only the critical defect selectable
during the synchronous damage call and restores the weights as that scope exits.
Every restoration is attempted even if another fails. Cleanup failures are warned
without replacing the original probe exception, and borrowed skills are revoked
on every exit.
It does not attach the deathrattle itself. `defectDamageHits` must be positive and
`defectDamageTrace` must name `damage_effect.critical_hit` for the run to exercise
the intended path. The dead vehicle need not retain the critical status in its
skill container. Damage variation can leave the initial hit above the heavy-defect
threshold, even with forced selection weights. That result, or a target whose
defect groups exclude the critical defect, does not exercise the regression.
Use a fresh target for another bounded attempt.

`Melee.Probe` reports the incoming defect's nesting depth, attacker, skill source
and recorded melee attacker. `Melee.Report` includes armour loss, since a blast
can damage vehicle armour without moving its hitpoints. Neither helper heals the
witness. Use fresh expendable witnesses for independent controls.

## Live validation

The native damage sequence and attribution are verified with expendable Sinbreaker
witnesses against Rogue Army autocannon and laser walkers. The owned damage
prefix/finalizer binding and normal unwind are also verified in game:

- Three independent zero-damage calls on a test vehicle complete before its
  critical-defect probe. The defect still arrives at depth 1, confirming that
  completed native calls leave no nesting behind.
- The defect damage arrives at depth 1 with a null attacker and a `Source` matching
  the recorded Pile Bunker attacker.
- Both the direct blast and delegated shrapnel are waived for the melee killer.
  The first witness retains 300 hitpoints and 900 armour durability.
- An adjacent bystander still takes lethal damage.
- Ranged-attributed critical defects damage or kill a previously protected witness.
  Its older melee waiver does not match the new blast.
- An ordinary follow-up hit still deals hitpoint damage.
- The surviving witness's skill list matches its initial list after both the
  critical-melee and ordinary-hit probes, with no cleanup warnings.

Exceptional unwind and failed-restoration paths are covered by the managed
harnesses and installed Harmony inspection, not by deliberately throwing in the
live mission. Test casualties can reduce the original unit's morale even outside
the blast radius, so cleanup checks morale as well as hitpoints, armour and AP.

The mercenary medium walker run does not produce a critical damage callback.
That run is inconclusive, not a protection pass or evidence that the variant
cannot cook off. These checks exercise the native damage and defect lifecycle
using an owned attack skill, not the skill-bar animation. The managed harness
separately covers repeated impacts and scene reset.
