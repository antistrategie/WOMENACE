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
    // Default outfit per character, rendered until the player picks another. Explicit per
    // character (like Unlocks) so a future doll with unconventional ids still routes correctly.
    private static readonly Dictionary<string, string> DefaultOutfits = new(StringComparer.Ordinal)
    {
        ["wmgfl_cheyanne"] = "armor.cheyanne_default",
        ["wmgfl_helen"] = "armor.helen_default",
        ["wmgfl_leva"] = "armor.leva_default",
        ["wmgfl_lewis"] = "armor.lewis_default",
        ["wmgfl_makiatto"] = "armor.makiatto_default",
        ["wmgfl_papasha"] = "armor.papasha_default",
        ["wmgfl_sextans"] = "armor.sextans_default",
        ["wmgfl_soppo"] = "armor.soppo_default",
        ["wmgfl_springfield"] = "armor.springfield_default",
        ["wmgfl_vector"] = "armor.vector_default",
        ["wmgfl_voymastina"] = "armor.voymastina_default",
    };

    // Memoised outfit lists per character. DefaultOutfits and Unlocks.ByCharacter are compile-time
    // static, so a character's options never change within a run: build once, hand back the same
    // list (callers only read it).
    private static readonly Dictionary<string, IReadOnlyList<Option>> OptionsCache = new(StringComparer.Ordinal);

    // One picker choice: an outfit and the affinity level that unlocks it (0 = always available).
    public sealed class Option
    {
        public string ArmorId;
        public int UnlockLevel;
    }

    // The character's default outfit id, or null when the tag is not one of our characters.
    public static string DefaultFor(string characterTag)
        => characterTag != null && DefaultOutfits.TryGetValue(characterTag, out var id) ? id : null;

    // The character's outfit choices in display order: the default first, then each Skins unlock
    // in level order (Unlocks entries are authored ascending). Memoised.
    public static IReadOnlyList<Option> OptionsFor(string characterTag)
    {
        if (characterTag == null)
            return Array.Empty<Option>();
        if (OptionsCache.TryGetValue(characterTag, out var cached))
            return cached;

        var options = new List<Option>();
        var def = DefaultFor(characterTag);
        if (def != null)
        {
            options.Add(new Option { ArmorId = def, UnlockLevel = 0 });
            foreach (var entry in Unlocks.EntriesFor(characterTag))
                if (entry.Feature == Unlocks.Feature.Skins && entry.Armors != null)
                    foreach (var id in entry.Armors)
                        options.Add(new Option { ArmorId = id, UnlockLevel = entry.Level });
        }
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
