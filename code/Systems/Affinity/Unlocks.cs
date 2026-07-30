using Jiangyu.Sdk;

namespace WOMENACE.Code;

// What each character unlocks as their affinity rises, declared per character. This is the single
// source the gameplay gates (skin grants, the Sinbreaker form swap) AND the badge popover read, so
// the level a feature unlocks at and the level the popover advertises can never disagree.
//
// Each Entry has a level, an optional gameplay Feature (Skins/Mech, or None for a flavour-only row),
// and its own Title. The Title is per character and per level: give every level whatever text you
// like, whether or not it unlocks anything mechanically. A character absent from the map (Cheyanne,
// Lewis) has no entries: default outfit only, no mech, an all-placeholder popover.
public static class Unlocks
{
    public enum Feature
    {
        // A flavour-only row: shows its Title in the popover, unlocks nothing mechanically.
        None,
        // Alternate outfits. Armors lists the transmog outfit ids unlocked at this level. The
        // transmog picker greys them out until the level is reached.
        Skins,
        // A deployable mech form (gated in FormSwapSystem).
        Mech,
        // An SSR special weapon granted to the shared inventory at this level. The weapon id is not
        // authored: it is weapon.<doll>_ssr by the Calibration convention. Equippable by anyone, but
        // the owner-only bonus lives in SsrImprintSystem.
        Weapon,
    }

    // One unlock at a level: its gameplay Feature (if any), the data that feature needs, and the
    // popover Title for that level. Title is a LocalisedText (explicit translation key + English) so
    // it is BOTH translatable (the compiler extracts the literal into the POT) and the single source
    // the popover reads.
    public sealed class Entry
    {
        public int Level;
        public Feature Feature = Feature.None;
        public string[] Armors = Array.Empty<string>();
        public LocalisedText Title;
    }

    // The per-character unlock map. Skin outfits listed here are pure transmog carriers (no unit
    // can equip them); the transmog picker offers each once the character reaches its level.
    public static readonly Dictionary<string, Entry[]> ByCharacter = new(StringComparer.Ordinal)
    {
        ["wmgfl_voymastina"] = new[]
        {
            new Entry { Level = 2, Feature = Feature.Skins, Armors = new[] { "armor.voymastina_erwin" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_voymastina/lv2", "Outfit: Erwin") },
            new Entry { Level = 4, Feature = Feature.Mech, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_voymastina/lv4", "Alternate form: Sinbreaker") },
        },
        ["wmgfl_leva"] = new[]
        {
            new Entry { Level = 2, Feature = Feature.Skins, Armors = new[] { "armor.leva_diamond_flower" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_leva/lv2", "Outfit: Diamond Flower") },
        },
        ["wmgfl_makiatto"] = new[]
        {
            new Entry { Level = 2, Feature = Feature.Skins, Armors = new[] { "armor.makiatto_ballroom" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_makiatto/lv2", "Outfit: Ballroom Interlude") },
            new Entry { Level = 3, Feature = Feature.Weapon, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_makiatto/lv3", "SSR Weapon: Bittersweet Caramel") },
            new Entry { Level = 4, Feature = Feature.Skins, Armors = new[] { "armor.makiatto_steamy_vacance" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_makiatto/lv4", "Outfit: Steamy Vacance") },
        },
        ["wmgfl_sextans"] = new[]
        {
            new Entry { Level = 2, Feature = Feature.Skins, Armors = new[] { "armor.sextans_nocte" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_sextans/lv2", "Outfit: Nocte Bewitchment") },
            new Entry { Level = 3, Feature = Feature.Weapon, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_sextans/lv3", "SSR Weapon: Twilight Rose") },
        },
        ["wmgfl_vector"] = new[]
        {
            new Entry { Level = 2, Feature = Feature.Skins, Armors = new[] { "armor.vector_vivi" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_vector/lv2", "Outfit: Vivi Sometimes Hides Her Molotovs") },
            new Entry { Level = 3, Feature = Feature.Weapon, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_vector/lv3", "SSR Weapon: Banshee's Whisper") },
        },
        ["wmgfl_soppo"] = new[]
        {
            new Entry { Level = 2, Feature = Feature.Skins, Armors = new[] { "armor.soppo_redline" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_soppo/lv2", "Outfit: Redline Racer") },
            new Entry { Level = 3, Feature = Feature.Weapon, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_soppo/lv3", "SSR Weapon: Skysunderer's Howl") },
        },
        ["wmgfl_helen"] = new[]
        {
            new Entry { Level = 2, Feature = Feature.Skins, Armors = new[] { "armor.helen_starlit_waltz" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_helen/lv2", "Outfit: Starlit Waltz") },
        },
        ["wmgfl_springfield"] = new[]
        {
            new Entry { Level = 2, Feature = Feature.Skins, Armors = new[] { "armor.springfield_fragrance" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_springfield/lv2", "Outfit: Enjoy the Fragrance") },
        },
        ["wmgfl_robella"] = new[]
        {
            new Entry { Level = 2, Feature = Feature.Skins, Armors = new[] { "armor.robella_future_navigator" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_robella/lv2", "Outfit: Future Navigator") },
            new Entry { Level = 4, Feature = Feature.Skins, Armors = new[] { "armor.robella_enforcer" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_robella/lv4", "Outfit: Enforcer of the Law") },
        },
    };

    public static IReadOnlyList<Entry> EntriesFor(string characterTag)
        => characterTag != null && ByCharacter.TryGetValue(characterTag, out var entries)
            ? entries
            : Array.Empty<Entry>();

    // The SSR weapon ids a character has unlocked at this level (one per Weapon entry at or below
    // it). Granted to the shared inventory, equippable by anyone regardless of who unlocked it.
    public static IEnumerable<string> UnlockedWeapons(string characterTag, int level)
    {
        foreach (var entry in EntriesFor(characterTag))
            if (entry.Feature == Feature.Weapon && level >= entry.Level)
                yield return Calibration.SsrWeaponIdFor(characterTag);
    }

    // The SSR weapon id this character's Weapon unlock grants, or null when she has no SSR unlock.
    // The id itself is the Calibration convention (weapon.<doll>_ssr); only its EXISTENCE is authored
    // here, so the unlock and its consumers can never drift.
    public static string SsrWeaponFor(string characterTag)
    {
        foreach (var entry in EntriesFor(characterTag))
            if (entry.Feature == Feature.Weapon)
                return Calibration.SsrWeaponIdFor(characterTag);
        return null;
    }

    // The level at which this character's mech form unlocks, or 0 if they have no mech.
    public static int MechLevel(string characterTag)
    {
        foreach (var entry in EntriesFor(characterTag))
            if (entry.Feature == Feature.Mech)
                return entry.Level;
        return 0;
    }

    public static bool HasMech(string characterTag) => MechLevel(characterTag) > 0;

    public static bool MechUnlocked(string characterTag, int level)
    {
        var unlockLevel = MechLevel(characterTag);
        return unlockLevel > 0 && level >= unlockLevel;
    }

    // The unlock entries a character has at or below a level, newest-level first, for the affinity
    // tooltip's UNLOCKS section. Only entries with authored text (a real reward line) are returned.
    public static IEnumerable<Entry> RewardEntries(string characterTag)
    {
        foreach (var entry in EntriesFor(characterTag))
            if (entry.Title.HasText)
                yield return entry;
    }
}
