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
// everything goes back on leaving. The names lie on the floor in front of the units instead
// (StrategyDiorama.SquadNames.cs).
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

    // The pan that stands the shot's subject at its place across the screen, whatever the
    // screen's shape and however the UI is fitted to it: the place is measured on the screen's
    // own panel, from the shell's left edge.
    private float StagePan()
    {
        if (_staging.StagePanFrame == Time.frameCount)
            return _staging.CurrentStagePan;
        _staging.StagePanFrame = Time.frameCount;
        return _staging.CurrentStagePan = MeasureStagePan();
    }


    private float MeasureStagePan()
    {
        if (_staging.CurrentStageShot.ScreenX is not { } x)
            return _staging.CurrentStageShot.Pan;
        var camera = _scene.Camera != null ? _scene.Camera.GetComponent<Camera>() : null;
        var width = _shellRoot?.panel?.visualTree?.worldBound.width ?? 0f;
        if (camera == null || !(width > 1f))
            return _staging.CurrentStageShot.Pan;
        var across = (_shellRoot.worldBound.x + x) / width;
        return (0.5f - across) * 2f * Mathf.Tan(ArmoryFov * 0.5f * Mathf.Deg2Rad) * camera.aspect;
    }

    // The camera's travel from one staging to the next: where it left from and how far along it
    // is, 0 to 1. A frame the game spent changing screens counts as a short one, so the travel
    // is seen.
    private const float StageTravelSeconds = 0.8f;
    private const float StageLongestStep = 1f / 20f;

    // Where the camera stands while something is staged, or nothing when the hall is not showing.
    private (Vector3 Position, Quaternion Rotation)? StagePose() =>
        _staging.Stage != null && _scene.Camera != null ? (_scene.Camera.transform.position, _scene.Camera.transform.rotation) : null;

    private void TravelFrom((Vector3 Position, Quaternion Rotation)? from)
    {
        _staging.StageTravel = from != null ? 0f : 1f;
        if (from is { } pose)
            _staging.StageFrom = pose;
    }

    private sealed class StagedRoot
    {
        public Transform Root;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
    }


    // The room's own dolls make way for the squad.
    private void HideRoomDolls()
    {
        foreach (var doll in _life.Crew)
            if (doll != null)
                doll.SetActive(false);
    }

    private void StageArmory(GameObject vanilla)
    {
        if (vanilla == null || _scene.Room == null || _scene.Camera == null)
            return;
        if (_staging.ArmoryVanilla == vanilla)
        {
            // The UI manager turns its background camera back on whenever it updates the screen.
            if (_staging.ArmoryCamera != null)
                _staging.ArmoryCamera.enabled = false;
            return;
        }
        var from = StagePose();
        UnstageArmory();
        TravelFrom(from);
        _staging.ArmoryVanilla = vanilla;
        _staging.CurrentStageShot = SquadShot;
        try
        {
            var refit = SpawnSet(_scene.Refit);
            if (refit != null)
                ActivateSet(_scene.Refit);
            _staging.Stage = (refit ?? _scene.Room).transform;
            if (refit != null)
            {
                MapHall(refit.transform);
            }
            var stand = _staging.Stage.TransformPoint(ArmoryStand);
            _staging.ArmoryCamera = vanilla.GetComponentInChildren<Camera>(true);
            var vanillaEye = _staging.ArmoryCamera != null ? _staging.ArmoryCamera.transform.position : Vector3.zero;
            if (_staging.ArmoryCamera != null)
                _staging.ArmoryCamera.enabled = false;
            _staging.ArmoryLabels = vanilla.transform.Find("Squad_Canvas")?.gameObject;
            if (_staging.ArmoryLabels != null)
                _staging.ArmoryLabels.SetActive(false);

            // Toward the starting room camera, flattened: the squad turns so the side the vanilla
            // camera saw faces it.
            var towardCamera = _staging.Stage.rotation * (Quaternion.Euler(0f, ArmoryYaw, 0f) * Vector3.back);
            _staging.SquadFacing = towardCamera;
            foreach (var name in SquadRoots)
            {
                var root = vanilla.transform.Find(name);
                if (root == null)
                    continue;
                _staging.StagedRoots.Add(new StagedRoot { Root = root, Position = root.position, Rotation = root.rotation, Scale = root.localScale });
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
                _staging.ArmoryHidden.Add(renderer);
            }

            _staging.OrbitYaw = _staging.OrbitYawTarget = ArmoryYaw;
            _staging.OrbitPitch = _staging.OrbitPitchTarget = ArmoryPitch;
            _staging.OrbitDistance = _staging.OrbitDistanceTarget = ArmoryDistance;
            _staging.OrbitRoutine = Context.Coroutines.Start(Orbit());
            Context.Log.Info($"strategy diorama: armoury staged with {_staging.StagedRoots.Count} squad roots, {_staging.ArmoryHidden.Count} vanilla renderers hidden");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: armoury staging failed: {ex.GetType().Name}: {ex.Message}");
            UnstageArmory();
        }
    }

    private bool UnderSquad(Transform t)
    {
        foreach (var staged in _staging.StagedRoots)
            if (staged.Root != null && t.IsChildOf(staged.Root))
                return true;
        return false;
    }

    private void UnstageArmory()
    {
        if (_staging.OrbitRoutine != null)
        {
            Context.Coroutines.Stop(_staging.OrbitRoutine);
            _staging.OrbitRoutine = null;
        }
        foreach (var staged in _staging.StagedRoots)
        {
            if (staged.Root == null)
                continue;
            RemoveRigs(staged.Root);
            foreach (var lod in staged.Root.GetComponentsInChildren<LODGroup>(true))
                lod.ForceLOD(-1);
            staged.Root.SetPositionAndRotation(staged.Position, staged.Rotation);
            staged.Root.localScale = staged.Scale;
        }
        _staging.StagedRoots.Clear();
        ClearNamePlates();
        _staging.RoomLitUnits.Clear();
        _staging.SquadSignature = 0;
        foreach (var (renderer, block) in _staging.ProbeLitUnits.Values)
        {
            if (renderer == null)
                continue;
            renderer.SetPropertyBlock(block);
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
        }
        _staging.ProbeLitUnits.Clear();
        ClearGrips();
        foreach (var renderer in _staging.ArmoryHidden)
            if (renderer != null)
                renderer.enabled = true;
        _staging.ArmoryHidden.Clear();
        if (_staging.ArmoryCamera != null)
            _staging.ArmoryCamera.enabled = true;
        _staging.ArmoryCamera = null;
        if (_staging.ArmoryLabels != null)
            _staging.ArmoryLabels.SetActive(true);
        _staging.ArmoryLabels = null;
        _staging.ArmoryVanilla = null;
        ClearRecruitStand();
        _staging.Stage = null;
        if (_scene.Active == _scene.Refit)
            ActivateSet(_scene.Ops);
    }

    // Each unit takes the set's light probes where it stands.
    private bool SquadChanged()
    {
        long signature = 17;
        foreach (var staged in _staging.StagedRoots)
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
        if (signature == _staging.SquadSignature)
            return false;
        _staging.SquadSignature = signature;
        return true;
    }

    private void ProbeLightSquad()
    {
        var set = _scene.Active;
        foreach (var staged in _staging.StagedRoots)
            if (staged.Root != null && staged.Root.gameObject.activeInHierarchy)
                for (int i = 0; i < staged.Root.childCount; i++)
                {
                    var slot = staged.Root.GetChild(i);
                    for (int j = 0; j < slot.childCount; j++)
                        if (slot.GetChild(j).gameObject.activeInHierarchy)
                        {
                            var unit = slot.GetChild(j).gameObject;
                            foreach (var renderer in unit.GetComponentsInChildren<Renderer>(true))
                                if (!_staging.ProbeLitUnits.ContainsKey(renderer.Pointer))
                                {
                                    MaterialPropertyBlock own = null;
                                    if (renderer.HasPropertyBlock())
                                        renderer.GetPropertyBlock(own = new MaterialPropertyBlock());
                                    _staging.ProbeLitUnits[renderer.Pointer] = (renderer, own);
                                }
                            ProbeLight(unit, set);
                        }
                }
    }

    private const int RoomLitRecheckFrames = 30;

    private bool TakesRoomLight(Transform unit)
    {
        var frame = Time.frameCount;
        if (_staging.RoomLitUnits.TryGetValue(unit.Pointer, out var known) && frame - known.Frame < RoomLitRecheckFrames)
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
        _staging.RoomLitUnits[unit.Pointer] = (lit, frame);
        return lit;
    }

    // Each unit (the element under every Unit_NN slot, or the vehicle) that the room does not
    // light carries its own rig. A doll whose outfit body arrives after she is staged is not lit
    // by the room at first, and gives the rig up once she is.
    private void LightSquad()
    {
        foreach (var staged in _staging.StagedRoots)
        {
            if (staged.Root == null || !staged.Root.gameObject.activeInHierarchy)
                continue;
            for (int i = 0; i < staged.Root.childCount; i++)
            {
                var slot = staged.Root.GetChild(i);
                for (int j = 0; j < slot.childCount; j++)
                {
                    var unit = slot.GetChild(j);
                    if (!unit.gameObject.activeInHierarchy || unit.name.StartsWith("WOMENACE_", StringComparison.Ordinal))
                        continue;
                    if (TakesRoomLight(unit))
                        RemoveRig(unit.gameObject);
                    else
                        LightDoll(unit.gameObject, _staging.CurrentStageShot.LightScale);
                }
            }
        }
    }

    // Vanilla parks its vehicle well behind its infantry, and the infantry well forward of their
    // root. Here whichever stands is centred on the stand, so a squad and a vehicle share the spot.
    // The controller respawns the units on every selection, so this runs on each refocus.
    private void CentreSquad()
    {
        foreach (var staged in _staging.StagedRoots)
        {
            if (staged.Root == null || !staged.Root.gameObject.activeInHierarchy)
                continue;
            var has = false;
            var bounds = new Bounds();
            AddSquadBounds(staged.Root, ref bounds, ref has);
            if (!has)
                continue;
            var offset = _staging.Stage.TransformPoint(ArmoryStand) - bounds.center;
            offset.y = 0f;
            if (offset.sqrMagnitude > 0.01f)
                staged.Root.position += offset;
        }
    }

    // What a squad is measured by: its units' solid surfaces. A see-through renderer is an effect,
    // not the unit's shape: a vehicle's headlights carry halo quads metres across, which would
    // stand the camera far back and aim it off the hull.
    private static bool FramesSquad(Renderer renderer) =>
        renderer.enabled && renderer.TryCast<ParticleSystemRenderer>() == null
        && (renderer.sharedMaterial is not { } material || material.renderQueue < TransparentQueueStart);

    private static void AddSquadBounds(Transform root, ref Bounds bounds, ref bool has)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (!FramesSquad(renderer))
                continue;
            if (!has) { bounds = renderer.bounds; has = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
    }

    // The squad's centre: the middle of its units' renderer bounds, or the stand spot when empty.
    // Also gives how much room the squad takes in the shot, which sets how far back the camera
    // stands: its footprint's radius across the floor, or its height where it is taller than it
    // is wide (a walker). An infantry squad fits the default shot, a vehicle needs more room.
    private const float TallFraming = 1.6f;

    private Vector3 SquadFocus(out float radius)
    {
        radius = 0f;
        var has = false;
        var bounds = new Bounds();
        foreach (var staged in _staging.StagedRoots)
        {
            if (staged.Root == null || !staged.Root.gameObject.activeInHierarchy)
                continue;
            AddSquadBounds(staged.Root, ref bounds, ref has);
        }
        var stand = _staging.Stage.TransformPoint(ArmoryStand);
        if (!has)
            return stand + Vector3.up * (_staging.CurrentStageShot.AimHeight ?? 0.8f);
        // What stands over the aim counts for more than its height: the shot looks down, so
        // the top of a tall thing is nearer the camera than its foot.
        radius = Mathf.Max(new Vector2(bounds.extents.x, bounds.extents.z).magnitude, bounds.extents.y * TallFraming);
        if (_staging.CurrentStageShot.AimHeight is { } height)
            return new Vector3(bounds.center.x, stand.y + height, bounds.center.z);
        // Aim a little below the middle: the units' feet matter more than the air above them.
        return new Vector3(bounds.center.x, stand.y + Mathf.Min(bounds.extents.y, 2.4f) * 0.75f, bounds.center.z);
    }

    private System.Collections.IEnumerator Orbit()
    {
        var focus = SquadFocus(out var framed);
        var aim = focus;
        FrameSquad(framed);
        var (refocusAt, settledAt, posedAt) = (0f, Time.unscaledTime + SquadSettleSeconds, 0f);
        var dragging = false;
        var goodYaw = ArmoryYaw;
        var shown = 0f;
        while (_scene.Camera != null && _staging.Stage != null)
        {
            // The controller respawns the units on every selection, so their grips and their light
            // are found again on the frame the new squad appears. Its name plates are placed by
            // the settling below, which runs on the same frame.
            if (SquadChanged())
            {
                TrackGrips();
                HoldCarried();
                ProbeLightSquad();
                (refocusAt, settledAt, posedAt) = (0f, Time.unscaledTime + SquadSettleSeconds, Time.unscaledTime + SquadPoseSeconds);
            }
            // The squad is centred and the shot aimed at it while a new squad settles into its
            // pose, and then left alone: the units' outlines move as they breathe, and a shot
            // that followed them would never be still.
            if (Time.unscaledTime < settledAt && Time.unscaledTime >= refocusAt)
            {
                CentreSquad();
                PlaceNamePlates();
                aim = SquadFocus(out var radius);
                // A new selection (infantry to vehicle and back) re-frames the shot. A walker
                // takes little more room than a squad, and all of it upwards. Units measure
                // about 0.4 m wider on the frame they appear than a moment later, so the footprint
                // is read once they have settled: a doll changing her outfit keeps the shot she had.
                if (Time.unscaledTime >= posedAt && Mathf.Abs(radius - framed) > 0.25f)
                {
                    framed = radius;
                    FrameSquad(radius);
                }
                refocusAt = Time.unscaledTime + 0.25f;
            }
            ReadOrbitInput(ref dragging);
            var smoothing = OrbitSmoothing();
            focus = Vector3.Lerp(focus, aim, smoothing);
            _staging.OrbitYaw = Mathf.Lerp(_staging.OrbitYaw, _staging.OrbitYawTarget, smoothing);
            _staging.OrbitPitch = Mathf.Lerp(_staging.OrbitPitch, _staging.OrbitPitchTarget, smoothing);
            _staging.OrbitDistance = Mathf.Lerp(_staging.OrbitDistance, _staging.OrbitDistanceTarget, smoothing);

            var rotation = _staging.Stage.rotation * Quaternion.Euler(_staging.OrbitPitch, _staging.OrbitYaw, 0f);
            // The hangar's roof beams sit 6 m up: a far shot (a vehicle) stays under them.
            var headroom = _staging.Stage.TransformPoint(ArmoryStand).y + ArmoryCeiling - focus.y;
            var distance = Mathf.Min(_staging.OrbitDistance, headroom / Mathf.Max(0.1f, Mathf.Sin(_staging.OrbitPitch * Mathf.Deg2Rad)));
            // Where even the closest framing would stand the camera outside the hall, the orbit
            // stops at the last bearing that kept it inside.
            if (!InsideHall(focus, rotation, ref distance))
            {
                _staging.OrbitYaw = _staging.OrbitYawTarget = goodYaw;
                rotation = _staging.Stage.rotation * Quaternion.Euler(_staging.OrbitPitch, _staging.OrbitYaw, 0f);
                distance = Mathf.Min(_staging.OrbitDistance, headroom / Mathf.Max(0.1f, Mathf.Sin(_staging.OrbitPitch * Mathf.Deg2Rad)));
                InsideHall(focus, rotation, ref distance);
            }
            else
                goodYaw = _staging.OrbitYaw;
            // And no further out than the hall's fittings leave room for: the camera comes in
            // along its own line until it stands clear of them.
            distance = ClearOfHall(focus, rotation, distance);
            // What the hall allows changes in steps (its fittings are known by the cell), and
            // the camera goes to it over a few frames.
            shown = shown <= 0f ? distance : shown + (distance - shown) * (1f - MathF.Exp(-HallEasing * HitchStep(StageLongestStep)));
            distance = shown;
            var pan = rotation * Vector3.right * (distance * StagePan());
            var (eye, facing) = (focus + rotation * (Vector3.back * distance) + pan, rotation);
            // Between Squads and Recruit the camera travels from the shot it had to its new one.
            if (_staging.StageTravel < 1f)
            {
                _staging.StageTravel = Mathf.Min(1f, _staging.StageTravel + HitchStep(StageLongestStep) / StageTravelSeconds);
                var along = Ease.InOutCubic(_staging.StageTravel);
                eye = Vector3.Lerp(_staging.StageFrom.Position, eye, along);
                facing = Quaternion.Slerp(_staging.StageFrom.Rotation, facing, along);
            }
            _scene.Camera.transform.SetPositionAndRotation(eye, facing);
            var camera = _scene.Camera.GetComponent<Camera>();
            if (camera != null)
                SetLens(camera, ArmoryFov, 0f, 0f);
            LightSquad();
            // The controller shows the name labels again whenever it respawns the squad.
            if (_staging.ArmoryLabels != null && _staging.ArmoryLabels.activeSelf)
                _staging.ArmoryLabels.SetActive(false);
            yield return null;
        }
    }


    // How long a newly shown squad is followed while it takes up its pose.
    private const float SquadSettleSeconds = 1.2f;
    private const float SquadPoseSeconds = 0.4f;


    // How far the orbit swings either side of the default bearing: the full range round a squad,
    // narrower round a vehicle, whose far shot would otherwise swing out through the hall's walls.
    private float YawRange() => Mathf.Lerp(OrbitYawRange, 25f, Mathf.InverseLerp(3.5f, 5.5f, _staging.FramedRadius));

    // To the right the swing stops short of the hall's pillar: past it the camera has the pillar
    // between it and the squad and closes right in on them to see round it.
    private const float OrbitYawRight = 50f;
    private float YawMin() => ArmoryYaw - YawRange();
    private float YawMax() => ArmoryYaw + Mathf.Min(YawRange(), OrbitYawRight);

    private void FrameSquad(float radius)
    {
        _staging.FramedRadius = radius;
        var fit = radius * ArmoryFrameMargin / Mathf.Sin(ArmoryFov * 0.5f * Mathf.Deg2Rad);
        var distance = Mathf.Max(_staging.CurrentStageShot.Distance, fit);
        // Further out than the framing shot by a part of it. Where that would back the camera
        // into the hall's fittings, it stops short of them (ClearOfHall).
        _staging.OrbitDistanceLimit = Mathf.Max(OrbitDistanceMax, distance * 1.3f);
        _staging.OrbitDistanceTarget = distance;
        // Standing that far back at the default pitch would put the camera through the roof, so a
        // wide footprint is shot flatter (the focus sits about a metre up).
        var flattest = Mathf.Asin(Mathf.Clamp01((ArmoryCeiling - 1f) / distance)) * Mathf.Rad2Deg;
        _staging.OrbitPitchTarget = Mathf.Clamp(Mathf.Min(_staging.CurrentStageShot.Pitch, flattest), _staging.CurrentStageShot.PitchMin, OrbitPitchMax);
        Context.Log.Info($"strategy diorama: armoury frames a {radius:F1} m footprint from {distance:F1} m at {_staging.OrbitPitchTarget:F0} degrees");
    }

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
            _staging.OrbitYawTarget = Mathf.Clamp(_staging.OrbitYawTarget + delta.x * OrbitDegreesPerPixel, YawMin(), YawMax());
            _staging.OrbitPitchTarget = Mathf.Clamp(_staging.OrbitPitchTarget - delta.y * OrbitDegreesPerPixel, _staging.CurrentStageShot.PitchMin, OrbitPitchMax);
        }
        if (Mathf.Abs(scroll) > 0.01f && !PointerOverUi(position))
            _staging.OrbitDistanceTarget = Mathf.Clamp(_staging.OrbitDistanceTarget * OrbitZoom(scroll), Mathf.Max(OrbitDistanceMin, _staging.FramedRadius * 1.6f), _staging.OrbitDistanceLimit);
    }

}
