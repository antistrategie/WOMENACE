using Jiangyu.Sdk;

namespace WOMENACE.Code;

// A support team is a squad of one: its single element is the whole crew, so it takes no
// squaddies. SquaddieCap holds a team at zero.
//
// SoloSquadSystem, DollSquadFloorSystem and DollSquadLimitSystem patch the same getters. Each of
// them only ever lowers the count, so the order they run in does not matter, and a team's 0 and 0
// follows the same rule.
public sealed class SupportTeamSquadSystem : JiangyuSystem
{
    private readonly SquaddieCap _cap = new("support team", leader => SupportTeams.IsTeam(leader) ? 0 : null);

    public override void OnInit() => _cap.Install(Context);
}
