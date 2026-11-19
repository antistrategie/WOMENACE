using UnityEngine;

namespace WOMENACE.Code;

// The light rig a staged unit the room does not light carries: the armoury's squads and
// MENACE's own soldiers and vehicles, and the workshop's calibration doll where she needs one.
public sealed partial class StrategyDiorama
{
    // The armoury squad's rig. MENACE's character shader takes no ambient, so each staged unit gets
    // a small rig on top of the room's live lights: a warm window key from camera left, a cool fill
    // from the camera and a teal rim from beyond it, as (offset from its feet in the camera's frame
    // with +x camera right and +z away from the camera, colour, lumens). The room's surfaces shade
    // themselves and ignore it. The ops room's dolls take none: GFL2 lights its characters with the
    // room's sun, probes and the lights it keeps on characters, and nothing else.
    private static readonly (Vector3 Offset, Color Colour, float Lumens)[] UnitRig =
    {
        (new Vector3(-1.6f, 2.2f, -0.8f), new Color(1f, 0.9f, 0.78f), 14f),
        (new Vector3(0.6f, 1.4f, -1.8f), new Color(0.75f, 0.88f, 1f), 5f),
        (new Vector3(0.8f, 2.0f, 1.4f), new Color(0.5f, 0.85f, 0.95f), 10f),
    };
    // The rig against the room's exposure.
    internal const float RigLightScale = 1.41f;


    // Lights a unit with the rig, in the frame of the room camera looking at it.
    private void LightDoll(GameObject doll, float scale = 1f)
    {
        if (!_life.DollRigs.TryGetValue(doll.Pointer, out var rig) || rig[0] == null)
            _life.DollRigs[doll.Pointer] = rig = BuildRig(doll);
        var away = _scene.Camera != null ? doll.transform.position - _scene.Camera.transform.position : doll.transform.forward;
        away.y = 0f;
        var frame = Quaternion.LookRotation(away.sqrMagnitude > 1e-6f ? away : Vector3.forward, Vector3.up);
        var chest = doll.transform.position + Vector3.up * 1.3f;
        for (int i = 0; i < UnitRig.Length; i++)
        {
            var (offset, _, lumens) = UnitRig[i];
            var t = rig[i].transform;
            t.position = doll.transform.position + frame * offset;
            t.rotation = Quaternion.LookRotation(chest - t.position);
            rig[i].intensity = lumens * RigLightScale * scale;
        }
    }

    private static Light[] BuildRig(GameObject doll)
    {
        var rig = new Light[UnitRig.Length];
        for (int i = 0; i < rig.Length; i++)
            rig[i] = RigLight(doll, "WOMENACE_DollLight" + i, UnitRig[i].Colour, 8f);
        return rig;
    }

    // One light of a doll's rig: a point light of her own, which shadows nothing.
    private static Light RigLight(GameObject doll, string name, Color colour, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(doll.transform, false);
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.shadows = LightShadows.None;
        // Every rendering layer: the armoury's units sit on MENACE's own light layers.
        light.renderingLayerMask = -1;
        light.color = colour;
        light.range = range;
        light.lightUnit = UnityEngine.Rendering.LightUnit.Lumen;
        go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>().affectsVolumetric = false;
        return light;
    }

    // Takes the rig off one unit.
    private void RemoveRig(GameObject doll)
    {
        if (!_life.DollRigs.Remove(doll.Pointer, out var rig))
            return;
        foreach (var light in rig)
            if (light != null)
                UnityEngine.Object.Destroy(light.gameObject);
    }

    // Takes the rig off every doll under a root (the armoury's squad, given back to vanilla).
    private void RemoveRigs(Transform root)
    {
        List<IntPtr> gone = null;
        foreach (var (doll, rig) in _life.DollRigs)
        {
            if (rig[0] != null && !rig[0].transform.IsChildOf(root))
                continue;
            foreach (var light in rig)
                if (light != null)
                    UnityEngine.Object.Destroy(light.gameObject);
            (gone ??= new()).Add(doll);
        }
        if (gone != null)
            foreach (var doll in gone)
                _life.DollRigs.Remove(doll);
    }
}
