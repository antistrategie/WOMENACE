using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.UI;
using Jiangyu.Game;
using Jiangyu.Game.Ui;
using Jiangyu.Game.Audio;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The Squads screen: vanilla's armoury restyled into the roster at the left, the squad standing
// in the refitting room, and the squad panel at the right. The roster is vanilla's unit selector
// and the panel is vanilla's unit window, both left wired as they are.
public sealed partial class StrategyDiorama
{
    private const string SquadsClass = "wm-squads";

    // The frame between the bars, in the panel's units: both columns start and end the same
    // distance from the bars.
    // The class of a list that scrolls on the shell's slim bar.
    private const string SlimScrollClass = "wui-scroll";
    private const float SquadsTop = 80f;
    private const float SquadsBottom = 640f;
    // The panels stand on the bars' own side edges: 32 in from the screen's, where the Home
    // button starts and the bottom bar's last destination ends.
    private const float RosterLeft = 32f;
    private const float RosterWidth = 180.8f;
    private const float RosterTabsHeight = 22.4f;
    private const float RosterTabsGap = 6.4f;
    // Vanilla's roster tiles carry this much margin on their left.
    private const float RosterTileInset = 3.2f;
    private const float SquadPanelLeft = 742.4f;
    private const float SquadPanelWidth = 505.6f;
    private static readonly UnityEngine.Color SquadPanelFill = new(0f, 0f, 0f, 0.92f);
    private static readonly UnityEngine.Color SquadPanelLine = new(67f / 255f, 83f / 255f, 106f / 255f);

    private bool _rosterDeceased;

    private void LayOutSquads(VisualElement root)
    {
        if (!root.ClassListContains(SquadsClass))
        {
            root.AddToClassList(SquadsClass);
            _shellUndo.Add(() => root.RemoveFromClassList(SquadsClass));
        }
        _squadPanelInPrep = false;
        LayOutRoster(root, UI.Find(root, UiSelector.Name(VanillaUi.Elements.UnitsScrollView)));
        LayOutSquadPanel(UI.Find(root, UiSelector.TypeName(VanillaUi.Elements.UnitWindow)));
    }

    // Vanilla's list is the living squads, a "Deceased" heading and the dead under it, in one
    // scroll view. Here the two are tabs over the list, and the list fills the height under them.
    private void LayOutRoster(VisualElement root, VisualElement list)
    {
        if (list == null)
            return;
        var top = SquadsTop + RosterTabsHeight + RosterTabsGap;
        Inline(list, e =>
        {
            var s = e.style;
            s.marginLeft = 0;
            s.marginTop = 0;
            s.left = RosterLeft;
            s.top = top;
            s.width = RosterWidth;
            s.height = SquadsBottom - top;
        }, e =>
        {
            var s = e.style;
            s.marginLeft = StyleKeyword.Null;
            s.marginTop = StyleKeyword.Null;
            s.left = StyleKeyword.Null;
            s.top = StyleKeyword.Null;
            s.width = StyleKeyword.Null;
            s.height = StyleKeyword.Null;
        });
        var living = UI.Find(list, UiSelector.Name(VanillaUi.Elements.UnitsContainer));
        var dead = UI.Find(list, UiSelector.Name(VanillaUi.Elements.DeadUnitsContainer));
        if (living == null || dead == null)
            return;
        var tabs = UI.Find(root, UiSelector.Name(RosterTabsName));
        if (tabs == null)
        {
            tabs = new VisualElement { name = RosterTabsName };
            tabs.AddToClassList("wui-seg-row");
            tabs.style.left = RosterLeft + RosterTileInset;
            tabs.style.top = SquadsTop;
            tabs.style.width = RosterWidth - RosterTileInset - RosterScrollerWidth;
            tabs.style.height = RosterTabsHeight;
            tabs.Add(RosterTab(root, "WOMENACE::ui/squads/active", "ACTIVE", false));
            tabs.Add(RosterTab(root, "WOMENACE::ui/squads/deceased", "DECEASED", true));
            // Beside the list and under the unit window in the screen's own row, so the window's
            // flyouts (the gift panel) open over the tabs.
            if (list.parent != null)
                list.parent.Insert(0, tabs);
            else
                root.Add(tabs);
            var made = tabs;
            _shellUndo.Add(() =>
            {
                made.RemoveFromHierarchy();
                living.style.display = StyleKeyword.Null;
                dead.style.display = StyleKeyword.Null;
                foreach (var chip in UI.FindAll(living, UiSelector.Name(PromotableName)).ToList())
                    chip.RemoveFromHierarchy();
            });
        }
        ShowRoster(tabs, living, dead);
    }

    // The other systems' header buttons arrive after the screen opens, so the panel is gone over
    // again a few times in its first frames.
    private System.Collections.IEnumerator SettleSquadPanel(VisualElement window)
    {
        var shell = _shell;
        for (var frame = 0; frame < 30; frame++)
        {
            yield return null;
            if (_shell != shell || window.panel == null)
                yield break;
            if (frame % 5 == 4)
                RefreshSquadPanel(window);
        }
    }

    private const float SquadMetaSize = 8f;
    private const float RankInsigniaSize = 16f;
    private static readonly UnityEngine.Color SquadMetaInk = new(169f / 255f, 157f / 255f, 134f / 255f);

    private void HeaderText(VisualElement label, float size, UnityEngine.Color ink)
    {
        Inline(label, e =>
        {
            e.style.fontSize = size;
            e.style.color = ink;
            // Vanilla nudges each label by its own margin, which leaves a row's words on different lines.
            e.style.marginTop = 0;
            e.style.marginBottom = 0;
            e.style.paddingTop = 0;
            e.style.paddingBottom = 0;
        }, e =>
        {
            e.style.fontSize = StyleKeyword.Null;
            e.style.color = StyleKeyword.Null;
            e.style.marginTop = StyleKeyword.Null;
            e.style.marginBottom = StyleKeyword.Null;
            e.style.paddingTop = StyleKeyword.Null;
            e.style.paddingBottom = StyleKeyword.Null;
        });
    }

    private const string RosterTabsName = "wm-roster-tabs";
    private const float RosterScrollerWidth = 8f;

    private VisualElement RosterTab(VisualElement root, string key, string fallback, bool deceased)
    {
        var tab = new VisualElement();
        tab.AddToClassList("wui-seg");
        var label = new Label(Locale.Text(key, fallback));
        label.AddToClassList("wui-seg__label");
        tab.Add(label);
        tab.RegisterCallback((EventCallback<ClickEvent>)(_ =>
        {
            _rosterDeceased = deceased;
            LayOutRoster(root, UI.Find(root, UiSelector.Name(VanillaUi.Elements.UnitsScrollView)));
        }));
        return tab;
    }

    private void ShowRoster(VisualElement tabs, VisualElement living, VisualElement dead)
    {
        living.style.display = _rosterDeceased ? DisplayStyle.None : DisplayStyle.Flex;
        dead.style.display = _rosterDeceased ? DisplayStyle.Flex : DisplayStyle.None;
        for (var i = 0; i < tabs.childCount && i < 2; i++)
            tabs.ElementAt(i).EnableInClassList("wui-seg--on", (i == 1) == _rosterDeceased);
    }

    // Vanilla's unit window, at its own size (it is no longer scaled to fit), as one flat panel
    // from the top gap to the bottom one.
    private void LayOutSquadPanel(VisualElement window)
    {
        if (window == null)
            return;
        _squadWindow = window;
        if (_squadRestores != window.Pointer)
        {
            _squadRestores = window.Pointer;
            _shellUndo.Add(() =>
            {
                _squadRestores = IntPtr.Zero;
                RestoreSquadPanel(window);
            });
        }
        // The picker's backing is vanilla's own backing element, flat at the panel's opacity, and its
        // hairline is on the picker. On the elements, since vanilla's rules for them outrank shell.uss.
        if (UI.Find(window, UiSelector.Name(VanillaUi.Elements.EquipmentPicker)) is { } picker)
        {
            WatchPickerScroll(picker);
            Inline(picker, e => Border(e.style, 0.64f, SquadPanelLine), e => Border(e.style, null, null));
            Inline(UI.Find(picker, UiSelector.Name(VanillaUi.Elements.UnitWindowBackground)), e =>
            {
                e.style.backgroundImage = StyleKeyword.None;
                e.style.backgroundColor = SquadPanelFill;
            }, e =>
            {
                e.style.backgroundImage = StyleKeyword.Null;
                e.style.backgroundColor = StyleKeyword.Null;
            });
        }
        var typeIconShown = UI.Find(window, UiSelector.Name(VanillaUi.Elements.UnitTypeIcon));
        if (typeIconShown != null && _inlined.Add(typeIconShown.Pointer))
            _shellUndo.Add(() => typeIconShown.style.display = StyleKeyword.Null);
        Inline(window, e =>
        {
            var s = e.style;
            s.marginTop = 0;
            // In mission preparation the window sits in a column, which is given the panel's place.
            s.left = _squadPanelInPrep ? 0 : SquadPanelLeft;
            s.top = _squadPanelInPrep ? 0 : SquadsTop;
            if (_squadPanelInPrep)
            {
                s.marginLeft = 0;
                s.marginRight = 0;
                s.marginBottom = 0;
            }
            s.right = StyleKeyword.Auto;
            s.bottom = StyleKeyword.Auto;
            s.width = SquadPanelWidth;
            s.height = SquadsBottom - SquadsTop;
        }, e =>
        {
            var s = e.style;
            s.marginTop = StyleKeyword.Null;
            s.marginLeft = StyleKeyword.Null;
            s.marginRight = StyleKeyword.Null;
            s.marginBottom = StyleKeyword.Null;
            s.left = StyleKeyword.Null;
            s.top = StyleKeyword.Null;
            s.right = StyleKeyword.Null;
            s.bottom = StyleKeyword.Null;
            s.width = StyleKeyword.Null;
            s.height = StyleKeyword.Null;
        });
        var frame = UI.Find(window, UiSelector.Name(VanillaUi.Elements.UnitWindowFrame));
        Inline(frame, e =>
        {
            var s = e.style;
            s.flexGrow = 1;
            s.backgroundColor = SquadPanelFill;
            Border(s, 0.64f, SquadPanelLine);
        }, e =>
        {
            var s = e.style;
            s.flexGrow = StyleKeyword.Null;
            s.backgroundColor = StyleKeyword.Null;
            Border(s, null, null);
        });
        if (frame == null)
            return;
        // Vanilla's drawn backing and frame go: the panel is the frame.
        for (var i = 0; i < frame.childCount; i++)
        {
            var part = frame.ElementAt(i);
            if (part.name == VanillaUi.Elements.UnitWindowBackground || part.name == VanillaUi.Elements.UnitWindowBorder)
                HideInline(part);
        }
        LayOutSquadHeader(frame);
        // Vanilla hangs the portrait and its bars from the window's bottom edge. The window is
        // shorter here, so they are held at vanilla's size from the top.
        Place(UI.Find(frame, UiSelector.Name(VanillaUi.Elements.LeaderMask)), null, null, null, VanillaPortraitHeight, true);
        Place(UI.Find(frame, UiSelector.Name(VanillaUi.Elements.LeaderElements)), null, VanillaPortraitBarsTop, null, null);
        // The stats' numbers are white, on the labels themselves: vanilla's rule for them outranks shell.uss.
        if (UI.Find(frame, UiSelector.Name(VanillaUi.Elements.AdditionalStats)) is { } stats)
            foreach (var value in UI.FindAll(stats, UiSelector.Name(VanillaUi.Elements.StatValue)))
                Inline(value, e =>
                {
                    e.style.color = UnityEngine.Color.white;
                    // Vanilla drops the number below its icon's middle. It sits level with it here.
                    e.style.marginTop = 0;
                    e.style.marginBottom = 0;
                    e.style.alignSelf = Align.Center;
                }, e =>
                {
                    e.style.color = StyleKeyword.Null;
                    e.style.marginTop = StyleKeyword.Null;
                    e.style.marginBottom = StyleKeyword.Null;
                    e.style.alignSelf = StyleKeyword.Null;
                });
        // A dark box with a hairline edge behind the portrait, which runs past it as vanilla's does.
        if (UI.Find(frame, UiSelector.Name(VanillaUi.Elements.StatsColumn)) is { } column
            && UI.Find(column, UiSelector.Name(PortraitBoxName)) == null)
        {
            var box = new VisualElement { name = PortraitBoxName, pickingMode = PickingMode.Ignore };
            box.AddToClassList("wm-squads__portrait-box");
            column.Insert(0, box);
            _shellUndo.Add(box.RemoveFromHierarchy);
        }
        LayOutSquadActions(window, frame);
        LayOutSquadTabs(window, frame);
        ScrollLoadout(window);
        RefreshSquadPanel(window);
        Context.Coroutines.Start(SettleSquadPanel(window));
    }

    // The header keeps the form swap and the statistics. On Squads it gains Dismiss and loses
    // vanilla's deployment button. In mission preparation it keeps that button and has no Dismiss.
    private void LayOutSquadActions(VisualElement window, VisualElement frame)
    {
        var header = UI.Find(frame, UiSelector.Name(VanillaUi.Elements.UnitWindowHeader));
        var statistics = header != null ? UI.Find(header, UiSelector.Name(VanillaUi.Elements.StatisticsButton)) : null;
        if (header == null || statistics == null)
            return;
        if (!_squadPanelInPrep && UI.Find(header, UiSelector.Name(DismissName)) == null)
        {
            var dismiss = MiniButton(DismissName, Locale.Text("WOMENACE::ui/squads/dismiss", "DISMISS"), () => AskDismiss(window));
            dismiss.AddToClassList("wui-mini--danger");
            header.Insert(header.IndexOf(statistics), dismiss);
            _shellUndo.Add(dismiss.RemoveFromHierarchy);
        }
        // Who goes on a mission is chosen in mission preparation, so the button shows only there.
        Inline(UI.Find(header, UiSelector.Name(VanillaUi.Elements.UnitWindowButton)), e =>
        {
            e.style.display = _squadPanelInPrep ? StyleKeyword.Null : DisplayStyle.None;
            e.style.height = _squadPanelInPrep ? HeaderButtonHeight : StyleKeyword.Null;
        }, e =>
        {
            e.style.display = StyleKeyword.Null;
            e.style.height = StyleKeyword.Null;
        });
        var row = UI.Find(frame, UiSelector.Name(VanillaUi.Elements.Squaddies))?.parent;
        if (row != null && !row.ClassListContains("wm-squads__type-row"))
        {
            row.AddToClassList("wm-squads__type-row");
            _shellUndo.Add(() => row.RemoveFromClassList("wm-squads__type-row"));
        }
    }

    private const string DismissName = "wm-squads-dismiss";

    // Vanilla dismisses from the hiring screen: a yes or no question, then the leader leaves the
    // roster, her promotion points come back and authority changes by the campaign's set amount.
    // This asks the same question and makes the same calls.
    private void AskDismiss(VisualElement window)
    {
        try
        {
            var leader = Affinity.LeaderOf(window);
            if (leader == null || !leader.IsAlive())
                return;
            var refund = leader.GetFullDemotePromotionRefund();
            var name = UI.Find(window, UiSelector.Name(VanillaUi.Elements.LeaderName))?.TryCast<Label>()?.text?.Trim() ?? "";
            var headline = Locale.Format("WOMENACE::ui/squads/dismiss_title", "DISMISS {0}", name);
            var text = Locale.Format("WOMENACE::ui/squads/dismiss_text",
                "{0} leaves the roster for good. Her equipment returns to the armoury and {1} promotion points are refunded.", name, refund);
            var pointer = leader.Pointer;
            UIManager.Get()?.AddSimpleDialog()?.InitYesNo("wm-dismiss", headline, text,
                Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(() => Dismiss(window, pointer, refund))), null, false);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: dismiss question failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Dismiss(VisualElement window, IntPtr pointer, int refund)
    {
        try
        {
            var leader = Affinity.LeaderOf(window);
            var state = StrategyState.Get();
            // Only the leader the question was asked about.
            if (leader == null || state == null || leader.Pointer != pointer || !state.Roster.TryDismissLeader(leader))
                return;
            state.ChangeVar(StrategyVars.PromotionPoints, refund, true, true, true);
            var config = StrategyConfig.Current;
            if (config != null)
                state.ChangeVar(StrategyVars.Authority, config.AuthorityChangeOnDismiss, true, true, true);
            Context.Log.Info($"strategy diorama: dismissed a leader, {refund} promotion points refunded");
            // Vanilla's armoury builds its roster when it opens, so it is opened afresh.
            Context.Coroutines.Start(ReopenSquads());
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: dismiss failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static System.Collections.IEnumerator ReopenSquads()
    {
        yield return null;
        VanillaNavigation.Go(VanillaNavigation.Target.Bridge);
        yield return null;
        yield return null;
        VanillaNavigation.Go(VanillaNavigation.Target.Armory);
    }

    private static VisualElement MiniButton(string name, string text, Action pressed)
    {
        var button = new VisualElement { name = name };
        button.AddToClassList("wui-mini");
        var label = new Label(text);
        label.AddToClassList("wui-mini__label");
        button.Add(label);
        button.RegisterCallback((EventCallback<ClickEvent>)(_ =>
        {
            Sound.Click();
            pressed();
        }));
        return button;
    }

    private int _squadTab;
    private bool _dressLinks;
    private const string PanelTabsName = "wm-panel-tabs";
    private const string PanelTabName = "wm-panel-tab-";
    private const string AppearanceTabName = "wm-tab-appearance";
    private const string AffinityTabName = "wm-tab-affinity";
    private const string DressRowName = "wm-dress-row";

    // Loadout, Appearance and Affinity head the panel's right column. Loadout is vanilla's own
    // column. The other two stand in its place, filled by the outfit picker and the affinity
    // system from the same content their own windows show.
    private void LayOutSquadTabs(VisualElement window, VisualElement frame)
    {
        var column = UI.Find(frame, UiSelector.Name(VanillaUi.Elements.EquipmentColumn));
        if (column == null || UI.Find(column, UiSelector.Name(PanelTabsName)) != null)
            return;
        var tabs = new VisualElement { name = PanelTabsName };
        tabs.AddToClassList("wui-utab-row");
        var names = new[] { ("WOMENACE::ui/squads/loadout", "LOADOUT"), ("WOMENACE::ui/squads/appearance", "APPEARANCE"), ("WOMENACE::ui/squads/affinity", "AFFINITY") };
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            var tab = new VisualElement { name = PanelTabName + i };
            tab.AddToClassList("wui-utab");
            var label = new Label(Locale.Text(names[i].Item1, names[i].Item2));
            label.AddToClassList("wui-utab__label");
            tab.Add(label);
            tab.RegisterCallback((EventCallback<ClickEvent>)(_ =>
            {
                Sound.Click();
                _squadTab = index;
                RefreshSquadPanel(window);
            }));
            tabs.Add(tab);
        }
        var appearance = new VisualElement { name = AppearanceTabName };
        appearance.AddToClassList("wm-squads__tab");
        var dress = new VisualElement { name = DressRowName };
        dress.AddToClassList("wui-seg-row");
        dress.AddToClassList("wm-squads__dress");
        dress.Add(DressTab(window, "WOMENACE::ui/squads/dress_leader", "SQUAD LEADER", false));
        dress.Add(DressTab(window, "WOMENACE::ui/squads/dress_links", "DUMMY LINKS", true));
        appearance.Add(dress);
        if (TransmogPickerSystem.Instance?.BuildHosted() is { } outfits)
            appearance.Add(Scrolling(outfits));
        var affinity = new VisualElement { name = AffinityTabName };
        affinity.AddToClassList("wm-squads__tab");
        var level = new VisualElement { name = AffinityLevelName };
        level.AddToClassList("wm-squads__level");
        var number = new Label { name = AffinityLevelName + "-number" };
        number.AddToClassList("wm-squads__level-number");
        var toNext = new Label { name = AffinityLevelName + "-next" };
        toNext.AddToClassList("wm-squads__level-next");
        level.Add(number);
        level.Add(toNext);
        var gap = new VisualElement();
        gap.style.flexGrow = 1;
        level.Add(gap);
        // Give Gift stands at the level's right, over the bar it fills.
        var gifts = MiniButton(AffinitySystem.InPlaceGiftButton, Locale.Text("WOMENACE::ui/squads/give_gift", "GIVE GIFT"), () => AffinitySystem.Instance?.ToggleGifts(window));
        gifts.AddToClassList("wm-squads__gift");
        gifts.AddToClassList("wui-mini--gold");
        level.Add(gifts);
        affinity.Add(level);
        var bar = new VisualElement();
        bar.AddToClassList("wm-squads__level-bar");
        var fill = new VisualElement { name = AffinityLevelName + "-fill" };
        fill.AddToClassList("wm-squads__level-fill");
        bar.Add(fill);
        affinity.Add(bar);
        if (AffinitySystem.Instance?.BuildInPlace() is { } levels)
            affinity.Add(Scrolling(levels));
        column.Insert(0, tabs);
        column.Add(appearance);
        column.Add(affinity);
        _shellUndo.Add(() =>
        {
            tabs.RemoveFromHierarchy();
            appearance.RemoveFromHierarchy();
            affinity.RemoveFromHierarchy();
            for (var i = 0; i < column.childCount; i++)
                column.ElementAt(i).style.display = StyleKeyword.Null;
        });
    }

    private const string AffinityLevelName = "wm-affinity-level";

    private const string LoadoutScrollName = "wm-loadout-scroll";

    // The loadout's slots scroll once they are taller than the panel: a vehicle has a slot more
    // than a squad, and the panel is shorter than vanilla's window. Vanilla's own holder of the
    // slots goes into the scroll view whole, so everything that finds a slot by it still does.
    private void ScrollLoadout(VisualElement window)
    {
        var slots = UI.Find(window, UiSelector.TypeName(VanillaUi.Elements.EquipmentSlots));
        if (slots?.parent == null || UI.Find(window, UiSelector.Name(LoadoutScrollName)) != null)
            return;
        var (home, index) = (slots.parent, slots.parent.IndexOf(slots));
        var scroll = Scrolling(slots);
        scroll.name = LoadoutScrollName;
        if (scroll.TryCast<ScrollView>() is { } view)
            view.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        home.Insert(index, scroll);
        _shellUndo.Add(() =>
        {
            // The scroll view may already be out of the panel: the slots go back where they were.
            var at = home.IndexOf(scroll);
            home.Insert(at >= 0 ? at : Math.Min(index, home.childCount), slots);
            scroll.RemoveFromHierarchy();
            slots.style.alignSelf = StyleKeyword.Null;
            slots.style.flexShrink = StyleKeyword.Null;
        });
    }

    // A list that takes the rest of its tab's height and scrolls once it is longer than that:
    // a doll with many outfits, or more levels than fit.
    private static VisualElement Scrolling(VisualElement content)
    {
        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.AddToClassList(SlimScrollClass);
        scroll.Add(content);
        // The scroll view lays its own container out in a row, where it is only as wide as the
        // list's words. It grows to the view's width, keeps its own height (it is not stretched
        // to the view's, so a long list scrolls) and stacks its contents, so the list takes its
        // width. Set on the elements, since the scroll view's own rules outrank shell.uss.
        var holder = scroll.contentContainer;
        holder.style.flexDirection = FlexDirection.Column;
        holder.style.flexGrow = 1;
        holder.style.flexBasis = 0;
        holder.style.alignSelf = Align.FlexStart;
        content.style.alignSelf = Align.Stretch;
        content.style.flexShrink = 0;
        return scroll;
    }

    // The level in large figures, how far along it is to the next, and a bar of the same.
    private void ShowAffinityLevel(VisualElement affinity, BaseUnitLeader leader)
    {
        var key = Affinity.KeyFor(leader);
        if (key == 0)
            return;
        var points = Affinity.PointsFor(Context, key);
        var level = Affinity.LevelForPoints(points);
        var top = level >= Affinity.MaxLevel;
        var floor = level > 1 ? Affinity.StepThresholds[level - 2] : 0;
        var share = top ? 1f : UnityEngine.Mathf.Clamp01((points - floor) / (float)(Affinity.StepThresholds[level - 1] - floor));
        AffinitySystem.ShowLevel(affinity, AffinityLevelName, level, top, share);
    }

    private VisualElement DressTab(VisualElement window, string key, string fallback, bool links)
    {
        var tab = new VisualElement { name = links ? "wm-dress-links" : "wm-dress-leader" };
        tab.AddToClassList("wui-seg");
        var label = new Label(Locale.Text(key, fallback));
        label.AddToClassList("wui-seg__label");
        tab.Add(label);
        tab.RegisterCallback((EventCallback<ClickEvent>)(_ =>
        {
            Sound.Click();
            _dressLinks = links;
            RefreshSquadPanel(window);
        }));
        return tab;
    }

    // Shows the chosen tab and fills it for the window's current leader. Runs again whenever the
    // window takes a leader.
    private void RefreshSquadPanel(VisualElement window)
    {
        try
        {
            var column = UI.Find(window, UiSelector.Name(VanillaUi.Elements.EquipmentColumn));
            var tabs = column != null ? UI.Find(column, UiSelector.Name(PanelTabsName)) : null;
            if (tabs == null)
                return;
            var leader = Affinity.LeaderOf(window);
            // A doll of ours has outfits and affinity. Anyone else has the loadout alone.
            // The chosen tab is kept for the next doll: only what shows changes.
            var ours = Affinity.KeyFor(leader) != 0;
            // Mission preparation has the loadout alone, with no tabs over it: outfits and affinity
            // are the Squads screen's.
            var tab = ours && !_squadPanelInPrep ? _squadTab : 0;
            tabs.style.display = _squadPanelInPrep ? DisplayStyle.None : StyleKeyword.Null;
            for (var i = 0; i < tabs.childCount; i++)
            {
                tabs.ElementAt(i).EnableInClassList("wui-utab--on", i == tab);
                tabs.ElementAt(i).EnableInClassList("wui-utab--off", i > 0 && !ours);
            }
            for (var i = 0; i < column.childCount; i++)
            {
                var part = column.ElementAt(i);
                if (part == tabs)
                    continue;
                var shown = part.name switch
                {
                    AppearanceTabName => tab == 1,
                    AffinityTabName => tab == 2,
                    _ => tab == 0,
                };
                part.style.display = shown ? StyleKeyword.Null : DisplayStyle.None;
            }
            if (tab == 1 && UI.Find(column, UiSelector.Name(AppearanceTabName)) is { } appearance)
            {
                // A solo doll has no links to dress, whatever the switch was last set to.
                var hasLinks = TransmogPickerSystem.Instance?.FillHosted(window, appearance, _dressLinks) == true;
                var links = _dressLinks && hasLinks;
                if (UI.Find(appearance, UiSelector.Name(DressRowName)) is { } dress)
                {
                    dress.style.display = hasLinks ? DisplayStyle.Flex : DisplayStyle.None;
                    for (var i = 0; i < dress.childCount; i++)
                        dress.ElementAt(i).EnableInClassList("wui-seg--on", (i == 1) == links);
                }
            }
            if (tab == 2 && UI.Find(column, UiSelector.Name(AffinityTabName)) is { } affinity)
            {
                if (UI.Find(affinity, UiSelector.Name(AffinitySystem.InPlaceLevels)) is { } levels)
                    AffinitySystem.Instance?.Populate(levels, window);
                ShowAffinityLevel(affinity, leader);
            }
            if (UI.Find(window, UiSelector.Name(DismissName)) is { } dismiss)
                KeepInHeader(window, dismiss);
            // The header's buttons are one height and as wide as their words. The form swap is
            // another system's, at vanilla's size.
            if (UI.Find(window, UiSelector.Name(HeaderButtons[0])) is { } swap)
            {
                swap.style.height = HeaderButtonHeight;
                swap.style.width = StyleKeyword.Auto;
            }
            MarkPromotable(window);
            Recolour(window);
            DrawStatIcons(window);
            // The gift button is the Affinity tab's now, and its own system shows it again on each refresh.
            if (UI.Find(window, UiSelector.Name(HeaderButtons[1])) is { } gift)
                gift.style.display = DisplayStyle.None;
            // The small unit type icon before the unit's name is the squad type row's, larger. Out of
            // the line's layout, so the rank starts under the name.
            if (UI.Find(window, UiSelector.Name(VanillaUi.Elements.UnitTypeIcon)) is { } typeIcon)
                typeIcon.style.display = DisplayStyle.None;
            // Vanilla shows its deployment button again each time it sets the window up.
            if (!_squadPanelInPrep && UI.Find(window, UiSelector.Name(VanillaUi.Elements.UnitWindowButton)) is { } deploy)
                deploy.style.display = DisplayStyle.None;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: squad panel refresh failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static readonly UnityEngine.Color SquadInk = UnityEngine.Color.white;
    private static readonly UnityEngine.Color SquadBlue = new(120f / 255f, 143f / 255f, 176f / 255f);

    // Vanilla's khaki and olive text in the shell's white, blue-grey and label colours. The
    // health bars keep vanilla's green. On the elements themselves, since vanilla's rules for them outrank
    // shell.uss, and again on each refresh, since vanilla rebuilds the slots.
    private void Recolour(VisualElement window)
    {
        Tint(window, VanillaUi.Elements.SlotHeaderText, SquadMetaInk);
        Tint(window, VanillaUi.Elements.ItemName, SquadInk);
        Tint(window, VanillaUi.Elements.ShortItemName, SquadBlue);
        Tint(window, VanillaUi.Elements.UnitTypeHeadline, SquadBlue);
        Tint(window, VanillaUi.Elements.UnitTypeName, SquadInk);
        Tint(window, VanillaUi.Elements.SquaddiesCount, SquadInk);
        foreach (var count in UI.FindAll(window, UiSelector.TypeName(VanillaUi.Elements.AccessoriesCount)))
            foreach (var label in UI.FindAll(count, UiSelector.TypeName("Label")))
                label.style.color = SquadBlue;
        // Vanilla's olive rules and frames, in the panel's hairline blue: the squad type row's
        // line, frame and icon box, and the slots' slanted label tabs (a sprite).
        // The header has no rule under it: the tabs' own rule and the portrait's box close it off.
        if (UI.Find(window, UiSelector.Name(VanillaUi.Elements.UnitWindowHeader)) is { } header)
            header.style.borderBottomWidth = 0;
        var typeRow = UI.Find(window, UiSelector.Name(VanillaUi.Elements.Squaddies))?.parent;
        for (var e = typeRow; e != null && e.name != VanillaUi.Elements.EquipmentColumn; e = e.parent)
        {
            Edge(e);
            if (UI.Find(e, UiSelector.Name(VanillaUi.Elements.TypeRowLine)) is { } line)
                line.style.backgroundColor = SquadPanelLine;
            Edge(UI.Find(e, UiSelector.Name(VanillaUi.Elements.UnitTypeIconBig)));
        }
        // A slot's heading is its words alone: vanilla's tab under them is a picture filled with
        // near black, which reads as a bar on this panel.
        foreach (var slotLabel in UI.FindAll(window, UiSelector.Class(VanillaUi.Elements.SlotHeaderClass)))
            if (slotLabel.childCount > 0)
                slotLabel.ElementAt(0).style.unityBackgroundImageTintColor = UnityEngine.Color.clear;
    }

    private const string StatIconName = "wm-stat-icon";

    // Vanilla's stat icons are white tiles with the picture cut out of them, which no tint turns
    // dark. A stat whose vanilla picture is known wears a line icon of ours in its box, and its
    // own picture is not drawn. Any other stat keeps vanilla's tile.
    private static readonly Dictionary<string, string> StatIcons = new()
    {
        ["icon_armor"] = "wm-stat-icon--armor",
        ["icon_detection"] = "wm-stat-icon--detection",
        ["icon_concealment"] = "wm-stat-icon--concealment",
        ["icon_vision"] = "wm-stat-icon--vision",
    };

    private static void DrawStatIcons(VisualElement window)
    {
        var stats = UI.Find(window, UiSelector.Name(VanillaUi.Elements.AdditionalStats));
        if (stats == null)
            return;
        foreach (var icon in UI.FindAll(stats, UiSelector.Name(VanillaUi.Elements.StatIcon)))
        {
            var sprite = icon.resolvedStyle.backgroundImage.sprite;
            var ours = sprite != null && StatIcons.TryGetValue(sprite.name, out var known) ? known : null;
            var drawn = UI.Find(icon, UiSelector.Name(StatIconName));
            if (ours == null)
            {
                drawn?.RemoveFromHierarchy();
                icon.style.unityBackgroundImageTintColor = StyleKeyword.Null;
                continue;
            }
            if (drawn == null)
            {
                drawn = new VisualElement { name = StatIconName, pickingMode = PickingMode.Ignore };
                drawn.AddToClassList("wm-stat-icon");
                icon.Add(drawn);
            }
            foreach (var each in StatIcons.Values)
                drawn.EnableInClassList(each, each == ours);
            icon.style.unityBackgroundImageTintColor = UnityEngine.Color.clear;
        }
    }

    private static void Edge(VisualElement element)
    {
        if (element == null)
            return;
        var s = element.style;
        s.borderLeftColor = SquadPanelLine;
        s.borderTopColor = SquadPanelLine;
        s.borderRightColor = SquadPanelLine;
        s.borderBottomColor = SquadPanelLine;
    }

    private IntPtr _squadRestores;

    // Takes off what RefreshSquadPanel, Recolour, DrawStatIcons and DriveSquads set on vanilla's
    // window. They run on every refresh, so they are undone together, once, when the shell comes off.
    private static void RestoreSquadPanel(VisualElement window)
    {
        foreach (var name in RecolouredLabels)
            foreach (var label in UI.FindAll(window, UiSelector.Name(name)))
                label.style.color = StyleKeyword.Null;
        foreach (var count in UI.FindAll(window, UiSelector.TypeName(VanillaUi.Elements.AccessoriesCount)))
            foreach (var label in UI.FindAll(count, UiSelector.TypeName("Label")))
                label.style.color = StyleKeyword.Null;
        if (UI.Find(window, UiSelector.Name(VanillaUi.Elements.UnitWindowHeader)) is { } header)
            header.style.borderBottomWidth = StyleKeyword.Null;
        var typeRow = UI.Find(window, UiSelector.Name(VanillaUi.Elements.Squaddies))?.parent;
        for (var e = typeRow; e != null && e.name != VanillaUi.Elements.EquipmentColumn; e = e.parent)
        {
            Unedge(e);
            if (UI.Find(e, UiSelector.Name(VanillaUi.Elements.TypeRowLine)) is { } line)
                line.style.backgroundColor = StyleKeyword.Null;
            Unedge(UI.Find(e, UiSelector.Name(VanillaUi.Elements.UnitTypeIconBig)));
        }
        foreach (var slotLabel in UI.FindAll(window, UiSelector.Class(VanillaUi.Elements.SlotHeaderClass)))
            if (slotLabel.childCount > 0)
                slotLabel.ElementAt(0).style.unityBackgroundImageTintColor = StyleKeyword.Null;
        if (UI.Find(window, UiSelector.Name(VanillaUi.Elements.AdditionalStats)) is { } stats)
            foreach (var icon in UI.FindAll(stats, UiSelector.Name(VanillaUi.Elements.StatIcon)))
            {
                UI.Find(icon, UiSelector.Name(StatIconName))?.RemoveFromHierarchy();
                icon.style.unityBackgroundImageTintColor = StyleKeyword.Null;
            }
        if (UI.Find(window, UiSelector.Name(HeaderButtons[0])) is { } swap)
        {
            swap.style.height = StyleKeyword.Null;
            swap.style.width = StyleKeyword.Null;
        }
        if (UI.Find(window, UiSelector.Name(HeaderButtons[1])) is { } gift)
            gift.style.display = StyleKeyword.Null;
        if (UI.Find(window, UiSelector.Name(VanillaUi.Elements.EquipmentPicker)) is { } picker
            && UI.Find(picker, UiSelector.Name(VanillaUi.Elements.Row)) is { } filters)
            filters.style.borderBottomWidth = StyleKeyword.Null;
    }

    // Every label Recolour and DriveSquads give a colour.
    private static readonly string[] RecolouredLabels =
    {
        VanillaUi.Elements.SlotHeaderText, VanillaUi.Elements.ItemName, VanillaUi.Elements.ShortItemName,
        VanillaUi.Elements.UnitTypeHeadline, VanillaUi.Elements.UnitTypeName, VanillaUi.Elements.SquaddiesCount,
        VanillaUi.Elements.PickerTitle, VanillaUi.Elements.PickerFilterLabel, VanillaUi.Elements.PickerSortLabel,
    };

    private static void Unedge(VisualElement element)
    {
        if (element == null)
            return;
        var s = element.style;
        s.borderLeftColor = StyleKeyword.Null;
        s.borderTopColor = StyleKeyword.Null;
        s.borderRightColor = StyleKeyword.Null;
        s.borderBottomColor = StyleKeyword.Null;
    }

    // The four stats beside the portrait are drawn over it: vanilla draws the portrait last, and
    // a doll's picture is wider than vanilla's leaders'. They are moved to the end of the row the
    // portrait is in, at the place they had, once that place has been laid out.
    private void RaiseStats()
    {
        if (_squadWindow?.panel == null)
            return;
        // Asked every frame: the two are looked for once a window, and again only if they
        // have left it.
        if (_statsWindow != _squadWindow || _statsRaised?.panel == null || _statsColumn?.panel == null)
        {
            _statsWindow = _squadWindow;
            _statsRaised = UI.Find(_squadWindow, UiSelector.Name(VanillaUi.Elements.AdditionalStats));
            _statsColumn = UI.Find(_squadWindow, UiSelector.Name(VanillaUi.Elements.StatsColumn));
        }
        var (stats, column) = (_statsRaised, _statsColumn);
        var row = column?.parent;
        if (stats == null || row == null || stats.parent != column)
            return;
        var (at, origin) = (stats.worldBound, row.worldBound);
        if (!(at.width > 1f) || !(origin.width > 1f))
            return;
        MoveInto(row, stats);
        Inline(stats, e =>
        {
            var s = e.style;
            s.left = at.x - origin.x;
            s.top = at.y - origin.y;
            s.right = StyleKeyword.Auto;
            s.bottom = StyleKeyword.Auto;
            s.width = at.width;
        }, e =>
        {
            var s = e.style;
            s.left = StyleKeyword.Null;
            s.top = StyleKeyword.Null;
            s.right = StyleKeyword.Null;
            s.bottom = StyleKeyword.Null;
            s.width = StyleKeyword.Null;
        });
    }

    private VisualElement _statsWindow, _statsRaised, _statsColumn;

    private const string PickerScrollWatchedClass = "wm-picker-scroll-watched";
    private bool _pickerScrollPress;

    // Vanilla shuts the equipment picker when a press ends off it. A drag of its scroll bar
    // drifts off the picker's edge, which is beside the bar, and let go there would shut it. A
    // press that began on the scroll bar is noted here, and the screen is not told of its end.
    private void WatchPickerScroll(VisualElement picker)
    {
        if (picker.ClassListContains(PickerScrollWatchedClass))
            return;
        picker.AddToClassList(PickerScrollWatchedClass);
        picker.RegisterCallback((EventCallback<PointerDownEvent>)(evt =>
        {
            _pickerScrollPress = false;
            // Mission preparation has this panel too, and its screen's press end is not
            // caught: a press noted there would wait for the armoury's next one and stop that.
            if (_policyScreen != ArmoryScreen)
                return;
            for (var at = evt.target?.TryCast<VisualElement>(); at != null && at != picker; at = at.parent)
                if (at.TryCast<Scroller>() != null)
                {
                    _pickerScrollPress = true;
                    break;
                }
        }), TrickleDown.TrickleDown);
    }

    private void HookPickerScroll()
    {
        var target = VanillaUi.Targets.ArmoryPressEnd;
        Context.Patches.Prefix(target.Type, target.Method, info =>
        {
            if (!_pickerScrollPress)
                return;
            _pickerScrollPress = false;
            info.Skip = true;
        });
    }

    private VisualElement _squadWindow;
    private bool _squadPanelInPrep;
    private int _squadPickerTick;
    // Whether the equipment picker is open over the room, as last looked.
    private bool _squadPickerOpen;

    // Vanilla fills its equipment picker when a slot is clicked, which refreshes nothing. While a
    // picker is open its cards' names are given the panel's colours every few frames.
    private void DriveSquads()
    {
        RaiseStats();
        if (_squadWindow == null || _squadWindow.panel == null || _shellRoot == null || !_shellRoot.ClassListContains(SquadsClass))
        {
            _squadPickerOpen = false;
            return;
        }
        if (++_squadPickerTick % 6 != 0)
            return;
        var picker = UI.Find(_squadWindow, UiSelector.Name(VanillaUi.Elements.EquipmentPicker));
        _squadPickerOpen = picker != null && picker.resolvedStyle.display != DisplayStyle.None && picker.resolvedStyle.visibility != Visibility.Hidden;
        if (!_squadPickerOpen)
            return;
        Tint(picker, VanillaUi.Elements.ItemName, SquadInk);
        Tint(picker, VanillaUi.Elements.ShortItemName, SquadBlue);
        Tint(picker, VanillaUi.Elements.PickerTitle, SquadInk);
        Tint(picker, VanillaUi.Elements.PickerFilterLabel, SquadBlue);
        Tint(picker, VanillaUi.Elements.PickerSortLabel, SquadBlue);
        // No rule under the picker's row of filters, as the panel's header has none.
        if (UI.Find(picker, UiSelector.Name(VanillaUi.Elements.Row)) is { } filters)
            filters.style.borderBottomWidth = 0;
    }

    private static void Tint(VisualElement under, string name, UnityEngine.Color colour)
    {
        foreach (var label in UI.FindAll(under, UiSelector.Name(name)))
            label.style.color = colour;
    }

    private const string PromotableName = "wm-promotable";

    // A gold chip on each roster tile whose doll has a perk to take and the points for it.
    // Vanilla says so only by pulsing the perks of the doll that is open.
    private void MarkPromotable(VisualElement window)
    {
        var living = _shellRoot != null ? UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.UnitsContainer)) : null;
        if (living == null)
            return;
        var points = StrategyState.Get()?.GetVar(StrategyVars.PromotionPoints) ?? 0;
        for (var i = 0; i < living.childCount; i++)
        {
            var slot = living.ElementAt(i);
            var leader = slot.TryCast<Il2CppMenace.UI.Strategy.BaseUnitSelectSlot>()?.GetLeader();
            var chip = UI.Find(slot, UiSelector.Name(PromotableName));
            var can = leader != null && leader.IsAlive() && leader.CanBePromoted() && leader.GetPromotionCost() <= points;
            if (!can)
            {
                chip?.RemoveFromHierarchy();
                continue;
            }
            if (chip != null || slot.childCount == 0)
                continue;
            chip = new VisualElement { name = PromotableName, pickingMode = PickingMode.Ignore };
            chip.AddToClassList("wm-squads__promotable");
            var mark = new VisualElement { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("wm-squads__promotable-mark");
            // Gold on the elements themselves: the tile's own rules reach its children first.
            mark.style.unityBackgroundImageTintColor = Wui.Gold;
            Border(chip.style, 0.8f, Wui.Gold);
            chip.Add(mark);
            slot.ElementAt(0).Add(chip);
        }
    }

    // The form swap and the gift button arrive after vanilla's deployment button. They stand
    // ahead of Dismiss.
    private static void KeepInHeader(VisualElement window, VisualElement before)
    {
        var header = before.parent;
        if (header == null)
            return;
        foreach (var name in HeaderButtons)
            if (UI.Find(window, UiSelector.Name(name)) is { } button && button.parent != header)
                header.Insert(header.IndexOf(before), button);
    }

    private const float HeaderButtonHeight = 24f;
    private static readonly string[] HeaderButtons = { "wm-formswap", "gift-open" };

    // Vanilla sets the window's leader and refreshes it on every pick and change.
    private void HookSquadPanel()
    {
        // A gift changes the level without vanilla refreshing the window.
        _squadAffinityChanged = window =>
        {
            if (_shellRoot != null && _shellRoot.ClassListContains(SquadsClass))
                RefreshSquadPanel(window);
        };
        Affinity.Changed += _squadAffinityChanged;
        foreach (var method in new[] { "SetLeader", "Refresh" })
            Context.Patches.Postfix(VanillaUi.Targets.UnitWindowType, method, info =>
            {
                if (info.Instance is VisualElement window && _shellRoot != null && _shellRoot.ClassListContains(SquadsClass))
                    RefreshSquadPanel(window);
            });
    }

    private Action<VisualElement> _squadAffinityChanged;

    private const string PortraitBoxName = "wm-portrait-box";
    private const float VanillaPortraitHeight = 325.6f;
    private const float VanillaPortraitBarsTop = 251.2f;

    // The header reads name first, then rank and unit on one small line, as the roster's other
    // text does. Vanilla keeps the rank beside the name, so the rank's label moves down a row.
    private void LayOutSquadHeader(VisualElement frame)
    {
        var rank = UI.Find(frame, UiSelector.Name(VanillaUi.Elements.LeaderRank));
        var unit = UI.Find(frame, UiSelector.Name(VanillaUi.Elements.UnitName));
        // Sizes and colours on the labels themselves: vanilla's own rules for them outrank shell.uss.
        HeaderText(UI.Find(frame, UiSelector.Name(VanillaUi.Elements.LeaderName)), 16f, UnityEngine.Color.white);
        HeaderText(rank, SquadMetaSize, SquadMetaInk);
        HeaderText(unit, SquadMetaSize, SquadMetaInk);
        if (rank == null || unit == null || unit.parent == null || rank.parent == unit.parent)
            return;
        var from = rank.parent;
        var index = from.IndexOf(rank);
        var to = unit.parent;
        var divider = new Label("//");
        divider.AddToClassList("wm-squads__divider");
        divider.style.fontSize = SquadMetaSize;
        divider.style.color = SquadMetaInk;
        // The rank's insignia goes with it, at the small line's size, and the name starts the row.
        var insignia = UI.Find(from, UiSelector.Name(VanillaUi.Elements.RankIcon));
        var insigniaIndex = insignia != null ? from.IndexOf(insignia) : 0;
        if (insignia != null)
        {
            to.Insert(to.IndexOf(unit), insignia);
            insignia.style.width = RankInsigniaSize;
            insignia.style.height = RankInsigniaSize;
            insignia.style.marginRight = 3.2f;
            // The picture has blank space round it, so it is pulled left to start under the name.
            insignia.style.marginLeft = -4f;
        }
        to.Insert(to.IndexOf(unit), rank);
        to.Insert(to.IndexOf(unit), divider);
        _shellUndo.Add(() =>
        {
            divider.RemoveFromHierarchy();
            if (from == null)
                return;
            if (insignia == null)
            {
                from.Insert(Math.Min(index, from.childCount), rank);
                return;
            }
            insignia.style.width = StyleKeyword.Null;
            insignia.style.height = StyleKeyword.Null;
            insignia.style.marginRight = StyleKeyword.Null;
            insignia.style.marginLeft = StyleKeyword.Null;
            // The one that stood first goes back first, so the other's place counts it.
            var first = insigniaIndex < index ? insignia : rank;
            var second = insigniaIndex < index ? rank : insignia;
            from.Insert(Math.Clamp(Math.Min(index, insigniaIndex), 0, from.childCount), first);
            from.Insert(Math.Min(Math.Max(index, insigniaIndex), from.childCount), second);
        });
    }
}
