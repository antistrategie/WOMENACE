using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The Doll button in the item list's filter bar shows only doll weapons. The filter is
// the KDL clone wmgfl_doll_weapons, inserted first in StrategyConfig.ItemFilters, which
// the bar reads and narrows to the filters available for the item slot.
//
// ItemSlots and Tags on ItemFilterTemplate are Odin-serialised and cannot be authored,
// so both verdicts are replaced here for this one filter. The clone inherits the
// assault rifle filter's slots (main weapon only), so availability is widened to the
// special-weapon slot, where most SSR weapons sit. An item matches when it carries the
// wmgfl_doll_weapon tag.
public sealed class DollWeaponFilterSystem : JiangyuSystem
{
    private const string FilterId = "wmgfl_doll_weapons";
    private const string DollWeaponTag = "wmgfl_doll_weapon";

    // The filter's pointer once seen. MatchesItemFilter runs once per filter per listed
    // item, so the id string is read only until the clone is recognised. Templates are
    // rebuilt on apply, which drops the cache.
    private IntPtr _filter;

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Items.ItemFilterTemplate", "MatchesItemFilter", OnMatches);
        Context.Patches.Postfix("Il2CppMenace.Items.ItemFilterTemplate", "IsAvailableForSlot", OnAvailable);
    }

    public override void OnTemplatesApplied() => _filter = IntPtr.Zero;

    private void OnAvailable(PatchInfo info)
    {
        try
        {
            if (!IsDollFilter(info) || info.Args is not { Count: > 0 })
                return;
            var slot = info.Args[0] is ItemSlot boxed ? boxed : (ItemSlot)(int)info.Args[0];
            if (slot is ItemSlot.InfantryWeapon or ItemSlot.InfantrySpecial)
                info.Result = true;
        }
        catch (Exception ex)
        {
            Context.Log.Error($"doll weapon filter: {ex}");
        }
    }

    private void OnMatches(PatchInfo info)
    {
        try
        {
            if (!IsDollFilter(info))
                return;
            var item = (info.Args is { Count: > 0 } ? info.Args[0] as Il2CppObjectBase : null)?.TryCast<BaseItemTemplate>();
            info.Result = IsDollWeapon(item);
        }
        catch (Exception ex)
        {
            Context.Log.Error($"doll weapon filter: {ex}");
        }
    }

    private bool IsDollFilter(PatchInfo info)
    {
        if (info.Instance is not Il2CppObjectBase instance)
            return false;
        if (_filter != IntPtr.Zero)
            return instance.Pointer == _filter;
        if (instance.TryCast<ItemFilterTemplate>()?.GetID() != FilterId)
            return false;
        _filter = instance.Pointer;
        return true;
    }

    private static bool IsDollWeapon(BaseItemTemplate item)
    {
        var tags = item?.Tags;
        for (var i = 0; tags != null && i < tags.Count; i++)
            if (tags[i]?.GetID() == DollWeaponTag)
                return true;
        return false;
    }
}
