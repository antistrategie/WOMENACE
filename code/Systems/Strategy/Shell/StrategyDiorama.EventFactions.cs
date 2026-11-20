using Il2CppMenace.UI;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The story factions' trust while a story event plays. Vanilla keeps its faction list in view
// beside its event dialog, so a choice that costs one faction's trust for another's is not made
// blind. Here the list is on the Intel board, which the event shuts, so the Dinergate's relay
// shows it at the left of the frame instead: a hologram in the board's look, with vanilla's own
// list laid over it as the board's cards. Nothing in it takes the pointer.
public sealed partial class StrategyDiorama
{
    private sealed class EventFactionsState
    {
        // An event plays over a screen with the story faction list.
        public bool On;
        public float Since;
        // The panel has grown out, and when it started to.
        public bool Open;
        public float OpenedAt;
        // The vanilla list shows on it.
        public bool Listed;
        // The panel has room beside the event's dialog.
        public bool Fits;
        // The cards' height as they laid out on the panel.
        public float Lists;
        public float Restyle;
        public VisualElement Panel;
        public VisualElement Scan;
        public VisualElement Dialog;
        // The dialog the speakers were read from, and their templates.
        public IntPtr SpeakersOf;
        public HashSet<IntPtr> Speakers = new();
        // What the panel changed on vanilla's elements, put back when the event ends.
        public readonly List<Action> Restore = new();
        public readonly HashSet<IntPtr> Quieted = new();
        public readonly HashSet<IntPtr> Lit = new();
    }

    private EventFactionsState _eventFactions = new();

    private const string EventFactionsName = "wm-event-factions";
    private const string EventFactionsFrameName = "wm-event-factions-frame";
    // The panel stands in the middle of the screen's left third, and never nearer the root's left
    // than the bars' own margin.
    private const float EventPanelMargin = BarPadding;
    // The heading under the panel's top padding (shell.uss .wui-event-factions), and the cards under it.
    private const float EventPanelPadTop = 14f;
    private const float EventHeadingHeight = 30f;
    private const float EventListsTop = EventPanelPadTop + EventHeadingHeight + 4f;
    // How far the panel keeps from the dialog, and the narrowest it is worth showing at.
    private const float EventPanelGap = 10f;
    private const float EventPanelMinWidth = 200f;
    // The shutters' move in for the event (shell.uss .wui-shutter): the panel grows once they are in.
    private const float EventPanelDelaySeconds = 0.3f;
    // Vanilla's event dialog (event_dialog.uxml) before it has laid out: a window 463 wide in the
    // middle of the screen.
    private const float EventWindowWidth = 463f;

    private static readonly UnityEngine.Color Gold = new(215f / 255f, 192f / 255f, 116f / 255f);

    // Asked a few times a second with the event dialog playing over the screen, or null. The panel
    // is for a screen whose faction list the board shows (the system map).
    private void WatchEventFactions(VisualElement shell, VisualElement dialog)
    {
        var state = _eventFactions;
        state.Dialog = dialog;
        var factions = _boardFactions;
        var want = dialog != null && factions?.panel != null && factions.parent == _shellRoot;
        if (want == state.On)
            return;
        if (!want)
        {
            HideEventFactions();
            return;
        }
        state.On = true;
        state.Since = UnityEngine.Time.unscaledTime;
        state.Open = state.Listed = false;
        state.Lists = 0f;
        state.Restyle = 0f;
        EventFactionsPanel(shell);
        PlaceOnEventPanel(factions);
    }

    // The panel shrinks the moment the event ends, and the list goes with it: put away, or back on
    // the board when the board reopens after.
    private void HideEventFactions()
    {
        var state = _eventFactions;
        if (!state.On)
            return;
        state.On = state.Open = state.Listed = false;
        state.Panel?.EnableInClassList("wui-board--open", false);
        if (_boardFactions?.panel != null)
            _boardFactions.style.display = DisplayStyle.None;
        RestoreEventFactions(state);
    }

    // Every frame of the shell's: the panel grows once the bars are in and the list arrives once it
    // has grown, as on the board. The cards are styled and measured again a few times a second.
    private void DriveEventFactions()
    {
        var state = _eventFactions;
        if (!state.On)
            return;
        var now = UnityEngine.Time.unscaledTime;
        if ((state.Restyle -= UnityEngine.Time.unscaledDeltaTime) <= 0f)
        {
            state.Restyle = 0.3f;
            PlaceOnEventPanel(_boardFactions);
        }
        var open = state.Fits && now - state.Since >= EventPanelDelaySeconds;
        if (open != state.Open)
        {
            state.Open = open;
            state.OpenedAt = now;
            state.Panel?.EnableInClassList("wui-board--open", open);
            if (!open && state.Listed)
            {
                state.Listed = false;
                if (_boardFactions?.panel != null)
                    _boardFactions.style.display = DisplayStyle.None;
            }
        }
        if (state.Open && !state.Listed && now - state.OpenedAt >= BoardGrowSeconds)
        {
            state.Listed = true;
            ShowOnEventPanel(_boardFactions);
        }
        if (state.Open)
            DriftScan(state.Scan, now);
    }

    // The panel, made once for each shell: the board's fill and frame, its hologram and the heading
    // of the board's faction column.
    private void EventFactionsPanel(VisualElement shell)
    {
        var state = _eventFactions;
        if (state.Panel?.parent == shell)
            return;
        var panel = new VisualElement { name = EventFactionsName, pickingMode = PickingMode.Ignore };
        panel.AddToClassList("wui-board");
        panel.AddToClassList("wui-event-factions");
        (_, state.Scan) = AddHoloFrame(panel, EventFactionsFrameName);
        var heading = HoloPart(panel, "wui-board__heading");
        HoloPart(heading, "wui-board__tick");
        var title = new Label(Locale.Text("WOMENACE::ui/intel/factions", "FACTIONS")) { pickingMode = PickingMode.Ignore };
        title.AddToClassList("wui-board__title");
        heading.Add(title);
        shell.Add(panel);
        // Over the black, under the bottom bar and the board, as the board stands.
        var board = UI.Find(shell, UiSelector.Name("wm-board"));
        if (board != null && board.parent == shell)
            panel.PlaceBehind(board);
        state.Panel = panel;
        // The shell coming off in the middle of an event leaves the list as vanilla made it.
        _shellUndo.Add(() => RestoreEventFactions(state));
    }

    // Places the panel and lays vanilla's list over it. The panel is the board's faction column
    // with the board's padding, as tall as its cards, in the middle of the space between the bars,
    // and narrower where the event's dialog would otherwise reach it.
    private void PlaceOnEventPanel(VisualElement factions)
    {
        var state = _eventFactions;
        var (root, panel) = (_shellRoot, state.Panel);
        if (factions?.panel == null || root == null || panel == null)
            return;
        var (rootWidth, rootHeight) = (root.layout.width, root.layout.height);
        if (!(rootWidth > 1f) || !(rootHeight > 1f))
        {
            state.Fits = false;
            return;
        }
        var inset = _shellInset ?? default;
        var clear = EventDialogLeft(state.Dialog, root);
        var width = MathF.Min(FactionsWidth + 2 * BoardPad, clear - EventPanelGap - EventPanelMargin);
        state.Fits = width >= EventPanelMinWidth;
        width = MathF.Max(width, EventPanelMinWidth);
        var left = MathF.Max(EventPanelMargin, MathF.Min(rootWidth / 6f - width / 2f, clear - EventPanelGap - width));

        // Sized from the cards as they laid out once they show, from their count before that.
        var cards = UI.Find(factions, UiSelector.Name(VanillaUi.Elements.FactionsContainer));
        if (state.Listed && cards != null && cards.layout.height > 1f)
            state.Lists = cards.layout.height;
        var count = UI.FindAll(factions, UiSelector.TypeName(VanillaUi.Elements.StoryFactionSlot))
            .Count(slot => slot.resolvedStyle.display != DisplayStyle.None);
        // A long list reaches into the black, but never as far as the bars' contents.
        var most = MathF.Max(FactionPitch, rootHeight - 2 * BarHeight - EventListsTop - BoardPad);
        var lists = Math.Clamp(state.Lists > 1f ? state.Lists : count * FactionPitch, FactionPitch, most);
        var height = EventListsTop + lists + BoardPad;
        // The bars' edges in the root's own space: the black stands from the screen's edges, which
        // a screen taller than 16:9 has beyond the root's.
        var (above, below) = (BarEventHeight - inset.Top, rootHeight + inset.Bottom - BarEventHeight);
        var top = (above + below) / 2f - height / 2f;

        panel.style.left = inset.Left + left;
        panel.style.top = inset.Top + top;
        panel.style.width = width;
        panel.style.height = height;
        PlaceList(factions, 0f, 0f, left + BoardPad, top + EventListsTop, width - 2 * BoardPad, lists,
            VanillaUi.Elements.StoryFactionsInner, VanillaUi.Elements.StoryFactionsHeadline);
        if (state.Listed && state.Fits)
            ShowOnEventPanel(factions);
    }

    private void ShowOnEventPanel(VisualElement factions)
    {
        if (factions?.panel == null)
            return;
        StyleLists(null, factions);
        QuietFactions(factions);
        LightSpeakersFaction(factions);
        factions.style.display = DisplayStyle.Flex;
    }

    // The list is read only here: the event's choices are the only thing to click, and a faction's
    // window opening from under the dialog would stand over it.
    private void QuietFactions(VisualElement factions)
    {
        var state = _eventFactions;
        foreach (var element in UI.FindAll(factions, UiSelector.Type<VisualElement>()))
        {
            if (element.pickingMode == PickingMode.Ignore || !state.Quieted.Add(element.Pointer))
                continue;
            var was = element.pickingMode;
            element.pickingMode = PickingMode.Ignore;
            state.Restore.Add(() => element.pickingMode = was);
        }
    }

    // The faction whose representative speaks in the event has its card edged in gold, as a card
    // under the pointer is on the board. The speakers are the conversation's cast, read from the
    // dialog's presenter.
    private void LightSpeakersFaction(VisualElement factions)
    {
        var state = _eventFactions;
        var dialog = state.Dialog;
        if (dialog == null)
            return;
        if (state.SpeakersOf != dialog.Pointer || state.Speakers.Count == 0)
        {
            state.SpeakersOf = dialog.Pointer;
            state.Speakers = EventSpeakers(dialog);
        }
        if (state.Speakers.Count == 0)
            return;
        foreach (var element in UI.FindAll(factions, UiSelector.TypeName(VanillaUi.Elements.StoryFactionSlot)))
        {
            Il2CppMenace.Conversations.SpeakerTemplate representative = null;
            try { representative = element.TryCast<Il2CppMenace.UI.Strategy.StoryFactionSlot>()?.GetFaction()?.GetTemplate()?.Representative; }
            catch { }
            var card = UI.Find(element, UiSelector.Name(VanillaUi.Elements.Pickable));
            if (representative == null || card == null || !state.Speakers.Contains(representative.Pointer) || !state.Lit.Add(card.Pointer))
                continue;
            SetBorderColour(card, Gold);
            state.Restore.Add(() => SetBorderColour(card, null));
        }
    }

    private static void SetBorderColour(VisualElement element, UnityEngine.Color? colour)
    {
        var value = colour is { } c ? new StyleColor(c) : new StyleColor(StyleKeyword.Null);
        element.style.borderTopColor = value;
        element.style.borderBottomColor = value;
        element.style.borderLeftColor = value;
        element.style.borderRightColor = value;
    }

    private static HashSet<IntPtr> EventSpeakers(VisualElement dialog)
    {
        var speakers = new HashSet<IntPtr>();
        try
        {
            var cast = dialog.TryCast<EventDialog>()?.GetPresenter()?.GetConversationInstance()?.m_Speakers;
            if (cast != null)
                foreach (var speaker in cast.Values)
                    if (speaker != null)
                        speakers.Add(speaker.Pointer);
        }
        catch { }
        return speakers;
    }

    // Puts back what the panel changed on vanilla's list. Once only, however often asked.
    private static void RestoreEventFactions(EventFactionsState state)
    {
        foreach (var restore in state.Restore)
        {
            try { restore(); }
            catch { }
        }
        state.Restore.Clear();
        state.Quieted.Clear();
        state.Lit.Clear();
    }

    // Where the event's dialog window starts from the root's left, in the root's own space. The
    // speaker's and the stage's pictures are left out: their boxes reach far wider than what is
    // drawn in them, and the panel stands clear of the speaker in the left third anyway.
    private static float EventDialogLeft(VisualElement dialog, VisualElement root)
    {
        var window = dialog != null ? UI.Find(dialog, UiSelector.Name(VanillaUi.Elements.EventWindow)) : null;
        float? left = Laid(window) ? window.worldBound.xMin : null;
        if (left is not { } x)
            return root.layout.width / 2f - EventWindowWidth / 2f;
        // The dialog is on the permanent layers. Should they be a panel of their own, a place is
        // carried across by its share of the whole screen's width.
        var (from, to) = (dialog.panel?.visualTree, root.panel?.visualTree);
        if (from != null && to != null && from.Pointer != to.Pointer && from.worldBound.width > 1f)
            x = to.worldBound.xMin + (x - from.worldBound.xMin) / from.worldBound.width * to.worldBound.width;
        return x - root.worldBound.xMin;

        static bool Laid(VisualElement e) => e?.panel != null && e.worldBound.width > 1f && e.resolvedStyle.display != DisplayStyle.None;
    }
}
