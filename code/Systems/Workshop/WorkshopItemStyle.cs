using Il2CppMenace.Items;
using Jiangyu.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

internal static class WorkshopItemStyle
{
    public static void Apply(VisualElement slot, BaseItemTemplate template)
    {
        var id = template?.TryCast<BlueprintTemplate>()?.GetCraftingResult()?.GetID() ?? template?.GetID();
        var icon = UI.Find(slot, UiSelector.Name("Icon")) ?? UI.Find(slot, UiSelector.Name("Image"));
        if (icon != null)
            icon.style.unityBackgroundImageTintColor = Tint(id);

        var badge = UI.Find(slot, UiSelector.Name("wm-part-class"))?.TryCast<Label>();
        if (!WeaponParts.IsPart(id))
        {
            badge?.SetVisible(false);
            if (badge != null)
            {
                var reusedStack = UI.Find(slot, UiSelector.Name("StackSize"));
                if (reusedStack != null)
                    reusedStack.style.top = reusedStack.style.right = reusedStack.style.left = reusedStack.style.bottom = new StyleLength(StyleKeyword.Null);
            }
            return;
        }
        if (badge == null)
        {
            badge = new Label { name = "wm-part-class", pickingMode = PickingMode.Ignore };
            badge.AddToClassList("wm-part-class");
            // Native Workshop slots sit outside the injected UXML's stylesheet scope.
            // Keep the badge out of their flex layout even where the shop stylesheet cannot reach.
            badge.style.position = Position.Absolute;
            badge.style.left = new StyleLength(1f);
            badge.style.top = new StyleLength(0f);
            badge.style.marginLeft = badge.style.marginRight = badge.style.marginTop = badge.style.marginBottom = new StyleLength(0f);
            badge.style.paddingTop = badge.style.paddingBottom = new StyleLength(0f);
            badge.style.paddingLeft = badge.style.paddingRight = new StyleLength(2f);
            badge.style.fontSize = new StyleLength(8f);
            badge.style.color = new Color(222f / 255f, 208f / 255f, 154f / 255f);
            badge.style.backgroundColor = new Color(0f, 0f, 0f, .75f);
            badge.style.unityTextAlign = TextAnchor.UpperLeft;
            slot.Add(badge);
        }
        badge.text = WeaponClasses.Code(WeaponParts.ClassOfPart(id));
        badge.SetVisible(true);
        // Native material stacks use the upper left as well. Keep their count in the lower left.
        var stack = UI.Find(slot, UiSelector.Name("StackSize"));
        if (stack != null)
        {
            stack.style.top = new StyleLength(StyleKeyword.Auto);
            stack.style.right = new StyleLength(StyleKeyword.Auto);
            stack.style.left = stack.style.bottom = new StyleLength(0f);
        }
    }

    public static Color Tint(string id) => id == CalibrationCores.AdvancedId ? new Color(1f, .85f, .2f, 1f) : Color.white;
}
