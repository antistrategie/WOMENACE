using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for driving the strategy UI over the dev-loader bridge, e.g.
// {verb: "Nav.OpenArmory", mutate: true}. Dev-loader only; *.Dev.cs keeps them out of releases.
[DevVerb]
public static class Nav
{
    // Open the armoury screen, as the ship navigation would.
    [MutatingVerb]
    public static object OpenArmory()
    {
        var manager = UIManager.Get();
        if (manager == null)
            return new { error = "no ui manager" };
        var screen = manager.OpenScreen(ArmoryUIScreen.PREFAB_NAME);
        return new { ok = screen != null };
    }

    // Select the hired leader carrying the character tag (e.g. "wmgfl_sextans") on the active
    // screen, as clicking her roster slot would. On the armoury this drives the unit selector,
    // which also respawns the 3D squad preview; elsewhere it binds the UnitWindow directly.
    [MutatingVerb]
    public static object SelectLeader(string characterTag)
    {
        var screen = UIManager.Get()?.GetActiveScreen();
        var leaders = StrategyState.Get()?.Roster?.m_HiredLeaders;
        for (var i = 0; leaders != null && i < leaders.Count; i++)
        {
            var leader = leaders[i];
            if (leader == null || Affinity.CharacterTag(leader) != characterTag)
                continue;

            var selector = screen?.TryCast<ArmoryUIScreen>()?.m_UnitSelector;
            if (selector != null)
            {
                selector.SetSelectedUnit(leader);
                return new { ok = true, via = "selector", leader = leader.GetTemplate()?.GetID() };
            }

            var root = screen?.GetRootElement();
            var window = root != null ? UI.Find(root, UiSelector.TypeName("UnitWindow"))?.TryCast<UnitWindow>() : null;
            if (window == null)
                return new { error = "no unit selector or unit window on the active screen" };
            window.SetLeader(leader);
            return new { ok = true, via = "window", leader = leader.GetTemplate()?.GetID() };
        }
        return new { error = $"no hired leader tagged '{characterTag}'" };
    }

    // Apply a transmog selection through the picker's own Select path (what clicking an outfit
    // tile in the strip runs), against the active screen's UnitWindow.
    [MutatingVerb]
    public static object SetTransmog(string characterTag, string armorId)
    {
        var picker = TransmogPickerSystem.Instance;
        if (picker == null)
            return new { error = "transmog picker system not initialised" };
        return picker.DevSelect(characterTag, armorId)
            ? new { ok = true }
            : new { error = "no unit window on the active screen" };
    }
}
