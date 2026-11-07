using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Avalanche, Suomi's tier 1 perk: every hit she lands builds Freeze on the target (the Phase
// damage ledger, ElementsSystem), and her attacks on a Frozen target deal more damage. A skill's
// BuildUpPerAttack is split across its repetitions, so a burst builds a little per round and a
// single heavy shot builds it all at once.
[JiangyuType("Avalanche")]
public sealed partial class Avalanche : SkillEventHandlerTemplate
{
    public float BuildUpPerAttack = 24f;
    public float FrozenDamageMult = 1.2f;

    public override SkillEventHandler Create()
        => new AvalancheHandler { BuildUpPerAttack = BuildUpPerAttack, FrozenDamageMult = FrozenDamageMult };
}

[JiangyuType("AvalancheHandler")]
public sealed partial class AvalancheHandler : SkillEventHandler
{
    public float BuildUpPerAttack = 24f;
    public float FrozenDamageMult = 1.2f;

    private static int Freeze => ElementsSystem.ElementIndex("Freeze");

    // Fires per target per connecting hit of any of her attacks.
    public override void OnTargetHit(Skill skill, Entity targetEntity, DamageInfo damageInfo)
    {
        try
        {
            if (skill == null || !skill.IsAttack() || BuildUpPerAttack <= 0f)
                return;
            var victim = targetEntity?.TryCast<Actor>();
            var attacker = GetActor();
            if (victim == null || !victim.IsAlive() || attacker == null || !Pierce.IsHostileTo(attacker, victim))
                return;
            var repetitions = System.Math.Max(1, (int)(skill.GetTemplate()?.TryCast<SkillTemplate>()?.Repetitions ?? 1));
            ElementsSystem.AddBuildUp(victim, Freeze, BuildUpPerAttack / repetitions);
        }
        catch (Exception ex)
        {
            Log.Warn($"avalanche: build-up failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public override void OnBeforeAnySkillUsed(Skill _skill, Tile _fromTile, Tile _targetTile, EntityProperties _properties, Entity _overrideTargetEntity)
    {
        try
        {
            if (_skill == null || _properties == null || !_skill.IsAttack())
                return;
            var victim = _overrideTargetEntity?.TryCast<Actor>() ?? TileActors.TargetOn(_targetTile);
            if (victim != null && ElementsSystem.HasLiveEffect(victim, Freeze))
                _properties.DamageMult *= FrozenDamageMult;
        }
        catch (Exception ex)
        {
            Log.Warn($"avalanche: pre-attack failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
