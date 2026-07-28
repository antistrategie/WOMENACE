using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Keeps a target that dies to one of our melee attacks from killing the doll who
// killed it.
//
// MENACE hangs its on-death blasts off a Deathrattle skill handler: a pirate rocket
// truck carries racial.pirate_rocket_truck_explosion_death (40% chance), a gas tank
// racial.gas_tank_death, and ANY vehicle that took the critical-hit defect picks up
// racial.critical_hit_explosion for the rest of the mission. Each fires an area
// attack centred on the wreck when it dies. That is a fair trade for a rifle squad
// shooting from cover, and a death sentence for a doll who had to close to blade
// range to land the kill.
//
// The blast still happens: it is drawn, it is heard, and it still guts whatever else
// is standing around the wreck. Only the melee attacker is passed over. Cancelling
// the deathrattle outright would be simpler, but it would delete the explosion's
// presentation and hand the player a silent, consequence-free demolition tool, and it
// would matter even when the attacker was never in the radius to begin with (the
// mech's pile bunker reaches six tiles).
//
// Scope: the killing skill must carry wmgfl_melee, a tag no vanilla skill has, so
// only our blades and the pile bunker trip it, and the deathrattle's payload must be
// an attack, so a harmless on-death event such as the worker drone's morale scream is
// untouched.
public sealed class MeleeCookOffSystem : JiangyuSystem
{
    private const string MeleeTag = "wmgfl_melee";

#if JIANGYU_DEV
    // Gate-by-gate counters for the Melee.Probe dev verb. The suppression is invisible
    // when it works and invisible when it silently does not, so a failed run needs to
    // say WHICH gate rejected it rather than leaving log absence to be guessed at.
    internal sealed class Counters
    {
        public int MeleeHitsSeen, LedgerAdds, DeathrattleCalls, PayloadResolved,
                   PayloadIsAttack, WreckInLedger, KillerResolved, SpareRecorded, DamageZeroed;
        public string LastSkillEvaluated = "none";
        public bool LastSkillVerdict;
        public string LastDeathrattlePayload = "none";
        public int DamageAtSpared;
        public string LastSparedHit = "none";
        public int TurnEndClears;
        public string LastSparedSource = "none";
        public string LastBlastSource = "none";
    }

    internal static MeleeCookOffSystem Instance { get; private set; }
    internal readonly Counters Diagnostics = new();
    internal int LedgerSize => _meleeStruck.Count;
    internal int SparedSize => _sparedFrom.Count;
#endif

    // Verdict per skill template, so the tag list is walked once per skill rather than
    // once per hit. Caching the TagTemplate's own pointer instead looks tidier but does
    // not work: the instance the loader registers is not the instance that ends up in
    // a skill's Tags list, so the compare never matches.
    private readonly Dictionary<IntPtr, bool> _isMelee = new();

    // Attackers being passed over, and which blasts each is being passed over for.
    //
    // Matching on the blast rather than on "the next hit that lands" is what makes this
    // safe. TriggerSkill rolls the deathrattle's Chance AFTER this prefix has run, and
    // both pirate trucks are Chance 40, so most entries are for a blast that never
    // fires. Those orphans are harmless here: nothing else carries that SourceSkill, so
    // they simply never match and get dropped at the next clear. A waiver scoped to
    // "the next hit" would instead be spent absorbing reaction fire or a burn tick.
    //
    // Entries are not consumed on use, because one blast can land more than once:
    // active.rocket_truck_explosion is Repetitions 6, and one thrust or ult can kill two
    // exploders at once.
    private sealed class Waiver
    {
        public Actor Attacker;
        public readonly HashSet<IntPtr> Blasts = new();
    }

    private readonly Dictionary<IntPtr, Waiver> _sparedFrom = new();

    // The last melee hit each actor took, and who landed it, so the deathrattle can ask
    // what killed the wreck and which unit to pass the blast over. Entity.GetLastAttackedBySkill() looks like it should serve
    // instead, but a bridge probe read it back null on a kill that Entity.Killer
    // recorded fine: the engine sets it further up the attack path than
    // Actor.OnDamageReceived, so it cannot be relied on here. Only melee hits are
    // recorded, and any later hit clears the entry, so this stays a handful of entries
    // at most. The Actor wrapper is retained alongside the pointer key so the address
    // can never be recycled to a different actor mid-mission.
    private sealed class MeleeHit
    {
        public Actor Victim;
        public Actor Attacker;
    }

    private readonly Dictionary<IntPtr, MeleeHit> _meleeStruck = new();

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        _sparedFrom.Clear();
        _meleeStruck.Clear();
        _isMelee.Clear();
    }

    public override void OnInit()
    {
#if JIANGYU_DEV
        Instance = this;
#endif
        // Both DeathrattleHandler.OnDeath and OnElementDeath funnel through this
        // private helper, so one hook covers the actor-wide and per-element forms.
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.Effects.DeathrattleHandler", "TriggerSkill", OnDeathrattle);
        // The 3-arg overload is the concrete Actor override (a 4-arg form also
        // exists), the same binding MeleeVsFliersSystem uses.
        Context.Patches.Prefix("Il2CppMenace.Tactical.Actor", "OnDamageReceived", 3, OnDamageReceived);
        // InvokeOnTurnEnd(Actor) is raised from Actor.SetTurnDone, so this fires whenever
        // any unit anywhere finishes acting, not on a turn boundary. That makes it a
        // coarse upper bound on how long a waiver can linger rather than a precise
        // lifetime, which is fine now that a waiver only matches its own blast.
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnTurnEnd", _ =>
        {
#if JIANGYU_DEV
            if (_sparedFrom.Count > 0)
                Diagnostics.TurnEndClears++;
#endif
            _sparedFrom.Clear();
        });
    }

    // A wreck is about to detonate. If a melee attack of ours is what killed it, note
    // its killer, then let the blast run.
    private void OnDeathrattle(PatchInfo info)
    {
        try
        {
#if JIANGYU_DEV
            Diagnostics.DeathrattleCalls++;
#endif
            var handler = (info.Instance as Il2CppObjectBase)?.TryCast<DeathrattleHandler>();
            var payload = handler?.m_Template?.Skill;
#if JIANGYU_DEV
            if (payload != null)
            {
                Diagnostics.PayloadResolved++;
                Diagnostics.LastDeathrattlePayload = payload.GetID();
                if (payload.IsAttack)
                    Diagnostics.PayloadIsAttack++;
            }
#endif
            // IsAttack separates the seven damaging blasts from the on-death events
            // that cannot hurt anyone.
            if (payload == null || !payload.IsAttack)
                return;

            var wreck = handler.GetActor();
            if (wreck == null || !_meleeStruck.TryGetValue(wreck.Pointer, out var hit))
                return;
#if JIANGYU_DEV
            Diagnostics.WreckInLedger++;
#endif
            // The attacker comes from the ledger, not Entity.Killer: a bridge probe
            // caught Killer still null here and populated only once the damage call
            // has returned, which is after the deathrattle has already run.
            var killer = hit.Attacker;
            if (killer == null)
                return;
#if JIANGYU_DEV
            Diagnostics.KillerResolved++;
            Diagnostics.SpareRecorded++;
#endif

            if (!_sparedFrom.TryGetValue(killer.Pointer, out var waiver))
                _sparedFrom[killer.Pointer] = waiver = new Waiver { Attacker = killer };
            waiver.Blasts.Add(payload.Pointer);
            Context.Log.Debug($"melee cook-off: '{payload.GetID()}' will pass over its melee attacker");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"melee cook-off: deathrattle check failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The blast resolves over the following frames and arrives here once per target.
    // Zero it for the attacker being passed over and leave every other target alone.
    private void OnDamageReceived(PatchInfo info)
    {
        try
        {
            if (info.Instance is not Actor victim)
                return;
            var skill = (info.Args is { Count: > 1 } ? info.Args[1] : null) as Skill;
            var payload = skill?.GetTemplate();

            // Remember what last hit this actor. Every hit overwrites the verdict, so a
            // vehicle softened up in melee and then finished with gunfire still cooks
            // off on its killer: only the last blow counts.
            if (IsMelee(payload))
            {
#if JIANGYU_DEV
                Diagnostics.MeleeHitsSeen++;
                if (!_meleeStruck.ContainsKey(victim.Pointer))
                    Diagnostics.LedgerAdds++;
#endif
                var attacker = ((info.Args is { Count: > 0 } ? info.Args[0] : null) as Il2CppObjectBase)?.TryCast<Actor>();
                _meleeStruck[victim.Pointer] = new MeleeHit { Victim = victim, Attacker = attacker };
            }
            else
            {
                _meleeStruck.Remove(victim.Pointer);
            }

            // Nothing is being spared almost all of the time, so the blast-sparing half
            // costs one dictionary count check per damage event in the game.
            if (_sparedFrom.Count == 0)
                return;
            if (!_sparedFrom.TryGetValue(victim.Pointer, out var waiver))
                return;
            // The blast delegates its damage to a differently named skill, but the engine
            // stamps that skill's SourceSkill with the deathrattle payload it came from,
            // so this identifies the blast precisely. Skill.Source would be tidier still
            // and is what TriggerSkill sets, but a bridge probe read it back null here.
            var origin = SafeTemplatePointer(skill?.SourceSkill);
            if (origin == IntPtr.Zero || !waiver.Blasts.Contains(origin))
                return;

            var damageInfo = (info.Args is { Count: > 2 } ? info.Args[2] : null) as DamageInfo;
#if JIANGYU_DEV
            Diagnostics.DamageAtSpared++;
            Diagnostics.LastSparedHit = damageInfo == null
                ? $"{payload?.GetID() ?? "null-skill"} damageInfo=null"
                : $"{payload?.GetID() ?? "null-skill"} aoe={damageInfo.IsAoE} dmg={damageInfo.Damage} armour={damageInfo.ArmorDamage}";
            // TriggerSkill stamps the blast it launches with Source = the wreck's last
            // attacker and SourceSkill = the deathrattle skill. If the delegated damage
            // skill still carries them, the whole waiver can go: the gate becomes
            // "this damage came from a blast whose source is me", evaluated at damage
            // time, with no window to orphan and no repetition to miss.
            Diagnostics.LastSparedSource = Describe(skill);
#endif
            if (damageInfo == null)
                return;
            damageInfo.Damage = 0;
            damageInfo.ArmorDamage = 0;
#if JIANGYU_DEV
            Diagnostics.DamageZeroed++;
#endif
            Context.Log.Debug($"melee cook-off: waived area damage from '{payload?.GetID() ?? "unknown"}' on its melee killer");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"melee cook-off: sparing the attacker failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

#if JIANGYU_DEV
    // What the engine attributes an incoming skill to, for the Source/SourceSkill probe.
    private static string Describe(Skill skill)
    {
        try
        {
            if (skill == null)
                return "skill=null";
            var source = skill.Source;
            var sourceSkill = skill.SourceSkill;
            var sourceName = source == null ? "null" : (source.TryCast<Actor>() != null ? "actor" : "entity");
            var sourceId = sourceSkill?.GetTemplate()?.GetID() ?? "null";
            return $"Source={sourceName} SourceSkill={sourceId}";
        }
        catch (Exception ex) { return $"unreadable {ex.GetType().Name}"; }
    }
#endif

    // The template a skill instance came from, as a raw pointer, or zero when it cannot
    // be read. Reading a field off a dead Il2Cpp object can throw.
    private static IntPtr SafeTemplatePointer(Skill skill)
    {
        try { return skill?.GetTemplate()?.Pointer ?? IntPtr.Zero; }
        catch { return IntPtr.Zero; }
    }

    // Runs per damage event, so the answer is memoised per skill template: the tag walk
    // and its string marshalling happen once per skill in a mission, not once per shot.
    private bool IsMelee(SkillTemplate template)
    {
        if (template == null)
            return false;
        if (_isMelee.TryGetValue(template.Pointer, out var cached))
            return cached;
        var melee = false;
        var tags = template.Tags;
        for (var i = 0; !melee && tags != null && i < tags.Count; i++)
            melee = tags[i]?.name == MeleeTag;
        _isMelee[template.Pointer] = melee;
#if JIANGYU_DEV
        Diagnostics.LastSkillEvaluated = template.GetID();
        Diagnostics.LastSkillVerdict = melee;
#endif
        return melee;
    }
}
