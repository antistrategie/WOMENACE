using Il2CppMenace.Tactical;

namespace WOMENACE.Code;

// Which once-per-mission charges have been spent, keyed by the actor holding them and the group
// name the KDL handler declares. The store is the only state MissionCharge needs: the gate itself
// is the handler's IsUsable override, so nothing here patches or mutates the engine.
//
// Keyed per actor so two dolls carrying the same kit do not share one charge, and cleared between
// missions by MissionChargeSystem.
public static class MissionCharges
{
    private static readonly HashSet<(IntPtr Actor, string Group)> Spent = new();

    private static bool TryKey(Actor actor, string group, out (IntPtr, string) key)
    {
        key = default;
        if (actor == null || string.IsNullOrEmpty(group))
            return false;
        key = (actor.Pointer, group);
        return true;
    }

    public static bool IsSpent(Actor actor, string group)
        => TryKey(actor, group, out var key) && Spent.Contains(key);

    // Returns false when the group was already spent this mission, so a caller can tell a first
    // spend from a click that slipped through after the charge was gone.
    public static bool MarkSpent(Actor actor, string group)
        => TryKey(actor, group, out var key) && Spent.Add(key);

    // Charges refill between missions. Actor pointers do not survive a mission, so the whole store
    // goes rather than being pruned.
    public static void ResetForMission() => Spent.Clear();
}
