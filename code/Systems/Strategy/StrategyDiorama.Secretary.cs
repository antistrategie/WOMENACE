using Il2CppMenace.States;
using UnityEngine;

namespace WOMENACE.Code;

// The room's dolls: the player's hired dolls about the ops room, each in the outfit she currently
// wears, each at a spot she keeps on every screen of the room.
// The model is the doll's own body prefab, driven by her GFL2 lobby loops.
public sealed partial class StrategyDiorama
{
    // Doll bodies are baked at GFL2 canon height times the 1.2 world factor that sizes them
    // against MENACE's soldiers. The rooms are GFL2's own, so a doll stands at canon height in them.
    internal const float RoomDollScale = 1f / 1.2f;
    // The armoury squad's rig. MENACE's character shader takes no ambient, so each staged unit gets
    // a small rig on top of the room's live lights: a warm window key from camera left, a cool fill
    // from the camera and a teal rim from beyond it, as (offset from its feet in the camera's frame
    // with +x camera right and +z away from the camera, colour, lumens). The room's surfaces shade
    // themselves and ignore it. The ops room's dolls take none: GFL2 lights its characters with the
    // room's sun, probes and the lights it keeps on characters, and nothing else.
    private static readonly (Vector3 Offset, Color Colour, float Lumens)[] UnitRig =
    {
        (new Vector3(-1.6f, 2.2f, -0.8f), new Color(1f, 0.9f, 0.78f), 14f),
        (new Vector3(0.6f, 1.4f, -1.8f), new Color(0.75f, 0.88f, 1f), 5f),
        (new Vector3(0.8f, 2.0f, 1.4f), new Color(0.5f, 0.85f, 0.95f), 10f),
    };
    // The rig against the room's exposure.
    internal const float RigLightScale = 1.41f;

    // A place a doll stands in the room: feet position and facing in room space, her pose there,
    // and what the spot asks for. Spots that share a group fill together or not at all (two dolls
    // talking need two dolls). A spot that asks for the doll's own animation takes only a doll
    // with her own loop for the pose, and a seat always takes a doll who has a sitting loop, her
    // own or her stand-in's. The floor's height is the author's, corrected to the room's own
    // floor under the spot where its meshes can be read.
    internal readonly record struct DollSpot(string Name, Vector3 Position, float Yaw, string Pose, int Order, bool Table, string Group = null, bool OwnOnly = false);

    // Placed on a plan of the room (Diorama.TopDown renders it). The pit floor around the table is
    // at y -0.97 and the balcony at 0.66. Table spots fill first, in their order, then the rest.
    internal static readonly DollSpot[] DollSpots =
    {
        new("table-left", new Vector3(-14.2f, -0.97f, -1.7f), 0f, "Idle", 1, true),
        new("table-right", new Vector3(-15.42f, -0.97f, 1.72f), 139f, "Sit", 2, true),
        new("table-chair", new Vector3(-16.3f, -0.97f, -1.72f), 60f, "Sit", 3, true, OwnOnly: true),
        new("table-near", new Vector3(-13.55f, -0.97f, 1.81f), 180f, "Idle", 4, true),
        new("table-back-left", new Vector3(-17.59f, -0.97f, -0.71f), 80f, "LongIdle", 5, true),
        new("walkway-right", new Vector3(-17.31f, 0.66f, 4.21f), 170f, "Idle", 7, false),
        new("walkway-left", new Vector3(-9.18f, 0.66f, -5.39f), 180f, "Idle", 6, false),
        new("table-back-right", new Vector3(-17.62f, -0.97f, 0.26f), 90f, "Idle", 8, false),
        // The talking pair stand on the flat floor at the stairs' foot: the stairs' own steps
        // reach to about x -8.5 here.
        new("stairs-talk-a", new Vector3(-8.85f, -0.97f, -2.99f), 270f, "Idle", 9, false, "stairs-talk"),
        new("stairs-talk-b", new Vector3(-10.05f, -0.97f, -2.98f), 90f, "Idle", 10, false, "stairs-talk"),
    };

    private readonly List<GameObject> _crew = new();
    private readonly Dictionary<GameObject, DollSpot> _crewSpots = new();
    // Each spot's floor height in the room, measured once: the room does not change.
    private static readonly Dictionary<string, float> SpotFloors = new();

    // The dolls' own GFL2 lobby loops, as one controller whose states are "<body>-<pose>"
    // (Idle, LongIdle, Sit), built by Womenace.EditorTools.BuildDioramaPoses. A doll with
    // no lobby set borrows a teammate's.
    private const string PosesAsset = "environment/doll_poses/main";
    private static readonly Dictionary<string, string> PoseStandIns = new() { ["mechty"] = "klukai" };
    private const string PoseFallback = "lenna";
    private RuntimeAnimatorController _poses;
    private GameObject _posesPrefab;
    // The contact shadow's size under a doll (metres at canon height, X across her, Z front to back),
    // and its strength: a faint darkening right under her feet, which the directional shadow map is
    // too coarse to give (her cast shadow comes from StrategyDiorama.Shadows).
    internal static readonly Vector2 ShadowSize = new(0.7f, 0.55f);
    internal const float ContactShadowStrength = 0.35f;
    private const string ContactShadowName = "WOMENACE_DollShadow";

    private GameObject PosesPrefab() => _posesPrefab != null ? _posesPrefab : (_posesPrefab = Context.Assets.Load<GameObject>(PosesAsset));
    private void SpawnRoomDolls()
    {
        try
        {
            foreach (var doll in _crew)
                if (doll != null)
                    UnityEngine.Object.Destroy(doll);
            _crew.Clear();
            _crewSpots.Clear();
            _crewSeatShifts.Clear();
            _crewFidgets.Clear();
            if (_room == null)
                return;
            // Only dolls the player has hired stand in the room, in roster order, so each keeps
            // her spot for as long as the roster stays as it is.
            var dolls = new List<(string Tag, string Body)>();
            var bodies = HiredDollBodies();
            foreach (var (tag, asset) in bodies)
                dolls.Add((tag, asset.Split('/')[0]));
            _crewWears = Wearing(bodies);
            _crewWornOn = _policyScreen;
            foreach (var (tag, spot) in AssignSpots(dolls))
            {
                var doll = SpawnDoll(tag, "WOMENACE_RoomDoll_" + spot.Name);
                if (doll == null)
                    continue;
                Pose(doll, spot.Pose);
                _crew.Add(doll);
                _crewSpots[doll] = spot;
            }
            PlaceRoomDolls(true);
            Context.Log.Info($"strategy diorama: {_crew.Count} dolls in the room of {dolls.Count} hired");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: room dolls failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Who stands where. Table spots come first and then the rest, each lot in its order. A group
    // comes up at its first spot and takes all its spots at once or none. Each spot takes the
    // first doll in the roster who is still free and suits it.
    private List<(string Tag, DollSpot Spot)> AssignSpots(List<(string Tag, string Body)> dolls)
    {
        var units = new List<List<DollSpot>>();
        foreach (var spot in DollSpots.OrderBy(s => s.Table ? 0 : 1).ThenBy(s => s.Order))
        {
            var unit = spot.Group != null ? units.Find(u => u[0].Group == spot.Group) : null;
            if (unit == null)
                units.Add(unit = new List<DollSpot>());
            unit.Add(spot);
        }
        var placed = new List<(string Tag, DollSpot Spot)>();
        var taken = new HashSet<string>();
        foreach (var unit in units)
        {
            var picks = new List<(string Tag, DollSpot Spot)>();
            foreach (var spot in unit)
            {
                var pick = dolls.FindIndex(d => !taken.Contains(d.Tag) && !picks.Exists(p => p.Tag == d.Tag) && Suits(d.Body, spot));
                if (pick < 0)
                    break;
                picks.Add((dolls[pick].Tag, spot));
            }
            if (picks.Count != unit.Count)
                continue;
            foreach (var pick in picks)
                taken.Add(pick.Tag);
            placed.AddRange(picks);
        }
        return placed;
    }

    private bool Suits(string body, DollSpot spot)
    {
        if (spot.OwnOnly)
            return HasPose(body, spot.Pose);
        // Nobody stands in a chair: a seat is for a doll with a sitting loop to play.
        if (spot.Pose == "Sit")
            return HasPose(body, spot.Pose) || (PoseStandIns.TryGetValue(body, out var stand) && HasPose(stand, spot.Pose));
        return true;
    }

    // A new campaign shows the room before its leaders are hired: nobody stands in it yet and
    // nobody is on the roster. The bars stay shut over a room in that state for a moment, so the
    // dolls are in it when it shows.
    private bool RoomAwaitsDolls()
    {
        if (_room == null || _active != _ops || _crew.Count > 0)
            return false;
        var hired = StrategyState.Get()?.Roster?.m_HiredLeaders;
        return hired == null || hired.Count == 0 || HiredDollTags().Count > 0;
    }

    // Whether vanilla's dialog for a new campaign's first leaders is open.
    private static bool PickingLeaders() =>
        Il2CppMenace.UI.UIManager.Get()?.GetCurrentDialog()?.TryCast<Il2CppMenace.UI.PickInitialLeadersDialog>() != null;

    private string _crewWears, _crewWornOn;
    private float _crewCheckedAt;

    // Brings the room's dolls in again when the roster or an outfit is not what they were built
    // from. True when it did.
    private bool RefreshRoomDolls()
    {
        if (_crewWears == Wearing())
            return false;
        SpawnRoomDolls();
        return true;
    }

    // A new campaign shows the room before its leaders are hired, and a doll can be hired or
    // dismissed without the screen changing: the room looks at its roster every second.
    private void DriveRoomDolls()
    {
        // Each leader chosen for a new campaign is hired on the spot: the room waits for the
        // whole choice and brings them in once.
        if (_room == null || _active != _ops || Time.unscaledTime - _crewCheckedAt < 1f || PickingLeaders())
            return;
        _crewCheckedAt = Time.unscaledTime;
        if (!RefreshRoomDolls())
            Fidget();
    }

    // A standing doll breaks into her fidget now and then, at times of her own: GFL2 gives
    // several dolls the same fidget over their own stance, and two who started it together
    // would be seen to.
    private void Fidget()
    {
        foreach (var doll in _crew)
        {
            if (doll == null || !doll.activeInHierarchy || !_crewFidgets.TryGetValue(doll, out var fidget) || Time.unscaledTime < fidget.At)
                continue;
            var animator = doll.GetComponentInChildren<Animator>();
            if (animator == null || animator.IsInTransition(0) || animator.GetCurrentAnimatorStateInfo(0).shortNameHash != fidget.Idle)
                continue;
            animator.CrossFadeInFixedTime(fidget.Fidget, 0.4f, 0);
            _crewFidgets[doll] = (fidget.Idle, fidget.Fidget, Time.unscaledTime + UnityEngine.Random.Range(FidgetWaitMin, FidgetWaitMax));
        }
    }

    // Every hired doll with a model, and the body of the outfit her squad leader wears.
    private List<(string Tag, string Asset)> HiredDollBodies()
    {
        var bodies = new List<(string, string)>();
        foreach (var tag in HiredDollTags())
        {
            var asset = Transmog.BodyAssetFor(tag, Transmog.SelectionFor(Context, tag));
            if (asset != null)
                bodies.Add((tag, asset));
        }
        return bodies;
    }

    private string Wearing() => Wearing(HiredDollBodies());

    private static string Wearing(List<(string Tag, string Asset)> bodies) => string.Join(";", bodies.Select(b => b.Asset));

    private Animator _poseProbe;

    // Whether the poses hold this body's own loop for a pose, asked of a controller of our own
    // that plays nothing: a controller answers for its states only through a live Animator.
    private bool HasPose(string body, string pose)
    {
        if (_poseProbe == null)
        {
            var prefab = PosesPrefab();
            if (prefab == null || _room == null)
                return false;
            var probe = UnityEngine.Object.Instantiate(prefab, _room.transform, false);
            probe.name = "WOMENACE_PoseProbe";
            foreach (var renderer in probe.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            _poseProbe = probe.GetComponent<Animator>();
        }
        return _poseProbe != null && _poseProbe.HasState(0, Animator.StringToHash($"Base Layer.{body}-{pose}"));
    }

    // A doll's body in the outfit she wears now, under the room, or null when she has no model.
    private GameObject SpawnDoll(string tag, string name)
    {
        var asset = Transmog.BodyAssetFor(tag, Transmog.SelectionFor(Context, tag));
        var prefab = asset != null ? Context.Assets.Load<GameObject>(asset) : null;
        if (prefab == null)
            return null;
        // Her hair and cloth move: the spring system hears of bodies the outfit system hands out,
        // and the room places its dolls itself.
        SpringBoneSystem.Instance?.NoteBodyPrefab(prefab);
        var doll = UnityEngine.Object.Instantiate(prefab, _room.transform, false);
        doll.name = name + ":" + asset.Split('/')[0];
        doll.transform.localScale = Vector3.one * RoomDollScale;
        // Her contact shadow on the floor.
        var shadow = PosesPrefab()?.transform.Find("shadow");
        if (shadow != null)
        {
            var blob = UnityEngine.Object.Instantiate(shadow.gameObject, doll.transform, false);
            blob.name = ContactShadowName;
            blob.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            blob.transform.localScale = new Vector3(ShadowSize.x, 1f, ShadowSize.y);
            var material = blob.GetComponent<Renderer>()?.sharedMaterial;
            if (material != null)
                material.SetFloat("_Strength", ContactShadowStrength);
        }
        return doll;
    }

    // The range a doll's loops' pace is drawn from, and how long she stands between fidgets.
    private const float PoseSpeedMin = 0.9f;
    private const float PoseSpeedMax = 1.1f;
    private const float FidgetWaitMin = 12f;
    private const float FidgetWaitMax = 45f;
    // Each standing doll's idle and fidget loops, and when she next fidgets.
    private readonly Dictionary<GameObject, (int Idle, int Fidget, float At)> _crewFidgets = new();

    // Swaps the doll's soldier controller for the poses and starts her own loop for the pose.
    private void Pose(GameObject doll, string pose)
    {
        if (_poses == null)
            _poses = PosesPrefab()?.GetComponent<Animator>()?.runtimeAnimatorController;
        var animator = doll.GetComponentInChildren<Animator>();
        if (_poses == null || animator == null)
            return;
        var body = doll.name.Substring(doll.name.IndexOf(':') + 1);
        animator.runtimeAnimatorController = _poses;
        animator.applyRootMotion = false;
        // Her pose is read off her bones to seat her (SeatShift), before any camera has seen her.
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // The room's dolls are switched off with the room and for the armoury, and an Animator
        // switched off starts over in the pose set's first loop, in step with every other doll.
        // Hers keeps its loop and its place in it.
        animator.keepAnimatorStateOnDisable = true;
        // Her own pose, a stand-in's, her own idle, then the fallback doll's pose.
        var stand = PoseStandIns.GetValueOrDefault(body);
        var candidates = new[] { (body, pose), (stand, pose), (stand ?? body, "Idle"), (PoseFallback, pose) };
        foreach (var (who, what) in candidates)
        {
            if (who == null)
                continue;
            var state = Animator.StringToHash($"Base Layer.{who}-{what}");
            if (!animator.HasState(0, state))
                continue;
            // Every standing and sitting loop is two seconds of breathing: each doll breathes
            // from a point and at a pace of her own.
            animator.speed = UnityEngine.Random.Range(PoseSpeedMin, PoseSpeedMax);
            animator.Play(state, 0, UnityEngine.Random.value);
            var (idle, fidget) = (Animator.StringToHash($"{who}-Idle"), Animator.StringToHash($"{who}-LongIdle"));
            if (what != "Sit" && animator.HasState(0, idle) && animator.HasState(0, fidget))
                _crewFidgets[doll] = (idle, fidget, Time.unscaledTime + UnityEngine.Random.Range(0f, FidgetWaitMax));
            animator.Update(0f);
            return;
        }
        Context.Log.Info($"strategy diorama: no {pose} pose for {body}");
    }

    // Stands each doll at her spot, on the room's floor there, and lights her from the room's
    // probes. The armoury hides the room's dolls, and the room's next screen shows them again.
    private void PlaceRoomDolls(bool spawned = false)
    {
        if (_room == null)
            return;
        // A doll wears her squad leader's outfit as it is now, and the room holds the roster as
        // it is now: either changing on another screen brings the room's dolls in again.
        // Vanilla asks for a screen's policy many times over: the dolls are looked at once a
        // screen, when they are first brought in, and whenever the armoury has put them away.
        var arrived = spawned || _crewWornOn != _policyScreen;
        if (arrived && !spawned)
        {
            _crewWornOn = _policyScreen;
            if (RefreshRoomDolls())
                return;
        }
        foreach (var doll in _crew)
        {
            if (doll == null || !_crewSpots.TryGetValue(doll, out var spot))
                continue;
            if (doll.activeSelf && !arrived)
                continue;
            PlaceAt(doll, spot);
        }
    }

    private void PlaceAt(GameObject doll, DollSpot spot)
    {
        doll.SetActive(true);
        var at = spot.Position;
        at.y = FloorAt(spot);
        doll.transform.localPosition = at;
        doll.transform.localRotation = Quaternion.Euler(0f, spot.Yaw, 0f);
        doll.transform.localScale = Vector3.one * RoomDollScale;
        if (spot.Pose == "Sit")
            doll.transform.localPosition += doll.transform.localRotation * SeatShift(doll);
        var share = WindowShare(at);
        ProbeLight(doll, _ops, share, share > 0f ? WindowLightFrom(doll, at) : null);
    }

    // How far behind her root a sitter's hips are in the sit the seats' spots were set for.
    private const float SeatHipsBack = 0.1f;
    private readonly Dictionary<GameObject, Vector3> _crewSeatShifts = new();

    // How far a sitter moves from her spot (in her own axes, room metres) for her hips to be
    // over the seat. Each doll's sitting loop was made for a seat of her own in GFL2 and puts
    // her hips somewhere else behind her root: one sits a hand's width back and another a
    // forearm's, which in the room's chair is through its back with her knees in the seat.
    // Measured off her own pose once, so she keeps her place for as long as she is in the room.
    private Vector3 SeatShift(GameObject doll)
    {
        if (_crewSeatShifts.TryGetValue(doll, out var known))
            return known;
        var shift = Vector3.zero;
        var animator = doll.GetComponentInChildren<Animator>();
        var left = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg) : null;
        var right = left != null ? animator.GetBoneTransform(HumanBodyBones.RightUpperLeg) : null;
        if (right != null)
        {
            // Her pose as it is now, written to her bones whether or not a camera sees her.
            animator.Update(0f);
            var hips = doll.transform.InverseTransformPoint((left.position + right.position) * 0.5f);
            shift = new Vector3(-hips.x, 0f, -(hips.z + SeatHipsBack)) * RoomDollScale;
        }
        return _crewSeatShifts[doll] = shift;
    }

    // The room's floor under a spot: of every upward face of the room over the spot, the one
    // nearest the height the spot was given, which tells the floor from a chair's seat or a rail
    // above it. A room whose meshes cannot be read keeps the given height.
    private float FloorAt(DollSpot spot)
    {
        if (SpotFloors.TryGetValue(spot.Name, out var known))
            return known;
        var floor = spot.Position.y;
        try
        {
            var room = _room.transform;
            var world = room.TransformPoint(spot.Position);
            var best = float.PositiveInfinity;
            foreach (var renderer in room.GetComponentsInChildren<MeshRenderer>())
            {
                var bounds = renderer.bounds;
                if (world.x < bounds.min.x || world.x > bounds.max.x || world.z < bounds.min.z || world.z > bounds.max.z)
                    continue;
                if (renderer.GetComponentInParent<Animator>() != null)
                    continue;
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                    continue;
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                var matrix = renderer.transform.localToWorldMatrix;
                for (var i = 0; i + 2 < triangles.Length; i += 3)
                {
                    var a = matrix.MultiplyPoint3x4(vertices[triangles[i]]);
                    var b = matrix.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                    var c = matrix.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                    // Where the upright line through the spot crosses this face, if it does.
                    var area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);
                    if (Mathf.Abs(area) < 1e-7f)
                        continue;
                    var u = ((world.x - a.x) * (c.z - a.z) - (c.x - a.x) * (world.z - a.z)) / area;
                    var v = ((b.x - a.x) * (world.z - a.z) - (world.x - a.x) * (b.z - a.z)) / area;
                    if (u < 0f || v < 0f || u + v > 1f)
                        continue;
                    // Only faces a doll could stand on: turned up, not a wall or an underside.
                    var normal = Vector3.Cross(b - a, c - a);
                    if (Mathf.Abs(normal.y) < 0.7f * normal.magnitude)
                        continue;
                    var y = room.InverseTransformPoint(a + (b - a) * u + (c - a) * v).y;
                    if (Mathf.Abs(y - spot.Position.y) < Mathf.Abs(best - spot.Position.y))
                        best = y;
                }
            }
            if (!float.IsPositiveInfinity(best))
                floor = best;
            Context.Log.Info($"strategy diorama: spot {spot.Name} floor {floor:F2} (given {spot.Position.y:F2})");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: floor under {spot.Name} not measured: {ex.GetType().Name}: {ex.Message}");
        }
        return SpotFloors[spot.Name] = floor;
    }

    // Each lit unit's rig, by the unit.
    private readonly Dictionary<IntPtr, Light[]> _dollRigs = new();

    // Lights a unit with the rig, in the frame of the room camera looking at it.
    private void LightDoll(GameObject doll, float scale = 1f)
    {
        if (!_dollRigs.TryGetValue(doll.Pointer, out var rig) || rig[0] == null)
            _dollRigs[doll.Pointer] = rig = BuildRig(doll);
        var away = _camera != null ? doll.transform.position - _camera.transform.position : doll.transform.forward;
        away.y = 0f;
        var frame = Quaternion.LookRotation(away.sqrMagnitude > 1e-6f ? away : Vector3.forward, Vector3.up);
        var chest = doll.transform.position + Vector3.up * 1.3f;
        for (int i = 0; i < UnitRig.Length; i++)
        {
            var (offset, _, lumens) = UnitRig[i];
            var t = rig[i].transform;
            t.position = doll.transform.position + frame * offset;
            t.rotation = Quaternion.LookRotation(chest - t.position);
            rig[i].intensity = lumens * RigLightScale * scale;
        }
    }

    private static Light[] BuildRig(GameObject doll)
    {
        var rig = new Light[UnitRig.Length];
        for (int i = 0; i < rig.Length; i++)
            rig[i] = RigLight(doll, "WOMENACE_DollLight" + i, UnitRig[i].Colour, 8f);
        return rig;
    }

    // One light of a doll's rig: a point light of her own, which shadows nothing.
    private static Light RigLight(GameObject doll, string name, Color colour, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(doll.transform, false);
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.shadows = LightShadows.None;
        // Every rendering layer: the armoury's units sit on MENACE's own light layers.
        light.renderingLayerMask = -1;
        light.color = colour;
        light.range = range;
        light.lightUnit = UnityEngine.Rendering.LightUnit.Lumen;
        go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>().affectsVolumetric = false;
        return light;
    }

    // Takes the rig off every doll under a root (the armoury's squad, given back to vanilla).
    private void RemoveRigs(Transform root)
    {
        List<IntPtr> gone = null;
        foreach (var (doll, rig) in _dollRigs)
        {
            if (rig[0] != null && !rig[0].transform.IsChildOf(root))
                continue;
            foreach (var light in rig)
                if (light != null)
                    UnityEngine.Object.Destroy(light.gameObject);
            (gone ??= new()).Add(doll);
        }
        if (gone != null)
            foreach (var doll in gone)
                _dollRigs.Remove(doll);
    }

    // The character tags of every hired leader that is one of ours, in roster order, once each
    // (a doll's two forms share one character).
    private static List<string> HiredDollTags()
    {
        var tags = new List<string>();
        var hired = StrategyState.Get()?.Roster?.m_HiredLeaders;
        if (hired == null)
            return tags;
        for (int i = 0; i < hired.Count; i++)
        {
            var tag = Affinity.CharacterTag(hired[i]);
            if (tag != null && !tags.Contains(tag))
                tags.Add(tag);
        }
        return tags;
    }

}
