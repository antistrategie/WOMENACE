using Il2CppMenace.States;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Tactical;
using Jiangyu.Game.Tactical;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Dev verbs for clearing the mission UI out of the shot, so a tactical scene can be looked at
// or captured with nothing over it. Invoked over the dev-loader bridge as
// {verb: "Hud.Hide", mutate: true} / {verb: "Hud.Show", mutate: true}, or
// scripts/bridge.py hud off|on|toggle.
//
// Five mechanisms compose, because no single one covers the screen:
//
//   UITactical.SetUIVisibility(bool)  MENACE's own static toggle, the one the operation
//                                     screenshot recorder drives. Scoped to the tactical screen.
//   the tactical screen root          A superset belt over the above, so anything a mod injected
//                                     onto that screen goes with it.
//   UITacticalHUD.SetVisibility       The world-space layer: unit health bars, name plates,
//                                     objective and movement HUDs, floating icons.
//   the permanent layers root         Notifications, dialogs and the cheats layer, which live on
//                                     UIManager rather than on any screen.
//   BaseHUD.s_RenderWorldSpaceMarkers The world-space marker draw, as a static gate.
//
// Tooltips get closed and then gated off at TacticalState.s_TooltipsEnabled, so a stray hover
// while the UI is down cannot pop one back into frame.
//
// Only these known roots are touched, never every live UIDocument: a screen kept open in the
// background is legitimately collapsed, and a blind sweep would reveal it on the way back.
[DevVerb]
public static class Hud
{
    private static bool _hidden;

    // Take the mission UI down. `markers` false leaves the world-space marker draw alone,
    // for a shot that wants the objective and unit markers but none of the panels.
    [MutatingVerb]
    public static object Hide(bool markers = true) => Apply(visible: false, markers);

    // Put it all back. Restores the marker and tooltip gates regardless of how Hide was called,
    // since rendered-and-enabled is their normal state.
    [MutatingVerb]
    public static object Show() => Apply(visible: true, markers: true);

    // Flip whichever way this verb last left things. Convenient as one repeated bridge call while
    // framing a shot. Tracked in-process, so it assumes nothing else moved the UI in between.
    [MutatingVerb]
    public static object Toggle() => Apply(visible: _hidden, markers: true);

    // Whether the UI is down, and what the two static gates currently read. Read-only.
    public static object State() => new
    {
        inMission = InMission(),
        hidden = _hidden,
        worldSpaceMarkers = Read(() => BaseHUD.s_RenderWorldSpaceMarkers),
        tooltipsEnabled = Read(() => TacticalState.s_TooltipsEnabled),
    };

    private static object Apply(bool visible, bool markers)
    {
        if (!InMission())
            return new { error = "not in a tactical mission" };

        var ui = Screen();
        if (ui == null)
            return new { error = "no tactical UI screen (the mission is still loading)" };

        // Close first when hiding, restore the gate last when showing: an open tooltip is a
        // separate overlay panel that neither root below reaches.
        var tooltips = !visible && Try(() => UIManager.Get()?.RemoveAllTooltips());

        var toggled = Try(() => UITactical.SetUIVisibility(visible));
        var screenRoot = Display(Element(() => ui.GetRootElement()), visible);
        var hudLayer = Try(() => ui.GetHUD()?.SetVisibility(visible));
        var permanentRoot = Display(Element(() => UIManager.Get()?.GetPermanentLayersRoot()), visible);
        var markerGate = (markers || visible) && Try(() => BaseHUD.s_RenderWorldSpaceMarkers = visible);
        var tooltipGate = Try(() => TacticalState.s_TooltipsEnabled = visible);

        _hidden = !visible;
        return new
        {
            ok = true,
            hidden = _hidden,
            // Which mechanism actually ran. A false here is the thing to read when the UI is
            // still on screen: it names the one member this game build did not accept.
            applied = new
            {
                tacticalToggle = toggled,
                screenRoot,
                hudLayer,
                permanentRoot,
                worldSpaceMarkers = markerGate,
                tooltipGate,
                tooltipsClosed = tooltips,
            },
        };
    }

    // Outside a mission TacticalState is gone and every member below faults, so the verbs gate
    // on this rather than letting the game throw.
    private static bool InMission()
    {
        try { return Mission.InMission && TacticalState.Get() != null; }
        catch { return false; }
    }

    private static UITactical Screen()
    {
        try { return TacticalState.Get()?.GetUI(); }
        catch { return null; }
    }

    private static VisualElement Element(Func<VisualElement> fetch)
    {
        try { return fetch(); }
        catch { return null; }
    }

    private static bool Display(VisualElement element, bool visible)
    {
        if (element == null)
            return false;
        return Try(() => element.style.display =
            new StyleEnum<DisplayStyle>(visible ? DisplayStyle.Flex : DisplayStyle.None));
    }

    // Each step is independent: a member this game build renamed should cost that one step, not
    // the whole verb, and the report says which one it was.
    private static bool Try(Action step)
    {
        try { step(); return true; }
        catch { return false; }
    }

    private static object Read(Func<bool> gate)
    {
        try { return gate(); }
        catch { return "unreadable"; }
    }
}
