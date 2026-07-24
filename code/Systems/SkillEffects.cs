using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;

namespace WOMENACE.Code;

// Shared skill-container plumbing for status-effect style skills, so the
// instantiate-and-add dance and the queue-aware instance count exist once
// (ElementsSystem and SoppoFormsSystem both apply effects, EffectHudIconSystem
// and SoppoFormsSystem both count them).
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

    // Live instances of the template, including ones still sitting in the
    // container's add queue: the Add postfix fires before the queue drains,
    // so a settled-list-only count misses just-applied skills.
    internal static int CountInstances(SkillContainer skills, SkillTemplate template)
    {
        if (skills == null || template == null)
            return 0;
        var count = 0;
        var all = skills.GetAllSkills();
        for (var i = 0; all != null && i < all.Count; i++)
            if (all[i]?.GetTemplate()?.Pointer == template.Pointer)
                count++;
        var queued = skills.GetSkillsInAddQueue();
        for (var i = 0; queued != null && i < queued.Count; i++)
            if (queued[i]?.GetTemplate()?.Pointer == template.Pointer)
                count++;
        return count;
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
        for (var i = 0; all != null && i < all.Count; i++)
        {
            var pointer = all[i]?.GetTemplate()?.Pointer ?? IntPtr.Zero;
            if (pointer != IntPtr.Zero && slots.TryGetValue(pointer, out var slot))
                counts[slot]++;
        }
        var queued = skills.GetSkillsInAddQueue();
        for (var i = 0; queued != null && i < queued.Count; i++)
        {
            var pointer = queued[i]?.GetTemplate()?.Pointer ?? IntPtr.Zero;
            if (pointer != IntPtr.Zero && slots.TryGetValue(pointer, out var slot))
                counts[slot]++;
        }
    }
}
