using Il2CppMenace.Tactical.Skills;

namespace WOMENACE.Code;

internal static class MeleeSkills
{
    private const string Tag = "wmgfl_melee";

    internal static bool IsMelee(SkillTemplate template)
    {
        // Registered tags and template-list tags can be different native instances.
        var tags = template?.Tags;
        for (var i = 0; tags != null && i < tags.Count; i++)
            if (tags[i]?.name == Tag)
                return true;
        return false;
    }
}
