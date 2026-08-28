using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Il2CppMenace.Tactical.Skills.SkillFilters;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Barrage+ (perk.wmgfl_barrage, OTs-14's clone of Barrage): the vanilla
// discount gates on the vehicle HEAVY weapon slot through an Odin-routed
// ISkillFilter that KDL cannot touch, so an infantry bay never qualifies.
// The CLONE's own deep-copied filter is wrapped (the vanilla perk is left
// untouched, so this is hers alone): anything the vanilla filter accepted
// still passes, plus bay-granted skills whose authored template carried a
// deployment/setup gate - the bay's definition of a HEAVY weapon (mortars,
// tripod guns); rockets and rifles stay full price.
public sealed class BarrageBaySystem : JiangyuSystem
{
    public override void OnTemplatesApplied()
    {
        try
        {
            var perk = Templates.ById<SkillTemplate>("perk.wmgfl_barrage");
            var handlers = perk?.EventHandlers;
            if (handlers == null)
            {
                Context.Log.Warn("barrage+: perk.wmgfl_barrage not found, bay discount not installed");
                return;
            }
            var wrapped = 0;
            for (var i = 0; i < handlers.Count; i++)
            {
                var cost = handlers[i]?.TryCast<ChangeActionPointCost>();
                if (cost != null && Wrap(cost.SkillFilter, f => cost.SkillFilter = f))
                    wrapped++;
            }
            if (wrapped > 0)
                Context.Log.Debug($"barrage+: wrapped {wrapped} filter(s) to accept heavy bay weapons");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"barrage+: filter install failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // OnTemplatesApplied can run more than once per session: a filter that is
    // already ours is left alone so the chain never nests.
    private static bool Wrap(ISkillFilter existing, Action<ISkillFilter> assign)
    {
        if (existing != null && existing.TryCast<BarrageBayFilter>() != null)
            return false;
        assign(new BarrageBayFilter { Inner = existing }.Cast<ISkillFilter>());
        return true;
    }
}

// The widened filter: the vanilla filter's verdict, OR a bay-granted skill
// whose authored template carried a deployment gate.
[JiangyuType("BarrageBayFilter", Interfaces = new[] { typeof(ISkillFilter) })]
public sealed partial class BarrageBayFilter : Il2CppSystem.Object
{
    public ISkillFilter Inner;

    public bool Matches(Skill _skill)
    {
        try
        {
            if (Inner != null && Inner.Matches(_skill))
                return true;
            // Classify from the skill's OWN item, never the bay registry: the
            // game evaluates AP costs DURING the grant, milliseconds before
            // the registry entry is written (log-proven), so a registry
            // lookup here always misses. skill.m_Item is set at construction,
            // so it is already correct.
            var item = _skill?.GetItem();
            return item != null && Bay.IsBayWeapon(item) && Bay.IsOrdnance(item);
        }
        catch
        {
            return false;
        }
    }
}
