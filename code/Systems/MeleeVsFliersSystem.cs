using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Lets OUR dolls' melee attacks deal their hitpoint damage to airborne targets.
//
// The engine zeroes the hitpoint component of a melee hit against a flier
// (MovementType.Flying) while still scuffing armour and firing on-hit effects:
// a bridge probe confirmed the DamageInfo arrives at Actor.OnDamageReceived
// with Damage already 0 (the zeroing is upstream, in attack resolution), and
// OnDamageReceived then applies whatever Damage it is handed unchanged. So
// restoring Damage in a prefix, before the receive path applies it, makes the
// hit land. Ranged is untouched (its Damage arrives non-zero), and a melee
// swing that is genuinely meant to deal no hitpoints (Attack.Damage 0) stays 0.
//
// Scoped to WOMENACE dolls (the attacker carries a wmgfl character tag): this
// is a buff to our melee-only leaders (Sextans, the Voymastina mech), not a
// blanket rewrite of the engine's airborne rule for every unit.
public sealed class MeleeVsFliersSystem : JiangyuSystem
{
    public override void OnInit()
        // The 3-arg overload is the concrete Actor override. Bind it by arity
        // because OnDamageReceived is overloaded (a 4-arg form also exists).
        => Context.Patches.Prefix("Il2CppMenace.Tactical.Actor", "OnDamageReceived", 3, OnDamageReceived);

    private static bool IsFlying(Actor actor)
        => actor?.GetTemplate()?.TryCast<EntityTemplate>()?.MovementType?.Flying ?? false;

    private void OnDamageReceived(PatchInfo info)
    {
        try
        {
            if (info.Instance is not Actor victim || !IsFlying(victim))
                return;
            // Our dolls only: the attacker must carry a WOMENACE character tag.
            var attacker = (info.Args is { Count: > 0 } ? info.Args[0] : null) as Entity;
            if (attacker == null || string.IsNullOrEmpty(Affinity.CharacterTag(attacker)))
                return;
            var damageInfo = (info.Args is { Count: > 2 } ? info.Args[2] : null) as DamageInfo;
            // The victim is airborne (checked above), so the engine has already
            // run its melee-vs-flier rule by now: a connecting melee hit arrives
            // with its hitpoint Damage zeroed. Damage still non-zero means the
            // engine let it through (a ranged shot), so leave that alone.
            if (damageInfo == null || damageInfo.Damage != 0)
                return;

            var skill = (info.Args is { Count: > 1 } ? info.Args[1] : null) as Skill;
            var intended = AuthoredHitpointDamage(skill);
            if (intended <= 0f)
                return;

            damageInfo.Damage = Mathf.RoundToInt(intended);
            Context.Log.Debug($"melee vs fliers: restored {damageInfo.Damage} hitpoint damage from '{skill?.GetTemplate()?.GetID()}' onto a flier");
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
