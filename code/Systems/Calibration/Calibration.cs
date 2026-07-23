using Il2CppMenace.Items;
using Il2CppMenace.Strategy;

namespace WOMENACE.Code;

// The shared weapon-calibration model: the id conventions and the component schedule every
// calibration consumer reads (CalibrationSystem, the affinity badge popover, the dev verbs).
// A doll's weapon calibrates from rank 0 (the base template) to rank 6 by merging in freshly
// crafted duplicates at the workshop. The rank templates are hand-authored KDL clones named
// <base>_r<N>, a weapon's component commodity is commodity.wmgfl_component_<weapon tail> and its
// duplicate recipe is blueprint.wmgfl_<weapon tail>_duplicate, so everything derives from the
// weapon id and no second registry can drift out of step with the templates.
public static class Calibration
{
    public const int MaxRank = 6;

    // Rank-marker colours (baked into the weapon name, matching theme.uss). Calibrated ranks read in
    // the game's gold; the base rank (R0) reads in a muted grey so it shows as "uncalibrated" rather
    // than competing with the gold. CalibrationSystem enables rich text on the labels that show the
    // name (equipped slot, tooltip, weapon-select flyout) so these render instead of raw tags.
    public const string RankMarkerColor = "#E9CB4F";
    public const string BaseRankMarkerColor = "#8C8574";

    // A weapon's display name with its calibration rank appended, e.g. "WA 2000 <color=#E9CB4F>R3</color>".
    // R0 (the base) gets a grey marker; ranks 1-6 get the gold one.
    public static string RankedName(string baseName, int rank)
        => rank <= 0
            ? $"{baseName} <color={BaseRankMarkerColor}>R0</color>"
            : $"{baseName} <color={RankMarkerColor}>R{rank}</color>";

    // A weapon name with any baked rank marker stripped, for surfaces that show the rank separately
    // (the calibration list draws its own R-badge, so its name must not also carry the marker).
    public static string CleanName(string name)
    {
        if (name == null)
            return null;
        var marker = name.IndexOf(" <color=", StringComparison.Ordinal);
        return marker >= 0 ? name.Substring(0, marker) : name;
    }

    // Affinity levels that grant one component each. The normal run opens the track at level 1; the
    // SSR run opens at level 4, the level right after the SSR weapon unlocks. Both hand out six
    // components (levels 1-6 and 4-9), overlapping at 4-6 where a level grants one of each: exactly
    // the six duplicates a weapon needs to reach rank 6, no slack.
    public static readonly int[] NormalComponentLevels = { 1, 2, 3, 4, 5, 6 };
    public static readonly int[] SsrComponentLevels = { 4, 5, 6, 7, 8, 9 };

    // A character's calibratable weapons. Only the signature gun is authored here: the SSR id is
    // derived from the character's Weapon unlock in Unlocks (the single map of what each character
    // gets), so an SSR landing for a doll is one edit there, never a second registry to keep in step.
    // A doll whose SSR's rank templates have not shipped simply resolves no SSR (no SSR components
    // at all, and the retroactive grant pass back-fills her reached levels once it lands).
    public sealed class Entry
    {
        public string CharacterTag;
        public string NormalWeaponId;
        public string SsrWeaponId => Unlocks.SsrWeaponFor(CharacterTag);
    }

    public static readonly Dictionary<string, Entry> ByCharacter = new(StringComparer.Ordinal)
    {
        ["wmgfl_makiatto"] = new Entry { CharacterTag = "wmgfl_makiatto", NormalWeaponId = "weapon.makiatto_wa2000" },
        ["wmgfl_voymastina"] = new Entry { CharacterTag = "wmgfl_voymastina", NormalWeaponId = "weapon.voymastina_ak15" },
        ["wmgfl_papasha"] = new Entry { CharacterTag = "wmgfl_papasha", NormalWeaponId = "weapon.papasha_ppsh" },
        ["wmgfl_cheyanne"] = new Entry { CharacterTag = "wmgfl_cheyanne", NormalWeaponId = "weapon.cheyanne_m200" },
        ["wmgfl_sextans"] = new Entry { CharacterTag = "wmgfl_sextans", NormalWeaponId = "weapon.sextans_sword" },
        ["wmgfl_vector"] = new Entry { CharacterTag = "wmgfl_vector", NormalWeaponId = "weapon.vector_kriss" },
        ["wmgfl_soppo"] = new Entry { CharacterTag = "wmgfl_soppo", NormalWeaponId = "weapon.soppo_m4" },
        ["wmgfl_lewis"] = new Entry { CharacterTag = "wmgfl_lewis", NormalWeaponId = "weapon.lewis" },
        ["wmgfl_leva"] = new Entry { CharacterTag = "wmgfl_leva", NormalWeaponId = "weapon.leva_ump45" },
        ["wmgfl_helen"] = new Entry { CharacterTag = "wmgfl_helen", NormalWeaponId = "weapon.helen_dp12" },
    };

    public static Entry EntryFor(string characterTag)
        => characterTag != null && ByCharacter.TryGetValue(characterTag, out var entry) ? entry : null;

    // The id tail after the collection prefix, e.g. "makiatto_wa2000" out of "weapon.makiatto_wa2000".
    private static string Tail(string templateId)
    {
        var dot = templateId.IndexOf('.');
        return dot < 0 ? templateId : templateId.Substring(dot + 1);
    }

    public static string RankId(string baseWeaponId, int rank)
        => rank <= 0 ? baseWeaponId : $"{baseWeaponId}_r{rank}";

    public static string ComponentIdFor(string baseWeaponId)
        => "commodity.wmgfl_component_" + Tail(baseWeaponId);

    public static string BlueprintIdFor(string baseWeaponId)
        => $"blueprint.wmgfl_{Tail(baseWeaponId)}_duplicate";

    // Parse a possibly ranked weapon template id against a base id: the base itself is rank 0 and
    // clones carry an _r<N> suffix. False when the id belongs to a different weapon.
    public static bool TryParseRank(string templateId, string baseWeaponId, out int rank)
    {
        rank = 0;
        if (templateId == null || baseWeaponId == null)
            return false;
        if (templateId == baseWeaponId)
            return true;
        if (!templateId.StartsWith(baseWeaponId + "_r", StringComparison.Ordinal))
            return false;
        return int.TryParse(templateId.Substring(baseWeaponId.Length + 2), out rank)
            && rank >= 1 && rank <= MaxRank;
    }

    // A readable holder name from a character tag ("wmgfl_makiatto" -> "Makiatto"), for the weapon
    // list. Derived from the tag so it needs no per-doll name table.
    public static string HolderName(string characterTag)
    {
        if (string.IsNullOrEmpty(characterTag))
            return "";
        var name = characterTag.StartsWith(Affinity.Tag + "_", StringComparison.Ordinal)
            ? characterTag.Substring(Affinity.Tag.Length + 1)
            : characterTag;
        var words = name.Split('_');
        for (var i = 0; i < words.Length; i++)
            if (words[i].Length > 0)
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
        return string.Join(" ", words);
    }

}

// One calibratable weapon the player owns: a specific instance, its rank, and who holds it (a doll,
// or null for unequipped stock). Weapons are not doll-bound, so two of the same weapon can sit at
// different ranks on different dolls. The screen lists these and operates on the chosen one.
public sealed class CalibrationInstance
{
    public Item Item;
    public string BaseWeaponId = "";
    public string WeaponName = "";
    public int Rank;
    public string Holder;         // doll display name, or null when in stock
    public BaseUnitLeader Leader; // the equipping leader, or null when in stock
}

// One stat's current value and its value at the next rank (equal at max rank).
public struct StatDelta
{
    public string Name;
    public float Current;
    public float Next;
    public readonly bool Changed => System.Math.Abs(Next - Current) > 0.001f;
}

// Persisted component-grant ledger per character key (the same key AffinityState uses). Components
// are consumable, so "already owned" can never stand in for "already granted": the granted levels
// themselves are recorded, and a level grants exactly once however the inventory changes afterwards.
public sealed class CalibrationState
{
    public Dictionary<int, CharacterCalibration> Characters { get; set; } = [];

    public CharacterCalibration ForCharacter(int key)
    {
        if (!Characters.TryGetValue(key, out var state))
            Characters[key] = state = new CharacterCalibration();
        return state;
    }
}

public sealed class CharacterCalibration
{
    public List<int> NormalComponentLevels { get; set; } = [];
    public List<int> SsrComponentLevels { get; set; } = [];
}
