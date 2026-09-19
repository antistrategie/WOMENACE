using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Applies Gathering Steam's per-stack damage bonus. Same hook and shape as SsrImprintSystem's
// owner-only damage boost: added to the DamageInfo a shot just built, per shot, so the shared
// WeaponTemplate.Damage is never mutated and can never leak a buffed value to another reader
// (loadout preview, non-owner tooltip). Character-scoped (any weapon Lewis fires), not
// weapon-scoped, so unlike SsrImprintSystem this has no per-weapon registry: identity is read
// straight off the firing entity's speaker tag.
public sealed class GatheringSteamSystem : JiangyuSystem
{
    private const string OwnerTag = "wmgfl_lewis";

    public override void OnInit()
        => Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "FillDamageInfo", OnFillDamageInfo);

    private void OnFillDamageInfo(PatchInfo info)
    {
        try
        {
            var skill = (info.Instance as Il2CppSystem.Object)?.TryCast<Skill>();
            var owner = Wielder(skill);
            if (owner == null || Affinity.CharacterTag(owner) != OwnerTag)
                return;

            var bonus = GatheringSteamHandler.BonusDamageFor(owner);
            if (bonus <= 0)
                return;

            var damageInfo = info.Args != null && info.Args.Count > 0
                ? (info.Args[0] as Il2CppSystem.Object)?.TryCast<DamageInfo>()
                : null;
            if (damageInfo == null)
                return;

            damageInfo.Damage += bonus;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"gathering steam: damage boost failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // GetEntity() first (matches how the perk handler reads its own owner), GetActor() as a
    // fallback, mirroring SsrImprintSystem's multi-accessor wielder lookup.
    private static Entity Wielder(Skill skill)
    {
        if (skill == null)
            return null;
        var entity = skill.GetEntity();
        if (entity != null)
            return entity;
        return (skill.GetActor() as Il2CppObjectBase)?.TryCast<Entity>();
    }
}
