using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Holds the leaders a predicate picks to at most a given number of squaddies (a squad of N is the
// leader plus N-1 squaddies). The getter alone is not enough, so the cap is enforced three ways:
//   - GetMinValidSquaddies and GetMaxValidSquaddies are both lowered to the cap. The min follows
//     the max down, or a min above the max would leave the leader unable to pass deploy validation.
//   - Squaddies past the cap are stripped on every min-getter call. The getters run on every
//     validation pass, so this is the enforcement point no deploy can skip, and it catches
//     squaddies added by paths that never consult the getter (auto-fill, a pre-filled starting
//     squad, add-squaddie effects).
//   - An add that takes the squad past the cap is undone in TryAddSquaddie and reported as refused.
//
// The cap only ever lowers a count, so every system built on it composes with the others in any
// order: the smallest cap wins. Each system keeps its own instance, and so its own strip guard.
internal sealed class SquaddieCap
{
    private readonly string _label;
    // The cap for a leader, or null when the leader is not one this instance holds.
    private readonly Func<BaseUnitLeader, int?> _capFor;
    private readonly Action<BaseUnitLeader> _afterMin;
    private IModLog _log;
    // TryRemoveSquaddie re-enters the valid-range getters mid-strip.
    private bool _stripping;

    // afterMin runs for every held leader on each min-getter call, after the strip.
    public SquaddieCap(string label, Func<BaseUnitLeader, int?> capFor, Action<BaseUnitLeader> afterMin = null)
    {
        _label = label;
        _capFor = capFor;
        _afterMin = afterMin;
    }

    public void Install(ModContext context)
    {
        _log = context.Log;
        // The concrete SquadLeader overrides, never the BaseUnitLeader virtuals (detouring those
        // crashes on boot). TryAddSquaddie is non-virtual on the base, so it is patched there.
        context.Patches.Postfix("Il2CppMenace.Strategy.SquadLeader", "GetMinValidSquaddies", OnGetMinValidSquaddies);
        context.Patches.Postfix("Il2CppMenace.Strategy.SquadLeader", "GetMaxValidSquaddies", OnGetMaxValidSquaddies);
        context.Patches.Postfix("Il2CppMenace.Strategy.BaseUnitLeader", "TryAddSquaddie", OnTryAddSquaddie);
    }

    // Removes every squaddie past the leader's cap, the earliest kept.
    public void Strip(BaseUnitLeader leader)
    {
        var cap = CapOf(leader);
        if (cap.HasValue)
            Strip(leader, cap.Value);
    }

    private void OnGetMinValidSquaddies(PatchInfo info)
    {
        var leader = LeaderOf(info);
        var cap = Lower(info, leader);
        if (!cap.HasValue)
            return;
        if (!_stripping)
            Strip(leader, cap.Value);
        _afterMin?.Invoke(leader);
    }

    private void OnGetMaxValidSquaddies(PatchInfo info) => Lower(info, LeaderOf(info));

    private int? Lower(PatchInfo info, BaseUnitLeader leader)
    {
        try
        {
            if (info.Result is not int count)
                return null;
            var cap = CapOf(leader);
            if (cap.HasValue && count > cap.Value)
                info.Result = cap.Value;
            return cap;
        }
        catch (Exception ex)
        {
            _log?.Warn($"{_label}: squaddie cap failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private void OnTryAddSquaddie(PatchInfo info)
    {
        try
        {
            if (info.Result is not bool added || !added)
                return;
            var leader = LeaderOf(info);
            var cap = CapOf(leader);
            if (!cap.HasValue || (leader.m_SquaddieIds?.Count ?? 0) <= cap.Value)
                return;
            if (info.Args is { Count: > 0 } && info.Args[0] is int id)
                leader.TryRemoveSquaddie(id);
            info.Result = false;
            _log?.Debug($"{_label}: refused a squaddie on {leader.GetTemplate()?.GetID()}");
        }
        catch (Exception ex)
        {
            _log?.Warn($"{_label}: squaddie refusal failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Strip(BaseUnitLeader leader, int cap)
    {
        try
        {
            var current = leader.m_SquaddieIds;
            if (current == null || current.Count <= cap)
                return;
            _stripping = true;
            var excess = new List<int>();
            for (var i = cap; i < current.Count; i++)
                excess.Add(current[i]);
            foreach (var id in excess)
                leader.TryRemoveSquaddie(id);
            _log?.Debug($"{_label}: stripped {excess.Count} squaddie(s) from {leader.GetTemplate()?.GetID()}");
        }
        catch (Exception ex)
        {
            _log?.Warn($"{_label}: squaddie strip failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _stripping = false;
        }
    }

    private int? CapOf(BaseUnitLeader leader) => leader == null ? null : _capFor(leader);

    private static BaseUnitLeader LeaderOf(PatchInfo info)
        => (info.Instance as Il2CppObjectBase)?.TryCast<BaseUnitLeader>();
}
