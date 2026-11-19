using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMenace.Strategy;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace WOMENACE.Code;

// Mission select's planet over the holotable. Vanilla's backdrop for the screen is a video of the
// operation's planet seen from orbit, for the operation's time of day. Here the video plays into a
// texture and is shown on a part of a true globe: the planet's edge is a circle in the picture,
// so each pixel is a point of a sphere facing the viewer, and the picture's part of that sphere
// stands over the table, leaning back. The missions' cards stand on it and the chibi walks its
// surface (StrategyDiorama.PlanetMarkers), and where a planet has no video or no edge on record
// the table keeps GFL2's land.
public sealed partial class StrategyDiorama
{
    // The planet's edge in its video, by planet: the circle's centre and radius in pixels of a
    // 1600 by 900 frame, fitted by scripts/diorama/planet_edge.py. A planet's day and night share one.
    private static readonly Dictionary<string, Vector3> PlanetEdges = new()
    {
        ["planet.backbone"] = new(1362f, 1813f, 1923f),
        ["planet.dice"] = new(-716f, 917f, 2329f),
        ["planet.mock"] = new(959f, 2494f, 2454f),
    };

    private const float PlanetFrameWidth = 1600f;
    private const float PlanetFrameHeight = 900f;
    // The globe's radius in metres, where the section's middle stands in room space (over the
    // middle of the table), and how far it leans back from upright.
    private const float PlanetRadius = 5f;
    private static readonly Vector3 PlanetPlace = new(-14.4f, 1.15f, 0.3f);
    private const float PlanetLean = 35f;
    // The point of the picture taken as the section's middle.
    private static readonly Vector2 PlanetMiddle = new(0.55f, 0.45f);
    // A little of the room shows through, as through a hologram.
    private const float PlanetOpacity = 0.85f;
    private const float PlanetIntensity = 1f;
    private const int PlanetAcross = 96;
    private const int PlanetDown = 54;
    private const int PlanetVideoWidth = 1920;
    private const int PlanetVideoHeight = 1080;
    // The room's holograms draw in their renderers' sorting order, which counts ahead of the
    // render queue. The table's dots are at 15 and its lines at 50. The planet stands in front
    // of both, then the chibi and the cards: she walks behind a card in her way.
    private const int PlanetOrder = 60;

    // From the table's right-hand side, looking across it at the planet, the lens shifted so
    // the planet stands left of the selected mission's panel.
    private static readonly Framing PlanetFraming = new(new Vector3(-11.95f, 2.17f, -4.22f), new Vector3(-14.5f, 1.4f, 0.2f), 55f, 0.11f);

    private static readonly Color PlanetEdgeTint = new(0.45f, 0.95f, 1f);

    // A planet's part of the globe: where a point of its picture lies on the unit sphere, and in
    // the room.
    private sealed class PlanetShape
    {
        public string Id;
        public float CentreX, CentreY, Rim;
        public Quaternion Turn;
        public Vector3 Middle;

        // A point of the picture (0 to 1 across and up) on the unit sphere facing the viewer,
        // and whether it is on the planet at all.
        public Vector3 OnSphere(float across, float up, out bool onPlanet)
        {
            var aspect = PlanetFrameHeight / PlanetFrameWidth;
            var (dx, dy) = ((across - CentreX) / Rim, -((1f - up) * aspect - CentreY * aspect) / Rim);
            var r2 = dx * dx + dy * dy;
            onPlanet = r2 < 0.995f;
            if (!onPlanet)
                (dx, dy, r2) = (dx * Mathf.Sqrt(0.995f / r2), dy * Mathf.Sqrt(0.995f / r2), 0.995f);
            return new Vector3(dx, dy, -Mathf.Sqrt(1f - r2));
        }

        // Where a point of the unit sphere is in room space.
        public Vector3 InRoom(Vector3 spot) => PlanetPlace + InSection(spot);

        // Where a point of the unit sphere is in the section's own space, about its middle.
        public Vector3 InSection(Vector3 spot) => Turn * (spot - Middle) * PlanetRadius;

        // The same in plain maths, for what is worked out every frame.
        public NVector3 InRoom(NVector3 spot) => PlaceN + NVector3.Transform(spot - MiddleN, TurnN) * PlanetRadius;

        public VideoClip Clip;
        public NQuaternion TurnN;
        public NVector3 MiddleN;
        private static readonly NVector3 PlaceN = N(PlanetPlace);
    }

    // The way the section's middle faces in room space: towards the table's long side, leaning
    // back. The section is upright with this lean taken out, and the globe's own middle is a
    // radius behind the section's along it.
    private static readonly Vector3 PlanetFacing = new(0f, Mathf.Sin(PlanetLean * Mathf.Deg2Rad), -Mathf.Cos(PlanetLean * Mathf.Deg2Rad));
    private static readonly Quaternion PlanetLeaning = Quaternion.FromToRotation(Vector3.back, PlanetFacing);
    private static readonly Vector3 GlobeRest = PlanetPlace - PlanetFacing * PlanetRadius;


    // The running operation's planet as a shape, or null where it has no edge on record or no
    // video for the operation's time of day.
    private PlanetShape CurrentPlanet() => PlanetOf(CurrentOperation());

    // An operation's planet is worked out once and kept while mission select stays open.
    private PlanetShape PlanetOf(Operation operation)
    {
        if (operation == null)
            return null;
        if (operation.Pointer == _missions.PlanetOperation)
            return _missions.PlanetShape;
        _missions.PlanetOperation = operation.Pointer;
        return _missions.PlanetShape = MakePlanetShape(operation);
    }

    static partial void PlanetIdOverride(ref string id);

    private static PlanetShape MakePlanetShape(Operation operation)
    {
        try
        {
            var template = operation.GetPlanetTemplate();
            var id = template?.GetID();
            PlanetIdOverride(ref id);
            if (id == null || !PlanetEdges.TryGetValue(id, out var edge))
                return null;
            // Vanilla's video of the planet at the operation's time of day.
            var clip = template.GetMissionSelectBackgroundVideo(operation.GetCurrentLightCondition())?.GetVideoClip();
            if (clip == null)
                return null;
            var shape = new PlanetShape { Id = id, Clip = clip, CentreX = edge.x / PlanetFrameWidth, CentreY = edge.y / PlanetFrameHeight, Rim = edge.z / PlanetFrameWidth };
            shape.Middle = shape.OnSphere(PlanetMiddle.x, PlanetMiddle.y, out _);
            // The middle of the section faces the way the planet does, whichever way its
            // picture was taken from.
            shape.Turn = Quaternion.FromToRotation(shape.Middle, PlanetFacing);
            (shape.MiddleN, shape.TurnN) = (N(shape.Middle), N(shape.Turn));
            return shape;
        }
        catch { return null; }
    }

    private static Operation CurrentOperation()
    {
        try { return UIManager.Get()?.GetActiveScreen()?.TryCast<MissionSelectUIScreen>()?.m_Operation; }
        catch { return null; }
    }

    // The planet's arrival, as parts of its length. A small wireframe globe draws itself from
    // its lower pole to its upper over the middle of the table, turns to bring the missions'
    // side round, and grows until that side stands over the table. The picture is then laid by
    // a scan line that takes the wireframe as it passes, and each mission's card opens as the
    // line passes its place. Leaving plays it backwards, faster.
    private const float PlanetArriveSeconds = 1.6f;
    private const float PlanetLeaveSeconds = 0.8f;
    private const float GlobeDrawnBy = 0.16f;
    private const float GlobeTurnedBy = 0.5f;
    private const float GlobeTurn = 200f;
    private const float GlobeGrowsFrom = 0.09f;
    private const float GlobeGrownBy = 0.53f;
    private const float PlanetScanFrom = 0.53f;
    private const float PlanetScanTo = 0.72f;
    private const float CardOpen = 0.1f;
    // The small globe: its radius and where its middle is in room space.
    private const float GlobeSmall = 0.6f;
    private static readonly Vector3 GlobeStart = new(-14.4f, 0.75f, 0f);
    // The grown globe's wireframe keeps to the table: a line shows only within this far of the
    // section's middle, measured round the globe, so it is a cap over the table.
    private const float GlobeReach = 2.3f;
    private const float GlobeReachSoften = 0.2f;
    private const float GlobeIntensity = 1.3f;
    private static readonly Color GlobeTint = new(0.35f, 0.9f, 1f);
    // The wireframe's pole leans, so its grid does not sit square on to the view.
    private static readonly Quaternion GlobeLean = Quaternion.Euler(0f, 0f, 24f) * Quaternion.Euler(18f, 0f, 0f);

    // The hand-off to mission preparation: the section unrolls from the globe into a flat sheet
    // while it tips onto the table, where preparation's map is. The map then builds over it and
    // the sheet is taken away along the table as the map's own build sweeps it.
    private const float PlanetLaySeconds = 1f;
    private const float PlanetWaitsForMap = 6f;
    // Flat on the table, the picture's left at the table's head and its right at the foot:
    // the map builds from the foot, and a sheet is taken from its right.
    private static readonly Quaternion PlanetLying = Quaternion.LookRotation(Vector3.down, Vector3.forward);


    // A dev build may hold the arrival at a moment, for looking at it.
    partial void HoldPlanetMoment(ref float moment);

    // A frame's share of time for the planet's own motion, which a stalled frame does not skip.
    private const float PlanetLongestStep = 1f / 20f;
    private static float PlanetStep => HitchStep(PlanetLongestStep);

    // Whether the planet stands arrived, or is already on its way down, as mission preparation
    // opens: it is then laid on the table for the map.
    private bool PlanetLays => _missions.Planet != null && (_missions.PlanetMoment >= 1f || _missions.PlanetLay > 0f);

    // The planet shows in mission select's Operations view: it arrives when the view is entered
    // and leaves when it is left.
    private void DrivePlanet()
    {
        if (_policyScreen == VanillaUi.Screens.MissionPrep && _missions.PlanetShape != null && PlanetLays)
        {
            LayPlanetForPrep(_missions.PlanetShape);
            return;
        }
        if (_missions.PlanetLay > 0f)
            EndPlanetLay();
        var onScreen = _scene.Room != null && _policyScreen == VanillaUi.Screens.MissionSelect;
        // A new visit asks again: the operation's time of day, and so its video, may have moved on.
        if (!onScreen)
            _missions.PlanetOperation = IntPtr.Zero;
        var shape = onScreen ? CurrentPlanet() : null;
        var inView = shape != null && _homeView == HomeView.Operations;
        if (inView && (_missions.Planet == null || _missions.PlanetBuilt != (shape.Id, shape.Clip.Pointer)))
            _missions.PlanetBuilt = BuildPlanet(shape) ? (shape.Id, shape.Clip.Pointer) : default;
        var wanted = inView && _missions.PlanetBuilt.Id == shape.Id;
        HideLandForPlanet(wanted);
        // The globe draws and grows while the camera is still on its way. The picture and the
        // cards wait for it to arrive, so they are seen from the view they are laid out for.
        var upTo = wanted ? (_shotTravel < 1f ? PlanetScanFrom : 1f) : 0f;
        if (shape == null)
            _missions.PlanetMoment = 0f;
        else if (_missions.PlanetMoment < upTo || !wanted)
            _missions.PlanetMoment = Mathf.MoveTowards(_missions.PlanetMoment, upTo, PlanetStep / (wanted ? PlanetArriveSeconds : PlanetLeaveSeconds));
        if (wanted)
            HoldPlanetMoment(ref _missions.PlanetMoment);
        if (_missions.Planet == null)
            return;
        var shown = _missions.PlanetMoment > 0f;
        if (shown != _missions.PlanetShown)
        {
            _missions.PlanetShown = shown;
            _missions.Planet.SetActive(shown);
            Cue(shown ? TableCue.Appear : TableCue.Vanish);
            if (shown)
            {
                _missions.PlanetPlayer.Play();
            }
            else
            {
                _missions.PlanetPlayer.Pause();
                ShowPlanetLines(false, false);
            }
        }
        if (shown)
            ApplyPlanetArrival(shape, _missions.PlanetMoment);
    }


    private void ShowPlanetLines(bool edge, bool globe)
    {
        if (edge != _missions.PlanetEdgeShown)
            _missions.PlanetEdge.SetActive(_missions.PlanetEdgeShown = edge);
        if (globe != _missions.PlanetGlobeShown)
            _missions.PlanetGlobe.SetActive(_missions.PlanetGlobeShown = globe);
    }

    // When the planet's sheet will lie flat on the table, for preparation's map to build after.
    private float PlanetLaidBy() => PlanetLays ? Time.unscaledTime + (1f - _missions.PlanetLay) * PlanetLaySeconds : Time.unscaledTime;

    private void LayPlanetForPrep(PlanetShape shape)
    {
        if (_missions.PlanetLay <= 0f)
        {
            ShowPlanetLines(false, false);
            _missions.PlanetLaidAt = float.MinValue;
        }
        // The sheet lies where preparation's map will: the picture's width along the table,
        // as the map's length is, covering the map's own ground once that is known.
        var (along, across) = _prep.Map != null && _prep.MapTile > 0f ? (_prep.MapSizeZ * _prep.MapTile, _prep.MapSizeX * _prep.MapTile) : (PrepMapSide, PrepMapSide);
        if (_missions.PlanetLay < 1f || _missions.PlanetLaidFor != (along, across))
        {
            if (_missions.PlanetUpright == null)
            {
                var upright = N(Quaternion.Inverse(PlanetLeaning));
                _missions.PlanetUpright = new NVector3[_missions.PlanetRest.Length];
                for (var i = 0; i < _missions.PlanetRest.Length; i++)
                    _missions.PlanetUpright[i] = NVector3.Transform(N(_missions.PlanetRest[i]), upright);
            }
            _missions.PlanetLay = Mathf.Min(1f, _missions.PlanetLay + PlanetStep / PlanetLaySeconds);
            _missions.PlanetLaidFor = (along, across);
            var flat = Ease.InOutCubic(_missions.PlanetLay);
            for (var j = 0; j <= PlanetDown; j++)
                for (var i = 0; i <= PlanetAcross; i++)
                {
                    var at = j * (PlanetAcross + 1) + i;
                    var from = _missions.PlanetUpright[at];
                    var (x, y) = (((float)i / PlanetAcross - 0.5f) * along, ((float)j / PlanetDown - 0.5f) * across);
                    _missions.PlanetLaid[at] = new Vector3(from.X + (x - from.X) * flat, from.Y + (y - from.Y) * flat, from.Z * (1f - flat));
                }
            _missions.PlanetMesh.vertices = _missions.PlanetLaid;
            _missions.PlanetMesh.RecalculateBounds();
            _missions.Planet.transform.localScale = Vector3.one;
            _missions.Planet.transform.localRotation = Quaternion.Slerp(PlanetLeaning, PlanetLying, flat);
            _missions.Planet.transform.localPosition = Vector3.Lerp(PlanetPlace, PrepMapCentre, flat);
            if (_missions.PlanetLay >= 1f && _missions.PlanetLaidAt == float.MinValue)
                _missions.PlanetLaidAt = Time.unscaledTime;
            if (_missions.PlanetLay < 1f)
                return;
        }
        // The sheet lies there until preparation's map is made (vanilla generates it first).
        // It does not wait for ever.
        if (_prep.Map == null)
        {
            if (Time.unscaledTime - _missions.PlanetLaidAt > PlanetWaitsForMap)
                EndPlanetLay();
            return;
        }
        // The map builds from the table's foot to its head, and takes the sheet with it: the
        // sheet's edge is the line the map's tiles rise along.
        var taken = (Time.unscaledTime - Mathf.Max(_missions.PlanetLaidAt, _prep.MapBornAt)) / PrepBuildSweep;
        _missions.PlanetMaterial.SetFloat(RevealId, 1f - Ease.InOutCubic(taken));
        if (taken >= 1f)
            EndPlanetLay();
    }

    // The sheet is put away and the section is the globe's again, for the next visit.
    private void EndPlanetLay()
    {
        (_missions.PlanetLay, _missions.PlanetMoment, _missions.PlanetPlacedAt) = (0f, 0f, -1f);
        if (_missions.Planet == null)
            return;
        _missions.PlanetMesh.vertices = _missions.PlanetRest;
        _missions.PlanetMesh.RecalculateBounds();
        _missions.Planet.SetActive(_missions.PlanetShown = false);
        _missions.PlanetPlayer.Pause();
    }

    private void ApplyPlanetArrival(PlanetShape shape, float moment)
    {
        if (moment == _missions.PlanetPlacedAt)
            return;
        _missions.PlanetPlacedAt = moment;
        var grown = Ease.SmoothStep(GlobeGrowsFrom, GlobeGrownBy, moment);
        var size = GlobeSmall * MathF.Pow(PlanetRadius / GlobeSmall, grown);
        var centre = Vector3.Lerp(GlobeStart, GlobeRest, (size - GlobeSmall) / (PlanetRadius - GlobeSmall));
        var facing = Quaternion.AngleAxis(GlobeTurn * (1f - Ease.SmoothStep(0f, GlobeTurnedBy, moment)), Vector3.up) * shape.Turn;
        var scan = Math.Clamp((moment - PlanetScanFrom) / (PlanetScanTo - PlanetScanFrom), 0f, 1f);

        // The section rides the globe: its mesh is built about its own middle at its full size.
        var (turned, scaled, placed) = (facing * Quaternion.Inverse(shape.Turn), Vector3.one * (size / PlanetRadius), centre + facing * shape.Middle * size);
        Place(_missions.Planet.transform);
        Place(_missions.PlanetEdge.transform);
        void Place(Transform part)
        {
            part.localRotation = turned;
            part.localScale = scaled;
            part.localPosition = placed;
        }
        _missions.PlanetMaterial.SetFloat(RevealId, scan);
        ShowPlanetLines(scan >= 1f, scan < 1f);
        if (scan >= 1f)
            return;
        _missions.PlanetGlobe.transform.localPosition = centre;
        _missions.PlanetGlobe.transform.localRotation = facing;
        _missions.PlanetGlobe.transform.localScale = Vector3.one * size;
        var drawn = Math.Clamp(moment / GlobeDrawnBy, 0f, 1f);
        var solid = GlobeReach - GlobeReachSoften;
        for (var i = 0; i < _missions.GlobeShades.Length; i++)
        {
            var round = _missions.GlobeRound[i] * size;
            var shade = round <= solid ? 1f : round >= GlobeReach ? 0f : 1f - (round - solid) / GlobeReachSoften;
            // Not yet drawn, or already taken by the picture's scan line.
            if (_missions.GlobeUp[i] > drawn || (scan > 0f && _missions.GlobeAcross[i] <= scan))
                shade = 0f;
            _missions.GlobeShades[i] = new Color(shade, shade, shade, 1f);
        }
        _missions.GlobeMesh.colors = _missions.GlobeShades;
    }

    // What each of the wireframe's points needs for its shade on this planet.
    private void MeasureGlobe(PlanetShape shape)
    {
        var (middle, upright) = (N(shape.Middle), N(Quaternion.Inverse(GlobeLean)));
        for (var i = 0; i < _missions.GlobePoints.Length; i++)
        {
            var point = _missions.GlobePoints[i];
            _missions.GlobeRound[i] = MathF.Acos(Math.Clamp(NVector3.Dot(point, middle), -1f, 1f));
            _missions.GlobeUp[i] = (NVector3.Transform(point, upright).Y + 1f) / 2f;
            _missions.GlobeAcross[i] = point.X * shape.Rim + shape.CentreX;
        }
    }

    // Lines of latitude and longitude every 15 degrees on the unit sphere, as line segments so
    // they stay one pixel wide at any size.
    private GameObject BuildGlobe(GameObject template)
    {
        const int rings = 11, meridians = 24, steps = 72;
        var points = new List<Vector3>();
        void Line(Func<float, Vector3> at, int count)
        {
            for (var i = 0; i < count; i++)
            {
                points.Add(GlobeLean * at((float)i / count));
                points.Add(GlobeLean * at((float)(i + 1) / count));
            }
        }
        for (var ring = 1; ring <= rings; ring++)
        {
            var latitude = (ring * 15f - 90f) * Mathf.Deg2Rad;
            Line(f => new Vector3(Mathf.Cos(latitude) * Mathf.Cos(f * 2f * Mathf.PI), Mathf.Sin(latitude), Mathf.Cos(latitude) * Mathf.Sin(f * 2f * Mathf.PI)), steps);
        }
        for (var meridian = 0; meridian < meridians; meridian++)
        {
            var longitude = meridian * 15f * Mathf.Deg2Rad;
            Line(f => new Vector3(Mathf.Sin(f * Mathf.PI) * Mathf.Cos(longitude), -Mathf.Cos(f * Mathf.PI), Mathf.Sin(f * Mathf.PI) * Mathf.Sin(longitude)), steps / 2);
        }
        _missions.GlobePoints = points.Select(N).ToArray();
        (_missions.GlobeRound, _missions.GlobeUp, _missions.GlobeAcross) = (new float[points.Count], new float[points.Count], new float[points.Count]);
        _missions.GlobeShades = new Il2CppStructArray<Color>(points.Count);
        _missions.GlobeMesh = LineMesh("WOMENACE_PlanetGlobe", points);
        var globe = Quad(template, "WOMENACE_PlanetGlobe", Texture2D.whiteTexture);
        globe.GetComponent<MeshFilter>().sharedMesh = _missions.GlobeMesh;
        var renderer = globe.GetComponent<MeshRenderer>();
        renderer.sortingOrder = PlanetOrder + 1;
        PlainHologram(renderer.material, 0f, GlobeIntensity, GlobeTint);
        return globe;
    }

    // A mesh of line segments, each pair of points one, which stay a pixel wide at any size.
    private static Mesh LineMesh(string name, List<Vector3> ends)
    {
        var mesh = new Mesh { name = name, vertices = ends.ToArray(), uv = new Vector2[ends.Count] };
        mesh.SetIndices(Enumerable.Range(0, ends.Count).ToArray(), MeshTopology.Lines, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private bool BuildPlanet(PlanetShape shape)
    {
        var template = MarkerTemplate();
        if (template == null)
            return false;
        if (_missions.PlanetTexture == null)
            _missions.PlanetTexture = new RenderTexture(PlanetVideoWidth, PlanetVideoHeight, 0) { name = "WOMENACE_Planet" };
        if (_missions.PlanetPlayer == null)
        {
            var host = new GameObject("WOMENACE_PlanetVideo");
            host.transform.SetParent(_scene.Room.transform, false);
            _missions.PlanetPlayer = host.AddComponent<VideoPlayer>();
            _missions.PlanetPlayer.playOnAwake = false;
            _missions.PlanetPlayer.isLooping = true;
            _missions.PlanetPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _missions.PlanetPlayer.renderMode = VideoRenderMode.RenderTexture;
            _missions.PlanetPlayer.targetTexture = _missions.PlanetTexture;
        }
        _missions.PlanetPlayer.Stop();
        _missions.PlanetPlayer.source = VideoSource.VideoClip;
        _missions.PlanetPlayer.clip = shape.Clip;
        _missions.PlanetPlayer.Play();

        // The picture as a grid of points on the globe. A point off the planet (the space round
        // it in the picture) is left out: it draws nothing and darkens nothing.
        var vertices = new Vector3[(PlanetAcross + 1) * (PlanetDown + 1)];
        var uvs = new Vector2[vertices.Length];
        var colours = new Color[vertices.Length];
        for (var j = 0; j <= PlanetDown; j++)
            for (var i = 0; i <= PlanetAcross; i++)
            {
                var (u, v) = ((float)i / PlanetAcross, (float)j / PlanetDown);
                var at = j * (PlanetAcross + 1) + i;
                vertices[at] = shape.InSection(shape.OnSphere(u, v, out var onPlanet));
                uvs[at] = new Vector2(u, v);
                colours[at] = onPlanet ? Color.white : Color.clear;
            }
        var triangles = new int[PlanetAcross * PlanetDown * 6];
        var t = 0;
        for (var j = 0; j < PlanetDown; j++)
            for (var i = 0; i < PlanetAcross; i++)
            {
                var o = j * (PlanetAcross + 1) + i;
                foreach (var corner in new[] { o, o + PlanetAcross + 1, o + 1, o + 1, o + PlanetAcross + 1, o + PlanetAcross + 2 })
                    triangles[t++] = corner;
            }
        var mesh = new Mesh { name = "WOMENACE_Planet", vertices = vertices, uv = uvs, colors = colours, triangles = triangles };
        (_missions.PlanetRest, _missions.PlanetUpright, _missions.PlanetLaid, _missions.PlanetLay, _missions.PlanetPlacedAt, _missions.PlanetMesh) = (vertices, null, new Il2CppStructArray<Vector3>(vertices.Length), 0f, -1f, mesh);
        mesh.RecalculateBounds();

        // A bright line round the picture's edge, where the edge is on the planet.
        var ends = new List<Vector3>();
        void Edge(Func<float, (float U, float V)> along, int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                var (a, b) = (along((float)i / steps), along((float)(i + 1) / steps));
                var from = shape.OnSphere(a.U, a.V, out var fromOn);
                var to = shape.OnSphere(b.U, b.V, out var toOn);
                if (!fromOn || !toOn)
                    continue;
                ends.Add(shape.InSection(from));
                ends.Add(shape.InSection(to));
            }
        }
        Edge(f => (f, 0f), PlanetAcross);
        Edge(f => (f, 1f), PlanetAcross);
        Edge(f => (0f, f), PlanetDown);
        Edge(f => (1f, f), PlanetDown);
        var line = LineMesh("WOMENACE_PlanetEdge", ends);

        if (_missions.Planet == null)
        {
            _missions.Planet = Quad(template, "WOMENACE_Planet", _missions.PlanetTexture);
            _missions.PlanetEdge = Quad(template, "WOMENACE_PlanetEdge", Texture2D.whiteTexture);
            _missions.PlanetGlobe = BuildGlobe(template);
            (_missions.PlanetShown, _missions.PlanetEdgeShown, _missions.PlanetGlobeShown) = (true, true, true);
        }
        MeasureGlobe(shape);
        foreach (var (part, partMesh, order) in new[] { (_missions.Planet, mesh, PlanetOrder), (_missions.PlanetEdge, line, PlanetOrder + 1) })
        {
            part.transform.localPosition = PlanetPlace;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = Vector3.one;
            var filter = part.GetComponent<MeshFilter>();
            // A rebuild for another planet or clip replaces the meshes the last build made.
            DestroyBuiltMesh(filter.sharedMesh, partMesh);
            filter.sharedMesh = partMesh;
            part.GetComponent<MeshRenderer>().sortingOrder = order;
        }
        var material = _missions.PlanetMaterial = _missions.Planet.GetComponent<MeshRenderer>().material;
        material.SetFloat("_ScanDepth", 0.22f);
        material.SetFloat("_ScanLines", 420f);
        material.SetFloat("_EdgeFade", 0f);
        material.SetFloat("_CutByAlpha", 0f);
        material.SetFloat("_Intensity", PlanetIntensity);
        material.SetFloat("_Plate", PlanetOpacity);
        // The scan that lays the picture is ragged, a band to each row of the section's mesh,
        // and trails the wireframe's colour.
        material.SetFloat("_RevealRagged", 1f);
        material.SetFloat("_RevealBands", PlanetDown);
        material.SetColor("_RevealGlow", GlobeTint);
        material.SetColor("_Tint", Color.white);
        material.SetVector("_UvRect", new Vector4(1f, 1f, 0f, 0f));
        PlainHologram(_missions.PlanetEdge.GetComponent<MeshRenderer>().material, 0f, 1f, PlanetEdgeTint);
        return true;
    }

    // GFL2's land and its curtain of dots give way to the planet while it shows.
    private void HideLandForPlanet(bool hide)
    {
        if (hide == _missions.PlanetHidLand)
            return;
        _missions.PlanetHidLand = hide;
        foreach (var (layer, _, _) in _missions.LandLayers)
            if (layer != null)
                foreach (var renderer in layer.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = !hide;
        if (_missions.Curtain.Node != null)
            foreach (var renderer in _missions.Curtain.Node.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = !hide;
    }

    // A mesh the planet built, not the holotable quad's own, which the parts start with.
    private static void DestroyBuiltMesh(Mesh mesh, Mesh keep)
    {
        if (mesh != null && mesh != keep && mesh.name.StartsWith("WOMENACE_", StringComparison.Ordinal))
            UnityEngine.Object.Destroy(mesh);
    }

    private void ClearPlanet()
    {
        HideLandForPlanet(false);
        // The parts' meshes and their materials' copies outlive the parts.
        foreach (var part in new[] { _missions.Planet, _missions.PlanetEdge, _missions.PlanetGlobe })
            if (part != null)
            {
                DestroyBuiltMesh(part.GetComponent<MeshFilter>()?.sharedMesh, null);
                var material = part.GetComponent<MeshRenderer>()?.sharedMaterial;
                if (material != null)
                    UnityEngine.Object.Destroy(material);
            }
        foreach (var part in new[] { _missions.Planet, _missions.PlanetEdge, _missions.PlanetGlobe, _missions.PlanetPlayer != null ? _missions.PlanetPlayer.gameObject : null })
            if (part != null)
                UnityEngine.Object.Destroy(part);
        if (_missions.PlanetTexture != null)
        {
            _missions.PlanetTexture.Release();
            UnityEngine.Object.Destroy(_missions.PlanetTexture);
        }
        (_missions.Planet, _missions.PlanetEdge, _missions.PlanetGlobe, _missions.PlanetPlayer, _missions.PlanetTexture, _missions.PlanetShape) = (null, null, null, null, null, null);
        (_missions.PlanetOperation, _missions.PlanetBuilt) = (IntPtr.Zero, default);
        (_missions.PlanetMoment, _missions.PlanetLay, _missions.PlanetPlacedAt) = (0f, 0f, -1f);
        (_missions.PlanetMesh, _missions.GlobeMesh, _missions.PlanetMaterial) = (null, null, null);
    }
}
