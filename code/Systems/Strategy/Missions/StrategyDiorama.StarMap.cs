using Il2CppMenace.UI;
using UnityEngine;
using UnityEngine.UIElements;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

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


    // Shows the copy of a vanilla system map scene over the table, or puts it away.
    private void SetStarMap(GameObject vanilla)
    {
        if (vanilla == null || _scene.Room == null)
        {
            if (_missions.StarMap != null && _missions.StarMap.activeSelf)
            {
                _missions.StarMap.SetActive(false);
                Cue(TableCue.Vanish);
                _missions.StarHiddenAt = Time.unscaledTime;
            }
            ShowTableSparks(true);
            RestoreStarMapCamera();
            return;
        }
        AimStarMap(vanilla);
        if (_missions.StarMap == null || _missions.StarMapSource != vanilla)
            BuildStarMap(vanilla);
        if (_missions.StarMap != null)
        {
            if (!_missions.StarMap.activeSelf)
                Cue(TableCue.Appear);
            _missions.StarMap.SetActive(true);
            DriveStarMap();
        }
    }

    private void BuildStarMap(GameObject vanilla)
    {
        ClearStarMap();
        _missions.StarMapSource = vanilla;
        _missions.StarMap = new GameObject("WOMENACE_StarMap");
        _missions.StarMap.transform.SetParent(_scene.Room.transform, false);
        _missions.StarMap.transform.localPosition = _missions.StarPosition;
        _missions.StarMap.transform.localScale = Vector3.one * _missions.StarScale;
        var source = vanilla.transform;
        foreach (var renderer in vanilla.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter = renderer.GetComponent<MeshFilter>();
            // Vanilla's rays from under each planet are left out: they are its own picture of a
            // projection, and here the planets stand over a table that projects them.
            if (filter == null || filter.sharedMesh == null || renderer.name.StartsWith(StarMapRays, StringComparison.Ordinal))
                continue;
            var copy = new GameObject(renderer.name);
            copy.layer = _scene.Room.layer;
            var t = copy.transform;
            t.SetParent(_missions.StarMap.transform, false);
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
            _missions.StarMapParts.Add((renderer, copy, IsOperationMarker(renderer.transform, source)));
        }
        LiftStarMapQueues();
        SyncStarMap();
        Context.Log.Info($"strategy diorama: system map over the table, {_missions.StarMapParts.Count} of vanilla's meshes");
    }

    // The map's see-through parts (planets 2986, markers 3000, lines 3010) are drawn after the
    // dolls' see-through ones (3000), keeping their own order. Drawn before, a doll's veil or cape
    // standing behind the map, which writes no depth, is painted over the planets in front of it.
    // The materials are vanilla's own, whose map is not drawn while this one is, and are given back
    // their queues with the copy.
    private const int StarMapAfterDolls = 3001;

    private void LiftStarMapQueues()
    {
        var lowest = int.MaxValue;
        var seen = new HashSet<IntPtr>();
        foreach (var (_, copy, _) in _missions.StarMapParts)
            foreach (var material in copy.GetComponent<MeshRenderer>().sharedMaterials)
                if (material != null && material.renderQueue >= TransparentQueueStart && seen.Add(material.Pointer))
                {
                    _missions.StarMapQueues.Add((material, material.renderQueue));
                    lowest = Math.Min(lowest, material.renderQueue);
                }
        if (lowest >= StarMapAfterDolls)
            return;
        foreach (var (material, queue) in _missions.StarMapQueues)
            material.renderQueue = queue + StarMapAfterDolls - lowest;
    }

    private void RestoreStarMapQueues()
    {
        foreach (var (material, queue) in _missions.StarMapQueues)
            if (material != null)
                material.renderQueue = queue;
        _missions.StarMapQueues.Clear();
    }

    // Where Unity's see-through queues begin (AlphaTest ends at 2450).
    private const int TransparentQueueStart = 2451;

    // Whether a part belongs to an operation's marker, which vanilla names by this prefix.
    private static bool IsOperationMarker(Transform part, Transform root)
    {
        for (var t = part; t != null && t != root; t = t.parent)
            if (t.name.StartsWith(VanillaUi.Elements.OperationMarkerPrefix, StringComparison.Ordinal))
                return true;
        return false;
    }

    private const string StarMapRays = "ray_";

    // A copy shows while vanilla's own shows, at vanilla's size: vanilla switches parts on and off
    // (the ship's icon at the place it is) and swells an operation's marker under the pointer.
    private void SyncStarMap()
    {
        // A briefing shows the planet alone, as vanilla's does, without the operations' markers.
        var briefing = _policyScreen == VanillaUi.Screens.OperationSelect;
        ShowTableSparks(!briefing);
        if (_missions.StarOffTable.Count != _missions.StarMapParts.Count)
            FindOffTable();
        var written = _missions.StarPartsWritten;
        if (written.Count != _missions.StarMapParts.Count)
        {
            written.Clear();
            for (var i = 0; i < _missions.StarMapParts.Count; i++)
                written.Add((null, new NVector3(float.NaN)));
        }
        for (var i = 0; i < _missions.StarMapParts.Count; i++)
        {
            var (source, copy, marker) = _missions.StarMapParts[i];
            if (source == null || copy == null)
                continue;
            var shown = source.enabled && source.gameObject.activeInHierarchy && !(briefing && (marker || _missions.StarOffTable[i]));
            var scale = source.transform.lossyScale;
            var (wasShown, wasScale) = written[i];
            if (shown != wasShown)
                copy.SetActive(shown);
            if (scale.x != wasScale.X || scale.y != wasScale.Y || scale.z != wasScale.Z)
                copy.transform.localScale = scale;
            written[i] = (shown, new NVector3(scale.x, scale.y, scale.z));
        }
    }

    // The table's ends in room space, with a little grace: a briefing shows nothing of the grown
    // star system that would hang in the air past them.
    private const float TableHead = -17.45f;
    private const float TableFoot = -11.3f;

    // Which parts reach past the table's ends where the star system is going to stand. Judged
    // at that place, not where it is on the way, so nothing drops out part way through the zoom.
    // The briefing's own planet always shows.
    private void FindOffTable()
    {
        _missions.StarOffTable.Clear();
        foreach (var (source, copy, _) in _missions.StarMapParts)
        {
            var off = false;
            if (source != null && copy != null && source != _missions.StarBriefingPlanet)
            {
                var filter = copy.GetComponent<MeshFilter>();
                var reach = filter != null && filter.sharedMesh != null
                    ? filter.sharedMesh.bounds.extents.x * Mathf.Abs(source.transform.lossyScale.x) * _missions.StarToScale
                    : 0f;
                var centre = _missions.StarToPosition.x + copy.transform.localPosition.x * _missions.StarToScale;
                off = centre - reach < TableHead || centre + reach > TableFoot;
            }
            _missions.StarOffTable.Add(off);
        }
    }


    // Each frame the map shows: vanilla's map camera is stood where the room camera stands
    // relative to the copy. Vanilla finds the operation under the pointer and places its planet
    // names through that camera, so both follow the room's view of the copy.
    private void DriveStarMap()
    {
        if (_missions.StarMap == null || !_missions.StarMap.activeSelf || _missions.StarMapSource == null || _scene.Camera == null)
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
        // The map camera is kept once found: it stays under its scene for as long as the copy is of it.
        var mapCamera = _missions.StarMapCamera != null ? _missions.StarMapCamera : _missions.StarMapSource.GetComponentInChildren<Camera>(true);
        var roomCamera = _life.RoomCamera != null ? _life.RoomCamera : _scene.Camera.GetComponent<Camera>();
        if (mapCamera == null || roomCamera == null)
            return;
        if (_missions.StarMapCamera != mapCamera)
        {
            RestoreStarMapCamera();
            _missions.StarMapCamera = mapCamera;
            _missions.StarMapCameraPosition = mapCamera.transform.localPosition;
            _missions.StarMapCameraRotation = mapCamera.transform.localRotation;
            _missions.StarMapCameraFov = mapCamera.fieldOfView;
        }
        var copy = _missions.StarMap.transform;
        var source = _missions.StarMapSource.transform;
        if (_missions.StarMapNames.Count == 0)
            ReadStarMapNames(mapCamera);
        FitPlanetPlates();
        var (eye, facing) = (roomCamera.transform.position, roomCamera.transform.rotation);
        mapCamera.transform.SetPositionAndRotation(
            source.TransformPoint(copy.InverseTransformPoint(eye)),
            source.rotation * (Quaternion.Inverse(copy.rotation) * facing));
        var fov = roomCamera.fieldOfView;
        mapCamera.fieldOfView = fov;
        // Vanilla places a planet's name once, when the map opens. Each is kept on its place in
        // the map as the room camera sees it, which moves only with the view or the map.
        var view = (new NVector3(eye.x, eye.y, eye.z), new NQuaternion(facing.x, facing.y, facing.z, facing.w), fov, _lensShift, _lensLift,
            new NVector3(_missions.StarPosition.x, _missions.StarPosition.y, _missions.StarPosition.z), _missions.StarScale, Screen.width, Screen.height);
        if (_missions.StarMapNames.Count == 0 || _missions.StarNamesPlacedFor == view)
            return;
        _missions.StarNamesPlacedFor = view;
        foreach (var (hud, _, _, point, scale) in _missions.StarMapNames)
        {
            var seen = roomCamera.WorldToScreenPoint(copy.TransformPoint(point));
            hud.style.left = seen.x / scale;
            hud.style.top = (Screen.height - seen.y) / scale;
        }
    }


    // Where each planet's name stands in the map: the place vanilla gave it on screen, cast from
    // vanilla's own camera view onto the map's plane. Read once the names are laid out.
    private void ReadStarMapNames(Camera mapCamera)
    {
        var hud = UIManager.Get()?.GetActiveScreen()?.TryCast<Il2CppMenace.UI.Strategy.SystemMapUIScreen>()?.m_HUDElement;
        if (hud == null || float.IsNaN(hud.resolvedStyle.height) || hud.resolvedStyle.height <= 0f)
            return;
        var scale = Screen.height / hud.resolvedStyle.height;
        var source = _missions.StarMapSource.transform;
        var plane = new Plane(source.forward, source.position);
        mapCamera.transform.localPosition = _missions.StarMapCameraPosition;
        mapCamera.transform.localRotation = _missions.StarMapCameraRotation;
        mapCamera.fieldOfView = _missions.StarMapCameraFov;
        // Every name has its reputation laid under it, but only once the names have read: until
        // then this runs every frame, and the parts would be built again each time.
        var names = new List<VisualElement>();
        for (var i = 0; i < hud.childCount; i++)
        {
            var name = hud.ElementAt(i);
            if (name.GetIl2CppType().Name != VanillaUi.Elements.PlanetHud)
                continue;
            names.Add(name);
            var (left, top) = (name.resolvedStyle.left, name.resolvedStyle.top);
            if (float.IsNaN(left) || float.IsNaN(top))
                continue;
            var ray = mapCamera.ScreenPointToRay(new Vector3(left * scale, Screen.height - top * scale, 0f));
            if (plane.Raycast(ray, out var distance))
                _missions.StarMapNames.Add((name, left, top, source.InverseTransformPoint(ray.GetPoint(distance)), scale));
        }
        if (_missions.StarMapNames.Count == 0)
            return;
        _missions.StarNamesPlacedFor = null;
        foreach (var name in names)
            ShowPlanetRep(name);
    }

    // Vanilla's whole-map camera stands this far from the map's plane, in the map's units. A
    // briefing's camera stands closer, off to one side, and that is the zoom.
    private const float StarMapCameraDistance = 30f;
    private const float StarZoomSeconds = 0.9f;
    // Hidden for longer than this, the map comes back at its place with no travel.
    private const float StarZoomForgetSeconds = 0.5f;


    // Where the star system stands for the screen: whole over the table, or, for a briefing,
    // grown in its own plane so the room camera sees what vanilla's briefing camera sees, the
    // operation's planet large and to one side.
    private void AimStarMap(GameObject vanilla)
    {
        var (position, scale) = (StarMapOrigin, StarMapScale);
        _missions.StarBriefingPlanet = null;
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
                _missions.StarBriefingPlanet = planet;
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
        if (Time.unscaledTime - _missions.StarHiddenAt > StarZoomForgetSeconds && (_missions.StarMap == null || !_missions.StarMap.activeSelf))
        {
            (_missions.StarPosition, _missions.StarScale) = (position, scale);
            _missions.StarZoomTime = StarZoomSeconds;
        }
        if (position != _missions.StarToPosition || scale != _missions.StarToScale)
        {
            (_missions.StarFromPosition, _missions.StarFromScale) = (_missions.StarPosition, _missions.StarScale);
            (_missions.StarToPosition, _missions.StarToScale) = (position, scale);
            _missions.StarOffTable.Clear();
            _missions.StarZoomTime = (_missions.StarPosition == position && _missions.StarScale == scale) ? StarZoomSeconds : 0f;
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
        if (_missions.StarZoomTime < StarZoomSeconds)
        {
            _missions.StarZoomTime += Time.unscaledDeltaTime;
            var k = Ease.InOutCubic(Mathf.Clamp01(_missions.StarZoomTime / StarZoomSeconds));
            _missions.StarPosition = Vector3.Lerp(_missions.StarFromPosition, _missions.StarToPosition, k);
            _missions.StarScale = Mathf.Lerp(_missions.StarFromScale, _missions.StarToScale, k);
        }
        _missions.StarMap.transform.localPosition = _missions.StarPosition;
        _missions.StarMap.transform.localScale = Vector3.one * _missions.StarScale;
    }

    private void RestoreStarMapCamera()
    {
        foreach (var (hud, left, top, _, _) in _missions.StarMapNames)
        {
            hud.style.left = left;
            hud.style.top = top;
        }
        _missions.StarMapNames.Clear();
        _missions.StarNamesPlacedFor = null;
        if (_missions.StarMapCamera != null)
        {
            _missions.StarMapCamera.transform.localPosition = _missions.StarMapCameraPosition;
            _missions.StarMapCamera.transform.localRotation = _missions.StarMapCameraRotation;
            _missions.StarMapCamera.fieldOfView = _missions.StarMapCameraFov;
        }
        _missions.StarMapCamera = null;
    }

    private void ClearStarMap()
    {
        RestoreStarMapCamera();
        RestoreStarMapQueues();
        if (_missions.StarMap != null)
            UnityEngine.Object.Destroy(_missions.StarMap);
        (_missions.StarMap, _missions.StarMapSource) = (null, null);
        _missions.StarMapParts.Clear();
        _missions.StarOffTable.Clear();
        _missions.StarPartsWritten.Clear();
    }
}
