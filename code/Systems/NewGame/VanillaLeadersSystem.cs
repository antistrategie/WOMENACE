using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Jiangyu.Game;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Enforces the "disable vanilla squad leaders and pilots" new-game option: when it is on for the
// campaign, the vanilla leaders are removed wherever the game hands out leaders, and every
// mod-added leader (WOMENACE's dolls and any other mod's custom leaders) stays offered. Vanilla is
// recognised by asset provenance (see IsVanilla), so no leader roster is maintained here and the
// filter tracks whatever leaders a game patch or another mod introduces.
//
// Three entry points are filtered:
//   - the new-game initial-leader pick, by narrowing StrategyConfig.InitialPickableUnitLeaders when
//     START GAME is pressed, before the pick dialog reads it,
//   - the dossier hiring pools, by narrowing DossierItemTemplate.m_UnlockedLeaders for the duration
//     of each Redeem,
//   - the black market stock, by removing any dossier whose effective pool is exhausted (a pilot
//     dossier with no modded pilot to grant, a squad-leader dossier once every modded leader is
//     acquired) so a dead, un-redeemable dossier never sits on the shelf.
// The pick filter reads the live box choice (the option is not committed to the campaign until the
// campaign scene loads, which is after the pick), the dossier and market filters read the committed
// per-campaign option so they keep working after a save reload.
//
// When the option is off this system leaves the leader pools alone: other mods (custom squad
// leaders, all-leaders-pickable tweaks) own whatever they put there. The only write the off path
// ever makes is re-adding vanilla leaders this system itself removed on an earlier press, and that
// merge only adds entries, so another mod's pool edits are never overwritten and a merge that finds
// nothing missing writes nothing.
public sealed class VanillaLeadersSystem : JiangyuSystem
{
    private const string StrategyConfigId = "strategy_config";

    // Every vanilla leader the pick filter has removed this session, in pool order. The narrow must
    // outlive ExecNewGame (the pick dialog reads the pool later, from the campaign scene), so it is
    // undone on the next press by merging these back into whatever pool is live then. A merge never
    // removes or reorders other entries and retries on every press, so a pool another mod replaced,
    // a Current config minted from the narrowed template, or a press whose merge threw all heal on
    // the next press instead of losing the removed leaders for the session.
    private readonly List<UnitLeaderTemplate> _removedVanilla = new();

    // Every doll the show-all-dolls widening has appended this session, the mirror ledger of
    // _removedVanilla: the next press removes exactly these before deciding afresh, so unticking the
    // option restores the authored pick pool without touching entries anyone else put there.
    private readonly List<UnitLeaderTemplate> _addedDolls = new();

    // The dossiers whose leader rosters union into "all dolls" for the widening.
    private static readonly string[] DollDossierIds = { "dossier.squad_leader", "dossier.pilot" };

    // Redeem transient-filter state (redeems are not re-entrant, so one slot suffices).
    private DossierItemTemplate _swappedDossier;
    private Il2CppReferenceArray<UnitLeaderTemplate> _savedUnlocked;
    private Il2CppReferenceArray<UnitLeaderTemplate> _writtenUnlocked;

    // Guards the market scrub against re-entering itself via the window rebuild it can trigger.
    private bool _scrubbing;

    public override void OnInit()
    {
        // START GAME: merge back any previous narrowing, then narrow the live pool if the option is on.
        Context.Patches.Prefix("Il2CppMenace.UI.Menu.NewGameWindow", "ExecNewGame", _ => ApplyPickFilter());

        // Each dossier redeem: while the option is on, roll only from modded leaders.
        Context.Patches.Prefix("Il2CppMenace.Items.DossierItemTemplate", "Redeem", OnRedeemPre);
        Context.Patches.Postfix("Il2CppMenace.Items.DossierItemTemplate", "Redeem", OnRedeemPost);

        // After the black market restocks, drop any dossier that can no longer grant a modded
        // leader. Restock is the outermost fill path (it drives the dossier fill-up and dedupe, and
        // the reroll item restocks through it), so scrubbing in its postfix runs once, after
        // everything the game adds has settled. Only the outer Restock is hooked, never the inner
        // fill-up it calls, so the scrub never mutates the market mid-fill.
        Context.Patches.Postfix("Il2CppMenace.Strategy.BlackMarket", "Restock", OnMarketRestocked);

        // Also scrub just before the market window builds its item list, so a save loaded
        // mid-session (or any refresh that did not route through Restock) never shows a dead dossier.
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.BlackMarketUIScreen", "UpdateWindow", OnMarketWindowUpdating);
    }

    // Heal, then narrow. Both steps read the pool live at press time (not from a snapshot), so
    // leaders other mods added to it at any earlier point pass through untouched. Applied to both
    // the config template and the live Current config, since the campaign may read either.
    private void ApplyPickFilter()
    {
        try
        {
            var disable = NewGameSettings.Pending.DisableVanillaLeaders;
            var showAll = NewGameSettings.Pending.ShowAllDolls;
            var sawConfig = false;
            foreach (var config in Configs())
            {
                sawConfig = true;
                MergeBackRemoved(config);
                RemoveAddedDolls(config);
                if (disable)
                    NarrowPickPool(config);
                if (showAll)
                    WidenPickPool(config);
            }

            // The filter cannot act without a config, which should never happen: say so rather than
            // leave an inert option-on press with no vanilla-leaders line in the log at all.
            if (!sawConfig && disable)
                Context.Log.Warn("vanilla-leaders: no strategy config resolved, initial-pick filter inactive");
        }
        catch (Exception ex) { Context.Log.Warn($"vanilla-leaders: pick filter failed: {ex.Message}"); }
    }

    // Re-add every vanilla leader this system removed earlier in the session that is missing from
    // the config's pool, at the front (the position the game ships them in, ahead of the appended
    // modded leaders). Idempotent, and additive only.
    private void MergeBackRemoved(StrategyConfig config)
    {
        try
        {
            if (_removedVanilla.Count == 0)
                return;
            var pool = ToList(config.InitialPickableUnitLeaders);
            var missing = _removedVanilla.Where(r => !pool.Any(p => p.Pointer == r.Pointer)).ToList();
            if (missing.Count == 0)
                return;
            config.InitialPickableUnitLeaders = ToArray(missing.Concat(pool));
            Context.Log.Info($"vanilla-leaders: merged {missing.Count} removed vanilla leader(s) back into the initial pick pool");
        }
        catch (Exception ex) { Context.Log.Warn($"vanilla-leaders: pick pool merge-back failed: {ex.Message}"); }
    }

    // Drop the dolls an earlier show-all press appended, so each press decides afresh from the
    // authored pool. Only entries in the _addedDolls ledger are touched: a doll strategy_config
    // registers itself is never in the ledger (the widening skips entries already present), so
    // this cannot remove anything this system did not add.
    private void RemoveAddedDolls(StrategyConfig config)
    {
        try
        {
            if (_addedDolls.Count == 0)
                return;
            var pool = ToList(config.InitialPickableUnitLeaders);
            var kept = pool.Where(p => !_addedDolls.Any(a => a.Pointer == p.Pointer)).ToList();
            if (kept.Count == pool.Count)
                return;
            config.InitialPickableUnitLeaders = ToArray(kept);
            Context.Log.Info($"vanilla-leaders: removed {pool.Count - kept.Count} show-all doll(s) from the initial pick pool");
        }
        catch (Exception ex) { Context.Log.Warn($"vanilla-leaders: show-all removal failed: {ex.Message}"); }
    }

    // Append every WOMENACE doll missing from the pick pool, sourced from the dossier rosters (the
    // one registry that lists every doll, including those strategy_config leaves out of the initial
    // pick). Additive only, filtered to our own leaders by speaker tag, so vanilla entries and other
    // mods' leaders are never dragged in.
    private void WidenPickPool(StrategyConfig config)
    {
        try
        {
            var pool = ToList(config.InitialPickableUnitLeaders);
            var added = new List<UnitLeaderTemplate>();
            foreach (var dossierId in DollDossierIds)
            {
                var dossier = Templates.ById<DossierItemTemplate>(dossierId);
                var leaders = dossier?.m_UnlockedLeaders;
                if (leaders == null)
                    continue;
                for (var i = 0; i < leaders.Length; i++)
                {
                    var leader = leaders[i];
                    if (leader == null || !IsOurs(leader))
                        continue;
                    if (pool.Any(p => p.Pointer == leader.Pointer) || added.Any(a => a.Pointer == leader.Pointer))
                        continue;
                    added.Add(leader);
                }
            }
            if (added.Count == 0)
                return;
            config.InitialPickableUnitLeaders = ToArray(pool.Concat(added));
            foreach (var leader in added)
                if (!_addedDolls.Any(a => a.Pointer == leader.Pointer))
                    _addedDolls.Add(leader);
            Context.Log.Info($"vanilla-leaders: widened the initial pick pool with {added.Count} doll(s)");
        }
        catch (Exception ex) { Context.Log.Warn($"vanilla-leaders: show-all widening failed: {ex.Message}"); }
    }

    private void NarrowPickPool(StrategyConfig config)
    {
        try
        {
            var original = config.InitialPickableUnitLeaders;
            if (original == null || !TryFilterVanilla(original, out var kept, out var removed))
                return;

            // Never hand the pick dialog an empty pool: if no modded leader resolves (the WOMENACE
            // templates failed to load and no other mod added one), keep the full pool so the new
            // game is still startable, and warn rather than soft-lock.
            if (kept.Count == 0)
            {
                Context.Log.Warn("vanilla-leaders: narrowing would empty the initial pick pool, keeping the full pool");
                return;
            }

            config.InitialPickableUnitLeaders = ToArray(kept);
            foreach (var leader in removed)
                if (!_removedVanilla.Any(r => r.Pointer == leader.Pointer))
                    _removedVanilla.Add(leader);
            Context.Log.Info($"vanilla-leaders: initial pick pool narrowed to {kept.Count} modded leader(s) (dropped {removed.Count} vanilla)");
        }
        catch (Exception ex) { Context.Log.Warn($"vanilla-leaders: pick pool narrow failed: {ex.Message}"); }
    }

    private void OnRedeemPre(PatchInfo info)
    {
        // Heal a swap a previous Redeem left un-restored: the SDK exposes only a Harmony postfix (no
        // finalizer), so if the original Redeem threw, OnRedeemPost did not run and the shared
        // template stayed narrowed. Restoring here on the next Redeem keeps a throw from leaking the
        // narrowed pool onto the shared dossier for the rest of the session.
        RestoreSwapped();
        try
        {
            if (!NewGameSettings.DisableVanillaLeaders(Context))
                return;
            var dossier = (info.Instance as Il2CppObjectBase)?.TryCast<DossierItemTemplate>();
            var original = dossier?.m_UnlockedLeaders;
            if (original == null || !TryFilterVanilla(original, out var kept, out _))
                return;   // no vanilla leaders in this pool, nothing to filter

            // Skip on grantability, not mere membership: if no modded entry is still available to
            // grant (a pilot dossier with no modded pilots, or a squad-leader dossier whose modded
            // leaders are all acquired) block the roll rather than let it roll an exhausted pool.
            // Mirrors the market scrub, so an un-scrubbed dossier still behaves.
            var roster = StrategyState.Get()?.Roster;
            var grantable = roster == null ? kept.Count : kept.Count(t => IsGrantable(t, roster));
            if (grantable == 0)
            {
                info.Skip = true;
                return;
            }

            var written = ToArray(kept);
            _swappedDossier = dossier;
            _savedUnlocked = original;
            _writtenUnlocked = written;
            dossier.m_UnlockedLeaders = written;
        }
        catch (Exception ex) { Context.Log.Warn($"vanilla-leaders: dossier filter (pre) failed: {ex.Message}"); }
    }

    private void OnRedeemPost(PatchInfo info) => RestoreSwapped();

    // Put a narrowed dossier's original pool back, and clear the slot. Safe to call when nothing is
    // swapped. Runs from OnRedeemPost and defensively from the next OnRedeemPre. Restores only when
    // the dossier still holds the exact array this system wrote: if another mod replaced the pool in
    // the meantime (possible on the delayed heal path), the current array is theirs to keep.
    private void RestoreSwapped()
    {
        try
        {
            if (_swappedDossier != null && _savedUnlocked != null && _writtenUnlocked != null
                && _swappedDossier.m_UnlockedLeaders?.Pointer == _writtenUnlocked.Pointer)
                _swappedDossier.m_UnlockedLeaders = _savedUnlocked;
        }
        catch (Exception ex) { Context.Log.Warn($"vanilla-leaders: dossier restore failed: {ex.Message}"); }
        finally
        {
            _swappedDossier = null;
            _savedUnlocked = null;
            _writtenUnlocked = null;
        }
    }

    private void OnMarketRestocked(PatchInfo info)
        => ScrubMarket((info.Instance as Il2CppObjectBase)?.TryCast<BlackMarket>());

    private void OnMarketWindowUpdating(PatchInfo info)
        => ScrubMarket(StrategyState.Get()?.BlackMarket);

    // Remove every stocked dossier that can no longer grant a modded leader, when the option is
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

    // Add any stocked dossier that can no longer grant a modded leader to `dead`. A dossier is
    // exhausted when none of its modded pool entries is still status Unknown (never acquired):
    // a pilot dossier with only vanilla pilots has no modded entries at all, and a squad-leader
    // dossier reaches this once every modded leader has been picked or hired. Mirrors what Redeem
    // would find grantable.
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

    // A leader still grantable by a dossier: mod-added and never acquired (status Unknown, the same
    // entry the game's own redeem would roll). Shared by the market scrub and the Redeem skip so the
    // two cannot disagree on what "exhausted" means. A form-swap doll wearing her alt form reads as
    // Unknown here (the swap stashes her base form out of the roster), but she is acquired: her base
    // form is never grantable while the alt is active.
    private static bool IsGrantable(UnitLeaderTemplate leader, Roster roster)
    {
        if (leader == null || IsVanilla(leader))
            return false;
        if (FormSwapSystem.BaseFormStashed(leader.GetID()))
            return false;
        roster.GetLeaderByTemplate(leader, out var status);
        return status == UnitLeaderStatus.Unknown;
    }

    // Split a leader pool into mod-added entries to keep and vanilla entries to drop, shared by the
    // pick filter and the dossier filter so the two cannot disagree on which leaders count as
    // vanilla. Returns false when rewriting the pool would change nothing, judged against the raw
    // array length so a pool with null padding is still scrubbed by the swap.
    private static bool TryFilterVanilla(Il2CppReferenceArray<UnitLeaderTemplate> original,
        out List<UnitLeaderTemplate> kept, out List<UnitLeaderTemplate> removed)
    {
        kept = new List<UnitLeaderTemplate>();
        removed = new List<UnitLeaderTemplate>();
        for (var i = 0; i < original.Length; i++)
        {
            var leader = original[i];
            if (leader == null)
                continue;
            if (IsVanilla(leader))
                removed.Add(leader);
            else
                kept.Add(leader);
        }
        return kept.Count != original.Length;
    }

    // A vanilla leader is one the game loaded from its own asset files, recognised by Unity object
    // provenance: objects deserialised from persistent assets carry a positive instance id, objects
    // created at runtime carry a negative one. Every known mod-added leader is a runtime creation
    // (templates are Il2Cpp game types, so mods clone them in code rather than shipping them inside
    // asset bundles), so no leader roster is baked in and a game patch that adds leaders needs no
    // mod update. WOMENACE's own dolls are additionally recognised by their wmgfl speaker tag, so
    // even if the provenance convention ever broke, the toggle would degrade to offering too many
    // leaders, never to hiding the dolls or emptying the pick pool.
    private static bool IsVanilla(UnitLeaderTemplate leader)
    {
        try
        {
            if (leader == null)
                return false;
            if (leader.GetInstanceID() <= 0)
                return false;   // runtime-created: mod-added, whoever made it
            return !IsOurs(leader);
        }
        catch { return false; }
    }

    // A WOMENACE leader carries the shared character marker on its speaker Tags. The marker string
    // is sourced from Affinity.Tag so it stays single-defined, but this is only a "belongs to us"
    // check, distinct from Affinity's per-character identity parsing.
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
