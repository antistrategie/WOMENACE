using System.Collections;
using Il2CppMenace.States;
using Il2CppMenace.UI;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Cheyanne lining up the shot: a first-person target range laid over the
// mission.
//
// First-person is faked, and deliberately so. There is no camera and no 3D:
// the OS cursor is locked and hidden, a reticle sits pinned at screen centre,
// and a gridded plane carrying the targets pans underneath by raw mouse
// delta. Relative input under a fixed crosshair is what makes aim feel like
// aiming, and the grid exists so the motion is visible. The real trainers'
// "flat wall" modes are built exactly this way.
//
// Three phases. A countdown that says what to do, a timed round, then a
// results card the player leaves by firing. The round keeps a fixed handful
// of balls on the wall at all times: a ball stays until hit and respawns
// elsewhere, every hit scores, and points scale the shot without cap (the
// rates are BounceChain's exchange constants). Layout is UXML
// (cheyanne/aim-trainer) so the chrome is authored, not built; only the balls
// and the pan are code.
//
// The cursor lock is the one dangerous resource here: every exit path MUST
// release it or the player is stranded mid-mission with no pointer. It is
// released on finish, on cancel, on any exception (the Run finally), on
// Close() (which scene loads call), and Cheyanne.Unlock exists as a bridge
// escape hatch.
internal static class CheyanneAimTrainer
{
    public const float RoundSeconds = 7f;
    public const int BallCount = 3;
    private const float BallSize = 20f;

    // The spawn window, as a fraction of the screen each side of centre. A
    // tighter window keeps the round about flicking between near targets
    // rather than trekking across the wall.
    private const float SpawnFractionX = 0.24f;
    private const float SpawnFractionY = 0.20f;

    // A downed ball waits a beat before coming back, somewhere in this range,
    // so the next target is a reaction rather than an appointment.
    private const float RespawnDelayMin = 0.3f;
    private const float RespawnDelayMax = 0.9f;
    private const int CountdownFrom = 3;

    // Reticle-to-ball-centre distance that counts as a hit: the ball itself,
    // with only a sliver of forgiveness. A generous pad read as a hitbox
    // bigger than the circle.
    private const float HitRadius = BallSize / 2f + 1.5f;

    private const float WorldSize = 4096f;
    private const float WorldCentre = WorldSize / 2f;
    // How far the aim point may wander from the wall's centre, so the player
    // can look around without getting lost in an empty corner of the plane.
    private const float RoamHalf = 640f;

    private static VisualElement _root;
    private static VisualElement _world;
    private static VisualElement _timerFill;
    private static Label _points;
    private static VisualElement _countStack;
    private static readonly List<VisualElement> _lights = new();
    private static Label _hint;
    private static VisualElement _results;

    private sealed class Ball
    {
        public VisualElement Element;
        public Vector2 Pos;      // centre, world coords
        public bool Waiting;     // downed, waiting out its respawn delay
        public float RespawnAt;  // round-elapsed seconds to come back at
    }

    private static readonly List<Ball> Balls = new();

    private static ModContext _context;
    private static RenderTexture _backdrop;
    private static Vector2 _aim;
    private static int _score;
    private static int _hits;
    private static int _shots;
    private static bool _cancelled;
    private static bool _continued;
    private static bool _locked;

    public static bool IsOpen => _root != null;

    // Opens the overlay and runs the round. onDone gets the points once the
    // player fires off the results card; onCancel fires instead if they back
    // out. Returns false when there is nothing to draw on, which the caller
    // must treat as "the trainer did not run".
    public static bool Open(ModContext context, Action<int> onDone, Action onCancel)
    {
        if (IsOpen)
            return false;
        try
        {
            var host = FindHost(context);
            if (host == null)
            {
                context.Log.Warn("cheyanne trainer: no UI root to draw on, shot proceeds unaimed");
                return false;
            }

            var tree = context.Assets.Load<VisualTreeAsset>("cheyanne/aim-trainer");
            if (tree == null)
            {
                context.Log.Warn("cheyanne trainer: uxml 'cheyanne/aim-trainer' not in the mod bundles, shot proceeds unaimed");
                return false;
            }

            _context = context;
            _score = 0;
            _hits = 0;
            _shots = 0;
            _cancelled = false;
            _continued = false;
            _aim = new Vector2(WorldCentre, WorldCentre);

            var container = tree.Instantiate();
            Fill(container);
            host.Add(container);
            _root = container;

            _world = UI.Find(container, UiSelector.Name("wm-aim-world"));
            _timerFill = UI.Find(container, UiSelector.Name("wm-aim-timer-fill"));
            _points = UI.Find(container, UiSelector.Name("wm-aim-points"))?.TryCast<Label>();
            _countStack = UI.Find(container, UiSelector.Name("wm-aim-countstack"));
            _lights.Clear();
            for (var i = 0; i < CountdownFrom; i++)
                _lights.Add(UI.Find(container, UiSelector.Name($"wm-aim-light-{i}")));
            _hint = UI.Find(container, UiSelector.Name("wm-aim-hint"))?.TryCast<Label>();
            _results = UI.Find(container, UiSelector.Name("wm-aim-results"));

            var fire = UI.Find(container, UiSelector.Name("wm-aim-continue"))?.TryCast<Button>();
            if (fire != null)
                fire.clickable.clicked += (Action)(() => { Sound.Click(); _continued = true; });

            BlurBackdrop(container);

            BuildGrid();
            AimInput.Detect(context);
            Lock();
            context.Coroutines.Start(Run(context, onDone, onCancel));
            return true;
        }
        catch (Exception ex)
        {
            context.Log.Warn($"cheyanne trainer: open failed: {ex.GetType().Name}: {ex.Message}");
            Close();
            return false;
        }
    }

    private static IEnumerator Run(ModContext context, Action<int> onDone, Action onCancel)
    {
        try
        {
            yield return Countdown();
            if (_root != null && !_cancelled)
                yield return Round();
            if (_root != null && !_cancelled)
                yield return Results();
        }
        finally
        {
            // Whatever happened above, the pointer comes back.
            Unlock();
        }

        var abandoned = _cancelled || _root == null;
        var score = _score;
        Close();

        if (abandoned)
        {
            context.Log.Info("cheyanne trainer: round abandoned, shot held");
            onCancel?.Invoke();
            yield break;
        }
        context.Log.Info($"cheyanne trainer: {score} points -> {Describe(BounceChain.ForPoints(score))}");
        onDone?.Invoke(score);
    }

    // The countdown explains the job before asking for it, and the wall
    // already pans while it runs, so the player gets the feel in hand before
    // anything is at stake. Start-light grammar: the red lights come on one
    // per beat with the beep, and the round begins the moment the last beat
    // ends.
    private static IEnumerator Countdown()
    {
        for (var n = 0; n < CountdownFrom && !_cancelled && _root != null; n++)
        {
            if (n < _lights.Count && _lights[n] != null)
                _lights[n].AddToClassList("wm-aim-light--on");
            CheyanneAimSound.Light(_context);
            var t = 0f;
            while (t < 1f && !_cancelled && _root != null)
            {
                t += Time.deltaTime;
                Pan();
                if (AimInput.CancelPressed())
                    _cancelled = true;
                yield return null;
            }
        }
        _countStack?.SetVisible(false);
    }

    private static IEnumerator Round()
    {
        for (var i = 0; i < BallCount; i++)
            SpawnBall();

        var elapsed = 0f;
        while (elapsed < RoundSeconds && !_cancelled && _root != null)
        {
            elapsed += Time.deltaTime;
            Pan();

            if (AimInput.CancelPressed())
            {
                _cancelled = true;
                break;
            }
            if (AimInput.FirePressed())
            {
                _shots++;
                for (var i = 0; i < Balls.Count; i++)
                {
                    if (Balls[i].Waiting
                        || (Balls[i].Pos - _aim).sqrMagnitude > HitRadius * HitRadius)
                        continue;
                    _hits++;
                    _score += BounceChain.PointsPerHit;
                    CheyanneAimSound.Hit(_context);
                    Balls[i].Waiting = true;
                    Balls[i].RespawnAt = elapsed + UnityEngine.Random.Range(RespawnDelayMin, RespawnDelayMax);
                    Balls[i].Element.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
                    ReviveIfEmpty(elapsed);
                    break;   // one ball per trigger pull
                }
                if (_points != null)
                    _points.text = $"{_score} PTS";
            }

            foreach (var ball in Balls)
            {
                if (!ball.Waiting || elapsed < ball.RespawnAt)
                    continue;
                ball.Waiting = false;
                ball.Element.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);
                Respawn(ball);
            }

            if (_timerFill != null)
                _timerFill.style.width = new StyleLength(new Length(
                    Mathf.Clamp01(1f - elapsed / RoundSeconds) * 100f, LengthUnit.Percent));
            yield return null;
        }

        foreach (var ball in Balls)
            ball.Element?.RemoveFromHierarchy();
        Balls.Clear();
    }

    // The results card. The pointer comes back here (the card has a button),
    // and nothing resolves until the player pulls the trigger on it.
    private static IEnumerator Results()
    {
        Unlock();
        _hint?.SetVisible(false);
        // The card carries the score; the top readout would just repeat it.
        _points?.SetVisible(false);
        if (_timerFill != null)
            _timerFill.style.width = new StyleLength(new Length(0f, LengthUnit.Percent));

        var shot = BounceChain.ForPoints(_score);
        var points = UI.Find(_root, UiSelector.Name("wm-aim-results-points"))?.TryCast<Label>();
        if (points != null)
            points.text = $"{_score} PTS";

        var rows = UI.Find(_root, UiSelector.Name("wm-aim-results-rows"));
        if (rows != null)
        {
            rows.Clear();
            rows.Add(Row(shot.Bounces == 1 ? "ricochet" : "ricochets", $"{shot.Bounces}"));
            rows.Add(Row(shot.Range == 1 ? "tile of reach" : "tiles of reach", $"{shot.Range}"));
            if (_shots > 0)
                rows.Add(Row($"accuracy ({_hits} of {_shots})", $"{Mathf.RoundToInt(100f * _hits / _shots)}%"));
            rows.Add(Row("hits per second", $"{_hits / RoundSeconds:0.0}"));
            if (shot.Bounces == 0)
            {
                var note = new Label("the round goes straight through and stops");
                note.AddToClassList("wm-aim-row-note");
                rows.Add(note);
            }
        }
        _results?.SetVisible(true);

        while (!_continued && !_cancelled && _root != null)
            yield return null;
    }

    // ---- the wall -----------------------------------------------------------

    // Move the aim by this frame's mouse delta and slide the wall so the aim
    // point sits under the fixed reticle. Balls ride the wall, so this one
    // translation moves everything, grid included.
    private static void Pan()
    {
        if (_root == null || _world == null)
            return;
        var delta = AimInput.Delta() * PlayerSettingsSystem.AimSensitivity;
        _aim.x = Mathf.Clamp(_aim.x + delta.x, WorldCentre - RoamHalf, WorldCentre + RoamHalf);
        _aim.y = Mathf.Clamp(_aim.y + delta.y, WorldCentre - RoamHalf, WorldCentre + RoamHalf);

        var w = _root.resolvedStyle.width;
        var h = _root.resolvedStyle.height;
        if (float.IsNaN(w) || w <= 0) w = 1920f;
        if (float.IsNaN(h) || h <= 0) h = 1080f;

        _world.style.left = w / 2f - _aim.x;
        _world.style.top = h / 2f - _aim.y;
    }

    // The mission, frozen and defocused, as the range's backdrop. UI Toolkit
    // has no backdrop blur, but the game underneath is turn-based and still,
    // so one grab of the frame at open, bounced down through small render
    // targets and stretched back up bilinearly, reads as a blur for free. A
    // dim layer then pulls it down so the range sits on top. Any failure just
    // leaves the plain translucent backdrop.
    private static void BlurBackdrop(VisualElement container)
    {
        try
        {
            var frame = UnityEngine.ScreenCapture.CaptureScreenshotAsTexture();
            if (frame == null)
                return;
            var w = Mathf.Max(frame.width / 8, 8);
            var h = Mathf.Max(frame.height / 8, 8);
            var tiny = RenderTexture.GetTemporary(w / 2, h / 2);
            _backdrop = new RenderTexture(w, h, 0) { filterMode = FilterMode.Bilinear };
            Graphics.Blit(frame, tiny);        // down, hard
            Graphics.Blit(tiny, _backdrop);    // back up a step, smoothing again
            RenderTexture.ReleaseTemporary(tiny);
            UnityEngine.Object.Destroy(frame);

            var screen = UI.Find(container, UiSelector.Name("wm-aim-screen"));
            if (screen == null)
                return;
            screen.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_backdrop));
            var dim = new VisualElement { pickingMode = PickingMode.Ignore };
            dim.AddToClassList("wm-aim-dim");
            screen.Insert(0, dim);
        }
        catch (Exception ex)
        {
            Log.Debug($"[CheyanneTrainer] backdrop blur unavailable: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The wall's own grid, one crisp pixel per line, minor lines with a
    // brighter major every few: enough texture for the pan to read at a
    // glance without shouting over the mission showing through behind it.
    private static void BuildGrid()
    {
        if (_world == null)
            return;
        for (var offset = 0f; offset <= WorldSize; offset += 64f)
        {
            var major = (int)offset % 256 == 0;
            var vertical = new VisualElement { pickingMode = PickingMode.Ignore };
            vertical.AddToClassList("wm-aim-gridline");
            if (major)
                vertical.AddToClassList("wm-aim-gridline--major");
            vertical.style.left = offset;
            vertical.style.top = 0f;
            vertical.style.width = 1f;
            vertical.style.height = WorldSize;
            _world.Add(vertical);

            var horizontal = new VisualElement { pickingMode = PickingMode.Ignore };
            horizontal.AddToClassList("wm-aim-gridline");
            if (major)
                horizontal.AddToClassList("wm-aim-gridline--major");
            horizontal.style.left = 0f;
            horizontal.style.top = offset;
            horizontal.style.width = WorldSize;
            horizontal.style.height = 1f;
            _world.Add(horizontal);
        }
    }

    // The wall must never be bare: if the last visible ball just went down,
    // the earliest-due waiting one comes back immediately. The random delay
    // stands for everything else, so the guarantee costs the rhythm nothing.
    private static void ReviveIfEmpty(float elapsed)
    {
        foreach (var ball in Balls)
            if (!ball.Waiting)
                return;
        Ball earliest = null;
        foreach (var ball in Balls)
            if (earliest == null || ball.RespawnAt < earliest.RespawnAt)
                earliest = ball;
        if (earliest == null)
            return;
        earliest.Waiting = false;
        earliest.Element.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex);
        Respawn(earliest);
    }

    private static void SpawnBall()
    {
        var element = new VisualElement();
        element.AddToClassList("wm-aim-ball");
        element.pickingMode = PickingMode.Ignore;
        _world.Add(element);
        var ball = new Ball { Element = element };
        Balls.Add(ball);
        Respawn(ball);
    }

    // A fresh spot on the wall: inside the window the player can reach, not
    // under the reticle, not on top of another ball. Best effort after a dozen
    // tries, which in a field this size means never in practice.
    private static void Respawn(Ball ball)
    {
        var w = _root?.resolvedStyle.width ?? 1920f;
        var h = _root?.resolvedStyle.height ?? 1080f;
        if (float.IsNaN(w) || w <= 0) w = 1920f;
        if (float.IsNaN(h) || h <= 0) h = 1080f;
        var halfX = w * SpawnFractionX;
        var halfY = h * SpawnFractionY;

        var pos = ball.Pos;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            pos = new Vector2(
                WorldCentre + UnityEngine.Random.Range(-halfX, halfX),
                WorldCentre + UnityEngine.Random.Range(-halfY, halfY));
            if ((pos - _aim).magnitude < 140f)
                continue;
            var clear = true;
            foreach (var other in Balls)
                if (other != ball && (other.Pos - pos).magnitude < BallSize * 2f)
                    clear = false;
            if (clear)
                break;
        }
        ball.Pos = pos;
        ball.Element.style.left = pos.x - BallSize / 2f;
        ball.Element.style.top = pos.y - BallSize / 2f;
        CheyanneAimSound.Spawn(_context);
    }

    // ---- plumbing -----------------------------------------------------------

    private static string Describe(BounceChain.Shot shot)
        => $"{shot.Bounces} bounces, {shot.Range} tiles";

    private static VisualElement Row(string label, string value)
    {
        var row = new VisualElement();
        row.AddToClassList("wm-aim-row");
        var text = new Label(label);
        text.AddToClassList("wm-aim-row-label");
        row.Add(text);
        var big = new Label(value);
        big.AddToClassList("wm-aim-row-value");
        row.Add(big);
        return row;
    }

    // Where the overlay hangs. The Dialogs layer first: it is the game's own
    // home for a modal, above the screens and indifferent to which one is
    // active. The tactical screen's roots are fallbacks; every candidate is
    // named in the failure log because a bare "no root" already cost a run.
    private static VisualElement FindHost(ModContext context)
    {
        var manager = UIManager.Get();
        var screen = manager?.GetActiveScreen()?.TryCast<UITactical>()
            ?? TacticalState.Get()?.GetUI()?.TryCast<UITactical>();

        var candidates = new (string Name, VisualElement Element)[]
        {
            ("layer:Dialogs", manager?.GetLayer(PermanentUILayers.Dialogs)),
            ("permanentLayersRoot", manager?.GetPermanentLayersRoot()),
            ("tactical:rootElement", screen?.GetRootElement()),
            ("tactical:rootParent", screen?.GetRootParent()),
            ("tactical:uiDocument", screen?.GetUIDocument()?.rootVisualElement),
        };

        foreach (var candidate in candidates)
            if (candidate.Element != null)
            {
                Log.Debug($"[CheyanneTrainer] hosting on {candidate.Name}");
                return candidate.Element;
            }

        var active = manager?.GetActiveScreen();
        context.Log.Warn("cheyanne trainer: every UI root was null. manager="
            + (manager != null) + " activeScreen="
            + (active != null ? active.GetIl2CppType().Name : "null")
            + " tacticalCast=" + (screen != null));
        return null;
    }

    private static void Fill(VisualElement element)
    {
        element.style.position = new StyleEnum<Position>(Position.Absolute);
        element.style.left = new StyleLength(0f);
        element.style.top = new StyleLength(0f);
        element.style.right = new StyleLength(0f);
        element.style.bottom = new StyleLength(0f);
    }

    private static void Lock()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;
        _locked = true;
    }

    private static void Unlock()
    {
        if (!_locked)
            return;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
        _locked = false;
    }

    // The bridge escape hatch (Cheyanne.Unlock): a stranded pointer costs one
    // dev-verb call instead of a restart.
    public static void ForceUnlock()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
        _locked = false;
    }

    public static void Close()
    {
        Unlock();
        Balls.Clear();
        if (_backdrop != null)
        {
            _backdrop.Release();
            UnityEngine.Object.Destroy(_backdrop);
            _backdrop = null;
        }
        _root?.RemoveFromHierarchy();
        _root = null;
        _world = null;
        _timerFill = null;
        _points = null;
        _countStack = null;
        _lights.Clear();
        _hint = null;
        _results = null;
    }
}

// Raw mouse input for the trainer, backend-agnostic. The game ships both
// input backends; whichever answers is used, and which one is logged once.
// Deltas come back in screen convention (y down) and are position-free, which
// is the point: a locked cursor has no position worth reading.
internal static class AimInput
{
    // The legacy axis is pre-scaled by the input manager rather than being
    // pixels, so it gets its own factor to land in the same range.
    private const float LegacyAxisScale = 18f;

    private static bool _useNewInput;

    public static void Detect(ModContext context)
    {
        try
        {
            _useNewInput = UnityEngine.InputSystem.Mouse.current != null;
        }
        catch
        {
            _useNewInput = false;
        }
        context.Log.Debug($"cheyanne trainer: input backend = {(_useNewInput ? "InputSystem" : "legacy")}");
    }

    public static Vector2 Delta()
    {
        try
        {
            if (_useNewInput)
            {
                var d = UnityEngine.InputSystem.Mouse.current.delta.ReadValue();
                return new Vector2(d.x, -d.y);
            }
            return new Vector2(
                Input.GetAxisRaw("Mouse X"),
                -Input.GetAxisRaw("Mouse Y")) * LegacyAxisScale;
        }
        catch
        {
            return Vector2.zero;
        }
    }

    public static bool FirePressed()
    {
        try
        {
            return _useNewInput
                ? UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame
                : Input.GetMouseButtonDown(0);
        }
        catch
        {
            return false;
        }
    }

    public static bool CancelPressed()
    {
        try
        {
            if (_useNewInput)
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            }
            return Input.GetKeyDown(KeyCode.Escape);
        }
        catch
        {
            return false;
        }
    }
}

// The trainer's sounds. Loaded straight out of the mod's own bundle and
// played through a 2D AudioSource of their own rather than a SoundBank: a
// bank buys positional audio and bus routing a flat overlay has no use for.
// Audio additions register under "<folder>__<file>", so clips in
// assets/additions/audio/cheyanne/ carry the cheyanne__ prefix; the bare name
// is kept as a fallback because the two failure logs read the same, which is
// what hid the prefix for a run.
internal static class CheyanneAimSound
{
    private const string HitClip = "wm_aim_hit";
    private const string SpawnClip = "wm_aim_spawn";
    private const string LightClip = "wm_aim_light";

    private static AudioSource _source;
    private static readonly Dictionary<string, AudioClip> Clips = new(StringComparer.Ordinal);

    public static void Hit(ModContext context) => Play(context, HitClip);

    public static void Spawn(ModContext context) => Play(context, SpawnClip);

    public static void Light(ModContext context) => Play(context, LightClip);

    private static void Play(ModContext context, string name)
    {
        try
        {
            if (!Clips.TryGetValue(name, out var clip))
            {
                clip = context.Assets.Load<AudioClip>($"cheyanne__{name}")
                    ?? context.Assets.Load<AudioClip>(name);
                Clips[name] = clip;   // cached even when null, so a missing clip warns once
                if (clip == null)
                {
                    context.Log.Warn($"cheyanne trainer: audio clip '{name}' not in the mod bundles");
                    return;
                }
            }
            if (clip == null)
                return;
            if (_source == null)
            {
                // Its own object, kept across scene loads, so a sound is not
                // cut off by whatever else is being torn down mid-mission.
                var host = new GameObject("wm-cheyanne-aim-audio");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _source = host.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.spatialBlend = 0f;   // 2D: this is UI, not a thing in the world
            }
            _source.PlayOneShot(clip);
        }
        catch (Exception ex)
        {
            Log.Debug($"cheyanne trainer sound: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void Forget() => Clips.Clear();
}
