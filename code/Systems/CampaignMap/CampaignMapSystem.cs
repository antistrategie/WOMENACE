using System.Collections;
using static Il2CppInterop.Runtime.DelegateSupport;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Strategy;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Stands a walking Voymastina chibi on the campaign mission-select board (MissionSelectUIScreen's
// MissionPois container). She idles beside the selected mission's state icon and walks to a newly
// selected mission (playing her run animation, facing her travel direction) when the player picks
// a different one, always heading to the latest pick even if selection changes mid-walk. Any click
// during the walk snaps it to the end.
//
// The board lives on the screen's own UIDocument, outside the active screen's GetRootElement(),
// so everything here rides Harmony postfixes on the live element instances (MissionPoi.SetSelected
// for the selection, MissionPoisContainer.Init for the board) rather than screen-tree injection.
public sealed class CampaignMapSystem : JiangyuSystem
{
    // The chibi element is a square (every frame ships on a 256x256 canvas), sized so the
    // character reads a little taller than the 40px state icon.
    private const float ChibiHeight = 76f;

    // She stands to the left of the icon so she covers neither it nor the mission name to its
    // right. The gap is horizontal space between her centre line and the icon's left edge, and
    // her feet rest on the ground ellipse (Circle) drawn under the icon.
    private const float IconGap = 14f;
    private const float FootOffset = 2f;

    // Travel in the 1280x720 panel space, constant speed. Deliberately unhurried so the walk
    // reads as a little journey rather than a snap.
    private const float WalkSpeed = 150f;
    private const float ArriveEpsilon = 1.5f;

    private const int WaitFps = 20;
    private const int MoveFps = 22;
    private const int WaitFrameCount = 80;
    private const int MoveFrameCount = 18;

    // Each mission shows one of three state panels, each with its own icon and ground ellipse.
    private static readonly string[] StatePanels = ["Playable", "Completed", "Failed"];

    private Texture2D[] _waitFrames, _moveFrames;
    private bool _framesLoaded;

    // Live board state. Cleared when the strategy scene unloads.
    private VisualElement _container;
    private VisualElement _chibi;
    private VisualElement _selectedPoi;  // last node the game selected
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
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.MissionPoi", "SetSelected", OnPoiSetSelected);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.MissionPoisContainer", "Init", OnContainerInit);
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        // The board and its elements belong to the strategy scene we just left. Drop the refs and
        // stop the flipbooks so a stale element can never be poked.
        StopRoutine(ref _walkHandle);
        StopRoutine(ref _idleHandle);
        _container = null;
        _chibi = null;
        _selectedPoi = null;
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
        _selectedPoi = null;
        _currentPoi = null;
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
        // Two frames so the board has laid out and the game has preselected a mission.
        yield return null;
        yield return null;
        try
        {
            if (_container == null || !LoadFrames())
                yield break;
            EnsureChibi();
            HookSkip();
            var selected = SameRef(_selectedPoi?.parent, _container) ? _selectedPoi : FirstPoi();
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

    private VisualElement FirstPoi()
    {
        foreach (var poi in Pois())
        {
            if (Shown(poi))
                return poi;
        }
        return null;
    }

    // Selection changed: aim the chibi at the newly selected node. The walk loop reads _targetPoi
    // live, so switching selection rapidly just retargets an in-flight walk to the latest node
    // (from wherever the chibi actually is) rather than stranding it or restarting from the last
    // resting node.
    private void OnPoiSetSelected(PatchInfo info)
    {
        if (info.Args.Count == 0 || info.Args[0] is not bool selected || !selected)
            return;
        var target = Cast<VisualElement>(info.Instance);
        if (target == null)
            return;
        _selectedPoi = target;
        if (_container == null || _chibi == null)
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
        // element.
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

    // The point the chibi's feet rest on, in container space: left of the visible state panel's
    // icon, level with the ground ellipse under it. Falls back to the node's left edge.
    private Vector2 FootPoint(VisualElement poi)
    {
        VisualElement panel = null;
        foreach (var name in StatePanels)
        {
            var candidate = UI.Find(poi, UiSelector.Name(name));
            if (candidate != null && Shown(candidate))
            {
                panel = candidate;
                break;
            }
        }
        var icon = panel == null ? null : UI.Find(panel, UiSelector.Name(panel.name == "Playable" ? "Icon" : "Image"));
        var ground = panel == null ? null : UI.Find(panel, UiSelector.Name("Circle"));
        var iconBox = (icon ?? panel ?? poi).worldBound;
        var feetY = ground != null ? ground.worldBound.center.y : iconBox.yMax;
        var local = _container.WorldToLocal(new Vector2(iconBox.xMin - IconGap, feetY));
        return new Vector2(local.x, local.y + FootOffset);
    }

    private static bool Shown(VisualElement element)
    {
        try { return element.resolvedStyle.display != DisplayStyle.None && element.worldBound.width > 0.5f; }
        catch { return false; }
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
