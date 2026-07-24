using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The KDL-facing Phase damage handler. An SSR weapon skill declares
//
//     append "EventHandlers" type="WOMENACE:ElementalDamage" {
//         set "Element" "Freeze"
//         set "AmountPerHit" 20.0
//     }
//
// and every hit that connects feeds that much build-up of that element into
// the victim's gauge (ElementsSystem owns the ledger and the max-hp-scaled
// proc threshold).
[JiangyuType("ElementalDamage")]
public sealed partial class ElementalDamage : SkillEventHandlerTemplate
{
    // one of Burn, Shock, Freeze, Corrosion, Hydro
    public string Element = "";
    public float AmountPerHit;

    public override SkillEventHandler Create()
        => new ElementalDamageHandler { Element = Element, AmountPerHit = AmountPerHit };
}

[JiangyuType("ElementalDamageHandler")]
public sealed partial class ElementalDamageHandler : SkillEventHandler
{
    public string Element = "";
    public float AmountPerHit;

    private int _element = -2;

    // Fires per target entity per connecting hit, so a burst feeds the gauge
    // once per landed shot and misses feed nothing.
    public override void OnTargetHit(Skill skill, Entity targetEntity, DamageInfo damageInfo)
    {
        try
        {
            // OnTargetHit dispatches actor-wide (every skill's hits reach every
            // handler in the container): only the carrying skill's own hits
            // build its element, or the wielder's other weapons would too
            var parent = ParentSkill;
            if (parent == null || skill == null || skill.Pointer != parent.Pointer)
                return;
            if (_element == -2)
            {
                _element = ElementsSystem.ElementIndex(Element);
                if (_element < 0)
                    ElementsSystem.Warn($"elements: unknown element '{Element}' on '{skill?.GetTemplate()?.GetID()}'");
            }
            if (_element < 0 || AmountPerHit <= 0f)
                return;
            var victim = targetEntity?.TryCast<Actor>();
            if (victim == null || !victim.IsAlive())
                return;
            // GetActor(), not ParentSkill.Source: for an item-granted skill
            // the source is not the wielding actor
            var attacker = GetActor();
            if (attacker == null || !Pierce.IsHostileTo(attacker, victim))
                return;
            // the SSR owner's imprint can build their weapon's element faster
            ElementsSystem.AddBuildUp(victim, _element, AmountPerHit * SsrImprintSystem.ElementalMultiplier(skill));
        }
        catch (Exception ex)
        {
            ElementsSystem.Warn($"elements: build-up failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
