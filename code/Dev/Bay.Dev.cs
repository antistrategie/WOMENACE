using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for OTs-14's weapons bay, the stand-in equip surface until the
// bay modal exists. Invoked over the dev-loader bridge as e.g.
// {verb: "Bay.Equip", args: {weaponId: "specialweapon.rpg_launcher_tier_1"}, mutate: true}.
// Changes take effect on the next tactical spawn (mounting happens at
// Element.CreateAttachments).
[DevVerb]
public static class BayVerbs
{
    private static readonly Dictionary<string, WeaponTemplate> Cache = new(StringComparer.Ordinal);

    // Slot an owned instance of the weapon into the bay: the first free slot,
    // or the given one. Mints a fresh instance into the inventory when no
    // unslotted one is owned.
    [MutatingVerb]
    public static object Equip(string weaponId, int slot = -1)
    {
        var context = BayMountSystem.Instance?.Context;
        if (context == null)
            return new { error = "bay system not initialised" };
        var template = Templates.Resolve<WeaponTemplate>(weaponId, Cache, msg => context.Log.Warn($"bay: {msg}"));
        if (template == null)
            return new { error = $"unknown weapon template '{weaponId}'" };
        var owned = Jiangyu.Game.Strategy.Inventory.Owned;
        if (owned == null)
            return new { error = "no strategy state / owned items" };

        var slots = Bay.Loadout(context);
        var inBay = new HashSet<string>(slots.Where(g => g != null), StringComparer.Ordinal);
        Item item = null;
        var all = new Il2CppSystem.Collections.Generic.List<BaseItem>();
        owned.GetInstances(all);
        for (var i = 0; i < all.Count; i++)
        {
            var candidate = all[i]?.TryCast<Item>();
            if (candidate == null || candidate.GetTemplate()?.GetID() != weaponId)
                continue;
            if (inBay.Contains(candidate.GetGuid()))
                continue;
            item = candidate;
            break;
        }
        if (slot < 0)
        {
            slot = Array.IndexOf(slots, null);
            if (slot < 0)
                return new { error = "bay is full, pass slot to replace" };
        }

        var minted = false;
        if (item == null)
        {
            item = (owned.AddItem(template, false, false) as Il2CppObjectBase)?.TryCast<Item>();
            minted = true;
            if (item == null)
                return new { error = $"could not mint an instance of '{weaponId}'" };
        }
        if (!Bay.TrySetSlot(context, slot, item, out var error))
            return new { error };
        // A loadout changed while a mission is running (or loading) only
        // takes effect at the next element spawn: surface that so a bridge
        // driver never mistakes a late equip for an applied one.
        var missionRunning = false;
        try
        {
            missionRunning = Il2CppMenace.States.TacticalState.Get() != null;
        }
        catch
        {
            // strategy layer
        }
        return new { ok = true, slot, weaponId, minted, missionRunning };
    }

    // Empty one slot, or the whole bay.
    [MutatingVerb]
    public static object Clear(int slot = -1)
    {
        var context = BayMountSystem.Instance?.Context;
        if (context == null)
            return new { error = "bay system not initialised" };
        var slots = Bay.Loadout(context);
        // -1 wipes everything, an index clears one slot, and anything else
        // refuses: a 1-based habit passing 4 must not empty the whole bay.
        if (slot == -1)
            for (var i = 0; i < Bay.SlotCount; i++)
                slots[i] = null;
        else if (slot >= 0 && slot < Bay.SlotCount)
            slots[slot] = null;
        else
            return new { error = $"slot {slot} out of range 0..{Bay.SlotCount - 1}, or -1 for all" };
        return new { ok = true, loadout = Describe(context) };
    }

    // Returns the list directly: the verb runner stringifies anonymous
    // wrappers, which would render the loadout as an opaque List`1.
    public static object Show()
    {
        var context = BayMountSystem.Instance?.Context;
        if (context == null)
            return new { error = "bay system not initialised" };
        return Describe(context);
    }

    // The skills the bay granted to her actor in the current mission.
    // Every twin-link counter side by side: each arm's uses against the
    // linked skill's, so a mismatch is read rather than deduced.
    public static object Links()
    {
        var actor = Il2CppMenace.Tactical.TacticalManager.Get()?.GetActiveActor();
        if (actor == null)
            return new { error = "no active actor" };
        return BaySkillSystem.DescribeLinks(actor.Pointer)
            .Select(r => (object)string.Join("  ", r.Select(kv => $"{kv.Key}={kv.Value}")))
            .ToList();
    }

    public static object Skills()
        => new { granted = BaySkillSystem.DescribeGranted() };

    // Dictionaries, not anonymous types: the verb runner stringifies anonymous
    // types, which renders nested arrays as "System.Object[]".
    private static List<Dictionary<string, object>> Describe(ModContext context)
    {
        var slots = Bay.Loadout(context);
        var result = new List<Dictionary<string, object>>();
        for (var i = 0; i < Bay.SlotCount; i++)
        {
            var item = Bay.ResolveItem(slots[i]);
            result.Add(new Dictionary<string, object>
            {
                ["slot"] = i,
                ["hand"] = Bay.HandBones[i],
                ["weapon"] = item?.GetTemplate()?.GetID(),
                ["guid"] = slots[i],
            });
        }
        return result;
    }
}
