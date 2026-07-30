using Il2CppMenace.Items;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The shared transmog model: which outfit each doll RENDERS, decoupled from the armour she has
// equipped. Dolls equip vanilla armour for stats like any other leader. The outfit templates
// (armor.<doll>_*) are pure cosmetic carriers (model, icons, name) that no unit can equip, since
// their OnlyEquipableBy names only the wmgfl_transmog marker tag. TransmogSystem swaps the
// rendered body prefab to the selection and the picker on the unit window writes it. Selections
// persist per save slot through Context.State, keyed like affinity (Affinity.KeyForTag), so a
// character's choice survives Voymastina's form swap.
public static class Transmog
{
    // Resolved armor.<name>_default templates, keyed by outfit id. Hits only (Templates.Resolve),
    // so a probe that runs before the mod's templates register never pins a doll as non-transmog.
    private static readonly Dictionary<string, ArmorTemplate> DefaultProbeCache = new(StringComparer.Ordinal);

    // Memoised outfit lists per character. Only built once the character's default outfit template
    // resolves, and Unlocks.ByCharacter is compile-time static, so a cached list never changes
    // within a run: build once, hand back the same list (callers only read it).
    private static readonly Dictionary<string, IReadOnlyList<Option>> OptionsCache = new(StringComparer.Ordinal);

    // One picker choice: an outfit and the affinity level that unlocks it (0 = always available).
    public sealed class Option
    {
        public string ArmorId;
        public int UnlockLevel;
    }

    // The character's default outfit id (armor.<name>_default for a wmgfl_<name> tag), or null
    // when the tag is not one of our characters. A tag counts as a character exactly when its
    // derived default outfit template exists, so a new doll needs no registration here and marker
    // tags (wmgfl_transmog, wmgfl_class_*) never match.
    public static string DefaultFor(string characterTag)
    {
        if (characterTag == null || !characterTag.StartsWith("wmgfl_", StringComparison.Ordinal))
            return null;
        var id = $"armor.{characterTag["wmgfl_".Length..]}_default";
        return Templates.Resolve(id, DefaultProbeCache) != null ? id : null;
    }

    // The character's outfit choices in display order: the default first, then each Skins unlock
    // in level order (Unlocks entries are authored ascending). Memoised.
    public static IReadOnlyList<Option> OptionsFor(string characterTag)
    {
        if (characterTag == null)
            return Array.Empty<Option>();
        if (OptionsCache.TryGetValue(characterTag, out var cached))
            return cached;

        // A null default is not cached: the character's templates may simply not be registered yet.
        var def = DefaultFor(characterTag);
        if (def == null)
            return Array.Empty<Option>();

        var options = new List<Option> { new() { ArmorId = def, UnlockLevel = 0 } };
        foreach (var entry in Unlocks.EntriesFor(characterTag))
            if (entry.Feature == Unlocks.Feature.Skins && entry.Armors != null)
                foreach (var id in entry.Armors)
                    options.Add(new Option { ArmorId = id, UnlockLevel = entry.Level });
        OptionsCache[characterTag] = options;
        return options;
    }

    // The outfit a character renders: the saved selection, or the default when none is saved or
    // the saved id is not one of the character's outfits.
    public static string SelectionFor(ModContext context, string characterTag)
    {
        var def = DefaultFor(characterTag);
        if (def == null)
            return null;
        var key = Affinity.KeyForTag(characterTag);
        if (!context.State.Get<TransmogState>().Selections.TryGetValue(key, out var id) || id == null)
            return def;
        foreach (var option in OptionsFor(characterTag))
            if (option.ArmorId == id)
                return id;
        return def;
    }

    public static void SetSelection(ModContext context, string characterTag, string armorId)
    {
        var key = Affinity.KeyForTag(characterTag);
        if (key == 0)
            return;
        context.State.Get<TransmogState>().Selections[key] = armorId;
    }
}

// Persisted per-character outfit choice, keyed like AffinityState (Affinity.KeyForTag).
public sealed class TransmogState
{
    public Dictionary<int, string> Selections { get; set; } = [];
}
