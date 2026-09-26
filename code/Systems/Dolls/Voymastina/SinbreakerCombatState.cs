namespace WOMENACE.Code;

// One ledger per Sinbreaker, never a round-global clock. Runtime actor pointers
// are used only inside the mission and their wrappers are retained by the system.
internal sealed class SinbreakerCombatState
{
    internal const string GunId = "active.voymastina_mech_gun";
    internal const string BunkerId = "active.voymastina_mech_drill";
    private readonly Dictionary<long, int> _marks = new();
    private readonly HashSet<long> _executed = new();
    private readonly Stack<(long Skill, int Usage)> _damageBuilds = new();
    private (long Skill, int Usage)? _bunkerUse;
    private int _turn;
    private bool _repairSpent;
    private bool _turnEnded = true;

    internal long BunkerTarget { get; private set; }
    internal bool BunkerLocked { get; private set; }
    internal bool GuardActive { get; private set; }
    internal IEnumerable<long> MarkedTargets => _marks.Keys;

    internal void Mark(long target) => _marks[target] = _turn + 1;
    internal bool HasMark(long target) => _marks.ContainsKey(target);
    internal int MarkEndsRemaining(long target)
        => _marks.TryGetValue(target, out var expires) ? expires - _turn + (_turnEnded ? 0 : 1) : 0;

    internal bool IsCommittedUse(long skill, int usage) => _bunkerUse == (skill, usage);

    internal void EnterDamageBuild(long skill, int usage) => _damageBuilds.Push((skill, usage));
    internal void ExitDamageBuild() => _damageBuilds.Pop();

    internal float BunkerMultiplier(long target, float bonus, long skill, int usage)
        => HasMark(target) || (IsCommittedUse(skill, usage)
            && _damageBuilds.TryPeek(out var building) && building == (skill, usage)
            && BunkerTarget == target && BunkerLocked) ? 1f + bonus : 1f;

    internal bool BeginBunker(long target, bool guard, long skill, int usage)
    {
        if (IsCommittedUse(skill, usage))
            return false;
        _bunkerUse = (skill, usage);
        BunkerTarget = target;
        BunkerLocked = _marks.Remove(target);
        GuardActive |= guard;
        return true;
    }

    internal void StartTurn()
    {
        _turn++;
        _turnEnded = false;
        _repairSpent = false;
        GuardActive = false;
    }

    internal void EndTurn()
    {
        _turnEnded = true;
        foreach (var target in _marks.Where(pair => pair.Value <= _turn).Select(pair => pair.Key).ToArray())
            _marks.Remove(target);
    }

    internal bool TryExecution(long target, bool directBunker, bool hostile, bool destroyed)
    {
        if (!directBunker || !hostile || !destroyed || target != BunkerTarget
            || _repairSpent || !_executed.Add(target))
            return false;
        // A qualifying execution spends the activation even at full armour.
        _repairSpent = true;
        return true;
    }

    internal static int RepairedDurability(int current, int maximum, bool vehicle, float ordinaryFraction)
        => Math.Clamp(current + (int)MathF.Round(maximum * (vehicle ? 1f : ordinaryFraction)), 0, maximum);

    internal static (float Penetration, float ArmourDamageMultiplier) ArcModifiers(
        string skillId, float penetration, float armourDamageMultiplier)
        => skillId == GunId ? (penetration, armourDamageMultiplier) : (0f, 1f);
}
