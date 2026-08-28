using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The OS Tuning field kit's single charge: Repair Kit and Ammo Refill are
// separate one-use skills, and using either spends the kit for both.
//
// Spending CONSUMES BOTH SKILLS' USES and keeps them consumed: perk-granted
// uses re-synchronise every turn, so the consumption is re-applied at the
// engine's own resync point (Skill.SynchronizeUses) for the rest of the
// mission. The earlier remove-both design is gone: garbage-collecting the
// reload skill while its refill flow was still resolving left the engine
// waiting on a dead action (input softlocked on a spinner); repair only
// survived because its heal completes within the frame.
//
// Rides Skill.OnUse (the three-parameter overload) with NO gate on its bool
// result: an instant handler-only skill reports false from OnUse even when
// the use lands. Reaching OnUse at all means the click was allowed.
public sealed class Ots14OsTuningSystem : JiangyuSystem
{
    private const string RepairId = "active.wmgfl_bay_repair";
    private const string ReloadId = "active.wmgfl_bay_reload";

    // The kit has been spent this mission: both skills stay consumed.
    private bool _kitSpent;

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "OnUse", 3, OnSkillUsed);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "SynchronizeUses", OnSynchronizeUses);
        // The reload's own refill restores uses of limited-use skills, the
        // kit included, un-spending the kit the moment it is spent: after
        // any ammo refill on her, re-apply the depletion.
        Context.Patches.Postfix("Il2CppMenace.Tactical.Actor", "RefillAmmo", 3, OnRefillAmmo);
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        _kitSpent = false;
    }

    private static bool IsKitSkill(string id) => id is RepairId or ReloadId;

    private void OnSkillUsed(PatchInfo info)
    {
        try
        {
            var skill = (info.Instance as Il2CppSystem.Object)?.TryCast<Skill>();
            var id = skill?.GetTemplate()?.GetID();
            if (id == null || !IsKitSkill(id))
                return;
            var actor = (info.Args is { Count: > 0 } ? info.Args[0] : null) as Il2CppMenace.Tactical.Actor ?? skill.GetActor();
            // The heal rides the first spend only: once the kit is spent a
            // repair click that slips through must not heal again.
            if (id == RepairId && !_kitSpent)
                HealToFull(actor);
            if (_kitSpent)
                return;
            _kitSpent = true;

            // The ACTOR's skill list, the enumeration the sibling was
            // provably found in; the used skill's own GetContainer resolved
            // to a container the sibling was not in (log: 0 depleted).
            var all = actor?.GetSkills()?.GetAllSkills();
            var found = 0;
            var spent = 0;
            for (var i = 0; all != null && i < all.Count; i++)
            {
                var candidate = all[i]?.TryCast<Skill>();
                if (candidate == null || !IsKitSkill(candidate.GetTemplate()?.GetID()))
                    continue;
                found++;
                spent += Deplete(candidate);
                Context.Log.Debug($"os tuning:   {candidate.GetTemplate()?.GetID()}: uses {candidate.GetUses()}/{candidate.GetMaxUses()}, consumed {candidate.GetUsesConsumed()}");
            }
            Context.Log.Debug($"os tuning: '{id}' spent the kit charge ({found} kit skill(s) found, {spent} depleted)");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"os tuning: kit spend failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The engine re-derives uses from the template at its own resync points
    // (each turn among them), which un-greys a consumed one-use skill. After
    // the kit is spent, the consumption is re-applied right after every
    // resync, so both skills stay depleted for the rest of the mission.
    private void OnSynchronizeUses(PatchInfo info)
    {
        try
        {
            if (!_kitSpent)
                return;
            var skill = (info.Instance as Il2CppSystem.Object)?.TryCast<Skill>();
            if (skill == null || !IsKitSkill(skill.GetTemplate()?.GetID()))
                return;
            Deplete(skill);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"os tuning: kit resync failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnRefillAmmo(PatchInfo info)
    {
        try
        {
            if (!_kitSpent)
                return;
            var actor = (info.Instance as Il2CppObjectBase)?.TryCast<Il2CppMenace.Tactical.Actor>();
            var all = actor?.GetSkills()?.GetAllSkills();
            var redepleted = 0;
            for (var i = 0; all != null && i < all.Count; i++)
            {
                var candidate = all[i]?.TryCast<Skill>();
                if (candidate == null || !IsKitSkill(candidate.GetTemplate()?.GetID()))
                    continue;
                redepleted += Deplete(candidate);
            }
            if (redepleted > 0)
                Context.Log.Debug($"os tuning: refill restored {redepleted} kit skill(s), re-depleted");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"os tuning: kit refill re-depletion failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Spend the skill outright on BOTH counters: the usability gate follows
    // the consumed counter (ChangeUsesConsumed greys it), while the number
    // on the button is the remaining-uses counter (SetUses). The engine
    // only writes the latter on a real use, and the reload's self-refill
    // even writes it back up, so depletion sets both explicitly. Returns 1
    // when anything changed.
    private static int Deplete(Skill skill)
    {
        var changed = 0;
        if (skill.GetUses() > 0)
        {
            skill.SetUses(0);
            changed = 1;
        }
        var missing = skill.GetMaxUses() - skill.GetUsesConsumed();
        if (missing > 0)
        {
            skill.ChangeUsesConsumed(missing);
            changed = 1;
        }
        return changed;
    }

    // The heal lives here, not on the template: the treat_wounds base's
    // Regeneration handler is an OnTurnStart full heal that fires passively
    // every round while the granted skill exists, so the clone's handlers
    // are cleared in KDL and the use path restores her explicitly. The
    // HUD-notify trio is the reinforcement-fairy recipe.
    private void HealToFull(Il2CppMenace.Tactical.Actor actor)
    {
        try
        {
            if (actor == null)
                return;
            var elements = actor.GetElements();
            for (var i = 0; elements != null && i < elements.Count; i++)
            {
                var element = elements[i];
                if (element != null && element.GetHitpoints() > 0)
                    element.SetHitpoints(element.GetHitpointsMax());
            }
            actor.UpdateHitpoints();
            Il2CppMenace.Tactical.TacticalManager.Get()?.InvokeOnHitpointsChanged(actor, actor.GetHitpointsPct(), 0);
            Context.Log.Debug($"os tuning: repair kit restored {actor.GetHitpoints()}/{actor.GetHitpointsMax()} hp");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"os tuning: repair heal failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
