using UnityEngine;
using Vec = System.Numerics.Vector3;

namespace WOMENACE.Code;

// Hooks for the diorama dev verbs (code/Dev/Diorama.Dev.cs). *.Dev.cs files stay out of release builds.
public sealed partial class StrategyDiorama
{
    // Dev: each story faction's trust level, upgrades taken and whether it has more to give, with
    // whether a claim waits and whether the Dinergate is calling.
    // Dev: changes a story faction's trust, by its template id, and lays the shell out again so the
    // board and the Dinergate take it in.
    internal static string FactionTrustFromDev(string template, int change)
    {
        var factions = Il2CppMenace.States.StrategyState.Get()?.StoryFactions?.m_Factions;
        if (factions == null)
            return "no factions";
        foreach (var faction in factions.Values)
            if (faction?.GetTemplate()?.name == template)
            {
                faction.ChangeTrust(change);
                _instance?.ApplyHome(_instance._homeScreen, _instance._homeRoomShown);
                return $"{template}: level {faction.GetTrustLevel()}, total {faction.GetTotalTrust()}";
            }
        return "no faction " + template;
    }

    // Every element on any panel carrying the game font class, with the font it draws in.
    internal static string GameFontsFromDev()
    {
        var lines = new List<string>();
        void Walk(string document, UnityEngine.UIElements.VisualElement e)
        {
            if (e.ClassListContains(VanillaNavigation.GameFontClass))
            {
                var label = e.TryCast<UnityEngine.UIElements.TextElement>() ?? FirstText(e);
                var font = label?.resolvedStyle.unityFontDefinition.fontAsset;
                lines.Add($"{document}/{e.name}: {(font != null ? font.name : "none")} ({label?.text})");
            }
            for (var i = 0; i < e.childCount; i++)
                Walk(document, e[i]);
        }
        static UnityEngine.UIElements.TextElement FirstText(UnityEngine.UIElements.VisualElement e)
        {
            for (var i = 0; i < e.childCount; i++)
                if ((e[i].TryCast<UnityEngine.UIElements.TextElement>() ?? FirstText(e[i])) is { } found)
                    return found;
            return null;
        }
        foreach (var document in UnityEngine.Object.FindObjectsOfType<UnityEngine.UIElements.UIDocument>())
            if (document?.rootVisualElement is { } root)
                Walk(document.name, root);
        return lines.Count > 0 ? string.Join("\n", lines) : "no game font elements";
    }

    internal static string FactionClaimsFromDev()
    {
        var factions = Il2CppMenace.States.StrategyState.Get()?.StoryFactions?.m_Factions;
        if (factions == null)
            return "no factions";
        var lines = new List<string>();
        foreach (var faction in factions.Values)
            if (faction != null)
                lines.Add($"{faction.GetTemplate()?.name}: level {faction.GetTrustLevel()}, taken {faction.GetUnlockedUpgradeCount()}, more {faction.HasLockedUpgrades()}, status {faction.GetStatus()}");
        lines.Add($"claim waits {FactionUnlockWaiting()}, notice {_instance?.DinergateNotice}");
        return string.Join("\n", lines);
    }

    // Dev: mission select's card places as the view starts, in the shell's pixels across, against
    // the side panels' inner edges the cards keep between.
    internal static string PlanetCardsFromDev()
    {
        var d = _instance;
        var camera = d?._camera != null ? d._camera.GetComponent<Camera>() : null;
        if (camera == null)
            return "no camera";
        var (fov, shift, lift) = (camera.fieldOfView, d._lensShift, d._lensLift);
        d.SetLens(camera, PlanetFraming.Fov, PlanetFraming.Shift, PlanetFraming.Lift);
        var lens = camera.projectionMatrix;
        d.SetLens(camera, fov, shift, lift);
        var perSeen = lens.m00 * 2f * Mathf.Tan(PlanetFraming.Fov * 0.5f * Mathf.Deg2Rad);
        var block = Mathf.Min(1f, 16f / 9f / camera.aspect);
        float Shell(float seen) => ((perSeen * seen - lens.m02 + 1f) / 2f - 0.5f) / block * 1280f + 640f;
        var free = d.PlanetFreeAcross();
        var lines = new List<string> { $"free {Shell(free.Left):0} to {Shell(free.Right):0}" };
        foreach (var marker in d._markers)
            lines.Add($"{marker.Poi?.name}: place at {Shell(SeenAtStart(new Vector3(marker.Surface.X, marker.Surface.Y, marker.Surface.Z)).X):0}");
        return string.Join("\n", lines);
    }

    // Dev: structures drawn on the deployment map as another kind, by template name.
    private static readonly Dictionary<string, PrepStructure> PrepKindsAs = new();

    static partial void PrepKindOverride(string template, ref PrepStructure kind)
    {
        if (template != null && PrepKindsAs.TryGetValue(template, out var drawn))
            kind = drawn;
    }

    internal static string PrepKindAsFromDev(string template, string kind)
    {
        if (string.IsNullOrEmpty(kind))
            PrepKindsAs.Remove(template);
        else if (Enum.TryParse<PrepStructure>(kind, true, out var drawn))
            PrepKindsAs[template] = drawn;
        else
            return "kind is plain or marked";
        if (_instance != null)
            _instance._prepMapOf = IntPtr.Zero;
        return $"{template} drawn as {(string.IsNullOrEmpty(kind) ? "itself" : kind)}, the map builds again";
    }

    // Dev: a plan of the Squads hall from above, in the stage's own axes, 0.4 metres to a
    // character: '#' where a room piece stands between knee and head height, 'T' the staged
    // squad, 'S' the stand. x runs across the page (low to high), z down it (high to low). A room
    // piece is drawn as the box of its mesh, turned as the piece is, so a slanted wall is true.
    internal static string HallPlanFromDev(float reach, float cx = 0f, float cz = 0f, float floor = 0f, float cell = 0.4f)
    {
        var d = _instance;
        if (d?._room == null)
            return "no room";
        var stage = d._stage != null ? d._stage : d._room.transform;
        var side = (int)(reach * 2f / cell);
        var grid = new char[side, side];
        for (var z = 0; z < side; z++)
            for (var x = 0; x < side; x++)
                grid[x, z] = '.';
        Vector3 Local(Vector3 world) => stage.InverseTransformPoint(world) - new Vector3(cx, floor, cz);
        void Mark(Vector3 world, char mark, bool band)
        {
            var local = Local(world);
            if (band && (local.y < 0.4f || local.y > 2.2f))
                return;
            var (x, z) = ((int)((local.x + reach) / cell), (int)((local.z + reach) / cell));
            if (x >= 0 && z >= 0 && x < side && z < side && (grid[x, z] == '.' || mark == 'S' || mark == 'D'))
                grid[x, z] = mark;
        }
        var squad = new HashSet<int>();
        foreach (var staged in d._stagedRoots)
            foreach (var r in staged.Root.GetComponentsInChildren<Renderer>())
            {
                squad.Add(r.GetInstanceID());
                if (!r.enabled || r.TryCast<ParticleSystemRenderer>() != null)
                    continue;
                var b = r.localBounds;
                for (var i = 0; i <= 16; i++)
                    for (var j = 0; j <= 16; j++)
                        for (var k = 0; k <= 16; k++)
                            Mark(r.transform.TransformPoint(new Vector3(
                                Mathf.Lerp(b.min.x, b.max.x, i / 16f),
                                Mathf.Lerp(b.min.y, b.max.y, j / 16f),
                                Mathf.Lerp(b.min.z, b.max.z, k / 16f))), 'T', false);
            }
        var (pieces, seen, meshless, far, wide) = (0, 0, 0, 0, 0);
        foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || squad.Contains(r.GetInstanceID()) || r.transform.root.name.StartsWith("WOMENACE_") || r.GetComponentInParent<Animator>() != null)
                continue;
            seen++;
            var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null)
            {
                meshless++;
                continue;
            }
            var world = r.bounds;
            var near = Local(world.center);
            if (Mathf.Abs(near.x) > reach + world.extents.magnitude || Mathf.Abs(near.z) > reach + world.extents.magnitude)
            {
                far++;
                continue;
            }
            if (world.size.x > 8f && world.size.z > 8f)
            {
                wide++;
                continue;
            }
            pieces++;
            var box = mesh.bounds;
            var eye = Local(d._camera.transform.position);
            var (ex, ez) = ((int)((eye.x + reach) / cell), (int)((eye.z + reach) / cell));
            if (ex >= 0 && ez >= 0 && ex < side && ez < side)
                grid[ex, ez] = 'C';
            for (var i = 0; i <= 12; i++)
                for (var j = 0; j <= 12; j++)
                    for (var k = 0; k <= 12; k++)
                        Mark(r.transform.TransformPoint(new Vector3(
                            Mathf.Lerp(box.min.x, box.max.x, i / 12f),
                            Mathf.Lerp(box.min.y, box.max.y, j / 12f),
                            Mathf.Lerp(box.min.z, box.max.z, k / 12f))), '#', true);
        }
        if (d._stage != null)
            Mark(d._stage.TransformPoint(ArmoryStand), 'S', false);
        foreach (var doll in d._crew)
            if (doll != null && doll.activeInHierarchy)
                Mark(doll.transform.position, 'D', false);
        var text = new System.Text.StringBuilder($"{pieces} pieces of {seen} ({meshless} without a mesh, {far} far, {wide} wide), centre ({cx}, {cz}), x {cx - reach} to {cx + reach} across, z {cz + reach} to {cz - reach} down, {cell} m a character\n");
        for (var z = side - 1; z >= 0; z--)
        {
            for (var x = 0; x < side; x++)
                text.Append(grid[x, z]);
            text.Append('\n');
        }
        return text.ToString();
    }

    // Dev: who is listening and what is playing: every AudioListener and whether it is on, the
    // engine's volume and pause, and how many sources are playing.
    internal static string EarsFromDev()
    {
        var lines = new List<string> { $"volume {AudioListener.volume}, paused {AudioListener.pause}" };
        foreach (var ear in UnityEngine.Object.FindObjectsOfType<AudioListener>(true))
            lines.Add($"listener {PathOf(ear.transform)} enabled {ear.enabled} active {ear.gameObject.activeInHierarchy}");
        var sources = UnityEngine.Object.FindObjectsOfType<AudioSource>(true);
        var playing = 0;
        foreach (var source in sources)
            if (source.isPlaying)
            {
                playing++;
                if (playing <= 12)
                    lines.Add($"playing {PathOf(source.transform)} clip {(source.clip != null ? source.clip.name : "none")} volume {source.volume} mute {source.mute} spatial {source.spatialBlend} group {(source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "none")}");
            }
        lines.Add($"{playing} of {sources.Length} sources playing");
        return string.Join("\n", lines);
    }

    private static string PathOf(Transform t)
    {
        var path = t.name;
        for (var p = t.parent; p != null; p = p.parent)
            path = p.name + "/" + path;
        return path;
    }

    // Dev: every showing renderer within a reach of the room camera's line of sight end, written
    // to a file: path, material, shader, where it is and how big.
    internal static string SceneRenderersFromDev(string path)
    {
        var d = _instance;
        var lines = new List<string>();
        foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            var m = r.sharedMaterial;
            var colour = m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : m != null && m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "";
            var texture = m != null && m.HasProperty("_MainTex") && m.mainTexture != null ? m.mainTexture.name : "";
            lines.Add($"{PathOf(r.transform)} | {(m != null ? m.name : "none")} | {(m != null ? m.shader.name : "")} | {colour} | {texture} | centre {r.bounds.center} size {r.bounds.size} | room {(d?._room != null ? d._room.transform.InverseTransformPoint(r.bounds.center).ToString("F2") : "")} yaw {r.transform.eulerAngles.y - (d?._room != null ? d._room.transform.eulerAngles.y : 0f):F0}");
        }
        var camera = d?._camera != null ? d._camera.GetComponent<Camera>() : null;
        if (camera != null)
            lines.Add($"camera at {camera.transform.position} facing {camera.transform.forward} clear {camera.clearFlags} background {camera.backgroundColor}");
        System.IO.File.WriteAllLines(path, lines.ToArray());
        return $"{lines.Count} lines";
    }

    internal static string HologramFloatFromDev(string name, float value)
    {
        var d = _instance;
        if (d == null)
            return "no diorama";
        foreach (var material in d._candidateHolograms)
            material.SetFloat(name, value);
        return $"{d._candidateHolograms.Count} materials";
    }

    // Dev: drive the orbit as a drag and a wheel would.
    internal static string NudgeOrbit(float yaw, float pitch, float zoomNotches)
    {
        var d = _instance;
        if (d?._armoryVanilla == null)
            return "armoury not staged";
        d._orbitYawTarget = Mathf.Clamp(d._orbitYawTarget + yaw, d.YawMin(), d.YawMax());
        d._orbitPitchTarget = Mathf.Clamp(d._orbitPitchTarget + pitch, d._stageShot.PitchMin, OrbitPitchMax);
        d._orbitDistanceTarget = Mathf.Clamp(d._orbitDistanceTarget * Mathf.Exp(-zoomNotches * OrbitZoomPerNotch * 2f), OrbitDistanceMin, d._orbitDistanceMax);
        return $"orbit yaw {d._orbitYawTarget:F0} pitch {d._orbitPitchTarget:F0} distance {d._orbitDistanceTarget:F1}";
    }

    // Dev: sets the player's shadow quality without saving it and reports each shadowed light's
    // resolution, which follows on the next frame.
    internal static string ShadowQuality(int quality)
    {
        var d = _instance;
        if (d == null)
            return "no diorama";
        var settings = Il2CppMenace.PlayerSettings.PersistentPlayerSettings.DisplaySettings;
        var before = settings.ShadowQuality;
        if (quality >= 0)
        {
            settings.ShadowQuality = quality;
            Il2CppMenace.PlayerSettings.PersistentPlayerSettings.DisplaySettings = settings;
        }
        string Res(Light l) => l.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>() is { } hd ? $"{l.name}={hd.shadowResolution.@override}" : $"{l.name}=-";
        var lines = new System.Collections.Generic.List<string> { $"quality {before} -> {(quality >= 0 ? quality : before)}" };
        if (d._shadowLight != null)
            lines.Add(Res(d._shadowLight));
        foreach (var set in d.Sets)
            if (set?.LiveLights != null)
                foreach (var light in set.LiveLights)
                    if (light != null && light.shadows != LightShadows.None)
                        lines.Add(Res(light));
        return string.Join(" ", lines);
    }

    // Dev: counts frames for a while and keeps the rate, for comparing one view's cost with another's.
    internal static string MeasureFromDev(float seconds)
    {
        if (_instance == null)
            return "no diorama";
        _measured = null;
        _instance.Context.Coroutines.Start(Measure(seconds));
        return $"measuring for {seconds} s";
    }

    internal static string MeasuredFromDev() => _measured ?? "not measured yet";

    private static string _measured;

    private static System.Collections.IEnumerator Measure(float seconds)
    {
        var (frames, worst, start) = (0, 0f, Time.realtimeSinceStartup);
        while (Time.realtimeSinceStartup - start < seconds)
        {
            yield return null;
            frames++;
            worst = Mathf.Max(worst, Time.unscaledDeltaTime);
        }
        var took = Time.realtimeSinceStartup - start;
        _measured = $"{frames / took:F1} fps ({took / frames * 1000f:F1} ms a frame, worst {worst * 1000f:F0} ms) at {Screen.width} x {Screen.height}";
    }

    // Set while a dev verb stands in for the Escape key (EscapeUsed).
    private static bool _devEscape;

    // Dev: runs the active screen's own answer to the back key, as Escape does.
    internal static string BackKeyFromDev()
    {
        var screen = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen();
        if (screen == null)
            return "no screen";
        _devEscape = true;
        try
        {
            screen.OnBackKeyUp();
        }
        finally
        {
            _devEscape = false;
        }
        return "back key on " + screen.GetIl2CppType().Name;
    }

    // Dev: switches a home screen between its Home and Operations views.
    internal static string SetHomeViewFromDev(string view)
    {
        if (_instance == null || !Enum.TryParse<HomeView>(view, true, out var v))
            return "no diorama or unknown view";
        _instance.SetHomeView(v);
        return $"view {_instance._homeView}, shell {(_instance._shell != null ? "shown" : "absent")}";
    }

    // Dev: puts a room on screen ("ops", "refit" or "weapon") and stands the camera in it, in the
    // room's own space.
    internal static string RoomLookFromDev(string room, float ex, float ey, float ez, float tx, float ty, float tz, float fov)
    {
        var d = _instance;
        var camera = RoomCamera;
        var set = room == "weapon" ? d?._weaponSet : room == "refit" ? d?._refitSet : d?._ops;
        if (d == null || camera == null || d.SpawnSet(set) == null)
            return "no room camera or no such room";
        d.ActivateSet(set);
        var eye = set.Root.transform.TransformPoint(new Vector3(ex, ey, ez));
        var target = set.Root.transform.TransformPoint(new Vector3(tx, ty, tz));
        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
        camera.fieldOfView = fov;
        camera.orthographic = false;
        d._shot = (camera.transform.position, camera.transform.rotation, fov, d._lensShift);
        d._shotTravel = 1f;
        return $"{room}: looking from ({ex}, {ey}, {ez}) towards ({tx}, {ty}, {tz}) at {fov}";
    }

    // Points the room camera somewhere, in room space, until the next screen frames it again.
    internal static string LookFromDev(float ex, float ey, float ez, float tx, float ty, float tz, float fov)
    {
        var d = _instance;
        var camera = RoomCamera;
        if (d == null || camera == null || d._room == null)
            return "no room camera";
        var eye = d._room.transform.TransformPoint(new Vector3(ex, ey, ez));
        var target = d._room.transform.TransformPoint(new Vector3(tx, ty, tz));
        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
        camera.fieldOfView = fov;
        camera.orthographic = false;
        d._shot = (camera.transform.position, camera.transform.rotation, fov, d._lensShift);
        d._shotTravel = 1f;
        return $"looking from ({ex}, {ey}, {ez}) towards ({tx}, {ty}, {tz}) at {fov}";
    }

    // Shifts the room camera's lens, in parts of the frame: the view slides across the screen
    // while lines that were parallel to the screen stay so.
    internal static string LensShiftFromDev(float x, float y)
    {
        var camera = RoomCamera;
        if (camera == null)
            return "no room camera";
        _instance?.SetLens(camera, camera.fieldOfView, x, y);
        return $"lens shift ({x}, {y}), fov {camera.fieldOfView:0.#}, gate fit {camera.gateFit}, sensor {camera.sensorSize}";
    }

    // Dev: the room camera straight down over a room-space point, orthographic, its near plane
    // set to cut the ceiling away: a true plan of the room for placing things on.
    internal static string TopDownFromDev(float cx, float cz, float height, float halfHeight, float near)
    {
        var d = _instance;
        var camera = RoomCamera;
        if (camera == null || d?._room == null)
            return "no room camera";
        var room = d._room.transform;
        // The picture is the room as the home camera faces it: its top is room -x, its right room +z.
        var rotation = Quaternion.LookRotation(room.TransformDirection(Vector3.down), room.TransformDirection(Vector3.left));
        d._camera.transform.SetPositionAndRotation(room.TransformPoint(new Vector3(cx, height, cz)), rotation);
        d.SetLens(camera, camera.fieldOfView, 0f, 0f);
        camera.orthographic = true;
        camera.orthographicSize = halfHeight;
        camera.nearClipPlane = near;
        d._shot = (d._camera.transform.position, rotation, camera.fieldOfView, 0f);
        d._shotTravel = 1f;
        return $"plan over ({cx}, {cz}) from {height}, {halfHeight * 2f} m tall, {halfHeight * 2f * camera.aspect} m wide, near {near}; anchor {room.InverseTransformPoint(d._anchor.position)} looking {room.InverseTransformDirection(d._anchor.forward)}";
    }

    // Makes the room camera orthographic at a half height in room units, or perspective again at 0.
    internal static string OrthoFromDev(float size)
    {
        var camera = RoomCamera;
        if (camera == null)
            return "no room camera";
        camera.orthographic = size > 0f;
        if (size > 0f)
            camera.orthographicSize = size;
        return camera.orthographic ? $"orthographic, half height {size}" : "perspective";
    }

    // What vanilla's system map would find under the pointer: the scene's main camera, the camera
    // the screen holds, what it has under the pointer now, and what a ray from the main camera
    // hits across a grid of screen places (pixels from the top left).
    internal static string StarProbeFromDev(int step)
    {
        var screen = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.TryCast<Il2CppMenace.UI.Strategy.SystemMapUIScreen>();
        var main = Camera.main;
        var lines = new List<string>
        {
            $"main camera {(main != null ? main.name + (main.enabled ? " on" : " off") : "none")}",
            $"screen's camera {(screen?.m_Camera != null ? screen.m_Camera.name + (screen.m_Camera.enabled ? " on" : " off") : "none")}, hovered {(screen?.m_Hovered != null ? screen.m_Hovered.name : "nothing")}",
            $"engine's pointer {Input.mousePosition}",
        };
        var camera = screen?.m_Camera != null ? screen.m_Camera : main;
        if (camera == null)
            return string.Join("\n", lines);
        var hits = new Dictionary<string, (int Count, int X, int Y)>();
        for (var y = step / 2; y < Screen.height; y += step)
            for (var x = step / 2; x < Screen.width; x += step)
                if (Physics.Raycast(camera.ScreenPointToRay(new Vector3(x, Screen.height - y, 0f)), out var hit, 10000f))
                {
                    var name = hit.collider.name + "/" + (hit.collider.transform.parent != null ? hit.collider.transform.parent.name : "");
                    hits[name] = hits.TryGetValue(name, out var had) ? (had.Count + 1, had.X, had.Y) : (1, x, y);
                }
        lines.AddRange(hits.Select(h => $"{h.Key}: {h.Value.Count} places, first at ({h.Value.X}, {h.Value.Y})"));
        return string.Join("\n", lines);
    }

    // Shows or hides every renderer of the active room whose object's name starts with some
    // words, to see what a part of the room adds to the picture.
    internal static string PartsFromDev(string nameStart, bool show)
    {
        var d = _instance;
        if (d?._active?.Root == null)
            return "no room";
        var (count, lines) = (0, new List<string>());
        foreach (var renderer in d._active.Root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.name.StartsWith(nameStart, StringComparison.Ordinal))
                continue;
            renderer.enabled = show;
            count++;
            if (lines.Count < 14)
                lines.Add($"  {renderer.name} [{renderer.sharedMaterial?.name}, {renderer.sharedMaterial?.shader?.name}, queue {renderer.sharedMaterial?.renderQueue}] at {d._active.Root.transform.InverseTransformPoint(renderer.bounds.center)} size {renderer.bounds.size} active {renderer.gameObject.activeInHierarchy}");
        }
        return $"{count} renderers named {nameStart}* {(show ? "shown" : "hidden")}\n" + string.Join("\n", lines);
    }

    // Sets a number on the materials of every renderer of the active room whose object's name
    // starts with some words, to find what a part's look hangs on.
    internal static string PartsSetFromDev(string nameStart, string property, float value)
    {
        var d = _instance;
        if (d?._active?.Root == null)
            return "no room";
        var count = 0;
        foreach (var renderer in d._active.Root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.name.StartsWith(nameStart, StringComparison.Ordinal))
                continue;
            renderer.material.SetFloat(property, value);
            count++;
        }
        return $"{property} = {value} on {count} renderers named {nameStart}*";
    }

    // The staged units' renderers the shot is framed by, each with its type and its box about the
    // stand, and the radius and aim the framing takes from them.
    internal static string StagedBoundsFromDev()
    {
        var d = _instance;
        if (d?._stage == null || d._stagedRoots.Count == 0)
            return "nothing staged";
        var stand = d._stage.TransformPoint(ArmoryStand);
        var lines = new List<string>();
        foreach (var staged in d._stagedRoots)
        {
            if (staged.Root == null || !staged.Root.gameObject.activeInHierarchy)
                continue;
            lines.Add($"== {staged.Root.name}");
            foreach (var renderer in staged.Root.GetComponentsInChildren<Renderer>())
            {
                if (!FramesSquad(renderer))
                    continue;
                var b = renderer.bounds;
                lines.Add($"  {renderer.GetIl2CppType().Name} {renderer.name}: centre {b.center - stand} extents {b.extents} mat {renderer.sharedMaterial?.name}");
            }
        }
        var focus = d.SquadFocus(out var radius);
        lines.Add($"radius {radius:F2}, aim {focus - stand}");
        return string.Join("\n", lines);
    }

    // Sets what lights the staged squad, to tell its lights apart: the share of the sun's key
    // its units take, and whether the room's lamps that light characters and the units' own
    // rigs are on.
    internal static string SquadLightFromDev(float sun, bool lamps, bool rigs)
    {
        var d = _instance;
        if (d?._active?.Root == null || d._stagedRoots.Count == 0)
            return "nothing staged";
        var block = new MaterialPropertyBlock();
        var renderers = 0;
        foreach (var staged in d._stagedRoots)
            if (staged.Root != null)
                foreach (var renderer in staged.Root.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.GetPropertyBlock(block);
                    block.SetFloat("_KeyOcclusion", sun);
                    renderer.SetPropertyBlock(block);
                    renderers++;
                }
        var lit = 0;
        foreach (var light in d._active.LiveLights)
            if (light != null && LightsCharacters(light))
            {
                light.enabled = lamps;
                lit++;
            }
        return $"sun {sun} on {renderers} renderers, {lit} lamps {(lamps ? "on" : "off")}, {DollRigs(rigs)}";
    }

    // Stands a doll's body in the room as its prefab holds it, with no pose played, to see the
    // shape she is baked in apart from what any animation does to it.
    internal static string BarePoseFromDev(string tag, float x, float y, float z, float yaw)
    {
        var d = _instance;
        if (d == null || d._room == null)
            return "no room";
        var doll = d.SpawnDoll(tag, "WOMENACE_BarePose");
        if (doll == null)
            return $"no body for {tag}";
        doll.transform.localPosition = new Vector3(x, y, z);
        doll.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        foreach (var animator in doll.GetComponentsInChildren<Animator>())
            animator.enabled = false;
        ProbeLight(doll, d._ops);
        return $"{doll.name} stands bare at ({x}, {y}, {z})";
    }

    // The light a doll takes standing at a point in the active room: the set's sun and whom it
    // lights, and the nearest light probes with their distance, their share of the sun and
    // their flat ambient colour.
    internal static string ProbesNearFromDev(float x, float y, float z, int count)
    {
        var d = _instance;
        var set = d?._active;
        if (set?.Root == null)
            return "no room";
        var at = new Vector3(x, y + 0.9f, z);
        var rows = new List<string>
        {
            set.Sun == null ? "no sun" : $"sun intensity {set.Sun.intensity:F2} colour {set.Sun.color} lights characters {LightsCharacters(set.Sun)} room {LightsRoom(set.Sun)} towards {set.Root.transform.InverseTransformDirection(-set.Sun.transform.forward)}",
            $"{set.LightProbes.Length} probes, {set.LiveLights.Count} live lights",
            $"live shadow strength {Shader.GetGlobalFloat("_WmShadowStrength"):F2}, shadow light {(d._shadowLight == null ? "none" : $"intensity {d._shadowLight.intensity:F2} shadows {d._shadowLight.shadows} dimmer {d._shadowLight.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>()?.shadowDimmer:F2}")}",
        };
        foreach (var light in set.LiveLights)
            if (light != null)
                rows.Add($"  light {light.name} {light.type} at {set.Root.transform.InverseTransformPoint(light.transform.position)} intensity {light.intensity:F2} range {light.range:F1} characters {LightsCharacters(light)} room {LightsRoom(light)}");
        if (set == d._ops)
            foreach (var spot in DollSpots)
            {
                var chest = new Vector3(spot.Position.x, d.FloorAt(spot) + ChestHeight, spot.Position.z);
                var lamps = d.Lamps(chest);
                rows.Add($"  {spot.Name}: lamps reach {lamps.Reach:F1} (theirs from {LampsReachLevel}) from {lamps.From:F2} colour {lamps.Colour}");
            }
        foreach (var probe in set.LightProbes.OrderBy(p => (p.Position - at).sqrMagnitude).Take(count))
            rows.Add($"  probe at {probe.Position} {(probe.Position - at).magnitude:F2} m away, sun {probe.SunOcclusion:F2}, ambient ({probe.Sh[0].w:F3}, {probe.Sh[1].w:F3}, {probe.Sh[2].w:F3})");
        return string.Join("\n", rows);
    }

    // Where the room's dolls stand, in room space.
    internal static string DollsFromDev()
    {
        var d = _instance;
        if (d == null || d._room == null)
            return "no room";
        var rows = new List<string>();
        foreach (var doll in d._crew)
            if (doll != null)
                rows.Add($"{doll.name} at {d._room.transform.InverseTransformPoint(doll.transform.position)} yaw {doll.transform.localEulerAngles.y:F0} {(doll.activeInHierarchy ? "shown" : "hidden")}"
                    + (d._crewSeatShifts.TryGetValue(doll, out var shift) ? $", seated {shift.z:F2} forward and {shift.x:F2} right of her spot" : "")
                    + ", plays " + PlayingFromDev(doll));
        return string.Join("\n", rows);
    }

    // Which of the pose set's loops a doll's animator is in, and how far through it.
    private static string PlayingFromDev(GameObject doll)
    {
        var animator = doll.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            return "nothing";
        var info = animator.GetCurrentAnimatorStateInfo(0);
        foreach (var body in new[] { "alva", "asteria", "cheyanne", "helen", "jiangyu", "klukai", "koleda", "lenna", "leva", "lewis", "makiatto", "nemesis", "papasha", "robella", "sextans", "soppo", "springfield", "suomi", "vector", "voymastina", "welrod" })
            foreach (var pose in new[] { "Idle", "LongIdle", "Sit" })
                if (info.IsName($"{body}-{pose}"))
                    return $"{body}-{pose} at {info.normalizedTime % 1f:F2} (controller {animator.runtimeAnimatorController.name})";
        return $"an unknown state (controller {animator.runtimeAnimatorController.name})";
    }

    internal static string IntelFromDev(string state)
    {
        var d = _instance;
        if (d == null)
            return "no diorama";
        d.SetIntel(string.Equals(state, "on", StringComparison.OrdinalIgnoreCase));
        return $"intel {(d._intelOpen ? "open" : "closed")}, {d._intelMessages} messages";
    }

    // Dev: moves the Dinergate to a room-space point, for placing it.
    internal static string MoveDinergate(float x, float y, float z)
    {
        var d = _instance;
        if (d?._dinergate == null)
            return "no dinergate";
        d._dinergate.transform.localPosition = new Vector3(x, y, z);
        return $"dinergate at ({x}, {y}, {z})";
    }

    internal static void Respawn()
    {
        if (_instance == null)
            return;
        _instance.Remove();
        if (Enabled)
            _instance.Spawn();
    }

    // Points the room camera from one room-space point at another (mockups and framing work).
    internal static string AimCamera(float x, float y, float z, float tx, float ty, float tz, float fov)
    {
        var d = _instance;
        if (d?._camera == null || d._room == null)
            return "no room camera";
        var from = d._room.transform.TransformPoint(new Vector3(x, y, z));
        var to = d._room.transform.TransformPoint(new Vector3(tx, ty, tz));
        d._camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from, Vector3.up));
        var camera = d._camera.GetComponent<Camera>();
        if (fov > 0)
            camera.fieldOfView = fov;
        // The shot the room camera holds, or its director puts the camera back next frame.
        d._shot = (d._camera.transform.position, d._camera.transform.rotation, camera.fieldOfView, d._lensShift);
        d._shotTravel = 1f;
        return $"camera at ({x}, {y}, {z}) looking at ({tx}, {ty}, {tz}), fov {camera.fieldOfView}";
    }

    internal static string ResetCamera()
    {
        var d = _instance;
        if (d?._camera == null)
            return "no room camera";
        d.Frame(null, d._room.transform);
        return "camera reset";
    }

    // Simulates a click at a screen point (pixels, origin bottom left) through the real hit test.
    internal static string ClickAt(float x, float y)
    {
        if (_instance == null)
            return "no room";
        var camera = RoomCamera;
        var hit = _instance.TryClick(new Vector2(x, y));
        var screen = camera != null && _instance._dinergate != null
            ? camera.WorldToScreenPoint(_instance._dinergate.transform.position + Vector3.up * 0.3f) : Vector3.zero;
        return $"hit {hit}, dinergate centre on screen at ({screen.x:0}, {screen.y:0}) of {Screen.width}x{Screen.height}";
    }

    internal static RenderTexture CardsTexture => _instance?._cardsTexture;

    internal static RenderTexture MirrorTexture => _instance?._mirrorTexture;

    // Plays a pose on one of the room's dolls, for previewing the lobby loops.
    internal static string PoseDoll(int index, string pose)
    {
        var self = _instance;
        var doll = self == null ? null : index >= 0 && index < self._crew.Count ? self._crew[index] : null;
        if (doll == null)
            return "no doll " + index;
        self.Pose(doll, pose);
        return $"{doll.name} {pose}";
    }

    // Plays a named pose state (such as "lenna-Idle") on the room doll at a spot, to tell a bad
    // clip from a bad body: a clip that is wrong on every doll, or a doll wrong in every clip.
    internal static string PlayStateOn(string spot, string state)
    {
        var self = _instance;
        var doll = self?._crew.Find(d => d != null && d.name.Contains("_" + spot + ":"));
        var animator = doll != null ? doll.GetComponentInChildren<Animator>() : null;
        if (animator == null)
            return "no doll at " + spot;
        var hash = Animator.StringToHash("Base Layer." + state);
        if (!animator.HasState(0, hash))
            return "no state " + state;
        animator.Play(hash, 0, 0f);
        return $"{doll.name} plays {state}";
    }

    private readonly List<GameObject> _staged = new();

    // Stands one doll three times in a row at the stairs' foot, in her default outfit, each copy
    // held at the same moment of one lobby pose (Idle, LongIdle, Sit), with the room's
    // own dolls put away: one picture shows all her poses, and two builds can be compared.
    internal static string StagePosesFromDev(string name)
    {
        var d = _instance;
        if (d?._room == null)
            return "no room";
        foreach (var old in d._staged)
            if (old != null)
                UnityEngine.Object.Destroy(old);
        d._staged.Clear();
        d.HideRoomDolls();
        var tag = "wmgfl_" + name;
        var poses = new[] { "Idle", "LongIdle", "Sit" };
        var found = new List<string>();
        for (var i = 0; i < poses.Length; i++)
        {
            var doll = d.SpawnDoll(tag, "WOMENACE_Staged" + i);
            if (doll == null)
                return "no body for " + name;
            d._staged.Add(doll);
            doll.transform.localPosition = new Vector3(-8.7f - i * 0.85f, -1.0f, -2.99f);
            doll.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);
            doll.transform.localScale = Vector3.one * RoomDollScale;
            ProbeLight(doll, d._ops);
            var animator = doll.GetComponentInChildren<Animator>();
            if (d._poses == null)
                d._poses = d.PosesPrefab()?.GetComponent<Animator>()?.runtimeAnimatorController;
            if (animator == null || d._poses == null)
                continue;
            animator.runtimeAnimatorController = d._poses;
            animator.applyRootMotion = false;
            var body = doll.name.Substring(doll.name.IndexOf(':') + 1);
            var state = Animator.StringToHash($"Base Layer.{body}-{poses[i]}");
            if (!animator.HasState(0, state))
            {
                doll.SetActive(false);
                continue;
            }
            animator.Play(state, 0, 0.25f);
            animator.Update(0f);
            animator.speed = 0f;
            found.Add(poses[i]);
        }
        return $"{name}: {string.Join(" ", found)}";
    }

    // Seats one doll at a spot as the room would, in her default outfit, with the room's own
    // dolls put away, and says how far she was moved for her hips to be over the seat.
    internal static string SeatDollFromDev(string name, string spotName)
    {
        var d = _instance;
        if (d?._room == null)
            return "no room";
        var at = Array.FindIndex(DollSpots, s => s.Name == spotName);
        if (at < 0)
            return "no spot " + spotName;
        foreach (var old in d._staged)
            if (old != null)
                UnityEngine.Object.Destroy(old);
        d._staged.Clear();
        d.HideRoomDolls();
        var doll = d.SpawnDoll("wmgfl_" + name, "WOMENACE_Staged0");
        if (doll == null)
            return "no body for " + name;
        d._staged.Add(doll);
        d.Pose(doll, DollSpots[at].Pose);
        d.PlaceAt(doll, DollSpots[at]);
        var shift = d.SeatShift(doll);
        return $"{name} at {spotName}: seated {shift.z:F2} forward and {shift.x:F2} right of the spot";
    }

    // Mission select's camera round the planet put at a bearing (degrees from its start), a
    // tilt and a distance, with the distance the room allows there and the camera's measured
    // clearance from the room's surfaces.
    internal static string PlanetOrbitFromDev(float turn, float tilt, float distance)
    {
        var d = _instance;
        if (d?._room == null)
            return "no room";
        var start = PlanetOrbitStart();
        d._planetYawTarget = Mathf.Clamp(start.Yaw + turn, start.Yaw - PlanetOrbitTurnBack, start.Yaw + PlanetOrbitTurnOn);
        d._planetTiltTarget = Mathf.Clamp(tilt, PlanetOrbitTiltMin, PlanetOrbitTiltMax);
        d._planetDistanceTarget = Mathf.Clamp(distance, PlanetOrbitNearest, start.Distance);
        (d._planetYaw, d._planetTilt, d._planetDistance) = (d._planetYawTarget, d._planetTiltTarget, d._planetDistanceTarget);
        var allowed = d.ClearDistance(d._planetYaw, d._planetTilt, d._planetDistance);
        var eye = PlanetOrbitEye(d._planetYaw, d._planetTilt, allowed);
        d._planetShotFor = (float.NaN, float.NaN, float.NaN);
        return $"turn {d._planetYaw - start.Yaw:0} tilt {d._planetTilt:0} asked {d._planetDistance:0.00} allowed {allowed:0.00}, eye {eye}, clearance {d.RoomClearance(eye, 2f):0.00} m";
    }

    internal static string UiPickFromDev(float x, float y)
    {
        var d = _instance;
        if (d?._shellRoot?.panel == null)
            return "no shell";
        var at = UnityEngine.UIElements.RuntimePanelUtils.ScreenToPanel(d._shellRoot.panel, new Vector2(x * Screen.width, y * Screen.height));
        var names = new List<string>();
        for (var e = d._shellRoot.panel.Pick(at); e != null; e = e.parent)
            names.Add($"{e.GetIl2CppType().Name}#{e.name}{(e == d._shellRoot ? " (shell root)" : "")}");
        return names.Count == 0 ? "nothing" : string.Join(" < ", names);
    }

    // What the shell's panel picks at the middle and the four corners of each mission card's
    // click target: the target itself where a click reaches it, or what lies over it.
    internal static string MarkerPicksFromDev()
    {
        var d = _instance;
        if (d?._shellRoot?.panel == null)
            return "no shell";
        var lines = new List<string>();
        foreach (var marker in d._markers)
        {
            var box = marker.Target?.worldBound ?? default;
            var found = new List<string>();
            foreach (var (fx, fy) in new[] { (0.5f, 0.5f), (0.1f, 0.2f), (0.9f, 0.2f), (0.1f, 0.8f), (0.9f, 0.8f) })
            {
                var picked = d._shellRoot.panel.Pick(new Vector2(box.x + box.width * fx, box.y + box.height * fy));
                var chain = new List<string>();
                for (var e = picked; e != null && chain.Count < 4; e = e.parent)
                    chain.Add($"{e.GetIl2CppType().Name}#{e.name}");
                found.Add(picked == marker.Target ? "own" : string.Join("<", chain));
            }
            lines.Add($"{marker.Target?.name} {box.x:F0},{box.y:F0} {box.width:F0}x{box.height:F0}: {string.Join(" | ", found)}");
        }
        return string.Join("\n", lines);
    }

    // The moment mission select's planet is held at while it arrives, or null to let it run.
    internal static float? PlanetHeldAt;

    partial void HoldPlanetMoment(ref float moment)
    {
        if (PlanetHeldAt is { } held)
            moment = held;
    }

    // The mission cards' state: how many, the fade, and each card's place, whether it shows and
    // what its material is drawing with.
    internal static string MarkersFromDev()
    {
        var d = _instance;
        if (d == null)
            return "no diorama";
        var lines = new List<string> { $"planet {d._planetShape?.Id} built for {d._planetBuilt.Id} moment {d._planetMoment:F2} lay {d._planetLay:F2} video {(d._planetPlayer != null ? $"playing {d._planetPlayer.isPlaying} prepared {d._planetPlayer.isPrepared} frame {d._planetPlayer.frame}" : "none")} chibi {(d._chibi != null ? $"active {d._chibi.activeSelf} at {d._chibi.transform.localPosition} spot {d._chibiSpot} tall {d._chibiTall:F2} from her card {(d._selectedMarker is { } hers ? (System.Numerics.Vector3.Distance(d._chibiSpot, hers.ChibiSpot) * PlanetRadius).ToString("F4") : "none selected")} m, frame {d._chibi.GetComponent<MeshRenderer>()?.material?.mainTexture?.name}" : "none")}", $"{d._markers.Count} markers, fade {d._markerFade}, arrived {Time.unscaledTime - d._markersArrive:F1}s ago, cards texture {(d._cardsTexture != null ? $"{d._cardsTexture.width}x{d._cardsTexture.height} created {d._cardsTexture.IsCreated()}" : "none")}, slot {(d._cardsSlot != null ? $"panel {d._cardsSlot.panel != null} children {d._cardsSlot.childCount}" : "none")}, travel {d._shotTravel}" };
        foreach (var marker in d._markers)
        {
            var card = marker.Card;
            var renderer = card != null ? card.GetComponent<MeshRenderer>() : null;
            var material = renderer != null ? renderer.material : null;
            lines.Add($"  open {marker.Open:F2} selected {marker.Selected} box {marker.Box} width {marker.Width:F2} order {renderer?.sortingOrder} target {(marker.Target != null ? $"{marker.Target.resolvedStyle.display} {marker.Target.resolvedStyle.left:F0},{marker.Target.resolvedStyle.top:F0} {marker.Target.resolvedStyle.width:F0}x{marker.Target.resolvedStyle.height:F0}" : "none")} interactable {(marker.Poi?.panel != null ? marker.Poi.IsInteractable().ToString() : "no panel")}");
            lines.Add(card == null ? "card gone" : $"card at {d._room.transform.InverseTransformPoint(card.transform.position)} scale {card.transform.localScale} active {card.activeInHierarchy} renderer {renderer != null && renderer.enabled} intensity {material?.GetFloat("_Intensity")} reveal {material?.GetFloat("_Reveal")} plate {material?.GetFloat("_Plate")} texture {(material?.mainTexture != null ? material.mainTexture.name : "none")} uv {(material != null && material.HasProperty("_UvRect") ? material.GetVector("_UvRect").ToString() : "")} poi panel {marker.Poi?.panel != null}");
        }
        return string.Join("\n", lines);
    }

    // Saves an outfit as a doll's selection without the picker, which needs her panel under the
    // screen's own root. The squad shows it the next time it is spawned (select her again).
    internal static string WearFromDev(string characterTag, string armorId)
    {
        var d = _instance;
        var key = Affinity.KeyForTag(characterTag);
        if (d == null || key == 0)
            return "no such doll";
        d.Context.State.Get<TransmogState>().Selections[key] = armorId;
        return $"{characterTag} wears {Transmog.SelectionFor(d.Context, characterTag)}";
    }

    // The Squads camera's line from the squad outwards through the hall's map: at each step how
    // many of the five lines abreast meet a surface, and where the step is in the set's space.
    internal static string HallLineFromDev()
    {
        var d = _instance;
        if (d?._stage == null || d._hallSolid == null || d._camera == null)
            return "no hall map";
        var rotation = d._stage.rotation * Quaternion.Euler(d._orbitPitch, d._orbitYaw, 0f);
        var centre = d.SquadFocus(out _);
        var at = d._stage.InverseTransformPoint(centre);
        var back = d._stage.InverseTransformDirection(rotation * Vector3.back + rotation * Vector3.right * d.StagePan());
        var right = d._stage.InverseTransformDirection(rotation * Vector3.right) * HallAbreast;
        var up = d._stage.InverseTransformDirection(rotation * Vector3.up) * HallAbreast;
        var eye = d._stage.InverseTransformPoint(d._camera.transform.position);
        var lines = new List<string> { $"focus {at}, camera {eye} ({(eye - at).magnitude:F1} m out), wanted {d._orbitDistance:F1}, radius {d._framedRadius:F1}, pitch {d._orbitPitch:F0}, yaw {d._orbitYaw:F0}" };
        for (var dist = 2f; dist <= 24f; dist += 0.4f)
        {
            var on = at + back * dist;
            var marks = "";
            foreach (var beside in new[] { Vector3.zero, right, -right, up, -up })
                marks += d.HallSolidAt(on + beside) || d.HallSolidAt(on + beside + back * 0.2f) ? "#" : ".";
            if (marks != ".....")
                lines.Add($"{dist:F1} m {marks} at {on}");
        }
        return string.Join("\n", lines);
    }

    // Whether a point of the refitting room (in its own space) is on the camera's floor plan.
    internal static string HallFloorFromDev(float x, float z)
    {
        var d = _instance;
        if (d?._hallFloor == null)
            return "no floor plan";
        return d.OnHallFloor(new Vector3(x, 0f, z)) ? "on the floor" : "off the floor";
    }

    internal static string RespawnRoomDolls()
    {
        if (_instance == null)
            return "no diorama";
        _instance.SpawnRoomDolls();
        return $"{_instance._crew.Count} dolls in the room";
    }

    // Switches every doll's light rig on or off, for judging the room's own light on them.
    internal static string DollRigs(bool on)
    {
        var d = _instance;
        if (d == null)
            return "no room";
        var lights = 0;
        foreach (var rig in d._dollRigs.Values)
            foreach (var light in rig)
                if (light != null)
                {
                    light.enabled = on;
                    lights++;
                }
        return $"{lights} rig lights {(on ? "on" : "off")}";
    }

    // Dev: reads what each held hand carries afresh and says, part by part, how near its head
    // comes to it as the hand goes out.
    internal static string CarryProbeFromDev()
    {
        var lines = new List<string>();
        foreach (var (_, grip) in Grips)
        {
            if (grip.Carried is not { } carried || carried.Unit == null)
                continue;
            var centre = N(carried.Head.position) + N(carried.Unit.up) * (CarryHeadRise * carried.Scale);
            var (right, up, forward) = (Vec.Normalize(N(carried.Unit.right)), Vec.Normalize(N(carried.Unit.up)), Vec.Normalize(N(carried.Unit.forward)));
            var outward = right * carried.Way.X + up * carried.Way.Y + forward * carried.Way.Z;
            lines.Add($"{carried.Unit.parent?.name}: way {carried.Way.X:F2},{carried.Way.Y:F2},{carried.Way.Z:F2} (right, up, forward) head at {centre.X:F2},{centre.Y:F2},{centre.Z:F2} radius {CarryHeadRadius * carried.Scale:F3} scale {carried.Scale:F3} out now {carried.Out:F3}");
            for (var i = 0; i < carried.Socket.childCount; i++)
            {
                var model = carried.Socket.GetChild(i);
                var toModel = ToMat(model.worldToLocalMatrix);
                var toWorld = ToMat(model.localToWorldMatrix);
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled)
                        continue;
                    var corners = new List<Vec>();
                    var read = new List<string>();
                    ReadTriangles(renderer.name, renderer, toModel, corners, read);
                    if (corners.Count == 0)
                    {
                        lines.Add($"  {renderer.name}: {string.Join(", ", read)}");
                        continue;
                    }
                    var (low, high) = (new Vec(float.MaxValue), new Vec(float.MinValue));
                    foreach (var corner in corners)
                    {
                        var world = Vec.Transform(corner, toWorld);
                        (low, high) = (Vec.Min(low, world), Vec.Max(high, world));
                    }
                    var near = new List<string>();
                    for (var slid = 0f; slid <= 0.251f; slid += 0.05f)
                    {
                        // The hand is out by what it is now, so the ball starts that much nearer.
                        var at = centre - outward * (slid - carried.Out);
                        var nearest = float.MaxValue;
                        for (var c = 0; c + 2 < corners.Count; c += 3)
                            nearest = MathF.Min(nearest, Vec.Distance(NearestOnTriangle(at, Vec.Transform(corners[c], toWorld), Vec.Transform(corners[c + 1], toWorld), Vec.Transform(corners[c + 2], toWorld)), at));
                        near.Add($"{slid * 100f:F0}:{nearest * 100f:F0}");
                    }
                    var b = renderer.bounds;
                    lines.Add($"  {renderer.name}: {string.Join(", ", read)} spans {low.X:F2},{low.Y:F2},{low.Z:F2} to {high.X:F2},{high.Y:F2},{high.Z:F2} (renderer says {b.min.x:F2},{b.min.y:F2},{b.min.z:F2} to {b.max.x:F2},{b.max.y:F2},{b.max.z:F2}) head to it by cm out {string.Join(" ", near)}");
                }
            }
        }
        return lines.Count > 0 ? string.Join("\n", lines) : "no hand held";
    }

    // Dev: how far each held hand is out, and what was read of what is in it.
    internal static string CarryReportFromDev()
    {
        var lines = new List<string>();
        foreach (var (_, grip) in Grips.Concat(MissionGrips))
            if (grip.Carried is { } carried && carried.Unit != null)
                lines.Add($"{carried.Unit.parent?.name} {carried.Key}: {carried.Out * 100f:F0} of {carried.Wanted * 100f:F0} cm, {(carried.Until > 0f ? "settling" : "kept")} ({string.Join(", ", carried.Read)})");
        return lines.Count > 0 ? string.Join("; ", lines) : "no hand held";
    }

    internal static string CardHoverFromDev(int index, float x, float y)
    {
        var d = _instance;
        if (d == null || index < 0 || index >= d._markers.Count)
            return $"no card {index} of {d?._markers.Count ?? 0}";
        HoverPart(d._markers[index], x < 0f ? null : new Vector2(x, y));
        var part = d._markers[index].Part;
        return part != null ? $"over {part.GetIl2CppType().Name} #{part.name}" : "over no part with a tooltip";
    }

    // Dev: how the calibration doll sits in the lounge chair. Her feet's and her whole body's lowest
    // points against the floor, her place and lean, and her leg joints in the seat's frame.
    internal static string CalibrationFeetFromDev()
    {
        var d = _instance;
        var doll = d?._calibrationDoll;
        if (doll == null)
            return "no calibration doll";
        var room = d._weaponSet.Root.transform;
        var animator = doll.GetComponentInChildren<Animator>();
        string Joint(HumanBodyBones bone)
        {
            var t = animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;
            if (t == null)
                return $"{bone} none";
            // Along the seat's facing: x right, y up, z forward from the seat's middle.
            var at = Quaternion.Inverse(LoungeSeatFacing) * (room.InverseTransformPoint(t.position) - LoungeSeat);
            return $"{bone} {at.x:F3},{at.y:F3},{at.z:F3}";
        }
        var offset = Quaternion.Inverse(LoungeSeatFacing) * (doll.transform.localPosition - LoungeSeat);
        var joints = new[]
        {
            HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        };
        var (feet, whole) = d.LowestPoints(doll);
        return $"{doll.name}: feet lowest {feet.y:F3}, whole lowest {whole.y:F3}, floor {LoungeFloor:F3}\n"
            + $"offset from seat (seat frame) {offset.x:F3},{offset.y:F3},{offset.z:F3}, rotation {doll.transform.localEulerAngles}\n"
            + string.Join("\n", joints.Select(Joint));
    }
}
