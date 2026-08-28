using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Soppo's two stances, part of her SSR imprint: the weapon is a dual-element
// AR for anyone, but only Soppo herself takes the forms. Using an SSR fire
// skill puts her in the matching form (the skill-use hook below: the freeze
// skill enters Hunter Form, the burn skill Feral Form), and the pursuit
// skill's HunterOpening handler stands her up in Hunter Form at every
// mission start. The forms are mutually exclusive and never stack, so the
// system also watches the skill container: when one form lands, the other is
// stripped, and a duplicate of the same form is swallowed.
public sealed class SoppoFormsSystem : JiangyuSystem
{
    private const string OwnerTag = "wmgfl_soppo";
    private const string HunterId = "effect.soppo_hunter_form";
    private const string FeralId = "effect.soppo_feral_form";

    // fire skill -> the form it puts Soppo in
    private static readonly Dictionary<string, string> FormBySkill = new(StringComparer.Ordinal)
    {
        ["active.soppo_ssr_pursuit"] = HunterId,
        ["active.soppo_ssr_bite"] = FeralId,
    };

    private static SoppoFormsSystem _instance;

    private SkillTemplate _hunter;
    private SkillTemplate _feral;

    public override void OnInit()
    {
        _instance = this;
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.SkillContainer", "Add", OnSkillAdded);
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnSkillUse", OnSkillUsed);
    }

    public override void OnTemplatesApplied()
    {
        _hunter = Templates.ById<SkillTemplate>(HunterId, msg => Context.Log.Warn($"soppo forms: {msg}"));
        _feral = Templates.ById<SkillTemplate>(FeralId, msg => Context.Log.Warn($"soppo forms: {msg}"));
    }

    // The pursuit skill's HunterOpening handler reports mission start. Driven
    // by the handler rather than a container-add hook because weapon-granted
    // skills persist in the container across missions, so an add only fires
    // on the very first one.
    internal static void OpenInHunterForm(Actor actor)
    {
        var self = _instance;
        if (self?._hunter == null || self._feral == null || actor == null)
            return;
        var skills = actor.GetSkills();
        if (SkillEffects.CountInstances(skills, self._hunter) > 0 || SkillEffects.CountInstances(skills, self._feral) > 0)
            return;
        self.EnterForm(actor, HunterId);
    }

    internal static void Warn(string message)
        => _instance?.Context.Log.Warn(message);

    private void OnSkillUsed(PatchInfo info)
    {
        try
        {
            if (info.Args == null || info.Args.Count < 2)
                return;
            var actor = info.Args[0] as Actor;
            var skill = info.Args[1] as Skill;
            var skillId = skill?.GetTemplate()?.GetID();
            if (actor == null || skillId == null || !FormBySkill.TryGetValue(skillId, out var formId))
                return;
            EnterForm(actor, formId);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"soppo forms: skill-use hook failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The forms are Soppo's imprint, not the weapon's: another doll wielding
    // the SSR fires the same dual-element skills but takes no stance. The
    // weapons-bay carrier inherits the imprint, forms included (the SSR
    // grants pursuit and bite as a pair, so gating on pursuit covers both).
    private void EnterForm(Actor actor, string formId)
    {
        if (!SsrImprintSystem.IsOwningActor(actor, OwnerTag, "active.soppo_ssr_pursuit"))
            return;
        var form = formId == HunterId ? _hunter : _feral;
        // queue-aware presence check, or a form applied moments ago (still in
        // the add queue) would be applied again
        if (form == null || SkillEffects.CountInstances(actor.GetSkills(), form) > 0)
            return;
        if (!SkillEffects.TryAddEffect(actor, form, msg => Context.Log.Warn($"soppo forms: {msg}")))
            return;
        // the Add postfix strips the opposite form and any duplicate
        Context.Log.Debug($"soppo forms: Soppo entered '{formId}'");
    }

    private void OnSkillAdded(PatchInfo info)
    {
        try
        {
            if (_hunter == null || _feral == null)
                return;
            if (info.Instance is not SkillContainer container)
                return;
            var added = (info.Args is { Count: > 0 } ? info.Args[0] : null) as BaseSkill;
            var template = added?.GetTemplate();
            if (template == null)
                return;

            SkillTemplate entering = null;
            SkillTemplate leaving = null;
            if (template.Pointer == _hunter.Pointer)
            {
                entering = _hunter;
                leaving = _feral;
            }
            else if (template.Pointer == _feral.Pointer)
            {
                entering = _feral;
                leaving = _hunter;
            }
            if (entering == null)
                return;

            var actor = container.m_Owner?.TryCast<Actor>();
            if (actor == null)
                return;

            var strippedOther = 0;
            while (container.Remove(leaving))
                strippedOther++;
            // collapse to a single instance: remove pairs until one is left.
            // Count includes the add queue, so the fresh instance is seen.
            var strippedDupes = 0;
            while (SkillEffects.CountInstances(container, entering) > 1 && container.Remove(entering))
                strippedDupes++;
            if (strippedOther > 0 || strippedDupes > 0)
            {
                Context.Log.Debug($"soppo forms: entered '{entering.GetID()}', stripped {strippedOther} other / {strippedDupes} duplicate");
                // Remove(SkillTemplate) is overloaded and unpatchable, so the
                // icon mirror cannot see these strips on its own
                EffectHudIconSystem.Resync(actor);
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"soppo forms: swap failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// KDL-facing marker for the pursuit skill: at every mission start its holder
// opens in Hunter Form (identity-gated in the system, so a non-Soppo wielder
// gets nothing).
[JiangyuType("HunterOpening")]
public sealed partial class HunterOpening : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create()
        => new HunterOpeningHandler();
}

[JiangyuType("HunterOpeningHandler")]
public sealed partial class HunterOpeningHandler : SkillEventHandler
{
    public override void OnMissionStarted()
    {
        try
        {
            // GetActor(), not ParentSkill.Source: for an item-granted skill
            // the source is not the wielding actor
            SoppoFormsSystem.OpenInHunterForm(GetActor());
        }
        catch (Exception ex)
        {
            SoppoFormsSystem.Warn($"soppo forms: mission-start opening failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
