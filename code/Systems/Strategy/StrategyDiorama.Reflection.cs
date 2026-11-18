using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace WOMENACE.Code;

// The floor's planar reflection, as GFL2 stages it: its planar reflection volume renders a blurred
// mirror image, which the ops room's RerlectionAdd overlay adds over the pit with a tint and a
// fresnel, and which the refitting room's polished floor shows directly. Here a second camera
// mirrors the room camera below the active set's floor plane and renders into a texture every frame
// (its resolution set by the player's shadow quality). Womenace/BakedScene samples it in screen space
// on surfaces lying in that plane and adds it over their shading. The active set (StrategyDiorama.Rooms.cs) carries the
// floor, strength, blur and tint.
//
// The ops room's GFL2 mirror reflects one layer, a box showing a cube pre-baked from the room
// (Womenace/PrebakedCube), so its reflection is a frozen image with no characters or effects in
// it. For a set carrying that box, Womenace/BakedScene finds where the mirror would see the box
// through each floor pixel and reads the cube there, and no mirror is rendered: HDRP's work for
// a camera costs about as much again as the room (2 ms of 5 at 1440p) whatever its resolution.
// The refitting and weapon rooms have no such box and mirror the live room, with the passes that
// only refine the image left out.
//
// The mirror camera is a proper rotation (mirrored position and forward, up flipped), so nothing
// renders inside out: it keeps the reflection's right and turns its up over, so its image is the
// true reflection upside down, and the floor samples it flipped vertically. An oblique near plane
// on the floor drops everything below it.
public sealed partial class StrategyDiorama
{
    // The mirror re-renders the room every frame, a cost of the same kind as a shadow map, so it
    // follows the player's shadow quality: off at the lowest, then a quarter, half and full of the
    // screen's resolution.
    private static float MirrorResolution()
    {
        var quality = Il2CppMenace.PlayerSettings.PersistentPlayerSettings.DisplaySettings.ShadowQuality;
        return quality <= 0 ? 0f : quality == 1 ? 0.25f : quality == 2 ? 0.5f : 1f;
    }
    private const int ReflectionVolumeLayer = 30;

    private static readonly int PlanarTextureId = Shader.PropertyToID("_WmPlanarReflection");
    private static readonly int PlanarParamsId = Shader.PropertyToID("_WmPlanarParams");
    private static readonly int PlanarTintId = Shader.PropertyToID("_WmPlanarTint");
    private static readonly int PlanarCubeId = Shader.PropertyToID("_WmPlanarCube");
    private static readonly int PlanarCubeSizeId = Shader.PropertyToID("_WmPlanarCubeSize");
    private static readonly int PlanarBoxId = Shader.PropertyToID("_WmPlanarBox");

    // The mirror's image is blurred and added faintly over the floor, so the screen-space effects,
    // volumetrics, motion vectors and post effects that would refine it are left out of its render.
    // Its shadow maps stay, for the characters' shadows in it.
    private static readonly FrameSettingsField[] MirrorSkips =
    {
        FrameSettingsField.SSAO, FrameSettingsField.SSR, FrameSettingsField.TransparentSSR, FrameSettingsField.SSGI,
        FrameSettingsField.ContactShadows, FrameSettingsField.ScreenSpaceShadows, FrameSettingsField.Volumetrics,
        FrameSettingsField.ReprojectionForVolumetrics, FrameSettingsField.VolumetricClouds, FrameSettingsField.MotionVectors,
        FrameSettingsField.ObjectMotionVectors, FrameSettingsField.TransparentsWriteMotionVector, FrameSettingsField.Decals,
        FrameSettingsField.Refraction, FrameSettingsField.Distortion, FrameSettingsField.LowResTransparent,
        FrameSettingsField.SubsurfaceScattering, FrameSettingsField.Transmission, FrameSettingsField.StopNaN,
        FrameSettingsField.DepthOfField, FrameSettingsField.MotionBlur, FrameSettingsField.PaniniProjection,
        FrameSettingsField.Bloom, FrameSettingsField.LensFlareScreenSpace, FrameSettingsField.LensFlareDataDriven,
        FrameSettingsField.LensDistortion, FrameSettingsField.ChromaticAberration, FrameSettingsField.Vignette,
        FrameSettingsField.FilmGrain, FrameSettingsField.Dithering, FrameSettingsField.Antialiasing,
        FrameSettingsField.CustomPostProcess,
    };

    private GameObject _mirror;
    private Camera _mirrorCamera;
    private Camera _roomCamera;
    private RenderTexture _mirrorTexture;
    private object _mirrorRoutine;

    private void SpawnMirror()
    {
        if (_camera == null || _mirror != null)
            return;
        _roomCamera = _camera.GetComponent<Camera>();
        _mirror = new GameObject("WOMENACE_StrategyRoomMirror") { layer = ReflectionVolumeLayer };
        _mirrorCamera = _mirror.AddComponent<Camera>();
        _mirrorCamera.depth = _roomCamera.depth - 0.5f;
        _mirrorCamera.clearFlags = CameraClearFlags.SolidColor;
        _mirrorCamera.backgroundColor = Color.black;
        _mirrorCamera.nearClipPlane = _roomCamera.nearClipPlane;
        _mirrorCamera.farClipPlane = _roomCamera.farClipPlane;
        // Rendered by hand rather than left enabled: some screens (the armoury) switch off every
        // camera but their own each frame, which would freeze the reflection.
        _mirrorCamera.enabled = false;
        var hd = _mirror.AddComponent<HDAdditionalCameraData>();
        hd.volumeLayerMask = 1 << ReflectionVolumeLayer;
        hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
        hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
        hd.backgroundColorHDR = Color.black;
        SkipMirrorPasses(hd);

        // Its own volume: the room's exposure and no tonemapping or screen effects, so the texture
        // holds exposed linear light the floor can add straight in.
        var volume = _mirror.AddComponent<UnityEngine.Rendering.Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        AddFixedExposure(profile, NeutralExposureEv);
        SetParameter(AddVolumeComponent<Tonemapping>(profile).mode, TonemappingMode.None);
        SetParameter(AddVolumeComponent<Bloom>(profile).intensity, 0f);
        SetParameter(AddVolumeComponent<Vignette>(profile).intensity, 0f);
        SetParameter(AddVolumeComponent<FilmGrain>(profile).intensity, 0f);
        SetParameter(AddVolumeComponent<VisualEnvironment>(profile).skyType, 0);
        SetParameter(AddVolumeComponent<UnityEngine.Rendering.HighDefinition.Fog>(profile).enabled, false);
        volume.sharedProfile = profile;

        _mirrorRoutine = Context.Coroutines.Start(FollowMirror());
    }

    // The interop hands the frame settings out as copies, so their bits are set on the copies' own
    // words and the copies written back whole.
    private static void SkipMirrorPasses(HDAdditionalCameraData hd)
    {
        var settings = hd.m_RenderingPathCustomFrameSettings;
        var mask = hd.renderingPathCustomFrameSettingsOverrideMask;
        foreach (var field in MirrorSkips)
        {
            var bit = 1ul << ((int)field & 63);
            if ((int)field < 64)
            {
                mask.mask.data1 |= bit;
                settings.bitDatas.data1 &= ~bit;
            }
            else
            {
                mask.mask.data2 |= bit;
                settings.bitDatas.data2 &= ~bit;
            }
        }
        hd.m_RenderingPathCustomFrameSettings = settings;
        hd.renderingPathCustomFrameSettingsOverrideMask = mask;
        hd.customRenderingSettings = true;
    }

    private System.Collections.IEnumerator FollowMirror()
    {
        while (_mirror != null)
        {
            try
            {
                ApplyShadowQuality();
                UpdateMirror();
            }
            catch (Exception ex)
            {
                Context.Log.Warn($"strategy diorama: mirror failed: {ex.GetType().Name}: {ex.Message}");
                _mirrorRoutine = null;
                ClearMirror();
                yield break;
            }
            yield return null;
        }
    }

    // The frame the screen last changed. The mirror sits that frame out: vanilla switches its
    // background scenes after the screen policy has run, so for the rest of the frame the last
    // screen's sun can still be on beside the room's, and HDRP logs an error for a render with two
    // shadow-casting suns (seen going from O.C.I. to the armoury).
    private int _mirrorHeldFrame = -1;

    private void UpdateMirror()
    {
        if (UnityEngine.Time.frameCount <= _mirrorHeldFrame)
            return;
        var set = _active;
        var shown = set?.Root != null && set.Root.activeInHierarchy && _roomCamera != null && _roomCamera.isActiveAndEnabled;
        var resolution = MirrorResolution();
        if (!shown || (resolution <= 0f && set.MirrorCube == null))
        {
            Shader.SetGlobalVector(PlanarParamsId, Vector4.zero);
            return;
        }
        var look = set.Look;
        var planeY = set.Root.transform.TransformPoint(0f, set.MirrorFloor, 0f).y;
        if (set.MirrorCube != null)
        {
            // The box costs the floor a few sums, so it shows at every shadow quality. Its blur mip
            // is the live mirror's: a half-screen render, then the look's blur.
            Shader.SetGlobalColor(PlanarTintId, look.MirrorTint);
            Shader.SetGlobalTexture(PlanarCubeId, set.MirrorCube);
            Shader.SetGlobalFloat(PlanarCubeSizeId, set.MirrorCube.width);
            Shader.SetGlobalMatrix(PlanarBoxId, set.MirrorBox.transform.worldToLocalMatrix);
            Shader.SetGlobalVector(PlanarParamsId, new Vector4(planeY, look.MirrorStrength, look.MirrorBlur + 1f, 1f));
            return;
        }
        // The blur is a mip of a half-screen render: a larger render takes more mips for the same blur.
        var planar = new Vector4(planeY, look.MirrorStrength, Mathf.Max(0f, look.MirrorBlur + Mathf.Log(resolution / 0.5f, 2f)), 0f);
        Shader.SetGlobalColor(PlanarTintId, look.MirrorTint);

        var width = Mathf.Max(64, (int)(_roomCamera.pixelWidth * resolution));
        var height = Mathf.Max(64, (int)(_roomCamera.pixelHeight * resolution));
        if (_mirrorTexture == null || _mirrorTexture.width != width || _mirrorTexture.height != height)
        {
            _mirrorCamera.targetTexture = null;
            ReleaseTexture(ref _mirrorTexture);
            _mirrorTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf)
            {
                name = "WOMENACE_FloorReflection",
                useMipMap = true,
                autoGenerateMips = true,
            };
            Shader.SetGlobalTexture(PlanarTextureId, _mirrorTexture);
        }
        _mirrorCamera.targetTexture = _mirrorTexture;
        _mirrorCamera.cullingMask = _roomCamera.cullingMask;

        var eye = _roomCamera.transform.position;
        var forward = _roomCamera.transform.forward;
        var up = _roomCamera.transform.up;
        _mirror.transform.SetPositionAndRotation(
            new Vector3(eye.x, 2f * planeY - eye.y, eye.z),
            Quaternion.LookRotation(new Vector3(forward.x, -forward.y, forward.z), new Vector3(-up.x, up.y, -up.z)));
        // The room camera's own projection, not just its field of view: the game's quality pass
        // makes it a physical camera, whose sensor and gate fit change the frustum, and a mirror
        // built from the field of view alone lands its image shrunk and shifted off what it mirrors.
        _mirrorCamera.ResetProjectionMatrix();
        // The mirror's image is upside down, so a lens lifted up the frame is lifted down its own.
        var projection = _roomCamera.projectionMatrix;
        projection.m12 = -projection.m12;
        _mirrorCamera.projectionMatrix = projection;
        // Oblique near plane on the floor: keep only what stands above it.
        var toCamera = _mirrorCamera.worldToCameraMatrix;
        var point = toCamera.MultiplyPoint(new Vector3(0f, planeY, 0f));
        var normal = toCamera.MultiplyVector(Vector3.up).normalized;
        _mirrorCamera.projectionMatrix = _mirrorCamera.CalculateObliqueMatrix(new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(point, normal)));
        // The floor itself draws in the mirror render and must not read the texture it is being
        // drawn into, or each frame's reflection would carry the last.
        Shader.SetGlobalVector(PlanarParamsId, Vector4.zero);
        ShowNamePlates(false);
        try
        {
            _mirrorCamera.Render();
        }
        finally
        {
            ShowNamePlates(true);
        }
        Shader.SetGlobalVector(PlanarParamsId, planar);
    }

    private void ClearMirror()
    {
        Shader.SetGlobalVector(PlanarParamsId, Vector4.zero);
        if (_mirrorRoutine != null)
            Context.Coroutines.Stop(_mirrorRoutine);
        _mirrorRoutine = null;
        if (_mirror != null)
        {
            DestroyProfile(_mirror.GetComponent<UnityEngine.Rendering.Volume>()?.sharedProfile);
            UnityEngine.Object.Destroy(_mirror);
        }
        _mirror = null;
        _mirrorCamera = null;
        _roomCamera = null;
        ReleaseTexture(ref _mirrorTexture);
    }

}
