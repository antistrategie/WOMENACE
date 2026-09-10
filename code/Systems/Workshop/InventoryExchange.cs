using Il2CppMenace.Items;
using Jiangyu.Game.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

internal static class InventoryExchange
{
    // Prepare outputs before consuming stock. Rollback restores the original objects to
    // preserve their GUIDs.
    public static (bool ok, string error) Run(IReadOnlyList<BaseItem> inputs,
        IReadOnlyList<(BaseItemTemplate Template, int Count)> outputs, IModLog log = null, InventoryStock stock = null)
    {
        var owned = Inventory.Owned;
        if (owned == null)
            return (false, Locale.Text("WOMENACE::ui/workshop/no_inventory", "No campaign inventory."));
        stock ??= new InventoryStock();
        if (!stock.ContainsAll(inputs))
            return (false, Locale.Text("WOMENACE::ui/workshop/stock_unavailable", "Selected stock is no longer available."));
        if (outputs.Any(output => output.Template == null || output.Count < 1))
            return (false, Locale.Text("WOMENACE::ui/workshop/template_unavailable", "Item template unavailable."));

        var added = new List<BaseItem>();
        var removed = new List<BaseItem>();
        try
        {
            foreach (var (template, count) in outputs)
                for (var i = 0; i < count; i++)
                {
                    var item = Inventory.AddItem(template);
                    if (item == null)
                        throw new InvalidOperationException("Could not create the item.");
                    added.Add(item);
                }
            Remove(inputs, removed);
            return (true, null);
        }
        catch (Exception ex)
        {
            log?.Error($"Inventory exchange failed: {ex}");
            // Attempt both rollback steps even if native inventory access fails in either one.
            try
            {
                Restore(removed);
            }
            catch (Exception rollbackError)
            {
                log?.Error($"Inventory exchange could not restore inputs: {rollbackError}");
            }
            try
            {
                Remove(added, new List<BaseItem>());
            }
            catch (Exception rollbackError)
            {
                log?.Error($"Inventory exchange could not remove outputs: {rollbackError}");
            }
            return (false, Locale.Text("WOMENACE::ui/workshop/exchange_failed", "The exchange could not be completed."));
        }
    }

    private static void Remove(IReadOnlyList<BaseItem> items, List<BaseItem> removed)
    {
        var groups = new Dictionary<IntPtr, (BaseItemTemplate Template, List<BaseItem> Items)>();
        foreach (var item in items)
        {
            var template = item.GetBaseItemTemplate();
            if (!groups.TryGetValue(template.Pointer, out var group))
                groups[template.Pointer] = group = (template, []);
            group.Items.Add(item);
        }
        var instances = Inventory.Owned.GetRawInstances();
        foreach (var group in groups.Values)
        {
            if (group.Items.Count > 1 && group.Template.TryCast<CommodityTemplate>() != null
                && instances.TryGetValue(group.Template, out var list)
                && (RemoveRun(list, group.Items, 0) || RemoveRun(list, group.Items, list.Count - group.Items.Count)))
            {
                removed.AddRange(group.Items);
                continue;
            }
            foreach (var item in group.Items)
            {
                if (!Inventory.RemoveItem(item))
                    throw new InvalidOperationException("Selected stock changed.");
                removed.Add(item);
            }
        }
    }

    private static bool RemoveRun(Il2CppSystem.Collections.Generic.List<BaseItem> stock,
        IReadOnlyList<BaseItem> items, int start)
    {
        if (start < 0 || items.Count > stock.Count - start)
            return false;
        for (var i = 0; i < items.Count; i++)
            if (stock[start + i]?.Pointer != items[i].Pointer)
                return false;
        // Native RemoveItem (RVA 0x5AB0C0) has no side effects for these commodities beyond
        // list removal. Batch a payment at the front, or newly minted rollback outputs at
        // the end, so spending thousands of Sardis moves the remaining pointers only once.
        stock.RemoveRange(start, items.Count);
        return true;
    }

    public static void Restore(IEnumerable<BaseItem> items)
    {
        // OwnedItems.RemoveItem(BaseItem), RVA 0x5AB0C0, removes from the template list and
        // calls TryRemoveVehicle. It does not change seen items or unloadable instances.
        // Rollback inputs are weapons or commodities. Restoring their original objects keeps
        // their GUIDs. Vehicle inputs require OwnedItems to rebuild the strategy vehicle registry.
        var instances = Inventory.Owned.GetRawInstances();
        foreach (var item in items)
        {
            var template = item.GetBaseItemTemplate();
            if (!instances.TryGetValue(template, out var list))
                instances[template] = list = new Il2CppSystem.Collections.Generic.List<BaseItem>();
            list.Add(item);
        }
    }
}
