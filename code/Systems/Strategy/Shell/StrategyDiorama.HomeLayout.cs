using Il2CppMenace.UI;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Lays out the vanilla panels of a home screen's Operations view round the holotable:
//
//   - mission select boxes the operation's strip at the left and the selected mission's window
//     at the right, in the shell's style, and stands the missions on the table as cards
//
// Every change is an inline style on a vanilla element, undone before each re-apply and whenever the
// view is left, so vanilla's own USS is back in charge everywhere else.
public sealed partial class StrategyDiorama
{
    private readonly List<Action> _homeUndo = new();

    // Mission select: the mission panel's column and how far the markers shift to clear it.
    private const float MissionColumnWidth = 328f;
    private const float OperationsCornerTop = 70f;
    private const float OperationsCornerLeft = 32f;
    private const float MissionColumnRight = 32f;

    private void SetHomeLayout(bool home, string screenName)
    {
        foreach (var undo in _homeUndo)
        {
            try { undo(); }
            catch { }
        }
        _homeUndo.Clear();
        if (!home)
            return;
        var root = UIManager.Get()?.GetActiveScreen()?.m_Container?.rootVisualElement;
        if (root == null)
            return;

        // The conditions and assets strip clears the top bar.
        var corner = UI.Find(root, UiSelector.Name(VanillaUi.Elements.TopRightCorner));
        if (corner != null)
        {
            // At the top left, where it clears the mission panel as well.
            corner.style.top = OperationsCornerTop;
            corner.style.left = OperationsCornerLeft;
            corner.style.right = StyleKeyword.Auto;
            corner.style.alignItems = Align.FlexStart;
            _homeUndo.Add(() =>
            {
                corner.style.top = StyleKeyword.Null;
                corner.style.left = StyleKeyword.Null;
                corner.style.right = StyleKeyword.Null;
                corner.style.alignItems = StyleKeyword.Null;
            });
        }

        if (screenName == VanillaUi.Screens.MissionSelect)
            LayOutMissions(root, corner);
    }

    // Mission select round the holotable: the operation and its missions listed in a left column,
    // the selected mission in a right one, and each mission standing on the table as a marker.
    private void LayOutMissions(VisualElement root, VisualElement corner)
    {
        // The selected mission: vanilla's window as one box in the shell's style, at the right, its
        // top level with the operation box's and as tall as what it holds.
        var window = UI.Find(root, UiSelector.Name(VanillaUi.Elements.MissionWindow));
        if (window != null)
        {
            var windowWas = window.resolvedStyle.position;
            Dress(window, s =>
            {
                s.position = Position.Absolute;
                s.left = StyleKeyword.Auto;
                s.right = MissionColumnRight;
                s.top = OpsColumnTop;
                s.width = MissionColumnWidth;
                s.height = StyleKeyword.Auto;
                s.maxHeight = OpsColumnMaxHeight;
                s.overflow = Overflow.Hidden;
                s.backgroundColor = OpsBoxFill;
                Border(s, 0.8f, OpsBoxLine);
                Pad(s, OpsBoxPad);
            }, s =>
            {
                s.position = windowWas;
                s.left = StyleKeyword.Null;
                s.right = StyleKeyword.Null;
                s.top = StyleKeyword.Null;
                s.width = StyleKeyword.Null;
                s.height = StyleKeyword.Null;
                s.maxHeight = StyleKeyword.Null;
                s.overflow = StyleKeyword.Null;
                s.backgroundColor = StyleKeyword.Null;
                Border(s, null, null);
                Pad(s, null);
            });
            // The box has no heading line: the mission's name leads it.
            Dress(UI.Find(window, UiSelector.Name(VanillaUi.Elements.SelectedMissionCategory)),
                s => s.display = DisplayStyle.None, s => s.display = StyleKeyword.Null);
            // Its two parts carry their own black and insets: the box has both now.
            foreach (var name in new[] { VanillaUi.Elements.MissionWindowHeader, VanillaUi.Elements.MissionWindowBody })
                Dress(UI.Find(window, UiSelector.Name(name)), s =>
                {
                    s.backgroundColor = UnityEngine.Color.clear;
                    Border(s, 0f, null);
                    s.paddingLeft = 0;
                    s.paddingRight = 0;
                    s.height = StyleKeyword.Auto;
                    s.flexGrow = 0;
                }, s =>
                {
                    s.backgroundColor = StyleKeyword.Null;
                    Border(s, null, null);
                    s.paddingLeft = StyleKeyword.Null;
                    s.paddingRight = StyleKeyword.Null;
                    s.height = StyleKeyword.Null;
                    s.flexGrow = StyleKeyword.Null;
                });
        }

        // The operation's own strip becomes the left box: progress, planet, light and assets, each
        // a row across it without a backing of its own.
        if (corner != null)
        {
            BoxUp(corner, OpsColumnTop);
            DressOperationRows(corner, Dress);
        }

        // Vanilla's mission markers become the column's second box, a list. Where each stood on
        // vanilla's board is kept: the table's markers stand there.
        var board = UI.Find(root, UiSelector.Name(VanillaUi.Elements.MissionPois));
        if (board == null)
            return;
        // Its markers are only just placed on a first showing: their places are read once laid out.
        var unplaced = UI.FindAll(board, UiSelector.TypeName(VanillaUi.Elements.MissionPoi)).Any(p =>
            p.resolvedStyle.display != DisplayStyle.None && !_missions.MarkerBoard.ContainsKey(p.Pointer)
            && !(p.resolvedStyle.left > 0f || p.resolvedStyle.top > 0f));
        if (unplaced)
        {
            Context.Coroutines.Start(LayOutMissionsLater());
            return;
        }
        var shown = new List<Il2CppMenace.UI.Strategy.MissionPoi>();
        foreach (var element in UI.FindAll(board, UiSelector.TypeName(VanillaUi.Elements.MissionPoi)))
        {
            var poi = element.TryCast<Il2CppMenace.UI.Strategy.MissionPoi>();
            if (poi == null || poi.resolvedStyle.display == DisplayStyle.None)
                continue;
            RememberBoardPlace(poi);
            shown.Add(poi);
        }
        // The board leaves the screen for the cards' texture: its markers show on the table. Without
        // the quad the cards are made from, it stays where vanilla put it and missions stay pickable.
        if (MarkerTemplate() == null)
            return;
        var back = TakeBoardOffScreen(board, shown);
        if (back != null)
            _homeUndo.Add(back);
        ShowMarkers(shown);
        _homeUndo.Add(ClearMarkers);
    }

    private const float OpsColumnLeft = 32f;
    private const float OpsColumnTop = 80f;
    private const float OpsColumnWidth = 264f;
    private const float OpsProgressBarWidth = 70f;
    private const float LightIconsHeight = 14.4f;
    private const float FactionIconInset = -5f;
    // The side boxes, as the mockup draws them (its pixels at four fifths): the fill, the
    // hairline round it, the inset, and the most height the space between the bars allows.
    // The game blends its UI in linear light, where the mockup's 85% black lets far more of a
    // bright room through than it does on the mockup's canvas. This alpha looks as that one does.
    private static readonly UnityEngine.Color OpsBoxFill = new(0f, 0f, 0f, 0.975f);
    private static readonly UnityEngine.Color OpsBoxLine = new(50f / 255f, 60f / 255f, 74f / 255f);
    private const float OpsBoxPad = 13f;
    private const float OpsColumnMaxHeight = 503f;
    private const string OpsBoxClass = "wui-ops";

    private System.Collections.IEnumerator LayOutMissionsLater()
    {
        yield return null;
        yield return null;
        if (_shell != null)
            ApplyHome(_homeScreen, _homeRoomShown);
    }

    // Styles the operation's strip as rows of a box in the shell's style: progress, planet, light and
    // assets each a row across it without a backing of its own, under a small caption. Mission select
    // boxes it at the left of the table and O.C.I. puts it in the ship's panel, each taking the styles
    // off again its own way.
    private void DressOperationRows(VisualElement corner, Action<VisualElement, Action<IStyle>, Action<IStyle>> dress)
    {
        for (var i = 0; i < corner.childCount; i++)
            dress(corner.ElementAt(i), s =>
            {
                s.flexDirection = FlexDirection.Column;
                s.flexWrap = Wrap.NoWrap;
                s.alignItems = Align.Stretch;
                s.width = Length.Percent(100);
                s.marginTop = 0;
                s.marginBottom = 0;
            }, s =>
            {
                s.flexDirection = StyleKeyword.Null;
                s.flexWrap = StyleKeyword.Null;
                s.alignItems = StyleKeyword.Null;
                s.width = StyleKeyword.Null;
                s.marginTop = StyleKeyword.Null;
                s.marginBottom = StyleKeyword.Null;
            });
        foreach (var type in VanillaUi.Elements.OperationDisplays)
        {
            var display = UI.Find(corner, UiSelector.TypeName(type));
            dress(display, s =>
            {
                s.width = Length.Percent(100);
                s.marginLeft = 0;
                s.marginRight = 0;
                s.marginTop = 0;
                s.marginBottom = 5;
            }, s =>
            {
                s.width = StyleKeyword.Null;
                s.marginLeft = StyleKeyword.Null;
                s.marginRight = StyleKeyword.Null;
                s.marginTop = StyleKeyword.Null;
                s.marginBottom = StyleKeyword.Null;
            });
            // The planet's row keeps its own width: stretched, its icons stretch with it.
            var spread = type != VanillaUi.Elements.OperationPlanetDisplay;
            dress(display != null ? UI.Find(display, UiSelector.Name(VanillaUi.Elements.Pickable)) : null, s =>
            {
                s.backgroundColor = UnityEngine.Color.clear;
                s.paddingLeft = 0;
                s.paddingRight = 0;
                if (spread)
                {
                    s.width = Length.Percent(100);
                    s.justifyContent = Justify.SpaceBetween;
                }
                else
                    s.alignSelf = Align.FlexStart;
            }, s =>
            {
                s.backgroundColor = StyleKeyword.Null;
                s.width = StyleKeyword.Null;
                s.paddingLeft = StyleKeyword.Null;
                s.paddingRight = StyleKeyword.Null;
                s.justifyContent = StyleKeyword.Null;
                s.alignSelf = StyleKeyword.Null;
            });
            // A display's caption is the box's small label, on one line.
            foreach (var caption in display != null ? UI.FindAll(display, UiSelector.Type<Label>()).Where(l => VanillaUi.Elements.OperationCaptions.Contains(l.name) || string.IsNullOrEmpty(l.name)).ToList() : new List<VisualElement>())
                dress(caption, s =>
                {
                    s.fontSize = 10;
                    s.color = InkLabel;
                    s.whiteSpace = WhiteSpace.NoWrap;
                    s.width = StyleKeyword.Auto;
                    s.flexGrow = 1;
                }, s =>
                {
                    s.fontSize = StyleKeyword.Null;
                    s.color = StyleKeyword.Null;
                    s.whiteSpace = StyleKeyword.Null;
                    s.width = StyleKeyword.Null;
                    s.flexGrow = StyleKeyword.Null;
                });
        }
        // The planet leads the box, over the progress: vanilla's row holds them the other way round.
        if (corner.childCount > 0)
            dress(corner.ElementAt(0), s => s.flexDirection = FlexDirection.ColumnReverse, s => s.flexDirection = StyleKeyword.Null);
        // The light conditions' caption sits level with their row of icons, not with the row and
        // the pointer under it together.
        var light = UI.Find(corner, UiSelector.Name(VanillaUi.Elements.OperationCaptions[1]));
        var icons = UI.Find(corner, UiSelector.Name(VanillaUi.Elements.LightConditionIcons));
        dress(light, s =>
        {
            s.alignSelf = Align.FlexStart;
            s.height = icons != null && icons.layout.height > 1f ? icons.layout.height : LightIconsHeight;
            s.unityTextAlign = UnityEngine.TextAnchor.MiddleLeft;
            s.marginTop = 0;
            s.marginBottom = 0;
            s.paddingTop = 0;
            s.paddingBottom = 0;
        }, s =>
        {
            s.alignSelf = StyleKeyword.Null;
            s.height = StyleKeyword.Null;
            s.unityTextAlign = StyleKeyword.Null;
            s.marginTop = StyleKeyword.Null;
            s.marginBottom = StyleKeyword.Null;
            s.paddingTop = StyleKeyword.Null;
            s.paddingBottom = StyleKeyword.Null;
        });
        // The progress strip's own row spans the box, its bar at the right.
        var progress = UI.Find(corner, UiSelector.TypeName(VanillaUi.Elements.OperationDisplays[0]));
        var strip = progress != null ? UI.Find(progress, UiSelector.Name(VanillaUi.Elements.Row)) : null;
        dress(strip, s =>
        {
            s.width = Length.Percent(100);
            s.marginLeft = 0;
            s.paddingLeft = 0;
        }, s =>
        {
            s.width = StyleKeyword.Null;
            s.marginLeft = StyleKeyword.Null;
            s.paddingLeft = StyleKeyword.Null;
        });
        // The faction's icon has empty space inside its picture: it is drawn back by that much, so
        // what shows of it starts where the rows' text does.
        dress(UI.Find(corner, UiSelector.Name(VanillaUi.Elements.OperationFactionIcon)), s => s.marginLeft = FactionIconInset, s => s.marginLeft = StyleKeyword.Null);
        dress(UI.Find(corner, UiSelector.Name(VanillaUi.Elements.OperationProgressBar)), s => s.width = OpsProgressBarWidth, s => s.width = StyleKeyword.Null);
    }

    // Turns a vanilla container into the left column's box.
    private void BoxUp(VisualElement box, float top)
    {
        box.AddToClassList(OpsBoxClass);
        box.style.position = Position.Absolute;
        box.style.left = OpsColumnLeft;
        box.style.top = top;
        box.style.right = StyleKeyword.Auto;
        box.style.width = OpsColumnWidth;
        box.style.marginLeft = 0;
        box.style.flexDirection = FlexDirection.Column;
        box.style.alignItems = Align.Stretch;
        // The fill and the hairline on the element itself: vanilla's own rules for it outrank shell.uss.
        box.style.backgroundColor = OpsBoxFill;
        Border(box.style, 0.8f, OpsBoxLine);
        _homeUndo.Add(() =>
        {
            box.style.backgroundColor = StyleKeyword.Null;
            Border(box.style, null, null);
            box.RemoveFromClassList(OpsBoxClass);
            box.style.left = StyleKeyword.Null;
            box.style.top = StyleKeyword.Null;
            box.style.right = StyleKeyword.Null;
            box.style.width = StyleKeyword.Null;
            box.style.marginLeft = StyleKeyword.Null;
            box.style.flexDirection = StyleKeyword.Null;
            box.style.alignItems = StyleKeyword.Null;
        });
    }

    // Sets inline styles on a vanilla element for the Operations view, and takes them off with it.
    private void Dress(VisualElement element, Action<IStyle> apply, Action<IStyle> undo)
    {
        if (element == null)
            return;
        apply(element.style);
        _homeUndo.Add(() => undo(element.style));
    }

    private static void Border(IStyle s, float? width, UnityEngine.Color? colour)
    {
        StyleFloat w = width is { } value ? value : StyleKeyword.Null;
        (s.borderTopWidth, s.borderBottomWidth, s.borderLeftWidth, s.borderRightWidth) = (w, w, w, w);
        if (width == null || colour != null)
        {
            StyleColor c = colour is { } tint ? tint : StyleKeyword.Null;
            (s.borderTopColor, s.borderBottomColor, s.borderLeftColor, s.borderRightColor) = (c, c, c, c);
        }
    }

    private static void Pad(IStyle s, float? pad)
    {
        StyleLength p = pad is { } value ? value : StyleKeyword.Null;
        (s.paddingTop, s.paddingBottom, s.paddingLeft, s.paddingRight) = (p, p, p, p);
    }
}
