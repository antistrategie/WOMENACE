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
        // A deployable mech form (gated in VoymastinaFormSwapSystem).
        Mech,
        // An SSR special weapon granted to the shared inventory at this level. Weapons lists the
        // weapon template ids. Equippable by anyone, but the owner-only bonus lives in SsrImprintSystem.
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
        public string[] Weapons = Array.Empty<string>();
        public LocalisedText Title;
    }

    // The per-character unlock map. Skin outfits listed here are pure transmog carriers (no unit
    // can equip them); the transmog picker offers each once the character reaches its level.
    public static readonly Dictionary<string, Entry[]> ByCharacter = new(StringComparer.Ordinal)
    {
        ["wmgfl_voymastina"] = new[]
        {
            new Entry { Level = 2, Feature = Feature.Skins, Armors = new[] { "armor.voymastina_erwin" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_voymastina/lv2", "Outfit(s): Erwin") },
            new Entry { Level = 4, Feature = Feature.Mech, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_voymastina/lv4", "Alternate form: Sinbreaker") },
        },
        ["wmgfl_leva"] = new[]
        {
            new Entry { Level = 2, Feature = Feature.Skins, Armors = new[] { "armor.leva_diamond_flower" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_leva/lv2", "Outfit(s): Diamond Flower") },
        },
        ["wmgfl_makiatto"] = new[]
        {
            new Entry { Level = 2, Feature = Feature.Skins, Armors = new[] { "armor.makiatto_ballroom" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_makiatto/lv2", "Outfit(s): Ballroom Interlude") },
            new Entry { Level = 3, Feature = Feature.Weapon, Weapons = new[] { "specialweapon.makiatto_ssr" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_makiatto/lv3", "SSR Weapon: Bittersweet Caramel") },
            new Entry { Level = 4, Feature = Feature.Skins, Armors = new[] { "armor.makiatto_steamy_vacance" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_makiatto/lv4", "Outfit(s): Steamy Vacance") },
        },
        ["wmgfl_sextans"] = new[]
        {
            new Entry { Level = 2, Feature = Feature.Skins, Armors = new[] { "armor.sextans_nocte" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_sextans/lv2", "Outfit(s): Nocte Bewitchment") },
        },
        ["wmgfl_vector"] = new[]
        {
            new Entry { Level = 2, Feature = Feature.Skins, Armors = new[] { "armor.vector_vivi" }, Title = new LocalisedText("WOMENACE::ui/affinity/wmgfl_vector/lv2", "Outfit(s): Vivi Sometimes Hides Her Molotovs") },
        },
    };

    public static IReadOnlyList<Entry> EntriesFor(string characterTag)
        => characterTag != null && ByCharacter.TryGetValue(characterTag, out var entries)
            ? entries
            : Array.Empty<Entry>();

    // The SSR weapon ids a character has unlocked at this level (every Weapon entry at or below it).
    // Granted to the shared inventory, equippable by anyone regardless of who unlocked it.
    public static IEnumerable<string> UnlockedWeapons(string characterTag, int level)
    {
        foreach (var entry in EntriesFor(characterTag))
            if (entry.Feature == Feature.Weapon && level >= entry.Level && entry.Weapons != null)
                foreach (var id in entry.Weapons)
                    yield return id;
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

    // A popover row: the row text at a level. Text is null when nothing is authored for that level
    // (rendered as a dim placeholder).
    public readonly struct Row
    {
        public readonly int Level;
        public readonly string Text;

        public Row(int level, string text)
        {
            Level = level;
            Text = text;
        }
    }

    // The per-level rows for a character's badge popover: levels 1..MaxLevel, each level carrying the
    // Title of its entry (localised, falling back to the authored text) and the rest left as a null
    // placeholder.
    public static List<Row> RowsFor(string characterTag)
    {
        var byLevel = new Dictionary<int, string>();
        foreach (var entry in EntriesFor(characterTag))
            if (entry.Title.HasText)
                byLevel[entry.Level] = entry.Title.Resolve();

        var rows = new List<Row>(Affinity.MaxLevel);
        for (var level = 1; level <= Affinity.MaxLevel; level++)
            rows.Add(new Row(level, byLevel.TryGetValue(level, out var text) ? text : null));
        return rows;
    }
}
