using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

public sealed class SinbreakerCombatSystem : JiangyuSystem
{
    private const string MonarchId = "perk.voymastina_monarch";
    private const string GuardId = "perk.voymastina_breach_guard";
    private const string MarkEffectId = "effect.voymastina_target_lock";
    private const string GuardEffectId = "effect.voymastina_breach_guard";
    private static SinbreakerCombatSystem _instance;
    private SkillTemplate _monarch, _guard, _markEffect, _guardEffect;

    private sealed class Owner
    {
        internal Actor Actor;
        internal readonly SinbreakerCombatState State = new();
        internal readonly Dictionary<long, Actor> Targets = new();
        internal Skill BunkerSkill;

        internal bool IsCommittedUse(Skill skill)
            => skill != null && State.IsCommittedUse(skill.Pointer.ToInt64(), skill.UsageId);
    }

    private sealed class Hit
    {
        internal Owner Owner;
        internal Actor Victim;
        internal bool Vehicle;
        internal float RepairFraction;
    }
    private readonly Dictionary<long, Owner> _owners = new();
    // The receive path can recurse through deathrattles. Every entry, including
    // an unrelated hit, gets a frame so its postfix cannot claim an outer hit.
    private readonly Stack<Hit> _hits = new();

    public override void OnInit()
    {
        _instance = this;
        Context.Hooks.Subscribe<MissionFinishedContext>(_ => ResetCombat());
        Context.Patches.Prefix("Il2CppMenace.Tactical.TacticalManager", "Dispose", _ => ResetCombat());
        Context.Patches.Prefix("Il2CppMenace.Tactical.TacticalManager", "OnInit", _ => ResetCombat());
        // Expire guard before the owner's native turn-start effects can deal damage.
        Context.Patches.Prefix("Il2CppMenace.Tactical.Actor", "OnTurnStart", OnTurnStart);
        // This is after SkillContainer.OnTurnEnd, not InvokeOnTurnEnd, which is
        // raised before the native effect countdown.
        Context.Patches.Postfix("Il2CppMenace.Tactical.Actor", "OnTurnEnd", OnTurnEnd);
        Context.Patches.Prefix("Il2CppMenace.Tactical.Actor", "OnDamageReceived", 3, BeforeDamage);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Actor", "OnDamageReceived", 3, AfterDamage);
        // FillDamageInfo (0x70A230) builds actual hit properties at 0x70A3D6.
        // GetExpectedDamage (0x70C340) uses its own builder at 0x70C51E.
        // ApplyToTile schedules delayed repetitions, so its return is not an
        // attack-completion boundary. Keep one committed use, gated by the exact
        // skill and UsageId, until the next real bunker use or scene reset. Previews
        // never enter this scope and cannot borrow its consumed Target Lock.
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.Skill", "FillDamageInfo", BeforeDamageBuild);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "FillDamageInfo", AfterDamageBuild);
    }

    public override void OnTemplatesApplied()
    {
        _monarch = Templates.ById<SkillTemplate>(MonarchId, Context.Log.Warn);
        _guard = Templates.ById<SkillTemplate>(GuardId, Context.Log.Warn);
        _markEffect = Templates.ById<SkillTemplate>(MarkEffectId, Context.Log.Warn);
        _guardEffect = Templates.ById<SkillTemplate>(GuardEffectId, Context.Log.Warn);
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
        => ResetCombat();

    // Campaign saves end the tactical manager instead of serialising combat.
    // Clear on manager lifecycle too, since replacement need not change scenes.
    private void ResetCombat()
    {
        _owners.Clear();
        _hits.Clear();
    }

    private Owner Find(Actor actor)
        => actor != null && _owners.TryGetValue(actor.Pointer.ToInt64(), out var owner) ? owner : null;

    private Owner Get(Actor actor)
    {
        if (actor == null || Affinity.CharacterTag(actor) != "wmgfl_voymastina"
            || actor.GetTemplate()?.GetID() is not ("player_vehicle.voymastina_mech" or "player_vehicle.voymastina_mech_erwin"))
            return null;
        var key = actor.Pointer.ToInt64();
        if (!_owners.TryGetValue(key, out var owner))
            _owners[key] = owner = new Owner { Actor = actor };
        return owner;
    }

    private SinbreakerMonarchHandler Monarch(Actor actor)
        => SkillEffects.FindInstance(actor?.GetSkills(), _monarch)?.GetEventHandlerOfType<SinbreakerMonarchHandler>();

    internal static float TargetMultiplier(Actor actor, Skill skill, Actor target, float bonus)
        => skill != null && target != null && _instance?.Find(actor) is { } owner
            ? owner.State.BunkerMultiplier(target.Pointer.ToInt64(), bonus, skill.Pointer.ToInt64(), skill.UsageId)
            : 1f;

    internal static bool HasGuard(Actor actor) => _instance?.Find(actor)?.State.GuardActive == true;

    private void BeforeDamageBuild(PatchInfo info)
    {
        if (info.Instance is Skill skill && Find(skill.GetActor()) is { } owner)
            owner.State.EnterDamageBuild(skill.Pointer.ToInt64(), skill.UsageId);
    }

    private void AfterDamageBuild(PatchInfo info)
    {
        if (info.Instance is Skill skill && Find(skill.GetActor()) is { } owner)
            owner.State.ExitDamageBuild();
    }

    internal static void Mark(Actor actor, Actor target)
    {
        var system = _instance;
        if (system == null || target == null || !target.IsAlive() || !Pierce.IsHostileTo(actor, target)
            || system.Monarch(actor) == null || system.Get(actor) is not { } owner)
            return;
        var key = target.Pointer.ToInt64();
        owner.Targets[key] = target;
        owner.State.Mark(key);
        system.RefreshMark(target);
    }

    internal static void BeginBunker(Actor actor, Skill skill, Actor target)
    {
        var system = _instance;
        if (system == null || target == null || !target.IsAlive() || !Pierce.IsHostileTo(actor, target)
            || system.Get(actor) is not { } owner)
            return;
        var hasGuard = SkillEffects.FindInstance(actor.GetSkills(), system._guard) != null;
        if (!owner.State.BeginBunker(target.Pointer.ToInt64(), hasGuard, skill.Pointer.ToInt64(), skill.UsageId))
            return;
        owner.BunkerSkill = skill;
        system.RefreshMark(target);
        system.RefreshGuard(owner);
    }

    private void OnTurnStart(PatchInfo info)
    {
        if (info.Instance is not Actor actor || Get(actor) is not { } owner)
            return;
        owner.State.StartTurn();
        RefreshGuard(owner);
        RefreshMarks(owner);
    }

    private void OnTurnEnd(PatchInfo info)
    {
        if (info.Instance is not Actor actor || Find(actor) is not { } owner)
            return;
        owner.State.EndTurn();
        RefreshMarks(owner);
    }

    private void RefreshMarks(Owner owner)
    {
        foreach (var target in owner.Targets.Values)
            RefreshMark(target);
    }

    private void RefreshMark(Actor target)
    {
        if (target == null)
            return;
        var key = target.Pointer.ToInt64();
        var remaining = _owners.Values.Select(owner => owner.State.MarkEndsRemaining(key)).DefaultIfEmpty().Max();
        SetStatus(target, _markEffect, remaining);
    }

    private void RefreshGuard(Owner owner)
    {
        SetStatus(owner.Actor, _guardEffect, owner.State.GuardActive ? 1 : 0);
        owner.Actor.GetSkills()?.ScheduleUpdate();
    }

    private void SetStatus(Actor actor, SkillTemplate template, int count)
    {
        var skills = actor.GetSkills();
        if (count == 0 || !actor.IsAlive())
        {
            // Deferred removals must clear the overhead icon before the container sweep.
            if (SkillEffects.RemoveInstances(skills, template) > 0)
                EffectHudIconSystem.Resync(actor);
        }
        else
        {
            var effect = SkillEffects.FindInstance(skills, template);
            if (effect == null && SkillEffects.TryAddEffect(actor, template, Context.Log.Warn))
                effect = SkillEffects.FindInstance(skills, template);
            if (effect != null)
                effect.StackCount = count;
        }
        RallyBarsSystem.RefreshStatusIcons(actor);
    }

    private void BeforeDamage(PatchInfo info)
    {
        Hit hit = null;
        try
        {
            if (info.Instance is Actor victim && victim.IsAlive()
                && info.Args is { Count: > 2 } && info.Args[1] is Skill skill
                && skill.GetID() == SinbreakerCombatState.BunkerId
                && (info.Args[0] as Entity)?.TryCast<Actor>() is { } attacker
                && Find(attacker) is { } owner && Monarch(attacker) is { } monarch
                && owner.IsCommittedUse(skill) && owner.State.BunkerTarget == victim.Pointer.ToInt64()
                && Pierce.IsHostileTo(attacker, victim))
                hit = new Hit { Owner = owner, Victim = victim, Vehicle = victim.IsVehicle(), RepairFraction = monarch.RepairFraction };
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"Sinbreaker execution capture failed: {ex.Message}");
        }
        _hits.Push(hit);
    }

    private void AfterDamage(PatchInfo info)
    {
        if (_hits.Count == 0 || _hits.Pop() is not { } hit)
            return;
        try
        {
            var actor = hit.Owner.Actor;
            if (!actor.IsAlive() || !hit.Owner.State.TryExecution(hit.Victim.Pointer.ToInt64(),
                directBunker: true, hostile: true, destroyed: !hit.Victim.IsAlive()))
                return;
            actor.SetArmorDurability(SinbreakerCombatState.RepairedDurability(actor.GetArmorDurability(),
                actor.GetArmorDurabilityMax(), hit.Vehicle, hit.RepairFraction));
            actor.GetSkills()?.ScheduleUpdate();
            actor.UpdateActorState();
            // Direct durability writes do not notify the overhead armour bar.
            TacticalManager.Get()?.InvokeOnArmorChanged(actor, actor.GetArmorDurabilityPct(),
                actor.GetCurrentProperties().GetArmor(), 0);
            RallyBarsSystem.RefreshStatusIcons(actor);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"Sinbreaker execution repair failed: {ex.Message}");
        }
    }
}
