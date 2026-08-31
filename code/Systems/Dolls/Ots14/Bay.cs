using Il2CppMenace.Items;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Il2CppInterop.Runtime.InteropTypes;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The weapons bay model: which owned special-weapon instances ride which of
// OTs-14's four arms. Slots hold ITEM GUIDS, not template ids, so calibration
// ranks and imprint identity travel with the exact instance the player
// slotted, and the bay survives save round-trips through Context.State like
// transmog and affinity do. The equip surface (the user's modal, and the dev
// verbs until it exists) writes slots through TrySetSlot, which owns the
// rules: special weapons only, each owned instance in at most one slot, and
// never an instance some doll has equipped (one physical item in two
// loadouts aliases its skill list across both units, so each bar drew the
// union and either doll could fire the other's copy on the other's AP).
public sealed class BayState
{
    // character tag -> item guid per slot (null = empty).
    public Dictionary<string, string[]> Loadouts { get; set; } = [];
}

public static class Bay
{
    public const int SlotCount = 4;
    public const string CharacterTag = "wmgfl_ots14";
    public const string WeaponId = "weapon.ots14";

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

    // Drop slots whose item no longer exists (sold, or otherwise gone) or is
    // no longer available (a doll equipped it, which ResolveItem reports as
    // missing). A stale guid paints an empty tile that still offers UNEQUIP.
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
            var item = (Jiangyu.Game.Strategy.Inventory.Owned?.GetItemByGuid(guid) as Il2CppObjectBase)?.TryCast<Item>();
            // An instance some doll has equipped is off limits even when a
            // slot still names it (a doll equipped it AFTER it was slotted):
            // resolving it as missing makes every consumer treat the slot as
            // empty, and the next Prune turns that into the persistent
            // eviction. Equip wins, the bay yields.
            return item != null && IsEquipped(item) ? null : item;
        }
        catch
        {
            return null;
        }
    }

    // Whether some unit has this instance equipped. An equipped item sits in
    // its owner's ItemContainer; stock and bay instances have none (the
    // container lifecycle keeps the link current on every equip/unequip, and
    // it is what the vanilla unused-instance lookups read too).
    public static bool IsEquipped(BaseItem item)
    {
        try
        {
            return item?.GetContainer() != null;
        }
        catch
        {
            return false;
        }
    }

    // Whether a leader item container is OTs-14's own, whatever primary she
    // carries. The owner linkage names her outright (the strategy leader, or
    // the tactical entity once it is wired), and a container whose owner
    // names some OTHER doll is never hers, even holding her signature rifle.
    // The rifle fallback decides only for containers with no readable doll
    // owner: element creation can run before the owner is wired, and vanilla
    // squads carry no speaker tag, so that path keeps the old weapon-only
    // gate's behaviour.
    public static bool IsHerContainer(ItemContainer items)
    {
        if (items == null)
            return false;
        try
        {
            var owner = items.GetOwner() as Il2CppObjectBase;
            var tag = Affinity.CharacterTag(owner?.TryCast<BaseUnitLeader>())
                ?? Affinity.CharacterTag(owner?.TryCast<Entity>());
            if (tag != null)
                return tag == CharacterTag;
        }
        catch
        {
            // owner unreadable mid-creation: the rifle fallback decides
        }
        try
        {
            return items.GetItemAtSlot(ItemSlot.InfantryWeapon)?.GetTemplate()?.GetID() == WeaponId;
        }
        catch
        {
            return false;
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
    //
    // A vanilla label never translates (the loader only ever rewrites text a
    // mod authored), so vanilla is read from the label alone. One of our own
    // weapons does have its default replaced with the active language once a
    // translation is installed, so those are read from the template id first,
    // which never translates.
    //
    // The id pass is OURS ONLY on purpose. An id carries the whole build,
    // attachments included, so an assault rifle with an underbarrel launcher
    // is `..._kpac_grenade_launcher` while its label is honestly "Assault
    // Rifle". Reading vanilla ids would call that ordnance and contradict the
    // bullets rule above; reading only our own ids keeps the naming under our
    // control.
    public static bool IsOrdnance(Item item)
    {
        var weapon = WeaponOf(item);
        if (weapon == null)
            return false;

        // Ours is answered from the id and stops there. Falling through to the label would put us
        // back on translated text, which is the whole reason the id pass exists, and matching short
        // English needles ("emp", "rpg", "stun") as substrings of arbitrary translated prose flips
        // classifications by locale with nothing in the log to show for it.
        if (IsOurs(weapon))
            return IdSaysOrdnance(weapon.GetID());

        var label = Templates.DefaultText(weapon.ShortName)?.ToLowerInvariant();
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

    // Whether the weapon is one of ours, read from the marker every WOMENACE weapon carries on its
    // Tags. Authored, so it answers for exactly the weapons we name and no others. The instance-id
    // sign would also separate runtime clones from serialised templates, but it answers a different
    // question: the game and other mods build templates at runtime too, and one of those reaching
    // the id pass is how a vanilla rifle with an underbarrel launcher gets called ordnance.
    private static bool IsOurs(WeaponTemplate weapon)
    {
        try
        {
            var tags = weapon.Tags;
            if (tags == null)
                return false;
            for (var i = 0; i < tags.Count; i++)
                if (tags[i]?.name?.StartsWith(OurTagPrefix, StringComparison.Ordinal) == true)
                    return true;
            return false;
        }
        catch { return false; }
    }

    // The prefix every WOMENACE tag shares, which is what marks a template as ours.
    private const string OurTagPrefix = "wmgfl_";

    // Ordnance words matched against the id's `_`- and `.`-delimited segments rather than as bare
    // substrings, so "autocannon" cannot match "cannon" and "grenadier" cannot match "grenade".
    // Multi-word entries ("at rifle", "anti-tank") match a RUN of consecutive segments, for the
    // same reason: joining the segments and searching the string would let "combat rifle" satisfy
    // "at rifle" across the word boundary.
    private static bool IdSaysOrdnance(string templateId)
    {
        if (string.IsNullOrEmpty(templateId))
            return false;

        var segments = templateId.ToLowerInvariant().Split('.', '_');
        foreach (var word in OrdnanceWords)
        {
            var parts = word.Replace('-', ' ').Split(' ');
            for (var i = 0; i + parts.Length <= segments.Length; i++)
            {
                var run = true;
                for (var j = 0; j < parts.Length && run; j++)
                    run = segments[i + j] == parts[j];
                if (run)
                    return true;
            }
        }
        foreach (var word in OrdnanceExactWords)
            foreach (var segment in segments)
                if (segment == word)
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
        if (IsEquipped(item))
        {
            error = "that instance is equipped on a unit";
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
