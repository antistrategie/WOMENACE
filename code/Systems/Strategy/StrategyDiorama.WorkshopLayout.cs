using Jiangyu.Game.Ui;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The Workshop's panels, round the station the room shows between them.
public sealed partial class StrategyDiorama
{
    private const string WorkshopClass = "wm-workshop";
    // The panels' places, in the shell's units: what it is made from down the left, what there
    // is to work on down the right, and the work itself in a strip between them under the
    // station, so all three are seen together and nothing scrolls to compare them.
    private const float WorkshopListWidth = 300f;
    private const float WorkshopStripGap = 16f;
    private const float WorkshopStripHeight = 104f;
    private const float WorkshopStripPad = 10f;
    // Across the strip: the picture of what is made at its left, then its name over the heading
    // and what it takes. The button stands at the strip's top right, level with the name, so
    // what it takes has the strip's whole width under it.
    private const float WorkshopImageWidth = 130f;
    private const string WorkshopImageName = "wm-workshop-image";
    private const float WorkshopHeadingTop = 20f;
    private const float WorkshopTakesTop = 34f;
    private const float WorkshopButtonWidth = 130f;
    private const float WorkshopButtonHeight = 28f;
    private const string WorkshopNameName = "wm-workshop-name";

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
        var tall = SquadsBottom - SquadsTop;
        Dock(materials, RosterLeft, SquadsTop, WorkshopListWidth, tall);
        Dock(projects, 1280f - RosterLeft - WorkshopListWidth, SquadsTop, WorkshopListWidth, tall);
        var stripLeft = RosterLeft + WorkshopListWidth + WorkshopStripGap;
        var stripWidth = 1280f - 2f * stripLeft;
        Dock(work, stripLeft, SquadsBottom - WorkshopStripHeight, stripWidth, WorkshopStripHeight);
        // Vanilla stacks the work's parts down a tall window. In the strip each has a place of
        // its own. What is made is the hologram over the strip, so vanilla's picture of it is
        // put away and its name stands in the strip.
        var centre = UI.Find(work, UiSelector.Name("Center"));
        var inner = stripWidth - 2f * WorkshopStripPad;
        var innerTall = WorkshopStripHeight - 2f * WorkshopStripPad;
        Place(centre, WorkshopStripPad, WorkshopStripPad, inner, innerTall);
        Inline(UI.Find(work, UiSelector.Name("ProjectPreview")), e => e.style.display = DisplayStyle.None, e => e.style.display = StyleKeyword.Null);
        var text = WorkshopImageWidth + WorkshopStripPad;
        if (centre != null && UI.Find(centre, UiSelector.Name(WorkshopNameName)) == null)
        {
            var image = new VisualElement { name = WorkshopImageName, pickingMode = PickingMode.Ignore };
            image.AddToClassList("wm-workshop__image");
            image.style.width = WorkshopImageWidth;
            image.style.height = innerTall;
            var name = new Label { name = WorkshopNameName, pickingMode = PickingMode.Ignore };
            name.AddToClassList("wm-workshop__name");
            name.style.left = text;
            // The strip's words are in the shell's face, as the words of Taking apart are:
            // vanilla's window would give them its own.
            NavigationMenu.ApplyGameFont(name);
            centre.Add(image);
            centre.Add(name);
            _shellUndo.Add(() =>
            {
                image.RemoveFromHierarchy();
                name.RemoveFromHierarchy();
            });
            _workedOnTemplate = IntPtr.Zero;
        }
        var headline = UI.Find(work, UiSelector.Name("ReqMaterialsHeadline"));
        Place(headline, text, WorkshopHeadingTop, null, null);
        Inline(headline, NavigationMenu.ApplyGameFont, e => e.style.unityFontDefinition = StyleKeyword.Null);
        Place(UI.Find(work, UiSelector.Name("ReqMaterialsContainer")), text, WorkshopTakesTop, inner - text, null);
        Place(UI.Find(work, UiSelector.Name("AssembleButton")), inner - WorkshopButtonWidth, 0f, WorkshopButtonWidth, WorkshopButtonHeight);
        Inline(UI.Find(work, UiSelector.Name("ReqMaterialsContainer")), e =>
        {
            // One row: more than fit it are drawn smaller (WorkshopSystem.FitRequired).
            e.style.flexWrap = Wrap.NoWrap;
            e.style.justifyContent = Justify.FlexStart;
        }, e =>
        {
            e.style.flexWrap = StyleKeyword.Null;
            e.style.justifyContent = StyleKeyword.Null;
        });
    }

    // Stands one of a window's parts at a place of its own in it, as wide and as tall as given
    // or as what it holds.
    private void Place(VisualElement part, float left, float top, float? width, float? height) =>
        Inline(part, e =>
        {
            var s = e.style;
            s.position = Position.Absolute;
            s.left = left;
            s.top = top;
            s.width = width is { } w ? w : StyleKeyword.Auto;
            s.height = height is { } h ? h : StyleKeyword.Auto;
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
            s.width = StyleKeyword.Null;
            s.height = StyleKeyword.Null;
            s.marginLeft = StyleKeyword.Null;
            s.marginTop = StyleKeyword.Null;
            s.marginRight = StyleKeyword.Null;
            s.marginBottom = StyleKeyword.Null;
        });

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
