using Il2CppMenace.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Holds each added enemy at a fixed share of its army list's picks, whatever other
// mods add to the list. The KDL patch places the entry, and this sets its weight as
// the army is built, by which point every template mod has finished editing the list,
// so load order between mods does not matter. The weight is solved from the other
// entries' weights on every build, so it never compounds.
public sealed class ArmyShareSystem : JiangyuSystem
{
    internal readonly record struct Share(string ArmyId, string TemplateId, float Fraction);

    internal static readonly Share[] Shares =
    {
        new("army.rogue_army_list_general_mid_game", "enemy.wmgfl_titan_support", 0.03f),
        new("army.rogue_army_list_general_mid_game_reinforcements", "enemy.wmgfl_titan_support", 0.045f),
        new("army.pirate_offensive_mid_game", "enemy.wmgfl_oljefat", 0.03f),
        new("army.pirate_defensive_mid_game", "enemy.wmgfl_oljefat", 0.03f),
        new("army.pirate_offensive_mid_game_reinforcements", "enemy.wmgfl_oljefat", 0.045f),
    };

    public override void OnInit() => Context.Patches.Prefix("Il2CppMenace.Strategy.Army", "CreateArmy", OnCreateArmy);

    private void OnCreateArmy(PatchInfo info)
    {
        try
        {
            if ((info.Args is { Count: > 2 } ? info.Args[2] : null) is not ArmyTemplate army)
                return;
            var armyId = army.GetID();
            Context.Log.Debug($"army share: building '{armyId}'");
            foreach (var share in Shares)
                if (share.ArmyId == armyId)
                {
                    var (weight, others) = Apply(army, share);
                    Context.Log.Debug($"army share: '{share.TemplateId}' in '{armyId}' weight {weight} against {others}");
                }
        }
        catch (Exception ex)
        {
            Context.Log.Error($"army share: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static (int Weight, int Others) Apply(ArmyTemplate army, Share share)
    {
        var units = army.PossibleUnits;
        if (units == null)
            return (0, 0);
        var others = 0;
        for (var i = 0; i < units.Count; i++)
            if (units[i]?.Template != null && units[i].Template.GetID() != share.TemplateId)
                others += units[i].Weight;
        var weight = Math.Max(1, (int)MathF.Round(share.Fraction * others / (1f - share.Fraction)));
        // A second copy of the entry, from another mod appending the same unit, would
        // double the share, so only the first carries the weight.
        var matched = false;
        for (var i = 0; i < units.Count; i++)
            if (units[i]?.Template?.GetID() == share.TemplateId)
            {
                units[i].Weight = matched ? 0 : weight;
                matched = true;
            }
        return matched ? (weight, others) : (0, others);
    }
}
