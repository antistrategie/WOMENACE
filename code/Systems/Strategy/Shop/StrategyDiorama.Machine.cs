using UnityEngine;

namespace WOMENACE.Code;

// GFL2's capsule machine (the dorm playroom's clothes gacha) as a hologram over the holotable,
// for Procurement: the machine with its crank, drum and capsules, its two pull clips, GFL2's own
// camera for the pull, its effects and its sounds, all played on GFL2's timeline for the pull's
// rarity. A pull in Kalina's shop shows the ops room for as long as the machine runs
// (PlayPull), and the shop's own result cards follow.
public sealed partial class StrategyDiorama
{
    // The machine's height over the table for a pull, and the view the pull opens on before
    // GFL2's camera takes over: from the table's side, the machine filling the frame's height.
    private const float PullMachineHeight = 1.6f;
    private const float MachineShift = -0.2f;
    private static readonly Framing MachineFraming = new(new Vector3(-14.4f, 0.95f, 3.4f), new Vector3(-14.4f, 0.68f, 0f), 42f, MachineShift);
    // How long a pull runs, and how soon a click may cut it short.
    private const float PullSeconds = 8.7f;
    private const float PullSkipAfter = 0.6f;


    // Whether a pull is running at the machine, which shows the ops room behind Kalina's shop.
    internal static bool PullRunning => _instance?._shop.PullOn ?? false;

    // Runs a pull at the machine: one or ten, at a rarity (white, purple, golden, colorful), and
    // calls back when it is over or was cut short. False where there is no room or no machine
    // to run it at, and nothing was started.
    internal static bool PlayPull(bool ten, string rarity, Action done)
    {
        var d = _instance;
        if (d == null || !Enabled || d._shop.PullOn)
            return false;
        // Made here for the shop, it goes when the shop is done with it, as one DriveShop makes.
        if (d._shop.Machine == null && d._scene.Room != null && d._scene.Camera != null && d._scene.Active == d._scene.Ops)
        {
            ShowMachine(PullMachineHeight, 0f, 0f);
            d._shop.MachineForShop = d._shop.Machine != null;
        }
        if (d._shop.Machine == null)
            return false;
        d._shop.PullOn = true;
        d.SetPullBars(true);
        d._shop.PullDone = done;
        PlayMachine(ten ? "Ten" : "One", -1f, true, rarity);
        d._shop.PullRoutine = d.Context.Coroutines.Start(d.RunPull());
        return true;
    }

    // The skit on the machine's screen is GFL2's, with Voymastina in Mayling's part: she runs
    // on at the left and shoots a target at the right, a soldier for the common tiers and a gold
    // machine for the rare ones. Each is a Spine rig in GFL2, drawn here from a sheet of its
    // frames (scripts/doll/chibi_frames.py). A target stands where GFL2's prefab stands its
    // rig: its origin at a place in the machine's own space, turned as the screen leans, a rig
    // unit this many metres. Its one animation starts with the pull, which is what puts its
    // fall under the shot. A rig plays its beats: each is from when, its first frame in the
    // sheet, how many, over how long, and whether it goes round. A sheet's cell, origin and
    // pixels to a unit are what chibi_frames.py reports when it writes it:
    //   TargetMachineN1 and TargetMachineKing: --animation action --frames 50 --columns 10 --cell 200 --cell-width 300
    //   Voymastina: --animation move:12 wait:40 attack:14 hello:32 --columns 10
    private readonly record struct SkitBeat(float From, int First, int Frames, float Seconds, bool Loops);
    private readonly record struct SkitRig(string Asset, int Columns, int Rows, SkitBeat[] Beats, bool Runs,
        Vector2 Cell, Vector2 Origin, float PixelsToUnit, Vector3 At, Quaternion Turn, float Unit);
    private static readonly SkitBeat[] SkitTargetBeats = { new(0f, 0, 50, 5f, false) };
    private static readonly SkitRig SkitTarget = new("procurement__skit_target", 10, 5, SkitTargetBeats, false,
        new(300f, 200f), new(150f, 139f), 0.679f, new(-0.1026f, 0.6087f, 0.3201f), new(0f, 0.9898f, -0.1423f, 0f), 0.000502f);
    private static readonly SkitRig SkitTargetRare = new("procurement__skit_target_gold", 10, 5, SkitTargetBeats, false,
        new(300f, 200f), new(150f, 163.3f), 0.4071f, new(-0.0974f, 0.6127f, 0.3209f), new(0.0041f, 0.9906f, -0.1365f, -0.0025f), 0.000474f);
    // Voymastina's rig has no animation made for the skit, so hers is put together from the
    // ones every doll has, in one sheet: running, waiting, firing, waiting, and a wave at the
    // verdict. She stands on Mayling's line and is Mayling's height, comes on when and from
    // where Mayling does and stops where she stops, and fires under GFL2's shot effect.
    private const float SkitRunFrom = 0.75f;
    private const float SkitRunUntil = 1.9f;
    private const float SkitShooterStops = 0.132f;
    private static readonly SkitRig SkitShooter = new("procurement__skit_voymastina", 10, 10, new SkitBeat[]
        {
            new(0f, 0, 12, 0.6f, true),
            new(SkitRunUntil, 12, 40, 4f, true),
            new(2.2f, 52, 14, 0.667f, false),
            new(2.87f, 12, 40, 4f, true),
            new(5f, 66, 32, 2.667f, false),
            new(7.67f, 12, 40, 4f, true),
        }, true,
        new(240f, 160f), new(120f, 144.8f), 0.9007f, new(0.308f, 0.606f, 0.315f), new(0f, 0.9908f, -0.1355f, 0f), 0.0006f);

    // A rig's picture on the screen, and what it was last shown as, so a frame of the pull
    // writes only what has changed.
    private sealed class SkitPicture
    {
        public SkitRig Rig;
        public GameObject Picture;
        public Transform Transform;
        public Material Material;
        public Vector3 Stands;
        public int Frame = -1;
        public float Run = -1f;
        public bool Shown = true;
    }


    private void ClearSkit()
    {
        foreach (var picture in _shop.Skit)
        {
            UnityEngine.Object.Destroy(picture.Material);
            UnityEngine.Object.Destroy(picture.Picture);
        }
        _shop.Skit.Clear();
    }

    private void MakeSkit(string rarity)
    {
        ClearSkit();
        var body = _shop.Machine != null ? _shop.Machine.GetComponentInChildren<Animator>() : null;
        var template = MarkerTemplate();
        if (body == null || template == null)
            return;
        foreach (var rig in new[] { rarity is "golden" or "colorful" ? SkitTargetRare : SkitTarget, SkitShooter })
        {
            var sheet = Context.Assets.Load<Texture2D>(rig.Asset);
            if (sheet == null)
                continue;
            // Solid and in its own colours, where the machine about it is a hologram.
            var picture = Quad(template, "wm-skit", sheet);
            PlainHologram(picture, 1f);
            var material = picture.GetComponent<MeshRenderer>().material;
            material.SetFloat(PlateId, 1f);
            material.SetFloat(CutByAlphaId, 1f);
            // A frame is placed by the rig's origin in it, which is not its middle.
            var metres = rig.Unit / rig.PixelsToUnit;
            var stands = rig.At + rig.Turn * new Vector3((rig.Cell.x / 2f - rig.Origin.x) * metres, (rig.Origin.y - rig.Cell.y / 2f) * metres, 0f);
            picture.transform.SetParent(body.transform, false);
            picture.transform.localRotation = rig.Turn;
            picture.transform.localScale = new Vector3(rig.Cell.x * metres, rig.Cell.y * metres, 1f);
            picture.transform.localPosition = stands;
            _shop.Skit.Add(new SkitPicture { Rig = rig, Picture = picture, Transform = picture.transform, Material = material, Stands = stands });
        }
    }

    // The skit as it stands at a moment of the pull.
    private void PlaceSkit(float at)
    {
        at = Math.Max(at, 0f);
        foreach (var picture in _shop.Skit)
        {
            var rig = picture.Rig;
            var beat = rig.Beats[0];
            foreach (var next in rig.Beats)
                if (at >= next.From)
                    beat = next;
            var through = (int)((at - beat.From) / beat.Seconds * beat.Frames);
            var frame = beat.First + (beat.Loops ? through % beat.Frames : Math.Min(through, beat.Frames - 1));
            if (frame != picture.Frame)
            {
                picture.Frame = frame;
                picture.Material.SetVector(UvRectId, SheetCell(frame, rig.Columns, rig.Rows));
            }
            if (!rig.Runs)
                continue;
            // The screen's right is the machine's -x, so only her place across it changes.
            var run = Math.Clamp((at - SkitRunFrom) / (SkitRunUntil - SkitRunFrom), 0f, 1f);
            if (run != picture.Run)
            {
                picture.Run = run;
                var place = picture.Stands;
                place.x += (SkitShooterStops - rig.At.x) * run;
                picture.Transform.localPosition = place;
            }
            var shown = at >= SkitRunFrom;
            if (shown != picture.Shown)
                picture.Picture.SetActive(picture.Shown = shown);
        }
    }

    // The bars come in bare for a pull, as they do for a story event, and go back after it.
    private void SetPullBars(bool pulling)
    {
        if (_shell == null)
            return;
        MoveBars(_shell, pulling ? BarEventHeight : BarsOpen);
        _shell.EnableInClassList(BarsEventClass, pulling);
        _shell.parent?.EnableInClassList(BarsBareClass, pulling);
    }

    // The machine as it stands between pulls: still, its lights running and its screen's scene on.
    private void RestMachine()
    {
        var animator = _shop.Machine != null ? _shop.Machine.GetComponentInChildren<Animator>() : null;
        if (animator == null)
            return;
        ClearSkit();
        animator.speed = 1f;
        animator.Play("Rest", 0, 0f);
        foreach (var (name, effect) in _shop.MachineEffects)
            effect.SetActive(name is "body:Loop" or "body:BJ001");
    }

    private System.Collections.IEnumerator RunPull()
    {
        var started = Time.unscaledTime;
        while (_shop.PullOn && Time.unscaledTime - started < PullSeconds)
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (Time.unscaledTime - started > PullSkipAfter && mouse != null && mouse.leftButton.wasPressedThisFrame)
                break;
            yield return null;
        }
        _shop.PullRoutine = null;
        EndPull();
    }

    // The machine goes, the shop's screen has its own backing again, and the shop is told.
    private void EndPull()
    {
        if (!_shop.PullOn)
            return;
        if (_shop.PullRoutine != null)
            Context.Coroutines.Stop(_shop.PullRoutine);
        _shop.PullRoutine = null;
        _shop.PullOn = false;
        SetPullBars(false);
        if (_shop.MachineCameraRoutine != null)
            Context.Coroutines.Stop(_shop.MachineCameraRoutine);
        _shop.MachineCameraRoutine = null;
        StopMachineEffects();
        RestMachine();
        // The camera goes back to the shop's own view of the machine.
        _shotFraming = null;
        ApplyScreenPolicy();
        var done = _shop.PullDone;
        _shop.PullDone = null;
        done?.Invoke();
    }

    // A pull the scene ends under: the shop is still told it is over, so it shows itself again.
    // The bars and the camera go with the scene.
    private void AbandonPull()
    {
        if (!_shop.PullOn)
            return;
        if (_shop.PullRoutine != null)
            Context.Coroutines.Stop(_shop.PullRoutine);
        (_shop.PullRoutine, _shop.PullOn) = (null, false);
        var done = _shop.PullDone;
        _shop.PullDone = null;
        try { done?.Invoke(); }
        catch (Exception ex) { Context.Log.Warn($"strategy diorama: an abandoned pull's ending failed: {ex.GetType().Name}: {ex.Message}"); }
    }


    // The pull's effects stop being switched, and what it was playing stops with them: a pull
    // cut short takes its sounds with it.
    private void StopMachineEffects()
    {
        if (_shop.MachineEffectsRoutine != null)
            Context.Coroutines.Stop(_shop.MachineEffectsRoutine);
        _shop.MachineEffectsRoutine = null;
        foreach (var sound in _shop.MachineSounds)
            if (sound != null && sound.Playing)
                sound.Stop();
        _shop.MachineSounds.Clear();
    }

    // Stands the machine over the holotable, in the ship's place where O.C.I. is showing: its
    // height in metres (zero takes it away), how far along the table from its middle, and its turn.
    internal static string ShowMachine(float height, float along, float yaw)
    {
        var d = _instance;
        if (d?._scene.Room == null)
            return "no room";
        d.StopMachineCamera();
        foreach (var material in d._shop.MachineMaterials)
            UnityEngine.Object.Destroy(material);
        d._shop.MachineMaterials.Clear();
        if (d._shop.Machine != null)
            UnityEngine.Object.Destroy(d._shop.Machine);
        d._shop.Machine = null;
        d.StopMachineEffects();
        d._shop.MachineEffects.Clear();
        d.ClearSkit();
        if (height <= 0f)
        {
            if (d._oci.Ship != null)
                d._oci.Ship.SetActive(d._oci.On);
            return "machine away";
        }
        var prefab = d.Context.Assets.Load<GameObject>("environment/gacha_machine/main");
        if (prefab == null || d.HologramMaterial() == null)
            return "no machine prefab or hologram material";
        // The model is 1.94 m tall.
        const float modelHeight = 1.94f;
        var machine = UnityEngine.Object.Instantiate(prefab, d._scene.Room.transform, false);
        machine.name = "wm-machine";
        machine.transform.localPosition = new Vector3(OciShipFoot.x + along, OciShipFoot.y, OciShipFoot.z);
        machine.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        machine.transform.localScale = Vector3.one * (height / modelHeight);
        var foot = machine.transform.position.y;
        foreach (var renderer in machine.GetComponentsInChildren<Renderer>(true))
            d.WearHolograms(renderer, d._shop.MachineMaterials, (worn, _) =>
            {
                worn.SetFloat(HologramFloorId, foot);
                worn.SetFloat(HologramHeightId, height);
                worn.SetFloat(IntensityId, OciIntensity);
                worn.SetFloat("_ScanDensity", 60f);
                worn.SetFloat(HologramRevealId, 1.1f);
                // More of the machine's own pastel colours than a doll keeps, or it reads as white.
                worn.SetFloat("_OwnColour", 0.75f);
            });
        d._shop.Machine = machine;
        // GFL2's effects for the pull, each off until its moment: the pull's own and the
        // machine's in the machine's space, the capsule's on its first capsule, and the camera's
        // on a holder that goes where the pull's camera does.
        d._shop.MachineEffects.Clear();
        var body = machine.GetComponentInChildren<Animator>();
        if (body != null)
        {
            Transform Node(string name) => body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
            d._shop.MachineCameraNode = Node("Camera");
            d._shop.MachineCameraEffects = new GameObject("wm-machine-camera-effects").transform;
            d._shop.MachineCameraEffects.SetParent(body.transform, false);
            d.StageMachineEffects("environment/gacha_machine_vfx/main", body.transform, "fx");
            d.StageMachineEffects("environment/gacha_machine_vfx_body/main", body.transform, "body");
            d.StageMachineEffects("environment/gacha_machine_vfx_egg/main", Node("TwistEggA_01"), "egg");
            d.StageMachineEffects("environment/gacha_machine_vfx_egg_one/main", Node("TwistEggA_01"), "one");
            d.StageMachineEffects("environment/gacha_machine_vfx_camera/main", d._shop.MachineCameraEffects, "cam");
        }
        if (d._oci.Ship != null)
            d._oci.Ship.SetActive(false);
        return $"machine {height} m tall at {machine.transform.localPosition}, {d._shop.MachineMaterials.Count} materials, {d._shop.MachineEffects.Count} effects";
    }


    // One of the pull's effect prefabs under a part of the machine. Its effects are the children
    // of its VFX groups, kept by the prefab's tag and their name, with a 2 for the second group.
    private void StageMachineEffects(string asset, Transform under, string tag)
    {
        var prefab = Context.Assets.Load<GameObject>(asset);
        if (prefab == null || under == null)
            return;
        var staged = UnityEngine.Object.Instantiate(prefab, under, false);
        staged.transform.localPosition = Vector3.zero;
        staged.transform.localRotation = Quaternion.identity;
        foreach (var node in staged.GetComponentsInChildren<Transform>(true))
            if (node.parent != null && node.parent.name.StartsWith("VFX", StringComparison.Ordinal))
            {
                _shop.MachineEffects[$"{tag}{(node.parent.name == "VFX (2)" ? "2" : "")}:{node.name}"] = node.gameObject;
                node.gameObject.SetActive(false);
            }
    }

    // The camera's effects stand where the pull's camera does, at their own size: the camera's
    // node carries its lens's focal length as its scale, which they must not take.
    private void PlaceMachineCameraEffects()
    {
        if (_shop.MachineCameraNode == null || _shop.MachineCameraEffects == null)
            return;
        _shop.MachineCameraEffects.SetPositionAndRotation(_shop.MachineCameraNode.position, _shop.MachineCameraNode.rotation);
    }

    // GFL2's timeline for the pull (Ani_One and Ani_Ten in its bundle, a group of tracks for each
    // rarity): each effect's start and length in seconds, for a pull of one or ten and its
    // rarity (white, purple, golden, colorful). The first three are the timeline's own group of
    // tracks, shared by every pull.
    private static (string Effect, float Start, float Length)[] MachineSchedule(bool ten, string rarity)
    {
        var gold = rarity is "golden" or "colorful";
        // The capsule's effects are the ten pull's or the one pull's own, and the colourful
        // rarity's are each one's second group.
        var egg = (ten ? "egg" : "one") + (rarity == "colorful" ? "2" : "") + ":";
        var schedule = new List<(string, float, float)>
        {
            ("fx:Smoke", 0.33f, 4.85f),
            ("fx:Test", 0.42f, 0.52f),
            ("cam:BJ", 1.22f, 7.78f),
            ("body:BJ001", 0f, 9f),
            ("body:Loop", 0f, 9f),
            ("body:once", ten ? 0f : 1.42f, 3.58f),
            (gold ? "fx:shoot_jiguang_gold" : "fx:shoot_jiguang_nor", 2.27f, !gold ? 1f : ten ? 1.09f : 1.25f),
            (gold ? "fx:shoot" : "fx:shoot02", gold ? 2.57f : 2.6f, 1.2f),
            // Only the colourful rarity is called perfect.
            (rarity == "colorful" ? "body:T_Perfect" : "body:T_Great", 5f, 4f),
        };
        switch (rarity)
        {
            case "colorful":
                schedule.Add(ten ? ("fx:Ten_BOM_Colorful", 6.48f, 2.52f) : ("fx:One_BOM_Colorful", 6.7f, 2.3f));
                schedule.Add(ten ? ("cam:BJ_Colorful_Ten", 6.82f, 2.18f) : ("cam:BJ_Colorful_One", 6.43f, 2.57f));
                schedule.Add(ten ? (egg + "Ten_SG", 7.05f, 1.95f) : (egg + "SG", 6.62f, 2.38f));
                schedule.Add(ten ? (egg + "Ten_Colorful", 7.05f, 1.95f) : (egg + "Colorful", 7.75f, 1.25f));
                break;
            case "golden":
                schedule.Add(ten ? ("fx:Ten_BOM_Golden", 6.48f, 2.52f) : ("fx:One_BOM_Golden", 6.7f, 2.3f));
                schedule.Add(ten ? ("cam:BJ (2)", 6.82f, 2.18f) : ("cam:BJ (1)", 6.43f, 2.57f));
                schedule.Add(ten ? (egg + "Ten_SG", 7.05f, 1.95f) : (egg + "SG", 6.62f, 2.38f));
                schedule.Add(ten ? (egg + "Ten_Golden", 7.05f, 1.95f) : (egg + "Golden", 7.75f, 1.25f));
                break;
            case "purple":
                schedule.Add(ten ? ("fx:Ten_BOM", 6.6f, 2.4f) : ("fx:One_BOM", 6.58f, 2.42f));
                if (!ten)
                    schedule.Add((egg + "Sphere", 6.58f, 2.42f));
                schedule.Add(ten ? (egg + "Ten_VFX", 7.17f, 1.83f) : (egg + "VFX", 7.42f, 1.58f));
                break;
            default:
                schedule.Add(ten ? ("fx:Ten_BOM", 6.93f, 2.07f) : ("fx:One_BOM", 6.61f, 2.39f));
                break;
        }
        return schedule.ToArray();
    }

    // GFL2's sound cues for the pull, from its timeline's audio tracks: the sound and its moment.
    private static (string Sound, float At)[] MachineSounds(bool ten, string rarity)
    {
        var cues = new List<(string, float)> { (ten ? "machine_ten_01" : "machine_one_01", 0.02f), (ten ? "machine_ten_02" : "machine_one_02", 6.48f) };
        if (rarity is "golden" or "colorful")
            cues.AddRange(new[] { ("machine_golden_01", 5f), ("machine_golden_02", ten ? 7.05f : 7.75f) });
        else if (rarity == "purple")
            cues.Add(("machine_white_02", ten ? 7.17f : 7.42f));
        return cues.ToArray();
    }

    // Shows the effects as they stand at a moment of the pull, each simulated up to it.
    private void HoldMachineEffects((string Effect, float Start, float Length)[] schedule, float at)
    {
        PlaceMachineCameraEffects();
        PlaceSkit(at);
        foreach (var effect in _shop.MachineEffects.Values)
            effect.SetActive(false);
        foreach (var (name, start, length) in schedule)
        {
            if (!_shop.MachineEffects.TryGetValue(name, out var effect) || at < start || at > start + length)
                continue;
            effect.SetActive(true);
            foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Simulate(at - start, false, true);
                particles.Pause(false);
            }
        }
    }

    // Switches each effect on at its start and off after its length, as the pull plays.
    private System.Collections.IEnumerator PlayMachineEffects((string Effect, float Start, float Length)[] schedule, (string Sound, float At)[] sounds)
    {
        var bank = SoundIds.FromName(Procurement.SoundBankId);
        var heard = new bool[sounds.Length];
        foreach (var effect in _shop.MachineEffects.Values)
            effect.SetActive(false);
        var started = Time.unscaledTime;
        var shown = new bool[schedule.Length];
        while (_shop.Machine != null && Time.unscaledTime - started < 10f)
        {
            var at = Time.unscaledTime - started;
            PlaceMachineCameraEffects();
            PlaceSkit(at);
            for (var i = 0; i < sounds.Length; i++)
                if (!heard[i] && at >= sounds[i].At)
                {
                    heard[i] = true;
                    if (new Il2CppStem.ID(bank, SoundIds.FromName(sounds[i].Sound)).Play(1f, 1f) is { } playing)
                        _shop.MachineSounds.Add(playing);
                }
            for (var i = 0; i < schedule.Length; i++)
            {
                var on = at >= schedule[i].Start && at <= schedule[i].Start + schedule[i].Length;
                if (on != shown[i] && _shop.MachineEffects.TryGetValue(schedule[i].Effect, out var effect))
                    effect.SetActive(shown[i] = on);
            }
            yield return null;
        }
        _shop.MachineEffectsRoutine = null;
    }

    // Plays one of the machine's pull clips (One or Ten) from its start, or holds it at a moment
    // of the clip, in seconds, for a still. With the camera, the room camera goes to GFL2's own
    // for the pull and follows it.
    internal static string PlayMachine(string clip, float at, bool camera, string rarity)
    {
        var d = _instance;
        var animator = d?._shop.Machine != null ? d._shop.Machine.GetComponentInChildren<Animator>() : null;
        if (animator == null)
            return "no machine, or it has no animator";
        const float clipSeconds = 8.15f;
        animator.speed = at < 0f ? 1f : 0f;
        animator.Play(clip, 0, at < 0f ? 0f : Mathf.Clamp01(at / clipSeconds));
        animator.Update(0f);
        d.StopMachineEffects();
        var schedule = MachineSchedule(clip == "Ten", rarity);
        d.MakeSkit(rarity);
        if (at < 0f)
            d._shop.MachineEffectsRoutine = d.Context.Coroutines.Start(d.PlayMachineEffects(schedule, MachineSounds(clip == "Ten", rarity)));
        else
            d.HoldMachineEffects(schedule, at);
        d.StopMachineCamera();
        if (camera)
            d._shop.MachineCameraRoutine = d.Context.Coroutines.Start(d.FollowMachineCamera(at < 0f ? clipSeconds + 2f : float.PositiveInfinity));
        return (at < 0f ? $"playing {clip}" : $"{clip} held at {at} s") + $", {rarity}" + (camera ? ", with its camera" : "");
    }

    // The room camera is its screen's own again.
    private void StopMachineCamera()
    {
        if (_shop.MachineCameraRoutine == null)
            return;
        Context.Coroutines.Stop(_shop.MachineCameraRoutine);
        _shop.MachineCameraRoutine = null;
        LeaveMachineCamera();
    }

    // O.C.I. takes its own view back. A pull's view goes with the pull.
    private void LeaveMachineCamera()
    {
        if (!_oci.On)
            return;
        _shotLift = OciFraming.Lift;
        _oci.ShotFor = (float.NaN, float.NaN);
    }

    // The room camera eases to the machine's Camera node and then stays on it: the node stands
    // and looks where GFL2's camera does, and its scale along x is the focal length GFL2's clip
    // gives its lens, in millimetres over a sensor this high.
    private const float MachineSensorHeight = 24f;
    // GFL2's camera holds the machine's screen below the middle of its picture. The lens is
    // lifted by this much of the picture's height, which puts the screen in the middle of the
    // band the bars leave.
    private const float PullLift = -0.064f;

    private System.Collections.IEnumerator FollowMachineCamera(float seconds)
    {
        var node = _shop.MachineCameraNode;
        var up = _scene.Room.transform.up;
        var (from, lift, started) = (_shot, _shotLift, Time.unscaledTime);
        while (node != null && _shop.Machine != null)
        {
            var since = Time.unscaledTime - started;
            if (since >= seconds)
                break;
            var k = Ease.InOutCubic(Math.Clamp(since / 0.9f, 0f, 1f));
            var facing = Quaternion.LookRotation(node.forward, up);
            var fov = 2f * MathF.Atan(MachineSensorHeight / 2f / Math.Abs(node.localScale.x)) * (180f / MathF.PI);
            // Once it has arrived it is the node's own view, with nothing left to ease.
            _shot = k >= 1f
                ? (node.position, facing, fov, 0f)
                : (Vector3.Lerp(from.Position, node.position, k), Quaternion.Slerp(from.Rotation, facing, k), from.Fov + (fov - from.Fov) * k, from.Shift * (1f - k));
            _shotLift = lift + (PullLift - lift) * k;
            yield return null;
        }
        _shop.MachineCameraRoutine = null;
        LeaveMachineCamera();
    }
}
