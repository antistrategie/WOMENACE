using AccessTools = HarmonyLib.AccessTools;
using HarmonyMethod = HarmonyLib.HarmonyMethod;
using HarmonyPatcher = HarmonyLib.Harmony;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using TemplateList = Il2CppSystem.Collections.Generic.List<Il2CppMenace.Items.ItemTemplate>;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// A support team's equipment is its own: the weapon and the outfit it is hired with stay on it.
//
// The unit window hides the armour box and the "Unequip Squad" button for a team, and the weapon
// box shows the launcher but opens no list. BayEquipUISystem hides the special-weapon box, as it
// does for Sextans, so the two systems never write the same element.
//
// A hidden box is not a locked one. The jy_*_restricted tags only empty the alternatives lists,
// while a right click, "Unequip Squad" and "Unequip All Squads" take the weapon and the outfit
// off, and the outfit could then be worn by anyone. So the removals themselves are refused at
// the item container for a team's own items, whichever control asked for them, and the slot
// clicks on a team's weapon and armour do nothing.
//
// The slots a team can open (its accessory slot) list only the items made for support teams: an
// item tagged wmgfl_support_item, or one for this team alone, tagged wmgfl_team_<name> or naming
// it in OnlyEquipableBy as the team's own weapon and outfit do. Jiangyu's restriction tags cover
// no accessory slot, and vanilla's OnlyEquipableBy works the other way (an item naming who may
// take it), so the list is cut here. UnitWindowEquipment.UpdateEquipmentAlternatives builds it
// through SortedFilteredItemList.GetSortedAndFilteredItems, and while it does so for a team, that
// call's result loses every item the team does not accept. The team is cleared in a finalizer, which
// runs however UpdateEquipmentAlternatives ends, so a list that throws never leaves the cut on for
// the lists other screens build after it.
public sealed class SupportTeamEquipSystem : JiangyuSystem
{
    // Unequip buttons this system hid, so only those are shown again for another leader. Vanilla
    // decides the button's own state for everyone else.
    private readonly HashSet<IntPtr> _hiddenButtons = new();

    private static SupportTeamEquipSystem Instance;
    // The patches the SDK cannot place: overloads it cannot tell apart, and a finalizer.
    private HarmonyPatcher _harmony;
    // The team whose slot list is being built, for the length of UpdateEquipmentAlternatives.
    private SupportTeams.Team _listingFor;

    public override void OnInit()
    {
        Instance = this;
        Context.Patches.Prefix("Il2CppMenace.Items.ItemContainer", "TryUnequip", OnTryUnequip);
        // ItemContainer.Remove has two overloads of two parameters each, which the SDK cannot tell
        // apart by name and count, so both are patched here by their parameter types.
        _harmony = new HarmonyPatcher("WOMENACE.SupportTeamEquipSystem");
        _harmony.Patch(RemoveOverload(typeof(Item), typeof(ItemEquipFlags)),
            prefix: new HarmonyMethod(typeof(SupportTeamEquipSystem), nameof(OnRemoveItem)));
        _harmony.Patch(RemoveOverload(typeof(ItemSlot), typeof(int)),
            prefix: new HarmonyMethod(typeof(SupportTeamEquipSystem), nameof(OnRemoveAt)));
        // The sorted list has a second two-parameter overload (of BaseItem), so this one is
        // patched by its parameter types too.
        _harmony.Patch(AccessTools.DeclaredMethod(typeof(SortedFilteredItemList), nameof(SortedFilteredItemList.GetSortedAndFilteredItems),
                new[] { typeof(TemplateList).MakeByRefType(), typeof(TemplateList) })
                ?? throw new MissingMethodException(typeof(SortedFilteredItemList).FullName, nameof(SortedFilteredItemList.GetSortedAndFilteredItems)),
            postfix: new HarmonyMethod(typeof(SupportTeamEquipSystem), nameof(OnSortedList)));
        _harmony.Patch(AccessTools.DeclaredMethod(typeof(UnitWindowEquipment), nameof(UnitWindowEquipment.UpdateEquipmentAlternatives))
                ?? throw new MissingMethodException(typeof(UnitWindowEquipment).FullName, nameof(UnitWindowEquipment.UpdateEquipmentAlternatives)),
            prefix: new HarmonyMethod(typeof(SupportTeamEquipSystem), nameof(OnListStart)),
            finalizer: new HarmonyMethod(typeof(SupportTeamEquipSystem), nameof(OnListEnd)));
        Context.Patches.Prefix("Il2CppMenace.Items.ItemContainer", "UnequipAll", OnUnequipAll);
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.UnitWindowEquipment", "OnEquipmentSlotRightClick", OnSlotClick);
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.UnitWindowEquipment", "OnEquipmentSlotLeftClick", OnSlotClick);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindowEquipment", "UpdateButtons", OnUpdateButtons);
        Context.Patches.Postfix(VanillaUi.Targets.UnitWindowSetLeader.Type, VanillaUi.Targets.UnitWindowSetLeader.Method, OnWindowChanged);
        Context.Patches.Postfix(VanillaUi.Targets.UnitWindowRefresh.Type, VanillaUi.Targets.UnitWindowRefresh.Method, OnWindowChanged);
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName) => _hiddenButtons.Clear();

    public override void OnUnload()
    {
        try { _harmony?.UnpatchSelf(); }
        finally
        {
            _harmony = null;
            if (Instance == this)
                Instance = null;
        }
    }

    private static System.Reflection.MethodInfo RemoveOverload(params Type[] parameters)
        => AccessTools.DeclaredMethod(typeof(ItemContainer), nameof(ItemContainer.Remove), parameters)
           ?? throw new MissingMethodException(typeof(ItemContainer).FullName, nameof(ItemContainer.Remove));

    // Set on every list, so a list for anyone else is never cut.
    private static void OnListStart(UnitWindowEquipment __instance)
    {
        if (Instance != null)
            Instance._listingFor = SupportTeams.Of(__instance?.m_Leader);
    }

    // A finalizer that takes no exception leaves any exception to go on as it was.
    private static void OnListEnd()
    {
        if (Instance != null)
            Instance._listingFor = null;
    }

    private static void OnSortedList(TemplateList __1)
    {
        var team = Instance?._listingFor;
        if (team == null || __1 == null)
            return;
        try
        {
            var removed = 0;
            for (var i = __1.Count - 1; i >= 0; i--)
            {
                if (team.Accepts(__1[i]))
                    continue;
                __1.RemoveAt(i);
                removed++;
            }
            if (removed > 0)
                Instance.Context.Log.Debug($"support team: '{team.Name}' slot list left out {removed} item(s) not made for teams");
        }
        catch (Exception ex) { Instance.Context.Log.Warn($"support team: slot list filter failed: {ex.Message}"); }
    }

    private void OnTryUnequip(PatchInfo info)
    {
        if (info.Args is { Count: > 0 } && SupportTeams.IsLocked((info.Instance as Il2CppObjectBase)?.TryCast<ItemContainer>(), AsItem(info.Args[0])))
            Refuse(info, "unequip");
    }

    // Harmony prefixes: returning false skips the removal and leaves its result false.
    private static bool OnRemoveItem(ItemContainer __instance, Item __0, ref bool __result) => Allow(__instance, __0, ref __result);

    private static bool OnRemoveAt(ItemContainer __instance, ItemSlot __0, int __1, ref bool __result)
        => Allow(__instance, ItemAt(__instance, __0, __1), ref __result);

    private static bool Allow(ItemContainer container, Item item, ref bool result)
    {
        try
        {
            if (!SupportTeams.IsLocked(container, item))
                return true;
            result = false;
            Instance?.Context.Log.Debug("support team: refused to remove a team's own item");
            return false;
        }
        catch (Exception ex)
        {
            Instance?.Context.Log.Warn($"support team: remove guard failed: {ex.Message}");
            return true;
        }
    }

    // A team's container gives up everything but its own weapon and outfit.
    private void OnUnequipAll(PatchInfo info)
    {
        try
        {
            var container = (info.Instance as Il2CppObjectBase)?.TryCast<ItemContainer>();
            var all = container?.GetAllItems();
            if (all == null)
                return;
            var others = new List<Item>();
            var locked = false;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] is not { } item)
                    continue;
                if (SupportTeams.IsLocked(container, item))
                    locked = true;
                else
                    others.Add(item);
            }
            if (!locked)
                return;
            info.Skip = true;
            foreach (var item in others)
                container.TryUnequip(item);
            Context.Log.Debug($"support team: unequipped {others.Count} item(s), kept the team's own");
        }
        catch (Exception ex) { Context.Log.Warn($"support team: unequip-all guard failed: {ex.Message}"); }
    }

    private void Refuse(PatchInfo info, string what)
    {
        info.Skip = true;
        info.Result = false;
        Context.Log.Debug($"support team: refused to {what} a team's own item");
    }

    private static Item AsItem(object arg) => (arg as Il2CppObjectBase)?.TryCast<Item>();

    private static Item ItemAt(ItemContainer container, ItemSlot slot, int index)
    {
        var lists = container?.m_Items;
        var s = (int)slot;
        if (lists == null || s < 0 || s >= lists.Length)
            return null;
        var list = lists[s];
        return list != null && index >= 0 && index < list.Count ? list[index] : null;
    }

    // A team's weapon slot shows the launcher and opens nothing, and its armour slot is hidden.
    private void OnSlotClick(PatchInfo info)
    {
        try
        {
            var equipment = (info.Instance as Il2CppObjectBase)?.TryCast<UnitWindowEquipment>();
            if (equipment == null || !SupportTeams.IsTeam(equipment.m_Leader))
                return;
            var slot = (info.Args is { Count: > 0 } ? info.Args[0] as Il2CppObjectBase : null)?.TryCast<EquipmentSlot>();
            var type = slot?.GetItemSlotType();
            if (type is ItemSlot.InfantryWeapon or ItemSlot.InfantryArmor or ItemSlot.InfantrySpecial)
                info.Skip = true;
        }
        catch (Exception ex) { Context.Log.Warn($"support team: slot click guard failed: {ex.Message}"); }
    }

    private void OnUpdateButtons(PatchInfo info)
        => UpdateUnequipButton((info.Instance as Il2CppObjectBase)?.TryCast<UnitWindowEquipment>());

    private void UpdateUnequipButton(UnitWindowEquipment equipment)
    {
        try
        {
            var button = equipment?.m_UnequipButton;
            if (button == null)
                return;
            if (SupportTeams.IsTeam(equipment.m_Leader))
            {
                button.style.display = DisplayStyle.None;
                _hiddenButtons.Add(button.Pointer);
            }
            else if (_hiddenButtons.Remove(button.Pointer))
                button.style.display = DisplayStyle.Flex;
        }
        catch (Exception ex) { Context.Log.Warn($"support team: unequip button update failed: {ex.Message}"); }
    }

    // The window is reused across leaders, so everyone else gets the armour box back.
    private void OnWindowChanged(PatchInfo info)
    {
        try
        {
            if (info.Instance is not VisualElement window)
                return;
            var team = SupportTeams.IsTeam(Affinity.LeaderOf(window));
            foreach (var element in UI.FindAll(window, UiSelector.TypeName("EquipmentSlot")))
            {
                var slot = element.TryCast<EquipmentSlot>();
                if (slot == null || slot.GetItemSlotType() != ItemSlot.InfantryArmor || AppearanceSlotUi.InsideAlternatives(element, window))
                    continue;
                element.SetVisible(!team);
            }
            if (window.TryCast<UnitWindow>()?.m_Equipment is { } equipment)
                UpdateUnequipButton(equipment);
        }
        catch (Exception ex) { Context.Log.Warn($"support team: window update failed: {ex.Message}"); }
    }
}
