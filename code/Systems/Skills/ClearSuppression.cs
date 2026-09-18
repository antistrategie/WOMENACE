using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The KDL-facing "remove all suppression on use" handler. A skill declares
//
//     append "EventHandlers" type="WOMENACE:ClearSuppression" {}
//
// and using it brings the user's suppression to exactly zero through the native
// adjustment, so the AP the suppression was holding back is released as well.
//
// Mechty's Sleep Aid Kit needs this because vanilla's ChangeSuppression subtracts a flat
// amount and the engine does not clamp the result at zero: a -100 on a lightly suppressed
// unit leaves negative suppression, which the accuracy formula pays out as a bonus for the
// rest of the mission.
[JiangyuType("ClearSuppression")]
public sealed partial class ClearSuppression : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create() => new ClearSuppressionHandler();
}

[JiangyuType("ClearSuppressionHandler")]
public sealed partial class ClearSuppressionHandler : SkillEventHandler
{
    public override void OnUse(Actor _user, Tile _targetTile, UsageParameter _usageParams, ref bool _applyToTile)
    {
        try
        {
            var actor = _user ?? GetActor();
            if (actor == null)
                return;
            var current = actor.GetSuppression();
            if (current > 0f)
                actor.ChangeSuppressionAndUpdateAP(-current);
            Log.Debug($"clear suppression: {current:0.#} -> {actor.GetSuppression():0.#}");
        }
        catch (Exception ex)
        {
            Log.Warn($"clear suppression: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
