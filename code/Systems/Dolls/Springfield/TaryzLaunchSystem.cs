using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// The deploy skill throws its summon onto the tile as an arc projectile, and the projectile's prefab
// lives in the skill's Odin-serialised ProjectileData, which KDL cannot reach. The deploy clone
// inherits the Falcon skybot there, so without this the skybot flies out and Taryz appears on
// landing. The prefab is set the first time a mission starts with the perk, so Taryz's bundle stays
// unloaded until Springfield fields her.
public sealed class TaryzLaunchSystem : JiangyuSystem
{
    private const string FalconDeploy = "active.deploy_recon_skybot";

    private static TaryzLaunchSystem _instance;
    private IntPtr _done;

    public override void OnInit() => _instance = this;

    internal static void Ensure(SkillTemplate deploy) => _instance?.Apply(deploy);

    private void Apply(SkillTemplate deploy)
    {
        if (deploy == null || deploy.Pointer == _done)
            return;
        _done = deploy.Pointer;
        var projectile = deploy.ProjectileData;
        // Writing into a projectile the vanilla Falcon deploy also holds would throw Taryz from
        // every Falcon for the rest of the session.
        var falcon = Templates.ById<SkillTemplate>(FalconDeploy, message => Context.Log.Warn($"taryz launch: {message}"));
        if (projectile != null && falcon?.ProjectileData?.Pointer == projectile.Pointer)
        {
            Context.Log.Warn($"taryz launch: projectile shared with '{FalconDeploy}', left as the skybot");
            return;
        }
        var model = Context.Assets.Load<GameObject>(VehicleModels.AssetFor(VehicleModels.Taryz));
        if (projectile == null || model == null)
        {
            Context.Log.Warn($"taryz launch: not set (projectile={projectile != null}, model={model != null})");
            return;
        }
        projectile.Prefab = model;
    }
}
