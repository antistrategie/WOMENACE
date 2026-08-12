using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Marks every hostile the carrying skill hits with a status effect: a fresh
// application attaches the effect (its drip fires once), a repeat one only
// resets the effect's remaining lifetime, so the mark never doubles up.
// Scoped to the carrying skill, unlike the native AddSkill handler whose
// OnAttack event is container-wide: any attack by the owner fires EVERY
// AddSkill handler the owner carries, so with one each on the slash and the
// thrust every hit marked twice, and the ult's blank tile application even
// marked Sextans herself.
[JiangyuType("MarkOnHit")]
public sealed partial class MarkOnHit : SkillEventHandlerTemplate
{
    public string EffectId = "";

    // the carrying skill also banks one Coagulation charge per enemy hit
    public bool GrantsCharge;

    public override SkillEventHandler Create()
        => new MarkOnHitHandler { EffectId = EffectId, GrantsCharge = GrantsCharge };
}

[JiangyuType("MarkOnHitHandler")]
public sealed partial class MarkOnHitHandler : SkillEventHandler
{
    public string EffectId = "";
    public bool GrantsCharge;

    public override void OnApply(Actor _user, Tile _userTile, Tile _targetTile, Tile _centerTargetTile, Element _element, bool _isHit)
    {
        try
        {
            if (!_isHit || _user == null || _targetTile == null || string.IsNullOrEmpty(EffectId))
                return;
            // once per application, not once per executing element
            if (_element != null && _element.GetElementIndex() != 0)
                return;
            var victim = _targetTile.GetEntity()?.TryCast<Actor>();
            if (victim == null || !Pierce.IsHostileTo(_user, victim))
                return;
            // Only a survivor is worth marking. The charge is banked either way: the
            // damage handler sits ahead of this one in the skill's list, so a hit that
            // kills arrives here with a corpse, and gating the charge on aliveness meant
            // a killing blow paid nothing.
            if (victim.IsAlive())
                SextansUltSystem.AddOrRefreshById(victim, EffectId);
            if (GrantsCharge)
                SextansUltSystem.AddChargeFor(_user, victim);
        }
        catch (Exception ex)
        {
            Log.Error($"[MarkOnHit] failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The other half of the kill case: a victim whose corpse has already left the tile by
    // the time OnApply runs is invisible to it, so the kill event banks the charge instead.
    // The charge ledger dedupes the two paths, so an enemy pays out once however it dies.
    //
    // Container-wide, like every On*Target* event: it fires on the handlers of EVERY skill
    // its owner carries, so the kill must be attributed to the carrying skill or the slash
    // would bank a charge for the thrust's kills as well.
    public override void OnTargetKilled(Skill _skill, Entity _targetEntity)
    {
        try
        {
            if (!GrantsCharge || _skill == null || ParentSkill == null || _skill.Pointer != ParentSkill.Pointer)
                return;
            var user = GetActor();
            var victim = _targetEntity?.TryCast<Actor>();
            if (user == null || victim == null || !Pierce.IsHostileTo(user, victim))
                return;
            SextansUltSystem.AddChargeFor(user, victim);
        }
        catch (Exception ex)
        {
            Log.Error($"[MarkOnHit] kill charge failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
