using System.Collections;
using System.Collections.Generic;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;
using MenaceBar = Il2CppMenace.UI.ProgressBar;

namespace WOMENACE.Code;

// Blood Rally, the Bloodborne rally mechanic as a perk. Damage the carrier
// takes lingers as a recoverable pool, every enemy their attacks find claws
// a fixed amount of it back, and the pool expires at the end of their next
// turn. RallyBarsSystem below paints the pool as a grey band on the health
// bars.
//
// The whole mechanic rides the perk's own event handler: perks are skill
// templates, their handlers live in the actor's skill container and receive
// the container-wide events (damage taken, targets hit, turn ends), so no
// game patches are needed for the rules themselves.
[JiangyuType("Rally")]
public sealed partial class Rally : SkillEventHandlerTemplate
{
    public float RecoverFraction = 0.4f;
    // the most a single hit can claw back, however much damage it dealt
    public int RecoverCap = 20;
    public int WindowTurns = 1;

    public override SkillEventHandler Create()
        => new RallyHandler { RecoverFraction = RecoverFraction, RecoverCap = RecoverCap, WindowTurns = WindowTurns };
}

[JiangyuType("RallyHandler")]
public sealed partial class RallyHandler : SkillEventHandler
{
    public float RecoverFraction = 0.4f;
    public int RecoverCap = 20;
    public int WindowTurns = 1;

    private int _pool;
    private int _knownHp = -1;
    private int _turnsLeft;

    internal int Pool => _pool;

    public override void OnMissionStarted()
    {
        _pool = 0;
        _knownHp = RallyBarsSystem.SumHitpoints(GetActor());
        _turnsLeft = 0;
        // register so the bar sweep can find the pool by actor pointer
        // instead of scanning every unit's skill container
        RallyBarsSystem.Register(GetActor(), this);
    }

    public override void OnMissionFinished()
    {
        _pool = 0;
        _knownHp = -1;
        RallyBarsSystem.Unregister(GetActor());
    }

    public override void OnDeath()
    {
        _pool = 0;
        // clear the band: a paint that races ahead of the HP-zero event
        // could otherwise leave the grey band drawn on the corpse
        var actor = GetActor();
        if (actor != null)
            RallyBarsSystem.Repaint(actor, this);
    }

    // The pool grows from ACTUAL hitpoint loss, not DamageInfo: armour,
    // durability and Toughness have all had their say by the time hitpoints
    // move, and the grey band must never promise more than was really lost.
    public override void OnDamageReceived(Entity attacker, DamageInfo damageInfo)
        => SyncHp();

    // The per-tick sync catches what OnDamageReceived does not: damage over
    // time feeding the pool, and heals (the ult's feast, this rally itself)
    // re-baselining the snapshot without growing it.
    public override void OnUpdate(EntityProperties properties)
        => SyncHp();

    private void SyncHp()
    {
        var actor = GetActor();
        if (actor == null || !actor.IsAlive())
            return;
        // Register on every tick, not just OnMissionStarted: the bar sweep
        // finds the pool by pointer, and this is the reliable point where the
        // handler has a live actor (GetActor may be null at OnMissionStarted,
        // and a mid-mission spawn never sees it). Idempotent, so cheap.
        RallyBarsSystem.Register(actor, this);
        var current = RallyBarsSystem.SumHitpoints(actor);
        if (_knownHp < 0)
        {
            _knownHp = current;
            return;
        }
        if (current == _knownHp)
            return;

        if (current < _knownHp)
        {
            _pool += _knownHp - current;
            var headroom = RallyBarsSystem.SumHitpointsMax(actor) - current;
            if (_pool > headroom)
                _pool = headroom;
            _turnsLeft = WindowTurns;
            RallyBarsSystem.Debug($"rally: pool {_pool} after losing {_knownHp - current} hp");
        }
        _knownHp = current;
        RallyBarsSystem.Repaint(actor, this);
    }

    // Every enemy an attack connects with claws back a fixed bite of the
    // pool. Fires per target entity per hit, so the thrust through a crowd
    // and the ult's per-victim riders each feed multiple times: aggression
    // at scale is the intended answer to taking a hit.
    public override void OnTargetHit(Skill skill, Entity targetEntity, DamageInfo damageInfo)
    {
        try
        {
            var victim = targetEntity?.TryCast<Actor>();
            // While the ult's own strikes land, the per-hit recovery stands
            // down (the ult does one combined heal instead), but each hit's
            // raw damage is reported so the ult heal is a fraction of the
            // damage dealt, overkill included. Reported before the pool check
            // because the ult heal does not depend on the pool.
            if (SextansUltSystem.UltStrikesResolving)
            {
                if (victim != null && damageInfo != null)
                    SextansUltSystem.AccumulateUltDamage(victim, damageInfo.Damage);
                return;
            }

            if (_pool <= 0)
                return;
            var actor = GetActor();
            if (actor == null || victim == null || !Pierce.IsHostileTo(actor, victim))
                return;

            // recover a fraction of the damage this hit dealt (overkill
            // included), capped per hit and by the remaining pool
            var dealt = damageInfo != null ? damageInfo.Damage : 0;
            var recovered = Mathf.Min(_pool, Mathf.Min(RecoverCap, Mathf.RoundToInt(dealt * RecoverFraction)));
            var element = actor.GetElement(0);
            if (recovered <= 0 || element == null)
                return;
            var max = element.GetHitpointsMax();
            var before = element.GetHitpoints();
            var after = Mathf.Min(max, before + recovered);
            if (after <= before)
                return;

            _pool -= recovered;
            element.SetHitpoints(after);
            actor.UpdateHitpoints();
            _knownHp = RallyBarsSystem.SumHitpoints(actor);
            // a direct SetHitpoints fires no hitpoints-changed event: poke
            // the overhead bar by hand (the ult heal precedent). That poke
            // runs through the bar patch and already sweeps every overhead
            // HUD, so only the panel needs a further repaint here.
            EffectHudIconSystem.FindHud(actor)?.OnHitpointsChanged(actor, actor.GetHitpointsPct(), 250);
            RallyBarsSystem.RepaintPanel(actor, this);
            RallyBarsSystem.Debug($"rally: recovered {after - before}, pool {_pool} left");
        }
        catch (Exception ex)
        {
            RallyBarsSystem.Warn($"rally: recovery failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Draws up to `amount` out of the grey pool (the ult's reduced reclaim),
    // returning what was actually taken, and repaints the shrunken band.
    internal int DrainPool(int amount)
    {
        if (amount <= 0 || _pool <= 0)
            return 0;
        var drained = Mathf.Min(_pool, amount);
        _pool -= drained;
        var actor = GetActor();
        if (actor != null)
            RallyBarsSystem.Repaint(actor, this);
        return drained;
    }

    // The window: whatever the strikes have not reclaimed by the end of the
    // carrier's next turn is gone. Damage taken during her own turn expires
    // at that same turn's end, so the answer has to come immediately.
    public override void OnTurnEnd()
    {
        if (_pool <= 0)
            return;
        _turnsLeft--;
        if (_turnsLeft > 0)
            return;
        _pool = 0;
        RallyBarsSystem.Debug("rally: window closed, pool lost");
        var actor = GetActor();
        if (actor != null)
            RallyBarsSystem.Repaint(actor, this);
    }
}

// Paints the rally pool as a grey band on the health bars: the overhead unit
// HUD and the tactical selected-unit panel. The band is an overlay element
// added as a child of the game's ProgressBar, spanning from the current
// hitpoints fraction to current-plus-pool. The bar's own preview layer
// cannot carry it: SetPreviewFillFraction only renders BELOW the fill (it
// exists for damage previews), a fraction above the fill draws nothing.
// Children of the bar render above its generated mesh and survive its
// redraws, so the band only needs repositioning when the numbers move.
public sealed class RallyBarsSystem : JiangyuSystem
{
    private static RallyBarsSystem _instance;

    private static readonly Color RallyGrey = new(0.63f, 0.63f, 0.63f, 1f);

    private Il2CppMenace.UI.Tactical.SelectedUnitPanel _panel;
    private Actor _panelActor;

    // Rally handlers register their owning actor here so the bar sweep is a
    // pointer lookup, not a per-unit skill-container scan on every HP event.
    private readonly Dictionary<System.IntPtr, RallyHandler> _rallies = new();

    // Coalesces a same-frame burst of coagulation-charge refreshes (one per
    // enemy a multi-hit swing finds) into a single next-frame panel redraw.
    private bool _statusRefreshPending;
    private Actor _statusRefreshActor;

    public override void OnInit()
    {
        _instance = this;
        // non-virtual methods, safe to patch where they are declared
        Context.Patches.Postfix("Il2CppMenace.UI.Tactical.EntityHUD", "OnHitpointsChanged", OnHudHitpointsChanged);
        Context.Patches.Postfix("Il2CppMenace.UI.Tactical.EntityHUD", "InitBars", OnHudRebound);
        // recycled HUDs can rebind through SetActor without InitBars
        Context.Patches.Postfix("Il2CppMenace.UI.Tactical.UnitHUD", "SetActor", OnHudActorSet);
        Context.Patches.Postfix("Il2CppMenace.UI.Tactical.SelectedUnitPanel", "SetActor", OnPanelActorSet);
        Context.Patches.Postfix("Il2CppMenace.UI.Tactical.SelectedUnitPanel", "UpdateStats", OnPanelStatsUpdated);
    }

    internal static void Repaint(Actor actor, RallyHandler rally)
        => _instance?.PaintAll(actor, rally);

    // Panel-only repaint: callers that already swept the overhead HUDs (via
    // an OnHitpointsChanged poke) use this so the sweep does not run twice.
    internal static void RepaintPanel(Actor actor, RallyHandler rally)
        => _instance?.PaintPanelFor(actor, rally);

    internal static void Register(Actor actor, RallyHandler handler)
    {
        if (_instance == null || actor == null || handler == null)
            return;
        _instance._rallies[actor.Pointer] = handler;
    }

    internal static void Unregister(Actor actor)
    {
        if (_instance == null || actor == null)
            return;
        _instance._rallies.Remove(actor.Pointer);
    }

    // The ult's reduced reclaim: take up to `amount` from the actor's grey
    // pool, returning what was actually drained.
    internal static int DrainPool(Actor actor, int amount)
        => FindRally(actor)?.DrainPool(amount) ?? 0;

    // The element-summed current and max hitpoints of an actor: the one
    // implementation the rally pool clamp and the grey-band width both read.
    internal static int SumHitpoints(Actor actor)
    {
        var elements = actor?.GetElements();
        var total = 0;
        for (var i = 0; elements != null && i < elements.Count; i++)
            total += elements[i]?.GetHitpoints() ?? 0;
        return total;
    }

    internal static int SumHitpointsMax(Actor actor)
    {
        var elements = actor?.GetElements();
        var total = 0;
        for (var i = 0; elements != null && i < elements.Count; i++)
            total += elements[i]?.GetHitpointsMax() ?? 0;
        return total;
    }

    // Redraws the selected-unit panel's status icon row. Writing a skill's
    // StackCount fires no container event, so the badge goes stale until the
    // panel is poked. A multi-hit swing banks several charges in one frame;
    // coalesce them into a single next-frame redraw so only the final count
    // is drawn.
    internal static void RefreshStatusIcons(Actor actor)
        => _instance?.QueueStatusRefresh(actor);

    private void QueueStatusRefresh(Actor actor)
    {
        if (actor == null)
            return;
        _statusRefreshActor = actor;
        if (_statusRefreshPending)
            return;
        _statusRefreshPending = true;
        Context.Coroutines.Start(FlushStatusRefresh());
    }

    private IEnumerator FlushStatusRefresh()
    {
        // let the whole same-frame charge burst settle before redrawing
        yield return null;
        _statusRefreshPending = false;
        var actor = _statusRefreshActor;
        _statusRefreshActor = null;
        if (_panel == null || _panelActor == null || actor == null || _panelActor.Pointer != actor.Pointer)
            yield break;
        try
        {
            _panel.UpdateStatusAndDamageEffects();
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"rally bars: status icon refresh failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // handlers carry no logging context of their own
    internal static void Debug(string message)
        => _instance?.Context.Log.Debug(message);

    internal static void Warn(string message)
        => _instance?.Context.Log.Warn(message);

    // Every overhead HUD event reconciles EVERY hud, not just its own: the
    // HUD list churns as units enter and leave view, its pooled visual trees
    // carry painted bands to new owners through recycling paths no single
    // hook reliably sees, and per-hud cleanup proved leaky in practice
    // (stale bands with the rally actor's geometry surfaced on other units'
    // bars). A full sweep is ~20 bars and the events are sparse.
    private void OnHudRebound(PatchInfo info)
        => SweepHuds();

    private void OnHudActorSet(PatchInfo info)
        => SweepHuds();

    private void OnHudHitpointsChanged(PatchInfo info)
        => SweepHuds();

    private void SweepHuds()
    {
        try
        {
            var screen = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.TryCast<Il2CppMenace.UI.UITactical>();
            var hudList = screen?.GetHUD()?.m_HUDList;
            for (var i = 0; hudList != null && i < hudList.Count; i++)
            {
                var hud = hudList[i]?.TryCast<Il2CppMenace.UI.Tactical.UnitHUD>();
                if (hud == null)
                    continue;
                var actor = hud.GetActor();
                Paint(hud.m_HitpointsBar, actor, actor != null ? FindRally(actor) : null);
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"rally bars: hud sweep failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnPanelActorSet(PatchInfo info)
    {
        try
        {
            _panel = (info.Instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<Il2CppMenace.UI.Tactical.SelectedUnitPanel>();
            _panelActor = (info.Args is { Count: > 0 } ? info.Args[0] : null) as Actor;
            PaintPanel();
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"rally bars: panel bind failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnPanelStatsUpdated(PatchInfo info)
        => PaintPanel();

    private void PaintPanel()
    {
        try
        {
            if (_panel == null || _panelActor == null)
                return;
            // the panel is one shared widget: selecting a unit without the
            // rally handler must hide the band the previous owner left
            Paint(_panel.m_HitpointsBar, _panelActor, FindRally(_panelActor));
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"rally bars: panel paint failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void PaintPanelFor(Actor actor, RallyHandler rally)
    {
        try
        {
            if (_panel != null && _panelActor != null && actor != null && _panelActor.Pointer == actor.Pointer)
                Paint(_panel.m_HitpointsBar, actor, rally);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"rally bars: panel paint failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void PaintAll(Actor actor, RallyHandler rally)
    {
        try
        {
            SweepHuds();
            if (_panel != null && _panelActor != null && _panelActor.Pointer == actor.Pointer)
                Paint(_panel.m_HitpointsBar, actor, rally);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"rally bars: repaint failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private const string BandName = "wm-rally-band";

    private void Paint(MenaceBar bar, Actor actor, RallyHandler rally)
    {
        if (bar == null)
            return;
        var max = actor != null ? SumHitpointsMax(actor) : 0;
        if (rally == null || rally.Pool <= 0 || max <= 0)
        {
            var leftover = FindBand(bar);
            if (leftover != null)
                leftover.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
            return;
        }
        var band = FindBand(bar) ?? CreateBand(bar);
        var current = Mathf.Clamp01(actor.GetHitpointsPct());
        var width = Mathf.Min((float)rally.Pool / max, 1f - current);
        band.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);
        band.style.left = new StyleLength(Length.Percent(current * 100f));
        band.style.width = new StyleLength(Length.Percent(width * 100f));
    }

    private static VisualElement FindBand(MenaceBar bar)
    {
        for (var i = 0; i < bar.childCount; i++)
        {
            var child = bar.ElementAt(i);
            if (child != null && child.name == BandName)
                return child;
        }
        return null;
    }

    private static VisualElement CreateBand(MenaceBar bar)
    {
        var band = new VisualElement { name = BandName, pickingMode = PickingMode.Ignore };
        band.style.position = new StyleEnum<Position>(Position.Absolute);
        band.style.top = new StyleLength(0f);
        band.style.bottom = new StyleLength(0f);
        band.style.backgroundColor = new StyleColor(RallyGrey);
        bar.Add(band);
        return band;
    }

    // The rally handler registered itself for its owning actor at mission
    // start, so this is a pointer lookup rather than a scan of the actor's
    // whole skill container on every HP event of every unit.
    private static RallyHandler FindRally(Actor actor)
    {
        if (_instance == null || actor == null)
            return null;
        _instance._rallies.TryGetValue(actor.Pointer, out var rally);
        return rally;
    }
}
