using Il2CppMenace.UI;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// A mission launch from the shell's bottom bar, through the bars.
public sealed partial class StrategyDiorama
{
    // Launching a mission: the bars shut to black, then vanilla's launch runs.
    //
    // Vanilla's launch does its work in one frame, so the bars must shut before it is called, and
    // the call itself cannot be patched (StartMission takes structs by value). Start Mission may
    // first ask (too few supplies used) and be declined. So: where no question is expected the bars
    // shut and then the vanilla button is pressed. Where one is expected, or one comes anyway, the
    // question shows over the open screen and its Yes is wrapped to shut the bars before it acts.
    private System.Collections.IEnumerator LaunchMission(VisualElement shell)
    {
        var button = _shellRoot != null ? UI.Find(_shellRoot, UiSelector.Name(_actionPrimary))?.TryCast<InteractiveElement>() : null;
        if (button == null || !button.IsInteractable() || _cutting)
            yield break;
        _cutting = true;
        Sound.Click();
        var barsFirst = !ExpectSuppliesQuestion();
        if (barsFirst)
        {
            TableSounds.Play(TableSounds.Launch);
            ShutBars(shell, BarsLaunchClass);
            for (var t = 0f; t < BarsLaunchSeconds; t += UnityEngine.Time.unscaledDeltaTime)
                yield return null;
        }
        button.OnClicked(MouseButton.LeftMouse);
        yield return null;
        yield return null;
        _cutting = false;
        var yes = FindDialogButton(VanillaUi.Elements.YesButton);
        if (shell.panel == null || _shell != shell)
            yield break;
        if (yes == null)
        {
            // Vanilla declined without a question (a notice, a refusal): no loading screen comes,
            // and the bars shut for the launch open again.
            for (var t = 0f; barsFirst && t < LaunchRefusedSeconds; t += UnityEngine.Time.unscaledDeltaTime)
                yield return null;
            if (barsFirst && shell.panel != null && _shell == shell && UIManager.Get()?.IsLoadingOverlayVisible() != true)
                OpenShutBars(shell, BarsLaunchClass);
            yield break;
        }
        if (barsFirst)
            OpenShutBars(shell, BarsLaunchClass);
        var act = yes.GetOnLeftClickedAction();
        if (act == null)
            yield break;
        yes.SetOnLeftClickedAction((Il2CppSystem.Action<InteractiveElement>)(Action<InteractiveElement>)(pressed =>
            Context.Coroutines.Start(AnswerThroughBars(shell, pressed, act))));
    }

    private System.Collections.IEnumerator AnswerThroughBars(VisualElement shell, InteractiveElement pressed, Il2CppSystem.Action<InteractiveElement> act)
    {
        // One answer only: a second press while the bars shut would launch twice, or cancel a
        // launch that then runs anyway.
        if (_answering)
            yield break;
        _answering = true;
        // The question steps aside while the bars shut, unseen and unanswerable, then its own Yes runs.
        var dialog = UIManager.Get()?.GetCurrentDialog();
        if (dialog != null)
        {
            dialog.style.opacity = 0f;
            dialog.SetEnabled(false);
        }
        if (shell != null && shell.panel != null)
        {
            ShutBars(shell, BarsLaunchClass);
            for (var t = 0f; t < BarsLaunchSeconds; t += UnityEngine.Time.unscaledDeltaTime)
                yield return null;
        }
        try
        {
            act.Invoke(pressed);
        }
        finally
        {
            _answering = false;
            // The dialog's element may serve the next question.
            if (dialog != null)
            {
                dialog.style.opacity = StyleKeyword.Null;
                dialog.SetEnabled(true);
            }
        }
    }

    private bool _answering;

    private const float LaunchRefusedSeconds = 0.75f;

    private static InteractiveElement FindDialogButton(string name)
    {
        var layers = UIManager.Get()?.GetPermanentLayersRoot();
        var element = layers != null ? UI.Find(layers, UiSelector.Name(name)) : null;
        // Shown only if nothing above it is hidden either: vanilla keeps a dialog's elements and
        // hides the dialog, which leaves an old button's own display untouched.
        for (var e = element; e != null && e != layers; e = e.parent)
            if (e.resolvedStyle.display == DisplayStyle.None)
                return null;
        return element?.TryCast<InteractiveElement>();
    }

    // Whether Start Mission will ask about unused supplies: vanilla asks under 70% used. Read off
    // the loadout bar's own "used / max" label. Unsure means yes, which only costs the bars.
    private bool ExpectSuppliesQuestion()
    {
        var panel = _shellRoot != null ? UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.SuppliesPanel)) : null;
        if (panel == null)
            return true;
        foreach (var element in UI.FindAll(panel, UiSelector.Type<Label>()))
        {
            var match = System.Text.RegularExpressions.Regex.Match(element.TryCast<Label>()?.text ?? "", @"(\d+)\s*/\s*(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var used) && int.TryParse(match.Groups[2].Value, out var max) && max > 0)
                return used < SuppliesQuestionShare * max;
        }
        return true;
    }

    private const float SuppliesQuestionShare = 0.7f;
}
