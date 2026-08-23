using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Il2CppMenace.Tactical.Skills.SkillFilters;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Drive By's post-movement AP discount never reaches our melee kit. The discount lives on
// effect.drive_by's ChangeActionPointCost (and its ConsumeOnSkillUse partner), both gated by an
// Odin-routed ISkillFilter that KDL cannot touch, and the vanilla filter does not match the
// blade skills. The fix wraps the shipped filter: anything it accepted still passes, and skills
// carrying the wmgfl_melee tag now pass too, so a doll that moves and stabs gets the same tempo
// the perk gives a doll that moves and shoots.
public sealed class DriveByMeleeSystem : JiangyuSystem
{
    public override void OnTemplatesApplied()
    {
        try
        {
            var effect = Templates.ById<SkillTemplate>("effect.drive_by");
            var handlers = effect?.EventHandlers;
            if (handlers == null)
            {
                Context.Log.Warn("drive by: effect.drive_by not found, melee discount not installed");
                return;
            }
            var wrapped = 0;
            for (var i = 0; i < handlers.Count; i++)
            {
                var cost = handlers[i]?.TryCast<ChangeActionPointCost>();
                if (cost != null && Wrap(cost.SkillFilter, f => cost.SkillFilter = f))
                    wrapped++;
                var consume = handlers[i]?.TryCast<ConsumeOnSkillUse>();
                if (consume != null && Wrap(consume.SkillFilter, f => consume.SkillFilter = f))
                    wrapped++;
            }
            if (wrapped > 0)
                Context.Log.Debug($"drive by: wrapped {wrapped} filter(s) to accept melee skills");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"drive by: melee filter install failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // OnTemplatesApplied can run more than once per session: a filter that is already ours is
    // left alone so the chain never nests.
    private static bool Wrap(ISkillFilter existing, Action<ISkillFilter> assign)
    {
        if (existing != null && existing.TryCast<DriveByMeleeFilter>() != null)
            return false;
        assign(new DriveByMeleeFilter { Inner = existing }.Cast<ISkillFilter>());
        return true;
    }
}

// The widened filter: the vanilla filter's verdict, OR the skill carries wmgfl_melee. The game
// calls Matches per skill per cost evaluation, so the tag walk is memoised per skill template
// the same way MeleeCookOffSystem's melee test is.
[JiangyuType("DriveByMeleeFilter", Interfaces = new[] { typeof(ISkillFilter) })]
public sealed partial class DriveByMeleeFilter : Il2CppSystem.Object
{
    private const string MeleeTag = "wmgfl_melee";
    private static readonly Dictionary<IntPtr, bool> _isMelee = new();

    public ISkillFilter Inner;

    public bool Matches(Skill _skill)
    {
        try
        {
            if (Inner != null && Inner.Matches(_skill))
                return true;
            return IsMelee(_skill?.GetTemplate());
        }
        catch
        {
            return false;
        }
    }

    private static bool IsMelee(SkillTemplate template)
    {
        if (template == null)
            return false;
        if (_isMelee.TryGetValue(template.Pointer, out var cached))
            return cached;
        var melee = false;
        var tags = template.Tags;
        for (var i = 0; !melee && tags != null && i < tags.Count; i++)
            melee = tags[i]?.name == MeleeTag;
        _isMelee[template.Pointer] = melee;
        return melee;
    }
}
