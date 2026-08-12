using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.Rendering;
using System.Reflection;

namespace WOMENACE.Code;

// Reports what lights a scene carries, for reading against how the dolls render.
//
// Womenace/DollToon reads HDRP's own light lists and shadow maps directly, so
// nothing here feeds the shader. That is the point: a shader that reads the
// pipeline's light data cannot disagree with the scene, where pushing globals
// from managed code could and did. MENACE builds a tactical map after the scene
// loads and builds the armoury stage when the screen opens, so any push has an
// ordering problem that reading the light list does not.
//
// What remains is a census, because the lighting a scene actually provides is
// the first thing worth knowing when a doll renders wrong. The armoury, for
// instance, carries no directional light at all, which is why the shader has an
// extra-lights path rather than a fallback illuminance.
public sealed class DollLightingSystem : JiangyuSystem
{
    private static bool _loggedShadowSettings;

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        try
        {
            Context.Log.Debug($"doll lighting: scene '{sceneName}' lights: {DescribeLights()}");
            if (!_loggedShadowSettings)
            {
                _loggedShadowSettings = true;
                Context.Log.Debug($"doll lighting: {DescribeShadowSettings()}");
            }
        }
        catch (Exception ex)
        {
            // Diagnostics are cosmetic. A failure here must not take a scene down.
            Context.Log.Warn($"doll lighting: could not survey the scene's lights "
                + $"({ex.GetType().Name}: {ex.Message})");
        }
    }

    // The pipeline's shadow configuration, read out of the running game.
    //
    // Worth doing rather than reasoning about, because it decides whether a
    // hair-to-face shadow is reachable through the shadow map at all. A directional
    // cascade's texel covers (cascade extent / atlas resolution) of world, and the
    // fringe hangs about 26 mm off the forehead: if one texel is larger than that,
    // no caster thickness or bias setting can resolve the two, and the band has to
    // come from somewhere other than the shadow map.
    //
    // Reflective because naming HDRP's types would put a package dependency into a
    // mod that needs none, the same reason the light intensity is read that way.
    // Anything shadow-related is dumped rather than a fixed list of fields, since
    // the useful names differ by HDRP version.
    private static string DescribeShadowSettings()
    {
        var pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline == null)
            return "no render pipeline asset bound";

        var found = new List<string>();
        Collect(pipeline, pipeline.GetType().Name, found, depth: 0);
        return found.Count == 0
            ? $"pipeline '{pipeline.GetType().Name}' exposed no shadow settings"
            : "shadow settings: " + string.Join(", ", found);
    }

    private static void Collect(object target, string path, List<string> found, int depth)
    {
        if (target == null || depth > 3 || found.Count > 40) return;
        var type = target.GetType();

        foreach (var name in EnumerateMemberNames(type))
        {
            object value;
            try { value = ReadMember(target, name); }
            catch { continue; }
            if (value == null) continue;

            var interesting = name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0
                || value.GetType().Name.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0;
            var valueType = value.GetType();

            if (valueType.IsPrimitive || value is string || valueType.IsEnum)
            {
                if (interesting) found.Add($"{path}.{name}={value}");
            }
            else if (interesting || name.IndexOf("settings", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Collect(value, $"{path}.{name}", found, depth + 1);
            }
        }
    }

    private static IEnumerable<string> EnumerateMemberNames(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (var p in type.GetProperties(flags))
            if (p.GetIndexParameters().Length == 0) yield return p.Name;
        foreach (var f in type.GetFields(flags)) yield return f.Name;
    }

    private static object ReadMember(object target, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = target.GetType();
        var property = type.GetProperty(name, flags);
        if (property != null && property.GetIndexParameters().Length == 0)
            return property.GetValue(target);
        return type.GetField(name, flags)?.GetValue(target);
    }

    // A one-line census: the count of each light type, and the brightest
    // directional light where there is one, since that is the light the shader
    // treats as the key.
    private static string DescribeLights()
    {
        var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        if (lights == null || lights.Length == 0)
            return "none";

        var counts = new Dictionary<LightType, int>();
        Light key = null;
        foreach (var light in lights)
        {
            if (light == null || !light.enabled || !light.gameObject.activeInHierarchy) continue;
            counts.TryGetValue(light.type, out var n);
            counts[light.type] = n + 1;
            if (light.type == LightType.Directional
                && (key == null || light.intensity > key.intensity))
                key = light;
        }
        if (counts.Count == 0) return "none enabled";

        var census = string.Join(", ", counts.Select(kv => $"{kv.Value}x {kv.Key}"));
        return key == null
            ? $"{census}; no directional light, so the dolls are lit by the extra-lights path"
            : $"{census}; key '{key.name}'";
    }
}
