using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Il2CppMenace.Tactical.Skills.SkillFilters;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Drive By+ (effect.wmgfl_drive_by). Vanilla Drive By promises "-10 AP for next
// Standard Attack after moving", but both of the handlers that deliver it are
// gated by an ItemSlotFilter listing only the five vehicle weapon slots. That
// rejects every infantry weapon, and it rejects any skill granted straight off
// an EntityTemplate's Skills array, which has no item and therefore no slot at
// all. The perk is inert for all four of our units that offer it: OTs-14's
// rifle and weapons bay, Sextans' blades, the Sinner's minigun and Voymastina's
// mech guns.
//
// Both filters are replaced with IsAttackFilter, a fieldless vanilla filter
// that asks exactly the question the perk's own text asks. Vanilla pairs it
// with the same vehicle slot list elsewhere, so the type is load-bearing in the
// base game rather than a curiosity. The swap lands on OUR clone, so vanilla
// units keep the vanilla behaviour.
public sealed class DriveBySystem : JiangyuSystem
{
    private const string EffectId = "effect.wmgfl_drive_by";
    private const string PerkId = "perk.wmgfl_drive_by";

    public override void OnTemplatesApplied()
    {
        try
        {
            var effect = Templates.ById<SkillTemplate>(EffectId);
            var handlers = effect?.EventHandlers;
            if (handlers == null)
            {
                Context.Log.Warn($"drive by: {EffectId} not found, the discount keeps its vehicle-only filter");
                return;
            }
            // ChangeActionPointCost applies the discount, ConsumeOnSkillUse
            // spends it, and the two MUST agree. A skill the cost filter
            // accepts while the consume filter rejects it is discounted again
            // on every attack that turn instead of once, so both take the same
            // instance rather than two separately built filters.
            var filter = new IsAttackFilter().Cast<ISkillFilter>();
            var applied = 0;
            for (var i = 0; i < handlers.Count; i++)
            {
                var cost = handlers[i]?.TryCast<ChangeActionPointCost>();
                if (cost != null)
                {
                    cost.SkillFilter = filter;
                    applied++;
                }
                var consume = handlers[i]?.TryCast<ConsumeOnSkillUse>();
                if (consume != null)
                {
                    consume.SkillFilter = filter;
                    applied++;
                }
            }
            // Assignment rather than wrapping, so a second OnTemplatesApplied
            // is a no-op instead of nesting a chain.
            if (applied == 2)
                Context.Log.Debug($"drive by: {EffectId} now discounts any attack");
            else
                Context.Log.Warn($"drive by: expected 2 filters on {EffectId}, set {applied}");
            VerifyPerkPointsAtClone();
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"drive by: filter swap failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The clone re-points its AddSkillAfterMovement at our effect from KDL, and
    // a KDL field that fails to route is silently ignored rather than reported.
    // A clone still holding vanilla's effect would take the vanilla filter and
    // the perk would be quietly inert again, which is the exact failure this
    // system exists to end, so it is worth one line in the boot log.
    private void VerifyPerkPointsAtClone()
    {
        try
        {
            var perk = Templates.ById<SkillTemplate>(PerkId);
            var handlers = perk?.EventHandlers;
            if (handlers == null)
            {
                Context.Log.Warn($"drive by: {PerkId} not found");
                return;
            }
            for (var i = 0; i < handlers.Count; i++)
            {
                var add = handlers[i]?.TryCast<AddSkillAfterMovement>();
                if (add == null)
                    continue;
                var id = add.Effect?.GetID();
                if (id != EffectId)
                    Context.Log.Warn($"drive by: {PerkId} still grants '{id}', not {EffectId}");
                return;
            }
            Context.Log.Warn($"drive by: {PerkId} has no AddSkillAfterMovement handler");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"drive by: perk check failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
