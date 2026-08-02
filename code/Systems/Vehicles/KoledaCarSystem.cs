using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Doors;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Il2CppMenace.Tactical.Skills.SkillFilters;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Runtime behaviour for The Sinner, Koleda's supercar transport.
//
// Boarding from any side: container entry paths through an element's door
// component, and vehicles get a VehicleDoorComp whose convention is a rear
// entrance arc. That arc is why the car could only be boarded from behind
// and refused entirely with its tail against a wall. The door comp's own
// methods are VIRTUAL (detouring them crashes on first native invocation),
// so the hook rides the element's non-virtual RecalculateEntranceTiles
// wrapper, which the game already calls at spawn, and swaps the comp's
// tile list.
//
// Doors and facing ride the salvo skill's own lifecycle. The vehicle
// driver's aiming parameters are write-only noise for an entity-granted
// skill (OnAimChanged never fires for the car and the aim state never
// clears), so the controller's doors layer listens to a single mod-owned
// DoorsOut bool instead: set when the salvo starts, cleared when its use
// completes (the controller's linger state delays the actual close).
//
// Weapon parity: the salvo is entity-granted, so no item backs it, and
// every vanilla mechanic that identifies vehicle weapon skills through
// their granting item rejects it. Drive By's AP discount and effect
// consumption gate on an ItemSlotFilter of the vehicle weapon slots, the
// dropship supply drop refills through an IsItemSkillFilter, and Vehicle
// Ammo Cases' AmmoPouch requires the granting item's type to be Weapon.
// (The scavenger drop works untouched: its filter matches tags, and the
// salvo inherits VEHICLE_WEAPON from the minigun it was cloned from.)
// The filter patches make the salvo count as a ModularVehicleLight weapon
// skill, and the pouch patch mirrors the bonus its gate cannot reach.
public sealed class KoledaCarSystem : JiangyuSystem
{
    private const string CarId = "player_vehicle.koleda_car";
    private const string SalvoId = "active.sinner_mg";
    private const string AmmoCasesPassiveId = "passive.ammo_case";
    private const string DoorsParam = "DoorsOut";

    // Template identity caches. These hooks run for every skill filter check
    // and every entrance recalculation in the game, and GetID marshals a fresh
    // managed string per call, so the templates are resolved by id ONCE and
    // matched by pointer afterwards. Il2cpp's GC does not move objects, so a
    // template's pointer is a stable key for as long as the templates live,
    // and OnTemplatesApplied drops the cache whenever they are rebuilt.
    private static System.IntPtr _carTemplate;
    private static System.IntPtr _salvoTemplate;
    private static bool _carResolved;
    private static bool _salvoResolved;

    // Live coroutines keyed by the entity (or actor) they serve, so a second
    // car's spawn or salvo never cancels the first car's. Cleared wholesale
    // on scene change, which also bounds the pointer keys' lifetime.
    private readonly Dictionary<System.IntPtr, object> _watchers = new();
    private readonly Dictionary<System.IntPtr, object> _settles = new();

    // Salvo skill instances already granted the Ammo Cases bonus, so the
    // mission-start sweep and the skill-added hook never stack it. Pointer
    // keys share the coroutine dictionaries' scene-bounded lifetime.
    private readonly HashSet<System.IntPtr> _boostedSalvos = new();

    // The salvo's multi-tile crash protection lives in
    // [[SelectedTilesGuardSystem]], which covers every skill that picks its
    // own tiles rather than this one alone.
    //
    // The dropship supply drop's refill is the only vanilla consumer of
    // IsItemSkillFilter, so its override is scoped to the refill call itself
    // rather than answering for every consumer of a general predicate.
    private bool _inRefill;

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Tactical.Element", "RecalculateEntranceTiles", 0, OnEntrancesRecalculated);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "OnUse", 3, OnSalvoUse);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "OnAfterUse", 1, OnSalvoAfterUse);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.SkillFilters.ItemSlotFilter", "Matches", OnItemSlotFilterMatches);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.SkillFilters.IsItemSkillFilter", "Matches", OnItemSkillFilterMatches);
        Context.Patches.Prefix("Il2CppMenace.Tactical.Actor", "RefillAmmo", 3, OnRefillStart);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Actor", "RefillAmmo", 3, OnRefillEnd);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Effects.AmmoPouchHandler", "OnMissionStarted", OnAmmoPouchMissionStarted);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Effects.AmmoPouchHandler", "OnAnySkillAdded", OnAmmoPouchSkillAdded);
    }

    // A template rebuild invalidates every cached pointer, so they are
    // re-resolved on the next lookup rather than left pointing at freed
    // templates, which would silently stop matching the car and its salvo.
    public override void OnTemplatesApplied()
    {
        _carResolved = false;
        _salvoResolved = false;
        _carTemplate = System.IntPtr.Zero;
        _salvoTemplate = System.IntPtr.Zero;
    }

    // Missions never hand a live car across a scene change, so every watcher
    // and settle belongs to the scene that started it. Stopping them here
    // keeps a car that survives the mission from polling into the strategy
    // layer and pinning the dead mission's wrapper graph.
    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        ResetSceneState();
    }

    public override void OnUnload()
    {
        ResetSceneState();
    }

    private void ResetSceneState()
    {
        foreach (var handle in _watchers.Values)
            Context.Coroutines.Stop(handle);
        _watchers.Clear();
        foreach (var handle in _settles.Values)
            Context.Coroutines.Stop(handle);
        _settles.Clear();
        _boostedSalvos.Clear();
        _inRefill = false;
    }

    private static bool IsTheCar(Entity entity)
    {
        var template = entity?.GetTemplate();
        if (template == null)
            return false;
        if (!_carResolved)
        {
            _carTemplate = Templates.ById<EntityTemplate>(CarId)?.Pointer ?? System.IntPtr.Zero;
            _carResolved = true;
        }
        return _carTemplate != System.IntPtr.Zero && template.Pointer == _carTemplate;
    }

    private static bool IsTheSalvo(PatchInfo info, out Skill skill)
    {
        skill = (info.Instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<Skill>();
        return IsSalvoSkill(skill);
    }

    private static bool IsSalvoSkill(Skill skill)
    {
        var template = skill?.GetTemplate();
        if (template == null)
            return false;
        if (!_salvoResolved)
        {
            _salvoTemplate = Templates.ById<SkillTemplate>(SalvoId)?.Pointer ?? System.IntPtr.Zero;
            _salvoResolved = true;
        }
        return _salvoTemplate != System.IntPtr.Zero && template.Pointer == _salvoTemplate;
    }

    private static ElementAnimator CarAnimator(Actor actor)
        => IsTheCar(actor) ? actor?.GetFirstElementOrNull()?.m_ElementAnimator : null;

    private void OnEntrancesRecalculated(PatchInfo info)
    {
        try
        {
            var element = (info.Instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<Element>();
            // cheap gate first: most elements have no door comp at all
            var comp = element?.m_DoorComp?.TryCast<VehicleDoorComp>();
            if (comp == null)
                return;
            var entity = element.GetEntity();
            if (!IsTheCar(entity))
                return;

            RefreshEntrances(comp, entity);

            // The game only recalculates entrances at spawn, so the swapped
            // ring goes stale on the spawn tile's neighbourhood once the car
            // drives off (boarding then only works where the old and new
            // neighbourhoods overlap, which reads as "back only"). The
            // entrance readers are base-class virtuals (never detoured), so a
            // watcher re-swaps the ring whenever this car's tile changes.
            StopKeyed(_watchers, entity.Pointer);
            _watchers[entity.Pointer] = Context.Coroutines.Start(WatchCarTile(comp, entity));
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"koleda car: entrances postfix failed: {ex.Message}");
        }
    }

    // Swap the door comp's entrance list for the 8 tiles around the entity's current tile.
    private void RefreshEntrances(VehicleDoorComp comp, Entity entity)
    {
        try
        {
            var tile = entity?.GetTile();
            if (comp == null || tile == null)
                return;

            var entrances = new Il2CppSystem.Collections.Generic.List<Tile>();
            for (var dir = 0; dir < 8; dir++)
            {
                var neighbour = tile.GetNextTile((Direction)dir);
                if (neighbour != null)
                    entrances.Add(neighbour);
            }
            if (entrances.Count == 0)
                return;

            comp.m_EntranceTiles = entrances;
            Context.Log.Debug($"koleda car: {entrances.Count} adjacent entrance tile(s) set");
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"koleda car: entrance refresh failed: {ex.Message}");
        }
    }

    // Re-swap the entrance ring whenever the car changes tile. Polls rather
    // than patching: the per-move recalc paths live on base-class virtuals,
    // and half a second of lag on a boarding target is invisible next to the
    // drive that caused it. Exits when the car dies, and the scene teardown
    // stops it when the mission ends first.
    private System.Collections.IEnumerator WatchCarTile(VehicleDoorComp comp, Entity entity)
    {
        var wait = new WaitForSeconds(0.5f);
        var lastTile = entity?.GetTile()?.Pointer ?? System.IntPtr.Zero;
        while (true)
        {
            yield return wait;
            Tile tile;
            try
            {
                if (comp == null || entity == null || !entity.IsAlive())
                    yield break;
                tile = entity.GetTile();
            }
            catch
            {
                yield break;
            }
            if (tile == null || tile.Pointer == lastTile)
                continue;
            lastTile = tile.Pointer;
            RefreshEntrances(comp, entity);
        }
    }

    private void StopKeyed(Dictionary<System.IntPtr, object> handles, System.IntPtr key)
    {
        if (!handles.TryGetValue(key, out var handle))
            return;
        Context.Coroutines.Stop(handle);
        handles.Remove(key);
    }

    // Doors out at the start of the use. DelayAfterAnimationTrigger in the KDL
    // holds both the first shot and its sound until the 1.53s door swing
    // lands, so the doors are clear before the guns speak.
    private void OnSalvoUse(PatchInfo info)
    {
        try
        {
            // OnUse returns bool: a false result means the use was refused, so
            // nothing fires and the doors have no reason to open
            if (info.Result is false)
                return;
            if (!IsTheSalvo(info, out _))
                return;
            OpenDoors((info.Args is { Count: > 0 } ? info.Args[0] : null) as Actor);
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"koleda car: salvo-use postfix failed: {ex.Message}");
        }
    }

    private void OpenDoors(Actor actor)
    {
        var animator = CarAnimator(actor);
        if (animator?.m_Animator == null)
            return;
        // a fresh salvo takes over the model rotation: stop any settle
        // still easing this car back from the previous one
        StopKeyed(_settles, actor.Pointer);
        animator.m_Animator.SetBool(DoorsParam, true);
        Context.Log.Debug("koleda car: doors out");
    }

    private void OnSalvoAfterUse(PatchInfo info)
    {
        try
        {
            if (!IsTheSalvo(info, out _))
                return;
            Context.Log.Debug("koleda car: doors in");
            var actor = (info.Args is { Count: > 0 } ? info.Args[0] : null) as Actor;
            var animator = CarAnimator(actor);
            if (animator == null)
                return;
            if (animator.m_Animator != null)
                animator.m_Animator.SetBool(DoorsParam, false);
            StopKeyed(_settles, actor.Pointer);
            _settles[actor.Pointer] = Context.Coroutines.Start(SettleFacing(actor));
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"koleda car: salvo-after postfix failed: {ex.Message}");
        }
    }

    // Aiming rotates the car's transform at the target and abandons it there:
    // the logical direction never changes, and the pathfinder plans the next
    // move from that logical facing (hence the wrong-way turn and reverse
    // drive while the model points elsewhere). No stored state remembers the
    // pre-aim rotation (the aim comp re-captures its "start" every
    // repetition), but the logical facing's world yaw is pure tile geometry:
    // the vector to the tile directly ahead in the logical direction. Settle
    // swings the model back onto it after the burst, bailing out if the car
    // dies mid-wait, starts moving, or turns to a new logical facing (those
    // writers own the transform then).
    private System.Collections.IEnumerator SettleFacing(Actor actor)
    {
        Context.Log.Debug("koleda car: settle scheduled");
        yield return new WaitForSeconds(2.5f);
        Context.Log.Debug("koleda car: settle waking");
        var element = actor?.GetFirstElementOrNull();
        if (element == null || element.transform == null)
            yield break;
        var tile = actor.GetTile();
        var direction = actor.GetDirection();
        var aheadTile = tile?.GetNextTile(direction);
        if (aheadTile == null)
            yield break;

        var forward = element.GetTargetPosOnTile(aheadTile, 0) - element.GetTargetPosOnTile(tile, 0);
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f)
            yield break;
        var current = element.transform.eulerAngles;
        var yaw = Quaternion.LookRotation(forward.normalized, Vector3.up).eulerAngles.y;
        var target = Quaternion.Euler(current.x, yaw, current.z);
        Context.Log.Debug($"koleda car settle: dir={direction} root={current.y:F1} -> {yaw:F1}");

        var start = element.transform.rotation;
        for (var t = 0f; t < 1f; t += Time.deltaTime / 0.6f)
        {
            if (element == null || element.transform == null
                || actor.GetTile()?.Pointer != tile.Pointer
                || actor.GetDirection() != direction)
                yield break;
            element.transform.rotation = Quaternion.Slerp(start, target, t);
            yield return null;
        }
        if (element != null && element.transform != null
            && actor.GetTile()?.Pointer == tile.Pointer && actor.GetDirection() == direction)
            element.transform.rotation = target;
    }

    // The salvo passes an ItemSlotFilter whenever the filter targets the
    // ModularVehicleLight slot, exactly as if the minigun it was cloned
    // from still backed it. Filters aimed at other slots (infantry ammo
    // bags, heavy turret perks) stay rejected.
    private void OnItemSlotFilterMatches(PatchInfo info)
    {
        try
        {
            if (info.Result is true)
                return;
            var skill = (info.Args is { Count: > 0 } ? info.Args[0] : null) as Skill;
            if (!IsSalvoSkill(skill))
                return;
            var slots = (info.Instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)
                ?.TryCast<ItemSlotFilter>()?.ItemSlots;
            if (slots == null)
                return;
            foreach (var slot in slots)
            {
                if (slot != ItemSlot.ModularVehicleLight)
                    continue;
                info.Result = true;
                return;
            }
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"koleda car: item-slot filter postfix failed: {ex.Message}");
        }
    }

    private void OnRefillStart(PatchInfo info) => _inRefill = true;

    private void OnRefillEnd(PatchInfo info) => _inRefill = false;

    // IsItemSkillFilter is a general "is an item behind this skill" predicate,
    // so answering yes everywhere would reach consumers that have nothing to
    // do with resupply. The dropship supply drop is its only vanilla user and
    // it asks from inside Actor.RefillAmmo, so the override is confined to
    // that call.
    private void OnItemSkillFilterMatches(PatchInfo info)
    {
        try
        {
            if (!_inRefill || info.Result is true)
                return;
            var skill = (info.Args is { Count: > 0 } ? info.Args[0] : null) as Skill;
            if (IsSalvoSkill(skill))
                info.Result = true;
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"koleda car: item-skill filter postfix failed: {ex.Message}");
        }
    }

    private static bool IsAmmoCases(AmmoPouchHandler handler)
        => handler?.ParentSkill?.GetTemplate()?.GetID() == AmmoCasesPassiveId;

    private void OnAmmoPouchMissionStarted(PatchInfo info)
    {
        try
        {
            var handler = (info.Instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<AmmoPouchHandler>();
            if (!IsAmmoCases(handler))
                return;
            var entity = handler.GetEntity();
            if (!IsTheCar(entity))
                return;
            var skills = entity.GetSkills()?.GetAllSkills();
            if (skills == null)
                return;
            foreach (var candidate in skills)
            {
                var skill = candidate?.TryCast<Skill>();
                if (!IsSalvoSkill(skill))
                    continue;
                BoostSalvoUses(skill, handler);
                return;
            }
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"koleda car: ammo pouch mission-start postfix failed: {ex.Message}");
        }
    }

    private void OnAmmoPouchSkillAdded(PatchInfo info)
    {
        try
        {
            var skill = (info.Args is { Count: > 0 } ? info.Args[0] : null) as Skill;
            if (!IsSalvoSkill(skill))
                return;
            var handler = (info.Instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<AmmoPouchHandler>();
            if (IsAmmoCases(handler) && IsTheCar(handler.GetEntity()))
                BoostSalvoUses(skill, handler);
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"koleda car: ammo pouch skill-added postfix failed: {ex.Message}");
        }
    }

    // Defers the arithmetic to the pouch's own GetNewSkillUses rather than
    // restating it: that method truncates where a re-derivation is tempted to
    // round, and it applies the pouch's SkillFilter, so any balance or filter
    // change flows through untouched. The item type it is handed is Weapon,
    // matching how the game would present an item-backed vehicle gun.
    //
    // Only an untouched salvo is raised: a max that already differs from the
    // template's base means a loaded save or another modifier owns it. The
    // ledger is claimed only once the boost actually lands, so a sweep that
    // arrives too early leaves the later skill-added hook free to retry.
    private void BoostSalvoUses(Skill skill, AmmoPouchHandler handler)
    {
        if (_boostedSalvos.Contains(skill.Pointer))
            return;
        var template = skill.GetTemplate();
        if (handler.m_Template == null || template == null)
            return;
        var max = skill.GetMaxUses();
        var baseUses = template.Uses;
        if (baseUses <= 0 || max != baseUses)
            return;

        var raised = handler.GetNewSkillUses(max, template, new Il2CppSystem.Nullable<ItemType>(ItemType.Weapon));
        var bonus = raised - max;
        if (bonus <= 0)
            return;
        _boostedSalvos.Add(skill.Pointer);
        skill.SetMaxUses(raised);
        skill.SetUses(skill.GetUses() + bonus);
        Context.Log.Debug($"koleda car: ammo cases raised salvo uses by {bonus}");
    }
}
