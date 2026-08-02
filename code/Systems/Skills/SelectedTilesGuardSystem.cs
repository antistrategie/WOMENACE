using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Keeps multi-tile skills from killing the process.
//
// Skill.ApplyToTile schedules one delayed closure per repetition per element,
// and that closure (Skill+<>c__DisplayClass154_3::<ApplyToTile>b__5, verified
// from a Proton fault trace at GameAssembly+0x726A06) computes
//
//     Repetitions / SelectedTiles.Count
//
// with no zero guard. SelectedTiles is skill-instance state, and the engine
// empties it within milliseconds of a use ending, so any closure still in
// flight at that moment divides by zero. That is EXCEPTION_INT_DIVIDE_BY_ZERO
// in native code: no managed exception, no crash dump, the process simply
// dies. A long burst leaves closures airborne for seconds, which is why it
// reads as random.
//
// This is a vanilla defect, not a modding mistake. Vanilla only ever fields
// short bursts of these skills at costs no unit can pay twice in a turn, so
// its closures always land before the wipe. Anything cheaper, longer, or
// delayed reaches it.
//
// Every skill whose template selects its own tiles (AoEType SelectTilesOnUse)
// is covered, ours and vanilla's alike, on two fronts:
//
//   * the tile list is kept populated from a snapshot for as long as closures
//     may still read it, refusing the emptying write outright where the write
//     goes through the property setter and repairing it per frame otherwise
//     (an in-place Clear never calls the setter),
//   * the skill reports unusable across that same window, so no second use
//     starts while the first is airborne and no tile picker fights the guard
//     for the list.
//
// Both halves share one window deliberately. Splitting them lets a picker run
// while the guard still owns the list, and its clear-then-add sequence then
// lands on a list the guard has already replaced.
//
// WHEN CAN THIS SYSTEM GO? It exists only for a defect in the game, so it
// should be deleted the moment the game stops having it, and the counters
// below are how that gets noticed. Every mission that used a guarded skill
// logs one line: how many uses were guarded, and how many of those actually
// needed the guard (the engine emptied the list while the skill was still
// airborne). Today that second number matches the first, because the engine
// wipes the list within milliseconds of every use ending.
//
// A game update that reports "0 of N needed the guard" across a few missions
// means the engine no longer empties the list under a live skill, and this
// system is dead weight. Confirm before deleting: with this system disabled,
// assign an empty list to a live skill's SelectedTiles part way through a
// burst (a three-line dev verb). Surviving that means the divide is guarded
// upstream and this can go. Doing it while the guard still exists proves only
// that the guard works, since the guard refuses the assignment.
public sealed class SelectedTilesGuardSystem : JiangyuSystem
{
    // Mission tally, the removal signal described above.
    private int _guardedUses;
    private int _usesNeedingGuard;
    private readonly HashSet<System.IntPtr> _intervened = new();

    // How long past the end of a use a scheduled closure may still fire.
    // Measured worst case was around 2.5s on a 40-repetition burst.
    private const float TailSeconds = 4f;

    // Ceiling for a use whose OnAfterUse never arrives, so a guard can never
    // strand a skill unusable for the rest of the mission.
    private const float CeilingSeconds = 12f;

    private readonly Dictionary<System.IntPtr, float> _until = new();
    private readonly Dictionary<System.IntPtr, object> _pollers = new();
    private readonly Dictionary<System.IntPtr, List<Tile>> _snapshots = new();

    // Whether a template selects its own tiles, keyed by template pointer.
    // Il2cpp's GC does not move objects, so the pointer is a stable key, and
    // this keeps the hot IsUsable path off both a property read and a string
    // marshal for the overwhelming majority of skills that never qualify.
    private static readonly Dictionary<System.IntPtr, bool> ShapeCache = new();

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "OnUse", 3, OnSkillUse);
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.Skill", "OnAfterUse", 1, OnSkillAfterUse);
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.Skill", "set_SelectedTiles", 1, OnSelectedTilesAssigned);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "IsUsable", 0, OnUsableCheck);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "IsUsable", 1, OnUsableCheck);
    }

    // Pointers into freed templates answer for whatever is allocated there
    // next, so the shape cache is dropped whenever the templates are rebuilt.
    public override void OnTemplatesApplied() => ShapeCache.Clear();

    public override void OnSceneLoaded(int buildIndex, string sceneName) => ResetSceneState();

    public override void OnUnload() => ResetSceneState();

    private void ResetSceneState()
    {
        foreach (var handle in _pollers.Values)
            Context.Coroutines.Stop(handle);
        _pollers.Clear();
        _until.Clear();
        _snapshots.Clear();
        ReportTally();
    }

    // One line per mission that fired a guarded skill. Reads as noise until the
    // day it reads "0 of N", which is the signal that this system has outlived
    // the defect it exists for.
    private void ReportTally()
    {
        if (_guardedUses > 0)
        {
            var verdict = _usesNeedingGuard == 0
                ? "none needed it, so the engine may no longer empty the list under a live skill"
                : "each of those would have divided by zero unguarded";
            Context.Log.Info($"tile guard: {_usesNeedingGuard} of {_guardedUses} guarded use(s) needed the guard, {verdict}");
        }
        _guardedUses = 0;
        _usesNeedingGuard = 0;
        _intervened.Clear();
    }

    /// <summary>Whether this skill is inside its guarded window right now.</summary>
    public bool IsAirborne(Skill skill)
        => skill != null && _until.TryGetValue(skill.Pointer, out var until) && Time.time < until;

    private static bool SelectsItsOwnTiles(Skill skill)
    {
        var template = skill?.GetTemplate();
        if (template == null)
            return false;
        if (ShapeCache.TryGetValue(template.Pointer, out var known))
            return known;
        var shaped = template.AoEType == SkillAoEType.SelectTilesOnUse;
        ShapeCache[template.Pointer] = shaped;
        return shaped;
    }

    private static Skill AsSkill(PatchInfo info)
        => (info.Instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<Skill>();

    private void OnSkillUse(PatchInfo info)
    {
        try
        {
            // OnUse returns bool: a false result means the use was refused and
            // nothing was scheduled, so guarding it would strand the skill.
            if (info.Result is false)
                return;
            var skill = AsSkill(info);
            if (!SelectsItsOwnTiles(skill))
                return;

            var live = skill.SelectedTiles;
            if (live == null || live.Count == 0)
                return;
            var snapshot = new List<Tile>(live.Count);
            foreach (var tile in live)
                if (tile != null)
                    snapshot.Add(tile);
            if (snapshot.Count == 0)
                return;

            var key = skill.Pointer;
            _guardedUses++;
            _intervened.Remove(key);
            _snapshots[key] = snapshot;
            _until[key] = Time.time + CeilingSeconds;
            if (_pollers.TryGetValue(key, out var running))
                Context.Coroutines.Stop(running);
            _pollers[key] = Context.Coroutines.Start(HoldTiles(skill));
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"tile guard: use postfix failed: {ex.Message}");
        }
    }

    // The burst has finished but its trailing closures have not, so the window
    // shrinks to the tail rather than ending here.
    private void OnSkillAfterUse(PatchInfo info)
    {
        try
        {
            var skill = AsSkill(info);
            if (skill == null || !_until.ContainsKey(skill.Pointer))
                return;
            _until[skill.Pointer] = Time.time + TailSeconds;
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"tile guard: after-use prefix failed: {ex.Message}");
        }
    }

    // The deterministic half: refuse the write itself, with no frame in which
    // the list reads empty.
    private void OnSelectedTilesAssigned(PatchInfo info)
    {
        try
        {
            var skill = AsSkill(info);
            if (skill == null || !IsAirborne(skill))
                return;
            var incoming = (info.Args is { Count: > 0 } ? info.Args[0] : null)
                as Il2CppSystem.Collections.Generic.List<Tile>;
            if (incoming != null && incoming.Count > 0)
                return;
            info.Skip = true;
            _intervened.Add(skill.Pointer);
            Context.Log.Debug("tile guard: refused an empty tile assignment under a live skill");
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"tile guard: tile-assign prefix failed: {ex.Message}");
        }
    }

    private void OnUsableCheck(PatchInfo info)
    {
        try
        {
            // near-free rejection for the common case: nothing is airborne, so
            // this never touches the skill at all
            if (_until.Count == 0 || info.Result is false)
                return;
            var skill = AsSkill(info);
            if (skill == null || !_until.TryGetValue(skill.Pointer, out var until))
                return;
            if (Time.time < until)
                info.Result = false;
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"tile guard: usable postfix failed: {ex.Message}");
        }
    }

    // The backstop for an in-place Clear, which never reaches the setter.
    private System.Collections.IEnumerator HoldTiles(Skill skill)
    {
        var key = skill.Pointer;
        var held = false;
        while (true)
        {
            var restored = false;
            try
            {
                if (!_until.TryGetValue(key, out var until) || Time.time >= until)
                    break;
                var live = skill.SelectedTiles;
                if ((live == null || live.Count == 0) && _snapshots.TryGetValue(key, out var snapshot))
                {
                    // a fresh list every time: whatever emptied the old one may
                    // still hold a reference to it
                    var replacement = new Il2CppSystem.Collections.Generic.List<Tile>();
                    foreach (var tile in snapshot)
                        replacement.Add(tile);
                    skill.SelectedTiles = replacement;
                    restored = true;
                    held = true;
                    _intervened.Add(key);
                }
            }
            catch (System.Exception ex)
            {
                Context.Log.Warn($"tile guard: hold failed: {ex.Message}");
                break;
            }
            if (restored)
                Context.Log.Debug("tile guard: restored the tile list under a live skill");
            yield return null;
        }

        Release(skill, key, held);
    }

    // Hand the engine its own state back: no closure can still be pending, and
    // leaving tiles selected would seed the next selection with stale ones.
    private void Release(Skill skill, System.IntPtr key, bool held)
    {
        _until.Remove(key);
        _pollers.Remove(key);
        _snapshots.Remove(key);
        if (_intervened.Remove(key))
            _usesNeedingGuard++;
        try
        {
            if (held)
                skill.SelectedTiles = new Il2CppSystem.Collections.Generic.List<Tile>();
            // the skill bar only re-polls on events, so nudge it: without this
            // the button can sit greyed until an unrelated refresh
            skill.m_Container?.ScheduleUpdate();
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"tile guard: release failed: {ex.Message}");
        }
    }
}
