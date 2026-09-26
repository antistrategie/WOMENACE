using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

[JiangyuType("SinbreakerMonarch")]
public sealed partial class SinbreakerMonarch : SkillEventHandlerTemplate
{
    public float TargetLockBonus = 0.25f;
    public float RepairFraction = 0.3f;

    public override SkillEventHandler Create()
        => new SinbreakerMonarchHandler { TargetLockBonus = TargetLockBonus, RepairFraction = RepairFraction };
}

[JiangyuType("SinbreakerMonarchHandler")]
public sealed partial class SinbreakerMonarchHandler : SkillEventHandler
{
    public float TargetLockBonus;
    public float RepairFraction;

    public override void OnBeforeAnySkillUsed(Skill skill, Tile fromTile, Tile targetTile,
        EntityProperties properties, Entity overrideTargetEntity)
    {
        if (properties != null && skill?.GetID() == SinbreakerCombatState.BunkerId)
            properties.DamageMult *= SinbreakerCombatSystem.TargetMultiplier(GetActor(), skill,
                (overrideTargetEntity ?? targetTile?.GetEntity())?.TryCast<Actor>(), TargetLockBonus);
    }
}

[JiangyuType("SinbreakerArcRounds")]
public sealed partial class SinbreakerArcRounds : SkillEventHandlerTemplate
{
    public float Penetration = 20f;
    public float ArmourDamageMultiplier = 2f;

    public override SkillEventHandler Create()
        => new SinbreakerArcRoundsHandler { Penetration = Penetration, ArmourDamageMultiplier = ArmourDamageMultiplier };
}

[JiangyuType("SinbreakerArcRoundsHandler")]
public sealed partial class SinbreakerArcRoundsHandler : SkillEventHandler
{
    public float Penetration;
    public float ArmourDamageMultiplier;

    public override void OnBeforeAnySkillUsed(Skill skill, Tile fromTile, Tile targetTile,
        EntityProperties properties, Entity overrideTargetEntity)
    {
        if (properties == null)
            return;
        var modifiers = SinbreakerCombatState.ArcModifiers(skill?.GetID(), Penetration, ArmourDamageMultiplier);
        properties.ArmorPenetration += modifiers.Penetration;
        properties.DamageToArmorDurabilityMult *= modifiers.ArmourDamageMultiplier;
    }
}

[JiangyuType("SinbreakerBreachGuard")]
public sealed partial class SinbreakerBreachGuard : SkillEventHandlerTemplate
{
    public float DamageMultiplier = 0.75f;

    public override SkillEventHandler Create()
        => new SinbreakerBreachGuardHandler { DamageMultiplier = DamageMultiplier };
}

[JiangyuType("SinbreakerBreachGuardHandler")]
public sealed partial class SinbreakerBreachGuardHandler : SkillEventHandler
{
    public float DamageMultiplier;

    public override void OnUpdate(EntityProperties properties)
    {
        // FillDamageInfo folds DamageSustainedMult into hull damage at 0x70A651.
        // Armour wear is calculated separately at 0x70AAA9. GetExpectedDamage
        // uses the same split, so this also preserves hover parity.
        if (SinbreakerCombatSystem.HasGuard(GetActor()))
            properties.DamageSustainedMult *= DamageMultiplier;
    }
}

// Carrying-skill callbacks distinguish real use and hits from property queries.
// OnApply's hit flag includes a hit which armour stopped completely.
[JiangyuType("SinbreakerWeapon")]
public sealed partial class SinbreakerWeapon : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create() => new SinbreakerWeaponHandler();
}

[JiangyuType("SinbreakerWeaponHandler")]
public sealed partial class SinbreakerWeaponHandler : SkillEventHandler
{
    public override void OnUse(Actor user, Tile targetTile, UsageParameter usageParams, ref bool applyToTile)
    {
        if (ParentSkill?.GetID() == SinbreakerCombatState.BunkerId)
            SinbreakerCombatSystem.BeginBunker(user, ParentSkill, targetTile?.GetEntity()?.TryCast<Actor>());
    }

    public override void OnApply(Actor user, Tile userTile, Tile targetTile, Tile centreTargetTile, Element element, bool isHit)
    {
        if (isHit && ParentSkill?.GetID() == SinbreakerCombatState.GunId)
            SinbreakerCombatSystem.Mark(user, targetTile?.GetEntity()?.TryCast<Actor>());
    }
}
