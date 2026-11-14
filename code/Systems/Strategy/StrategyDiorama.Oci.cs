using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;
using NVector3 = System.Numerics.Vector3;

namespace WOMENACE.Code;

// O.C.I. in the ops room: the ship as a hologram over the holotable, seen from the table's side
// by a camera that goes round it, vanilla's module slots in a group for each kind across the top
// with a line from each group to its part of the hull, and the ship's figures at the right.
public sealed partial class StrategyDiorama
{
    private const string OciClass = "wm-oci";
    private const string ElmoAsset = "environment/elmo/main";
    private const string OciShipName = "wm-oci-ship";
    // The top bar's label of the ship's name.
    private const string ShellShipName = "wm-shell-ship";

    // The ship's foot over the middle of the table, in the room's space, and its length there.
    // The model's own length is its prefab's, bow to stern.
    private static readonly Vector3 OciShipFoot = new(-14.4f, -0.12f, 0f);
    private const float OciShipLength = 4.2f;
    private const float ElmoLength = 75.59f;
    private const float ElmoHeight = 20.29f;
    // Bow to the left of the view the screen opens on.
    private static readonly Quaternion OciShipFacing = Quaternion.Euler(0f, 90f, 0f);
    private const float OciIntensity = 1.8f;
    private const float OciRevealSeconds = 1.1f;
    // Coarser lines than a doll's: seen from across the table, finer ones run into bands.
    private const float OciLinesPerMetre = 30f;

    private const float OciNearest = 3.4f;
    // How far the camera turns either way from the view it opens on.
    private const float OciTurn = 20f;

    // Where on the hull each kind of module is, in the model's own space, by the slot kind's ID.
    private static readonly (string Kind, Vector3 Anchor)[] OciAnchors =
    {
        ("ship_upgrade_slot.bridge", new Vector3(0f, 13f, 30f)),
        ("ship_upgrade_slot.armament", new Vector3(0f, 19f, 7f)),
        ("ship_upgrade_slot.hull", new Vector3(0f, 6f, -8f)),
        ("ship_upgrade_slot.wmgfl_fairies", new Vector3(0f, 16f, -24f)),
    };

    // The groups' row between the bars' left edge and the figures' panel, in the shell's units.
    private const float OciGroupsWidth = 860f;
    private const float OciSlotWidth = 190f;
    // A slot's picture is this wide and tall, and its name's box takes the rest of the width
    // at the same height.
    private const float OciSlotIcon = 40.5f;
    private const float OciPanelLeft = 908f;
    private const float OciPanelWidth = 340f;
    private const float OciStem = 10f;
    // A group's padding, its header's height, a slot's height with the gap under it, and the
    // room a slot keeps over itself for the name of its kind, which the group's header gives.
    private const float OciGroupPad = 5f;
    private const float OciGroupHead = 22f;
    private const float OciSlotPitch = 43f;
    private const float OciSlotLabelRoom = 13f;
    // Vanilla draws a slot smaller than it lays it out, about its middle: this is the share it
    // is drawn at where the slot does not say, and a slot's height as laid out.
    private const float OciSlotScale = 0.9f;
    private const float OciSlotHeight = 59.5f;
    private const string OciGroupsName = "wm-oci-groups";
    private const string OciLinesName = "wm-oci-lines";
    private const string OciGroupClass = "wm-oci-group";
    private const string OciLineOnClass = "wm-oci-line--on";

    private GameObject _ociShip;
    private readonly List<Material> _ociHolograms = new();
    private float _ociReveal;
    private bool _ociOn, _ociOrbiting;
    // The view each visit opens on, round the ship from it, and the view the shot was last made for.
    private (float Yaw, float Tilt, float Distance) _ociStart;
    private float _ociYaw, _ociDistance, _ociYawTarget, _ociDistanceTarget;
    private (float Yaw, float Distance) _ociShotFor;
    // A group with the parts of its line, and the group the pointer is over.
    private readonly List<(VisualElement Group, Vector3 Anchor, VisualElement Stem, VisualElement Line, VisualElement Ring)> _ociGroups = new();
    private int _ociPicked = -1;
    // Each slot's group, by the slot, and how many elements the slots' holder had when laid out.
    private readonly Dictionary<IntPtr, int> _ociSlotGroups = new();
    private int _ociSlotCount;
    // Frames the lines are still placed for after anything they follow last moved.
    private int _ociSettling;

    private static (float Yaw, float Tilt, float Distance) OciOrbitStart()
    {
        var back = N(OciFraming.Eye - OciFraming.Target);
        var far = back.Length();
        return (MathF.Atan2(back.X, back.Z) * Degrees, MathF.Asin(back.Y / far) * Degrees, far);
    }

    // The ship over the table, made once for the room and put away between visits.
    private GameObject OciShip()
    {
        if (_ociShip != null)
            return _ociShip;
        var prefab = Context.Assets.Load<GameObject>(ElmoAsset);
        if (prefab == null || HologramMaterial() == null)
            return null;
        _ociShip = UnityEngine.Object.Instantiate(prefab, _room.transform, false);
        _ociShip.name = OciShipName;
        _ociShip.transform.localPosition = OciShipFoot;
        _ociShip.transform.localRotation = OciShipFacing;
        _ociShip.transform.localScale = Vector3.one * (OciShipLength / ElmoLength);
        var foot = _ociShip.transform.position.y;
        var tall = ElmoHeight * OciShipLength / ElmoLength;
        foreach (var renderer in _ociShip.GetComponentsInChildren<Renderer>(true))
        {
            Material[] own = renderer.sharedMaterials;
            var worn = new Material[own.Length];
            for (var i = 0; i < own.Length; i++)
            {
                worn[i] = HologramOf(own[i]);
                worn[i].SetFloat(HologramFloorId, foot);
                worn[i].SetFloat(HologramHeightId, tall);
                worn[i].SetFloat(IntensityId, OciIntensity);
                worn[i].SetFloat("_ScanDensity", OciLinesPerMetre);
                _ociHolograms.Add(worn[i]);
            }
            renderer.sharedMaterials = worn;
        }
        return _ociShip;
    }

    // What O.C.I. showed goes with its room, and is made again when the room is.
    private void ClearOci()
    {
        foreach (var material in _ociHolograms)
            if (material != null)
                UnityEngine.Object.Destroy(material);
        _ociHolograms.Clear();
        _ociGroups.Clear();
        _ociSlotGroups.Clear();
        (_ociShip, _ociOn, _ociPicked) = (null, false, -1);
    }

    private void DriveOci()
    {
        var on = _policyScreen == VanillaUi.Screens.ShipUpgrades && _room != null && _camera != null && _active == _ops
            && _shellRoot?.panel != null;
        if (!on)
        {
            if (_ociOn && _ociShip != null)
            {
                _ociShip.SetActive(false);
                Cue(TableCue.Vanish);
            }
            _ociOn = false;
            return;
        }
        var ship = OciShip();
        if (ship == null)
            return;
        if (!_ociOn)
        {
            // Each visit opens on the side view, the ship drawn in from its foot.
            _ociOn = true;
            Cue(TableCue.Appear);
            _ociOrbiting = false;
            _ociStart = OciOrbitStart();
            _ociShotFor = (_ociYaw, _ociDistance) = (_ociYawTarget, _ociDistanceTarget) = (_ociStart.Yaw, _ociStart.Distance);
            _ociReveal = 0f;
            ship.SetActive(true);
        }
        if (_ociReveal < 1f)
        {
            _ociReveal = MathF.Min(1f, _ociReveal + HitchStep(ShotHitchSeconds, ShotHitchStep) / OciRevealSeconds);
            var drawn = Ease.InOutCubic(_ociReveal) * 1.1f;
            foreach (var material in _ociHolograms)
                material.SetFloat(HologramRevealId, drawn);
        }
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            if (!mouse.middleButton.isPressed)
                _ociOrbiting = false;
            else if (mouse.middleButton.wasPressedThisFrame)
                _ociOrbiting = PointerOverOciRoom();
            if (_ociOrbiting)
            {
                // Round the ship a part of the way and no higher or lower: tilted or further
                // round, the camera sees out of the room's windows.
                _ociYawTarget = Math.Clamp(_ociYawTarget + mouse.delta.ReadValue().x * OrbitDegreesPerPixel, _ociStart.Yaw - OciTurn, _ociStart.Yaw + OciTurn);
            }
            var scroll = WheelNotches(mouse.scroll.ReadValue().y);
            if (MathF.Abs(scroll) > 0.01f && PointerOverOciRoom())
                _ociDistanceTarget = Math.Clamp(_ociDistanceTarget * OrbitZoom(scroll), OciNearest, _ociStart.Distance);
        }
        var smoothing = OrbitSmoothing();
        _ociYaw += (_ociYawTarget - _ociYaw) * smoothing;
        _ociDistance += (_ociDistanceTarget - _ociDistance) * smoothing;
        if (_ociShotFor != (_ociYaw, _ociDistance))
        {
            _ociShotFor = (_ociYaw, _ociDistance);
            // The camera keeps clear of the room's walls all the way round.
            var (target, way) = (N(OciFraming.Target), PlanetOrbitWay(_ociYaw, _ociStart.Tilt));
            var local = target + way * ClearDistance(target, way, _ociDistance, OciNearest);
            var eye = _room.transform.TransformPoint(new Vector3(local.X, local.Y, local.Z));
            var focus = _room.transform.TransformPoint(OciFraming.Target);
            _shot = (eye, Quaternion.LookRotation(focus - eye, _room.transform.up), OciFraming.Fov, OciFraming.Shift);
            _ociSettling = 30;
        }
        if (_shotTravel < 1f)
            _ociSettling = 30;
        // Slots added after the screen was laid out (the fairies') are given their places.
        if (UnityEngine.Time.frameCount % 10 == 0 && UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.Slots)) is { } slots && slots.childCount != _ociSlotCount)
            LayOutOci(_shellRoot);
        PickOciGroup();
        if (_ociSettling > 0)
        {
            _ociSettling--;
            PlaceOciLines(ship);
        }
    }

    internal static string OciOrbitFromDev(float yaw, float distance)
    {
        var d = _instance;
        if (d == null || !d._ociOn)
            return "O.C.I. is not showing";
        var start = d._ociStart;
        (d._ociYawTarget, d._ociDistanceTarget) = (Math.Clamp(yaw, start.Yaw - OciTurn, start.Yaw + OciTurn), Math.Clamp(distance, OciNearest, start.Distance));
        return $"orbit to {d._ociYawTarget:F0}, {d._ociDistanceTarget:F1}";
    }

    // Whether the pointer is over the room: nothing of the shell's or vanilla's panels under it.
    private bool PointerOverOciRoom()
    {
        var picked = UnderPointer();
        return picked == null || picked == _shellRoot || picked.name == VanillaUi.Elements.Slots;
    }

    // The group the pointer is over is marked, with its line.
    private void PickOciGroup()
    {
        if (UnityEngine.Time.frameCount % 4 != 0)
            return;
        var over = -1;
        for (var element = UnderPointer(); element != null && over < 0; element = element.parent)
            if (element.ClassListContains(OciGroupClass))
                over = _ociGroups.FindIndex(g => g.Group == element);
            else if (_ociSlotGroups.TryGetValue(element.Pointer, out var group))
                over = group;
        if (over == _ociPicked)
            return;
        _ociPicked = over;
        for (var i = 0; i < _ociGroups.Count; i++)
        {
            var (_, _, stem, line, ring) = _ociGroups[i];
            stem.EnableInClassList(OciLineOnClass, i == over);
            line.EnableInClassList(OciLineOnClass, i == over);
            ring.EnableInClassList(OciLineOnClass, i == over);
        }
    }

    // Each group's line runs from under its middle to its part of the hull, as the camera sees it.
    private void PlaceOciLines(GameObject ship)
    {
        var camera = _camera.GetComponent<Camera>();
        var panel = _shellRoot.panel;
        foreach (var (group, anchor, stem, line, ring) in _ociGroups)
        {
            var box = group.worldBound;
            var at = camera.WorldToScreenPoint(ship.transform.TransformPoint(anchor));
            var shown = at.z > 0f && box.width > 1f && _ociReveal > 0.5f;
            stem.style.display = line.style.display = ring.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (!shown)
            {
                // Asked again until the groups have been laid out and the ship is drawn.
                _ociSettling = Math.Max(_ociSettling, 2);
                continue;
            }
            var end = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(at.x, Screen.height - at.y));
            var (fromX, fromY) = (box.center.x, box.yMax + OciStem);
            stem.style.left = fromX;
            stem.style.top = box.yMax;
            var (dx, dy) = (end.x - fromX, end.y - fromY);
            line.style.left = fromX;
            line.style.top = fromY;
            line.style.width = MathF.Sqrt(dx * dx + dy * dy);
            line.style.rotate = new Rotate(new Angle(MathF.Atan2(dy, dx) * Degrees, AngleUnit.Degree));
            ring.style.left = end.x;
            ring.style.top = end.y;
        }
    }

    // Vanilla's slots stand in a group for each kind across the top, and its ship window is the
    // panel at the right.
    private void LayOutOci(VisualElement root)
    {
        var slots = UI.Find(root, UiSelector.Name(VanillaUi.Elements.Slots));
        var window = UI.Find(root, UiSelector.Name(VanillaUi.Elements.ShipInfoWindow));
        if (slots == null || window == null || _active != _ops)
            return;
        if (!root.ClassListContains(OciClass))
        {
            root.AddToClassList(OciClass);
            _shellUndo.Add(() => root.RemoveFromClassList(OciClass));
        }
        Dock(window, OciPanelLeft, SquadsTop, OciPanelWidth, 0f);
        AtItsOwnHeight(window);
        // The ship goes by the shell's name for it here, as on the top bar.
        var called = UI.Find(root, UiSelector.Name(ShellShipName))?.TryCast<Label>()?.text;
        if (!string.IsNullOrEmpty(called) && UI.Find(window, UiSelector.Name(VanillaUi.Elements.ShipName))?.TryCast<Label>() is { } ship && ship.text != called)
        {
            var vanilla = ship.text;
            ship.text = called;
            _shellUndo.Add(() => ship.text = vanilla);
        }
        // The planet and assets strip goes under the figures, in their panel.
        var corner = UI.Find(root, UiSelector.Name(VanillaUi.Elements.TopRightCorner));
        if (corner != null)
        {
            MoveInto(window, corner);
            Inline(corner, e =>
            {
                e.style.position = Position.Relative;
                e.style.top = 0;
                e.style.marginTop = 0;
            }, e =>
            {
                e.style.position = StyleKeyword.Null;
                e.style.top = StyleKeyword.Null;
                e.style.marginTop = StyleKeyword.Null;
            });
        }

        var lines = UI.Find(root, UiSelector.Name(OciLinesName));
        var groups = UI.Find(root, UiSelector.Name(OciGroupsName));
        if (lines == null || groups == null)
        {
            lines = new VisualElement { name = OciLinesName, pickingMode = PickingMode.Ignore };
            groups = new VisualElement { name = OciGroupsName, pickingMode = PickingMode.Ignore };
            groups.style.left = RosterLeft;
            groups.style.top = SquadsTop;
            groups.style.width = OciGroupsWidth;
            // Under the slots, which are drawn on their groups.
            var at = root.IndexOf(slots);
            root.Insert(at, groups);
            root.Insert(at + 1, lines);
            var (madeLines, madeGroups) = (lines, groups);
            _shellUndo.Add(() =>
            {
                madeLines.RemoveFromHierarchy();
                madeGroups.RemoveFromHierarchy();
                _ociGroups.Clear();
            });
            _ociGroups.Clear();
            _ociPicked = -1;
        }
        // Every slot stands in its kind's group, in the slots' own order. The slots stay in
        // vanilla's holder, which lies over the whole screen, and are placed over their group.
        var all = new List<ShipSlot>();
        for (var i = 0; i < slots.childCount; i++)
            if (slots[i].TryCast<ShipSlot>() is { } one)
                all.Add(one);
        _ociSlotCount = slots.childCount;
        var ships = Il2CppMenace.States.StrategyState.Get()?.ShipUpgrades;
        // A group is as wide as a slot, and the groups are spread evenly across their row.
        var column = OciSlotWidth + OciGroupPad * 2f;
        var step = (OciGroupsWidth - column) / (OciAnchors.Length - 1);
        // How far down its group the next slot goes, by the group.
        var held = new Dictionary<int, float>();
        _ociSlotGroups.Clear();
        foreach (var slot in all.OrderBy(s => s.SlotIdx))
        {
            var template = slot.GetSlotTemplate() ?? ships?.GetSlotTemplateByIdx(slot.SlotIdx);
            var kind = template?.GetID();
            if (kind == null)
                continue;
            var name = $"wm-oci-group-{kind}";
            var index = _ociGroups.FindIndex(g => g.Group.name == name);
            if (index < 0)
            {
                var group = new VisualElement { name = name };
                group.AddToClassList(OciGroupClass);
                group.style.width = column;
                if (groups.childCount > 0)
                    group.style.marginLeft = step - column;
                var header = new VisualElement();
                header.AddToClassList("wm-oci-group-header");
                var title = new Label(template.Name?.GetTranslated()?.ToUpperInvariant() ?? "");
                title.AddToClassList("wm-oci-group-title");
                var rule = new VisualElement();
                rule.AddToClassList("wm-oci-group-rule");
                header.Add(title);
                header.Add(rule);
                group.Add(header);
                groups.Add(group);
                var (stem, line, ring) = (new VisualElement(), new VisualElement(), new VisualElement());
                stem.AddToClassList("wm-oci-stem");
                line.AddToClassList("wm-oci-line");
                ring.AddToClassList("wm-oci-ring");
                foreach (var part in new[] { stem, line, ring })
                {
                    part.pickingMode = PickingMode.Ignore;
                    part.style.display = DisplayStyle.None;
                    lines.Add(part);
                }
                stem.style.height = OciStem;
                var anchor = OciAnchors.FirstOrDefault(a => a.Kind == kind);
                // A kind with no part of the hull on record points at the ship's middle.
                _ociGroups.Add((group, anchor.Kind != null ? anchor.Anchor : new Vector3(0f, ElmoHeight * 0.5f, 0f), stem, line, ring));
                index = _ociGroups.Count - 1;
            }
            var down = held.GetValueOrDefault(index);
            held[index] = down + OciSlotPitch;
            _ociSlotGroups[slot.Pointer] = index;
            // What the slot had is put back, since the fairies' slots are placed from code.
            var (position, left, top) = (slot.style.position, slot.style.left, slot.style.top);
            // The sizes given are the slot's as laid out, which come out as wanted once drawn.
            var drawn = slot.resolvedStyle.scale.value.x;
            if (!(drawn > 0.1f))
                drawn = OciSlotScale;
            var wide = OciSlotWidth / drawn;
            var (x, y) = (RosterLeft + index * step + OciGroupPad - wide * (1f - drawn) / 2f,
                SquadsTop + OciGroupHead + down - OciSlotLabelRoom - OciSlotHeight * (1f - drawn) / 2f);
            Inline(slot, e =>
            {
                e.style.position = Position.Absolute;
                e.style.left = x;
                e.style.top = y;
                e.style.width = wide;
            }, e =>
            {
                e.style.position = position;
                e.style.left = left;
                e.style.top = top;
                e.style.width = StyleKeyword.Null;
            });
            // Vanilla gives the name's box a width and a height of its own, shorter than the
            // picture. Here it is the picture's height and runs to the slot's edge, as does
            // everything it stands in.
            var named = UI.Find(slot, UiSelector.Name(VanillaUi.Elements.SlotLabelContainer));
            for (var inner = named?.parent; inner != null && inner.Pointer != slot.Pointer; inner = inner.parent)
                Inline(inner, e => e.style.width = wide, e => e.style.width = StyleKeyword.Null);
            Inline(named, e =>
            {
                e.style.width = wide - OciSlotIcon / drawn;
                e.style.height = OciSlotIcon / drawn;
                e.style.marginTop = 0;
                e.style.marginBottom = 0;
            }, e =>
            {
                e.style.width = StyleKeyword.Null;
                e.style.height = StyleKeyword.Null;
                e.style.marginTop = StyleKeyword.Null;
                e.style.marginBottom = StyleKeyword.Null;
            });
        }
        for (var i = 0; i < _ociGroups.Count; i++)
            _ociGroups[i].Group.style.height = OciGroupHead + held.GetValueOrDefault(i) + OciGroupPad;
        _ociSettling = 30;
    }
}
