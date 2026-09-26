using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Game.Tactical;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Dev verbs for exercising MeleeCookOffSystem in a disposable mission. Deathrattles
// and vehicle defects are often chance gated, and a control blast can kill its witness.
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
    private const string PileBunkerSkillId = "active.voymastina_mech_drill";
    private const string ControlSkillId = "active.voymastina_mech_gun";
    private const string DefaultTargetId = "enemy.pirate_vehicle.chaingun_guntruck";

    // The witness of the last CookOff run: the player unit the target was spawned
    // next to, and its hitpoints at the moment the killing blow landed.
    private static Actor _witness;
    private static int _witnessHpAtKill;
    private static int _witnessArmourAtKill;
    private static int _witnessSceneGeneration;
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
            rows.Add(new { id, melee = MeleeSkills.IsMelee(template), tags = names });
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
            blastsLaunched = d.BlastsLaunched,
            sourceMisses = d.SourceMisses,
            lastSparedSource = d.LastSparedSource,
            lastBlastSource = d.LastBlastSource,
            inheritedDefectHits = d.InheritedDefectHits,
            damageEffectHits = d.DamageEffectHits,
            lastDamageEffectHit = d.LastDamageEffectHit,
            lastMeleeHit = d.LastMeleeHit,
            airborneDamageRestored = MeleeVsFliersSystem.Instance?.DamageRestored ?? 0,
            lastAirborneHit = MeleeVsFliersSystem.Instance?.LastRestoredHit ?? "none",
        };
    }

    public static object Airborne(Actor target)
    {
        if (!Mission.InMission || target == null || !InCurrentMission(target))
            return new { error = "select a target in the current mission" };
        var template = target.GetTemplate()?.TryCast<EntityTemplate>();
        var tags = new List<string>();
        for (var i = 0; template?.Tags != null && i < template.Tags.Count; i++)
            tags.Add(template.Tags[i]?.name);
        return new
        {
            target = template?.GetID(),
            flying = template?.MovementType?.Flying ?? false,
            tags = string.Join(", ", tags),
            airborne = MeleeVsFliersSystem.IsAirborne(target),
            hp = target.GetHitpoints(),
        };
    }

    // The verdict of the last CookOff run. Call a second or two after CookOff so the
    // death and any blast have resolved.
    public static object Report()
    {
        if (_witness == null)
            return new { run = _lastRun, error = "no CookOff run yet" };
        // Dead witnesses can leave the live actor list. Use the scene generation so
        // a lethal blast is reported as damage rather than as a mission change.
        if (!Mission.InMission || _witnessSceneGeneration != MeleeCookOffSystem.Instance.SceneGeneration)
            return new { run = _lastRun, error = "the cook-off test was in a different mission, re-run it" };

        var alive = _witness.IsAlive();
        // The witness is normally a multi-element squad, and a blast that guts a
        // non-lead element does not move Actor.GetHitpoints. Sum the elements, the
        // same way BloodRally asks how hurt an actor is.
        var hp = alive ? RallyBarsSystem.SumHitpoints(_witness) : 0;
        var armour = alive ? Units.ArmorDurability(_witness) : 0;
        return new
        {
            run = _lastRun,
            witnessAlive = alive,
            witnessHpAtKill = _witnessHpAtKill,
            witnessHpNow = hp,
            witnessArmourAtKill = _witnessArmourAtKill,
            witnessArmourNow = armour,
            blastLanded = !alive || hp < _witnessHpAtKill || armour < _witnessArmourAtKill,
        };
    }

    // Leave a target alive for the player to kill with a normal attack. A native
    // walker with grantBlast=false exercises its own explosion rather than replacing
    // that path with the guaranteed test blast. Removing armour keeps melee viable.
    [MutatingVerb]
    public static object Spawn(Actor witness, Tile tile = null, string targetId = DefaultTargetId, bool grantBlast = true)
    {
        if (!Mission.InMission || witness == null || !InCurrentMission(witness) || !witness.IsAlive())
            return new { error = "select a living witness in the current mission" };
        var origin = witness.GetTile();
        var target = Templates.ById<EntityTemplate>(targetId);
        var blast = grantBlast ? Templates.ById<SkillTemplate>(BlastId) : null;
        if (origin == null || target == null || (grantBlast && blast == null))
            return new { error = "unresolved target, blast or witness tile" };

        var victim = tile == null ? SpawnAdjacent(target, origin) : Units.Spawn(target, HostileFaction(), tile);
        if (victim == null)
            return new { error = "could not spawn the target on a free tile" };
        string refusal = null;
        if (grantBlast && !SkillEffects.TryAddEffect(victim, blast, msg => refusal = msg))
        {
            Units.Despawn(victim);
            return new { error = $"could not grant '{BlastId}': {refusal ?? "unknown"}" };
        }

        victim.SetArmorDurability(0);
        _witness = witness;
        _witnessHpAtKill = RallyBarsSystem.SumHitpoints(witness);
        _witnessArmourAtKill = Units.ArmorDurability(witness);
        _witnessSceneGeneration = MeleeCookOffSystem.Instance.SceneGeneration;
        _lastRun = "manual melee";
        return new { target = victim, blast = grantBlast ? BlastId : "native", armour = 0, next = "kill the target with melee, then call Melee.Report and Melee.Probe" };
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
        using var cleanup = new ProbeCleanup(message => Debug.LogWarning(message));
        var weapon = BorrowSkill(cleanup, witness, skillTemplate);
        if (weapon == null)
            return new { error = $"could not grant '{skillTemplate.GetID()}' to the witness" };

        var victim = SpawnAdjacent(target, witnessTile);
        if (victim == null)
        {
            return new { error = "no free tile beside the witness to spawn the target on" };
        }

        string refusal = null;
        // grantBlast false leaves the target with only its own racial deathrattle, which
        // is how the chance-gated and multi-repetition blasts get exercised.
        if (grantBlast && !SkillEffects.TryAddEffect(victim, blast, msg => refusal = msg))
        {
            // Leave no live hostile behind next to the player's squad.
            Units.Despawn(victim);
            return new { error = $"could not grant '{BlastId}' to the target: {refusal ?? "unknown"}" };
        }

        // A control blast can kill the witness. Negative OnDamageReceived damage
        // does not heal this vehicle path, so tests require a disposable mission.
        _witness = witness;
        _witnessHpAtKill = RallyBarsSystem.SumHitpoints(witness);
        _witnessArmourAtKill = Units.ArmorDurability(witness);
        _witnessSceneGeneration = MeleeCookOffSystem.Instance.SceneGeneration;
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

    // Exercise the native defect path rather than attaching a deathrattle directly.
    // Only the synchronous damage call sees forced weights. Use a fresh test vehicle
    // and a disposable witness, not the mission's only player actor.
    [MutatingVerb]
    public static object CriticalHit(Actor witness, Actor target, bool melee = true)
    {
        if (!Mission.InMission || witness == null || target == null
            || !InCurrentMission(witness) || !InCurrentMission(target)
            || !witness.IsAlive() || !target.IsAlive() || witness.Pointer == target.Pointer)
            return new { error = "select a living witness and a separate test vehicle in this mission" };
        if (target.GetHitpoints() != Units.MaxHitpoints(target))
            return new { error = "use a fresh test vehicle at full hitpoints" };
        var skillTemplate = Templates.ById<SkillTemplate>(melee ? PileBunkerSkillId : ControlSkillId);
        var weights = Resources.FindObjectsOfTypeAll<DefectTemplate>()
            .Where(template => template != null)
            .Select(template => (Template: template, Chance: template.Chance))
            .ToList();
        var critical = weights.FirstOrDefault(entry => entry.Template.name == "defect.critical_hit").Template;
        if (critical == null || skillTemplate == null)
            return new { error = "unresolved critical defect or attack skill" };

        using var cleanup = new ProbeCleanup(message => Debug.LogWarning(message));
        var weapon = BorrowSkill(cleanup, witness, skillTemplate);
        if (weapon == null)
            return new { error = $"could not grant '{skillTemplate.GetID()}'" };
        _witness = witness;
        _witnessHpAtKill = RallyBarsSystem.SumHitpoints(witness);
        _witnessArmourAtKill = Units.ArmorDurability(witness);
        _witnessSceneGeneration = MeleeCookOffSystem.Instance.SceneGeneration;
        _lastRun = melee ? "critical defect melee" : "critical defect ranged control";

        var diagnostics = MeleeCookOffSystem.Instance.Diagnostics;
        var defectHitsBefore = diagnostics.DamageEffectHits;
        DamageInfo resolved;
        using (var weightCleanup = new ProbeCleanup(message => Debug.LogWarning(message)))
        {
            // Each restoration is independent, including when forcing a weight fails.
            foreach (var entry in weights)
                weightCleanup.Defer($"defect '{entry.Template.name}' Chance",
                    () => entry.Template.Chance = entry.Chance);
            foreach (var entry in weights)
                entry.Template.Chance = entry.Template.Pointer == critical.Pointer ? 100 : 0;
            target.SetArmorDurability(0);
            // A nonlethal hit crosses the heavy-defect threshold. Unlike the debug
            // kill or a bare Combat.Damage call, this explicitly permits defects.
            resolved = target.OnDamageReceived(witness, weapon, new DamageInfo
            {
                Damage = target.GetHitpoints() * 4 / 5,
                ArmorPenetration = 1000,
                IsAbleToInflictDefects = true,
            });
        }
        // Death can remove the effect before this call returns. Count its actual
        // damage callback instead of looking for a status on the dead vehicle.
        var defectDamageHits = diagnostics.DamageEffectHits - defectHitsBefore;
        return new
        {
            run = _lastRun,
            dealt = resolved?.Damage ?? 0,
            targetAlive = target.IsAlive(),
            defectDamageHits,
            defectDamageTrace = defectDamageHits > 0 ? diagnostics.LastDamageEffectHit : "none",
            next = "call Melee.Report and Melee.Probe after the blast resolves",
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
            return new { error = "the cook-off test was in a different mission, re-run it" };

        var rifle = Templates.ById<SkillTemplate>(ControlSkillId);
        if (rifle == null)
            return new { error = $"unresolved template '{ControlSkillId}'" };
        using var cleanup = new ProbeCleanup(message => Debug.LogWarning(message));
        var weapon = BorrowSkill(cleanup, _witness, rifle);
        if (weapon == null)
            return new { error = $"could not grant '{ControlSkillId}'" };

        var before = RallyBarsSystem.SumHitpoints(_witness);
        var resolved = _witness.OnDamageReceived(_witness, weapon, new DamageInfo { Damage = amount, ArmorPenetration = 200 });
        var after = RallyBarsSystem.SumHitpoints(_witness);
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

    private static Skill BorrowSkill(ProbeCleanup cleanup, Actor actor, SkillTemplate template)
    {
        var skills = actor?.GetSkills();
        if (skills == null)
            return null;
        var description = $"actor 0x{actor.Pointer.ToInt64():X}, skill '{template.GetID()}'";
        return cleanup.Borrow(description,
            () => SkillEffects.CountInstances(skills, template) > 0,
            () => SkillEffects.FindInstance(skills, template),
            () => SkillEffects.TryAddEffect(actor, template, message => Debug.LogWarning($"{description}: {message}")),
            () => ProbeCleanup.RemoveBorrowed(
                () => SkillEffects.CountInstances(skills, template),
                () => SkillEffects.RemoveInstances(skills, template)));
    }
}
