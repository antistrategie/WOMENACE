using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Inspection of the strategy diorama, from the bridge while the strategy layer is up.
//
//   {"verb":"Diorama.Snap","args":["Z:/tmp/room.png",true],"mutate":true}  the screen, UI hidden
//   {"verb":"Diorama.DebugTerm","args":[3],"mutate":true}           the rooms' shadowmask alone
//   {"verb":"Diorama.Toggle","args":[false],"mutate":true}          remove the room (true respawns it)
//   {"verb":"Diorama.Cameras"}                                       every camera and its components
//   {"verb":"Diorama.Render","args":["Z:/tmp/room.png",1600,900],"mutate":true}  the room camera alone, no UI
[DevVerb]
public static class Diorama
{
    public static string HallPlan(float reach = 9f, float cx = 0f, float cz = 0f, float floor = 0f, float cell = 0.4f) => StrategyDiorama.HallPlanFromDev(reach, cx, cz, floor, cell);

    [MutatingVerb]
    public static string TopDown(float cx, float cz, float height, float halfHeight, float near) => StrategyDiorama.TopDownFromDev(cx, cz, height, halfHeight, near);

    [MutatingVerb]
    public static string PlayState(string spot, string state) => StrategyDiorama.PlayStateOn(spot, state);

    [MutatingVerb]
    public static string StagePoses(string name) => StrategyDiorama.StagePosesFromDev(name);

    // Writes the mission cards' texture, to see what the cards are given to draw.
    public static string SaveCards(string path) => SavePng(StrategyDiorama.CardsTexture, path, "no cards texture");

    public static string Markers() => StrategyDiorama.MarkersFromDev();

    public static string MarkerPicks() => StrategyDiorama.MarkerPicksFromDev();

    // Holds the mission select planet's arrival at a moment from 0 to 1. A negative moment lets
    // it run again.
    [MutatingVerb]
    public static string PlanetMoment(float moment)
    {
        StrategyDiorama.PlanetHeldAt = moment < 0f ? null : moment;
        return moment < 0f ? "the arrival runs" : $"the arrival is held at {moment:0.00}";
    }

    // What the shell's panel picks at a point of the screen (0 to 1 across and down), with its
    // ancestors.
    public static string UiPick(float x, float y) => StrategyDiorama.UiPickFromDev(x, y);

    [MutatingVerb]
    public static string PlanetOrbit(float turn = 0f, float tilt = 8.5f, float distance = 6.1f) => StrategyDiorama.PlanetOrbitFromDev(turn, tilt, distance);

    [MutatingVerb]
    public static string Wear(string characterTag, string armorId) => StrategyDiorama.WearFromDev(characterTag, armorId);

    [MutatingVerb]
    public static string SeatDoll(string name, string spot) => StrategyDiorama.SeatDollFromDev(name, spot);

    public static string HallFloor(float x, float z) => StrategyDiorama.HallFloorFromDev(x, z);

    public static string HallLine() => StrategyDiorama.HallLineFromDev();

    public static string Ears() => StrategyDiorama.EarsFromDev();

    public static string SceneRenderers(string path) => StrategyDiorama.SceneRenderersFromDev(path);

    public static string Orbit(float yaw, float pitch, float zoomNotches) => StrategyDiorama.NudgeOrbit(yaw, pitch, zoomNotches);

    // Renderers in the room whose name or material name contains a substring, with room-space
    // bounds (centre, size).
    public static string Find(string part)
    {
        var room = StrategyDiorama.Room;
        if (room == null)
            return "no room";
        var lines = new List<string>();
        foreach (var r in room.GetComponentsInChildren<Renderer>(true))
        {
            var material = r.sharedMaterial != null ? r.sharedMaterial.name : "";
            if (!r.name.Contains(part, StringComparison.OrdinalIgnoreCase) && !material.Contains(part, StringComparison.OrdinalIgnoreCase))
                continue;
            var b = r.bounds;
            lines.Add($"{r.name} ({material}): centre {room.transform.InverseTransformPoint(b.center)} size {b.size}");
            if (lines.Count >= 40)
                break;
        }
        return string.Join("\n", lines);
    }

    // Transforms in the room (dolls included) named exactly this, with room-space position.
    public static string Bone(string name)
    {
        var room = StrategyDiorama.Room;
        if (room == null)
            return "no room";
        var lines = new List<string>();
        foreach (var t in room.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            var root = t;
            while (root.parent != null && root.parent != room.transform)
                root = root.parent;
            lines.Add($"{root.name}: {room.transform.InverseTransformPoint(t.position)}");
        }
        return string.Join("\n", lines);
    }

    // Sends a click to the first element with this name on the active screen, for testing folds and
    // buttons headlessly.
    public static string UiClick(string name)
    {
        var root = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.m_Container?.rootVisualElement;
        var element = root != null ? Jiangyu.Game.Ui.UI.Find(root, Jiangyu.Game.Ui.UiSelector.Name(name)) : null;
        if (element == null)
            return "no element " + name;
        var click = UnityEngine.UIElements.ClickEvent.GetPooled();
        click.target = element.Cast<UnityEngine.UIElements.IEventHandler>();
        element.SendEvent(click);
        click.Dispose();
        // A UI Toolkit button acts on a press and release, not on a click event: what it does
        // when pressed is run directly.
        element.TryCast<UnityEngine.UIElements.Button>()?.clickable?.clicked?.Invoke();
        return "clicked " + name;
    }

    // Runs the game at a frame rate of its own, with vertical sync off, to see what only shows at a
    // player's rate. 0 gives the game's own back.
    [MutatingVerb]
    public static string FrameRate(int fps)
    {
        QualitySettings.vSyncCount = fps > 0 ? 0 : 1;
        Application.targetFrameRate = fps > 0 ? fps : -1;
        return $"target {Application.targetFrameRate} fps, vertical sync {QualitySettings.vSyncCount}";
    }

    // Saves the floor reflection's mirror render to a PNG at a Windows path.
    public static string SaveMirror(string path) => SavePng(StrategyDiorama.MirrorTexture, path, "no mirror");

    private static string SavePng(RenderTexture rt, string path, string missing)
    {
        if (rt == null)
            return missing;
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = active;
        System.IO.File.WriteAllBytes(path, (byte[])ImageConversion.EncodeToPNG(tex));
        UnityEngine.Object.Destroy(tex);
        return $"wrote {path} {rt.width}x{rt.height}";
    }

    // Each room effect particle system: active, playing, live particles, renderer mode, material.
    public static string Particles()
    {
        var room = StrategyDiorama.Room;
        if (room == null)
            return "no room";
        var lines = new List<string>();
        foreach (var ps in room.GetComponentsInChildren<ParticleSystem>(true))
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            lines.Add($"{ps.name}: active {ps.gameObject.activeInHierarchy} playing {ps.isPlaying} n {ps.particleCount} mode {r?.renderMode} mat {r?.sharedMaterial?.name} mesh {r?.mesh?.name} pos {room.transform.InverseTransformPoint(ps.transform.position)}");
        }
        return string.Join("\n", lines);
    }

    // Every volume in the scene with its layer, priority, weight and fog, for finding what else
    // reaches the room camera.
    public static string Volumes()
    {
        var lines = new List<string>();
        foreach (var v in UnityEngine.Object.FindObjectsOfType<UnityEngine.Rendering.Volume>(true))
        {
            var profile = v.sharedProfile;
            var fog = "";
            if (profile != null)
                foreach (var c in profile.components)
                    if (c != null)
                        fog += c.GetIl2CppType().Name + (c.active ? "" : "(off)") + " ";
            lines.Add($"{v.gameObject.name} layer={v.gameObject.layer} global={v.isGlobal} priority={v.priority} weight={v.weight} enabled={v.enabled && v.gameObject.activeInHierarchy}: {fog}");
        }
        return string.Join("\n", lines);
    }

    // The room camera's HDRP frame settings that decide post-processing, as the game's quality pass
    // leaves them.
    public static string CamInfo()
    {
        var camera = StrategyDiorama.RoomCamera;
        if (camera == null)
            return "no room camera";
        var hd = camera.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        if (hd == null)
            return "no hd data";
        var mask = hd.renderingPathCustomFrameSettingsOverrideMask;
        var settings = hd.renderingPathCustomFrameSettings;
        string Bit(UnityEngine.Rendering.HighDefinition.FrameSettingsField f) => $"{f}={(settings.IsEnabled(f) ? 1 : 0)}{(mask.mask[(uint)f] ? "*" : "")}";
        return $"custom={hd.customRenderingSettings} aa={hd.antialiasing} hdr={camera.allowHDR} "
            + string.Join(" ", new[] { Bit(UnityEngine.Rendering.HighDefinition.FrameSettingsField.Postprocess), Bit(UnityEngine.Rendering.HighDefinition.FrameSettingsField.Tonemapping), Bit(UnityEngine.Rendering.HighDefinition.FrameSettingsField.ColorGrading), Bit(UnityEngine.Rendering.HighDefinition.FrameSettingsField.Bloom), Bit(UnityEngine.Rendering.HighDefinition.FrameSettingsField.SSAO), Bit(UnityEngine.Rendering.HighDefinition.FrameSettingsField.AtmosphericScattering) });
    }

    // Shows or hides a room child by name (the effects, the Dinergate), for measuring its share of
    // the frame.
    [MutatingVerb]
    public static string Toggle(string name, bool on)
    {
        var room = StrategyDiorama.Room;
        var t = room != null ? room.transform.Find(name) : null;
        var go = t != null ? t.gameObject : GameObject.Find(name);
        if (go == null)
            return $"no {name}";
        go.SetActive(on);
        return $"{name} {(on ? "shown" : "hidden")}";
    }

    // Shows one shading term of the baked rooms as greyscale: 1 AO, 2 realtime shadow, 3 sun mask.
    [MutatingVerb]
    public static string DebugTerm(float term)
    {
        Shader.SetGlobalFloat("_WmDebugTerm", term);
        return $"debug term {term}";
    }

    // Dims the rooms' reflections where their baked light is dark: how far (0 to 1), and the baked
    // brightness at which a reflection is left whole.
    [MutatingVerb]
    public static string ReflectionShade(float strength, float whole)
    {
        Shader.SetGlobalVector("_WmReflectionShade", new Vector4(strength, whole, 0f, 0f));
        return $"reflection shade {strength}, whole at {whole}";
    }

    // What the game's music is doing: the track playing, the one waiting to, and how far in it is.
    public static string Music()
    {
        var audio = Il2CppMenace.Audio.AudioManager.Get();
        if (audio == null)
            return "no audio manager";
        var playing = audio.m_CurrentMusicInstance;
        // A track by the name of its sound, or by its ids when the bank has no such sound.
        string Id(Il2CppStem.ID id) => !id.IsValid() ? "none" : Il2CppStem.SoundManager.GetSound(id)?.name ?? $"{id.BankId}/{id.ItemId}";
        return $"current {Id(audio.m_CurrentMusicId)} scheduled {Id(audio.m_ScheduledMusicId)} previous {Id(audio.m_PreviousMusicId)} "
            + (playing == null ? "no instance" : $"playing {playing.Playing} paused {playing.Paused} at {playing.TimeSamples} volume {playing.Volume:F2} clip {playing.Clip?.name}");
    }

    // Sets a shader global float (_WmDollDebug and the like), for trying a value live.
    [MutatingVerb]
    public static string SetFloat(string name, float value)
    {
        var before = Shader.GetGlobalFloat(name);
        Shader.SetGlobalFloat(name, value);
        return $"{name} {before} -> {value}";
    }

    // Sets a shader global vector (the rooms' _Wm* inputs), for trying a value live.
    [MutatingVerb]
    public static string SetGlobal(string name, float x, float y, float z, float w)
    {
        var before = Shader.GetGlobalVector(name);
        Shader.SetGlobalVector(name, new Vector4(x, y, z, w));
        return $"{name} {before} -> {Shader.GetGlobalVector(name)}";
    }

    // Applies an anti-aliasing quality the way the options screen does, without saving it, and
    // reports what each camera ends up with.
    [MutatingVerb]
    public static string AntiAliasing(int quality)
    {
        var settings = Il2CppMenace.PlayerSettings.PersistentPlayerSettings.DisplaySettings;
        var before = settings.AntiAliasingQuality;
        settings.AntiAliasingQuality = quality;
        Il2CppMenace.PlayerSettings.PersistentPlayerSettings.DisplaySettings = settings;
        settings.ApplyToAllCameras();
        var lines = new List<string> { $"quality {before} -> {quality}" };
        foreach (var cam in UnityEngine.Camera.allCameras)
        {
            var hd = cam.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
            lines.Add($"{cam.name} aa={(hd != null ? hd.antialiasing.ToString() : "-")}"
                + (hd != null && hd.antialiasing == UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing ? $" smaa={hd.SMAAQuality}" : ""));
        }
        return string.Join("\n", lines);
    }

    [MutatingVerb]
    public static string Shadows(int quality) => StrategyDiorama.ShadowQuality(quality);

    [MutatingVerb]
    public static string DollRigs(bool on) => StrategyDiorama.DollRigs(on);

    public static string Pose(int index, string pose) => StrategyDiorama.PoseDoll(index, pose);

    public static string RoomDolls() => StrategyDiorama.RespawnRoomDolls();

    // A click at a screen point (pixels, bottom left origin) through the real hit test.
    public static string ClickAt(float x, float y) => StrategyDiorama.ClickAt(x, y);

    // Room camera from (x,y,z) looking at (tx,ty,tz) in room space. Fov 0 keeps the current one.
    public static string Camera(float x, float y, float z, float tx, float ty, float tz, float fov) => StrategyDiorama.AimCamera(x, y, z, tx, ty, tz, fov);

    public static string CameraReset() => StrategyDiorama.ResetCamera();

    // The navigation screen: what each destination maps to right now, and going to one
    // (Bridge, Armory, Hiring, Oci, Market, Workshop, Shop) as a click on it would.
    public static string NavState() => NavigationMenu.Describe() + "; open " + NavigationMenu.IsOpen;

    public static string NavGo(string target) => NavigationMenu.GoFromDev(target);


    public static string HomeView(string view) => StrategyDiorama.SetHomeViewFromDev(view);

    // The transforms under the first one in the room of this name: path, active, local position
    // and scale, components and material.
    public static string Tree(string name, int depth = 3)
    {
        var room = StrategyDiorama.Room;
        if (room == null)
            return "no room";
        Transform root = null;
        foreach (var t in room.GetComponentsInChildren<Transform>(true))
            if (t.name == name)
            {
                root = t;
                break;
            }
        if (root == null)
            return "none";
        var lines = new List<string>();
        void Walk(Transform t, int level)
        {
            var parts = new List<string>();
            foreach (var c in t.GetComponents<Component>())
                if (c != null && c.TryCast<Transform>() == null)
                    parts.Add(c.GetIl2CppType().Name);
            var renderer = t.GetComponent<Renderer>();
            var material = renderer != null && renderer.sharedMaterial != null ? " mat " + renderer.sharedMaterial.name : "";
            lines.Add($"{new string(' ', level * 2)}{t.name} {(t.gameObject.activeSelf ? "on" : "off")} pos {t.localPosition} rot {t.localEulerAngles} scale {t.localScale} [{string.Join(",", parts)}]{material}");
            if (level < depth)
                for (var i = 0; i < t.childCount; i++)
                    Walk(t.GetChild(i), level + 1);
        }
        Walk(root, 0);
        return string.Join("\n", lines);
    }

    // What vanilla's current background camera has under a screen pixel (x from the left, y from
    // the top): the collider a ray through it hits.
    public static string Pick(float x, float y)
    {
        var scene = Il2CppMenace.UI.UIManager.Get()?.GetCurrentBackgroundScene();
        var camera = scene != null ? scene.GetComponentInChildren<Camera>(true) : null;
        if (camera == null)
            return "no camera";
        var ray = camera.ScreenPointToRay(new Vector3(x, Screen.height - y, 0f));
        return Physics.Raycast(ray, out var hit, 1000f) ? $"{hit.collider.name} under {hit.collider.transform.parent?.name}" : "nothing";
    }

    // The picture each element of this name under the active screen carries: its sprite's or
    // texture's name and size, to tell vanilla's icons apart.
    public static string UiImages(string name)
    {
        var root = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.GetRootElement();
        if (root == null)
            return "no screen";
        var found = new List<string>();
        foreach (var e in Jiangyu.Game.Ui.UI.FindAll(root, Jiangyu.Game.Ui.UiSelector.Name(name)))
        {
            var image = e.resolvedStyle.backgroundImage;
            var what = image.sprite != null ? $"sprite {image.sprite.name} {image.sprite.rect.width}x{image.sprite.rect.height}"
                : image.texture != null ? $"texture {image.texture.name} {image.texture.width}x{image.texture.height}"
                : image.vectorImage != null ? "vector " + image.vectorImage.name : "none";
            found.Add($"{e.parent?.parent?.name}/{e.parent?.name}: {what} tint {e.resolvedStyle.unityBackgroundImageTintColor}");
        }
        return string.Join("\n", found.Take(40));
    }

    // Adds promotion points to the campaign, to see what the UI does for a doll who can be promoted.
    public static string PromotionPoints(int amount)
    {
        var state = Il2CppMenace.States.StrategyState.Get();
        if (state == null)
            return "no strategy state";
        state.ChangeVar(Il2CppMenace.Strategy.StrategyVars.PromotionPoints, amount, false, true, false);
        return "promotion points " + state.GetVar(Il2CppMenace.Strategy.StrategyVars.PromotionPoints);
    }

    // Shows or hides every element of this name under the active screen, to find which element
    // draws something. Says how many it changed and each one's type and parent.
    public static string UiShow(string name, bool on)
    {
        var root = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.GetRootElement();
        if (root == null)
            return "no screen";
        var found = new List<string>();
        foreach (var e in Jiangyu.Game.Ui.UI.FindAll(root, Jiangyu.Game.Ui.UiSelector.Name(name)))
        {
            e.style.visibility = on ? UnityEngine.UIElements.StyleKeyword.Null : UnityEngine.UIElements.Visibility.Hidden;
            found.Add($"{e.GetIl2CppType().Name} in {e.parent?.name}");
        }
        return $"{found.Count}: " + string.Join("; ", found.Take(12));
    }

    // The layout of a named element of the active screen and of everything under it to a depth.
    public static string Layout(string name, int depth = 2)
    {
        var root = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.GetRootElement();
        var element = root != null ? Jiangyu.Game.Ui.UI.Find(root, Jiangyu.Game.Ui.UiSelector.Name(name)) : null;
        if (element == null)
            return "not found";
        var lines = new List<string>();
        void Walk(UnityEngine.UIElements.VisualElement e, int level)
        {
            var r = e.resolvedStyle;
            lines.Add($"{new string(' ', level * 2)}{e.GetIl2CppType().Name}#{e.name} pos {r.position} l {r.left} t {r.top} r {r.right} b {r.bottom} w {r.width} h {r.height} m {r.marginLeft},{r.marginTop},{r.marginRight},{r.marginBottom} p {r.paddingLeft},{r.paddingTop},{r.paddingRight},{r.paddingBottom} dir {r.flexDirection} align {r.alignItems} self {r.alignSelf} grow {r.flexGrow} shrink {r.flexShrink} tr {r.translate} sc {r.scale.value} bw {r.borderLeftWidth} disp {r.display} vis {r.visibility} fs {r.fontSize} wrap {r.flexWrap} bg {r.backgroundColor} op {r.opacity} img {(r.backgroundImage.sprite != null ? r.backgroundImage.sprite.name : r.backgroundImage.texture != null ? r.backgroundImage.texture.name : "none")}");
            if (level < depth)
                for (var i = 0; i < e.childCount; i++)
                    Walk(e.ElementAt(i), level + 1);
        }
        Walk(element.parent ?? element, 0);
        return string.Join("\n", lines);
    }

    // The room's renderers at least this large in any direction, with room-space bounds: its
    // scenery cards and shells.
    public static string Big(float size)
    {
        var room = StrategyDiorama.Room;
        if (room == null)
            return "no room";
        var lines = new List<string>();
        foreach (var r in room.GetComponentsInChildren<Renderer>(true))
        {
            var b = r.bounds;
            if (Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) < size)
                continue;
            var path = r.name;
            for (var t = r.transform.parent; t != null && t != room.transform; t = t.parent)
                path = t.name + "/" + path;
            var filter = r.GetComponent<MeshFilter>();
            var mesh = filter != null && filter.sharedMesh != null ? $" mesh centre {filter.sharedMesh.bounds.center} size {filter.sharedMesh.bounds.size}" : "";
            lines.Add($"{path} ({(r.sharedMaterial != null ? $"{r.sharedMaterial.name} / {r.sharedMaterial.shader.name} queue {r.sharedMaterial.renderQueue}" : "none")}) {(r.gameObject.activeInHierarchy && r.enabled ? "on" : "off")}: centre {room.transform.InverseTransformPoint(b.center)} size {b.size} at {room.transform.InverseTransformPoint(r.transform.position)} rot {r.transform.eulerAngles} scale {r.transform.lossyScale}{mesh}");
        }
        return string.Join("\n", lines);
    }

    // What vanilla's current background scene is made of: its cameras and every renderer with its
    // type, mesh, material, shader, main texture and size.
    public static string Background(string part = "")
    {
        var scene = Il2CppMenace.UI.UIManager.Get()?.GetCurrentBackgroundScene();
        if (scene == null)
            return "no background scene";
        var lines = new List<string> { $"== {scene.name} {(scene.activeInHierarchy ? "on" : "off")}" };
        foreach (var camera in scene.GetComponentsInChildren<Camera>(true))
            lines.Add($"camera {camera.name} {(camera.orthographic ? "orthographic size " + camera.orthographicSize : "perspective fov " + camera.fieldOfView)} at {camera.transform.position} rot {camera.transform.eulerAngles}");
        foreach (var r in scene.GetComponentsInChildren<Renderer>(true))
        {
            if (part.Length > 0 && !r.name.Contains(part, StringComparison.OrdinalIgnoreCase)
                && !(r.sharedMaterial != null && r.sharedMaterial.name.Contains(part, StringComparison.OrdinalIgnoreCase)))
                continue;
            var filter = r.GetComponent<MeshFilter>();
            var mesh = filter != null && filter.sharedMesh != null ? $" mesh {filter.sharedMesh.name} ({filter.sharedMesh.vertexCount} verts)" : "";
            var material = r.sharedMaterial;
            var texture = material != null && material.HasProperty("_MainTex") && material.mainTexture != null ? " tex " + material.mainTexture.name : "";
            lines.Add($"{r.name} [{r.GetIl2CppType().Name}] {(r.gameObject.activeInHierarchy ? "on" : "off")}{mesh} mat {(material != null ? material.name + " / " + material.shader.name : "none")}{texture} at {r.transform.position} size {r.bounds.size}");
            if (lines.Count > 60)
                break;
        }
        return string.Join("\n", lines);
    }

    // Shows only the children of a room transform whose names are listed (comma separated), or
    // all of them for an empty list.
    public static string Only(string parent, string names)
    {
        var room = StrategyDiorama.Room;
        if (room == null)
            return "no room";
        var keep = names.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var shown = new List<string>();
        foreach (var t in room.GetComponentsInChildren<Transform>(true))
            if (t.parent != null && t.parent.name == parent)
            {
                var on = keep.Length == 0 || keep.Contains(t.name);
                t.gameObject.SetActive(on);
                if (on)
                    shown.Add(t.name);
            }
        return "showing " + string.Join(", ", shown);
    }

    // Every renderer under the room transforms of this name: its path from there, type, material,
    // whether it shows, its transform and its mesh's bounds.
    public static string Renderers(string under)
    {
        var room = StrategyDiorama.Room;
        if (room == null)
            return "no room";
        var lines = new List<string>();
        foreach (var root in room.GetComponentsInChildren<Transform>(true))
        {
            if (root.name != under)
                continue;
            lines.Add($"== {under} {(root.gameObject.activeInHierarchy ? "on" : "off")} at {room.transform.InverseTransformPoint(root.position)} scale {root.lossyScale}");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var path = r.name;
                for (var t = r.transform.parent; t != null && t != root; t = t.parent)
                    path = t.name + "/" + path;
                var filter = r.GetComponent<MeshFilter>();
                var mesh = filter != null && filter.sharedMesh != null ? $" mesh {filter.sharedMesh.name} centre {filter.sharedMesh.bounds.center} size {filter.sharedMesh.bounds.size}" : "";
                lines.Add($"{path} [{r.GetIl2CppType().Name}] {(r.gameObject.activeInHierarchy && r.enabled ? "on" : "off")} mat {(r.sharedMaterial != null ? $"{r.sharedMaterial.name} / {r.sharedMaterial.shader.name} queue {r.sharedMaterial.renderQueue}" : "none")} at {room.transform.InverseTransformPoint(r.transform.position)} rot {r.transform.eulerAngles} scale {r.transform.lossyScale}{mesh}");
            }
        }
        return string.Join("\n", lines);
    }

    [MutatingVerb]
    public static string RoomLook(string room, float ex, float ey, float ez, float tx, float ty, float tz, float fov) => StrategyDiorama.RoomLookFromDev(room, ex, ey, ez, tx, ty, tz, fov);

    // GFL2's capsule machine as a hologram over the holotable, while O.C.I. is showing: height in
    // metres (zero takes it away), metres along the table from its middle, and its turn.
    [MutatingVerb]
    public static string GachaProto(float height = 1.3f, float along = 0f, float yaw = 0f) => StrategyDiorama.ShowMachine(height, along, yaw);

    // Plays the capsule machine's pull (One or Ten), or holds it at a moment in seconds, with
    // GFL2's own camera for the pull unless told otherwise, and its effects for a rarity (white,
    // purple, golden, colorful).
    [MutatingVerb]
    public static string GachaPlay(string clip = "Ten", float at = -1f, bool camera = true, string rarity = "golden") => StrategyDiorama.PlayMachine(clip, at, camera, rarity);

    // O.C.I.'s camera sent round the ship without a mouse: bearing, distance.
    [MutatingVerb]
    public static string OciOrbit(float yaw, float distance) => StrategyDiorama.OciOrbitFromDev(yaw, distance);

    public static string Look(float ex, float ey, float ez, float tx, float ty, float tz, float fov) => StrategyDiorama.LookFromDev(ex, ey, ez, tx, ty, tz, fov);

    [MutatingVerb]
    public static string LensShift(float x, float y) => StrategyDiorama.LensShiftFromDev(x, y);

    [MutatingVerb]
    public static string Ortho(float size) => StrategyDiorama.OrthoFromDev(size);

    // Mission preparation's hologram map without a mouse: show vanilla's unseen map, point a
    // stand-in pointer at a screen place (pixels from the top left, negative for the real one),
    // press or release there, and read what vanilla makes of it.
    [MutatingVerb]
    public static string PrepFlatMap(bool shown) => StrategyDiorama.PrepFlatMapFromDev(shown);

    [MutatingVerb]
    public static string PrepPointer(float x, float y) => StrategyDiorama.PrepPointerFromDev(x, y);

    [MutatingVerb]
    public static string PrepPress(bool down, int squad = -1) => StrategyDiorama.PrepPressFromDev(down, squad);

    public static string PrepState() => StrategyDiorama.PrepStateFromDev();

    [MutatingVerb]
    public static string PrepDeployAll() => StrategyDiorama.PrepDeployAllFromDev();

    [MutatingVerb]
    public static string PrepChibis(bool shown) => StrategyDiorama.PrepChibisFromDev(shown);

    [MutatingVerb]
    public static string PrepOrbit(float yaw, float pitch, float distance) => StrategyDiorama.PrepOrbitFromDev(yaw, pitch, distance);

    [MutatingVerb]
    public static string PrepRebuild() => StrategyDiorama.PrepRebuildFromDev();

    [MutatingVerb]
    public static string MouseButton(bool down) => StrategyDiorama.MouseButtonFromDev(down);

    public static string StarProbe(int step = 20) => StrategyDiorama.StarProbeFromDev(step);

    public static string Dolls() => StrategyDiorama.DollsFromDev();

    [MutatingVerb]
    public static string Parts(string nameStart, bool show) => StrategyDiorama.PartsFromDev(nameStart, show);

    [MutatingVerb]
    public static string PartsSet(string nameStart, string property, float value) => StrategyDiorama.PartsSetFromDev(nameStart, property, value);

    [MutatingVerb]
    public static string SquadLight(float sun, bool lamps, bool rigs) => StrategyDiorama.SquadLightFromDev(sun, lamps, rigs);

    [MutatingVerb]
    public static string BarePose(string tag, float x, float y, float z, float yaw) => StrategyDiorama.BarePoseFromDev(tag, x, y, z, yaw);

    // What lights a doll standing at a point of the active room, in room space.
    public static string ProbesNear(float x, float y, float z, int count = 6) => StrategyDiorama.ProbesNearFromDev(x, y, z, count);

    public static string DinergateAt(float x, float y, float z) => StrategyDiorama.MoveDinergate(x, y, z);

    // Opens the briefing of an operation on offer, as a click on its marker on the system map does.
    public static string OpenOperation(int index, int hud = 0)
    {
        Il2CppMenace.UI.Strategy.OperationSelectUIScreen.SetSelectedOperationIdx(index, hud);
        Il2CppMenace.UI.UIManager.Get()?.OpenScreen("OperationSelectUIScreen", null, false, false);
        return "active screen now " + Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.GetIl2CppType().Name;
    }

    // Presses the first vanilla interactive element of this type name on the active screen (a
    // message in the list is an EventMessageElement).
    // The index picks among several: the second roster slot is ArmoryUnitSelectSlot 1.
    public static string PressType(string typeName, int index = 0)
    {
        var root = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.m_Container?.rootVisualElement;
        if (root == null)
            return "no screen";
        var found = Jiangyu.Game.Ui.UI.FindAll(root, Jiangyu.Game.Ui.UiSelector.TypeName(typeName)).ToList();
        var pressable = index >= 0 && index < found.Count ? found[index].TryCast<Il2CppMenace.UI.InteractiveElement>() : null;
        if (pressable == null)
            return $"no {typeName} {index} of {found.Count}";
        pressable.OnClicked(UnityEngine.UIElements.MouseButton.LeftMouse);
        return $"pressed {typeName} {index} of {found.Count}";
    }

    // Presses a button of the dialog that is open (YesButton, NoButton), as a click on it does.
    public static string DialogPress(string button = "YesButton")
    {
        var layers = Il2CppMenace.UI.UIManager.Get()?.GetPermanentLayersRoot();
        var element = layers != null ? Jiangyu.Game.Ui.UI.Find(layers, Jiangyu.Game.Ui.UiSelector.Name(button)) : null;
        var pressable = element?.TryCast<Il2CppMenace.UI.InteractiveElement>();
        if (pressable == null)
            return "no " + button;
        pressable.OnClicked(UnityEngine.UIElements.MouseButton.LeftMouse);
        return "pressed " + button;
    }

    // Closes whatever dialogs are open over the screen.
    public static string CloseDialogs() { Il2CppMenace.UI.UIManager.Get()?.CloseAllDialogs(); return "closed"; }

    // Opens (On) or closes (Off) the Intel board.
    public static string Intel(string state) => StrategyDiorama.IntelFromDev(state);

    public static string NavClose() { NavigationMenu.Close(); return "closed"; }

    // The active screen, the background prefab it asks for, the background the UI manager has
    // instantiated, and the enabled cameras.
    public static string Bg()
    {
        var manager = Il2CppMenace.UI.UIManager.Get();
        var screen = manager?.GetActiveScreen();
        string prefab;
        try { prefab = screen?.GetBackgroundScenePrefab()?.name ?? "null"; } catch (Exception ex) { prefab = "threw " + ex.GetType().Name; }
        var current = manager?.GetCurrentBackgroundScene();
        var cams = new List<string>();
        foreach (var cam in UnityEngine.Camera.allCameras)
            cams.Add($"{Path(cam.transform)}@{cam.depth}");
        return $"screen {screen?.GetIl2CppType().Name}; asks {prefab}; current {(current != null ? Path(current.transform) + (current.activeInHierarchy ? "" : " (inactive)") : "null")}; cameras [{string.Join(", ", cams)}]";
    }

    // The current vanilla background's hierarchy with each object's components. Video players
    // and renderers also list their output target and material textures.
    public static string BgTree()
    {
        var current = Il2CppMenace.UI.UIManager.Get()?.GetCurrentBackgroundScene();
        if (current == null)
            return "no background";
        var lines = new List<string>();
        foreach (var t in current.GetComponentsInChildren<Transform>(true))
        {
            var comps = new List<string>();
            foreach (var c in t.GetComponents<Component>())
            {
                var name = c.GetIl2CppType().Name;
                var video = c.TryCast<UnityEngine.Video.VideoPlayer>();
                if (video != null)
                    name += $"(mode {video.renderMode}, rt {video.targetTexture?.name} {video.targetTexture?.width}x{video.targetTexture?.height}, clip {video.clip?.name}, url {video.url}, mat {video.targetMaterialRenderer?.name}.{video.targetMaterialProperty})";
                var renderer = c.TryCast<Renderer>();
                if (renderer != null && renderer.sharedMaterial != null)
                    name += $"({renderer.sharedMaterial.shader?.name} main {renderer.sharedMaterial.mainTexture?.name})";
                var camera = c.TryCast<UnityEngine.Camera>();
                if (camera != null)
                    name += $"(mask {camera.cullingMask}, rt {camera.targetTexture?.name}, fov {camera.fieldOfView})";
                comps.Add(name);
            }
            lines.Add($"{Path(t)} active={t.gameObject.activeSelf} layer={t.gameObject.layer} [{string.Join(", ", comps)}]");
        }
        return string.Join("\n", lines);
    }

    // Room renderers whose screen-space bounds cover a viewport point (0..1, y up), nearest first,
    // with their material and shader, for identifying what a spot in a render is.
    public static string WhatsAt(float x, float y)
    {
        var room = StrategyDiorama.Room;
        var camera = StrategyDiorama.RoomCamera;
        if (room == null || camera == null)
            return "no room";
        var hits = new List<(float d, string s)>();
        foreach (var r in room.GetComponentsInChildren<MeshRenderer>())
        {
            var b = r.bounds;
            float minX = 9, minY = 9, maxX = -9, maxY = -9; var front = false;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var v = camera.WorldToViewportPoint(corner);
                if (v.z > 0) front = true;
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x); minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            }
            if (!front || x < minX || x > maxX || y < minY || y > maxY)
                continue;
            var d = Vector3.Distance(camera.transform.position, b.ClosestPoint(camera.transform.position));
            hits.Add((d, $"{d:0.0}m {r.name} mat {r.sharedMaterial?.name} shader {r.sharedMaterial?.shader?.name} centre {room.transform.InverseTransformPoint(b.center)} size {b.size}"));
        }
        return string.Join("\n", hits.OrderBy(h => h.d).Take(30).Select(h => h.s));
    }

    // Room renderers whose name contains a substring: enabled, visible, room-space bounds, material.
    public static string RendererState(string part)
    {
        var room = StrategyDiorama.Room;
        if (room == null)
            return "no room";
        var lines = new List<string>();
        foreach (var r in room.GetComponentsInChildren<Renderer>(true))
            if (r.name.Contains(part, StringComparison.OrdinalIgnoreCase))
                lines.Add($"{r.name} {r.GetIl2CppType().Name} enabled {r.enabled} active {r.gameObject.activeInHierarchy} visible {r.isVisible} centre {room.transform.InverseTransformPoint(r.bounds.center)} size {r.bounds.size} mat {r.sharedMaterial?.name} shader {r.sharedMaterial?.shader?.name} queue {r.sharedMaterial?.renderQueue} order {r.sortingOrder} sorting layer {r.sortingLayerID} priority {r.rendererPriority} layer {r.gameObject.layer}");
        return string.Join("\n", lines);
    }

    // Sets the draw order of every room renderer whose object name contains a substring: its
    // sorting order, and its materials' queue where one is given.
    [MutatingVerb]
    public static string DrawOrder(string part, int order, int queue = -1)
    {
        var room = StrategyDiorama.Room;
        if (room == null)
            return "no room";
        var n = 0;
        foreach (var r in room.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.name.Contains(part, StringComparison.OrdinalIgnoreCase))
                continue;
            r.sortingOrder = order;
            if (queue >= 0)
                foreach (var material in r.materials)
                    material.renderQueue = queue;
            n++;
        }
        return $"{n} renderers matching {part} at order {order}{(queue >= 0 ? $", queue {queue}" : "")}";
    }

    // Shows or hides every room renderer whose object name contains a substring.
    public static string Show(string part, bool on)
    {
        var room = StrategyDiorama.Room;
        if (room == null)
            return "no room";
        var n = 0;
        foreach (var r in room.GetComponentsInChildren<Renderer>(true))
            if (r.name.Contains(part, StringComparison.OrdinalIgnoreCase)) { r.enabled = on; n++; }
        return $"{(on ? "showing" : "hiding")} {n} renderers matching {part}";
    }

    public static string Toggle(bool on)
    {
        StrategyDiorama.Enabled = on;
        StrategyDiorama.Respawn();
        return on ? "room spawned" : "room removed";
    }

    // The room camera's lens as it is now: where it stands and looks, its field of view and shift.
    public static string Lens()
    {
        var camera = StrategyDiorama.RoomCamera;
        if (camera == null)
            return "no room camera";
        return $"pos={camera.transform.position:F3} forward={camera.transform.forward:F3} fov={camera.fieldOfView:F2} "
            + $"physical={camera.usePhysicalProperties} shift={camera.lensShift:F3} rect={camera.rect} pixel={camera.pixelRect}";
    }

    public static string Cameras()
    {
        var lines = new List<string>();
        foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>())
        {
            if (cam == null || !cam.gameObject.scene.IsValid())
                continue;
            var comps = new List<string>();
            foreach (var c in cam.GetComponents<Component>())
                comps.Add(c == null ? "null" : c.GetIl2CppType().Name);
            var volume = cam.GetComponentInChildren<UnityEngine.Rendering.Volume>(true);
            lines.Add($"{Path(cam.transform)} active={cam.isActiveAndEnabled} depth={cam.depth} clear={cam.clearFlags} "
                + $"mask={cam.cullingMask} target={(cam.targetTexture != null ? cam.targetTexture.name : "-")} "
                + $"pos={cam.transform.position} comps=[{string.Join(",", comps)}] volume={(volume != null ? Path(volume.transform) : "-")}");
        }
        return string.Join("\n", lines);
    }

    // Logs, for some seconds, every frame on which what can hear or what plays the music changes:
    // the listeners switched on, the audio's pause and volume, and the music's source.
    public static string AudioWatch(float seconds = 6f)
    {
        var context = StrategyDiorama.SystemContext;
        if (context == null)
            return "no context";
        context.Coroutines.Start(WatchAudio(context, seconds));
        return $"watching the audio for {seconds} s";
    }

    private static System.Collections.IEnumerator WatchAudio(ModContext context, float seconds)
    {
        var until = Time.unscaledTime + seconds;
        var last = "";
        while (Time.unscaledTime < until)
        {
            var ears = new List<string>();
            foreach (var listener in UnityEngine.Object.FindObjectsOfType<AudioListener>(true))
                ears.Add($"{listener.gameObject.name}:{(listener.isActiveAndEnabled ? "on" : "off")}");
            var source = Il2CppMenace.Audio.AudioManager.Get()?.m_CurrentMusicInstance?.Source;
            var now = $"listeners [{string.Join(", ", ears)}] paused {AudioListener.pause} volume {AudioListener.volume:F2} "
                + (source == null ? "no music source" : $"music playing {source.isPlaying} mute {source.mute} volume {source.volume:F2} active {source.isActiveAndEnabled}");
            if (now != last)
                context.Log.Info($"audio watch, frame {Time.frameCount} ({Time.unscaledDeltaTime * 1000f:F0} ms): {now}");
            last = now;
            yield return null;
        }
        context.Log.Info("audio watch: done");
    }

    // The full frame as the player sees it, UI included, written at the end of the next frame.
    // CaptureScreenshot fails here (missing interop method), CaptureScreenshotAsTexture works.
    public static string Snap(string path, bool hideUi = false)
    {
        var context = StrategyDiorama.SystemContext;
        if (context == null)
            return "no context";
        context.Coroutines.Start(SnapAtEndOfFrame(path, hideUi));
        return "capturing " + path;
    }

    // Takes every UI document off screen. Display, unlike visibility, is not inherited, so no
    // element under a hidden root can draw itself back in.
    internal static List<(UnityEngine.UIElements.VisualElement, UnityEngine.UIElements.StyleEnum<UnityEngine.UIElements.DisplayStyle>)> HideUi()
    {
        var hidden = new List<(UnityEngine.UIElements.VisualElement, UnityEngine.UIElements.StyleEnum<UnityEngine.UIElements.DisplayStyle>)>();
        foreach (var doc in UnityEngine.Object.FindObjectsOfType<UnityEngine.UIElements.UIDocument>())
        {
            var root = doc.rootVisualElement;
            if (root == null)
                continue;
            hidden.Add((root, root.style.display));
            root.style.display = UnityEngine.UIElements.DisplayStyle.None;
        }
        return hidden;
    }

    internal static void ShowUi(List<(UnityEngine.UIElements.VisualElement, UnityEngine.UIElements.StyleEnum<UnityEngine.UIElements.DisplayStyle>)> hidden)
    {
        foreach (var (root, display) in hidden)
            if (root != null)
                root.style.display = display;
    }

    private static System.Collections.IEnumerator SnapAtEndOfFrame(string path, bool hideUi)
    {
        var hidden = hideUi ? HideUi() : new List<(UnityEngine.UIElements.VisualElement, UnityEngine.UIElements.StyleEnum<UnityEngine.UIElements.DisplayStyle>)>();
        for (var i = 0; i < (hideUi ? 5 : 0); i++)
            yield return null;
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        ShowUi(hidden);
        if (tex == null)
            yield break;
        var png = ImageConversion.EncodeToPNG(tex);
        System.IO.File.WriteAllBytes(path, (byte[])png);
        UnityEngine.Object.Destroy(tex);
    }

    // Renders the room camera alone (no UI) into a PNG at a Windows path (Z:/ is the Linux root).
    public static string Render(string path, int width, int height)
    {
        var camera = StrategyDiorama.RoomCamera;
        if (camera == null)
            return "no room camera";
        var rt = new RenderTexture(width, height, 24);
        var previous = camera.targetTexture;
        camera.targetTexture = rt;
        camera.Render();
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = active;
        camera.targetTexture = previous;
        var png = ImageConversion.EncodeToPNG(tex);
        System.IO.File.WriteAllBytes(path, (byte[])png);
        UnityEngine.Object.Destroy(tex);
        rt.Release();
        UnityEngine.Object.Destroy(rt);
        return $"wrote {path} ({png.Length} bytes)";
    }

    // The distinct materials on the room, in a stable order.
    private static List<Material> RoomMaterials()
    {
        var mats = new List<Material>();
        var seen = new HashSet<IntPtr>();
        var room = StrategyDiorama.Room;
        if (room == null)
            return mats;
        foreach (var r in room.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m != null && seen.Add(m.Pointer))
                    mats.Add(m);
        return mats;
    }

    // Lists the room materials, or one material's shader properties and current values.
    public static string Materials(int index)
    {
        var mats = RoomMaterials();
        if (index < 0 || index >= mats.Count)
        {
            var names = new List<string>();
            for (int i = 0; i < mats.Count; i++)
                names.Add($"{i} {mats[i].name} [{mats[i].shader?.name}]");
            return string.Join("\n", names);
        }
        var m = mats[index];
        var sh = m.shader;
        var lines = new List<string> { $"{m.name} shader={sh?.name} keywords={string.Join(" ", m.shaderKeywords)} queue={m.renderQueue}" };
        for (int i = 0; sh != null && i < sh.GetPropertyCount(); i++)
        {
            var n = sh.GetPropertyName(i);
            var t = sh.GetPropertyType(i);
            var v = t switch
            {
                UnityEngine.Rendering.ShaderPropertyType.Texture => m.GetTexture(n)?.name ?? "null",
                UnityEngine.Rendering.ShaderPropertyType.Color => m.GetColor(n).ToString(),
                UnityEngine.Rendering.ShaderPropertyType.Vector => m.GetVector(n).ToString(),
                _ => m.GetFloat(n).ToString("0.###"),
            };
            lines.Add($"  {n} ({t}) = {v}");
        }
        return string.Join("\n", lines);
    }

    private static string Path(Transform t)
    {
        var parts = new List<string>();
        for (; t != null; t = t.parent)
            parts.Insert(0, t.name);
        return "/" + string.Join("/", parts);
    }
}
