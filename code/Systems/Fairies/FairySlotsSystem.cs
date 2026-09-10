using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

public sealed class FairySlotsState
{
    public List<FairyInstallation> Slots { get; set; } = [];
}

public sealed class FairyInstallation
{
    public string UpgradeId { get; set; }
    public int SpentComponents { get; set; }
}

// MENACE fixes slot bounds at ten in its getters, equip methods and cost calculation
// (ShipUpgrades RVAs 0x5CB050, 0x5CC1B0, 0x5CB1A0). Save loading does not resize the spent-cost
// array (SaveState.ProcessIntArray, RVA 0x5B3640). Keep the campaign arrays intact and persist the fairy slots
// through mod state. A separate native ShipUpgrades instance supplies the same construction,
// refund and effect behaviour for these slots, with local indices 0 to 2.
public sealed class FairySlotsSystem : JiangyuSystem
{
    internal const int SlotCount = 3;
    internal const int FirstSlot = 10;
    internal const string SlotTemplateId = "ship_upgrade_slot.wmgfl_fairies";
    private const string ShipType = "Il2CppMenace.Strategy.ShipUpgrades";
    private const string ScreenType = "Il2CppMenace.UI.Strategy.ShipUpgradesUIScreen";

    // GetSlotTemplate searches registered templates by value. A mod-owned value keeps
    // fairy construction separate from Hull and the game's special Hidden upgrade type.
    private const ShipUpgradeType FairyType = (ShipUpgradeType)0x574D46;

    private ShipUpgradeSlotTemplate _slotTemplate;
    private readonly Dictionary<string, ShipUpgradeTemplate> _upgrades = new(StringComparer.Ordinal);
    private FairySlotsState _state;
    private ShipUpgrades _ships;

    public override void OnInit()
    {
        Context.Patches.Prefix(ShipType, "GetEquippedUpgrade", OnGetEquipped);
        Context.Patches.Prefix(ShipType, "GetSlotTemplateByIdx", OnGetSlotTemplate);
        Context.Patches.Prefix(ShipType, "TryEquipUpgrade", OnEquip);
        Context.Patches.Postfix(ShipType, "TryUnequipUpgrade", OnUnequip);
        Context.Patches.Prefix(ShipType, "GetFullShipUpgradeInstallCosts", OnGetCosts);
        Context.Patches.Postfix(ShipType, "GetInstallsCount", OnGetInstallsCount);
        Context.Patches.Postfix(ShipType, "GetEmptySlotCount", OnGetEmptySlotCount);
        Context.Patches.Postfix(ShipType, "GetFilledSlotCount", OnGetFilledSlotCount);
        Context.Patches.Postfix(ShipType, "GetSpentOciComponentsBySlotType", OnGetSpentComponents);
        Context.Patches.Postfix(ShipType, "ForEachActiveEffect", OnForEachActiveEffect);
        Context.Patches.Postfix(ScreenType, "OnOpened", OnScreenOpened);
    }

    public override void OnTemplatesApplied()
    {
        _slotTemplate = Templates.ById<ShipUpgradeSlotTemplate>(SlotTemplateId,
            message => Context.Log.Warn($"fairy slots: {message}"));
        _upgrades.Clear();
        _ships = null;
        if (_slotTemplate == null)
            return;
        _slotTemplate.UpgradeType = FairyType;
        foreach (var root in _slotTemplate.BasicUpgrades)
            RegisterTree(root);
    }

    private void RegisterTree(ShipUpgradeTemplate upgrade)
    {
        if (upgrade == null || !_upgrades.TryAdd(upgrade.GetID(), upgrade))
            return;
        upgrade.UpgradeType = FairyType;
        if (upgrade.ChildUpgrades != null)
            foreach (var child in upgrade.ChildUpgrades)
                RegisterTree(child);
    }

    private ShipUpgrades Ships()
    {
        if (_slotTemplate == null)
            return null;
        var state = Context.State.Get<FairySlotsState>();
        if (_ships != null && ReferenceEquals(_state, state))
            return _ships;

        _state = state;
        _ships = new ShipUpgrades();
        while (state.Slots.Count < SlotCount)
            state.Slots.Add(new FairyInstallation());
        for (var i = 0; i < SlotCount; i++)
        {
            _ships.m_SlotTemplates[i] = _slotTemplate;
            var saved = state.Slots[i];
            _ships.m_EquippedUpgrades[i] = saved.UpgradeId == null ? null : _upgrades.GetValueOrDefault(saved.UpgradeId);
            _ships.m_SpentOciComponentsBySlot[i] = saved.SpentComponents;
        }
        return _ships;
    }

    private void SaveSlots()
    {
        for (var i = 0; i < SlotCount; i++)
        {
            _state.Slots[i].UpgradeId = _ships.GetEquippedUpgrade(i)?.GetID();
            _state.Slots[i].SpentComponents = _ships.m_SpentOciComponentsBySlot[i];
        }
    }

    private static T As<T>(object value) where T : Il2CppObjectBase
        => (value as Il2CppObjectBase)?.TryCast<T>();

    private static bool IsFairySlot(int index) => index >= FirstSlot && index < FirstSlot + SlotCount;

    private static bool IsCampaign(PatchInfo info)
        => As<ShipUpgrades>(info.Instance) is { } ships && ships.Pointer == StrategyState.Get()?.ShipUpgrades?.Pointer;

    private void OnGetEquipped(PatchInfo info)
    {
        if (info.Args[0] is not int index || !IsFairySlot(index) || !IsCampaign(info))
            return;
        info.Skip = true;
        info.Result = Ships()?.GetEquippedUpgrade(index - FirstSlot);
    }

    private void OnGetSlotTemplate(PatchInfo info)
    {
        if (info.Args[0] is not int index || !IsFairySlot(index) || !IsCampaign(info))
            return;
        info.Skip = true;
        info.Result = _slotTemplate;
    }

    private void OnEquip(PatchInfo info)
    {
        if (!IsCampaign(info))
            return;
        var upgrade = As<ShipUpgradeTemplate>(info.Args[0]);
        var index = (int)info.Args[2];
        if (!IsFairySlot(index) && !(index == -1 && upgrade?.UpgradeType == FairyType))
            return;
        info.Skip = true;
        info.Result = false;
        var ships = Ships();
        if (ships == null || (upgrade != null && !_upgrades.ContainsKey(upgrade.GetID())))
            return;
        if (index == -1)
        {
            for (var i = 0; i < SlotCount; i++)
                if (ships.GetEquippedUpgrade(i) == null)
                {
                    index = FirstSlot + i;
                    break;
                }
            if (index == -1)
                return;
        }
        info.Result = ships.TryEquipUpgrade(upgrade, (int)info.Args[1], index - FirstSlot, (bool)info.Args[3]);
        SaveSlots();
    }

    private void OnUnequip(PatchInfo info)
    {
        if (info.Result is not false || !IsCampaign(info)
            || As<ShipUpgradeTemplate>(info.Args[0]) is not { } upgrade || upgrade.UpgradeType != FairyType
            || Ships() is not { } ships)
            return;
        info.Result = ships.TryUnequipUpgrade(upgrade);
        SaveSlots();
    }

    private void OnGetCosts(PatchInfo info)
    {
        if (info.Args[0] is not int index || !IsFairySlot(index) || !IsCampaign(info) || Ships() is not { } ships)
            return;
        // Jiangyu forwards Harmony's __args array, including the ref refund argument.
        var args = (object[])info.Args;
        info.Result = ships.GetFullShipUpgradeInstallCosts(index - FirstSlot, As<ShipUpgradeTemplate>(args[1]), out var refund);
        args[2] = refund;
        info.Skip = true;
    }

    private void OnGetInstallsCount(PatchInfo info)
    {
        if (!IsCampaign(info) || As<ShipUpgradeTemplate>(info.Args[0]) is not { } upgrade
            || upgrade.UpgradeType != FairyType || Ships() is not { } ships)
            return;
        info.Result = (int)info.Result + ships.GetInstallsCount(upgrade);
    }

    private void OnGetEmptySlotCount(PatchInfo info)
    {
        if (info.Args[0] is ShipUpgradeType type && type == FairyType && IsCampaign(info) && Ships() is { } ships)
            info.Result = ships.GetEmptySlotCount(FairyType);
    }

    private void OnGetFilledSlotCount(PatchInfo info)
    {
        if (info.Args[0] is ShipUpgradeType type && type == FairyType && IsCampaign(info) && Ships() is { } ships)
            info.Result = ships.GetFilledSlotCount(FairyType);
    }

    private void OnGetSpentComponents(PatchInfo info)
    {
        if (info.Args[0] is ShipUpgradeType type && type == FairyType && IsCampaign(info) && Ships() is { } ships)
            info.Result = ships.GetSpentOciComponentsBySlotType(FairyType);
    }

    private void OnForEachActiveEffect(PatchInfo info)
    {
        if (!IsCampaign(info) || Ships() is not { } ships
            || As<Il2CppSystem.Action<BaseGameEffect, Origin>>(info.Args[0]) is not { } action)
            return;
        // Calling ForEachActiveEffect on the second container would repeat the ship's global
        // effects. Enumerate its installed modules with the same origins used by OnAdd.
        for (var i = 0; i < SlotCount; i++)
            ships.GetEquippedUpgrade(i)?.ForEachEffect(action, _slotTemplate, i);
    }

    private void OnScreenOpened(PatchInfo info)
    {
        var screen = As<ShipUpgradesUIScreen>(info.Instance);
        var container = screen == null ? null : UI.Find(screen.GetRootElement(), UiSelector.Name("Slots"));
        if (container == null || Ships() is not { } ships)
            return;
        for (var i = 0; i < SlotCount; i++)
        {
            var name = $"wm-fairy-slot-{i}";
            var slot = UI.Find(container, UiSelector.Name(name))?.TryCast<ShipSlot>();
            if (slot == null)
            {
                slot = new ShipSlot { name = name, SlotIdx = FirstSlot + i };
                container.Add(slot);
                screen.m_ShipSlots.Add(slot);
                slot.SetOnLeftClickedAction(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<InteractiveElement>>(
                    (Action<InteractiveElement>)screen.OnSlotClickedAction));
            }
            var upgrade = ships.GetEquippedUpgrade(i);
            slot.Init(upgrade, _slotTemplate, FirstSlot + i);
            slot.SetSelected(upgrade != null);
        }
        // Use the native UXML's canvas coordinates so these mounts scale with the other slots.
        foreach (var slot in screen.m_ShipSlots)
        {
            switch (slot.SlotIdx)
            {
                case 4: Position(slot, 558f, 431.5f); break;
                case 5: Position(slot, 442f, 529.5f); break;
                case 7: Position(slot, 1059.84f, 329.76f); break;
                case 8: Position(slot, 1094f, 193f); break;
                case 9: Position(slot, 1080.5f, 66f); break;
                case FirstSlot: Position(slot, 866.5f, 71f); break;
                case FirstSlot + 1: Position(slot, 641.5f, 363.5f); break;
                case FirstSlot + 2: Position(slot, 838.4f, 356.4f); break;
            }
        }
    }

    private static void Position(VisualElement slot, float left, float top)
    {
        slot.style.position = UnityEngine.UIElements.Position.Absolute;
        slot.style.left = left;
        slot.style.top = top;
        slot.style.right = slot.style.bottom = new StyleLength(StyleKeyword.Auto);
        slot.style.translate = new StyleTranslate(StyleKeyword.None);
        slot.style.scale = new StyleScale(new Scale(new UnityEngine.Vector3(0.9f, 0.9f, 1f)));
    }
}
