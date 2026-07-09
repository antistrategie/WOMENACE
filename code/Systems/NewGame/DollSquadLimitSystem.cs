using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// New-game option "Limit doll squad size to 5": while on for the campaign, a WOMENACE leader's
// squad is capped at five bodies (the doll plus at most four squaddie copies). The lever is the
// valid-squaddie range the solo-squad system also drives (a squad of N is the leader plus N-1
// squaddies), enforced the same three ways SoloSquadSystem enforces its squad-of-one, because the
// max getter alone is not enough:
//   - GetMaxValidSquaddies is capped at MaxSquaddies (=4), and GetMinValidSquaddies is capped there
//     too so the min can never exceed the max (a min above the cap would leave the doll unable to
//     satisfy deploy validation, i.e. permanently undeployable),
//   - excess squaddies already assigned are stripped back to the cap on every validation pass (the
//     enforcement point no deploy can skip), for squaddies added through paths that never consult
//     the getter (auto-fill, a pre-filled starting squad, mid-campaign add-squaddie effects),
//   - an add that would push the squad past the cap is undone at the source.
// The cap only ever lowers a count (a smaller squad is left alone) and composes with the solo
// squad's 0 (min wins, so a solo doll stays solo). Only WOMENACE leaders (speaker carries the
// shared marker) are affected, so vanilla squads are untouched.
public sealed class DollSquadLimitSystem : JiangyuSystem
{
    private const int MaxSquadSize = 5;
    private const int MaxSquaddies = MaxSquadSize - 1;

    // TryRemoveSquaddie re-enters the valid-range getters mid-strip, so the getter-driven strip is
    // guarded against recursing into itself.
    private bool _stripping;

    public override void OnInit()
    {
        // Concrete SquadLeader overrides, never the BaseUnitLeader virtuals (detouring those crashes
        // on boot). TryAddSquaddie is non-virtual on the base, so it is patched where it lives. Same
        // targets and reasoning as SoloSquadSystem.
        Context.Patches.Postfix("Il2CppMenace.Strategy.SquadLeader", "GetMaxValidSquaddies", OnGetMaxValidSquaddies);
        Context.Patches.Postfix("Il2CppMenace.Strategy.SquadLeader", "GetMinValidSquaddies", OnGetMinValidSquaddies);
        Context.Patches.Postfix("Il2CppMenace.Strategy.BaseUnitLeader", "TryAddSquaddie", OnTryAddSquaddie);
    }

    private void OnGetMaxValidSquaddies(PatchInfo info) => CapReturn(info);

    private void OnGetMinValidSquaddies(PatchInfo info)
    {
        if (!CapReturn(info))
            return;
        // The getters run on every validation pass, so this is the enforcement point no deploy can
        // skip: trim any squaddies over the cap that slipped in off a non-getter path.
        if (!_stripping)
            TrimExcess(LeaderOf(info));
    }

    // Lower the returned count to the cap for a WOMENACE leader while the option is on. Returns
    // whether this leader is a capped one, so the min getter knows to also trim.
    private bool CapReturn(PatchInfo info)
    {
        try
        {
            if (info.Result is not int count)
                return false;
            if (!NewGameSettings.LimitDollSquadSize(Context))
                return false;
            var leader = LeaderOf(info);
            if (leader == null || Affinity.OurSpeakerTags(leader) == null)
                return false;
            if (count > MaxSquaddies)
                info.Result = MaxSquaddies;
            return true;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"doll squad limit: cap failed: {ex.Message}");
            return false;
        }
    }

    // Undo an add that pushes a capped doll's squad past the cap, at the source, whatever issued it.
    private void OnTryAddSquaddie(PatchInfo info)
    {
        try
        {
            if (info.Result is not bool added || !added)
                return;
            if (!NewGameSettings.LimitDollSquadSize(Context))
                return;
            var leader = LeaderOf(info);
            if (leader == null || Affinity.OurSpeakerTags(leader) == null)
                return;
            if ((leader.m_SquaddieIds?.Count ?? 0) <= MaxSquaddies)
                return;
            var squaddieId = info.Args is { Count: > 0 } && info.Args[0] is int id ? id : (int?)null;
            if (squaddieId.HasValue)
                leader.TryRemoveSquaddie(squaddieId.Value);
            info.Result = false;
        }
        catch (Exception ex) { Context.Log.Warn($"doll squad limit: add refusal failed: {ex.Message}"); }
    }

    private void TrimExcess(BaseUnitLeader leader)
    {
        try
        {
            var current = leader?.m_SquaddieIds;
            if (current == null || current.Count <= MaxSquaddies)
                return;
            _stripping = true;
            var excess = new List<int>();
            for (var i = MaxSquaddies; i < current.Count; i++)
                excess.Add(current[i]);
            foreach (var id in excess)
                leader.TryRemoveSquaddie(id);
            Context.Log.Debug($"doll squad limit: trimmed {excess.Count} squaddie(s) from {leader.GetTemplate()?.GetID()}");
        }
        catch (Exception ex) { Context.Log.Warn($"doll squad limit: trim failed: {ex.Message}"); }
        finally { _stripping = false; }
    }

    private static BaseUnitLeader LeaderOf(PatchInfo info)
        => (info.Instance as Il2CppObjectBase)?.TryCast<BaseUnitLeader>();
}
