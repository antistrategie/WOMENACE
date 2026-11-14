using Il2CppMenace.UI;
using Il2CppMenace.UI.Menu;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Dev verbs for starting a fresh campaign from the title screen over the bridge, for tests that
// need a campaign state no existing save is in (the system map outside an operation, say).
//
//   {"verb":"NewRun.Start","mutate":true}   NewGameWindow.StartNewGame(), default settings
//   {"verb":"NewRun.Slots"}                  the open leader-pick dialog's slots
//   {"verb":"NewRun.Pick","args":[4],"mutate":true}  hire the first n pickable leaders and confirm
[DevVerb]
public static class NewRun
{
    [MutatingVerb]
    public static string Start()
    {
        NewGameWindow.StartNewGame();
        return "new game started; the leader pick dialog follows";
    }

    // Clicks a named interactive element on the active screen (or the open dialog), as a left
    // click would: NewGameButton, then StartGameButton, starts a campaign from the title screen.
    [MutatingVerb]
    public static string Click(string name)
    {
        var manager = UIManager.Get();
        var roots = new List<VisualElement>();
        var dialog = manager?.GetCurrentDialog();
        if (dialog != null)
            roots.Add(dialog);
        var doc = manager?.GetActiveScreen()?.m_Container?.rootVisualElement;
        if (doc != null)
            roots.Add(doc);
        foreach (var root in roots)
        {
            var el = UI.Find(root, UiSelector.Name(name))?.TryCast<InteractiveElement>();
            if (el == null)
                continue;
            el.OnClicked(MouseButton.LeftMouse);
            return $"clicked {name} ({el.GetIl2CppType().Name})";
        }
        return "no interactive element named " + name;
    }

    private static PickInitialLeadersDialog Dialog() =>
        UIManager.Get()?.GetCurrentDialog()?.TryCast<PickInitialLeadersDialog>();

    private static List<InteractiveElement> Slots(PickInitialLeadersDialog dialog)
    {
        var slots = new List<InteractiveElement>();
        VisualElement root = dialog;
        if (root == null)
            return slots;
        foreach (var el in UI.FindAll(root, UiSelector.Type<InteractiveElement>()))
        {
            var slot = el.TryCast<InteractiveElement>();
            var type = slot?.GetIl2CppType().Name ?? "";
            if (slot != null && (type.Contains("Slot") || (slot.name ?? "").Contains("Slot")))
                slots.Add(slot);
        }
        return slots;
    }

    public static string Slots()
    {
        var dialog = Dialog();
        if (dialog == null)
            return "no leader pick dialog open; current dialog " + UIManager.Get()?.GetCurrentDialog()?.GetIl2CppType().Name;
        var slots = Slots(dialog);
        return $"{slots.Count} slots, picked {dialog.GetPickedLeaderCount()}: "
            + string.Join(", ", slots.Select(s => $"{s.GetIl2CppType().Name}#{s.name}"));
    }

    [MutatingVerb]
    public static string Pick(int count)
    {
        var dialog = Dialog();
        if (dialog == null)
            return "no leader pick dialog open";
        var slots = Slots(dialog);
        foreach (var slot in slots)
        {
            if (dialog.GetPickedLeaderCount() >= count)
                break;
            dialog.ToggleLeaderHired(slot);
        }
        var picked = dialog.GetPickedLeaderCount();
        dialog.ExecuteConfirmButton();
        return $"picked {picked} of {slots.Count}, confirmed; dialog now {UIManager.Get()?.GetCurrentDialog()?.GetIl2CppType().Name ?? "closed"}";
    }
}
