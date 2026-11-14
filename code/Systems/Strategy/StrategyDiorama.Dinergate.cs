using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The Dinergate: the little command-centre robot GFL2 keeps at the bottom left of its ops room,
// which opens the Intel panel when clicked. It stands on the balcony floor in front of
// the room camera, turned to face it, idling. A click is a ray from the room camera against its
// collider (the room has no other colliders). Its answer is the flight itself: GFL2's Click clip
// carries the body off to one side, which reads as a second, late move after the flight.
public sealed partial class StrategyDiorama
{
    private const string DinergateAsset = "environment/dinergate/main";

    // Room space (the source scene's coordinates): hovering over the balcony floor (0.672), bottom
    // left of the view and clear of the bottom bar.
    internal static readonly Vector3 DinergatePosition = new(-4.4f, 0.90f, -0.8f);
    // Where it flies to project the Intel board, from its post: to the left of the view so it
    // stands clear of the board's left edge, and up until its lens is level with the middle of the
    // screen, where the board is. It turns its projection across the view (to the view's right),
    // a little towards the camera so the lens still shows.
    private const float DinergateProjectingLeft = 0.3f;
    // The rise when the projection's source is not found.
    private const float DinergateProjectingRise = 0.3f;
    private const float DinergateProjectingTowardsCamera = 0.45f;
    private const float DinergateFlightSeconds = 0.5f;
    // How far above its origin the tag's pointer sits.
    private const float DinergateTagHeight = 0.36f;

    private bool _dinergateHover;
    private bool _dinergateProjecting;
    private int _dinergateFlight;
    private float _dinergateYaw;

    private GameObject _dinergate;
    private Animator _dinergateAnimator;

    private void SpawnDinergate()
    {
        try
        {
            var prefab = Context.Assets.Load<GameObject>(DinergateAsset);
            if (prefab == null || _room == null)
            {
                Context.Log.Warn($"strategy diorama: '{DinergateAsset}' is not in the bundles");
                return;
            }
            _dinergate = UnityEngine.Object.Instantiate(prefab, _room.transform, false);
            _dinergate.name = "WOMENACE_Dinergate";
            _dinergateAnimator = _dinergate.GetComponentInChildren<Animator>(true);
            GlowDinergate();
            AttachDinergateEffects();
            PlaceDinergate();
            _dinergateProjecting = false;
            ProbeLight(_dinergate, _ops);
            Context.Coroutines.Start(WatchClicks(_dinergate));
            Context.Log.Info($"strategy diorama: dinergate at {DinergatePosition}, animator {(_dinergateAnimator != null ? "found" : "missing")}");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: dinergate failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // GFL2's ops-room Dinergate carries its effects on its own bones: the eye's hint glow and light
    // on Dinergate01_M (its orange "something new" call, kept off), and on Root_M the HUD (the
    // holo rings round the lens, and light beams) and the lens projection (off until it projects). They are rebuilt from GFL2's particle systems by
    // Womenace.EditorTools.BuildRoomVfx (environment/dinergate_vfx) and hung on the same bones
    // here. Their local offsets are in GFL2 metres, so they are rescaled against the bone's scale.
    private const string DinergateVfxAsset = "environment/dinergate_vfx/main";
    private static readonly (string Effect, string Bone)[] DinergateEffectBones =
    {
        ("EFF_Dinergate_Hint", "Dinergate01_M"),
        ("EFF_Dinergate_Projection", "Root_M"),
        ("Dinergate_commendCenter_HUD", "Root_M"),
    };

    private void AttachDinergateEffects()
    {
        try
        {
            var prefab = Context.Assets.Load<GameObject>(DinergateVfxAsset);
            if (prefab == null)
                return;
            var effects = UnityEngine.Object.Instantiate(prefab, _dinergate.transform, false);
            effects.name = "WOMENACE_DinergateVfx";
            var bones = _dinergate.GetComponentsInChildren<Transform>(true);
            var worldScale = _dinergate.transform.lossyScale.x;
            var attached = 0;
            foreach (var (effectName, boneName) in DinergateEffectBones)
            {
                var effect = effects.transform.Find(effectName);
                var bone = bones.FirstOrDefault(b => b.name == boneName);
                if (effect == null || bone == null)
                    continue;
                var factor = worldScale / Mathf.Max(1e-6f, bone.lossyScale.x);
                var (position, rotation, scale) = (effect.localPosition, effect.localRotation, effect.localScale);
                effect.SetParent(bone, false);
                effect.localPosition = position * factor;
                effect.localRotation = rotation;
                effect.localScale = scale * factor;
                attached++;
            }
            // At rest: no hint call, and no HUD light beams.
            foreach (var node in _dinergate.GetComponentsInChildren<Transform>(true))
                if (node.name == "EFF_Dinergate_Hint" || node.name.StartsWith("guangshu", StringComparison.Ordinal))
                    node.gameObject.SetActive(false);
            StartDinergateHud();
            Context.Log.Info($"strategy diorama: dinergate effects on {attached} bones");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: dinergate effects failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The eye lens and status strip glow GFL2's holo cyan, bright enough for the room's bloom to
    // halo them as GFL2's eye does (its glow map is a pale blue mask, so the colour does the
    // tinting).
    private static readonly Color DinergateGlow = new(1.4f, 10.6f, 15.3f);

    private void GlowDinergate()
    {
        if (_dinergate == null)
            return;
        var block = new MaterialPropertyBlock();
        foreach (var renderer in _dinergate.GetComponentsInChildren<Renderer>(true))
        {
            renderer.GetPropertyBlock(block);
            block.SetColor("_GlowColor", DinergateGlow);
            renderer.SetPropertyBlock(block);
        }
    }

    private void PlaceDinergate()
    {
        if (_dinergate == null || _room == null)
            return;
        var t = _dinergate.transform;
        t.localPosition = DinergatePosition;
        var toCamera = _room.transform.InverseTransformPoint(_anchor.position) - DinergatePosition;
        var yaw = Mathf.Atan2(toCamera.x, toCamera.z) * Mathf.Rad2Deg;
        _dinergateYaw = yaw;
        t.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    // Flies the Dinergate up to project the Intel board, its lens projection and beams on, or back
    // down to its post.
    private void SetDinergateProjecting(bool on)
    {
        if (_dinergateProjecting == on || _dinergate == null)
            return;
        _dinergateProjecting = on;
        TableSounds.Play(on ? TableSounds.ProjectorOn : TableSounds.ProjectorOff);
        Context.Coroutines.Start(FlyDinergate(_dinergate, on, ++_dinergateFlight));
    }

    private System.Collections.IEnumerator FlyDinergate(GameObject dinergate, bool up, int flight)
    {
        var t = dinergate.transform;
        var (fromPosition, fromRotation) = (t.localPosition, t.localRotation);
        var toPosition = DinergatePosition;
        var toRotation = Quaternion.Euler(0f, _dinergateYaw, 0f);
        var camera = RoomCamera;
        if (up && camera != null && _room != null)
        {
            // Along the camera's own right and back axes, a line from the middle height of the
            // screen stays at that height, so the projection runs level across the view.
            var view = camera.transform;
            var aim = _room.transform.InverseTransformDirection((view.right - view.forward * DinergateProjectingTowardsCamera).normalized);
            var level = _room.transform.InverseTransformDirection(view.right);
            level.y = 0f;
            // The projection's axis and source, as the body carries them.
            var fire = ProjectionNode(dinergate);
            var axis = t.InverseTransformDirection(fire != null ? -fire.forward : t.forward);
            toRotation = Quaternion.LookRotation(aim, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(axis, Vector3.up));
            var post = DinergatePosition - level.normalized * DinergateProjectingLeft;
            // The height that puts the projection's source at the middle of the screen, found by
            // standing the body at each candidate and reading where the source lands. The body
            // is back where it was before the frame draws.
            var (low, high) = (-0.5f, 1.5f);
            if (fire != null)
            {
                t.localRotation = toRotation;
                for (var i = 0; i < 14; i++)
                {
                    var rise = (low + high) / 2f;
                    t.localPosition = post + Vector3.up * rise;
                    if (camera.WorldToViewportPoint(fire.position).y < 0.5f)
                        low = rise;
                    else
                        high = rise;
                }
                t.localPosition = fromPosition;
                t.localRotation = fromRotation;
            }
            else
                (low, high) = (DinergateProjectingRise, DinergateProjectingRise);
            toPosition = post + Vector3.up * ((low + high) / 2f);
        }
        if (!up)
            SetProjection(dinergate, false);
        for (var time = 0f; time < DinergateFlightSeconds; time += Time.unscaledDeltaTime)
        {
            if (dinergate == null || flight != _dinergateFlight)
                yield break;
            // The flight draws back before it goes and overshoots before it settles. The turn does
            // neither, or it would read as a shake of the head.
            var u = time / DinergateFlightSeconds;
            t.localPosition = Vector3.LerpUnclamped(fromPosition, toPosition, Ease.InOutBack(u));
            t.localRotation = Quaternion.Slerp(fromRotation, toRotation, Ease.InOutCubic(u));
            yield return null;
        }
        if (dinergate == null || flight != _dinergateFlight)
            yield break;
        t.localPosition = toPosition;
        t.localRotation = toRotation;
        if (up)
            SetProjection(dinergate, true);
    }

    // The lens projection's source. GFL2 lays the projection's parts out along this node's back axis.
    private static Transform ProjectionNode(GameObject dinergate)
    {
        foreach (var node in dinergate.GetComponentsInChildren<Transform>(true))
            if (node.name == "fire" && node.parent != null && node.parent.name == "EFF_Dinergate_Projection")
                return node;
        return null;
    }

    private static void SetProjection(GameObject dinergate, bool on)
    {
        foreach (var node in dinergate.GetComponentsInChildren<Transform>(true))
            if (node.name == "EFF_Dinergate_Projection")
                node.gameObject.SetActive(on);
    }

    // Whether a screen point is over the Dinergate, nothing drawn in front of it.
    private bool OverDinergate(Vector2 screenPoint)
    {
        var camera = _camera != null ? _camera.GetComponent<Camera>() : null;
        if (camera == null || !camera.isActiveAndEnabled || _dinergate == null || PointerOverUi(screenPoint))
            return false;
        var ray = camera.ScreenPointToRay(new Vector3(screenPoint.x, screenPoint.y, 0f));
        return Physics.Raycast(ray, out var hit, 50f) && hit.collider != null && hit.collider.gameObject == _dinergate;
    }

    // Whether the pointer is over the Dinergate, which its tag answers.
    private void TrackDinergateHover()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        Vector2 position = mouse != null ? mouse.position.ReadValue() : (Vector2)Input.mousePosition;
        // It answers the pointer on the Home view only, where the Intel board is. The test costs a
        // pick through every UI document, so it runs when the pointer has moved.
        if (!OnHomeView(_policyScreen) || _shell == null)
            _dinergateHover = false;
        else if (position != _dinergatePointer || _dinergateProjecting)
            _dinergateHover = !_dinergateProjecting && OverDinergate(position);
        // The holotable answers it too, where the Dinergate is not in front of it.
        if (!OnHomeView(_policyScreen) || _shell == null)
            _tableHover = false;
        else if (position != _dinergatePointer)
            _tableHover = !_dinergateHover && OverTable(position);
        _dinergatePointer = position;
    }

    private bool _tableHover;
    // The holotable's tag in the shell, found when the shell is made.
    private VisualElement _tableTag;
    // Where the holotable's tag points, in room space: over the middle of the table.
    private static readonly Vector3 TableTagPoint = new(-14.4f, 0.3f, 0f);
    private Vector2 _dinergatePointer = new(float.MinValue, float.MinValue);

    // The centre of the Dinergate's lens, where its HUD sits.
    private Vector3 DinergateEye
    {
        get
        {
            var hud = _hudShown ?? _hudRest ?? _hudNoticeVariant;
            return hud?.Root != null ? hud.Root.transform.position : _dinergate.transform.position;
        }
    }

    // The holotable's top in room space, corner by corner round it.
    private static readonly Vector3[] TableTop =
    {
        new(-17.15f, -0.18f, -1.25f), new(-17.15f, -0.18f, 1.25f), new(-11.6f, -0.18f, 1.25f), new(-11.6f, -0.18f, -1.25f),
    };

    // Whether a screen point lies on the holotable's top, on the Home view with nothing drawn over it.
    private bool OverTable(Vector2 screenPoint)
    {
        var camera = RoomCamera;
        if (camera == null || !camera.isActiveAndEnabled || _room == null || _shell == null || _intelOpen || !OnHomeView(_policyScreen))
            return false;
        // The table's tag is a part of it.
        var tag = _tableTag;
        if (tag?.panel != null && tag.resolvedStyle.display != DisplayStyle.None && !tag.ClassListContains("wui-tag--off")
            && tag.worldBound.Contains(RuntimePanelUtils.ScreenToPanel(tag.panel, new Vector2(screenPoint.x, Screen.height - screenPoint.y))))
            return true;
        if (PointerOverUi(screenPoint))
            return false;
        // Inside the top's outline on screen: on the same side of each of its edges.
        var sign = 0f;
        for (var i = 0; i < TableTop.Length; i++)
        {
            var a = camera.WorldToScreenPoint(_room.transform.TransformPoint(TableTop[i]));
            var b = camera.WorldToScreenPoint(_room.transform.TransformPoint(TableTop[(i + 1) % TableTop.Length]));
            if (a.z <= 0f || b.z <= 0f)
                return false;
            var side = (b.x - a.x) * (screenPoint.y - a.y) - (b.y - a.y) * (screenPoint.x - a.x);
            if (side != 0f && sign != 0f && Mathf.Sign(side) != sign)
                return false;
            if (side != 0f)
                sign = Mathf.Sign(side);
        }
        return true;
    }

    // A click on the holotable from the Home view goes to Operations, as the bottom bar's button does.
    private bool TryClickTable(Vector2 screenPoint)
    {
        if (!OverTable(screenPoint))
            return false;
        SetHomeView(HomeView.Operations);
        return true;
    }

    private System.Collections.IEnumerator WatchClicks(GameObject dinergate)
    {
        while (dinergate != null && _dinergate == dinergate)
        {
            TrackDinergateHover();
            if (!_tableVideo && !NavigationMenu.IsOpen && MousePressed(out var position) && !TryClick(position))
                TryClickTable(position);
            yield return null;
        }
    }

    private static bool MousePressed(out Vector2 position)
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            position = mouse.position.ReadValue();
            return mouse.leftButton.wasPressedThisFrame;
        }
        position = Input.mousePosition;
        return Input.GetMouseButtonDown(0);
    }

    // Whether a click at this screen point lands on the Dinergate, which reacts when it does.
    private bool TryClick(Vector2 screenPoint)
    {
        // It opens the Intel board, which is the Home view's. A click on a panel drawn over the
        // Dinergate is the panel's.
        if (!OnHomeView(_policyScreen) || _shell == null || !OverDinergate(screenPoint))
            return false;
        OnDinergateClicked();
        return true;
    }

    private void OnDinergateClicked()
    {
        Context.Log.Info("strategy diorama: dinergate clicked");
        ToggleIntel();
    }

    // The HUD on the Dinergate's lens. GFL2 builds it twice under Dinergate_commendCenter_HUD:
    // "Icon" (two rings round a target dot, shown at rest) and "RedPoint" (the same rings round a
    // notice icon, off at rest, GFL2's "something new" state). Each carries an Animator on one
    // controller, Ani_Btn_Dinergate_commendCenter_HUD, with a fade layer (FadeIn 0.73 s on entry,
    // FadeOut 0.33 s on its trigger) and a loop layer (FadeLoop, 8 s) playing together. The
    // controller is not in the bundles the mod ships, so the three clips are played here from
    // their own keys: GFL2's streamed curves, one cubic per key.
    //
    // A notice swaps the rest variant for the notice one, whose centre icon the bake sets to GFL2's
    // mail icon (dinergate_vfx/overrides.txt), and the body plays its Hint clip.
    private enum HudPart { Root, Ring1, Ring2, Ring3, Ring1Mesh, Ring2Mesh, Ring3Mesh, Frame }
    private enum HudChannel { PosY, EulerX, EulerY, EulerZ, Scale, Alpha }

    // From Time on the value is Value + ((A * dt + B) * dt + C) * dt, until the next key.
    private readonly record struct HudKey(float Time, float Value, float A, float B, float C);

    private sealed class HudCurve
    {
        public readonly HudPart Part;
        public readonly HudChannel Channel;
        public readonly HudKey[] Keys;

        public HudCurve(HudPart part, HudChannel channel, params HudKey[] keys) => (Part, Channel, Keys) = (part, channel, keys);

        public float At(float time)
        {
            var key = Keys[0];
            for (var i = 1; i < Keys.Length && Keys[i].Time <= time; i++)
                key = Keys[i];
            var dt = MathF.Max(0f, time - key.Time);
            return key.Value + ((key.A * dt + key.B) * dt + key.C) * dt;
        }
    }

    private const float HudFadeInLength = 0.7333f;
    private const float HudFadeOutLength = 0.3333f;
    private const float HudLoopLength = 8f;
    // The fade the clips animate on each mesh. GFL2 binds a material float the port does not name,
    // running 0 to 1, and Womenace/GfVfx multiplies every family's output by this one.
    private const string HudAlphaProperty = "_AdditionalAlpha";
    // How often the body repeats its Hint clip while a notice stands.
    private const float HintEvery = 6f;

    private static readonly HudCurve[] HudIconFadeIn =
    {
        new(HudPart.Root, HudChannel.PosY, new HudKey(0f, -1f, -248f, 68f, 0f), new HudKey(0.16667f, -0.25926f, 4f, -5f, 2f), new HudKey(0.5f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.EulerX, new HudKey(0.06667f, 40.499f, 647.98f, -485.99f, 0f), new HudKey(0.56667f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.EulerY, new HudKey(0.06667f, 251.73f, 4027.8f, -3020.8f, 0f), new HudKey(0.56667f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.EulerZ, new HudKey(0.06667f, 143.05f, 2288.8f, -1716.6f, 0f), new HudKey(0.56667f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.EulerX, new HudKey(0f, 1.169f, 18.704f, -14.028f, 0f), new HudKey(0.5f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.EulerY, new HudKey(0f, 236.2f, 3779.2f, -2834.4f, 0f), new HudKey(0.5f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.EulerZ, new HudKey(0f, 207.86f, 3325.7f, -2494.3f, 0f), new HudKey(0.5f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.Scale, new HudKey(0.06667f, 0.35f, -161.2f, 44.2f, 0f), new HudKey(0.23333f, 0.83148f, 2.6f, -3.25f, 1.3f), new HudKey(0.56667f, 1f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.Scale, new HudKey(0f, 0.35f, -161.2f, 44.2f, 0f), new HudKey(0.16667f, 0.83148f, 2.6f, -3.25f, 1.3f), new HudKey(0.5f, 1f, 0f, 0f, 0f)),
        new(HudPart.Ring3, HudChannel.Scale, new HudKey(0.26667f, 0.35f, -157.86f, 43.643f, 0f), new HudKey(0.43333f, 0.83148f, 2.9933f, -3.6684f, 1.3929f), new HudKey(0.73333f, 1f, 0f, 0f, 0f)),
        new(HudPart.Frame, HudChannel.Alpha, new HudKey(0.16667f, 0f, -54f, 27f, 0f), new HudKey(0.5f, 1f, 0f, 0f, 0f)),
        new(HudPart.Ring2Mesh, HudChannel.Alpha, new HudKey(0.06667f, 0f, -105.47f, 42.187f, 0f), new HudKey(0.33333f, 1f, 0f, 0f, 0f)),
        new(HudPart.Ring3Mesh, HudChannel.Alpha, new HudKey(0.26667f, 0f, -105.47f, 42.187f, 0f), new HudKey(0.53333f, 1f, 0f, 0f, 0f)),
        new(HudPart.Ring1Mesh, HudChannel.Alpha, new HudKey(0f, 1f, 0f, 0f, 0f)),
    };
    private static readonly HudCurve[] HudIconFadeOut =
    {
        new(HudPart.Root, HudChannel.PosY, new HudKey(0f, 0f, 8.64f, -4.32f, 0f), new HudKey(0.33333f, -0.16f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.EulerX, new HudKey(0f, 0f, 5279.5f, -1583.8f, 0f), new HudKey(0.2f, -21.118f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.EulerY, new HudKey(0f, 0f, 60112f, -18034f, 0f), new HudKey(0.2f, -240.45f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.EulerZ, new HudKey(0f, 0f, -5045.7f, 1513.7f, 0f), new HudKey(0.2f, 20.183f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.EulerX, new HudKey(0f, 0f, -4531.3f, 1812.5f, 0f), new HudKey(0.26667f, 42.963f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.EulerY, new HudKey(0f, 0f, -13885f, 5554.2f, 0f), new HudKey(0.26667f, 131.65f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.EulerZ, new HudKey(0f, 0f, -9076.9f, 3630.7f, 0f), new HudKey(0.26667f, 86.062f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.Scale, new HudKey(0f, 1f, 110.26f, -33.077f, 0f), new HudKey(0.2f, 0.55898f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.Scale, new HudKey(0f, 1f, 47.328f, -18.931f, 0f), new HudKey(0.26667f, 0.55126f, 0f, 0f, 0f)),
        new(HudPart.Ring3, HudChannel.Scale, new HudKey(0f, 1f, 95.74f, -23.935f, 0f), new HudKey(0.16667f, 0.77838f, 0f, 0f, 0f)),
        new(HudPart.Frame, HudChannel.Alpha, new HudKey(0f, 1f, 105.47f, -42.187f, 0f), new HudKey(0.26667f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring1Mesh, HudChannel.Alpha, new HudKey(0f, 1f, 250f, -75f, 0f), new HudKey(0.2f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring2Mesh, HudChannel.Alpha, new HudKey(0f, 1f, 105.47f, -42.187f, 0f), new HudKey(0.26667f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring3Mesh, HudChannel.Alpha, new HudKey(0f, 1f, 432f, -108f, 0f), new HudKey(0.16667f, 0f, 0f, 0f, 0f)),
    };
    private static readonly HudCurve[] HudIconFadeLoop =
    {
        new(HudPart.Ring1Mesh, HudChannel.EulerZ, new HudKey(0f, 0f, 0f, 0f, -45f), new HudKey(8f, -360f, 0f, 0f, 0f)),
        new(HudPart.Ring3Mesh, HudChannel.EulerZ, new HudKey(0f, 0f, 0f, 0f, 90f), new HudKey(8f, 720f, 0f, 0f, 0f)),
        new(HudPart.Ring2Mesh, HudChannel.Scale, new HudKey(0f, 1f, 1.0551f, -0.79135f, 0f), new HudKey(0.5f, 0.93405f, -5.9245f, 2.9622f, 0f), new HudKey(0.83333f, 1.0438f, 3.855f, -1.9275f, 0f), new HudKey(1.1667f, 0.97238f, -2.2469f, 1.1235f, 0f), new HudKey(1.5f, 1.014f, 2.5804f, -1.4192f, 0f), new HudKey(1.8667f, 0.95039f, -6.2161f, 3.1081f, 0f), new HudKey(2.2f, 1.0655f, 19.515f, -5.8544f, 0f), new HudKey(2.4f, 0.98744f, -2.219f, 1.1095f, 0f), new HudKey(2.7333f, 1.0285f, 3.0095f, -1.2038f, 0f), new HudKey(3f, 1f, 0f, 0f, 0f), new HudKey(4f, 1f, 0.86941f, -0.69553f, 0f), new HudKey(4.5333f, 0.93405f, -4.4511f, 2.4481f, 0f), new HudKey(4.9f, 1.0438f, 3.855f, -1.9275f, 0f), new HudKey(5.2333f, 0.97238f, -1.6882f, 0.92848f, 0f), new HudKey(5.6f, 1.014f, 1.9875f, -1.1925f, 0f), new HudKey(6f, 0.95039f, -6.2161f, 3.1081f, 0f), new HudKey(6.3333f, 1.0655f, 12.289f, -4.3012f, 0f), new HudKey(6.5667f, 0.98744f, -2.219f, 1.1095f, 0f), new HudKey(6.9f, 1.0285f, 2.1136f, -0.95114f, 0f), new HudKey(7.2f, 1f, 0f, 0f, 0f)),
    };
    private static readonly HudCurve[] HudNoticeFadeIn =
    {
        new(HudPart.Root, HudChannel.PosY, new HudKey(0f, -1f, -248f, 68f, 0f), new HudKey(0.16667f, -0.25926f, 4f, -5f, 2f), new HudKey(0.5f, 0f, 0f, 0f, 0f), new HudKey(0.53333f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.EulerX, new HudKey(0f, 1.169f, 18.704f, -14.028f, 0f), new HudKey(0.5f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.EulerY, new HudKey(0f, 236.2f, 3779.2f, -2834.4f, 0f), new HudKey(0.5f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.EulerZ, new HudKey(0f, 207.86f, 3325.7f, -2494.3f, 0f), new HudKey(0.5f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.EulerX, new HudKey(0.06667f, 40.499f, 647.98f, -485.99f, 0f), new HudKey(0.56667f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.EulerY, new HudKey(0.06667f, 251.73f, 4027.8f, -3020.8f, 0f), new HudKey(0.56667f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.EulerZ, new HudKey(0.06667f, 143.05f, 2288.8f, -1716.6f, 0f), new HudKey(0.56667f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.Scale, new HudKey(0f, 0.35f, -161.2f, 44.2f, 0f), new HudKey(0.16667f, 0.83148f, 2.6f, -3.25f, 1.3f), new HudKey(0.5f, 1f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.Scale, new HudKey(0.06667f, 0.35f, -161.2f, 44.2f, 0f), new HudKey(0.23333f, 0.83148f, 2.6f, -3.25f, 1.3f), new HudKey(0.56667f, 1f, 0f, 0f, 0f)),
        new(HudPart.Ring3, HudChannel.Scale, new HudKey(0.26667f, 0.35f, -157.86f, 43.643f, 0f), new HudKey(0.43333f, 0.83148f, 2.9933f, -3.6684f, 1.3929f), new HudKey(0.73333f, 1f, 0f, 0f, 0f)),
        new(HudPart.Frame, HudChannel.Alpha, new HudKey(0.16667f, 0f, -54f, 27f, 0f), new HudKey(0.5f, 1f, 0f, 0f, 0f)),
        new(HudPart.Ring1Mesh, HudChannel.Alpha, new HudKey(0f, 0f, -105.47f, 42.187f, 0f), new HudKey(0.26667f, 1f, 0f, 0f, 0f)),
        new(HudPart.Ring2Mesh, HudChannel.Alpha, new HudKey(0.06667f, 0f, -54f, 27f, 0f), new HudKey(0.4f, 1f, 0f, 0f, 0f)),
        new(HudPart.Ring3Mesh, HudChannel.Alpha, new HudKey(0.26667f, 0f, -54f, 27f, 0f), new HudKey(0.6f, 1f, 0f, 0f, 0f)),
    };
    private static readonly HudCurve[] HudNoticeFadeOut =
    {
        new(HudPart.Root, HudChannel.PosY, new HudKey(0f, 0f, 8.64f, -4.32f, 0f), new HudKey(0.33333f, -0.16f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.EulerX, new HudKey(0f, 0f, 5279.5f, -1583.8f, 0f), new HudKey(0.2f, -21.118f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.EulerY, new HudKey(0f, 0f, 60112f, -18034f, 0f), new HudKey(0.2f, -240.45f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.EulerZ, new HudKey(0f, 0f, -5045.7f, 1513.7f, 0f), new HudKey(0.2f, 20.183f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.EulerX, new HudKey(0f, 0f, -4531.3f, 1812.5f, 0f), new HudKey(0.26667f, 42.963f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.EulerY, new HudKey(0f, 0f, -13885f, 5554.2f, 0f), new HudKey(0.26667f, 131.65f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.EulerZ, new HudKey(0f, 0f, -9076.9f, 3630.7f, 0f), new HudKey(0.26667f, 86.062f, 0f, 0f, 0f)),
        new(HudPart.Ring1, HudChannel.Scale, new HudKey(0f, 1f, 110.26f, -33.077f, 0f), new HudKey(0.2f, 0.55898f, 0f, 0f, 0f)),
        new(HudPart.Ring2, HudChannel.Scale, new HudKey(0f, 1f, 47.328f, -18.931f, 0f), new HudKey(0.26667f, 0.55126f, 0f, 0f, 0f)),
        new(HudPart.Ring3, HudChannel.Scale, new HudKey(0f, 1f, 95.74f, -23.935f, 0f), new HudKey(0.16667f, 0.77838f, 0f, 0f, 0f)),
        new(HudPart.Frame, HudChannel.Alpha, new HudKey(0f, 1f, 105.47f, -42.187f, 0f), new HudKey(0.26667f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring1Mesh, HudChannel.Alpha, new HudKey(0f, 1f, 250f, -75f, 0f), new HudKey(0.2f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring2Mesh, HudChannel.Alpha, new HudKey(0f, 1f, 105.47f, -42.187f, 0f), new HudKey(0.26667f, 0f, 0f, 0f, 0f)),
        new(HudPart.Ring3Mesh, HudChannel.Alpha, new HudKey(0f, 1f, 432f, -108f, 0f), new HudKey(0.16667f, 0f, 0f, 0f, 0f)),
    };
    private static readonly HudCurve[] HudNoticeFadeLoop =
    {
        new(HudPart.Ring1Mesh, HudChannel.EulerZ, new HudKey(0f, 0f, 0f, 0f, -45f), new HudKey(8f, -360f, 0f, 0f, 0f)),
        new(HudPart.Ring3Mesh, HudChannel.Scale, new HudKey(0.5f, 1f, 1.5803f, -1.1852f, 0f), new HudKey(1f, 0.90123f, -98.47f, 24.617f, 0f), new HudKey(1.1667f, 1.1292f, 411.54f, -61.731f, 0f), new HudKey(1.2667f, 0.9234f, -126.44f, 25.287f, 0f), new HudKey(1.4f, 1.0733f, 90.325f, -18.065f, 0f), new HudKey(1.5333f, 0.9662f, -67.599f, 10.14f, 0f), new HudKey(1.6333f, 1f, 0f, 0f, 0f), new HudKey(3.3f, 1f, 1.5803f, -1.1852f, 0f), new HudKey(3.8f, 0.90123f, -98.47f, 24.617f, 0f), new HudKey(3.9667f, 1.1292f, 411.54f, -61.731f, 0f), new HudKey(4.0667f, 0.9234f, -126.44f, 25.287f, 0f), new HudKey(4.2f, 1.0733f, 90.324f, -18.065f, 0f), new HudKey(4.3333f, 0.9662f, -67.599f, 10.14f, 0f), new HudKey(4.4333f, 1f, 0f, 0f, 0f), new HudKey(6.2333f, 1f, 1.5803f, -1.1852f, 0f), new HudKey(6.7333f, 0.90123f, -98.469f, 24.617f, 0f), new HudKey(6.9f, 1.1292f, 411.54f, -61.731f, 0f), new HudKey(7f, 0.9234f, -126.44f, 25.287f, 0f), new HudKey(7.1333f, 1.0733f, 90.324f, -18.065f, 0f), new HudKey(7.2667f, 0.9662f, -67.599f, 10.14f, 0f), new HudKey(7.3667f, 1f, 0f, 0f, 0f)),
    };

    // One of the HUD's two builds: its animated nodes, by the names both builds share.
    private sealed class HudVariant
    {
        public GameObject Root;
        public readonly Transform[] Parts = new Transform[8];
        public readonly Renderer[] Renderers = new Renderer[8];
        public readonly Vector3[] RestEuler = new Vector3[8];
        public readonly List<GameObject> Beams = new();
        public HudCurve[] FadeIn, FadeOut, Loop;
    }

    private enum HudFade { None, In, Out }

    private HudVariant _hudRest;
    private HudVariant _hudNoticeVariant;
    private HudVariant _hudShown;
    private HudFade _hudFade;
    private float _hudFadeTime;
    private float _hudLoopTime;
    private float _hintTimer;
    private bool _dinergateNotice;
    private MaterialPropertyBlock _hudBlock;

    // Whether the Dinergate is calling for attention. On: the notice HUD fades in and loops and the
    // body plays Hint, again every few seconds. Off: the notice HUD fades out and the rest HUD
    // returns.
    internal void SetDinergateNotice(bool on)
    {
        if (_dinergateNotice == on)
            return;
        _dinergateNotice = on;
        if (_hudShown == null)
            return;
        // The fade out ends in the swap to the other variant.
        _hudFade = HudFade.Out;
        _hudFadeTime = 0f;
        _hintTimer = 0f;
        if (on)
            _dinergateAnimator?.Play("Hint", 0, 0f);
    }

    internal bool DinergateNotice => _dinergateNotice;

    private void StartDinergateHud()
    {
        _hudRest = FindHudVariant("Icon", "HudIcon", HudIconFadeIn, HudIconFadeOut, HudIconFadeLoop);
        _hudNoticeVariant = FindHudVariant("RedPoint", "HudRedPoint", HudNoticeFadeIn, HudNoticeFadeOut, HudNoticeFadeLoop);
        _hudShown = null;
        _hudFade = HudFade.None;
        if (_hudRest == null || _hudNoticeVariant == null)
        {
            Context.Log.Warn("strategy diorama: the dinergate's HUD is not as expected, it stays as built");
            return;
        }
        _hudBlock ??= new MaterialPropertyBlock();
        ShowHudVariant(_dinergateNotice ? _hudNoticeVariant : _hudRest);
        Context.Coroutines.Start(DriveDinergateHud(_dinergate));
    }

    private HudVariant FindHudVariant(string animatorNode, string rootNode, HudCurve[] fadeIn, HudCurve[] fadeOut, HudCurve[] loop)
    {
        Transform holder = null;
        foreach (var node in _dinergate.GetComponentsInChildren<Transform>(true))
            if (node.name == animatorNode && node.parent != null && node.parent.name == "Dinergate_commendCenter_HUD")
            {
                holder = node;
                break;
            }
        var root = holder != null ? holder.Find(rootNode) : null;
        if (root == null)
            return null;
        var variant = new HudVariant { Root = holder.gameObject, FadeIn = fadeIn, FadeOut = fadeOut, Loop = loop };
        variant.Parts[(int)HudPart.Root] = root;
        variant.Parts[(int)HudPart.Ring1] = root.Find("ImgRound1");
        variant.Parts[(int)HudPart.Ring2] = root.Find("ImgRound2");
        variant.Parts[(int)HudPart.Ring3] = root.Find("ImgRound3");
        variant.Parts[(int)HudPart.Ring1Mesh] = root.Find("ImgRound1/ImgRound1");
        variant.Parts[(int)HudPart.Ring2Mesh] = root.Find("ImgRound2/ImgRound2");
        variant.Parts[(int)HudPart.Ring3Mesh] = root.Find("ImgRound3/ImgRound3");
        variant.Parts[(int)HudPart.Frame] = root.Find("ImgFrame");
        for (var i = 0; i < variant.Parts.Length; i++)
        {
            var part = variant.Parts[i];
            if (part == null)
                return null;
            variant.Renderers[i] = part.GetComponent<Renderer>();
            variant.RestEuler[i] = part.localEulerAngles;
        }
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
            if (node.name.StartsWith("guangshu", StringComparison.Ordinal))
                variant.Beams.Add(node.gameObject);
        return variant;
    }

    // Shows one variant from the start of its fade in, the other off. The notice variant carries
    // its light beams, the rest variant does not.
    private void ShowHudVariant(HudVariant variant)
    {
        var other = variant == _hudRest ? _hudNoticeVariant : _hudRest;
        other.Root.SetActive(false);
        foreach (var beam in variant.Beams)
            beam.SetActive(variant == _hudNoticeVariant);
        _hudShown = variant;
        _hudFade = HudFade.In;
        _hudFadeTime = 0f;
        ApplyHud(variant, variant.FadeIn, 0f);
        variant.Root.SetActive(true);
    }

    private System.Collections.IEnumerator DriveDinergateHud(GameObject dinergate)
    {
        while (dinergate != null && _dinergate == dinergate && _hudShown != null)
        {
            var dt = Time.deltaTime;
            try
            {
                _hudLoopTime = (_hudLoopTime + dt) % HudLoopLength;
                if (_hudFade != HudFade.None)
                {
                    _hudFadeTime += dt;
                    var fadingIn = _hudFade == HudFade.In;
                    var length = fadingIn ? HudFadeInLength : HudFadeOutLength;
                    ApplyHud(_hudShown, fadingIn ? _hudShown.FadeIn : _hudShown.FadeOut, MathF.Min(_hudFadeTime, length));
                    if (_hudFadeTime >= length)
                    {
                        _hudFade = HudFade.None;
                        if (!fadingIn)
                            ShowHudVariant(_dinergateNotice ? _hudNoticeVariant : _hudRest);
                    }
                }
                ApplyHud(_hudShown, _hudShown.Loop, _hudLoopTime);
                if (_dinergateNotice && !NavigationMenu.IsOpen)
                {
                    _hintTimer += dt;
                    if (_hintTimer >= HintEvery)
                    {
                        _hintTimer = 0f;
                        _dinergateAnimator?.Play("Hint", 0, 0f);
                    }
                }
            }
            catch (Exception ex)
            {
                Context.Log.Warn($"strategy diorama: dinergate HUD stopped: {ex.GetType().Name}: {ex.Message}");
                yield break;
            }
            yield return null;
        }
    }

    // Writes one clip's values at a time onto a variant. A part's three euler channels make one
    // rotation, in Unity's order (z, then x, then y), over the angles the part rests at.
    private void ApplyHud(HudVariant variant, HudCurve[] clip, float time)
    {
        Span<bool> turned = stackalloc bool[8];
        Span<System.Numerics.Vector3> euler = stackalloc System.Numerics.Vector3[8];
        foreach (var curve in clip)
        {
            var index = (int)curve.Part;
            var part = variant.Parts[index];
            var value = curve.At(time);
            switch (curve.Channel)
            {
                case HudChannel.PosY:
                    part.localPosition = new Vector3(0f, value, 0f);
                    break;
                case HudChannel.Scale:
                    part.localScale = new Vector3(value, value, value);
                    break;
                case HudChannel.Alpha:
                    {
                        var renderer = variant.Renderers[index];
                        if (renderer == null)
                            break;
                        renderer.GetPropertyBlock(_hudBlock);
                        _hudBlock.SetFloat(HudAlphaProperty, value);
                        renderer.SetPropertyBlock(_hudBlock);
                        break;
                    }
                default:
                    {
                        if (!turned[index])
                        {
                            turned[index] = true;
                            var rest = variant.RestEuler[index];
                            euler[index] = new System.Numerics.Vector3(rest.x, rest.y, rest.z);
                        }
                        var angles = euler[index];
                        if (curve.Channel == HudChannel.EulerX) angles.X = value;
                        else if (curve.Channel == HudChannel.EulerY) angles.Y = value;
                        else angles.Z = value;
                        euler[index] = angles;
                        break;
                    }
            }
        }
        const float toRadians = MathF.PI / 180f;
        for (var i = 0; i < 8; i++)
        {
            if (!turned[i])
                continue;
            var q = System.Numerics.Quaternion.CreateFromYawPitchRoll(euler[i].Y * toRadians, euler[i].X * toRadians, euler[i].Z * toRadians);
            variant.Parts[i].localRotation = new Quaternion(q.X, q.Y, q.Z, q.W);
        }
    }
}
