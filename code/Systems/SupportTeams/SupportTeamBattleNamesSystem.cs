using Il2CppMenace.Tactical;
using Il2CppMenace.Tools;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Tactical;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The selected unit's panel in a mission names the leader in its header and lists her squaddies
// under it, one button each from a pool (m_SquaddieButtons), written in SelectedUnitPanel.SetActor
// from the leader's squaddie ids. A support team has no squaddies, so the list would be empty:
// it lists the whole crew instead, under the team's name in the header.
//
// A pool button keeps the tooltip and the click and hover actions vanilla gave it for whichever
// squaddie last used it. A crew member is no squaddie to describe or select, so the crew's buttons
// have all of them taken off.
//
// The hover tooltip over a unit on the map is headed by UnitActor.GetTitle, the squad template's
// Title, which for a team is its affiliation. While Actor.AppendTooltipData builds that tooltip a
// team's title reads as the team's name (its speaker's Forename) instead. Everywhere else the
// title stays the affiliation, as the unit panel shows it under the team's name.
public sealed class SupportTeamBattleNamesSystem : JiangyuSystem
{
    // The team actor whose tooltip is being built, or zero.
    private IntPtr _tooltipActor;

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.UI.Tactical.SelectedUnitPanel", "SetActor", OnSetActor);
        Context.Patches.Prefix("Il2CppMenace.Tactical.Actor", "AppendTooltipData", OnTooltipStart);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Actor", "AppendTooltipData", OnTooltipEnd);
        Context.Patches.Postfix("Il2CppMenace.Tactical.UnitActor", "GetTitle", OnGetTitle);
    }

    private void OnTooltipStart(PatchInfo info)
    {
        _tooltipActor = IntPtr.Zero;
        try
        {
            var actor = (info.Instance as Il2CppSystem.Object)?.TryCast<UnitActor>();
            if (actor != null && SupportTeams.Of(actor.GetLeader()) != null)
                _tooltipActor = actor.Pointer;
        }
        catch (Exception ex) { Context.Log.Warn($"support team: tooltip title failed: {ex.Message}"); }
    }

    private void OnTooltipEnd(PatchInfo info) => _tooltipActor = IntPtr.Zero;

    private void OnGetTitle(PatchInfo info)
    {
        if (_tooltipActor == IntPtr.Zero)
            return;
        try
        {
            var actor = (info.Instance as Il2CppSystem.Object)?.TryCast<UnitActor>();
            if (actor == null || actor.Pointer != _tooltipActor)
                return;
            var name = actor.GetSpeakerTemplate()?.Forename;
            if (name != null)
                info.Result = name;
        }
        catch (Exception ex) { Context.Log.Warn($"support team: tooltip title failed: {ex.Message}"); }
    }

    private void OnSetActor(PatchInfo info)
    {
        try
        {
            var panel = (info.Instance as Il2CppSystem.Object)?.TryCast<SelectedUnitPanel>();
            var team = SupportTeams.Of(panel?.m_Actor?.TryCast<UnitActor>()?.GetLeader());
            var buttons = panel?.m_SquaddieButtons;
            if (team == null || buttons == null)
                return;
            VisualElementExtensions.SetChildrenVisibility(buttons, false);
            for (var i = 0; i < team.CrewCount; i++)
            {
                var button = VisualElementExtensions.GetOrAddChildWithIdx<TextButton>(buttons, i);
                if (button == null)
                    continue;
                VisualElementExtensions.Show(button);
                button.SetText(team.Crew[i].Name, false);
                button.SetCreateTooltipFunc(null);
                button.SetOnLeftClickedAction(null);
                button.SetOnRightClickedAction(null);
                button.SetOnHoverStartAction(null);
                button.SetOnHoverEndAction(null);
            }
            if (panel.m_SquaddiesContainer != null)
                VisualElementExtensions.Show(panel.m_SquaddiesContainer);
        }
        catch (Exception ex) { Context.Log.Warn($"support team: battle crew names failed: {ex.Message}"); }
    }
}
