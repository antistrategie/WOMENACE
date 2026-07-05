using System.Collections;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// The ultimate's brain, in two halves. The handler below rides on the ult
// skill itself and owns the charge economy: every slash or thrust usage adds
// a Coagulation stack, at ChargesNeeded the ult unlocks (its uses flip from
// 0 to 1), and using it drains everything. SextansUltSystem owns the
// presentation and the payoffs: the mid-animation teleport to the end of the
// pierce line, then the delayed rend/heal/consecration sweep.
[JiangyuType("SextansUlt")]
public sealed partial class SextansUlt : SkillEventHandlerTemplate
{
    public int ChargesNeeded = 5;
    public int MaxCharges = 7;
    public float TeleportDelay = 1.0f;
    public float DashDuration = 0.2f;
    public float PayoffDelay = 2.4f;
    // The ult's lifesteal: a fraction of the damage it deals, higher on Blood
    // Kiss victims. Real HP, so it heals past the grey pool.
    public float HealFraction = 0.3f;
    public float HealFractionMarked = 0.4f;
    // Blood Rally reclaimed by the ult itself, from the grey pool, at a
    // reduced rate (the per-hit Blood Rally is suppressed during the ult so
    // the two never stack). Tops the combined heal up to HealCap.
    public float UltRallyFraction = 0.1f;
    // The whole ult heal (lifesteal + reclaim) is capped here per cast.
    public int HealCap = 20;

    public override SkillEventHandler Create()
        => new SextansUltHandler { ChargesNeeded = ChargesNeeded, MaxCharges = MaxCharges };
}

// Flips on the game's own stack-count badge for the carrying effect's icon:
// the UI asks every handler via HasStackCountDisplayed and renders the
// skill's StackCount when one says yes. Carries no behaviour of its own.
[JiangyuType("StackBadge")]
public sealed partial class StackBadge : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create()
        => new StackBadgeHandler();
}

[JiangyuType("StackBadgeHandler")]
public sealed partial class StackBadgeHandler : SkillEventHandler
{
    public override bool HasStackCountDisplayed()
        => true;
}

[JiangyuType("SextansUltHandler")]
public sealed partial class SextansUltHandler : SkillEventHandler
{
    public int ChargesNeeded = 5;
    public int MaxCharges = 7;

    // Per-skill-instance, and skills are recreated per mission, so the count
    // resets with the mission for free. OnMissionStarted re-arms it anyway.
    private int _charges;

    public override void OnMissionStarted()
    {
        _charges = 0;
        Lock();
    }

    // Driven by MarkOnHit: one charge per enemy the slash or the thrust
    // actually hits, so a thrust through a crowd banks several. Stacks past
    // ChargesNeeded up to MaxCharges are kept for the next wind-up.
    internal void AddCharge()
    {
        if (_charges >= MaxCharges)
            return;
        _charges++;
        // GetActor(), not ParentSkill.Source: for an item-granted skill the
        // source is not the wielding actor
        SextansUltSystem.SetCoagulationStage(GetActor(), _charges);
        if (_charges == ChargesNeeded)
        {
            ParentSkill?.SetMaxUses(1);
            ParentSkill?.SetUses(1);
            // charges land during the APPLICATION, after the skill bar's
            // own post-use refresh: without a poke the ult button stays
            // greyed until something else redraws the bar
            SextansUltSystem.RefreshSkillBar();
        }
    }

    // The ult eats ChargesNeeded and keeps the overflow. No SetUses here:
    // the native use-consumption runs after the use event, so zeroing now
    // would land the display on -1.
    internal void ConsumeCharges()
    {
        _charges = Math.Max(0, _charges - ChargesNeeded);
        SextansUltSystem.SetCoagulationStage(GetActor(), _charges);
    }

    private void Lock()
    {
        var parent = ParentSkill;
        if (parent == null)
            return;
        parent.SetMaxUses(1);
        parent.SetUses(0);
    }
}

// Watches for the ult being used (the same TacticalManager.InvokeOnSkillUse
// hook the mech's drill dash rides) and runs the sequence: cache the dash
// lane and its victims at commit time, teleport Sextans to the chosen
// destination mid wind-up, and after the skill's own damage has landed,
// deliver the payoffs. Victims are counted before any damage resolves, so
// enemies the ult kills still feed the heal and the consecration.
public sealed class SextansUltSystem : JiangyuSystem
{
    private const string UltSkillId = "active.sextans_ult";
    private const string StrikeSkillId = "active.sextans_ult_strike";
    private const string RendSkillId = "active.sextans_ult_rend";
    private const string BloodKissId = "effect.sextans_blood_kiss";
    private const string CoagulationId = "effect.sextans_coagulation";
    private const string MarkId = "effect.sextans_holy_blood_mark";

    private static SextansUltSystem _instance;

    // True only while the ult's own strike/rend applications are landing, so
    // the per-hit Blood Rally recovery can stand down and let the ult fold a
    // reduced-rate reclaim into its single combined heal instead of stacking.
    internal static bool UltStrikesResolving;

    // Raw damage (the popup number, overkill included) the ult's hits deal
    // while resolving, reported per hit by Blood Rally's OnTargetHit and
    // split by whether the victim carries Blood Kiss. The combined heal is a
    // fraction of these.
    private static int _ultDamageNormal;
    private static int _ultDamageMarked;

    private SkillTemplate _bloodKiss;
    private SkillTemplate _coagulation;
    private SkillTemplate _mark;
    private SkillTemplate _strike;
    private SkillTemplate _rend;
    private SkillTemplate _ult;

    // MarkOnHit resolves its effect templates by id through here
    private readonly Dictionary<string, SkillTemplate> _effectsById = new();

    public override void OnInit()
    {
        _instance = this;
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnSkillUse", OnSkillUse);
    }

    public override void OnTemplatesApplied()
    {
        _bloodKiss = Templates.ById<SkillTemplate>(BloodKissId, msg => Context.Log.Warn($"ult: {msg}"));
        _coagulation = Templates.ById<SkillTemplate>(CoagulationId, msg => Context.Log.Warn($"ult: {msg}"));
        _mark = Templates.ById<SkillTemplate>(MarkId, msg => Context.Log.Warn($"ult: {msg}"));
        _strike = Templates.ById<SkillTemplate>(StrikeSkillId, msg => Context.Log.Warn($"ult: {msg}"));
        _rend = Templates.ById<SkillTemplate>(RendSkillId, msg => Context.Log.Warn($"ult: {msg}"));
        _ult = Templates.ById<SkillTemplate>(UltSkillId, msg => Context.Log.Warn($"ult: {msg}"));
    }

    private Skill FindGrantedSkill(Actor user, SkillTemplate template, string id)
    {
        if (template == null)
            return null;
        var skill = user.GetSkills()?.GetSkillByTemplate(template, null)?.TryCast<Skill>();
        if (skill == null)
            Context.Log.Warn($"ult: '{id}' not granted to the user, its hits are skipped");
        return skill;
    }

    // One coagulation effect instance whose StackCount carries the charge:
    // the StackBadge handler on the effect makes the UI draw the number on
    // its icon, so the count needs no per-stage templates.
    internal static void SetCoagulationStage(Actor user, int count)
        => _instance?.SetStage(user, count);

    // MarkOnHit's charge entry point: one charge per enemy actually hit by
    // a charge-granting skill.
    internal static void AddChargeFor(Actor user)
        => _instance?.FindUltHandler(user)?.AddCharge();

    // Reported by Blood Rally's OnTargetHit for each of the ult's hits while
    // it resolves. Buckets by the victim's Blood Kiss so the heal can weigh
    // marked damage higher; the mark is still present here (stripped only
    // after the heal).
    internal static void AccumulateUltDamage(Actor victim, int rawDamage)
    {
        if (rawDamage <= 0 || victim == null || _instance == null)
            return;
        if (HasEffect(victim, _instance._bloodKiss))
            _ultDamageMarked += rawDamage;
        else
            _ultDamageNormal += rawDamage;
    }

    internal static void RefreshSkillBar()
    {
        try
        {
            Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.TryCast<Il2CppMenace.UI.UITactical>()
                ?.GetSkillBar()?.UpdateSkills();
        }
        catch (Exception ex)
        {
            _instance?.Context.Log.Warn($"ult: skill bar refresh failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void SetStage(Actor user, int count)
    {
        if (user == null || _coagulation == null)
            return;
        if (count <= 0)
        {
            RemoveEffectStacks(user, _coagulation);
            return;
        }
        var existing = user.GetSkills()?.GetSkillByTemplate(_coagulation, null)?.TryCast<Skill>()
            ?? AddEffect(user, _coagulation)?.TryCast<Skill>();
        if (existing == null)
            return;
        existing.StackCount = count;
        // a StackCount write fires no container event: several charges from
        // one swing would keep showing the first number until something else
        // redraws the panel
        RallyBarsSystem.RefreshStatusIcons(user);
    }

    // MarkOnHit's entry point: apply the effect, or refresh its remaining
    // lifetime if the target already carries it.
    internal static void AddOrRefreshById(Actor target, string effectId)
        => _instance?.AddOrRefreshResolved(target, effectId);

    private void AddOrRefreshResolved(Actor target, string effectId)
    {
        if (!_effectsById.TryGetValue(effectId, out var template))
        {
            template = Templates.ById<SkillTemplate>(effectId, msg => Context.Log.Warn($"ult: {msg}"));
            _effectsById[effectId] = template;
        }
        AddOrRefresh(target, template);
    }

    private void OnSkillUse(PatchInfo info)
    {
        try
        {
            if (info.Args == null || info.Args.Count < 2)
                return;
            var actor = info.Args[0] as Actor;
            var skill = info.Args[1] as Skill;
            if (actor == null || skill == null)
                return;

            var id = skill.GetTemplate()?.GetID();
            if (!string.Equals(id, UltSkillId, StringComparison.Ordinal))
                return;

            FindUltHandler(actor)?.ConsumeCharges();
            var tile = info.Args.Count > 2 ? info.Args[2] as Tile : null;
            if (tile != null)
                Context.Coroutines.Start(UltSequence(actor, skill, tile));
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"ult: skill-use hook failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private SextansUltHandler FindUltHandler(Actor actor)
    {
        if (_ult == null)
            return null;
        var handlers = actor?.GetSkills()?.GetSkillByTemplate(_ult, null)?.TryCast<Skill>()?.GetSkillEventHandlers();
        for (var i = 0; handlers != null && i < handlers.Length; i++)
        {
            var handler = handlers[i]?.TryCast<SextansUltHandler>();
            if (handler != null)
                return handler;
        }
        return null;
    }

    private IEnumerator UltSequence(Actor user, Skill ult, Tile aim)
    {
        var origin = user.GetTile();
        if (origin == null)
            yield break;

        // tuning from the KDL handler blocks on the ult template
        var tiles = 8;
        var width = 1;
        var teleportDelay = 1.0f;
        var dashDuration = 0.2f;
        var payoffDelay = 2.4f;
        var healFraction = 0.3f;
        var healFractionMarked = 0.4f;
        var ultRallyFraction = 0.1f;
        var healCap = 20;
        var template = ult.GetTemplate();
        var handlers = template?.EventHandlers;
        for (var i = 0; handlers != null && i < handlers.Count; i++)
        {
            var pierce = handlers[i]?.TryCast<PierceLine>();
            if (pierce != null)
            {
                tiles = pierce.Tiles;
                width = pierce.Width;
                continue;
            }
            var tuning = handlers[i]?.TryCast<SextansUlt>();
            if (tuning == null)
                continue;
            teleportDelay = tuning.TeleportDelay;
            dashDuration = tuning.DashDuration;
            payoffDelay = tuning.PayoffDelay;
            healFraction = tuning.HealFraction;
            healFractionMarked = tuning.HealFractionMarked;
            ultRallyFraction = tuning.UltRallyFraction;
            healCap = tuning.HealCap;
        }

        // Geometry and victims are captured at commit time: after the
        // teleport the origin has changed, and after the damage lands some
        // victims are corpses, so neither can be derived later. The dash
        // lane runs from her to the aimed tile.
        var swathe = new List<Tile>();
        Pierce.WalkBetween(origin, aim, tiles, width, (t, _) =>
        {
            if (t != null && !swathe.Contains(t))
                swathe.Add(t);
        });

        // A multi-element enemy occupies several lane tiles that all resolve
        // to the SAME Actor, so dedup by pointer: the strike, the rend and
        // the heal are all per-enemy, not per-tile.
        var victims = new List<Actor>();
        var kissed = new List<Actor>();
        var seen = new HashSet<System.IntPtr>();
        foreach (var t in swathe)
        {
            var enemy = t.GetEntity()?.TryCast<Actor>();
            if (enemy == null || !enemy.IsAlive() || !Pierce.IsHostileTo(user, enemy))
                continue;
            if (!seen.Add(enemy.Pointer))
                continue;
            victims.Add(enemy);
            if (HasEffect(enemy, _bloodKiss))
                kissed.Add(enemy);
        }

        var waited = 0f;
        while (waited < teleportDelay)
        {
            waited += Time.deltaTime;
            yield return null;
        }
        if (!Alive(user))
            yield break;

        // The dash: a fast slide along the lane mid-animation, then the
        // logical tile commit (the drill dash contract).
        var dashedRenderers = new List<UnityEngine.SkinnedMeshRenderer>();
        var landing = PickLanding(user, origin, aim, tiles);
        if (landing != null)
        {
            var elements = user.GetElements();
            var count = elements?.Count ?? 0;
            var starts = new Vector3[count];
            var ends = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                var element = elements[i];
                if (element == null)
                    continue;
                starts[i] = element.transform.position;
                ends[i] = element.GetTargetPosOnTile(landing, -1);
            }

            var slide = 0f;
            while (slide < dashDuration)
            {
                slide += Time.deltaTime;
                var f = Mathf.Clamp01(slide / dashDuration);
                for (var i = 0; i < count; i++)
                {
                    var element = elements[i];
                    if (element != null)
                        element.transform.position = Vector3.Lerp(starts[i], ends[i], f);
                }
                yield return null;
            }
            waited += slide;
            if (user == null || !user.IsAlive())
                yield break;

            for (var i = 0; i < count; i++)
            {
                var element = elements[i];
                if (element == null)
                    continue;
                element.transform.position = ends[i];
                // The dash outruns the skinned mesh bounds: the renderers
                // still claim the old tile, the camera culls them at the new
                // one, and she vanishes until something refreshes the
                // bounds. Recompute while offscreen until she settles, then
                // reset (below) so it is not a standing per-frame cost.
                foreach (var renderer in element.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true))
                {
                    renderer.updateWhenOffscreen = true;
                    dashedRenderers.Add(renderer);
                }
            }
            user.SetTile(landing);
            // GetPos() reads the cached average position and the dash fires
            // no movement events, so refresh it and flag vision dirty by
            // hand (the drill dash contract).
            user.UpdateAveragePosition();
            user.VisionDirty = true;
            Context.Log.Debug($"ult: dashed to ({landing.GetX()},{landing.GetZ()})");
        }

        while (waited < payoffDelay)
        {
            waited += Time.deltaTime;
            yield return null;
        }
        if (!Alive(user))
            yield break;

        // The dash bounds have long since recomputed by now: drop back to
        // the default so it is not a standing per-frame cost for the mission.
        foreach (var renderer in dashedRenderers)
        {
            if (renderer != null)
                renderer.updateWhenOffscreen = false;
        }

        // 1. The wounds arrive. The ult's own application is a blank (it
        // resolves post-teleport and hits tiles, not sides, so it must never
        // carry a payload she could stand in). The strike is dealt here, to
        // hostiles only, with the full application pipeline (damage numbers,
        // reactions, impact effect) per enemy. Marked enemies take the rend
        // on top.
        var struck = 0;
        var rent = 0;
        // The per-hit Blood Rally stands down while the ult's own hits land
        // (no stacking); each hit instead reports its raw damage into the
        // accumulators via OnTargetHit -> AccumulateUltDamage, split by Blood
        // Kiss, and the single combined heal below is a fraction of those.
        _ultDamageNormal = 0;
        _ultDamageMarked = 0;
        UltStrikesResolving = true;
        try
        {
            var strike = FindGrantedSkill(user, _strike, StrikeSkillId);
            foreach (var enemy in victims)
            {
                if (strike == null)
                    break;
                if (enemy == null || !enemy.IsAlive())
                    continue;
                var t = enemy.GetTile();
                if (t == null)
                    continue;
                strike.ApplyToTile(t, UsageParameter.Free | UsageParameter.InstantResolve);
                struck++;
            }

            var rend = FindGrantedSkill(user, _rend, RendSkillId);
            foreach (var enemy in kissed)
            {
                if (rend == null)
                    break;
                if (enemy == null || !enemy.IsAlive())
                    continue;
                var t = enemy.GetTile();
                if (t == null)
                    continue;
                rend.ApplyToTile(t, UsageParameter.Free | UsageParameter.InstantResolve);
                rent++;
            }
        }
        finally
        {
            UltStrikesResolving = false;
        }
        Context.Log.Debug($"ult: struck {struck} victim(s), rent {rent} marked");

        // 2. The feast: ONE combined heal, capped per cast, healing her real
        // HP past the grey pool. Lifesteal takes healFraction of the raw
        // damage dealt to unmarked victims and healFractionMarked of marked
        // damage (overkill included); a reduced reclaim then draws from the
        // pool to top the heal toward the cap.
        var dmgNormal = _ultDamageNormal;
        var dmgMarked = _ultDamageMarked;
        var lifesteal = Mathf.Min(healCap, Mathf.RoundToInt(dmgNormal * healFraction + dmgMarked * healFractionMarked));
        var reclaimWanted = Mathf.RoundToInt((dmgNormal + dmgMarked) * ultRallyFraction);
        var reclaim = RallyBarsSystem.DrainPool(user, Mathf.Min(healCap - lifesteal, reclaimWanted));
        var heal = lifesteal + reclaim;
        if (heal > 0)
        {
            var element = user.GetElement(0);
            if (element != null)
            {
                var max = element.GetHitpointsMax();
                element.SetHitpoints(Mathf.Min(max, element.GetHitpoints() + heal));
                user.UpdateHitpoints();
                // a direct SetHitpoints fires no hitpoints-changed event, so
                // the overhead bar stays stale until poked by hand (the unit
                // window reads live values and needs no help)
                EffectHudIconSystem.FindHud(user)?.OnHitpointsChanged(user, user.GetHitpointsPct(), 500);
                Context.Log.Debug($"ult: healed {heal} (lifesteal {lifesteal} + reclaim {reclaim}) from {dmgNormal + dmgMarked} damage");
            }
        }

        // The feast consumes the marks: surviving victims lose Blood Kiss.
        foreach (var enemy in kissed)
        {
            if (enemy == null || !enemy.IsAlive())
                continue;
            RemoveEffectStacks(enemy, _bloodKiss);
        }

        // 3. The consecration: every allied unit is blessed with the Holy
        // Blood Mark whenever the ult is used. Sextans herself is not, and
        // an ally already carrying it has its remaining turns refreshed.
        if (_mark == null)
            yield break;
        var blessed = 0;
        var manager = TacticalManager.Get();
        var factions = manager?.GetFactions();
        for (var i = 0; factions != null && i < factions.Length; i++)
        {
            var actors = factions[i]?.GetActors();
            for (var j = 0; actors != null && j < actors.Count; j++)
            {
                var ally = actors[j];
                if (ally == null || !ally.IsAlive())
                    continue;
                if (ally.Pointer == user.Pointer || !ally.IsAlliedWith(user))
                    continue;
                AddOrRefresh(ally, _mark);
                blessed++;
            }
        }
        Context.Log.Debug($"ult: consecrated {blessed} all(ies) ({victims.Count} enem(ies) struck)");
    }

    // The landing is the chosen destination: the snapped end of the dash
    // lane. Targeting only offers empty tiles, so normally it is free; if
    // something moved onto it (or an off-axis click snapped onto an occupied
    // tile), the centre row is scanned backwards toward her for the nearest
    // standable tile. Null when the whole lane is closed: she strikes from
    // where she stands.
    private Tile PickLanding(Actor user, Tile origin, Tile aim, int tiles)
    {
        try
        {
            var direction = origin.GetDirectionTo(aim);
            var steps = Math.Min(Pierce.Distance(origin, aim), tiles);
            var row = new List<Tile>();
            var walk = origin;
            for (var i = 0; i < steps; i++)
            {
                walk = walk.GetNextTile(direction);
                if (walk == null)
                    break;
                row.Add(walk);
            }

            for (var i = row.Count - 1; i >= 0; i--)
            {
                var candidate = row[i];
                if (candidate.HasActor())
                    continue;
                if (!candidate.CanBeEnteredBy(user) && !candidate.IsValidMovementDestination())
                    continue;
                return candidate;
            }
            Context.Log.Debug("ult: no free tile on the lane, striking in place");
            return null;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"ult: landing pick failed, striking in place: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // A destroyed Il2Cpp object is not managed-null (Actor is not a
    // UnityEngine.Object), so `== null` cannot see a mid-coroutine teardown
    // and IsAlive() on a freed object throws. Wrap the check: a torn-down
    // actor reads as not-alive and the coroutine bails cleanly.
    private static bool Alive(Actor actor)
    {
        try
        {
            return actor != null && actor.IsAlive();
        }
        catch
        {
            return false;
        }
    }

    private static bool HasEffect(Actor actor, SkillTemplate effect)
        => effect != null && actor?.GetSkills()?.GetSkillByTemplate(effect, null) != null;

    // Fresh targets get the effect (its attach visuals fire once); carriers
    // only get their remaining lifetime wound back up, so nothing doubles up.
    private void AddOrRefresh(Actor actor, SkillTemplate effect)
    {
        var skills = actor?.GetSkills();
        if (skills == null || effect == null)
            return;
        var existing = skills.GetSkillByTemplate(effect, null);
        if (existing == null)
        {
            AddEffect(actor, effect);
            return;
        }
        var handlers = existing.TryCast<Skill>()?.GetSkillEventHandlers();
        for (var i = 0; handlers != null && i < handlers.Length; i++)
        {
            var lifetime = handlers[i]?.TryCast<LifetimeLimitHandler>();
            if (lifetime == null)
                continue;
            lifetime.m_TurnsLeft = lifetime.m_Template?.Lifetime ?? lifetime.m_TurnsLeft;
            Context.Log.Debug($"ult: refreshed '{effect.GetID()}'");
        }
    }

    private BaseSkill AddEffect(Actor actor, SkillTemplate effect)
    {
        var skills = actor?.GetSkills();
        if (skills == null || effect == null)
            return null;
        // a boxed EMPTY nullable, not managed null: null for an
        // Il2CppSystem.Nullable proxy misbehaves in the interop marshalling
        // (the blank-tooltip precedent)
        var instance = effect.CreateSkill(new Il2CppSystem.Nullable<Il2CppMenace.Strategy.Origin>());
        if (instance == null)
        {
            Context.Log.Warn($"ult: CreateSkill returned null for '{effect.GetID()}'");
            return null;
        }
        if (!skills.Add(instance))
        {
            Context.Log.Warn($"ult: container rejected '{effect.GetID()}'");
            return null;
        }
        Context.Log.Debug($"ult: applied '{effect.GetID()}'");
        return instance;
    }

    private void RemoveEffectStacks(Actor actor, SkillTemplate effect)
    {
        var skills = actor?.GetSkills();
        if (skills == null || effect == null)
            return;
        var removed = 0;
        while (skills.Remove(effect))
            removed++;
        if (removed == 0)
            return;
        Context.Log.Debug($"ult: stripped {removed} of '{effect.GetID()}'");
        // Remove(SkillTemplate) is overloaded and unpatchable, so the icon
        // mirror cannot see this path on its own
        EffectHudIconSystem.Resync(actor);
    }
}
