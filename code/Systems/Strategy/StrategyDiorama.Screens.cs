using Il2CppMenace.UI;
using UnityEngine;

namespace WOMENACE.Code;

// Which strategy screens show the room. MENACE gives every screen a background prefab that the UI
// manager instantiates under itself, each with its own Main Camera, and enables exactly one camera
// (UIManager.UpdateBackgroundScene, then UpdateActiveCamera). Right after it decides, this applies
// the room policy for the active screen:
//
//   room screens  - the room and its camera on, the vanilla background object off (so it neither
//                   renders behind the room nor plays its video), the camera framed for the screen
//   other screens - the room off, the vanilla background left exactly as the manager set it
//
// The armoury is the exception: its vanilla background stays alive because it spawns the squad,
// which StrategyDiorama.Armory carries into the room. A screen not listed here (the O.C.I. ship,
// mission prep's desk) stays vanilla, and taking a screen off the list is the per-screen fallback.
public sealed partial class StrategyDiorama
{
    // Room-space camera framings (eye, look-at, field of view), by screen.
    // Shift slides the view across the screen, in parts of its width, without turning the camera:
    // what the camera looks straight at is drawn off the screen's middle and keeps its symmetry.
    // Lift does the same up and down, in parts of the screen's height: what the camera looks
    // straight at is drawn below the screen's middle.
    private readonly record struct Framing(Vector3 Eye, Vector3 Target, float Fov, float Shift = 0f, float Lift = 0f);

    // Down the holotable from its foot, for the screens whose vanilla background is the hologram.
    private static readonly Framing TableFraming = new(new Vector3(-9.0f, 2.2f, 0f), new Vector3(-15.5f, 0.0f, 0f), 55f);

    // Mission select: straight down the table from higher up, tilted down so the whole table is
    // in view, and shifted left of the selected mission's panel, with room beside each mission's
    // icon for its name. Much higher and the camera is in the ceiling.
    private static readonly Framing MissionFraming = new(new Vector3(-9.44f, 3.0f, 0f), new Vector3(-14.2f, -0.3f, 0f), 55f, 0.11f);

    // The system map: across the table from its left side, level with the star system standing
    // over it.
    private static readonly Framing SystemMapFraming = new(new Vector3(-14.4f, 1.5f, -6.0f), new Vector3(-14.4f, 1.2f, 0f), 50f);

    // O.C.I.: a little down on the ship from the table's right side, near enough that the ship
    // fills most of the frame's width, with the lens lifted so that it is drawn under the slots'
    // groups. The left side has a pillar in the way.
    private static readonly Framing OciFraming = new(new Vector3(-14.4f, 1.64f, 4.44f), new Vector3(-14.4f, 0.45f, 0f), 42f, 0f, 0.11f);

    // The black market: down on the table from high over its right side, so that the squares
    // lie in the lower part of the view with room over them for a tall block of cubes, and the
    // crew at the table's near side stand low in the frame.
    private static readonly Framing MarketFraming = new(new Vector3(-14.4f, 2.6f, 5.1f), new Vector3(-14.4f, 0.45f, 0f), 42f, 0f, -0.08f);

    private static readonly Dictionary<string, Framing?> RoomScreens = new()
    {
        // Framed by the station it is working at (FramingFor).
        [VanillaUi.Screens.Workshop] = null,
        [VanillaUi.Screens.MissionSelect] = MissionFraming,
        [VanillaUi.Screens.SystemMap] = SystemMapFraming,
        // An operation's briefing keeps the system map's view, and the star system grows in it.
        [VanillaUi.Screens.OperationSelect] = SystemMapFraming,
        [VanillaUi.Screens.MissionPrep] = PrepFraming,
        [VanillaUi.Screens.OperationResult] = null,
        [VanillaUi.Screens.Hiring] = null,
        [VanillaUi.Screens.BlackMarket] = MarketFraming,
        [VanillaUi.Screens.ShipUpgrades] = OciFraming,
        [ArmoryScreen] = null,
    };

    private GameObject _hiddenVanilla;
    private bool _spawning;
    private string _spawnFailedIn;
    private bool _roomShown;

    private void HookScreens()
    {
        VanillaUi.SelfCheck(Context);
        // The system map takes its picking camera as it opens, which the policy then settles.
        foreach (var target in new[] { VanillaUi.Targets.UpdateActiveCamera, VanillaUi.Targets.UpdateBackgroundScene, VanillaUi.Targets.SystemMapOpened })
            Context.Patches.Postfix(target.Type, target.Method, _ => ApplyScreenPolicy());
        HookBugReport();
        HookEscape();
        HookPrepFlat();
        HookMandatoryMessages();
        HookFactionWindow();
        HookMissionBoard();
        HookSquadPanel();
        HookPickerScroll();
        HookRecruit();
        HookOciInfo();
        Context.Coroutines.Start(WatchVideo());
    }

    private void ApplyScreenPolicy()
    {
        // A video of vanilla's plays on vanilla's own scene and camera, which the policy would
        // put away for a room. Nothing is changed under one: the policy is applied when it ends.
        if (_tableVideo)
            return;
        try
        {
            var manager = UIManager.Get();
            var screen = manager?.GetActiveScreen();
            var name = screen != null ? screen.GetIl2CppType().Name : null;
            // Kalina's shop is the workshop's screen over its own backing.
            var workshop = name == VanillaUi.Screens.Workshop;
            // Kept on every pass, shop or not, so that a change is seen once.
            _workshopStation = WorkshopStation;
            // Kalina's shop is at the ops room's holotable, with the capsule machine on Procurement.
            var shop = workshop && ShopSystem.IsShopOpen;
            var show = name != null && RoomScreens.ContainsKey(name);
            HideBugReportButton();
            if (name != _policyScreen)
            {
                _mirrorHeldFrame = UnityEngine.Time.frameCount;
                _earsKeenUntil = Time.unscaledTime + EarsKeenSeconds;
            }
            TrackHomeView(name);
            // A home screen's Home view is the room from its own camera, the hologram put away.
            var homeView = show && OnHomeView(name);
            // The strategy layer runs in MissionPreparation after a load but in the Title scene for
            // a new campaign, so the room spawns on the first room screen in whatever scene that is.
            if (_room == null && show && Enabled && !_spawning && _spawnFailedIn != UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
            {
                _spawning = true;
                try { Spawn(); } finally { _spawning = false; }
                // A room that would not spawn is not tried again in this scene: the layer stays vanilla.
                if (_room == null)
                    _spawnFailedIn = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            }
            if (_room == null || _camera == null)
            {
                ClearHome();
                return;
            }
            var vanilla = manager?.GetCurrentBackgroundScene();

            var onTable = show && HolotableScreens.Contains(name);
            // Squads and Recruit are both in the refitting room, round whoever stands on its floor.
            var recruit = show && name == VanillaUi.Screens.Hiring;
            var armory = show && name == ArmoryScreen;
            var staged = armory || recruit;
            if (!staged)
                UnstageArmory();
            else
                DropShot();
            if (show)
            {
                QuietVanillaAmbience(screen);
                if (!onTable && !armory && vanilla != null && vanilla.activeSelf)
                {
                    vanilla.SetActive(false);
                    _hiddenVanilla = vanilla;
                }
                // The armoury's own scene holds the squad, and is the one that stays on.
                if (armory && vanilla != null && _hiddenVanilla == vanilla)
                {
                    vanilla.SetActive(true);
                    _hiddenVanilla = null;
                }
                // The Workshop is in the weapon room, and in the ops room if that would not spawn.
                var set = workshop && !shop && SpawnSet(_weaponSet) != null ? _weaponSet : _ops;
                if (!staged)
                    ActivateSet(set);
                _camera.SetActive(true);
                // The sun lights the room's characters, and nothing of a screen that shows no room.
                if (_shadowLight != null)
                    _shadowLight.enabled = true;
                var camera = _camera.GetComponent<Camera>();
                if (camera != null)
                    camera.enabled = true;
                // The refitting room puts the room's dolls away, so they are placed only where they show.
                if (!staged)
                {
                    Frame(shop ? ShopSystem.IsProcurementOpen ? MachineFraming : ShopFraming
                        : homeView || (workshop && set == _ops) ? null : FramingFor(name), set.Root.transform);
                    if (set == _ops)
                        PlaceRoomDolls();
                }
                RestTableCamera(onTable ? vanilla : null, !homeView);
                // The system map's Operations view shows the star system over the table. Its Home
                // view is the room alone.
                SetStarMap(onTable && !homeView ? vanilla : null);

                if (staged)
                    HideRoomDolls();
                if (armory)
                    StageArmory(vanilla);
                else if (recruit)
                    StageRecruit();
            }
            else
            {
                RestTableCamera(null);
                SetStarMap(null);
                if (_shotLive && float.IsPositiveInfinity(_shotLeftAt))
                    _shotLeftAt = Time.unscaledTime;
                if (_active?.Root != null)
                    _active.Root.SetActive(false);
                _camera.SetActive(false);
                if (_shadowLight != null)
                    _shadowLight.enabled = false;
                // A vanilla background we hid for a room screen comes back when it is current again.
                if (_hiddenVanilla != null && _hiddenVanilla == vanilla && !_hiddenVanilla.activeSelf)
                    _hiddenVanilla.SetActive(true);
                _hiddenVanilla = null;
                if (NavigationMenu.IsOpen)
                    NavigationMenu.Close();
            }
            // The shell is worn by screens that show no room as well.
            ApplyHome(name, show || (name != null && ShellScreens.Contains(name)));
            if (show != _roomShown)
                Context.Log.Info($"strategy diorama: {name} {(show ? "shows the room" : "keeps its vanilla background")}");
            _roomShown = show;
        }
        catch (Exception ex)
        {
            ClearHome();
            Context.Log.Warn($"strategy diorama: screen policy failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Moves the room camera to a screen's framing, or the room's own shot when it has none.
    private void Frame(Framing? framing, Transform space)
    {
        var camera = _camera.GetComponent<Camera>();
        (Vector3 Position, Quaternion Rotation, float Fov, float Shift) shot;
        if (framing is { } f)
        {
            var eye = space.TransformPoint(f.Eye);
            var target = space.TransformPoint(f.Target);
            shot = (eye, Quaternion.LookRotation(target - eye, Vector3.up), f.Fov, f.Shift);
        }
        else
            shot = (_anchor.position, _anchor.rotation, RoomFov, 0f);
        // Vanilla asks for the same screen's camera many times over, which changes nothing.
        var same = _shotLive && Nullable.Equals(_shotFraming, framing);
        _shotFraming = framing;
        (_shotLiftFrom, _shotLift) = (_lensLift, framing?.Lift ?? 0f);
        if (same)
        {
            // Back at the view it left: the room is showing again, so its next view is travelled to.
            _shotLeftAt = float.PositiveInfinity;
            return;
        }
        // The camera travels to a new view of the room it is already showing, and cuts only when
        // it comes to the room from somewhere else (another set, or a screen without the room).
        var stayed = _shotLive && _shotSpace == space && Time.unscaledTime - _shotLeftAt < ShotLeaveSeconds;
        _shotSpace = space;
        _shotLive = true;
        _shotLeftAt = float.PositiveInfinity;
        _shot = shot;
        Context.Log.Info($"strategy diorama: camera {(stayed ? "travels" : "cuts")} to {_policyScreen ?? "none"}");
        _shotVia = null;
        if (stayed)
        {
            Cue(TableCue.Travel);
            _shotFrom = (_camera.transform.position, _camera.transform.rotation, camera.fieldOfView, _lensShift);
            _shotTravel = 0f;
            // Each room has a side that is not for showing: the weapon room's doorway opens on
            // nothing, and the ops room's back, behind the Dinergate, is not dressed. A turn
            // between two shots that would pass that side on its shorter way round goes the
            // other way, by the room's far end.
            Vector3? unseen = _weaponSet.Root != null && space == _weaponSet.Root.transform ? WeaponRoomDoorway
                : _room != null && space == _room.transform ? OpsRoomBack : null;
            if (unseen is { } side)
            {
                var away = space.TransformDirection(side);
                float Towards(Quaternion facing) => Vector3.Dot(facing * Vector3.forward, away);
                var most = float.MinValue;
                for (var part = 1; part < 10; part++)
                    most = Mathf.Max(most, Towards(Quaternion.Slerp(_shotFrom.Rotation, shot.Rotation, part / 10f)));
                if (most > Mathf.Max(Towards(_shotFrom.Rotation), Towards(shot.Rotation)) + 0.05f)
                {
                    var level = Quaternion.Slerp(_shotFrom.Rotation, shot.Rotation, 0.5f).eulerAngles.x;
                    _shotVia = Quaternion.LookRotation(-away, Vector3.up) * Quaternion.Euler(level > 180f ? level - 360f : level, 0f, 0f);
                }
            }
            return;
        }
        _shotTravel = 1f;
        _camera.transform.SetPositionAndRotation(shot.Position, shot.Rotation);
        SetLens(camera, shot.Fov, shot.Shift, _shotLift);
    }

    // The room camera's shot in the ops room: where it is going, where it left from and when. A
    // view that moves (mission preparation's orbit) keeps the shot up to date itself.
    private (Vector3 Position, Quaternion Rotation, float Fov, float Shift) _shot, _shotFrom;
    // How far along its travel the camera is, 0 to 1. It advances by each frame's own time, and
    // by a single short step over a frame the game spent loading, so a hitch never jumps the
    // camera and a game that only runs slowly still arrives.
    private float _shotTravel = 1f;
    // A frame's share of time for the mod's own motion: a frame that stalled for as long as a
    // hitch (a screen opening, a map being made) counts as a shorter one, so the motion is not
    // skipped past.
    // What a label says without the tags vanilla colours it with, whose own digits are not its figures.
    private static string Untagged(string text) => System.Text.RegularExpressions.Regex.Replace(text ?? "", "<[^>]*>", "");

    // A value gone towards another by no more than a step.
    private static float Towards(float value, float target, float step)
        => target > value ? MathF.Min(target, value + step) : MathF.Max(target, value - step);

    private static float HitchStep(float longest) => HitchStep(longest, longest);

    private static float HitchStep(float hitch, float counted)
    {
        var step = Time.unscaledDeltaTime;
        return step < hitch ? step : counted;
    }

    // The framing a room screen is seen from: mission select's planet has its own.
    private Framing? FramingFor(string name) =>
        name == VanillaUi.Screens.MissionSelect && CurrentPlanet() != null ? PlanetFraming
        : name == VanillaUi.Screens.Workshop ? WorkshopStations[WorkshopStation].Framing
        : RoomScreens[name];

    // The room the shot is in. A shot in another room is cut to.
    private Transform _shotSpace;
    // A facing the camera turns by on its way to the shot, where the straight turn would not do.
    private Quaternion? _shotVia;
    // The way out of the weapon room's doorway, in the room's space.
    private static readonly Vector3 WeaponRoomDoorway = Vector3.left;
    // The way to the ops room's back, past the Dinergate from the holotable, in the room's space.
    private static readonly Vector3 OpsRoomBack = Vector3.right;
    // Which of the Workshop's stations the camera was last sent to.
    private int _workshopStation;

    private const float ShotHitchSeconds = 0.1f;
    private const float ShotHitchStep = 1f / 60f;
    private bool _shotLive, _shotTravelling;
    private Framing? _shotFraming;
    // When the room last stopped showing. Vanilla passes through other screens between two of
    // the room's, and a pass that short is not a departure.
    private float _shotLeftAt = float.PositiveInfinity;
    private const float ShotLeaveSeconds = 0.75f;

    private AudioListener _ears;
    private float _earsCheckedAt;
    // For a moment after the screen changes the listeners are counted every frame: vanilla
    // switches its scene cameras then, and a wait for the next count is a gap in the music.
    private float _earsKeenUntil;
    private const float EarsKeenSeconds = 0.6f;

    // Vanilla's listeners are on its scene cameras, which are all off while the room shows, and
    // with nobody listening nothing is heard. The room camera listens whenever none of them does.
    private void KeepListening()
    {
        if (_camera == null || (Time.unscaledTime > _earsKeenUntil && Time.unscaledTime - _earsCheckedAt < 0.5f))
            return;
        _earsCheckedAt = Time.unscaledTime;
        if (_ears == null)
            _ears = _camera.GetComponent<AudioListener>() ?? _camera.AddComponent<AudioListener>();
        var others = false;
        foreach (var ear in UnityEngine.Object.FindObjectsOfType<AudioListener>())
            others |= ear != _ears && ear.enabled;
        if (_ears.enabled == others)
            _ears.enabled = !others;
    }

    // The camera is leaving the ops room's shots: its next view of the room is a cut.
    private void DropShot()
    {
        (_shotLive, _shotTravel) = (false, 1f);
        // A travel cut short leaves nothing half arrived.
        if (_shotTravelling)
        {
            _shotTravelling = false;
            SetOperationsFade(1f);
        }
    }

    // Each frame: the camera eases from where it left to its shot, and then holds the shot. The
    // holotable's hologram and the view's panels arrive over the travel's second half.
    private void DriveRoomCamera()
    {
        if (_camera == null || !_camera.activeSelf)
            return;
        KeepListening();
        if (!_shotLive)
            return;
        var camera = _camera.GetComponent<Camera>();
        if (camera == null)
            return;
        if (_shotTravel < 1f)
        {
            var step = HitchStep(ShotHitchSeconds, ShotHitchStep);
            _shotTravel = Mathf.Min(1f, _shotTravel + step / GlideSeconds);
        }
        var t = _shotTravel;
        if (t < 1f)
        {
            _shotTravelling = true;
            var k = Ease.InOutCubic(t);
            var facing = _shotVia is { } via
                ? Quaternion.Slerp(Quaternion.Slerp(_shotFrom.Rotation, via, k), Quaternion.Slerp(via, _shot.Rotation, k), k)
                : Quaternion.Slerp(_shotFrom.Rotation, _shot.Rotation, k);
            _camera.transform.SetPositionAndRotation(Vector3.Lerp(_shotFrom.Position, _shot.Position, k), facing);
            SetLens(camera, Mathf.Lerp(_shotFrom.Fov, _shot.Fov, k), Mathf.Lerp(_shotFrom.Shift, _shot.Shift, k), Mathf.Lerp(_shotLiftFrom, _shotLift, k));
            SetOperationsFade(Ease.OutCubic(Mathf.Clamp01((t - 0.5f) * 2f)));
            return;
        }
        // The view's hologram and panels are fully there whenever the camera is at rest.
        if (_shotTravelling || _markerFade < 1f)
        {
            _shotTravelling = false;
            SetOperationsFade(1f);
        }
        _camera.transform.SetPositionAndRotation(_shot.Position, _shot.Rotation);
        if (_lensShift != _shot.Shift || _lensLift != _shotLift || camera.fieldOfView != _shot.Fov)
            SetLens(camera, _shot.Fov, _shot.Shift, _shotLift);
    }

    private float _lensShift, _lensLift;
    // The lens's lift at the shot the camera is going to, and at the one it left.
    private float _shotLift, _shotLiftFrom;

    // The room camera's field of view and its lens shift, sideways and up. A shifted lens needs
    // the camera's physical properties, where the field of view comes from the focal length.
    private void SetLens(Camera camera, float fov, float shift, float lift)
    {
        (_lensShift, _lensLift) = (shift, lift);
        var shifted = _lensShift != 0f || _lensLift != 0f;
        // The shift is written even when it is none: the camera keeps drawing with the last one
        // it was given after its physical properties are switched off.
        camera.lensShift = new Vector2(_lensShift, _lensLift);
        camera.usePhysicalProperties = shifted;
        camera.fieldOfView = fov;
        if (shifted)
            camera.focalLength = Camera.FieldOfViewToFocalLength(fov, camera.sensorSize.y);
    }

    internal static void Reapply() => _instance?.ApplyScreenPolicy();

    // Screens whose vanilla background stays alive behind the room: the system map and an
    // operation's briefing. Their star system is copied over the holotable (StarMap), and vanilla's
    // screen still finds the operation under the pointer and places its planet names through the
    // scene's own camera, which stays on but draws nothing (RestTableCamera).
    private static readonly HashSet<string> HolotableScreens = new() { VanillaUi.Screens.SystemMap, VanillaUi.Screens.OperationSelect };

    private Camera _tableCamera;
    private int _tableMask;
    private RenderTexture _tableTarget;

    // Vanilla's own camera stays switched on: its screens look through the scene's main camera
    // to find what is under the pointer, and its video overlay plays on it. It is kept from
    // drawing over the room by drawing nothing, into a texture the size of the screen (so a
    // place on the screen is still the same place for it).
    private void RestTableCamera(GameObject vanilla, bool picking = true)
    {
        var mapCamera = vanilla != null ? vanilla.GetComponentInChildren<Camera>(true) : null;
        if (_tableCamera != mapCamera)
        {
            WakeTableCamera();
            _tableCamera = mapCamera;
            if (mapCamera != null)
                _tableMask = mapCamera.cullingMask;
        }
        if (mapCamera == null)
            return;
        if (_tableTarget == null || _tableTarget.width != Screen.width || _tableTarget.height != Screen.height)
        {
            if (_tableTarget != null)
            {
                _tableTarget.Release();
                UnityEngine.Object.Destroy(_tableTarget);
            }
            _tableTarget = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32) { name = "WOMENACE_TableCamera" };
        }
        mapCamera.targetTexture = _tableTarget;
        mapCamera.cullingMask = 0;
        SetTablePick(picking ? mapCamera : null);
    }

    // Vanilla's system map looks for the planet or operation under the pointer through the
    // camera its screen holds (m_Camera, taken from Camera.main as the screen opens), and looks
    // for nothing while it holds none. The Operations view hands it the scene's camera and the
    // Home view, which shows no star system, hands it none.
    private static void SetTablePick(Camera mapCamera)
    {
        var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<Il2CppMenace.UI.Strategy.SystemMapUIScreen>();
        if (screen != null && screen.m_Camera != mapCamera)
            screen.m_Camera = mapCamera;
    }

    // A video of vanilla's (a new campaign's introduction) plays on that camera. For as long as
    // one is up the camera is vanilla's again, drawing to the screen, the room's own camera
    // draws nothing under it, the screen policy changes nothing, and the shell is away with
    // its buttons, which would take the click that skips it.
    // Whether one is up is asked of vanilla every frame in every scene (a new campaign's strategy
    // layer runs in the Title scene), and never taken
    // from a video's start and finish. A new campaign's introduction is two videos: the
    // first's finish starts the second from inside itself, so anything told of the finish
    // hears of it after the second has started. It also starts while its scene is still
    // coming in, so the room, its camera and the shell may all be made under it, which is
    // why the screen is held for it every frame and not handed over once.
    private bool _tableVideo;

    private System.Collections.IEnumerator WatchVideo()
    {
        while (true)
        {
            try
            {
                var up = UIManager.Get()?.m_CurrentVideoOverlay;
                if (up != null)
                {
                    _tableVideo = true;
                    HoldVideo(up);
                }
                else if (_tableVideo)
                {
                    EndVideo();
                }
            }
            catch (Exception ex)
            {
                Context.Log.Warn($"strategy diorama: video watch failed: {ex.GetType().Name}: {ex.Message}");
            }
            yield return null;
        }
    }

    private void HoldVideo(Il2CppMenace.UI.VideoOverlay video)
    {
        var room = _camera != null ? _camera.GetComponent<Camera>() : null;
        if (room != null && room.enabled)
            room.enabled = false;
        if (_shell != null && _shell.style.display != UnityEngine.UIElements.DisplayStyle.None)
            _shell.style.display = UnityEngine.UIElements.DisplayStyle.None;
        // What was put away for a room is where the video plays.
        if (_hiddenVanilla != null)
        {
            _hiddenVanilla.SetActive(true);
            _hiddenVanilla = null;
        }
        // The camera it plays on, and vanilla's map camera, draw to the screen with what
        // they drew before the room took them.
        foreach (var plays in new[] { video.m_Camera, _tableCamera })
        {
            if (plays == null || plays.targetTexture == null)
                continue;
            plays.targetTexture = null;
            if (plays.cullingMask == 0 && _tableMask != 0)
                plays.cullingMask = _tableMask;
        }
    }

    // The video is over: the room has its camera and the shell back, and the policy is applied
    // to whatever screen it left.
    private void EndVideo()
    {
        _tableVideo = false;
        try
        {
            var room = _camera != null ? _camera.GetComponent<Camera>() : null;
            if (room != null)
                room.enabled = true;
            if (_shell != null)
                _shell.style.display = UnityEngine.UIElements.StyleKeyword.Null;
            ApplyScreenPolicy();
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: video hand-over failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void WakeTableCamera()
    {
        if (_tableCamera != null)
        {
            try
            {
                _tableCamera.targetTexture = null;
                _tableCamera.cullingMask = _tableMask;
            }
            catch { }
        }
        _tableCamera = null;
    }

    private void ReleaseTableTarget()
    {
        WakeTableCamera();
        if (_tableTarget != null)
        {
            _tableTarget.Release();
            UnityEngine.Object.Destroy(_tableTarget);
        }
        _tableTarget = null;
    }
}
