using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Jiangyu.Sdk;
using System.Text.Json;

namespace WOMENACE.Code;

// Dev verbs for the O.C.I. economy, so ship upgrade work (the Fairy Lodge tree) can be tested
// without grinding missions. Invoked over the dev-loader bridge as {verb: "Oci.Grant", mutate:
// true}. The bridge and verb runner live only in the dev loader, so these are unreachable in a
// shipped mod, and the *.Dev.cs name keeps them out of release builds.
[DevVerb]
public static class Oci
{
    public static string Status()
    {
        var ships = StrategyState.Get()?.ShipUpgrades;
        if (ships == null)
            return "No campaign loaded.";
        var slots = new List<object>();
        for (var i = 0; i < FairySlotsSystem.FirstSlot + FairySlotsSystem.SlotCount; i++)
        {
            var template = ships.GetSlotTemplateByIdx(i);
            slots.Add(new { index = i, category = template?.GetID(), upgrade = ships.GetEquippedUpgrade(i)?.GetID() });
        }
        return JsonSerializer.Serialize(slots);
    }

    public static string Quote(int slot, string upgradeId = "oci.wmgfl_fairy_lodge")
    {
        var ships = StrategyState.Get()?.ShipUpgrades;
        var upgrade = Templates.ById<ShipUpgradeTemplate>(upgradeId);
        if (ships == null || upgrade == null)
            return "Campaign or upgrade unavailable.";
        var cost = ships.GetFullShipUpgradeInstallCosts(slot, upgrade, out var refund);
        return JsonSerializer.Serialize(new { cost, refund, unlocked = upgrade.IsUnlocked(), installs = ships.GetInstallsCount(upgrade) });
    }

    // Presses a module slot on the O.C.I. screen, as a click on it does, which opens its tree.
    [MutatingVerb]
    public static string OpenSlot(int slot)
    {
        var screen = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.TryCast<Il2CppMenace.UI.Strategy.ShipUpgradesUIScreen>();
        if (screen == null)
            return "O.C.I. is not the active screen.";
        foreach (var held in screen.m_ShipSlots)
            if (held.SlotIdx == slot)
            {
                screen.OnSlotClickedAction(held);
                return $"pressed slot {slot}";
            }
        return $"no slot {slot}";
    }

    // What the tooltips on the O.C.I. screen say: a module slot's, and each off-map ability
    // icon's in the ship's panel. Built into an element of its own and read back as text.
    public static string Tooltips(int slot)
    {
        var screen = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.TryCast<Il2CppMenace.UI.Strategy.ShipUpgradesUIScreen>();
        if (screen == null)
            return "O.C.I. is not the active screen.";
        var said = new List<string>();
        void Read(string what, Il2CppMenace.UI.TooltipData data)
        {
            if (data == null)
            {
                said.Add($"{what}: no tooltip");
                return;
            }
            var built = new UnityEngine.UIElements.VisualElement();
            data.Build(built);
            var texts = new List<string>();
            void Walk(UnityEngine.UIElements.VisualElement element)
            {
                if (element.TryCast<UnityEngine.UIElements.TextElement>() is { } text && !string.IsNullOrWhiteSpace(text.text))
                    texts.Add(text.text);
                for (var i = 0; i < element.childCount; i++)
                    Walk(element[i]);
            }
            Walk(built);
            said.Add($"{what}: {string.Join(" | ", texts)}");
        }
        foreach (var held in screen.m_ShipSlots)
            if (held.SlotIdx == slot)
            {
                Read("slot", held.GetTooltipData());
                Read("upgrade slot", held.m_ShipUpgradeSlot?.GetTooltipData());
            }
        var abilities = screen.m_OffmapAbilitiesContainer;
        void Icons(UnityEngine.UIElements.VisualElement element)
        {
            if (element.TryCast<Il2CppMenace.UI.InteractiveElement>() is { } icon && icon.HasTooltip())
                Read($"ability {element.GetIl2CppType().Name}", icon.GetTooltipData());
            for (var i = 0; i < element.childCount; i++)
                Icons(element[i]);
        }
        if (abilities != null)
            Icons(abilities);
        return string.Join("\n", said);
    }

    // Installs a ship upgrade for free in the first slot that takes it, unlocked or not, so a
    // screen that shows what the upgrade grants (an off-map ability in mission preparation) can
    // be looked at.
    [MutatingVerb]
    public static string Install(string upgradeId)
    {
        var ships = StrategyState.Get()?.ShipUpgrades;
        var upgrade = Templates.ById<ShipUpgradeTemplate>(upgradeId);
        if (ships == null || upgrade == null)
            return "Campaign or upgrade unavailable.";
        for (var slot = 0; slot < ships.m_EquippedUpgrades.Length; slot++)
            if (ships.GetEquippedUpgrade(slot) == null && ships.TryEquipUpgrade(upgrade, 0, slot, false))
                return $"installed {upgradeId} in slot {slot}";
        return $"no slot took {upgradeId}";
    }

    // Add O.C.I. components through the game's own currency path, the same ChangeVar the campaign
    // rewards use, so the resource display and any listeners update normally. A negative amount
    // takes components away.
    [MutatingVerb]
    public static object Grant(int amount = 500)
    {
        var state = StrategyState.Get();
        if (state == null)
            return new { error = "no strategy state (load a campaign save first)" };

        state.ChangeVar(StrategyVars.OciComponents, amount, true, true, false);
        return new { ok = true, granted = amount, balance = state.GetVar(StrategyVars.OciComponents) };
    }
}
