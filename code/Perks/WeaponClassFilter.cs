using Il2CppMenace.Items;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Il2CppMenace.Tactical.Skills.SkillFilters;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Points vanilla ChangeActionPointCost handlers at the skills of one weapon class. Their SkillFilter
// is an Odin-routed ISkillFilter that KDL cannot author, so a perk appends the handler with only
// its CostDelta and this system installs the filter once templates are applied. Each entry names
// the perk, the handler's index and the same WeaponClass, Invert and Slot that WeaponClassBonus
// takes. Welrod's Case Controller makes squad weapons other than pistols cost more to use.
public sealed class WeaponClassFilterSystem : JiangyuSystem
{
    private static readonly (string Perk, int Handler, string WeaponClass, bool Invert, ItemSlot Slot)[] Filters =
    {
        ("perk.welrod_case_controller", 2, "HG", true, ItemSlot.InfantryWeapon),
    };

    public override void OnTemplatesApplied()
    {
        foreach (var entry in Filters)
        {
            try
            {
                var handlers = Templates.ById<SkillTemplate>(entry.Perk)?.EventHandlers;
                var cost = handlers != null && entry.Handler < handlers.Count
                    ? handlers[entry.Handler]?.TryCast<ChangeActionPointCost>()
                    : null;
                if (cost == null)
                {
                    Context.Log.Warn($"weapon class filter: {entry.Perk} has no ChangeActionPointCost at {entry.Handler}");
                    continue;
                }
                cost.SkillFilter = new WeaponClassFilter
                {
                    WeaponClass = entry.WeaponClass,
                    Invert = entry.Invert,
                    Slot = entry.Slot,
                }.Cast<ISkillFilter>();
            }
            catch (Exception ex)
            {
                Context.Log.Warn($"weapon class filter: install on {entry.Perk} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}

[JiangyuType("WeaponClassFilter", Interfaces = new[] { typeof(ISkillFilter) })]
public sealed partial class WeaponClassFilter : Il2CppSystem.Object
{
    public string WeaponClass = "";
    public bool Invert;
    public ItemSlot Slot = ItemSlot.None;

    public bool Matches(Skill _skill)
    {
        try
        {
            return WeaponClassMatch.Matches(_skill, WeaponClass, Invert, Slot);
        }
        catch
        {
            return false;
        }
    }
}
