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
// it. A set carrying that box has the mirror camera render the box alone. The refitting room has
// no such box and mirrors the live room.
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
        var resolution = MirrorResolution();
        var live = resolution > 0f && set?.Root != null && set.Root.activeInHierarchy && _roomCamera != null && _roomCamera.isActiveAndEnabled;
        if (!live)
        {
            Shader.SetGlobalVector(PlanarParamsId, Vector4.zero);
            return;
        }
        var look = set.Look;
        var planeY = set.Root.transform.TransformPoint(0f, set.MirrorFloor, 0f).y;
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
        var box = set.MirrorBox;
        _mirrorCamera.cullingMask = box != null ? 1 << ReflectionVolumeLayer : _roomCamera.cullingMask;

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
        // The box draws only in this render: the room camera must never see it.
        if (box != null)
            box.enabled = true;
        try
        {
            _mirrorCamera.Render();
        }
        finally
        {
            if (box != null)
                box.enabled = false;
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
