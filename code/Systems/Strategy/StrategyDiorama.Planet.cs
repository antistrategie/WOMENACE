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
    // 1600 by 900 frame. Fitted by eye to a frame of each. A planet's day and night share one.
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
        public Vector3 InRoom(Vector3 spot) => PlanetPlace + Turn * (spot - Middle) * PlanetRadius;

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

    // The planet of the operation last asked about, and the planet the parts were built for.
    private IntPtr _planetOperation;
    private PlanetShape _planetShape;
    private (string Id, IntPtr Clip) _planetBuilt;
    private GameObject _planet;
    private GameObject _planetEdge;
    private VideoPlayer _planetPlayer;
    private RenderTexture _planetTexture;
    private bool _planetHidLand;

    // The running operation's planet as a shape, or null where it has no edge on record or no
    // video for the operation's time of day.
    private PlanetShape CurrentPlanet() => PlanetOf(CurrentOperation());

    // An operation's planet is worked out once and kept while mission select stays open.
    private PlanetShape PlanetOf(Operation operation)
    {
        if (operation == null)
            return null;
        if (operation.Pointer == _planetOperation)
            return _planetShape;
        _planetOperation = operation.Pointer;
        return _planetShape = MakePlanetShape(operation);
    }

    private static PlanetShape MakePlanetShape(Operation operation)
    {
        try
        {
            var template = operation.GetPlanetTemplate();
            var id = template?.GetID();
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
    private (float Along, float Across) _planetLaidFor;
    private float _planetLay;
    private float _planetLaidAt = float.MinValue;
    // The section's points as the globe has them, the same stood upright (the lean taken out)
    // for unrolling, and the points being written while it unrolls.
    private Vector3[] _planetRest;
    private NVector3[] _planetUpright;
    private Il2CppStructArray<Vector3> _planetLaid;

    private float _planetMoment;
    // The moment the parts were last placed for. They are placed again only when it changes.
    private float _planetPlacedAt = -1f;
    private GameObject _planetGlobe;
    private Mesh _planetMesh, _globeMesh;
    private Material _planetMaterial;
    // Each of the wireframe's points: how far round the globe it is from the section's middle
    // (an angle), how far up the globe from its lower pole (0 to 1), and how far across the
    // planet's picture (0 to 1). Its shade is worked out from these every frame it arrives.
    private NVector3[] _globePoints;
    private float[] _globeRound, _globeUp, _globeAcross;
    private Il2CppStructArray<Color> _globeShades;

    // A dev build may hold the arrival at a moment, for looking at it.
    partial void HoldPlanetMoment(ref float moment);

    // A frame's share of time for the planet's own motion, which a stalled frame does not skip.
    private const float PlanetLongestStep = 1f / 20f;
    private static float PlanetStep => HitchStep(PlanetLongestStep);

    // Whether the planet stands arrived, or is already on its way down, as mission preparation
    // opens: it is then laid on the table for the map.
    private bool PlanetLays => _planet != null && (_planetMoment >= 1f || _planetLay > 0f);

    // The planet shows in mission select's Operations view: it arrives when the view is entered
    // and leaves when it is left.
    private void DrivePlanet()
    {
        if (_policyScreen == VanillaUi.Screens.MissionPrep && _planetShape != null && PlanetLays)
        {
            LayPlanetForPrep(_planetShape);
            return;
        }
        if (_planetLay > 0f)
            EndPlanetLay();
        var onScreen = _room != null && _policyScreen == VanillaUi.Screens.MissionSelect;
        // A new visit asks again: the operation's time of day, and so its video, may have moved on.
        if (!onScreen)
            _planetOperation = IntPtr.Zero;
        var shape = onScreen ? CurrentPlanet() : null;
        var inView = shape != null && _homeView == HomeView.Operations;
        if (inView && (_planet == null || _planetBuilt != (shape.Id, shape.Clip.Pointer)))
            _planetBuilt = BuildPlanet(shape) ? (shape.Id, shape.Clip.Pointer) : default;
        var wanted = inView && _planetBuilt.Id == shape.Id;
        HideLandForPlanet(wanted);
        // The globe draws and grows while the camera is still on its way. The picture and the
        // cards wait for it to arrive, so they are seen from the view they are laid out for.
        var upTo = wanted ? (_shotTravel < 1f ? PlanetScanFrom : 1f) : 0f;
        if (shape == null)
            _planetMoment = 0f;
        else if (_planetMoment < upTo || !wanted)
            _planetMoment = Mathf.MoveTowards(_planetMoment, upTo, PlanetStep / (wanted ? PlanetArriveSeconds : PlanetLeaveSeconds));
        if (wanted)
            HoldPlanetMoment(ref _planetMoment);
        if (_planet == null)
            return;
        var shown = _planetMoment > 0f;
        if (shown != _planetShown)
        {
            _planetShown = shown;
            _planet.SetActive(shown);
            Cue(shown ? TableCue.Appear : TableCue.Vanish);
            if (shown)
            {
                _planetPlayer.Play();
            }
            else
            {
                _planetPlayer.Pause();
                ShowPlanetLines(false, false);
            }
        }
        if (shown)
            ApplyPlanetArrival(shape, _planetMoment);
    }

    // The section and the lines that go with it: the bright line round its edge, shown once
    // the picture is laid, and the wireframe globe until then.
    private bool _planetShown, _planetEdgeShown, _planetGlobeShown;

    private void ShowPlanetLines(bool edge, bool globe)
    {
        if (edge != _planetEdgeShown)
            _planetEdge.SetActive(_planetEdgeShown = edge);
        if (globe != _planetGlobeShown)
            _planetGlobe.SetActive(_planetGlobeShown = globe);
    }

    // When the planet's sheet will lie flat on the table, for preparation's map to build after.
    private float PlanetLaidBy() => PlanetLays ? Time.unscaledTime + (1f - _planetLay) * PlanetLaySeconds : Time.unscaledTime;

    private void LayPlanetForPrep(PlanetShape shape)
    {
        if (_planetLay <= 0f)
        {
            ShowPlanetLines(false, false);
            _planetLaidAt = float.MinValue;
        }
        // The sheet lies where preparation's map will: the picture's width along the table,
        // as the map's length is, covering the map's own ground once that is known.
        var (along, across) = _prepMap != null && _prepMapTile > 0f ? (_prepMapSizeZ * _prepMapTile, _prepMapSizeX * _prepMapTile) : (PrepMapSide, PrepMapSide);
        if (_planetLay < 1f || _planetLaidFor != (along, across))
        {
            if (_planetUpright == null)
            {
                var upright = N(Quaternion.Inverse(PlanetLeaning));
                _planetUpright = new NVector3[_planetRest.Length];
                for (var i = 0; i < _planetRest.Length; i++)
                    _planetUpright[i] = NVector3.Transform(N(_planetRest[i]), upright);
            }
            _planetLay = Mathf.Min(1f, _planetLay + PlanetStep / PlanetLaySeconds);
            _planetLaidFor = (along, across);
            var flat = Ease.InOutCubic(_planetLay);
            for (var j = 0; j <= PlanetDown; j++)
                for (var i = 0; i <= PlanetAcross; i++)
                {
                    var at = j * (PlanetAcross + 1) + i;
                    var from = _planetUpright[at];
                    var (x, y) = (((float)i / PlanetAcross - 0.5f) * along, ((float)j / PlanetDown - 0.5f) * across);
                    _planetLaid[at] = new Vector3(from.X + (x - from.X) * flat, from.Y + (y - from.Y) * flat, from.Z * (1f - flat));
                }
            _planetMesh.vertices = _planetLaid;
            _planetMesh.RecalculateBounds();
            _planet.transform.localScale = Vector3.one;
            _planet.transform.localRotation = Quaternion.Slerp(PlanetLeaning, PlanetLying, flat);
            _planet.transform.localPosition = Vector3.Lerp(PlanetPlace, PrepMapCentre, flat);
            if (_planetLay >= 1f && _planetLaidAt == float.MinValue)
                _planetLaidAt = Time.unscaledTime;
            if (_planetLay < 1f)
                return;
        }
        // The sheet lies there until preparation's map is made (vanilla generates it first).
        // It does not wait for ever.
        if (_prepMap == null)
        {
            if (Time.unscaledTime - _planetLaidAt > PlanetWaitsForMap)
                EndPlanetLay();
            return;
        }
        // The map builds from the table's foot to its head, and takes the sheet with it: the
        // sheet's edge is the line the map's tiles rise along.
        var taken = (Time.unscaledTime - Mathf.Max(_planetLaidAt, _prepMapBornAt)) / PrepBuildSweep;
        _planetMaterial.SetFloat(RevealId, 1f - Ease.InOutCubic(taken));
        if (taken >= 1f)
            EndPlanetLay();
    }

    // The sheet is put away and the section is the globe's again, for the next visit.
    private void EndPlanetLay()
    {
        (_planetLay, _planetMoment, _planetPlacedAt) = (0f, 0f, -1f);
        if (_planet == null)
            return;
        _planetMesh.vertices = _planetRest;
        _planetMesh.RecalculateBounds();
        _planet.SetActive(_planetShown = false);
        _planetPlayer.Pause();
    }

    private void ApplyPlanetArrival(PlanetShape shape, float moment)
    {
        if (moment == _planetPlacedAt)
            return;
        _planetPlacedAt = moment;
        var grown = Ease.SmoothStep(GlobeGrowsFrom, GlobeGrownBy, moment);
        var size = GlobeSmall * MathF.Pow(PlanetRadius / GlobeSmall, grown);
        var centre = Vector3.Lerp(GlobeStart, GlobeRest, (size - GlobeSmall) / (PlanetRadius - GlobeSmall));
        var facing = Quaternion.AngleAxis(GlobeTurn * (1f - Ease.SmoothStep(0f, GlobeTurnedBy, moment)), Vector3.up) * shape.Turn;
        var scan = Math.Clamp((moment - PlanetScanFrom) / (PlanetScanTo - PlanetScanFrom), 0f, 1f);

        // The section rides the globe: its mesh is built about its own middle at its full size.
        var (turned, scaled, placed) = (facing * Quaternion.Inverse(shape.Turn), Vector3.one * (size / PlanetRadius), centre + facing * shape.Middle * size);
        Place(_planet.transform);
        Place(_planetEdge.transform);
        void Place(Transform part)
        {
            part.localRotation = turned;
            part.localScale = scaled;
            part.localPosition = placed;
        }
        _planetMaterial.SetFloat(RevealId, scan);
        ShowPlanetLines(scan >= 1f, scan < 1f);
        if (scan >= 1f)
            return;
        _planetGlobe.transform.localPosition = centre;
        _planetGlobe.transform.localRotation = facing;
        _planetGlobe.transform.localScale = Vector3.one * size;
        var drawn = Math.Clamp(moment / GlobeDrawnBy, 0f, 1f);
        var solid = GlobeReach - GlobeReachSoften;
        for (var i = 0; i < _globeShades.Length; i++)
        {
            var round = _globeRound[i] * size;
            var shade = round <= solid ? 1f : round >= GlobeReach ? 0f : 1f - (round - solid) / GlobeReachSoften;
            // Not yet drawn, or already taken by the picture's scan line.
            if (_globeUp[i] > drawn || (scan > 0f && _globeAcross[i] <= scan))
                shade = 0f;
            _globeShades[i] = new Color(shade, shade, shade, 1f);
        }
        _globeMesh.colors = _globeShades;
    }

    // What each of the wireframe's points needs for its shade on this planet.
    private void MeasureGlobe(PlanetShape shape)
    {
        var (middle, upright) = (N(shape.Middle), N(Quaternion.Inverse(GlobeLean)));
        for (var i = 0; i < _globePoints.Length; i++)
        {
            var point = _globePoints[i];
            _globeRound[i] = MathF.Acos(Math.Clamp(NVector3.Dot(point, middle), -1f, 1f));
            _globeUp[i] = (NVector3.Transform(point, upright).Y + 1f) / 2f;
            _globeAcross[i] = point.X * shape.Rim + shape.CentreX;
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
        _globePoints = points.Select(N).ToArray();
        (_globeRound, _globeUp, _globeAcross) = (new float[points.Count], new float[points.Count], new float[points.Count]);
        _globeShades = new Il2CppStructArray<Color>(points.Count);
        _globeMesh = LineMesh("WOMENACE_PlanetGlobe", points);
        var globe = Quad(template, "WOMENACE_PlanetGlobe", Texture2D.whiteTexture);
        globe.GetComponent<MeshFilter>().sharedMesh = _globeMesh;
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
        if (_planetTexture == null)
            _planetTexture = new RenderTexture(PlanetVideoWidth, PlanetVideoHeight, 0) { name = "WOMENACE_Planet" };
        if (_planetPlayer == null)
        {
            var host = new GameObject("WOMENACE_PlanetVideo");
            host.transform.SetParent(_room.transform, false);
            _planetPlayer = host.AddComponent<VideoPlayer>();
            _planetPlayer.playOnAwake = false;
            _planetPlayer.isLooping = true;
            _planetPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _planetPlayer.renderMode = VideoRenderMode.RenderTexture;
            _planetPlayer.targetTexture = _planetTexture;
        }
        _planetPlayer.Stop();
        _planetPlayer.source = VideoSource.VideoClip;
        _planetPlayer.clip = shape.Clip;
        _planetPlayer.Play();

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
                vertices[at] = shape.Turn * (shape.OnSphere(u, v, out var onPlanet) - shape.Middle) * PlanetRadius;
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
        (_planetRest, _planetUpright, _planetLaid, _planetLay, _planetPlacedAt, _planetMesh) = (vertices, null, new Il2CppStructArray<Vector3>(vertices.Length), 0f, -1f, mesh);
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
                ends.Add(shape.Turn * (from - shape.Middle) * PlanetRadius);
                ends.Add(shape.Turn * (to - shape.Middle) * PlanetRadius);
            }
        }
        Edge(f => (f, 0f), PlanetAcross);
        Edge(f => (f, 1f), PlanetAcross);
        Edge(f => (0f, f), PlanetDown);
        Edge(f => (1f, f), PlanetDown);
        var line = LineMesh("WOMENACE_PlanetEdge", ends);

        if (_planet == null)
        {
            _planet = Quad(template, "WOMENACE_Planet", _planetTexture);
            _planetEdge = Quad(template, "WOMENACE_PlanetEdge", Texture2D.whiteTexture);
            _planetGlobe = BuildGlobe(template);
            (_planetShown, _planetEdgeShown, _planetGlobeShown) = (true, true, true);
        }
        MeasureGlobe(shape);
        foreach (var (part, shown, order) in new[] { (_planet, mesh, PlanetOrder), (_planetEdge, line, PlanetOrder + 1) })
        {
            part.transform.localPosition = PlanetPlace;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = Vector3.one;
            var filter = part.GetComponent<MeshFilter>();
            // A rebuild for another planet or clip replaces the meshes the last build made.
            DestroyBuiltMesh(filter.sharedMesh, shown);
            filter.sharedMesh = shown;
            part.GetComponent<MeshRenderer>().sortingOrder = order;
        }
        var material = _planetMaterial = _planet.GetComponent<MeshRenderer>().material;
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
        PlainHologram(_planetEdge.GetComponent<MeshRenderer>().material, 0f, 1f, PlanetEdgeTint);
        return true;
    }

    // GFL2's land and its curtain of dots give way to the planet while it shows.
    private void HideLandForPlanet(bool hide)
    {
        if (hide == _planetHidLand)
            return;
        _planetHidLand = hide;
        foreach (var (layer, _, _) in _landLayers)
            if (layer != null)
                foreach (var renderer in layer.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = !hide;
        if (_curtain.Node != null)
            foreach (var renderer in _curtain.Node.GetComponentsInChildren<Renderer>(true))
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
        foreach (var part in new[] { _planet, _planetEdge, _planetGlobe })
            if (part != null)
            {
                DestroyBuiltMesh(part.GetComponent<MeshFilter>()?.sharedMesh, null);
                var material = part.GetComponent<MeshRenderer>()?.sharedMaterial;
                if (material != null)
                    UnityEngine.Object.Destroy(material);
            }
        foreach (var part in new[] { _planet, _planetEdge, _planetGlobe, _planetPlayer != null ? _planetPlayer.gameObject : null })
            if (part != null)
                UnityEngine.Object.Destroy(part);
        if (_planetTexture != null)
        {
            _planetTexture.Release();
            UnityEngine.Object.Destroy(_planetTexture);
        }
        (_planet, _planetEdge, _planetGlobe, _planetPlayer, _planetTexture, _planetShape) = (null, null, null, null, null, null);
        (_planetOperation, _planetBuilt) = (IntPtr.Zero, default);
        (_planetMoment, _planetLay, _planetPlacedAt) = (0f, 0f, -1f);
        (_planetMesh, _globeMesh, _planetMaterial) = (null, null, null);
    }
}
