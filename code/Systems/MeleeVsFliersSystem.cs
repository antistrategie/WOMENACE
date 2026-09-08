using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Restores WOMENACE dolls' melee hitpoint damage against flying and hovering targets.
// Airborne attack resolution can zero Damage before Actor.OnDamageReceived while
// still applying armour damage and on-hit effects. The receive prefix restores the
// authored Attack.Damage only for wmgfl_melee skills with positive hitpoint damage.
public sealed class MeleeVsFliersSystem : JiangyuSystem
{
#if JIANGYU_DEV
    internal static MeleeVsFliersSystem Instance { get; private set; }
    internal int DamageRestored { get; private set; }
    internal string LastRestoredHit { get; private set; } = "none";
#endif

    public override void OnInit()
    {
#if JIANGYU_DEV
        Instance = this;
#endif
        // The 3-arg overload is the concrete Actor override. Bind it by arity
        // because OnDamageReceived is overloaded (a 4-arg form also exists).
        Context.Patches.Prefix("Il2CppMenace.Tactical.Actor", "OnDamageReceived", 3, OnDamageReceived);
    }

    internal static bool IsAirborne(Actor actor)
    {
        var template = actor?.GetTemplate()?.TryCast<EntityTemplate>();
        if (template?.MovementType?.Flying == true)
            return true;
        // The hover bomb's movement_type.construct_hoverbomb has Flying=false.
        // Its EntityTemplate instead identifies it with the vanilla hovering tag.
        var tags = template?.Tags;
        for (var i = 0; tags != null && i < tags.Count; i++)
            if (tags[i]?.name == "hovering")
                return true;
        return false;
    }

    private void OnDamageReceived(PatchInfo info)
    {
        try
        {
            if (info.Instance is not Actor victim || !IsAirborne(victim))
                return;
            var skill = (info.Args is { Count: > 1 } ? info.Args[1] : null) as Skill;
            // Damage can also be zero because another system spared the target.
            // Restrict restoration to our melee skills so blasts and ranged attacks
            // retain their own damage rules.
            if (!MeleeSkills.IsMelee(skill?.GetTemplate()))
                return;
            // Our dolls only: the attacker must carry a WOMENACE character tag.
            var attacker = (info.Args is { Count: > 0 } ? info.Args[0] : null) as Entity;
            if (attacker == null || string.IsNullOrEmpty(Affinity.CharacterTag(attacker)))
                return;
            var damageInfo = (info.Args is { Count: > 2 } ? info.Args[2] : null) as DamageInfo;
            // Preserve hits the engine already allowed through with non-zero damage.
            if (damageInfo == null || damageInfo.Damage != 0)
                return;

            var intended = AuthoredHitpointDamage(skill);
            if (intended <= 0f)
                return;

            damageInfo.Damage = Mathf.RoundToInt(intended);
#if JIANGYU_DEV
            DamageRestored++;
            LastRestoredHit = $"{skill?.GetTemplate()?.GetID()} -> {victim.GetTemplate()?.GetID()} damage={damageInfo.Damage}";
#endif
            Context.Log.Debug($"melee vs fliers: restored {damageInfo.Damage} hitpoint damage from '{skill?.GetTemplate()?.GetID()}' onto an airborne target");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"melee vs fliers: restore failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The skill's authored per-hit hitpoint damage: the Damage on its Attack
    // handler (where melee skills carry it, the weapon's own Damage being 0).
    // Zero when the skill has no Attack handler or deals no hitpoints.
    private static float AuthoredHitpointDamage(Skill skill)
    {
        var handlers = skill?.GetTemplate()?.EventHandlers;
        for (var i = 0; handlers != null && i < handlers.Count; i++)
        {
            var attack = handlers[i]?.TryCast<Attack>();
            if (attack != null)
                return attack.Damage;
        }
        return 0f;
    }
}
