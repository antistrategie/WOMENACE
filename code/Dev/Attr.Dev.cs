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
        {
            rows.Add(
                $"in={v:0} ap={UnitLeaderAttributes.GetActionPointsAsFloat(v):0.##}/{UnitLeaderAttributes.GetActionPoints(v)}"
                + $" dmg={UnitLeaderAttributes.GetDamageSustainedMultAsFloat(v):0.###}/{UnitLeaderAttributes.GetDamageSustainedMult(v)}"
                + $" dmgDec={UnitLeaderAttributes.GetDamageSustainedMultDecimals(v):0.###}"
                + $" acc={UnitLeaderAttributes.GetAccuracyAsFloat(v):0.##}"
                + $" crit={UnitLeaderAttributes.GetCriticalChanceAsFloat(v):0.##}"
                + $" def={UnitLeaderAttributes.GetDefenseMultAsFloat(v):0.##}"
                + $" hp={UnitLeaderAttributes.GetHitpointsPerElement(v)}");
        }
        return rows;
    }
}
