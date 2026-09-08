using Il2CppMenace.Items;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

public enum WeaponClass { None, AssaultRifle, Smg, Rifle, MachineGun, Shotgun, Blade }

// Shared weapon taxonomy for proficiency, workshop ingredients and shop presentation.
public static class WeaponClasses
{
    private static readonly string[] GunParts = { "barrel", "receiver", "grip", "sights" };
    private static readonly Descriptor[] Descriptors =
    {
        new(WeaponClass.AssaultRifle, "AR", "ar", new LocalisedText("WOMENACE::ui/proficiency/class_ar", "assault rifles"),
            GunParts, 2, new[] { "assault_rifle", "carbine" }),
        new(WeaponClass.Smg, "SMG", "smg", new LocalisedText("WOMENACE::ui/proficiency/class_smg", "SMGs"),
            GunParts, 4, new[] { "smg", "pdw" }),
        new(WeaponClass.Rifle, "RF", "rifle", new LocalisedText("WOMENACE::ui/proficiency/class_rifle", "rifles"),
            GunParts, 1, new[] { "battle_rifle", "sniper", "marksman", "dmr", "anti_materiel" }),
        new(WeaponClass.MachineGun, "MG", "mg", new LocalisedText("WOMENACE::ui/proficiency/class_mg", "machine guns"),
            GunParts, 5, new[] { "machinegun", "chaingun", "minigun", "repeater" }),
        new(WeaponClass.Shotgun, "SG", "shotgun", new LocalisedText("WOMENACE::ui/proficiency/class_shotgun", "shotguns"),
            GunParts, 3, new[] { "shotgun", "sweeper" }),
        new(WeaponClass.Blade, "BLD", "blade", new LocalisedText("WOMENACE::ui/proficiency/class_blade", "blades"),
            new[] { "blank", "hilt" }, 0, new[] { "sword", "blade", "melee" }),
    };
    private static readonly Descriptor[] ByIdentifierPriority = Descriptors.OrderBy(entry => entry.IdentifierPriority).ToArray();
    public static IReadOnlyList<WeaponClass> All { get; } = Descriptors.Select(entry => entry.Class).ToArray();

    public static Descriptor Describe(WeaponClass weaponClass) => Descriptors.FirstOrDefault(entry => entry.Class == weaponClass);
    public static string Code(WeaponClass weaponClass) => Describe(weaponClass)?.Code ?? "";

    public sealed class Descriptor(WeaponClass weaponClass, string code, string tagSuffix, LocalisedText plural,
        IReadOnlyList<string> parts, int identifierPriority, IReadOnlyList<string> identifiers)
    {
        public readonly WeaponClass Class = weaponClass;
        public readonly string Code = code, TagSuffix = tagSuffix;
        public readonly LocalisedText Plural = plural;
        public readonly IReadOnlyList<string> Parts = parts, Identifiers = identifiers;
        public readonly int IdentifierPriority = identifierPriority;
    }

    private const string ClassTagPrefix = "wmgfl_class_";

    // Speakers and weapons share the wmgfl_class_* tags authored in KDL.
    public static WeaponClass ClassFromTags(string speakerTags)
    {
        if (string.IsNullOrEmpty(speakerTags))
            return WeaponClass.None;
        foreach (var token in speakerTags.Split(' '))
            if (token.StartsWith(ClassTagPrefix, StringComparison.Ordinal))
                return FromSuffix(token.Substring(ClassTagPrefix.Length));
        return WeaponClass.None;
    }

    private static WeaponClass FromSuffix(string suffix)
        => Descriptors.FirstOrDefault(entry => entry.TagSuffix == suffix)?.Class ?? WeaponClass.None;

    // Class tags survive localisation. Native weapons use their ShortName category, with the ID
    // naming convention covering enemy and cut weapons that have no recognised category.
    public static WeaponClass Classify(WeaponTemplate weapon)
    {
        if (weapon == null)
            return WeaponClass.None;
        var byTag = ClassifyByTag(weapon);
        if (byTag != WeaponClass.None)
            return byTag;
        var byShortName = ClassifyByShortName(weapon);
        return byShortName != WeaponClass.None ? byShortName : ClassifyById(weapon.GetID());
    }

    // The ShortName category as it reads in the armoury/tooltip subtitle, mapped to a weapon
    // class. Anything not here (Laser Rifle, Plasma, launchers, mortars, ...) is left unclassified.
    private static readonly Dictionary<string, WeaponClass> ShortNameClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Assault Rifle"] = WeaponClass.AssaultRifle,
        ["Heavy Assault Rifle"] = WeaponClass.AssaultRifle,
        ["Enhanced AR"] = WeaponClass.AssaultRifle,
        ["Carbine"] = WeaponClass.AssaultRifle,
        ["Automatic Rifle"] = WeaponClass.AssaultRifle,
        ["SMG"] = WeaponClass.Smg,
        ["Heavy SMG"] = WeaponClass.Smg,
        ["PDW"] = WeaponClass.Smg,
        ["Battle Rifle"] = WeaponClass.Rifle,
        ["Sniper Rifle"] = WeaponClass.Rifle,
        ["Sniper"] = WeaponClass.Rifle,
        ["Bolt-Action Rifle"] = WeaponClass.Rifle,
        ["DMR"] = WeaponClass.Rifle,
        ["AT Rifle"] = WeaponClass.Rifle,
        ["Light Machinegun"] = WeaponClass.MachineGun,
        ["Medium MG"] = WeaponClass.MachineGun,
        ["MMG"] = WeaponClass.MachineGun,
        ["HMG"] = WeaponClass.MachineGun,
        ["Minigun"] = WeaponClass.MachineGun,
        ["Shotgun"] = WeaponClass.Shotgun,
        ["Sweeper"] = WeaponClass.Shotgun,
        ["Sword"] = WeaponClass.Blade,
    };

    // Mod localisation rewrites authored category text. Tags keep classification independent of
    // language and of which Doll equips the weapon.
    private static WeaponClass ClassifyByTag(WeaponTemplate weapon)
    {
        try
        {
            // Selected-unit stats ask every frame. Cache the tag walk to avoid repeated marshalling.
            if (_classByTemplate.TryGetValue(weapon.Pointer, out var cached))
                return cached;

            var wc = ReadClassTag(weapon);
            _classByTemplate[weapon.Pointer] = wc;
            return wc;
        }
        catch { return WeaponClass.None; }
    }

    private static readonly Dictionary<IntPtr, WeaponClass> _classByTemplate = new();

    private static WeaponClass ReadClassTag(WeaponTemplate weapon)
    {
        try
        {
            var tags = weapon.Tags;
            if (tags == null)
                return WeaponClass.None;
            for (var i = 0; i < tags.Count; i++)
            {
                var wc = ClassFromTags(tags[i]?.name);
                if (wc != WeaponClass.None)
                    return wc;
            }
            return WeaponClass.None;
        }
        catch { return WeaponClass.None; }
    }

    // Disassembly uses this strict category match without the tag or ID fallbacks.
    public static WeaponClass ClassifyByShortName(WeaponTemplate weapon)
    {
        try
        {
            var text = Templates.DefaultText(weapon.ShortName);
            return !string.IsNullOrEmpty(text) && ShortNameClasses.TryGetValue(text.Trim(), out var wc)
                ? wc
                : WeaponClass.None;
        }
        catch { return WeaponClass.None; }
    }

    // Fallback for weapons with no ShortName (enemy constructs, cut tier3 guns). Ordered so a
    // battle-rifle marksman variant is a rifle before the carbine (assault-rifle) rule, and the
    // sniper/marksman family classifies as rifle. A bare "rifle" is deliberately not matched, so an
    // energy "laser_rifle"/"plasma_rifle" stays unclassified.
    private static WeaponClass ClassifyById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return WeaponClass.None;
        foreach (var entry in ByIdentifierPriority)
            if (entry.Identifiers.Any(id.Contains))
                return entry.Class;
        return WeaponClass.None;
    }

    // A friendly plural for the class, for the tooltip text. Separate from ShortNameClasses above:
    // that one matches the game's English category labels, this one is shown to the player.
    public static string Plural(WeaponClass weaponClass)
        => Describe(weaponClass)?.Plural.Resolve() ?? Locale.Text("WOMENACE::ui/proficiency/class_any", "weapons");

    public static WeaponClass FromCode(string code)
        => Descriptors.FirstOrDefault(entry => string.Equals(entry.Code, code, StringComparison.OrdinalIgnoreCase))?.Class ?? WeaponClass.None;

    internal static void ClearCache() => _classByTemplate.Clear();
}
