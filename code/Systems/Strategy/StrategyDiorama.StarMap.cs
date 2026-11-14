using Il2CppMenace.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Vanilla's system map over the holotable. Vanilla draws the star system as flat pictures in one
// plane before a fixed camera (planets, moons and the sun are squares carrying a picture of a
// globe, only the jump gate is a model). Each of those is copied into the room, mesh and vanilla's
// own hologram material, and stood in the same arrangement along the table as an upright pane
// facing the side the Operations camera looks from. Vanilla's scene stays as it is, since its
// screen keeps working from it.
public sealed partial class StrategyDiorama
{
    // Vanilla's map units to the room's metres: the map's width laid along the table's length.
    private const float StarMapScale = 0.185f;
    // Where the map's own origin hangs in room space: over the middle of the table, high enough
    // for the lowest planet to clear its top.
    private static readonly Vector3 StarMapOrigin = new(-14.4f, 1.45f, 0f);

    private GameObject _starMap;
    private GameObject _starMapSource;

    // Shows the copy of a vanilla system map scene over the table, or puts it away.
    private void SetStarMap(GameObject vanilla)
    {
        if (vanilla == null || _room == null)
        {
            if (_starMap != null && _starMap.activeSelf)
            {
                _starMap.SetActive(false);
                Cue(TableCue.Vanish);
                _starHiddenAt = Time.unscaledTime;
            }
            ShowTableSparks(true);
            RestoreStarMapCamera();
            return;
        }
        AimStarMap(vanilla);
        if (_starMap == null || _starMapSource != vanilla)
            BuildStarMap(vanilla);
        if (_starMap != null)
        {
            if (!_starMap.activeSelf)
                Cue(TableCue.Appear);
            _starMap.SetActive(true);
            DriveStarMap();
        }
    }

    private void BuildStarMap(GameObject vanilla)
    {
        ClearStarMap();
        _starMapSource = vanilla;
        _starMap = new GameObject("WOMENACE_StarMap");
        _starMap.transform.SetParent(_room.transform, false);
        _starMap.transform.localPosition = _starPosition;
        _starMap.transform.localScale = Vector3.one * _starScale;
        var source = vanilla.transform;
        foreach (var renderer in vanilla.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter = renderer.GetComponent<MeshFilter>();
            // Vanilla's rays from under each planet are left out: they are its own picture of a
            // projection, and here the planets stand over a table that projects them.
            if (filter == null || filter.sharedMesh == null || renderer.name.StartsWith(StarMapRays, StringComparison.Ordinal))
                continue;
            var copy = new GameObject(renderer.name);
            copy.layer = _room.layer;
            var t = copy.transform;
            t.SetParent(_starMap.transform, false);
            // Where it stands in vanilla's scene, kept as it is: vanilla's camera looks the way the
            // Operations camera does across the table.
            t.localPosition = source.InverseTransformPoint(renderer.transform.position);
            t.localRotation = Quaternion.Inverse(source.rotation) * renderer.transform.rotation;
            t.localScale = renderer.transform.lossyScale;
            copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var drawn = copy.AddComponent<MeshRenderer>();
            // Vanilla's own materials, shared: what vanilla changes on a planet shows here too.
            drawn.sharedMaterials = renderer.sharedMaterials;
            drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            drawn.receiveShadows = false;
            _starMapParts.Add((renderer, copy, IsOperationMarker(renderer.transform, source)));
        }
        SyncStarMap();
        Context.Log.Info($"strategy diorama: system map over the table, {_starMapParts.Count} of vanilla's meshes");
    }

    // Whether a part belongs to an operation's marker, which vanilla names by this prefix.
    private static bool IsOperationMarker(Transform part, Transform root)
    {
        for (var t = part; t != null && t != root; t = t.parent)
            if (t.name.StartsWith(VanillaUi.Elements.OperationMarkerPrefix, StringComparison.Ordinal))
                return true;
        return false;
    }

    private const string StarMapRays = "ray_";
    private readonly List<(MeshRenderer Source, GameObject Copy, bool Marker)> _starMapParts = new();

    // A copy shows while vanilla's own shows, at vanilla's size: vanilla switches parts on and off
    // (the ship's icon at the place it is) and swells an operation's marker under the pointer.
    private void SyncStarMap()
    {
        // A briefing shows the planet alone, as vanilla's does, without the operations' markers.
        var briefing = _policyScreen == VanillaUi.Screens.OperationSelect;
        ShowTableSparks(!briefing);
        if (_starOffTable.Count != _starMapParts.Count)
            FindOffTable();
        for (var i = 0; i < _starMapParts.Count; i++)
        {
            var (source, copy, marker) = _starMapParts[i];
            if (source == null || copy == null)
                continue;
            copy.SetActive(source.enabled && source.gameObject.activeInHierarchy && !(briefing && (marker || _starOffTable[i])));
            copy.transform.localScale = source.transform.lossyScale;
        }
    }

    // The table's ends in room space, with a little grace: a briefing shows nothing of the grown
    // star system that would hang in the air past them.
    private const float TableHead = -17.45f;
    private const float TableFoot = -11.3f;
    private readonly List<bool> _starOffTable = new();
    private Renderer _starBriefingPlanet;

    // Which parts reach past the table's ends where the star system is going to stand. Judged
    // at that place, not where it is on the way, so nothing drops out part way through the zoom.
    // The briefing's own planet always shows.
    private void FindOffTable()
    {
        _starOffTable.Clear();
        foreach (var (source, copy, _) in _starMapParts)
        {
            var off = false;
            if (source != null && copy != null && source != _starBriefingPlanet)
            {
                var filter = copy.GetComponent<MeshFilter>();
                var reach = filter != null && filter.sharedMesh != null
                    ? filter.sharedMesh.bounds.extents.x * Mathf.Abs(source.transform.lossyScale.x) * _starToScale
                    : 0f;
                var centre = _starToPosition.x + copy.transform.localPosition.x * _starToScale;
                off = centre - reach < TableHead || centre + reach > TableFoot;
            }
            _starOffTable.Add(off);
        }
    }

    // The sparks' objects under the table's effects. Its grid and glow are particle systems too.
    private const string TableSparks = "Particle System";
    private readonly List<Renderer> _tableSparks = new();
    private bool _tableSparksShown = true;

    // The table's rising lines of sparks stand along its far side, and vanilla's planets are not
    // solid, so they would show through a briefing's planet as dotted lines across it.
    private void ShowTableSparks(bool show)
    {
        if (show == _tableSparksShown || _room == null)
            return;
        _tableSparksShown = show;
        _tableSparks.RemoveAll(r => r == null);
        if (_tableSparks.Count == 0)
            foreach (var t in _room.GetComponentsInChildren<Transform>(true))
                if (t.name == TableEffects)
                    foreach (var sparks in t.GetComponentsInChildren<ParticleSystemRenderer>(true))
                        if (sparks.name.StartsWith(TableSparks, StringComparison.Ordinal))
                            _tableSparks.Add(sparks);
        foreach (var sparks in _tableSparks)
            sparks.enabled = show;
    }

    private Camera _starMapCamera;
    private Vector3 _starMapCameraPosition;
    private Quaternion _starMapCameraRotation;
    private float _starMapCameraFov;

    // Each frame the map shows: vanilla's map camera is stood where the room camera stands
    // relative to the copy. Vanilla finds the operation under the pointer and places its planet
    // names through that camera, so both follow the room's view of the copy.
    private void DriveStarMap()
    {
        if (_starMap == null || !_starMap.activeSelf || _starMapSource == null || _camera == null)
        {
            RestoreStarMapCamera();
            return;
        }
        SyncStarMap();
        ZoomStarMap();
        // A briefing's vanilla camera stays where vanilla put it: nothing is picked through it,
        // and the room camera takes its view.
        if (_policyScreen == VanillaUi.Screens.OperationSelect)
        {
            RestoreStarMapCamera();
            return;
        }
        var mapCamera = _starMapSource.GetComponentInChildren<Camera>(true);
        var roomCamera = _camera.GetComponent<Camera>();
        if (mapCamera == null || roomCamera == null)
            return;
        if (_starMapCamera != mapCamera)
        {
            RestoreStarMapCamera();
            _starMapCamera = mapCamera;
            _starMapCameraPosition = mapCamera.transform.localPosition;
            _starMapCameraRotation = mapCamera.transform.localRotation;
            _starMapCameraFov = mapCamera.fieldOfView;
        }
        var copy = _starMap.transform;
        var source = _starMapSource.transform;
        if (_starMapNames.Count == 0)
            ReadStarMapNames(mapCamera);
        mapCamera.transform.SetPositionAndRotation(
            source.TransformPoint(copy.InverseTransformPoint(roomCamera.transform.position)),
            source.rotation * (Quaternion.Inverse(copy.rotation) * roomCamera.transform.rotation));
        mapCamera.fieldOfView = roomCamera.fieldOfView;
        // Vanilla places a planet's name once, when the map opens. Each is kept on its place in
        // the map as the room camera sees it.
        foreach (var (hud, _, _, point, scale) in _starMapNames)
        {
            var seen = roomCamera.WorldToScreenPoint(copy.TransformPoint(point));
            hud.style.left = seen.x / scale;
            hud.style.top = (Screen.height - seen.y) / scale;
        }
    }

    private readonly List<(VisualElement Hud, float Left, float Top, Vector3 Point, float Scale)> _starMapNames = new();

    // Where each planet's name stands in the map: the place vanilla gave it on screen, cast from
    // vanilla's own camera view onto the map's plane. Read once the names are laid out.
    private void ReadStarMapNames(Camera mapCamera)
    {
        var hud = UIManager.Get()?.GetActiveScreen()?.TryCast<Il2CppMenace.UI.Strategy.SystemMapUIScreen>()?.m_HUDElement;
        if (hud == null || float.IsNaN(hud.resolvedStyle.height) || hud.resolvedStyle.height <= 0f)
            return;
        var scale = Screen.height / hud.resolvedStyle.height;
        var source = _starMapSource.transform;
        var plane = new Plane(source.forward, source.position);
        mapCamera.transform.localPosition = _starMapCameraPosition;
        mapCamera.transform.localRotation = _starMapCameraRotation;
        mapCamera.fieldOfView = _starMapCameraFov;
        for (var i = 0; i < hud.childCount; i++)
        {
            var name = hud.ElementAt(i);
            if (name.GetIl2CppType().Name != VanillaUi.Elements.PlanetHud)
                continue;
            var (left, top) = (name.resolvedStyle.left, name.resolvedStyle.top);
            if (float.IsNaN(left) || float.IsNaN(top))
                continue;
            var ray = mapCamera.ScreenPointToRay(new Vector3(left * scale, Screen.height - top * scale, 0f));
            if (plane.Raycast(ray, out var distance))
                _starMapNames.Add((name, left, top, source.InverseTransformPoint(ray.GetPoint(distance)), scale));
        }
    }

    // Vanilla's whole-map camera stands this far from the map's plane, in the map's units. A
    // briefing's camera stands closer, off to one side, and that is the zoom.
    private const float StarMapCameraDistance = 30f;
    private const float StarZoomSeconds = 0.9f;
    // Hidden for longer than this, the map comes back at its place with no travel.
    private const float StarZoomForgetSeconds = 0.5f;

    private Vector3 _starFromPosition = StarMapOrigin, _starToPosition = StarMapOrigin, _starPosition = StarMapOrigin;
    private float _starFromScale = StarMapScale, _starToScale = StarMapScale, _starScale = StarMapScale;
    private float _starZoomTime = StarZoomSeconds;
    private float _starHiddenAt = float.NegativeInfinity;

    // Where the star system stands for the screen: whole over the table, or, for a briefing,
    // grown in its own plane so the room camera sees what vanilla's briefing camera sees, the
    // operation's planet large and to one side.
    private void AimStarMap(GameObject vanilla)
    {
        var (position, scale) = (StarMapOrigin, StarMapScale);
        _starBriefingPlanet = null;
        var mapCamera = _policyScreen == VanillaUi.Screens.OperationSelect ? vanilla.GetComponentInChildren<Camera>(true) : null;
        if (mapCamera != null)
        {
            var stood = vanilla.transform.InverseTransformPoint(mapCamera.transform.position);
            if (Mathf.Abs(stood.z) > 0.01f)
            {
                scale = StarMapScale * StarMapCameraDistance / Mathf.Abs(stood.z);
                // The planet stays whole between the table's top and the top of the view: the map
                // is smaller than vanilla's close view if the planet would not fit, and stands
                // higher if the planet would sink into the table.
                var planet = BriefingPlanet(vanilla, stood);
                _starBriefingPlanet = planet;
                var (low, high) = (0f, 0f);
                if (planet != null)
                {
                    var centre = vanilla.transform.InverseTransformPoint(planet.bounds.center);
                    (low, high) = (centre.y - planet.bounds.extents.y, centre.y + planet.bounds.extents.y);
                    scale = Mathf.Min(scale, (StarZoomCeiling - StarZoomFloor) / Mathf.Max(high - low, 0.01f));
                }
                position = StarMapOrigin - new Vector3(stood.x, stood.y, 0f) * scale;
                if (planet != null)
                {
                    position.y += Mathf.Max(0f, StarZoomFloor - (position.y + low * scale));
                    position.y -= Mathf.Max(0f, position.y + high * scale - StarZoomCeiling);
                }
            }
        }
        if (Time.unscaledTime - _starHiddenAt > StarZoomForgetSeconds && (_starMap == null || !_starMap.activeSelf))
        {
            (_starPosition, _starScale) = (position, scale);
            _starZoomTime = StarZoomSeconds;
        }
        if (position != _starToPosition || scale != _starToScale)
        {
            (_starFromPosition, _starFromScale) = (_starPosition, _starScale);
            (_starToPosition, _starToScale) = (position, scale);
            _starOffTable.Clear();
            _starZoomTime = (_starPosition == position && _starScale == scale) ? StarZoomSeconds : 0f;
        }
    }

    // Room heights the briefing's planet stays between: just over the table's top, and the top of
    // the camera's view under the top bar.
    private const float StarZoomFloor = -0.08f;
    private const float StarZoomCeiling = 3.25f;

    // The planet a briefing is about: the selected operation's, or failing that the one nearest
    // the point vanilla's briefing camera stands over.
    private static Renderer BriefingPlanet(GameObject vanilla, Vector3 stood)
    {
        var selected = Il2CppMenace.UI.Strategy.OperationSelectUIScreen.GetSelectedOperation()?.GetPlanet();
        Renderer nearest = null;
        var nearestDistance = float.MaxValue;
        foreach (var planet in vanilla.GetComponentsInChildren<Il2CppMenace.Strategy.SystemMapPlanet>(true))
        {
            var renderer = planet.m_PlanetRenderer;
            if (renderer == null)
                continue;
            if (selected != null && planet.m_Planet != null && planet.m_Planet.Pointer == selected.Pointer)
                return renderer;
            var at = vanilla.transform.InverseTransformPoint(renderer.bounds.center);
            var distance = (new Vector2(at.x, at.y) - new Vector2(stood.x, stood.y)).sqrMagnitude;
            if (distance < nearestDistance)
                (nearest, nearestDistance) = (renderer, distance);
        }
        return nearest;
    }

    private void ZoomStarMap()
    {
        if (_starZoomTime < StarZoomSeconds)
        {
            _starZoomTime += Time.unscaledDeltaTime;
            var k = Ease.InOutCubic(Mathf.Clamp01(_starZoomTime / StarZoomSeconds));
            _starPosition = Vector3.Lerp(_starFromPosition, _starToPosition, k);
            _starScale = Mathf.Lerp(_starFromScale, _starToScale, k);
        }
        _starMap.transform.localPosition = _starPosition;
        _starMap.transform.localScale = Vector3.one * _starScale;
    }

    private void RestoreStarMapCamera()
    {
        foreach (var (hud, left, top, _, _) in _starMapNames)
        {
            hud.style.left = left;
            hud.style.top = top;
        }
        _starMapNames.Clear();
        if (_starMapCamera != null)
        {
            _starMapCamera.transform.localPosition = _starMapCameraPosition;
            _starMapCamera.transform.localRotation = _starMapCameraRotation;
            _starMapCamera.fieldOfView = _starMapCameraFov;
        }
        _starMapCamera = null;
    }

    private void ClearStarMap()
    {
        RestoreStarMapCamera();
        if (_starMap != null)
            UnityEngine.Object.Destroy(_starMap);
        (_starMap, _starMapSource) = (null, null);
        _starMapParts.Clear();
        _starOffTable.Clear();
    }
}
