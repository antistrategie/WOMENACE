using UnityEngine;

namespace WOMENACE.Code;

// Kalina's shop at the ops room's holotable: its view of the table, its basket standing on the table
// as holograms of what is in it, and its panel's place on the screen. A Procurement pull runs at the
// capsule machine (StrategyDiorama.Machine.cs).
public sealed partial class StrategyDiorama
{
    // Kalina's shop on Buy and Sell: the table from its side, drawn to the right of the list.
    private static readonly Framing ShopFraming = new(new Vector3(-14.4f, 1.64f, 4.44f), new Vector3(-14.4f, 0.45f, 0f), 42f, MachineShift, 0.11f);
    private const string ShopClass = "wm-kalina";
    private const string ShopProcurementClass = "wm-kalina--procurement";

    // The holotable is the shop's basket. Every item put in it stands on the table as a
    // hologram of its picture, on a ring the table projects it from, with how many of it are
    // wanted at its shoulder. A few of them stand one behind another as well, which tells one
    // from several at a glance and no more than that. The cards are in the part of the table
    // seen between the shop's panel and Kalina, in rows that start at the front, and are
    // smaller the more there are. The shop looks across the table from low down, so a card
    // hides the table for some way behind it: a row stands far enough behind the one in front
    // for most of it to be seen over it, and to one side of it. They are lit more than the ship
    // is, being small pictures seen past two panels. The basket is looked at every few frames.
    private static readonly Vector3 ShopCartAt = new(-13.92f, -0.12f, 0.5f);
    private const float ShopCartWidth = 1.45f;
    private const float ShopCartDepth = 1.15f;
    // How many of its own heights a card must be behind another to be mostly seen over it.
    private const float ShopCardClearance = 2.2f;
    private const float ShopCardLargest = 0.5f;
    private const int ShopCardsInRow = 4;
    private const int ShopCardsDeep = 5;
    private const float ShopCardIntensity = 2.8f;
    private const float ShopRingIntensity = 1.4f;
    private const int ShopCardFrames = 10;
    // The counts are labels in one panel of their own, drawn into one texture: a grid of cells,
    // each count's quad showing its own cell.
    private const int ShopCountColumns = 8;
    private const int ShopCountRows = 6;
    private const int ShopCountCellWidth = 128;
    private const int ShopCountCellHeight = 64;
    private const float ShopCountPlate = 0.8f;
    // The cards of an item new to the basket rise from the table. Each is its material and
    // when it started.
    private const float ShopCardRise = 0.35f;

    // The panel the counts are drawn by, made the first time a basket is shown.
    private bool ShopCountPanel()
    {
        if (_shop.CountHost != null)
            return true;
        if (!TexturePanel("WOMENACE_ShopCounts", _shellRoot, ShopCountColumns * ShopCountCellWidth, ShopCountRows * ShopCountCellHeight, 1f,
            out _shop.CountHost, out _shop.CountSettings, out _shop.CountTexture, out var root))
            return false;
        for (var i = 0; i < ShopCountColumns * ShopCountRows; i++)
        {
            var label = new UnityEngine.UIElements.Label { pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
            var style = label.style;
            style.position = UnityEngine.UIElements.Position.Absolute;
            style.left = i % ShopCountColumns * ShopCountCellWidth;
            style.top = i / ShopCountColumns * ShopCountCellHeight;
            style.width = ShopCountCellWidth;
            style.height = ShopCountCellHeight;
            style.fontSize = 44;
            style.color = Color.white;
            // What a cell draws is what its quad darkens the room behind, so a count is read
            // over whatever stands behind it.
            style.backgroundColor = Color.black;
            style.unityFontStyleAndWeight = FontStyle.Bold;
            style.unityTextAlign = TextAnchor.MiddleCenter;
            style.marginLeft = style.marginRight = style.marginTop = style.marginBottom = 0;
            style.paddingLeft = style.paddingRight = style.paddingTop = style.paddingBottom = 0;
            root.Add(label);
            _shop.Counts.Add(label);
        }
        return true;
    }

    // The basket's holograms go with the room, and the panel their counts are drawn by with them.
    private void ClearShopCart()
    {
        ShowShopCart(false);
        foreach (var made in new UnityEngine.Object[] { _shop.CountHost, _shop.CountSettings })
            if (made != null)
                UnityEngine.Object.Destroy(made);
        ReleaseTexture(ref _shop.CountTexture);
        (_shop.CountHost, _shop.CountSettings, _shop.CountTexture) = (null, null, null);
        _shop.Counts.Clear();
    }

    // Kalina's shop in the ops room: the machine stands on the table while Procurement is open,
    // and the camera's view follows the shop's own tabs, which change without a screen change.
    private void DriveShop()
    {
        var open = _policyScreen == VanillaUi.Screens.Workshop && ShopSystem.IsShopOpen;
        var procurement = open && ShopSystem.IsProcurementOpen;
        if (procurement != _shop.Procurement)
        {
            _shop.Procurement = procurement;
            if (open)
            {
                ApplyScreenPolicy();
                ApplyHome(_homeScreen, _homeRoomShown);
            }
        }
        // Nothing is asked of the clock while there is no shop open and no basket standing.
        if ((open || _shop.CartFor != 0) && UnityEngine.Time.frameCount % ShopCardFrames == 0)
            ShowShopCart(open && !procurement && _room != null && _active == _ops);
        if (_shop.Arriving.Count > 0)
            RaiseShopCards();
        if (_shop.PullOn)
            return;
        var wanted = procurement && _room != null && _camera != null && _active == _ops;
        if (wanted && _shop.Machine == null)
        {
            ShowMachine(PullMachineHeight, 0f, 0f);
            _shop.MachineForShop = _shop.Machine != null;
            RestMachine();
        }
        else if (!wanted && _shop.Machine != null && _shop.MachineForShop)
        {
            ShowMachine(0f, 0f, 0f);
            _shop.MachineForShop = false;
        }
    }

    // Stands the basket on the table, or clears it. Nothing is touched while the basket is as
    // it was last shown.
    private void ShowShopCart(bool shown)
    {
        _shop.Basket.Clear();
        if (shown)
            ShopSystem.Basket(_shop.Basket);
        // What stands on the table follows which items are in the basket and how many of each
        // are stacked. The number at a card's shoulder is a label, so a count that changes
        // nothing else is written to its label and nothing is built.
        var (basket, counts) = (0L, 0L);
        foreach (var (template, count) in _shop.Basket)
        {
            basket = unchecked(basket * 31 + template.Pointer.ToInt64() * 7 + Math.Min(count, ShopCardsDeep));
            counts = unchecked(counts * 31 + count);
        }
        if (basket == _shop.CartFor)
        {
            if (counts != _shop.CartCounts)
                for (var i = 0; i < _shop.Basket.Count && i < _shop.Counts.Count; i++)
                    _shop.Counts[i].text = ShopCount(_shop.Basket[i].Count);
            _shop.CartCounts = counts;
            return;
        }
        (_shop.CartFor, _shop.CartCounts) = (basket, counts);
        _shop.Arriving.Clear();
        foreach (var card in _shop.Cards)
            UnityEngine.Object.Destroy(card);
        foreach (var material in _shop.CardMaterials)
            UnityEngine.Object.Destroy(material);
        _shop.Cards.Clear();
        _shop.CardMaterials.Clear();
        var marker = MarkerTemplate();
        if (_shop.Basket.Count == 0 || HologramMaterial() == null || _room == null || marker == null)
        {
            _shop.Standing.Clear();
            return;
        }
        var counted = ShopCountPanel();
        var rows = (_shop.Basket.Count + ShopCardsInRow - 1) / ShopCardsInRow;
        var inRow = (_shop.Basket.Count + rows - 1) / rows;
        var size = Mathf.Min(ShopCardLargest, ShopCartWidth / inRow * 0.8f);
        if (rows > 1)
            size = Mathf.Min(size, ShopCartDepth / (rows - 1) / ShopCardClearance);
        var between = rows > 1 ? ShopCartDepth / (rows - 1) : 0f;
        // The cards face the shop's view of the table. A ring lies on the table.
        var away = ShopCartAt - ShopFraming.Eye;
        away.y = 0f;
        var facing = Quaternion.LookRotation(away.normalized, Vector3.up);
        var lying = Quaternion.LookRotation(Vector3.down, Vector3.back);
        for (var i = 0; i < _shop.Basket.Count; i++)
        {
            var (item, count) = _shop.Basket[i];
            var picture = item.Icon;
            if (picture == null || picture.texture == null)
                continue;
            var (row, column) = (i / inRow, i % inRow);
            var across = Mathf.Min(inRow, _shop.Basket.Count - row * inRow);
            var aside = rows > 1 ? (row % 2 == 0 ? -0.25f : 0.25f) : 0f;
            var foot = ShopCartAt + new Vector3((column - (across - 1) / 2f + aside) * ShopCartWidth / inRow, 0f, -row * between);
            var ring = Quad(marker, "wm-shop-ring", RingTexture());
            ring.transform.localPosition = foot + new Vector3(0f, 0.004f, 0f);
            ring.transform.localRotation = lying;
            ring.transform.localScale = new Vector3(size * 0.95f, size * 0.95f, 1f);
            var ringMaterial = ring.GetComponent<MeshRenderer>().material;
            ringMaterial.SetFloat(IntensityId, ShopRingIntensity);
            ringMaterial.SetFloat("_ScanDepth", 0f);
            ringMaterial.SetFloat("_Plate", 0f);
            ringMaterial.SetFloat("_EdgeFade", 0f);
            _shop.Cards.Add(ring);
            _shop.CardMaterials.Add(ringMaterial);
            if (counted && i < _shop.Counts.Count)
            {
                _shop.Counts[i].text = ShopCount(count);
                var number = Quad(marker, "wm-shop-count", _shop.CountTexture);
                // At the card's shoulder, a little nearer than the card so it is drawn over it.
                number.transform.localPosition = foot + new Vector3(size * 0.38f, size * 0.92f, 0.03f);
                number.transform.localRotation = facing;
                number.transform.localScale = new Vector3(size * 0.6f, size * 0.6f * ShopCountCellHeight / ShopCountCellWidth, 1f);
                PlainHologram(number, MarketNumberIntensity);
                var numberMaterial = number.GetComponent<MeshRenderer>().material;
                numberMaterial.SetFloat("_Plate", ShopCountPlate);
                numberMaterial.SetVector(UvRectId, SheetCell(i, ShopCountColumns, ShopCountRows));
                _shop.Cards.Add(number);
                _shop.CardMaterials.Add(numberMaterial);
            }
            var shape = picture.rect;
            var widest = Mathf.Max(shape.width, shape.height);
            var material = IconHologram(picture);
            material.SetFloat(HologramFloorId, _room.transform.TransformPoint(foot).y);
            material.SetFloat(HologramHeightId, size * 1.3f);
            var arriving = !_shop.Standing.Contains(item.Pointer);
            material.SetFloat(HologramRevealId, arriving ? 0f : ShopCardRevealed);
            material.SetFloat(IntensityId, ShopCardIntensity);
            _shop.CardMaterials.Add(material);
            if (arriving)
                _shop.Arriving.Add((material, Time.unscaledTime));
            for (var behind = 0; behind < Mathf.Min(count, ShopCardsDeep); behind++)
            {
                var card = IconCard(_room.transform, picture, size);
                card.name = "wm-shop-card";
                // Each stands on the table, a little behind the one in front of it.
                card.transform.localPosition = foot + new Vector3(behind * size * 0.05f, size * shape.height / widest / 2f, -behind * size * 0.1f);
                card.transform.localRotation = facing;
                card.GetComponent<Renderer>().sharedMaterial = material;
                _shop.Cards.Add(card);
            }
        }
        _shop.Standing.Clear();
        foreach (var (item, _) in _shop.Basket)
            _shop.Standing.Add(item.Pointer);
    }

    private const float ShopCardRevealed = 1.1f;

    private static string ShopCount(int count) => "×" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // Raises the cards that are new to the basket.
    private void RaiseShopCards()
    {
        var now = Time.unscaledTime;
        for (var i = _shop.Arriving.Count - 1; i >= 0; i--)
        {
            var (material, started) = _shop.Arriving[i];
            var share = Math.Min(1f, (now - started) / ShopCardRise);
            material.SetFloat(HologramRevealId, share * ShopCardRevealed);
            if (share >= 1f)
                _shop.Arriving.RemoveAt(i);
        }
    }

    // The shop's own panel is a list at the left of the ops room, Kalina at the right on Buy and
    // Sell, and the machine's side of the frame left clear on Procurement.
    private void LayOutShop(UnityEngine.UIElements.VisualElement root)
    {
        var open = ShopSystem.IsShopOpen;
        if (open && !root.ClassListContains(ShopClass))
            _shellUndo.Add(() =>
            {
                root.RemoveFromClassList(ShopClass);
                root.RemoveFromClassList(ShopProcurementClass);
            });
        root.EnableInClassList(ShopClass, open);
        root.EnableInClassList(ShopProcurementClass, open && ShopSystem.IsProcurementOpen);
    }
}
