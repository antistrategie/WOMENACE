using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Items;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Linking the bay's arms: arms holding the same weapon can fire together as
// one action, as a pair or as all four at once.
//
// Vanilla has the pair form for modular vehicles and it is worth following
// exactly. ItemsModularVehicle.CheckForTwinFire raises an IsTwinFire flag once
// GetEquippedCount sees two of the same ModularVehicleWeaponTemplate, and the
// weapon grants BOTH variants up front (mod_weapon.medium.minigun lists
// active.mod.minigun_salvo and active.mod.twinfire.minigun_salvo), with
// SkillTemplate.IsTwinFireSkill picking which is live. The twinfire variant is
// the single one with Repetitions DOUBLED and RepetitionDelay HALVED, for the
// same AP and the same per-shot damage. Diffed across four vanilla pairs:
// minigun 15 -> 30, light machinegun 15 -> 30, rocket 1 -> 2, flamethrower
// 1 -> 2, delays 0.03 -> 0.015, 0.08 -> 0.04, 0.5 -> 0.05.
//
// None of that machinery reaches us: it lives on ItemsModularVehicle and keys
// off ModularVehicleWeaponTemplate, while the bay holds InfantrySpecial
// weapons, and no twinfire variant is authored for those. So the RECIPE is
// reused rather than the code, and scaled by the number of arms it spans.
//
// Unlike a walker, the link is a MID-MISSION toggle rather than a property of
// the loadout, because her ammo is the constraint her kit is built on: a
// linked shot spends a use from every arm in the group, so keeping single
// fire available is what lets a player spend one rocket when one is enough.
public static class BayLink
{
    // The link groups, by slot. Bay.HandBones orders slots spatially (0 her
    // top-left, 1 top-right, 2 bottom-left, 3 bottom-right), so the pairs are
    // the two arms on the same level, and the quad is the whole bay. A slot
    // sits in several groups, but at most ONE of them is linked at a time:
    // SetLinked drops any group sharing a slot with the one being armed.
    public static readonly int[][] Groups = { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 0, 1, 2, 3 } };

    public const int QuadGroup = 2;

    // The linked group a slot currently fires in, or -1.
    public static int LinkedGroupOf(int slot)
    {
        foreach (var g in Linked)
            if (Array.IndexOf(Groups[g], slot) >= 0)
                return g;
        return -1;
    }

    // Weapons link when they are the same weapon REGARDLESS OF RANK: a player
    // holding two of the same gun reads them as a pair whether or not both
    // have been calibrated, and a rank mismatch is not something the bay
    // should quietly refuse over. Doll weapons resolve to their base id;
    // vanilla specials carry no rank suffix, so their template id already is
    // the rank-insensitive identity.
    public static string LinkKeyFor(Item item)
    {
        var id = item?.GetTemplate()?.GetID();
        if (id == null)
            return null;
        return Calibration.TryResolveWeaponId(id, out var baseId, out _) ? baseId : id;
    }

    // Whether the group's arms all hold the same weapon. Says nothing about
    // ammo: that gates the TOGGLE, not the grouping, so the button can show a
    // group as linkable while it is temporarily unavailable.
    public static bool IsGrouped(ModContext context, int group, string characterTag = Bay.CharacterTag)
    {
        if (group < 0 || group >= Groups.Length)
            return false;
        var slots = Bay.LoadoutOrNull(context, characterTag);
        if (slots == null)
            return false;
        string key = null;
        foreach (var slot in Groups[group])
        {
            var k = LinkKeyFor(Bay.ResolveItem(slots[slot]));
            if (k == null)
                return false;
            key ??= k;
            if (k != key)
                return false;
        }
        return key != null;
    }

    // Which groups are currently firing linked. MISSION-SCOPED and
    // deliberately not persisted: the toggle is a tactical decision, so it
    // starts every mission off and is cleared with the rest of the bay's
    // mission state.
    private static readonly HashSet<int> Linked = new();

    public static bool IsLinked(int group) => Linked.Contains(group);

    public static void Clear() => Linked.Clear();

    // Whether the player may flip the toggle right now. Linking is refused
    // while any arm in the group is DRY (metered and every one of its actives
    // empty), because a linked shot spends from all of them and a group that
    // cannot pay must not present a button that looks like it can. An arm out
    // of one kind of ammo but holding another is still a live arm. A group
    // whose linked variants failed to grant refuses too: the toggle would
    // otherwise hide arms and fire one bare, which looked like a linked shot
    // that cost one gun's ammo.
    public static bool CanToggle(ModContext context, IntPtr actorPtr, int group,
        string characterTag = Bay.CharacterTag)
    {
        if (!IsGrouped(context, group, characterTag))
            return false;
        if (IsLinked(group))
            return true; // unlinking is always allowed
        if (!BaySkillSystem.HasLinkedSkills(group))
            return false;
        foreach (var slot in Groups[group])
            if (BaySkillSystem.IsSlotDry(actorPtr, slot))
                return false;
        return true;
    }

    // Arming a group frees every slot it spans first, so the quad absorbs
    // linked pairs in one press and no slot ever fires in two groups at once.
    public static void SetLinked(int group, bool linked)
    {
        if (group < 0 || group >= Groups.Length)
            return;
        if (!linked)
        {
            Linked.Remove(group);
            return;
        }
        Linked.RemoveWhere(g => g != group && Groups[g].Any(s => Array.IndexOf(Groups[group], s) >= 0));
        Linked.Add(group);
    }

    // Drop any group whose arms can no longer all pay for a shot. Called
    // after every use so an arm running dry takes the mode down with it
    // rather than leaving a button that fires part of a volley. A broken quad
    // degrades into whichever pairs can still pay, so running one weapon dry
    // costs the volley width it actually costs and no more.
    public static int DropSpentGroups(IntPtr actorPtr)
    {
        var dropped = 0;
        for (var group = Groups.Length - 1; group >= 0; group--)
        {
            if (!IsLinked(group))
                continue;
            var dry = false;
            foreach (var slot in Groups[group])
                if (BaySkillSystem.IsSlotDry(actorPtr, slot))
                {
                    dry = true;
                    break;
                }
            if (!dry)
                continue;
            Linked.Remove(group);
            dropped++;
            if (group != QuadGroup)
                continue;
            for (var pair = 0; pair < QuadGroup; pair++)
            {
                if (!BaySkillSystem.HasLinkedSkills(pair))
                    continue;
                var pairDry = false;
                foreach (var slot in Groups[pair])
                    if (BaySkillSystem.IsSlotDry(actorPtr, slot))
                    {
                        pairDry = true;
                        break;
                    }
                if (!pairDry)
                    Linked.Add(pair);
            }
        }
        return dropped;
    }

    // ----- the linked weapon -------------------------------------------

    // Group -> the stat source a linked shot fires with. A synthetic Item is
    // needed whatever the ranks, because the skill bar draws a tile's buttons
    // from its ITEM's own skill list: the linked variants have to hang off
    // something other than the real weapons or they would appear on the
    // single tiles too. It is never added to inventory and exists only for
    // the mission.
    private static readonly Dictionary<int, Item> LinkedItems = new();

    // Source weapon id set -> the averaged template built for it.
    private static readonly Dictionary<string, WeaponTemplate> LinkedWeapons = new(StringComparer.Ordinal);

    public static Item LinkedItemFor(ModContext context, int group, string characterTag = Bay.CharacterTag)
    {
        if (LinkedItems.TryGetValue(group, out var cached) && cached != null)
            return cached;
        if (!IsGrouped(context, group, characterTag))
            return null;
        try
        {
            var slots = Bay.LoadoutOrNull(context, characterTag);
            var items = new List<Item>();
            foreach (var slot in Groups[group])
                items.Add(Bay.ResolveItem(slots[slot]));
            var weapon = LinkedWeaponFor(items);
            if (weapon == null)
                return null;
            // A guid of our own: it must not collide with a real owned item,
            // since ResolveItem and the bay slots both look items up by guid.
            var item = new Item(weapon.Cast<BaseItemTemplate>(), $"{LinkedGuidPrefix}{group}");
            LinkedItems[group] = item;
            return item;
        }
        catch
        {
            return null;
        }
    }

    // The weapon a linked group fires as: the shared template when every arm
    // sits at the same rank, or the AVERAGE across the arms otherwise.
    //
    // Averaging is what makes rank-insensitive linking honest. Firing n arms
    // once deals the sum of their damages; one skill firing n times at the
    // mean deals the same total. Taking the lowest rank would invent a
    // penalty and taking the highest would hand out a free upgrade. Armour
    // penetration is the one stat this only approximates, because it is
    // compared against armour rather than summed, but the mean is still
    // closer to the truth than any single arm. When the ranks match the
    // average is the identity, so the ordinary case is untouched.
    private static WeaponTemplate LinkedWeaponFor(IReadOnlyList<Item> items)
    {
        var weapons = new List<WeaponTemplate>();
        foreach (var item in items)
        {
            var w = Bay.WeaponOf(item);
            if (w == null)
                return null;
            weapons.Add(w);
        }
        if (weapons.Count == 0)
            return null;
        if (weapons.All(w => w.Pointer == weapons[0].Pointer))
            return weapons[0];
        var key = string.Join("|", weapons.Select(w => w.GetID()));
        if (LinkedWeapons.TryGetValue(key, out var cached) && cached != null)
            return cached;
        var clone = UnityEngine.Object.Instantiate(weapons[0]);
        clone.name = weapons[0].name;
        // GetID caches on FIRST call, which happens during Instantiate while
        // the clone is still named "X(Clone)". The rename above does not
        // refresh that cache, so it is stamped straight from the source or
        // every id-keyed gate (the imprint's ByWeapon) misses the clone.
        clone.m_ID = weapons[0].GetID();
        clone.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
        clone.Damage = weapons.Average(w => w.Damage);
        clone.ArmorPenetration = weapons.Average(w => w.ArmorPenetration);
        clone.DamageToArmorDurability = weapons.Average(w => w.DamageToArmorDurability);
        LinkedWeapons[key] = clone;
        return clone;
    }

    // Vanilla's twinfire recipe scaled to the group: Repetitions MULTIPLIED
    // by the arm count so every arm's shots land, RepetitionDelay DIVIDED by
    // it so the volley still takes the time a single burst took. AP cost,
    // uses and per-shot damage are left exactly alone, which is what makes
    // the link worth flipping.
    public static SkillTemplate LinkedSkillFor(SkillTemplate source, int arms)
    {
        if (source == null || arms < 2)
            return null;
        var cacheKey = (source.Pointer, arms);
        if (LinkedSkills.TryGetValue(cacheKey, out var cached) && cached != null)
            return cached;
        var clone = UnityEngine.Object.Instantiate(source);
        clone.name = source.name;
        // Same id-cache stamp as the weapon clone above: without it the
        // linked skill's GetID answered "...(Clone)", the imprint's BySkill
        // and Cheyanne's aim gates missed it, and a linked SSR fired without
        // its owner bonuses.
        clone.m_ID = source.GetID();
        // Instantiate re-deserialises the template from Odin bytes, which
        // drops RUNTIME-INJECTED managed state. The linked recipe only
        // touches template FIELDS (Repetitions, RepetitionDelay, the
        // deployment gates), so the source's own handler list and custom AoE
        // shape are carried across by reference: Cheyanne's ricochet is a
        // managed shape in CustomAoEShape, and its wiped copy left
        // UseCustomAoEShape true with a null shape, so the container add
        // NRE'd GetAoeRadius and her pair never got linked variants.
        clone.EventHandlers = source.EventHandlers;
        clone.CustomAoEShape = source.CustomAoEShape;
        clone.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
        clone.IsDeploymentRequired = false;
        clone.IsWeaponSetupRequired = false;
        clone.IsRemovedAfterCombat = true;
        // Repetitions is a narrow integer on the template, so the scaling is
        // done in int and cast back, with a floor of one shot per arm.
        clone.Repetitions = (ushort)Math.Min(ushort.MaxValue, Math.Max(arms, (int)source.Repetitions * arms));
        if (source.RepetitionDelay > 0f)
            clone.RepetitionDelay = source.RepetitionDelay / arms;
        // Vanilla only ever applies this recipe to skills that do NOT animate
        // per repetition (minigun salvo: a 0.03s engine-timed stream, the
        // animation played once). The tripod MGs animate per repetition at
        // their authored 0.1-0.16s cadence, and a linked stream at a quarter
        // of that waits on each ShootBurst re-trigger instead: bullets trail
        // the trigger pull and stretch with frame time. Below the slowest
        // per-rep cadence vanilla ships, the animation cannot pace the
        // stream, so the linked clone fires it engine-timed; slower linked
        // skills (a rocket per arm) keep their per-shot animation.
        if (clone.IsPlayingAnimationForEachRepetition && clone.RepetitionDelay < 0.1f)
            clone.IsPlayingAnimationForEachRepetition = false;
        LinkedSkills[cacheKey] = clone;
        return clone;
    }

    // The synthetic linked items are recognisable by guid, for gates that see
    // only an item (the imprint tooltip): they exist in no container, so a
    // wielder can never be read off them.
    public const string LinkedGuidPrefix = "wmgfl_link_";

    private static readonly Dictionary<(IntPtr Source, int Arms), SkillTemplate> LinkedSkills = new();

    // Mission teardown. The synthetic items go with the mission that built
    // them: a loadout edited between missions must not keep firing the group
    // it was built from. The template clones are assets and are kept, keyed
    // by source and arm count, so a repeated grouping reuses them.
    public static void ClearMissionState()
    {
        Linked.Clear();
        LinkedItems.Clear();
    }
}
