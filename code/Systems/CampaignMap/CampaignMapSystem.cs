using System.Collections;
using Il2CppInterop.Runtime;
using static Il2CppInterop.Runtime.DelegateSupport;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Strategy;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Reskins the campaign mission-select map (MissionSelectUIScreen's MissionPois board) into the
// GFL1 mission UI: every mission node becomes a circular "spot" that carries its state from the
// mission's own status (blue = Played/cleared, hollow ring = still to do, red command post = the
// enemy-held final mission), keeping the vanilla mission-type glyph inside the circle plus the
// name strip and reward assets. The icon's selection-highlight box is removed: the chibi standing
// on the selected node marks the selection instead. That Voymastina chibi idles on the selected
// node and walks to a newly selected node (playing her run animation, facing her travel
// direction) when the player picks a different mission, always heading to the latest pick even if
// selection changes mid-walk. Any click during the walk snaps it to the end.
//
// The board lives on the screen's own UIDocument, outside the active screen's GetRootElement(),
// so everything here rides Harmony postfixes on the live element instances (MissionPoi.SetMission
// for the nodes, MissionPoisContainer.Init/SetSelectedMission for the board and selection) rather
// than screen-tree injection.
public sealed class CampaignMapSystem : JiangyuSystem
{
    // The circle a bit larger than the vanilla 36px icon so it reads as the node, with the
    // type glyph inset inside it. The final/boss node is drawn larger so its detailed art reads.
    private const float NodeSize = 42f;
    private const float EnemyNodeSize = 58f;
    private const float GlyphFraction = 0.54f;

    // The chibi element is a square (every frame ships on a 256x256 canvas), sized so the
    // character reads a little taller than a node. Her feet sit just below the node centre.
    private const float ChibiHeight = 76f;
    private const float FootOffset = 7f;

    // Travel in the 1280x720 panel space, constant speed. Deliberately unhurried so the walk
    // reads as a little journey rather than a snap.
    private const float WalkSpeed = 150f;
    private const float ArriveEpsilon = 1.5f;

    private const int WaitFps = 20;
    private const int MoveFps = 22;
    private const int WaitFrameCount = 80;
    private const int MoveFrameCount = 18;

    // Cleared nodes tint the glyph a darker shade of the circle's own blue so it reads as one
    // blue node, every other node keeps a light glyph.
    private static readonly Color ClearedGlyphColour = new(105f / 255f, 141f / 255f, 174f / 255f, 1f);
    private static readonly Color LightGlyphColour = new(1f, 1f, 1f, 0.92f);

    private Texture2D _spotComplete, _spotIncomplete, _spotEnemy;
    private Texture2D[] _waitFrames, _moveFrames;
    private bool _framesLoaded;

    // Live board state. Cleared when the strategy scene unloads.
    private VisualElement _container;
    private VisualElement _chibi;
    private VisualElement _currentPoi;   // node the chibi is resting on
    private VisualElement _targetPoi;    // node the chibi is walking toward (may change mid-walk)
    private Vector2 _chibiFoot;          // the chibi's live foot position, in container space
    private object _walkHandle;
    private object _idleHandle;
    private bool _walking;
    private bool _skipWalk;
    private bool _skipHooked;

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.MissionPoi", "SetMission", OnPoiSetMission);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.MissionPoi", "SetSelected", OnPoiSetSelected);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.MissionPoisContainer", "Init", OnContainerInit);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.MissionPoisContainer", "SetSelectedMission", OnSelectedMissionChanged);
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        // The board and its elements belong to the strategy scene we just left. Drop the refs and
        // stop the flipbooks so a stale element can never be poked.
        StopRoutine(ref _walkHandle);
        StopRoutine(ref _idleHandle);
        _container = null;
        _chibi = null;
        _currentPoi = null;
        _targetPoi = null;
        _walking = false;
        _skipHooked = false;
    }

    public override void OnUnload()
    {
        StopRoutine(ref _walkHandle);
        StopRoutine(ref _idleHandle);
        _chibi?.RemoveFromHierarchy();
        _chibi = null;
        _container = null;
        _currentPoi = null;
    }

    // ---- node reskin -------------------------------------------------------------------------

    private void OnPoiSetMission(PatchInfo info)
    {
        var poi = Cast<VisualElement>(info.Instance);
        if (poi == null)
            return;
        // Defer a frame so the vanilla icon's sprite and layout have resolved before we read them.
        Context.Coroutines.Start(ReskinNextFrame(poi));
    }

    private IEnumerator ReskinNextFrame(VisualElement poi)
    {
        yield return null;
        try { Reskin(poi); }
        catch (System.Exception ex) { Context.Log.Warn($"campaign map: reskin failed: {ex.Message}"); }
    }

    // The game re-shows the icon selection border whenever a node is (de)selected, so keep it
    // hidden here too. The chibi standing on the node marks the selection instead.
    private void OnPoiSetSelected(PatchInfo info)
    {
        var poi = Cast<VisualElement>(info.Instance);
        if (poi != null)
            Hide(poi, "MissionIconBorder");
    }

    private void Reskin(VisualElement poi)
    {
        if (poi.panel == null)
            return;
        var icon = UI.Find(poi, UiSelector.Name("MissionIcon"));
        if (icon == null)
            return;

        var isFinal = UI.Find(poi, UiSelector.Name("FinalAssetIcon")) != null;
        var glyph = IconSprite(icon);
        // The authoritative state is the mission's status, not the icon sprite: the game shows a
        // "play" starburst on the selected playable node, which is not the same as completion.
        var status = poi.TryCast<MissionPoi>()?.GetMission()?.GetStatus();
        var played = status == MissionStatus.Played;
        var circle = isFinal ? SpotEnemy() : played ? SpotComplete() : SpotIncomplete();
        if (circle == null)
            return;

        // The circle sits over the vanilla icon: the vanilla icon is hidden and its glyph is
        // redrawn inside the circle. The circle spills past the vanilla icon's box, so stop its
        // ancestors clipping it, and insert it as the first child so every later sibling (the
        // name strip) draws on top and it can never cover the mission title.
        SetOverflowVisible(poi);
        SetOverflowVisible(icon.parent);

        var node = UI.Find(poi, UiSelector.Name("wm-node"));
        if (node == null)
        {
            node = new VisualElement { name = "wm-node", pickingMode = PickingMode.Ignore };
            node.style.position = new StyleEnum<Position>(Position.Absolute);
            node.style.justifyContent = new StyleEnum<Justify>(Justify.Center);
            node.style.alignItems = new StyleEnum<Align>(Align.Center);
            icon.parent.Insert(0, node);
        }

        var size = isFinal ? EnemyNodeSize : NodeSize;
        var box = icon.layout;
        if (!float.IsNaN(box.x) && !float.IsNaN(box.y) && !float.IsNaN(box.width) && !float.IsNaN(box.height))
        {
            var cx = box.x + box.width / 2f;
            var cy = box.y + box.height / 2f;
            node.style.left = new StyleLength(cx - size / 2f);
            node.style.top = new StyleLength(cy - size / 2f);
        }
        node.style.width = new StyleLength(size);
        node.style.height = new StyleLength(size);
        node.style.backgroundImage = new StyleBackground(circle);

        // Keep the mission-type glyph inside the circle on every node except the final one
        // (whose command-post art is self-contained). A cleared node tints the glyph the
        // circle's own blue so it reads as one blue node, the rest stay light.
        var inner = node.childCount > 0 ? node.ElementAt(0) : null;
        if (!isFinal && glyph != null)
        {
            if (inner == null)
            {
                inner = new VisualElement { name = "wm-glyph", pickingMode = PickingMode.Ignore };
                node.Add(inner);
            }
            inner.style.width = new StyleLength(size * GlyphFraction);
            inner.style.height = new StyleLength(size * GlyphFraction);
            inner.style.backgroundImage = new StyleBackground(glyph);
            inner.style.unityBackgroundImageTintColor = new StyleColor(played ? ClearedGlyphColour : LightGlyphColour);
            inner.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);
        }
        else if (inner != null)
        {
            inner.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
        }

        // Remove the selection highlight box the game draws over the selected node's icon. The
        // chibi standing on the node (plus the name strip's own highlight) marks the selection.
        Hide(poi, "MissionIconBorder");
        icon.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
    }

    private static Sprite IconSprite(VisualElement icon)
    {
        try { return icon.resolvedStyle.backgroundImage.sprite; }
        catch { return null; }
    }

    private static void SetOverflowVisible(VisualElement element)
    {
        if (element != null)
            element.style.overflow = new StyleEnum<Overflow>(Overflow.Visible);
    }

    private static void Hide(VisualElement root, string name)
    {
        var element = UI.Find(root, UiSelector.Name(name));
        if (element != null)
            element.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
    }

    // ---- chibi -------------------------------------------------------------------------------

    private void OnContainerInit(PatchInfo info)
    {
        var container = Cast<VisualElement>(info.Instance);
        if (container == null)
            return;
        // The board can rebuild within the same scene (reopening the map) as a fresh container
        // element. Reset per-board state so a stale walk cannot drive the new chibi, and so
        // click-to-skip re-attaches to the new container instead of the discarded one.
        StopRoutine(ref _walkHandle);
        StopRoutine(ref _idleHandle);
        _walking = false;
        _skipHooked = false;
        _currentPoi = null;
        _targetPoi = null;
        _container = container;
        Context.Coroutines.Start(InitChibiNextFrame());
    }

    private IEnumerator InitChibiNextFrame()
    {
        // Two frames: one for the board to lay out, one for our reskin pass to settle.
        yield return null;
        yield return null;
        try
        {
            if (_container == null || !LoadFrames())
                yield break;
            EnsureChibi();
            HookSkip();
            var selected = SelectedPoi();
            if (selected != null)
            {
                _currentPoi = selected;
                _targetPoi = selected;
                PlaceChibi(selected);
            }
        }
        catch (System.Exception ex) { Context.Log.Warn($"campaign map: chibi init failed: {ex.Message}"); }
    }

    private void EnsureChibi()
    {
        if (_chibi != null && _chibi.parent == _container)
            return;
        _chibi?.RemoveFromHierarchy();
        _chibi = new VisualElement { name = "wm-chibi", pickingMode = PickingMode.Ignore };
        _chibi.style.position = new StyleEnum<Position>(Position.Absolute);
        _chibi.style.width = new StyleLength(ChibiHeight);
        _chibi.style.height = new StyleLength(ChibiHeight);
        _container.Add(_chibi);
        StartIdle();
    }

    // The selected node: the POI showing its name-strip selection border (InfoBorder). The icon
    // border is hidden by the reskin, so the name-strip border is the readable selection tell.
    // Falls back to the first node.
    private VisualElement SelectedPoi()
    {
        VisualElement first = null;
        foreach (var poi in Pois())
        {
            first ??= poi;
            var border = UI.Find(poi, UiSelector.Name("InfoBorder"));
            if (border != null && BorderShown(border))
                return poi;
        }
        return first;
    }

    private static bool BorderShown(VisualElement border)
    {
        try { return border.resolvedStyle.display != DisplayStyle.None && border.resolvedStyle.width > 0.5f; }
        catch { return false; }
    }

    // Selection changed: aim the chibi at the newly selected node. The walk loop reads _targetPoi
    // live, so switching selection rapidly just retargets an in-flight walk to the latest node
    // (from wherever the chibi actually is) rather than stranding it or restarting from the last
    // resting node.
    private void OnSelectedMissionChanged(PatchInfo info)
    {
        if (_container == null || _chibi == null)
            return;
        var mission = Cast<Mission>(info.Args.Count > 0 ? info.Args[0] : null);
        var target = mission != null ? PoiForMission(mission) : SelectedPoi();
        if (target == null)
            return;
        _targetPoi = target;

        // Nowhere to walk from yet (first show): just stand there.
        if (_currentPoi == null && _walkHandle == null)
        {
            _currentPoi = target;
            PlaceChibi(target);
            return;
        }
        // Already resting on the target and not walking: nothing to do. POIs are compared by
        // native pointer, as separate interop calls can hand back distinct wrappers for one
        // element (the same reason Mission equality uses SameRef).
        if (_walkHandle == null && SameRef(_currentPoi, target))
            return;
        // Start the walk loop if one is not already running. A running loop picks up the new
        // target on its next step. Start can run WalkLoop to completion synchronously (already at
        // the target), which clears _walkHandle itself, so only keep the handle when the loop
        // actually yielded and is still walking, or a synchronous finish would leave a stale
        // non-null handle that blocks every future walk.
        if (_walkHandle == null)
        {
            var handle = Context.Coroutines.Start(WalkLoop());
            if (_walking)
                _walkHandle = handle;
        }
    }

    private IEnumerator WalkLoop()
    {
        _walking = true;
        _skipWalk = false;
        StopRoutine(ref _idleHandle);

        var animTime = 0f;
        VisualElement destPoi = null;
        var dest = Vector2.zero;
        while (_chibi != null)
        {
            var target = _targetPoi;
            if (target == null)
                break;

            // The destination is constant until the selection retargets, so only re-resolve it (a
            // recursive element search plus a layout read) when the target node actually changes.
            if (!SameRef(destPoi, target))
            {
                destPoi = target;
                dest = FootPoint(target);
            }

            // A click during the walk snaps straight to the latest target.
            if (_skipWalk)
            {
                SetFoot(dest);
                _currentPoi = target;
                break;
            }

            var delta = dest - _chibiFoot;
            var distance = delta.magnitude;
            if (distance <= ArriveEpsilon)
            {
                SetFoot(dest);
                _currentPoi = target;
                // Arrived, and the target has not moved on: done. Otherwise keep walking to the
                // node that was selected while we were arriving.
                if (SameRef(_targetPoi, target))
                    break;
                yield return null;
                continue;
            }

            FaceDirection(delta.x >= 0f ? 1f : -1f);
            var step = Mathf.Min(distance, WalkSpeed * Time.deltaTime);
            SetFoot(_chibiFoot + delta / distance * step);
            animTime += Time.deltaTime;
            SetFrame(_moveFrames[(int)(animTime * MoveFps) % MoveFrameCount]);
            yield return null;
        }

        _walking = false;
        _skipWalk = false;
        _walkHandle = null;
        StartIdle();
    }

    private void StartIdle()
    {
        StopRoutine(ref _idleHandle);
        _idleHandle = Context.Coroutines.Start(IdleLoop());
    }

    private IEnumerator IdleLoop()
    {
        if (_chibi == null || _waitFrames == null)
            yield break;
        FaceDirection(1f);
        if (_currentPoi != null)
            SetFoot(FootPoint(_currentPoi));
        var wait = new WaitForSeconds(1f / WaitFps);
        var i = 0;
        while (_chibi != null)
        {
            SetFrame(_waitFrames[i % WaitFrameCount]);
            i++;
            yield return wait;
        }
    }

    // Place the chibi standing on a node at rest (idle pose), used for the first show.
    private void PlaceChibi(VisualElement poi)
    {
        FaceDirection(1f);
        SetFoot(FootPoint(poi));
        StartIdle();
    }

    // The point the chibi's feet rest on: the node's centre in container space, nudged down. The
    // vanilla icon is hidden (its bounds go stale), so anchor on IconPos, which keeps the node's
    // live position, falling back to the reskinned circle or the POI itself.
    private Vector2 FootPoint(VisualElement poi)
    {
        var anchor = UI.Find(poi, UiSelector.Name("IconPos"))
                     ?? UI.Find(poi, UiSelector.Name("wm-node"))
                     ?? poi;
        var world = anchor.worldBound.center;
        var local = _container.WorldToLocal(world);
        return new Vector2(local.x, local.y + FootOffset);
    }

    private void SetFoot(Vector2 foot)
    {
        _chibiFoot = foot;
        _chibi.style.left = new StyleLength(foot.x - ChibiHeight / 2f);
        _chibi.style.top = new StyleLength(foot.y - ChibiHeight);
    }

    private void SetFrame(Texture2D frame) => _chibi.style.backgroundImage = new StyleBackground(frame);

    private void FaceDirection(float sign) =>
        _chibi.style.scale = new StyleScale(new Scale(new Vector3(sign, 1f, 1f)));

    // ---- click to skip -----------------------------------------------------------------------

    private void HookSkip()
    {
        if (_skipHooked || _container == null)
            return;
        _skipHooked = true;
        _container.RegisterCallback(
            ConvertDelegate<EventCallback<PointerDownEvent>>((System.Action<PointerDownEvent>)(_ =>
            {
                if (_walking)
                    _skipWalk = true;
            })),
            TrickleDown.TrickleDown);
    }

    // ---- helpers -----------------------------------------------------------------------------

    private System.Collections.Generic.IEnumerable<VisualElement> Pois()
    {
        for (var i = 0; i < _container.childCount; i++)
        {
            var child = _container.ElementAt(i);
            if (child != null && child.TryCast<MissionPoi>() != null)
                yield return child;
        }
    }

    private VisualElement PoiForMission(Mission mission)
    {
        foreach (var poi in Pois())
        {
            var mp = poi.TryCast<MissionPoi>();
            if (mp != null && SameRef(mp.GetMission(), mission))
                return poi;
        }
        return null;
    }

    private bool LoadFrames()
    {
        if (_framesLoaded)
            return _waitFrames != null && _moveFrames != null;
        _framesLoaded = true;
        _waitFrames = LoadSequence("wait_", WaitFrameCount);
        _moveFrames = LoadSequence("move_", MoveFrameCount);
        if (_waitFrames == null || _moveFrames == null)
            Context.Log.Warn("campaign map: chibi frames missing from the bundle");
        return _waitFrames != null && _moveFrames != null;
    }

    private Texture2D[] LoadSequence(string prefix, int count)
    {
        var frames = new Texture2D[count];
        for (var i = 0; i < count; i++)
        {
            frames[i] = Context.Assets.Load<Texture2D>($"{prefix}{i:000}");
            if (frames[i] == null)
                return null;
        }
        return frames;
    }

    private Texture2D SpotComplete() => _spotComplete ??= Context.Assets.Load<Texture2D>("spot_complete");
    private Texture2D SpotIncomplete() => _spotIncomplete ??= Context.Assets.Load<Texture2D>("spot_incomplete");
    private Texture2D SpotEnemy() => _spotEnemy ??= Context.Assets.Load<Texture2D>("spot_enemy");

    private void StopRoutine(ref object handle)
    {
        if (handle != null)
            Context.Coroutines.Stop(handle);
        handle = null;
    }

    private static T Cast<T>(object instance) where T : Il2CppObjectBase =>
        (instance as Il2CppObjectBase)?.TryCast<T>();

    private static bool SameRef(Il2CppObjectBase a, Il2CppObjectBase b) =>
        a != null && b != null && a.Pointer == b.Pointer;
}
