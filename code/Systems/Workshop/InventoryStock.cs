using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMenace.Items;
using Il2CppMenace.Strategy;
using Jiangyu.Game.Strategy;

namespace WOMENACE.Code;

// A stock read shares the same equipped and stashed-form exclusions across all its templates.
internal sealed class InventoryStock
{
    private readonly OwnedItems _owned = Inventory.Owned;
    private readonly Dictionary<string, int> _reserved = new(StringComparer.Ordinal);
    private readonly HashSet<IntPtr> _equipped = [];
    private readonly List<(BaseUnitLeader Leader, Item Item)> _equippedItems = [];
    public IReadOnlyList<(BaseUnitLeader Leader, Item Item)> EquippedItems => _equippedItems;

    public InventoryStock()
    {
        foreach (var id in FormSwapSystem.Instance?.StashedItemTemplateIds() ?? Array.Empty<string>())
            _reserved[id] = _reserved.GetValueOrDefault(id) + 1;
        foreach (var leader in Leaders.Hired() ?? [])
            foreach (var item in Leaders.EquippedItems(leader) ?? [])
                if (item != null)
                {
                    _equipped.Add(item.Pointer);
                    _equippedItems.Add((leader, item));
                }
    }

    public IEnumerable<BaseItem> Available(BaseItemTemplate template, int limit = int.MaxValue)
    {
        if (_owned == null || template == null || limit <= 0
            || !_owned.GetRawInstances().TryGetValue(template, out var items))
            yield break;
        var reserved = _reserved.GetValueOrDefault(template.GetID());
        var count = items.Count;
        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            if (item == null || _equipped.Contains(item.Pointer) || item.GetContainer() != null || item.IsTemporary())
                continue;
            if (reserved > 0)
            {
                reserved--;
                continue;
            }
            yield return item;
            if (--limit == 0)
                yield break;
        }
    }

    public IEnumerable<Item> Weapons(Func<WeaponTemplate, bool> include)
    {
        if (_owned == null)
            yield break;
        foreach (var weapon in WeaponTemplates(_owned))
        {
            if (!include(weapon))
                continue;
            foreach (var item in Available(weapon))
                if (item.TryCast<Item>() is { } instance)
                    yield return instance;
        }
    }

    public static IEnumerable<WeaponTemplate> WeaponTemplates(OwnedItems owned)
    {
        if (owned == null)
            yield break;
        // Copy template keys natively, then index the array. This avoids boxed Il2Cpp
        // enumerators and never materialises currency or other non-weapon instances.
        var keys = owned.GetRawInstances().Keys;
        var templates = new Il2CppReferenceArray<BaseItemTemplate>(keys.Count);
        keys.CopyTo(templates, 0);
        for (var i = 0; i < templates.Length; i++)
            if (templates[i]?.TryCast<WeaponTemplate>() is { } weapon)
                yield return weapon;
    }

    public bool ContainsAll(IReadOnlyList<BaseItem> items)
    {
        var groups = new Dictionary<IntPtr, (BaseItemTemplate Template, HashSet<IntPtr> Items)>();
        var unique = new HashSet<IntPtr>();
        foreach (var item in items)
        {
            var template = item?.GetBaseItemTemplate();
            if (template == null || !unique.Add(item.Pointer))
                return false;
            if (!groups.TryGetValue(template.Pointer, out var group))
                groups[template.Pointer] = group = (template, []);
            group.Items.Add(item.Pointer);
        }
        foreach (var group in groups.Values)
        {
            foreach (var item in Available(group.Template))
            {
                group.Items.Remove(item.Pointer);
                if (group.Items.Count == 0)
                    break;
            }
            if (group.Items.Count != 0)
                return false;
        }
        return true;
    }
}
