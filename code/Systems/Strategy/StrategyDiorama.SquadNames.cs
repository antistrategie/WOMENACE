using Jiangyu.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Each unit of the squad standing in the refitting room has its name lying on the floor in front
// of it, where vanilla floats a label under each unit that tracks its own camera. The names are
// drawn by a panel of their own into one texture, a row each, and every unit's plate is a hologram
// quad showing its row. They are in the shell's face (MENACE's Jura Bold), read off its title.
//
// The leader stands in the first slot and her squaddies in the rest, in her squad's order, as the
// armoury spawns them. The plates are placed again whenever the squad is (StrategyDiorama.Armory.cs),
// not every frame: the units stand still once they have settled.
public sealed partial class StrategyDiorama
{
    // The infantry root of the armoury's squad, and the most units it holds.
    private const string SquadFullRoot = "Squad_Full";
    private const int NamePlateRows = 9;
    // A row of the texture, and the plate's width on the floor, in metres.
    private const int NamePlateTextureWidth = 1024;
    private const int NamePlateRowHeight = 128;
    private const int NamePlateFontSize = 72;
    private const float NamePlateWidth = 1.8f;
    // How far in front of a unit's feet the plate lies, and how far over the floor.
    private const float NamePlateAhead = 0.5f;
    private const float NamePlateLift = 0.01f;
    private const float NamePlateIntensity = 1.4f;

    // The way the squad faces, towards the room camera's starting shot, flat on the floor.
    private Vector3 _squadFacing = Vector3.back;
    private GameObject _namePlateHost;
    private PanelSettings _namePlateSettings;
    private RenderTexture _namePlateTexture;
    private readonly Label[] _namePlateLabels = new Label[NamePlateRows];
    private readonly GameObject[] _namePlates = new GameObject[NamePlateRows];
    // Each plate's renderer, which the mirror turns off and on around every render it makes.
    private readonly MeshRenderer[] _namePlateRenderers = new MeshRenderer[NamePlateRows];

    // The texture, its labels and the plates, made once while the squad is staged.
    private bool NamePlatePicture()
    {
        if (_namePlates[0] != null)
            return true;
        var template = MarkerTemplate();
        if (template == null || _stage == null)
            return false;
        if (!TexturePanel("WOMENACE_SquadNames", _shellRoot, NamePlateTextureWidth, NamePlateRowHeight * NamePlateRows, 1f,
            out _namePlateHost, out _namePlateSettings, out _namePlateTexture, out var root))
            return false;
        var heading = _shell != null ? UI.Find(_shell, UiSelector.Name("wm-shell-title")) : null;
        for (var row = 0; row < NamePlateRows; row++)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            var style = label.style;
            style.position = Position.Absolute;
            style.left = 0;
            style.right = 0;
            style.top = row * NamePlateRowHeight;
            style.height = NamePlateRowHeight;
            style.fontSize = NamePlateFontSize;
            style.color = SquadMetaInk;
            style.unityTextAlign = TextAnchor.MiddleCenter;
            style.marginLeft = style.marginRight = style.marginTop = style.marginBottom = 0;
            style.paddingLeft = style.paddingRight = style.paddingTop = style.paddingBottom = 0;
            if (heading != null)
                style.unityFontDefinition = heading.resolvedStyle.unityFontDefinition;
            root.Add(label);
            _namePlateLabels[row] = label;

            var plate = Quad(template, $"WOMENACE_SquadName_{row}", _namePlateTexture);
            plate.transform.SetParent(_stage, false);
            plate.transform.localScale = new Vector3(NamePlateWidth, NamePlateWidth * NamePlateRowHeight / NamePlateTextureWidth, 1f);
            PlainHologram(plate, NamePlateIntensity);
            var renderer = plate.GetComponent<MeshRenderer>();
            renderer.material.SetVector(UvRectId, SheetCell(row, 1, NamePlateRows));
            plate.SetActive(false);
            (_namePlates[row], _namePlateRenderers[row]) = (plate, renderer);
        }
        return true;
    }

    // Each standing unit's plate in front of it, with its name. The rest are put away.
    private void PlaceNamePlates()
    {
        var squad = SquadFull();
        if (squad == null || !NamePlatePicture())
        {
            foreach (var plate in _namePlates)
                plate?.SetActive(false);
            return;
        }
        var leader = Affinity.LeaderOf(_squadWindow);
        var leaderName = UI.Find(_squadWindow, UiSelector.Name(VanillaUi.Elements.LeaderName)) is Label shown ? shown.text : null;
        // Its top is away from the camera, so it reads from where the squad is shot.
        var lying = Quaternion.LookRotation(Vector3.down, -_squadFacing);
        var floor = _stage.TransformPoint(ArmoryStand).y + NamePlateLift;
        for (var row = 0; row < NamePlateRows; row++)
        {
            var unit = row < squad.childCount ? StandingUnit(squad.GetChild(row)) : null;
            var name = unit == null ? null : row == 0 ? leaderName : leader?.GetSquaddie(row - 1)?.Name;
            var plate = _namePlates[row];
            if (string.IsNullOrEmpty(name))
            {
                plate.SetActive(false);
                continue;
            }
            var text = name.ToUpperInvariant();
            if (_namePlateLabels[row].text != text)
                _namePlateLabels[row].text = text;
            var at = unit.position + _squadFacing * NamePlateAhead;
            at.y = floor;
            plate.transform.SetPositionAndRotation(at, lying);
            plate.SetActive(true);
        }
    }

    // The floor's mirror leaves the plates out: they lie on the floor, and a name reflected
    // under itself reads as a smudge.
    private void ShowNamePlates(bool shown)
    {
        foreach (var renderer in _namePlateRenderers)
            if (renderer != null)
                renderer.enabled = shown;
    }

    private Transform SquadFull()
    {
        foreach (var staged in _stagedRoots)
            if (staged.Root != null && staged.Root.name == SquadFullRoot && staged.Root.gameObject.activeInHierarchy)
                return staged.Root;
        return null;
    }

    // The unit standing in an armoury slot, or null when the slot is empty.
    private static Transform StandingUnit(Transform slot)
    {
        for (var i = 0; i < slot.childCount; i++)
            if (slot.GetChild(i).gameObject.activeInHierarchy)
                return slot.GetChild(i);
        return null;
    }

    private void ClearNamePlates()
    {
        for (var row = 0; row < NamePlateRows; row++)
        {
            if (_namePlates[row] != null)
            {
                UnityEngine.Object.Destroy(_namePlateRenderers[row]?.sharedMaterial);
                UnityEngine.Object.Destroy(_namePlates[row]);
            }
            (_namePlates[row], _namePlateRenderers[row], _namePlateLabels[row]) = (null, null, null);
        }
        foreach (var made in new UnityEngine.Object[] { _namePlateHost, _namePlateSettings })
            if (made != null)
                UnityEngine.Object.Destroy(made);
        ReleaseTexture(ref _namePlateTexture);
        (_namePlateHost, _namePlateSettings) = (null, null);
    }
}
