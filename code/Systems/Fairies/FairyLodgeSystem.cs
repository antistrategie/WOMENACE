using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The Fairy Lodge's own passive. The vanilla tree roots all do something by themselves (the
// Garage repairs, the Quarters house), so the lodge keeps the Dolls company: while it is
// installed, every deployed Doll gains a little affinity after each mission, one legendary gift's
// worth. Granted through the same AffinityState the gift modal writes, so levels, unlock
// reconciliation and the badge all read the new total the next time they look.
public sealed class FairyLodgeSystem : JiangyuSystem
{
    internal const string LodgeId = "oci.wmgfl_fairy_lodge";
    private const int AffinityPerMission = 20;

    private ShipUpgradeTemplate _lodge;
    private IntPtr _lastGrantedResult;

    // The mission-result screen is the one moment the mission is over, the strategy state is
    // live, and the battle plan still names who sortied: the same delivery point the gift drops
    // use.
    public override void OnInit()
        => Context.Patches.Prefix("Il2CppMenace.UI.MissionResult.MissionResultUIScreen", "ShowMissionWindow", OnShowMissionResult);

    // Templates are re-registered whenever a campaign loads, so a pointer held across two
    // campaigns names a dead template and every install count reads zero.
    public override void OnTemplatesApplied()
        => _lodge = Templates.ById<ShipUpgradeTemplate>(LodgeId, msg => Context.Log.Warn($"fairy lodge: {msg}"));

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
            // a slot holds one module, so a fairy in a slot IS the lodge aboard: the whole
            // tree counts, not just the lodge's own tile
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
    internal static bool InstalledInTree(ShipUpgrades ships, ShipUpgradeTemplate node, int depth)
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

// The fairy tree's unlock rule, computed live: a fairy module is unlocked while the lodge tree
// is aboard (the lodge itself or any of its fairies holding a slot), and an enhanced tier while
// its own fairy or itself is installed. Kept out of the game's own unlock effects on purpose:
// those raise an unlock notification every time they activate, and one install activates every
// effect in the tree.
public sealed class FairyUnlockGateSystem : JiangyuSystem
{
    private ShipUpgradeTemplate _lodge;
    private readonly HashSet<IntPtr> _fairies = new();
    private readonly Dictionary<IntPtr, ShipUpgradeTemplate> _enhancedParents = new();

    private ShipUpgradeTechTreeDialog _dialog;

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Strategy.ShipUpgradeTemplate", "IsUnlocked", OnIsUnlocked);
        // A row draws its padlock in ShipUpgradeSlot.Init and nothing re-reads IsUnlocked while
        // the dialog stays open, so installing a parent leaves its children looking locked until
        // the dialog is reopened. Track the open dialog and repaint its rows on every install.
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.ShipUpgradeTechTreeDialog", "Init", OnDialogInit);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.ShipUpgradeTechTreeDialog", "OnClose", OnDialogClosed);
        Context.Patches.Postfix("Il2CppMenace.Strategy.ShipUpgrades", "TryEquipUpgrade", OnInstallChanged);
        Context.Patches.Postfix("Il2CppMenace.Strategy.ShipUpgrades", "TryUnequipUpgrade", OnInstallChanged);
    }

    private void OnDialogInit(PatchInfo info)
        => _dialog = (info.Instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<ShipUpgradeTechTreeDialog>();

    private void OnDialogClosed(PatchInfo info) => _dialog = null;

    private void OnInstallChanged(PatchInfo info)
    {
        try
        {
            var container = _dialog != null && _dialog.IsAlive() ? _dialog.m_Container : null;
            if (container == null)
                return;
            var repainted = 0;
            foreach (var element in UI.FindAll(container, UiSelector.TypeName("ShipUpgradeSlot")))
            {
                var slot = element?.TryCast<ShipUpgradeSlot>();
                var template = slot?.m_ShipUpgradeTemplate;
                if (template == null)
                    continue;
                var locked = !template.IsUnlocked();
                if (slot.m_IsLocked == locked)
                    continue;
                slot.m_IsLocked = locked;
                // The game's own lock visual, read straight off m_IsLocked.
                slot.ToggleLockedState();
                repainted++;
            }
            if (repainted > 0)
                Context.Log.Debug($"fairy gate: repainted {repainted} tech-tree row(s)");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"fairy gate: tech-tree repaint failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public override void OnTemplatesApplied()
    {
        try
        {
            _fairies.Clear();
            _enhancedParents.Clear();
            _lodge = Templates.ById<ShipUpgradeTemplate>(FairyLodgeSystem.LodgeId,
                msg => Context.Log.Warn($"fairy gate: {msg}"));
            var fairies = _lodge?.ChildUpgrades;
            for (var i = 0; i < (fairies?.Length ?? 0); i++)
            {
                var fairy = fairies[i];
                if (fairy == null)
                    continue;
                _fairies.Add(fairy.Pointer);
                var children = fairy.ChildUpgrades;
                for (var j = 0; j < (children?.Length ?? 0); j++)
                    if (children[j] != null)
                        _enhancedParents[children[j].Pointer] = fairy;
            }
            Context.Log.Debug($"fairy gate: gating {_fairies.Count} fairy module(s), {_enhancedParents.Count} enhanced tier(s)");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"fairy gate: setup failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnIsUnlocked(PatchInfo info)
    {
        try
        {
            // Runs for every module the tech tree asks about, so the two membership tests come
            // first and a non-fairy costs one hash lookup.
            var template = (info.Instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<ShipUpgradeTemplate>();
            if (template == null || _lodge == null)
                return;
            var isFairy = _fairies.Contains(template.Pointer);
            if (!isFairy && !_enhancedParents.TryGetValue(template.Pointer, out _))
                return;
            var ships = StrategyState.Get()?.ShipUpgrades;
            if (ships == null)
                return;
            if (isFairy)
                info.Result = FairyLodgeSystem.InstalledInTree(ships, _lodge, 0);
            else if (_enhancedParents.TryGetValue(template.Pointer, out var fairy))
                info.Result = ships.GetInstallsCount(fairy) > 0 || ships.GetInstallsCount(template) > 0;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"fairy gate: unlock check failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
