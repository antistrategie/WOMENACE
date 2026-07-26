using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// A WOMENACE leader is never forced to bring squaddies: her minimum squad is one body (herself, zero
// squaddies), so the player may deploy her alone or fill the squad up to the max, as they choose.
//
// The lever is GetMinValidSquaddies, the "needs at least N squaddies" gate mission prep reads: floored
// to zero for our leaders, validation passes with an empty squad. Only the floor moves, so the max
// getter (and the optional five-body cap) still bound how many squaddies can be added. Lowering the
// floor never strips a squad the player has already filled: it only lifts the requirement.
//
// Composition with the other squad-size systems is clean because they all push the count the same way:
//   - SoloSquadSystem forces a solo doll's min AND max to zero (a fixed squad of one), so this floor is
//     a no-op there,
//   - DollSquadLimitSystem caps the max (and its own min-cap) at four squaddies when its option is on,
//     which sits above this floor of zero.
public sealed class DollSquadFloorSystem : JiangyuSystem
{
    public override void OnInit()
    {
        // The concrete SquadLeader override, never the BaseUnitLeader virtual (detouring that crashes
        // on boot). Same target and reasoning as SoloSquadSystem.
        Context.Patches.Postfix("Il2CppMenace.Strategy.SquadLeader", "GetMinValidSquaddies", OnGetMinValidSquaddies);
    }

    private void OnGetMinValidSquaddies(PatchInfo info)
    {
        try
        {
            if (info.Result is not int count || count <= 0)
                return;
            var leader = (info.Instance as Il2CppObjectBase)?.TryCast<BaseUnitLeader>();
            if (leader == null || Affinity.OurSpeakerTags(leader) == null)
                return;
            info.Result = 0;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"doll squad floor: min floor failed: {ex.Message}");
        }
    }
}
