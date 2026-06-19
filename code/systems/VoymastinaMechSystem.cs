using System.Collections;
using System.Collections.Generic;
using Il2CppMenace.Tactical;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

public sealed class VoymastinaMechSystem : JiangyuSystem
{
    private const float SpeedMultiplier = 5f;

    private static readonly string[] TargetTemplateIds =
        { "player_vehicle.voymastina_mech", "player_vehicle.voymastina_mech_erwin" };

    private static readonly Dictionary<string, string> SkillAnimTrigger = new(StringComparer.Ordinal)
    {
        { "active.voymastina_mech_rocket", "SpecialAttack1" },
        { "active.voymastina_mech_drill",  "SpecialAttack2" },
    };

    // Drill dash tuning.
    private const string DrillSkillId = "active.voymastina_mech_drill";
    private const string DrillHitState = "UltraSkillHit"; // the slam (second) animation state
    private const float DashWindupTimeout = 3f;           // fall back to sliding if the slam state never reports
    private const float DashSlideDuration = 0.1f;

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Tactical.PathMover", "StartMovement", OnMechStartMovement);
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnSkillUse", BridgeSkillAnimation);
    }

    // On every move the mech begins: scale its speed (the hover reads as fast flight) and suppress
    // cover vaulting. The mech flies and has no vault clip, so if it vaults, PathMover stalls at the
    // cover waiting for a vault-complete signal the rig never sends, and the move never finishes.
    // VaultMode.None stops the actor vaulting on subsequent moves. DisallowVaultingUntilEndOfMovement
    // covers the move already in flight (it resets when the movement ends, so it is reapplied here).
    private void OnMechStartMovement(PatchInfo info)
    {
        try
        {
            if (info.Instance is not PathMover mover)
                return;
            var actor = mover.m_Actor;
            if (actor == null || !IsMech(actor))
                return;

            mover.m_MaxSpeed *= SpeedMultiplier;

            actor.VaultMode = VaultMode.None;
            mover.DisallowVaultingUntilEndOfMovement();
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"mech movement postfix failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void BridgeSkillAnimation(PatchInfo info)
    {
        try
        {
            if (info.Args == null || info.Args.Count < 2)
                return;
            var actor = info.Args[0] as Actor;
            var skill = info.Args[1] as Il2CppMenace.Tactical.Skills.Skill;
            if (actor == null || skill == null || !IsMech(actor))
                return;

            var skillTemplate = skill.GetTemplate();
            string skillId = skillTemplate?.GetID() ?? skillTemplate?.name;
            if (skillId == null || !SkillAnimTrigger.TryGetValue(skillId, out var trigger))
                return;

            var element = actor.GetElement(0);
            if (element == null)
                return;

            Animator matched = null;
            foreach (var anim in element.GetComponentsInChildren<Animator>(true))
            {
                if (anim == null) continue;
                var parms = anim.parameters;
                if (parms == null) continue;
                foreach (var p in parms)
                {
                    if (p.name != trigger) continue;
                    anim.SetTrigger(trigger);
                    matched = anim;
                    break;
                }
                if (matched != null) break;
            }

            if (matched != null)
                Context.Log.Debug($"skill-anim bridge: {skillId} -> SetTrigger({trigger})");
            else
                Context.Log.Warn($"skill-anim bridge: no animator with '{trigger}' for {skillId}");

            // Drill: slide the mech up to the target during the slam animation. The skill resolves
            // damage at range, so the hit lands regardless of where the slide ends up.
            if (string.Equals(skillId, DrillSkillId, StringComparison.Ordinal))
            {
                var targetTile = info.Args.Count > 2 ? info.Args[2] as Tile : null;
                if (targetTile != null)
                    Context.Coroutines.Start(DrillDash(actor, targetTile, matched));
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"skill-anim bridge failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Code-driven dash. The engine has no auto-path-to-melee for players, and the native leap can
    // strand a vehicle on an illegal tile, so we relocate the mech ourselves: wait for the slam
    // animation, then quickly slide the visual to a free tile next to the target and commit the
    // logical tile. Both ultra clips are in-place, so nothing fights the slide.
    private IEnumerator DrillDash(Actor mech, Tile targetTile, Animator anim)
    {
        Tile mechTile = mech.GetTile();
        if (mechTile == null)
            yield break;

        Tile landing = PickLandingTile(mech, mechTile, targetTile);
        if (landing == null)
        {
            Context.Log.Debug("drill dash: no free tile adjacent to target; drilling in place");
            yield break;
        }
        if (landing.GetX() == mechTile.GetX() && landing.GetZ() == mechTile.GetZ())
            yield break; // already adjacent, nothing to slide

        // Wait for the slam to start. Fall back to a timeout so we always reposition.
        float waited = 0f;
        while (waited < DashWindupTimeout)
        {
            if (anim != null && anim.GetCurrentAnimatorStateInfo(0).IsName(DrillHitState))
                break;
            waited += Time.deltaTime;
            yield return null;
        }
        // The mission can end (the actor is destroyed) during the up-to-3s wait, which would leave
        // the calls below dereferencing a dead actor. A destroyed Il2Cpp actor is a live managed
        // wrapper, so check IsAlive(), not just != null.
        if (mech == null || !mech.IsAlive())
            yield break;

        // Another actor may have stepped onto the landing tile during the windup wait. Re-validate
        // (and re-pick) so the slide never lands two actors on one tile.
        if (landing.HasActor())
        {
            var repick = PickLandingTile(mech, mechTile, targetTile);
            if (repick == null || (repick.GetX() == mechTile.GetX() && repick.GetZ() == mechTile.GetZ()))
            {
                Context.Log.Debug("drill dash: landing tile taken during windup; drilling in place");
                yield break;
            }
            landing = repick;
        }

        var elements = mech.GetElements();
        int n = elements == null ? 0 : elements.Count;
        if (n == 0)
            yield break;

        var starts = new Vector3[n];
        var ends = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var el = elements[i];
            if (el == null) continue;
            starts[i] = el.transform.position;
            ends[i] = el.GetTargetPosOnTile(landing, -1);
        }

        float t = 0f;
        while (t < DashSlideDuration)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / DashSlideDuration);
            for (int i = 0; i < n; i++)
            {
                var el = elements[i];
                if (el != null)
                    el.transform.position = Vector3.Lerp(starts[i], ends[i], f);
            }
            yield return null;
        }

        for (int i = 0; i < n; i++)
        {
            var el = elements[i];
            if (el != null)
                el.transform.position = ends[i];
        }
        if (mech == null || !mech.IsAlive())
            yield break; // destroyed during the slide: do not commit a tile for a dead actor
        mech.SetTile(landing);

        // GetPos() (which the overhead UI reads) returns the cached m_AveragePosition, refreshed by
        // neither the transform lerp nor SetTile, so the UI stays at the old tile. Recompute it from
        // the now-moved elements. No movement events are fired, so nothing wakes overwatch or a
        // spot-interrupt.
        mech.UpdateAveragePosition();

        // A normal move recomputes the unit's vision per tile, lifting fog of war around the new
        // position. The dash skips that, so flag the vision dirty: the game's CheckForDirtyVision
        // pass recomputes it and reveals the fog the mech can now see.
        mech.VisionDirty = true;
        Context.Log.Debug($"drill dash: slid to ({landing.GetX()},{landing.GetZ()})");
    }

    // The tile next to the target on the mech's side. A dash bypasses normal pathing, but the
    // landing must still be a tile the mech can legally occupy and then move off. IsBlocked is
    // line-of-sight blocking, not movement, so a geometric "unoccupied + not blocked" test stranded
    // the mech on props and rough terrain it could not leave. CanBeEnteredBy is the engine's own
    // occupancy predicate (footprint + surface aware), so it is used first. If the target is ringed
    // by tiles the vehicle cannot enter, fall back to any valid movement destination, which still
    // excludes props/structures/blocked tiles. If even that finds nothing, the caller drills in
    // place (the skill resolves damage at range regardless).
    private Tile PickLandingTile(Actor mech, Tile mechTile, Tile targetTile)
    {
        return BestLanding(mechTile, targetTile, t => t.CanBeEnteredBy(mech))
            ?? BestLanding(mechTile, targetTile, t => t.IsValidMovementDestination());
    }

    // The neighbour directly toward the mech is preferred. The fallback ranks by straight-line
    // distance (not GetDistanceTo, which is path distance and would send the mech to the far side
    // when one side is obstructed). A candidate must be unoccupied and pass the usable predicate.
    private static Tile BestLanding(Tile mechTile, Tile targetTile, Func<Tile, bool> usable)
    {
        int mechX = mechTile.GetX();
        int mechZ = mechTile.GetZ();

        bool Ok(Tile t)
        {
            if (t == null)
                return false;
            if (t.GetX() == mechX && t.GetZ() == mechZ)
                return true; // the mech's own tile (already adjacent)
            return !t.HasActor() && usable(t);
        }

        Tile preferred = targetTile.GetNextTile(targetTile.GetDirectionTo(mechTile));
        if (Ok(preferred))
            return preferred;

        Tile best = null;
        long bestDist = long.MaxValue;
        for (int d = 0; d < 8; d++)
        {
            Tile cand = targetTile.GetNextTile((Direction)d);
            if (!Ok(cand))
                continue;
            long dx = cand.GetX() - mechX;
            long dz = cand.GetZ() - mechZ;
            long dist = dx * dx + dz * dz;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = cand;
            }
        }
        return best;
    }

    private static bool IsMech(Actor actor)
    {
        var template = actor.GetTemplate();
        if (template == null)
            return false;
        string id = template.GetID();
        string nm = template.name;
        foreach (var t in TargetTemplateIds)
            if (string.Equals(id, t, StringComparison.Ordinal) || string.Equals(nm, t, StringComparison.Ordinal))
                return true;
        return false;
    }
}
