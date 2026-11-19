using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace WOMENACE.Code;

// Mission preparation's map as a hologram on the holotable: the ground, roads and deployment zone
// as a flat sheet over the table, and each structure a block standing on its tiles. It is built
// from the preview vanilla generates for the screen, which is the map the mission is then fought
// on. Units are left to vanilla's own map, which shows them by the mission's intelligence level.
public sealed partial class StrategyDiorama
{
    // Straight down on the table from over its foot, so the map lies nearly square in the view,
    // shifted to the middle of the space between the frame's left and the roster.
    private static readonly Framing PrepFraming = new(new Vector3(-11.9f, 3.4f, 0f), new Vector3(-14.5f, 0.3f, 0f), 50f, 0.279f);

    // The map's middle in room space, and its longer side's length. It is wider than the table, so it
    // floats clear above it, as a projection.
    private static readonly Vector3 PrepMapCentre = new(-14.4f, 0.45f, 0f);
    private const float PrepMapSide = 2.6f;
    private const int PrepTilePixels = 8;
    private const float PrepMapIntensity = 1.4f;

    private static readonly Color32 PrepGround = new(8, 20, 30, 255);
    private static readonly Color32 PrepGrid = new(26, 58, 84, 255);
    private static readonly Color32 PrepRoad = new(86, 106, 128, 255);
    private static readonly Color32 PrepRough = new(44, 96, 124, 255);
    // The zone is a light wash inside a bright outline, so what stands in it still reads.
    private static readonly Color32 PrepZone = new(70, 54, 18, 255);
    private static readonly Color32 PrepZoneGrid = new(160, 126, 50, 255);
    private static readonly Color32 PrepZoneEdge = new(250, 200, 84, 255);
    private static readonly Color32 PrepZoneRefused = new(40, 34, 24, 255);
    private static readonly Color32 PrepZoneRoad = new(126, 104, 58, 255);
    private static readonly Color32 PrepZoneRefusedRoad = new(86, 74, 48, 255);
    private static readonly Color32 PrepZoneRefusedGrid = new(92, 76, 40, 255);
    private static readonly Color32 PrepObjectiveEdge = new(236, 244, 250, 255);
    private static readonly Color32 PrepRoof = new(56, 112, 150, 255);
    private static readonly Color32 PrepRoofEdge = new(150, 215, 245, 255);
    private static readonly Color32 PrepWallTop = new(110, 180, 215, 255);
    private static readonly Color32 PrepWallFoot = new(14, 34, 48, 255);
    // What vanilla marks on its own map (towers and bunkers a squad can garrison, a weapon cache),
    // in violet, a hue nothing else on the map has: the zone is gold, civilians orange, enemies red
    // and objectives white.
    private static readonly Color32 PrepMarkedRoof = new(92, 50, 150, 255);
    private static readonly Color32 PrepMarkedEdge = new(205, 155, 255, 255);
    private static readonly Color32 PrepMarkedWallTop = new(170, 120, 240, 255);
    private static readonly Color32 PrepMarkedWallFoot = new(26, 12, 46, 255);

    // What a structure is on the map, which its roof, edge and walls are shaded for.
    private enum PrepStructure : byte { Plain, Marked }

    // Dev builds can draw a structure as another kind, to see a kind no mission at hand has.
    static partial void PrepKindOverride(string template, ref PrepStructure kind);

    // Vanilla's own map gives a structure its own colour where its template asks (towers, bunkers,
    // the weapon cache).
    private static PrepStructure PrepKind(Il2CppMenace.Tactical.EntityTemplate template) =>
        template != null && template.OverrideMissionPreviewColor ? PrepStructure.Marked : PrepStructure.Plain;

    // The map's build. The ground is a square to a tile, and the squares come up row by row
    // from the near side to the far, each from below its place: it dips first, rises past its
    // place, where it is at its lightest, and settles. A structure grows out of its square once
    // the square is nearly home. Every face is four vertices and has the moment its tile starts.
    // Until then a face is closed up on its first vertex.
    private const float PrepBuildSweep = 1.3f;
    // Along a row the squares start a little later towards its ends.
    private const float PrepBuildSpread = 0.18f;
    private const float PrepBuildTile = 0.55f;
    private const float PrepBuildRise = 0.45f;
    // How far below its place a square starts, in tiles, and how much lighter it is at its peak.
    private const float PrepBuildDrop = 1.6f;
    private const float PrepBuildFlash = 1.6f;

    // When a tile's square starts up, in seconds from the map's start.
    private float PrepBuildStart(float x, float z)
    {
        var across = _prep.MapSizeX > 1 ? MathF.Abs(x - _prep.MapSizeX / 2f) / (_prep.MapSizeX / 2f) : 0f;
        // The build's front gathers speed up the map and settles at its head (Ease.InOutCubic
        // of the sweep's time), so a row starts when the front reaches it.
        return Ease.InOutCubicInverse(z / Math.Max(1, _prep.MapSizeZ)) * PrepBuildSweep + across * PrepBuildSpread;
    }

    // How far a thing standing on a tile has grown, 0 to 1 and a little past on the way: it
    // starts once the tile's square is most of the way home.
    private float PrepBuilt(float x, float z, float now)
    {
        var t = (now - _prep.MapBornAt - PrepBuildStart(x, z) - PrepBuildTile * 0.7f) / PrepBuildRise;
        return t <= 0f ? 0f : Ease.OutBack(t);
    }

    private void ShowPrepMapBuild()
    {
        if (_prep.MapMesh == null || _prep.MapFull == null)
            return;
        var age = Time.unscaledTime - _prep.MapBornAt;
        // Native arrays, made once a map: the mesh takes them without a copy into the game's
        // memory, and filling them is plain arithmetic, every frame of the build.
        if (_prep.MapShown == null || _prep.MapShown.Length != _prep.MapFull.Length)
            (_prep.MapShown, _prep.MapLit) = (new Il2CppStructArray<Vector3>(_prep.MapFull.Length), new Il2CppStructArray<Color>(_prep.MapFull.Length));
        var (shown, lit) = (_prep.MapShown, _prep.MapLit);
        var white = new Color { r = 1f, g = 1f, b = 1f, a = 1f };
        var drop = PrepBuildDrop * _prep.MapTile;
        for (var i = 0; i < shown.Length; i++)
        {
            var since = age - _prep.MapStarts[i];
            if (since < 0f)
            {
                var start = _prep.MapFull[i - i % 4];
                start.y = -drop;
                shown[i] = start;
                lit[i] = white;
                continue;
            }
            var eased = since >= PrepBuildTile ? 1f : Ease.InOutBack(since / PrepBuildTile);
            var grow = (since - PrepBuildTile * 0.7f) / PrepBuildRise;
            var grown = grow <= 0f ? 0f : Ease.OutBack(grow);
            var place = _prep.MapFull[i];
            place.y = place.y * grown - drop * (1f - eased);
            shown[i] = place;
            // Past its place a square is lighter, by how far past it is.
            var light = 1f + PrepBuildFlash * Math.Clamp((eased - 1f) / 0.1f, 0f, 1f);
            lit[i] = new Color { r = light, g = light, b = light, a = 1f };
        }
        _prep.MapMesh.vertices = shown;
        _prep.MapMesh.colors = lit;
        if (age >= PrepBuildSweep + PrepBuildSpread + PrepBuildTile + PrepBuildRise)
            _prep.MapBuilding = false;
    }


    // A place on the map in the world: tile coordinates (a tile's middle is half past its number)
    // and a height in tiles.
    // The height is above the tile's own ground, or above the map's foot when onGround is off.
    private Vector3 PrepMapPoint(float x, float z, float up, bool onGround = true)
    {
        var local = PrepMapLocal(x, z, up, onGround);
        return _prep.Map.transform.TransformPoint(new Vector3(local.X, local.Y, local.Z));
    }

    // The same place in the map's own space, in plain arithmetic for what is placed every frame.
    private NVector3 PrepMapLocal(float x, float z, float up, bool onGround = true) =>
        new(-(z - _prep.MapSizeZ / 2f) * _prep.MapTile, (up + (onGround ? PrepGroundAt(x, z) : 0f)) * _prep.MapTile, (x - _prep.MapSizeX / 2f) * _prep.MapTile);

    // The land's relief. The preview's elevation is a small part of 1 over a whole map, so it
    // is drawn many times its size, from the map's lowest tile up, in steps a quarter tile high:
    // a hill stands as terraces, each with a wall down to the one below.
    private const float PrepReliefTiles = 30f;
    private const float PrepReliefStep = 0.25f;

    // How high a tile's ground stands above the map's foot, in tiles.
    private float PrepGroundAt(float x, float z)
    {
        if (_prep.MapGround == null)
            return 0f;
        var (tx, tz) = (Math.Clamp((int)MathF.Floor(x), 0, _prep.MapSizeX - 1), Math.Clamp((int)MathF.Floor(z), 0, _prep.MapSizeZ - 1));
        return _prep.MapGround[tx, tz];
    }

    // The tile a ray lands on, as tile coordinates. The ground stands at several heights, so
    // the ray is met with each from the highest down and lands on the first whose tile is there.
    // The ray is taken into the map's own space once, where each height is a level plane.
    private Vector2 PrepTileUnder(Ray ray)
    {
        var tile = Vector2.zero;
        var toMap = ToMat(_prep.Map.transform.worldToLocalMatrix);
        var from = NVector3.Transform(N(ray.origin), toMap);
        var way = NVector3.TransformNormal(N(ray.direction), toMap);
        foreach (var level in _prep.MapLevels)
        {
            var local = from + way * ((level * _prep.MapTile - from.Y) / way.Y);
            tile = new Vector2(local.Z / _prep.MapTile + _prep.MapSizeX / 2f, _prep.MapSizeZ / 2f - local.X / _prep.MapTile);
            if (tile.x >= 0f && tile.y >= 0f && tile.x < _prep.MapSizeX && tile.y < _prep.MapSizeZ && PrepGroundAt(tile.x, tile.y) >= level - 0.01f)
                break;
        }
        return tile;
    }

    // The objectives' areas as vanilla's own map shows them, in tiles: read from its elements, so
    // the hologram marks what that map marks and no more.
    private List<RectInt> PrepObjectiveAreas(int sizeX, int sizeZ)
    {
        var areas = new List<RectInt>();
        var holder = _shellRoot != null ? UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.PrepObjectiveAreas)) : null;
        if (holder == null || holder.layout.width <= 0f)
            return areas;
        var (tileX, tileZ) = (holder.layout.width / sizeX, holder.layout.height / sizeZ);
        for (var i = 0; i < holder.childCount; i++)
        {
            var box = holder.ElementAt(i).layout;
            if (box.width <= 0f || box.height <= 0f)
                continue;
            var (x0, x1) = (Mathf.RoundToInt(box.xMin / tileX), Mathf.RoundToInt(box.xMax / tileX));
            // The element's top is the map's far side.
            var (z0, z1) = (sizeZ - Mathf.RoundToInt(box.yMax / tileZ), sizeZ - Mathf.RoundToInt(box.yMin / tileZ));
            areas.Add(new RectInt(x0, z0, x1 - x0, z1 - z0));
        }
        return areas;
    }

    // Builds the hologram once the screen's preview is ready, again when the deployment zone changes size, and puts it away with the screen.
    private void DrivePrepMap()
    {
        if (_scene.Room == null || _policyScreen != VanillaUi.Screens.MissionPrep)
        {
            ClearPrepMap();
            return;
        }
        if (_prep.Map != null && _prep.MapBuilding)
        {
            // The build is heard as it starts, which is later than the map is made when the
            // planet's sheet is still on its way down to the table.
            if (_prep.MapUnheard && Time.unscaledTime >= _prep.MapBornAt)
            {
                _prep.MapUnheard = false;
                TableSounds.Play(TableSounds.MapBuild);
            }
            ShowPrepMapBuild();
        }
        if (Time.frameCount % 10 != 0)
            return;
        try
        {
            var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<MissionPrepUIScreen>();
            var preview = screen?.GetMission()?.GetPreview();
            if (preview == null)
                return;
            // Vanilla widens the zone for a squad whose effects say so, while that squad is dragged.
            var scale = screen.m_DeploymentScale;
            var areas = PrepObjectiveAreas(preview.SizeX, preview.SizeZ);
            var areasKey = string.Join(";", areas.Select(a => $"{a.x},{a.y},{a.width},{a.height}"));
            if (preview.Pointer == _prep.MapOf && scale.Equals(_prep.MapScale) && areasKey == _prep.MapAreas)
                return;
            // A new mission's map is built up before the eye. The same map made again (its zone
            // or its objectives changed) takes up where that had got to.
            // Coming from mission select, the map builds once the planet's sheet lies on the table.
            var born = preview.Pointer == _prep.MapOf ? _prep.MapBornAt : PlanetLaidBy();
            if (preview.Pointer != _prep.MapOf)
                _prep.MapUnheard = true;
            ClearPrepMap();
            // A map that could not be built is tried again, as a map not yet seen.
            if (!BuildPrepMap(preview, scale, areas))
                return;
            (_prep.MapOf, _prep.MapScale, _prep.MapAreas, _prep.MapBornAt, _prep.MapBuilding) = (preview.Pointer, scale, areasKey, born, true);
            ShowPrepMapBuild();
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: mission map failed: {ex.GetType().Name}: {ex.Message}");
            _prep.MapOf = IntPtr.Zero;
        }
    }

    private void ClearPrepMap()
    {
        if (_prep.Map != null)
            UnityEngine.Object.Destroy(_prep.Map);
        _prep.MapMade.Clear();
        // The flat view's picture is cut from the texture and goes with it. It is known by the
        // texture's address, which the next texture may be given.
        if (_prep.FlatPicture != null)
            UnityEngine.Object.Destroy(_prep.FlatPicture);
        (_prep.FlatPicture, _prep.FlatPictureOf) = (null, IntPtr.Zero);
        (_prep.Map, _prep.MapTexture, _prep.MapOf, _prep.MapAreas, _prep.MapMesh, _prep.MapFull, _prep.MapStarts) = (null, null, IntPtr.Zero, null, null, null, null);
        (_prep.MapShown, _prep.MapLit) = (null, null);
        (_prep.MapSizeX, _prep.MapSizeZ, _prep.MapTile, _prep.MapGround, _prep.MapLevels) = (0, 0, 0f, null, PrepNoLevels);
    }

    // The heights of a map with no relief, or of no map.
    private static readonly float[] PrepNoLevels = { 0f };

    // How tall a structure stands, in tiles, from what its template is called.
    private static float PrepHeight(string template)
    {
        if (template.Contains("tower", StringComparison.Ordinal) || template.Contains("antenna", StringComparison.Ordinal))
            return 3.2f;
        if (template.Contains("warehouse", StringComparison.Ordinal) || template.Contains("factory_2x", StringComparison.Ordinal))
            return 1.6f;
        if (template.StartsWith("building", StringComparison.Ordinal))
            return 1.2f;
        return 0.5f;
    }

    // False when there is nothing yet to build the map with.
    private bool BuildPrepMap(MissionPreviewResult preview, DeploymentZoneScale scale, List<RectInt> areas)
    {
        var template = MarkerTemplate();
        if (template == null)
            return false;
        int sizeX = preview.SizeX, sizeZ = preview.SizeZ;
        if (sizeX <= 0 || sizeZ <= 0)
            return false;
        var tiles = preview.Tiles;
        var heights = new float[sizeX, sizeZ];
        var names = new string[sizeX, sizeZ];
        var kinds = new PrepStructure[sizeX, sizeZ];
        var roads = new bool[sizeX, sizeZ];
        var rough = new bool[sizeX, sizeZ];
        var ground = new float[sizeX, sizeZ];
        var lowest = float.MaxValue;
        for (var z = 0; z < sizeZ; z++)
        {
            for (var x = 0; x < sizeX; x++)
            {
                var tile = tiles.GetTile(x, z);
                roads[x, z] = preview.HasRoad(x, z);
                ground[x, z] = tile.GetElevation();
                lowest = Math.Min(lowest, ground[x, z]);
                if (tile.GetEntityType() != EntityType.Structure)
                {
                    // Ground with nothing built on it that no squad can stand on, which vanilla's
                    // own map shades the same as a structure.
                    rough[x, z] = !tile.IsValidDeployPosition();
                    continue;
                }
                names[x, z] = tile.m_EntityTemplate != null ? tile.m_EntityTemplate.name : "";
                kinds[x, z] = PrepKind(tile.m_EntityTemplate);
                PrepKindOverride(names[x, z], ref kinds[x, z]);
                heights[x, z] = PrepHeight(names[x, z]);
            }
        }
        for (var z = 0; z < sizeZ; z++)
            for (var x = 0; x < sizeX; x++)
                ground[x, z] = MathF.Round((ground[x, z] - lowest) * PrepReliefTiles / PrepReliefStep) * PrepReliefStep;
        _prep.MapGround = ground;
        _prep.MapLevels = ground.Cast<float>().Distinct().OrderByDescending(level => level).ToArray();
        var zone = new bool[sizeX, sizeZ];
        var refused = new bool[sizeX, sizeZ];
        foreach (var drawn in preview.DeploymentZones.ToArray())
        {
            // Vanilla counts a zone's far corner as part of it, in what it draws and where it lets
            // a squad stand, so a zone is a tile longer each way than its size says.
            var rect = MissionPreviewResult.ScaleDeploymentZone(drawn, scale, sizeX, sizeZ);
            for (var z = Math.Max(0, rect.y); z <= Math.Min(sizeZ - 1, rect.y + rect.height); z++)
                for (var x = Math.Max(0, rect.x); x <= Math.Min(sizeX - 1, rect.x + rect.width); x++)
                {
                    // Ground no squad can stand on (rock, water, a pocket nothing reaches) is in
                    // no picture of the hologram's, so its part of the zone is dimmed, which is
                    // how the eye is told a squad will be refused there.
                    var tile = tiles.GetTile(x, z);
                    zone[x, z] = true;
                    refused[x, z] = tile.IsBlocked() || tile.IsIsolated() || !tile.IsValidDeployPosition();
                }
        }

        var objective = new bool[sizeX, sizeZ];
        foreach (var area in areas)
            for (var z = Math.Max(0, area.y); z < Math.Min(sizeZ, area.yMax); z++)
                for (var x = Math.Max(0, area.x); x < Math.Min(sizeX, area.xMax); x++)
                    objective[x, z] = true;
        bool Objective(int x, int z) => x >= 0 && z >= 0 && x < sizeX && z < sizeZ && objective[x, z];

        // How bright a tile's lines are: 2 in the zone, 1 in its refused part, 0 elsewhere.
        int Lit(int x, int z) => x < 0 || z < 0 || x >= sizeX || z >= sizeZ || !zone[x, z] ? 0 : refused[x, z] ? 1 : 2;
        bool Road(int x, int z) => x >= 0 && z >= 0 && roads[x, z] && heights[x, z] <= 0f;

        // The sheet's texture: a block of pixels per tile, and above the map one more row of blocks
        // holding the wall's shade from its foot to its top.
        int p = PrepTilePixels, width = sizeX * p, height = (sizeZ + 1) * p;
        var pixels = new Color32[width * height];
        for (var z = 0; z < sizeZ; z++)
        {
            for (var x = 0; x < sizeX; x++)
            {
                var built = heights[x, z] > 0f;
                var road = Road(x, z);
                var hatched = !built && !zone[x, z] && !road && rough[x, z];
                var inZone = zone[x, z] ? (refused[x, z] ? (road ? PrepZoneRefusedRoad : PrepZoneRefused) : road ? PrepZoneRoad : PrepZone) : default;
                var kind = built ? kinds[x, z] : PrepStructure.Plain;
                var (roof, roofEdge) = kind == PrepStructure.Marked ? (PrepMarkedRoof, PrepMarkedEdge) : (PrepRoof, PrepRoofEdge);
                var fill = built ? roof : zone[x, z] ? inZone : road ? PrepRoad : PrepGround;
                for (var j = 0; j < p; j++)
                {
                    for (var i = 0; i < p; i++)
                    {
                        // A structure's roof is edged where the structure ends, not between its tiles.
                        // Ground has a line between each two tiles, drawn on the brighter of the
                        // two so it stays with that tile where they stand at different heights,
                        // and on the far or right one's near side where they are alike.
                        bool edge;
                        var lit = 0;
                        if (built)
                            edge = (i == 0 && !Same(names, x, z, x - 1, z)) || (i == p - 1 && !Same(names, x, z, x + 1, z))
                                || (j == 0 && !Same(names, x, z, x, z - 1)) || (j == p - 1 && !Same(names, x, z, x, z + 1));
                        else
                        {
                            lit = Lit(x, z);
                            edge = (i == 0 && Lit(x - 1, z) <= lit) || (j == 0 && Lit(x, z - 1) <= lit)
                                || (i == p - 1 && Lit(x + 1, z) < lit) || (j == p - 1 && Lit(x, z + 1) < lit);
                        }
                        var pixel = edge ? (built ? roofEdge : lit == 2 ? PrepZoneGrid : lit == 1 ? PrepZoneRefusedGrid : PrepGrid)
                            : hatched && (i + j) % 4 == 0 ? PrepRough : fill;
                        // The zone is outlined where it ends, its refused part included. The outline is
                        // the line between its tiles and the next, so its edge tiles are as large as
                        // the rest.
                        if (!built && zone[x, z] && ((i == 0 && Lit(x - 1, z) == 0) || (i == p - 1 && Lit(x + 1, z) == 0)
                            || (j == 0 && Lit(x, z - 1) == 0) || (j == p - 1 && Lit(x, z + 1) == 0)))
                            pixel = PrepZoneEdge;
                        // An objective's area is lit a little and outlined where it ends.
                        if (objective[x, z])
                        {
                            var rim = (i < 2 && !Objective(x - 1, z)) || (i >= p - 2 && !Objective(x + 1, z))
                                || (j < 2 && !Objective(x, z - 1)) || (j >= p - 2 && !Objective(x, z + 1));
                            pixel = rim ? PrepObjectiveEdge : Color32.Lerp(pixel, PrepObjectiveEdge, built ? 0f : 0.16f);
                        }
                        pixels[(z * p + j) * width + x * p + i] = pixel;
                    }
                }
            }
        }
        for (var j = 0; j < p; j++)
        {
            // The wall shades side by side, foot to top (PrepWallShades).
            for (var i = 0; i < width; i++)
            {
                var (foot, top) = PrepWallShades[i * PrepWallShades.Length / width];
                pixels[(sizeZ * p + j) * width + i] = Color32.Lerp(foot, top, j / (p - 1f));
            }
        }
        _prep.MapTexture = _prep.MapMade.Add(new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
        _prep.MapTexture.SetPixels32(pixels);
        _prep.MapTexture.Apply(false, true);

        // The mesh, in the room's space about the map's middle: the map's x runs across the table
        // to the view's right, its z up the table away from the view.
        var tileSize = PrepMapSide / Math.Max(sizeX, sizeZ);
        (_prep.MapSizeX, _prep.MapSizeZ, _prep.MapTile) = (sizeX, sizeZ, tileSize);
        var vertices = new List<Vector3>();
        var starts = new List<float>();
        var start = 0f;
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        float mapV = sizeZ / (sizeZ + 1f), wallFoot = (sizeZ * p + 0.5f) / height, wallTop = (sizeZ * p + p - 0.5f) / height;
        Vector3 At(float x, float z, float up) => new(-(z - sizeZ / 2f) * tileSize, up * tileSize, (x - sizeX / 2f) * tileSize);
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            var first = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (var corner = 0; corner < 4; corner++)
                starts.Add(start);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
        }
        Vector2 Uv(float x, float z) => new(x / sizeX, z / sizeZ * mapV);
        // A wall takes the shade of the tile whose side it is: its structure's, or the zone's.
        int Shade(int x, int z) => heights[x, z] > 0f ? (kinds[x, z] == PrepStructure.Marked ? 3 : 0) : Lit(x, z);
        void Wall(float x0, float z0, float x1, float z1, float foot, float top, int shade)
        {
            var u = (shade + 0.5f) / PrepWallShades.Length;
            Face(At(x0, z0, foot), At(x1, z1, foot), At(x1, z1, top), At(x0, z0, top),
                new Vector2(u, wallFoot), new Vector2(u, wallFoot), new Vector2(u, wallTop), new Vector2(u, wallTop));
        }

        // The ground is a square to a tile, so the squares can come up one by one.
        // A tile's ground stands at its own height and a structure on top of that. A wall stands
        // where the neighbouring tile is lower, from that tile's height up, and at the map's edge
        // down to its foot.
        float Top(int x, int z) => x < 0 || z < 0 || x >= sizeX || z >= sizeZ ? 0f : ground[x, z] + heights[x, z];
        for (var z = 0; z < sizeZ; z++)
        {
            for (var x = 0; x < sizeX; x++)
            {
                start = PrepBuildStart(x, z);
                var top = Top(x, z);
                if (heights[x, z] <= 0f || top <= 0f)
                    Face(At(x, z, ground[x, z]), At(x + 1, z, ground[x, z]), At(x + 1, z + 1, ground[x, z]), At(x, z + 1, ground[x, z]), Uv(x, z), Uv(x + 1, z), Uv(x + 1, z + 1), Uv(x, z + 1));
                else
                    Face(At(x, z, top), At(x + 1, z, top), At(x + 1, z + 1, top), At(x, z + 1, top), Uv(x, z), Uv(x + 1, z), Uv(x + 1, z + 1), Uv(x, z + 1));
                if (Top(x - 1, z) < top) Wall(x, z, x, z + 1, Top(x - 1, z), top, Shade(x, z));
                if (Top(x + 1, z) < top) Wall(x + 1, z, x + 1, z + 1, Top(x + 1, z), top, Shade(x, z));
                if (Top(x, z - 1) < top) Wall(x, z, x + 1, z, Top(x, z - 1), top, Shade(x, z));
                if (Top(x, z + 1) < top) Wall(x, z + 1, x + 1, z + 1, Top(x, z + 1), top, Shade(x, z));
            }
        }
        var mesh = _prep.MapMade.Add(new Mesh { name = "WOMENACE_PrepMap", vertices = vertices.ToArray(), uv = uvs.ToArray(), triangles = triangles.ToArray() });
        mesh.RecalculateBounds();
        (_prep.MapMesh, _prep.MapFull, _prep.MapStarts) = (mesh, vertices.ToArray(), starts.ToArray());

        _prep.Map = Quad(template, "WOMENACE_PrepMap", _prep.MapTexture);
        _prep.Map.transform.localPosition = PrepMapCentre;
        _prep.Map.transform.localRotation = Quaternion.identity;
        _prep.Map.transform.localScale = Vector3.one;
        _prep.Map.GetComponent<MeshFilter>().sharedMesh = mesh;
        var material = _prep.MapMade.Material(_prep.Map.GetComponent<MeshRenderer>());
        if (material != null)
        {
            material.SetFloat("_ScanDepth", 0f);
            material.SetFloat("_EdgeFade", 0f);
            material.SetFloat("_Reveal", 1f);
            material.SetFloat("_Intensity", PrepMapIntensity);
            material.SetColor("_Tint", Color.white);
            material.SetVector("_UvRect", new Vector4(1f, 1f, 0f, 0f));
            // Before the tokens that stand on it.
            material.renderQueue = TokenQueue - 2;
        }
        Context.Log.Info($"strategy diorama: mission map on the table, {sizeX} by {sizeZ} tiles, {vertices.Count} vertices");
        return true;
    }

    // The walls' shades, by Shade: a structure's, the zone's refused part's, the zone's, then the
    // marked structures'.
    private static readonly (Color32 Foot, Color32 Top)[] PrepWallShades =
    {
        (PrepWallFoot, PrepWallTop),
        (PrepZoneRefused, PrepZoneRefusedGrid),
        (PrepZone, PrepZoneGrid),
        (PrepMarkedWallFoot, PrepMarkedWallTop),
    };

    private static bool Same(string[,] names, int x, int z, int nx, int nz) =>
        nx >= 0 && nz >= 0 && nx < names.GetLength(0) && nz < names.GetLength(1) && names[nx, nz] == names[x, z];
}
