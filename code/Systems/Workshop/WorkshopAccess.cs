using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMenace.States;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

public sealed class WorkshopAccess : JiangyuSystem
{
    public const string RogueArmyUnlocked = "rogue_army_ops_unlocked";

    public static int Read(string name)
    {
        var state = StrategyState.Get();
        if (state == null)
            return 0;
        var chars = new Il2CppStructArray<char>(name.ToCharArray());
        return state.GetConversationVarValue(chars);
    }

    public static bool IsUnlocked => Read(StrategyState.CONV_VAR_WORKSHOP_UNLOCKED) > 0;

    public override void OnInit()
    {
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.StrategyNavigation", "Init", _ => Reconcile());
        // The introduction sets this flag after adding the first Rogue Army operation. Its event
        // can occur after four operations, with a random check, so an operation count is not enough.
        Context.Patches.Postfix("Il2CppMenace.States.StrategyState", "SetConversationVarValue", info =>
        {
            if (info.Args[0]?.ToString() == RogueArmyUnlocked)
                Reconcile();
        });
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName) => Reconcile();

    internal static void Reconcile()
    {
        if (Read(RogueArmyUnlocked) <= 0 || IsUnlocked)
            return;
        var state = StrategyState.Get();
        state.SetConversationVarValue(StrategyState.CONV_VAR_WORKSHOP_UNLOCKED, 1);
        state.SetConversationVarValue(StrategyState.CONV_VAR_BLUEPRINT_VOUCHERS_UNLOCKED, 1);
    }
}
