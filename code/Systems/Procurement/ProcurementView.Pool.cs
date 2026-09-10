using System.Globalization;
using Il2CppInterop.Runtime;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;
using static WOMENACE.Code.ShopVisuals;

namespace WOMENACE.Code;

internal sealed partial class ProcurementView
{
    private Action _refreshExchange;

    public void OpenPool(ProcurementSection section)
    {
        if (_busy)
            return;
        _section = section;
        _poolPage = 0;
        _filter = "";
        var opening = !_pool.IsVisible();
        BuildPool();
        _pool.SetVisible(true);
        if (opening)
            ShopVisuals.Enter(_pool[0]);
    }

    private void BuildPool()
    {
        _pool.Clear();
        var dialog = Element("wm-proc-pool", _pool);
        ShopVisuals.Surface(dialog, ShopSurface.Dialog);
        var header = Element("wm-proc-row wm-proc-between wm-proc-pool-heading", dialog);
        Text(Locale.Text("WOMENACE::ui/procurement/pool_title", "Reward pool"), "wm-proc-title", header);
        header.Add(ShopCurrency.SymbolButton(() => CloseOverlay(_pool), "wm-proc-close", true));
        var tabs = Element("wm-proc-pool-tabs", dialog);
        foreach (var section in Procurement.Sections)
        {
            var button = Button("", () => OpenPool(section), "wm-proc-pool-tab");
            button.name = "wm-procurement-pool-" + section.ToString().ToLowerInvariant();
            button.EnableInClassList("wm-proc-selected", section == _section);
            ShopVisuals.Surface(button, ShopSurface.Tab, Colour(section));
            Text(ProcurementCatalogue.SectionName(section), "wm-proc-pool-tab-name", button);
            var rate = Text(FormattableString.Invariant($"{Rate(section):0.###}%"), "wm-proc-pool-tab-rate", button);
            if (section == _section)
                rate.style.color = Colour(section);
            tabs.Add(button);
        }
        var detail = Element("wm-proc-pool-detail", dialog);
        var title = Element("wm-proc-row wm-proc-between wm-proc-pool-title", detail);
        var names = Element("wm-proc-row", title);
        Text(ProcurementCatalogue.SectionName(_section), "wm-proc-section-title", names);
        if (_section is ProcurementSection.Special or ProcurementSection.Dossiers or ProcurementSection.Curios)
            Text(Limited(), "wm-proc-limited", names);
        var chance = Element("", title);
        Text(FormattableString.Invariant($"{Rate(_section):0.###}%"), "wm-proc-rate", chance).style.color = Colour(_section);
        Text(Locale.Text("WOMENACE::ui/procurement/base_chance", "BASE CHANCE"), "wm-proc-rate-caption", chance);
        if (Procurement.Pity(_section) > 0)
        {
            var row = Element("wm-proc-row wm-proc-between wm-proc-pity-row", detail);
            Text(Eligible(_section) ? Locale.Text("WOMENACE::ui/procurement/guarantee", "GUARANTEE") : Complete(), "wm-proc-muted", row);
            Text(FormattableString.Invariant($"{State.Counter(_section)} / {Procurement.Pity(_section)}"), "wm-proc-muted", row);
            Progress(detail, _section);
        }
        if (_section == ProcurementSection.Equipment)
        {
            var filters = Element("wm-proc-row wm-proc-filters", detail);
            AddFilter(filters, "", Locale.Text("WOMENACE::ui/procurement/all", "All"));
            AddFilter(filters, ProcurementCatalogue.TypeWeapon, Locale.Text("WOMENACE::ui/procurement/weapons", "Weapons"));
            AddFilter(filters, ProcurementCatalogue.TypeArmour, Locale.Text("WOMENACE::ui/procurement/armour", "Armour"));
            AddFilter(filters, ProcurementCatalogue.TypeVehicle, Locale.Text("WOMENACE::ui/procurement/vehicles", "Vehicles"));
        }
        _poolItems = Element("wm-proc-pool-items", detail);
        BuildPoolItems();
    }

    private void AddFilter(VisualElement parent, string value, string text)
    {
        var button = Button(text, () => { _filter = value; _poolPage = 0; BuildPool(); }, "wm-proc-filter");
        button.EnableInClassList("wm-proc-selected", _filter == value);
        parent.Add(button);
    }

    private void BuildPoolItems()
    {
        _poolItems.Clear();
        var entries = Catalogue.Entries.Where(entry => entry.Reward.Section == _section
            && (_filter.Length == 0 || entry.Type == _filter)).ToList();
        var pageSize = _section is ProcurementSection.Dossiers or ProcurementSection.Curios ? 6
            : _section is ProcurementSection.Parts or ProcurementSection.Materials ? 12 : 9;
        var pages = Math.Max(1, (entries.Count + pageSize - 1) / pageSize);
        _poolPage = Math.Clamp(_poolPage, 0, pages - 1);
        var grid = Element("wm-proc-grid", _poolItems);
        var columns = pageSize == 12 ? 4 : 3;
        var index = 0;
        foreach (var rowEntries in entries.Skip(_poolPage * pageSize).Take(pageSize).Chunk(columns))
        {
            var row = Element("wm-proc-grid-row", grid);
            // Empty cells keep the last row on the same column tracks as full rows.
            // Equal flex bases include the gaps without percentage rounding at UI scale.
            for (var column = 0; column < columns; column++)
            {
                if (column >= rowEntries.Length)
                {
                    var spacer = Element("wm-proc-pool-spacer", row, PickingMode.Ignore);
                    spacer.EnableInClassList("wm-proc-column-end", column == columns - 1);
                    continue;
                }
                var entry = rowEntries[column];
                var hidden = _section == ProcurementSection.Curios && State.Claimed(entry.Reward.Id) == 0;
                var portrait = entry.IsUnlock || _section == ProcurementSection.Curios;
                var card = Element("wm-proc-pool-item wm-proc-row", row);
                card.EnableInClassList("wm-proc-pool-equipment", _section is ProcurementSection.Equipment or ProcurementSection.Special);
                card.EnableInClassList("wm-proc-column-end", column == columns - 1);
                card.name = "wm-procurement-item-" + entry.Reward.Id;
                ShopVisuals.Surface(card, ShopSurface.Card, Colour(_section));
                if (portrait)
                    card.AddToClassList("wm-proc-pool-dossier");
                if (hidden)
                    Text("?", "wm-proc-pool-question", card).style.color = Colour(_section);
                else if (entry.IsUnlock)
                    Portrait(entry, "wm-proc-pool-art", card);
                else
                    Icon(entry, card);
                var copy = Element("wm-proc-item-copy", card);
                if (portrait)
                    ShopVisuals.Draw(copy, context => ShopVisuals.Gradient(context, context.visualElement.contentRect,
                        new Color(.04f, .06f, .05f, .93f), new Color(.04f, .06f, .05f, .35f)));
                Text(hidden ? "???" : entry.Leader == null ? entry.Name : entry.DollName, "wm-proc-item-name", copy);
                if (entry.Reward.Limit > 0)
                {
                    Text(Claim(entry), "wm-proc-claim", card);
                    card.AddToClassList("wm-proc-pool-limited");
                    card.EnableInClassList("wm-proc-claimed", !State.Available(entry.Reward));
                }
                ShopVisuals.Enter(card, 4, index++ * 12);
            }
        }
        if (entries.Count == 0)
            Text(Locale.Text("WOMENACE::ui/procurement/no_matches", "No matches"), "wm-proc-empty", grid);
        var pager = Element("wm-proc-row wm-proc-between wm-proc-pagination", _poolItems);
        Text(entries.Count == 1 ? Locale.Text("WOMENACE::ui/procurement/one_item", "1 ITEM")
            : Locale.Format("WOMENACE::ui/procurement/items", "{0} ITEMS", entries.Count), "wm-proc-muted", pager);
        if (pages > 1)
        {
            var controls = Element("wm-proc-row wm-proc-pagination-controls", pager);
            var previous = Button("‹", () => { _poolPage--; BuildPoolItems(); }, "wm-proc-page");
            previous.SetEnabled(_poolPage > 0);
            controls.Add(previous);
            Text(FormattableString.Invariant($"{_poolPage + 1} / {pages}"), "wm-proc-muted wm-proc-page-number", controls);
            var next = Button("›", () => { _poolPage++; BuildPoolItems(); }, "wm-proc-page");
            next.SetEnabled(_poolPage + 1 < pages);
            controls.Add(next);
        }
    }

    public void OpenExchange(int amount)
    {
        if (_busy)
            return;
        const int maximum = 99999;
        CloseOverlay(_exchange);
        var dialog = Element("wm-proc-exchange-dialog", _exchange);
        ShopVisuals.Surface(dialog, ShopSurface.Dialog);
        var header = Element("wm-proc-row wm-proc-between", dialog);
        Text(Locale.Text("WOMENACE::ui/procurement/pieces", "Collapse Pieces"), "wm-proc-title", header);
        header.Add(ShopCurrency.SymbolButton(() => CloseOverlay(_exchange), "wm-proc-close", true));
        _artwork.Create(ShopCurrency.Pieces, "wm-proc-exchange-art", dialog);
        _currency.Amount(dialog, ShopCurrency.Gold, Procurement.PiecePrice.ToString(CultureInfo.InvariantCulture), "wm-proc-exchange-price");
        var quantity = new IntegerField { value = Math.Clamp(amount, 1, maximum), name = "wm-procurement-quantity" };
        quantity.AddToClassList("wm-proc-quantity");
        dialog.Add(quantity);
        var presets = Element("wm-proc-row wm-proc-presets", dialog);
        var total = _currency.Amount(dialog, ShopCurrency.Gold, "");
        var feedback = Text("", "wm-proc-feedback", dialog);
        var confirm = Button(Locale.Text("WOMENACE::ui/procurement/buy_pieces", "BUY PIECES  →"), () =>
        {
            var count = Math.Clamp(quantity.value, 1, maximum);
            var result = ShopSystem.Instance.Trade(new Dictionary<string, int> { [Procurement.PieceId] = count }, true);
            if (result.ok)
            {
                CloseOverlay(_exchange);
                _refreshShop();
                Refresh();
            }
            else
                feedback.text = result.error;
        }, "wm-shop-primary wm-proc-buy-pieces");
        confirm.name = "wm-procurement-buy-pieces";
        dialog.Add(confirm);
        void UpdatePrice()
        {
            if (quantity.value > maximum)
                quantity.value = maximum;
            var price = (long)Math.Max(quantity.value, 0) * Procurement.PiecePrice;
            total.text = FormattableString.Invariant($"{price:N0} / {ShopTrade.Balance:N0}");
            confirm.SetEnabled(quantity.value > 0 && price <= ShopTrade.Balance);
        }
        foreach (var number in new[] { 1, 5, 50, 100 })
            presets.Add(Button(number.ToString(CultureInfo.InvariantCulture), () => { quantity.value = number; UpdatePrice(); }, "wm-proc-filter"));
        presets.Add(Button(Locale.Text("WOMENACE::ui/procurement/all", "All"), () =>
        {
            quantity.value = Math.Clamp(ShopTrade.Balance / Procurement.PiecePrice, 0, maximum);
            UpdatePrice();
        }, "wm-proc-filter"));
        quantity.RegisterCallback<ChangeEvent<int>>(DelegateSupport.ConvertDelegate<EventCallback<ChangeEvent<int>>>(
            (Action<ChangeEvent<int>>)(_ => UpdatePrice())));
        _refreshExchange = UpdatePrice;
        _refreshExchange();
        _exchange.SetVisible(true);
        ShopVisuals.Enter(dialog);
        quantity.Focus();
    }
}
