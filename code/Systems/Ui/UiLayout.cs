using UnityEngine.UIElements;

namespace WOMENACE.Code;

internal static class UiLayout
{
    public static void Fill(VisualElement element)
    {
        element.style.position = Position.Absolute;
        element.style.left = element.style.right = element.style.top = element.style.bottom = new StyleLength(0f);
    }
}
