using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The pointer on mission preparation's hologram map. Vanilla's flat map is unseen and is moved
// every frame so that the tile under the pointer on the hologram is the tile under the pointer on
// that map. Vanilla then does everything itself, as it would on its own map: it picks a squad up,
// drags it, swaps it with another, judges where it may stand, puts it down, and shows a marker's
// tooltip. The hologram only shows the result (PrepTokens).
public sealed partial class StrategyDiorama
{
    // Where the flat map waits while the pointer is over no part of the hologram's plane.
    private const float PrepFlatMapParked = -5000f;

    private bool _prepFlatMapShown;
    // A stand-in for the pointer, in screen pixels from the bottom left, for driving the map
    // without a mouse.
    private Vector2? _prepPointerOverride;
    // The pointer on the panel, and the tile it is over (a tile's middle is half past its number).
    private Vector2 _prepPointerPanel;
    private Vector2? _prepPointerTile;
    // How far the pointer was from a chibi's tile when it came onto her, in tiles.
    private Vector2 _prepGrab;
    // A drag in progress: where the pointer was pressed, in screen pixels, and whether it has
    // moved further than a hand's tremor since.
    private const float PrepDragSlack = 6f;
    private bool _prepDragging, _prepMoved;
    private Vector2 _prepPressedAt;

    // The camera turns and tilts round the map, and the map stays where it is over the table:
    // hold the middle button off the panels and drag, and roll the wheel to come closer. Bearing 0
    // is the view down the table from its foot.
    private static readonly Vector3 PrepOrbitFocus = new(-14.5f, 0.3f, 0f);
    private const float PrepOrbitPitch = 50f;
    private const float PrepOrbitDistance = 4.05f;
    private const float PrepOrbitPitchMin = 30f;
    private const float PrepOrbitPitchMax = 88f;
    private const float PrepOrbitDistanceMin = 2.6f;
    // How much further out than the view that fits the map the wheel goes.
    private const float PrepOrbitZoomOut = 1.1f;
    // The camera stays under the room's ceiling, in the room's units.
    private const float PrepOrbitCeiling = 3.9f;
    private float _prepYaw, _prepPitch = PrepOrbitPitch, _prepDistance = PrepOrbitDistance;
    private float _prepYawTarget, _prepPitchTarget = PrepOrbitPitch, _prepDistanceTarget = PrepOrbitDistance;
    private bool _prepOrbiting;
    private bool _prepOrbitOn;

    // The view a visit starts from: the tilt, the distance and the lens's shift at which the whole
    // map lies in the frame's free part, between the frame's left and the roster and between the
    // chips and the foot of the panels. It is worked out from the map's own shape, which is as
    // long as it is wide for one mission and not for the next, whatever its size in tiles.
    private float _prepFitPitch = PrepOrbitPitch, _prepFitDistance = PrepOrbitDistance, _prepFitShift = PrepFraming.Shift;
    private (int X, int Z, float Aspect) _prepFitFor;
    private const float PrepFitMargin = 12f;
    // The map stands this far right of the free part's middle: its near edge, the widest, is low
    // in the frame, where its corner may run a little under the roster's foot, and the objectives
    // lie over its far left corner.
    private const float PrepFitRightward = 20f;
    // What stands on the map is taken to reach this far over it.
    private const float PrepFitHeight = 0.12f;

    private float PrepDistanceMax => _prepFitDistance * PrepOrbitZoomOut;

    private void FitPrepView(Camera camera)
    {
        // The lens as the camera really draws with it (its sensor's fit to the screen included),
        // unshifted, and how far a shift of one moves the picture.
        var (fov, shift, lift) = (camera.fieldOfView, _lensShift, _lensLift);
        SetLens(camera, PrepFraming.Fov, 0f, 0f);
        var lens = camera.projectionMatrix;
        SetLens(camera, PrepFraming.Fov, 1f, 0f);
        var perShift = -(camera.projectionMatrix.m02 - lens.m02);
        SetLens(camera, fov, shift, lift);

        // The free part of the frame as the camera's own picture has it. The frame is the middle
        // 16:9 of a wider screen.
        var block = Mathf.Min(1f, 16f / 9f / camera.aspect);
        float Across(float panel) => (0.5f + (panel / 1280f - 0.5f) * block) * 2f - 1f;
        var (left, right) = (Across(RosterLeft + PrepFitMargin), Across(PrepRosterLeft - PrepFitMargin));
        var (top, bottom) = (1f - 2f * (SquadsTop + PrepChipHeight + PrepGap + PrepFitMargin) / 720f, 1f - 2f * (SquadsBottom - PrepFitMargin) / 720f);

        var (halfAcross, halfAlong) = (_prepMapSizeX * _prepMapTile / 2f, _prepMapSizeZ * _prepMapTile / 2f);
        var corners = new Vector3[8];
        for (var i = 0; i < 8; i++)
            corners[i] = _room.transform.TransformPoint(PrepMapCentre + new Vector3((i & 1) == 0 ? -halfAlong : halfAlong, (i & 4) == 0 ? 0f : PrepFitHeight, (i & 2) == 0 ? -halfAcross : halfAcross));
        var focus = _room.transform.TransformPoint(PrepOrbitFocus);
        var upright = _room.transform.TransformDirection(Vector3.left);

        // The map's box in the picture from a tilt and a distance, with the lens unshifted.
        (float Left, float Right, float Top, float Bottom) Seen(float tilt, float distance)
        {
            var eye = _room.transform.TransformPoint(PrepOrbitFocus + new Vector3(Mathf.Cos(tilt) * distance, Mathf.Sin(tilt) * distance, 0f));
            var facing = Quaternion.LookRotation(focus - eye, upright);
            var (across, up, ahead) = (facing * Vector3.right, facing * Vector3.up, facing * Vector3.forward);
            var box = (Left: float.MaxValue, Right: float.MinValue, Top: float.MinValue, Bottom: float.MaxValue);
            foreach (var corner in corners)
            {
                var from = corner - eye;
                var seen = lens * new Vector4(Vector3.Dot(from, across), Vector3.Dot(from, up), -Vector3.Dot(from, ahead), 1f);
                var (x, y) = (seen.x / seen.w, seen.y / seen.w);
                box = (Mathf.Min(box.Left, x), Mathf.Max(box.Right, x), Mathf.Max(box.Top, y), Mathf.Min(box.Bottom, y));
            }
            return box;
        }
        bool Fits(float tilt, float distance)
        {
            var box = Seen(tilt, distance);
            return box.Right - box.Left <= right - left && box.Top <= top && box.Bottom >= bottom;
        }

        // From the usual tilt down to the flattest: a flatter view may stand further back under
        // the room's ceiling. The nearest distance that fits is taken.
        var (pitch, far) = (PrepOrbitPitchMin, (PrepOrbitCeiling - PrepOrbitFocus.y) / Mathf.Sin(PrepOrbitPitchMin * Mathf.Deg2Rad));
        for (var tilt = PrepOrbitPitch; tilt >= PrepOrbitPitchMin; tilt -= 1f)
        {
            var limit = (PrepOrbitCeiling - PrepOrbitFocus.y) / Mathf.Sin(tilt * Mathf.Deg2Rad);
            if (!Fits(tilt * Mathf.Deg2Rad, limit))
                continue;
            var near = PrepOrbitDistanceMin;
            (pitch, far) = (tilt, limit);
            for (var step = 0; step < 20; step++)
            {
                var middle = (near + far) / 2f;
                if (Fits(tilt * Mathf.Deg2Rad, middle))
                    far = middle;
                else
                    near = middle;
            }
            break;
        }
        var fitted = Seen(pitch * Mathf.Deg2Rad, far);
        (_prepFitPitch, _prepFitDistance) = (pitch, far);
        var stands = (left + right) / 2f + PrepFitRightward / 1280f * block * 2f;
        _prepFitShift = Mathf.Abs(perShift) > 0.0001f ? (stands - (fitted.Left + fitted.Right) / 2f) / perShift : PrepFraming.Shift;
        _prepFitFor = (_prepMapSizeX, _prepMapSizeZ, camera.aspect);
        Context.Log.Info($"strategy diorama: mission preparation fits a {_prepMapSizeX} by {_prepMapSizeZ} map from {far:F2} m at {pitch:F0} degrees, lens shifted {_prepFitShift:F3}");
    }

    private void DrivePrepCamera()
    {
        var on = _policyScreen == VanillaUi.Screens.MissionPrep && _room != null && _camera != null && _shellRoot?.panel != null;
        if (!on)
        {
            _prepOrbitOn = false;
            return;
        }
        // The view is fitted to the map once the map is known, and again for another map or
        // another shape of screen.
        var lensCamera = RoomCamera;
        var refit = _prepMapSizeX > 0 && lensCamera != null && _prepFitFor != (_prepMapSizeX, _prepMapSizeZ, lensCamera.aspect);
        if (refit)
            FitPrepView(lensCamera);
        if (!_prepOrbitOn || refit)
        {
            // Each visit starts from the view down the table.
            _prepOrbitOn = true;
            _prepOrbiting = false;
            (_prepYaw, _prepPitch, _prepDistance) = (_prepYawTarget, _prepPitchTarget, _prepDistanceTarget) = (0f, _prepFitPitch, _prepFitDistance);
        }
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            var held = mouse.middleButton.isPressed;
            if (!held)
                _prepOrbiting = false;
            else if (mouse.middleButton.wasPressedThisFrame)
                _prepOrbiting = PointerOverPrepMap();
            if (_prepOrbiting)
            {
                var delta = mouse.delta.ReadValue();
                _prepYawTarget += delta.x * OrbitDegreesPerPixel;
                _prepPitchTarget = Mathf.Clamp(_prepPitchTarget - delta.y * OrbitDegreesPerPixel, PrepOrbitPitchMin, PrepOrbitPitchMax);
            }
            var scroll = WheelNotches(mouse.scroll.ReadValue().y);
            if (Mathf.Abs(scroll) > 0.01f && PointerOverPrepMap())
                _prepDistanceTarget = Mathf.Clamp(_prepDistanceTarget * OrbitZoom(scroll), PrepOrbitDistanceMin, PrepDistanceMax);
        }
        var smoothing = OrbitSmoothing();
        _prepYaw = Mathf.Lerp(_prepYaw, _prepYawTarget, smoothing);
        _prepPitch = Mathf.Lerp(_prepPitch, _prepPitchTarget, smoothing);
        _prepDistance = Mathf.Lerp(_prepDistance, _prepDistanceTarget, smoothing);

        // No further than the ceiling allows at this tilt.
        var pitch = _prepPitch * Mathf.Deg2Rad;
        var distance = Mathf.Min(_prepDistance, (PrepOrbitCeiling - PrepOrbitFocus.y) / Mathf.Max(0.1f, Mathf.Sin(pitch)));
        var out3 = Quaternion.Euler(0f, _prepYaw, 0f) * new Vector3(Mathf.Cos(pitch) * distance, Mathf.Sin(pitch) * distance, 0f);
        var eye = _room.transform.TransformPoint(PrepOrbitFocus + out3);
        var focus = _room.transform.TransformPoint(PrepOrbitFocus);
        // The camera's top points along its bearing, which is the upright seen from any tilt and
        // still has a direction looking straight down.
        var top = _room.transform.TransformDirection(Quaternion.Euler(0f, _prepYaw, 0f) * Vector3.left);
        var facing = Quaternion.LookRotation(focus - eye, top);
        // The room camera's shot follows the orbit, and the camera travels to it on arriving.
        _shot = (eye, facing, PrepFraming.Fov, _prepFitShift);
    }

    // What the shell's panel has under the pointer, or null where it has nothing.
    private VisualElement UnderPointer()
    {
        var real = RealPointer();
        return _shellRoot.panel.Pick(RuntimePanelUtils.ScreenToPanel(_shellRoot.panel, new Vector2(real.x, Screen.height - real.y)));
    }

    // The panels that may lie over a part of the map's place on screen.
    private static readonly string[] PrepPanels = { VanillaUi.Elements.PrepSelectedColumn, PrepRosterName, PrepChipsName };

    // Whether the pointer is over the map's part of the screen: the room, vanilla's unseen map
    // or the map's own tokens under it, or anything else there that is not one of the panels.
    private bool PointerOverPrepMap()
    {
        var picked = UnderPointer();
        if (picked == null || picked == _shellRoot)
            return true;
        for (var e = picked; e != null; e = e.parent)
            if (e.name == VanillaUi.Elements.MapColumn || e.name == PrepTokensName)
                return true;
        // Where the pointer's line meets the map itself, whatever unseen element lies there.
        if (_prepPointerTile is { } tile && tile.x >= 0f && tile.y >= 0f && tile.x <= _prepMapSizeX && tile.y <= _prepMapSizeZ)
        {
            for (var e = picked; e != null; e = e.parent)
                if (Array.IndexOf(PrepPanels, e.name) >= 0)
                    return false;
            return true;
        }
        return false;
    }

    internal static string PrepOrbitFromDev(float yaw, float pitch, float distance)
    {
        var d = _instance;
        if (d == null)
            return "no diorama";
        d._prepYawTarget = yaw;
        d._prepPitchTarget = Mathf.Clamp(pitch, PrepOrbitPitchMin, PrepOrbitPitchMax);
        d._prepDistanceTarget = Mathf.Clamp(distance, PrepOrbitDistanceMin, d.PrepDistanceMax);
        return $"orbit to bearing {yaw:0}, tilt {d._prepPitchTarget:0}, distance {d._prepDistanceTarget:0.0}";
    }

    private void DrivePrepInput()
    {
        _prepPointerTile = null;
        if (_shellRoot == null || _policyScreen != VanillaUi.Screens.MissionPrep || _prepMap == null || _camera == null)
            return;
        var column = UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.MapColumn));
        var preview = column != null ? UI.Find(column, UiSelector.Name(VanillaUi.Elements.PrepPreview)) : null;
        var camera = _camera.GetComponent<Camera>();
        if (preview == null || camera == null || column.panel == null || preview.layout.width <= 0f)
            return;

        var real = RealPointer();
        var aim = _prepPointerOverride ?? real;
        _prepPointerPanel = RuntimePanelUtils.ScreenToPanel(column.panel, new Vector2(aim.x, Screen.height - aim.y));

        // Where the pointer's ray meets the map's ground, in tiles.
        var ray = camera.ScreenPointToRay(aim);
        if (ray.direction.y > -1e-4f)
        {
            column.style.left = PrepFlatMapParked;
            return;
        }
        var tile = PrepTileUnder(ray);

        // A chibi stands taller than her tile, so the pointer on her is over tiles behind her.
        // While nothing is dragged, the pointer on a chibi counts as on her tile, so a press there
        // picks her up. Once she is dragged she keeps to the pointer as she was held.
        var drag = PrepDrag();
        if (drag == null)
        {
            _prepGrab = Vector2.zero;
            Token held = null;
            foreach (var token in _prepTokens.Values)
                if (token.Kind == TokenKind.Squad && token.Seen.Contains(_prepPointerPanel) && (held == null || token.Seen.yMax > held.Seen.yMax))
                    held = token;
            if (held != null)
            {
                _prepGrab = tile - held.Tile;
                tile = held.Tile;
            }
            _prepDragging = false;
        }
        else
        {
            // A press and release without moving is a pick, not a move: until the pointer has
            // left where it was pressed, she stays over the tile she was picked up from. The
            // pointer here is the one the map is aimed with, a stand-in included.
            if (!_prepDragging)
                (_prepDragging, _prepPressedAt, _prepMoved) = (true, aim, false);
            _prepMoved |= Vector2.Distance(aim, _prepPressedAt) > PrepDragSlack;
            tile = _prepMoved ? tile - _prepGrab : new Vector2(drag.Value.From.x + 0.5f, drag.Value.From.y + 0.5f);
        }
        _prepPointerTile = tile;

        // That tile on the flat map, whose top is the map's far side, is put under the real
        // pointer, which is the one vanilla reads.
        var onMap = new Vector2(tile.x * preview.layout.width / _prepMapSizeX, (_prepMapSizeZ - tile.y) * preview.layout.height / _prepMapSizeZ);
        var pointer = RuntimePanelUtils.ScreenToPanel(column.panel, new Vector2(real.x, Screen.height - real.y));
        var inColumn = preview.worldBound.position - column.worldBound.position;
        column.style.left = pointer.x - inColumn.x - onMap.x;
        column.style.top = pointer.y - inColumn.y - onMap.y;
    }

    // The pointer as vanilla reads it, in screen pixels from the bottom left. Vanilla's own map
    // works from the engine's mouse, which a mouse moved through the input system alone does not
    // move, so that is the one read here.
    private static Vector2 RealPointer()
    {
        try { return Input.mousePosition; }
        catch
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        }
    }

    // The squad vanilla is dragging, where it would stand now, where it stood, and whether it may
    // stand there. Null while nothing is dragged.
    private (IntPtr Squad, Vector2Int Now, Vector2Int From, bool Valid)? PrepDrag()
    {
        var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<MissionPrepUIScreen>();
        var dragged = screen?.m_DraggedEntity;
        if (dragged == null)
            return null;
        return (dragged.Pointer, screen.m_DragCurrentPos, screen.m_DragStartPos, screen.m_IsValidDragPos);
    }

    // Builds the hologram map again from nothing, to watch it come up.
    internal static string PrepRebuildFromDev()
    {
        if (_instance == null)
            return "no diorama";
        _instance._prepMapOf = IntPtr.Zero;
        return "the map builds again";
    }

    // Stands every squad as its badge, as a squad with no chibi stands.
    internal static string PrepChibisFromDev(bool shown)
    {
        if (_instance == null)
            return "no diorama";
        _instance._prepChibisOff = !shown;
        return shown ? "chibis shown" : "badges in place of chibis";
    }

    // Puts every squad of the roster on the mission as a player would: each squad's tile is
    // pressed, then vanilla's own button in the unit window. To see the loadout past its limit.
    internal static string PrepDeployAllFromDev() => PrepDeployFromDev(null);

    // Adds the squads not in the mission, or with character tags given (comma separated) adds
    // those and takes every other squad out, through the squad window's own button.
    internal static string PrepDeployFromDev(string only)
    {
        var wanted = only?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<MissionPrepUIScreen>();
        var selector = screen?.m_UnitSelector;
        var d = _instance;
        if (selector == null || d?._shellRoot == null || d._squadWindow == null)
            return "mission preparation is not open";
        var added = 0;
        foreach (var element in UI.FindAll(d._shellRoot, UiSelector.TypeName(VanillaUi.Elements.PrepUnitSlot)).ToList())
        {
            var slot = element.TryCast<MissionPrepUnitSelectSlot>();
            var leader = slot?.GetLeader();
            if (leader == null || !slot.IsInteractable())
                continue;
            var inMission = selector.GetDeployedEntity(leader) != null;
            if (inMission == (wanted == null || wanted.Contains(Affinity.CharacterTag(leader))))
                continue;
            slot.OnClicked(MouseButton.LeftMouse);
            var button = UI.Find(d._squadWindow, UiSelector.Name(VanillaUi.Elements.UnitWindowButton))?.TryCast<InteractiveElement>();
            if (button == null || !button.IsInteractable())
                continue;
            button.OnClicked(MouseButton.LeftMouse);
            added++;
        }
        return $"pressed the squad window's button for {added} squads";
    }

    internal static string PrepFlatMapFromDev(bool shown)
    {
        var d = _instance;
        if (d == null)
            return "no diorama";
        d._prepFlatMapShown = shown;
        if (d._shellRoot != null && UI.Find(d._shellRoot, UiSelector.Name(VanillaUi.Elements.MapColumn)) is { } column)
            column.style.opacity = shown ? 0.55f : 0f;
        return shown ? "flat map shown" : "flat map unseen";
    }

    // Points the stand-in pointer at a place on the screen, in pixels from the top left, or lets
    // the real pointer back in with a negative place.
    internal static string PrepPointerFromDev(float x, float y)
    {
        var d = _instance;
        if (d == null)
            return "no diorama";
        d._prepPointerOverride = x < 0f ? null : new Vector2(x, Screen.height - y);
        // The real pointer is sent there too, through the input system, so vanilla's own
        // reading of the mouse is what gets driven.
        if (x >= 0f)
            QueueMouse(new Vector2(x, Screen.height - y));
        return x < 0f ? "real pointer" : $"pointer at ({x}, {y})";
    }

    // Presses or releases the left button as vanilla's own input does: the screen is told what is
    // under the pointer, or is given a deployed squad by its number to pick up wherever it stands.
    internal static string PrepPressFromDev(bool down, int squad)
    {
        var d = _instance;
        var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<MissionPrepUIScreen>();
        if (d?._shellRoot?.panel == null || screen == null)
            return "mission preparation is not open";
        var real = RealPointer();
        var at = RuntimePanelUtils.ScreenToPanel(d._shellRoot.panel, new Vector2(real.x, Screen.height - real.y));
        VisualElement raw = null;
        InteractiveElement hovered = null;
        var deployed = screen.m_Deployed;
        for (var i = 0; deployed != null && i < deployed.childCount && hovered == null; i++)
        {
            var entity = deployed.ElementAt(i).TryCast<MissionPrepDeployedEntity>();
            var pickable = entity?.GetPickableElement();
            if (entity != null && (squad == i || (squad < 0 && pickable != null && pickable.worldBound.Contains(at))))
                (hovered, raw) = (entity, pickable);
        }
        if (hovered == null)
        {
            raw = d._shellRoot.panel.Pick(at);
            for (var e = raw; e != null && hovered == null; e = e.parent)
                hovered = e.TryCast<InteractiveElement>();
        }
        if (down)
            screen.OnPressStart(MouseButton.LeftMouse, hovered, raw);
        else
            screen.OnPressEnd(MouseButton.LeftMouse, hovered, raw);
        return $"{(down ? "pressed" : "released")} at panel ({at.x:0}, {at.y:0}) over {(hovered != null ? hovered.GetIl2CppType().Name : "nothing")}; "
            + $"dragged {(screen.m_DraggedEntity != null ? "yes" : "no")}";
    }

    private static bool _devLeftHeld;

    // The real mouse's state as the input system holds it: where it is, and the left button.
    private static string QueueMouse(Vector2 position)
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null)
            return "no mouse";
        try
        {
            var state = new UnityEngine.InputSystem.LowLevel.MouseState { position = position };
            state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, _devLeftHeld);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, state);
            return $"mouse at {position}, left button {(_devLeftHeld ? "held" : "up")}";
        }
        catch (Exception ex)
        {
            return $"could not queue: {ex.GetType().Name}: {ex.Message}";
        }
    }

    // Holds or lets go of the real left button, where the pointer is.
    internal static string MouseButtonFromDev(bool down)
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null)
            return "no mouse";
        _devLeftHeld = down;
        return QueueMouse(mouse.position.ReadValue());
    }

    internal static string PrepStateFromDev()
    {
        var d = _instance;
        var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<MissionPrepUIScreen>();
        if (d == null || screen == null)
            return "mission preparation is not open";
        var mouse = UnityEngine.InputSystem.Mouse.current;
        var lines = new List<string>
        {
            $"left button held {(mouse != null && mouse.leftButton.isPressed)}",
            $"vanilla's pointer {RealPointer()}, input system's {(mouse != null ? mouse.position.ReadValue().ToString() : "no mouse")}, stand-in {d._prepPointerOverride?.ToString() ?? "none"}, tile {d._prepPointerTile?.ToString("0.00") ?? "none"}",
            $"dragged {(screen.m_DraggedEntity != null ? "yes" : "no")}, now {screen.m_DragCurrentPos}, from {screen.m_DragStartPos}, valid {screen.m_IsValidDragPos}",
        };
        var deployed = screen.m_UnitSelector?.GetDeployedEntities();
        if (screen.m_Deployed != null)
            for (var i = 0; i < screen.m_Deployed.childCount; i++)
                if (screen.m_Deployed.ElementAt(i).TryCast<MissionPrepDeployedEntity>() is { } squad)
                    lines.Add($"squad {i}: tile {squad.GetEntity()?.GetTilePosition()}, element at {squad.layout.center}");
        return string.Join("\n", lines);
    }
}
