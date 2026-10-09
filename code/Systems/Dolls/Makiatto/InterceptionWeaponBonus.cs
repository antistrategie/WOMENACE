using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Interception bonuses for shots fired with one weapon, at any calibration rank: extra damage, and
// a discount InterceptionSystem.Fire adds to Overwatch's own when it prices the shot.
//
// The damage bonus asks InterceptionSystem.IsReaction, so her own shots and vanilla auto-attacks
// (counterattacks set WasAutoTriggered too) never carry it. The damage is scaled in
// OnBeforeAnySkillUsed, the window Focus Fire uses, so it lands on that shot's property snapshot
// only.
[JiangyuType("InterceptionWeaponBonus")]
public sealed partial class InterceptionWeaponBonus : SkillEventHandlerTemplate
{
    public WeaponTemplate Weapon;
    public float DamageBonus = 0.2f;
    // share of the weapon's AP cost spared on top of Overwatch's ApDiscount, 0 to 1
    public float ApDiscount = 0.15f;

    public override SkillEventHandler Create()
        => new InterceptionWeaponBonusHandler { Weapon = Weapon, DamageBonus = DamageBonus, ApDiscount = ApDiscount };
}

[JiangyuType("InterceptionWeaponBonusHandler")]
public sealed partial class InterceptionWeaponBonusHandler : SkillEventHandler
{
    public WeaponTemplate Weapon;
    public float DamageBonus = 0.2f;
    public float ApDiscount = 0.15f;

    private Actor _actor;

    // whether the skill belongs to the weapon, any calibration rank (weapon_rN) included
    internal bool Matches(Skill skill)
        => Weapon != null && skill != null
            && Calibration.TryParseRank(skill.GetItem()?.GetTemplate()?.GetID(), Weapon.GetID(), out _);

    // GetActor can be null at OnMissionStarted, so she is registered on the first update with a
    // live actor, as InterceptionHandler is.
    public override void OnUpdate(EntityProperties _properties)
    {
        if (_actor != null)
            return;
        var actor = GetActor();
        if (actor == null || !actor.IsAlive())
            return;
        _actor = actor;
        InterceptionSystem.RegisterBonus(actor, this);
    }

    public override void OnMissionFinished()
    {
        InterceptionSystem.UnregisterBonus(_actor);
        _actor = null;
    }

    public override void OnBeforeAnySkillUsed(Skill _skill, Tile _fromTile, Tile _targetTile, EntityProperties _properties, Entity _overrideTargetEntity)
    {
        try
        {
            if (_properties == null || !InterceptionSystem.IsReaction(_skill) || !_skill.IsAttack() || !Matches(_skill))
                return;
            _properties.DamageMult *= 1f + DamageBonus;
        }
        catch (Exception ex)
        {
            Log.Warn($"interception bonus: pre-attack failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
