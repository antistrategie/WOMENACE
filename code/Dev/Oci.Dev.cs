using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for the O.C.I. economy, so ship upgrade work (the Fairy Lodge tree) can be tested
// without grinding missions. Invoked over the dev-loader bridge as {verb: "Oci.Grant", mutate:
// true}. The bridge and verb runner live only in the dev loader, so these are unreachable in a
// shipped mod, and the *.Dev.cs name keeps them out of release builds.
[DevVerb]
public static class Oci
{
    // Add O.C.I. components through the game's own currency path, the same ChangeVar the campaign
    // rewards use, so the resource display and any listeners update normally. A negative amount
    // takes components away.
    [MutatingVerb]
    public static object Grant(int amount = 500)
    {
        var state = StrategyState.Get();
        if (state == null)
            return new { error = "no strategy state (load a campaign save first)" };

        state.ChangeVar(StrategyVars.OciComponents, amount, true, true, false);
        return new { ok = true, granted = amount, balance = state.GetVar(StrategyVars.OciComponents) };
    }
}
