using System.Globalization;
using Jiangyu.Game.Audio;
using UnityEngine.UIElements;
using static WOMENACE.Code.ShopVisuals;

namespace WOMENACE.Code;

internal sealed class ShopCurrency(ShopArtwork artwork)
{
    public const string Gold = "procurement__sardis_gold";
    public const string Pieces = "procurement__collapse_piece";
    private Label _gold, _pieces;

    public void BuildWallet(VisualElement root, Action buyPieces)
    {
        root.Clear();
        _gold = Amount(root, Gold, "");
        _pieces = Amount(root, Pieces, "");
        var add = SymbolButton(buyPieces, "wm-shop-wallet-add");
        add.name = "wm-shop-buy-pieces";
        root.Add(add);
    }

    public void Refresh(int gold, int pieces)
    {
        _gold.text = gold.ToString("N0", CultureInfo.InvariantCulture);
        _pieces.text = pieces.ToString("N0", CultureInfo.InvariantCulture);
    }

    public Label Amount(VisualElement parent, string asset, string value, string variant = null)
    {
        var group = Element("wm-shop-currency", parent, PickingMode.Ignore);
        if (variant != null)
            group.AddToClassList(variant);
        artwork.Create(asset, "wm-shop-currency-icon", group);
        var label = new Label(value) { pickingMode = PickingMode.Ignore };
        label.AddToClassList("wm-shop-currency-value");
        group.Add(label);
        return label;
    }

    public static Button SymbolButton(Action action, string classes, bool close = false)
    {
        var button = new Button();
        button.AddToClassList("wm-shop-symbol-button");
        button.AddToClassList(classes);
        ShopVisuals.Surface(button, ShopSurface.Button);
        button.clickable.clicked += (Action)(() => { Sound.Click(); action(); });
        var symbol = Element("wm-shop-symbol", button, PickingMode.Ignore);
        symbol.EnableInClassList("wm-shop-symbol-close", close);
        Element("wm-shop-symbol-horizontal", symbol, PickingMode.Ignore);
        Element("wm-shop-symbol-vertical", symbol, PickingMode.Ignore);
        return button;
    }
}
