using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The KDL-facing "restore the user to full HP" handler. A skill declares
//
//     append "EventHandlers" type="WOMENACE:HealUserToFull" {}
//
// and using it brings every living element of the user back to full hitpoints.
//
// OTs-14's Repair Kit needs this because its `active.treat_wounds` base cannot be reused as-is:
// that template's Regeneration handler is an OnTurnStart full heal that fires passively every
// round for as long as the granted skill exists. The clone clears its handlers in KDL and puts
// the heal on the use path instead.
[JiangyuType("HealUserToFull")]
public sealed partial class HealUserToFull : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create() => new HealUserToFullHandler();
}

[JiangyuType("HealUserToFullHandler")]
public sealed partial class HealUserToFullHandler : SkillEventHandler
{
    public override void OnUse(Actor _user, Tile _targetTile, UsageParameter _usageParams, ref bool _applyToTile)
    {
        try
        {
            var actor = _user ?? GetActor();
            if (actor == null)
                return;
            var elements = actor.GetElements();
            for (var i = 0; elements != null && i < elements.Count; i++)
            {
                var element = elements[i];
                // A downed element stays down: this restores the wounded, it does not revive.
                if (element != null && element.GetHitpoints() > 0)
                    element.SetHitpoints(element.GetHitpointsMax());
            }
            actor.UpdateHitpoints();
            // The HUD-notify trio, the same sequence the reinforcement fairy uses to make the
            // overhead bar redraw.
            TacticalManager.Get()?.InvokeOnHitpointsChanged(actor, actor.GetHitpointsPct(), 0);
            Log.Debug($"heal to full: restored {actor.GetHitpoints()}/{actor.GetHitpointsMax()} hp");
        }
        catch (Exception ex)
        {
            Log.Warn($"heal to full: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
