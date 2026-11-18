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
    private GameObject _workedOn;
    private IntPtr _workedOnTemplate;
    private bool _workedOnNothing;
    private const int WorkshopLookFrames = 6;
    private int _workedOnStation = -1;

    private readonly List<Material> _workedOnHolograms = new();

    // What the Workshop showed goes with its room, and is made again when the room is.
    private void ClearWorkshop()
    {
        foreach (var material in _workedOnHolograms)
            if (material != null)
                UnityEngine.Object.Destroy(material);
        _workedOnHolograms.Clear();
        if (_calibrationDoll != null)
            _dollRigs.Remove(_calibrationDoll.Pointer);
        (_workedOn, _workedOnTemplate, _workedOnStation, _workedOnNothing) = (null, IntPtr.Zero, -1, false);
        (_calibrationDoll, _calibrationDollTag, _calibrationLeader, _calibrationTag) = (null, null, IntPtr.Zero, null);
    }

    // What is being worked on, at the station it is worked on at: a weapon's own model, or the
    // picture of a thing that has none (armour, a calibration core) on a card.
    private void ShowWorkedOnWeapon()
    {
        var room = _weaponSet.Root;
        if (room == null)
            return;
        // Vanilla greys the Assemble button's word where the project's materials are short and
        // marks the button no other way, so the shell's rules are told by a class.
        if (_shellRoot != null && Jiangyu.Game.Ui.UI.Find(_shellRoot, Jiangyu.Game.Ui.UiSelector.Name("AssembleButton")) is { } assemble)
            assemble.EnableInClassList("wm-workshop__off",
                UIManager.Get()?.GetActiveScreen()?.TryCast<WorkshopUIScreen>()?.m_SelectedBlueprint?.OwnsPlayerAllCraftingMaterials() != true);
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
        if (pointer == _workedOnTemplate && station == _workedOnStation && (_workedOn != null || _workedOnNothing))
            return;
        (_workedOnTemplate, _workedOnStation) = (pointer, station);
        // Its name, in the strip under it.
        if (_shellRoot != null && Jiangyu.Game.Ui.UI.Find(_shellRoot, Jiangyu.Game.Ui.UiSelector.Name(WorkshopNameName))?.TryCast<UnityEngine.UIElements.Label>() is { } named)
            named.text = template != null ? (Templates.DefaultText(template.Title) ?? "").ToUpperInvariant() : "";
        if (_shellRoot != null && Jiangyu.Game.Ui.UI.Find(_shellRoot, Jiangyu.Game.Ui.UiSelector.Name(WorkshopImageName)) is { } pictured)
        {
            var picture = template != null ? template.TryCast<WeaponTemplate>()?.IconEquipment ?? template.Icon : null;
            pictured.style.backgroundImage = picture != null ? new UnityEngine.UIElements.StyleBackground(picture) : UnityEngine.UIElements.StyleKeyword.None;
        }
        // Nothing to show, until something is made below: asked once, not on every look.
        _workedOnNothing = _weaponSet.Root != null;
        if (_workedOn != null)
            UnityEngine.Object.Destroy(_workedOn);
        _workedOn = null;
        foreach (var material in _workedOnHolograms)
            if (material != null)
                UnityEngine.Object.Destroy(material);
        _workedOnHolograms.Clear();
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
            _workedOn = UnityEngine.Object.Instantiate(model, room.transform, false);
            _workedOn.transform.localRotation = WeaponTurn(_workedOn, rest.Barrel, rest.Top);
            if (rest.Hologram)
                _workedOn.transform.localScale *= HologramScale;
        }
        else
        {
            var picture = weapon?.IconEquipment ?? template.Icon;
            if (picture == null || picture.texture == null || HologramMaterial() == null)
                return;
            _workedOn = IconCard(room.transform, picture, PictureSize);
            // The card faces the shot.
            var away = at - eye;
            away.y = 0f;
            _workedOn.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            var card = IconHologram(picture);
            _workedOn.GetComponent<Renderer>().sharedMaterial = card;
            _workedOnHolograms.Add(card);
        }
        _workedOn.name = WorkedOnName;
        _workedOnNothing = false;
        // Its middle goes to the rest, wherever in the model its origin is.
        var taken = WeaponBounds(_workedOn, rest.Shadow);
        var any = taken != null;
        var bounds = taken ?? default;
        var from = any ? bounds.center : _workedOn.transform.position;
        if (rest.Laid && any)
            from.y = bounds.min.y;
        _workedOn.transform.position += at - from;
        if (rest.Hologram && model != null && HologramMaterial() != null)
        {
            // Every part wears the hologram with its own texture's light and dark.
            foreach (var renderer in _workedOn.GetComponentsInChildren<Renderer>(true))
            {
                Material[] own = renderer.sharedMaterials;
                var worn = new Material[own.Length];
                for (var i = 0; i < own.Length; i++)
                {
                    worn[i] = HologramOf(own[i]);
                    _workedOnHolograms.Add(worn[i]);
                }
                renderer.sharedMaterials = worn;
            }
        }
        if (_workedOnHolograms.Count > 0)
        {
            // Drawn whole, its lines measured from its own foot.
            var foot = any ? bounds.min.y + (at.y - bounds.center.y) : at.y - PictureSize * 0.5f;
            var tall = any ? Mathf.Max(bounds.size.y, 0.1f) : PictureSize;
            foreach (var material in _workedOnHolograms)
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
        var lamp = RigLight(_workedOn, "WOMENACE_DisplayLight", new Color(1f, 0.93f, 0.84f), 3f);
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

    private static bool HasMuzzle(GameObject model)
    {
        foreach (var part in model.GetComponentsInChildren<Transform>(true))
            if (part.name.StartsWith("muzzle", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    // The armchair on the left of the calibration shot, which the side panels leave in view: the
    // middle of its seat and the seat's height, in the weapon room's space. It faces the table,
    // along -z.
    private static readonly Vector3 LoungeSeat = new(-24.75f, 19.69f, -93.95f);
    private static readonly Quaternion LoungeSeatFacing = Quaternion.Euler(0f, 180f, 0f);
    // From the seat's middle to its front edge, and how far past that edge a sitter's knees are.
    private const float LoungeSeatHalfDepth = 0.47f;
    private const float KneesPastSeat = 0.05f;
    private const float AnklesPastSeat = 0.07f;
    // How far over the seat the middle of a thigh is where it crosses the seat's front edge.
    private const float ThighOverSeat = 0.045f;
    // Where a doll with no sitting loop stands instead: between the table and the other
    // armchair, turned to the hall.
    private static readonly Vector3 LoungeStand = new(-24.3f, 19.19f, -96.6f);
    // The lounge's floor height, in the weapon room's space.
    private const float LoungeFloor = 19.19f;
    // How far a seated doll's soles stay above it, and how far back she may lean to keep them there.
    private const float SoleClearance = 0.005f;
    private const float MaxSeatedLean = 15f;
    // How far a sitter's hip joints are above what she sits on.
    private const float SeatedHipHeight = 0.09f;
    private const int CalibrationDollRelightFrames = 30;
    private GameObject _calibrationDoll;
    private string _calibrationDollTag;
    private IntPtr _calibrationLeader;
    private string _calibrationTag;
    private string _calibrationWeapon;

    // The doll whose weapon is being calibrated sits in the lounge with it.
    private void ShowCalibrationDoll()
    {
        var lit = false;
        var room = _weaponSet.Root;
        var instance = WorkshopStation == CalibrateStation ? CalibrationUISystem.Instance?.Picked : null;
        var leader = instance?.Leader;
        // The doll who carries the weapon, or for one in stock the doll whose weapon it is.
        // Her tag is read when the leader or the weapon picked changes, and not on every look.
        var picked = leader != null ? leader.Pointer : IntPtr.Zero;
        var weapon = leader == null ? instance?.BaseWeaponId : null;
        if (picked != _calibrationLeader || weapon != _calibrationWeapon)
            (_calibrationLeader, _calibrationWeapon, _calibrationTag) = (picked, weapon,
                leader != null ? Affinity.CharacterTag(leader) : weapon != null ? Calibration.OwnerTagFor(weapon) : null);
        var tag = _calibrationTag;
        if (tag != _calibrationDollTag || (tag != null && _calibrationDoll == null))
        {
            _calibrationDollTag = tag;
            if (_calibrationDoll != null)
            {
                _crewFidgets.Remove(_calibrationDoll);
                _crewSeatShifts.Remove(_calibrationDoll);
                _dollRigs.Remove(_calibrationDoll.Pointer);
                UnityEngine.Object.Destroy(_calibrationDoll);
            }
            _calibrationDoll = null;
            var doll = tag != null && room != null ? SpawnDoll(tag, "WOMENACE_CalibrationDoll") : null;
            if (doll == null)
                return;
            _calibrationDoll = doll;
            doll.transform.SetParent(room.transform, false);
            doll.transform.localScale = Vector3.one * RoomDollScale;
            var body = doll.name.Substring(doll.name.IndexOf(':') + 1);
            // She sits in her own sitting loop or her stand-in's, which Pose finds. A doll with
            // neither sits in the loop every doll falls back on, where Pose would stand her: the
            // armchair is for sitting in. Her own animator is asked which she has, since the ops
            // room's pose probe is off with its room.
            Pose(doll, "Sit");
            var animator = doll.GetComponentInChildren<Animator>();
            var sitter = PoseStandIns.GetValueOrDefault(body) ?? body;
            bool Has(string who) => animator != null && animator.HasState(0, Animator.StringToHash($"Base Layer.{who}-Sit"));
            var sits = Has(body) || Has(sitter);
            if (!sits && Has(PoseFallback))
            {
                _crewFidgets.Remove(doll);
                animator.Play(Animator.StringToHash($"Base Layer.{PoseFallback}-Sit"), 0, UnityEngine.Random.value);
                sits = true;
            }
            if (sits)
            {
                doll.transform.localRotation = LoungeSeatFacing;
                doll.transform.localPosition = LoungeSeat;
                // Her round shadow is for a floor under her feet. Seated it would hang in the
                // chair, which the room's ambient occlusion seats her in.
                foreach (var renderer in doll.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name == ContactShadowName)
                        renderer.enabled = false;
                // She is seated off her own pose: her hip joints at their height over the seat,
                // and as far forward as her legs need. The seat is deeper than her thighs are
                // long and her loop tucks a foot back, so her knees and her ankles both go past
                // the seat's front edge, and her thighs clear the cushion where they cross it.
                var legs = new[]
                {
                    (animator?.GetBoneTransform(HumanBodyBones.LeftUpperLeg), animator?.GetBoneTransform(HumanBodyBones.LeftLowerLeg), animator?.GetBoneTransform(HumanBodyBones.LeftFoot)),
                    (animator?.GetBoneTransform(HumanBodyBones.RightUpperLeg), animator?.GetBoneTransform(HumanBodyBones.RightLowerLeg), animator?.GetBoneTransform(HumanBodyBones.RightFoot)),
                };
                if (animator != null && animator.isHuman && legs.All(leg => leg.Item1 != null && leg.Item2 != null && leg.Item3 != null))
                {
                    animator.Update(0f);
                    Vector3 Own(Transform joint) => doll.transform.InverseTransformPoint(joint.position) * RoomDollScale;
                    var hips = (Own(legs[0].Item1) + Own(legs[1].Item1)) * 0.5f;
                    var forward = float.NegativeInfinity;
                    foreach (var (_, knee, ankle) in legs)
                        forward = Mathf.Max(forward, Mathf.Max(LoungeSeatHalfDepth + KneesPastSeat - Own(knee).z, LoungeSeatHalfDepth + AnklesPastSeat - Own(ankle).z));
                    var lift = 0f;
                    foreach (var (hip, knee, _) in legs)
                    {
                        var (from, to) = (Own(hip), Own(knee));
                        var along = Mathf.Clamp01((LoungeSeatHalfDepth - (forward + from.z)) / Mathf.Max(to.z - from.z, 0.01f));
                        var over = Mathf.Lerp(from.y, to.y, along) - hips.y + SeatedHipHeight;
                        lift = Mathf.Max(lift, ThighOverSeat - over);
                    }
                    doll.transform.localPosition = LoungeSeat + LoungeSeatFacing * new Vector3(-hips.x, SeatedHipHeight + lift - hips.y, forward);
                    ClearOfFloor(doll, doll.transform.localPosition + LoungeSeatFacing * hips);
                }
            }
            else
            {
                doll.transform.localRotation = Quaternion.Euler(0f, -70f, 0f);
                doll.transform.localPosition = LoungeStand;
            }
            // The room's own light on her, as on every doll in a room, and her rig at once.
            ProbeLight(doll, _weaponSet);
            lit = true;
        }
        if (_calibrationDoll != null && (lit || Time.frameCount % CalibrationDollRelightFrames == 0))
            LightDoll(_calibrationDoll, SquadLightScale);
    }

    // The lowest point of a doll's body as it is posed now, in the weapon room's space: its full
    // detail meshes baked in their pose.
    private float LowestPoint(GameObject doll) => Lowest(doll).y;

    private Vector3 Lowest(GameObject doll)
    {
        var lowest = new Vector3(0f, float.PositiveInfinity, 0f);
        var room = _weaponSet.Root.transform;
        foreach (var renderer in doll.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (renderer.name.Contains("_LOD") && !renderer.name.EndsWith("_LOD0"))
                continue;
            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked, false);
                var toRoom = room.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                foreach (var vertex in baked.vertices)
                {
                    var at = toRoom.MultiplyPoint3x4(vertex);
                    if (at.y < lowest.y)
                        lowest = at;
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(baked);
            }
        }
        return lowest;
    }

    // A long-shinned doll seated on the cushion has her soles through the floor (Voymastina's by
    // 8 cm). She leans back about her hips until they are clear of it, as a lounging sitter does,
    // which swings her feet forward and up, and is lifted for what the furthest lean leaves. Her
    // lowest point is read again after each step, as the heel may give way to the toe.
    private void ClearOfFloor(GameObject doll, Vector3 hips)
    {
        var room = _weaponSet.Root.transform;
        var leant = 0f;
        for (var step = 0; step < 4; step++)
        {
            var low = Lowest(doll);
            var under = LoungeFloor + SoleClearance - low.y;
            if (under <= 0f)
                return;
            // The point's place about her hips, along the way she faces and up.
            var facing = room.InverseTransformDirection(doll.transform.forward);
            var from = low - hips;
            var along = facing.x * from.x + facing.z * from.z;
            var reach = Mathf.Sqrt(along * along + from.y * from.y);
            var now = Mathf.Atan2(from.y, along);
            var wanted = Mathf.Asin(Mathf.Clamp((from.y + under) / Mathf.Max(reach, 1e-4f), -1f, 1f));
            var lean = Mathf.Min((wanted - now) * Mathf.Rad2Deg, MaxSeatedLean - leant);
            if (lean <= 0.01f)
            {
                doll.transform.localPosition += Vector3.up * under;
                return;
            }
            doll.transform.RotateAround(room.TransformPoint(hips), doll.transform.right, -lean);
            leant += lean;
        }
    }

    // How a weapon's model is turned for its barrel to point one way and its top another. Models
    // do not agree on which way a barrel runs: it runs from the model's origin, the grip, towards
    // its muzzle.
    private static Quaternion WeaponTurn(GameObject shown, Vector3 barrelTo, Vector3 topTo)
    {
        var barrel = Vector3.forward;
        foreach (var part in shown.GetComponentsInChildren<Transform>(true))
            if (part.name.StartsWith("muzzle", StringComparison.OrdinalIgnoreCase))
            {
                var along = shown.transform.InverseTransformPoint(part.position);
                barrel = Mathf.Abs(along.x) > Mathf.Abs(along.z) ? new Vector3(Mathf.Sign(along.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(along.z));
                break;
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

    // A bare card in a room for an item's picture, as large as the picture's own shape allows
    // within a size.
    private static GameObject IconCard(Transform room, Sprite picture, float size)
    {
        var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var collider = card.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.Destroy(collider);
        card.transform.SetParent(room, false);
        var shape = picture.rect;
        var widest = Mathf.Max(shape.width, shape.height);
        card.transform.localScale = new Vector3(size * shape.width / widest, size * shape.height / widest, 1f);
        return card;
    }

    // The hologram of an item's picture: its own part of the sheet it is drawn on, cut by its
    // alpha.
    private Material IconHologram(Sprite picture)
    {
        var material = new Material(HologramMaterial());
        material.SetTexture(BaseMapId, picture.texture);
        var sheet = picture.textureRect;
        material.SetTextureScale(BaseMapId, new Vector2(sheet.width / picture.texture.width, sheet.height / picture.texture.height));
        material.SetTextureOffset(BaseMapId, new Vector2(sheet.x / picture.texture.width, sheet.y / picture.texture.height));
        material.SetFloat("_AlphaClip", 1f);
        return material;
    }
}
