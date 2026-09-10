using Il2CppMenace.Items;

namespace WOMENACE.Code;

// Ingredient families use their mod-owned ID namespaces across recipes, stock and presentation.
public static class WeaponParts
{
    public const string PartPrefix = "commodity.wmgfl_part_";
    public const string ComponentPrefix = "commodity.wmgfl_component_";
    public const double TwoPartChance = 0.5;

    public static IEnumerable<string> ForClass(WeaponClass weaponClass)
    {
        var descriptor = WeaponClasses.Describe(weaponClass);
        if (descriptor == null)
            yield break;
        var prefix = PartPrefix + WeaponClasses.Code(weaponClass).ToLowerInvariant() + "_";
        foreach (var part in descriptor.Parts)
            yield return prefix + part;
    }

    public static bool IsPart(string id) => id?.StartsWith(PartPrefix, StringComparison.Ordinal) == true;
    public static bool IsComponent(string id) => id?.StartsWith(ComponentPrefix, StringComparison.Ordinal) == true;

    public static WeaponClass ClassOfPart(string id)
    {
        if (!IsPart(id))
            return WeaponClass.None;
        var part = id.Substring(PartPrefix.Length);
        var separator = part.IndexOf('_');
        return separator > 0 ? WeaponClasses.FromCode(part.Substring(0, separator)) : WeaponClass.None;
    }

    public static WeaponClass DisassemblyClass(WeaponTemplate weapon)
        => BaseGameWeapons.Contains(weapon)
            ? WeaponClasses.ClassifyByShortName(weapon)
            : WeaponClass.None;

    // Sampling without replacement keeps a two-part drop useful for two recipe slots.
    public static IReadOnlyList<string> Roll(WeaponClass weaponClass, Random random)
    {
        var parts = ForClass(weaponClass).ToList();
        if (parts.Count == 0)
            return Array.Empty<string>();
        var count = random.NextDouble() < TwoPartChance ? 2 : 1;
        var result = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var index = random.Next(parts.Count);
            result.Add(parts[index]);
            parts.RemoveAt(index);
        }
        return result;
    }
}

public sealed class WorkshopState
{
    // Recipes remain available after the last copy is consumed or sold. Workshop entry also
    // discovers starting loadouts and weapons unlocked by a hired Doll's affinity.
    public List<string> KnownWeaponIds { get; set; } = [];
}
