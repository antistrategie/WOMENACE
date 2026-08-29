using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Gathering Steam, Lewis's signature mechanic. In GFL1, Lewis gains an extra round and faster
// reloads every time she reloads, stacking three times over a fight. MENACE has no reload event
// we can hook, so the callback rides turns instead: landing a hit with her own weapon builds a
// stack (once per turn, capped at MaxStacks), and GatheringSteamSystem's FillDamageInfo postfix
// (same hook SsrImprintSystem uses for the SSR owner bonus) adds DamagePerStack flat damage per
// shot for however many stacks she is currently carrying. Stacks only ever grow across a mission,
// never decay: the longer Lewis is in the fight, the harder she hits, exactly like the source kit.
[JiangyuType("GatheringSteam")]
public sealed partial class GatheringSteam : SkillEventHandlerTemplate
{
    public int MaxStacks = 3;
    public int DamagePerStack = 4;
    public SkillTemplate StatusEffect;

    public override SkillEventHandler Create()
        => new GatheringSteamHandler { MaxStacks = MaxStacks, DamagePerStack = DamagePerStack, StatusTemplate = StatusEffect };
}

// Slotted onto the status skill so it renders its StackCount badge (the stacks-so-far number),
// same trick DeathDefyingStatus uses.
[JiangyuType("GatheringSteamStatus")]
public sealed partial class GatheringSteamStatus : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create() => new GatheringSteamStatusHandler();
}

[JiangyuType("GatheringSteamStatusHandler")]
public sealed partial class GatheringSteamStatusHandler : SkillEventHandler
{
    public override bool HasStackCountDisplayed() => true;
}

[JiangyuType("GatheringSteamHandler")]
public sealed partial class GatheringSteamHandler : SkillEventHandler
{
    public int MaxStacks = 3;
    public int DamagePerStack = 4;
    public SkillTemplate StatusTemplate;

    private int _stacks;
    private bool _gainedThisTurn;
    private Skill _statusSkill;

    // Registered by owner pointer so GatheringSteamSystem's damage postfix is a lookup, not a
    // per-shot scan of the attacker's whole skill container. Same shape as RallyBarsSystem's
    // registry.
    private static readonly Dictionary<IntPtr, GatheringSteamHandler> Registry = new();

    internal static int BonusDamageFor(Entity owner)
    {
        if (owner == null || !Registry.TryGetValue(owner.Pointer, out var handler))
            return 0;
        return handler._stacks * handler.DamagePerStack;
    }

    public override void OnMissionStarted()
    {
        _stacks = 0;
        _gainedThisTurn = false;
        var owner = GetEntity();
        if (owner != null)
            Registry[owner.Pointer] = this;
    }

    public override void OnMissionFinished()
    {
        var owner = GetEntity();
        if (owner != null)
            Registry.Remove(owner.Pointer);
        _stacks = 0;
        RemoveStatus();
    }

    public override void OnTurnStart()
        => _gainedThisTurn = false;

    // Fires per target entity Lewis's own attacks hit this turn (the Rally precedent: a container
    // event scoped to the owner's own skills). One stack per turn, on the first hit that lands.
    public override void OnTargetHit(Skill skill, Entity targetEntity, DamageInfo damageInfo)
    {
        try
        {
            if (_gainedThisTurn || _stacks >= MaxStacks)
                return;

            var actor = GetActor();
            var victim = targetEntity?.TryCast<Actor>();
            if (actor == null || victim == null || !Pierce.IsHostileTo(actor, victim))
                return;

            _stacks++;
            _gainedThisTurn = true;
            GrantOrRefreshStatus();
        }
        catch (Exception ex)
        {
            Log.Error($"[GatheringSteam] stack gain failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Adds (first hit of the mission) or refreshes (later hits) the status badge and its stack
    // count. Mirrors DeathDefying's GrantStatus: build the instance from the perk's own
    // ApplySkillToSelf handler so it is a valid skill instance, not a hand-built one.
    private void GrantOrRefreshStatus()
    {
        try
        {
            if (_statusSkill == null)
            {
                var ps = ParentSkill;
                var applier = ps?.GetEventHandlerOfType<ApplySkillToSelfHandler>();
                if (applier == null)
                    return;
                applier.ApplySkill();
                var inst = applier.GetSkillInstance();
                var owner = GetEntity();
                var skills = owner?.GetSkills();
                if (inst == null || skills == null)
                    return;
                var tmpl = inst.GetTemplate();
                if (tmpl == null || skills.GetSkillByTemplate(tmpl) == null)
                    skills.Add(inst);
                _statusSkill = inst;
            }
            SetStackCount(_stacks);
        }
        catch (Exception ex)
        {
            Log.Error($"[GatheringSteam] GrantOrRefreshStatus failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void RemoveStatus()
    {
        _statusSkill = null;
        if (StatusTemplate == null)
            return;
        try
        {
            var owner = GetEntity();
            var skills = owner?.GetSkills();
            skills?.Remove(StatusTemplate);
        }
        catch (Exception ex)
        {
            Log.Error($"[GatheringSteam] RemoveStatus failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void SetStackCount(int n)
        => _statusSkill?.StackCount = n;
}
