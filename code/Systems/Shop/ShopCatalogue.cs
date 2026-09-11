using Il2CppMenace.Items;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Each view or transaction gets an immutable catalogue. Gift values are read from the live
// templates when it is created, so prices need no separate refresh lifecycle.
public sealed class ShopCatalogue
{
    public const int PartPrice = 100;

    private static readonly (bool Buy, LocalisedText Name, Func<IEnumerable<Group>> Groups)[] Definitions =
    {
        (true, new LocalisedText("WOMENACE::ui/procurement/pieces", "Collapse Pieces"), PieceGroups),
        (true, new LocalisedText("WOMENACE::ui/workshop/weapon_parts", "Weapon Parts"), PartGroups),
        (false, new LocalisedText("WOMENACE::ui/kalina/gifts", "Gifts"), GiftGroups),
    };

    public IReadOnlyList<Category> Categories { get; }
    public IReadOnlyDictionary<string, Entry> Items { get; }

    private ShopCatalogue(IEnumerable<Category> categories)
    {
        Categories = categories.ToList();
        var items = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var category in Categories)
            foreach (var group in category.Groups)
                foreach (var entry in group.Items)
                    items.Add(entry.Template.GetID(), entry);
        Items = items;
    }

    public static ShopCatalogue Create(bool buy)
        => new(Definitions.Where(definition => definition.Buy == buy)
            .Select(definition => new Category(definition.Name, definition.Groups().ToList())));

    private static IEnumerable<Group> PieceGroups()
    {
        if (Templates.ById<CommodityTemplate>(Procurement.PieceId) is { } template)
            yield return new Group(null, new[] { new Entry(template, Procurement.PiecePrice) });
    }

    private static IEnumerable<Group> PartGroups()
    {
        foreach (var weaponClass in WeaponClasses.All)
        {
            var entries = new List<Entry>();
            foreach (var id in WeaponParts.ForClass(weaponClass))
                if (Templates.ById<CommodityTemplate>(id) is { } template)
                    entries.Add(new Entry(template, PartPrice));
            yield return new Group(WeaponClasses.Code(weaponClass), entries);
        }
    }

    private static IEnumerable<Group> GiftGroups()
    {
        // Gift sale prices use the same TradeValue as their base Doll affinity.
        yield return new Group(null, GiftCatalog.All().Select(template => new Entry(template, template.TradeValue)).ToList());
    }

    public long Total(IReadOnlyDictionary<string, int> selection)
    {
        long total = 0;
        foreach (var (id, count) in selection)
        {
            if (count == 0)
                continue;
            if (count < 0 || !Items.TryGetValue(id, out var entry) || entry.Price <= 0)
                return -1;
            total += (long)count * entry.Price;
            if (total > int.MaxValue)
                return -1;
        }
        return total;
    }

    public sealed class Category(LocalisedText name, IReadOnlyList<Group> groups)
    {
        public readonly LocalisedText Name = name;
        public readonly IReadOnlyList<Group> Groups = groups;
    }

    public sealed class Group(string name, IReadOnlyList<Entry> items)
    {
        public readonly string Name = name;
        public readonly IReadOnlyList<Entry> Items = items;
    }

    public sealed class Entry(BaseItemTemplate template, int price)
    {
        public readonly BaseItemTemplate Template = template;
        public readonly int Price = price;
    }
}
