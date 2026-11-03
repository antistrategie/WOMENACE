using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Handgun Dolls carry their pistol as the main weapon, so it needs the pistol aiming pose
// without vanilla's pistol draw.
//
// Element.SetAiming (RVA 0x65F600) calls TargetAimComp.DrawPistol / HolsterPistol only for a
// skill whose template sets UsePistolAnimation. DrawPistol (0x668220) hides the main weapon
// slot, swaps the first attachment in the Pistol_Holster slot into the hand and raises the
// animator's IsPistol bool. A handgun in the main slot has no holster attachment, so that
// path would leave the hand empty while firing. Handgun fire skills therefore keep
// UsePistolAnimation off, and this system raises IsPistol itself while the element aims one.
// The bool drives the controller's pistol correction layer and Pistol_ShootOnce, and nothing
// but Draw/HolsterPistol writes it, so the pose holds for exactly the aim.
public sealed class HandgunPoseSystem : JiangyuSystem
{
    private readonly HashSet<IntPtr> _posed = new();

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Tactical.Element", "SetAiming", OnSetAiming);
    }

    public override void OnUnload() => _posed.Clear();

    private void OnSetAiming(PatchInfo info)
    {
        if (info.Instance is not Element element || info.Args[0] is not bool aiming)
            return;
        try
        {
            // An aim at another skill while posed (switching straight from the handgun to a
            // grenade) drops the pose as an un-aim does.
            if (aiming && IsHandgunSkill((info.Args[1] as Il2CppSystem.Object)?.TryCast<Skill>()))
            {
                element.m_ElementAnimator?.SetAnimatorBool(ElementAnimator.IS_PISTOL_ID, true);
                _posed.Add(element.Pointer);
            }
            else if (_posed.Remove(element.Pointer))
            {
                element.m_ElementAnimator?.SetAnimatorBool(ElementAnimator.IS_PISTOL_ID, false);
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"HandgunPoseSystem: {ex.Message}");
        }
    }

    private static bool IsHandgunSkill(Skill skill)
    {
        if (skill == null || skill.GetTemplate()?.UsePistolAnimation == true)
            return false;
        var weapon = skill.GetItem()?.GetTemplate()?.TryCast<WeaponTemplate>();
        return weapon != null && WeaponClasses.Classify(weapon) == WeaponClass.Handgun;
    }
}
