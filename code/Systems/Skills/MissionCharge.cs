using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The KDL-facing once-per-mission charge. A skill declares
//
//     append "EventHandlers" type="WOMENACE:MissionCharge" {
//         set "Group" "asteria_bond"
//     }
//
// and it can be used once per mission. Skills naming the SAME Group share one charge, so using
// any of them spends it for all (OTs-14's field kit is one charge across Repair Kit and Ammo
// Refill). The charge is per actor, so two dolls carrying the same kit each get their own.
//
// `Uses 1` in KDL does NOT buy this on its own. The engine re-derives a skill's uses from its
// template at every resync point, turn boundaries among them, so a one-use skill on a perk grant
// recharges each turn. Vanilla `active.treat_wounds` only feels once-per-mission because the
// medkit ITEM's charges back it, and a perk-granted clone has no item behind it.
//
// A charge-gated skill must ALSO set `IsLimitedUses #false`, so the tile shows no use counter.
// The counter and the charge are two different things and they visibly disagree: the engine
// decrements only the skill actually clicked, so a spent group's other skills still read their
// full count while greyed, and after the next resync even the used one reads full again. Vanilla
// `active.rogue_officer_whistle` is the shape to copy, an ordinary active with no count whose
// availability is governed by something other than a use budget.
//
// The gate is `IsUsable`, the same hook vanilla's `LimitUsabilityHandler` greys a skill with.
// That vanilla handler would have done the whole job as pure data (`NotUsableIfActorHasSkill`
// naming a marker effect the skill's own `AddSkill` applies), but `AddSkill` lands on the skill's
// TARGET, and an ally-targeted skill would mark the ally rather than the caster.
[JiangyuType("MissionCharge")]
public sealed partial class MissionCharge : SkillEventHandlerTemplate
{
    // Skills sharing a Group name spend one charge between them. A group of one is a plain
    // once-per-mission skill.
    public string Group = "";

    public override SkillEventHandler Create() => new MissionChargeHandler { Group = Group };
}

[JiangyuType("MissionChargeHandler")]
public sealed partial class MissionChargeHandler : SkillEventHandler
{
    public string Group = "";

    public override bool IsUsable()
    {
        try
        {
            return !MissionCharges.IsSpent(GetActor(), Group);
        }
        catch (Exception ex)
        {
            Log.Warn($"mission charge: usability check failed: {ex.GetType().Name}: {ex.Message}");
            return true;
        }
    }

    // OnUse fires on the used skill's own handlers. A sibling in the same group needs no hook of
    // its own: it reads the shared per-actor charge when the engine next asks whether it is usable.
    public override void OnUse(Actor _user, Tile _targetTile, UsageParameter _usageParams, ref bool _applyToTile)
    {
        try
        {
            if (MissionCharges.MarkSpent(_user ?? GetActor(), Group))
                Log.Debug($"mission charge: '{ParentSkill?.GetTemplate()?.GetID()}' spent group '{Group}'");
        }
        catch (Exception ex)
        {
            Log.Warn($"mission charge: spend failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
