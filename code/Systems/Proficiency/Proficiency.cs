using Il2CppMenace.Strategy;

namespace WOMENACE.Code;

// Affinity-scaled weapon proficiency rules shared by combat and the affinity tooltip.
// WeaponClasses owns the weapon taxonomy and the trained class carried by speaker tags.
public static class Proficiency
{
    // Whether a leader is a proficiency doll (her speaker names a trained class).
    public static bool HasClass(BaseUnitLeader leader)
        => WeaponClasses.ClassFromTags(Affinity.OurSpeakerTags(leader)) != WeaponClass.None;

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
