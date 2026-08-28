using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// What one arm slot carries at runtime, recorded at mount time so the other
// bay systems never re-derive it: the reveal animation drives Scapula and
// Mount scales, and the fire routing borrows Muzzle.
internal sealed class BayMountRecord
{
    public Transform Scapula;
    public Transform Mount;
    public GameObject Muzzle;
    public float InvHandScale = 1f;
    // The slot holds a weapon, whether or not a model resolved for it: the
    // reveal grows an occupied arm even with an empty fist.
    public bool Occupied;
}

// The four slots of one element, plus which reveal driver owns it: a
// tactical mission grows an arm while its bay skill is aimed, the armoury
// grows one while its tile's picker is open.
internal sealed class BayMountSet
{
    public BayMountRecord[] Slots;
    public bool Concealed;
    public bool Tactical;
    // The arm assembly's own Animator, for the per-shot recoil triggers.
    public Animator ArmsAnimator;
}

// Mounts the bay loadout onto OTs-14's arms when her tactical element spawns.
//
// Each slotted special weapon contributes its IN-HANDS model only
// (WeaponTemplate.ModelSecondary; the Model slot is the stowed backpack
// piece, which the bay never shows). The instance parents to the arm
// prefab's palm bone for that slot, oriented muzzle-forward, so it rides the
// stance, the breathing sway and the aim-follow servo for free.
//
// Deployable weapons spawn OPEN: there is no deploy step in the bay, so any
// Animator on the mounted model has its deploy parameters raised and is
// snapped straight into its deployed state instead of playing the unfold.
public sealed class BayMountSystem : JiangyuSystem
{
    private const string WeaponId = "weapon.ots14";
    private const string MountNodeName = "wmgfl_bay_weapon";

    // Mount placement tuning, all relative to her facing at spawn: weapons sit
    // forward of the palm, pushed outward past her silhouette, and the four
    // heights converge toward the middle (high hands drop a little more than
    // low hands rise, per the user's eye).
    private const float FrontOffset = 0.18f;
    private const float OutwardOffset = 0.24f;
    private const float ConvergeUp = 0.08f;
    private const float ConvergeDown = 0.12f;

    // Hand scaling: the fist grows or shrinks toward its weapon's WIDTH, the
    // smallest bounds dimension (side-to-side thickness). Length would hand a
    // slim sniper a minigun-sized fist, and the middle dimension is dominated
    // by magazine and scope height, which measures near-identical for every
    // gun. The reference is the railgun slab's thickness the stance was posed
    // around, and the response is deliberately sub-linear. The breathing clip
    // ships with its scale channels stripped so the Animator cannot stomp this.
    private const float HandScaleRefWidth = 0.12f;
    private const float HandScaleMin = 0.7f;
    private const float HandScaleMax = 1.5f;

    // Dev verbs reach the shared ModContext through here (the verb runner
    // marshals JSON args only).
    internal static BayMountSystem Instance { get; private set; }

    // Element pointer -> that element's mounted bay, for the reveal and
    // fire-routing systems. Il2Cpp's GC never moves objects, so the pointer
    // is a stable identity for the element's lifetime.
    internal static readonly Dictionary<IntPtr, BayMountSet> Mounts = new();

    // A mesh node unique to the arms prefab: its presence identifies an
    // OTs-14 armoury preview.
    private const string ArmMeshNode = "c_OTs14SSR01_Arm_slg_1_lod0";

    public override void OnInit()
    {
        Instance = this;
        Context.Patches.Postfix("Il2CppMenace.Tactical.Element", "CreateAttachments", OnCreateAttachments);
        // The armoury preview never goes through Element.CreateAttachments:
        // its attachments are built by the ArmoryElement flow, so the bay
        // mounts (and the concealment) ride those hooks there.
        Context.Patches.Postfix("Il2CppUI.PrefabControllers.ArmoryElement", "Create", OnArmoryElement);
        Context.Patches.Postfix("Il2CppUI.PrefabControllers.ArmoryElement", "RefreshAttachments", OnArmoryElement);
        // The preview's re-equip ANIMATION replaces attachments through
        // spawn events after Create/RefreshAttachments return, handing back
        // fresh unconcealed arms with no mounts: re-mount after every spawn
        // event too (idempotent, and the guard skips non-arm events).
        Context.Patches.Postfix("Il2CppUI.PrefabControllers.ArmoryElement", "OnSpawnAttachment", OnArmoryElement);
        // An equip's stage rebuild goes through the unit selector's
        // subscriber and replaces attachments through internal paths none of
        // the hooks above see: watch for a while after it fires and re-mount
        // any preview whose recorded arms died (replaced arms leave dead
        // transforms behind in the registry).
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.ArmoryUnitSelector", "OnVisualAlterationChanged", OnStageRefreshed);
    }

    private readonly List<Il2CppUI.PrefabControllers.ArmoryElement> _previews = new();
    private bool _watching;

    private void OnStageRefreshed(PatchInfo info)
    {
        if (!_watching)
        {
            _watching = true;
            Context.Coroutines.Start(RemountWatch());
        }
    }

    // Watch the tracked previews EVERY FRAME for a couple of seconds: the
    // rebuild can replace attachments at any point of its animation, and a
    // freshly spawned arms prefab stands at authored full scale until the
    // remount conceals it, so a coarser poll flashed all four arms for up
    // to a whole poll interval on every equip.
    private System.Collections.IEnumerator RemountWatch()
    {
        try
        {
            var end = UnityEngine.Time.time + 2.5f;
            while (UnityEngine.Time.time < end)
            {
                yield return null;
                for (var i = _previews.Count - 1; i >= 0; i--)
                {
                    var preview = _previews[i];
                    var dead = false;
                    try
                    {
                        if (preview == null || preview.WasCollected || preview.gameObject == null)
                            dead = true;
                    }
                    catch
                    {
                        dead = true;
                    }
                    if (dead)
                    {
                        _previews.RemoveAt(i);
                        continue;
                    }
                    try
                    {
                        if (SceneQuery.FindNamed(preview.gameObject, ArmMeshNode) != null && NeedsRemount(preview.Pointer))
                            MountOnArms(preview.Pointer, preview.gameObject, ArmouryFacing(preview), null, tactical: false);
                    }
                    catch
                    {
                        _previews.RemoveAt(i);
                    }
                }
            }
        }
        finally
        {
            _watching = false;
        }
    }

    // Which way the DOLL faces on the stage: read off the BODY's humanoid
    // avatar, never from the hierarchy or from any pose. Every prior
    // geometric read flipped somewhere (transform walk on nesting, rifle
    // direction on equip-animation timing, arm run on the folded rest pose,
    // where the hands sit BEHIND the scapulas). The avatar's bone map labels
    // left and right authoritatively, and a standing idle never twists the
    // pelvis 180 degrees, so the horizontal cross of her hip line with world
    // up IS her forward. Written onto a helper transform under the preview so
    // MountOnArms can consume it like the mission element's transform.
    private static readonly string[] FacingScapulas = { "Scapula_L", "Scapula1_R", "Scapula1_L", "Scapula_R" };

    private static Transform ArmouryFacing(Il2CppUI.PrefabControllers.ArmoryElement preview)
    {
        var anchor = SceneQuery.FindNamed(preview.gameObject, "wmgfl_bay_facing");
        if (anchor == null)
        {
            anchor = new GameObject("wmgfl_bay_facing").transform;
            anchor.SetParent(preview.transform, false);
        }

        var body = FindHumanoidAnimator(preview.transform);
        if (body == null)
            return preview.transform;
        var right = SideLine(body, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg);
        if (right == Vector3.zero)
            right = SideLine(body, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm);
        if (right == Vector3.zero)
            return preview.transform;
        var forward = Vector3.Cross(right, Vector3.up);
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            return preview.transform;

        var roots = Vector3.zero;
        var count = 0;
        foreach (var name in FacingScapulas)
        {
            var root = SceneQuery.FindNamed(preview.gameObject, name);
            if (root == null)
                continue;
            roots += root.position;
            count++;
        }
        anchor.position = count > 0
            ? roots / count
            : body.GetBoneTransform(HumanBodyBones.Hips)?.position ?? preview.transform.position;
        anchor.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        return anchor;
    }

    // The doll's right-pointing side line, horizontalised. Zero when the
    // avatar cannot supply both bones.
    private static Vector3 SideLine(Animator body, HumanBodyBones left, HumanBodyBones right)
    {
        var l = body.GetBoneTransform(left);
        var r = body.GetBoneTransform(right);
        if (l == null || r == null)
            return Vector3.zero;
        var line = r.position - l.position;
        line.y = 0f;
        return line.sqrMagnitude < 0.0001f ? Vector3.zero : line;
    }

    // Depth-first search for the body model's Animator: the one carrying a
    // humanoid avatar (the arms attachment's own Animator is generic).
    // Manual walk because the generic GetComponentInChildren instantiates an
    // interop Cast<T> that throws VerificationException at runtime.
    private static Animator FindHumanoidAnimator(Transform node)
    {
        if (node == null)
            return null;
        var animator = node.GetComponent<Animator>();
        if (animator != null && animator.isHuman)
            return animator;
        for (var i = 0; i < node.childCount; i++)
        {
            var found = FindHumanoidAnimator(node.GetChild(i));
            if (found != null)
                return found;
        }
        return null;
    }

    // Replaced arms leave the registered records pointing at destroyed
    // transforms; a missing registry entry means the preview never mounted.
    private static bool NeedsRemount(IntPtr key)
    {
        if (!Mounts.TryGetValue(key, out var set) || set.Slots == null)
            return true;
        foreach (var record in set.Slots)
            if (record?.Scapula == null)
                return true;
        return false;
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        Mounts.Clear();
        _previews.Clear();
        _watching = false;
    }

    private void OnCreateAttachments(PatchInfo info)
    {
        try
        {
            var element = (info.Instance as Il2CppObjectBase)?.TryCast<Element>();
            if (element == null || info.Args == null || info.Args.Count < 2)
                return;
            if (info.Args[0] is not int elementIndex || elementIndex != 0)
                return;
            var items = (info.Args[1] as Il2CppObjectBase)?.TryCast<ItemContainer>();
            if (items?.GetItemAtSlot(ItemSlot.InfantryWeapon)?.GetTemplate()?.GetID() != WeaponId)
                return;
            var attachments = element.GetAttachments();
            if (attachments == null)
                return;
            attachments.TryGetFirstAttachmentInSlot(VisualAlterationSlot.Back_Special, out var back);
            if (back == null)
                return;
            var tactical = false;
            try
            {
                tactical = Il2CppMenace.States.TacticalState.Get() != null
                    || (element.GetEntity() as Il2CppObjectBase)?.TryCast<Actor>() != null;
            }
            catch
            {
                // no tactical state machinery in this scene
            }
            MountOnArms(element.Pointer, back, element.transform, element, tactical);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay mount failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The armoury preview's attachments come from the ArmoryElement flow,
    // identified as hers by the arms prefab's unique mesh node.
    private void OnArmoryElement(PatchInfo info)
    {
        try
        {
            var preview = (info.Instance as Il2CppObjectBase)?.TryCast<Il2CppUI.PrefabControllers.ArmoryElement>();
            if (preview == null || SceneQuery.FindNamed(preview.gameObject, ArmMeshNode) == null)
                return;
            if (!_previews.Exists(p => p != null && p.Pointer == preview.Pointer))
                _previews.Add(preview);
            MountOnArms(preview.Pointer, preview.gameObject, ArmouryFacing(preview), null, tactical: false);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay armoury mount failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void MountOnArms(IntPtr key, GameObject armsHost, Transform facing, Element revealElement, bool tactical)
    {
        try
        {
            // Idempotent: the armoury re-creates or refreshes attachments per
            // leader selection and per equip, so stale mounts, leg sinks and
            // hand scaling are purged before mounting fresh.
            var stale = new List<GameObject>();
            foreach (var t in armsHost.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith(MountNodeName, StringComparison.Ordinal) || t.name == "wmgfl_bay_leg_sink")
                    stale.Add(t.gameObject);
                else if (t.name.StartsWith("Elbow", StringComparison.Ordinal) && !t.name.Contains("Part"))
                    t.localScale = Vector3.one;
            }
            foreach (var go in stale)
                UnityEngine.Object.Destroy(go);

            var slots = Bay.Loadout(Context);
            var hands = new Transform[Bay.SlotCount];
            var records = new BayMountRecord[Bay.SlotCount];
            var meanHeight = 0f;
            var handCount = 0;
            for (var i = 0; i < Bay.SlotCount; i++)
            {
                records[i] = new BayMountRecord();
                hands[i] = SceneQuery.FindNamed(armsHost, Bay.HandBones[i]);
                if (hands[i] == null)
                    continue;
                for (var up = hands[i]; up != null && up != armsHost.transform; up = up.parent)
                    if (up.name.StartsWith("Scapula", StringComparison.Ordinal))
                    {
                        records[i].Scapula = up;
                        break;
                    }
                meanHeight += hands[i].position.y;
                handCount++;
            }
            if (handCount > 0)
                meanHeight /= handCount;
            var mounted = 0;
            for (var i = 0; i < Bay.SlotCount; i++)
            {
                var item = Bay.ResolveItem(slots[i]);
                var weapon = Bay.WeaponOf(item);
                if (weapon == null)
                    continue;
                // A slot with a weapon grows its arm even when no model
                // resolves (the reveal's cue), the fist just stays empty.
                records[i].Occupied = true;
                // Which field holds the in-hands model varies per family
                // (flamethrower: Model, RPG: ModelSecondary), so pick by the
                // slot each model is authored for, never by field position.
                // Unity-null aware at every step: ?? on interop references
                // sails past a destroyed/unset fake-null (the light mortar's
                // EntireModel fields taught this).
                var primary = weapon.Model != null ? weapon.Model : null;
                var secondary = weapon.ModelSecondary != null ? weapon.ModelSecondary : null;
                var prefab = weapon.VisualAlterationSlot == VisualAlterationSlot.Hand_R_Special && primary != null
                    ? primary
                    : weapon.VisualAlterationSlotSecondary == VisualAlterationSlot.Hand_R_Special && secondary != null
                        ? secondary
                        : secondary != null ? secondary : primary;
                if (prefab == null)
                {
                    Context.Log.Warn($"bay: '{weapon.GetID()}' has no model to mount");
                    continue;
                }
                var hand = hands[i];
                if (hand == null)
                {
                    Context.Log.Warn($"bay: hand bone '{Bay.HandBones[i]}' not found on the arms");
                    continue;
                }
                var mount = UnityEngine.Object.Instantiate(prefab, hand, false);
                mount.name = $"{MountNodeName}_{i}";
                // Size the arm to the weapon before any placement math: the
                // scale rides the ELBOW so the whole forearm and hand grow
                // with a big weapon, not just the fist, and the mount
                // compensates inversely so the weapon stays true size.
                // Measure in the prefab's own frame: at world-identity
                // rotation every weapon lies in its authoring convention, so
                // the box axes mean what they say. Measuring in the stance
                // (the first attempt) smeared length into every axis of the
                // world box and made all weapons measure alike.
                mount.transform.rotation = Quaternion.identity;
                var width = WidthDimension(mount);
                // Square-root response: protrusions (rocket fins, folded legs) inflate
                // the measured box, and a linear curve let one fin drag a hand to the
                // cap. The root keeps the ordering while flattening outliers.
                var handScale = Mathf.Clamp(0.55f + 0.45f * Mathf.Sqrt(width / HandScaleRefWidth), HandScaleMin, HandScaleMax);
                var scaleBone = hand.parent;
                for (var up = scaleBone; up != null && up != armsHost.transform; up = up.parent)
                    if (up.name.StartsWith("Elbow", StringComparison.Ordinal) && !up.name.Contains("Part"))
                    {
                        scaleBone = up;
                        break;
                    }
                if (scaleBone != null && width > 0f)
                {
                    scaleBone.localScale = Vector3.one * handScale;
                    mount.transform.localScale = Vector3.one / handScale;
                    records[i].InvHandScale = 1f / handScale;
                }
                records[i].Mount = mount.transform;
                Context.Log.Debug($"bay: '{weapon.GetID()}' width {width:F3} -> hand scale {handScale:F2} on {scaleBone?.name}");
                mount.transform.rotation = facing.rotation;
                // Each family's prefab has its own authoring frame: align the
                // prefab's own muzzle node with the element's facing instead
                // of trusting the root frame.
                var muzzle = SceneQuery.FindNamed(mount, "muzzle");
                records[i].Muzzle = muzzle?.gameObject;
                if (muzzle != null)
                {
                    var current = muzzle.rotation * Vector3.forward;
                    if (current.sqrMagnitude > 1e-6f)
                        mount.transform.rotation =
                            Quaternion.FromToRotation(current, facing.forward) * mount.transform.rotation;
                }
                mount.transform.position = hand.position;
                // The engine finds IK and shot spawnpoints by node NAME across
                // the whole element, and every vanilla in-hands model carries
                // its own muzzle / weapon_hand_l: rename them so her equipped
                // rifle's nodes stay the only ones the searches can hit.
                foreach (var t in mount.GetComponentsInChildren<Transform>(true))
                    if (t.name is "muzzle" or "weapon_hand_l" or "weapon_hand_l_pistol")
                        t.name = "wmgfl_bay_" + t.name;
                ForceDeployed(mount);
                HideGroundMounts(mount, weapon.GetID());
                // Centre and tune placement AFTER the animator settled: the
                // bounds of a skinned weapon mean nothing at bindpose (the
                // packed minigun centred wrong and appeared to float before
                // its deploy transition finished).
                var renderers = mount.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    for (var r = 1; r < renderers.Length; r++)
                        bounds.Encapsulate(renderers[r].bounds);
                    mount.transform.position += hand.position - bounds.center;
                }
                var forward = facing.forward;
                var right = facing.right;
                var side = Mathf.Sign(Vector3.Dot(hand.position - facing.position, right));
                var vertical = hand.position.y > meanHeight ? -ConvergeDown : ConvergeUp;
                mount.transform.position += forward * FrontOffset
                    + right * side * OutwardOffset
                    + Vector3.up * vertical;
                mounted++;
            }
            // The whole bay spawns concealed everywhere: a mission grows an
            // arm while its bay skill is aimed, the armoury while its tile's
            // picker is open (Ots14BayRevealSystem drives both). Concealment
            // is a scale, not SetActive: the game's visibility passes
            // re-enable disabled objects, and the export ships without scale
            // channels so the Animator cannot stomp it.
            foreach (var record in records)
            {
                if (record.Scapula != null)
                    record.Scapula.localScale = Vector3.one * 0.001f;
                if (record.Mount != null)
                    record.Mount.localScale = Vector3.zero;
            }
            // The arms Animator, for the recoil triggers: a manual parent
            // walk from a scapula (the generic GetComponentInParent JITs an
            // interop cast that throws, the armoury-restore lesson).
            Animator armsAnimator = null;
            foreach (var record in records)
            {
                if (record.Scapula == null)
                    continue;
                for (var node = record.Scapula; node != null && armsAnimator == null; node = node.parent)
                    armsAnimator = node.GetComponent<Animator>();
                if (armsAnimator != null)
                    break;
            }
            if (armsAnimator != null)
            {
                // Concealed arms have microscopic renderer bounds: a culled
                // animator would freeze the state machine and swallow the
                // recoil triggers.
                armsAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Context.Log.Debug("bay: arms animator "
                    + (armsAnimator.isActiveAndEnabled ? "active" : "INACTIVE")
                    + ", controller '" + (armsAnimator.runtimeAnimatorController?.name ?? "NONE") + "'");
            }
            else
            {
                Context.Log.Warn("bay: no arms animator found for recoil");
            }
            Mounts[key] = new BayMountSet
            {
                Slots = records,
                Concealed = true,
                Tactical = tactical,
                ArmsAnimator = armsAnimator,
            };
            Ots14BayRevealSystem.OnBayMounted(key, revealElement, tactical);
            if (mounted > 0)
                Context.Log.Debug($"bay: mounted {mounted} weapon(s) on the arms (tactical: {tactical})");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay mount failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Ground-mount hardware makes no sense held in a fist. The legs are
    // SKINNED geometry on tripod rigs and their bones are animated every
    // frame, so scaling the bones directly gets stomped by the Animator
    // (only the curveless back feet ever stayed zeroed). Instead the skin's
    // own bone table is repointed: every leg bone entry is swapped for one
    // zero-scale dummy the Animator knows nothing about, so the leg vertices
    // collapse while the Animator keeps every other animation running (the
    // barrel spin stays). Plain unskinned leg nodes just get scaled.
    private void HideGroundMounts(GameObject mount, string weaponId)
    {
        var leggy = new HashSet<Transform>();
        foreach (var t in mount.GetComponentsInChildren<Transform>(true))
            if (t.name.Contains("leg", StringComparison.OrdinalIgnoreCase)
                || t.name.Contains("bipod", StringComparison.OrdinalIgnoreCase))
                leggy.Add(t);
        if (leggy.Count == 0)
            return;

        bool IsLeggy(Transform t)
        {
            for (var p = t; p != null && p != mount.transform; p = p.parent)
                if (leggy.Contains(p))
                    return true;
            return false;
        }

        Transform dummy = null;
        var repointed = 0;
        foreach (var smr in mount.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var bones = smr.bones;
            if (bones == null)
                continue;
            var changed = false;
            for (var i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null || !IsLeggy(bones[i]))
                    continue;
                if (dummy == null)
                {
                    dummy = new GameObject("wmgfl_bay_leg_sink").transform;
                    dummy.SetParent(mount.transform, false);
                    dummy.localScale = Vector3.zero;
                }
                bones[i] = dummy;
                changed = true;
                repointed++;
            }
            if (changed)
                smr.bones = bones;
        }
        // Unskinned leg parts (plain mesh nodes) are not animator-bound: a
        // scale zero holds for them.
        foreach (var t in leggy)
            t.localScale = Vector3.zero;
        Context.Log.Debug($"bay: hid ground mounts on '{weaponId}' ({leggy.Count} node(s), {repointed} skin bone(s) repointed)");
    }

    // Raise every deploy-shaped parameter the weapon's Animator declares and
    // snap it into a deployed-looking state, so nothing sits folded waiting
    // for a deployment that can never come.
    private static void ForceDeployed(GameObject mount)
    {
        foreach (var animator in mount.GetComponentsInChildren<Animator>(true))
        {
            try
            {
                var parameters = animator.parameters;
                for (var i = 0; parameters != null && i < parameters.Length; i++)
                {
                    var p = parameters[i];
                    switch (p.name)
                    {
                        case "Stance":
                            animator.SetInteger(p.nameHash, 1);
                            break;
                        case "IsWeaponDeployed":
                        case "IsWeaponAttached":
                            animator.SetBool(p.nameHash, true);
                            break;
                    }
                }
                foreach (var state in new[] { "Deployed", "deployed" })
                {
                    var hash = Animator.StringToHash(state);
                    if (animator.HasState(0, hash))
                    {
                        animator.Play(hash, 0, 0f);
                        break;
                    }
                }
                // Fast-forward through whatever transition chain the raised
                // parameters trigger (packed to deployed runs about a second
                // on tripods, and not every controller has a state literally
                // named Deployed for the Play above to snap to): several
                // simulated seconds settle every transition before the first
                // rendered frame, so nothing rides its unfold animation in
                // view.
                for (var step = 0; step < 10; step++)
                    animator.Update(0.5f);
                // The animator stays RUNNING (the minigun's barrel spin is
                // wanted), and never culls: zeroed legs and scaled mounts can
                // wreck the renderer bounds culling keys off, and a culled
                // animator freezes the weapon in bindpose.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            catch
            {
                // an animator with no controller, or armoury-stripped: leave it
            }
        }
    }

    // Side-to-side thickness, read with the object at world-identity rotation
    // so the box axes match the authoring convention: length runs on Z, and
    // of the remaining two the smaller is the width (the larger is height,
    // inflated by magazines and scopes).
    private static float WidthDimension(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return 0f;
        var bounds = renderers[0].bounds;
        for (var r = 1; r < renderers.Length; r++)
            bounds.Encapsulate(renderers[r].bounds);
        var s = bounds.size;
        return Mathf.Min(s.x, s.y);
    }

}
