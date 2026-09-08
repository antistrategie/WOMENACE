using Il2CppMenace.Items;
using Jiangyu.Game.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

public static class ShopTrade
{
    public static int Balance => Inventory.Owned is { } owned
        && Templates.ById<CommodityTemplate>(Kalina.CurrencyId) is { } currency
            ? owned.GetInstanceCount(currency) : 0;

    public static (bool ok, string error) Run(IReadOnlyDictionary<string, int> selection, bool buy, KalinaState state, IModLog log = null)
    {
        if (!WorkshopAccess.IsUnlocked)
            return (false, "Workshop is locked.");
        var catalogue = ShopCatalogue.Create(buy);
        var total = catalogue.Total(selection);
        if (total <= 0)
            return (false, "Select items first.");
        var currency = Templates.ById<CommodityTemplate>(Kalina.CurrencyId);
        if (currency == null || Inventory.Owned == null)
            return (false, "Sardis Gold is unavailable.");
        if (buy && (total > Balance || state.SardisSpent > long.MaxValue - total))
            return (false, "Not enough Sardis.");
        if (!buy && total > int.MaxValue - (long)Balance)
            return (false, "Sardis balance is full.");

        var inputs = new List<BaseItem>();
        var outputs = new List<(BaseItemTemplate Template, int Count)>();
        var inventory = new InventoryStock();
        // Resolve only the selected template stacks. A large Sardis balance must not multiply the
        // cost of enumerating unrelated inventory once for every coin or selected gift.
        foreach (var (id, count) in selection)
        {
            if (count == 0)
                continue;
            if (!catalogue.Items.TryGetValue(id, out var entry))
                return (false, "Item is unavailable.");
            var template = entry.Template;
            if (buy)
                outputs.Add((template, count));
            else
            {
                var stock = inventory.Available(template, count).ToList();
                if (stock.Count != count)
                    return (false, "Selected stock is no longer available.");
                inputs.AddRange(stock);
            }
        }
        if (buy)
        {
            inputs.AddRange(inventory.Available(currency, (int)total));
            if (inputs.Count != total)
                return (false, "Not enough Sardis.");
        }
        else
            outputs.Add((currency, (int)total));

        var result = InventoryExchange.Run(inputs, outputs, log, inventory);
        if (result.ok && buy)
            state.SardisSpent += total;
        return result;
    }
}
