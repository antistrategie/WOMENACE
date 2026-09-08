using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Protects the killer from a deathrattle blast after a wmgfl_melee killing blow.
// Only the killer's hitpoint and armour damage are waived. The explosion's effects
// and damage to everyone else still resolve, including delegated shrapnel.
// Non-attack deathrattles, such as the worker drone's morale scream, are unaffected.
public sealed class MeleeCookOffSystem : JiangyuSystem
{
#if JIANGYU_DEV
    // Gate counters distinguish missing kill attribution from unmatched blast damage.
    internal sealed class Counters
    {
        public int MeleeHitsSeen, LedgerAdds, DeathrattleCalls, PayloadResolved,
                   PayloadIsAttack, WreckInLedger, KillerResolved, SpareRecorded, DamageZeroed;
        public string LastSkillEvaluated = "none";
        public bool LastSkillVerdict;
        public string LastDeathrattlePayload = "none";
        public int DamageAtSpared;
        public string LastSparedHit = "none";
        public int BlastsLaunched, SourceMisses;
        public string LastSparedSource = "none";
        public string LastBlastSource = "none";
    }

    internal static MeleeCookOffSystem Instance { get; private set; }
    internal readonly Counters Diagnostics = new();
    internal int LedgerSize => _meleeStruck.Count;
    internal int SparedSize => _sparedFrom.Count;
    internal int SceneGeneration { get; private set; }
#endif

    // Cache the tag verdict per skill template to avoid marshalling tags on every hit.
    private readonly Dictionary<IntPtr, bool> _isMelee = new();

    private sealed class Deathrattle
    {
        // Retain the wrapper while its native pointer is used as a dictionary key.
        public Skill Origin;
        public Actor Attacker;
        public string PayloadId;
    }

    private readonly Dictionary<IntPtr, Deathrattle> _deathrattles = new();

    // Retain the actual launched skills, not their shared templates. Another wreck
    // can use the same template without sharing this protection. Repetitions and
    // delayed impacts keep their waiver until the mission ends, including when an
    // unrelated actor finishes its turn while the explosion is still resolving.
    private sealed class Waiver
    {
        public Actor Attacker;
        public readonly Dictionary<IntPtr, Skill> Blasts = new();
    }

    private readonly Dictionary<IntPtr, Waiver> _sparedFrom = new();

    // A deathrattle can run before Killer and GetLastAttackedBySkill are populated.
    // Record the attacker before damage resolves and clear the entry on a later
    // non-melee hit. Retaining the actor wrappers prevents native pointer reuse.
    private sealed class MeleeHit
    {
        public Actor Victim;
        public Actor Attacker;
    }

    private readonly Dictionary<IntPtr, MeleeHit> _meleeStruck = new();

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        _deathrattles.Clear();
        _sparedFrom.Clear();
        _meleeStruck.Clear();
        _isMelee.Clear();
#if JIANGYU_DEV
        SceneGeneration++;
#endif
    }

    public override void OnInit()
    {
#if JIANGYU_DEV
        Instance = this;
#endif
        // Both DeathrattleHandler.OnDeath and OnElementDeath funnel through this
        // private helper, so one hook covers the actor-wide and per-element forms.
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.Effects.DeathrattleHandler", "TriggerSkill", OnDeathrattle);
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.Skill", "Use", 2, OnBlastUse);
        // The 3-arg overload is the concrete Actor override (a 4-arg form also
        // exists), the same binding MeleeVsFliersSystem uses.
        Context.Patches.Prefix("Il2CppMenace.Tactical.Actor", "OnDamageReceived", 3, OnDamageReceived);
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
            var origin = handler?.ParentSkill;
            if (origin == null)
                return;
            // Per-element deathrattles can fire again after a different attacker
            // lands the next kill. Each invocation decides its own protection.
            _deathrattles.Remove(origin.Pointer);
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
            // Only attack payloads need damage protection.
            if (payload == null || !payload.IsAttack)
                return;

            var wreck = handler.GetActor();
            if (wreck == null || !_meleeStruck.TryGetValue(wreck.Pointer, out var hit))
                return;
#if JIANGYU_DEV
            Diagnostics.WreckInLedger++;
#endif
            // The receive prefix captures the attacker before this nested deathrattle.
            var killer = hit.Attacker;
            if (killer == null)
                return;
#if JIANGYU_DEV
            Diagnostics.KillerResolved++;
#endif
            _deathrattles[origin.Pointer] = new Deathrattle
            {
                Origin = origin,
                Attacker = killer,
                PayloadId = payload.GetID(),
            };
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"melee cook-off: deathrattle check failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // TriggerSkill (native RVA 0x752F90) sets the launched skill's SourceSkill to
    // the handler's ParentSkill before calling Use. Bind here, after the chance roll,
    // so failed rolls never create a waiver. AddSkillHandler.OnApply (0x745100)
    // attributes delegated shrapnel to this launched instance through SourceSkill.
    private void OnBlastUse(PatchInfo info)
    {
        try
        {
            if (_deathrattles.Count == 0 || info.Instance is not Skill blast)
                return;
            var source = blast.SourceSkill;
            if (source == null || !_deathrattles.TryGetValue(source.Pointer, out var deathrattle)
                || blast.GetTemplate()?.GetID() != deathrattle.PayloadId)
                return;

            var killer = deathrattle.Attacker;
            if (!_sparedFrom.TryGetValue(killer.Pointer, out var waiver))
                _sparedFrom[killer.Pointer] = waiver = new Waiver { Attacker = killer };
            waiver.Blasts[blast.Pointer] = blast;
#if JIANGYU_DEV
            Diagnostics.BlastsLaunched++;
            Diagnostics.SpareRecorded++;
            Diagnostics.LastBlastSource = Describe(blast);
#endif
            Context.Log.Debug($"melee cook-off: '{deathrattle.PayloadId}' will pass over its melee attacker");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"melee cook-off: blast tracking failed: {ex.GetType().Name}: {ex.Message}");
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
            var damageInfo = (info.Args is { Count: > 2 } ? info.Args[2] : null) as DamageInfo;
#if JIANGYU_DEV
            // Record before matching so a rejected source is visible in Melee.Probe.
            Diagnostics.DamageAtSpared++;
            Diagnostics.LastSparedHit = damageInfo == null
                ? $"{payload?.GetID() ?? "null-skill"} damageInfo=null"
                : $"{payload?.GetID() ?? "null-skill"} aoe={damageInfo.IsAoE} dmg={damageInfo.Damage} armour={damageInfo.ArmorDamage}";
            Diagnostics.LastSparedSource = Describe(skill);
#endif
            // Critical-hit and hover-bomb explosions both deliver direct payload
            // damage followed by shrapnel. Include the incoming skill itself before
            // walking its sources. Instance identity keeps unrelated blasts separate.
            if (!ComesFromBlast(skill, waiver))
            {
#if JIANGYU_DEV
                Diagnostics.SourceMisses++;
#endif
                return;
            }
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
            var chain = new List<string>();
            for (var depth = 0; skill != null && depth < 16; depth++)
            {
                chain.Add($"{skill.GetTemplate()?.GetID() ?? "null"}@{skill.Pointer.ToInt64():X}");
                skill = skill.SourceSkill;
            }
            return string.Join(" <- ", chain);
        }
        catch (Exception ex) { return $"unreadable {ex.GetType().Name}"; }
    }
#endif

    private static bool ComesFromBlast(Skill skill, Waiver waiver)
    {
        // Bound traversal in case another mod supplies a cyclic source chain.
        for (var depth = 0; skill != null && depth < 16; depth++)
        {
            if (waiver.Blasts.ContainsKey(skill.Pointer))
                return true;
            skill = skill.SourceSkill;
        }
        return false;
    }

    // Runs per damage event, so the answer is memoised per skill template: the tag walk
    // and its string marshalling happen once per skill in a mission, not once per shot.
    private bool IsMelee(SkillTemplate template)
    {
        if (template == null)
            return false;
        if (_isMelee.TryGetValue(template.Pointer, out var cached))
            return cached;
        var melee = MeleeSkills.IsMelee(template);
        _isMelee[template.Pointer] = melee;
#if JIANGYU_DEV
        Diagnostics.LastSkillEvaluated = template.GetID();
        Diagnostics.LastSkillVerdict = melee;
#endif
        return melee;
    }
}
