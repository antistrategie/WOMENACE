using Il2CppMenace.States;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for the gift economy, invoked over the dev-loader bridge as e.g.
// {verb: "Gifts.Give", mutate: true}. The bridge and verb runner live only in the dev loader, so
// these are unreachable in a shipped mod, and the *.Dev.cs name keeps them out of release builds.
[DevVerb]
public static class Gifts
{
    // Top the player's inventory up with `count` of each gift commodity (default 5), for testing the
    // affinity system without grinding drops. Mutates the inventory, so it runs only with mutate:true.
    [MutatingVerb]
    public static object Give(int count = 5)
    {
        var owned = StrategyState.Get()?.OwnedItems;
        if (owned == null)
            return new { error = "no strategy state / owned items" };

        var granted = 0;
        var kinds = 0;
        foreach (var gift in GiftCatalog.All())
        {
            kinds++;
            for (var k = 0; k < count; k++)
            {
                owned.AddItem(gift, false);
                granted++;
            }
        }
        return new { ok = true, granted, kinds };
    }
}
