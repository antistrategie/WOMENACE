using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using UnityEngine;

namespace WOMENACE.Code;

// Mission preparation's map as a hologram on the holotable: the ground, roads and deployment zone
// as a flat sheet over the table, and each structure a block standing on its tiles. It is built
// from the preview vanilla generates for the screen, which is the map the mission is then fought
// on. Units are left to vanilla's own map, which shows them by the mission's intelligence level.
public sealed partial class StrategyDiorama
{
    // Straight down on the table from over its foot, so the map lies nearly square in the view,
    // shifted to the middle of the space between the roster and the squad panel.
    private static readonly Framing PrepFraming = new(new Vector3(-11.9f, 3.4f, 0f), new Vector3(-14.5f, 0.3f, 0f), 50f, 0.195f);

    // The map's middle in room space, and its longer side's length. It is wider than the table, so it
    // floats clear above it, as a projection.
    private static readonly Vector3 PrepMapCentre = new(-14.4f, 0.45f, 0f);
    private const float PrepMapSide = 2.6f;
    private const int PrepTilePixels = 8;
    private const float PrepMapIntensity = 1.4f;

    private static readonly Color32 PrepGround = new(8, 20, 30, 255);
    private static readonly Color32 PrepGrid = new(26, 58, 84, 255);
    private static readonly Color32 PrepRoad = new(40, 70, 96, 255);
    private static readonly Color32 PrepZone = new(120, 92, 30, 255);
    private static readonly Color32 PrepZoneGrid = new(215, 170, 70, 255);
    private static readonly Color32 PrepZoneRefused = new(40, 34, 24, 255);
    private static readonly Color32 PrepZoneRefusedGrid = new(92, 76, 40, 255);
    private static readonly Color32 PrepObjectiveEdge = new(215, 192, 116, 255);
    private static readonly Color32 PrepRoof = new(56, 112, 150, 255);
    private static readonly Color32 PrepRoofEdge = new(150, 215, 245, 255);
    private static readonly Color32 PrepWallTop = new(110, 180, 215, 255);
    private static readonly Color32 PrepWallFoot = new(14, 34, 48, 255);

    private GameObject _prepMap;
    private Texture2D _prepMapTexture;
    private IntPtr _prepMapOf;
    private DeploymentZoneScale _prepMapScale;
    private string _prepMapAreas;
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
    private Mesh _prepMapMesh;
    private Vector3[] _prepMapFull;
    private float[] _prepMapStarts;
    private float _prepMapBornAt;
    private bool _prepMapBuilding;
    // A new mission's map whose build has not been heard yet.
    private bool _prepMapUnheard;

    // When a tile's square starts up, in seconds from the map's start.
    private float PrepBuildStart(float x, float z)
    {
        var across = _prepMapSizeX > 1 ? Mathf.Abs(x - _prepMapSizeX / 2f) / (_prepMapSizeX / 2f) : 0f;
        // The build's front gathers speed up the map and settles at its head (Ease.InOutCubic
        // of the sweep's time), so a row starts when the front reaches it.
        return Ease.InOutCubicInverse(z / Math.Max(1, _prepMapSizeZ)) * PrepBuildSweep + across * PrepBuildSpread;
    }

    // Eases 0 to 1, drawing back below 0 first and running past 1 before it settles.
    private static float BackAndPast(float t)
    {
        const float c = 1.70158f * 1.525f;
        return t < 0.5f
            ? 4f * t * t * ((c + 1f) * 2f * t - c) / 2f
            : ((2f * t - 2f) * (2f * t - 2f) * ((c + 1f) * (2f * t - 2f) + c) + 2f) / 2f;
    }

    // How far a thing standing on a tile has grown, 0 to 1 and a little past on the way: it
    // starts once the tile's square is most of the way home.
    private float PrepBuilt(float x, float z)
    {
        var t = (Time.unscaledTime - _prepMapBornAt - PrepBuildStart(x, z) - PrepBuildTile * 0.7f) / PrepBuildRise;
        if (t <= 0f)
            return 0f;
        if (t >= 1f)
            return 1f;
        var back = t - 1f;
        return 1f + 2.70158f * back * back * back + 1.70158f * back * back;
    }

    private void ShowPrepMapBuild()
    {
        if (_prepMapMesh == null || _prepMapFull == null)
            return;
        var age = Time.unscaledTime - _prepMapBornAt;
        var shown = new Vector3[_prepMapFull.Length];
        var lit = new Color[_prepMapFull.Length];
        var drop = PrepBuildDrop * _prepMapTile;
        for (var i = 0; i < shown.Length; i++)
        {
            var since = age - _prepMapStarts[i];
            if (since < 0f)
            {
                shown[i] = _prepMapFull[i - i % 4];
                shown[i].y = -drop;
                lit[i] = Color.white;
                continue;
            }
            var eased = since >= PrepBuildTile ? 1f : BackAndPast(since / PrepBuildTile);
            var grow = (since - PrepBuildTile * 0.7f) / PrepBuildRise;
            var grown = grow <= 0f ? 0f : grow >= 1f ? 1f : 1f + 2.70158f * (grow - 1f) * (grow - 1f) * (grow - 1f) + 1.70158f * (grow - 1f) * (grow - 1f);
            shown[i] = _prepMapFull[i];
            shown[i].y = shown[i].y * grown - drop * (1f - eased);
            // Past its place a square is lighter, by how far past it is.
            var light = 1f + PrepBuildFlash * Mathf.Clamp01((eased - 1f) / 0.1f);
            lit[i] = new Color(light, light, light, 1f);
        }
        _prepMapMesh.vertices = shown;
        _prepMapMesh.colors = lit;
        if (age >= PrepBuildSweep + PrepBuildSpread + PrepBuildTile + PrepBuildRise)
            _prepMapBuilding = false;
    }

    private int _prepMapSizeX, _prepMapSizeZ;
    private float _prepMapTile;

    // A place on the map in the world: tile coordinates (a tile's middle is half past its number)
    // and a height in tiles.
    private Vector3 PrepMapPoint(float x, float z, float up) =>
        _prepMap.transform.TransformPoint(new Vector3(-(z - _prepMapSizeZ / 2f) * _prepMapTile, up * _prepMapTile, (x - _prepMapSizeX / 2f) * _prepMapTile));

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
        if (_room == null || _policyScreen != VanillaUi.Screens.MissionPrep)
        {
            ClearPrepMap();
            return;
        }
        if (_prepMap != null && _prepMapBuilding)
        {
            // The build is heard as it starts, which is later than the map is made when the
            // planet's sheet is still on its way down to the table.
            if (_prepMapUnheard && Time.unscaledTime >= _prepMapBornAt)
            {
                _prepMapUnheard = false;
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
            if (preview.Pointer == _prepMapOf && scale.Equals(_prepMapScale) && areasKey == _prepMapAreas)
                return;
            // A new mission's map is built up before the eye. The same map made again (its zone
            // or its objectives changed) takes up where that had got to.
            // Coming from mission select, the map builds once the planet's sheet lies on the table.
            var born = preview.Pointer == _prepMapOf ? _prepMapBornAt : PlanetLaidBy();
            if (preview.Pointer != _prepMapOf)
                _prepMapUnheard = true;
            ClearPrepMap();
            BuildPrepMap(preview, scale, areas);
            (_prepMapOf, _prepMapScale, _prepMapAreas, _prepMapBornAt, _prepMapBuilding) = (preview.Pointer, scale, areasKey, born, true);
            ShowPrepMapBuild();
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: mission map failed: {ex.GetType().Name}: {ex.Message}");
            _prepMapOf = IntPtr.Zero;
        }
    }

    private void ClearPrepMap()
    {
        if (_prepMap != null)
            UnityEngine.Object.Destroy(_prepMap);
        if (_prepMapTexture != null)
            UnityEngine.Object.Destroy(_prepMapTexture);
        if (_prepMapMesh != null)
            UnityEngine.Object.Destroy(_prepMapMesh);
        (_prepMap, _prepMapTexture, _prepMapOf, _prepMapAreas, _prepMapMesh, _prepMapFull, _prepMapStarts) = (null, null, IntPtr.Zero, null, null, null, null);
    }

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

    private void BuildPrepMap(MissionPreviewResult preview, DeploymentZoneScale scale, List<RectInt> areas)
    {
        var template = MarkerTemplate();
        if (template == null)
            return;
        int sizeX = preview.SizeX, sizeZ = preview.SizeZ;
        if (sizeX <= 0 || sizeZ <= 0)
            return;
        var tiles = preview.Tiles;
        var heights = new float[sizeX, sizeZ];
        var names = new string[sizeX, sizeZ];
        var roads = new bool[sizeX, sizeZ];
        for (var z = 0; z < sizeZ; z++)
        {
            for (var x = 0; x < sizeX; x++)
            {
                var tile = tiles.GetTile(x, z);
                roads[x, z] = preview.HasRoad(x, z);
                if (tile.GetEntityType() != EntityType.Structure)
                    continue;
                names[x, z] = tile.m_EntityTemplate != null ? tile.m_EntityTemplate.name : "";
                heights[x, z] = PrepHeight(names[x, z]);
            }
        }
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
                    refused[x, z] = tile.IsBlocked() || tile.IsIsolated();
                }
        }

        var objective = new bool[sizeX, sizeZ];
        foreach (var area in areas)
            for (var z = Math.Max(0, area.y); z < Math.Min(sizeZ, area.yMax); z++)
                for (var x = Math.Max(0, area.x); x < Math.Min(sizeX, area.xMax); x++)
                    objective[x, z] = true;
        bool Objective(int x, int z) => x >= 0 && z >= 0 && x < sizeX && z < sizeZ && objective[x, z];

        // How bright a tile's lines are: 2 in the zone, 1 in its refused part, 0 elsewhere.
        int Lit(int x, int z) => x < 0 || z < 0 || !zone[x, z] ? 0 : refused[x, z] ? 1 : 2;

        // The sheet's texture: a block of pixels per tile, and above the map one more row of blocks
        // holding the wall's shade from its foot to its top.
        int p = PrepTilePixels, width = sizeX * p, height = (sizeZ + 1) * p;
        var pixels = new Color32[width * height];
        for (var z = 0; z < sizeZ; z++)
        {
            for (var x = 0; x < sizeX; x++)
            {
                var built = heights[x, z] > 0f;
                var fill = built ? PrepRoof : zone[x, z] ? (refused[x, z] ? PrepZoneRefused : PrepZone) : roads[x, z] ? PrepRoad : PrepGround;
                for (var j = 0; j < p; j++)
                {
                    for (var i = 0; i < p; i++)
                    {
                        // A structure's roof is edged where the structure ends, not between its tiles.
                        bool edge;
                        if (built)
                            edge = (i == 0 && !Same(names, x, z, x - 1, z)) || (i == p - 1 && !Same(names, x, z, x + 1, z))
                                || (j == 0 && !Same(names, x, z, x, z - 1)) || (j == p - 1 && !Same(names, x, z, x, z + 1));
                        else
                            edge = i == 0 || j == 0;
                        // A tile draws the lines on its near and left sides, which are its neighbours'
                        // far and right ones: a line is the brightest of the tiles it lies between.
                        var lit = !edge || built ? 0 : Math.Max(Math.Max(Lit(x, z), i == 0 ? Lit(x - 1, z) : 0),
                            Math.Max(j == 0 ? Lit(x, z - 1) : 0, i == 0 && j == 0 ? Lit(x - 1, z - 1) : 0));
                        var pixel = edge ? (built ? PrepRoofEdge : lit == 2 ? PrepZoneGrid : lit == 1 ? PrepZoneRefusedGrid : PrepGrid) : fill;
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
            var shade = Color32.Lerp(PrepWallFoot, PrepWallTop, j / (p - 1f));
            for (var i = 0; i < width; i++)
                pixels[(sizeZ * p + j) * width + i] = shade;
        }
        _prepMapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        _prepMapTexture.SetPixels32(pixels);
        _prepMapTexture.Apply(false, true);

        // The mesh, in the room's space about the map's middle: the map's x runs across the table
        // to the view's right, its z up the table away from the view.
        var tileSize = PrepMapSide / Math.Max(sizeX, sizeZ);
        (_prepMapSizeX, _prepMapSizeZ, _prepMapTile) = (sizeX, sizeZ, tileSize);
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
        void Wall(float x0, float z0, float x1, float z1, float foot, float top)
        {
            var u = 0.5f;
            Face(At(x0, z0, foot), At(x1, z1, foot), At(x1, z1, top), At(x0, z0, top),
                new Vector2(u, wallFoot), new Vector2(u, wallFoot), new Vector2(u, wallTop), new Vector2(u, wallTop));
        }

        // The ground is a square to a tile, so the squares can come up one by one.
        for (var z = 0; z < sizeZ; z++)
        {
            for (var x = 0; x < sizeX; x++)
            {
                start = PrepBuildStart(x, z);
                Face(At(x, z, 0), At(x + 1, z, 0), At(x + 1, z + 1, 0), At(x, z + 1, 0), Uv(x, z), Uv(x + 1, z), Uv(x + 1, z + 1), Uv(x, z + 1));
            }
        }
        for (var z = 0; z < sizeZ; z++)
        {
            for (var x = 0; x < sizeX; x++)
            {
                start = PrepBuildStart(x, z);
                var top = heights[x, z];
                if (top <= 0f)
                    continue;
                Face(At(x, z, top), At(x + 1, z, top), At(x + 1, z + 1, top), At(x, z + 1, top), Uv(x, z), Uv(x + 1, z), Uv(x + 1, z + 1), Uv(x, z + 1));
                // A wall stands where the neighbouring tile is lower, from that tile's height up.
                float Beside(int nx, int nz) => nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ ? 0f : heights[nx, nz];
                if (Beside(x - 1, z) < top) Wall(x, z, x, z + 1, Beside(x - 1, z), top);
                if (Beside(x + 1, z) < top) Wall(x + 1, z, x + 1, z + 1, Beside(x + 1, z), top);
                if (Beside(x, z - 1) < top) Wall(x, z, x + 1, z, Beside(x, z - 1), top);
                if (Beside(x, z + 1) < top) Wall(x, z + 1, x + 1, z + 1, Beside(x, z + 1), top);
            }
        }
        var mesh = new Mesh { name = "WOMENACE_PrepMap", vertices = vertices.ToArray(), uv = uvs.ToArray(), triangles = triangles.ToArray() };
        mesh.RecalculateBounds();
        (_prepMapMesh, _prepMapFull, _prepMapStarts) = (mesh, vertices.ToArray(), starts.ToArray());

        _prepMap = Quad(template, "WOMENACE_PrepMap", _prepMapTexture);
        _prepMap.transform.localPosition = PrepMapCentre;
        _prepMap.transform.localRotation = Quaternion.identity;
        _prepMap.transform.localScale = Vector3.one;
        _prepMap.GetComponent<MeshFilter>().sharedMesh = mesh;
        var material = _prepMap.GetComponent<MeshRenderer>()?.material;
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
    }

    private static bool Same(string[,] names, int x, int z, int nx, int nz) =>
        nx >= 0 && nz >= 0 && nx < names.GetLength(0) && nz < names.GetLength(1) && names[nx, nz] == names[x, z];
}
