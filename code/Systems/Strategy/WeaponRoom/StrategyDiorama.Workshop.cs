using Il2CppMenace.Items;
using Jiangyu.Game.Audio;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using UnityEngine;

namespace WOMENACE.Code;

// The Workshop in the weapon room. Its three tabs are three places: assembling at one of the
// hall's two shelf racks, taking apart at the rack facing it, and calibration at the lounge's
// table. The weapon being worked on is shown at the place, and every shot looks away from the
// room's doorway, which opens on nothing.
public sealed partial class StrategyDiorama
{
    // A station's shot, and how what is worked on is shown there, all in the weapon room's space:
    // where its middle rests (or, laid on a surface, where its middle is over and the height of
    // the surface its underside lies on), which way its barrel points and which way its top
    // faces (a weapon's model has its barrel along its length and its top along +y), whether
    // it is a hologram, and whether it casts a shadow.
    private readonly record struct Station(Framing Framing, Vector3 Rest, Vector3 Barrel, Vector3 Top, bool Hologram, bool Shadow, bool Laid = false);

    // A rack's shelves are panels leaning back at 57 degrees, 0.41 m up the slope: this is the
    // middle one's normal on the Disassemble rack, and the way up its slope.
    private static readonly Vector3 ShelfNormal = new(0f, 0.54f, 0.84f);
    private static readonly Vector3 ShelfUp = new(0f, 0.84f, -0.54f);

    private static readonly Station[] WorkshopStations =
    {
        // Assemble: the rack seen as the facing one is for Disassemble. What a blueprint makes
        // does not exist yet, and not all of it would fit a shelf, so it floats as a hologram
        // between the shot and the rack, side-on to the shot.
        new(new Framing(new Vector3(-31.9f, 20.5f, -93.4f), new Vector3(-34.4f, 20.3f, -89.3f), 50f),
            new Vector3(-33.4f, 20.4f, -90.94f), new Vector3(0.85f, 0f, 0.52f), Vector3.up, Hologram: true, Shadow: false),
        // Disassemble: the weapon itself lies against the facing rack's middle shelf, in the
        // shelf's own plane, and casts its shadow on it.
        new(new Framing(new Vector3(-31.9f, 20.5f, -97.9f), new Vector3(-34.4f, 20.3f, -102.0f), 50f),
            new Vector3(-34.6f, 20.39f, -102.214f) + ShelfNormal * 0.035f, Vector3.right, ShelfUp, Hologram: false, Shadow: true),
        // Calibrate: close on the lounge's table, where the weapon lies on its side on the
        // table's top (at 19.682), pointing at the wall behind, with the armchair its doll sits
        // in on the left. Its shadow falls straight under it, from the room's overhead shadow
        // light, and is what sets it on the table.
        new(new Framing(new Vector3(-28.2f, 20.5f, -96.4f), new Vector3(-24.9f, 19.75f, -95.2f), 50f),
            new Vector3(-24.75f, 19.684f, -95.7f), Vector3.right, Vector3.back, Hologram: false, Shadow: true, Laid: true),
    };

    // GFL2's loose gun parts on the lounge's table, which make way for a weapon shown on it.
    private const string TableClutter = "_ArtNode/_Environment/WeaponRoom_Lobby/_Staticstate/Bullet";
    // How wide a thing with no model of its own is shown, as its picture on a card.
    private const float PictureSize = 0.8f;
    // A hologram is shown larger than life and brighter than the recruit's, to read against the rack.
    private const float HologramScale = 1.3f;
    private const float HologramIntensity = 2.2f;

    private const int AssembleStation = 0, DisassembleStation = 1, CalibrateStation = 2;

    // Where the Workshop is working, from what its panels are doing.
    private static int WorkshopStation =>
        CalibrationUISystem.IsOpen ? CalibrateStation
        : WorkshopSystem.Instance is { Disassembling: true } ? DisassembleStation
        : AssembleStation;

    // Sets the Workshop's panels to a station's work. The camera follows what they are doing.
    private static void GoToStation(int station)
    {
        Sound.Click();
        SetStation(station);
    }

    private static void SetStation(int station)
    {
        if (station == CalibrateStation)
        {
            CalibrationUISystem.Instance?.Open();
            return;
        }
        CalibrationUISystem.Instance?.Close();
        WorkshopSystem.Instance?.SetDisassembling(station == DisassembleStation);
    }

    private const string WorkedOnName = "WOMENACE_WorkedOn";
    private const float DisplayLumens = 3f;
    private const int WorkshopLookFrames = 6;


    // What the Workshop showed goes with its room, and is made again when the room is. The rest of
    // its state goes with the scene.
    private void ClearWorkshop()
    {
        foreach (var material in _weaponRoom.WorkedOnHolograms)
            if (material != null)
                UnityEngine.Object.Destroy(material);
        _weaponRoom.WorkedOnHolograms.Clear();
        if (_weaponRoom.CalibrationDoll != null)
            _life.DollRigs.Remove(_weaponRoom.CalibrationDoll.Pointer);
    }

    // What is being worked on, at the station it is worked on at: a weapon's own model, or the
    // picture of a thing that has none (armour, a calibration core) on a card.
    private void ShowWorkedOnWeapon()
    {
        var room = _scene.Weapon.Root;
        if (room == null)
            return;
        // Vanilla greys the Assemble button's word where the project's materials are short and
        // marks the button no other way, so the shell's rules are told by a class.
        if (_shellRoot != null && Jiangyu.Game.Ui.UI.Find(_shellRoot, Jiangyu.Game.Ui.UiSelector.Name("AssembleButton")) is { } assemble)
        {
            var materialsShort = UIManager.Get()?.GetActiveScreen()?.TryCast<WorkshopUIScreen>()?.m_SelectedBlueprint?.OwnsPlayerAllCraftingMaterials() != true;
            Inline(assemble, e => e.EnableInClassList("wm-workshop__off", materialsShort), e => e.RemoveFromClassList("wm-workshop__off"));
        }
        var station = WorkshopStation;
        // With no project picked there is nothing to say under the station, and the strip is
        // not drawn: an empty box with a heading over nothing.
        if (_shellRoot != null && Jiangyu.Game.Ui.UI.Find(_shellRoot, Jiangyu.Game.Ui.UiSelector.Name("AssemblePanel")) is { } strip)
        {
            var takes = Jiangyu.Game.Ui.UI.Find(strip, Jiangyu.Game.Ui.UiSelector.Name("ReqMaterialsContainer"));
            var nothing = station == AssembleStation && (takes == null || takes.childCount == 0);
            Inline(strip, e => e.style.visibility = nothing ? UnityEngine.UIElements.Visibility.Hidden : UnityEngine.UIElements.StyleKeyword.Null,
                e => e.style.visibility = UnityEngine.UIElements.StyleKeyword.Null);
        }
        var template = station == CalibrateStation
            ? CalibrationUISystem.Instance?.Picked?.Item?.GetTemplate()
            : WorkshopSystem.Instance?.WorkedOn(UIManager.Get()?.GetActiveScreen()?.TryCast<WorkshopUIScreen>());
        var pointer = template != null ? template.Pointer : IntPtr.Zero;
        if (pointer == _weaponRoom.WorkedOnTemplate && station == _weaponRoom.WorkedOnStation && (_weaponRoom.WorkedOn != null || _weaponRoom.WorkedOnNothing))
            return;
        (_weaponRoom.WorkedOnTemplate, _weaponRoom.WorkedOnStation) = (pointer, station);
        // Its name, in the strip under it.
        if (_shellRoot != null && Jiangyu.Game.Ui.UI.Find(_shellRoot, Jiangyu.Game.Ui.UiSelector.Name(WorkshopNameName))?.TryCast<UnityEngine.UIElements.Label>() is { } named)
            named.text = template != null ? (Templates.DefaultText(template.Title) ?? "").ToUpperInvariant() : "";
        if (_shellRoot != null && Jiangyu.Game.Ui.UI.Find(_shellRoot, Jiangyu.Game.Ui.UiSelector.Name(WorkshopImageName)) is { } pictured)
        {
            var picture = template != null ? template.TryCast<WeaponTemplate>()?.IconEquipment ?? template.Icon : null;
            pictured.style.backgroundImage = picture != null ? new UnityEngine.UIElements.StyleBackground(picture) : UnityEngine.UIElements.StyleKeyword.None;
        }
        // Nothing to show, until something is made below: asked once, not on every look.
        _weaponRoom.WorkedOnNothing = true;
        if (_weaponRoom.WorkedOn != null)
            UnityEngine.Object.Destroy(_weaponRoom.WorkedOn);
        _weaponRoom.WorkedOn = null;
        foreach (var material in _weaponRoom.WorkedOnHolograms)
            if (material != null)
                UnityEngine.Object.Destroy(material);
        _weaponRoom.WorkedOnHolograms.Clear();
        var clutter = room.transform.Find(TableClutter);
        if (clutter != null)
            clutter.gameObject.SetActive(!(station == CalibrateStation && template != null));
        if (template == null)
            return;
        var rest = WorkshopStations[station];
        var weapon = template.TryCast<WeaponTemplate>();
        var model = WeaponModel(weapon);
        var at = room.transform.TransformPoint(rest.Rest);
        var eye = room.transform.TransformPoint(rest.Framing.Eye);
        if (model != null)
        {
            _weaponRoom.WorkedOn = UnityEngine.Object.Instantiate(model, room.transform, false);
            _weaponRoom.WorkedOn.transform.localRotation = WeaponTurn(_weaponRoom.WorkedOn, rest.Barrel, rest.Top);
            if (rest.Hologram)
                _weaponRoom.WorkedOn.transform.localScale *= HologramScale;
        }
        else
        {
            var picture = weapon?.IconEquipment ?? template.Icon;
            if (picture == null || picture.texture == null || HologramMaterial() == null)
                return;
            _weaponRoom.WorkedOn = IconCard(room.transform, picture, PictureSize);
            // The card faces the shot.
            var away = at - eye;
            away.y = 0f;
            _weaponRoom.WorkedOn.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            var card = IconHologram(picture);
            _weaponRoom.WorkedOn.GetComponent<Renderer>().sharedMaterial = card;
            _weaponRoom.WorkedOnHolograms.Add(card);
        }
        _weaponRoom.WorkedOn.name = WorkedOnName;
        _weaponRoom.WorkedOnNothing = false;
        // Its middle goes to the rest, wherever in the model its origin is.
        var taken = WeaponBounds(_weaponRoom.WorkedOn, rest.Shadow);
        var any = taken != null;
        var bounds = taken ?? default;
        var from = any ? bounds.center : _weaponRoom.WorkedOn.transform.position;
        if (rest.Laid && any)
            from.y = bounds.min.y;
        _weaponRoom.WorkedOn.transform.position += at - from;
        if (rest.Hologram && model != null && HologramMaterial() != null)
        {
            // Every part wears the hologram with its own texture's light and dark.
            foreach (var renderer in _weaponRoom.WorkedOn.GetComponentsInChildren<Renderer>(true))
                WearHolograms(renderer, _weaponRoom.WorkedOnHolograms);
        }
        if (_weaponRoom.WorkedOnHolograms.Count > 0)
        {
            // Drawn whole, its lines measured from its own foot.
            var foot = any ? bounds.min.y + (at.y - bounds.center.y) : at.y - PictureSize * 0.5f;
            var tall = any ? Mathf.Max(bounds.size.y, 0.1f) : PictureSize;
            foreach (var material in _weaponRoom.WorkedOnHolograms)
            {
                material.SetFloat(HologramFloorId, foot);
                material.SetFloat(HologramHeightId, tall);
                material.SetFloat(HologramRevealId, 1.1f);
                material.SetFloat("_Intensity", HologramIntensity);
            }
            return;
        }
        // The room's own light is dim on what it did not bake, so a weapon has a light of its
        // own, from the camera's side and above.
        var lamp = RigLight(_weaponRoom.WorkedOn, "WOMENACE_DisplayLight", new Color(1f, 0.93f, 0.84f), 3f);
        lamp.transform.position = at + (eye - at).normalized * 0.7f + Vector3.up * 0.5f;
        lamp.intensity = DisplayLumens * RigLightScale;
    }

    // The model that is the weapon itself. A weapon may carry two: a machine gun's first is its
    // ammunition pack and its second the gun, and another's second is the rig that holds its
    // gun in a mission. The gun is the one with a muzzle, and the second where that does not
    // tell them apart.
    private static GameObject WeaponModel(WeaponTemplate weapon)
    {
        if (weapon == null)
            return null;
        WeaponModelSystem.Ensure(weapon);
        if (weapon.ModelSecondary == null || weapon.Model == null)
            return weapon.ModelSecondary != null ? weapon.ModelSecondary : weapon.Model;
        return HasMuzzle(weapon.Model) && !HasMuzzle(weapon.ModelSecondary) ? weapon.Model : weapon.ModelSecondary;
    }

    private static bool HasMuzzle(GameObject model) => Muzzle(model) != null;

    // A model's muzzle empty, where its barrel ends.
    private static Transform Muzzle(GameObject model)
    {
        foreach (var part in model.GetComponentsInChildren<Transform>(true))
            if (part.name.StartsWith("muzzle", StringComparison.OrdinalIgnoreCase))
                return part;
        return null;
    }

    // How a weapon's model is turned for its barrel to point one way and its top another. Models
    // do not agree on which way a barrel runs: it runs from the model's origin, the grip, towards
    // its muzzle.
    private static Quaternion WeaponTurn(GameObject shown, Vector3 barrelTo, Vector3 topTo)
    {
        var barrel = Vector3.forward;
        if (Muzzle(shown) is { } muzzle)
        {
            var along = shown.transform.InverseTransformPoint(muzzle.position);
            barrel = Mathf.Abs(along.x) > Mathf.Abs(along.z) ? new Vector3(Mathf.Sign(along.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(along.z));
        }
        return Quaternion.LookRotation(barrelTo, topTo) * Quaternion.Inverse(Quaternion.LookRotation(barrel, Vector3.up));
    }

    // What a shown thing's meshes take up, with its shadows set on the way. None for no mesh.
    private static Bounds? WeaponBounds(GameObject shown, bool shadows)
    {
        Bounds? bounds = null;
        foreach (var renderer in shown.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.GetIl2CppType().Name is not ("MeshRenderer" or "SkinnedMeshRenderer"))
                continue;
            if (bounds is { } sofar)
            {
                sofar.Encapsulate(renderer.bounds);
                bounds = sofar;
            }
            else
                bounds = renderer.bounds;
            renderer.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        return bounds;
    }

}
