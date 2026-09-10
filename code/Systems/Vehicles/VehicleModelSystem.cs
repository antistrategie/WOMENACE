using Il2CppMenace.Tactical;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// The native squad preview and tactical spawn both select their body through
// DetermineArmorPrefab. Install the vehicle's prefab immediately before that choice
// so unused vehicles keep their model and texture bundles unloaded.
public sealed class VehicleModelSystem : JiangyuSystem
{
    private readonly HashSet<IntPtr> _checkedTemplates = [];

    public override void OnInit()
    {
        Context.Patches.Prefix("Il2CppMenace.Tactical.EntityVisuals", "DetermineArmorPrefab", 7, BeforeDetermineArmorPrefab);
    }

    private void BeforeDetermineArmorPrefab(PatchInfo info)
    {
        if (info.Args[0] is not EntityTemplate template || template == null || !_checkedTemplates.Add(template.Pointer))
            return;

        var id = template.GetID();
        var asset = VehicleModels.AssetFor(id);
        if (asset == null)
            return;

        var model = Context.Assets.Load<GameObject>(asset);
        if (model == null)
        {
            Context.Log.Warn($"Vehicle model '{asset}' is unavailable for '{id}'.");
            return;
        }

        // Copy the inherited list before replacing its first entry. Its remaining
        // variants and the native random selection keep their authored behaviour.
        var inherited = template.Prefabs;
        var prefabs = new Il2CppSystem.Collections.Generic.List<GameObject>();
        for (var i = 0; inherited != null && i < inherited.Count; i++)
            prefabs.Add(inherited[i]);
        if (prefabs.Count == 0)
            prefabs.Add(model);
        else
            prefabs[0] = model;
        template.Prefabs = prefabs;
    }
}
