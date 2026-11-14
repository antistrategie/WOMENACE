using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Mission preparation: the roster at the left (one column, widened to three from the rail beside
// it), the mission's facts as chips and its objectives in a box over the table's far corner, the
// map on the holotable in the middle (PrepMap, PrepTokens, PrepInput), and the squad panel at the right as on Squads. The roster,
// the objectives box and the panel are vanilla's own, left wired as they are.
public sealed partial class StrategyDiorama
{
    private const string PrepClass = "wm-prep";
    private const string PrepRosterName = "wm-prep-roster";
    private const string PrepCountName = "wm-prep-count";
    private const string PrepRailName = "wm-prep-rail";
    private const string PrepChipsName = "wm-prep-chips";

    // Vanilla's roster tile with its margin, and the roster's widths in tiles.
    private const float PrepTile = 57.6f;
    private const int PrepColumnsWide = 3;
    private const float PrepScrollerWidth = 7.2f;
    // The count's box is the chips' height.
    private const float PrepChipHeight = 24f;
    private const float PrepGap = 6.4f;
    private const float PrepRailWidth = 9.6f;
    // Between the roster's scrollbar and the rail.
    private const float PrepRailGap = 3.2f;
    private const float PrepInfoWidth = 210f;
    private bool _prepRosterWide;
    private string _prepChipsOf;
    private int _prepTick;

    private void LayOutPrep(VisualElement root)
    {
        foreach (var name in new[] { SquadsClass, PrepClass })
        {
            if (root.ClassListContains(name))
                continue;
            root.AddToClassList(name);
            _shellUndo.Add(() => root.RemoveFromClassList(name));
        }
        var row = UI.Find(root, UiSelector.Name(VanillaUi.Elements.MapColumn))?.parent;
        if (row == null)
            return;
        LayOutPrepRoster(row);
        LayOutPrepInfo(row);
        LayOutPrepFlatMap(row);
        LayOutPrepTokens(row);
        LayOutPrepAbilities(root, row);
        LayOutPrepSupplies(root);

        _squadPanelInPrep = true;
        var column = UI.Find(row, UiSelector.Name(VanillaUi.Elements.PrepSelectedColumn));
        Inline(column, e =>
        {
            var s = e.style;
            s.position = Position.Absolute;
            s.left = SquadPanelLeft;
            s.top = SquadsTop;
            s.width = SquadPanelWidth;
            s.height = SquadsBottom - SquadsTop;
            s.marginTop = 0;
            s.marginBottom = 0;
        }, e =>
        {
            var s = e.style;
            s.marginTop = StyleKeyword.Null;
            s.marginBottom = StyleKeyword.Null;
            s.position = StyleKeyword.Null;
            s.left = StyleKeyword.Null;
            s.top = StyleKeyword.Null;
            s.width = StyleKeyword.Null;
            s.height = StyleKeyword.Null;
        });
        LayOutSquadPanel(column != null ? UI.Find(column, UiSelector.TypeName(VanillaUi.Elements.UnitWindow)) : null);
        RefreshPrep(row);
    }

    // Vanilla's roster is a fixed block of tiles two across. Here it is one column that scrolls,
    // under a count of the squads deployed, and the rail at its right widens it to three.
    private void LayOutPrepRoster(VisualElement row)
    {
        var tiles = UI.Find(row, UiSelector.Name(VanillaUi.Elements.UnitsContainer));
        if (tiles == null)
            return;
        var columns = _prepRosterWide ? PrepColumnsWide : 1;
        var width = columns * PrepTile;
        var top = SquadsTop + PrepChipHeight + PrepGap;

        var scroll = UI.Find(row, UiSelector.Name(PrepRosterName));
        if (scroll == null)
        {
            var (home, index) = (tiles.parent, tiles.parent.IndexOf(tiles));
            scroll = Scrolling(tiles);
            scroll.name = PrepRosterName;
            // The tiles never run wider than the list, so nothing scrolls sideways.
            if (scroll.TryCast<ScrollView>() is { } view)
                view.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.position = Position.Absolute;
            row.Insert(row.IndexOf(home), scroll);
            var made = scroll;
            _shellUndo.Add(() =>
            {
                home.Insert(Math.Min(index, home.childCount), tiles);
                made.RemoveFromHierarchy();
                var s = tiles.style;
                s.alignSelf = StyleKeyword.Null;
                s.flexShrink = StyleKeyword.Null;
                s.width = StyleKeyword.Null;
                s.height = StyleKeyword.Null;
                s.flexWrap = StyleKeyword.Null;
                s.marginTop = StyleKeyword.Null;
            });
        }
        scroll.style.left = RosterLeft;
        scroll.style.top = top;
        scroll.style.width = width + PrepScrollerWidth;
        scroll.style.height = SquadsBottom - top;
        tiles.style.width = width;
        tiles.style.height = StyleKeyword.Auto;
        tiles.style.flexWrap = Wrap.Wrap;
        tiles.style.alignSelf = Align.FlexStart;
        // Vanilla starts its block of tiles below its own header.
        tiles.style.marginTop = 0;

        var count = UI.Find(row, UiSelector.Name(PrepCountName));
        if (count == null)
        {
            count = new Label { name = PrepCountName, pickingMode = PickingMode.Ignore };
            count.AddToClassList("wm-prep__count");
            row.Insert(row.IndexOf(scroll), count);
            _shellUndo.Add(count.RemoveFromHierarchy);
        }
        count.style.left = RosterLeft + RosterTileInset;
        count.style.top = SquadsTop;
        count.style.width = width - RosterTileInset;
        count.style.height = PrepChipHeight;

        var rail = UI.Find(row, UiSelector.Name(PrepRailName));
        if (rail == null)
        {
            rail = new VisualElement { name = PrepRailName };
            rail.AddToClassList("wm-prep__rail");
            var chevron = new VisualElement { pickingMode = PickingMode.Ignore };
            chevron.AddToClassList("wm-prep__rail-chevron");
            rail.Add(chevron);
            rail.RegisterCallback((EventCallback<ClickEvent>)(_ =>
            {
                Sound.Click();
                _prepRosterWide = !_prepRosterWide;
                LayOutPrepRoster(row);
                LayOutPrepInfo(row);
            }));
            row.Insert(row.IndexOf(scroll), rail);
            _shellUndo.Add(rail.RemoveFromHierarchy);
        }
        rail.style.left = RosterLeft + width + PrepScrollerWidth + PrepRailGap;
        rail.style.top = SquadsTop;
        rail.style.height = SquadsBottom - SquadsTop;
        rail.EnableInClassList("wm-prep__rail--open", _prepRosterWide);
    }

    // Where the middle space starts: past the roster and its rail. The widened roster opens over
    // it, so the chips and the objectives stay where they are.
    private static float PrepMiddleLeft => RosterLeft + PrepTile + PrepScrollerWidth + PrepRailGap + PrepRailWidth + PrepGap;

    // Vanilla's details and objectives box, out of the map's column: the details become chips in
    // a row, and the box under them holds the objectives.
    private void LayOutPrepInfo(VisualElement row)
    {
        var chips = UI.Find(row, UiSelector.Name(PrepChipsName));
        if (chips == null)
        {
            chips = new VisualElement { name = PrepChipsName, pickingMode = PickingMode.Ignore };
            chips.AddToClassList("wm-prep__chips");
            row.Insert(0, chips);
            _shellUndo.Add(() =>
            {
                chips.RemoveFromHierarchy();
                _prepChipsOf = null;
            });
        }
        chips.style.left = PrepMiddleLeft;
        chips.style.top = SquadsTop;
        chips.style.height = PrepChipHeight;

        var info = UI.Find(row, UiSelector.TypeName(VanillaUi.Elements.PrepInfoOverlay));
        if (info == null)
            return;
        if (info.parent != row)
        {
            var (home, index) = (info.parent, info.parent.IndexOf(info));
            row.Insert(0, info);
            _shellUndo.Add(() => home.Insert(Math.Min(index, home.childCount), info));
        }
        Inline(info, e =>
        {
            var s = e.style;
            s.position = Position.Absolute;
            s.left = PrepMiddleLeft;
            s.top = SquadsTop + PrepChipHeight + PrepGap;
            s.width = PrepInfoWidth;
            s.marginLeft = 0;
            s.marginTop = 0;
        }, e =>
        {
            var s = e.style;
            s.position = StyleKeyword.Null;
            s.left = StyleKeyword.Null;
            s.top = StyleKeyword.Null;
            s.width = StyleKeyword.Null;
            s.marginLeft = StyleKeyword.Null;
            s.marginTop = StyleKeyword.Null;
        });
        // On the elements: vanilla's own rules for them outrank shell.uss.
        Inline(UI.Find(info, UiSelector.Name(VanillaUi.Elements.PrepInfoContainer)), e =>
        {
            e.style.backgroundImage = StyleKeyword.None;
            e.style.backgroundColor = SquadPanelFill;
            Border(e.style, 0.64f, SquadPanelLine);
            e.style.paddingTop = PrepGap;
            e.style.paddingBottom = PrepGap;
            // Folded, vanilla gives the box the heading's own height, which the padding would
            // push the heading out of: it is never shorter than the heading and its padding.
            e.style.minHeight = PrepFoldedHeight;
        }, e =>
        {
            e.style.minHeight = StyleKeyword.Null;
            e.style.paddingTop = StyleKeyword.Null;
            e.style.paddingBottom = StyleKeyword.Null;
            e.style.backgroundImage = StyleKeyword.Null;
            e.style.backgroundColor = StyleKeyword.Null;
            Border(e.style, null, null);
        });
        HideInline(UI.Find(info, UiSelector.Name(VanillaUi.Elements.PrepInfoBorder)));
        // The fold arrow lies over the whole box from its top edge. The box's own padding moved
        // the heading down, so the arrow comes down with it to stay level.
        Inline(UI.Find(info, UiSelector.Name(VanillaUi.Elements.PrepInfoFold)), e => e.style.top = PrepGap, e => e.style.top = StyleKeyword.Null);
        // The list's rows are restyled on every refresh (RefreshPrep). They go back with the shell.
        Inline(UI.Find(info, UiSelector.Name(VanillaUi.Elements.PrepObjectives)), _ => { }, e =>
        {
            e.style.borderLeftWidth = StyleKeyword.Null;
            e.style.marginLeft = StyleKeyword.Null;
            e.style.paddingLeft = StyleKeyword.Null;
            for (var i = 0; i < e.childCount; i++)
            {
                if (UI.Find(e.ElementAt(i), UiSelector.TypeName("Label")) is { } words)
                    words.style.color = StyleKeyword.Null;
                if (UI.Find(e.ElementAt(i), UiSelector.Name(VanillaUi.Elements.PrepObjectiveMark)) is { } mark)
                    mark.style.unityBackgroundImageTintColor = StyleKeyword.Null;
            }
        });
        foreach (var name in new[] { VanillaUi.Elements.PrepDetailsHeadline, VanillaUi.Elements.PrepDetails })
            Inline(UI.Find(info, UiSelector.Name(name)), e => e.style.display = DisplayStyle.None, e => e.style.display = StyleKeyword.Null);
        Inline(UI.Find(info, UiSelector.Name(VanillaUi.Elements.PrepObjectivesHeadline)), e =>
        {
            e.style.color = SquadBlue;
            e.style.fontSize = 8f;
            e.style.letterSpacing = 0.96f;
        }, e =>
        {
            e.style.color = StyleKeyword.Null;
            e.style.fontSize = StyleKeyword.Null;
            e.style.letterSpacing = StyleKeyword.Null;
        });
    }

    private const string PrepSuppliesLeftName = "wm-prep-supplies-left";
    private const string PrepSuppliesUsesName = "wm-prep-supplies-uses";
    // Vanilla sizes the bar's parts for a bar this wide, so the panel is exactly that.
    private const float PrepSuppliesWidth = 368f;
    private const float PrepSuppliesHeight = 46f;
    private const float PrepSuppliesBarTop = 19f;
    private const float PrepSuppliesBarHeight = 8f;

    // Vanilla's loadout box on the bottom bar, without its frame: a line of words and numbers,
    // the bar under it, and under that what the open squad uses, which vanilla prints on the bar.
    private void LayOutPrepSupplies(VisualElement root)
    {
        var panel = UI.Find(root, UiSelector.Name(VanillaUi.Elements.SuppliesPanel));
        if (panel == null || panel.parent != root)
            return;
        Inline(panel, e =>
        {
            var s = e.style;
            s.width = PrepSuppliesWidth;
            s.marginLeft = -PrepSuppliesWidth / 2f;
            s.height = PrepSuppliesHeight;
            s.bottom = (BarHeight - PrepSuppliesHeight) / 2f;
        }, e =>
        {
            var s = e.style;
            s.height = StyleKeyword.Null;
        });
        foreach (var name in new[] { VanillaUi.Elements.PrepInfoBorder, VanillaUi.Elements.SuppliesIcon, VanillaUi.Elements.SuppliesCaption,
                     VanillaUi.Elements.SuppliesBarBacking, VanillaUi.Elements.SuppliesStripes })
            Inline(UI.Find(panel, UiSelector.Name(name)), e => e.style.display = DisplayStyle.None, e => e.style.display = StyleKeyword.Null);
        void Put(string name, float? left, float? right, float top, Action<IStyle> more = null)
        {
            Inline(UI.Find(panel, UiSelector.Name(name)), e =>
            {
                var s = e.style;
                s.position = Position.Absolute;
                s.left = left is { } l ? l : StyleKeyword.Auto;
                s.right = right is { } r ? r : StyleKeyword.Auto;
                s.top = top;
                s.bottom = StyleKeyword.Auto;
                s.marginLeft = s.marginRight = s.marginTop = s.marginBottom = 0;
                s.paddingLeft = s.paddingRight = s.paddingTop = s.paddingBottom = 0;
                more?.Invoke(s);
            }, e =>
            {
                var s = e.style;
                s.position = StyleKeyword.Null;
                s.left = s.right = s.top = s.bottom = StyleKeyword.Null;
                s.marginLeft = s.marginRight = s.marginTop = s.marginBottom = StyleKeyword.Null;
                s.paddingLeft = s.paddingRight = s.paddingTop = s.paddingBottom = StyleKeyword.Null;
                s.fontSize = StyleKeyword.Null;
                s.color = StyleKeyword.Null;
                s.letterSpacing = StyleKeyword.Null;
                s.width = StyleKeyword.Null;
                s.height = StyleKeyword.Null;
                s.backgroundColor = StyleKeyword.Null;
            });
        }
        Put(VanillaUi.Elements.SuppliesLoadout, 0f, null, 2.2f, s =>
        {
            s.fontSize = 8f;
            s.letterSpacing = 0.64f;
            s.color = SquadMetaInk;
        });
        Put(VanillaUi.Elements.SuppliesDelta, null, 26f, 0f, s => s.fontSize = 10.4f);
        Put(VanillaUi.Elements.SuppliesValue, null, 62f, 0f, s => s.fontSize = 10.4f);
        Put(VanillaUi.Elements.SuppliesBar, 0f, null, PrepSuppliesBarTop, s =>
        {
            s.width = PrepSuppliesWidth;
            s.height = PrepSuppliesBarHeight;
            s.backgroundColor = new UnityEngine.Color(34f / 255f, 39f / 255f, 46f / 255f);
        });
        // The open squad's figure is printed on the bar, which is too thin for it here. Vanilla
        // shows and hides it by its display, so it is put out of sight another way.
        Inline(UI.Find(panel, UiSelector.Name(VanillaUi.Elements.SuppliesSelected)), e => e.style.opacity = 0f, e => e.style.opacity = StyleKeyword.Null);
        foreach (var (name, style, left, right, top) in new[]
                 {
                     (PrepSuppliesLeftName, "wm-prep__supplies-left", (float?)null, (float?)0f, 1.6f),
                     (PrepSuppliesUsesName, "wm-prep__supplies-uses", (float?)0f, (float?)null, PrepSuppliesBarTop + PrepSuppliesBarHeight + 4.8f),
                 })
        {
            if (UI.Find(panel, UiSelector.Name(name)) != null)
                continue;
            var label = new Label { name = name, pickingMode = PickingMode.Ignore };
            label.AddToClassList(style);
            label.style.left = left is { } l ? l : StyleKeyword.Auto;
            label.style.right = right is { } r ? r : StyleKeyword.Auto;
            label.style.top = top;
            panel.Add(label);
            _shellUndo.Add(label.RemoveFromHierarchy);
        }
        if (UI.Find(panel, UiSelector.Name(PrepSuppliesLeftName))?.TryCast<Label>() is { } leftWord)
            leftWord.text = Locale.Text("WOMENACE::ui/prep/left", "LEFT");
    }

    // Vanilla's red for a loadout over its limit.
    private static readonly UnityEngine.Color PrepOverInk = new(179f / 255f, 48f / 255f, 53f / 255f);
    // A secondary objective's words: the shell's body text, a little under a primary's white.
    private static readonly UnityEngine.Color PrepSecondaryInk = new(225f / 255f, 225f / 255f, 225f / 255f);

    private const float PrepFoldedHeight = 28.8f;
    private const string PrepAbilitiesCaptionName = "wm-prep-abilities-caption";
    private const float PrepAbilitiesRight = 56f;
    private const float PrepAbilitiesTop = 20f;

    // Vanilla's off-map abilities sit over the unit window. Here they are on the top bar's right,
    // under a caption, each a square with its uses in the corner. The panel is put over the shell,
    // as the loadout bar is, so its squares take the pointer.
    private void LayOutPrepAbilities(VisualElement root, VisualElement row)
    {
        var panel = UI.Find(root, UiSelector.Name(VanillaUi.Elements.PrepAbilitiesPanel));
        if (panel == null)
            return;
        if (panel.parent == root)
        {
            panel.BringToFront();
            return;
        }
        var (home, index) = (panel.parent, panel.parent.IndexOf(panel));
        root.Add(panel);
        var caption = new Label(Locale.Text("WOMENACE::ui/prep/abilities", "O.C.I. ABILITIES")) { name = PrepAbilitiesCaptionName, pickingMode = PickingMode.Ignore };
        caption.AddToClassList("wm-prep__abilities-caption");
        panel.Insert(0, caption);
        var s = panel.style;
        s.position = Position.Absolute;
        s.right = PrepAbilitiesRight;
        s.top = PrepAbilitiesTop;
        s.left = StyleKeyword.Auto;
        s.width = StyleKeyword.Auto;
        s.height = StyleKeyword.Auto;
        s.alignItems = Align.FlexEnd;
        HideInline(UI.Find(panel, UiSelector.Name(VanillaUi.Elements.PrepInfoBorder)));
        _shellUndo.Add(() =>
        {
            caption.RemoveFromHierarchy();
            s.position = StyleKeyword.Null;
            s.right = StyleKeyword.Null;
            s.top = StyleKeyword.Null;
            s.left = StyleKeyword.Null;
            s.width = StyleKeyword.Null;
            s.height = StyleKeyword.Null;
            s.alignItems = StyleKeyword.Null;
            home.Insert(Math.Min(index, home.childCount), panel);
        });
    }

    // Vanilla's flat map is not seen. It stays in the row, under everything else of the screen,
    // as the surface the pointer acts on: DrivePrepInput keeps it under the pointer so that the
    // tile the pointer is over on the hologram is the tile it is over on that map.
    private void LayOutPrepFlatMap(VisualElement row)
    {
        var column = UI.Find(row, UiSelector.Name(VanillaUi.Elements.MapColumn));
        if (column == null)
            return;
        var picking = column.pickingMode;
        Inline(column, e =>
        {
            var s = e.style;
            // Out of the row, which centres what is left in it.
            s.position = Position.Absolute;
            s.left = PrepFlatMapParked;
            s.top = 0;
            s.opacity = _prepFlatMapShown ? 0.55f : 0f;
            // The column itself takes no pointer: only the map in it does.
            e.pickingMode = PickingMode.Ignore;
        }, e =>
        {
            var s = e.style;
            s.position = StyleKeyword.Null;
            s.left = StyleKeyword.Null;
            s.top = StyleKeyword.Null;
            s.opacity = StyleKeyword.Null;
            e.pickingMode = picking;
        });
        column.SendToBack();
    }

    // Vanilla changes who is deployed and what the mission's details say without telling anyone,
    // so the count and the chips are read again a few times a second.
    private void DrivePrep()
    {
        if (_shellRoot == null || _policyScreen != VanillaUi.Screens.MissionPrep || !_shellRoot.ClassListContains(PrepClass))
            return;
        if (++_prepTick % 12 != 0)
            return;
        var row = UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.MapColumn))?.parent;
        if (row != null)
            RefreshPrep(row);
    }

    private void RefreshPrep(VisualElement row)
    {
        try
        {
            // The roster's scrollbar shows only while there is more roster than fits.
            if (UI.Find(row, UiSelector.Name(PrepRosterName))?.TryCast<ScrollView>() is { } roster)
            {
                var more = roster.contentContainer.layout.height > roster.contentViewport.layout.height + 1f;
                var bar = more ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
                if (roster.verticalScrollerVisibility != bar)
                    roster.verticalScrollerVisibility = bar;
            }
            if (UI.Find(row, UiSelector.Name(PrepCountName))?.TryCast<Label>() is { } count
                && UI.Find(row, UiSelector.Name(VanillaUi.Elements.UnitsContainer)) is { } tiles)
            {
                var (squads, deployed) = (0, 0);
                foreach (var slot in UI.FindAll(tiles, UiSelector.TypeName(VanillaUi.Elements.PrepUnitSlot)))
                {
                    squads++;
                    var mark = UI.Find(slot, UiSelector.Name(VanillaUi.Elements.PrepDeployedIcon));
                    if (mark != null && mark.resolvedStyle.display != DisplayStyle.None)
                        deployed++;
                }
                var text = $"{deployed} / {squads}";
                if (count.text != text)
                    count.text = text;
            }

            // The abilities' caption shows only while there are abilities under it.
            if (UI.Find(_shellRoot, UiSelector.Name(PrepAbilitiesCaptionName)) is { } caption
                && UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.PrepAbilities)) is { } abilities)
                caption.style.display = abilities.childCount > 0 && abilities.resolvedStyle.display != DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
            // An ability's uses, small and gold in its square's corner, on the label itself: vanilla's
            // rule for it outranks shell.uss.
            if (UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.PrepAbilities)) is { } squares)
                foreach (var uses in UI.FindAll(squares, UiSelector.Name(VanillaUi.Elements.PrepAbilityUses)))
                {
                    uses.style.fontSize = 8.8f;
                    uses.style.color = Wui.Gold;
                    uses.style.backgroundColor = UnityEngine.Color.clear;
                }

            // Vanilla makes the objectives' rows again as it fills them, in its own colours. Here a
            // primary objective (vanilla's bold) is white with a gold mark, the others quieter,
            // and the list has no rule down its left.
            if (UI.Find(row, UiSelector.Name(VanillaUi.Elements.PrepObjectives)) is { } objectives)
            {
                objectives.style.borderLeftWidth = 0;
                // The points start under the heading's first letter, not set in from it.
                objectives.style.marginLeft = 0;
                objectives.style.paddingLeft = 0;
                for (var i = 0; i < objectives.childCount; i++)
                {
                    var objective = objectives.ElementAt(i);
                    var words = UI.Find(objective, UiSelector.TypeName("Label"));
                    if (words == null)
                        continue;
                    var primary = words.resolvedStyle.unityFontStyleAndWeight is UnityEngine.FontStyle.Bold or UnityEngine.FontStyle.BoldAndItalic;
                    words.style.color = primary ? SquadInk : PrepSecondaryInk;
                    if (UI.Find(objective, UiSelector.Name(VanillaUi.Elements.PrepObjectiveMark)) is { } mark)
                        mark.style.unityBackgroundImageTintColor = primary ? Wui.Gold : SquadBlue;
                }
            }

            // The word after what is left is red with vanilla's figure once the loadout is over.
            if (UI.Find(_shellRoot, UiSelector.Name(PrepSuppliesLeftName)) is { } leftWord
                && UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.SuppliesDelta))?.TryCast<Label>() is { } delta)
                leftWord.style.color = (delta.text ?? "").Contains('-') ? PrepOverInk : StyleKeyword.Null;

            // What the open squad uses, from the figure vanilla prints on the bar.
            if (UI.Find(_shellRoot, UiSelector.Name(PrepSuppliesUsesName))?.TryCast<Label>() is { } usesLine
                && UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.SuppliesSelected))?.TryCast<Label>() is { } figure)
            {
                var who = _squadWindow != null ? UI.Find(_squadWindow, UiSelector.Name(VanillaUi.Elements.LeaderName))?.TryCast<Label>()?.text?.Trim() : null;
                var line = string.IsNullOrEmpty(figure.text) || string.IsNullOrEmpty(who) ? ""
                    : Locale.Format("WOMENACE::ui/prep/uses", "{0} USES <color=#EBC95D>{1}</color>", who.ToUpperInvariant(), figure.text);
                if (usesLine.text != line)
                    usesLine.text = line;
            }

            var chips = UI.Find(row, UiSelector.Name(PrepChipsName));
            if (chips == null)
                return;
            // Each of vanilla's detail rows reads "name: value", and the map's corner label ends
            // with the intelligence level in the same form.
            var facts = new List<string>();
            if (UI.Find(row, UiSelector.Name(VanillaUi.Elements.PrepDetails)) is { } details)
                foreach (var label in UI.FindAll(details, UiSelector.TypeName("Label")))
                    facts.Add(label.TryCast<Label>()?.text ?? "");
            var corner = UI.Find(row, UiSelector.Name(VanillaUi.Elements.PrepCoordinates))?.TryCast<Label>()?.text ?? "";
            facts.Add(corner.Split('\n').LastOrDefault(line => line.Contains(':')) ?? "");
            var signature = string.Join("|", facts);
            if (signature == _prepChipsOf)
                return;
            _prepChipsOf = signature;
            chips.Clear();
            foreach (var fact in facts)
            {
                var at = fact.IndexOf(':');
                if (at <= 0)
                    continue;
                var chip = new VisualElement();
                chip.AddToClassList("wm-prep__chip");
                var name = new Label(fact[..at].Trim().ToUpperInvariant());
                name.AddToClassList("wm-prep__chip-name");
                var value = new Label(fact[(at + 1)..].Trim().ToUpperInvariant());
                value.AddToClassList("wm-prep__chip-value");
                chip.Add(name);
                chip.Add(value);
                chips.Add(chip);
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: mission preparation refresh failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
