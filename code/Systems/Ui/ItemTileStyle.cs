using Jiangyu.Game.Ui;
using Jiangyu.Game.Ui.Components;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

internal static class ItemTileStyle
{
    public static void Align(ItemTile tile)
    {
        // The native Pickable inset is inline and overrides USS. Its artwork must fill the
        // same bounds as the SDK's hover and selection overlays.
        foreach (var name in new[] { "Pickable", "Background", "Icon", "Border" })
        {
            var element = UI.Find(tile.Root, UiSelector.Name(name));
            if (element != null)
                Fill(element);
        }
        var slot = UI.Find(tile.Root, UiSelector.TypeName("MissionResultLootSlot"));
        if (slot != null)
        {
            Fill(slot);
            slot.style.paddingLeft = slot.style.paddingRight = slot.style.paddingTop = slot.style.paddingBottom = new StyleLength(0f);
        }
    }

    private static void Fill(VisualElement element)
    {
        UiLayout.Fill(element);
        element.style.marginLeft = element.style.marginRight = element.style.marginTop = element.style.marginBottom = new StyleLength(0f);
    }
}
