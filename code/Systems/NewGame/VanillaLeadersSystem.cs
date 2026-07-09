using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Jiangyu.Game;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Enforces the "disable vanilla squad leaders and pilots" new-game option: when it is on for the
// campaign, only WOMENACE (Girls' Frontline) leaders are offered wherever the game hands out
// leaders. A WOMENACE leader is any whose speaker carries the shared "wmgfl" tag (Affinity.Tag),
// so the filter needs no per-leader allow-list and never drifts from the roster of dolls.
//
// Three entry points are filtered:
//   - the new-game initial-leader pick, by narrowing StrategyConfig.InitialPickableUnitLeaders when
//     START GAME is pressed, before the pick dialog reads it,
//   - the dossier hiring pools, by narrowing DossierItemTemplate.m_UnlockedLeaders for the duration
//     of each Redeem,
//   - the black market stock, by removing any dossier whose effective pool is exhausted (the pilot
//     dossier always, since there are no WOMENACE pilots, and the squad-leader dossier once every
//     doll is acquired) so a dead, un-redeemable dossier never sits on the shelf.
// The pick filter reads the live box choice (the option is not committed to the campaign until the
// campaign scene loads, which is after the pick), the dossier and market filters read the committed
// per-campaign option so they keep working after a save reload.
public sealed class VanillaLeadersSystem : JiangyuSystem
{
    private const string StrategyConfigId = "strategy_config";

    // The full initial-pick pool (vanilla leaders plus the WOMENACE dolls appended by common.kdl),
    // captured once so the pool can be restored when the option is off.
    private List<UnitLeaderTemplate> _fullPickable;

    // Redeem transient-filter state (redeems are not re-entrant, so one slot suffices).
    private DossierItemTemplate _swappedDossier;
    private Il2CppReferenceArray<UnitLeaderTemplate> _savedUnlocked;

    // Guards the market scrub against re-entering itself via the window rebuild it can trigger.
    private bool _scrubbing;

    public override void OnTemplatesApplied()
    {
        var config = Templates.ById<StrategyConfig>(StrategyConfigId, msg => Context.Log.Warn($"vanilla-leaders: {msg}"));
        if (config == null)
        {
            Context.Log.Warn("vanilla-leaders: strategy_config not found, initial-pick filter disabled");
            return;
        }
        _fullPickable = ToList(config.InitialPickableUnitLeaders);
    }

    public override void OnInit()
    {
        // START GAME: narrow (or restore) the initial-pick pool before the pick dialog reads it.
        Context.Patches.Prefix("Il2CppMenace.UI.Menu.NewGameWindow", "ExecNewGame", _ => ApplyPickFilter());

        // Each dossier redeem: while the option is on, roll only from WOMENACE leaders.
        Context.Patches.Prefix("Il2CppMenace.Items.DossierItemTemplate", "Redeem", OnRedeemPre);
        Context.Patches.Postfix("Il2CppMenace.Items.DossierItemTemplate", "Redeem", OnRedeemPost);

        // After the black market restocks, drop any dossier that can no longer grant a WOMENACE
        // leader. Restock is the outermost fill path (it drives the dossier fill-up and dedupe, and
        // the reroll item restocks through it), so scrubbing in its postfix runs once, after
        // everything the game adds has settled. Only the outer Restock is hooked, never the inner
        // fill-up it calls, so the scrub never mutates the market mid-fill.
        Context.Patches.Postfix("Il2CppMenace.Strategy.BlackMarket", "Restock", OnMarketRestocked);

        // Also scrub just before the market window builds its item list, so a save loaded
        // mid-session (or any refresh that did not route through Restock) never shows a dead dossier.
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.BlackMarketUIScreen", "UpdateWindow", OnMarketWindowUpdating);
    }

    // Narrow the initial-pick pool to WOMENACE leaders when the box has the option on, else restore
    // the full pool. Applied to both the config template and the live Current config, since the
    // campaign may read either. Recomputed from the cached full pool each time, so it self-corrects
    // whether the previous new game had the option on or off.
    private void ApplyPickFilter()
    {
        try
        {
            if (_fullPickable == null)
                return;
            var disable = NewGameSettings.Pending.DisableVanillaLeaders;
            var ours = disable ? _fullPickable.Where(IsOurs).ToList() : null;

            // Never hand the pick dialog an empty pool: if the option is on but no WOMENACE leader
            // resolves (tags unresolved, roster of dolls empty), fall back to the full pool so the
            // new game is still startable, and warn rather than soft-lock.
            List<UnitLeaderTemplate> kept;
            if (disable && ours.Count > 0)
                kept = ours;
            else
            {
                kept = _fullPickable;
                if (disable)
                    Context.Log.Warn("vanilla-leaders: no WOMENACE leaders resolved for the initial pick, keeping the full pool");
            }

            // One array, shared by both configs (the pool is read-only to the pick dialog).
            var pool = ToArray(kept);
            foreach (var config in Configs())
                config.InitialPickableUnitLeaders = pool;
            Context.Log.Info($"vanilla-leaders: initial pick pool = {(kept == _fullPickable ? "full" : "WOMENACE only")} ({kept.Count} leaders)");
        }
        catch (Exception ex) { Context.Log.Warn($"vanilla-leaders: pick filter failed: {ex.Message}"); }
    }

    private void OnRedeemPre(PatchInfo info)
    {
        // Heal a swap a previous Redeem left un-restored: the SDK exposes only a Harmony postfix (no
        // finalizer), so if the original Redeem threw, OnRedeemPost did not run and the shared
        // template stayed narrowed. Restoring here on the next Redeem keeps a throw from leaking the
        // WOMENACE-only pool onto the shared dossier for the rest of the session.
        RestoreSwapped();
        try
        {
            if (!NewGameSettings.DisableVanillaLeaders(Context))
                return;
            var dossier = (info.Instance as Il2CppObjectBase)?.TryCast<DossierItemTemplate>();
            var original = dossier?.m_UnlockedLeaders;
            if (original == null)
                return;

            var kept = ToList(original).Where(IsOurs).ToList();
            if (kept.Count == original.Length)
                return;   // no vanilla leaders in this pool, nothing to filter

            // Skip on grantability, not mere membership: if no WOMENACE entry is still available to
            // grant (a pilot dossier with no dolls, or a squad-leader dossier whose dolls are all
            // acquired) block the roll rather than let it roll an exhausted pool. Mirrors the market
            // scrub, so an un-scrubbed dossier still behaves.
            var roster = StrategyState.Get()?.Roster;
            var grantable = roster == null ? kept.Count : kept.Count(t => IsGrantable(t, roster));
            if (grantable == 0)
            {
                info.Skip = true;
                return;
            }

            _swappedDossier = dossier;
            _savedUnlocked = original;
            dossier.m_UnlockedLeaders = ToArray(kept);
        }
        catch (Exception ex) { Context.Log.Warn($"vanilla-leaders: dossier filter (pre) failed: {ex.Message}"); }
    }

    private void OnRedeemPost(PatchInfo info) => RestoreSwapped();

    // Put a narrowed dossier's original pool back, and clear the slot. Safe to call when nothing is
    // swapped. Runs from OnRedeemPost and defensively from the next OnRedeemPre.
    private void RestoreSwapped()
    {
        try
        {
            if (_swappedDossier != null && _savedUnlocked != null)
                _swappedDossier.m_UnlockedLeaders = _savedUnlocked;
        }
        catch (Exception ex) { Context.Log.Warn($"vanilla-leaders: dossier restore failed: {ex.Message}"); }
        finally
        {
            _swappedDossier = null;
            _savedUnlocked = null;
        }
    }

    private void OnMarketRestocked(PatchInfo info)
        => ScrubMarket((info.Instance as Il2CppObjectBase)?.TryCast<BlackMarket>());

    private void OnMarketWindowUpdating(PatchInfo info)
        => ScrubMarket(StrategyState.Get()?.BlackMarket);

    // Remove every stocked dossier that can no longer grant a WOMENACE leader, when the option is
    // on. Guarded against re-entrancy: RemoveItem can raise a market-changed event that rebuilds the
    // window, which would re-enter this through the UpdateWindow prefix.
    private void ScrubMarket(BlackMarket market)
    {
        if (_scrubbing || market == null)
            return;
        try
        {
            if (!NewGameSettings.DisableVanillaLeaders(Context))
                return;
            var roster = StrategyState.Get()?.Roster;
            if (roster == null)
                return;

            _scrubbing = true;
            var dead = new List<BaseItem>();
            CollectDeadDossiers(market, false, roster, dead);
            CollectDeadDossiers(market, true, roster, dead);
            foreach (var item in dead)
                market.RemoveItem(item);
            if (dead.Count > 0)
                Context.Log.Info($"vanilla-leaders: removed {dead.Count} exhausted dossier(s) from the black market");
        }
        catch (Exception ex) { Context.Log.Warn($"vanilla-leaders: market scrub failed: {ex.Message}"); }
        finally { _scrubbing = false; }
    }

    // Add any stocked dossier that can no longer grant a WOMENACE leader to `dead`. A dossier is
    // exhausted when none of its WOMENACE pool entries is still status Unknown (never acquired):
    // the pilot dossier has no WOMENACE entries at all, and the squad-leader dossier reaches this
    // once every doll has been picked or hired. Mirrors what Redeem would find grantable.
    private void CollectDeadDossiers(BlackMarket market, bool specialOffers, Roster roster, List<BaseItem> dead)
    {
        var buffer = new Il2CppSystem.Collections.Generic.List<BaseItem>();
        market.GetInstances(buffer, specialOffers);
        for (var i = 0; i < buffer.Count; i++)
        {
            var item = buffer[i];
            var dossier = item?.GetBaseItemTemplate()?.TryCast<DossierItemTemplate>();
            if (dossier != null && !HasGrantableLeader(dossier, roster))
                dead.Add(item);
        }
    }

    private static bool HasGrantableLeader(DossierItemTemplate dossier, Roster roster)
    {
        var pool = dossier.m_UnlockedLeaders;
        // No populated leader pool: not a dossier this filter judges (a non-leader dossier, or one
        // that fills its pool lazily). Leave it on the shelf rather than scrub it off.
        if (pool == null || pool.Length == 0)
            return true;
        for (var i = 0; i < pool.Length; i++)
            if (IsGrantable(pool[i], roster))
                return true;
        return false;
    }

    // A leader still grantable by a dossier: one of ours and never acquired (status Unknown, the same
    // entry the game's own redeem would roll). Shared by the market scrub and the Redeem skip so the
    // two cannot disagree on what "exhausted" means.
    private static bool IsGrantable(UnitLeaderTemplate leader, Roster roster)
    {
        if (leader == null || !IsOurs(leader))
            return false;
        roster.GetLeaderByTemplate(leader, out var status);
        return status == UnitLeaderStatus.Unknown;
    }

    // A WOMENACE leader carries the shared character marker on its speaker Tags. Vanilla leaders
    // carry none, so "keep only ours" drops exactly the vanilla squad leaders and pilots. The marker
    // string is sourced from Affinity.Tag so it stays single-defined, but this is only a "belongs to
    // us" check, distinct from Affinity's per-character identity parsing.
    private static bool IsOurs(UnitLeaderTemplate leader)
    {
        try
        {
            var speaker = leader != null ? leader.SpeakerTemplate : null;
            var tags = speaker != null && speaker.IsAlive() ? speaker.Tags : null;
            return !string.IsNullOrEmpty(tags) && tags.Contains(Affinity.Tag);
        }
        catch { return false; }
    }

    // The config instances the initial pick may read: the loaded config template and the live
    // Current config, de-duplicated (they are often the same instance).
    private IEnumerable<StrategyConfig> Configs()
    {
        var seen = new HashSet<IntPtr>();
        var template = Templates.ById<StrategyConfig>(StrategyConfigId);
        if (template != null && seen.Add(template.Pointer))
            yield return template;

        StrategyConfig current = null;
        try { current = StrategyConfig.Current; } catch { }
        if (current != null && seen.Add(current.Pointer))
            yield return current;
    }

    private static List<UnitLeaderTemplate> ToList(Il2CppReferenceArray<UnitLeaderTemplate> array)
    {
        var list = new List<UnitLeaderTemplate>();
        if (array == null)
            return list;
        for (var i = 0; i < array.Length; i++)
            if (array[i] != null)
                list.Add(array[i]);
        return list;
    }

    private static Il2CppReferenceArray<UnitLeaderTemplate> ToArray(IEnumerable<UnitLeaderTemplate> items)
    {
        var list = items.Where(x => x != null).ToList();
        var array = new Il2CppReferenceArray<UnitLeaderTemplate>(list.Count);
        for (var i = 0; i < list.Count; i++)
            array[i] = list[i];
        return array;
    }
}
