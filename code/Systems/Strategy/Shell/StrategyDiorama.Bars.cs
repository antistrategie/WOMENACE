using Il2CppMenace.UI;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The shell's black in motion: two shutters behind the bars (shell.uss), out of the frame at rest.
// They shut for a cut to another room and open on arrival, shut for a mission launch, and come
// part way in for a story event, moved by a translate. The bars and their contents stay where
// they are.
public sealed partial class StrategyDiorama
{
    private static string RoomOf(VanillaNavigation.Target target) => target switch
    {
        VanillaNavigation.Target.Armory or VanillaNavigation.Target.Hiring => RefittingRoomKey,
        VanillaNavigation.Target.Workshop => WeaponRoomKey,
        _ => OpsRoomKey,
    };

    // Goes to a destination through the vanilla tab bar. Into another room the bars shut first, the
    // cut happens behind them, and the next screen's shell opens them.
    private void GoTo(VanillaNavigation.Target target)
    {
        if (_scene.Cutting)
            return;
        var from = _policyScreen != null ? LookFor(_policyScreen).RoomKey : null;
        if (_shell == null || from == RoomOf(target) || !VanillaNavigation.Available(target))
        {
            VanillaNavigation.Go(target);
            return;
        }
        Context.Coroutines.Start(CutTo(target, _shell));
    }

    private System.Collections.IEnumerator CutTo(VanillaNavigation.Target target, VisualElement shell)
    {
        _scene.Cutting = true;
        Sound.Click();
        ShutBars(shell, BarsCutClass);
        for (var t = 0f; t < BarsCutSeconds; t += UnityEngine.Time.unscaledDeltaTime)
            yield return null;
        // The screens change in one long frame, with the whole game stopped for it. The bars
        // are shut before it by what is drawn, not by the clock: their move starts when the
        // panel next styles them, which is later than the frame it was asked for in.
        var shutter = UI.Find(shell, UiSelector.Name("wm-shutter-top"));
        for (var t = 0f; shutter != null && shutter.resolvedStyle.translate.y < -0.5f && t < BarsShutMaxSeconds; t += UnityEngine.Time.unscaledDeltaTime)
            yield return null;
        // Black over everything while the screens change: the next screen draws a frame or two
        // before its shell is on it. It is up for two frames first, so it is what the screen
        // shows through the long one, whatever was left of the bars' move.
        CoverCut();
        yield return null;
        yield return null;
        _scene.Cutting = false;
        _scene.ArrivedThroughBars = true;
        VanillaNavigation.Go(target);
        // A destination that did not open leaves the bars shut with nothing to open them.
        for (var t = 0f; t < CoverMaxSeconds; t += UnityEngine.Time.unscaledDeltaTime)
            yield return null;
        if (_scene.ArrivedThroughBars && _shell == shell && shell.panel != null)
        {
            _scene.ArrivedThroughBars = false;
            Context.Coroutines.Start(OpenBars(shell));
        }
    }

    private VisualElement _cutCover;

    private void CoverCut()
    {
        UncoverCut();
        // The permanent layers keep to 16:9 in the middle of a wider screen, as every screen's
        // root does, so the cover goes on the panel's own root, which is the whole screen.
        var layers = UIManager.Get()?.GetPermanentLayersRoot();
        var host = layers?.panel?.visualTree ?? layers;
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
    private const float BarsCutSeconds = 0.2f;
    private const float BarsShutMaxSeconds = 1f;

    private void ShutBars(VisualElement shell, string paceClass)
    {
        shell.AddToClassList(paceClass);
        shell.AddToClassList(BarsClosedClass);
        KeepShopUnderShell(shell);
        CoverVanilla(shell.parent, true);
        MoveBars(shell, null);
    }

    private void OpenShutBars(VisualElement shell, string paceClass)
    {
        MoveBars(shell, BarsOpen);
        shell.RemoveFromClassList(BarsClosedClass);
        shell.RemoveFromClassList(paceClass);
        CoverVanilla(shell.parent, false);
    }

    private const string BarsLaunchClass = "wm-bars-launch";
    private const float BarsLaunchSeconds = 0.55f;

    private const string BarsClosedClass = "wm-bars-closed";
    private const string CoveredClass = "wm-covered";

    // Vanilla's own parts that stand over the bars (the resources, mission preparation's loadout
    // bar) go and come with the bars' contents. They are told so themselves: a class on the
    // screen's root has every element of the screen styled again, which on a screen of some
    // hundreds of item slots is a long frame in the middle of the bars' move.
    private static void CoverVanilla(VisualElement root, bool covered)
    {
        if (root == null)
            return;
        UI.Find(root, UiSelector.Class(VanillaUi.Elements.TabBarClass))?.EnableInClassList(CoveredClass, covered);
        UI.Find(root, UiSelector.Name(VanillaUi.Elements.SuppliesPanel))?.EnableInClassList(CoveredClass, covered);
    }
    private const string BarsEventClass = "wm-bars-event";
    // On the screen's root while a Procurement pull plays: the vanilla resources go too, and the
    // pull has the frame to itself.
    private const string BarsBareClass = "wm-bars-bare";
    // How tall shell.uss makes a bar, in panel pixels: what stands on a bar is placed by it.
    private const float BarHeight = 90f;
    // How far the black's edges stand from the screen's edges: open, and while a story event plays.
    private const float BarsOpen = 0f;
    private const float BarEventHeight = 140f;

    // Moves the bars' edges to a distance from the screen's edges, or shut (no distance given).
    // The black is the shell's two shutters, each half the screen, moved by a translate between
    // two pixel lengths so the move eases and nothing lays out again. False while the screen's
    // height is not known yet (shell.uss places the shutters until it is).
    private bool MoveBars(VisualElement shell, float? edge)
    {
        // The shell is as tall as the screen's root and what it reaches past the root above and
        // below (PlaceShell). Its own layout is a frame behind a change in that reach, and a
        // travel worked out from it leaves the black standing in the frame by as much.
        var height = shell.parent != null ? shell.parent.layout.height : shell.layout.height;
        if (shell == _shell && _shellInset is { } inset)
            height += inset.Top + inset.Bottom;
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
        KeepShopUnderShell(shell);
        CoverVanilla(root, true);
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
        // Held shut for a moment, so the room behind has drawn before it shows, and for as long
        // after as the frames come unevenly. The screen that has just opened is still loading
        // for some frames, each of them a long one, and bars that opened over them would open
        // in jerks. A long frame counts for little of the moment, so one stall does not use
        // the whole of it up.
        // The shell reaches past the screen's root to the edges of a screen wider than 16:9 only
        // once the root has a size to measure, which a screen still loading may not have had on
        // the shell's first frame. It is placed again every frame while shut, so the black covers
        // the whole screen and not only the root's part of it.
        var (held, shut, even) = (0f, 0f, 0);
        while (held < BarsHeldSeconds || (even < BarsEvenFrames && shut < BarsHeldMaxSeconds))
        {
            yield return null;
            if (shell.panel == null || _shell != shell)
                yield break;
            PlaceShell(shell);
            var frame = UnityEngine.Time.unscaledDeltaTime;
            held += MathF.Min(frame, BarsEvenFrame);
            shut += frame;
            even = frame <= BarsEvenFrame ? even + 1 : 0;
        }
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
            PlaceShell(shell);
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
        MoveBars(shell, BarsOpen);
        shell.RemoveFromClassList(BarsClosedClass);
        CoverVanilla(root, false);
    }

    private const float BarsHeldSeconds = 0.25f;
    // A frame no longer than this is an even one. The bars open after this many in a row, or
    // once they have been held for as long as anyone would wait.
    private const float BarsEvenFrame = 0.04f;
    private const int BarsEvenFrames = 5;
    private const float BarsHeldMaxSeconds = 2f;
    private const float BarsDollsSeconds = 4f;
    private const string BarsSnapClass = "wm-bars-snap";
}
