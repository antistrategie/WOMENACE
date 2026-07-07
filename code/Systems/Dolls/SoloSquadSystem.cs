using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Solo squads: the leaders registered here fight alone (3rd-generation dolls
// lead a squad of one) and run attributes past the vanilla 100 ceiling
// (Sextans' Vitality 400 buys the 80 HP a squad-of-one needs, via the
// AttributeOverrides map below).
//
// Squad size is held by three pieces. The valid-squaddie range collapses to
// zero for them (postfixes on GetMin/MaxValidSquaddies override the returned
// counts, so mission prep accepts them without squaddies). Any squaddies
// they somehow carry are stripped back off. And the squaddie row on their
// unit window (count, plus/minus, edit) is hidden on both the armoury and
// mission prep screens, since both build the same UnitWindow.
//
// Attributes are held by repair, not by detour: the ceiling is enforced
// wherever the game WRITES attribute values (the hire-time mint, growth
// rolls, the save round-trip), and detouring those paths crashes natively,
// so any attribute sitting BELOW its template value is written back up
// straight into the raw m_Values array, where no clamp runs. The repair
// fires on the strategy scene loading (before any UI can show stale values)
// and on every validation pass (which no flow can skip and which always
// precedes a deploy, so hitpoints are derived from repaired values). Values
// a growth roll pushed ABOVE the template are left alone.
public sealed class SoloSquadSystem : JiangyuSystem
{

    // InitialAttributes index order: agility 0, weapon_skill 1, valour 2,
    // toughness 3, vitality 4, precision 5, positioning 6.
    private const int VitalityIndex = 4;

    // Unit template ids of leaders that run solo (BaseUnitLeader.GetTemplate
    // returns the unit template, not the leader template). Add new solo
    // dolls here.
    private static readonly HashSet<string> SoloLeaderIds = new(StringComparer.Ordinal)
    {
        "player_squad.sextans",
    };

    // Attribute targets beyond what a template can carry: InitialAttributes
    // store as bytes (255 ceiling), the live values are floats. The repair
    // raises a solo leader's value to max(template, override). Keyed by
    // unit template id, then attribute index (agility 0, weapon_skill 1,
    // valour 2, toughness 3, vitality 4, precision 5, positioning 6).
    private static readonly Dictionary<string, Dictionary<int, float>> AttributeOverrides = new(StringComparer.Ordinal)
    {
        ["player_squad.sextans"] = new()
        {
            [VitalityIndex] = 500f,
        },
    };

    public override void OnInit()
    {
        // The patch targets are the CONCRETE SquadLeader overrides.
        // Detouring the virtuals on BaseUnitLeader crashes the game during
        // boot, before the main menu; the derived implementations detour
        // cleanly. TryAddSquaddie is non-virtual on the base, so it is safe
        // to patch where it lives.
        Context.Patches.Postfix("Il2CppMenace.Strategy.SquadLeader", "GetMinValidSquaddies", OnGetMinValidSquaddies);
        Context.Patches.Postfix("Il2CppMenace.Strategy.SquadLeader", "GetMaxValidSquaddies", OnGetMaxValidSquaddies);
        Context.Patches.Postfix("Il2CppMenace.Strategy.BaseUnitLeader", "TryAddSquaddie", OnTryAddSquaddie);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "SetLeader", OnWindowChanged);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "Refresh", OnWindowChanged);
        // Selection screens (initial squad hire, armoury, mission prep)
        // build their unit cards from this slot with freshly minted preview
        // leaders that never pass squaddie validation: repair BEFORE the
        // slot draws its numbers.
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.BaseUnitSelectSlot", "Init", OnSelectSlotInit);
        // The hiring dialog's detail panel is fed a minted preview leader
        // through these, not through the slots: repair before they draw.
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.UnitStatsAndAttributesPanel", "Update", OnSelectSlotInit);
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.HiringUnitInfo", "Init", OnSelectSlotInit);
    }

    internal static bool IsSoloUnitTemplateId(string id)
        => id != null && SoloLeaderIds.Contains(id);

    internal static bool IsSolo(BaseUnitLeader leader)
    {
        try
        {
            var template = leader?.GetTemplate();
            return IsSoloUnitTemplateId(template?.GetID() ?? template?.name);
        }
        catch
        {
            return false;
        }
    }

    // TryRemoveSquaddie can re-enter the valid-range getters mid-strip.
    private static bool _stripping;

    // A solo leader needs no squaddies: the deploy validation (the "needs at
    // least N squaddies" gate) reads this and passes at zero. The getters
    // run on every validation pass, which makes them the enforcement point
    // no flow can skip: a leader hired and deployed without ever being
    // SELECTED never goes through the unit window, but cannot reach a
    // mission without being validated.
    private void OnGetMinValidSquaddies(PatchInfo info)
    {
        var leader = (info.Instance as Il2CppObjectBase)?.TryCast<BaseUnitLeader>();
        if (leader == null || !IsSolo(leader))
            return;
        info.Result = 0;
        if (!_stripping)
            StripSquaddies(leader);
        ReconcileAttributes(leader);
    }

    // The strategy scene coming up (new game, load) reconciles the hired
    // roster before any screen can draw a clamped value. Unhired preview
    // leaders (hiring screens mint temporaries; the hirable pool itself
    // holds only templates) are repaired by the window path when drawn.
    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        try
        {
            var leaders = Il2CppMenace.States.StrategyState.Get()?.Roster?.m_HiredLeaders;
            for (var i = 0; leaders != null && i < leaders.Count; i++)
            {
                var leader = leaders[i];
                if (leader != null && IsSolo(leader))
                    ReconcileAttributes(leader);
            }
        }
        catch
        {
            // no strategy state on this scene: nothing to reconcile
        }
    }

    private void ReconcileAttributes(BaseUnitLeader leader)
    {
        try
        {
            var template = leader.LeaderTemplate;
            var initial = template?.InitialAttributes;
            var values = leader.GetAttributes()?.m_Values;
            if (initial == null || values == null)
                return;

            // Key the override with the same id-or-name fallback IsSolo
            // uses, or a leader matched by name (null GetID) would miss its
            // override and deploy at the byte-capped value.
            var unitId = leader.GetTemplate()?.GetID() ?? leader.GetTemplate()?.name;
            AttributeOverrides.TryGetValue(unitId ?? "", out var overrides);

            var repaired = 0;
            var count = Math.Min(initial.Length, values.Length);
            for (var i = 0; i < count; i++)
            {
                var target = (float)initial[i];
                if (overrides != null && overrides.TryGetValue(i, out var overridden) && overridden > target)
                    target = overridden;
                if (values[i] >= target)
                    continue;
                values[i] = target;
                repaired++;
            }

            // Attribute-derived stats (hitpoints, accuracy, ...) are CACHED
            // on the leader's properties and only refreshed by the game's own
            // recompute, so a raw value repair alone leaves them stale.
            // Rebuild when we changed a value, or when the cached hitpoints
            // disagree with what the current vitality implies (a freshly
            // loaded save carries correct values next to a stale cache). In
            // steady state neither holds, so the hot-path callers (panel
            // refresh, squaddie validation) skip the recompute.
            var needsRebuild = repaired > 0;
            if (!needsRebuild && count > VitalityIndex)
            {
                var expectedHp = UnitLeaderAttributes.GetHitpointsPerElement((int)values[VitalityIndex]);
                var cachedHp = (int)leader.GetEntityProperty(Il2CppMenace.Tactical.EntityPropertyType.HitpointsPerElement);
                needsRebuild = cachedHp != expectedHp;
            }
            if (needsRebuild)
            {
                leader.UpdatePropertiesBasedOnAttributes();
                if (repaired > 0)
                    Context.Log.Debug($"solo squad: repaired {repaired} clamped attribute(s) on '{template.GetID()}'");
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"solo squad: attribute repair failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Max collapses to zero too, so the game's own squaddie validation and
    // any auto-fill logic treat her squad as full at one.
    private void OnGetMaxValidSquaddies(PatchInfo info)
    {
        var leader = (info.Instance as Il2CppObjectBase)?.TryCast<BaseUnitLeader>();
        if (leader == null || !IsSolo(leader))
            return;
        info.Result = 0;
    }

    // Belt and braces at the source: an add on a solo leader is undone on
    // the spot and reported as refused, whatever path issued it.
    private void OnTryAddSquaddie(PatchInfo info)
    {
        try
        {
            var leader = (info.Instance as Il2CppObjectBase)?.TryCast<BaseUnitLeader>();
            if (leader == null || !IsSolo(leader))
                return;
            var added = info.Result is bool ok && ok;
            var squaddieId = info.Args != null && info.Args.Count > 0 && info.Args[0] is int id ? id : (int?)null;
            if (added && squaddieId.HasValue)
                leader.TryRemoveSquaddie(squaddieId.Value);
            info.Result = false;
            Context.Log.Debug($"solo squad: refused squaddie add on {leader.GetTemplate()?.GetID()}");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"solo squad: add refusal failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Shared prefix for every UI entry point that receives a leader about to
    // be drawn: repair first, then let the native code render.
    private void OnSelectSlotInit(PatchInfo info)
    {
        try
        {
            var leader = (info.Args is { Count: > 0 } ? info.Args[0] : null) as BaseUnitLeader;
            if (leader != null && IsSolo(leader))
                ReconcileAttributes(leader);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"solo squad: leader display repair failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Windows are reused across leaders, so the row is restored whenever the
    // window shows a regular squad leader.
    private void OnWindowChanged(PatchInfo info)
    {
        try
        {
            if (info.Instance is not VisualElement window)
                return;
            var squaddies = UI.Find(window, UiSelector.Name("Squaddies"));
            if (squaddies == null)
                return;
            var leader = Affinity.LeaderOf(window);
            if (leader == null)
                return;
            var solo = IsSolo(leader);
            squaddies.style.display = solo ? DisplayStyle.None : DisplayStyle.Flex;
            if (solo)
            {
                Context.Log.Debug($"solo squad: window shows {leader.GetTemplate()?.GetID()}, minValid={leader.GetMinValidSquaddies()}, maxValid={leader.GetMaxValidSquaddies()}, assigned={leader.m_SquaddieIds?.Count ?? -1}");
                StripSquaddies(leader);
                // hiring screens show leaders that never went through
                // squaddie validation: repair when the window draws them
                ReconcileAttributes(leader);
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"solo squad: window update failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void StripSquaddies(BaseUnitLeader leader)
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
            Context.Log.Debug($"solo squad: stripped {ids.Count} squaddie(s) from {leader.GetTemplate()?.GetID()}");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"solo squad: squaddie strip failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _stripping = false;
        }
    }
}
