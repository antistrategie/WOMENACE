using Il2CppMenace.UI;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Mission preparation's flat view: vanilla's own map, shown in place of the hologram for a
// player who would sooner place squads on it. The camera goes over the table and looks straight
// down, from where the hologram lies in the frame exactly as the flat map does, so the one is
// laid flat and gives way to the other in place. The choice is one of the player's settings.
public sealed partial class StrategyDiorama
{
    private const string PrepViewToggleName = "wm-prep-view";
    private const float PrepFlatSeconds = 0.7f;
    // How tall the hologram stands, of its own height, once it is laid flat.
    private const float PrepFlatHeight = 0.02f;
    // How far over the map the camera looks down from: under the machine on the ceiling.
    private const float PrepFlatEye = 3f;
    // The flat map's ground and frame. What lies on the ground is the hologram's own picture.
    private static readonly Color32 PrepFlatGround = new(6, 16, 24, 235);
    private static readonly Color32 PrepFlatLine = new(110, 205, 235, 255);
    private const string PrepHintLowClass = "wm-pan-hint--low";

    // Where the camera looks down from in the flat view, and with what field of view.

    private void DrivePrepFlat()
    {
        _prep.FlatShot = null;
        var column = _shellRoot != null && _policyScreen == VanillaUi.Screens.MissionPrep && _prep.Map != null && _scene.Room != null
            ? UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.MapColumn)) : null;
        var preview = column != null ? UI.Find(column, UiSelector.Name(VanillaUi.Elements.PrepPreview)) : null;
        if (preview == null || column.panel == null || preview.layout.width <= 0f || _prep.MapTile <= 0f)
        {
            (_prep.FlatVisit, _prep.FlatPlaced, _prep.TokensUp) = (false, false, 1f);
            return;
        }
        // A visit starts in the view last chosen, with no move into it.
        if (!_prep.FlatVisit)
        {
            _prep.Flat = PlayerSettingsSystem.DeploymentFlatView;
            (_prep.FlatVisit, _prep.FlatAmount, _prep.FlatUndressed) = (true, _prep.Flat ? 1f : 0f, true);
        }
        var target = _prep.Flat ? 1f : 0f;
        float? held = null;
        HoldPrepFlat(ref held);
        if (held is { } amount)
            _prep.FlatAmount = amount;
        else if (_prep.FlatAmount != target)
        {
            var step = HitchStep(StageLongestStep) / PrepFlatSeconds;
            _prep.FlatAmount = Towards(_prep.FlatAmount, target, step);
        }
        var along = Ease.InOutCubic(_prep.FlatAmount);

        // The hologram is laid flat as the camera comes over it, and what stands on it goes.
        _prep.Map.transform.localScale = new Vector3(1f, 1f + (PrepFlatHeight - 1f) * Math.Clamp(along, 0f, 1f), 1f);
        if (_prep.Map.GetComponent<MeshRenderer>() is { } hologram)
            hologram.enabled = _prep.FlatAmount < 1f;
        _prep.TokensUp = 1f - Math.Clamp(_prep.FlatAmount * 2.5f, 0f, 1f);
        if (UI.Find(_shellRoot, UiSelector.Name(PrepTokensName)) is { } tokens)
            tokens.style.opacity = _prep.TokensUp;
        ShowPrepViewToggle();

        if (_prep.FlatAmount <= 0f)
        {
            if (_prep.FlatPlaced)
            {
                _prep.FlatPlaced = false;
                column.style.scale = StyleKeyword.Null;
                column.style.transformOrigin = StyleKeyword.Null;
                column.style.left = PrepFlatMapParked;
                column.style.top = 0;
                column.style.opacity = PrepFlatMapOpacity();
                foreach (var name in PrepFlatButtons)
                    if (UI.Find(column, UiSelector.Name(name)) is { } button)
                        button.style.translate = StyleKeyword.Null;
            }
            return;
        }

        // The flat map in the frame's free part, between the frame's left and the roster and
        // between the chips and the foot of the panels, which is as tall as vanilla draws its
        // map. It stands there at vanilla's own size: vanilla finds the tile under the pointer
        // from the map's size as it laid it out, so a map made smaller is one whose tiles are
        // not where they are seen. Only a map too large for the space is made smaller.
        var (left, right) = (RosterLeft + PrepFitMargin, PrepRosterLeft - PrepFitMargin);
        var (top, bottom) = (SquadsTop + PrepChipHeight + PrepGap, SquadsBottom);
        var (width, height) = (preview.layout.width, preview.layout.height);
        var scale = MathF.Min(1f, MathF.Min((right - left) / width, (bottom - top) / height));
        var middle = new Vector2((left + right) / 2f, (top + bottom) / 2f);
        // The column is placed from its parent's corner, the frame from the screen's.
        var frame = _shellRoot.worldBound.position;
        var corner = column.parent != null ? column.parent.worldBound.position : Vector2.zero;
        column.style.transformOrigin = new TransformOrigin(0f, 0f, 0f);
        column.style.scale = new Scale(new Vector3(scale, scale, 1f));
        column.style.left = frame.x + middle.x - (preview.layout.x + width / 2f) * scale - corner.x;
        column.style.top = frame.y + middle.y - (preview.layout.y + height / 2f) * scale - corner.y;
        // It comes in over the hologram once that lies flat under it.
        column.style.opacity = Math.Clamp((_prep.FlatAmount - 0.55f) / 0.45f, 0f, 1f);
        if (!_prep.FlatPlaced)
        {
            _prep.FlatPlaced = true;
            // Vanilla's own two buttons stand in the column, over the map. The shell has them.
            foreach (var name in PrepFlatButtons)
                if (UI.Find(column, UiSelector.Name(name)) is { } button)
                    button.style.translate = new Translate(new Length(PrepFlatMapParked, LengthUnit.Pixel), new Length(0f, LengthUnit.Pixel));
        }
        // The hologram's picture is made again when the map's zone changes (a squad with a
        // zone of its own is picked up), and the one the flat map shows is gone with the old.
        if ((_prep.MapTexture != null ? _prep.MapTexture.Pointer : IntPtr.Zero) != _prep.FlatPictureOf)
            _prep.FlatUndressed = true;
        // Vanilla makes its zone's box again through a drag, and once more as the drag ends.
        if (PrepDrag() != null)
            _prep.FlatZoneFrames = 10;
        if (_prep.FlatZoneFrames > 0)
            (_prep.FlatZoneFrames, _prep.FlatZonesUndressed) = (_prep.FlatZoneFrames - 1, true);
        if (_prep.FlatUndressed)
        {
            _prep.FlatUndressed = !DressPrepFlat(preview);
            CountPrepFlatDressing();
        }
        else if (_prep.FlatZonesUndressed && UI.Find(preview, UiSelector.Name("DeploymentZones")) is { } zones)
            for (var i = 0; i < zones.childCount; i++)
                zones.ElementAt(i).style.opacity = 0f;
        _prep.FlatZonesUndressed = false;

        // The camera looks straight down from where the hologram's map is seen at the flat
        // map's size and place. A map lying square to the camera is seen at so many pixels a
        // metre whatever part of the frame it is in, so the size is the lens's doing and the
        // place is the camera's.
        if (_prep.LensScale > 0f)
        {
            var perMetre = width * scale / (_prep.MapSizeX * _prep.MapTile);
            var lens = perMetre * PrepFlatEye / 360f;
            var fov = 2f * MathF.Atan(MathF.Tan(PrepFraming.Fov * Mathf.Deg2Rad / 2f) * _prep.LensScale / lens) * Mathf.Rad2Deg;
            // The frame's down is the room's x and its right the room's z, seen from over the table.
            var eye = PrepMapCentre + new Vector3(-(middle.y - 360f) / perMetre, PrepFlatEye, -(middle.x - 640f) / perMetre);
            _prep.FlatShot = (_scene.Room.transform.TransformPoint(eye), fov);
        }
    }

    private static readonly string[] PrepFlatButtons = { "StartMissionButton", "AbortButton" };

    // The shot the camera takes: the orbit's, the flat view's, or on the way between them.
    private (Vector3 Position, Quaternion Rotation, float Fov, float Shift) PrepShot((Vector3 Position, Quaternion Rotation, float Fov, float Shift) orbit)
    {
        if (_prep.FlatAmount <= 0f || _prep.FlatShot is not { } flat)
            return orbit;
        var along = Math.Clamp(Ease.InOutCubic(_prep.FlatAmount), 0f, 1f);
        var down = Quaternion.LookRotation(_scene.Room.transform.TransformDirection(Vector3.down), _scene.Room.transform.TransformDirection(Vector3.left));
        return (Vector3.Lerp(orbit.Position, flat.Eye, along), Quaternion.Slerp(orbit.Rotation, down, along), orbit.Fov + (flat.Fov - orbit.Fov) * along, orbit.Shift * (1f - along));
    }


    // Vanilla makes its map's parts as it learns the map, and its deployment zone again for
    // each squad that is picked up.
    private void HookPrepFlat()
    {
        Context.Patches.Postfix(VanillaUi.Targets.PrepUpdatePreview.Type, VanillaUi.Targets.PrepUpdatePreview.Method, _ => _prep.FlatUndressed = true);
        Context.Patches.Postfix(VanillaUi.Targets.PrepUpdateZones.Type, VanillaUi.Targets.PrepUpdateZones.Method, 0, _ => _prep.FlatZonesUndressed = true);
    }

    // The flat map shows the hologram's own picture of the ground, with its structures, roads,
    // deployment zone and objectives, in place of vanilla's picture and of the boxes vanilla
    // draws over it for each of those. The boxes stay, unseen: they are what the pointer is
    // over. False until the hologram's picture is there to show.
    private bool DressPrepFlat(VisualElement preview)
    {
        preview.style.backgroundColor = (Color)PrepFlatGround;
        if (UI.Find(preview, UiSelector.Name("MapBorder")) is { } border)
        {
            var line = (Color)PrepFlatLine;
            border.style.borderLeftColor = line;
            border.style.borderRightColor = line;
            border.style.borderTopColor = line;
            border.style.borderBottomColor = line;
        }
        // A box is unseen whole: its fill, its outline and whatever vanilla paints in it.
        foreach (var holder in PrepFlatBoxes)
            if (UI.Find(preview, UiSelector.Name(holder)) is { } parts)
                for (var i = 0; i < parts.childCount; i++)
                    parts.ElementAt(i).style.opacity = 0f;
        if (UI.Find(preview, UiSelector.Name("Rects")) is not { } ground)
            return false;
        // Until the hologram's picture is made, vanilla's is not shown in its place.
        if (_prep.MapTexture == null)
        {
            ground.style.unityBackgroundImageTintColor = Color.clear;
            _prep.FlatPictureOf = IntPtr.Zero;
            return false;
        }
        // The picture's top rows are the shades its walls are drawn with, not ground.
        if (_prep.FlatPicture == null || _prep.FlatPictureOf != _prep.MapTexture.Pointer)
        {
            if (_prep.FlatPicture != null)
                UnityEngine.Object.Destroy(_prep.FlatPicture);
            _prep.FlatPictureOf = _prep.MapTexture.Pointer;
            var rows = _prep.MapTexture.height * _prep.MapSizeZ / (_prep.MapSizeZ + 1);
            _prep.FlatPicture = Sprite.Create(_prep.MapTexture, new Rect(0f, 0f, _prep.MapTexture.width, rows), new Vector2(0.5f, 0.5f), 100f);
        }
        ground.style.backgroundImage = new StyleBackground(_prep.FlatPicture);
        ground.style.unityBackgroundImageTintColor = Color.white;
        return true;
    }

    private static readonly string[] PrepFlatBoxes = { "Rects", "DeploymentZones", "ObjectiveAreas" };

    // The button beside the turn and zoom hint that goes between the two views. In the flat
    // view the hint itself goes: there is nothing to turn.
    private void ShowPrepViewToggle()
    {
        if (_panHint == null)
            return;
        var toggle = UI.Find(_panHint, UiSelector.Name(PrepViewToggleName))?.TryCast<Label>();
        if (toggle == null)
        {
            toggle = new Label { name = PrepViewToggleName, pickingMode = PickingMode.Position };
            toggle.AddToClassList("wm-pan-hint__key");
            toggle.AddToClassList("wm-pan-hint__toggle");
            toggle.RegisterCallback((EventCallback<ClickEvent>)(_ =>
            {
                _prep.Flat = !_prep.Flat;
                PlayerSettingsSystem.DeploymentFlatView = _prep.Flat;
                Sound.Click();
            }));
            _panHint.Insert(0, toggle);
        }
        var text = _prep.Flat
            ? Locale.Text("WOMENACE::ui/shell/view_hologram", "3D VIEW")
            : Locale.Text("WOMENACE::ui/shell/view_flat", "2D VIEW");
        if (toggle.text != text)
            toggle.text = text;
        for (var i = 1; i < _panHint.childCount; i++)
            _panHint.ElementAt(i).style.display = _prep.Flat ? DisplayStyle.None : DisplayStyle.Flex;
        // The flat map reaches the panels' foot, so the button stands under it, beside Back.
        _panHint.EnableInClassList(PrepHintLowClass, _prep.Flat);
    }}
