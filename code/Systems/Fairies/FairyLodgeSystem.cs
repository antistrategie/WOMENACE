using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Jiangyu.Game;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The Fairy Lodge's own passive. The vanilla tree roots all do something by themselves (the
// Garage repairs, the Quarters house), so the lodge keeps the Dolls company: while it is
// installed, every deployed Doll gains a little affinity after each mission, one legendary gift's
// worth. Granted through the same AffinityState the gift modal writes, so levels, unlock
// reconciliation and the badge all read the new total the next time they look.
public sealed class FairyLodgeSystem : JiangyuSystem
{
    private const string LodgeId = "oci.wmgfl_fairy_lodge";
    private const int AffinityPerMission = 20;

    private ShipUpgradeTemplate _lodge;
    private IntPtr _lastGrantedResult;

    // The mission-result screen is the one moment the mission is over, the strategy state is
    // live, and the battle plan still names who sortied: the same delivery point the gift drops
    // use.
    public override void OnInit()
        => Context.Patches.Prefix("Il2CppMenace.UI.MissionResult.MissionResultUIScreen", "ShowMissionWindow", OnShowMissionResult);

    private void OnShowMissionResult(PatchInfo info)
    {
        try
        {
            var state = StrategyState.Get();
            var result = state?.GetLastMissionResult();
            if (state == null || result == null || !result.IsAlive())
                return;
            // one grant per result: the window can rebuild for the same mission, and the guard
            // keys on the result object it rebuilds around
            if (result.Pointer == _lastGrantedResult)
                return;
            _lodge ??= Templates.ById<ShipUpgradeTemplate>(LodgeId);
            // fairy modules carry the lodge as an AdditionalShipUpgradeEffectGiver, so a
            // slot holding any module of the lodge's tree counts as the lodge being aboard
            if (_lodge == null || state.ShipUpgrades == null || !InstalledInTree(state.ShipUpgrades, _lodge, 0))
                return;
            _lastGrantedResult = result.Pointer;

            var deployed = state.BattlePlan?.m_EntitiesToDeploy;
            if (deployed == null || deployed.Count == 0)
            {
                Context.Log.Debug("fairy lodge: no deployed entities at result time, no affinity granted");
                return;
            }
            var affinity = Context.State.Get<AffinityState>();
            var granted = 0;
            for (var i = 0; i < deployed.Count; i++)
            {
                var leader = deployed[i]?.GetUnitLeader();
                var tag = Affinity.CharacterTag(leader);
                if (tag == null)
                    continue;
                affinity.ForLeader(Affinity.KeyFor(leader)).Affinity += AffinityPerMission;
                granted++;
                Context.Log.Debug($"fairy lodge: +{AffinityPerMission} affinity for {tag}");
            }
            if (granted > 0)
                Context.Log.Info($"fairy lodge: {granted} deployed Doll(s) gained +{AffinityPerMission} affinity");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"fairy lodge: affinity trickle failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Is the node or any descendant of its ChildUpgrades tree installed in a slot? The
    // depth cap is a cycle guard, the tree is two levels deep today.
    private static bool InstalledInTree(ShipUpgrades ships, ShipUpgradeTemplate node, int depth)
    {
        if (node == null || depth > 4)
            return false;
        if (ships.GetInstallsCount(node) > 0)
            return true;
        var children = node.ChildUpgrades;
        for (var i = 0; i < (children?.Length ?? 0); i++)
            if (InstalledInTree(ships, children[i], depth + 1))
                return true;
        return false;
    }
}
