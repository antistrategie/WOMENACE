using System.Diagnostics;
using Il2CppMenace.Items;
using Jiangyu.Game.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

[DevVerb]
public static class Shop
{
    public static object Status()
    {
        var state = ShopSystem.Instance?.State;
        return new
        {
            unlocked = WorkshopAccess.IsUnlocked,
            rogueArmy = WorkshopAccess.Read(WorkshopAccess.RogueArmyUnlocked),
            sardis = ShopTrade.Balance,
            spent = state?.SardisSpent,
            affinity = state?.Affinity,
            level = state?.Level,
            outfit = state?.OutfitId,
            parts = string.Join(", ", WeaponClasses.All.SelectMany(WeaponParts.ForClass).Select(id =>
                id + "=" + Inventory.Owned?.GetInstanceCount(Templates.ById<CommodityTemplate>(id)))),
        };
    }

    [MutatingVerb]
    public static object Open() => new { ok = ShopSystem.Instance?.Open() == true };

    [MutatingVerb]
    public static object Buy(string itemId, int count = 1) => Trade(itemId, count, true);

    [MutatingVerb]
    public static object Sell(string itemId, int count = 1) => Trade(itemId, count, false);

    private static object Trade(string id, int count, bool buy)
    {
        var watch = Stopwatch.StartNew();
        var result = ShopSystem.Instance.Trade(new Dictionary<string, int> { [id] = count }, buy);
        return new { result.ok, result.error, elapsedMs = watch.Elapsed.TotalMilliseconds, sardis = ShopTrade.Balance };
    }

    [MutatingVerb]
    public static object Sardis(int count = 1000)
    {
        var template = Templates.ById<CommodityTemplate>(Kalina.CurrencyId);
        if (Inventory.Owned == null || template == null)
            return new { error = "no campaign or currency template" };
        var watch = Stopwatch.StartNew();
        var granted = 0;
        for (var i = 0; i < Math.Clamp(count, 0, 100000); i++)
            if (Inventory.AddItem(template) != null)
                granted++;
        return new { granted, elapsedMs = watch.Elapsed.TotalMilliseconds, sardis = ShopTrade.Balance };
    }

    [MutatingVerb]
    public static object Outfit(string id) => new { ok = ShopSystem.Instance?.SelectOutfit(id) == true };

    [MutatingVerb]
    public static object AffinityLevel(int level)
    {
        level = Math.Clamp(level, 1, Kalina.MaxLevel);
        ShopSystem.Instance.State.SardisSpent = (long)(level - 1) * Kalina.AffinityPerLevel * Kalina.SardisPerAffinityPoint;
        return Status();
    }

    [MutatingVerb]
    public static object View(string view = "buy")
    {
        var system = ShopSystem.Instance;
        if (system == null || !system.Open())
            return new { error = "shop is locked or unavailable" };
        if (view == "outfits")
            system.ShowOutfits();
        else if (view == "affinity")
            system.ShowAffinity();
        else
            system.SetTab(view != "sell");
        return new { ok = true };
    }

    public static object Count(string itemId)
    {
        var template = Templates.ById<BaseItemTemplate>(itemId);
        return new { itemId, count = template == null ? 0 : Inventory.Owned?.GetInstanceCount(template) ?? 0 };
    }
}
