using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace WOMENACE.Code;

// The sun and live shadows. The diorama's one directional light is the active room's sun for the
// characters: GFL2's sun lights them too (the refitting room's lights nothing else), at its own
// colour and pi times its URP intensity in lux (the doll shader's key divides by pi), and each doll
// takes the light probes' occlusion of it. Womenace/BakedScene shades the room's sun itself, as
// GFL2 does: direction and colour (linear, times GFL2's intensity) from the active set's sun, gated
// by the room's shadowmask where its own geometry blocks it. The characters (the dolls, the
// Dinergate, the armoury's units) shadow the sun and the baked light under them, as GFL2's player
// shadow does, through this light's shadow map, which the shader reads directly. The room's own
// geometry sits on its own rendering layer, which this light's shadow leaves out (it casts for
// GFL2's live spots only).
public sealed partial class StrategyDiorama
{
    // How far a live shadow darkens the baked light under it. GFL2 sets its indoor player shadow
    // from code, outside the scene's data, so this is the diorama's own value.
    internal const float ShadowStrength = 0.7f;
    private const float ShadowDistance = 35f;
    // The rendering layer the rooms' geometry is on (Womenace.EditorTools.BakeRoom puts it there),
    // clear of MENACE's own light layers.
    private const uint RoomRenderingLayer = 1u << 8;
    private Light _shadowLight;

    // The shadow maps follow the player's shadow quality as MENACE's own do: the characters' sun at
    // 1024, 2048 and, at the top, GFL2's 4096, the room's live spots and points at half that (they
    // share HDRP's punctual atlas). Rechecked every frame, so a change in the options applies at once.
    private int _shadowQuality = -1;

    private void ApplyShadowQuality()
    {
        var quality = Il2CppMenace.PlayerSettings.PersistentPlayerSettings.DisplaySettings.ShadowQuality;
        if (quality == _shadowQuality)
            return;
        _shadowQuality = quality;
        var resolution = quality <= 1 ? 1024 : quality == 2 ? 2048 : 4096;
        _shadowLight?.GetComponent<HDAdditionalLightData>()?.SetShadowResolution(resolution);
        foreach (var set in Sets)
            foreach (var light in set.LiveLights)
                if (light != null && light.shadows != LightShadows.None)
                    light.GetComponent<HDAdditionalLightData>()?.SetShadowResolution(resolution / 2);
    }

    private void SpawnShadowLight(UnityEngine.Rendering.VolumeProfile profile)
    {
        try
        {
            var go = new GameObject("WOMENACE_RoomShadowLight");
            go.transform.SetParent(_camera.transform.parent, false);
            _shadowLight = go.AddComponent<Light>();
            _shadowLight.type = LightType.Directional;
            _shadowLight.shadows = LightShadows.Soft;
            _shadowLight.lightUnit = UnityEngine.Rendering.LightUnit.Lux;
            _shadowLight.intensity = 0.01f;
            var hd = go.AddComponent<HDAdditionalLightData>();
            hd.affectsVolumetric = false;
            hd.linkShadowLayers = false;
            _shadowLight.renderingLayerMask = unchecked((int)~RoomRenderingLayer);
            _shadowQuality = -1;

            var settings = AddVolumeComponent<HDShadowSettings>(profile);
            SetParameter(settings.maxShadowDistance, ShadowDistance);
            SetParameter(settings.cascadeShadowSplitCount, 2);
            Shader.SetGlobalFloat("_WmShadowStrength", ShadowStrength);
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"strategy diorama: shadow light failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Points the shader's sun and the shadow light along a set's sun.
    private void ApplySun(RoomSet set)
    {
        if (set.Sun == null)
        {
            Shader.SetGlobalVector("_WmSunColor", Vector4.zero);
            if (_shadowLight != null)
            {
                // A room with no sun is lit from its ceiling: its characters' shadow falls
                // straight under them, and not along the last room's sun.
                _shadowLight.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                _shadowLight.intensity = 0.01f;
            }
            return;
        }
        var rotation = set.Sun.transform.rotation;
        if (_shadowLight != null)
        {
            _shadowLight.transform.rotation = rotation;
            _shadowLight.color = set.Sun.color;
            // A sun that lights no character still shadows them, at a hundredth of a lux.
            _shadowLight.intensity = LightsCharacters(set.Sun) ? set.Sun.intensity * Mathf.PI : 0.01f;
        }
        var towardSun = -(rotation * Vector3.forward);
        Shader.SetGlobalVector("_WmSunDirection", new Vector4(towardSun.x, towardSun.y, towardSun.z, 0f));
        var colour = LightsRoom(set.Sun) ? set.Sun.color.linear * set.Sun.intensity : Color.black;
        Shader.SetGlobalVector("_WmSunColor", new Vector4(colour.r, colour.g, colour.b, 0f));
    }

    private void ClearShadowLight()
    {
        Shader.SetGlobalFloat("_WmShadowStrength", 0f);
        Shader.SetGlobalVector("_WmSunColor", Vector4.zero);
        if (_shadowLight != null)
            UnityEngine.Object.Destroy(_shadowLight.gameObject);
        _shadowLight = null;
    }
}
