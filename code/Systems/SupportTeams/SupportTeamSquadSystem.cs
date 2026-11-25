using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// A support team is a squad of one: its single element is the whole crew, so it takes no
// squaddies. Both valid-squaddie getters return zero for a team, an added squaddie is undone at
// TryAddSquaddie, and any squaddies a team carries are stripped on each validation pass.
//
// SoloSquadSystem, DollSquadFloorSystem and DollSquadLimitSystem patch the same getters. Each of
// them only ever lowers the count, so the order they run in does not matter, and a team's 0 and 0
// follows the same rule.
public sealed class SupportTeamSquadSystem : JiangyuSystem
{
    // TryRemoveSquaddie re-enters the valid-range getters mid-strip.
    private bool _stripping;

    public override void OnInit()
    {
        // The concrete SquadLeader overrides, never the BaseUnitLeader virtuals (detouring those
        // crashes on boot). TryAddSquaddie is non-virtual on the base, so it is patched there.
        Context.Patches.Postfix("Il2CppMenace.Strategy.SquadLeader", "GetMinValidSquaddies", OnGetMinValidSquaddies);
        Context.Patches.Postfix("Il2CppMenace.Strategy.SquadLeader", "GetMaxValidSquaddies", OnGetMaxValidSquaddies);
        Context.Patches.Postfix("Il2CppMenace.Strategy.BaseUnitLeader", "TryAddSquaddie", OnTryAddSquaddie);
    }

    public override void OnTemplatesApplied() => SupportTeams.ClearCache();

    // The getters run on every validation pass, so the min getter is also where a squaddie that
    // slipped in by another path is stripped.
    private void OnGetMinValidSquaddies(PatchInfo info)
    {
        var leader = LeaderOf(info);
        if (leader == null || !SupportTeams.IsTeam(leader))
            return;
        info.Result = 0;
        if (!_stripping)
            Strip(leader);
    }

    private void OnGetMaxValidSquaddies(PatchInfo info)
    {
        var leader = LeaderOf(info);
        if (leader != null && SupportTeams.IsTeam(leader))
            info.Result = 0;
    }

    private void OnTryAddSquaddie(PatchInfo info)
    {
        try
        {
            var leader = LeaderOf(info);
            if (leader == null || !SupportTeams.IsTeam(leader))
                return;
            if (info.Result is true && info.Args is { Count: > 0 } && info.Args[0] is int id)
                leader.TryRemoveSquaddie(id);
            info.Result = false;
            Context.Log.Debug($"support team: refused a squaddie on {leader.GetTemplate()?.GetID()}");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"support team: squaddie refusal failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Strip(BaseUnitLeader leader)
    {
        try
        {
            var current = leader.m_SquaddieIds;
            if (current == null || current.Count == 0)
                return;
            _stripping = true;
            var ids = new List<int>();
            for (var i = 0; i < current.Count; i++)
                ids.Add(current[i]);
            foreach (var id in ids)
                leader.TryRemoveSquaddie(id);
            Context.Log.Debug($"support team: stripped {ids.Count} squaddie(s) from {leader.GetTemplate()?.GetID()}");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"support team: squaddie strip failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _stripping = false;
        }
    }

    private static BaseUnitLeader LeaderOf(PatchInfo info)
        => (info.Instance as Il2CppObjectBase)?.TryCast<BaseUnitLeader>();
}
