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
//
// A support team (SupportTeams) is one unit holding its whole crew and takes no squaddies, so its
// crew have the rows instead: a plate each, on the floor just in front of its crew member. No two
// crew stand in one place, so the names never lie over each other, where a line across the team's
// front would crowd two members one behind the other from the camera's side.
public sealed partial class StrategyDiorama
{
    // The infantry root of the armoury's squad, and the most units it holds.
    private const string SquadFullRoot = "Squad_Full";
    private const int NamePlateRows = 9;
    // A row of the texture, and the plate's width on the floor. Lengths on the floor are in the
    // stage's metres: a plate is a child of the stage and scales with it, so an offset from a unit
    // is scaled by the stage too.
    private const int NamePlateTextureWidth = 1024;
    private const int NamePlateRowHeight = 128;
    private const int NamePlateFontSize = 72;
    private const float NamePlateWidth = 1.8f;
    // How far in front of a unit's feet the plate lies, and how far over the floor.
    private const float NamePlateAhead = 0.5f;
    private const float NamePlateLift = 0.01f;
    private const float NamePlateIntensity = 1.4f;
    // A crew member's plate is narrower than a unit's, since the crew stand closer together than
    // squad slots, and lies this far in front of her.
    private const float CrewNamePlateWidth = 1.2f;
    private const float CrewNameAhead = 0.3f;
    // The bone in a crew member's rig that places her.
    private const string CrewHipsBone = "Root_M";


    // The texture, its labels and the plates, made once while the squad is staged.
    private bool NamePlatePicture()
    {
        if (_staging.NamePlates[0] != null)
            return true;
        var template = MarkerTemplate();
        if (template == null || _staging.Stage == null)
            return false;
        if (!TexturePanel("WOMENACE_SquadNames", _shellRoot, NamePlateTextureWidth, NamePlateRowHeight * NamePlateRows, 1f,
            out _staging.NamePlateHost, out _staging.NamePlateSettings, out _staging.NamePlateTexture, out var root))
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
            _staging.NamePlateLabels[row] = label;

            var plate = Quad(template, $"WOMENACE_SquadName_{row}", _staging.NamePlateTexture);
            plate.transform.SetParent(_staging.Stage, false);
            plate.transform.localScale = new Vector3(NamePlateWidth, NamePlateWidth * NamePlateRowHeight / NamePlateTextureWidth, 1f);
            PlainHologram(plate, NamePlateIntensity);
            var renderer = plate.GetComponent<MeshRenderer>();
            renderer.material.SetVector(UvRectId, SheetCell(row, 1, NamePlateRows));
            plate.SetActive(false);
            (_staging.NamePlates[row], _staging.NamePlateRenderers[row]) = (plate, renderer);
        }
        return true;
    }

    // Each standing unit's plate in front of it, with its name. The rest are put away.
    private void PlaceNamePlates()
    {
        var squad = SquadFull();
        if (squad == null || !NamePlatePicture())
        {
            foreach (var plate in _staging.NamePlates)
                plate?.SetActive(false);
            return;
        }
        var leader = Affinity.LeaderOf(_staging.SquadWindow);
        var leaderName = UI.Find(_staging.SquadWindow, UiSelector.Name(VanillaUi.Elements.LeaderName)) is Label shown ? shown.text : null;
        // Its top is away from the camera, so it reads from where the squad is shot.
        var lying = Quaternion.LookRotation(Vector3.down, -_staging.SquadFacing);
        var floor = _staging.Stage.TransformPoint(ArmoryStand).y + NamePlateLift;
        var scale = _staging.Stage.lossyScale.x;
        var team = SupportTeams.Of(leader);
        if (team != null && squad.childCount > 0 && PlaceCrewPlates(team, StandingUnit(squad.GetChild(0)), lying, floor, scale))
            return;
        var ahead = NamePlateAhead * scale;
        for (var row = 0; row < NamePlateRows; row++)
        {
            var unit = row < squad.childCount ? StandingUnit(squad.GetChild(row)) : null;
            var name = unit == null ? null : row == 0 ? leaderName : leader?.GetSquaddie(row - 1)?.Name;
            var plate = _staging.NamePlates[row];
            if (string.IsNullOrEmpty(name))
            {
                plate.SetActive(false);
                continue;
            }
            var text = name.ToUpperInvariant();
            var at = unit.position + _staging.SquadFacing * ahead;
            at.y = floor;
            ShowNamePlate(row, text, NamePlateWidth, at, lying);
        }
    }

    private void ShowNamePlate(int row, string text, float width, Vector3 at, Quaternion lying)
    {
        if (_staging.NamePlateLabels[row].text != text)
            _staging.NamePlateLabels[row].text = text;
        var plate = _staging.NamePlates[row].transform;
        plate.localScale = new Vector3(width, width * NamePlateRowHeight / NamePlateTextureWidth, 1f);
        plate.SetPositionAndRotation(at, lying);
        plate.gameObject.SetActive(true);
    }

    // Each crew member's plate, false when the team's body is not standing or a member's rig is not
    // found in it, which leaves the unit to the plain leader plate.
    private bool PlaceCrewPlates(SupportTeams.Team team, Transform unit, Quaternion lying, float floor, float scale)
    {
        var count = team.CrewCount;
        if (unit == null || count > NamePlateRows)
            return false;
        var hips = new Transform[count];
        for (var i = 0; i < count; i++)
        {
            var member = FindDescendant(unit, team.Crew[i].Node);
            hips[i] = member != null ? FindDescendant(member, CrewHipsBone) : null;
            if (hips[i] == null)
                return false;
        }
        var ahead = CrewNameAhead * scale;
        for (var i = 0; i < count; i++)
        {
            var at = hips[i].position + _staging.SquadFacing * ahead;
            at.y = floor;
            ShowNamePlate(i, team.Crew[i].Name.ToUpperInvariant(), CrewNamePlateWidth, at, lying);
        }
        for (var row = count; row < NamePlateRows; row++)
            _staging.NamePlates[row].SetActive(false);
        return true;
    }

    // The floor's mirror leaves the plates out: they lie on the floor, and a name reflected
    // under itself reads as a smudge.
    private void ShowNamePlates(bool shown)
    {
        foreach (var renderer in _staging.NamePlateRenderers)
            if (renderer != null)
                renderer.enabled = shown;
    }

    private Transform SquadFull()
    {
        foreach (var staged in _staging.StagedRoots)
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
            if (_staging.NamePlates[row] != null)
            {
                UnityEngine.Object.Destroy(_staging.NamePlateRenderers[row]?.sharedMaterial);
                UnityEngine.Object.Destroy(_staging.NamePlates[row]);
            }
            (_staging.NamePlates[row], _staging.NamePlateRenderers[row], _staging.NamePlateLabels[row]) = (null, null, null);
        }
        foreach (var made in new UnityEngine.Object[] { _staging.NamePlateHost, _staging.NamePlateSettings })
            if (made != null)
                UnityEngine.Object.Destroy(made);
        ReleaseTexture(ref _staging.NamePlateTexture);
        (_staging.NamePlateHost, _staging.NamePlateSettings) = (null, null);
    }
}
