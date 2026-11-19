using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace WOMENACE.Code;

// The diorama's rooms. Each is a GFL2 scene baked by Womenace.EditorTools.BakeRoom, which settles
// everything GFL2's data decides about its geometry, materials, probes, shadow casting and lights.
// What is left is the scene's look, which GFL2 keeps in its post-processing volume and its scene
// fog rather than in the scene's objects: RoomLook carries those values as the scene authors them
// (~/gfl2-extract/<room>/dump/post.json), and the room's set applies them while it is on screen.
//
// One set is active at a time: its volume over the room camera's own, its sun, its live lights and
// its fog in Womenace/BakedScene's globals, and its floor mirror. The ops room is the strategy
// layer's set, the refitting room the armoury's.
public sealed partial class StrategyDiorama
{
    // The baked brightness at and over which a room's surface reflects the room in full. A darker
    // one reflects it less: the reflection probe is one picture of the whole room, and worn
    // metal on a wall in shadow would otherwise shine with the windows in blotches.
    private const float ReflectionWholeAt = 0.25f;

    // A GFL2 scene's look. Fog: GFL2's linear fog (colour, start, end in metres). Its exponential
    // height fog is left out (Womenace/BakedScene's ApplyFog says why).
    // Mirror: GFL2's planar reflection (strength, its blur radius taken as the mip of a half-screen
    // mirror render, tint).
    private readonly record struct RoomLook(
        float BloomThreshold, float BloomIntensity,
        float VignetteIntensity, float VignetteSmoothness, float VignetteGrey,
        float GrainIntensity, float GrainResponse,
        float Saturation, float Contrast, float PostExposure,
        float FilmSlope, float FilmToe, float FilmShoulder, float FilmBlackClip, float FilmWhiteClip,
        float LookupContribution,
        Color FogColour, float FogStart, float FogEnd,
        float MirrorStrength, float MirrorBlur, Color MirrorTint,
        float SfxIntensity,
        float AoIntensity = 0f, float AoRadius = 0f);

    // 02Aimo_Command_Room's "_LightPostEffect" volume. Its floor mirror is the RerlectionAdd overlay
    // on the pit (tint and strength from its material and the planar reflection volume), which
    // reflects the whole room.
    private static readonly RoomLook OpsLook = new(
        BloomThreshold: 0.9f, BloomIntensity: 0.2f,
        VignetteIntensity: 0.25f, VignetteSmoothness: 0.12f, VignetteGrey: 0.204f,
        GrainIntensity: 0.3f, GrainResponse: 0.8f,
        Saturation: 23.4f, Contrast: 0.1f, PostExposure: 0f,
        FilmSlope: 0.91f, FilmToe: 0.47f, FilmShoulder: 0.23f, FilmBlackClip: 0f, FilmWhiteClip: 0.035f,
        LookupContribution: 0.3f,
        FogColour: new Color(0.5918f, 0.7686f, 0.7686f), FogStart: 3.7f, FogEnd: 99f,
        MirrorStrength: 0.5f, MirrorBlur: 1f, MirrorTint: new Color(0.2f, 0.173f, 0.146f),
        SfxIntensity: 1f);

    // 04Aimo_Training_Platform's "_PostEffect" volume: no colour lookup and no film grain. Its
    // polished floor mirrors the hangar through GFL2's planar reflection volume (its probe is a dim,
    // coarse cube). Its mirror is less blurred than GFL2's, whose blur comes out blocky here.
    private static readonly RoomLook RefitLook = new(
        BloomThreshold: 0.9f, BloomIntensity: 0.2f,
        VignetteIntensity: 0.25f, VignetteSmoothness: 0.12f, VignetteGrey: 0.2f,
        GrainIntensity: 0f, GrainResponse: 0.8f,
        Saturation: 23.4f, Contrast: 0.1f, PostExposure: 0f,
        FilmSlope: 0.91f, FilmToe: 0.47f, FilmShoulder: 0.23f, FilmBlackClip: 0f, FilmWhiteClip: 0.035f,
        LookupContribution: 0f,
        FogColour: new Color(0.5347f, 0.736f, 0.766f), FogStart: 10f, FogEnd: 400f,
        MirrorStrength: 0.851f, MirrorBlur: 0.5f, MirrorTint: Color.white,
        SfxIntensity: 0.25f);

    // 01Aimo_WeaponRoom_Lobby's "_PostEffect_WeaponRoom" volume: a stronger bloom and contrast
    // than the other rooms, a raised exposure, no colour lookup and no film grain. Its furniture
    // is in no lightmap, and its ambient occlusion is what seats it (Womenace/BakedScene's RoomAo).
    private static readonly RoomLook WeaponLook = new(
        BloomThreshold: 0.9f, BloomIntensity: 0.7f,
        VignetteIntensity: 0.25f, VignetteSmoothness: 0.12f, VignetteGrey: 0.2f,
        GrainIntensity: 0f, GrainResponse: 0.8f,
        Saturation: 23.4f, Contrast: 9f, PostExposure: 0.63f,
        FilmSlope: 0.91f, FilmToe: 0.47f, FilmShoulder: 0.23f, FilmBlackClip: 0f, FilmWhiteClip: 0.035f,
        LookupContribution: 0f,
        FogColour: new Color(0.5176f, 0.6078f, 0.6902f), FogStart: 10f, FogEnd: 400f,
        MirrorStrength: 0.851f, MirrorBlur: 1.65f, MirrorTint: Color.white,
        SfxIntensity: 1f,
        AoIntensity: 0.8f, AoRadius: 0.5f);

    private sealed class RoomSet
    {
        public string Asset;
        // GFL2's own scene effects for the room, rebuilt from its particle systems by
        // Womenace.EditorTools.BuildRoomVfx in the scene's space, which is the room's.
        public string Effects;
        public Vector3 Origin;
        public RoomLook Look;
        // Room-space height of the floor the mirror lies in.
        public float MirrorFloor;
        public GameObject Root;
        public GameObject Volume;
        public Texture3D Grade;
        public Light Sun;
        public readonly List<Light> LiveLights = new();
        // GFL2's baked light probes, in the room's space: position, sun occlusion, SH constants.
        public LightProbe[] LightProbes = System.Array.Empty<LightProbe>();
        // GFL2's reflection probes, in the room's space.
        public ReflectionProbe[] ReflectionProbes = System.Array.Empty<ReflectionProbe>();
        // GFL2's pre-baked box and its cube, where its floor mirror shows that instead of the live
        // room. The box is never drawn: the floor reads the cube itself.
        public Renderer MirrorBox;
        public Texture MirrorCube;
    }

    private readonly record struct LightProbe(Vector3 Position, float SunOcclusion, Vector4[] Sh);

    private readonly record struct ReflectionProbe(Bounds Reach, int Importance, Texture Cube);

    // The rooms stand under the strategy layer's own scenes, out of their sight, and no further:
    // a position is only as fine as its distance from the world's origin allows (a tenth of a
    // millimetre at two kilometres), and the dolls' hair is simulated from the positions of their
    // bones, where that coarseness shows as a tremor that never settles.
    private static readonly Vector3 RoomOrigin = new(0f, -100f, 0f);
    // The sets themselves, one for each room, are the scene's (DioramaScene).

    // GFL2 renders without an exposure step, so the room camera exposes at HDRP's neutral value
    // (an exposure multiplier of 1, EV100 = -log2(1.2)) and the lightmap's values arrive as GFL2
    // shades them.
    private static readonly float NeutralExposureEv = -Mathf.Log(1.2f, 2f);

    // Spawns a set's room, far enough from the others that none sees another, inactive until shown.
    private GameObject SpawnSet(RoomSet set)
    {
        if (set.Root != null)
            return set.Root;
        var prefab = Context.Assets.Load<GameObject>(set.Asset);
        if (prefab == null)
        {
            Context.Log.Warn($"strategy diorama: '{set.Asset}' is not in the bundles");
            return null;
        }
        set.Root = UnityEngine.Object.Instantiate(prefab);
        set.Root.name = "WOMENACE_" + set.Asset.Split('/')[1];
        set.Root.transform.SetPositionAndRotation(set.Origin, Quaternion.identity);
        foreach (var light in set.Root.GetComponentsInChildren<Light>(true))
        {
            // The sun shades the room through Womenace/BakedScene, and its shadows come from the
            // diorama's shadow light, so the light itself stays off.
            if (light.type == LightType.Directional)
            {
                set.Sun = light;
                light.enabled = false;
            }
            else
            {
                set.LiveLights.Add(light);
                light.enabled = false;
            }
        }
        set.LightProbes = ReadLightProbes(set.Root);
        // New lights take the shadow quality on the next frame.
        _shadowQuality = -1;
        set.ReflectionProbes = ReadReflectionProbes(set.Root);
        set.MirrorBox = set.Root.transform.Find("_MirrorBox")?.GetComponent<MeshRenderer>();
        set.MirrorCube = set.MirrorBox?.sharedMaterial?.GetTexture("_Cubemap");
        SpawnEffects(set);
        ExtendScenery(set.Root);
        Context.Log.Info($"strategy diorama: spawned {set.Asset} with {set.LiveLights.Count} live lights, {set.LightProbes.Length} light probes");
        return set.Root;
    }

    // The set's post-processing, over the room camera's own volume: bloom, vignette, grain, colour
    // adjustments and GFL2's tonemapper with its colour lookup (StrategyDiorama.Grade), in HDRP's
    // equivalents. Enabled only while the set is active.
    // GFL2's scenery outside the ops room's windows is two long cards, one along each side, made
    // for its cameras, which look down the room. Seen across the room, the windows nearest its
    // head look past the cards' ends into nothing. Each card is continued past that end by a
    // copy of itself in its own plane, one card's length along, turned over so the picture carries
    // on mirrored without a seam.
    private const string SceneryMaterial = "s_slg_commandRoom_lit_BG_01";

    private void ExtendScenery(GameObject room)
    {
        var extended = 0;
        foreach (var card in room.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (card.sharedMaterial == null || !card.sharedMaterial.name.StartsWith(SceneryMaterial, StringComparison.Ordinal))
                continue;
            var from = card.transform;
            // The card's length is its own x axis. The copy lies along it, towards the room's head
            // (the room's larger x).
            var along = from.right * Mathf.Abs(from.lossyScale.x);
            if (room.transform.InverseTransformDirection(along).x < 0f)
                along = -along;
            var copy = UnityEngine.Object.Instantiate(card.gameObject, from.parent, false);
            copy.name = card.name + " (continued)";
            copy.transform.SetPositionAndRotation(from.position + along, from.rotation);
            var size = from.localScale;
            copy.transform.localScale = new Vector3(-size.x, size.y, size.z);
            extended++;
        }
        if (extended > 0)
            Context.Log.Info($"strategy diorama: {extended} scenery cards continued past the room's head");
    }

    private GameObject SpawnSetVolume(RoomSet set)
    {
        if (_camera == null)
            return null;
        var look = set.Look;
        var go = new GameObject("WOMENACE_SetVolume_" + set.Asset.Split('/')[1]) { layer = _camera.layer };
        go.transform.SetParent(_camera.transform, false);
        var volume = go.AddComponent<UnityEngine.Rendering.Volume>();
        volume.isGlobal = true;
        volume.priority = 200f;
        var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();

        var bloom = AddVolumeComponent<Bloom>(profile);
        SetParameter(bloom.threshold, look.BloomThreshold);
        SetParameter(bloom.intensity, look.BloomIntensity);
        var vignette = AddVolumeComponent<Vignette>(profile);
        SetParameter(vignette.intensity, look.VignetteIntensity);
        SetParameter(vignette.smoothness, look.VignetteSmoothness);
        SetParameter(vignette.color, new Color(look.VignetteGrey, look.VignetteGrey, look.VignetteGrey));
        var grain = AddVolumeComponent<FilmGrain>(profile);
        SetParameter(grain.intensity, look.GrainIntensity);
        SetParameter(grain.response, look.GrainResponse);
        var colour = AddVolumeComponent<ColorAdjustments>(profile);
        SetParameter(colour.saturation, look.Saturation);
        SetParameter(colour.contrast, look.Contrast);
        SetParameter(colour.postExposure, look.PostExposure);
        var tonemapping = AddVolumeComponent<Tonemapping>(profile);
        set.Grade = BuildGradeLut(look);
        if (set.Grade != null)
        {
            SetParameter(tonemapping.mode, TonemappingMode.External);
            SetParameter(tonemapping.lutTexture, set.Grade);
            SetParameter(tonemapping.lutContribution, 1f);
        }
        else
        {
            SetParameter(tonemapping.mode, TonemappingMode.ACES);
        }
        volume.sharedProfile = profile;
        volume.enabled = false;
        return go;
    }

    // Mission prep has its own music and so has each room (StrategyMusicSystem).
    private void DriveMusic()
    {
        if (_active == null)
            return;
        StrategyMusicSystem.Drive(_policyScreen == VanillaUi.Screens.MissionPrep,
            _active == _weaponSet ? StrategyMusicSystem.Room.Workshop
            : _active == _refitSet ? StrategyMusicSystem.Room.Refitting
            : StrategyMusicSystem.Room.Ops);
    }

    // Puts a set on screen: its room, volume, sun, live lights, fog and mirror. The other set's room
    // stops drawing (and its effects stop running).
    private void ActivateSet(RoomSet set)
    {
        if (set?.Root == null)
            return;
        set.Root.SetActive(true);
        if (_active == set)
            return;
        if (_active?.Root != null)
        {
            _active.Root.SetActive(false);
            if (_active.Volume != null)
                _active.Volume.GetComponent<UnityEngine.Rendering.Volume>().enabled = false;
        }
        _active = set;
        if (set.Volume == null)
            set.Volume = SpawnSetVolume(set);
        if (set.Volume != null)
            set.Volume.GetComponent<UnityEngine.Rendering.Volume>().enabled = true;
        foreach (var light in set.LiveLights)
            if (light != null)
                light.enabled = true;
        ApplySun(set);
        ApplyLiveLights(set);
        ApplyFog(set.Look);
        Shader.SetGlobalVector("_WmAo", new Vector4(set.Look.AoIntensity, set.Look.AoRadius, 0f, 0f));
        Shader.SetGlobalVector("_WmReflectionShade", new Vector4(1f, ReflectionWholeAt, 0f, 0f));
        // The scene's GameScene effect intensity, which GFL2's effect shaders scale by.
        Shader.SetGlobalFloat("_GlobalSfxIntensityScale", set.Look.SfxIntensity);
    }

    // GFL2's live lights for the room's surfaces, which Womenace/BakedScene shades itself. The HDRP
    // lights carry pi times GFL2's intensity (BakeRoom), for the dolls under them.
    // Womenace/BakedScene shades as many: the weapon room has eight.
    private const int MaxLiveLights = 8;
    // The layer GFL2's scenery renders on: a light whose culling mask leaves it out (the refitting
    // room's sun lights only GFL2's characters) does not shade the room.
    private const int SceneryLayer = 15;

    private static bool LightsRoom(Light light) => (light.cullingMask & (1 << SceneryLayer)) != 0;

    // The layer GFL2's characters render on.
    private const int CharacterLayer = 13;

    private static bool LightsCharacters(Light light) => (light.cullingMask & (1 << CharacterLayer)) != 0;

    private static void ApplyLiveLights(RoomSet set)
    {
        var count = 0;
        foreach (var light in set.LiveLights)
        {
            if (light == null || count == MaxLiveLights || !LightsRoom(light))
                continue;
            var p = light.transform.position;
            var toward = -light.transform.forward;
            var c = light.color.linear * (light.intensity / Mathf.PI);
            // URP's cone: smooth from the inner to the outer angle. A point light passes it whole.
            // A light whose shadow leaves the room out (a Mixed one, BakeRoom) takes the room's
            // shadows from the shadowmask.
            var masked = (light.renderingLayerMask & RoomRenderingLayer) == 0 ? 1f : 0f;
            var spot = new Vector4(0f, 1f, masked, 0f);
            if (light.type == LightType.Spot)
            {
                var cosOuter = Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad);
                var cosInner = Mathf.Cos(light.innerSpotAngle * 0.5f * Mathf.Deg2Rad);
                var scale = 1f / Mathf.Max(cosInner - cosOuter, 0.001f);
                spot = new Vector4(scale, -cosOuter * scale, masked, 0f);
            }
            SetLiveLight(count++, new Vector4(p.x, p.y, p.z, 1f / Mathf.Max(light.range * light.range, 1e-4f)),
                new Vector4(toward.x, toward.y, toward.z, 0f), new Vector4(c.r, c.g, c.b, 0f), spot);
        }
        while (count < MaxLiveLights)
            SetLiveLight(count++, Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero);
    }

    private static void SetLiveLight(int index, Vector4 position, Vector4 direction, Vector4 colour, Vector4 spot)
    {
        Shader.SetGlobalVector($"_WmLight{index}Position", position);
        Shader.SetGlobalVector($"_WmLight{index}Direction", direction);
        Shader.SetGlobalVector($"_WmLight{index}Color", colour);
        Shader.SetGlobalVector($"_WmLight{index}Spot", spot);
    }

    private static void ApplyFog(RoomLook look)
    {
        Shader.SetGlobalVector("_WmFogColor", (Vector4)look.FogColour.linear);
        Shader.SetGlobalVector("_WmFogLinear", new Vector4(0f, 0f, look.FogStart, look.FogEnd));
    }

    private void SpawnEffects(RoomSet set)
    {
        if (set.Effects == null)
            return;
        try
        {
            var prefab = Context.Assets.Load<GameObject>(set.Effects);
            if (prefab == null)
            {
                Context.Log.Warn($"strategy diorama: '{set.Effects}' is not in the bundles");
                return;
            }
            var effects = UnityEngine.Object.Instantiate(prefab, set.Root.transform, false);
            effects.name = "WOMENACE_RoomEffects";
            DrawTableFirst(effects);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: {set.Effects} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The holotable's own glow, grid and sparks are drawn ahead of vanilla's star system, whose
    // materials sit just under the usual transparent queue. Otherwise the table paints over the
    // foot of whatever stands on it, which then reads as standing behind it.
    private const string TableEffects = "s_Aimo_table_02";
    private const int TableEffectsQueue = 2980;

    private static void DrawTableFirst(GameObject effects)
    {
        foreach (var t in effects.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != TableEffects)
                continue;
            foreach (var renderer in t.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null)
                        material.renderQueue = TableEffectsQueue;
        }
    }

    // The probes Womenace.EditorTools.BakeRoom writes into the room's "_LightProbes" texture.
    private static LightProbe[] ReadLightProbes(GameObject room)
    {
        var holder = room.transform.Find("_LightProbes")?.GetComponent<MeshRenderer>();
        var data = holder?.sharedMaterial?.GetTexture("_BaseMap")?.TryCast<Texture2D>();
        if (data == null)
            return System.Array.Empty<LightProbe>();
        var pixels = data.GetPixels();
        var probes = new LightProbe[data.height];
        for (int i = 0; i < probes.Length; i++)
        {
            var row = i * data.width;
            var head = pixels[row];
            var sh = new Vector4[7];
            for (int k = 0; k < 7; k++)
                sh[k] = pixels[row + 1 + k];
            probes[i] = new LightProbe(new Vector3(head.r, head.g, head.b), head.a, sh);
        }
        return probes;
    }

    // The probes Womenace.EditorTools.BakeRoom writes under "_ReflectionProbes": each child's
    // position and scale are its box, its name its importance, its material its cube.
    private static ReflectionProbe[] ReadReflectionProbes(GameObject room)
    {
        var root = room.transform.Find("_ReflectionProbes");
        if (root == null)
            return System.Array.Empty<ReflectionProbe>();
        var probes = new List<ReflectionProbe>();
        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            var cube = child.GetComponent<MeshRenderer>()?.sharedMaterial?.GetTexture("_ReflectionCube");
            if (cube != null && int.TryParse(child.name, out var importance))
                probes.Add(new ReflectionProbe(new Bounds(child.localPosition, child.localScale), importance, cube));
        }
        return probes.ToArray();
    }

    // Unity's choice, as the bake makes it for the room's own renderers: the most important probe
    // whose box holds the point, the smaller box on a tie, else the room's widest probe.
    private static Texture PickReflection(RoomSet set, Vector3 local)
    {
        ReflectionProbe? best = null, widest = null;
        foreach (var p in set.ReflectionProbes)
        {
            if (widest == null || Volume(p.Reach) > Volume(widest.Value.Reach))
                widest = p;
            if (!p.Reach.Contains(local))
                continue;
            if (best == null || p.Importance > best.Value.Importance
                || (p.Importance == best.Value.Importance && Volume(p.Reach) < Volume(best.Value.Reach)))
                best = p;
        }
        return (best ?? widest)?.Cube;

        static float Volume(Bounds b) => b.size.x * b.size.y * b.size.z;
    }

    // GFL2 lights its characters (and anything else it does not lightmap) from the scene's light
    // probes, so whatever stands in a room takes the probes' light at its feet: the four nearest,
    // weighted by inverse square distance, handed to its renderers as Unity's per-renderer SH
    // (the doll shaders read unity_SHAr..SHC). Womenace/BakedScene props take the same values
    // through their own probe inputs.
    private static readonly string[] ShNames = { "unity_SHAr", "unity_SHAg", "unity_SHAb", "unity_SHBr", "unity_SHBg", "unity_SHBb", "unity_SHC" };

    // The four light probes nearest a place in the set's space, summed by inverse square
    // distance: their SH constants, their word on the sun, and the weights' total to divide by.
    private static (Vector4[] Sh, float Occlusion, float Total) BlendProbes(RoomSet set, Vector3 local)
    {
        var nearest = new List<(LightProbe Probe, float Weight)>(4);
        foreach (var probe in set.LightProbes)
        {
            var d = (probe.Position - local).sqrMagnitude;
            var w = 1f / Mathf.Max(d, 1e-4f);
            if (nearest.Count < 4)
                nearest.Add((probe, w));
            else
            {
                var weakest = 0;
                for (int i = 1; i < 4; i++)
                    if (nearest[i].Weight < nearest[weakest].Weight)
                        weakest = i;
                if (w > nearest[weakest].Weight)
                    nearest[weakest] = (probe, w);
            }
        }
        var sh = new Vector4[7];
        float total = 0f, occlusion = 0f;
        foreach (var (probe, w) in nearest)
        {
            for (int k = 0; k < 7; k++)
                sh[k] += probe.Sh[k] * w;
            occlusion += probe.SunOcclusion * w;
            total += w;
        }
        return (sh, occlusion, total);
    }

    private static void ProbeLight(GameObject unit, RoomSet set, float sun = 0f, Vector3? keyFrom = null, Color? keyColour = null)
    {
        if (unit == null || set?.Root == null || set.LightProbes.Length == 0)
            return;
        var local = set.Root.transform.InverseTransformPoint(unit.transform.position + Vector3.up * 0.9f);
        var (sh, occlusion, total) = BlendProbes(set, local);
        var reflection = PickReflection(set, local);
        // GFL2 draws its room characters at full detail. The bake's decimated LODs switch in at the
        // screen heights a battlefield sees, which is most of a room's dolls.
        foreach (var lod in unit.GetComponentsInChildren<LODGroup>(true))
            lod.ForceLOD(0);
        var block = new MaterialPropertyBlock();
        foreach (var renderer in unit.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.TryCast<ParticleSystemRenderer>() != null)
                continue;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.CustomProvided;
            renderer.GetPropertyBlock(block);
            for (int k = 0; k < 7; k++)
            {
                block.SetVector(ShNames[k], sh[k] / total);
                block.SetVector("_ProbeSH" + k, sh[k] / total);
            }
            block.SetFloat("_UseProbes", 1f);
            // A BakedScene prop's flat ambient level gives way to the probes.
            block.SetFloat("_LightmapScale", 1f);
            block.SetFloat("_ProbeOcclusion", occlusion / total);
            // The share of a mixed sun the doll shader applies to its key light: what the probes
            // let through, or what the caller gives the place, whichever is more.
            block.SetFloat("_KeyOcclusion", Mathf.Max(sun, occlusion / total));
            // The way her key comes from, where it is a window's light and not the sun's.
            block.SetVector("_KeyDirection", keyFrom is { } from ? new Vector4(from.x, from.y, from.z, 1f) : Vector4.zero);
            // The face shader's frame: GFL2's sweep reads the light in the skin's root space, and
            // the rooms' bodies are rooted at a bone standing upright with them, where the rig
            // the shader's frame is for has its root turned half over.
            var root = renderer.TryCast<SkinnedMeshRenderer>()?.rootBone;
            block.SetFloat("_FaceRootUpright", root != null && Vector3.Dot(root.up, unit.transform.up) > 0f ? 1f : 0f);
            // Her key's colour, where it is the lamps' light and not the sun's.
            block.SetVector("_KeyColour", keyColour is { } c ? new Vector4(c.r, c.g, c.b, 1f) : Vector4.zero);
            // The doll shaders' room terms: GFL2's own ramp and ambient, and the room's reflection
            // probe in place of the sky.
            block.SetFloat("_RoomLit", 1f);
            if (reflection != null)
            {
                block.SetTexture("_RoomReflection", reflection);
                block.SetFloat("_UseRoomReflection", 1f);
            }
            renderer.SetPropertyBlock(block);
        }
    }

    // Destroys what the sets made. The sets themselves go with the scene (DioramaScene).
    private void ClearSets()
    {
        foreach (var set in Sets)
        {
            if (set.Root != null)
                UnityEngine.Object.Destroy(set.Root);
            if (set.Volume != null)
            {
                DestroyProfile(set.Volume.GetComponent<UnityEngine.Rendering.Volume>()?.sharedProfile);
                UnityEngine.Object.Destroy(set.Volume);
            }
            if (set.Grade != null)
                UnityEngine.Object.Destroy(set.Grade);
        }
        for (var i = 0; i < MaxLiveLights; i++)
            SetLiveLight(i, Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero);
        Shader.SetGlobalVector("_WmFogLinear", new Vector4(0f, 0f, 1e6f, 1e7f));
        Shader.SetGlobalVector("_WmAo", Vector4.zero);
        Shader.SetGlobalVector("_WmReflectionShade", Vector4.zero);
        Shader.SetGlobalFloat("_WmDebugTerm", 0f);
        Shader.SetGlobalFloat(RoomShadeId, _roomShade = 0f);
    }
}
