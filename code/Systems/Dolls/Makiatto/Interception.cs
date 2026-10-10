using System.Collections;
using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tags;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Overwatch, Makiatto's reaction fire. The perk grants an Interception toggle per weapon she can
// intercept with: the squad weapon always, the squad leader's special weapon when one is equipped.
// Each toggle is a pair of skills, one to switch it on and one to switch it off, built like vanilla
// Take Aim: SwitchBetweenSkills shows one of the pair at a time, the on skill adds the slot's
// hidden marker effect and the off skill removes it. The marker is the toggle's state, so a slot is
// on exactly while she carries its marker. Markers go after combat, so every mission starts off.
// While a toggle is on, the AP she ends her turn with is held back, and every time an enemy
// finishes a move within that weapon's range and her sight, she fires at it, paying each shot's
// discounted AP cost out of the reserve. The reserve lapses at her next turn start. With both toggles on,
// the special weapon fires first and the squad weapon follows while AP remains.
//
// Reaction shots only fire at a decent hit chance, so she does not spend the reserve on long
// shots, and cost less AP than the weapon's own shot. A shot is fired the way the game's own overwatch fires
// (OverwatchHandler.OnMovementFinished, RVA 0x7A4D00): inside the movement-finished event, so it
// lands before the enemy acts on its new tile, with WasAutoTriggered set and Skill.Use(tile, Free).
// The game drops a reacting actor to 0 AP during the enemy turn, so the reserve is kept here.

internal static class InterceptionSlots
{
    internal const string Leader = "leader";
    internal const string Squad = "squad";
    // reaction order: the special weapon first, then the squad weapon with what AP remains
    internal static readonly string[] All = { Leader, Squad };

    internal static ItemSlot ItemSlotOf(string slot) => slot == Leader ? ItemSlot.InfantrySpecial : ItemSlot.InfantryWeapon;

    // The attack skill the weapon in the slot grants: a targeted, direct-fire attack. Indirect fire
    // cannot react to a target it has to arc onto. Found through the equipped item rather than a
    // scan of the actor's skills, which reads the slot directly.
    internal static Skill WeaponSkill(Actor actor, string slot)
    {
        var item = actor?.TryCast<UnitActor>()?.GetLeader()?.GetItems()?.GetItemAtSlot(ItemSlotOf(slot));
        if (item == null || item.GetTemplate()?.HasTag(TagType.INDIRECT_FIRE, false) == true)
            return null;
        var skills = item.GetSkills();
        for (var i = 0; skills != null && i < skills.Count; i++)
        {
            var skill = skills[i]?.TryCast<Skill>();
            if (skill != null && !skill.IsGarbage() && skill.IsAttack() && skill.IsActive() && skill.IsTargeted()
                && !skill.IsMovementSkill() && skill.GetMaxRange() > 0)
                return skill;
        }
        return null;
    }

    internal static string LeaderId(Actor actor) => actor?.TryCast<UnitActor>()?.GetLeader()?.LeaderTemplate?.GetID();
}

[JiangyuType("Interception")]
public sealed partial class Interception : SkillEventHandlerTemplate
{
    public SkillTemplate SquadToggle;
    public SkillTemplate SquadStop;
    public SkillTemplate LeaderToggle;
    public SkillTemplate LeaderStop;
    public SkillTemplate SquadMarker;
    public SkillTemplate LeaderMarker;
    // least hit chance, in percent, a reaction shot needs
    public int MinHitChance = 60;
    // share of a weapon's AP cost a reaction shot is spared, 0 to 1
    public float ApDiscount = 0.35f;

    public override SkillEventHandler Create() => new InterceptionHandler
    {
        SquadToggle = SquadToggle,
        SquadStop = SquadStop,
        LeaderToggle = LeaderToggle,
        LeaderStop = LeaderStop,
        SquadMarker = SquadMarker,
        LeaderMarker = LeaderMarker,
        MinHitChance = MinHitChance,
        ApDiscount = ApDiscount,
    };
}

[JiangyuType("InterceptionHandler")]
public sealed partial class InterceptionHandler : SkillEventHandler
{
    public SkillTemplate SquadToggle;
    public SkillTemplate SquadStop;
    public SkillTemplate LeaderToggle;
    public SkillTemplate LeaderStop;
    public SkillTemplate SquadMarker;
    public SkillTemplate LeaderMarker;
    public int MinHitChance;
    public float ApDiscount;

    private bool _registered;
    private bool _granted;
    private Actor _actor;
    // the slots whose toggles this mission granted
    internal bool HasSquad;
    internal bool HasLeader;
    // whether a reaction fired since her last turn start, so her turn start lowers the weapons
    internal bool Fired;
    internal int Reserve;
    // Her AP as last seen during her own turn, from her turn-start AP on. The game zeroes a unit's
    // AP before its turn-end events run, so the reserve is banked from this rather than read then.
    internal int LastAp;

    internal SkillTemplate MarkerFor(string slot) => slot == InterceptionSlots.Leader ? LeaderMarker : SquadMarker;

    public override void OnMissionFinished()
    {
        _registered = false;
        _granted = false;
        HasSquad = HasLeader = false;
        Fired = false;
        Reserve = 0;
        LastAp = 0;
        // GetActor can be null around mission boundaries, so the actor registered is the one removed
        InterceptionSystem.Unregister(_actor);
        _actor = null;
    }

    // GetActor can be null at OnMissionStarted, so she is registered on the first update with a
    // live actor, which is what lets the AP sampling see her turn.
    public override void OnUpdate(EntityProperties _properties)
    {
        if (_registered)
            return;
        var actor = GetActor();
        if (actor == null || !actor.IsAlive())
            return;
        _registered = true;
        _actor = actor;
        InterceptionSystem.Register(actor, this);
    }

    // The toggles wait for her first turn: her loadout's skills are not all in place on the first
    // update, and equipment cannot change mid-mission, so they are decided once.
    private void GrantOnce(Actor actor)
    {
        if (_granted)
            return;
        _granted = true;
        try
        {
            var leaderWeapon = InterceptionSlots.WeaponSkill(actor, InterceptionSlots.Leader);
            var squadWeapon = InterceptionSlots.WeaponSkill(actor, InterceptionSlots.Squad);
            Log.Debug($"interception: '{InterceptionSlots.LeaderId(actor)}' SL weapon={leaderWeapon?.GetID()}, squad weapon={squadWeapon?.GetID()}");
            HasSquad = squadWeapon != null;
            HasLeader = leaderWeapon != null;
            if (HasSquad)
                Grant(actor, SquadToggle, SquadStop);
            if (HasLeader)
                Grant(actor, LeaderToggle, LeaderStop);
        }
        catch (Exception ex)
        {
            Log.Warn($"interception: setup failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Grant(Actor actor, SkillTemplate on, SkillTemplate off)
    {
        var skills = actor.GetSkills();
        if (on != null && SkillEffects.FindInstance(skills, on) == null)
            SkillEffects.TryAddEffect(actor, on, message => Log.Warn($"interception: {message}"));
        if (off != null && SkillEffects.FindInstance(skills, off) == null)
            SkillEffects.TryAddEffect(actor, off, message => Log.Warn($"interception: {message}"));
    }

    // What she has left when her turn ends is what she intercepts with until her next one.
    public override void OnTurnEnd()
    {
        var actor = GetActor();
        if (actor == null)
            return;
        Reserve = InterceptionSystem.AnyOn(actor, this) ? LastAp : 0;
        Log.Debug($"interception: turn end, on={InterceptionSystem.AnyOn(actor, this)} last AP={LastAp} reserve={Reserve}");
    }

    public override void OnTurnStart()
    {
        Reserve = 0;
        var actor = GetActor();
        if (actor != null)
        {
            // The sampling only runs while she is the active unit, so a turn she never acts in
            // would otherwise bank the last turn's leftover.
            LastAp = actor.GetActionPointsAtTurnStart();
            GrantOnce(actor);
            // weapons raised by last round's reactions come down for her own turn. The game clears
            // WasAutoTriggered when each shot resolves (Skill.ClearBusy, RVA 0x72AC40), so the reset
            // below is only a backstop.
            InterceptionSystem.ClearReactions(actor);
            if (Fired)
            {
                Fired = false;
                actor.SetAiming(false, null);
                foreach (var slot in InterceptionSlots.All)
                {
                    var weapon = InterceptionSlots.WeaponSkill(actor, slot);
                    if (weapon != null)
                        weapon.WasAutoTriggered = false;
                }
            }
        }
    }
}

public sealed class InterceptionSystem : JiangyuSystem
{
    // A second weapon's shot waits for the first to resolve: two attacks cannot run at once.
    private const float FireDelaySeconds = 0.1f;

    private static InterceptionSystem _instance;
    private readonly Dictionary<IntPtr, (Actor Actor, InterceptionHandler Handler)> _watchers = new();
    // per-weapon bonuses from other perks (InterceptionWeaponBonus), by shooter
    private readonly Dictionary<IntPtr, InterceptionWeaponBonusHandler> _bonuses = new();
    // Weapon skills with an Interception shot still resolving, counted per skill. Vanilla's own
    // counter and auto-attack handlers set WasAutoTriggered too, so a reaction is a shot whose skill
    // is in here and still auto-triggered. A shot's mark is released once the game is no longer
    // done with it (ReleaseReaction), not when Skill.Use returns, because a burst's later
    // repetitions resolve after Use returns. A count rather than a flag, so a queued second shot of
    // the same skill keeps its mark when the first one's release lands.
    private readonly Dictionary<IntPtr, int> _reactionSkills = new();
    private readonly Queue<Reaction> _queue = new();
    private bool _firing;
    private bool _sampling;
    // Inside the movement-finished event the mover may still report the tile it left, so the
    // still-standing check is for queued shots only.
    private bool _inEvent;

    private sealed class Reaction
    {
        public Actor Shooter;
        public InterceptionHandler Handler;
        public Actor Target;
        public Tile Tile;
        public string Slot;
    }

    public override void OnInit()
    {
        _instance = this;
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnMovementFinished", OnMovementFinished);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Actor", "SetTransporterInactive", OnTransporterInactive);
    }

    // A reaction fires inside a transport's own action, so it can kill the passengers while the
    // transport is still the AI's active unit. Their death switches the transport off
    // (Actor.SetTransporterInactive, RVA 0x611A80) and nulls its m_Agent, and AIFaction.Process
    // (RVA 0x798C40) reads the active unit's agent before anything else every frame, so the enemy
    // turn throws on that null forever. Clearing the faction's active unit takes Process down its
    // own no-active-unit path, which finishes the unit and picks the next one.
    private void OnTransporterInactive(PatchInfo info)
    {
        try
        {
            if (info.Instance is not Actor transport || transport == null)
                return;
            var factions = TacticalManager.Get()?.GetFactions();
            for (var i = 0; factions != null && i < factions.Length; i++)
            {
                var faction = factions[i];
                if (faction?.m_ActiveActor?.Pointer != transport.Pointer)
                    continue;
                faction.m_ActiveActor = null;
                Context.Log.Debug($"interception: released switched-off transport '{transport.GetTemplate()?.GetID()}' as its faction's active unit");
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"interception: could not release a switched-off transport: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static void Register(Actor actor, InterceptionHandler handler)
    {
        if (_instance == null || actor == null)
            return;
        _instance._watchers[actor.Pointer] = (actor, handler);
        if (!_instance._sampling)
            _instance.Context.Coroutines.Start(_instance.SampleAp());
    }

    // Each frame of a watcher's own turn, her current AP, so her turn end banks what she had left.
    private IEnumerator SampleAp()
    {
        _sampling = true;
        try
        {
            while (_watchers.Count > 0)
            {
                var active = TacticalManager.Get()?.GetActiveActor();
                if (active != null && _watchers.TryGetValue(active.Pointer, out var watcher))
                    watcher.Handler.LastAp = active.GetActionPoints();
                yield return null;
            }
        }
        finally
        {
            _sampling = false;
        }
    }

    internal static void RegisterBonus(Actor actor, InterceptionWeaponBonusHandler bonus)
    {
        if (_instance != null && actor != null && bonus != null)
            _instance._bonuses[actor.Pointer] = bonus;
    }

    internal static void UnregisterBonus(Actor actor)
    {
        if (_instance != null && actor != null)
            _instance._bonuses.Remove(actor.Pointer);
    }

    // Whether the skill is resolving a shot Interception fired.
    internal static bool IsReaction(Skill skill)
        => _instance != null && skill != null && skill.WasAutoTriggered && _instance._reactionSkills.ContainsKey(skill.Pointer);

    internal static void ClearReactions(Actor actor)
    {
        if (_instance == null || actor == null)
            return;
        foreach (var slot in InterceptionSlots.All)
        {
            var weapon = InterceptionSlots.WeaponSkill(actor, slot);
            if (weapon != null)
                _instance._reactionSkills.Remove(weapon.Pointer);
        }
    }

    internal static void Unregister(Actor actor)
    {
        if (_instance != null && actor != null)
            _instance._watchers.Remove(actor.Pointer);
    }

    internal static bool AnyOn(Actor actor, InterceptionHandler handler)
    {
        if (_instance == null || handler == null)
            return false;
        foreach (var slot in InterceptionSlots.All)
            if (_instance.IsOn(actor, handler, slot))
                return true;
        return false;
    }

    // A slot counts only when this mission granted its toggle and she carries its marker.
    private bool IsOn(Actor actor, InterceptionHandler handler, string slot)
        => (slot == InterceptionSlots.Leader ? handler.HasLeader : handler.HasSquad)
            && SkillEffects.FindInstance(actor.GetSkills(), handler.MarkerFor(slot)) != null;

    private void OnMovementFinished(PatchInfo info)
    {
        try
        {
            if (info.Args[0] is not Actor mover || mover == null || !mover.IsAlive() || _watchers.Count == 0)
                return;
            // The event's destination, as the game's own overwatch reads it: the mover may not
            // stand on its new tile yet when the event fires.
            var tile = info.Args[1] as Tile ?? mover.GetTile();
            if (tile == null)
                return;
            _inEvent = true;
            // a shot fired below can run code that registers or unregisters a watcher
            foreach (var (shooter, handler) in _watchers.Values.ToArray())
            {
                if (shooter == null || !shooter.IsAlive() || !Pierce.IsHostileTo(shooter, mover))
                    continue;
                Context.Log.Debug($"interception: '{mover.GetTemplate()?.GetID()}' finished a move, reserve={handler.Reserve}, busy={TacticalManager.IsSkillBusy()}");
                if (handler.Reserve <= 0)
                    continue;
                // the first shot fires now, inside the event, and a second waits for it to resolve
                var fired = false;
                foreach (var slot in InterceptionSlots.All)
                {
                    if (!IsOn(shooter, handler, slot))
                        continue;
                    var reaction = new Reaction { Shooter = shooter, Handler = handler, Target = mover, Tile = tile, Slot = slot };
                    // Two attacks cannot run at once, so while an earlier reaction is still queued
                    // or any shot is resolving this one waits its turn, as vanilla's own reaction
                    // fire does (AutoAttackOnFleeingHandler.TryTargetActor). A shot that destroys
                    // a transport ejects its passengers, and their landing raises this event
                    // mid-shot.
                    if (!fired && !_firing && _queue.Count == 0 && !TacticalManager.IsSkillBusy())
                        fired = Fire(reaction);
                    else
                        _queue.Enqueue(reaction);
                }
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"interception: movement trigger failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _inEvent = false;
            if (_queue.Count > 0 && !_firing)
                Context.Coroutines.Start(Drain());
        }
    }

    // Lets go of a fired shot's reaction mark once the shot, every repetition included, has
    // resolved: Skill.ClearBusy (RVA 0x72AC40) clears WasAutoTriggered at exactly that point. The
    // turn-start ClearReactions and the scene-load clear back this up.
    private IEnumerator ReleaseReaction(Skill skill)
    {
        var pointer = skill.Pointer;
        yield return null;
        while (StillResolving(skill))
            yield return null;
        ReleaseMark(pointer);
    }

    // a skill that went away with its scene counts as resolved
    private static bool StillResolving(Skill skill)
    {
        try
        {
            return skill.WasAutoTriggered;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ReleaseMark(IntPtr skill)
    {
        if (!_reactionSkills.TryGetValue(skill, out var count))
            return;
        if (count <= 1)
            _reactionSkills.Remove(skill);
        else
            _reactionSkills[skill] = count - 1;
    }

    private IEnumerator Drain()
    {
        _firing = true;
        try
        {
            while (_queue.Count > 0)
            {
                var start = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - start < FireDelaySeconds || TacticalManager.IsSkillBusy())
                    yield return null;
                // a scene change clears the queue while this waits
                if (!_queue.TryDequeue(out var reaction))
                    break;
                try
                {
                    Fire(reaction);
                }
                catch (Exception ex)
                {
                    Context.Log.Warn($"interception: reaction failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        finally
        {
            _firing = false;
        }
    }

    // Revalidated at fire time: the target may have died to the shot before, and the reserve may
    // no longer cover this weapon.
    // Whether the shot was taken.
    private bool Fire(Reaction r)
    {
        bool Held(string reason)
        {
            Context.Log.Debug($"interception: {r.Slot} held at '{r.Target?.GetTemplate()?.GetID()}', {reason}");
            return false;
        }
        var shooter = r.Shooter;
        if (shooter == null || !shooter.IsAlive() || r.Target == null || !r.Target.IsAlive())
            return Held("shooter or target gone");
        if (shooter.IsStunned())
            return Held("stunned");
        // A transport whose passengers die inside is switched off rather than killed
        // (Actor.SetTransporterInactive, RVA 0x611A80: Neutral faction, AI agent disposed), and a
        // shot at it never lets the enemy turn finish.
        if (!Pierce.IsHostileTo(shooter, r.Target))
            return Held("target no longer hostile");
        // a queued second shot fires only while the target still stands where it stopped
        if (r.Target.GetTile()?.Pointer != r.Tile.Pointer && r.Target.GetTile() != null && !_inEvent)
            return Held("target moved on");
        var skill = InterceptionSlots.WeaponSkill(shooter, r.Slot);
        var from = shooter.GetTile();
        if (skill == null || from == null)
            return Held("no weapon skill or tile");
        // rounded up and capped, so no discount turns a shot free
        var discount = r.Handler.ApDiscount;
        if (_bonuses.TryGetValue(shooter.Pointer, out var bonus) && bonus.Matches(skill))
            discount += bonus.ApDiscount;
        discount = Math.Clamp(discount, 0f, 0.9f);
        var cost = Mathf.CeilToInt(skill.GetActionPointCost() * (1f - discount));
        if (cost <= 0 || cost > r.Handler.Reserve)
            return Held($"shot costs {cost} AP, reserve {r.Handler.Reserve}");
        if (skill.HasLimitedUses() && (skill.IsOutOfUses() || skill.GetUses() <= 0))
            return Held("out of ammo");
        // What the player cannot see cannot be targeted on her turn either. The targeting test
        // below checks line of fire from her tile, not the fog. The mover's own visibility is only
        // refreshed after the movement-finished event, so the tile it stops on counts too: tile
        // visibility comes from the player's units, which stand still while an enemy moves.
        if (r.Target.VisibilityToPlayer != Visibility.Visible && !r.Tile.IsVisibleToFaction(shooter.GetFactionID()))
            return Held("not visible to the player");
        // The game's own targeting test, the one that decides what she can shoot on her turn:
        // range, line of fire and whether the target can be attacked at all.
        if (!skill.IsUsableOn(r.Tile, from))
            return Held($"not targetable from her tile ({Pierce.Distance(from, r.Tile)} tiles, max {skill.GetMaxRange()})");
        var chance = HitChance(shooter, skill, r.Target, r.Tile);
        if (chance < r.Handler.MinHitChance)
        {
            return Held($"{chance}% under {r.Handler.MinHitChance}%");
        }

        var usesBefore = skill.GetUses();
        Aim(shooter, skill, r.Tile);
        skill.WasAutoTriggered = true;
        _reactionSkills[skill.Pointer] = _reactionSkills.GetValueOrDefault(skill.Pointer) + 1;
        var usage = "Free";
        bool used;
        try
        {
            used = skill.Use(r.Tile, UsageParameter.Free);
            if (!used)
            {
                // the off-turn 0 AP can fail the usability check the game's own overwatch passes
                usage = "Free|IgnoreUsabilityCheck";
                used = skill.Use(r.Tile, UsageParameter.Free | UsageParameter.IgnoreUsabilityCheck);
            }
        }
        catch (Exception)
        {
            // The enemy turn waits for every busy skill to clear, with no timeout, and a shot
            // that throws mid-resolution never clears its own.
            skill.ClearBusy(UsageParameter.Default);
            skill.WasAutoTriggered = false;
            ReleaseMark(skill.Pointer);
            throw;
        }
        if (!used)
        {
            // nothing fired, so the aim and the reaction mark come off again
            skill.WasAutoTriggered = false;
            ReleaseMark(skill.Pointer);
            shooter.SetAiming(false, null);
            return Held("refused by Skill.Use");
        }
        r.Handler.Reserve -= cost;
        r.Handler.Fired = true;
        Context.Coroutines.Start(ReleaseReaction(skill));
        // Skill.Use spends a shot's uses inline before it returns, so an unchanged count here
        // means this Free use skipped the spend and the shot is paid for by hand: its cost, then
        // the other skills sharing the weapon's pool brought into line.
        if (skill.HasLimitedUses() && skill.GetUses() == usesBefore)
        {
            skill.SetUses(Math.Max(0, usesBefore - skill.GetUsesConsumed()));
            skill.SynchronizeUses();
            shooter.GetSkills().ScheduleUpdate();
        }
        Context.Log.Debug($"interception: {r.Slot} fired at {chance}% via {usage}, reserve {r.Handler.Reserve} AP");
        return true;
    }

    // On her own turn the targeting UI raises the squad's weapons before a shot. Off-turn nothing
    // does, so the reaction raises them itself, every element at the target. They stay raised
    // through the enemy round and come down at her turn start (InterceptionHandler.OnTurnStart).
    private void Aim(Actor shooter, Skill skill, Tile tile)
    {
        try
        {
            var elements = new Il2CppSystem.Collections.Generic.List<int>();
            var count = shooter.GetElements()?.Count ?? 0;
            for (var i = 0; i < count; i++)
                elements.Add(i);
            shooter.SetAiming(true, skill);
            shooter.AimAt(tile, AimParams.Default, elements, skill);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"interception: aim failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The game's own hit chance from the current tile with current properties, so every live
    // modifier is already in it.
    private static int HitChance(Actor shooter, Skill skill, Actor target, Tile tile)
    {
        var shooterProperties = CurrentProperties(shooter);
        var targetProperties = CurrentProperties(target);
        if (shooterProperties == null || targetProperties == null)
            return -1;
        var hit = skill.GetHitchance(shooter.GetTile(), tile, shooterProperties, targetProperties, true, target, true);
        var value = hit.FinalValue;
        return float.IsNaN(value) || float.IsInfinity(value) ? -1 : Mathf.Clamp(Mathf.RoundToInt(value), 0, 100);
    }

    // Squads and vehicles are UnitActors, several enemy kinds are TransientActors.
    private static EntityProperties CurrentProperties(Actor actor)
        => actor.TryCast<UnitActor>()?.GetCurrentProperties() ?? actor.TryCast<TransientActor>()?.GetCurrentProperties();
}
