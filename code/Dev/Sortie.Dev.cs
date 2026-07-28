using Il2CppMenace.Tactical;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for getting into a tactical mission over the bridge, which otherwise
// needs a human clicking through the campaign map. MENACE only ever saves at the
// strategy layer, so no save can drop you straight into combat.
//
// Both screens expose a public entry point, so this drives the game's own path
// rather than synthesising UI clicks: MissionPrepUIScreen.TryOpen skips the map
// POI and Plan Mission steps, and LaunchMission is what the launch button calls.
//
//   Sortie.Where   -> where we are (scene, active screen, prep readiness, in-mission)
//   Sortie.Open    -> open mission prep for the current operation's mission
//   Sortie.Launch  -> launch the prepped mission, once its preview has landed
//
// Named Sortie, not Mission: the verb runner matches class short names
// case-insensitively and the SDK's own Mission class would shadow it.
[DevVerb]
public static class Sortie
{
    public static object Where()
    {
        var screen = ActiveScreen();
        var prep = Prep(screen);
        return new
        {
            inMission = TacticalManager.IsMissionRunning(),
            activeScreen = screen != null ? screen.GetIl2CppType().Name : "none",
            missionPrepOpen = prep != null,
            // The prep screen generates its preview asynchronously and only auto-deploys
            // the squad once it lands. Launching before then starts an empty mission with
            // no map and no actors, so wait for this before calling Launch.
            previewReady = prep != null && prep.GetMissionPreview() != null,
            loadoutValid = prep != null && prep.m_IsLoadoutValid,
        };
    }

    // Open the prep screen for the operation's current mission.
    [MutatingVerb]
    public static object Open()
    {
        var operation = Operations.Current();
        if (operation == null)
            return new { error = "no current operation" };
        var mission = Operations.CurrentMission(operation);
        if (mission == null)
            return new { error = "operation has no current mission" };

        var opened = MissionPrepUIScreen.TryOpen(mission);
        return new
        {
            opened,
            operation = Operations.Name(operation),
            maxSupplies = mission.GetMaxSupplies().GetAmount(),
            next = "call Sortie.Launch once the prep screen has settled",
        };
    }

    // Launch whatever the prep screen currently has deployed. The two resource
    // arguments are what the launch button passes: the mission's supply ceiling,
    // and the cost of the loadout as the screen itself totals it.
    [MutatingVerb]
    public static object Launch()
    {
        var screen = Prep(ActiveScreen());
        if (screen == null)
            return new { error = "mission prep screen is not the active screen; call Sortie.Open first" };

        var mission = screen.GetMission();
        if (mission == null)
            return new { error = "prep screen has no mission" };
        // Launching before the preview lands starts a mission with no map and no
        // actors, which no verb can recover from. The guard belongs here, on the
        // mutating call, not only on the diagnostic that reports the flags.
        if (screen.GetMissionPreview() == null || !screen.m_IsLoadoutValid)
            return new
            {
                error = "prep screen is not ready; poll Sortie.Where until previewReady and loadoutValid are both true",
                previewReady = screen.GetMissionPreview() != null,
                loadoutValid = screen.m_IsLoadoutValid,
            };

        var maxSupplies = mission.GetMaxSupplies();
        var deployCosts = screen.UpdateSupplies(maxSupplies);
        // The mission's own ceiling reads 0 here, and launching a loadout that costs more
        // than the ceiling deploys nothing: the scene loads with no map and no actors.
        // Raise the ceiling to what the loadout actually costs so the launch is accepted.
        if (maxSupplies.GetAmount() < deployCosts.GetAmount())
            maxSupplies = new Il2CppMenace.Strategy.OperationResources(deployCosts.GetAmount());
        screen.LaunchMission(deployCosts, maxSupplies);
        return new
        {
            ok = true,
            deployCosts = deployCosts.GetAmount(),
            maxSupplies = maxSupplies.GetAmount(),
            next = "poll Sortie.Where until inMission is true",
        };
    }

    private static UIScreen ActiveScreen()
    {
        try { return UIManager.Get()?.GetActiveScreen(); }
        catch { return null; }
    }

    private static MissionPrepUIScreen Prep(UIScreen screen)
    {
        try { return screen?.TryCast<MissionPrepUIScreen>(); }
        catch { return null; }
    }
}
