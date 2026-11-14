using UnityEngine;

namespace WOMENACE.Code;

// The armoury in the room. MENACE's armoury background (ArmoryScene) spawns the selected squad's
// 3D units under Squad_Full (Unit_01..09, infantry with their transmogged bodies) or Squad_Tanks
// (vehicles), and its ArmoryController respawns them into those slots whenever the selection
// changes. Rather than rebuild that, the armoury keeps the vanilla background alive and carries
// just those two roots into the refitting room's set: they stand at its armoury spot, scaled to the
// room's GFL2 heights, turned to face the room camera as they faced the vanilla one, and each unit
// lit by its own camera-relative doll rig, as a single doll is. The rest of ArmoryScene stops
// drawing (its renderers, its camera and the name labels, which track the vanilla camera), and
// everything goes back on leaving.
//
// The room camera orbits the squad: right-drag turns it, the wheel zooms, both ignored over UI
// panels. It starts like MENACE's armoury shot, further out and above, looking down but close
// enough to read the units, and stays in front of them (no swinging round to the empty side).
public sealed partial class StrategyDiorama
{
    private const string ArmoryScreen = "ArmoryUIScreen";
    private static readonly string[] SquadRoots = { "Squad_Full", "Squad_Tanks" };

    // Where the squad stands (in the refitting room's space) and the shot's starting orbit around
    // it. GFL2's own doll spot sits at the hangar's open front, which a wide squad or vehicle shot
    // looks straight past, so the squad stands further into the hall, with room behind it for the
    // camera: midway between the hall's side walls, so the orbit has the same room either way
    // and a vehicle's width clears both, and far enough short of the back wall for its length.
    internal static readonly Vector3 ArmoryStand = new(4.27f, 0f, -0.7f);
    internal const float ArmoryYaw = 20f;
    internal const float ArmoryPitch = 22f;
    internal const float ArmoryDistance = 9f;
    internal const float ArmoryFov = 40f;
    // The squad panel covers the right of the screen and the roster a strip at the left, so the
    // camera pans right by this share of its distance, which sets the squad midway between them.
    internal const float ArmoryPan = 0.165f;
    // How much room the framed footprint gets: 1 fills the frame's height edge to edge.
    internal const float ArmoryFrameMargin = 1.2f;
    // The highest the camera may stand above the stand floor, under the hangar's roof beams.
    internal const float ArmoryCeiling = 5.5f;
    // How far the orbit may turn from the starting shot, and how close and far it may zoom.
    private const float OrbitYawRange = 65f;
    private const float OrbitPitchMin = 8f, OrbitPitchMax = 62f;
    private const float OrbitDistanceMin = 2.2f, OrbitDistanceMax = 11f;
    private const float OrbitDegreesPerPixel = 0.25f;
    private const float OrbitZoomPerNotch = 0.1f;
    // How fast a turned camera closes on where it was asked to be: the part of the way left
    // that it covers in a second, as an exponent.
    private const float OrbitSettle = 12f;

    // The distance's factor for a roll of the wheel, and a frame's part of the way to where
    // the camera was asked to be. Every camera the player turns shares these.
    private static float OrbitZoom(float notches) => MathF.Exp(-notches * OrbitZoomPerNotch * 2f);

    private static float OrbitSmoothing() => 1f - MathF.Exp(-OrbitSettle * Time.unscaledDeltaTime);
    // Neighbouring units' rigs add up, so each unit's rig is dimmer than a lone doll's.
    internal const float SquadLightScale = 0.3f;
    // How what stands on the floor is lit and shot: the rig's strength, the nearest the framing
    // shot stands, how far right the camera pans as a share of its distance (which sets where on
    // screen its subject stands), the pitch it starts at and the flattest it turns to, how far
    // over the floor it aims (the middle of what stands there when not given), and where across
    // the screen its subject stands, in the shell's units from its left edge (the pan's own
    // place when not given).
    private readonly record struct StageShot(float LightScale, float Distance, float Pan, float Pitch, float PitchMin, float? AimHeight, float? ScreenX = null);

    private static readonly StageShot SquadShot = new(SquadLightScale, ArmoryDistance, ArmoryPan, ArmoryPitch, OrbitPitchMin, null);
    private StageShot _stageShot = SquadShot;

    // The pan that stands the shot's subject at its place across the screen, whatever the
    // screen's shape and however the UI is fitted to it: the place is measured on the screen's
    // own panel, from the shell's left edge.
    private float StagePan()
    {
        if (_stagePanFrame == Time.frameCount)
            return _stagePan;
        _stagePanFrame = Time.frameCount;
        return _stagePan = MeasureStagePan();
    }

    // Asked for several times a frame while the camera orbits, and measured once.
    private int _stagePanFrame = -1;
    private float _stagePan;

    private float MeasureStagePan()
    {
        if (_stageShot.ScreenX is not { } x)
            return _stageShot.Pan;
        var camera = _camera != null ? _camera.GetComponent<Camera>() : null;
        var width = _shellRoot?.panel?.visualTree?.worldBound.width ?? 0f;
        if (camera == null || !(width > 1f))
            return _stageShot.Pan;
        var across = (_shellRoot.worldBound.x + x) / width;
        return (0.5f - across) * 2f * Mathf.Tan(ArmoryFov * 0.5f * Mathf.Deg2Rad) * camera.aspect;
    }

    // The camera's travel from one staging to the next: where it left from and how far along it
    // is, 0 to 1. A frame the game spent changing screens counts as a short one, so the travel
    // is seen.
    private const float StageTravelSeconds = 0.8f;
    private const float StageLongestStep = 1f / 20f;
    private (Vector3 Position, Quaternion Rotation) _stageFrom;
    private float _stageTravel = 1f;

    // Where the camera stands while something is staged, or nothing when the hall is not showing.
    private (Vector3 Position, Quaternion Rotation)? StagePose() =>
        _stage != null && _camera != null ? (_camera.transform.position, _camera.transform.rotation) : null;

    private void TravelFrom((Vector3 Position, Quaternion Rotation)? from)
    {
        _stageTravel = from != null ? 0f : 1f;
        if (from is { } pose)
            _stageFrom = pose;
    }

    private sealed class StagedRoot
    {
        public Transform Root;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
    }

    private readonly List<StagedRoot> _stagedRoots = new();
    // The units under the staged roots, by pointer and active state, so a respawned squad is lit
    // and gripped on the frame it appears.
    private long _squadSignature;
    // Each unit renderer the room's probes light, with its own property block (null when it had
    // none), given back with the units.
    private readonly Dictionary<IntPtr, (Renderer Renderer, MaterialPropertyBlock Block)> _probeLitUnits = new();
    private readonly List<Renderer> _armoryHidden = new();
    private Camera _armoryCamera;
    private GameObject _armoryLabels;
    private GameObject _armoryVanilla;
    private Transform _stage;
    private float _orbitYaw, _orbitPitch, _orbitDistance;
    private float _orbitYawTarget, _orbitPitchTarget, _orbitDistanceTarget;
    private object _orbitRoutine;

    // The room's own dolls make way for the squad.
    private void HideRoomDolls()
    {
        foreach (var doll in _crew)
            if (doll != null)
                doll.SetActive(false);
    }

    private void StageArmory(GameObject vanilla)
    {
        if (vanilla == null || _room == null || _camera == null)
            return;
        if (_armoryVanilla == vanilla)
        {
            // The UI manager turns its background camera back on whenever it updates the screen.
            if (_armoryCamera != null)
                _armoryCamera.enabled = false;
            return;
        }
        var from = StagePose();
        UnstageArmory();
        TravelFrom(from);
        _armoryVanilla = vanilla;
        _stageShot = SquadShot;
        try
        {
            var refit = SpawnSet(_refitSet);
            if (refit != null)
                ActivateSet(_refitSet);
            _stage = (refit ?? _room).transform;
            if (refit != null)
            {
                MapHall(refit.transform);
            }
            var stand = _stage.TransformPoint(ArmoryStand);
            _armoryCamera = vanilla.GetComponentInChildren<Camera>(true);
            var vanillaEye = _armoryCamera != null ? _armoryCamera.transform.position : Vector3.zero;
            if (_armoryCamera != null)
                _armoryCamera.enabled = false;
            _armoryLabels = vanilla.transform.Find("Squad_Canvas")?.gameObject;
            if (_armoryLabels != null)
                _armoryLabels.SetActive(false);

            // Toward the starting room camera, flattened: the squad turns so the side the vanilla
            // camera saw faces it.
            var towardCamera = _stage.rotation * (Quaternion.Euler(0f, ArmoryYaw, 0f) * Vector3.back);
            foreach (var name in SquadRoots)
            {
                var root = vanilla.transform.Find(name);
                if (root == null)
                    continue;
                _stagedRoots.Add(new StagedRoot { Root = root, Position = root.position, Rotation = root.rotation, Scale = root.localScale });
                var seen = vanillaEye - root.position;
                seen.y = 0f;
                var turn = seen.sqrMagnitude > 1e-4f
                    ? Quaternion.FromToRotation(seen.normalized, towardCamera)
                    : Quaternion.identity;
                root.SetPositionAndRotation(stand, turn * root.rotation);
                root.localScale = root.localScale * RoomDollScale;
            }

            foreach (var renderer in vanilla.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || UnderSquad(renderer.transform))
                    continue;
                renderer.enabled = false;
                _armoryHidden.Add(renderer);
            }

            _orbitYaw = _orbitYawTarget = ArmoryYaw;
            _orbitPitch = _orbitPitchTarget = ArmoryPitch;
            _orbitDistance = _orbitDistanceTarget = ArmoryDistance;
            _orbitRoutine = Context.Coroutines.Start(Orbit());
            Context.Log.Info($"strategy diorama: armoury staged with {_stagedRoots.Count} squad roots, {_armoryHidden.Count} vanilla renderers hidden");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: armoury staging failed: {ex.GetType().Name}: {ex.Message}");
            UnstageArmory();
        }
    }

    private bool UnderSquad(Transform t)
    {
        foreach (var staged in _stagedRoots)
            if (staged.Root != null && t.IsChildOf(staged.Root))
                return true;
        return false;
    }

    private void UnstageArmory()
    {
        if (_orbitRoutine != null)
        {
            Context.Coroutines.Stop(_orbitRoutine);
            _orbitRoutine = null;
        }
        foreach (var staged in _stagedRoots)
        {
            if (staged.Root == null)
                continue;
            RemoveRigs(staged.Root);
            foreach (var lod in staged.Root.GetComponentsInChildren<LODGroup>(true))
                lod.ForceLOD(-1);
            staged.Root.SetPositionAndRotation(staged.Position, staged.Rotation);
            staged.Root.localScale = staged.Scale;
        }
        _stagedRoots.Clear();
        _roomLitUnits.Clear();
        _squadSignature = 0;
        foreach (var (renderer, block) in _probeLitUnits.Values)
        {
            if (renderer == null)
                continue;
            renderer.SetPropertyBlock(block);
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
        }
        _probeLitUnits.Clear();
        ClearGrips();
        foreach (var renderer in _armoryHidden)
            if (renderer != null)
                renderer.enabled = true;
        _armoryHidden.Clear();
        if (_armoryCamera != null)
            _armoryCamera.enabled = true;
        _armoryCamera = null;
        if (_armoryLabels != null)
            _armoryLabels.SetActive(true);
        _armoryLabels = null;
        _armoryVanilla = null;
        ClearRecruitStand();
        _stage = null;
        if (_active == _refitSet)
            ActivateSet(_ops);
    }

    // Each unit takes the set's light probes where it stands.
    private bool SquadChanged()
    {
        long signature = 17;
        foreach (var staged in _stagedRoots)
        {
            if (staged.Root == null || !staged.Root.gameObject.activeInHierarchy)
                continue;
            for (int i = 0; i < staged.Root.childCount; i++)
            {
                var slot = staged.Root.GetChild(i);
                for (int j = 0; j < slot.childCount; j++)
                {
                    var unit = slot.GetChild(j);
                    signature = signature * 31 + unit.Pointer.ToInt64() + (unit.gameObject.activeSelf ? 1 : 0);
                }
            }
        }
        if (signature == _squadSignature)
            return false;
        _squadSignature = signature;
        return true;
    }

    private void ProbeLightSquad()
    {
        var set = _active;
        foreach (var staged in _stagedRoots)
            if (staged.Root != null && staged.Root.gameObject.activeInHierarchy)
                for (int i = 0; i < staged.Root.childCount; i++)
                {
                    var slot = staged.Root.GetChild(i);
                    for (int j = 0; j < slot.childCount; j++)
                        if (slot.GetChild(j).gameObject.activeInHierarchy)
                        {
                            var unit = slot.GetChild(j).gameObject;
                            foreach (var renderer in unit.GetComponentsInChildren<Renderer>(true))
                                if (!_probeLitUnits.ContainsKey(renderer.Pointer))
                                {
                                    MaterialPropertyBlock own = null;
                                    if (renderer.HasPropertyBlock())
                                        renderer.GetPropertyBlock(own = new MaterialPropertyBlock());
                                    _probeLitUnits[renderer.Pointer] = (renderer, own);
                                }
                            ProbeLight(unit, set);
                        }
                }
    }

    // Whether a staged unit is shaded by the room itself: a doll's shaders take the room's light
    // probes and its sun, and the rig on top of them read as a spotlight on her. MENACE's own
    // soldiers and vehicles take neither, and keep the rig.
    // The answer is kept for a short while only: a doll's outfit body loads after she is staged,
    // and vanilla makes its units anew on each selection.
    private readonly Dictionary<IntPtr, (bool Lit, int Frame)> _roomLitUnits = new();
    private const int RoomLitRecheckFrames = 30;

    private bool TakesRoomLight(Transform unit)
    {
        var frame = Time.frameCount;
        if (_roomLitUnits.TryGetValue(unit.Pointer, out var known) && frame - known.Frame < RoomLitRecheckFrames)
            return known.Lit;
        var lit = false;
        foreach (var renderer in unit.GetComponentsInChildren<Renderer>(true))
        {
            var shader = renderer.sharedMaterial != null ? renderer.sharedMaterial.shader : null;
            if (shader != null && shader.name.StartsWith("Womenace/Doll", StringComparison.Ordinal))
            {
                lit = true;
                break;
            }
        }
        _roomLitUnits[unit.Pointer] = (lit, frame);
        return lit;
    }

    // Each unit (the element under every Unit_NN slot, or the vehicle) that the room does not
    // light carries its own rig.
    private void LightSquad()
    {
        foreach (var staged in _stagedRoots)
        {
            if (staged.Root == null || !staged.Root.gameObject.activeInHierarchy)
                continue;
            for (int i = 0; i < staged.Root.childCount; i++)
            {
                var slot = staged.Root.GetChild(i);
                for (int j = 0; j < slot.childCount; j++)
                {
                    var unit = slot.GetChild(j);
                    if (unit.gameObject.activeInHierarchy && !unit.name.StartsWith("WOMENACE_", StringComparison.Ordinal) && !TakesRoomLight(unit))
                        LightDoll(unit.gameObject, _stageShot.LightScale);
                }
            }
        }
    }

    // Vanilla parks its vehicle well behind its infantry, and the infantry well forward of their
    // root. Here whichever stands is centred on the stand, so a squad and a vehicle share the spot.
    // The controller respawns the units on every selection, so this runs on each refocus.
    private void CentreSquad()
    {
        foreach (var staged in _stagedRoots)
        {
            if (staged.Root == null || !staged.Root.gameObject.activeInHierarchy)
                continue;
            var has = false;
            var bounds = new Bounds();
            foreach (var renderer in staged.Root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer.TryCast<ParticleSystemRenderer>() != null)
                    continue;
                if (!has) { bounds = renderer.bounds; has = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!has)
                continue;
            var offset = _stage.TransformPoint(ArmoryStand) - bounds.center;
            offset.y = 0f;
            if (offset.sqrMagnitude > 0.01f)
                staged.Root.position += offset;
        }
    }

    // The squad's centre: the middle of its units' renderer bounds, or the stand spot when empty.
    // Also gives the squad's footprint radius across the floor, which sets how far back the camera
    // stands: an infantry squad fits the default shot, a vehicle needs more room.
    private Vector3 SquadFocus(out float radius)
    {
        radius = 0f;
        var has = false;
        var bounds = new Bounds();
        foreach (var staged in _stagedRoots)
        {
            if (staged.Root == null || !staged.Root.gameObject.activeInHierarchy)
                continue;
            foreach (var renderer in staged.Root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer.TryCast<ParticleSystemRenderer>() != null)
                    continue;
                if (!has) { bounds = renderer.bounds; has = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
        }
        var stand = _stage.TransformPoint(ArmoryStand);
        if (!has)
            return stand + Vector3.up * (_stageShot.AimHeight ?? 0.8f);
        radius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
        if (_stageShot.AimHeight is { } height)
            return new Vector3(bounds.center.x, stand.y + height, bounds.center.z);
        // Aim a little below the middle: the units' feet matter more than the air above them.
        return new Vector3(bounds.center.x, stand.y + Mathf.Min(bounds.extents.y, 1.6f) * 0.75f, bounds.center.z);
    }

    private System.Collections.IEnumerator Orbit()
    {
        var focus = SquadFocus(out var framed);
        var aim = focus;
        FrameSquad(framed);
        var (refocusAt, settledAt) = (0f, Time.unscaledTime + SquadSettleSeconds);
        var dragging = false;
        var goodYaw = ArmoryYaw;
        while (_camera != null && _stage != null)
        {
            // The controller respawns the units on every selection, so their grips and their light
            // are found again on the frame the new squad appears.
            if (SquadChanged())
            {
                TrackGrips();
                ProbeLightSquad();
                (refocusAt, settledAt) = (0f, Time.unscaledTime + SquadSettleSeconds);
            }
            // The squad is centred and the shot aimed at it while a new squad settles into its
            // pose, and then left alone: the units' outlines move as they breathe, and a shot
            // that followed them would never be still.
            if (Time.unscaledTime < settledAt && Time.unscaledTime >= refocusAt)
            {
                CentreSquad();
                aim = SquadFocus(out var radius);
                // A new selection (infantry to vehicle and back) re-frames the shot.
                if (Mathf.Abs(radius - framed) > 0.75f)
                {
                    framed = radius;
                    FrameSquad(radius);
                }
                refocusAt = Time.unscaledTime + 0.25f;
            }
            ReadOrbitInput(ref dragging);
            var smoothing = OrbitSmoothing();
            focus = Vector3.Lerp(focus, aim, smoothing);
            _orbitYaw = Mathf.Lerp(_orbitYaw, _orbitYawTarget, smoothing);
            _orbitPitch = Mathf.Lerp(_orbitPitch, _orbitPitchTarget, smoothing);
            _orbitDistance = Mathf.Lerp(_orbitDistance, _orbitDistanceTarget, smoothing);

            var rotation = _stage.rotation * Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);
            // The hangar's roof beams sit 6 m up: a far shot (a vehicle) stays under them.
            var headroom = _stage.TransformPoint(ArmoryStand).y + ArmoryCeiling - focus.y;
            var distance = Mathf.Min(_orbitDistance, headroom / Mathf.Max(0.1f, Mathf.Sin(_orbitPitch * Mathf.Deg2Rad)));
            // Where even the closest framing would stand the camera outside the hall, the orbit
            // stops at the last bearing that kept it inside.
            if (!InsideHall(focus, rotation, ref distance) || !InHall(focus + rotation * (Vector3.back * distance) + rotation * Vector3.right * (distance * StagePan())))
            {
                _orbitYaw = _orbitYawTarget = goodYaw;
                rotation = _stage.rotation * Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);
                distance = Mathf.Min(_orbitDistance, headroom / Mathf.Max(0.1f, Mathf.Sin(_orbitPitch * Mathf.Deg2Rad)));
                InsideHall(focus, rotation, ref distance);
            }
            else
                goodYaw = _orbitYaw;
            // And no further out than the hall's fittings leave room for: the camera comes in
            // along its own line until it stands clear of them.
            distance = ClearOfHall(focus, rotation, distance);
            var pan = rotation * Vector3.right * (distance * StagePan());
            var (eye, facing) = (focus + rotation * (Vector3.back * distance) + pan, rotation);
            // Between Squads and Recruit the camera travels from the shot it had to its new one.
            if (_stageTravel < 1f)
            {
                _stageTravel = Mathf.Min(1f, _stageTravel + HitchStep(StageLongestStep) / StageTravelSeconds);
                var along = Ease.InOutCubic(_stageTravel);
                eye = Vector3.Lerp(_stageFrom.Position, eye, along);
                facing = Quaternion.Slerp(_stageFrom.Rotation, facing, along);
            }
            _camera.transform.SetPositionAndRotation(eye, facing);
            var camera = _camera.GetComponent<Camera>();
            if (camera != null)
                SetLens(camera, ArmoryFov, 0f, 0f);
            LightSquad();
            // The controller shows the name labels again whenever it respawns the squad.
            if (_armoryLabels != null && _armoryLabels.activeSelf)
                _armoryLabels.SetActive(false);
            yield return null;
        }
    }

    // How far back the shot stands for a footprint of this radius: the default distance, or enough
    // for the footprint to fill the frame's height with a margin. The zoom-out limit follows it.
    private float _orbitDistanceMax = OrbitDistanceMax;

    // The refitting room's open floor, which the camera closes in on the squad rather than
    // leave, so a wide orbit never looks in through a wall: every cell of ground the squad's
    // spot connects to without crossing the room's walls and pillars, less a camera's margin
    // from them (MapHall). A room whose meshes cannot be read has no floor plan and no bound.
    private bool OnHallFloor(Vector3 local)
    {
        if (_hallFloor == null)
            return true;
        var (cx, cz) = ((int)((local.x + HallReach) / HallCell), (int)((local.z + HallReach) / HallCell));
        return cx >= 0 && cz >= 0 && cx < HallSide && cz < HallSide && _hallFloor[cx, cz];
    }

    // How long a newly shown squad is followed while it takes up its pose.
    private const float SquadSettleSeconds = 1.2f;

    // Where the room has surfaces, in the set's own space: a grid of cells marked from the
    // faces of the room's own meshes. Its floor plan (OnHallFloor) keeps the camera inside the
    // walls, and this keeps the room's props, pillars, door frames and roof beams from coming
    // between the camera and the squad. A piece's box will not do: a door frame's box is the
    // whole doorway.
    private const float HallCell = 0.4f;
    private const float HallReach = 30f;
    private const float HallHeight = 8f;
    private const int HallSide = (int)(HallReach * 2f / HallCell);
    private const int HallTall = (int)(HallHeight / HallCell);
    private bool[,,] _hallSolid;
    // The room's floor plan, by the same cells across and deep.
    private bool[,] _hallFloor;
    // Ground is a level face this near the squad's own height.
    private const float HallGroundBand = 0.3f;
    // A wall or a pillar stands through most of the height a camera moves in (the cells from
    // 0.4 m to 5.2 m up), where a crate or a workbench does not.
    private const int HallWallFrom = 1;
    private const int HallWallTo = 13;
    private const int HallWallCells = 9;
    // How many cells the floor plan keeps back from a wall.
    private const int HallFloorMargin = 2;
    private IntPtr _hallMapped;

    private void MapHall(Transform set)
    {
        if (_hallSolid != null && _hallMapped == set.Pointer)
            return;
        var (side, tall) = (HallSide, HallTall);
        var solid = new bool[side, tall, side];
        var ground = new bool[side, side];
        var (pieces, faces) = WalkSolidFaces(set, HallCell, 80, (p, level) =>
        {
            var (cx, cy, cz) = ((int)((p.X + HallReach) / HallCell), (int)(p.Y / HallCell), (int)((p.Z + HallReach) / HallCell));
            if (cx < 0 || cz < 0 || cx >= side || cz >= side)
                return;
            // The floor's own cells are left open: the squad stands there.
            if (cy >= 1 && cy < tall)
                solid[cx, cy, cz] = true;
            if (level && MathF.Abs(p.Y) < HallGroundBand)
                ground[cx, cz] = true;
        });
        (_hallSolid, _hallFloor, _hallMapped) = (solid, FloorPlan(solid, ground), set.Pointer);
        var cells = 0;
        if (_hallFloor != null)
            foreach (var cell in _hallFloor)
                if (cell)
                    cells++;
        Context.Log.Info($"strategy diorama: the refitting room is mapped from {faces} faces of {pieces} pieces, its floor {cells * HallCell * HallCell:F0} square metres");
    }

    // The floor the squad's spot connects to: ground cells with no wall over them and none
    // within the margin, spread out from the spot until walls close it in. Null when the spot
    // itself is not on such a cell, which leaves the camera unbounded by it.
    private static bool[,] FloorPlan(bool[,,] solid, bool[,] ground)
    {
        var side = HallSide;
        var open = new bool[side, side];
        for (var x = 0; x < side; x++)
            for (var z = 0; z < side; z++)
            {
                var wall = 0;
                for (var y = HallWallFrom; y < HallWallTo; y++)
                    if (solid[x, y, z])
                        wall++;
                open[x, z] = ground[x, z] && wall < HallWallCells;
            }
        bool Clear(int x, int z)
        {
            for (var dx = -HallFloorMargin; dx <= HallFloorMargin; dx++)
                for (var dz = -HallFloorMargin; dz <= HallFloorMargin; dz++)
                {
                    if (dx * dx + dz * dz > HallFloorMargin * HallFloorMargin)
                        continue;
                    var (a, b) = (x + dx, z + dz);
                    if (a < 0 || b < 0 || a >= side || b >= side || !open[a, b])
                        return false;
                }
            return true;
        }
        var start = ((int)((ArmoryStand.x + HallReach) / HallCell), (int)((ArmoryStand.z + HallReach) / HallCell));
        if (!Clear(start.Item1, start.Item2))
            return null;
        var floor = new bool[side, side];
        var reached = new Queue<(int X, int Z)>();
        floor[start.Item1, start.Item2] = true;
        reached.Enqueue(start);
        while (reached.Count > 0)
        {
            var (x, z) = reached.Dequeue();
            foreach (var (a, b) in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
            {
                if (a < 0 || b < 0 || a >= side || b >= side || floor[a, b] || !Clear(a, b))
                    continue;
                floor[a, b] = true;
                reached.Enqueue((a, b));
            }
        }
        return floor;
    }

    private bool HallSolidAt(Vector3 local) => HallSolidAt(new System.Numerics.Vector3(local.x, local.y, local.z));

    private bool HallSolidAt(System.Numerics.Vector3 local)
    {
        var (cx, cy, cz) = ((int)((local.X + HallReach) / HallCell), (int)(local.Y / HallCell), (int)((local.Z + HallReach) / HallCell));
        return cx >= 0 && cy >= 0 && cz >= 0 && cx < HallSide && cy < HallTall && cz < HallSide && _hallSolid[cx, cy, cz];
    }

    // How far back the camera stands clear of whatever first comes between it and the squad,
    // and how far apart the lines it looks along are.
    private const float HallClearance = 0.8f;
    private const float HallAbreast = 0.6f;

    private static System.Numerics.Vector3 Managed(Vector3 v) => new(v.x, v.y, v.z);

    // The furthest the camera can stand along its line, up to a distance, with nothing of the
    // hall walling it off from the squad. The line is walked out from the squad's own framing,
    // five abreast (the line and four beside it, above, below and to each side), and the camera
    // stops short of the first place where most of them meet a surface together: a wall, a door
    // header, a stack of crates. A cable, a rail or a hanging lamp crosses one of them and is
    // looked past, as the eye looks past it. The walk is in the set's space and in managed
    // maths: it runs every frame the armoury shows.
    private float ClearOfHall(Vector3 focus, Quaternion rotation, float distance)
    {
        if (_hallSolid == null || _stage == null)
            return distance;
        var closest = Mathf.Max(OrbitDistanceMin, _framedRadius * 1.6f);
        var at = Managed(_stage.InverseTransformPoint(focus));
        var back = Managed(_stage.InverseTransformDirection(rotation * Vector3.back + rotation * Vector3.right * StagePan()));
        var right = Managed(_stage.InverseTransformDirection(rotation * Vector3.right)) * HallAbreast;
        var up = Managed(_stage.InverseTransformDirection(rotation * Vector3.up)) * HallAbreast;
        var step = HallCell * 0.5f;
        for (var d = closest; d <= distance; d += step)
        {
            var on = at + back * d;
            var ahead = on + back * step;
            var met = 0;
            if (HallSolidAt(on) || HallSolidAt(ahead)) met++;
            if (HallSolidAt(on + right) || HallSolidAt(ahead + right)) met++;
            if (HallSolidAt(on - right) || HallSolidAt(ahead - right)) met++;
            if (HallSolidAt(on + up) || HallSolidAt(ahead + up)) met++;
            if (HallSolidAt(on - up) || HallSolidAt(ahead - up)) met++;
            if (met >= 4)
                return Mathf.Max(closest, d - HallClearance);
        }
        return distance;
    }

    private bool InHall(Vector3 point)
    {
        if (_stage == null)
            return true;
        var local = _stage.InverseTransformPoint(point);
        return OnHallFloor(local);
    }

    private bool InsideHall(Vector3 focus, Quaternion rotation, ref float distance)
    {
        if (_stage == null)
            return true;
        // Never closer than the footprint allows: a vehicle would swallow the camera.
        var closest = Mathf.Max(OrbitDistanceMin, _framedRadius * 1.6f);
        for (int i = 0; i < 24; i++)
        {
            var local = _stage.InverseTransformPoint(focus + rotation * (Vector3.back * distance));
            if (OnHallFloor(local))
                return true;
            if (distance * 0.92f <= closest)
                return false;
            distance *= 0.92f;
        }
        return false;
    }

    private float _framedRadius;

    // How far the orbit swings either side of the default bearing: the full range round a squad,
    // narrower round a vehicle, whose far shot would otherwise swing out through the hall's walls.
    private float YawRange() => Mathf.Lerp(OrbitYawRange, 25f, Mathf.InverseLerp(3.5f, 5.5f, _framedRadius));

    private float YawMin() => ArmoryYaw - YawRange();
    private float YawMax() => ArmoryYaw + YawRange();

    private void FrameSquad(float radius)
    {
        _framedRadius = radius;
        var fit = radius * ArmoryFrameMargin / Mathf.Sin(ArmoryFov * 0.5f * Mathf.Deg2Rad);
        var distance = Mathf.Max(_stageShot.Distance, fit);
        // A little further out than the framing shot and no more: further still, the camera
        // backs into the hall's fittings.
        _orbitDistanceMax = Mathf.Max(OrbitDistanceMax, distance * 1.12f);
        _orbitDistanceTarget = distance;
        // Standing that far back at the default pitch would put the camera through the roof, so a
        // wide footprint is shot flatter (the focus sits about a metre up).
        var flattest = Mathf.Asin(Mathf.Clamp01((ArmoryCeiling - 1f) / distance)) * Mathf.Rad2Deg;
        _orbitPitchTarget = Mathf.Clamp(Mathf.Min(_stageShot.Pitch, flattest), _stageShot.PitchMin, OrbitPitchMax);
        Context.Log.Info($"strategy diorama: armoury frames a {radius:F1} m footprint from {distance:F1} m at {_orbitPitchTarget:F0} degrees");
    }

    // A wheel's turn in notches. The input system reports a notch as 120 on Windows and as 1
    // elsewhere (and through some layers between), so a small figure is already in notches.
    internal static float WheelNotches(float raw) => Mathf.Abs(raw) >= 20f ? raw / 120f : raw;

    private void ReadOrbitInput(ref bool dragging)
    {
        Vector2 position, delta;
        bool held, pressed;
        float scroll;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            position = mouse.position.ReadValue();
            delta = mouse.delta.ReadValue();
            held = mouse.middleButton.isPressed;
            pressed = mouse.middleButton.wasPressedThisFrame;
            scroll = WheelNotches(mouse.scroll.ReadValue().y);
        }
        else
        {
            position = Input.mousePosition;
            delta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
            held = Input.GetMouseButton(1);
            pressed = Input.GetMouseButtonDown(1);
            scroll = Input.mouseScrollDelta.y;
        }
        // A drag starts only where the button went down off the UI.
        if (!held)
            dragging = false;
        else if (pressed)
            dragging = !PointerOverUi(position);
        if (dragging)
        {
            _orbitYawTarget = Mathf.Clamp(_orbitYawTarget + delta.x * OrbitDegreesPerPixel, YawMin(), YawMax());
            _orbitPitchTarget = Mathf.Clamp(_orbitPitchTarget - delta.y * OrbitDegreesPerPixel, _stageShot.PitchMin, OrbitPitchMax);
        }
        if (Mathf.Abs(scroll) > 0.01f && !PointerOverUi(position))
            _orbitDistanceTarget = Mathf.Clamp(_orbitDistanceTarget * OrbitZoom(scroll), Mathf.Max(OrbitDistanceMin, _framedRadius * 1.6f), _orbitDistanceMax);
    }

    // Whether a screen point lies over a drawn UI panel (anything with a background, or a scroll
    // view) rather than over the empty, see-through part of the screen.
    private static bool PointerOverUi(Vector2 screen)
    {
        foreach (var document in UnityEngine.Object.FindObjectsOfType<UnityEngine.UIElements.UIDocument>())
        {
            var root = document.rootVisualElement;
            var panel = root?.panel;
            if (panel == null || root.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None)
                continue;
            var point = UnityEngine.UIElements.RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            for (var element = panel.Pick(point); element != null && element != root; element = element.parent)
            {
                var style = element.resolvedStyle;
                if (style.backgroundColor.a > 0.05f || style.backgroundImage.texture != null
                    || style.backgroundImage.sprite != null || element.TryCast<UnityEngine.UIElements.ScrollView>() != null)
                    return true;
            }
        }
        return false;
    }

}
