using Il2CppMenace.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for the attribute system, invoked over the dev-loader bridge.
// Attr.Curves samples every attribute-to-stat conversion so the real slopes,
// intercepts and caps can be read off a running game (the native constants
// live in runtime-initialised float pools, unreadable statically). Inputs at
// or under 100 return pure vanilla values; higher inputs show the solo-doll
// extensions in action.
[DevVerb]
public static class Attr
{
    public static object Curves()
    {
        // One readable string per sampled input: the verb runner stringifies
        // nested shapes, so the rows carry their own formatting.
        var inputs = new[] { 0f, 25f, 50f, 75f, 100f, 150f, 200f, 300f, 500f };
        var rows = new List<object>();
        foreach (var v in inputs)
            rows.Add(At(v));
        return rows;
    }

    // Exact inputs expose rounding boundaries that the broad curve samples miss.
    public static string At(float value) =>
        $"in={value:0.##} ap={UnitLeaderAttributes.GetActionPointsAsFloat(value):0.##}/{UnitLeaderAttributes.GetActionPoints(value)}"
        + $" dmg={UnitLeaderAttributes.GetDamageSustainedMultAsFloat(value):0.###}/{UnitLeaderAttributes.GetDamageSustainedMult(value)}"
        + $" dmgDec={UnitLeaderAttributes.GetDamageSustainedMultDecimals(value):0.###}"
        + $" acc={UnitLeaderAttributes.GetAccuracyAsFloat(value):0.##}"
        + $" crit={UnitLeaderAttributes.GetCriticalChanceAsFloat(value):0.##}"
        + $" def={UnitLeaderAttributes.GetDefenseMultAsFloat(value):0.##}"
        + $" hp={UnitLeaderAttributes.GetHitpointsPerElement(value)}";
}
