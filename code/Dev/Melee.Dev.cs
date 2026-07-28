using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Game.Tactical;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for exercising MeleeCookOffSystem in a live mission, since the bridge
// cannot drive combat directly (no move-and-attack command) and the vanilla cook-off
// is chance-gated at 5%.
//
// CookOff runs one half of an A/B: it spawns a target next to a player unit, grants
// it racial.critical_hit_explosion (Chance 100, IsAttack payload) so the blast is
// certain, and kills it through the game's damage pipeline attributed to a skill of
// the caller's choosing. Death resolves over the following frames, so read the
// verdict with a Report call afterwards rather than from the CookOff return.
//
// Verb args are POSITIONAL and must be a JSON array. An object is silently ignored
// and the defaults run instead, which quietly turns the control arm into a second
// melee arm:
//
//   {"verb":"Melee.CookOff","args":[false],"mutate":true}  control: blast lands on the witness
//   {"verb":"Melee.CookOff","args":[true],"mutate":true}   test:    witness is passed over
[DevVerb]
public static class Melee
{
    private const string BlastId = "racial.critical_hit_explosion";
    private const string MeleeSkillId = "active.sextans_slash";
    private const string ControlSkillId = "active.voymastina_mech_gun";
    private const string DefaultTargetId = "enemy.pirate_vehicle.chaingun_guntruck";

    // The witness of the last CookOff run: the player unit the target was spawned
    // next to, and its hitpoints at the moment the killing blow landed.
    private static Actor _witness;
    private static int _witnessHpAtKill;
    private static string _lastRun = "none";

    // Which of our melee skills currently carry wmgfl_melee, read off the LIVE
    // templates. Confirms the KDL landed, including on the SSR swords and the
    // calibration rank clones, which inherit the tag through the loader's
    // chained-clone re-inheritance rather than declaring it themselves.
    public static object Tags()
    {
        var rows = new List<object>();
        foreach (var template in Templates.All<SkillTemplate>())
        {
            var id = template.GetID();
            if (id == null || (!id.StartsWith("active.sextans") && !id.StartsWith("active.voymastina_mech")))
                continue;
            var names = new List<string>();
            var tags = template.Tags;
            for (var i = 0; tags != null && i < tags.Count; i++)
                if (tags[i]?.name != null)
                    names.Add(tags[i].name);
            rows.Add(new { id, melee = names.Contains("wmgfl_melee"), tags = names });
        }
        return rows;
    }

    // MeleeCookOffSystem's internal state, gate by gate. Says which check rejected a
    // run rather than leaving an empty log to be interpreted: a zero at DeathrattleCalls
    // means the patch never fired, a zero at MeleeHitsSeen means the tag never matched,
    // WreckInLedger short of PayloadIsAttack means the ledger lost the victim, and so on.
    public static object Probe()
    {
        var system = MeleeCookOffSystem.Instance;
        if (system == null)
            return new { error = "MeleeCookOffSystem has not initialised" };
        var d = system.Diagnostics;
        return new
        {
            ledgerSize = system.LedgerSize,
            sparedSize = system.SparedSize,
            meleeHitsSeen = d.MeleeHitsSeen,
            ledgerAdds = d.LedgerAdds,
            deathrattleCalls = d.DeathrattleCalls,
            payloadResolved = d.PayloadResolved,
            payloadIsAttack = d.PayloadIsAttack,
            wreckInLedger = d.WreckInLedger,
            killerResolved = d.KillerResolved,
            spareRecorded = d.SpareRecorded,
            damageZeroed = d.DamageZeroed,
            lastSkillEvaluated = d.LastSkillEvaluated,
            lastSkillVerdict = d.LastSkillVerdict,
            lastDeathrattlePayload = d.LastDeathrattlePayload,
            damageAtSpared = d.DamageAtSpared,
            lastSparedHit = d.LastSparedHit,
            turnEndClears = d.TurnEndClears,
            lastSparedSource = d.LastSparedSource,
        };
    }

    // The verdict of the last CookOff run. Call a second or two after CookOff so the
    // death and any blast have resolved.
    public static object Report()
    {
        if (_witness == null)
            return new { run = _lastRun, error = "no CookOff run yet" };
        // Guard against answering with an actor from a finished mission (the Il2Cpp
        // wrapper keeps a dead one resolvable). Membership of the live actor list is
        // the test, not the round number, which ticks on during a run.
        if (!Mission.InMission || !InCurrentMission(_witness))
            return new { run = _lastRun, error = "the CookOff run was in a different mission; re-run it" };

        var alive = _witness.IsAlive();
        // The witness is normally a multi-element squad, and a blast that guts a
        // non-lead element does not move Actor.GetHitpoints. Sum the elements, the
        // same way BloodRally asks how hurt an actor is.
        var hp = alive ? RallyBarsSystem.SumHitpoints(_witness) : 0;
        return new
        {
            run = _lastRun,
            witnessAlive = alive,
            witnessHpAtKill = _witnessHpAtKill,
            witnessHpNow = hp,
            blastLanded = !alive || hp < _witnessHpAtKill,
        };
    }

    // Spawn an exploding target beside a player unit and kill it, attributing the kill
    // to a melee skill (melee: true) or a rifle skill (melee: false).
    [MutatingVerb]
    public static object CookOff(bool melee = true, string targetId = DefaultTargetId, bool grantBlast = true)
    {
        if (!Mission.InMission)
            return new { error = "not in a tactical mission" };

        var witness = FirstLivingPlayerActor();
        if (witness == null)
            return new { error = "no living player actor to stand next to the blast" };
        var witnessTile = witness.GetTile();
        if (witnessTile == null)
            return new { error = "witness is not on a tile" };

        var blast = grantBlast ? Templates.ById<SkillTemplate>(BlastId) : null;
        var skillTemplate = Templates.ById<SkillTemplate>(melee ? MeleeSkillId : ControlSkillId);
        var target = Templates.ById<EntityTemplate>(targetId);
        if ((grantBlast && blast == null) || skillTemplate == null || target == null)
            return new { error = $"unresolved template (blast={blast != null}, skill={skillTemplate != null}, target={target != null})" };

        // The killing blow has to be a real owned Skill: the engine records the
        // template off the instance, and the damage path expects an owner. Granted to
        // the witness for the duration and taken back below, because these skill
        // clones set IsRemovedAfterCombat #false and would otherwise ride a random
        // rifle squad's skill bar into the campaign save.
        var granted = SkillEffects.FindInstance(witness.GetSkills(), skillTemplate) == null;
        var weapon = GrantAndFind(witness, skillTemplate);
        if (weapon == null)
            return new { error = $"could not grant '{skillTemplate.GetID()}' to the witness" };

        var victim = SpawnAdjacent(target, witnessTile);
        if (victim == null)
        {
            Revoke(witness, skillTemplate, granted);
            return new { error = "no free tile beside the witness to spawn the target on" };
        }

        string refusal = null;
        // grantBlast false leaves the target with only its own racial deathrattle, which
        // is how the chance-gated and multi-repetition blasts get exercised.
        if (grantBlast && !SkillEffects.TryAddEffect(victim, blast, msg => refusal = msg))
        {
            // Leave no live hostile behind next to the player's squad.
            Units.Despawn(victim);
            Revoke(witness, skillTemplate, granted);
            return new { error = $"could not grant '{BlastId}' to the target: {refusal ?? "unknown"}" };
        }

        // Heal first: the arms run back to back in one mission, and a witness left
        // gutted by the previous blast dies to this one, ending the mission before the
        // verdict can be read.
        Combat.Heal(witness, RallyBarsSystem.SumHitpointsMax(witness));
        _witness = witness;
        _witnessHpAtKill = RallyBarsSystem.SumHitpoints(witness);
        _lastRun = melee ? "melee" : "control";

        // The game's own debug-destroy DamageInfo. A hand-built one carries no armour
        // penetration, so an armoured vehicle absorbs every point of it and never dies.
        var resolved = victim.OnDamageReceived(witness, weapon, DamageInfo.CreateDebugDestroy());
        var victimAlive = victim.IsAlive();
        // What MeleeCookOffSystem reads off the wreck. Reported so a run that fails to
        // spare anyone says whether the engine's own killer record was populated, or
        // whether the suppression logic simply did not fire.
        var lastSkill = SafeId(victim.GetLastAttackedBySkill());
        var killerSet = SafeKiller(victim);
        Revoke(witness, skillTemplate, granted);
        return new
        {
            run = _lastRun,
            killedWith = skillTemplate.GetID(),
            target = targetId,
            dealt = resolved != null ? resolved.Damage : 0,
            victimAlive,
            lastAttackedBySkill = lastSkill,
            killer = killerSet,
            witnessHpAtKill = _witnessHpAtKill,
            next = "call Melee.Report in a second or two",
        };
    }

    private static bool InCurrentMission(Actor actor)
    {
        var actors = Mission.Actors();
        for (var i = 0; actors != null && i < actors.Count; i++)
            if (actors[i]?.Pointer == actor.Pointer)
                return true;
        return false;
    }

    private static string SafeId(SkillTemplate template)
    {
        try { return template != null ? template.GetID() : "null"; }
        catch { return "unreadable"; }
    }

    private static string SafeKiller(Actor victim)
    {
        try
        {
            var killer = victim.Killer;
            if (killer == null)
                return "null";
            return killer.TryCast<Actor>() != null ? "actor" : "entity-not-actor";
        }
        catch { return "unreadable"; }
    }

    // Hit the last CookOff witness with an ordinary non-blast attack and report what
    // landed. If a waiver ever leaked into covering "the next hit of any kind" rather
    // than its own blast, this reads 0 instead of full damage.
    [MutatingVerb]
    public static object Poke(int amount = 25)
    {
        if (_witness == null)
            return new { error = "no CookOff run yet" };
        if (!Mission.InMission || !InCurrentMission(_witness))
            return new { error = "the CookOff run was in a different mission; re-run it" };

        var rifle = Templates.ById<SkillTemplate>(ControlSkillId);
        if (rifle == null)
            return new { error = $"unresolved template '{ControlSkillId}'" };
        var granted = SkillEffects.FindInstance(_witness.GetSkills(), rifle) == null;
        var weapon = GrantAndFind(_witness, rifle);
        if (weapon == null)
            return new { error = $"could not grant '{ControlSkillId}'" };

        var before = RallyBarsSystem.SumHitpoints(_witness);
        var resolved = _witness.OnDamageReceived(_witness, weapon, new DamageInfo { Damage = amount, ArmorPenetration = 200 });
        var after = RallyBarsSystem.SumHitpoints(_witness);
        Revoke(_witness, rifle, granted);
        return new
        {
            requested = amount,
            resolvedDamage = resolved != null ? resolved.Damage : 0,
            hpBefore = before,
            hpAfter = after,
            landed = resolved != null && resolved.Damage > 0,
        };
    }

    private static Actor FirstLivingPlayerActor()
    {
        var actors = Mission.Actors(FactionType.Player);
        for (var i = 0; actors != null && i < actors.Count; i++)
            if (actors[i] != null && actors[i].IsAlive() && actors[i].GetTile() != null)
                return actors[i];
        return null;
    }

    // The first of the eight neighbours the game accepts a spawn on.
    private static Actor SpawnAdjacent(EntityTemplate template, Tile origin)
    {
        var faction = HostileFaction();
        for (var dir = 0; dir < 8; dir++)
        {
            var tile = Tiles.Next(origin, (Direction)dir);
            if (tile == null)
                continue;
            var spawned = Units.Spawn(template, faction, tile);
            if (spawned != null)
                return spawned;
        }
        return null;
    }

    // A faction the mission actually fields, so the spawn is not refused for a faction
    // slot this map never set up. Missions vary in who the enemy is (Pirates, Rogue
    // Army, Constructs, Wildlife), so read it off the field rather than naming one.
    private static FactionType HostileFaction()
    {
        var factions = Mission.Manager?.GetFactions();
        for (var i = 0; factions != null && i < factions.Length; i++)
        {
            var faction = factions[i];
            if (faction == null)
                continue;
            var type = faction.GetFactionType();
            if (type is FactionType.Player or FactionType.PlayerAI or FactionType.Neutral or FactionType.Civilian)
                continue;
            var actors = faction.GetActors();
            if (actors != null && actors.Count > 0)
                return type;
        }
        return FactionType.Pirates;
    }

    // Add the template to the actor's container and hand back the live instance.
    private static Skill GrantAndFind(Actor actor, SkillTemplate template)
    {
        var skills = actor?.GetSkills();
        if (skills == null)
            return null;
        var existing = SkillEffects.FindInstance(skills, template);
        if (existing != null)
            return existing;
        return SkillEffects.TryAddEffect(actor, template, null) ? SkillEffects.FindInstance(skills, template) : null;
    }

    // Take the loaned skill back, but only when this run is what added it.
    private static void Revoke(Actor actor, SkillTemplate template, bool granted)
    {
        if (!granted)
            return;
        try { actor?.GetSkills()?.Remove(template); }
        catch { }
    }
}
