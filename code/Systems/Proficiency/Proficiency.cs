using Il2CppMenace.Strategy;

namespace WOMENACE.Code;

// The weapon-type proficiency rules, shared the same way Affinity and Unlocks are: a static model
// both the combat system (WeaponProficiencySystem) and the affinity badge popover (AffinitySystem)
// read, so the accuracy curve and a doll's trained class can never disagree between them. The
// systems never call each other, only this.
public static class Proficiency
{
    public enum WeaponClass { None, AssaultRifle, Smg, Rifle, MachineGun, Shotgun, Blade }

    private const string ClassTagPrefix = "wmgfl_class_";

    // The trained class named by a "wmgfl_class_<x>" token in a speaker Tags string, or None if the
    // speaker carries no such tag (any unit that is not one of our proficiency dolls). The mapping
    // is authored in KDL: each doll's speaker gets the tag (see templates/dolls/<name> + common.kdl),
    // so enrolling a doll is a data change, not a code one.
    public static WeaponClass ClassFromSpeakerTags(string speakerTags)
    {
        if (string.IsNullOrEmpty(speakerTags))
            return WeaponClass.None;
        foreach (var token in speakerTags.Split(' '))
            if (token.StartsWith(ClassTagPrefix, StringComparison.Ordinal))
                return FromSuffix(token.Substring(ClassTagPrefix.Length));
        return WeaponClass.None;
    }

    private static WeaponClass FromSuffix(string suffix) => suffix switch
    {
        "ar" => WeaponClass.AssaultRifle,
        "smg" => WeaponClass.Smg,
        "rifle" => WeaponClass.Rifle,
        "mg" => WeaponClass.MachineGun,
        "shotgun" => WeaponClass.Shotgun,
        "blade" => WeaponClass.Blade,
        _ => WeaponClass.None,
    };

    // Whether a leader is a proficiency doll (her speaker names a trained class).
    public static bool HasClass(BaseUnitLeader leader)
        => ClassFromSpeakerTags(Affinity.OurSpeakerTags(leader)) != WeaponClass.None;

    // The accuracy bonus at an affinity level: +2 per level through level 5 (so +10 at 5), then +1
    // per level (+15 at the current top level 10; it tracks MaxLevel if the curve is retuned). Level
    // 0 (not one of ours) gives nothing.
    public static int AccuracyBonusForLevel(int level)
    {
        if (level <= 0)
            return 0;
        return level <= 5 ? level * 2 : 10 + (level - 5);
    }
}
