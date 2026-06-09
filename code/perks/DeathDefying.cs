using System;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Death DEFYing: once per mission the first lethal hit is fully negated, leaving
// the owner alive and invulnerable for InvulnTurns turns, during which suppression
// is also cleared. Slotted onto a perk via KDL type="WOMENACE:DeathDefying".
[JiangyuType("DeathDefying")]
public sealed partial class DeathDefying : SkillEventHandlerTemplate
{
    public int InvulnTurns = 3;
    public SkillTemplate StatusEffect;

    public override SkillEventHandler Create()
        => new DeathDefyingHandler { Turns = InvulnTurns, StatusTemplate = StatusEffect };
}

// Slotted onto the status skill so the skill reports HasStackCountDisplayed()=true,
// which makes the status icon render its StackCount badge (the turns-left number).
[JiangyuType("DeathDefyingStatus")]
public sealed partial class DeathDefyingStatus : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create() => new DeathDefyingStatusHandler();
}

[JiangyuType("DeathDefyingStatusHandler")]
public sealed partial class DeathDefyingStatusHandler : SkillEventHandler
{
    public override bool HasStackCountDisplayed() => true;
}

[JiangyuType("DeathDefyingHandler")]
public sealed partial class DeathDefyingHandler : SkillEventHandler
{
    public int Turns = 3;
    public SkillTemplate StatusTemplate;
    private int _remaining;
    private Skill _statusSkill;

    // Once-per-mission, keyed by the owner's IL2CPP pointer. The perk spawns more
    // than one handler instance, so a per-instance flag wouldn't hold. Entity is an
    // Il2CppSystem.Object (no Unity instance id), so the native pointer is its
    // stable identity; cleared each mission to avoid cross-mission pointer reuse.
    private static readonly HashSet<IntPtr> SpentOwners = [];

    public override void OnBeforeMissionStarted() => SpentOwners.Clear();

    public override void OnBeforeDamageReceived(Skill _skill, Entity _attacker, DamageInfo _damageInfo, EntityProperties _properties)
    {
        if (_damageInfo == null)
            return;

        var owner = GetEntity();

        if (_remaining > 0)
        {
            // The active invulnerability window: negate everything.
            Negate(_damageInfo);
            ClearSuppression(owner);
            return;
        }

        if (owner == null || SpentOwners.Contains(owner.Pointer))
            return;

        var hp = owner.GetHitpoints();
        // Applied HP loss runs a little above DamageInfo.Damage (an element/structure
        // component the field omits), so a hit counts as lethal once it comes within
        // a small margin of current HP.
        var lethal = _damageInfo.IsTargetDestroyed || (hp > 0 && _damageInfo.Damage + 2 >= hp);
        if (!lethal)
            return;

        Negate(_damageInfo);
        _remaining = Turns;
        SpentOwners.Add(owner.Pointer);
        ClearSuppression(owner);
        GrantStatus();
        Log.Debug($"[DeathDefying] lethal hit negated; invulnerable for {_remaining} turn(s) (once per mission).");
    }

    public override void OnTurnStart()
    {
        if (_remaining <= 0)
            return;
        ClearSuppression(GetEntity());
        _remaining--;
        if (_remaining <= 0)
            RemoveStatus();
        else
            SetStackCount(_remaining);
    }

    // Cancel an incoming hit: zero Damage and clear the destroyed flags so neither
    // the element nor the entity is marked dead.
    private static void Negate(DamageInfo damageInfo)
    {
        damageInfo.Damage = 0;
        damageInfo.IsTargetDestroyed = false;
        damageInfo.IsElementDestroyed = false;
    }

    // Zero suppression so the active window also frees the owner from being pinned.
    private void ClearSuppression(Entity owner)
    {
        if (owner == null)
            return;
        try
        {
            var actor = owner.TryCast<Actor>();
            if (actor != null && actor.GetSuppression() > 0f)
                actor.SetSuppression(0f);
        }
        catch (Exception ex)
        {
            Log.Error($"[DeathDefying] ClearSuppression failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Show the status icon. The ApplySkillToSelf effect on the perk builds a valid
    // status-skill instance; we add that instance to the container so it renders.
    private void GrantStatus()
    {
        try
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
            SetStackCount(_remaining);
        }
        catch (Exception ex)
        {
            Log.Error($"[DeathDefying] GrantStatus failed: {ex.GetType().Name}: {ex.Message}");
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
            Log.Error($"[DeathDefying] RemoveStatus failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Drive the status icon's number badge with the turns left.
    private void SetStackCount(int n)
    {
        if (_statusSkill != null)
            _statusSkill.StackCount = n;
    }
}
