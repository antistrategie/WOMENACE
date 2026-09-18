using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The KDL-facing "heal one squaddie on use" handler. A skill declares
//
//     append "EventHandlers" type="WOMENACE:HealOneSquaddie" {}
//
// and using it restores the user's most wounded living element to full hitpoints.
//
// Mechty's Sleep Aid Kit needs this because vanilla's Regeneration handler only heals on
// OnApply or a turn boundary, and that kit closes the turn from its own use: the forced turn
// end dispatches synchronously inside the use, before the apply phase, so an OnApply heal
// would land after the bleed ticks it was meant to beat. Sibling of HealUserToFull, which
// restores every element.
[JiangyuType("HealOneSquaddie")]
public sealed partial class HealOneSquaddie : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create() => new HealOneSquaddieHandler();
}

[JiangyuType("HealOneSquaddieHandler")]
public sealed partial class HealOneSquaddieHandler : SkillEventHandler
{
    public override void OnUse(Actor _user, Tile _targetTile, UsageParameter _usageParams, ref bool _applyToTile)
    {
        try
        {
            var actor = _user ?? GetActor();
            if (actor == null)
                return;
            var elements = actor.GetElements();
            Element worst = null;
            var worstFraction = 1f;
            for (var i = 0; elements != null && i < elements.Count; i++)
            {
                var element = elements[i];
                // A downed element stays down: this patches the wounded, it does not revive.
                if (element == null || element.GetHitpoints() <= 0)
                    continue;
                var max = element.GetHitpointsMax();
                var fraction = max > 0 ? element.GetHitpoints() / (float)max : 1f;
                if (fraction < worstFraction)
                {
                    worstFraction = fraction;
                    worst = element;
                }
            }
            if (worst == null)
            {
                // Every living element is at full: a squad whose losses are all dead elements has
                // nothing this can patch, the same as vanilla's per-element Regeneration.
                Log.Debug("heal one squaddie: no wounded living element");
                return;
            }
            worst.SetHitpoints(worst.GetHitpointsMax());
            actor.UpdateHitpoints();
            // The HUD-notify trio HealUserToFull uses, so the overhead bar redraws.
            TacticalManager.Get()?.InvokeOnHitpointsChanged(actor, actor.GetHitpointsPct(), 0);
            Log.Debug($"heal one squaddie: restored an element from {worstFraction:P0} to full");
        }
        catch (Exception ex)
        {
            Log.Warn($"heal one squaddie: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
