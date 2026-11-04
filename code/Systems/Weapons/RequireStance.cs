using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Greys a skill out (usable false, never hidden) unless the squad is in the given stance.
// Vanilla's ChangeStance gate hides the skill instead, which is how Deploy and Get Up trade
// places. The stance effects (special.stance.deployed and friends) sit on every actor and
// gate themselves on the stance, so a DisableSkills on them would disable for good.
[JiangyuType("RequireStance")]
public sealed partial class RequireStance : SkillEventHandlerTemplate
{
    public ActorStance Stance = ActorStance.Default;

    public override SkillEventHandler Create() => new RequireStanceHandler { Stance = Stance };
}

[JiangyuType("RequireStanceHandler")]
public sealed partial class RequireStanceHandler : SkillEventHandler
{
    public ActorStance Stance = ActorStance.Default;

    public override bool IsUsable()
    {
        try
        {
            return GetActor() is not { } actor || actor.GetStance() == Stance;
        }
        catch
        {
            return true;
        }
    }
}
