using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for perks, invoked over the dev-loader bridge, e.g.
// {verb: "Perks.Give", args: ["ots14", "perk.wmgfl_barrage"], mutate: true}.
[DevVerb]
public static class Perks
{
    // Grant a perk to a hired leader without spending promotion points. The
    // leader matches by its unit template id, or the tail of it ("ots14"
    // matches player_squad.ots14).
    [MutatingVerb]
    public static object Give(string leader, string perkId)
    {
        var leaders = StrategyState.Get()?.Roster?.m_HiredLeaders;
        if (leaders == null)
            return new { error = "no strategy state / roster" };
        var perk = Templates.ById<PerkTemplate>(perkId);
        if (perk == null)
            return new { error = $"perk '{perkId}' not found" };

        for (var i = 0; i < leaders.Count; i++)
        {
            var candidate = leaders[i];
            var id = candidate?.GetTemplate()?.GetID();
            if (id == null || !(id == leader || id.EndsWith("." + leader, StringComparison.Ordinal)))
                continue;
            if (candidate.HasPerk(perk))
                return new { ok = true, leader = id, perk = perkId, note = "already has it" };
            candidate.AddPerk(perk, false);
            return new { ok = true, leader = id, perk = perkId };
        }
        return new { error = $"no hired leader matching '{leader}'" };
    }

    // The perks a hired leader currently holds.
    public static object Show(string leader)
    {
        var leaders = StrategyState.Get()?.Roster?.m_HiredLeaders;
        if (leaders == null)
            return new { error = "no strategy state / roster" };
        for (var i = 0; i < leaders.Count; i++)
        {
            var candidate = leaders[i];
            var id = candidate?.GetTemplate()?.GetID();
            if (id == null || !(id == leader || id.EndsWith("." + leader, StringComparison.Ordinal)))
                continue;
            var perks = new List<string>();
            for (var rank = 0; rank < candidate.GetPerkCount(); rank++)
                perks.Add(candidate.GetPerkByRank((UnitRankType)rank)?.GetID() ?? "?");
            return perks;
        }
        return new { error = $"no hired leader matching '{leader}'" };
    }
}
