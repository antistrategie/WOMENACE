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

    private bool _prepFlat;
    // 0 while the hologram shows, 1 while the flat map does.
    private float _prepFlatAmount;
    private float? _prepFlatHeld;
    private bool _prepFlatVisit, _prepFlatPlaced;
    // How much of their size the map's tokens stand at: none over the flat map.
    private float _prepTokensUp = 1f;
    // The lens's scale at mission preparation's field of view (FitPrepView reads it).
    private float _prepLensScale;
    // Where the camera looks down from in the flat view, and with what field of view.
    private (Vector3 Eye, float Fov)? _prepFlatShot;

    private void DrivePrepFlat()
    {
        _prepFlatShot = null;
        var column = _shellRoot != null && _policyScreen == VanillaUi.Screens.MissionPrep && _prepMap != null && _room != null
            ? UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.MapColumn)) : null;
        var preview = column != null ? UI.Find(column, UiSelector.Name(VanillaUi.Elements.PrepPreview)) : null;
        if (preview == null || column.panel == null || preview.layout.width <= 0f || _prepMapTile <= 0f)
        {
            (_prepFlatVisit, _prepFlatPlaced, _prepTokensUp) = (false, false, 1f);
            return;
        }
        // A visit starts in the view last chosen, with no move into it.
        if (!_prepFlatVisit)
        {
            _prepFlat = PlayerSettingsSystem.DeploymentFlatView;
            (_prepFlatVisit, _prepFlatAmount, _prepFlatUndressed) = (true, _prepFlat ? 1f : 0f, true);
        }
        var target = _prepFlat ? 1f : 0f;
        if (_prepFlatHeld is { } held)
            _prepFlatAmount = held;
        else if (_prepFlatAmount != target)
        {
            var step = HitchStep(StageLongestStep) / PrepFlatSeconds;
            _prepFlatAmount = Towards(_prepFlatAmount, target, step);
        }
        var along = Ease.InOutCubic(_prepFlatAmount);

        // The hologram is laid flat as the camera comes over it, and what stands on it goes.
        _prepMap.transform.localScale = new Vector3(1f, 1f + (PrepFlatHeight - 1f) * Math.Clamp(along, 0f, 1f), 1f);
        if (_prepMap.GetComponent<MeshRenderer>() is { } hologram)
            hologram.enabled = _prepFlatAmount < 1f;
        _prepTokensUp = 1f - Math.Clamp(_prepFlatAmount * 2.5f, 0f, 1f);
        if (UI.Find(_shellRoot, UiSelector.Name(PrepTokensName)) is { } tokens)
            tokens.style.opacity = _prepTokensUp;
        ShowPrepViewToggle();

        if (_prepFlatAmount <= 0f)
        {
            if (_prepFlatPlaced)
            {
                _prepFlatPlaced = false;
                column.style.scale = StyleKeyword.Null;
                column.style.transformOrigin = StyleKeyword.Null;
                column.style.left = PrepFlatMapParked;
                column.style.top = 0;
                column.style.opacity = _prepFlatMapShown ? 0.55f : 0f;
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
        column.style.opacity = Math.Clamp((_prepFlatAmount - 0.55f) / 0.45f, 0f, 1f);
        if (!_prepFlatPlaced)
        {
            _prepFlatPlaced = true;
            // Vanilla's own two buttons stand in the column, over the map. The shell has them.
            foreach (var name in PrepFlatButtons)
                if (UI.Find(column, UiSelector.Name(name)) is { } button)
                    button.style.translate = new Translate(new Length(PrepFlatMapParked, LengthUnit.Pixel), new Length(0f, LengthUnit.Pixel));
        }
        // The hologram's picture is made again when the map's zone changes (a squad with a
        // zone of its own is picked up), and the one the flat map shows is gone with the old.
        if ((_prepMapTexture != null ? _prepMapTexture.Pointer : IntPtr.Zero) != _prepFlatPictureOf)
            _prepFlatUndressed = true;
        // Vanilla makes its zone's box again through a drag, and once more as the drag ends.
        if (PrepDrag() != null)
            _prepFlatZoneFrames = 10;
        if (_prepFlatZoneFrames > 0)
            (_prepFlatZoneFrames, _prepFlatZonesUndressed) = (_prepFlatZoneFrames - 1, true);
        if (_prepFlatUndressed)
        {
            _prepFlatUndressed = !DressPrepFlat(preview);
            _prepFlatDressings++;
        }
        else if (_prepFlatZonesUndressed && UI.Find(preview, UiSelector.Name("DeploymentZones")) is { } zones)
            for (var i = 0; i < zones.childCount; i++)
                zones.ElementAt(i).style.opacity = 0f;
        _prepFlatZonesUndressed = false;

        // The camera looks straight down from where the hologram's map is seen at the flat
        // map's size and place. A map lying square to the camera is seen at so many pixels a
        // metre whatever part of the frame it is in, so the size is the lens's doing and the
        // place is the camera's.
        if (_prepLensScale > 0f)
        {
            var perMetre = width * scale / (_prepMapSizeX * _prepMapTile);
            var lens = perMetre * PrepFlatEye / 360f;
            var fov = 2f * MathF.Atan(MathF.Tan(PrepFraming.Fov * Mathf.Deg2Rad / 2f) * _prepLensScale / lens) * Mathf.Rad2Deg;
            // The frame's down is the room's x and its right the room's z, seen from over the table.
            var eye = PrepMapCentre + new Vector3(-(middle.y - 360f) / perMetre, PrepFlatEye, -(middle.x - 640f) / perMetre);
            _prepFlatShot = (_room.transform.TransformPoint(eye), fov);
        }
    }

    private static readonly string[] PrepFlatButtons = { "StartMissionButton", "AbortButton" };

    // The shot the camera takes: the orbit's, the flat view's, or on the way between them.
    private (Vector3 Position, Quaternion Rotation, float Fov, float Shift) PrepShot((Vector3 Position, Quaternion Rotation, float Fov, float Shift) orbit)
    {
        if (_prepFlatAmount <= 0f || _prepFlatShot is not { } flat)
            return orbit;
        var along = Math.Clamp(Ease.InOutCubic(_prepFlatAmount), 0f, 1f);
        var down = Quaternion.LookRotation(_room.transform.TransformDirection(Vector3.down), _room.transform.TransformDirection(Vector3.left));
        return (Vector3.Lerp(orbit.Position, flat.Eye, along), Quaternion.Slerp(orbit.Rotation, down, along), orbit.Fov + (flat.Fov - orbit.Fov) * along, orbit.Shift * (1f - along));
    }

    private Sprite _prepFlatPicture;
    // Set when vanilla has made its map's parts, which it does in its own colours.
    private bool _prepFlatUndressed = true;
    // The same for the deployment zone alone, which may be every frame of a drag.
    private bool _prepFlatZonesUndressed;
    private int _prepFlatDressings, _prepFlatZoneFrames;
    // The hologram's picture the flat map's was made from.
    private IntPtr _prepFlatPictureOf;

    // Vanilla makes its map's parts as it learns the map, and its deployment zone again for
    // each squad that is picked up.
    private void HookPrepFlat()
    {
        Context.Patches.Postfix(VanillaUi.Targets.PrepUpdatePreview.Type, VanillaUi.Targets.PrepUpdatePreview.Method, _ => _prepFlatUndressed = true);
        Context.Patches.Postfix(VanillaUi.Targets.PrepUpdateZones.Type, VanillaUi.Targets.PrepUpdateZones.Method, 0, _ => _prepFlatZonesUndressed = true);
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
        if (_prepMapTexture == null)
        {
            ground.style.unityBackgroundImageTintColor = Color.clear;
            _prepFlatPictureOf = IntPtr.Zero;
            return false;
        }
        // The picture's top rows are the shades its walls are drawn with, not ground.
        if (_prepFlatPicture == null || _prepFlatPictureOf != _prepMapTexture.Pointer)
        {
            if (_prepFlatPicture != null)
                UnityEngine.Object.Destroy(_prepFlatPicture);
            _prepFlatPictureOf = _prepMapTexture.Pointer;
            var rows = _prepMapTexture.height * _prepMapSizeZ / (_prepMapSizeZ + 1);
            _prepFlatPicture = Sprite.Create(_prepMapTexture, new Rect(0f, 0f, _prepMapTexture.width, rows), new Vector2(0.5f, 0.5f), 100f);
        }
        ground.style.backgroundImage = new StyleBackground(_prepFlatPicture);
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
                _prepFlat = !_prepFlat;
                PlayerSettingsSystem.DeploymentFlatView = _prepFlat;
                Sound.Click();
            }));
            _panHint.Insert(0, toggle);
        }
        var text = _prepFlat
            ? Locale.Text("WOMENACE::ui/shell/view_hologram", "3D VIEW")
            : Locale.Text("WOMENACE::ui/shell/view_flat", "2D VIEW");
        if (toggle.text != text)
            toggle.text = text;
        for (var i = 1; i < _panHint.childCount; i++)
            _panHint.ElementAt(i).style.display = _prepFlat ? DisplayStyle.None : DisplayStyle.Flex;
        // The flat map reaches the panels' foot, so the button stands under it, beside Back.
        _panHint.EnableInClassList(PrepHintLowClass, _prepFlat);
    }

    internal static string PrepFlatFromDev(bool flat, float held)
    {
        var d = _instance;
        if (d == null)
            return "no diorama";
        d._prepFlat = flat;
        d._prepFlatHeld = held < 0f ? null : held;
        return $"{(flat ? "flat" : "hologram")} view{(held < 0f ? "" : $", held at {held:0.00}")}, now at {d._prepFlatAmount:0.00}, dressed {d._prepFlatDressings} times, shot {(d._prepFlatShot is { } shot ? $"fov {shot.Fov:0.0}" : "none")}";
    }
}
