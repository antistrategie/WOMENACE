using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

[JiangyuType("BulwarkAura")]
public sealed partial class BulwarkAura : SkillEventHandlerTemplate
{
    public float MinimumSuppression = 10f;
    public float DamageMultiplier = 0.875f;

    public override SkillEventHandler Create()
        => new BulwarkAuraHandler { MinimumSuppression = MinimumSuppression, DamageMultiplier = DamageMultiplier };
}

// EmitAura.OnEntitySpawned (RVA 0x755530) assigns the emitter to every
// ChangePropertyAuraHandler subclass. This keeps the native ally and spawn
// handling while checking the emitter's suppression without a map scan.
[JiangyuType("BulwarkAuraHandler")]
public sealed partial class BulwarkAuraHandler : ChangePropertyAuraHandler
{
    public float MinimumSuppression = 10f;
    public float DamageMultiplier = 0.875f;

    public override bool IsEnabled()
        => m_Origin?.TryCast<Actor>() is { } wearer && wearer.IsAlive()
            && wearer.GetSuppression() >= MinimumSuppression
            && GetActor() is { } recipient && recipient.Pointer != wearer.Pointer
            && recipient.IsAlliedWith(wearer);

    public override bool IsHidden() => !IsEnabled();

    public override void OnUpdate(EntityProperties properties)
    {
        if (IsEnabled())
            properties.DamageSustainedMult *= DamageMultiplier;
    }
}

[JiangyuType("BulwarkBurden")]
public sealed partial class BulwarkBurden : SkillEventHandlerTemplate
{
    public float SuppressionFromZero = 30f;
    public float SuppressionWhilePressured = 10f;

    public override SkillEventHandler Create()
        => new BulwarkBurdenHandler
        {
            SuppressionFromZero = SuppressionFromZero,
            SuppressionWhilePressured = SuppressionWhilePressured,
        };
}

[JiangyuType("BulwarkBurdenHandler")]
public sealed partial class BulwarkBurdenHandler : SkillEventHandler
{
    public float SuppressionFromZero = 30f;
    public float SuppressionWhilePressured = 10f;

    public override void OnUse(Actor user, Tile targetTile, UsageParameter usageParams, ref bool applyToTile)
    {
        // Use the native adjustment so crossing a suppression threshold also
        // updates available AP, just as the target's suppression relief does.
        user.ChangeSuppressionAndUpdateAP(user.GetSuppression() > 0f
            ? SuppressionWhilePressured : SuppressionFromZero);
    }
}
