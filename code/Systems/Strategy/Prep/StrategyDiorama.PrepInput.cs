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

    // A drag in progress: where the pointer was pressed, in screen pixels, and whether it has
    // moved further than a hand's tremor since.
    private const float PrepDragSlack = 6f;

    // The camera turns and tilts round the map, and the map stays where it is over the table:
    // hold the middle button off the panels and drag, and roll the wheel to come closer. Bearing 0
    // is the view down the table from its foot.
    private static readonly Vector3 PrepOrbitFocus = new(-14.5f, 0.3f, 0f);
    private const float PrepOrbitPitch = 57f;
    private const float PrepOrbitDistance = 4.05f;
    private const float PrepOrbitPitchMin = 30f;
    private const float PrepOrbitPitchMax = 88f;
    private const float PrepOrbitDistanceMin = 2.6f;
    // How much further out than the view that fits the map the wheel goes.
    private const float PrepOrbitZoomOut = 1.1f;
    // The camera stays under the room's ceiling, in the room's units.
    private const float PrepOrbitCeiling = 3.9f;
    private const float PrepOrbitCeilingOverTable = 3.72f;
    private const float PrepOrbitFovMax = 75f;
    private const float PrepOrbitPitchOverTable = 75f;

    private const float PrepFitMargin = 12f;
    // The map stands this far right of the free part's middle: its near edge, the widest, is low
    // in the frame, where its corner may run a little under the roster's foot, and the objectives
    // lie over its far left corner.
    private const float PrepFitRightward = 20f;
    // What stands on the map is taken to reach this far over it.
    private const float PrepFitHeight = 0.12f;

    private float PrepDistanceMax => _prep.FitDistance * PrepOrbitZoomOut;

    private void FitPrepView(Camera camera)
    {
        // The lens as the camera really draws with it (its sensor's fit to the screen included),
        // unshifted, and how far a shift of one moves the picture.
        var (fov, shift, lift) = (camera.fieldOfView, _lensShift, _lensLift);
        SetLens(camera, PrepFraming.Fov, 0f, 0f);
        var lens = camera.projectionMatrix;
        _prep.LensScale = lens.m11;
        SetLens(camera, PrepFraming.Fov, 1f, 0f);
        var perShift = -(camera.projectionMatrix.m02 - lens.m02);
        SetLens(camera, fov, shift, lift);

        // The free part of the frame as the camera's own picture has it. The frame is the middle
        // 16:9 of a wider screen.
        var block = Mathf.Min(1f, 16f / 9f / camera.aspect);
        float Across(float panel) => (0.5f + (panel / 1280f - 0.5f) * block) * 2f - 1f;
        var (left, right) = (Across(RosterLeft + PrepFitMargin), Across(PrepRosterLeft - PrepFitMargin));
        var (top, bottom) = (1f - 2f * (SquadsTop + PrepChipHeight + PrepGap + PrepFitMargin) / 720f, 1f - 2f * (SquadsBottom - PrepFitMargin) / 720f);

        var (halfAcross, halfAlong) = (_prep.MapSizeX * _prep.MapTile / 2f, _prep.MapSizeZ * _prep.MapTile / 2f);
        var corners = new Vector3[8];
        for (var i = 0; i < 8; i++)
            corners[i] = _scene.Room.transform.TransformPoint(PrepMapCentre + new Vector3((i & 1) == 0 ? -halfAlong : halfAlong, (i & 4) == 0 ? 0f : PrepFitHeight, (i & 2) == 0 ? -halfAcross : halfAcross));
        var focus = _scene.Room.transform.TransformPoint(PrepOrbitFocus);
        var upright = _scene.Room.transform.TransformDirection(Vector3.left);

        // The map's box in the picture from a tilt and a distance, with the lens unshifted.
        (float Left, float Right, float Top, float Bottom) Seen(float tilt, float distance)
        {
            var eye = _scene.Room.transform.TransformPoint(PrepOrbitFocus + new Vector3(Mathf.Cos(tilt) * distance, Mathf.Sin(tilt) * distance, 0f));
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

        // At the usual tilt, the nearest distance at which the map fits. The room's ceiling may
        // stop the camera short of it: then the view starts from under the ceiling, with the
        // map's edges running a little under the panels beside it.
        var tilt = PrepOrbitPitch * Mathf.Deg2Rad;
        var far = (PrepOrbitCeiling - PrepOrbitFocus.y) / Mathf.Sin(tilt);
        if (Fits(tilt, far))
        {
            var near = PrepOrbitDistanceMin;
            for (var step = 0; step < 20; step++)
            {
                var middle = (near + far) / 2f;
                if (Fits(tilt, middle))
                    far = middle;
                else
                    near = middle;
            }
        }
        var fitted = Seen(tilt, far);
        _prep.FitDistance = far;
        var stands = (left + right) / 2f + PrepFitRightward / 1280f * block * 2f;
        _prep.FitShift = Mathf.Abs(perShift) > 0.0001f ? (stands - (fitted.Left + fitted.Right) / 2f) / perShift : PrepFraming.Shift;
        _prep.FitFor = (_prep.MapSizeX, _prep.MapSizeZ, camera.aspect);
        Context.Log.Info($"strategy diorama: mission preparation fits a {_prep.MapSizeX} by {_prep.MapSizeZ} map from {far:F2} m at {PrepOrbitPitch:F0} degrees, lens shifted {_prep.FitShift:F3}");
    }

    private void DrivePrepCamera()
    {
        var on = _policyScreen == VanillaUi.Screens.MissionPrep && _scene.Room != null && _scene.Camera != null && _shellRoot?.panel != null;
        if (!on)
        {
            _prep.OrbitOn = false;
            return;
        }
        // The view is fitted to the map once the map is known, and again for another map or
        // another shape of screen.
        var lensCamera = RoomCamera;
        var refit = _prep.MapSizeX > 0 && lensCamera != null && _prep.FitFor != (_prep.MapSizeX, _prep.MapSizeZ, lensCamera.aspect);
        if (refit)
            FitPrepView(lensCamera);
        if (!_prep.OrbitOn || refit)
        {
            // Each visit starts from the view down the table.
            _prep.OrbitOn = true;
            _prep.Orbiting = false;
            (_prep.Yaw, _prep.Pitch, _prep.Distance) = (_prep.YawTarget, _prep.PitchTarget, _prep.DistanceTarget) = (0f, PrepOrbitPitch, _prep.FitDistance);
        }
        // The flat view's camera is not turned or moved.
        var mouse = _prep.Flat || _prep.FlatAmount > 0f ? null : UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            var held = mouse.middleButton.isPressed;
            if (!held)
                _prep.Orbiting = false;
            else if (mouse.middleButton.wasPressedThisFrame)
                _prep.Orbiting = PointerOverPrepMap();
            if (_prep.Orbiting)
            {
                var delta = mouse.delta.ReadValue();
                _prep.YawTarget += delta.x * OrbitDegreesPerPixel;
                _prep.PitchTarget = Mathf.Clamp(_prep.PitchTarget - delta.y * OrbitDegreesPerPixel, PrepOrbitPitchMin, PrepOrbitPitchMax);
            }
            var scroll = WheelNotches(mouse.scroll.ReadValue().y);
            if (Mathf.Abs(scroll) > 0.01f && PointerOverPrepMap())
                _prep.DistanceTarget = Mathf.Clamp(_prep.DistanceTarget * OrbitZoom(scroll), PrepOrbitDistanceMin, PrepDistanceMax);
        }
        var smoothing = OrbitSmoothing();
        _prep.Yaw = Mathf.Lerp(_prep.Yaw, _prep.YawTarget, smoothing);
        _prep.Pitch = Mathf.Lerp(_prep.Pitch, _prep.PitchTarget, smoothing);
        _prep.Distance = Mathf.Lerp(_prep.Distance, _prep.DistanceTarget, smoothing);

        // No further than the ceiling allows at this tilt.
        var pitch = _prep.Pitch * Mathf.Deg2Rad;
        // Tilted up towards looking straight down, the camera comes over the table, where the
        // machine on the ceiling hangs lower (its underside is 3.8 m up) than the ceiling the
        // camera stands under further back. It looks down, so what is over it is behind it
        // and it need only stand under that.
        var overTable = Math.Clamp((_prep.Pitch - PrepOrbitPitch) / (PrepOrbitPitchOverTable - PrepOrbitPitch), 0f, 1f);
        var ceiling = PrepOrbitCeiling + (PrepOrbitCeilingOverTable - PrepOrbitCeiling) * overTable;
        var distance = MathF.Min(_prep.Distance, (ceiling - PrepOrbitFocus.y) / MathF.Max(0.1f, MathF.Sin(pitch)));
        var out3 = Quaternion.Euler(0f, _prep.Yaw, 0f) * new Vector3(Mathf.Cos(pitch) * distance, Mathf.Sin(pitch) * distance, 0f);
        var eye = _scene.Room.transform.TransformPoint(PrepOrbitFocus + out3);
        var focus = _scene.Room.transform.TransformPoint(PrepOrbitFocus);
        // The camera's top points along its bearing, which is the upright seen from any tilt and
        // still has a direction looking straight down.
        var top = _scene.Room.transform.TransformDirection(Quaternion.Euler(0f, _prep.Yaw, 0f) * Vector3.left);
        var facing = Quaternion.LookRotation(focus - eye, top);
        // The room camera's shot follows the orbit, and the camera travels to it on arriving.
        // Where the ceiling keeps the camera nearer than it was asked to stand, the lens is
        // widened by as much, so the map is seen at the size it would have been from there
        // and does not come closer as the camera is tilted up.
        var fov = distance < _prep.Distance
            ? MathF.Min(PrepOrbitFovMax, 2f * MathF.Atan(MathF.Tan(PrepFraming.Fov * Mathf.Deg2Rad / 2f) * _prep.Distance / distance) * Mathf.Rad2Deg)
            : PrepFraming.Fov;
        _shot = PrepShot((eye, facing, fov, _prep.FitShift));
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
    // under it, or anything else there that is not one of the panels. The tokens' layer takes no
    // pointer, so it is never what is picked.
    private bool PointerOverPrepMap()
    {
        var picked = UnderPointer();
        if (picked == null || picked == _shellRoot)
            return true;
        for (var e = picked; e != null; e = e.parent)
            if (e.name == VanillaUi.Elements.MapColumn)
                return true;
        // Where the pointer's line meets the map itself, whatever unseen element lies there.
        if (_prep.PointerTile is { } tile && tile.x >= 0f && tile.y >= 0f && tile.x <= _prep.MapSizeX && tile.y <= _prep.MapSizeZ)
        {
            for (var e = picked; e != null; e = e.parent)
                if (Array.IndexOf(PrepPanels, e.name) >= 0)
                    return false;
            return true;
        }
        return false;
    }

    private void DrivePrepInput()
    {
        _prep.PointerTile = null;
        if (_shellRoot == null || _policyScreen != VanillaUi.Screens.MissionPrep || _prep.Map == null || _scene.Camera == null)
            return;
        // In the flat view the pointer is on vanilla's map itself, which stands where it is seen.
        if (_prep.Flat || _prep.FlatAmount > 0f)
            return;
        var column = UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.MapColumn));
        var preview = column != null ? UI.Find(column, UiSelector.Name(VanillaUi.Elements.PrepPreview)) : null;
        var camera = _scene.Camera.GetComponent<Camera>();
        if (preview == null || camera == null || column.panel == null || preview.layout.width <= 0f)
            return;

        var real = RealPointer();
        var aim = real;
        StandInPrepPointer(ref aim);
        _prep.PointerPanel = RuntimePanelUtils.ScreenToPanel(column.panel, new Vector2(aim.x, Screen.height - aim.y));

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
            _prep.Grab = Vector2.zero;
            Token held = null;
            foreach (var token in _prep.Tokens.Values)
                if (token.Kind == TokenKind.Squad && token.Seen.Contains(_prep.PointerPanel) && (held == null || token.Seen.YMax > held.Seen.YMax))
                    held = token;
            if (held != null)
            {
                _prep.Grab = tile - held.Tile;
                tile = held.Tile;
            }
            _prep.Dragging = false;
        }
        else
        {
            // A press and release without moving is a pick, not a move: until the pointer has
            // left where it was pressed, she stays over the tile she was picked up from. The
            // pointer here is the one the map is aimed with, a stand-in included.
            if (!_prep.Dragging)
                (_prep.Dragging, _prep.PressedAt, _prep.Moved) = (true, aim, false);
            _prep.Moved |= Vector2.Distance(aim, _prep.PressedAt) > PrepDragSlack;
            tile = _prep.Moved ? tile - _prep.Grab : new Vector2(drag.Value.From.x + 0.5f, drag.Value.From.y + 0.5f);
        }
        _prep.PointerTile = tile;

        // That tile on the flat map, whose top is the map's far side, is put under the real
        // pointer, which is the one vanilla reads.
        var onMap = new Vector2(tile.x * preview.layout.width / _prep.MapSizeX, (_prep.MapSizeZ - tile.y) * preview.layout.height / _prep.MapSizeZ);
        var pointer = RuntimePanelUtils.ScreenToPanel(column.panel, new Vector2(real.x, Screen.height - real.y));
        var inColumn = preview.worldBound.position - column.worldBound.position;
        // The column is placed from its parent's corner, which on a screen wider than 16:9 is
        // not the panel's: the screen's block stands in the middle of it.
        var corner = column.parent != null ? column.parent.worldBound.position : Vector2.zero;
        column.style.left = pointer.x - corner.x - inColumn.x - onMap.x;
        column.style.top = pointer.y - corner.y - inColumn.y - onMap.y;
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
}
