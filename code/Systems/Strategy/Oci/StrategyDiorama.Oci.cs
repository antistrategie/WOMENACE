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
    // How far the ship panel's figures stand in from its sides, and the space between them and the
    // operation's rows under them.
    private const float OciFiguresInset = 20f;
    private const float OciOperationGap = 8f;
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

    // A slot is drawn once it stands in its group, so none shows where vanilla puts it.
    private const string OciPlacedClass = "wm-oci-slot--placed";

    private static (float Yaw, float Tilt, float Distance) OciOrbitStart()
    {
        var back = N(OciFraming.Eye - OciFraming.Target);
        var far = back.Length();
        return (MathF.Atan2(back.X, back.Z) * Degrees, MathF.Asin(back.Y / far) * Degrees, far);
    }

    // The ship over the table, made once for the room and put away between visits.
    private GameObject OciShip()
    {
        if (_oci.Ship != null)
            return _oci.Ship;
        var prefab = Context.Assets.Load<GameObject>(ElmoAsset);
        if (prefab == null || HologramMaterial() == null)
            return null;
        _oci.Ship = UnityEngine.Object.Instantiate(prefab, _scene.Room.transform, false);
        _oci.Ship.name = OciShipName;
        _oci.Ship.transform.localPosition = OciShipFoot;
        _oci.Ship.transform.localRotation = OciShipFacing;
        _oci.Ship.transform.localScale = Vector3.one * (OciShipLength / ElmoLength);
        var foot = _oci.Ship.transform.position.y;
        var tall = ElmoHeight * OciShipLength / ElmoLength;
        foreach (var renderer in _oci.Ship.GetComponentsInChildren<Renderer>(true))
            WearHolograms(renderer, _oci.Holograms, (worn, _) =>
            {
                worn.SetFloat(HologramFloorId, foot);
                worn.SetFloat(HologramHeightId, tall);
                worn.SetFloat(IntensityId, OciIntensity);
                worn.SetFloat("_ScanDensity", OciLinesPerMetre);
            });
        return _oci.Ship;
    }

    // What O.C.I. showed goes with its room, and is made again when the room is.
    private void ClearOci()
    {
        foreach (var material in _oci.Holograms)
            if (material != null)
                UnityEngine.Object.Destroy(material);
        _oci.Holograms.Clear();
    }

    private void DriveOci()
    {
        var on = _policyScreen == VanillaUi.Screens.ShipUpgrades && _scene.Room != null && _scene.Camera != null && _scene.Active == _scene.Ops
            && _shellRoot?.panel != null;
        if (!on)
        {
            if (_oci.On && _oci.Ship != null)
            {
                _oci.Ship.SetActive(false);
                Cue(TableCue.Vanish);
            }
            _oci.On = false;
            return;
        }
        var ship = OciShip();
        if (ship == null)
            return;
        if (!_oci.On)
        {
            // Each visit opens on the side view, the ship drawn in from its foot.
            _oci.On = true;
            Cue(TableCue.Appear);
            _oci.Orbiting = false;
            _oci.Start = OciOrbitStart();
            _oci.ShotFor = (_oci.Yaw, _oci.Distance) = (_oci.YawTarget, _oci.DistanceTarget) = (_oci.Start.Yaw, _oci.Start.Distance);
            _oci.Reveal = 0f;
            ship.SetActive(true);
        }
        if (_oci.Reveal < 1f)
        {
            _oci.Reveal = MathF.Min(1f, _oci.Reveal + HitchStep(ShotHitchSeconds, ShotHitchStep) / OciRevealSeconds);
            var drawn = Ease.InOutCubic(_oci.Reveal) * 1.1f;
            foreach (var material in _oci.Holograms)
                material.SetFloat(HologramRevealId, drawn);
        }
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            if (!mouse.middleButton.isPressed)
                _oci.Orbiting = false;
            else if (mouse.middleButton.wasPressedThisFrame)
                _oci.Orbiting = PointerOverOciRoom();
            if (_oci.Orbiting)
            {
                // Round the ship a part of the way and no higher or lower: tilted or further
                // round, the camera sees out of the room's windows.
                _oci.YawTarget = Math.Clamp(_oci.YawTarget + mouse.delta.ReadValue().x * OrbitDegreesPerPixel, _oci.Start.Yaw - OciTurn, _oci.Start.Yaw + OciTurn);
            }
            var scroll = WheelNotches(mouse.scroll.ReadValue().y);
            if (MathF.Abs(scroll) > 0.01f && PointerOverOciRoom())
                _oci.DistanceTarget = Math.Clamp(_oci.DistanceTarget * OrbitZoom(scroll), OciNearest, _oci.Start.Distance);
        }
        var smoothing = OrbitSmoothing();
        _oci.Yaw += (_oci.YawTarget - _oci.Yaw) * smoothing;
        _oci.Distance += (_oci.DistanceTarget - _oci.Distance) * smoothing;
        if (_oci.ShotFor != (_oci.Yaw, _oci.Distance))
        {
            _oci.ShotFor = (_oci.Yaw, _oci.Distance);
            // The camera keeps clear of the room's walls all the way round.
            var (target, way) = (N(OciFraming.Target), PlanetOrbitWay(_oci.Yaw, _oci.Start.Tilt));
            var local = target + way * ClearDistance(target, way, _oci.Distance, OciNearest);
            var eye = _scene.Room.transform.TransformPoint(new Vector3(local.X, local.Y, local.Z));
            var focus = _scene.Room.transform.TransformPoint(OciFraming.Target);
            _shot = (eye, Quaternion.LookRotation(focus - eye, _scene.Room.transform.up), OciFraming.Fov, OciFraming.Shift);
            _oci.Settling = 30;
        }
        if (_shotTravel < 1f)
            _oci.Settling = 30;
        // Vanilla sets its slots up over the first frames the screen shows, and the fairies' come
        // later: a slot added, or one set up after the last layout, is given its place at once.
        var holder = _oci.SlotsHolder?.panel != null ? _oci.SlotsHolder
            : UnityEngine.Time.frameCount % 10 == 0 ? UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.Slots)) : null;
        if (holder != null && (holder.childCount != _oci.SlotCount || _oci.Unplaced
            || (_oci.Unresolved.Count > 0 && UnityEngine.Time.frameCount % 10 == 0 && OciSlotResolved())))
            LayOutOci(_shellRoot);
        PickOciGroup();
        if (_oci.Settling > 0)
        {
            _oci.Settling--;
            PlaceOciLines(ship);
        }
    }

    private static Il2CppMenace.Strategy.ShipUpgradeSlotTemplate OciSlotTemplate(ShipSlot slot, Il2CppMenace.Strategy.ShipUpgrades ships) =>
        slot.GetSlotTemplate() ?? ships?.GetSlotTemplateByIdx(slot.SlotIdx);

    private bool OciSlotResolved()
    {
        var ships = Il2CppMenace.States.StrategyState.Get()?.ShipUpgrades;
        foreach (var slot in _oci.Unresolved)
            if (slot != null && OciSlotTemplate(slot, ships)?.GetID() != null)
                return true;
        return false;
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
                over = _oci.Groups.FindIndex(g => g.Group == element);
            else if (_oci.SlotGroups.TryGetValue(element.Pointer, out var group))
                over = group;
        if (over == _oci.Picked)
            return;
        _oci.Picked = over;
        for (var i = 0; i < _oci.Groups.Count; i++)
        {
            var (_, _, stem, line, ring) = _oci.Groups[i];
            stem.EnableInClassList(OciLineOnClass, i == over);
            line.EnableInClassList(OciLineOnClass, i == over);
            ring.EnableInClassList(OciLineOnClass, i == over);
        }
    }

    // Each group's line runs from under its middle to its part of the hull, as the camera sees it.
    private void PlaceOciLines(GameObject ship)
    {
        var camera = _scene.Camera.GetComponent<Camera>();
        var panel = _shellRoot.panel;
        foreach (var (group, anchor, stem, line, ring) in _oci.Groups)
        {
            var box = group.worldBound;
            var at = camera.WorldToScreenPoint(ship.transform.TransformPoint(anchor));
            var shown = at.z > 0f && box.width > 1f && _oci.Reveal > 0.5f;
            stem.style.display = line.style.display = ring.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (!shown)
            {
                // Asked again until the groups have been laid out and the ship is drawn.
                _oci.Settling = Math.Max(_oci.Settling, 2);
                continue;
            }
            // The lines are placed in their holder, which stands in from the edges of a screen
            // wider or taller than 16:9.
            var origin = line.parent != null ? line.parent.worldBound.position : Vector2.zero;
            var end = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(at.x, Screen.height - at.y)) - origin;
            var (fromX, fromY) = (box.center.x - origin.x, box.yMax + OciStem - origin.y);
            stem.style.left = fromX;
            stem.style.top = box.yMax - origin.y;
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
        if (slots == null || window == null || _scene.Active != _scene.Ops)
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
        // The operation's strip goes under the figures, in their panel, as its rows in mission
        // select's box, lined up with the figures. Coming from mission select, the camera's travel
        // has already begun fading the Operations panels in from nothing, and the fade lets go of a
        // panel moved out of the shell, so the strip is made whole here.
        var corner = UI.Find(root, UiSelector.Name(VanillaUi.Elements.TopRightCorner));
        if (corner != null)
        {
            MoveInto(window, corner);
            InlineStyle(corner, s =>
            {
                s.position = Position.Relative;
                s.top = 0;
                s.left = StyleKeyword.Auto;
                s.right = StyleKeyword.Auto;
                s.flexDirection = FlexDirection.Column;
                s.alignItems = Align.Stretch;
                s.marginTop = OciOperationGap;
                s.marginLeft = OciFiguresInset;
                s.marginRight = OciFiguresInset;
                s.opacity = StyleKeyword.Null;
            }, s =>
            {
                s.position = StyleKeyword.Null;
                s.top = StyleKeyword.Null;
                s.left = StyleKeyword.Null;
                s.right = StyleKeyword.Null;
                s.flexDirection = StyleKeyword.Null;
                s.alignItems = StyleKeyword.Null;
                s.marginTop = StyleKeyword.Null;
                s.marginLeft = StyleKeyword.Null;
                s.marginRight = StyleKeyword.Null;
            });
            DressOperationRows(corner, InlineStyle);
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
                _oci.Groups.Clear();
            });
            _oci.Groups.Clear();
            _oci.Picked = -1;
        }
        // Every slot stands in its kind's group, in the slots' own order. The slots stay in
        // vanilla's holder, which lies over the whole screen, and are placed over their group.
        var all = new List<ShipSlot>();
        for (var i = 0; i < slots.childCount; i++)
            if (slots[i].TryCast<ShipSlot>() is { } one)
                all.Add(one);
        _oci.SlotsHolder = slots;
        _oci.SlotCount = slots.childCount;
        _oci.Unplaced = false;
        _oci.Unresolved.Clear();
        var ships = Il2CppMenace.States.StrategyState.Get()?.ShipUpgrades;
        // A group is as wide as a slot, and the groups are spread evenly across their row.
        var column = OciSlotWidth + OciGroupPad * 2f;
        var step = (OciGroupsWidth - column) / (OciAnchors.Length - 1);
        // How far down its group the next slot goes, by the group.
        var held = new Dictionary<int, float>();
        _oci.SlotGroups.Clear();
        foreach (var slot in all.OrderBy(s => s.SlotIdx))
        {
            var template = OciSlotTemplate(slot, ships);
            var kind = template?.GetID();
            if (kind == null)
            {
                _oci.Unresolved.Add(slot);
                continue;
            }
            var name = $"wm-oci-group-{kind}";
            var index = _oci.Groups.FindIndex(g => g.Group.name == name);
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
                _oci.Groups.Add((group, anchor.Kind != null ? anchor.Anchor : new Vector3(0f, ElmoHeight * 0.5f, 0f), stem, line, ring));
                index = _oci.Groups.Count - 1;
            }
            var down = held.GetValueOrDefault(index);
            held[index] = down + OciSlotPitch + (DressOciInfo(slot) ? OciInfoStrip : 0f);
            _oci.SlotGroups[slot.Pointer] = index;
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
            Inline(slot, e => e.AddToClassList(OciPlacedClass), e => e.RemoveFromClassList(OciPlacedClass));
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
        for (var i = 0; i < _oci.Groups.Count; i++)
            _oci.Groups[i].Group.style.height = OciGroupHead + held.GetValueOrDefault(i) + OciGroupPad;
        _oci.Settling = 30;
    }
}
