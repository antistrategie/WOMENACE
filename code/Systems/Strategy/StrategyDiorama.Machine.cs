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
    // Kalina's shop on Buy and Sell: the table from its side, drawn to the right of the list.
    private static readonly Framing ShopFraming = new(new Vector3(-14.4f, 1.64f, 4.44f), new Vector3(-14.4f, 0.45f, 0f), 42f, MachineShift, 0.11f);
    private const string ShopClass = "wm-kalina";
    private const string ShopProcurementClass = "wm-kalina--procurement";
    // How long a pull runs, and how soon a click may cut it short.
    private const float PullSeconds = 8.7f;
    private const float PullSkipAfter = 0.6f;

    private bool _pullOn, _shopProcurement, _machineForShop;
    // The holotable is the shop's basket. Every item put in it stands on the table as a
    // hologram of its picture, on a ring the table projects it from, with how many of it are
    // wanted at its shoulder. A few of them stand one behind another as well, which tells one
    // from several at a glance and no more than that. The cards are in the part of the table
    // seen between the shop's panel and Kalina, in rows that start at the front, and are
    // smaller the more there are. The shop looks across the table from low down, so a card
    // hides the table for some way behind it: a row stands far enough behind the one in front
    // for most of it to be seen over it, and to one side of it. They are lit more than the ship
    // is, being small pictures seen past two panels. The basket is looked at every few frames.
    private static readonly Vector3 ShopCartAt = new(-13.92f, -0.12f, 0.5f);
    private const float ShopCartWidth = 1.45f;
    private const float ShopCartDepth = 1.15f;
    // How many of its own heights a card must be behind another to be mostly seen over it.
    private const float ShopCardClearance = 2.2f;
    private const float ShopCardLargest = 0.5f;
    private const int ShopCardsInRow = 4;
    private const int ShopCardsDeep = 5;
    private const float ShopCardIntensity = 2.8f;
    private const float ShopRingIntensity = 1.4f;
    private const int ShopCardFrames = 10;
    // The counts are labels in one panel of their own, drawn into one texture: a grid of cells,
    // each count's quad showing its own cell.
    private const int ShopCountColumns = 8;
    private const int ShopCountRows = 6;
    private const int ShopCountCellWidth = 128;
    private const int ShopCountCellHeight = 64;
    private const float ShopCountPlate = 0.8f;
    private readonly List<GameObject> _shopCards = new();
    private readonly List<Material> _shopCardMaterials = new();
    private readonly List<(Il2CppMenace.Items.BaseItemTemplate Template, int Count)> _shopBasket = new();
    private readonly List<UnityEngine.UIElements.Label> _shopCounts = new();
    private RenderTexture _shopCountTexture;
    private UnityEngine.UIElements.PanelSettings _shopCountSettings;
    private GameObject _shopCountHost;
    private long _shopCartFor;
    private long _shopCartCounts;
    // The cards of an item new to the basket rise from the table. Each is its material and
    // when it started.
    private const float ShopCardRise = 0.35f;
    private readonly List<(Material Material, float Started)> _shopArriving = new();
    private readonly HashSet<IntPtr> _shopStanding = new();

    // The panel the counts are drawn by, made the first time a basket is shown.
    private bool ShopCountPanel()
    {
        if (_shopCountHost != null)
            return true;
        if (!TexturePanel("WOMENACE_ShopCounts", _shellRoot, ShopCountColumns * ShopCountCellWidth, ShopCountRows * ShopCountCellHeight, 1f,
            out _shopCountHost, out _shopCountSettings, out _shopCountTexture, out var root))
            return false;
        for (var i = 0; i < ShopCountColumns * ShopCountRows; i++)
        {
            var label = new UnityEngine.UIElements.Label { pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
            var style = label.style;
            style.position = UnityEngine.UIElements.Position.Absolute;
            style.left = i % ShopCountColumns * ShopCountCellWidth;
            style.top = i / ShopCountColumns * ShopCountCellHeight;
            style.width = ShopCountCellWidth;
            style.height = ShopCountCellHeight;
            style.fontSize = 44;
            style.color = Color.white;
            // What a cell draws is what its quad darkens the room behind, so a count is read
            // over whatever stands behind it.
            style.backgroundColor = Color.black;
            style.unityFontStyleAndWeight = FontStyle.Bold;
            style.unityTextAlign = TextAnchor.MiddleCenter;
            style.marginLeft = style.marginRight = style.marginTop = style.marginBottom = 0;
            style.paddingLeft = style.paddingRight = style.paddingTop = style.paddingBottom = 0;
            root.Add(label);
            _shopCounts.Add(label);
        }
        return true;
    }

    // The basket's holograms go with the room, and the panel their counts are drawn by with them.
    private void ClearShopCart()
    {
        ShowShopCart(false);
        foreach (var made in new UnityEngine.Object[] { _shopCountHost, _shopCountSettings, _shopCountTexture })
            if (made != null)
                UnityEngine.Object.Destroy(made);
        (_shopCountHost, _shopCountSettings, _shopCountTexture) = (null, null, null);
        _shopCounts.Clear();
    }
    private Action _pullDone;
    private object _pullRoutine;

    // Whether a pull is running at the machine, which shows the ops room behind Kalina's shop.
    internal static bool PullRunning => _instance?._pullOn ?? false;

    // Runs a pull at the machine: one or ten, at a rarity (white, purple, golden, colorful), and
    // calls back when it is over or was cut short. False where there is no room or no machine
    // to run it at, and nothing was started.
    internal static bool PlayPull(bool ten, string rarity, Action done)
    {
        var d = _instance;
        if (d == null || !Enabled || d._pullOn)
            return false;
        if (d._machine == null && d._room != null && d._camera != null && d._active == d._ops)
            ShowMachine(PullMachineHeight, 0f, 0f);
        if (d._machine == null)
            return false;
        d._pullOn = true;
        d.SetPullBars(true);
        d._pullDone = done;
        PlayMachine(ten ? "Ten" : "One", -1f, true, rarity);
        d._pullRoutine = d.Context.Coroutines.Start(d.RunPull());
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

    private readonly List<SkitPicture> _skit = new();

    private void ClearSkit()
    {
        foreach (var picture in _skit)
        {
            UnityEngine.Object.Destroy(picture.Material);
            UnityEngine.Object.Destroy(picture.Picture);
        }
        _skit.Clear();
    }

    private void MakeSkit(string rarity)
    {
        ClearSkit();
        var body = _machine != null ? _machine.GetComponentInChildren<Animator>() : null;
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
            _skit.Add(new SkitPicture { Rig = rig, Picture = picture, Transform = picture.transform, Material = material, Stands = stands });
        }
    }

    // The skit as it stands at a moment of the pull.
    private void PlaceSkit(float at)
    {
        at = Math.Max(at, 0f);
        foreach (var picture in _skit)
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
        MoveBars(_shell, pulling ? BarEventHeight : BarHeight);
        _shell.EnableInClassList(BarsEventClass, pulling);
        _shell.parent?.EnableInClassList(BarsEventClass, pulling);
    }

    // The machine as it stands between pulls: still, its lights running and its screen's scene on.
    private void RestMachine()
    {
        var animator = _machine != null ? _machine.GetComponentInChildren<Animator>() : null;
        if (animator == null)
            return;
        ClearSkit();
        animator.speed = 1f;
        animator.Play("Rest", 0, 0f);
        foreach (var (name, effect) in _machineEffects)
            effect.SetActive(name is "body:Loop" or "body:BJ001");
    }

    // Kalina's shop in the ops room: the machine stands on the table while Procurement is open,
    // and the camera's view follows the shop's own tabs, which change without a screen change.
    private void DriveShop()
    {
        var open = _policyScreen == VanillaUi.Screens.Workshop && ShopSystem.IsShopOpen;
        var procurement = open && ShopSystem.IsProcurementOpen;
        if (procurement != _shopProcurement)
        {
            _shopProcurement = procurement;
            if (open)
            {
                ApplyScreenPolicy();
                ApplyHome(_homeScreen, _homeRoomShown);
            }
        }
        // Nothing is asked of the clock while there is no shop open and no basket standing.
        if ((open || _shopCartFor != 0) && UnityEngine.Time.frameCount % ShopCardFrames == 0)
            ShowShopCart(open && !procurement && _room != null && _active == _ops);
        if (_shopArriving.Count > 0)
            RaiseShopCards();
        if (_pullOn)
            return;
        var wanted = procurement && _room != null && _camera != null && _active == _ops;
        if (wanted && _machine == null)
        {
            ShowMachine(PullMachineHeight, 0f, 0f);
            _machineForShop = _machine != null;
            RestMachine();
        }
        else if (!wanted && _machine != null && _machineForShop)
        {
            ShowMachine(0f, 0f, 0f);
            _machineForShop = false;
        }
    }

    // Stands the basket on the table, or clears it. Nothing is touched while the basket is as
    // it was last shown.
    private void ShowShopCart(bool shown)
    {
        _shopBasket.Clear();
        if (shown)
            ShopSystem.Basket(_shopBasket);
        // What stands on the table follows which items are in the basket and how many of each
        // are stacked. The number at a card's shoulder is a label, so a count that changes
        // nothing else is written to its label and nothing is built.
        var (basket, counts) = (0L, 0L);
        foreach (var (template, count) in _shopBasket)
        {
            basket = unchecked(basket * 31 + template.Pointer.ToInt64() * 7 + Math.Min(count, ShopCardsDeep));
            counts = unchecked(counts * 31 + count);
        }
        if (basket == _shopCartFor)
        {
            if (counts != _shopCartCounts)
                for (var i = 0; i < _shopBasket.Count && i < _shopCounts.Count; i++)
                    _shopCounts[i].text = ShopCount(_shopBasket[i].Count);
            _shopCartCounts = counts;
            return;
        }
        (_shopCartFor, _shopCartCounts) = (basket, counts);
        _shopArriving.Clear();
        foreach (var card in _shopCards)
            UnityEngine.Object.Destroy(card);
        foreach (var material in _shopCardMaterials)
            UnityEngine.Object.Destroy(material);
        _shopCards.Clear();
        _shopCardMaterials.Clear();
        var marker = MarkerTemplate();
        if (_shopBasket.Count == 0 || HologramMaterial() == null || _room == null || marker == null)
        {
            _shopStanding.Clear();
            return;
        }
        var counted = ShopCountPanel();
        var rows = (_shopBasket.Count + ShopCardsInRow - 1) / ShopCardsInRow;
        var inRow = (_shopBasket.Count + rows - 1) / rows;
        var size = Mathf.Min(ShopCardLargest, ShopCartWidth / inRow * 0.8f);
        if (rows > 1)
            size = Mathf.Min(size, ShopCartDepth / (rows - 1) / ShopCardClearance);
        var between = rows > 1 ? ShopCartDepth / (rows - 1) : 0f;
        // The cards face the shop's view of the table. A ring lies on the table.
        var away = ShopCartAt - ShopFraming.Eye;
        away.y = 0f;
        var facing = Quaternion.LookRotation(away.normalized, Vector3.up);
        var lying = Quaternion.LookRotation(Vector3.down, Vector3.back);
        for (var i = 0; i < _shopBasket.Count; i++)
        {
            var (item, count) = _shopBasket[i];
            var picture = item.Icon;
            if (picture == null || picture.texture == null)
                continue;
            var (row, column) = (i / inRow, i % inRow);
            var across = Mathf.Min(inRow, _shopBasket.Count - row * inRow);
            var aside = rows > 1 ? (row % 2 == 0 ? -0.25f : 0.25f) : 0f;
            var foot = ShopCartAt + new Vector3((column - (across - 1) / 2f + aside) * ShopCartWidth / inRow, 0f, -row * between);
            var ring = Quad(marker, "wm-shop-ring", RingTexture());
            ring.transform.localPosition = foot + new Vector3(0f, 0.004f, 0f);
            ring.transform.localRotation = lying;
            ring.transform.localScale = new Vector3(size * 0.95f, size * 0.95f, 1f);
            var ringMaterial = ring.GetComponent<MeshRenderer>().material;
            ringMaterial.SetFloat(IntensityId, ShopRingIntensity);
            ringMaterial.SetFloat("_ScanDepth", 0f);
            ringMaterial.SetFloat("_Plate", 0f);
            ringMaterial.SetFloat("_EdgeFade", 0f);
            _shopCards.Add(ring);
            _shopCardMaterials.Add(ringMaterial);
            if (counted && i < _shopCounts.Count)
            {
                _shopCounts[i].text = ShopCount(count);
                var number = Quad(marker, "wm-shop-count", _shopCountTexture);
                // At the card's shoulder, a little nearer than the card so it is drawn over it.
                number.transform.localPosition = foot + new Vector3(size * 0.38f, size * 0.92f, 0.03f);
                number.transform.localRotation = facing;
                number.transform.localScale = new Vector3(size * 0.6f, size * 0.6f * ShopCountCellHeight / ShopCountCellWidth, 1f);
                PlainHologram(number, MarketNumberIntensity);
                var numberMaterial = number.GetComponent<MeshRenderer>().material;
                numberMaterial.SetFloat("_Plate", ShopCountPlate);
                numberMaterial.SetVector(UvRectId, SheetCell(i, ShopCountColumns, ShopCountRows));
                _shopCards.Add(number);
                _shopCardMaterials.Add(numberMaterial);
            }
            var shape = picture.rect;
            var widest = Mathf.Max(shape.width, shape.height);
            var material = IconHologram(picture);
            material.SetFloat(HologramFloorId, _room.transform.TransformPoint(foot).y);
            material.SetFloat(HologramHeightId, size * 1.3f);
            var arriving = !_shopStanding.Contains(item.Pointer);
            material.SetFloat(HologramRevealId, arriving ? 0f : ShopCardRevealed);
            material.SetFloat(IntensityId, ShopCardIntensity);
            _shopCardMaterials.Add(material);
            if (arriving)
                _shopArriving.Add((material, Time.unscaledTime));
            for (var behind = 0; behind < Mathf.Min(count, ShopCardsDeep); behind++)
            {
                var card = IconCard(_room.transform, picture, size);
                card.name = "wm-shop-card";
                // Each stands on the table, a little behind the one in front of it.
                card.transform.localPosition = foot + new Vector3(behind * size * 0.05f, size * shape.height / widest / 2f, -behind * size * 0.1f);
                card.transform.localRotation = facing;
                card.GetComponent<Renderer>().sharedMaterial = material;
                _shopCards.Add(card);
            }
        }
        _shopStanding.Clear();
        foreach (var (item, _) in _shopBasket)
            _shopStanding.Add(item.Pointer);
    }

    private const float ShopCardRevealed = 1.1f;

    private static string ShopCount(int count) => "×" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // Raises the cards that are new to the basket.
    private void RaiseShopCards()
    {
        var now = Time.unscaledTime;
        for (var i = _shopArriving.Count - 1; i >= 0; i--)
        {
            var (material, started) = _shopArriving[i];
            var share = Math.Min(1f, (now - started) / ShopCardRise);
            material.SetFloat(HologramRevealId, share * ShopCardRevealed);
            if (share >= 1f)
                _shopArriving.RemoveAt(i);
        }
    }

    // The shop's own panel is a list at the left of the ops room, Kalina at the right on Buy and
    // Sell, and the machine's side of the frame left clear on Procurement.
    private void LayOutShop(UnityEngine.UIElements.VisualElement root)
    {
        var open = ShopSystem.IsShopOpen;
        if (open && !root.ClassListContains(ShopClass))
            _shellUndo.Add(() =>
            {
                root.RemoveFromClassList(ShopClass);
                root.RemoveFromClassList(ShopProcurementClass);
            });
        root.EnableInClassList(ShopClass, open);
        root.EnableInClassList(ShopProcurementClass, open && ShopSystem.IsProcurementOpen);
    }

    private System.Collections.IEnumerator RunPull()
    {
        var started = Time.unscaledTime;
        while (_pullOn && Time.unscaledTime - started < PullSeconds)
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (Time.unscaledTime - started > PullSkipAfter && mouse != null && mouse.leftButton.wasPressedThisFrame)
                break;
            yield return null;
        }
        _pullRoutine = null;
        EndPull();
    }

    // The machine goes, the shop's screen has its own backing again, and the shop is told.
    private void EndPull()
    {
        if (!_pullOn)
            return;
        if (_pullRoutine != null)
            Context.Coroutines.Stop(_pullRoutine);
        _pullRoutine = null;
        _pullOn = false;
        SetPullBars(false);
        if (_machineCameraRoutine != null)
            Context.Coroutines.Stop(_machineCameraRoutine);
        _machineCameraRoutine = null;
        StopMachineEffects();
        RestMachine();
        // The camera goes back to the shop's own view of the machine.
        _shotFraming = null;
        ApplyScreenPolicy();
        var done = _pullDone;
        _pullDone = null;
        done?.Invoke();
    }

    private GameObject _machine;
    private readonly List<Il2CppStem.SoundInstance> _machineSounds = new();

    // The pull's effects stop being switched, and what it was playing stops with them: a pull
    // cut short takes its sounds with it.
    private void StopMachineEffects()
    {
        if (_machineEffectsRoutine != null)
            Context.Coroutines.Stop(_machineEffectsRoutine);
        _machineEffectsRoutine = null;
        foreach (var sound in _machineSounds)
            if (sound != null && sound.Playing)
                sound.Stop();
        _machineSounds.Clear();
    }
    private readonly List<Material> _machineMaterials = new();

    // Stands the machine over the holotable, in the ship's place where O.C.I. is showing: its
    // height in metres (zero takes it away), how far along the table from its middle, and its turn.
    internal static string ShowMachine(float height, float along, float yaw)
    {
        var d = _instance;
        if (d?._room == null)
            return "no room";
        d.StopMachineCamera();
        foreach (var material in d._machineMaterials)
            UnityEngine.Object.Destroy(material);
        d._machineMaterials.Clear();
        if (d._machine != null)
            UnityEngine.Object.Destroy(d._machine);
        d._machine = null;
        d.StopMachineEffects();
        d._machineEffects.Clear();
        d.ClearSkit();
        if (height <= 0f)
        {
            if (d._ociShip != null)
                d._ociShip.SetActive(d._ociOn);
            return "machine away";
        }
        var prefab = d.Context.Assets.Load<GameObject>("environment/gacha_machine/main");
        if (prefab == null || d.HologramMaterial() == null)
            return "no machine prefab or hologram material";
        // The model is 1.94 m tall.
        const float modelHeight = 1.94f;
        var machine = UnityEngine.Object.Instantiate(prefab, d._room.transform, false);
        machine.name = "wm-machine";
        machine.transform.localPosition = new Vector3(OciShipFoot.x + along, OciShipFoot.y, OciShipFoot.z);
        machine.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        machine.transform.localScale = Vector3.one * (height / modelHeight);
        var foot = machine.transform.position.y;
        foreach (var renderer in machine.GetComponentsInChildren<Renderer>(true))
        {
            Material[] own = renderer.sharedMaterials;
            var worn = new Material[own.Length];
            for (var i = 0; i < own.Length; i++)
            {
                worn[i] = d.HologramOf(own[i]);
                worn[i].SetFloat(HologramFloorId, foot);
                worn[i].SetFloat(HologramHeightId, height);
                worn[i].SetFloat(IntensityId, OciIntensity);
                worn[i].SetFloat("_ScanDensity", 60f);
                worn[i].SetFloat(HologramRevealId, 1.1f);
                // More of the machine's own pastel colours than a doll keeps, or it reads as white.
                worn[i].SetFloat("_OwnColour", 0.75f);
                d._machineMaterials.Add(worn[i]);
            }
            renderer.sharedMaterials = worn;
        }
        d._machine = machine;
        // GFL2's effects for the pull, each off until its moment: the pull's own and the
        // machine's in the machine's space, the capsule's on its first capsule, and the camera's
        // on a holder that goes where the pull's camera does.
        d._machineEffects.Clear();
        var body = machine.GetComponentInChildren<Animator>();
        if (body != null)
        {
            Transform Node(string name) => body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
            d._machineCameraNode = Node("Camera");
            d._machineCameraEffects = new GameObject("wm-machine-camera-effects").transform;
            d._machineCameraEffects.SetParent(body.transform, false);
            d.StageMachineEffects("environment/gacha_machine_vfx/main", body.transform, "fx");
            d.StageMachineEffects("environment/gacha_machine_vfx_body/main", body.transform, "body");
            d.StageMachineEffects("environment/gacha_machine_vfx_egg/main", Node("TwistEggA_01"), "egg");
            d.StageMachineEffects("environment/gacha_machine_vfx_egg_one/main", Node("TwistEggA_01"), "one");
            d.StageMachineEffects("environment/gacha_machine_vfx_camera/main", d._machineCameraEffects, "cam");
        }
        if (d._ociShip != null)
            d._ociShip.SetActive(false);
        return $"machine {height} m tall at {machine.transform.localPosition}, {d._machineMaterials.Count} materials, {d._machineEffects.Count} effects";
    }

    private object _machineCameraRoutine, _machineEffectsRoutine;
    private readonly Dictionary<string, GameObject> _machineEffects = new();
    private Transform _machineCameraNode, _machineCameraEffects;

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
                _machineEffects[$"{tag}{(node.parent.name == "VFX (2)" ? "2" : "")}:{node.name}"] = node.gameObject;
                node.gameObject.SetActive(false);
            }
    }

    // The camera's effects stand where the pull's camera does, at their own size: the camera's
    // node carries its lens's focal length as its scale, which they must not take.
    private void PlaceMachineCameraEffects()
    {
        if (_machineCameraNode == null || _machineCameraEffects == null)
            return;
        _machineCameraEffects.SetPositionAndRotation(_machineCameraNode.position, _machineCameraNode.rotation);
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
        foreach (var effect in _machineEffects.Values)
            effect.SetActive(false);
        foreach (var (name, start, length) in schedule)
        {
            if (!_machineEffects.TryGetValue(name, out var effect) || at < start || at > start + length)
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
        foreach (var effect in _machineEffects.Values)
            effect.SetActive(false);
        var started = Time.unscaledTime;
        var shown = new bool[schedule.Length];
        while (_machine != null && Time.unscaledTime - started < 10f)
        {
            var at = Time.unscaledTime - started;
            PlaceMachineCameraEffects();
            PlaceSkit(at);
            for (var i = 0; i < sounds.Length; i++)
                if (!heard[i] && at >= sounds[i].At)
                {
                    heard[i] = true;
                    if (new Il2CppStem.ID(bank, SoundIds.FromName(sounds[i].Sound)).Play(1f, 1f) is { } playing)
                        _machineSounds.Add(playing);
                }
            for (var i = 0; i < schedule.Length; i++)
            {
                var on = at >= schedule[i].Start && at <= schedule[i].Start + schedule[i].Length;
                if (on != shown[i] && _machineEffects.TryGetValue(schedule[i].Effect, out var effect))
                    effect.SetActive(shown[i] = on);
            }
            yield return null;
        }
        _machineEffectsRoutine = null;
    }

    // Plays one of the machine's pull clips (One or Ten) from its start, or holds it at a moment
    // of the clip, in seconds, for a still. With the camera, the room camera goes to GFL2's own
    // for the pull and follows it.
    internal static string PlayMachine(string clip, float at, bool camera, string rarity)
    {
        var d = _instance;
        var animator = d?._machine != null ? d._machine.GetComponentInChildren<Animator>() : null;
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
            d._machineEffectsRoutine = d.Context.Coroutines.Start(d.PlayMachineEffects(schedule, MachineSounds(clip == "Ten", rarity)));
        else
            d.HoldMachineEffects(schedule, at);
        d.StopMachineCamera();
        if (camera)
            d._machineCameraRoutine = d.Context.Coroutines.Start(d.FollowMachineCamera(at < 0f ? clipSeconds + 2f : float.PositiveInfinity));
        return (at < 0f ? $"playing {clip}" : $"{clip} held at {at} s") + $", {rarity}" + (camera ? ", with its camera" : "");
    }

    // The room camera is its screen's own again.
    private void StopMachineCamera()
    {
        if (_machineCameraRoutine == null)
            return;
        Context.Coroutines.Stop(_machineCameraRoutine);
        _machineCameraRoutine = null;
        LeaveMachineCamera();
    }

    // O.C.I. takes its own view back. A pull's view goes with the pull.
    private void LeaveMachineCamera()
    {
        if (!_ociOn)
            return;
        _shotLift = OciFraming.Lift;
        _ociShotFor = (float.NaN, float.NaN);
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
        var node = _machineCameraNode;
        var up = _room.transform.up;
        var (from, lift, started) = (_shot, _shotLift, Time.unscaledTime);
        while (node != null && _machine != null)
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
        _machineCameraRoutine = null;
        LeaveMachineCamera();
    }
}
