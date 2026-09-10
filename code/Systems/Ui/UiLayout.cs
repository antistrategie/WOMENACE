using UnityEngine.UIElements;

namespace WOMENACE.Code;

internal static class UiLayout
{
    // Ignore the whole subtree so a popover overlapping its anchor cannot steal hover or clicks.
    public static void IgnorePicking(VisualElement element)
    {
        if (element == null)
            return;
        element.pickingMode = PickingMode.Ignore;
        for (var i = 0; i < element.childCount; i++)
            IgnorePicking(element.ElementAt(i));
    }

    public static void Fill(VisualElement element)
    {
        element.style.position = Position.Absolute;
        element.style.left = element.style.right = element.style.top = element.style.bottom = new StyleLength(0f);
    }
}
