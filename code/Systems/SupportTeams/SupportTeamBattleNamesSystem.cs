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
// A pool button keeps the tooltip vanilla gave it for whichever squaddie last used it, and a crew
// member is no squaddie to describe, so the crew's buttons have theirs taken off.
public sealed class SupportTeamBattleNamesSystem : JiangyuSystem
{
    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.UI.Tactical.SelectedUnitPanel", "SetActor", OnSetActor);
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
                button.m_CreateTooltipFunc = null;
            }
            if (panel.m_SquaddiesContainer != null)
                VisualElementExtensions.Show(panel.m_SquaddiesContainer);
        }
        catch (Exception ex) { Context.Log.Warn($"support team: battle crew names failed: {ex.Message}"); }
    }
}
