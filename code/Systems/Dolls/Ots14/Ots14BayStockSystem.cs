using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Makes bay-slotted weapons read as USED to every vanilla stock surface.
//
// The bay holds guids while its items stay in stock (they sit in no
// container), so to vanilla they look free: the black market offered to sell
// them, and another unit's special-weapon dropdown counted them as unused
// and handed them out. Three guards restore the equipped-item semantics:
//
// - The black market's sell list drops bay items. The draw path is the choke
//   point: UpdateItemSlots receives the list each view renders, so stripping
//   there covers every rebuild order, and the sell-list accessors are
//   cleaned as well so no other consumer sees a bay item.
// - OwnedItems.GetUsers reports OTs-14 as the user of a bay-slotted
//   instance, once per instance, so the equipment dropdown's used counts and
//   used-by labels agree with the bay.
// - OwnedItems.GetUnusedInstance never hands out a bay-slotted instance: it
//   substitutes a genuinely free one, or null when the bay holds them all.
//   Anything that slips past regardless still resolves safely: equipping a
//   bay item evicts it from the bay (Bay.ResolveItem + Prune).
public sealed class Ots14BayStockSystem : JiangyuSystem
{
    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.BlackMarketUIScreen", "OnOpened", OnMarketOpened);
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.BlackMarketUIScreen", "UpdateItemSlots", OnMarketItemSlots);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.BlackMarketUIScreen", "GetSellItemList", OnMarketSellList);
        Context.Patches.Postfix("Il2CppMenace.Strategy.OwnedItems", "GetUsers", OnGetUsers);
        Context.Patches.Postfix("Il2CppMenace.Strategy.OwnedItems", "GetUnusedInstance", OnGetUnusedInstance);
        Context.Patches.Postfix("Il2CppMenace.Strategy.OwnedItems", "GetUnusedDefaultItemInstance", OnGetUnusedInstance);
    }

    // -- black market -------------------------------------------------------

    private void OnMarketOpened(PatchInfo info)
    {
        try
        {
            // A slot whose weapon was sold or equipped since the last visit
            // clears now, so its guid cannot shadow anything below.
            Bay.Prune(Context);
            var screen = (info.Instance as Il2CppObjectBase)?.TryCast<Il2CppMenace.UI.Strategy.BlackMarketUIScreen>();
            Strip(screen?.m_SellItems, "sell list at open");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay market open failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnMarketItemSlots(PatchInfo info)
    {
        try
        {
            if (info.Args == null || info.Args.Count < 1)
                return;
            var list = (info.Args[0] as Il2CppObjectBase)?.TryCast<Il2CppSystem.Collections.Generic.List<BaseItem>>();
            Strip(list, "view draw");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay market draw filter failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnMarketSellList(PatchInfo info)
    {
        try
        {
            // The getter hands out the backing list, so mutating it cleans
            // every consumer that pulls through the accessor.
            var list = (info.Result as Il2CppObjectBase)?.TryCast<Il2CppSystem.Collections.Generic.List<BaseItem>>();
            Strip(list, "sell list read");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay market sell filter failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Strip(Il2CppSystem.Collections.Generic.List<BaseItem> list, string where)
    {
        if (list == null)
            return;
        var slots = Bay.LoadoutOrNull(Context);
        if (slots == null)
            return;
        var removed = 0;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var guid = list[i]?.GetGuid();
            if (string.IsNullOrEmpty(guid) || Array.IndexOf(slots, guid) < 0)
                continue;
            list.RemoveAt(i);
            removed++;
        }
        if (removed > 0)
            Context.Log.Debug($"bay: kept {removed} bay weapon(s) off the black market ({where})");
    }

    // -- equipment dropdown counts ------------------------------------------

    // GetUsers answers "which leaders use these instances" off the container
    // linkage, which bay items lack: append her once per bay-slotted
    // instance in the query, so a 4/4-bayed template shows 4/4 used with her
    // name on the label instead of 0/4 free.
    private void OnGetUsers(PatchInfo info)
    {
        try
        {
            var users = (info.Result as Il2CppObjectBase)?.TryCast<Il2CppSystem.Collections.Generic.List<BaseUnitLeader>>();
            if (users == null || info.Args == null || info.Args.Count < 1)
                return;
            var slots = Bay.LoadoutOrNull(Context);
            if (slots == null)
                return;
            // The IReadOnlyList argument's runtime object is the backing
            // List (TryCast resolves against the object's own class); an
            // unexpected collection type just leaves the count vanilla.
            var items = (info.Args[0] as Il2CppObjectBase)?.TryCast<Il2CppSystem.Collections.Generic.List<Item>>();
            if (items == null)
                return;
            BaseUnitLeader her = null;
            var added = 0;
            for (var i = 0; i < items.Count; i++)
            {
                var guid = items[i]?.GetGuid();
                if (string.IsNullOrEmpty(guid) || Array.IndexOf(slots, guid) < 0)
                    continue;
                her ??= HerLeader();
                if (her == null)
                    return;
                users.Add(her);
                added++;
            }
            if (added > 0)
                Context.Log.Debug($"bay: reported her as user of {added} bay-slotted instance(s)");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay users report failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // A bay-slotted instance is not unused: substitute a genuinely free one
    // of the same template, or null when the bay holds every copy, so
    // vanilla equip flows cannot quietly grab a weapon off her arms.
    private void OnGetUnusedInstance(PatchInfo info)
    {
        try
        {
            var result = (info.Result as Il2CppObjectBase)?.TryCast<Item>();
            var guid = result?.GetGuid();
            if (string.IsNullOrEmpty(guid))
                return;
            var slots = Bay.LoadoutOrNull(Context);
            if (slots == null || Array.IndexOf(slots, guid) < 0)
                return;
            info.Result = FreeInstanceOf(result.GetTemplate(), slots);
            Context.Log.Debug($"bay: unused-instance lookup redirected off a bay-slotted '{result.GetID()}'");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay unused-instance guard failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private Item FreeInstanceOf(ItemTemplate template, string[] slots)
    {
        if (template == null)
            return null;
        var instances = (Jiangyu.Game.Strategy.Inventory.Owned?.GetInstances(template) as Il2CppObjectBase)
            ?.TryCast<Il2CppSystem.Collections.Generic.List<Item>>();
        for (var i = 0; instances != null && i < instances.Count; i++)
        {
            var candidate = instances[i];
            var guid = candidate?.GetGuid();
            if (string.IsNullOrEmpty(guid) || Bay.IsEquipped(candidate))
                continue;
            if (Array.IndexOf(slots, guid) < 0)
                return candidate;
        }
        return null;
    }

    private BaseUnitLeader HerLeader()
    {
        try
        {
            var leaders = Il2CppMenace.States.StrategyState.Get()?.Roster?.m_HiredLeaders;
            for (var i = 0; leaders != null && i < leaders.Count; i++)
                if (Affinity.CharacterTag(leaders[i]) == Bay.CharacterTag)
                    return leaders[i];
        }
        catch
        {
            // no strategy roster in this scene
        }
        return null;
    }
}
