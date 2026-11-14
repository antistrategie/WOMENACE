using UnityEngine;
using UnityEngine.UIElements;
using NVector3 = System.Numerics.Vector3;

namespace WOMENACE.Code;

// Mission select's camera round the planet: hold the right button off the panels and drag to
// turn and tilt it about the planet, and roll the wheel to come closer. It starts each visit at
// the view from the table's right-hand side and keeps to that side, and it never comes nearer
// the room's own surfaces than a set clearance, measured from the room's meshes.
public sealed partial class StrategyDiorama
{
    // How far the camera turns from its starting bearing, towards the table's foot (where the
    // planet is seen from its side) and round to face the planet, its tilt above level, and its
    // distance from what it looks at, in metres. Tilted further the camera is in the ceiling:
    // at these limits it measures 0.52 m or more from the room's surfaces.
    private const float PlanetOrbitTurnBack = 20f;
    private const float PlanetOrbitTurnOn = 40f;
    private const float PlanetOrbitTiltMin = 0f;
    private const float PlanetOrbitTiltMax = 20f;
    private const float PlanetOrbitNearest = 4f;
    // The camera keeps this far from any surface of the room.
    private const float PlanetOrbitClearance = 0.5f;

    private float _planetYaw, _planetTilt, _planetDistance;
    private float _planetYawTarget, _planetTiltTarget, _planetDistanceTarget;
    private bool _planetOrbiting;
    private bool _planetOrbitOn;

    // The starting view as a bearing about the upright, a tilt and a distance from its target.
    private static (float Yaw, float Tilt, float Distance)? _planetOrbitStart;

    private static (float Yaw, float Tilt, float Distance) PlanetOrbitStart()
    {
        if (_planetOrbitStart is { } known)
            return known;
        var back = N(PlanetFraming.Eye - PlanetFraming.Target);
        var far = back.Length();
        return (_planetOrbitStart = (MathF.Atan2(back.X, back.Z) * Degrees, MathF.Asin(back.Y / far) * Degrees, far)).Value;
    }

    private const float Degrees = 180f / MathF.PI;

    // The way out from what the camera looks at, on a bearing and a tilt.
    private static NVector3 PlanetOrbitWay(float yaw, float tilt)
    {
        var (bearing, lift) = (yaw / Degrees, tilt / Degrees);
        return new NVector3(MathF.Sin(bearing) * MathF.Cos(lift), MathF.Sin(lift), MathF.Cos(bearing) * MathF.Cos(lift));
    }

    private static NVector3 PlanetOrbitEye(float yaw, float tilt, float distance) => N(PlanetFraming.Target) + PlanetOrbitWay(yaw, tilt) * distance;

    // The bearing, tilt and distance the shot was last worked out for.
    private (float Yaw, float Tilt, float Distance) _planetShotFor = (float.NaN, float.NaN, float.NaN);

    private void DrivePlanetCamera()
    {
        var on = _policyScreen == VanillaUi.Screens.MissionSelect && _homeView == HomeView.Operations && _room != null && _camera != null
            && _shellRoot?.panel != null && _planet != null && _planetMoment > 0f && _planetLay <= 0f;
        if (!on)
        {
            _planetOrbitOn = false;
            return;
        }
        var start = PlanetOrbitStart();
        if (!_planetOrbitOn)
        {
            // Each visit starts from the right-hand side.
            _planetOrbitOn = true;
            _planetOrbiting = false;
            (_planetYaw, _planetTilt, _planetDistance) = (_planetYawTarget, _planetTiltTarget, _planetDistanceTarget) = start;
            _planetShotFor = (float.NaN, float.NaN, float.NaN);
        }
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            if (!mouse.rightButton.isPressed)
                _planetOrbiting = false;
            else if (mouse.rightButton.wasPressedThisFrame)
                _planetOrbiting = PointerOverPlanet();
            if (_planetOrbiting)
            {
                var delta = mouse.delta.ReadValue();
                _planetYawTarget = Math.Clamp(_planetYawTarget + delta.x * OrbitDegreesPerPixel, start.Yaw - PlanetOrbitTurnBack, start.Yaw + PlanetOrbitTurnOn);
                _planetTiltTarget = Math.Clamp(_planetTiltTarget - delta.y * OrbitDegreesPerPixel, PlanetOrbitTiltMin, PlanetOrbitTiltMax);
            }
            var scroll = WheelNotches(mouse.scroll.ReadValue().y);
            if (MathF.Abs(scroll) > 0.01f && PointerOverPlanet())
                _planetDistanceTarget = Math.Clamp(_planetDistanceTarget * OrbitZoom(scroll), PlanetOrbitNearest, start.Distance);
        }
        var smoothing = OrbitSmoothing();
        _planetYaw += (_planetYawTarget - _planetYaw) * smoothing;
        _planetTilt += (_planetTiltTarget - _planetTilt) * smoothing;
        _planetDistance += (_planetDistanceTarget - _planetDistance) * smoothing;

        // The room camera's shot follows the orbit, and the camera travels to it on arriving.
        // At rest it is the shot already worked out.
        if (_planetShotFor == (_planetYaw, _planetTilt, _planetDistance))
            return;
        _planetShotFor = (_planetYaw, _planetTilt, _planetDistance);
        var local = PlanetOrbitEye(_planetYaw, _planetTilt, ClearDistance(_planetYaw, _planetTilt, _planetDistance));
        var eye = _room.transform.TransformPoint(new Vector3(local.X, local.Y, local.Z));
        var focus = _room.transform.TransformPoint(PlanetFraming.Target);
        _shot = (eye, Quaternion.LookRotation(focus - eye, _room.transform.up), PlanetFraming.Fov, PlanetFraming.Shift);
    }

    // Whether the pointer is over the room: nothing of the shell's or vanilla's panels under it.
    // A mission's card counts as the room.
    private bool PointerOverPlanet()
    {
        var picked = UnderPointer();
        return picked == null || picked == _shellRoot || picked.name == "wm-markers" || picked.ClassListContains("wui-marker");
    }

    // The room's solid parts round the table, as cells of a grid in room space, from its meshes.
    private const float RoomCell = 0.2f;
    private static readonly Vector3 RoomMapFrom = new(-21f, -1f, -9f);
    private static readonly Vector3 RoomMapTo = new(-7f, 5f, 5f);
    private bool[,,] _roomSolid;
    private IntPtr _roomMapped;

    private void MapRoom()
    {
        if (_roomSolid != null && _roomMapped == _room.Pointer)
            return;
        var size = (RoomMapTo - RoomMapFrom) / RoomCell;
        var (nx, ny, nz) = ((int)size.x, (int)size.y, (int)size.z);
        var solid = new bool[nx, ny, nz];
        var (pieces, faces) = WalkSolidFaces(_room.transform, RoomCell, 120, (p, _) =>
        {
            var (cx, cy, cz) = ((int)MathF.Floor((p.X - RoomMapFrom.x) / RoomCell), (int)MathF.Floor((p.Y - RoomMapFrom.y) / RoomCell), (int)MathF.Floor((p.Z - RoomMapFrom.z) / RoomCell));
            if (cx >= 0 && cy >= 0 && cz >= 0 && cx < nx && cy < ny && cz < nz)
                solid[cx, cy, cz] = true;
        });
        (_roomSolid, _roomMapped) = (solid, _room.Pointer);
        Context.Log.Info($"strategy diorama: the room round the table is mapped from {faces} faces of {pieces} pieces");
    }

    // How far a point in room space is from the room's nearest surface, up to a limit.
    private float RoomClearance(NVector3 local, float limit)
    {
        MapRoom();
        var (nx, ny, nz) = (_roomSolid.GetLength(0), _roomSolid.GetLength(1), _roomSolid.GetLength(2));
        var reach = (int)MathF.Ceiling(limit / RoomCell);
        var (px, py, pz) = ((local.X - RoomMapFrom.x) / RoomCell, (local.Y - RoomMapFrom.y) / RoomCell, (local.Z - RoomMapFrom.z) / RoomCell);
        var (ox, oy, oz) = ((int)MathF.Floor(px), (int)MathF.Floor(py), (int)MathF.Floor(pz));
        var nearest = limit;
        for (var x = Math.Max(0, ox - reach); x <= Math.Min(nx - 1, ox + reach); x++)
            for (var y = Math.Max(0, oy - reach); y <= Math.Min(ny - 1, oy + reach); y++)
                for (var z = Math.Max(0, oz - reach); z <= Math.Min(nz - 1, oz + reach); z++)
                {
                    if (!_roomSolid[x, y, z])
                        continue;
                    // To the cell's middle, less half a cell for its own size.
                    var (dx, dy, dz) = (x + 0.5f - px, y + 0.5f - py, z + 0.5f - pz);
                    nearest = MathF.Min(nearest, (MathF.Sqrt(dx * dx + dy * dy + dz * dz) - 0.5f) * RoomCell);
                }
        return MathF.Max(0f, nearest);
    }

    // The furthest the camera may stand on a bearing and tilt, up to the distance asked for,
    // with the clearance kept all the way out from its nearest place.
    private float ClearDistance(float yaw, float tilt, float wanted) =>
        ClearDistance(N(PlanetFraming.Target), PlanetOrbitWay(yaw, tilt), wanted, PlanetOrbitNearest);

    private float ClearDistance(NVector3 target, NVector3 way, float wanted, float nearest)
    {
        var clear = nearest;
        for (var distance = nearest; distance <= wanted + 0.001f; distance += 0.1f)
        {
            if (RoomClearance(target + way * distance, PlanetOrbitClearance) < PlanetOrbitClearance)
                break;
            clear = distance;
        }
        return MathF.Min(wanted, clear);
    }
}
