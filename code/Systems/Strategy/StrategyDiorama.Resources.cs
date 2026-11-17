using Il2CppMenace.UI;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Vanilla's resource displays on the shell's top bar, as one line of boxes.
public sealed partial class StrategyDiorama
{
    private const float ResourceBoxHeight = 19f;

    // Each vanilla resource display gets its name before its amount.
    private static void AddResourceCaptions(VisualElement bar)
    {
        foreach (var (element, key, fallback) in ResourceCaptions)
        {
            var display = UI.Find(bar, UiSelector.Name(element));
            if (display == null || UI.Find(display, UiSelector.Class(CaptionClass)) != null)
                continue;
            var caption = new Label(Locale.Text(key, fallback)) { pickingMode = PickingMode.Ignore };
            caption.AddToClassList(CaptionClass);
            // The game's bold face: the tab bar's own is the regular one, thin for black on gold.
            NavigationMenu.ApplyGameFont(caption);
            display.Insert(0, caption);
        }
    }

    // The resources as one line of boxes, centred in the top bar. Set on the elements themselves: the tab
    // bar's own rules for these outrank shell.uss.
    private void LayOutResources(VisualElement bar)
    {
        void Set(VisualElement element, Action apply, Action undo)
        {
            if (element == null)
                return;
            apply();
            _shellUndo.Add(undo);
        }
        var container = UI.Find(bar, UiSelector.Name(VanillaUi.Elements.TabBarContainer));
        Set(container, () => { container.style.justifyContent = Justify.Center; container.style.width = StyleKeyword.Auto; },
            () => { container.style.justifyContent = StyleKeyword.Null; container.style.width = StyleKeyword.Null; });
        var resources = UI.Find(bar, UiSelector.Name(VanillaUi.Elements.TabBarResources));
        Set(resources, () =>
        {
            resources.style.flexDirection = FlexDirection.Row;
            resources.style.backgroundColor = UnityEngine.Color.clear;
            resources.style.width = StyleKeyword.Auto;
        }, () =>
        {
            resources.style.flexDirection = StyleKeyword.Null;
            resources.style.backgroundColor = StyleKeyword.Null;
            resources.style.width = StyleKeyword.Null;
        });
        if (resources == null)
            return;
        // Each resource is a gold-edged box: its name on gold, its amount on black.
        foreach (var (element, _, _) in ResourceCaptions)
        {
            var display = UI.Find(bar, UiSelector.Name(element));
            Set(display, () =>
            {
                display.style.flexDirection = FlexDirection.Row;
                display.style.alignItems = Align.Stretch;
                display.style.height = ResourceBoxHeight;
                display.style.width = StyleKeyword.Auto;
                display.style.marginLeft = 6;
                display.style.marginRight = 0;
                display.style.marginTop = 0;
                display.style.marginBottom = 0;
                display.style.paddingLeft = 0;
                display.style.paddingRight = 0;
                display.style.paddingTop = 0;
                display.style.paddingBottom = 0;
            }, () =>
            {
                display.style.flexDirection = StyleKeyword.Null;
                display.style.alignItems = StyleKeyword.Null;
                display.style.height = StyleKeyword.Null;
                display.style.width = StyleKeyword.Null;
                display.style.marginLeft = StyleKeyword.Null;
                display.style.marginRight = StyleKeyword.Null;
                display.style.marginTop = StyleKeyword.Null;
                display.style.marginBottom = StyleKeyword.Null;
                display.style.paddingLeft = StyleKeyword.Null;
                display.style.paddingRight = StyleKeyword.Null;
                display.style.paddingTop = StyleKeyword.Null;
                display.style.paddingBottom = StyleKeyword.Null;
            });
            var amount = display != null ? UI.Find(display, UiSelector.Name(VanillaUi.Elements.ResourceAmount)) : null;
            Set(amount, () =>
            {
                amount.style.fontSize = 11;
                amount.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
                amount.style.marginLeft = 0;
                amount.style.marginRight = 0;
                amount.style.marginTop = 0;
                amount.style.marginBottom = 0;
                amount.style.paddingLeft = 6;
                amount.style.paddingRight = 6;
                // The amount's typeface sits high in its line.
                amount.style.paddingTop = 1.6f;
                amount.style.paddingBottom = 0;
                amount.style.width = StyleKeyword.Auto;
                amount.style.height = StyleKeyword.Auto;
            }, () =>
            {
                amount.style.fontSize = StyleKeyword.Null;
                amount.style.unityTextAlign = StyleKeyword.Null;
                amount.style.marginLeft = StyleKeyword.Null;
                amount.style.marginRight = StyleKeyword.Null;
                amount.style.marginTop = StyleKeyword.Null;
                amount.style.marginBottom = StyleKeyword.Null;
                amount.style.paddingLeft = StyleKeyword.Null;
                amount.style.paddingRight = StyleKeyword.Null;
                amount.style.paddingTop = StyleKeyword.Null;
                amount.style.paddingBottom = StyleKeyword.Null;
                amount.style.width = StyleKeyword.Null;
                amount.style.height = StyleKeyword.Null;
            });
        }
        Set(resources, () => resources.style.alignItems = Align.Center, () => resources.style.alignItems = StyleKeyword.Null);
        for (var i = 0; i < resources.childCount; i++)
        {
            var row = resources.ElementAt(i);
            // Vanilla's markup lets each row wrap, which stands its two resources one over the
            // other when the row comes out a fraction narrower than they are. That is set on the
            // element in the markup, where no stylesheet rule reaches it.
            Set(row, () =>
            {
                row.style.width = StyleKeyword.Auto;
                row.style.flexWrap = Wrap.NoWrap;
                row.style.marginTop = 0;
                row.style.marginBottom = 0;
                row.style.alignItems = Align.Center;
            }, () =>
            {
                row.style.width = StyleKeyword.Null;
                row.style.flexWrap = StyleKeyword.Null;
                row.style.marginTop = StyleKeyword.Null;
                row.style.marginBottom = StyleKeyword.Null;
                row.style.alignItems = StyleKeyword.Null;
            });
        }
    }
}
