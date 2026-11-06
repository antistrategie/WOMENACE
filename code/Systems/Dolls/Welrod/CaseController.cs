using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Sets the owner's critical chance to a fixed value as her properties are rebuilt. Welrod's Case
// Controller sets it to zero. Bonuses added when an attack is built (Pistol Mastery) still apply on
// top, since those are added after this.
[JiangyuType("SetCriticalChance")]
public sealed partial class SetCriticalChance : SkillEventHandlerTemplate
{
    public int Value;

    public override SkillEventHandler Create() => new SetCriticalChanceHandler { Value = Value };
}

[JiangyuType("SetCriticalChanceHandler")]
public sealed partial class SetCriticalChanceHandler : SkillEventHandler
{
    public int Value;

    public override void OnUpdate(EntityProperties _properties)
    {
        if (_properties != null)
            _properties.CriticalChance = Value;
    }
}

