using Quat = System.Numerics.Quaternion;
using Vec = System.Numerics.Vector3;
using UnityEngine;

namespace WOMENACE.Code;

// The refitting room's hall as the armoury's camera sees it: a grid of where the room has
// surfaces, and its open floor, which keep the camera inside the walls and nothing between it
// and the squad.
public sealed partial class StrategyDiorama
{
    // The refitting room's open floor, which the camera closes in on the squad rather than
    // leave, so a wide orbit never looks in through a wall: every cell of ground the squad's
    // spot connects to without crossing the room's walls and pillars, less a camera's margin
    // from them (MapHall). A room whose meshes cannot be read has no floor plan and no bound.
    private bool OnHallFloor(Vector3 local) => OnHallFloor(local.x, local.z);

    private bool OnHallFloor(float x, float z)
    {
        if (_staging.HallFloor == null)
            return true;
        var (cx, cz) = ((int)((x + HallReach) / HallCell), (int)((z + HallReach) / HallCell));
        return cx >= 0 && cz >= 0 && cx < HallSide && cz < HallSide && _staging.HallFloor[cx, cz];
    }

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
    // Ground is a level face this near the squad's own height.
    private const float HallGroundBand = 0.3f;
    // A wall or a pillar stands through most of the height a camera moves in (the cells from
    // 0.4 m to 5.2 m up), where a crate or a workbench does not.
    private const int HallWallFrom = 1;
    private const int HallWallTo = 13;
    private const int HallWallCells = 9;
    // How many cells the floor plan keeps back from a wall.
    private const int HallFloorMargin = 2;

    private void MapHall(Transform set)
    {
        if (_staging.HallSolid != null && _staging.HallMapped == set.Pointer)
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
        (_staging.HallSolid, _staging.HallFloor, _staging.HallMapped) = (solid, FloorPlan(solid, ground), set.Pointer);
        var cells = 0;
        if (_staging.HallFloor != null)
            foreach (var cell in _staging.HallFloor)
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
        return cx >= 0 && cy >= 0 && cz >= 0 && cx < HallSide && cy < HallTall && cz < HallSide && _staging.HallSolid[cx, cy, cz];
    }

    // How far back the camera stands clear of whatever first comes between it and the squad,
    // and how far apart the lines it looks along are.
    private const float HallClearance = 0.8f;
    private const float HallEasing = 14f;
    private const float HallAbreast = 0.6f;

    // The furthest the camera can stand along its line, up to a distance, with nothing of the
    // hall walling it off from the squad. The line is walked out from the squad's own framing,
    // five abreast (the line and four beside it, above, below and to each side), and the camera
    // stops short of the first place where most of them meet a surface together: a wall, a door
    // header, a stack of crates. A cable, a rail or a hanging lamp crosses one of them and is
    // looked past, as the eye looks past it. The walk is in the set's space and in managed
    // maths: it runs every frame the armoury shows.
    private float ClearOfHall(Vec focus, Quat rotation, float distance)
    {
        if (_staging.HallSolid == null || _staging.Stage == null)
            return distance;
        var closest = MathF.Max(OrbitDistanceMin, _staging.FramedRadius * 1.6f);
        var across = Vec.Transform(Vec.UnitX, rotation);
        var at = N(_staging.Stage.InverseTransformPoint(U(focus)));
        var back = N(_staging.Stage.InverseTransformDirection(U(Vec.Transform(-Vec.UnitZ, rotation) + across * StagePan())));
        var right = N(_staging.Stage.InverseTransformDirection(U(across))) * HallAbreast;
        var up = N(_staging.Stage.InverseTransformDirection(U(Vec.Transform(Vec.UnitY, rotation)))) * HallAbreast;
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
                return MathF.Max(closest, d - HallClearance);
        }
        return distance;
    }

    private bool InsideHall(Vec focus, Quat rotation, ref float distance)
    {
        if (_staging.Stage == null)
            return true;
        // Never closer than the footprint allows: a vehicle would swallow the camera.
        var closest = MathF.Max(OrbitDistanceMin, _staging.FramedRadius * 1.6f);
        // The camera stands back along its line and across by the shot's pan, and that is the
        // place asked of the floor. The line's end alone can be clear at a distance where the
        // camera, beside it, stands off the floor (in the room kept round the pillar), and
        // then the bearing is refused where coming closer would do. Its place is a straight
        // line of the distance, taken into the hall's space once: each distance asked of it
        // is plain arithmetic, where Unity's maths is an interop call per operation.
        var start = N(_staging.Stage.InverseTransformPoint(U(focus)));
        var along = N(_staging.Stage.InverseTransformVector(U(Vec.Transform(new Vec(StagePan(), 0f, -1f), rotation))));
        bool Stands(float at) => OnHallFloor(start.X + along.X * at, start.Z + along.Z * at);
        for (int i = 0; i < 24; i++)
        {
            if (Stands(distance))
            {
                // The furthest it can stand, found between this step and the one before it:
                // taken a whole step at a time, the camera hops in and out as it turns.
                var (near, far) = (distance, i == 0 ? distance : distance / 0.92f);
                for (var cut = 0; cut < 7 && far > near; cut++)
                {
                    var middle = (near + far) / 2f;
                    if (Stands(middle))
                        near = middle;
                    else
                        far = middle;
                }
                distance = near;
                return true;
            }
            if (distance * 0.92f <= closest)
                return false;
            distance *= 0.92f;
        }
        return false;
    }
}
