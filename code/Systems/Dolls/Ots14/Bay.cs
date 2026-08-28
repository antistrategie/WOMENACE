using Il2CppMenace.Items;
using Il2CppInterop.Runtime.InteropTypes;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The weapons bay model: which owned special-weapon instances ride which of
// OTs-14's four arms. Slots hold ITEM GUIDS, not template ids, so calibration
// ranks and imprint identity travel with the exact instance the player
// slotted, and the bay survives save round-trips through Context.State like
// transmog and affinity do. The equip surface (the user's modal, and the dev
// verbs until it exists) writes slots through TrySetSlot, which owns the
// rules: special weapons only, each owned instance in at most one slot.
public sealed class BayState
{
    // character tag -> item guid per slot (null = empty).
    public Dictionary<string, string[]> Loadouts { get; set; } = [];
}

public static class Bay
{
    public const int SlotCount = 4;
    public const string CharacterTag = "wmgfl_ots14";

    // The one vanilla infrastructure skill every tripod weapon grants for its
    // set-up step. The bay has no deploy step, so this skill is never granted
    // from a bay weapon (its fire skills are made usable directly instead).
    public const string DeploySkillId = "active.infantry_deploy_heavy_weapon";

    // Slot index -> the arm prefab's palm bone the weapon mounts to. The
    // order is SPATIAL, relative to the doll: 0 her top-left arm, 1 top-
    // right, 2 bottom-left, 3 bottom-right (rest heights measured off the
    // arms glb: Cup_L +0.82, Cup1_R +0.69, Cup1_L -0.28, Cup_R -0.42), so
    // tile numbers, the tactical bar's left-to-right order and the arms
    // themselves all agree.
    public static readonly string[] HandBones = { "Cup_L", "Cup1_R", "Cup1_L", "Cup_R" };

    public static string[] Loadout(ModContext context, string characterTag = CharacterTag)
    {
        var loadouts = context.State.Get<BayState>().Loadouts;
        if (!loadouts.TryGetValue(characterTag, out var slots) || slots == null || slots.Length != SlotCount)
        {
            slots = new string[SlotCount];
            loadouts[characterTag] = slots;
        }
        return slots;
    }

    // Read-only view of a loadout: null when the character has no entry yet,
    // rather than minting one. READ paths must use this. Loadout above writes
    // a fresh array into persistent state on every miss, so calling it from
    // something as incidental as a tooltip hover permanently adds an entry for
    // whoever was hovered over.
    public static string[] LoadoutOrNull(ModContext context, string characterTag = CharacterTag)
    {
        var loadouts = context.State.Get<BayState>().Loadouts;
        return loadouts.TryGetValue(characterTag, out var slots) && slots != null && slots.Length == SlotCount
            ? slots
            : null;
    }

    // Drop slots whose item no longer exists (sold, or otherwise gone). A stale
    // guid paints an empty tile that still offers UNEQUIP.
    //
    // Guarded on the inventory being readable first: ResolveItem cannot tell
    // "this item is gone" from "there is no inventory to ask yet", and pruning
    // on the second would erase the whole bay on any scene without strategy
    // state. Returns how many slots it cleared.
    public static int Prune(ModContext context, string characterTag = CharacterTag)
    {
        try
        {
            if (Jiangyu.Game.Strategy.Inventory.Owned == null)
                return 0;
        }
        catch
        {
            return 0;
        }
        var slots = LoadoutOrNull(context, characterTag);
        if (slots == null)
            return 0;
        var dropped = 0;
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null || ResolveItem(slots[i]) != null)
                continue;
            slots[i] = null;
            dropped++;
        }
        return dropped;
    }

    public static Item ResolveItem(string guid)
    {
        if (string.IsNullOrEmpty(guid))
            return null;
        try
        {
            return (Jiangyu.Game.Strategy.Inventory.Owned?.GetItemByGuid(guid) as Il2CppObjectBase)?.TryCast<Item>();
        }
        catch
        {
            return null;
        }
    }

    public static WeaponTemplate WeaponOf(Item item)
        => (item?.GetTemplate() as Il2CppObjectBase)?.TryCast<WeaponTemplate>();

    public static bool IsBayWeapon(Item item)
    {
        var weapon = WeaponOf(item);
        return weapon != null && weapon.SlotType == ItemSlot.InfantrySpecial;
    }

    // Words that mark a weapon's CATEGORY (its ShortName, which vanilla uses
    // as the displayed category label) as ordnance or energy rather than a
    // gun firing bullets: launchers, rockets, mortars, artillery pieces,
    // beams, flame and plasma. Matched as substrings against the lowercased
    // label, so a new weapon classifies itself by being named sensibly.
    //
    // Deliberately NOT ordnance: rifles, snipers, DMRs, machineguns of every
    // size, autocannons, miniguns and chainguns - sustained-fire bullet
    // weapons, however heavy their mount. The anti-materiel "AT Rifle" is
    // ordnance by role (a bullet gun built to kill vehicles), the one
    // deliberate exception to the bullets rule.
    private static readonly string[] OrdnanceWords =
    {
        "launcher", "rocket", "rpg", "mortar", "grenade", "airburst", "stun",
        "anti-tank", "atgm", "laser", "lance", "plasma", "flamer", "emp",
        "railgun", "at rifle",
    };

    // Matched against the WHOLE label rather than as substrings, which keeps
    // "Autocannon" (a bullet gun) out while admitting a bare "Cannon".
    private static readonly string[] OrdnanceExactWords = { "cannon" };

    // Whether a bay item is ordnance/energy rather than a bullet gun. The
    // category label is the only honest signal: deployment gates cover
    // snipers and MMGs too, and AP costs are near-flat across the roster.
    public static bool IsOrdnance(Item item)
    {
        var label = Templates.DefaultText(WeaponOf(item)?.ShortName)?.ToLowerInvariant();
        if (string.IsNullOrEmpty(label))
            return false;
        foreach (var word in OrdnanceWords)
            if (label.Contains(word, StringComparison.Ordinal))
                return true;
        foreach (var word in OrdnanceExactWords)
            if (label == word)
                return true;
        return false;
    }

    // Whether the item could go into the slot, without writing anything: the
    // equip UI greys rows with the reason, TrySetSlot enforces on write.
    public static bool CanSlot(ModContext context, int slot, Item item, out string error,
        string characterTag = CharacterTag)
    {
        error = null;
        if (slot < 0 || slot >= SlotCount)
        {
            error = $"slot {slot} out of range 0..{SlotCount - 1}";
            return false;
        }
        if (item == null)
            return true;
        if (!IsBayWeapon(item))
        {
            error = $"'{item.GetTemplate()?.GetID()}' is not a special weapon";
            return false;
        }
        var slots = LoadoutOrNull(context, characterTag);
        if (slots == null)
            return true; // no bay minted yet, nothing to collide with
        var guid = item.GetGuid();
        for (var i = 0; i < SlotCount; i++)
        {
            if (i == slot || slots[i] == null)
                continue;
            if (slots[i] == guid)
            {
                error = "that instance is already in the bay";
                return false;
            }
        }
        return true;
    }

    public static bool TrySetSlot(ModContext context, int slot, Item item, out string error,
        string characterTag = CharacterTag)
    {
        if (!CanSlot(context, slot, item, out error, characterTag))
            return false;
        Loadout(context, characterTag)[slot] = item?.GetGuid();
        return true;
    }
}
