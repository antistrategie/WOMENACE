using Il2CppInterop.Runtime;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Puts a squad's own animator controller back on its armoury preview.
//
// ArmoryElement.Create gives every infantry preview vanilla's soldier controller, serialised on
// the armoury scene (ArmoryController.OverrideSoldierController), and never reads the squad's own.
// A body that brings a controller of its own loses it there while it plays in missions:
// - a support team (SupportTeams), whose body is a generic rig of the whole crew that no human
//   clip moves, so the crew stands frozen. Its controller is the one on its registered body.
// - Sextans, whose leader idle and melee stance are her own. Her controller is found by name,
//   since her body is whichever outfit she wears.
// A controller swap resets the animator's parameters, so the squad-leader flag that gates a
// leader's idle is raised again where the controller has one.
public sealed class ArmoryControllerSystem : JiangyuSystem
{
    private const string SextansSquadId = "player_squad.sextans";
    private const string SextansController = "sextans";
    private const string LeaderParameter = "IsSquadLeader";

    // Found once a scene: the loaded assets change with it.
    private RuntimeAnimatorController _sextans;

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppUI.PrefabControllers.ArmoryElement", "Create", OnArmoryElementCreate);
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName) => _sextans = null;

    // Args: BaseUnitLeader, elementIndex, ...
    private void OnArmoryElementCreate(PatchInfo info)
    {
        try
        {
            if (info.Instance is not Il2CppUI.PrefabControllers.ArmoryElement element
                || info.Args is not { Count: > 0 } || info.Args[0] is not Il2CppMenace.Strategy.BaseUnitLeader leader)
                return;
            var controller = ControllerFor(leader, out var owner);
            if (controller == null)
                return;
            var animator = element.GetComponentInChildren<Animator>(true);
            if (animator == null)
                return;
            animator.runtimeAnimatorController = controller;
            foreach (var parameter in animator.parameters)
                if (parameter.name == LeaderParameter)
                {
                    animator.SetBool(LeaderParameter, info.Args.Count > 1 && info.Args[1] is int index && index == 0);
                    break;
                }
            Context.Log.Debug($"armoury: '{owner}' preview plays its own controller");
        }
        catch (Exception ex) { Context.Log.Warn($"armoury: controller restore failed: {ex.Message}"); }
    }

    private RuntimeAnimatorController ControllerFor(Il2CppMenace.Strategy.BaseUnitLeader leader, out string owner)
    {
        var team = SupportTeams.Of(leader);
        if (team != null)
        {
            owner = team.Name;
            return Context.Assets.Load<GameObject>(team.BodyAsset)?.GetComponentInChildren<Animator>(true)?.runtimeAnimatorController;
        }
        var template = leader.GetTemplate();
        owner = template?.GetID() ?? template?.name;
        return owner == SextansSquadId ? Sextans() : null;
    }

    private RuntimeAnimatorController Sextans()
    {
        if (_sextans != null)
            return _sextans;
        foreach (var obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<RuntimeAnimatorController>()))
        {
            var controller = obj.TryCast<RuntimeAnimatorController>();
            if (controller != null && controller.name == SextansController)
                return _sextans = controller;
        }
        Context.Log.Warn("armoury: Sextans' controller is not among the loaded assets");
        return null;
    }
}
