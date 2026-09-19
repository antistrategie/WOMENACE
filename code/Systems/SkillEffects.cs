using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;

namespace WOMENACE.Code;

// Shared skill-container plumbing for status-effect style skills, so the
// instantiate-and-add dance, the queue-aware instance count and the bounded
// removal exist once (ElementsSystem and SoppoFormsSystem both apply effects,
// EffectHudIconSystem and SoppoFormsSystem both count them, four systems strip
// them).
internal static class SkillEffects
{
    // Instantiate the effect template and add it to the actor's container.
    // False (with the reason routed to warn) when anything refuses.
    internal static bool TryAddEffect(Actor actor, SkillTemplate template, Action<string> warn)
    {
        var skills = actor?.GetSkills();
        if (skills == null || template == null)
            return false;
        // a boxed EMPTY nullable, not managed null: null for an
        // Il2CppSystem.Nullable proxy misbehaves in the interop marshalling
        var instance = template.CreateSkill(new Il2CppSystem.Nullable<Il2CppMenace.Strategy.Origin>());
        if (instance == null)
        {
            warn?.Invoke($"CreateSkill returned null for '{template.GetID()}'");
            return false;
        }
        if (!skills.Add(instance))
        {
            warn?.Invoke($"container rejected '{template.GetID()}'");
            return false;
        }
        return true;
    }

    // The live instance of the template in the container, or null. Queue-aware for
    // the same reason CountInstances is: a skill added this frame is still in the
    // add queue, not the settled list.
    internal static Skill FindInstance(SkillContainer skills, SkillTemplate template)
    {
        if (skills == null || template == null)
            return null;
        // Keep scanning past a template match that is not a Skill: the container holds
        // BaseSkill, so a non-Skill entry sharing the template must not end the search.
        var all = skills.GetAllSkills();
        var settled = all?.Count ?? 0;
        for (var i = 0; i < settled; i++)
            if (IsLive(all[i], template) && all[i].TryCast<Skill>() is { } match)
                return match;
        var queued = skills.GetSkillsInAddQueue();
        var pending = queued?.Count ?? 0;
        for (var i = 0; i < pending; i++)
            if (IsLive(queued[i], template) && queued[i].TryCast<Skill>() is { } queuedMatch)
                return queuedMatch;
        return null;
    }

    // Live instances of the template, including ones still sitting in the
    // container's add queue: the Add postfix fires before the queue drains,
    // so a settled-list-only count misses just-applied skills.
    internal static int CountInstances(SkillContainer skills, SkillTemplate template)
    {
        if (skills == null || template == null)
            return 0;
        var count = 0;
        var all = skills.GetAllSkills();
        var settled = all?.Count ?? 0;
        for (var i = 0; i < settled; i++)
            if (IsLive(all[i], template))
                count++;
        var queued = skills.GetSkillsInAddQueue();
        var pending = queued?.Count ?? 0;
        for (var i = 0; i < pending; i++)
            if (IsLive(queued[i], template))
                count++;
        return count;
    }

    // An instance of the template that is still in play: a skill flagged as garbage
    // (a removal deferred until the container's sweep) is already gone for our purposes.
    private static bool IsLive(BaseSkill skill, SkillTemplate template)
        => skill != null && !skill.IsGarbage() && skill.GetTemplate()?.Pointer == template.Pointer;

    // Removes every live instance of the template, bounded: one pass over the settled list
    // and the add queue, one removal per instance found. With `keep` the call collapses to a
    // single instance instead: `keep` itself when it is live in the container, else the newest
    // live instance (a rejected Add only refreshes the existing one, and that one must stay).
    //
    // NEVER loop on SkillContainer.Remove(SkillTemplate). While the container's update
    // stack is raised, which it is for every OnMovementFinished / OnTurnEnd / OnUpdate
    // dispatch into handlers, that overload only flags the first match as garbage
    // (BaseSkill.RemoveSelf) and returns true, and the next call finds the same flagged
    // skill again, so `while (Remove(template))` never ends: this froze the game the
    // moment Mechty moved with Let Me Sleep stacks. Settled instances go through
    // Remove(BaseSkill), immediate outside a dispatch and a garbage flag inside one; an
    // instance still in the add queue is flagged with RemoveSelf so the sweep drops it
    // once it lands.
    internal static int RemoveInstances(SkillContainer skills, SkillTemplate template, BaseSkill keep = null)
    {
        if (skills == null || template == null)
            return 0;
        var settled = new List<BaseSkill>();
        var queued = new List<BaseSkill>();
        var all = skills.GetAllSkills();
        var count = all?.Count ?? 0;
        for (var i = 0; i < count; i++)
            if (IsLive(all[i], template))
                settled.Add(all[i]);
        var adding = skills.GetSkillsInAddQueue();
        var pending = adding?.Count ?? 0;
        for (var i = 0; i < pending; i++)
            if (IsLive(adding[i], template))
                queued.Add(adding[i]);
        if (keep != null)
        {
            var keeper = settled.Find(s => s.Pointer == keep.Pointer) ?? queued.Find(s => s.Pointer == keep.Pointer);
            keeper ??= queued.Count > 0 ? queued[^1] : settled.Count > 0 ? settled[^1] : null;
            if (keeper != null)
            {
                settled.RemoveAll(s => s.Pointer == keeper.Pointer);
                queued.RemoveAll(s => s.Pointer == keeper.Pointer);
            }
        }
        foreach (var skill in settled)
            skills.Remove(skill);
        foreach (var skill in queued)
            skill.RemoveSelf();
        // the sweep that drops flagged skills runs on the container's next update; ask for one
        if (settled.Count + queued.Count > 0)
            skills.ScheduleUpdate();
        return settled.Count + queued.Count;
    }

    // Queue-aware counts for several templates in ONE container pass: each
    // skill's template pointer is looked up in `slots` (pointer -> index into
    // `counts`). For a caller tracking N effects this replaces N full scans
    // (each re-marshalling every skill's GetTemplate) with one.
    internal static void CountInstancesInto(SkillContainer skills, Dictionary<IntPtr, int> slots, int[] counts)
    {
        Array.Clear(counts, 0, counts.Length);
        if (skills == null || slots.Count == 0)
            return;
        var all = skills.GetAllSkills();
        var settled = all?.Count ?? 0;
        for (var i = 0; i < settled; i++)
        {
            var pointer = all[i] != null && !all[i].IsGarbage() ? all[i].GetTemplate()?.Pointer ?? IntPtr.Zero : IntPtr.Zero;
            if (pointer != IntPtr.Zero && slots.TryGetValue(pointer, out var slot))
                counts[slot]++;
        }
        var queued = skills.GetSkillsInAddQueue();
        var pending = queued?.Count ?? 0;
        for (var i = 0; i < pending; i++)
        {
            var pointer = queued[i] != null && !queued[i].IsGarbage() ? queued[i].GetTemplate()?.Pointer ?? IntPtr.Zero : IntPtr.Zero;
            if (pointer != IntPtr.Zero && slots.TryGetValue(pointer, out var slot))
                counts[slot]++;
        }
    }
}
