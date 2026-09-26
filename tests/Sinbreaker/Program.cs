using WOMENACE.Code;

var checks = 0;
void Equal<T>(T expected, T actual, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
}

var state = new SinbreakerCombatState();
state.StartTurn();
state.Mark(11);
state.Mark(11);
Equal(1, state.MarkedTargets.Count(), "rounds and bursts do not stack");
Equal(1.25f, state.BunkerMultiplier(11, 0.25f, 100, 1), "preview multiplier");
Equal(1.25f, state.BunkerMultiplier(11, 0.25f, 100, 1), "repeated preview keeps bonus");
Equal(true, state.HasMark(11), "preview does not consume");
Equal(1f, state.BunkerMultiplier(12, 0.25f, 100, 1), "mark is target-specific");

state.Mark(12);
Equal(false, state.IsCommittedUse(100, 1), "a queried use is not a committed action");
Equal(true, state.BeginBunker(11, false, 100, 1), "first real use commits");
Equal(false, state.HasMark(11), "commit consumes one target mark");
Equal(true, state.HasMark(12), "other targets remain marked");
Equal(false, state.BeginBunker(12, true, 100, 1), "duplicate use cannot commit another target or guard");
Equal(11L, state.BunkerTarget, "duplicate callback preserves committed target");
Equal(true, state.BunkerLocked, "duplicate callback preserves consumed bonus");
Equal(true, state.HasMark(12), "duplicate callback cannot consume another mark");
Equal(false, state.IsCommittedUse(100, 2), "changed usage is not itself a commitment");
Equal(false, state.IsCommittedUse(101, 1), "another skill cannot borrow the use");
state.EnterDamageBuild(100, 1);
Equal(1.25f, state.BunkerMultiplier(11, 0.25f, 100, 1), "first element gets committed bonus");
state.ExitDamageBuild();
Equal(1f, state.BunkerMultiplier(11, 0.25f, 100, 1), "preview between elements cannot borrow bonus");
state.EnterDamageBuild(100, 1);
Equal(1.25f, state.BunkerMultiplier(11, 0.25f, 100, 1), "later element retains committed bonus");
state.EnterDamageBuild(101, 1);
Equal(1f, state.BunkerMultiplier(11, 0.25f, 100, 1), "nested unrelated damage scope hides outer commitment");
Equal(1f, state.BunkerMultiplier(11, 0.25f, 101, 1), "unrelated scope cannot borrow bonus");
state.ExitDamageBuild();
Equal(1.25f, state.BunkerMultiplier(11, 0.25f, 100, 1), "outer scope restored after nested build");
state.EnterDamageBuild(100, 2);
Equal(1f, state.BunkerMultiplier(11, 0.25f, 100, 2), "new usage without commitment receives no bonus");
state.ExitDamageBuild();
state.ExitDamageBuild();
Equal(1f, state.BunkerMultiplier(11, 0.25f, 100, 1), "hover cannot borrow committed bonus");
state.BeginBunker(11, false, 100, 2);
state.EnterDamageBuild(100, 2);
Equal(1f, state.BunkerMultiplier(11, 0.25f, 100, 2), "next unmarked action has no bonus");
state.ExitDamageBuild();
Equal(false, state.GuardActive, "unlearned guard never activates");

Equal(2, state.MarkEndsRemaining(12), "current and next owner turn end");
state.EndTurn();
Equal(true, state.HasMark(12), "mark survives current owner turn end");
Equal(1, state.MarkEndsRemaining(12), "badge after owner turn end counts only next end");
var otherOwner = new SinbreakerCombatState();
otherOwner.StartTurn();
otherOwner.EndTurn();
Equal(true, state.HasMark(12), "other actor turns cannot expire mark");
state.StartTurn();
Equal(1, state.MarkEndsRemaining(12), "next owner turn remaining end");
Equal(true, state.HasMark(12), "mark survives next owner turn start");
state.EndTurn();
Equal(false, state.HasMark(12), "mark expires at next owner turn end");
state.Mark(13);
Equal(1, state.MarkEndsRemaining(13), "off-turn mark expires at next owner turn end");
state.StartTurn();
state.Mark(13);
state.EndTurn();
Equal(true, state.HasMark(13), "new gun hit refreshes expiry");
state.StartTurn();
state.EndTurn();
Equal(false, state.HasMark(13), "refreshed mark expires");

state.BeginBunker(20, true, 100, 3);
Equal(true, state.GuardActive, "guard needs neither mark nor execution");
state.BeginBunker(20, true, 100, 4);
Equal(true, state.GuardActive, "guard refresh does not stack");
state.EndTurn();
Equal(true, state.GuardActive, "guard survives owner turn end");
state.StartTurn();
Equal(false, state.GuardActive, "guard expires at owner turn start");

state.BeginBunker(21, false, 100, 5);
Equal(false, state.TryExecution(21, true, true, false), "individual element kill is not unit execution");
Equal(false, state.TryExecution(22, true, true, true), "collateral unit is not committed target");
Equal(false, state.TryExecution(21, false, true, true), "cook-off is not direct bunker");
Equal(false, state.TryExecution(21, true, false, true), "friendly or neutral does not repair");
Equal(true, state.TryExecution(21, true, true, true), "unmarked hostile unit execution repairs");
Equal(false, state.TryExecution(21, true, true, true), "repeated kill callbacks cannot repair twice");
state.BeginBunker(22, false, 100, 6);
Equal(false, state.TryExecution(22, true, true, true), "second unit cannot upgrade repair that turn");
state.StartTurn();
Equal(true, state.TryExecution(22, true, true, true), "next owner turn has new activation");
state.StartTurn();
state.BeginBunker(21, false, 100, 7);
Equal(false, state.TryExecution(21, true, true, true), "destroyed unit cannot be claimed again");

Equal(370, SinbreakerCombatState.RepairedDurability(100, 900, false, 0.3f), "ordinary unit restores 30 percent");
Equal(460, SinbreakerCombatState.RepairedDurability(100, 1200, false, 0.3f), "current maximum controls repair");
Equal(900, SinbreakerCombatState.RepairedDurability(800, 900, false, 0.3f), "ordinary repair capped");
Equal(900, SinbreakerCombatState.RepairedDurability(0, 900, true, 0.3f), "vehicle repair branch restores full durability");
Equal(270, SinbreakerCombatState.RepairedDurability(0, 900, false, 0.3f), "non-vehicle repair branch uses ordinary fraction");
Equal(900, SinbreakerCombatState.RepairedDurability(900, 900, true, 0.3f), "full armour stays capped");
state.StartTurn();
state.BeginBunker(23, false, 100, 8);
Equal(true, state.TryExecution(23, true, true, true), "full armour execution still claims activation");
state.BeginBunker(24, false, 100, 9);
Equal(false, state.TryExecution(24, true, true, true), "full armour claim prevents later vehicle repair");

Equal((17f, 1.5f), SinbreakerCombatState.ArcModifiers(SinbreakerCombatState.GunId, 17, 1.5f),
    "Arc Rounds passes configured modifiers to the gun");
foreach (var id in new[] { SinbreakerCombatState.BunkerId, "active.voymastina_mech_rocket", "active.other", null })
    Equal((0f, 1f), SinbreakerCombatState.ArcModifiers(id, 17, 1.5f), "Arc Rounds gun-only scoping");
Console.WriteLine($"PASS {checks} Sinbreaker combat checks");
