using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Empties the MissionCharges store between missions.
//
// The charges themselves are pure KDL: a skill declaring a `WOMENACE:MissionCharge` handler gates
// on its own IsUsable, so nothing here patches the engine. The handler's own OnMissionStarted
// cannot own the reset: a skill granted BY a perk's OnMissionStart AddSkill arrives after that
// dispatch and would never see it.
public sealed class MissionChargeSystem : JiangyuSystem
{
    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        MissionCharges.ResetForMission();
    }
}
