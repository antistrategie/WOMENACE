using Il2CppMenace.UI;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The shell's bars in motion: shut for a cut to another room and opened on arrival, shut for a
// mission launch, and brought in for a story event. The black is two shutters behind the bars
// (shell.uss), moved by a translate. The bars and their contents stay where they are.
public sealed partial class StrategyDiorama
{
    private static string RoomOf(NavigationMenu.Target target) => target switch
    {
        NavigationMenu.Target.Armory or NavigationMenu.Target.Hiring => RefittingRoomKey,
        NavigationMenu.Target.Workshop => WeaponRoomKey,
        _ => OpsRoomKey,
    };

    private bool _cutting;

    // Goes to a destination through the vanilla tab bar. Into another room the bars shut first, the
    // cut happens behind them, and the next screen's shell opens them.
    private void GoTo(NavigationMenu.Target target)
    {
        if (_cutting)
            return;
        var from = _policyScreen != null ? LookFor(_policyScreen).RoomKey : null;
        if (_shell == null || from == RoomOf(target) || !NavigationMenu.Available(target))
        {
            NavigationMenu.Go(target);
            return;
        }
        Context.Coroutines.Start(CutTo(target, _shell));
    }

    private System.Collections.IEnumerator CutTo(NavigationMenu.Target target, VisualElement shell)
    {
        _cutting = true;
        Sound.Click();
        ShutBars(shell, BarsCutClass);
        for (var t = 0f; t < BarsCutSeconds; t += UnityEngine.Time.unscaledDeltaTime)
            yield return null;
        _cutting = false;
        _arrivedThroughBars = true;
        // Black over everything while the screens change: the next screen draws a frame or two
        // before its shell is on it.
        CoverCut();
        NavigationMenu.Go(target);
        // A destination that did not open leaves the bars shut with nothing to open them.
        for (var t = 0f; t < CoverMaxSeconds; t += UnityEngine.Time.unscaledDeltaTime)
            yield return null;
        if (_arrivedThroughBars && _shell == shell && shell.panel != null)
        {
            _arrivedThroughBars = false;
            Context.Coroutines.Start(OpenBars(shell));
        }
    }

    private VisualElement _cutCover;

    private void CoverCut()
    {
        UncoverCut();
        var host = UIManager.Get()?.GetPermanentLayersRoot();
        if (host == null)
            return;
        _cutCover = new VisualElement { pickingMode = PickingMode.Ignore };
        _cutCover.style.position = Position.Absolute;
        _cutCover.style.left = 0;
        _cutCover.style.top = 0;
        _cutCover.style.right = 0;
        _cutCover.style.bottom = 0;
        _cutCover.style.backgroundColor = UnityEngine.Color.black;
        host.Add(_cutCover);
        Context.Coroutines.Start(UncoverCutAfter(_cutCover));
    }

    private void UncoverCut()
    {
        try { _cutCover?.RemoveFromHierarchy(); } catch { }
        _cutCover = null;
    }

    // The cover never outlives a cut that did not arrive (a destination that refused to open).
    private System.Collections.IEnumerator UncoverCutAfter(VisualElement cover)
    {
        for (var t = 0f; t < CoverMaxSeconds; t += UnityEngine.Time.unscaledDeltaTime)
            yield return null;
        if (_cutCover == cover)
            UncoverCut();
    }

    private const float CoverMaxSeconds = 2.5f;

    private const string BarsCutClass = "wm-bars-cut";
    private const float BarsCutSeconds = 0.26f;
    // The room the last shell was in, and whether the bars were shut to leave it.
    private string _shellRoom;
    private bool _arrivedThroughBars;

    private void ShutBars(VisualElement shell, string paceClass)
    {
        shell.AddToClassList(paceClass);
        shell.AddToClassList(BarsClosedClass);
        shell.parent?.AddToClassList(BarsClosedClass);
        MoveBars(shell, null);
    }

    private void OpenShutBars(VisualElement shell, string paceClass)
    {
        MoveBars(shell, BarHeight);
        shell.RemoveFromClassList(BarsClosedClass);
        shell.RemoveFromClassList(paceClass);
        shell.parent?.RemoveFromClassList(BarsClosedClass);
    }

    private const string BarsLaunchClass = "wm-bars-launch";
    private const float BarsLaunchSeconds = 0.55f;

    private const string BarsClosedClass = "wm-bars-closed";
    private const string BarsEventClass = "wm-bars-event";
    // How far the bars' edges stand from the screen's edges, in panel pixels: at rest (the height
    // shell.uss gives a bar), and while a story event plays.
    private const float BarHeight = 90f;
    private const float BarEventHeight = 140f;

    // Moves the bars' edges to a distance from the screen's edges, or shut (no distance given).
    // The black is the shell's two shutters, each half the screen, moved by a translate between
    // two pixel lengths so the move eases and nothing lays out again. False while the screen's
    // height is not known yet (shell.uss places the shutters until it is).
    private static bool MoveBars(VisualElement shell, float? edge)
    {
        var height = shell.layout.height;
        if (!(height > 1f) && shell.parent != null)
            height = shell.parent.layout.height;
        if (!(height > 1f))
            return false;
        var travel = height / 2f - (edge ?? height / 2f);
        var top = UI.Find(shell, UiSelector.Name("wm-shutter-top"));
        var bottom = UI.Find(shell, UiSelector.Name("wm-shutter-bottom"));
        if (top != null)
            top.style.translate = new Translate(new Length(0f, LengthUnit.Pixel), new Length(-travel, LengthUnit.Pixel));
        if (bottom != null)
            bottom.style.translate = new Translate(new Length(0f, LengthUnit.Pixel), new Length(travel, LengthUnit.Pixel));
        return true;
    }

    // Arriving in a room, the bars start shut over it and open.
    private System.Collections.IEnumerator OpenBars(VisualElement shell)
    {
        // Shut from the first frame, with no transition into it, and opened at the bars' own pace.
        shell.RemoveFromClassList(BarsCutClass);
        shell.RemoveFromClassList(BarsLaunchClass);
        shell.AddToClassList(BarsClosedClass);
        shell.AddToClassList(BarsSnapClass);
        // The vanilla resources sit over the top bar and wait for it too.
        var root = shell.parent;
        root?.AddToClassList(BarsClosedClass);
        var placed = MoveBars(shell, null);
        yield return null;
        // A shell that has been replaced is left alone: its bars are no longer the screen's.
        if (shell.panel == null || _shell != shell)
            yield break;
        if (!placed)
            MoveBars(shell, null);
        yield return null;
        // A shell that has been replaced is left alone: its bars are no longer the screen's.
        if (shell.panel == null || _shell != shell)
            yield break;
        shell.RemoveFromClassList(BarsSnapClass);
        // The shut bars are drawn now, so a cut's cover can go.
        UncoverCut();
        // Held shut for a moment, so the room behind has drawn before it shows.
        for (var t = 0f; t < BarsHeldSeconds; t += UnityEngine.Time.unscaledDeltaTime)
            yield return null;
        // A new campaign opens on the choice of its first leaders, with the bars shut behind the
        // dialog for as long as it is open, and then for the moment its dolls take to stand in
        // the room: the room shows with them in it.
        var (waited, awaited) = (false, 0f);
        while (true)
        {
            var picking = PickingLeaders();
            if (!picking && (awaited >= BarsDollsSeconds || !RoomAwaitsDolls()))
                break;
            waited = true;
            if (shell.panel == null || _shell != shell)
                yield break;
            if (!picking)
            {
                awaited += UnityEngine.Time.unscaledDeltaTime;
                RefreshRoomDolls();
            }
            yield return null;
        }
        // The dolls take a few frames to settle into their poses and their light.
        for (var i = 0; waited && i < 6; i++)
            yield return null;
        // A shell that has been replaced is left alone: its bars are no longer the screen's.
        if (shell.panel == null || _shell != shell)
            yield break;
        MoveBars(shell, BarHeight);
        shell.RemoveFromClassList(BarsClosedClass);
        root?.RemoveFromClassList(BarsClosedClass);
    }

    private const float BarsHeldSeconds = 0.25f;
    private const float BarsDollsSeconds = 4f;
    private const string BarsSnapClass = "wm-bars-snap";
}
