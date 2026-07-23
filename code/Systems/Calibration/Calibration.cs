using Il2CppMenace.Items;
using Il2CppMenace.Strategy;

namespace WOMENACE.Code;

// The shared weapon-calibration model: the id conventions and the component schedule every
// calibration consumer reads (CalibrationSystem, the affinity badge popover, the dev verbs).
// A doll's weapon calibrates from rank 0 (the base template) to rank 6 by merging in freshly
// crafted duplicates at the workshop. Every id derives from the character tag (wmgfl_<doll>):
// the weapon is weapon.<doll>, its SSR weapon.<doll>_ssr, rank clones <base>_r<N>, its component
// commodity.wmgfl_component_<doll> and its duplicate recipe blueprint.wmgfl_<doll>_duplicate.
// There is no per-doll registry anywhere: enrolling a doll is pure KDL.
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

    // The doll name a character tag carries ("wmgfl_makiatto" -> "makiatto"). This is the one
    // primary key every weapon id, asset path and display name derives from.
    public static string DollName(string characterTag)
        => characterTag != null && characterTag.StartsWith(Affinity.Tag + "_", StringComparison.Ordinal)
            ? characterTag.Substring(Affinity.Tag.Length + 1)
            : characterTag;

    // A character's signature weapon id ("wmgfl_makiatto" -> "weapon.makiatto") and its SSR variant
    // ("weapon.makiatto_ssr"). Pure convention: every doll has the former; the latter exists when her
    // Weapon unlock is authored in Unlocks.
    public static string WeaponIdFor(string characterTag) => "weapon." + DollName(characterTag);
    public static string SsrWeaponIdFor(string characterTag) => WeaponIdFor(characterTag) + "_ssr";

    // Whether a doll name belongs to one of our characters: the character tag template exists.
    private static bool IsDoll(string dollName)
        => Templates.ById<Il2CppMenace.Tags.TagTemplate>(Affinity.Tag + "_" + dollName) != null;

    // Resolve a weapon template id (any rank) to the calibratable base id it belongs to, or false
    // when it is not a doll weapon. Accepts weapon.<doll>[_ssr][_r<N>]; membership comes from the
    // character tag convention, so a new doll's weapons resolve with no code change.
    public static bool TryResolveWeaponId(string templateId, out string baseId, out int rank)
    {
        baseId = null;
        rank = 0;
        if (templateId == null || !templateId.StartsWith("weapon.", StringComparison.Ordinal))
            return false;

        // Strip any _r<N> rank suffix, then an optional _ssr, to reach the doll name.
        var candidate = templateId;
        var underscore = candidate.LastIndexOf('_');
        if (underscore > "weapon.".Length)
        {
            var suffix = candidate.Substring(underscore + 1);
            if (suffix.Length > 1 && suffix[0] == 'r'
                && int.TryParse(suffix.Substring(1), out var parsed) && parsed >= 1 && parsed <= MaxRank)
            {
                rank = parsed;
                candidate = candidate.Substring(0, underscore);
            }
        }
        var name = candidate.Substring("weapon.".Length);
        if (name.EndsWith("_ssr", StringComparison.Ordinal))
            name = name.Substring(0, name.Length - "_ssr".Length);
        if (!IsDoll(name))
        {
            rank = 0;
            return false;
        }
        baseId = candidate;
        return true;
    }

    // The id tail after the collection prefix, e.g. "makiatto" out of "weapon.makiatto".
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
        var name = DollName(characterTag);
        if (string.IsNullOrEmpty(name))
            return "";
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
