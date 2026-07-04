using Il2CppInterop.Runtime;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Restores Sextans' custom animations on the armoury preview model.
//
// The armoury screen instantiates the same armour model prefab as tactical
// (so her custom controller arrives on it), but ArmoryElement.Create then
// unconditionally replaces every infantry animator's controller with the
// vanilla soldier controller serialized on the armoury scene
// (ArmoryController.OverrideSoldierController). EntityTemplate's
// OverrideAnimatorController is never consulted on this path, which is why
// she posed rifle-ready there while animating correctly in missions. The
// postfix puts her own controller back and re-raises the squad-leader flag
// (a controller swap resets animator parameters, and the leader idle is
// gated on IsSquadLeader).
public sealed class SextansArmoryPreviewSystem : JiangyuSystem
{
    private const string EntityTemplateId = "player_squad.sextans";
    private const string ControllerName = "sextans";

    private RuntimeAnimatorController _controller;

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppUI.PrefabControllers.ArmoryElement", "Create", OnArmoryElementCreate);
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        _controller = null; // loaded-asset set changes per scene
    }

    private void OnArmoryElementCreate(PatchInfo info)
    {
        try
        {
            if (info.Instance is not Il2CppUI.PrefabControllers.ArmoryElement element)
                return;
            if (info.Args.Count < 2
                || info.Args[0] is not Il2CppMenace.Strategy.BaseUnitLeader leader)
                return;
            var template = leader.GetTemplate();
            var id = template?.GetID() ?? template?.name;
            if (!string.Equals(id, EntityTemplateId, System.StringComparison.Ordinal))
                return;

            var animator = element.GetComponent<Animator>();
            if (animator == null)
                return;
            var controller = FindController();
            if (controller == null)
            {
                Context.Log.Warn("sextans armoury: controller asset not found in loaded assets");
                return;
            }

            animator.runtimeAnimatorController = controller;
            // the swap resets parameters: re-raise the leader flag that gates
            // the squad-leader idle
            var elementIndex = info.Args[1] is int idx ? idx : 0;
            animator.SetBool("IsSquadLeader", elementIndex == 0);
            Context.Log.Debug("sextans armoury: custom controller restored on preview model");
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"sextans armoury patch failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private RuntimeAnimatorController FindController()
    {
        if (_controller != null)
            return _controller;
        foreach (var obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<RuntimeAnimatorController>()))
        {
            var controller = obj.TryCast<RuntimeAnimatorController>();
            if (controller != null && controller.name == ControllerName)
            {
                _controller = controller;
                break;
            }
        }
        return _controller;
    }
}
