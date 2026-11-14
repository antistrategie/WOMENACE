using Jiangyu.Game.Ui;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The Workshop's panels, at the sides of the station the room shows between them.
public sealed partial class StrategyDiorama
{
    private const string WorkshopClass = "wm-workshop";
    // The panels' places between the bars, in the shell's units: what there is to work on down
    // the left, and on the right the work itself over what it is made from.
    private const float WorkshopListWidth = 300f;
    private const float WorkshopWorkLeft = 918f;
    private const float WorkshopWorkWidth = 330f;
    private const float WorkshopPreviewGap = 12f;

    // Vanilla's three windows stand at the sides, and the station shows between them. Kalina's
    // shop, on the same screen, keeps them as they come.
    private void LayOutWorkshop(VisualElement root)
    {
        var panels = UI.Find(root, UiSelector.Name(VanillaUi.Elements.Panels));
        var materials = panels != null ? UI.Find(panels, UiSelector.Name("PlayerPanel")) : null;
        var work = panels != null ? UI.Find(panels, UiSelector.Name("AssemblePanel")) : null;
        var projects = panels != null ? UI.Find(panels, UiSelector.Name("ProjectsPanel")) : null;
        // Kalina's shop puts these windows away and shows its own.
        if (ShopSystem.IsShopOpen)
            return;
        if (materials == null || work == null || projects == null || _active != _weaponSet)
        {
            Fit(panels, WindowScale, 50, 0);
            return;
        }
        if (!root.ClassListContains(WorkshopClass))
        {
            root.AddToClassList(WorkshopClass);
            _shellUndo.Add(() => root.RemoveFromClassList(WorkshopClass));
        }
        // Calibration has its own panels, and these make way for them.
        var calibrating = WorkshopStation == CalibrateStation;
        Inline(panels, e =>
        {
            var s = e.style;
            s.position = Position.Absolute;
            s.left = 0;
            s.top = 0;
            s.right = 0;
            s.bottom = 0;
            s.display = calibrating ? DisplayStyle.None : DisplayStyle.Flex;
        }, e =>
        {
            var s = e.style;
            s.position = StyleKeyword.Null;
            s.left = StyleKeyword.Null;
            s.top = StyleKeyword.Null;
            s.right = StyleKeyword.Null;
            s.bottom = StyleKeyword.Null;
            s.display = StyleKeyword.Null;
        });
        Dock(projects, RosterLeft, SquadsTop, WorkshopListWidth, SquadsBottom - SquadsTop);
        Dock(work, WorkshopWorkLeft, SquadsTop, WorkshopWorkWidth, SquadsBottom - SquadsTop);
        // Vanilla stands what a blueprint makes in the middle of a tall box, which at this size
        // is a gap over it.
        Inline(UI.Find(work, UiSelector.Name("ProjectPreview")), e =>
        {
            e.style.height = StyleKeyword.Auto;
            e.style.marginTop = WorkshopPreviewGap;
            e.style.marginBottom = WorkshopPreviewGap;
        }, e =>
        {
            e.style.height = StyleKeyword.Null;
            e.style.marginTop = StyleKeyword.Null;
            e.style.marginBottom = StyleKeyword.Null;
        });
        // What there is to make it from goes under what there is to work on, in the one panel,
        // which scrolls as a whole: the two lists stand at their full heights in it.
        var scroll = UI.Find(projects, UiSelector.Name(WorkshopScrollName))?.TryCast<ScrollView>();
        if (scroll == null)
        {
            scroll = new ScrollView(ScrollViewMode.Vertical) { name = WorkshopScrollName };
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.flexGrow = 1;
            // Vanilla's scroll views lay their content out in a row whichever way they scroll.
            scroll.contentContainer.style.flexDirection = FlexDirection.Column;
            scroll.contentContainer.style.flexWrap = Wrap.NoWrap;
            // Above the panel's hint, which keeps the panel's foot.
            var hint = UI.Find(projects, UiSelector.Name("Hint"));
            if (hint != null && hint.parent == projects)
                projects.Insert(projects.IndexOf(hint), scroll);
            else
                projects.Add(scroll);
            var made = scroll;
            _shellUndo.Add(() => made.RemoveFromHierarchy());
        }
        foreach (var part in new[]
        {
            UI.Find(projects, UiSelector.Name("WindowHeader")), UI.Find(projects, UiSelector.Name("Projects")),
            UI.Find(materials, UiSelector.Name("WindowHeader")), UI.Find(materials, UiSelector.Name("PlayerMaterials")),
        })
        {
            if (part == null)
                continue;
            MoveInto(scroll.contentContainer, part);
            AtItsOwnHeight(part);
            // A list's own scroller would scroll inside the panel's.
            AtItsOwnHeight(UI.Find(part, UiSelector.Name("ScrollView")));
        }
        Inline(materials, e => e.style.display = DisplayStyle.None, e => e.style.display = StyleKeyword.Null);
    }

    private const string WorkshopScrollName = "wm-workshop-scroll";

    // An element as tall as what it holds, neither stretched nor squeezed by its panel.
    private void AtItsOwnHeight(VisualElement element) =>
        Inline(element, e =>
        {
            e.style.flexGrow = 0;
            e.style.flexShrink = 0;
            e.style.height = StyleKeyword.Auto;
        }, e =>
        {
            e.style.flexGrow = StyleKeyword.Null;
            e.style.flexShrink = StyleKeyword.Null;
            e.style.height = StyleKeyword.Null;
        });

    // Stands a vanilla window at a place of its own, at a size of its own.
    private void Dock(VisualElement window, float left, float top, float width, float height) =>
        Inline(window, e =>
        {
            var s = e.style;
            s.position = Position.Absolute;
            s.left = left;
            s.top = top;
            s.right = StyleKeyword.Auto;
            s.bottom = StyleKeyword.Auto;
            s.width = width;
            s.height = height;
            s.marginLeft = 0;
            s.marginTop = 0;
            s.marginRight = 0;
            s.marginBottom = 0;
        }, e =>
        {
            var s = e.style;
            s.position = StyleKeyword.Null;
            s.left = StyleKeyword.Null;
            s.top = StyleKeyword.Null;
            s.right = StyleKeyword.Null;
            s.bottom = StyleKeyword.Null;
            s.width = StyleKeyword.Null;
            s.height = StyleKeyword.Null;
            s.marginLeft = StyleKeyword.Null;
            s.marginTop = StyleKeyword.Null;
            s.marginRight = StyleKeyword.Null;
            s.marginBottom = StyleKeyword.Null;
        });
}
