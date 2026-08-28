using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Keeps OTs-14's weapon-bay arms breathing on the armoury preview model.
//
// The arms prefab (weapon.ots14's ModelSecondary on Back_Special) carries its
// own Animator whose single looping state plays the breathing sway.
// ArmoryElement.Create replaces preview animator controllers with the vanilla
// soldier controller (the Sextans armoury lesson), which silences the sway:
// the stance survives because it is baked into the prefab's rest pose, but
// the arms stand rock still. The postfix hands the attachment its authored
// controller back, read straight off the ModelSecondary prefab asset, so no
// loaded-asset name search can grab another weapon's controller (BakeWeapon
// names them all "weapon").
public sealed class Ots14ArmoryPreviewSystem : JiangyuSystem
{
    private const string WeaponId = "weapon.ots14";

    // A mesh node unique to the arms prefab: its presence identifies an
    // OTs-14 preview, and the nearest ancestor Animator is the attachment's.
    private const string ArmMeshNode = "c_OTs14SSR01_Arm_slg_1_lod0";

    private readonly Dictionary<string, WeaponTemplate> _weapons = new(StringComparer.Ordinal);

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppUI.PrefabControllers.ArmoryElement", "Create", OnArmoryElement);
        Context.Patches.Postfix("Il2CppUI.PrefabControllers.ArmoryElement", "RefreshAttachments", OnArmoryElement);
    }

    private void OnArmoryElement(PatchInfo info)
    {
        try
        {
            var element = (info.Instance as Il2CppObjectBase)?.TryCast<Il2CppUI.PrefabControllers.ArmoryElement>();
            if (element == null)
                return;
            var armNode = SceneQuery.FindNamed(element.transform, ArmMeshNode);
            if (armNode == null)
                return; // not an OTs-14 preview

            // A manual parent walk: the generic GetComponentInParent overload
            // JITs an interop Cast<T> instantiation that throws
            // VerificationException at runtime, which silently killed this
            // restore on every preview refresh.
            Animator animator = null;
            for (var node = armNode; node != null && node.gameObject != element.gameObject; node = node.parent)
            {
                animator = node.GetComponent<Animator>();
                if (animator != null)
                    break;
            }
            if (animator == null || animator.gameObject == element.gameObject)
                return; // no attachment animator between the mesh and the element root

            var authored = AuthoredController();
            if (authored == null || animator.runtimeAnimatorController == authored)
                return;
            animator.runtimeAnimatorController = authored;
            Context.Log.Debug("ots14 armoury: arms controller restored on preview model");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"ots14 armoury patch failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The pristine controller lives on the ModelSecondary prefab asset the
    // template points at; instances only ever hold what the armoury stomped.
    private RuntimeAnimatorController AuthoredController()
    {
        var template = Templates.Resolve<WeaponTemplate>(WeaponId, _weapons, msg => Context.Log.Warn($"ots14 armoury: {msg}"));
        var prefab = template?.ModelSecondary;
        return prefab == null ? null : FindAnimator(prefab.transform)?.runtimeAnimatorController;
    }

    // The generic GetComponentInChildren overload JITs the same interop
    // Cast<T> instantiation as the GetComponentInParent twin described above
    // and throws VerificationException at runtime. The outer catch swallowed
    // it, so the controller was never restored and the preview arms stood
    // still. Walk by hand instead. Plain GetComponent<T> is safe, which is why
    // the parent walk uses it. Inactive children are covered because the walk
    // is over transforms, matching the includeInactive argument this replaces.
    private static Animator FindAnimator(Transform root)
    {
        if (root == null)
            return null;
        var animator = root.GetComponent<Animator>();
        if (animator != null)
            return animator;
        for (var i = 0; i < root.childCount; i++)
        {
            var hit = FindAnimator(root.GetChild(i));
            if (hit != null)
                return hit;
        }
        return null;
    }

}
