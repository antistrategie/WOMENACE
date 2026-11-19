using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace WOMENACE.Code;

// The 3D room behind the strategy layer. The strategy layer runs in the MissionPreparation scene,
// where the UI manager gives each screen its own background prefab and camera. The rooms
// (StrategyDiorama.Rooms.cs) are spawned far below all of them, with their own camera at the ops
// room's _CameraAnchor (the source scene's camera pose); StrategyDiorama.Screens.cs decides per
// screen whether a room or the vanilla background is shown.
//
// If anything fails the rooms are removed and the strategy layer stays exactly as vanilla.
public sealed partial class StrategyDiorama : JiangyuSystem
{
    internal static bool Enabled = true;
    private const int VolumeLayer = 31;
    // The ops room's camera: GFL2's MainCamera field of view at the room's _CameraAnchor.
    internal const float RoomFov = 48.6f;

    private static StrategyDiorama _instance;

    public override void OnInit()
    {
        _instance = this;
        HookScreens();
    }

    public override void OnUnload()
    {
        Affinity.Changed -= _staging.SquadAffinityChanged;
        Remove();
        _instance = null;
    }

    // A scene change destroys the rooms, the camera and the mirror camera (scene objects) but not
    // what the diorama made outside the scene, so everything is torn down as on removal.
    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        // A room that would not spawn in the last scene is tried again in this one.
        _spawnFailedIn = null;
        Remove();
    }

    // The room's work every frame, for as long as it stands, with or without a shell on the
    // screen: its camera, sounds and music, and every station's drive. A drive that throws is
    // reported once and the others go on.
    private System.Collections.IEnumerator RoomDriver(GameObject room)
    {
        var failed = false;
        while (room != null && _scene.Room == room)
        {
            try
            {
                DriveRoom();
            }
            catch (Exception ex)
            {
                if (!failed)
                    Context.Log.Warn($"strategy diorama: room drive failed: {ex.GetType().Name}: {ex.Message}");
                failed = true;
            }
            yield return null;
        }
    }

    private void DriveRoom()
    {
        ApplyShadowQuality();
        DriveLand();
        DriveTableGrid();
        DriveRoomShade();
        DrivePlanet();
        DriveStarMap();
        DriveSquads();
        DriveRecruit();
        DrivePrepMap();
        DrivePrep();
        DrivePrepFlat();
        DrivePrepCamera();
        DrivePlanetCamera();
        DriveOci();
        DriveMarket();
        DriveShop();
        DriveMusic();
        DriveTableSounds();
        DriveRoomCamera();
        DriveRoomDolls();
        DrivePrepInput();
        DrivePrepTokens();
        // The table's markers follow the camera and the selection.
        if (_missions.Markers.Count > 0)
            RefreshMarkers(false);
    }

    private const int RoomSimulatedLod = 4;

    private void Spawn()
    {
        try
        {
            _scene.Room = SpawnSet(_scene.Ops);
            if (_scene.Room == null)
                return;
            // The rooms' few dolls are small on screen and still have their hair and cloth simulated.
            SpringBoneSystem.MaxSimulatedLod = RoomSimulatedLod;
            _scene.Anchor = _scene.Room.transform.Find("_CameraAnchor");
            if (_scene.Anchor == null)
                throw new InvalidOperationException("the ops room has no _CameraAnchor");
            SpawnCamera();
            SpawnDinergate();
            SpawnRoomDolls();
            SpawnMirror();
            ActivateSet(_scene.Ops);
            // Before the first screen's shell, so on each frame the room has moved before the
            // shell places what it lays over it.
            Context.Coroutines.Start(RoomDriver(_scene.Room));
            // The UI manager has already set up the first screen's background by now.
            ApplyScreenPolicy();
            Context.Log.Info($"strategy diorama: spawned, camera at depth {_scene.Camera.GetComponent<Camera>().depth}, {Camera.allCamerasCount} cameras in scene");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: spawn failed, keeping vanilla: {ex}");
            Remove();
        }
    }

    private void SpawnCamera()
    {
        var depth = 0f;
        foreach (var other in Camera.allCameras)
            if (other != null)
                depth = Mathf.Max(depth, other.depth);
        _scene.Camera = new GameObject("WOMENACE_StrategyRoomCamera");
        _scene.Camera.transform.SetPositionAndRotation(_scene.Anchor.position, _scene.Anchor.rotation);
        var camera = _scene.Camera.AddComponent<Camera>();
        camera.fieldOfView = RoomFov;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 500f;
        camera.depth = depth + 1f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        // The game applies its quality settings to every camera whenever a screen opens and expects
        // HDRP camera data and a Volume beside each one. The room camera reads volumes on its own
        // layer only, so neither it nor the game's cameras touch the other's.
        _scene.Camera.layer = VolumeLayer;
        var hdCamera = _scene.Camera.AddComponent<HDAdditionalCameraData>();
        hdCamera.volumeLayerMask = 1 << VolumeLayer;
        // Past the end of a set's outdoor scenery a window shows the camera's own background:
        // the pale of the sky at the horizon, where HDRP's own is a deep blue.
        hdCamera.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
        hdCamera.backgroundColorHDR = new Color(0.52f, 0.68f, 0.75f);
        Context.Coroutines.Start(KeepRoomPost(hdCamera));

        // The camera's own volume, under each set's: GFL2's neutral exposure, no sky (reflections
        // come from the room's probes alone), no HDRP fog (Womenace/BakedScene fogs the room as GFL2
        // does), no screen-space AO, and the shadow light's settings.
        var volume = _scene.Camera.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AddFixedExposure(profile, NeutralExposureEv);
        SetParameter(AddVolumeComponent<VisualEnvironment>(profile).skyType, 0);
        SetParameter(AddVolumeComponent<UnityEngine.Rendering.HighDefinition.Fog>(profile).enabled, false);
        // Off: the AO texture Womenace/BakedScene would read keeps what has left the shot.
        SetParameter(AddVolumeComponent<ScreenSpaceAmbientOcclusion>(profile).intensity, 0f);
        SpawnShadowLight(profile);
        volume.sharedProfile = profile;
        // The player's anti-aliasing and post quality from the first frame, not only from the next
        // screen that opens.
        Il2CppMenace.PlayerSettings.PersistentPlayerSettings.DisplaySettings.ApplyQualitySettings(_scene.Camera.GetComponent<Camera>(), volume);
    }

    // Volume components are created and set as plain objects: the game's IL2CPP build carries no
    // instance of VolumeProfile.Add<T> or VolumeParameter<T>.Override for most of them.
    private static T AddVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var component = ScriptableObject.CreateInstance<T>();
        component.active = true;
        profile.components.Add(component);
        return component;
    }

    private static void SetParameter<T>(VolumeParameter<T> parameter, T value)
    {
        parameter.overrideState = true;
        parameter.value = value;
    }

    // A fixed exposure (HDRP's default auto exposure would fight every value the rooms carry).
    private static void AddFixedExposure(VolumeProfile profile, float ev)
    {
        var exposure = AddVolumeComponent<Exposure>(profile);
        SetParameter(exposure.mode, ExposureMode.Fixed);
        SetParameter(exposure.fixedExposure, ev);
    }

    private void Remove()
    {
        ClearHome();
        StopRoomBed();
        RestTableCamera(null);
        UnstageArmory();
        ClearMirror();
        ClearShadowLight();
        ClearWorkshop();
        ClearOci();
        ClearMarket();
        AbandonPull();
        ShowMachine(0f, 0f, 0f);
        ClearShopCart();
        ClearSets();
        if (_scene.Camera != null)
        {
            DestroyProfile(_scene.Camera.GetComponent<Volume>()?.sharedProfile);
            UnityEngine.Object.Destroy(_scene.Camera);
        }
        ClearMarkers();
        ClearPlanet();
        ReleaseCards();
        ClearStarMap();
        ClearTable();
        ReleaseTableTarget();
        ClearPrepMap();
        ReleasePrepTokens();
        SpringBoneSystem.ResetSimulatedLod();
        // A vanilla background hidden for a room screen comes back with the room gone.
        if (_hiddenVanilla != null && !_hiddenVanilla.activeSelf)
            _hiddenVanilla.SetActive(true);
        _hiddenVanilla = null;
        _lastShellScreen = null;
        // The next room starts with its own camera.
        DropShot();
        StrategyMusicSystem.Rest();
        // The next scene starts from nothing: no room, no camera, no arrival yet.
        _scene = new DioramaScene();
    }

    // A runtime volume profile and its components, which outlive the objects holding them.
    private static void DestroyProfile(VolumeProfile profile)
    {
        if (profile == null)
            return;
        foreach (var component in profile.components)
            if (component != null)
                UnityEngine.Object.Destroy(component);
        UnityEngine.Object.Destroy(profile);
    }

    private static void ReleaseTexture(ref RenderTexture texture)
    {
        if (texture != null)
        {
            texture.Release();
            UnityEngine.Object.Destroy(texture);
        }
        texture = null;
    }

    // The game's quality pass (DisplaySettings.ApplyToAllCameras, a struct method the interop cannot
    // patch) gives every camera custom frame settings with post-processing off whenever a screen
    // opens: the room would lose its grade, bloom and tonemapping. The room camera keeps HDRP's
    // defaults, where its own volumes carry the post, checked each frame for as long as it exists.
    private System.Collections.IEnumerator KeepRoomPost(HDAdditionalCameraData camera)
    {
        while (camera != null)
        {
            if (camera.customRenderingSettings)
                camera.customRenderingSettings = false;
            yield return null;
        }
    }

    internal static GameObject Room => _instance?._scene.Room;
    internal static ModContext SystemContext => _instance?.Context;

    internal static Camera RoomCamera => _instance?._scene.Camera?.GetComponent<Camera>();

}
