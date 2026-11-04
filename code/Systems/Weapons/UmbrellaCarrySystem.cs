using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// The umbrella is folded on the carrier's back while the squad fights, and comes to her
// hands when it opens. It shares RailgunCarrySystem's mechanics (SpecialWeaponCarry, and
// the railgun's header for the muzzle and off-hand tricks), with two differences: the
// trigger is the umbrella's own open effect, not the Deployed stance, and opening it also
// drops the squaddies into the deployed kneel behind her.
//
// The weapon ships two models: Back_Special is the folded umbrella, Hand_R_Special the
// rigged one whose Animator unfolds on the UmbrellaOpen bool. Vanilla never flips these
// slots for an AnimType=None special, so this system owns them. Closed, the carrier holds
// the squad's rifle (a cosmetic instance at her hand socket) and joins its volleys; open,
// the rifle hides and the vanilla volley exclusion of element 0 stands, so the squaddies
// shoot and she holds the umbrella.
//
// The open effect's SetAnimatorBool handler raises the bool on add and drops it on remove,
// so its OnAdded/OnRemoved are the open and close events. They are concrete overrides of
// SkillEventHandler's virtuals, which is what keeps them safe to patch.
//
// The kneel is visual only: each squaddie element gets the Deployed stance through the
// per-element OnStanceChanged that Actor.SetStance fans out (the fairy reinforcement uses
// the same call), and the actor's stance is never touched, so none of Deployed's
// modifiers apply. Closing hands them back the actor's real stance.
public sealed class UmbrellaCarrySystem : JiangyuSystem
{
    private const string UmbrellaId = "specialweapon.welrod_umbrella";
    private const string OpenParameter = "UmbrellaOpen";
    private const string RigName = "welrod_umbrella_rig";
    private const string RifleNodeName = "umbrella_carry_rifle";
    private const string ParkedSuffix = "_umbrella_parked";

    // The carrier's body plays Welrod's GFL2 ultimate while the umbrella is out: the
    // umbrella prefab carries a humanoid controller (child "carrier_controller", built by
    // BuildUmbrellaCarrier) that is swapped onto her Animator for the duration, Reach,
    // Swing, Open, then Hold until UmbrellaOpen drops, Fold, Stow. The vanilla controller
    // comes back once Stow ends. Times below are frames of the GFL2 takes (60 fps): the
    // umbrella leaves her back for her hand 30 frames into Reach (take frame 46), snaps
    // open 4 frames into Open, and is put away at the end of Stow.
    private const string CarrierControllerNode = "carrier_controller";
    private const float RevealIntoReach = 30f / 60f;
    private const float OpenIntoOpen = 4f / 60f;
    private static readonly int OpenHash = Animator.StringToHash(OpenParameter);

    // Where the umbrella sits in the right-hand socket while the GFL2 clips drive her arm:
    // GFL2's own hand-to-umbrella relation at the hold, measured against each body's frame
    // on the raw GFL2 rig and a baked doll (MeasureUmbrellaGrip).
    private static readonly Vector3 GripPosition = new(0.0514f, 0.0643f, 0.0463f);
    private static readonly Quaternion GripRotation = new(0.193307f, -0.448133f, -0.567993f, 0.662717f);

    // The canopy's snap open and fold, from her GFL2 combat bank.
    private const string SoundBankId = "wmgfl_weapons_umbrella_addition_bank";
    private const string OpenSoundId = "umbrella_open";
    private const string CloseSoundId = "umbrella_close";

    internal sealed class Carrier
    {
        public Element Element;
        public GameObject Umbrella;
        public GameObject Back;
        public GameObject Rifle;
        public Transform RifleHandL;
        public Transform UmbrellaHandL;
        public Vector3 HandLLocalPosition;
        public Quaternion HandLLocalRotation;
        public Animator Body;
        public RuntimeAnimatorController VanillaController;
        public RuntimeAnimatorController CarrierController;
        public Vector3 UmbrellaLocalPosition;
        public Quaternion UmbrellaLocalRotation;
        // the vanilla controller's parameters as the engine left them, written back when it
        // returns: a controller swap resets every parameter to its default
        public List<(int Hash, AnimatorControllerParameterType Type, float Value)> VanillaParameters;
        public bool Open;
        // the umbrella reached her hand (and opened) on this opening
        public bool Revealed;
        public int Seq;
    }

    private readonly Dictionary<IntPtr, Carrier> _carriers = new();

    internal static UmbrellaCarrySystem Instance { get; private set; }

    internal IReadOnlyDictionary<IntPtr, Carrier> Carriers => _carriers;

    public override void OnInit()
    {
        Instance = this;
        Context.Patches.Postfix("Il2CppMenace.Tactical.Element", "CreateAttachments", OnCreateAttachments);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Effects.SetAnimatorBoolHandler", "OnAdded", OnBoolAdded);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Effects.SetAnimatorBoolHandler", "OnRemoved", OnBoolRemoved);
        // closed, the carrier joins the squad's volleys; open, she holds the umbrella and
        // the vanilla exclusion of element 0 stands
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "GetExecutingElements",
            info => Guard("volley roster", () => SpecialWeaponCarry.JoinVolley(info,
                (_, element) => _carriers.TryGetValue(element.Pointer, out var carrier) && !carrier.Open && carrier.Rifle != null)));
        Context.Patches.Postfix("Il2CppUI.PrefabControllers.ArmoryElement", "Create", OnArmoryElement);
        Context.Patches.Postfix("Il2CppUI.PrefabControllers.ArmoryElement", "RefreshAttachments", OnArmoryElement);
    }

    private void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Warn($"{what} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Warn(string message) => Context.Log.Warn("umbrella carry: " + message);

    // The armoury preview shows the standing look: umbrella folded on the back, the
    // leader's own squad weapon in her hands.
    private void OnArmoryElement(PatchInfo info)
        => Guard("preview dress", () => SpecialWeaponCarry.DressPreview(info, RigName, RifleNodeName));

    private void OnCreateAttachments(PatchInfo info)
    {
        try
        {
            var element = (info.Instance as Il2CppObjectBase)?.TryCast<Element>();
            if (element == null || info.Args == null || info.Args.Count < 2)
                return;
            if (info.Args[0] is not int elementIndex || elementIndex != 0)
                return;
            SpecialWeaponCarry.Prune(_carriers, c => c.Element);

            var items = (info.Args[1] as Il2CppObjectBase)?.TryCast<ItemContainer>();
            if (items?.GetItemAtSlot(ItemSlot.InfantrySpecial)?.GetTemplate()?.GetID() != UmbrellaId)
            {
                _carriers.Remove(element.Pointer);
                return;
            }
            var attachments = element.GetAttachments();
            if (attachments == null)
                return;
            attachments.TryGetFirstAttachmentInSlot(VisualAlterationSlot.Hand_R_Special, out var umbrella);
            attachments.TryGetFirstAttachmentInSlot(VisualAlterationSlot.Back_Special, out var back);
            var carrier = new Carrier
            {
                Element = element,
                Umbrella = umbrella,
                Back = back,
                UmbrellaHandL = SceneQuery.FindNamed(umbrella, SpecialWeaponCarry.HandLName),
            };
            // the umbrella never fires, so its muzzle is parked for good and shots
            // always leave the rifle
            var muzzle = SceneQuery.FindNamed(umbrella, SpecialWeaponCarry.MuzzleName);
            if (muzzle != null)
                muzzle.name = SpecialWeaponCarry.MuzzleName + ParkedSuffix;
            AttachRifle(items, carrier);
            if (umbrella != null)
            {
                carrier.UmbrellaLocalPosition = umbrella.transform.localPosition;
                carrier.UmbrellaLocalRotation = umbrella.transform.localRotation;
                carrier.CarrierController = umbrella.transform.Find(CarrierControllerNode)?.GetComponent<Animator>()?.runtimeAnimatorController;
                carrier.Body = umbrella.transform.parent?.GetComponentInParent<Animator>();
                carrier.VanillaController = carrier.Body?.runtimeAnimatorController;
            }
            // attachments rebuilt while the umbrella is out: the body may already be on the
            // carrier clips, and the open state has to carry over or the close is ignored
            if (_carriers.TryGetValue(element.Pointer, out var previous) && previous.Open)
            {
                carrier.Open = true;
                carrier.Revealed = previous.Revealed;
                carrier.Seq = previous.Seq;
                carrier.VanillaController = previous.VanillaController;
                carrier.VanillaParameters = previous.VanillaParameters;
            }
            if (umbrella != null && carrier.CarrierController == null)
                Warn("the umbrella prefab carries no carrier_controller; re-run BuildUmbrellaCarrier after baking it");
            _carriers[element.Pointer] = carrier;
            Apply(carrier, open: carrier.Open && carrier.Revealed);
            if (carrier.Open && umbrella != null && carrier.Body != null && carrier.CarrierController != null
                && carrier.Body.runtimeAnimatorController?.Pointer == carrier.CarrierController.Pointer)
            {
                umbrella.transform.localPosition = GripPosition;
                umbrella.transform.localRotation = GripRotation;
            }
            Context.Log.Debug($"umbrella carry: dressed carrier (back={back != null}, hand={umbrella != null}, "
                + $"rifle={carrier.Rifle != null}, carrier clips={carrier.CarrierController != null})");
        }
        catch (Exception ex)
        {
            Warn($"attach failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The squad weapon in her hands while the umbrella is on her back, on the umbrella's
    // own hand socket.
    private void AttachRifle(ItemContainer items, Carrier carrier)
    {
        var rifle = SpecialWeaponCarry.MountSquadWeapon(items, carrier.Umbrella?.transform.parent, RifleNodeName);
        if (rifle == null)
        {
            Warn("could not mount the carrier's squad weapon");
            return;
        }
        carrier.Rifle = rifle;
        carrier.RifleHandL = SceneQuery.FindNamed(rifle, SpecialWeaponCarry.HandLName);
        if (carrier.RifleHandL != null)
        {
            carrier.HandLLocalPosition = carrier.RifleHandL.localPosition;
            carrier.HandLLocalRotation = carrier.RifleHandL.localRotation;
            // the engine resolves "weapon_hand_l" once per element: the rifle's node keeps
            // the name and travels onto the umbrella's grip while it is open
            if (carrier.UmbrellaHandL != null)
                carrier.UmbrellaHandL.name = SpecialWeaponCarry.HandLName + ParkedSuffix;
        }
    }

    private void OnBoolAdded(PatchInfo info) => OnOpenChanged(info, open: true);

    private void OnBoolRemoved(PatchInfo info) => OnOpenChanged(info, open: false);

    private void OnOpenChanged(PatchInfo info, bool open)
    {
        try
        {
            var handler = (info.Instance as Il2CppObjectBase)?.TryCast<SetAnimatorBoolHandler>();
            if (handler?.m_Template?.Parameter?.ParameterName != OpenParameter)
                return;
            var actor = handler.GetActor();
            var element = actor?.GetElement(0);
            if (element == null || !_carriers.TryGetValue(element.Pointer, out var carrier))
                return;
            if (carrier.Open == open)
                return;
            carrier.Open = open;
            carrier.Seq++;
            Kneel(actor, open);
            // a dead holder's body belongs to the ragdoll: no clips, no swap, no umbrella
            if (!element.IsAlive())
                return;
            if (open)
            {
                carrier.Revealed = false;
                if (carrier.Rifle != null)
                    carrier.Rifle.SetActive(false);
                SetUmbrellaOpen(carrier, false);
                var drawing = SwapBody(carrier, toCarrier: true);
                Context.Coroutines.Start(Draw(actor, carrier, carrier.Seq, drawing));
            }
            else
            {
                if (carrier.Revealed)
                    PlaySound(carrier, CloseSoundId);
                Context.Coroutines.Start(Stow(actor, carrier, carrier.Seq));
            }
        }
        catch (Exception ex)
        {
            Warn($"open/close failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private float ClipLength(Carrier carrier, string name)
    {
        foreach (var clip in carrier.CarrierController?.animationClips ?? Array.Empty<AnimationClip>())
            if (clip != null && clip.name == name)
                return clip.length;
        return 0f;
    }

    // With the carrier clips, the umbrella comes out when her hand reaches her back and
    // opens as the thrust lands. Without them (controller missing), it opens at once.
    private IEnumerator Draw(Actor actor, Carrier carrier, int seq, bool animated)
    {
        if (animated)
        {
            yield return SpecialWeaponCarry.Wait(RevealIntoReach);
            if (!Current(carrier, seq))
                yield break;
        }
        Apply(carrier, open: true);
        carrier.Revealed = true;
        if (animated)
        {
            yield return SpecialWeaponCarry.Wait(ClipLength(carrier, "Reach") - RevealIntoReach + ClipLength(carrier, "Swing") + OpenIntoOpen);
            if (!Current(carrier, seq))
                yield break;
        }
        SetUmbrellaOpen(carrier, true);
        PlaySound(carrier, OpenSoundId);
    }

    // The engine's drop of UmbrellaOpen reaches the carrier controller too, which folds
    // and stows from any stage of the draw; the umbrella goes back on her back as Stow ends.
    private IEnumerator Stow(Actor actor, Carrier carrier, int seq)
    {
        var animated = carrier.Body != null && carrier.CarrierController != null
            && carrier.Body.runtimeAnimatorController?.Pointer == carrier.CarrierController.Pointer;
        yield return SpecialWeaponCarry.Wait(animated ? ClipLength(carrier, "Fold") + ClipLength(carrier, "Stow") : 0.3f);
        if (!Current(carrier, seq))
            yield break;
        Apply(carrier, open: false);
        SwapBody(carrier, toCarrier: false);
    }

    // Still the latest open or close, with the holder alive: a holder who died meanwhile
    // keeps whatever her body is doing (it is the ragdoll's now).
    private static bool Current(Carrier carrier, int seq)
    {
        try
        {
            return carrier.Seq == seq && carrier.Element != null && carrier.Element.IsAlive();
        }
        catch
        {
            return false;
        }
    }

    // Swaps the body between its vanilla controller and the carrier clips. A swapped-in
    // controller shows the avatar's default pose for a frame unless updated at once, and
    // starts with its parameters at their defaults, so UmbrellaOpen is raised again.
    private bool SwapBody(Carrier carrier, bool toCarrier)
    {
        try
        {
            if (carrier.Body == null || carrier.CarrierController == null || carrier.VanillaController == null)
                return false;
            var umbrella = carrier.Umbrella?.transform;
            if (toCarrier)
            {
                carrier.VanillaParameters = ReadParameters(carrier.Body);
                carrier.Body.runtimeAnimatorController = carrier.CarrierController;
                carrier.Body.Update(0f);
                carrier.Body.SetBool(OpenHash, true);
                if (umbrella != null)
                {
                    umbrella.localPosition = GripPosition;
                    umbrella.localRotation = GripRotation;
                }
            }
            else
            {
                carrier.Body.runtimeAnimatorController = carrier.VanillaController;
                WriteParameters(carrier.Body, carrier.VanillaParameters);
                carrier.Body.Update(0f);
                if (umbrella != null)
                {
                    umbrella.localPosition = carrier.UmbrellaLocalPosition;
                    umbrella.localRotation = carrier.UmbrellaLocalRotation;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Warn($"body swap failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static List<(int, AnimatorControllerParameterType, float)> ReadParameters(Animator animator)
    {
        var values = new List<(int, AnimatorControllerParameterType, float)>();
        foreach (var p in animator.parameters)
        {
            switch (p.type)
            {
                case AnimatorControllerParameterType.Float:
                    values.Add((p.nameHash, p.type, animator.GetFloat(p.nameHash)));
                    break;
                case AnimatorControllerParameterType.Int:
                    values.Add((p.nameHash, p.type, animator.GetInteger(p.nameHash)));
                    break;
                case AnimatorControllerParameterType.Bool:
                    values.Add((p.nameHash, p.type, animator.GetBool(p.nameHash) ? 1f : 0f));
                    break;
            }
        }
        return values;
    }

    // Triggers are left out: a pending one would fire an action twice.
    private static void WriteParameters(Animator animator, List<(int Hash, AnimatorControllerParameterType Type, float Value)> values)
    {
        if (values == null)
            return;
        foreach (var (hash, type, value) in values)
        {
            switch (type)
            {
                case AnimatorControllerParameterType.Float:
                    animator.SetFloat(hash, value);
                    break;
                case AnimatorControllerParameterType.Int:
                    animator.SetInteger(hash, (int)value);
                    break;
                case AnimatorControllerParameterType.Bool:
                    animator.SetBool(hash, value != 0f);
                    break;
            }
        }
    }

    private void SetUmbrellaOpen(Carrier carrier, bool open)
    {
        try
        {
            var animator = carrier.Umbrella?.GetComponentInChildren<Animator>(true);
            animator?.SetBool(OpenHash, open);
        }
        catch (Exception ex)
        {
            Warn($"animator {(open ? "open" : "hold")} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void PlaySound(Carrier carrier, string soundId)
        => SpecialWeaponCarry.PlaySound(SoundBankId, soundId,
            carrier.Umbrella != null ? carrier.Umbrella.transform : carrier.Element?.transform, Warn);

    // Squaddies only: element 0 is the holder, standing behind her umbrella.
    private void Kneel(Actor actor, bool open)
    {
        var elements = actor.GetElements();
        var count = elements?.Count ?? 0;
        var stance = open ? ActorStance.Deployed : actor.GetStance();
        for (var i = 1; i < count; i++)
        {
            var e = elements[i];
            if (e == null || !e.IsAlive())
                continue;
            try
            {
                e.OnStanceChanged(stance, true, true);
            }
            catch (Exception ex)
            {
                Warn($"kneel of element {i} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private void Apply(Carrier carrier, bool open)
    {
        try
        {
            if (carrier.Back != null)
                carrier.Back.transform.localScale = open ? Vector3.zero : Vector3.one;
            if (carrier.Rifle != null)
                carrier.Rifle.SetActive(!open);
            // hidden means scaled to nothing, never deactivated: the element collected
            // the umbrella's Animator once, and an inactive one drops its parameters
            if (carrier.Umbrella != null)
                carrier.Umbrella.transform.localScale = open ? Vector3.one : Vector3.zero;
            if (carrier.RifleHandL != null && carrier.UmbrellaHandL != null)
            {
                if (open)
                    carrier.RifleHandL.SetPositionAndRotation(carrier.UmbrellaHandL.position, carrier.UmbrellaHandL.rotation);
                else
                {
                    carrier.RifleHandL.localPosition = carrier.HandLLocalPosition;
                    carrier.RifleHandL.localRotation = carrier.HandLLocalRotation;
                }
            }
        }
        catch (Exception ex)
        {
            Warn($"visual swap failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
