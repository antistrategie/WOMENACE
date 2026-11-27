using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// A support team's body tells the plain Deploy stance from its heavy weapon set up. Vanilla's
// "Set Up Heavy Weapon" skill changes no stance. It raises the actor's heavy weapon flag, which
// all the Stance parameter cannot carry, so the team's controller reads a SetUp bool as well,
// written here from Actor.IsHeavyWeaponDeployed. A team whose controller has no such parameter is
// left alone, since Unity warns on each write to a missing one.
//
// Vanilla writes the flag in place, never through SetHeavyWeaponDeployed, so the bool follows the
// three methods that write it:
// - DeployHeavyWeaponHandler.OnUse raises it and then unfolds the weapon's HeavyWeaponComp
//   (RVA 0x79EE40). A team's mortar is part of its body and has no such component, so the
//   vanilla handler throws and the skill stops before Skill.OnUse finishes. For a team the
//   handler is replaced: the flag is raised here and Skill.OnUse reports the tile applied, as the
//   vanilla handler does once the weapon has unfolded.
// - ElementAnimator.SetMovementParams lowers it when the actor moves, after playing the packing
//   clip on the heavy weapon's own animator (RVA 0x65F540), which a team has none of either. The
//   flag is lowered here first, so vanilla's branch is never taken.
// - Actor.SetStance lowers it when the stance goes back to Default.
public sealed class SupportTeamStanceSystem : JiangyuSystem
{
    private const string SetUpParameter = "SetUp";

    // Set while a team's set-up handler has been replaced inside Skill.OnUse, so that call
    // returns true as vanilla's own handler would.
    private bool _replacedSetUp;

    public override void OnInit()
    {
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.Effects.DeployHeavyWeaponHandler", "OnUse", OnDeployHeavyWeapon);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "OnUse", OnSkillUsed);
        Context.Patches.Prefix("Il2CppMenace.Tactical.ElementAnimator", "SetMovementParams", OnMovement);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Actor", "SetStance", OnActorChanged);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Element", "OnStanceChanged", OnElementChanged);
    }

    // Args: Actor user, Tile target, UsageParameter, ref bool applyToTile.
    private void OnDeployHeavyWeapon(PatchInfo info)
    {
        try
        {
            var actor = TeamActor(info.Args is { Count: > 0 } ? info.Args[0] as Il2CppSystem.Object : null);
            if (actor == null)
                return;
            actor.SetHeavyWeaponDeployed(true);
            WriteAll(actor);
            _replacedSetUp = true;
            info.Skip = true;
        }
        catch (Exception ex) { Context.Log.Warn($"support team: set up failed: {ex.Message}"); }
    }

    private void OnSkillUsed(PatchInfo info)
    {
        if (!_replacedSetUp)
            return;
        _replacedSetUp = false;
        info.Result = true;
    }

    private void OnMovement(PatchInfo info)
    {
        try
        {
            if (info.Instance is not ElementAnimator elementAnimator)
                return;
            var actor = TeamActor(elementAnimator.m_Element?.GetEntity());
            if (actor == null || !actor.IsHeavyWeaponDeployed())
                return;
            actor.SetHeavyWeaponDeployed(false);
            WriteAll(actor);
        }
        catch (Exception ex) { Context.Log.Warn($"support team: packing up failed: {ex.Message}"); }
    }

    private void OnActorChanged(PatchInfo info)
    {
        try
        {
            var actor = TeamActor(info.Instance as Il2CppSystem.Object);
            if (actor != null)
                WriteAll(actor);
        }
        catch (Exception ex) { Context.Log.Warn($"support team: set-up pose failed: {ex.Message}"); }
    }

    private void OnElementChanged(PatchInfo info)
    {
        try
        {
            if (info.Instance is not Element element)
                return;
            var actor = TeamActor(element.GetEntity());
            if (actor != null)
                Write(element, actor.IsHeavyWeaponDeployed());
        }
        catch (Exception ex) { Context.Log.Warn($"support team: set-up pose failed: {ex.Message}"); }
    }

    private static UnitActor TeamActor(Il2CppSystem.Object entity)
    {
        var actor = entity?.TryCast<UnitActor>();
        return actor != null && SupportTeams.Of(actor.GetLeader()) != null ? actor : null;
    }

    private static void WriteAll(UnitActor actor)
    {
        var elements = actor.GetElements();
        if (elements == null)
            return;
        var setUp = actor.IsHeavyWeaponDeployed();
        for (var i = 0; i < elements.Count; i++)
            Write(elements[i], setUp);
    }

    private static void Write(Element element, bool setUp)
    {
        var animator = element != null ? element.GetComponentInChildren<UnityEngine.Animator>() : null;
        if (animator == null)
            return;
        foreach (var parameter in animator.parameters)
            if (parameter.name == SetUpParameter)
            {
                animator.SetBool(SetUpParameter, setUp);
                return;
            }
    }
}
