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
    private const int AgilityIndex = 0;
    private const int WeaponSkillIndex = 1;
    private const int ValourIndex = 2;
    private const int ToughnessIndex = 3;
    private const int VitalityIndex = 4;
    private const int PrecisionIndex = 5;
    private const int PositioningIndex = 6;

    // The dolls that run solo. Two template families name the same unit and
    // both are needed: BaseUnitLeader.GetTemplate returns the UNIT template
    // (player_squad.<name>), while a UnitLeaderAttributes instance carries the
    // LEADER template it was built from (squad_leader.<name>). Deriving both
    // sets from one list stops them drifting apart. Add new solo dolls here.
    private static readonly string[] SoloDolls = { "sextans", "ots14" };

    private static readonly HashSet<string> SoloLeaderIds =
        new(SoloDolls.Select(d => "player_squad." + d), StringComparer.Ordinal);

    private static readonly HashSet<string> SoloLeaderTemplateIds =
        new(SoloDolls.Select(d => "squad_leader." + d), StringComparer.Ordinal);

    // Attribute targets beyond what a template can carry: InitialAttributes
    // store as bytes (255 ceiling), the live values are floats. The repair
    // raises a solo leader's value to max(template, override). Keyed by
    // unit template id, then attribute index (agility 0, weapon_skill 1,
    // valour 2, toughness 3, vitality 4, precision 5, positioning 6).
    // Every solo doll gets the same vitality target BY DERIVATION, so adding
    // a name to SoloDolls cannot silently ship a doll whose pool stayed at
    // the template byte ceiling. A doll needing different numbers overrides
    // its entry after this initialiser.
    private static readonly Dictionary<string, Dictionary<int, float>> AttributeOverrides = BuildAttributeOverrides();

    private static Dictionary<string, Dictionary<int, float>> BuildAttributeOverrides()
    {
        var overrides = new Dictionary<string, Dictionary<int, float>>(StringComparer.Ordinal);
        foreach (var doll in SoloDolls)
            overrides["player_squad." + doll] = new Dictionary<int, float> { [VitalityIndex] = 500f };
        return overrides;
    }

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
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.UnitStatsAndAttributesPanel", "ShowPanel", OnSelectSlotInit);
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.HiringUnitInfo", "Init", OnSelectSlotInit);

        // Growth cap raise for solo dolls: the native TryIncrease refuses any
        // increase at value >= 100 (ATTRIBUTE_MAX_VALUE) and clamps results to
        // it, so a repaired 100+ attribute never grows. The prefix replaces it
        // for a solo doll's attribute instance only, recognised by its own
        // LeaderTemplate (skip + boolean result, the dispatcher's prefix-result
        // path), with the same maths at cap 500.
        Context.Patches.Prefix("Il2CppMenace.Strategy.UnitLeaderAttributes", "TryIncrease", OnTryIncrease);

        // The COMBAT-side fix for the damage mult: UpdateProperties INLINES
        // the toughness conversion (its input clamp included) when caching
        // EntityProperties.DamageSustainedMult, so the extended converters
        // below never reach the value the damage pipeline reads
        // (disassembly: the clamp+line runs inline and stores to field 0x8c).
        // The postfix rewrites the cached property through the patched
        // converter for a solo doll with over-100 toughness.
        Context.Patches.Postfix("Il2CppMenace.Strategy.UnitLeaderAttributes", "UpdateProperties", OnUpdateProperties);

        // The two derived stats whose conversions CLAMP THE INPUT at 100
        // (disassembly-verified; accuracy, crit, "Defense" and hitpoints are
        // unclamped linears). The postfixes extend the same line past 100 up
        // to the raised cap, deriving slope and intercept from the vanilla
        // function itself at runtime, so vanilla tuning changes carry over.
        // The int variants inline the maths rather than calling the float
        // twins (IL2CPP inlining), so each is patched separately; the
        // AsFraction helpers call these and need nothing. Global patches,
        // but inputs above 100 exist only on solo dolls.
        // GetDamageSustainedMultDecimals is deliberately NOT extended: its
        // shape past the clamp is unverified (it may be the fractional part
        // of the mult, which no line models) and it only feeds display
        // formatting. Attr.Curves samples it for a later decision.
        Context.Patches.Postfix("Il2CppMenace.Strategy.UnitLeaderAttributes", "GetActionPoints", OnActionPointsInt);
        Context.Patches.Postfix("Il2CppMenace.Strategy.UnitLeaderAttributes", "GetActionPointsAsFloat", OnActionPointsFloat);
        Context.Patches.Postfix("Il2CppMenace.Strategy.UnitLeaderAttributes", "GetDamageSustainedMult", OnDamageMultInt);
        Context.Patches.Postfix("Il2CppMenace.Strategy.UnitLeaderAttributes", "GetDamageSustainedMultAsFloat", OnDamageMultFloat);

        // Attribute bars past the vanilla cap rescale to the raised one (per
        // row, only when its value exceeds 100). UnitInfoStat draws the STAT
        // rows through the same call, so the rescale is gated to the window
        // ShowAttributes draws in (it calls ShowProgressBars synchronously
        // per row; the UI is single-threaded). ShowStats clears the flag as
        // belt-and-braces against a draw aborted mid-window.
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.UnitStatsAndAttributesPanel", "ShowAttributes", OnShowAttributesPre);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitStatsAndAttributesPanel", "ShowAttributes", OnShowAttributesPost);
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.UnitStatsAndAttributesPanel", "ShowStats", OnShowStatsPre);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitStatsAndAttributesPanel", "ShowStats", OnShowStatsPost);
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.UnitInfoStat", "ShowProgressBars", OnShowProgressBars);
        // The zero-crossing marker the two signed stat rows carry. ShowStats
        // sets it AFTER drawing each of those bars, so it is corrected here
        // rather than during the draw.
        Context.Patches.Postfix("Il2CppMenace.UI.ProgressBar", "SetShowZeroPositionMarker", OnSetZeroMarker);
    }

    internal static bool IsSoloUnitTemplateId(string id)
        => id != null && SoloLeaderIds.Contains(id);

    // Verdicts memoised by template pointer: IsSolo sits under every squaddie validation pass
    // and IsSoloAttributes under the attribute getters, and each otherwise marshals the id
    // string. Il2cpp's GC does not move objects, so the pointer is a stable key until the
    // templates are rebuilt, when OnTemplatesApplied drops both maps.
    private static readonly Dictionary<IntPtr, bool> SoloByUnitTemplate = new();
    private static readonly Dictionary<IntPtr, bool> SoloByLeaderTemplate = new();

    public override void OnTemplatesApplied()
    {
        SoloByUnitTemplate.Clear();
        SoloByLeaderTemplate.Clear();
    }

    internal static bool IsSolo(BaseUnitLeader leader)
    {
        try
        {
            var template = leader?.GetTemplate();
            if (template == null)
                return false;
            if (SoloByUnitTemplate.TryGetValue(template.Pointer, out var known))
                return known;
            var solo = IsSoloUnitTemplateId(template.GetID() ?? template.name);
            SoloByUnitTemplate[template.Pointer] = solo;
            return solo;
        }
        catch
        {
            return false;
        }
    }

    // Solo-ness read straight off an attribute instance, which keeps the
    // UnitLeaderTemplate it was constructed with. The patched attribute
    // functions classify through this rather than through a registry of live
    // instances: nothing walks the strategy roster once a mission has started,
    // so a registry would sit empty for the whole tactical scene and the
    // combat-side rewrites below would never fire. Reading the instance also
    // means a recycled address cannot make a regular leader test positive.
    private static bool IsSoloAttributes(UnitLeaderAttributes attributes)
    {
        try
        {
            var template = attributes?.LeaderTemplate;
            if (template == null)
                return false;
            if (SoloByLeaderTemplate.TryGetValue(template.Pointer, out var known))
                return known;
            var id = template.GetID();
            var solo = id != null && SoloLeaderTemplateIds.Contains(id);
            SoloByLeaderTemplate[template.Pointer] = solo;
            return solo;
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
            var attributes = leader.GetAttributes();
            var values = attributes?.m_Values;
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
                var cap = CapFor(i);
                var target = (float)initial[i];
                if (overrides != null && overrides.TryGetValue(i, out var overridden) && overridden > target)
                    target = overridden;
                if (target > cap)
                    target = cap;
                // A per-attribute cap also LOWERS an over-cap value (a save
                // carrying toughness from before the cap existed); the plain
                // growth ceiling never does.
                if (values[i] > cap)
                {
                    values[i] = cap;
                    repaired++;
                    continue;
                }
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
                var expectedHp = UnitLeaderAttributes.GetHitpointsPerElement(values[VitalityIndex]);
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

    // ----- Growth cap raise (solo dolls) -----

    // Attributes grow to this instead of the engine's 100. Matches the
    // vitality override ceiling, so an overridden attribute is also the
    // highest growth can reach.
    private const float GrowthCap = 500f;

    // Per-attribute ceilings below the general cap, at the point the derived
    // stat saturates or stops buying anything (live-derived formulas):
    // agility 150 = 160 AP (40 + 0.8a, user ceiling; her template starts at
    // 125 = 140 AP); weapon skill 200 =
    // perfect hits through every malus the game can stack; valour 200 =
    // symmetric bound while discipline's consumers stay unmapped; toughness
    // 145 = 95% damage reduction (t - 50, user ceiling; immunity is 150);
    // vitality 500 = 100 hp (v / 5, equals her override so it is effectively
    // fixed); precision 400 = guaranteed crits (p / 4); positioning 200 =
    // 1.75x defence (0.75 + 0.005p, against vanilla's 1.25x ceiling). Defence
    // is held lower than the rest because it multiplies with the damage
    // reduction and the hitpoint pool rather than overlapping them, so it
    // compounds where the others do not. Enforced on growth AND on the
    // reconcile pass (which also lowers an over-cap value a save carried in).
    private static readonly Dictionary<int, float> AttributeCaps = new()
    {
        [AgilityIndex] = 150f,
        [WeaponSkillIndex] = 200f,
        [ValourIndex] = 200f,
        [ToughnessIndex] = 145f,
        [VitalityIndex] = 500f,
        [PrecisionIndex] = 400f,
        [PositioningIndex] = 200f,
    };

    private static float CapFor(int attribute)
        => AttributeCaps.TryGetValue(attribute, out var cap) ? Math.Min(cap, GrowthCap) : GrowthCap;

    // Replacement TryIncrease for solo attribute instances: the vanilla body
    // refuses any increase at >= 100 and clamps to 100; this applies the same
    // arithmetic against GrowthCap. Result mirrors vanilla's contract (true =
    // the value changed), so the roll machinery and its level-up toasts
    // behave normally. On any surprise the handler leaves Skip unset and
    // vanilla runs.
    private void OnTryIncrease(PatchInfo info)
    {
        try
        {
            var attributes = (info.Instance as Il2CppObjectBase)?.TryCast<UnitLeaderAttributes>();
            if (attributes == null || !IsSoloAttributes(attributes))
                return;
            var values = attributes.m_Values;
            if (values == null || info.Args is not { Count: >= 2 })
                return;
            var attribute = Convert.ToInt32(info.Args[0]);
            var delta = Convert.ToSingle(info.Args[1]);
            if (attribute < 0 || attribute >= values.Length)
                return;

            var cap = CapFor(attribute);
            var current = values[attribute];
            if (current >= cap)
            {
                info.Skip = true;
                info.Result = false;
                return;
            }
            var next = current + delta;
            if (next < 0f)
                next = 0f;
            if (next > cap)
                next = cap;
            values[attribute] = next;
            info.Skip = true;
            info.Result = true;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"solo squad: raised-cap growth failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The vanilla conversions clamp their INPUT at 100 and are linear below
    // it, so the line recovered from the vanilla function at two safe points
    // IS the scaling to continue past 100. Lazily derived: a probe call
    // re-enters the patched function, but with an input under the clamp the
    // postfix takes the no-override path, so the recursion is one level deep
    // and returns the vanilla value.
    private struct Line
    {
        public bool Known;
        public float Slope;
        public float Intercept;

        public float At(float v) => Intercept + Slope * v;
    }

    // One line per PATCHED FUNCTION, each derived from that function's own
    // below-clamp samples: the float and int variants of the same stat run
    // on DIFFERENT SCALES (the damage-sustained float is the TAKEN fraction,
    // 1.5 - 0.01t; the int is a signed REDUCTION modifier, t - 50: zero at
    // the default 50, 50 at the old cap, negative for frail units), so a
    // line recovered from one must never extend the other.
    private Line _apFloatLine;
    private Line _apIntLine;
    private Line _dmgFloatLine;
    private Line _dmgIntLine;

    private static Line Derive(Func<float, float> vanilla)
    {
        var f0 = vanilla(0f);
        var f50 = vanilla(50f);
        return new Line { Known = true, Slope = (f50 - f0) / 50f, Intercept = f0 };
    }

    // Shared override body: nothing changes at or below the vanilla clamp;
    // above it the recovered line continues to GrowthCap, bounded to the
    // stat's meaningful range where one exists (the damage-sustained mult is
    // a percentage of incoming damage and stays within 0..100 whichever way
    // its slope runs; action points have no natural ceiling).
    private void ExtendPastClamp(PatchInfo info, ref Line line, Func<float, float> vanilla, float? min, float? max, bool asInt)
    {
        try
        {
            if (info.Args is not { Count: >= 1 })
                return;
            var input = Convert.ToSingle(info.Args[0]);
            if (input <= 100f)
                return;
            if (!line.Known)
            {
                line = Derive(vanilla);
                Context.Log.Debug($"solo squad: derived stat line intercept {line.Intercept:0.###} slope {line.Slope:0.###}");
            }
            var value = line.At(Math.Min(input, GrowthCap));
            if (min.HasValue && value < min.Value)
                value = min.Value;
            if (max.HasValue && value > max.Value)
                value = max.Value;
            if (asInt)
                info.Result = (int)Math.Round(value);
            else
                info.Result = value;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"solo squad: derived-stat extension failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ----- Attribute and stat bar rescale (over-cap rows) -----

    // True while UnitStatsAndAttributesPanel.ShowAttributes is drawing its
    // rows. The two sections share one bar element with different
    // denominators, so each is recognised by its own draw window and rescaled
    // on its own terms.
    private bool _inAttributeDraw;

    // As above, for the ShowStats section.
    private bool _inStatDraw;

    // Reentrancy guard for the rescaled re-invoke.
    private bool _rescalingBar;

    private void OnShowAttributesPre(PatchInfo info)
    {
        _inAttributeDraw = true;
        _inStatDraw = false;
        _attributeRowIndex = 0;
        _soloAttributeDraw = IsSoloPanel(info);
        _panelValues = ValuesFor(info);
    }

    private void OnShowStatsPre(PatchInfo info)
    {
        // Closes the attribute window as well: the two sections draw back to
        // back through the same bar element, and the attribute branch must not
        // still be live when the stat rows arrive.
        _inAttributeDraw = false;
        _inStatDraw = true;
        _statRowIndex = 0;
        _soloStatDraw = IsSoloPanel(info);
        _panelValues = ValuesFor(info);
    }

    private Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> ValuesFor(PatchInfo info)
    {
        try
        {
            var panel = (info.Instance as Il2CppObjectBase)?.TryCast<Il2CppMenace.UI.Strategy.UnitStatsAndAttributesPanel>();
            return panel?.m_CurrentLeader?.GetAttributes()?.m_Values;
        }
        catch
        {
            return null; // no readable attributes: the rows keep the vanilla bars
        }
    }

    // The drawing leader's raw attribute values, captured for the stat rows.
    // Their fills are computed from these rather than from the fraction the
    // panel passes in, because that fraction arrives CLAMPED at 1 for exactly
    // the rows a raised cap affects: at damage reduction 85% the panel sends
    // 1.0, not the true 1.35, so scaling it produced a bar shorter than
    // vanilla's rather than longer. (The unclamped number does reach the row,
    // as the preview fraction, which is why the cyan segment lands in the
    // right place while the yellow fill does not.) Deriving from the value
    // also sidesteps the helpers disagreeing about what a fraction means:
    // most normalise against their own span, but critical chance divides by a
    // flat 100, so no single rescale of the incoming number is right for all
    // seven.
    private Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> _panelValues;

    private void OnShowStatsPost(PatchInfo info) => _inStatDraw = false;

    private bool IsSoloPanel(PatchInfo info)
    {
        try
        {
            var panel = (info.Instance as Il2CppObjectBase)?.TryCast<Il2CppMenace.UI.Strategy.UnitStatsAndAttributesPanel>();
            return IsSolo(panel?.m_CurrentLeader);
        }
        catch
        {
            return false; // an unreadable leader keeps the vanilla bars
        }
    }

    // Cached-property rewrite for the combat damage mult (see OnInit).
    private void OnUpdateProperties(PatchInfo info)
    {
        try
        {
            var attributes = (info.Instance as Il2CppObjectBase)?.TryCast<UnitLeaderAttributes>();
            if (attributes == null || !IsSoloAttributes(attributes))
                return;
            var props = (info.Args is { Count: > 0 } ? info.Args[0] as Il2CppObjectBase : null)?.TryCast<Il2CppMenace.Tactical.EntityProperties>();
            var values = attributes.m_Values;
            if (props == null || values == null)
                return;
            // Damage sustained is the ONE cached property that needs rewriting
            // here, because it is the one whose conversion this method inlines
            // (disassembled at UpdateProperties+0x236: an inline clamp, mulss,
            // addss, then a store straight into EntityProperties field 0x8c).
            // The patched converter is never called, so combat would otherwise
            // run on the vanilla-capped number while the panel, which calls the
            // converter directly, showed the extended one.
            //
            // Action points look like the same shape and are NOT: this method
            // CALLS GetActionPoints (UpdateProperties+0xaa) and assigns its
            // return, so the patched converter has already extended the value
            // by the time it lands. Correcting it here adds the past-clamp part
            // a second time, which is agility 125 reading 160 AP instead of 140.
            // The clamp lives in the converters; only one of the two conversions
            // is inlined.
            if (values.Length > ToughnessIndex && values[ToughnessIndex] > 100f)
                props.DamageSustainedMult = UnitLeaderAttributes.GetDamageSustainedMultAsFloat(values[ToughnessIndex]);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"solo squad: damage mult property rewrite failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnShowAttributesPost(PatchInfo info) => _inAttributeDraw = false;

    // The panel computes each row's fill against the vanilla 100 cap, so an
    // over-cap attribute arrives as a fraction above 1. Only then does the
    // row switch to the raised-cap scale (fraction/5 = value/500); rows at or
    // under 100 keep the vanilla bar exactly. The preview fraction rides the
    // same scale so a level-up crossing 100 keeps both bars coherent.
    // Which attribute the current bar belongs to: ShowAttributes draws its
    // rows through one ShowProgressBars call site in index order, so a
    // counter reset at the window start maps each call to its attribute,
    // and the rescale denominator becomes that attribute's own cap (a
    // toughness bar fills toward 130, a vitality bar toward 500).
    private int _attributeRowIndex;

    // The window belongs to a solo doll's panel: EVERY attribute bar scales
    // to its cap (user call), not only the over-100 rows.
    private bool _soloAttributeDraw;

    // Which stat row the current bar belongs to, and whether the panel is a
    // solo doll's. ShowStats draws seven rows through one ShowProgressBars
    // call site in a fixed order, so a counter reset at the window start maps
    // each call to its stat the same way the attribute counter does.
    private int _statRowIndex;
    private bool _soloStatDraw;

    // The seven stat rows in the order ShowStats draws them, each paired with
    // the attribute whose cap sets its ceiling. Recovered from the
    // disassembly of UnitStatsAndAttributesPanel.ShowStats: the seven bar
    // calls are fed by GetActionPoints, GetAccuracy (twice, for accuracy and
    // discipline), GetHitpointsPerElement, GetCriticalChance,
    // GetDamageSustainedMult and GetDefenseMult, in that order.
    private static readonly (Func<float, float> Value, int Attribute)[] StatRows =
    {
        (v => UnitLeaderAttributes.GetActionPoints(v), AgilityIndex),
        (v => UnitLeaderAttributes.GetAccuracy(v), WeaponSkillIndex),
        (v => UnitLeaderAttributes.GetAccuracy(v), ValourIndex),
        (v => UnitLeaderAttributes.GetHitpointsPerElement(v), VitalityIndex),
        (v => UnitLeaderAttributes.GetCriticalChance(v), PrecisionIndex),
        (v => UnitLeaderAttributes.GetDamageSustainedMult(v), ToughnessIndex),
        (v => UnitLeaderAttributes.GetDefenseMult(v), PositioningIndex),
    };

    private static readonly float[] StatScales = new float[StatRows.Length];

    // A stat row's fill arrives as (value - stat(0)) / (stat(100) - stat(0)),
    // so a stat derived from an over-100 attribute lands above 1 and the bar
    // pins full however far past the cap it goes. Rescaling to the raised
    // ceiling is one constant per row: the vanilla span over ours. Derived
    // from the game's own converters (already extended past their clamps by
    // the postfixes above), so the numbers follow vanilla tuning rather than
    // being restated here, and the bar reads full exactly at the cap.
    private static float StatScale(int row)
    {
        if (row < 0 || row >= StatRows.Length)
            return 1f;
        if (StatScales[row] > 0f)
            return StatScales[row];
        var scale = 1f;
        try
        {
            var (value, attribute) = StatRows[row];
            var cap = CapFor(attribute);
            var zero = value(0f);
            var ourSpan = value(cap) - zero;
            if (cap > 100f && Math.Abs(ourSpan) > 0.0001f)
                scale = (value(100f) - zero) / ourSpan;
            // Logged on the first draw, once per row. These numbers are read
            // out of the game's converters rather than written down here, so a
            // tuning change in a game update moves them silently: the log line
            // is the only place the derivation is visible, and comparing it
            // after an update is how a change gets noticed at all.
            Log.Debug($"solo squad: stat row {row} (attribute {attribute}) spans "
                + $"{zero} to {value(cap)} at cap {cap}, vanilla stopped at {value(100f)}, bar scale {scale:0.###}");
        }
        catch
        {
            // an unreadable converter keeps the vanilla bar
        }
        StatScales[row] = scale;
        return scale;
    }

    private void OnShowProgressBars(PatchInfo info)
    {
        try
        {
            if (_rescalingBar || (!_inAttributeDraw && !_inStatDraw))
                return;
            var row = (info.Instance as Il2CppObjectBase)?.TryCast<Il2CppMenace.UI.Strategy.UnitInfoStat>();
            if (row == null || info.Args is not { Count: >= 3 })
                return;
            var fill = Convert.ToSingle(info.Args[0]);
            var preview = Convert.ToSingle(info.Args[1]);
            var growth = Convert.ToSingle(info.Args[2]);
            float scale;
            if (_inStatDraw)
            {
                var stat = _statRowIndex++;
                // Vanilla units keep vanilla bars. The tick is cleared rather
                // than just skipped: these row elements are pooled across
                // leaders as well as across the two tabs, so a tick left
                // behind by a solo doll reappears on the next unit shown.
                if (!_soloStatDraw)
                {
                    HideCapTick(row);
                    return;
                }
                var trueFill = StatFill(stat);
                if (trueFill < 0f)
                {
                    HideCapTick(row);
                    return;
                }
                info.Skip = true;
                _rescalingBar = true;
                try
                {
                    // Preview takes the same value: it only diverged from the
                    // fill because the fill was clamped and it was not, so
                    // leaving it wider would paint a stray tail past the bar's
                    // real end.
                    row.ShowProgressBars(Math.Min(1f, trueFill), Math.Min(1f, trueFill), growth);
                }
                finally
                {
                    _rescalingBar = false;
                }
                ShowVanillaCapTick(row, StatScale(stat));
                return;
            }
            {
                var attribute = _attributeRowIndex++;
                // Only a solo doll's panel is ever rescaled. A vanilla row can
                // arrive with an unclamped growth preview above 1, and letting
                // it fall through here redrew a nearly-maxed vanilla bar at
                // half width against the solo cap table.
                if (!_soloAttributeDraw)
                {
                    HideCapTick(row);
                    return;
                }
                var cap = CapFor(attribute);
                // Taken from the value, not from the incoming fraction: these
                // arrive clamped at 1 exactly like the stat rows do, so
                // rescaling them drew an over-cap attribute SHORT (agility 125
                // filled to 0.667 rather than 0.833). The unclamped number does
                // reach the row as the preview fraction, which is why the cyan
                // tail landed in the right place while the bar did not.
                var value = attribute >= 0 && _panelValues != null && attribute < _panelValues.Length
                    ? _panelValues[attribute]
                    : -1f;
                if (value < 0f || cap <= 0f)
                {
                    HideCapTick(row);
                    return;
                }
                scale = value / cap;
            }
            info.Skip = true;
            _rescalingBar = true;
            try
            {
                // Preview matches the fill for the same reason it does on the
                // stat rows: their divergence was the clamp, not a real preview.
                row.ShowProgressBars(Math.Min(1f, scale), Math.Min(1f, scale), growth);
            }
            finally
            {
                _rescalingBar = false;
            }
            // The tick marks vanilla's attribute ceiling of 100 on the raised
            // bar. The two sections POOL these row elements, so each draw has
            // to set its own tick: left alone, an attribute row inherits
            // whatever position the stat row that used it last wrote, and the
            // two sections order their rows differently.
            ShowVanillaCapTick(row, 100f / CapFor(_attributeRowIndex - 1));
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"solo squad: attribute bar rescale failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // A stat row's true fill: the stat at the doll's current attribute, placed
    // on the span running from the stat at attribute zero to the stat at the
    // raised cap. Negative when it cannot be worked out, which leaves the row
    // exactly as vanilla drew it.
    private float StatFill(int stat)
    {
        if (stat < 0 || stat >= StatRows.Length || _panelValues == null)
            return -1f;
        try
        {
            var (value, attribute) = StatRows[stat];
            if (attribute < 0 || attribute >= _panelValues.Length)
                return -1f;
            var min = value(0f);
            var max = value(CapFor(attribute));
            var span = max - min;
            if (Math.Abs(span) < 0.0001f)
                return -1f;
            return (value(_panelValues[attribute]) - min) / span;
        }
        catch
        {
            return -1f;
        }
    }

    // A tick on the bar at the point vanilla's ceiling used to sit, so a
    // raised bar still says where the normal maximum is instead of silently
    // rescaling under the player. Its position IS StatScale: the vanilla span
    // as a fraction of ours (action points 0.667, hitpoints 0.200, damage
    // reduction 0.690). Coloured like the preview segment it replaces, which
    // used to mark that spot by accident, because the panel passed the fill
    // clamped and the preview unclamped.
    private const string CapTickName = "wmgfl-cap-tick";

    // Damage sustained and "Defense" are SIGNED modifiers (damage sustained is
    // toughness - 50), and the bar marks where the value crosses zero.
    // ProgressBar PAINTS that marker rather than parenting an element for it,
    // which is why no element-tree capture can see it: the row's children are
    // only ever Label, Fill, PreviewFill, Border and DarkLabelClip.
    //
    // Vanilla passes 0.5 because its span is symmetric (-50 to +50). A raised
    // cap makes the span lopsided, so the fraction has to be recomputed:
    // toughness 145 spans -50 to +95, putting zero at 34.5%, and "Defense" at
    // positioning 200 spans -25 to +75, putting it at 25%.
    //
    // The correction has to ride the setter itself, not the bar draw. ShowStats
    // calls SetShowZeroPositionMarker AFTER ShowProgressBars for each of the
    // two signed rows (disassembled: bar at +0x100a then marker at +0x105c, and
    // again at +0x139c / +0x13ea), so anything written while the bar is drawing
    // is overwritten a moment later.
    private bool _inZeroMarker;

    private void OnSetZeroMarker(PatchInfo info)
    {
        // Only the solo dolls' stat section, and never our own re-entry.
        if (_inZeroMarker || !_inStatDraw || !_soloStatDraw)
            return;
        try
        {
            // The setter runs just after its row's bar, so the counter has
            // already moved past the row being drawn.
            var stat = _statRowIndex - 1;
            if (stat < 0 || stat >= StatRows.Length)
                return;
            var (value, attribute) = StatRows[stat];
            var min = value(0f);
            var max = value(CapFor(attribute));
            // An unsigned stat never crosses zero inside the bar, and vanilla
            // draws it no marker: leave those rows alone.
            if (min >= 0f || max <= 0f)
                return;
            var bar = (info.Instance as Il2CppObjectBase)?.TryCast<Il2CppMenace.UI.ProgressBar>();
            if (bar == null)
                return;
            _inZeroMarker = true;
            try
            {
                bar.SetShowZeroPositionMarker(true, -min / (max - min));
            }
            finally
            {
                _inZeroMarker = false;
            }
        }
        catch (Exception ex)
        {
            _inZeroMarker = false;
            Context.Log.Warn($"solo squad: zero marker failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void HideCapTick(VisualElement row)
    {
        try
        {
            var host = HostFor(row);
            var tick = host == null ? null : FindNamed(host, CapTickName, 0);
            if (tick != null)
                tick.style.display = DisplayStyle.None;
        }
        catch
        {
            // a row with no tick has nothing to hide
        }
    }

    private static VisualElement HostFor(VisualElement row)
    {
        var bar = FindNamed(row, "StatProgressBar", 0);
        return bar != null && bar.childCount > 0 ? bar.ElementAt(0) : null;
    }

    private void ShowVanillaCapTick(VisualElement row, float fraction)
    {
        try
        {
            var host = HostFor(row);
            if (host == null)
                return;
            var tick = FindNamed(host, CapTickName, 0);
            // Nothing to mark when the row was never raised: the old ceiling
            // and the new one are the same place.
            if (fraction >= 1f || fraction <= 0f)
            {
                if (tick != null)
                    tick.style.display = DisplayStyle.None;
                return;
            }
            if (tick == null)
            {
                tick = new VisualElement { name = CapTickName };
                tick.style.position = Position.Absolute;
                tick.style.top = 0f;
                tick.style.bottom = 0f;
                // Vivid cyan, no border. It has to read against the gold fill
                // it usually sits on AND against the dark track when the stat
                // is low, which the muted preview-green did not; a high-chroma
                // colour carries both at 2px without needing edges. Staying
                // cyan also keeps it distinct from vanilla's own white tick,
                // which marks the zero crossing rather than the old ceiling.
                tick.style.width = 2f;
                tick.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.25f, 0.95f, 0.95f, 1f));
                // The bar is pooled and redrawn for whoever the window shows
                // next, so the tick must never eat a click meant for the row.
                tick.pickingMode = PickingMode.Ignore;
                host.Add(tick);
            }
            tick.style.display = DisplayStyle.Flex;
            tick.style.left = new StyleLength(Length.Percent(fraction * 100f));
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"solo squad: cap tick failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static VisualElement FindNamed(VisualElement element, string name, int depth)
    {
        if (element == null || depth > 6)
            return null;
        for (var i = 0; i < element.childCount; i++)
        {
            var child = element.ElementAt(i);
            if (child == null)
                continue;
            if (child.name == name)
                return child;
            var hit = FindNamed(child, name, depth + 1);
            if (hit != null)
                return hit;
        }
        return null;
    }

    private void OnActionPointsFloat(PatchInfo info)
        => ExtendPastClamp(info, ref _apFloatLine, UnitLeaderAttributes.GetActionPointsAsFloat, min: null, max: null, asInt: false);

    private void OnActionPointsInt(PatchInfo info)
        => ExtendPastClamp(info, ref _apIntLine, v => UnitLeaderAttributes.GetActionPoints(v), min: null, max: null, asInt: true);

    // Float: the taken fraction cannot go below zero (immunity). Int: the
    // reduction modifier cannot meaningfully exceed 100 (all damage), and
    // the display shows it raw, so an uncapped line read "250%".
    private void OnDamageMultFloat(PatchInfo info)
        => ExtendPastClamp(info, ref _dmgFloatLine, UnitLeaderAttributes.GetDamageSustainedMultAsFloat, min: 0f, max: null, asInt: false);

    private void OnDamageMultInt(PatchInfo info)
        => ExtendPastClamp(info, ref _dmgIntLine, v => UnitLeaderAttributes.GetDamageSustainedMult(v), min: null, max: 100f, asInt: true);
}
