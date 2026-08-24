using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppStem;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// The railgun rests on its back bracket while the squad stands, and only comes to the
// carrier's hands in the Deployed stance.
//
// The weapon ships two models: Back_Special is the bracket WITH a stowed copy of the gun
// (child "railgun_stowed"), Hand_R_Special is the animated gun. Vanilla never flips these
// slots for an AnimType=None special (HeavyWeaponComp, the only writer, exists only for
// tripods), so this system owns them: standing shows the stowed gun and hides the hands
// model, Deployed does the reverse. The swap is delayed into the stance transition so it
// reads as retrieving the weapon: on deploy it lands a beat into the kneel, on get-up it
// waits out the gun's own stowing clip.
//
// While the gun is on the back, the carrier fights with the squad's rifle: the rifle's
// model is instantiated at the special weapon's socket, and the squad's volleys include
// the carrier by appending element 0 to GetExecutingElements(AllButSpecial), which the
// engine excludes for any squad holding an InfantrySpecial. Deployed, both revert: the
// rifle hides and the volley exclusion is vanilla again, so only the squaddies shoot.
//
// The engine finds a shot's origin (and the left hand's IK target) by searching the
// element for a transform named "muzzle" ("weapon_hand_l"), depth-first, first match,
// and the carrier owns two of each: the railgun's and the rifle's. The hidden weapon's
// pair is parked under a different name each swap, so rifle tracers leave the rifle,
// the beam leaves the rails, and the left hand grips the weapon actually held.
//
// The hands model is never deactivated and its renderers are never touched: the
// engine hides attachments with SetActive, but the ElementAnimator collects
// attachment Animators exactly once at element creation and skips inactive objects,
// and renderer.enabled belongs to the game's own visibility passes, which re-enable
// it (the gun reappeared in hand at mission start until a stance cycle re-applied
// the swap). Hidden means scaled to nothing, GFL2's own trick for these parts: the
// object stays active, the Animator keeps its parameters, and no vanilla system
// fights over it.
public sealed class RailgunCarrySystem : JiangyuSystem
{
    private const string RailgunId = "specialweapon.asteria_railgun";
    private const string StowedNodeName = "railgun_stowed";
    private const string RifleNodeName = "railgun_carry_rifle";
    private const string MuzzleName = "muzzle";
    private const string HandLName = "weapon_hand_l";
    private const string ParkedSuffix = "_railgun_parked";

    // The deploy swap lands a beat into the kneel, when her hand reaches the back;
    // the stow swap waits out the gun's stowing clip (1.27 s) so the rails fold
    // before it returns to the bracket.
    private const float DeploySwapDelay = 0.6f;
    private const float StowSwapDelay = 1.4f;

    // The unfold clank from her GFL2 ult, played as the transformation starts in
    // either direction. Bank and item resolve the same way the loader resolves the
    // KDL's string ids: FNV-1a over UTF-8, reinterpreted signed.
    private const string SoundBankId = "wmgfl_weapons_railgun_addition_bank";
    private const string DeploySoundId = "railgun_deploy";

    internal sealed class Carrier
    {
        public Element Element;
        public GameObject HandGun;
        public GameObject Stowed;
        public GameObject Rifle;
        public Transform RailgunMuzzle;
        public Transform RifleMuzzle;
        public Transform RailgunHandL;
        public Transform RifleHandL;
        public Vector3 HandLLocalPosition;
        public Quaternion HandLLocalRotation;
        public bool PendingDeployed;
        public int Seq;
    }

    private readonly Dictionary<IntPtr, Carrier> _carriers = new();

    internal static RailgunCarrySystem Instance { get; private set; }

    internal IReadOnlyDictionary<IntPtr, Carrier> Carriers => _carriers;

    public override void OnInit()
    {
        Instance = this;
        Context.Patches.Postfix("Il2CppMenace.Tactical.Element", "CreateAttachments", OnCreateAttachments);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Actor", "SetStance", OnStanceSet);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "GetExecutingElements", OnGetExecutingElements);
        // the armoury and squad viewer build their own preview attachments outside the
        // tactical path; there the presentation is the vanilla special's (weapon in
        // hand, mount on the back), so only the stowed copy is hidden
        Context.Patches.Postfix("Il2CppUI.PrefabControllers.ArmoryElement", "Create", OnArmoryElementCreate);
        Context.Patches.Postfix("Il2CppUI.PrefabControllers.ArmoryElement", "RefreshAttachments", OnArmoryElementRefresh);
    }

    private void OnArmoryElementCreate(PatchInfo info) => DressPreview(info);

    private void OnArmoryElementRefresh(PatchInfo info) => DressPreview(info);

    // The preview matches the tactical standing state: railgun stowed on the back
    // bracket, the leader's own rifle in the hands.
    private void DressPreview(PatchInfo info)
    {
        try
        {
            var element = (info.Instance as Il2CppObjectBase)?.TryCast<Il2CppUI.PrefabControllers.ArmoryElement>();
            if (element == null)
                return;
            var rig = FindNamed(element.gameObject, "asteria_railgun_rig");
            if (rig == null)
                return; // not a railgun carrier preview
            var handGun = rig.parent;
            if (handGun != null)
                handGun.localScale = Vector3.zero;
            if (FindNamed(element.gameObject, RifleNodeName) != null)
                return; // refresh call, already dressed

            var leader = info.Args != null && info.Args.Count > 0
                ? (info.Args[0] as Il2CppObjectBase)?.TryCast<Il2CppMenace.Strategy.BaseUnitLeader>()
                : null;
            var rifleTemplate = (leader?.GetItems()?.GetItemAtSlot(ItemSlot.InfantryWeapon)?.GetTemplate()
                as Il2CppObjectBase)?.TryCast<WeaponTemplate>();
            var model = rifleTemplate?.Model;
            if (model == null || handGun == null || handGun.parent == null)
                return;
            var rifle = UnityEngine.Object.Instantiate(model, handGun.parent, false);
            rifle.name = RifleNodeName;
            rifle.transform.localPosition = Vector3.zero;
            rifle.transform.localRotation = Quaternion.identity;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"railgun carry: preview dress failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Element creation is the single mount point for equipped-item visuals, so this is
    // where a railgun carrier is recognised and dressed for standing.
    private void OnCreateAttachments(PatchInfo info)
    {
        try
        {
            var element = (info.Instance as Il2CppObjectBase)?.TryCast<Element>();
            if (element == null || info.Args == null || info.Args.Count < 2)
                return;
            if (info.Args[0] is not int elementIndex || elementIndex != 0)
                return;
            Prune();

            var items = (info.Args[1] as Il2CppObjectBase)?.TryCast<ItemContainer>();
            var special = items?.GetItemAtSlot(ItemSlot.InfantrySpecial);
            if (special?.GetTemplate()?.GetID() != RailgunId)
            {
                _carriers.Remove(element.Pointer);
                return;
            }

            var attachments = element.GetAttachments();
            if (attachments == null)
                return;
            attachments.TryGetFirstAttachmentInSlot(VisualAlterationSlot.Hand_R_Special, out var handGun);
            attachments.TryGetFirstAttachmentInSlot(VisualAlterationSlot.Back_Special, out var back);
            var carrier = new Carrier
            {
                Element = element,
                HandGun = handGun,
                Stowed = back?.transform.Find(StowedNodeName)?.gameObject,
                RailgunMuzzle = FindNamed(handGun, MuzzleName),
                RailgunHandL = FindNamed(handGun, HandLName),
            };
            AttachRifle(element, items, carrier);
            _carriers[element.Pointer] = carrier;
            Apply(carrier, deployed: false);
            Context.Log.Debug($"railgun carry: dressed carrier (stowed={carrier.Stowed != null}, "
                + $"hand={carrier.HandGun != null}, rifle={carrier.Rifle != null})");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"railgun carry: attach failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The squad's own rifle in the carrier's hands while the railgun is on the back.
    // Mounted through the engine's own socket primitive, then found by name; if the
    // socket resolution ever fails, the special socket's parent (the same hand bone)
    // is the fallback.
    private void AttachRifle(Element element, ItemContainer items, Carrier carrier)
    {
        var rifleTemplate = (items.GetItemAtSlot(ItemSlot.InfantryWeapon)?.GetTemplate()
            as Il2CppObjectBase)?.TryCast<WeaponTemplate>();
        var model = rifleTemplate?.Model;
        if (model == null)
            return;

        element.AttachPrefab(model, VisualAlterationSlot.Hand_R.GetAttachmentPointName());
        var rifle = FindNamed(element.m_Mesh?.gameObject, model.name + "(Clone)")?.gameObject;
        if (rifle == null && carrier.HandGun != null && carrier.HandGun.transform.parent != null)
        {
            rifle = UnityEngine.Object.Instantiate(model, carrier.HandGun.transform.parent, false);
            rifle.transform.localPosition = Vector3.zero;
            rifle.transform.localRotation = Quaternion.identity;
        }
        if (rifle == null)
        {
            Context.Log.Warn("railgun carry: could not mount the carrier's rifle");
            return;
        }
        rifle.name = RifleNodeName;
        carrier.Rifle = rifle;
        carrier.RifleMuzzle = FindNamed(rifle, MuzzleName);
        carrier.RifleHandL = FindNamed(rifle, HandLName);
        if (carrier.RifleHandL != null)
        {
            carrier.HandLLocalPosition = carrier.RifleHandL.localPosition;
            carrier.HandLLocalRotation = carrier.RifleHandL.localRotation;
            // the engine resolves "weapon_hand_l" once per element, so exactly one
            // node carries the name for the element's whole life: the rifle's. The
            // railgun's own is parked for good, and Apply moves the named node onto
            // whichever grip is live.
            if (carrier.RailgunHandL != null)
                carrier.RailgunHandL.name = HandLName + ParkedSuffix;
        }
    }

    // Stance is a plain field only SetStance writes, and it early-outs on no-change, so
    // a postfix fires once per real transition (and on forced re-sets, which the pending
    // check absorbs).
    private void OnStanceSet(PatchInfo info)
    {
        try
        {
            var actor = (info.Instance as Il2CppObjectBase)?.TryCast<Actor>();
            var element = actor?.GetElement(0);
            if (element == null || !_carriers.TryGetValue(element.Pointer, out var carrier))
                return;

            var deployed = actor.GetStance() == ActorStance.Deployed;
            if (carrier.PendingDeployed == deployed)
                return;
            carrier.PendingDeployed = deployed;
            carrier.Seq++;
            // the rifle leaves her hands the moment the deploy starts, so the kneel
            // reads as going for the railgun, not stowing the rifle
            if (deployed && carrier.Rifle != null)
                carrier.Rifle.SetActive(false);
            PlayDeployClank(carrier);
            Context.Coroutines.Start(SwapAfterDelay(actor, carrier, deployed, carrier.Seq));
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"railgun carry: stance postfix failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private IEnumerator SwapAfterDelay(Actor actor, Carrier carrier, bool deployed, int seq)
    {
        var delay = deployed ? DeploySwapDelay : StowSwapDelay;
        for (var waited = 0f; waited < delay; waited += Time.deltaTime)
            yield return null;
        if (carrier.Seq != seq)
            yield break; // the stance flipped again during the delay
        if (actor == null || !actor.IsAlive())
            yield break;
        Apply(carrier, deployed);
    }

    // The gun's own mechanism starts moving the moment the stance flips (the unfold
    // runs hidden until the swap lands), so the clank plays immediately, from the
    // carrier's position.
    private void PlayDeployClank(Carrier carrier)
    {
        try
        {
            var sound = SoundManager.GetSoundInstance(new ID(Fnv1a32(SoundBankId), Fnv1a32(DeploySoundId)));
            // m_Mesh is null on transmogged doll bodies, so the weapon itself is the anchor
            var at = carrier.HandGun != null ? carrier.HandGun.transform : carrier.Element?.transform;
            if (sound == null || at == null)
            {
                Context.Log.Warn($"railgun carry: deploy clank not played (sound={(sound != null)}, anchor={(at != null)})");
                return;
            }
            sound.Play3D(at.position);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"railgun carry: deploy sound failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int Fnv1a32(string s)
    {
        var hash = 2166136261u;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(s))
        {
            hash ^= b;
            hash *= 16777619u;
        }
        return unchecked((int)hash);
    }

    private void Apply(Carrier carrier, bool deployed)
    {
        try
        {
            if (carrier.Stowed != null)
                carrier.Stowed.SetActive(!deployed);
            if (carrier.Rifle != null)
                carrier.Rifle.SetActive(!deployed);
            if (carrier.HandGun != null)
                carrier.HandGun.transform.localScale = deployed ? Vector3.one : Vector3.zero;
            // shots re-resolve the muzzle, so a rename per swap suffices there; a
            // missing counterpart keeps the visible weapon's node live instead
            Park(carrier.RailgunMuzzle, carrier.RifleMuzzle, MuzzleName, deployed);
            // the left hand reads the once-resolved node, so that node travels:
            // onto the railgun's grip when deployed, home onto the rifle otherwise
            if (carrier.RifleHandL != null && carrier.RailgunHandL != null)
            {
                if (deployed)
                    carrier.RifleHandL.SetPositionAndRotation(
                        carrier.RailgunHandL.position, carrier.RailgunHandL.rotation);
                else
                {
                    carrier.RifleHandL.localPosition = carrier.HandLLocalPosition;
                    carrier.RifleHandL.localRotation = carrier.HandLLocalRotation;
                }
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"railgun carry: visual swap failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Park(Transform railgun, Transform rifle, string name, bool deployed)
    {
        if (railgun == null || rifle == null)
            return;
        railgun.name = deployed ? name : name + ParkedSuffix;
        rifle.name = deployed ? name + "_rifle_parked" : name;
    }

    // Standing, the carrier joins the squad's volleys. AllButSpecial is the rifle
    // volley's element filter, and the engine only excludes element 0 because the
    // special slot is occupied; with the rifle in hand the exclusion is wrong, so the
    // carrier is appended. Deployed (or dead) the vanilla exclusion stands, which is
    // exactly the ask: only the squaddies shoot the rifle then. Random-pick filters are
    // left alone: their result is an already-chosen element, not a roster.
    private void OnGetExecutingElements(PatchInfo info)
    {
        try
        {
            if (info.Args == null || info.Args.Count < 1)
                return;
            var type = info.Args[0] switch
            {
                ExecutingElementType t => t,
                int i => (ExecutingElementType)i,
                _ => ExecutingElementType.All,
            };
            if (type != ExecutingElementType.AllButSpecial)
                return;

            var skill = (info.Instance as Il2CppObjectBase)?.TryCast<Skill>();
            var actor = skill?.GetActor();
            var element = actor?.GetElement(0);
            if (element == null || !_carriers.TryGetValue(element.Pointer, out _))
                return;
            if (actor.GetStance() == ActorStance.Deployed || !element.IsAlive())
                return;

            var result = info.Result as Il2CppSystem.Collections.Generic.List<int>;
            if (result != null && !result.Contains(0))
                result.Add(0);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"railgun carry: volley roster failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static Transform FindNamed(GameObject root, string name)
    {
        if (root == null)
            return null;
        foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
            if (t.name == name)
                return t;
        return null;
    }

    // Wrappers outlive their mission; entries whose element died with its scene are
    // dropped whenever a new carrier registers.
    private void Prune()
    {
        List<IntPtr> stale = null;
        foreach (var (key, carrier) in _carriers)
        {
            var dead = false;
            try
            {
                dead = carrier.Element == null || carrier.Element.WasCollected;
            }
            catch
            {
                dead = true;
            }
            if (dead)
                (stale ??= new List<IntPtr>()).Add(key);
        }
        if (stale != null)
            foreach (var key in stale)
                _carriers.Remove(key);
    }
}
