using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// A module that grants an off-map ability says what kind it is, how long it takes, how long air
// power stays and how often it can be used, on one strip under its slot in the look of vanilla's
// panel there. Vanilla's panel lists these a line each, which would run the groups down over the
// ship. It is taken out of the layout, since one only made see-through still takes the pointer
// and shows its lines' tooltips over the room.
public sealed partial class StrategyDiorama
{
    private const string OciInfoName = "wm-oci-slot-info";
    private const string OciInfoClass = "wm-oci-slot-info";
    // The class vanilla gives each line of its panel, the first of which names the ability's kind.
    private const string OciInfoLineClass = "ship-slot-info-label";
    // The strip's height under a slot, in the shell's units, which its group makes room for.
    private const float OciInfoStrip = 18f;


    private void HookOciInfo() =>
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.ShipSlot", "UpdateInfoPanel", info =>
        {
            if (_oci.InfoRestoring || _shellRoot == null || !_shellRoot.ClassListContains(OciClass))
                return;
            if ((info.Instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<ShipSlot>() is not { } slot)
                return;
            // A module put in or taken out changes how much room its slot takes in its group.
            var had = OciInfoShown(slot);
            if (DressOciInfo(slot) != had)
                _oci.Unplaced = true;
        });

    // Read from the strip's own style, which DressOciInfo sets: the resolved style lags it until
    // the next layout, so a strip shown and hidden again before then would read as unchanged.
    private static bool OciInfoShown(ShipSlot slot) =>
        slot.m_InfoPanel?.parent is { } under && UI.Find(under, UiSelector.Name(OciInfoName)) is { } line
            && line.style.display.keyword == StyleKeyword.Undefined && line.style.display.value == DisplayStyle.Flex;

    // Whether the slot has a strip.
    private bool DressOciInfo(ShipSlot slot)
    {
        try
        {
            var panel = slot.m_InfoPanel;
            var under = panel?.parent;
            if (under == null)
                return false;
            var text = OciInfoText(slot, panel);
            Inline(panel, e => e.style.display = DisplayStyle.None, e =>
            {
                e.style.display = StyleKeyword.Null;
                _oci.InfoRestoring = true;
                try { slot.UpdateInfoPanel(); }
                finally { _oci.InfoRestoring = false; }
            });
            if (UI.Find(under, UiSelector.Name(OciInfoName))?.TryCast<Label>() is not { } line)
            {
                line = new Label { name = OciInfoName, pickingMode = PickingMode.Ignore };
                line.AddToClassList(OciInfoLineClass);
                line.AddToClassList(OciInfoClass);
                under.Insert(under.IndexOf(panel) + 1, line);
                var made = line;
                _shellUndo.Add(() => made.RemoveFromHierarchy());
            }
            var shown = !string.IsNullOrEmpty(text);
            line.text = text ?? "";
            line.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            return shown;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy UI: O.C.I. slot info failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    // The ability's figures from its instance in the operation, which carries what other modules
    // add to them, as vanilla's panel shows them. A fairy's uses come back every mission.
    private static string OciInfoText(ShipSlot slot, VisualElement panel)
    {
        var ability = OciGrantedAbility(slot.m_ShipUpgradeTemplate);
        if (ability == null)
            return null;
        var id = ability.GetID();
        Il2CppMenace.OffmapAbilities.OffmapAbilityInstance instance = null;
        var instances = StrategyState.Get()?.ActiveOffmapAbilities?.m_OffmapAbilityInstances;
        for (var i = 0; i < (instances?.Count ?? 0) && instance == null; i++)
            if (instances[i]?.TemplateId == id)
                instance = instances[i];

        var kind = panel != null ? UI.Find(panel, UiSelector.Class(OciInfoLineClass))?.TryCast<Label>()?.text : null;
        var parts = new List<string> { string.IsNullOrEmpty(kind) ? ability.OffmapAbilityType.ToString() : kind };
        var delay = instance?.GetDelayInRounds() ?? ability.DelayInRounds;
        if (delay > 0)
            parts.Add(Locale.Format("WOMENACE::ui/oci/delay", "Delay {0}", delay));
        // Only air power waits over the field to be called, as vanilla's panel says.
        var stays = instance?.GetAvailableDurationInRounds() ?? ability.AvailableDurationInRounds;
        if (ability.OffmapAbilityType == Il2CppMenace.OffmapAbilities.OffmapAbilityType.AirPower && stays > 0)
            parts.Add(Locale.Format("WOMENACE::ui/oci/loiter", "Loiter {0}", stays));
        if (instance != null && instance.GetMaxUses() > 0)
            parts.Add(FairyUsesSystem.RefillsEveryMission(id)
                ? Locale.Format("WOMENACE::ui/oci/uses_mission", "{0}/mission", instance.GetMaxUses())
                : Locale.Format("WOMENACE::ui/oci/uses_operation", "{0}/op", instance.GetMaxUses()));
        return string.Join(" · ", parts);
    }

    private static Il2CppMenace.OffmapAbilities.OffmapAbilityTemplate OciGrantedAbility(ShipUpgradeTemplate upgrade)
    {
        var effects = upgrade?.Effects;
        for (var i = 0; i < (effects?.Length ?? 0); i++)
            if (effects[i]?.TryCast<GrantOffmapAbilityEffect>()?.OffmapAbility is { } ability)
                return ability;
        return null;
    }
}
