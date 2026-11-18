using Il2CppMenace.UI;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The Intel board the Dinergate projects on the Home view: vanilla's messages panel and story
// faction list laid over the shell's board, and vanilla's faction window centred over it.
public sealed partial class StrategyDiorama
{
    private int _intelMessages;
    private int _intelSeq;

    // Opens or closes the Intel board the Dinergate projects.
    internal void ToggleIntel() => SetIntel(!_intelOpen);

    private void SetIntel(bool open)
    {
        if (_intelOpen == open)
            return;
        _intelOpen = open;
        _intelSeq++;
        _boardMeasured = 0f;
        if (!open)
        {
            FactionWindow(out var list);
            list?.HideFactionWindow();
        }
        Sound.Click();
        SetDinergateProjecting(open);
        ApplyHome(_homeScreen, _homeRoomShown);
        if (open)
            Context.Coroutines.Start(WatchIntelEscape(_intelSeq));
    }

    // A click beside the board, or Escape: a faction's window shuts first, then the board.
    private void StepBackFromIntel()
    {
        var window = FactionWindow(out var list);
        if (window != null && window.resolvedStyle.display != DisplayStyle.None)
            list.HideFactionWindow();
        else
            SetIntel(false);
    }

    // Vanilla's window for a story faction, opened by a click on its card. It is a child of the
    // faction list and shows where the list's corner of the vanilla screen put it.
    private VisualElement FactionWindow(out Il2CppMenace.UI.Strategy.StoryFactionList list)
    {
        var element = _shellRoot != null ? UI.Find(_shellRoot, UiSelector.TypeName(VanillaUi.Elements.StoryFactionList)) : null;
        list = element?.TryCast<Il2CppMenace.UI.Strategy.StoryFactionList>();
        _factionWindow = element != null ? UI.Find(element, UiSelector.TypeName(VanillaUi.Elements.StoryFactionWindow)) : null;
        return _factionWindow;
    }

    // Centres the faction window on the screen, over the board. Its size is vanilla's until it has
    // laid out.
    private void PlaceFactionWindow()
    {
        var window = FactionWindow(out _);
        var parent = window?.parent;
        if (parent == null || _shellRoot == null)
            return;
        var (width, height) = (window.layout.width, window.layout.height);
        if (!(width > 1f) || !(height > 1f))
            (width, height) = (FactionWindowWidth, FactionWindowHeight);
        StyleFactionWindow(window);
        var corner = parent.worldBound;
        var screen = _shellRoot.worldBound;
        var was = !_inlined.Contains(window.Pointer) ? window.resolvedStyle.position : Position.Absolute;
        Inline(window, e =>
        {
            e.style.position = Position.Absolute;
            e.style.left = screen.x + (screen.width - width) / 2f - corner.x;
            e.style.top = screen.y + (screen.height - height) / 2f - corner.y;
            e.style.right = StyleKeyword.Auto;
            e.style.bottom = StyleKeyword.Auto;
        }, e =>
        {
            e.style.position = was;
            e.style.left = StyleKeyword.Null;
            e.style.top = StyleKeyword.Null;
            e.style.right = StyleKeyword.Null;
            e.style.bottom = StyleKeyword.Null;
        });
    }

    private VisualElement _factionWindow;

    // The window is centred the moment vanilla opens it, not a frame later.
    private void HookFactionWindow()
    {
        var target = VanillaUi.Targets.FactionSlotClicked;
        Context.Patches.Postfix(target.Type, target.Method, _ =>
        {
            if (_shell == null || !_intelOpen)
                return;
            try { PlaceFactionWindow(); }
            catch { }
        });
    }

    private const string FactionFrameName = "wm-faction-frame";

    // The faction window in the board's hologram: its black backing turned to the board's fill and
    // frame, and the board's glow, scanlines and corners laid over it.
    private void StyleFactionWindow(VisualElement window)
    {
        var backing = UI.Find(window, UiSelector.Class(VanillaUi.Elements.WindowBackgroundClass));
        Inline(backing, e =>
        {
            e.style.backgroundColor = BoardFill;
            e.style.borderTopColor = HoloLine;
            e.style.borderBottomColor = HoloLine;
            e.style.borderLeftColor = HoloLine;
            e.style.borderRightColor = HoloLine;
        }, e =>
        {
            e.style.backgroundColor = StyleKeyword.Null;
            e.style.borderTopColor = StyleKeyword.Null;
            e.style.borderBottomColor = StyleKeyword.Null;
            e.style.borderLeftColor = StyleKeyword.Null;
            e.style.borderRightColor = StyleKeyword.Null;
        });
        var host = backing?.parent;
        if (host == null || UI.Find(host, UiSelector.Name(FactionFrameName)) != null)
            return;
        var frame = new VisualElement { name = FactionFrameName, pickingMode = PickingMode.Ignore };
        frame.AddToClassList("wui-holo-frame");
        VisualElement Part(VisualElement parent, params string[] classes)
        {
            var part = new VisualElement { pickingMode = PickingMode.Ignore };
            foreach (var c in classes)
                part.AddToClassList(c);
            parent.Add(part);
            return part;
        }
        Part(frame, "wui-board__glow");
        _factionScan = Part(Part(frame, "wui-board__fx"), "wui-board__scan");
        foreach (var corner in new[] { "tl", "tr", "bl", "br" })
            Part(frame, "wui-holo__corner", "wui-holo__corner--" + corner);
        host.Add(frame);
        _shellUndo.Add(() =>
        {
            frame.RemoveFromHierarchy();
            _factionScan = null;
        });
    }

    private VisualElement _factionScan;

    // The scanlines drift down, a line's pitch at a time, so the loop has no seam.
    private const float ScanPitch = 3f;
    private const float ScanSpeed = 5f;

    private static void DriftScan(VisualElement scan, float time)
    {
        if (scan?.panel != null)
            scan.style.translate = new Translate(new Length(0f, LengthUnit.Pixel), new Length(time * ScanSpeed % ScanPitch - ScanPitch, LengthUnit.Pixel));
    }

    // shell.uss's board fill and frame colour, for vanilla elements styled inline.
    private static readonly UnityEngine.Color BoardFill = new(3f / 255f, 18f / 255f, 25f / 255f, 0.92f);
    private static readonly UnityEngine.Color HoloLine = new(79f / 255f, 220f / 255f, 234f / 255f);

    private const float FactionWindowWidth = 900f;
    private const float FactionWindowHeight = 407f;

    // Vanilla refuses to start an operation or plan a mission while a mandatory message is unread,
    // and points at the message in its list. The list is on the Intel board, so the board opens.
    private void HookMandatoryMessages()
    {
        var target = VanillaUi.Targets.HighlightMandatoryEvents;
        Context.Patches.Postfix(target.Type, target.Method, _ =>
        {
            if (_shell == null)
                return;
            // From the Operations view the camera glides back first: the Dinergate takes its
            // projecting pose from where the camera stands.
            if (OnHomeView(_policyScreen))
                SetIntel(true);
            else if (_policyScreen == null || !HomeScreens.Contains(_policyScreen))
            {
                // Refused from the briefing: back to the map it was opened from, where the board is.
                _intelOnReturn = true;
                ClickVanilla(VanillaUi.Elements.CloseButton);
            }
            else
            {
                SetHomeView(HomeView.Home);
                Context.Coroutines.Start(OpenIntelAfterGlide(_shell));
            }
        });
    }

    // Set when a mandatory message was refused away from the home screen: arriving back there
    // shows the Home view with the board open.
    private bool _intelOnReturn;

    private System.Collections.IEnumerator OpenIntelOnArrival()
    {
        // The home screen's shell is on a frame or two after its screen opens.
        for (var i = 0; i < 4; i++)
            yield return null;
        if (_shell != null && OnHomeView(_policyScreen))
            SetIntel(true);
    }

    private System.Collections.IEnumerator OpenIntelAfterGlide(VisualElement shell)
    {
        for (var t = 0f; t < GlideSeconds; t += UnityEngine.Time.unscaledDeltaTime)
            yield return null;
        yield return null;
        if (_shell == shell && OnHomeView(_policyScreen))
            SetIntel(true);
    }

    private System.Collections.IEnumerator WatchIntelEscape(int seq)
    {
        while (_intelOpen && seq == _intelSeq)
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                StepBackFromIntel();
                if (!_intelOpen)
                    yield break;
            }
            yield return null;
        }
    }

    // The board is the shell's. Its lists are the vanilla messages panel and story faction list,
    // left where the screen made them and laid over the board's two columns.
    private void RefreshIntel(VisualElement root, VisualElement messages, VisualElement factions)
    {
        // The board belongs to the Home view of a home screen, where the Dinergate is. Leaving for
        // the Operations view or another screen shuts it.
        if (_intelOpen && !OnHomeView(_policyScreen))
        {
            _intelOpen = false;
            _intelSeq++;
            SetDinergateProjecting(false);
        }
        var (count, unread) = CountMessages(messages);
        _intelListed = count;
        _intelMessages = unread;
        var number = unread > 0 ? unread.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";

        // The board grows out once the Dinergate is nearly in place, and shrinks the moment it shuts.
        UI.Find(_shell, UiSelector.Name("wm-board"))?.EnableInClassList("wui-board--open", _intelOpen && _intelBoardShown == _intelSeq);
        UI.Find(_shell, UiSelector.Name("wm-board-blocker"))?.EnableInClassList("wui-board-blocker--on", _intelOpen);
        // Story factions are on the system map only, as in vanilla.
        Show("wm-board-factions", factions != null);
        ShowCount("wm-board-count", number);
        ShowCount("wm-dg-tag-count", number);
        SetText("wm-board-empty", count == 0 ? Locale.Text("WOMENACE::ui/intel/no_messages", "NO MESSAGES") : "");

        // The board is as tall as its longer list, within limits.
        var factionCount = factions != null
            ? UI.FindAll(factions, UiSelector.TypeName(VanillaUi.Elements.StoryFactionSlot)).Count(slot => slot.resolvedStyle.display != DisplayStyle.None) : 0;
        // Sized from the lists as they laid out once they show, from their counts before that.
        FitBoard(_intelOpen && _boardMeasured > 1f ? _boardMeasured : Math.Max(count * MessagePitch, factionCount * FactionPitch));

        _boardMessages = messages;
        _boardFactions = factions;
        // The lists arrive once the board has grown out, and go the moment it starts to close.
        var seq = _intelSeq;
        var messagesWidth = factions != null ? BoardWidth - 4 * BoardPad - FactionsWidth : BoardWidth - 2 * BoardPad;
        PlaceOnBoard(messages, BoardLeft + BoardPad, messagesWidth, VanillaUi.Elements.MessagesInner, VanillaUi.Elements.MessagesHeader);
        PlaceOnBoard(factions, BoardLeft + BoardWidth - BoardPad - FactionsWidth, FactionsWidth, VanillaUi.Elements.StoryFactionsInner, VanillaUi.Elements.StoryFactionsHeadline);
        if (_intelOpen)
        {
            if (_intelListsShown == seq)
                ShowLists(messages, factions);
            else
                Context.Coroutines.Start(ShowListsAfter(messages, factions, seq));
        }
        // The Dinergate calls for attention while messages wait, or a faction's upgrade waits to be
        // claimed where the board lists the factions, and the board is shut.
        SetDinergateNotice(!_intelOpen && (unread > 0 || (factions != null && FactionUnlockWaiting())));
    }

    // Whether a faction the player knows has an O.C.I. upgrade waiting to be claimed in its window.
    // The window offers one for each trust level the faction has reached past the upgrades already
    // taken, while it has upgrades left to give (StoryFactionWindow.OnTrustChanged). A faction not
    // yet met, or destroyed, is not listed to claim from.
    private static bool FactionUnlockWaiting()
    {
        var factions = Il2CppMenace.States.StrategyState.Get()?.StoryFactions?.m_Factions;
        if (factions == null)
            return false;
        foreach (var faction in factions.Values)
            if (faction != null && faction.GetStatus() == Il2CppMenace.Strategy.StoryFactionStatus.Known
                && faction.GetTrustLevel() > faction.GetUnlockedUpgradeCount() && faction.HasLockedUpgrades())
                return true;
        return false;
    }

    // The vanilla lists the board shows, as last placed.
    private VisualElement _boardMessages;
    private VisualElement _boardFactions;

    private int _intelListed;

    // How many messages the vanilla list holds, and how many of them are not played to their end.
    // Vanilla keeps the read ones listed.
    private static (int Listed, int Unread) CountMessages(VisualElement messages)
    {
        if (messages == null || messages.panel == null)
            return (0, 0);
        var rows = UI.FindAll(messages, UiSelector.TypeName(VanillaUi.Elements.MessageElement)).ToList();
        var unread = rows.Count(row =>
        {
            var message = row.TryCast<Il2CppMenace.UI.Strategy.EventMessageElement>()?.m_Event;
            return message == null || !message.IsFinished();
        });
        return (rows.Count, unread);
    }

    private int _intelListsShown = -1;

    private int _intelBoardShown = -1;

    private System.Collections.IEnumerator ShowListsAfter(VisualElement messages, VisualElement factions, int seq)
    {
        if (_intelBoardShown != seq)
        {
            for (var t = 0f; t < BoardDelaySeconds; t += UnityEngine.Time.unscaledDeltaTime)
                yield return null;
            if (!_intelOpen || seq != _intelSeq)
                yield break;
            _intelBoardShown = seq;
            if (_shell != null)
                UI.Find(_shell, UiSelector.Name("wm-board"))?.EnableInClassList("wui-board--open", true);
        }
        for (var t = 0f; t < BoardGrowSeconds; t += UnityEngine.Time.unscaledDeltaTime)
            yield return null;
        if (!_intelOpen || seq != _intelSeq)
            yield break;
        _intelListsShown = seq;
        ShowLists(messages, factions);
    }

    private void ShowLists(VisualElement messages, VisualElement factions)
    {
        StyleLists(messages, factions);
        PlaceFactionWindow();
        if (messages != null)
            messages.style.display = DisplayStyle.Flex;
        if (factions != null)
            factions.style.display = DisplayStyle.Flex;
    }

    private static readonly UnityEngine.Color InkLabel = new(169f / 255f, 157f / 255f, 134f / 255f);

    // The lists' rows as the board's cards. Set on the elements, since vanilla's own rules for
    // them outrank shell.uss. Vanilla rebuilds its rows when a message goes, so this runs again
    // while the board is open.
    private void StyleLists(VisualElement messages, VisualElement factions)
    {
        if (messages != null)
        {
            var scroll = UI.Find(messages, UiSelector.Name(VanillaUi.Elements.EventsScrollView));
            var scrollWas = scroll != null && !_inlined.Contains(scroll.Pointer) ? scroll.resolvedStyle.position : Position.Relative;
            Inline(scroll, e =>
            {
                e.style.position = Position.Relative;
                e.style.left = 0;
                e.style.top = 0;
                e.style.marginTop = 0;
                e.style.marginLeft = 0;
                e.style.width = Length.Percent(100);
                e.style.height = Length.Percent(100);
            }, e =>
            {
                e.style.position = scrollWas;
                e.style.left = StyleKeyword.Null;
                e.style.top = StyleKeyword.Null;
                e.style.marginTop = StyleKeyword.Null;
                e.style.marginLeft = StyleKeyword.Null;
                e.style.width = StyleKeyword.Null;
                e.style.height = StyleKeyword.Null;
            });
            // The scroll view's own boxes between it and the rows, which keep room at the right.
            var view = scroll?.TryCast<ScrollView>();
            // Vanilla keeps the scroller's width free at all times. Here it shows only for a list
            // too long for the board.
            if (view != null && view.verticalScrollerVisibility != ScrollerVisibility.Auto)
            {
                var was = view.verticalScrollerVisibility;
                view.verticalScrollerVisibility = ScrollerVisibility.Auto;
                _shellUndo.Add(() => view.verticalScrollerVisibility = was);
            }
            foreach (var inner in view != null ? new[] { view.contentContainer, view.contentViewport, view.contentViewport?.parent } : Array.Empty<VisualElement>())
                if (inner != null && inner != scroll)
                    Inline(inner, e =>
                    {
                        e.style.width = Length.Percent(100);
                        e.style.maxWidth = StyleKeyword.None;
                        e.style.marginRight = 0;
                        e.style.paddingRight = 0;
                    }, e =>
                    {
                        e.style.width = StyleKeyword.Null;
                        e.style.maxWidth = StyleKeyword.Null;
                        e.style.marginRight = StyleKeyword.Null;
                        e.style.paddingRight = StyleKeyword.Null;
                    });
            foreach (var name in new[] { VanillaUi.Elements.SlotsContainer, VanillaUi.Elements.SlotsScrollViewRow })
                foreach (var wide in UI.FindAll(messages, UiSelector.Name(name)))
                    Inline(wide, e =>
                    {
                        // Vanilla caps the list's width for its own narrow panel.
                        e.style.width = Length.Percent(100);
                        e.style.minWidth = Length.Percent(100);
                        e.style.maxWidth = StyleKeyword.None;
                        e.style.alignSelf = Align.Stretch;
                        e.style.marginRight = 0;
                        e.style.paddingRight = 0;
                    }, e =>
                    {
                        e.style.marginRight = StyleKeyword.Null;
                        e.style.paddingRight = StyleKeyword.Null;
                        e.style.width = StyleKeyword.Null;
                        e.style.minWidth = StyleKeyword.Null;
                        e.style.maxWidth = StyleKeyword.Null;
                        e.style.alignSelf = StyleKeyword.Null;
                    });
            foreach (var row in UI.FindAll(messages, UiSelector.TypeName(VanillaUi.Elements.MessageElement)))
            {
                Inline(row, e => { e.style.width = Length.Percent(100); e.style.flexGrow = 1; },
                    e => { e.style.width = StyleKeyword.Null; e.style.flexGrow = StyleKeyword.Null; });
                // The row's card, which vanilla gives a fixed width.
                Inline(UI.Find(row, UiSelector.Name(VanillaUi.Elements.Pickable)), e => e.style.width = Length.Percent(100), e => e.style.width = StyleKeyword.Null);
                Inline(UI.Find(row, UiSelector.Name(VanillaUi.Elements.SenderLabel)), e =>
                {
                    e.style.fontSize = 9;
                    e.style.color = InkLabel;
                    e.style.width = 96;
                    e.style.flexShrink = 0;
                }, e =>
                {
                    e.style.fontSize = StyleKeyword.Null;
                    e.style.color = StyleKeyword.Null;
                    e.style.width = StyleKeyword.Null;
                    e.style.flexShrink = StyleKeyword.Null;
                });
                // Vanilla pins the delete button at a fixed offset, which lands on the subject in a
                // wider row. It joins the row's flow, at its right end.
                var delete = UI.Find(row, UiSelector.Name(VanillaUi.Elements.DeleteButton));
                var deleteWas = delete != null && !_inlined.Contains(delete.Pointer) ? delete.resolvedStyle.position : Position.Absolute;
                Inline(delete, e =>
                {
                    e.style.position = Position.Relative;
                    e.style.left = StyleKeyword.Auto;
                    e.style.right = StyleKeyword.Auto;
                    e.style.top = StyleKeyword.Auto;
                    e.style.flexShrink = 0;
                }, e =>
                {
                    e.style.position = deleteWas;
                    e.style.left = StyleKeyword.Null;
                    e.style.right = StyleKeyword.Null;
                    e.style.top = StyleKeyword.Null;
                    e.style.flexShrink = StyleKeyword.Null;
                });
                Inline(UI.Find(row, UiSelector.Name(VanillaUi.Elements.MessageLabel)), e =>
                {
                    e.style.fontSize = 11;
                    e.style.color = UnityEngine.Color.white;
                    e.style.width = StyleKeyword.Auto;
                    e.style.height = StyleKeyword.Auto;
                    e.style.flexGrow = 1;
                    e.style.flexShrink = 1;
                    e.style.whiteSpace = WhiteSpace.NoWrap;
                    e.style.overflow = Overflow.Hidden;
                    e.style.textOverflow = TextOverflow.Ellipsis;
                    e.style.marginRight = 8;
                }, e =>
                {
                    e.style.flexShrink = StyleKeyword.Null;
                    e.style.overflow = StyleKeyword.Null;
                    e.style.textOverflow = StyleKeyword.Null;
                    e.style.marginRight = StyleKeyword.Null;
                    e.style.fontSize = StyleKeyword.Null;
                    e.style.color = StyleKeyword.Null;
                    e.style.width = StyleKeyword.Null;
                    e.style.height = StyleKeyword.Null;
                    e.style.flexGrow = StyleKeyword.Null;
                    e.style.whiteSpace = StyleKeyword.Null;
                });
            }
        }
        if (factions != null)
        {
            var cards = UI.Find(factions, UiSelector.Name(VanillaUi.Elements.FactionsContainer));
            var (directionWas, wrapWas) = cards != null && !_inlined.Contains(cards.Pointer)
                ? (cards.resolvedStyle.flexDirection, cards.resolvedStyle.flexWrap) : (FlexDirection.Column, Wrap.NoWrap);
            Inline(cards, e =>
            {
                e.style.flexDirection = FlexDirection.Column;
                e.style.flexWrap = Wrap.NoWrap;
                e.style.width = Length.Percent(100);
            }, e =>
            {
                e.style.flexDirection = directionWas;
                e.style.flexWrap = wrapWas;
                e.style.width = StyleKeyword.Null;
            });
            foreach (var slot in UI.FindAll(factions, UiSelector.TypeName(VanillaUi.Elements.StoryFactionSlot)))
            {
                Inline(slot, e => e.style.width = Length.Percent(100), e => e.style.width = StyleKeyword.Null);
                if (slot.childCount > 0)
                    Inline(slot.ElementAt(0), e => e.style.width = Length.Percent(100), e => e.style.width = StyleKeyword.Null);
            }
        }
    }

    private void ShowCount(string name, string number)
    {
        SetText(name, number);
        Show(name, number.Length > 0);
    }

    // The board in panel pixels, as shell.uss lays it out: its left edge from the screen's centre,
    // its size, its padding, the faction column, and where the lists start under the headings.
    private const float BoardLeft = -300f;
    private const float BoardWidth = 700f;
    // The lists' height: at least room for the "no messages" line, at most what fits between the bars.
    private const float BoardListsMin = 60f;
    // All seven story factions fit. A longer message list scrolls in vanilla's scroll view.
    private const float BoardListsMax = 400f;
    // A message row and a faction card with their gaps, for sizing the board before the lists show.
    private const float MessagePitch = 27f;
    private const float FactionPitch = 55f;
    private float _boardLists = BoardListsMin;
    private float _boardMeasured;
    private float BoardHeight => BoardListsTop + _boardLists + BoardPad;

    // Sizes the board to its lists and keeps it centred.
    private void FitBoard(float lists)
    {
        _boardLists = Math.Clamp(lists, BoardListsMin, BoardListsMax);
        var board = _shell != null ? UI.Find(_shell, UiSelector.Name("wm-board")) : null;
        if (board == null)
            return;
        board.style.height = BoardHeight;
        board.style.marginTop = -BoardHeight / 2f;
    }
    private const float BoardPad = 20f;
    private const float FactionsWidth = 230f;
    private const float BoardListsTop = 74f;
    private const float BoardGrowSeconds = 0.24f;
    // How long after the click the board starts to grow: most of the Dinergate's flight.
    private const float BoardDelaySeconds = 0.36f;

    // Lays a vanilla list over one of the board's columns, hidden until the board has grown. Set on
    // the elements themselves, since the lists' own rules outrank shell.uss. The list's own heading
    // goes, the board's column names it.
    private void PlaceOnBoard(VisualElement panel, float leftFromCentre, float width, string innerName, string headingName)
    {
        if (panel == null)
            return;
        // Vanilla places these lists through their own inline styles, so what was there is kept and
        // put back, not cleared.
        if (_intelPlaced.Add(panel.Pointer))
        {
            var inside = UI.Find(panel, UiSelector.Name(innerName));
            var title = UI.Find(panel, UiSelector.Name(headingName));
            var restore = new List<Action> { KeepPlacement(panel) };
            if (inside != null && inside != panel)
                restore.Add(KeepPlacement(inside));
            if (title != null)
            {
                var shown = title.resolvedStyle.display;
                restore.Add(() => title.style.display = shown);
            }
            _shellUndo.Add(() => restore.ForEach(r => r()));
        }
        panel.style.display = DisplayStyle.None;
        panel.style.position = Position.Absolute;
        panel.style.left = Length.Percent(50);
        panel.style.top = Length.Percent(50);
        panel.style.right = StyleKeyword.Auto;
        panel.style.bottom = StyleKeyword.Auto;
        panel.style.marginLeft = leftFromCentre;
        panel.style.marginTop = -BoardHeight / 2f + BoardListsTop;
        panel.style.width = width;
        panel.style.height = BoardHeight - BoardListsTop - BoardPad;
        var inner = UI.Find(panel, UiSelector.Name(innerName));
        if (inner != null && inner != panel)
        {
            inner.style.position = Position.Relative;
            inner.style.left = 0;
            inner.style.top = 0;
            inner.style.right = StyleKeyword.Auto;
            inner.style.bottom = StyleKeyword.Auto;
            inner.style.width = Length.Percent(100);
            inner.style.height = Length.Percent(100);
        }
        var heading = UI.Find(panel, UiSelector.Name(headingName));
        if (heading != null)
            heading.style.display = DisplayStyle.None;
    }

    // Remembers how an element was placed and returns the action that puts it back: lengths to no
    // inline value, position and display to the values the element had resolved to.
    //
    // Known limit: on the system map this does not return vanilla's messages panel and faction list
    // to their corners (cause not found, and reopening the screen reuses the same elements). The
    // shell only comes off a live screen through the dev switch (Diorama.Toggle), so vanilla
    // captures of the system map are taken from a session that never showed the shell.
    private static Action KeepPlacement(VisualElement e)
    {
        var (display, position) = (e.resolvedStyle.display, e.resolvedStyle.position);
        return () =>
        {
            e.style.display = display;
            e.style.position = position;
            e.style.left = StyleKeyword.Null;
            e.style.top = StyleKeyword.Null;
            e.style.right = StyleKeyword.Null;
            e.style.bottom = StyleKeyword.Null;
            e.style.width = StyleKeyword.Null;
            e.style.height = StyleKeyword.Null;
            e.style.marginLeft = StyleKeyword.Null;
            e.style.marginTop = StyleKeyword.Null;
        };
    }

    private readonly HashSet<IntPtr> _intelPlaced = new();

    // How many messages wait in the vanilla messages panel.
    internal int IntelMessages => _intelMessages;
}
