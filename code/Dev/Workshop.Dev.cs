using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMenace.States;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for jumping the ship's workshop unlock ahead of the campaign, so weapon crafting and
// upgrade work can be tested without deep story progress. Invoked over the dev-loader bridge as
// {verb: "Workshop.Unlock", mutate: true}. The bridge and verb runner live only in the dev loader,
// so these are unreachable in a shipped mod, and the *.Dev.cs name keeps them out of release builds.
[DevVerb]
public static class Workshop
{
    // Flip the story conversation variables that gate the workshop, exactly as the campaign does when
    // it unlocks the module. StrategyState fires OnConversationVarChanged on set, and the ship's
    // StrategyNavigation listens for it, so the workshop nav button appears (or vanishes) without a
    // screen reload. `on` drives the workshop gate; `vouchers` mirrors the same value onto the
    // blueprint-voucher gate the crafting UI needs, so a plain Unlock opens both.
    [MutatingVerb]
    public static object Unlock(bool on = true, bool vouchers = true)
    {
        var state = StrategyState.Get();
        if (state == null)
            return new { error = "no strategy state (load a campaign save first)" };

        var value = on ? 1 : 0;
        state.SetConversationVarValue(StrategyState.CONV_VAR_WORKSHOP_UNLOCKED, value);
        if (vouchers)
            state.SetConversationVarValue(StrategyState.CONV_VAR_BLUEPRINT_VOUCHERS_UNLOCKED, value);

        return new
        {
            ok = true,
            workshop = Read(state, StrategyState.CONV_VAR_WORKSHOP_UNLOCKED),
            blueprintVouchers = Read(state, StrategyState.CONV_VAR_BLUEPRINT_VOUCHERS_UNLOCKED),
        };
    }

    // Read a conversation var back through the game's own accessor to confirm the write landed. The
    // getter takes a ReadOnlySpan<char>, which only converts from an Il2Cpp array, so pack the name.
    private static int Read(StrategyState state, string name)
    {
        var chars = new Il2CppStructArray<char>(name.Length);
        for (var i = 0; i < name.Length; i++)
            chars[i] = name[i];
        return state.GetConversationVarValue(chars);
    }
}
