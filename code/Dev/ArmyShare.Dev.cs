using Il2CppMenace.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Runs the army-share solve on each configured list without waiting for a mission of
// that faction, and reports every entry's weight afterwards.
//   {"verb":"ArmyShare.Check","args":[],"mutate":true}
[DevVerb]
public static class ArmyShare
{
    [MutatingVerb]
    public static string Check()
    {
        var lines = new List<string>();
        foreach (var share in ArmyShareSystem.Shares)
        {
            var army = Templates.ById<ArmyTemplate>(share.ArmyId);
            if (army == null)
            {
                lines.Add($"{share.ArmyId}: not found");
                continue;
            }
            var (weight, others) = ArmyShareSystem.Apply(army, share);
            var total = weight + others;
            lines.Add($"{share.ArmyId}: {share.TemplateId} weight {weight} of {total} = {(total > 0 ? 100f * weight / total : 0f):0.0}% (target {100f * share.Fraction:0.0}%), {army.PossibleUnits?.Count ?? 0} entries");
        }
        return string.Join("\n", lines);
    }
}
