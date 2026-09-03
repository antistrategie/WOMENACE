using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for the black market loop, invoked over the dev-loader bridge as e.g.
// {verb: "Trade.Restock", mutate: true}. The bridge and verb runner live only in the dev loader,
// so these are unreachable in a shipped mod, and the *.Dev.cs name keeps them out of releases.
// Not named Market: the verb runner resolves class short names and the SDK's Jiangyu.Game.Strategy
// classes win, so a mod class called Market would be unreachable behind the SDK's Market (whose
// Refresh is the bare restock, without the shelf report or the window refresh below).
[DevVerb]
public static class Trade
{
    private const string DefaultGoodsId = "commodity.valuable_minerals";

    // Restock the black market, exactly as redeeming a restock token does (the token calls
    // Restock(true, false): item timeouts tick down, the restocks-since-operation counter keeps
    // counting). Refreshes the market window when it is the active screen, so the new shelf shows
    // without closing and reopening it.
    [MutatingVerb]
    public static object Restock(bool decrementRemainingTime = true, bool resetRestockCounter = false)
    {
        var market = StrategyState.Get()?.BlackMarket;
        if (market == null)
            return new { error = "no strategy state / black market (load a campaign save first)" };

        var before = Shelf(market);
        market.Restock(decrementRemainingTime, resetRestockCounter);
        var after = Shelf(market);

        var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<BlackMarketUIScreen>();
        screen?.UpdateWindow();

        return new
        {
            ok = true,
            before = before.Count,
            after = after.Count,
            refreshed = screen != null,
            stock = string.Join(", ", after),
        };
    }

    // Everything on the shelf right now, regular stock then special offers, as template ids.
    public static object Stock()
    {
        var market = StrategyState.Get()?.BlackMarket;
        if (market == null)
            return new { error = "no strategy state / black market (load a campaign save first)" };
        var items = Shelf(market);
        return new { ok = true, count = items.Count, stock = string.Join(", ", items) };
    }

    // Top the inventory up with trade goods to sell at the market, so dossiers and restock tokens
    // can be bought without a salvage grind. The default commodity trades at 80 a unit.
    [MutatingVerb]
    public static object Goods(int count = 30, string commodityId = DefaultGoodsId)
    {
        var owned = StrategyState.Get()?.OwnedItems;
        if (owned == null)
            return new { error = "no strategy state / owned items (load a campaign save first)" };
        var template = Templates.ById<CommodityTemplate>(commodityId);
        if (template == null)
            return new { error = $"unknown commodity template '{commodityId}'" };

        var granted = 0;
        for (var i = 0; i < Math.Clamp(count, 0, 200); i++)
            if (owned.AddItem(template, false, false) != null)
                granted++;
        var unitValue = template.GetTradeValue();
        return new { ok = true, commodity = commodityId, granted, unitValue, totalValue = granted * unitValue };
    }

    // Open the black market screen, as the ship navigation would.
    [MutatingVerb]
    public static object Open()
    {
        var manager = UIManager.Get();
        if (manager == null)
            return new { error = "no ui manager" };
        var screen = manager.OpenScreen(BlackMarketUIScreen.PREFAB_NAME);
        return new { ok = screen != null };
    }

    private static List<string> Shelf(Il2CppMenace.Strategy.BlackMarket market)
    {
        var ids = new List<string>();
        foreach (var specialOffers in new[] { false, true })
        {
            var buffer = new Il2CppSystem.Collections.Generic.List<BaseItem>();
            market.GetInstances(buffer, specialOffers);
            for (var i = 0; i < buffer.Count; i++)
            {
                var id = buffer[i]?.GetBaseItemTemplate()?.GetID();
                if (id != null)
                    ids.Add(specialOffers ? id + " (offer)" : id);
            }
        }
        return ids;
    }
}
