using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Render cost of unit bodies, measured by standing copies of one in front of the camera.
//
//   {"verb":"Perf.Spawn","args":["sextans/default/main",12],"mutate":true}   a doll body from the mod's bundles
//   {"verb":"Perf.Spawn","args":["armor.player_class_3_rmc_mib",12],"mutate":true}  an armour template's body
//   {"verb":"Perf.Mark","mutate":true}   start a frame-time window
//   {"verb":"Perf.Since"}                 average frame time since the mark, and what the copies draw
//   {"verb":"Perf.Clear","mutate":true}  remove the copies
//
// Spawn lifts the frame cap and vsync so the window reads the frame's own cost.
[DevVerb]
public static class Perf
{
    private static readonly List<GameObject> Copies = new();
    private static int _markFrame;
    private static float _markTime;
    private static (int VSync, int Target)? _saved;

    [MutatingVerb]
    public static string Spawn(string source, int count, float spacing = 1.2f, float distance = 8f)
    {
        var prefab = BodyFor(source);
        if (prefab == null)
            return $"no body for '{source}'";
        _saved ??= (QualitySettings.vSyncCount, Application.targetFrameRate);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        var cam = ScreenCamera();
        if (cam == null)
            return "no camera draws to the screen";

        Clear();
        var t = cam.transform;
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count * 2.0)));
        for (var i = 0; i < count; i++)
        {
            var column = i % columns - (columns - 1) * 0.5f;
            var row = i / columns;
            var at = t.position + t.forward * (distance + row * spacing) + t.right * (column * spacing) - t.up * 1.6f;
            var copy = UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(-new Vector3(t.forward.x, 0f, t.forward.z)));
            Copies.Add(copy);
        }
        return $"{count} copies of '{source}' ({prefab.name}) before '{cam.name}'";
    }

    // The strategy scene tags no camera as main, so the topmost enabled camera drawing to the
    // screen stands in.
    private static Camera ScreenCamera()
    {
        if (Camera.main != null)
            return Camera.main;
        Camera best = null;
        foreach (var c in Camera.allCameras)
            if (c.isActiveAndEnabled && c.targetTexture == null && (best == null || c.depth > best.depth))
                best = c;
        return best;
    }

    [MutatingVerb]
    public static string Mark()
    {
        _markFrame = Time.frameCount;
        _markTime = Time.realtimeSinceStartup;
        return "marked";
    }

    public static object Since()
    {
        var frames = Time.frameCount - _markFrame;
        var seconds = Time.realtimeSinceStartup - _markTime;
        int renderers = 0, visible = 0, slots = 0, triangles = 0;
        foreach (var copy in Copies)
        {
            if (copy == null)
                continue;
            foreach (var r in copy.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                renderers++;
                if (!r.isVisible)
                    continue;
                visible++;
                slots += r.sharedMaterials.Length;
                var mesh = r.sharedMesh;
                if (mesh != null)
                    for (var s = 0; s < mesh.subMeshCount; s++)
                        triangles += (int)(mesh.GetIndexCount(s) / 3);
            }
        }
        return new
        {
            frames,
            ms = frames > 0 ? seconds * 1000f / frames : 0f,
            copies = Copies.Count,
            renderers,
            visible,
            slots,
            triangles,
        };
    }

    // Every LOD group in the scene that draws a skinned body: its size, its transition heights,
    // the height on screen it has from the camera, and the LOD it is drawn at. Unity picks the
    // first LOD whose transition height is below the body's height on screen times the LOD bias.
    public static string Lods(int limit = 40)
    {
        var cam = ScreenCamera();
        if (cam == null)
            return "no camera draws to the screen";
        var rows = new List<string>
        {
            $"camera '{cam.name}' fov={cam.fieldOfView:0.0} lodBias={QualitySettings.lodBias:0.###} maxLodLevel={QualitySettings.maximumLODLevel} screen={Screen.width}x{Screen.height}",
        };
        var halfTan = Math.Tan(cam.fieldOfView * Math.PI / 360.0);
        foreach (var group in UnityEngine.Object.FindObjectsOfType<LODGroup>())
        {
            if (rows.Count > limit)
                break;
            var lods = group.GetLODs();
            if (lods.Length == 0 || lods[0].renderers.Length == 0 || lods[0].renderers[0]?.TryCast<SkinnedMeshRenderer>() == null)
                continue;
            var t = group.transform;
            var scale = Math.Max(Math.Abs(t.lossyScale.x), Math.Max(Math.Abs(t.lossyScale.y), Math.Abs(t.lossyScale.z)));
            var centre = t.TransformPoint(group.localReferencePoint);
            var distance = Vector3.Distance(cam.transform.position, centre);
            var height = group.size * scale / (2.0 * distance * halfTan);
            var drawn = -1;
            for (var i = 0; i < lods.Length && drawn < 0; i++)
                foreach (var r in lods[i].renderers)
                    if (r != null && r.isVisible)
                    {
                        drawn = i;
                        break;
                    }
            var heights = string.Join("/", lods.Select(l => l.screenRelativeTransitionHeight.ToString("0.###")));
            rows.Add($"{group.gameObject.name} ({lods[0].renderers[0].name}) size={group.size:0.##} scale={scale:0.##} dist={distance:0.#} onScreen={height:0.###} transitions={heights} drawn={drawn}");
        }
        return string.Join("\n", rows);
    }

    [MutatingVerb]
    public static string Clear()
    {
        foreach (var copy in Copies)
            if (copy != null)
                UnityEngine.Object.Destroy(copy);
        var n = Copies.Count;
        Copies.Clear();
        return $"{n} copies removed";
    }

    [MutatingVerb]
    public static string Restore()
    {
        Clear();
        if (_saved is { } s)
        {
            QualitySettings.vSyncCount = s.VSync;
            Application.targetFrameRate = s.Target;
            _saved = null;
        }
        return "restored";
    }

    // An armour template id gives its first female model, as the game picks it. Anything else is
    // a prefab asset name in the mod's bundles.
    private static GameObject BodyFor(string source)
    {
        if (source.StartsWith("armor.", StringComparison.Ordinal))
        {
            var template = Templates.ById<Il2CppMenace.Items.ArmorTemplate>(source);
            var models = template?.FemaleModels;
            if (models == null || models.Length == 0)
                models = template?.MaleModels;
            return models != null && models.Length > 0 ? models[0] : null;
        }
        return SpringBoneSystem.Instance?.Context.Assets.Load<GameObject>(source);
    }
}
