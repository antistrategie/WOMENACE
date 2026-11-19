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
            VanillaNavigation.ApplyGameFont(caption);
            display.Insert(0, caption);
        }
    }

    // The resources as one line of boxes, centred in the top bar. Set on the elements themselves: the tab
    // bar's own rules for these outrank shell.uss.
    private void LayOutResources(VisualElement bar)
    {
        var container = UI.Find(bar, UiSelector.Name(VanillaUi.Elements.TabBarContainer));
        InlineStyle(container, s => { s.justifyContent = Justify.Center; s.width = StyleKeyword.Auto; },
            s => { s.justifyContent = StyleKeyword.Null; s.width = StyleKeyword.Null; });
        var resources = UI.Find(bar, UiSelector.Name(VanillaUi.Elements.TabBarResources));
        InlineStyle(resources, s =>
        {
            s.flexDirection = FlexDirection.Row;
            s.backgroundColor = UnityEngine.Color.clear;
            s.width = StyleKeyword.Auto;
        }, s =>
        {
            s.flexDirection = StyleKeyword.Null;
            s.backgroundColor = StyleKeyword.Null;
            s.width = StyleKeyword.Null;
        });
        if (resources == null)
            return;
        // Each resource is a gold-edged box: its name on gold, its amount on black.
        foreach (var (element, _, _) in ResourceCaptions)
        {
            var display = UI.Find(bar, UiSelector.Name(element));
            InlineStyle(display, s =>
            {
                s.flexDirection = FlexDirection.Row;
                s.alignItems = Align.Stretch;
                s.height = ResourceBoxHeight;
                s.width = StyleKeyword.Auto;
                s.marginLeft = 6;
                s.marginRight = 0;
                s.marginTop = 0;
                s.marginBottom = 0;
                s.paddingLeft = 0;
                s.paddingRight = 0;
                s.paddingTop = 0;
                s.paddingBottom = 0;
            }, s =>
            {
                s.flexDirection = StyleKeyword.Null;
                s.alignItems = StyleKeyword.Null;
                s.height = StyleKeyword.Null;
                s.width = StyleKeyword.Null;
                s.marginLeft = StyleKeyword.Null;
                s.marginRight = StyleKeyword.Null;
                s.marginTop = StyleKeyword.Null;
                s.marginBottom = StyleKeyword.Null;
                s.paddingLeft = StyleKeyword.Null;
                s.paddingRight = StyleKeyword.Null;
                s.paddingTop = StyleKeyword.Null;
                s.paddingBottom = StyleKeyword.Null;
            });
            var amount = display != null ? UI.Find(display, UiSelector.Name(VanillaUi.Elements.ResourceAmount)) : null;
            InlineStyle(amount, s =>
            {
                s.fontSize = 11;
                s.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
                s.marginLeft = 0;
                s.marginRight = 0;
                s.marginTop = 0;
                s.marginBottom = 0;
                s.paddingLeft = 6;
                s.paddingRight = 6;
                // The amount's typeface sits high in its line.
                s.paddingTop = 1.6f;
                s.paddingBottom = 0;
                s.width = StyleKeyword.Auto;
                s.height = StyleKeyword.Auto;
            }, s =>
            {
                s.fontSize = StyleKeyword.Null;
                s.unityTextAlign = StyleKeyword.Null;
                s.marginLeft = StyleKeyword.Null;
                s.marginRight = StyleKeyword.Null;
                s.marginTop = StyleKeyword.Null;
                s.marginBottom = StyleKeyword.Null;
                s.paddingLeft = StyleKeyword.Null;
                s.paddingRight = StyleKeyword.Null;
                s.paddingTop = StyleKeyword.Null;
                s.paddingBottom = StyleKeyword.Null;
                s.width = StyleKeyword.Null;
                s.height = StyleKeyword.Null;
            });
        }
        InlineStyle(resources, s => s.alignItems = Align.Center, s => s.alignItems = StyleKeyword.Null);
        for (var i = 0; i < resources.childCount; i++)
        {
            var row = resources.ElementAt(i);
            // Vanilla's markup lets each row wrap, which stands its two resources one over the
            // other when the row comes out a fraction narrower than they are. That is set on the
            // element in the markup, where no stylesheet rule reaches it.
            InlineStyle(row, s =>
            {
                s.width = StyleKeyword.Auto;
                s.flexWrap = Wrap.NoWrap;
                s.marginTop = 0;
                s.marginBottom = 0;
                s.alignItems = Align.Center;
            }, s =>
            {
                s.width = StyleKeyword.Null;
                s.flexWrap = StyleKeyword.Null;
                s.marginTop = StyleKeyword.Null;
                s.marginBottom = StyleKeyword.Null;
                s.alignItems = StyleKeyword.Null;
            });
        }
    }
}
