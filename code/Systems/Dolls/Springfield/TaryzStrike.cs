using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Il2CppMenace.UI;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Taryz Strike, the imprint skill on Springfield's SSR rifle. Springfield aims it like a shot, but
// Taryz takes it: the target is checked from Taryz's tile, the hit chance and the aim line come
// from her, and on use Springfield's skill never runs. Springfield pays the AP and Taryz fires her
// own hidden strike skill (animation, projectile, damage and Hydro all hers). The shot is fired
// the way Makiatto's Interception fires off-turn: Skill.Use with Free | IgnoreUsabilityCheck.
//
// The command skill is Springfield's alone (IsOwnerWielding) and needs her Taryz deployed. Taryz
// is the Falcon skybot's kind of unit, an allied AI with its own turns, so her strike carries
// TaryzStrikeGate: it is usable only while the command checks or fires it, and her own AI never
// takes the shot.
[JiangyuType("TaryzCommand")]
public sealed partial class TaryzCommand : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create() => new TaryzCommandHandler();
}

[JiangyuType("TaryzCommandHandler")]
public sealed partial class TaryzCommandHandler : SkillEventHandler
{
    public override bool IsHidden() => !SsrImprintSystem.IsOwnerWielding(ParentSkill);

    public override bool IsUsable()
    {
        try
        {
            return SsrImprintSystem.IsOwnerWielding(ParentSkill) && TaryzStrikeSystem.StrikeOf(GetActor()).Strike != null;
        }
        catch
        {
            return false;
        }
    }

    // The command carries no Attack handler of its own, so the tooltip's damage and accuracy
    // graph comes from Taryz's strike: its Attack handler appends the section from her stats.
    // Without a deployed Taryz there is no strike instance and no graph.
    public override void AppendTooltipData(TooltipData _tooltip)
    {
        try
        {
            var handlers = TaryzStrikeSystem.StrikeOf(GetActor()).Strike?.m_EventHandlers;
            for (var i = 0; handlers != null && i < handlers.Length; i++)
            {
                if (handlers[i]?.TryCast<AttackHandler>() is { } attack)
                {
                    attack.AppendTooltipData(_tooltip);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"taryz strike: tooltip failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Range and line of fire from Taryz's tile, by her own strike skill's targeting rules.
    public override bool OnVerifyTarget(Tile _originTile, Tile _targetTile)
    {
        try
        {
            var (taryz, strike) = TaryzStrikeSystem.StrikeOf(GetActor());
            var from = taryz?.GetTile();
            if (strike == null || from == null || _targetTile == null)
                return false;
            TaryzStrikeSystem.Commanding = true;
            try { return strike.IsUsableOn(_targetTile, from); }
            finally { TaryzStrikeSystem.Commanding = false; }
        }
        catch
        {
            return false;
        }
    }
}

// On Taryz's strike: unusable unless Springfield's Taryz Strike is checking or firing it.
[JiangyuType("TaryzStrikeGate")]
public sealed partial class TaryzStrikeGate : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create() => new TaryzStrikeGateHandler();
}

[JiangyuType("TaryzStrikeGateHandler")]
public sealed partial class TaryzStrikeGateHandler : SkillEventHandler
{
    public override bool IsUsable() => TaryzStrikeSystem.Commanding;
}

public sealed class TaryzStrikeSystem : JiangyuSystem
{
    internal const string CommandSkillId = "active.springfield_ssr_taryz_strike";
    private const string StrikeSkillId = "active.springfield_taryz_strike";
    private const string TaryzId = "player_springfield_taryz";
    private const string MuzzleName = "muzzle";

    private bool _redrawing;

    // Set while Springfield's command checks or fires Taryz's strike (TaryzStrikeGate).
    internal static bool Commanding;

    public override void OnInit()
    {
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.Skill", "Use", OnSkillUse);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "GetHitchance", OnHitchance);
        Context.Patches.Prefix("Il2CppMenace.Tactical.TargetAimVisualizer", "UpdateAim", OnUpdateAim);
    }

    public override void OnUnload()
    {
        _commandTemplate = IntPtr.Zero;
        _taryzTemplate = null;
        _strikeTemplate = null;
        _lookups.Clear();
    }

    private static IntPtr _commandTemplate;
    private static EntityTemplate _taryzTemplate;
    private static SkillTemplate _strikeTemplate;

    // Pointer compare: GetHitchance runs for every hovered tile and every AI evaluation, and an id
    // read would marshal a string each time.
    private static bool IsCommand(Skill skill)
    {
        if (skill == null)
            return false;
        if (_commandTemplate == IntPtr.Zero)
            _commandTemplate = Templates.ById<SkillTemplate>(CommandSkillId)?.Pointer ?? IntPtr.Zero;
        return _commandTemplate != IntPtr.Zero && skill.GetTemplate()?.Pointer == _commandTemplate;
    }

    // One lookup per owner per frame. Targeting asks for every candidate tile, the hit chance
    // and the aim line on every hover, and each fresh lookup is a full actor scan.
    private sealed class Lookup
    {
        public int Frame = -1;
        public Actor Taryz;
        public Skill Strike;
    }

    private static readonly Dictionary<IntPtr, Lookup> _lookups = new();

    // The owner's deployed Taryz and her strike skill. SpawnLimit 1 on the deploy keeps one Taryz.
    internal static (Actor Taryz, Skill Strike) StrikeOf(Actor owner)
    {
        if (owner == null)
            return (null, null);
        _taryzTemplate ??= Templates.ById<EntityTemplate>(TaryzId);
        _strikeTemplate ??= Templates.ById<SkillTemplate>(StrikeSkillId);
        if (_taryzTemplate == null || _strikeTemplate == null)
            return (null, null);
        var frame = Time.frameCount;
        if (!_lookups.TryGetValue(owner.Pointer, out var lookup))
            _lookups[owner.Pointer] = lookup = new Lookup();
        if (lookup.Frame == frame)
            return (lookup.Taryz, lookup.Strike);
        Actor taryz = null;
        var template = _taryzTemplate.Pointer;
        TacticalManager.Get()?.ForEachActor((Il2CppSystem.Action<Actor>)(Action<Actor>)(actor =>
        {
            if (taryz == null && actor != null && actor.IsAlive() && actor.GetTile() != null
                && actor.GetTemplate()?.Pointer == template && actor.IsAlliedWith(owner))
                taryz = actor;
        }));
        lookup.Frame = frame;
        lookup.Taryz = taryz;
        lookup.Strike = taryz?.GetSkills()?.GetSkillByTemplate(_strikeTemplate, null)?.TryCast<Skill>();
        return (lookup.Taryz, lookup.Strike);
    }

    // Springfield's use is replaced: she pays the AP, Taryz fires. The skipped Use reports whether
    // the strike fired, and the skill-used notifications vanilla Use raises for the user (her own
    // container, the actor's turn state, the tactical event perks and barks listen to) are raised
    // for Springfield's command so the rest of the game sees her act.
    private void OnSkillUse(PatchInfo info)
    {
        if (info.Instance is not Skill skill || !IsCommand(skill))
            return;
        info.Skip = true;
        info.Result = false;
        try
        {
            var owner = skill.GetActor();
            var tile = ((info.Args is { Count: > 0 } ? info.Args[0] : null) as Il2CppSystem.Object)?.TryCast<Tile>();
            var (taryz, strike) = StrikeOf(owner);
            if (owner == null || tile == null || strike == null)
            {
                Context.Log.Warn("taryz strike: no Taryz or target, nothing fired");
                return;
            }
            var cost = skill.GetActionPointCost();
            if (owner.GetActionPoints() < cost)
                return;
            owner.SetActionPoints(owner.GetActionPoints() - cost, false);
            owner.SetAiming(false, null);
            Aim(taryz, strike, tile);
            bool fired;
            Commanding = true;
            try { fired = strike.Use(tile, UsageParameter.Free | UsageParameter.IgnoreUsabilityCheck); }
            finally { Commanding = false; }
            if (!fired)
            {
                taryz.SetAiming(false, null);
                owner.SetActionPoints(owner.GetActionPoints() + cost, false);
                Context.Log.Warn("taryz strike: Taryz's strike refused, AP refunded");
                return;
            }
            info.Result = true;
            owner.GetSkills()?.OnSkillUsed(skill, tile);
            owner.OnSkillUsed(skill);
            TacticalManager.Get()?.InvokeOnSkillUse(owner, skill, tile);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"taryz strike: fire failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Aim(Actor taryz, Skill strike, Tile tile)
    {
        try
        {
            var elements = new Il2CppSystem.Collections.Generic.List<int>();
            var count = taryz.GetElements()?.Count ?? 0;
            for (var i = 0; i < count; i++)
                elements.Add(i);
            taryz.SetAiming(true, strike);
            taryz.AimAt(tile, AimParams.Default, elements, strike);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"taryz strike: aim failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The preview shows Taryz's chance from her tile, not Springfield's.
    private void OnHitchance(PatchInfo info)
    {
        if (info.Instance is not Skill skill || !IsCommand(skill) || info.Args.Count < 7)
            return;
        try
        {
            var (taryz, strike) = StrikeOf(skill.GetActor());
            var from = taryz?.GetTile();
            var target = (info.Args[1] as Il2CppSystem.Object)?.TryCast<Tile>();
            if (strike == null || from == null || target == null)
                return;
            info.Result = strike.GetHitchance(from, target, null, null,
                info.Args[4] is bool dropoff && dropoff,
                (info.Args[5] as Il2CppSystem.Object)?.TryCast<Entity>(),
                info.Args[6] is bool immediate && immediate);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"taryz strike: hit chance failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // SkillAction.HandleMouseMoveOnTile draws the aim line from the active actor. For the command
    // the line is redrawn from Taryz's beak instead.
    private void OnUpdateAim(PatchInfo info)
    {
        if (_redrawing || info.Instance is not TargetAimVisualizer visualizer || info.Args.Count < 3)
            return;
        var skill = (info.Args[2] as Il2CppSystem.Object)?.TryCast<Skill>();
        if (!IsCommand(skill) || info.Args[1] is not Vector3 target)
            return;
        try
        {
            var (taryz, _) = StrikeOf(skill.GetActor());
            var origin = MuzzleOf(taryz);
            if (origin == null)
                return;
            info.Skip = true;
            _redrawing = true;
            visualizer.UpdateAim(origin.Value, target, skill);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"taryz strike: aim line failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _redrawing = false;
        }
    }

    private static Vector3? MuzzleOf(Actor taryz)
    {
        var elements = taryz?.GetElements();
        if (elements == null || elements.Count == 0 || elements[0] == null)
            return null;
        var element = elements[0];
        var muzzle = SceneQuery.FindNamed(element.gameObject, MuzzleName);
        return muzzle != null ? muzzle.transform.position : element.transform.position;
    }
}
