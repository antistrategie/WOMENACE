using System.Collections.Generic;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for setting up the strategy layer over the dev-loader bridge, e.g.
// {verb: "Nav.Hire", args: ["player_squad.koleda"], mutate: true}. *.Dev.cs keeps them out of releases.
[DevVerb]
public static class Nav
{
    // Hire a leader by template id (e.g. "player_squad.koleda") into the current roster.
    [MutatingVerb]
    public static object Hire(string templateId)
    {
        var roster = StrategyState.Get()?.Roster;
        var template = Templates.ById<UnitLeaderTemplate>(templateId);
        if (roster == null || template == null)
            return new { error = $"no roster or no template {templateId}" };
        var leader = roster.HireLeader(template);
        return new { ok = leader != null, hired = roster.m_HiredLeaders.Count };
    }

    // Put a leader on the list of who can be hired, by template id (e.g. "squad_leader.klukai_foot").
    [MutatingVerb]
    public static object Hirable(string templateId)
    {
        var roster = StrategyState.Get()?.Roster;
        var template = Templates.ById<UnitLeaderTemplate>(templateId);
        if (roster == null || template == null)
            return new { error = $"no roster or no template {templateId}" };
        roster.AddHirableLeader(template, true);
        return new { ok = true, hirable = roster.m_HirableLeaders.Count };
    }

    // Give the hired pilot at a roster index a new vehicle of this template (e.g. "vehicle.koleda_car")
    // and seat her in it, for previewing vehicles without a pilot who already has one.
    [MutatingVerb]
    public static object GiveVehicle(int index, string vehicleId)
    {
        var state = StrategyState.Get();
        var hired = state?.Roster?.m_HiredLeaders;
        if (hired == null || index < 0 || index >= hired.Count)
            return new { error = $"no hired leader at {index}" };
        var pilot = hired[index]?.TryCast<Pilot>();
        if (pilot == null)
            return new { error = $"leader {index} is not a pilot" };
        var template = Templates.ById<Il2CppMenace.Items.VehicleItemTemplate>(vehicleId);
        if (template == null)
            return new { error = $"no vehicle template {vehicleId}" };
        var added = state.OwnedItems.AddItem(template, false, false);
        var vehicle = added != null ? state.OwnedItems.GetVehicleByItemGuid(added.GetGuid()) : null;
        if (vehicle == null)
            return new { error = "vehicle not added" };
        // The squad panel reads the vehicle off the leader's items, which is also what adds the
        // vehicle's own weapon and module slots.
        var item = (added as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<Il2CppMenace.Items.Item>();
        var equipped = item != null && pilot.GetItems().Add(item, true);
        if (pilot.GetVehicle() == null)
            pilot.SetVehicle(vehicle);
        return new { ok = true, equipped, riding = pilot.GetVehicle() != null };
    }

    // Select the hired leader carrying the character tag (e.g. "wmgfl_sextans"), or with this leader
    // template id (e.g. "pilot.ivey"), or at a roster index ("#4"), on the active
    // screen, as clicking her roster slot would. On the armoury this drives the unit selector,
    // which also respawns the 3D squad preview. Elsewhere it binds the UnitWindow directly.
    [MutatingVerb]
    public static object SelectLeader(string characterTag)
    {
        var screen = UIManager.Get()?.GetActiveScreen();
        var leaders = StrategyState.Get()?.Roster?.m_HiredLeaders;
        for (var i = 0; leaders != null && i < leaders.Count; i++)
        {
            var leader = leaders[i];
            var byIndex = characterTag.StartsWith("#") && int.TryParse(characterTag.Substring(1), out var index) && index == i;
            if (leader == null || (!byIndex && Affinity.CharacterTag(leader) != characterTag && leader.GetTemplate()?.GetID() != characterTag))
                continue;

            var selector = screen?.TryCast<ArmoryUIScreen>()?.m_UnitSelector;
            if (selector != null)
            {
                selector.SetSelectedUnit(leader);
                return new { ok = true, via = "selector", leader = leader.GetTemplate()?.GetID() };
            }

            var root = screen?.GetRootElement();
            var window = root != null ? UI.Find(root, UiSelector.TypeName("UnitWindow"))?.TryCast<UnitWindow>() : null;
            if (window == null)
                return new { error = "no unit selector or unit window on the active screen" };
            window.SetLeader(leader);
            return new { ok = true, via = "window", leader = leader.GetTemplate()?.GetID() };
        }
        var ids = new List<string>();
        for (var i = 0; leaders != null && i < leaders.Count; i++)
            ids.Add(leaders[i]?.GetTemplate()?.GetID() ?? "?");
        return new { error = $"no hired leader tagged '{characterTag}'", hired = string.Join(", ", ids) };
    }

    // Apply a transmog selection through the picker's own Select path (what clicking an outfit
    // tile in the strip runs), against the active screen's UnitWindow.
    [MutatingVerb]
    public static object SetTransmog(string characterTag, string armorId)
    {
        var picker = TransmogPickerSystem.Instance;
        if (picker == null)
            return new { error = "transmog picker system not initialised" };
        return picker.DevSelect(characterTag, armorId)
            ? new { ok = true }
            : new { error = "no unit window on the active screen" };
    }
}
