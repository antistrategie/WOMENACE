using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Vector's SSR kit (Banshee's Whisper). Two behaviours live here:
//
// 1. The burning-target bonus: any wielder's hits with either of the weapon's
//    firing modes deal 20% more damage to a target that is already Burning.
//    Applied in an
//    Actor.OnDamageReceived prefix (the same seam MeleeVsFliersSystem uses),
//    before the receive path applies the DamageInfo, and mirrored into the
//    hover preview through the handler's IExpectedDamageContributor.
//
// 2. The Overburn imprint: in Vector's hands only, hits on a Burning target
//    (including the very bullet whose build-up ignites it) apply
//    effect.wmgfl_overburn (the OverburnOnHit handler below rides the skill
//    and gates on SsrImprintSystem.IsOwnerWielding). Overburn lasts as long
//    as the Burn does. When an Overburned actor dies while Burning, both the
//    fire and Overburn spread to the healthiest enemy within SpreadRangeTiles
//    that is not already Burning, so a chain of deaths keeps the fire moving.
public sealed class VectorSsrSystem : JiangyuSystem
{
    // Every skill Banshee's Whisper grants. Both carry the kit: the weapon is
    // one weapon whichever of its firing modes is selected.
    public static readonly string[] SkillIds =
    [
        "active.vector_ssr_meltdown",
        "active.vector_ssr_searing",
    ];

    public const float BonusVsBurningMult = 1.2f;
    public const int SpreadRangeTiles = 2;

    private const string OverburnId = "effect.wmgfl_overburn";

    private static VectorSsrSystem _instance;

    private readonly HashSet<IntPtr> _skillTemplates = [];
    private SkillTemplate _overburn;
    private int _burnElement = -2;

    // The Overburn ledger. The engine unlinks a dying actor from its tile
    // before InvokeOnDeath fires, so the spread cannot read the death position
    // off the actor: the OnDamageReceived prefix and the effect's
    // OverburnTracker OnUpdate tick (which sees DoT damage and movement the
    // prefix does not) keep the last-known tile fresh, and the death handler
    // reads that. The Actor wrapper is retained so the pointer key can never
    // be recycled to a different actor mid-mission (same pattern as
    // ElementsSystem's gauges).
    private sealed class OverburnEntry
    {
        public Actor Actor;
        public Tile LastTile;
        // True when Overburn exists only in this ledger because the victim
        // was already dead at apply time (a killing blow): the container
        // never received the effect, so the death gate cannot read liveness
        // off it. For living bearers the effect itself (removed by the
        // tracker the moment the Burn ends) is the authority.
        public bool LedgerOnly;
    }

    private readonly Dictionary<IntPtr, OverburnEntry> _overburned = new();

    public override void OnSceneLoaded(int buildIndex, string sceneName)
        => _overburned.Clear();

    public override void OnInit()
    {
        _instance = this;
        // The 3-arg overload is the concrete Actor override (the 4-arg form
        // also exists), same binding MeleeVsFliersSystem uses.
        Context.Patches.Prefix("Il2CppMenace.Tactical.Actor", "OnDamageReceived", 3, OnDamageReceived);
        // Fires on every actor death with (target, killer, killerFaction).
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnDeath", OnActorDied);
    }

    public override void OnTemplatesApplied()
    {
        _skillTemplates.Clear();
        foreach (var id in SkillIds)
        {
            var template = Templates.ById<SkillTemplate>(id, msg => Context.Log.Warn($"vector ssr: {msg}"));
            if (template != null)
                _skillTemplates.Add(template.Pointer);
        }
        _overburn = Templates.ById<SkillTemplate>(OverburnId, msg => Context.Log.Warn($"vector ssr: {msg}"));
        _burnElement = ElementsSystem.ElementIndex("Burn");
    }

    internal static bool IsBurning(Actor actor)
        => _instance != null && ElementsSystem.HasLiveEffect(actor, _instance._burnElement);

    private static bool HasOverburn(Actor actor)
        => _instance != null && SkillEffects.CountInstances(actor?.GetSkills(), _instance._overburn) > 0;

    // Apply Overburn. Called by OverburnOnHitHandler after its owner gate has
    // passed, and by the spread so the effect travels with the fire. The
    // ledger entry lands even when the victim is already dead (a killing
    // blow): the death handler may be about to run for that very hit and
    // needs the entry plus a death position.
    internal static void ApplyOverburn(Actor victim)
    {
        var self = _instance;
        if (self?._overburn == null || victim == null)
            return;
        if (!self._overburned.TryGetValue(victim.Pointer, out var entry))
            self._overburned[victim.Pointer] = entry = new OverburnEntry { Actor = victim };
        entry.LastTile = victim.GetTile() ?? entry.LastTile;
        if (!victim.IsAlive())
        {
            entry.LedgerOnly = true;
            return;
        }
        if (HasOverburn(victim))
            return;
        if (SkillEffects.TryAddEffect(victim, self._overburn, msg => self.Context.Log.Warn($"vector ssr: {msg}")))
            entry.LedgerOnly = false;
    }

    // The effect's OverburnTracker tick: refresh the bearer's death position.
    // Catches movement and DoT damage, which never route through the
    // OnDamageReceived prefix below.
    internal static void RefreshOverburnTile(Actor bearer)
    {
        if (_instance == null || bearer == null)
            return;
        if (_instance._overburned.TryGetValue(bearer.Pointer, out var entry))
            entry.LastTile = bearer.GetTile() ?? entry.LastTile;
    }

    // Overburn lives exactly as long as the bearer's Burn: the tracker tick
    // removes the effect (and the ledger entry) the moment the fire is out.
    internal static void ExpireIfNotBurning(Actor bearer)
    {
        var self = _instance;
        if (self?._overburn == null || bearer == null || !bearer.IsAlive() || IsBurning(bearer))
            return;
        try
        {
            self._overburned.Remove(bearer.Pointer);
            bearer.GetSkills()?.Remove(self._overburn);
        }
        catch (Exception ex)
        {
            self.Context.Log.Warn($"vector ssr: Overburn expiry failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Banshee's Whisper hits a Burning target 20% harder, whoever wields it.
    // Doubles as an Overburn-ledger tile refresh for direct damage.
    private void OnDamageReceived(PatchInfo info)
    {
        try
        {
            if (info.Instance is not Actor victim)
                return;
            if (_overburned.TryGetValue(victim.Pointer, out var entry))
                entry.LastTile = victim.GetTile() ?? entry.LastTile;
            var skill = (info.Args is { Count: > 1 } ? info.Args[1] : null) as Skill;
            var skillTemplate = skill?.GetTemplate();
            if (skillTemplate == null || !_skillTemplates.Contains(skillTemplate.Pointer))
                return;
            var damageInfo = (info.Args is { Count: > 2 } ? info.Args[2] : null) as DamageInfo;
            if (damageInfo == null || damageInfo.Damage <= 0 || !IsBurning(victim))
                return;
            damageInfo.Damage = Mathf.RoundToInt(damageInfo.Damage * BonusVsBurningMult);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"vector ssr: burning bonus failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // An Overburned actor dying on fire passes both the flame and Overburn on:
    // the healthiest of its still-standing allies within range that is not
    // already Burning ignites, and can itself spread on death. Gated on the
    // ledger first: a death with no entry (the overwhelming common case)
    // exits on a dictionary miss without touching the skill container. For a
    // living bearer the container effect must still be live (a 3-turn-expired
    // Overburn does not spread); LedgerOnly covers the killing-blow apply
    // where the container never got the effect.
    private void OnActorDied(PatchInfo info)
    {
        try
        {
            var target = ((info.Args is { Count: > 0 } ? info.Args[0] : null) as Il2CppObjectBase)?.TryCast<Actor>();
            if (target == null || !_overburned.TryGetValue(target.Pointer, out var entry))
                return;
            if (!entry.LedgerOnly && !HasOverburn(target))
                return;
            if (!IsBurning(target))
                return;
            // GetTile() is null here (the engine unlinks the actor from the
            // grid before this event): the ledger's refreshed tile is the
            // death position.
            var tile = target.GetTile() ?? entry.LastTile;
            if (tile == null)
            {
                Context.Log.Debug("vector ssr: spread aborted, no death position known");
                return;
            }

            // Preferred target: the healthiest enemy in range not yet Burning
            // (gets the fire and Overburn). Fallback when everyone nearby is
            // already alight: the healthiest Burning enemy without Overburn
            // (gets Overburn alone), so the chain does not fizzle in a crowd
            // that is all on fire.
            Actor bestUnburned = null, bestBurning = null;
            var bestUnburnedHp = 0;
            var bestBurningHp = 0;
            var factions = TacticalManager.Get()?.GetFactions();
            for (var i = 0; factions != null && i < factions.Length; i++)
            {
                var actors = factions[i]?.GetActors();
                for (var j = 0; actors != null && j < actors.Count; j++)
                {
                    var candidate = actors[j];
                    if (candidate == null || !candidate.IsAlive() || candidate.Pointer == target.Pointer)
                        continue;
                    if (!candidate.IsAlliedWith(target))
                        continue;
                    var t = candidate.GetTile();
                    if (t == null || Pierce.Distance(tile, t) > SpreadRangeTiles)
                        continue;
                    var hp = RallyBarsSystem.SumHitpoints(candidate);
                    if (!IsBurning(candidate))
                    {
                        if (hp <= bestUnburnedHp)
                            continue;
                        bestUnburned = candidate;
                        bestUnburnedHp = hp;
                    }
                    else if (hp > bestBurningHp && !HasOverburn(candidate))
                    {
                        bestBurning = candidate;
                        bestBurningHp = hp;
                    }
                }
            }
            if (bestUnburned != null)
            {
                if (ElementsSystem.ApplyEffectTo(bestUnburned, _burnElement))
                {
                    ApplyOverburn(bestUnburned);
                    Context.Log.Debug($"vector ssr: Overburn spread from '{target.GetTemplate()?.GetID()}' to '{bestUnburned.GetTemplate()?.GetID()}' ({bestUnburnedHp} hp)");
                }
            }
            else if (bestBurning != null)
            {
                ApplyOverburn(bestBurning);
                Context.Log.Debug($"vector ssr: Overburn passed to already-Burning '{bestBurning.GetTemplate()?.GetID()}' ({bestBurningHp} hp)");
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"vector ssr: spread failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// Rides Dead End Meltdown in KDL (type="WOMENACE:OverburnOnHit"). Applies
// Overburn to each Burning victim it hits, but only when the skill is in its
// owning doll's hands: Overburn is Vector's imprint, not the weapon's. Also
// contributes the skill's burning-target bonus to the hover damage preview
// (the applied-hit boost lives in VectorSsrSystem's OnDamageReceived prefix,
// which the preview pipeline never routes through).
[JiangyuType("OverburnOnHit")]
public sealed partial class OverburnOnHit : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create()
        => new OverburnOnHitHandler();
}

[JiangyuType("OverburnOnHitHandler", Interfaces = new[] { typeof(IExpectedDamageContributor) })]
public sealed partial class OverburnOnHitHandler : SkillEventHandler
{
    // Fires per target entity per connecting hit. OnTargetHit dispatches
    // actor-wide, so only the carrying skill's own hits apply. No IsAlive
    // gate: a killing blow must still reach the ledger so a finisher on an
    // already-Burning enemy can spread. Overburn only lands on a Burning
    // target: the skill's ElementalDamage handler sits earlier in the
    // handler list, so on the very bullet that fills the gauge the Burn is
    // already queued and the queue-aware check below sees it.
    public override void OnTargetHit(Skill skill, Entity targetEntity, DamageInfo damageInfo)
    {
        try
        {
            var parent = ParentSkill;
            if (parent == null || skill == null || skill.Pointer != parent.Pointer)
                return;
            var victim = targetEntity?.TryCast<Actor>();
            if (victim == null || !VectorSsrSystem.IsBurning(victim))
                return;
            var attacker = GetActor();
            if (attacker == null || !Pierce.IsHostileTo(attacker, victim))
                return;
            if (!SsrImprintSystem.IsOwnerWielding(skill))
                return;
            VectorSsrSystem.ApplyOverburn(victim);
        }
        catch (Exception ex)
        {
            ElementsSystem.Warn($"vector ssr: Overburn apply failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Preview-side mirror of the 20% burning bonus, so the hover number
    // matches the applied hit against Burning targets.
    public void OnCalculateExpectedDamage(Tile origin, Tile aim, Tile target, Entity defender, int repetition, Skill.ExpectedDamage expected)
    {
        try
        {
            if (expected == null)
                return;
            var victim = defender?.TryCast<Actor>();
            if (victim == null || !VectorSsrSystem.IsBurning(victim))
                return;
            expected.AverageDamage *= VectorSsrSystem.BonusVsBurningMult;
        }
        catch (Exception ex)
        {
            ElementsSystem.Warn($"vector ssr: preview bonus failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// Rides effect.wmgfl_overburn itself (type="WOMENACE:OverburnTracker"): the
// per-tick OnUpdate keeps the bearer's death position fresh in the Overburn
// ledger, covering movement and DoT ticks that never pass the damage prefix.
[JiangyuType("OverburnTracker")]
public sealed partial class OverburnTracker : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create()
        => new OverburnTrackerHandler();
}

[JiangyuType("OverburnTrackerHandler")]
public sealed partial class OverburnTrackerHandler : SkillEventHandler
{
    public override void OnUpdate(EntityProperties properties)
    {
        var bearer = GetActor();
        VectorSsrSystem.RefreshOverburnTile(bearer);
        VectorSsrSystem.ExpireIfNotBurning(bearer);
    }
}
