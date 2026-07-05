using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
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
            if (victim == null || !victim.IsAlive() || !Pierce.IsHostileTo(_user, victim))
                return;
            SextansUltSystem.AddOrRefreshById(victim, EffectId);
            if (GrantsCharge)
                SextansUltSystem.AddChargeFor(_user);
        }
        catch (Exception ex)
        {
            Log.Error($"[MarkOnHit] failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
