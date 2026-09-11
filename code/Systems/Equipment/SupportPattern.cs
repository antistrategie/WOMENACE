using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

[JiangyuType("SupportPattern")]
public sealed partial class SupportPattern : SkillEventHandlerTemplate
{
    public int Recipients = 2;
    public SkillTemplate Effect;

    public override SkillEventHandler Create()
        => new SupportPatternHandler { Recipients = Recipients, Effect = Effect };
}

[JiangyuType("SupportPatternHandler")]
public sealed partial class SupportPatternHandler : SkillEventHandler
{
    public int Recipients;
    public SkillTemplate Effect;

    public override void OnMissionStarted()
    {
        var wearer = GetActor();
        if (wearer == null || Effect == null || Recipients <= 0)
            return;

        var allies = new List<UnitActor>();
        TacticalManager.Get()?.ForEachActor((Il2CppSystem.Action<Actor>)(Action<Actor>)(actor =>
        {
            if (actor?.TryCast<UnitActor>() is { } unit && unit.IsAlive()
                && unit.IsAlliedWith(wearer) && unit.GetLeader() != null)
                allies.Add(unit);
        }));

        foreach (var ally in allies
            .OrderByDescending(unit => unit.GetLeader().GetAttributes().GetAsFloat(UnitLeaderAttribute.WeaponSkill))
            .ThenBy(unit => unit.GetLeader().LeaderTemplate.GetID(), StringComparer.Ordinal)
            .Take(Recipients))
        {
            if (SkillEffects.FindInstance(ally.GetSkills(), Effect) == null)
                SkillEffects.TryAddEffect(ally, Effect, message => Log.Warn($"Remolding Pattern: {message}"));
        }
    }
}
