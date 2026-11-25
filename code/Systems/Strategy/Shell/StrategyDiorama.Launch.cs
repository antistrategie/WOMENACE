using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
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
    // first ask, about a vehicle without a weapon and then about too few supplies used, and either
    // may be declined. Each Yes closes its dialog and goes on to the next question or the launch in
    // the same call. So the questions to come are counted first. With none the bars shut and then
    // the vanilla button is pressed. Otherwise each question shows over the open screen, and only
    // the last one's Yes is wrapped to shut the bars before it acts.
    private System.Collections.IEnumerator LaunchMission(VisualElement shell)
    {
        var button = _shellRoot != null ? UI.Find(_shellRoot, UiSelector.Name(_actionPrimary))?.TryCast<InteractiveElement>() : null;
        if (button == null || !button.IsInteractable() || _scene.Cutting)
            yield break;
        _scene.Cutting = true;
        int questions;
        // A launch that throws would otherwise leave every later room cut refused.
        try
        {
            Sound.Click();
            questions = (ExpectVehicleQuestion() ? 1 : 0) + (ExpectSuppliesQuestion() ? 1 : 0);
            if (questions == 0)
            {
                TableSounds.Play(TableSounds.Launch);
                ShutBars(shell, BarsLaunchClass);
                for (var t = 0f; t < BarsLaunchSeconds; t += UnityEngine.Time.unscaledDeltaTime)
                    yield return null;
            }
            button.OnClicked(MouseButton.LeftMouse);
            yield return null;
            yield return null;
        }
        finally
        {
            _scene.Cutting = false;
        }
        if (shell.panel == null || _shell != shell)
            yield break;
        var yes = FindDialogButton(VanillaUi.Elements.YesButton);
        if (yes == null)
        {
            // Vanilla declined without a question (a notice, a refusal): no loading screen comes,
            // and the bars shut for the launch open again.
            for (var t = 0f; questions == 0 && t < LaunchRefusedSeconds; t += UnityEngine.Time.unscaledDeltaTime)
                yield return null;
            if (questions == 0 && shell.panel != null && _shell == shell && UIManager.Get()?.IsLoadingOverlayVisible() != true)
                OpenShutBars(shell, BarsLaunchClass);
            yield break;
        }
        // A question that was not foreseen is the last one as far as anything here knows.
        if (questions == 0)
            OpenShutBars(shell, BarsLaunchClass);
        AnswerYesThroughBars(shell, yes, Math.Max(questions, 1));
    }

    // Wraps a question's Yes. Questions counts this one and those still to come after it.
    private void AnswerYesThroughBars(VisualElement shell, InteractiveElement yes, int questions)
    {
        var act = yes.GetOnLeftClickedAction();
        if (act == null)
            return;
        yes.SetOnLeftClickedAction((Il2CppSystem.Action<InteractiveElement>)(Action<InteractiveElement>)(pressed =>
            Context.Coroutines.Start(questions > 1 ? AnswerOnward(shell, pressed, act, questions) : AnswerThroughBars(shell, pressed, act))));
    }

    // A Yes that leads to the next question: vanilla's own Yes runs as it is, and the next
    // question's Yes is wrapped in turn once it shows.
    private System.Collections.IEnumerator AnswerOnward(VisualElement shell, InteractiveElement pressed, Il2CppSystem.Action<InteractiveElement> act, int questions)
    {
        if (_answering)
            yield break;
        _answering = true;
        try
        {
            act.Invoke(pressed);
        }
        finally
        {
            _answering = false;
        }
        yield return null;
        yield return null;
        if (shell == null || shell.panel == null || _shell != shell)
            yield break;
        var next = FindDialogButton(VanillaUi.Elements.YesButton);
        if (next != null)
            AnswerYesThroughBars(shell, next, questions - 1);
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
        // A question that was not foreseen may still follow. The bars open again for it, or its
        // No leaves the screen shut with nothing to open it, and its own Yes goes through the bars
        // in turn. Where nothing follows and no loading screen comes, vanilla refused the launch.
        yield return null;
        yield return null;
        if (shell == null || shell.panel == null || _shell != shell)
            yield break;
        var next = FindDialogButton(VanillaUi.Elements.YesButton);
        if (next == null)
        {
            for (var t = 0f; t < LaunchRefusedSeconds; t += UnityEngine.Time.unscaledDeltaTime)
                yield return null;
            if (shell.panel != null && _shell == shell && UIManager.Get()?.IsLoadingOverlayVisible() != true)
                OpenShutBars(shell, BarsLaunchClass);
            yield break;
        }
        OpenShutBars(shell, BarsLaunchClass);
        AnswerYesThroughBars(shell, next, 1);
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

    // Whether Start Mission will ask about a vehicle without a weapon. Vanilla's rule (the start
    // button's lambda in MissionPrepUIScreen.OnOpened): a deployed pilot with a vehicle whose
    // items carry a modular vehicle, with nothing in any vehicle weapon slot.
    private static bool ExpectVehicleQuestion()
    {
        var deployed = StrategyState.Get()?.BattlePlan?.m_EntitiesToDeploy;
        if (deployed == null)
            return false;
        foreach (var entity in deployed)
        {
            var pilot = entity?.GetUnitLeader()?.TryCast<Pilot>();
            var items = pilot?.GetVehicle() != null ? pilot.m_Items : null;
            if (items?.GetModularVehicle() == null)
                continue;
            var armed = false;
            foreach (var slot in VehicleWeaponSlots)
                armed |= items.GetItemAtSlot(slot) != null;
            if (!armed)
                return true;
        }
        return false;
    }

    // ItemSlotExtensions.IsVehicleWeaponSlot's slots, the ones vanilla's question looks in.
    private static readonly ItemSlot[] VehicleWeaponSlots =
    {
        ItemSlot.VehicleLightTurret, ItemSlot.VehicleHeavyTurret,
        ItemSlot.ModularVehicleLight, ItemSlot.ModularVehicleMedium, ItemSlot.ModularVehicleHeavy,
    };
}
