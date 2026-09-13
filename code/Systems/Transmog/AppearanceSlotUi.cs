using Il2CppInterop.Runtime;
using Jiangyu.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

internal static class AppearanceSlotUi
{
    private const float TileWidth = 80f, TileHeight = 36f;
    private const float PaddingRight = 8f, PaddingBottom = 6f;

    // Slot tiles sit outside the modal's stylesheet scope, so both surfaces set the tint here.
    public static void Artwork(VisualElement element, StyleBackground image, bool silhouette)
    {
        element.style.backgroundImage = image;
        element.style.unityBackgroundImageTintColor = silhouette
            ? new Color(154f / 255f, 151f / 255f, 135f / 255f)
            : Color.white;
    }

    public static Button Tile(string name, Action clicked)
    {
        var button = new Button { name = name, focusable = false };
        button.AddToClassList("wm-appearance-tile");
        button.style.position = UnityEngine.UIElements.Position.Absolute;
        button.style.width = TileWidth;
        button.style.height = TileHeight;
        button.style.paddingLeft = button.style.paddingRight = 0f;
        button.style.paddingTop = button.style.paddingBottom = 0f;
        button.style.backgroundColor = new Color(0f, 0f, 0f, .94f);
        var border = new StyleColor(new Color(116f / 255f, 108f / 255f, 75f / 255f));
        button.style.borderLeftColor = button.style.borderRightColor = border;
        button.style.borderTopColor = button.style.borderBottomColor = border;
        button.style.borderLeftWidth = button.style.borderRightWidth = 1f;
        button.style.borderTopWidth = button.style.borderBottomWidth = 1f;
        Overlay(button, "Selected", "slot-selected-border").SetVisible(false);
        button.clickable.clicked += clicked;
        return button;
    }

    public static VisualElement Overlay(VisualElement parent, string name, string classes)
    {
        var element = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
        foreach (var style in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            element.AddToClassList(style);
        element.style.position = UnityEngine.UIElements.Position.Absolute;
        element.style.left = element.style.right = 0f;
        element.style.top = element.style.bottom = 0f;
        parent.Add(element);
        return element;
    }

    public static bool InsideAlternatives(VisualElement element, VisualElement window)
    {
        for (var parent = element.parent; parent != null && parent != window; parent = parent.parent)
            if (parent.name == "EquipmentAlternatives")
                return true;
        return false;
    }

    public static void ClosePickers(VisualElement window)
    {
        foreach (var modal in UI.FindAll(window, UiSelector.Class("wm-outfit-modal")))
            (modal.parent ?? modal).SetVisible(false);
        foreach (var tile in UI.FindAll(window, UiSelector.Class("wm-appearance-tile")))
            UI.Find(tile, UiSelector.Name("Selected"))?.SetVisible(false);
    }

    // The tile is a sibling so a click cannot also open the underlying equipment slot.
    // Re-find it after a layout change because UnitWindow can rebuild its equipment column.
    public static void Position(VisualElement tile, VisualElement slot, string anchorClass)
    {
        Reposition(tile, slot);
        if (slot.ClassListContains(anchorClass))
            return;
        slot.AddToClassList(anchorClass);
        var host = slot.parent;
        var tileName = tile.name;
        slot.RegisterCallback<GeometryChangedEvent>(DelegateSupport.ConvertDelegate<EventCallback<GeometryChangedEvent>>(
            (Action<GeometryChangedEvent>)(_ =>
            {
                var current = host == null ? null : UI.Find(host, UiSelector.Name(tileName));
                if (current != null)
                    Reposition(current, slot);
            })));
    }

    private static void Reposition(VisualElement tile, VisualElement slot)
    {
        var rect = slot.layout;
        if (float.IsNaN(rect.x) || float.IsNaN(rect.width))
            return;
        tile.style.left = rect.xMax - TileWidth - PaddingRight;
        tile.style.top = rect.yMax - TileHeight - PaddingBottom;
    }
}
