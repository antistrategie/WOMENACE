using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Doors;
using Il2CppMenace.Tactical.Skills;
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
public sealed class KoledaCarSystem : JiangyuSystem
{
    private const string CarId = "player_vehicle.koleda_car";
    private const string SalvoId = "active.sinner_mg";
    private const string DoorsParam = "DoorsOut";

    // Template identity caches: both hooks run for every skill use and every
    // entrance recalculation in the game, and GetID marshals a fresh managed
    // string per call. Il2cpp's GC does not move objects, so a matched
    // template's pointer is a stable fast-path key. A miss still falls back
    // to the string compare, which repopulates the cache after any reload
    // that re-creates templates.
    private static System.IntPtr _carTemplate;
    private static System.IntPtr _salvoTemplate;

    // Live coroutines keyed by the entity (or actor) they serve, so a second
    // car's spawn or salvo never cancels the first car's. Cleared wholesale
    // on scene change, which also bounds the pointer keys' lifetime.
    private readonly Dictionary<System.IntPtr, object> _watchers = new();
    private readonly Dictionary<System.IntPtr, object> _settles = new();

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Tactical.Element", "RecalculateEntranceTiles", 0, OnEntrancesRecalculated);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "OnUse", 3, OnSalvoUse);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "OnAfterUse", 1, OnSalvoAfterUse);
    }

    // Missions never hand a live car across a scene change, so every watcher
    // and settle belongs to the scene that started it. Stopping them here
    // keeps a car that survives the mission from polling into the strategy
    // layer and pinning the dead mission's wrapper graph.
    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        StopAllCoroutines();
    }

    public override void OnUnload()
    {
        StopAllCoroutines();
    }

    private void StopAllCoroutines()
    {
        foreach (var handle in _watchers.Values)
            Context.Coroutines.Stop(handle);
        _watchers.Clear();
        foreach (var handle in _settles.Values)
            Context.Coroutines.Stop(handle);
        _settles.Clear();
    }

    private static bool IsTheCar(Entity entity)
    {
        var template = entity?.GetTemplate();
        if (template == null)
            return false;
        if (template.Pointer == _carTemplate)
            return true;
        if (template.GetID() != CarId)
            return false;
        _carTemplate = template.Pointer;
        return true;
    }

    private static bool IsTheSalvo(PatchInfo info, out Skill skill)
    {
        skill = (info.Instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<Skill>();
        var template = skill?.GetTemplate();
        if (template == null)
            return false;
        if (template.Pointer == _salvoTemplate)
            return true;
        if (template.GetID() != SalvoId)
            return false;
        _salvoTemplate = template.Pointer;
        return true;
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

    private void OnSalvoUse(PatchInfo info)
    {
        try
        {
            if (!IsTheSalvo(info, out _))
                return;
            var actor = (info.Args is { Count: > 0 } ? info.Args[0] : null) as Actor;
            var animator = CarAnimator(actor);
            if (animator?.m_Animator == null)
                return;
            // a fresh salvo takes over the model rotation: stop any settle
            // still easing this car back from the previous one
            StopKeyed(_settles, actor.Pointer);
            animator.m_Animator.SetBool(DoorsParam, true);
            Context.Log.Debug("koleda car: doors out");
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"koleda car: salvo-use postfix failed: {ex.Message}");
        }
    }

    private void OnSalvoAfterUse(PatchInfo info)
    {
        try
        {
            if (!IsTheSalvo(info, out _))
                return;
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
        yield return new WaitForSeconds(2.5f);
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
}
