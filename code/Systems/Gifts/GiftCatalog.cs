using Il2CppMenace.Items;

namespace WOMENACE.Code;

// The single source of "what counts as a gift": the commodity templates carrying the gift tag.
// The drop roller (GiftDropSystem), the gift modal (AffinitySystem) and the dev give-gifts verb all
// read this, so the gift contract (which tag marks a gift) lives in one place rather than being
// rescanned, slightly differently, by each.
public static class GiftCatalog
{
    public const string Tag = "wmgfl_gift";

    private static List<CommodityTemplate> _gifts;

    // Every gift commodity, discovered once. An EMPTY scan is not cached, so a call made before the
    // commodity templates are registered retries on the next call rather than sticking at "no gifts"
    // for the session. Only a successful (non-empty) scan is cached.
    public static IReadOnlyList<CommodityTemplate> All()
    {
        if (_gifts != null)
            return _gifts;

        var found = new List<CommodityTemplate>();
        try
        {
            foreach (var template in Templates.All<CommodityTemplate>())
                if (HasGiftTag(template))
                    found.Add(template);
        }
        catch { }

        if (found.Count > 0)
            _gifts = found;
        return found;
    }

    private static bool HasGiftTag(CommodityTemplate template)
    {
        try
        {
            var tags = template.Tags;
            if (tags == null)
                return false;
            for (var i = 0; i < tags.Count; i++)
            {
                var name = tags[i]?.name;
                if (!string.IsNullOrEmpty(name) && name.Contains(Tag))
                    return true;
            }
        }
        catch { }
        return false;
    }
}
