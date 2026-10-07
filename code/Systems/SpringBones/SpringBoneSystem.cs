using System.Collections;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Spring physics for Doll hair and cloth chains.
//
// A body prefab has no way to carry the solver: a component the game's
// assemblies do not define cannot be serialised into the bundle. So the
// bodies are found after they spawn. TransmogSystem's DetermineArmorPrefab
// postfix is the single choke point every Doll body passes through (the
// tactical spawn and the armoury preview both call it), and it reports each
// body prefab here. A prefab with a spring profile has its avatar remembered,
// and a short scan over the next second picks up every Animator built on that
// avatar. The scan is needed because the caller instantiates the prefab after
// the pick returns, and the armoury rebuilds its stage on later frames.
//
// The solver runs in LateUpdate, after the Animator has posed the body this
// frame, from a small injected driver component. Run any earlier (a mod
// coroutine resumes before the Animator) and every step reacts to the
// PREVIOUS frame's head motion with the CURRENT frame's time step: wherever
// frame times vary, the hair is fed mismatched motion and twitches, worst on
// aim snaps. The coroutine stays as the fallback if the driver cannot be
// injected.
public sealed class SpringBoneSystem : JiangyuSystem
{
    // A direct call from TransmogSystem rather than a sibling postfix on the
    // same method: sibling postfix order would decide whether this saw the
    // swapped prefab or the vanilla one.
    internal static SpringBoneSystem Instance { get; private set; }

    // Scan frames after a sprung body is picked, counted from the pick.
    private static readonly int[] ScanFrames = { 1, 2, 4, 8, 16, 30, 60 };

    private readonly Dictionary<IntPtr, SpringBodyProfile> _prefabProfiles = new();
    private readonly Dictionary<IntPtr, (SpringBodyProfile Profile, GameObject Prefab)> _avatarProfiles = new();
    private readonly Dictionary<IntPtr, SpringBoneRig> _rigs = new();
    private readonly HashSet<IntPtr> _rejected = new();
    private object _loopHandle;
    private object _scanHandle;
    private static bool _driverRegistered;
    private GameObject _driver;
    private bool _driverFailed;
    private int _scanStartFrame;

    internal IReadOnlyCollection<SpringBoneRig> Rigs => _rigs.Values;

    // Bodies drawn at this LOD or closer simulate. The baked LODs switch at
    // 50, 25, 10 and 2 per cent of screen height, so LOD1 is a body filling a
    // quarter of the screen or more.
    internal static int MaxSimulatedLod { get; set; } = 1;

    // Set by the Springs.Record dev verb: rigs built afterwards start
    // recording straight away, so a mission loaded later is covered too.
    internal int RecordFrames { get; set; }

    public override void OnInit()
    {
        Instance = this;
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        // Mod coroutines survive scene loads, so the loops are stopped
        // explicitly rather than orphaned beside fresh ones.
        StopRoutine(ref _loopHandle);
        StopRoutine(ref _scanHandle);
        _rigs.Clear();
        _rejected.Clear();
    }

    // Called with every body prefab DetermineArmorPrefab hands out.
    internal void NoteBodyPrefab(GameObject prefab)
    {
        try
        {
            if (prefab == null)
                return;
            var profile = ProfileForPrefab(prefab);
            if (profile == null)
                return;
            // Unity-null checks, never ?. : interop references to a destroyed
            // or absent object are fake-null and ?. sails past them.
            var animator = prefab.GetComponent<Animator>();
            if (animator == null)
                return;
            var avatar = animator.avatar;
            if (avatar == null)
                return;
            _avatarProfiles[avatar.Pointer] = (profile, prefab);
            _scanStartFrame = Time.frameCount;
            if (_scanHandle == null)
                _scanHandle = Context.Coroutines.Start(ScanLoop());
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"springs: body note failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private SpringBodyProfile ProfileForPrefab(GameObject prefab)
    {
        if (_prefabProfiles.TryGetValue(prefab.Pointer, out var cached))
            return cached;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in prefab.GetComponentsInChildren<Transform>(includeInactive: true))
            names.Add(t.name);
        var found = SpringBodyProfiles.Match(names);
        _prefabProfiles[prefab.Pointer] = found;
        return found;
    }

    // A fresh pick restarts the schedule, so a run of spawns keeps scanning
    // until a second after the last of them.
    private IEnumerator ScanLoop()
    {
        var next = 0;
        var lastStart = _scanStartFrame;
        while (next < ScanFrames.Length)
        {
            yield return null;
            if (_scanStartFrame != lastStart)
            {
                lastStart = _scanStartFrame;
                next = 0;
            }
            if (Time.frameCount - lastStart < ScanFrames[next])
                continue;
            next++;
            try { Scan(); }
            catch (Exception ex) { Context.Log.Warn($"springs: scan failed: {ex.GetType().Name}: {ex.Message}"); }
        }
        _scanHandle = null;
    }

    internal int Scan()
    {
        var added = 0;
        foreach (var animator in UnityEngine.Object.FindObjectsOfType<Animator>())
        {
            if (_rigs.ContainsKey(animator.Pointer) || _rejected.Contains(animator.Pointer))
                continue;
            var avatar = animator.avatar;
            if (avatar == null || !_avatarProfiles.TryGetValue(avatar.Pointer, out var body))
                continue;
            var profile = body.Profile;
            SpringBoneRig rig;
            string error;
            // One body that cannot be built is skipped without ending the
            // scan for the bodies after it.
            try
            {
                rig = SpringBoneRig.Build(animator, body.Prefab, profile, out error);
            }
            catch (Exception ex)
            {
                rig = null;
                error = $"{ex.GetType().Name}: {ex.Message}";
            }
            if (rig == null)
            {
                _rejected.Add(animator.Pointer);
                Context.Log.Warn($"springs: '{profile.LodMesh}' instance skipped: {error}");
                continue;
            }
            if (error != null)
                Context.Log.Warn($"springs: '{profile.LodMesh}' {error}");
            _rigs[animator.Pointer] = rig;
            if (RecordFrames > 0)
                rig.StartRecording(RecordFrames);
            added++;
            Context.Log.Debug($"springs: '{profile.LodMesh}' rigged, {rig.ChainCount} chains, {rig.JointCount} joints");
        }
        if (added > 0 && !EnsureDriver() && _loopHandle == null)
            _loopHandle = Context.Coroutines.Start(SimulateLoop());
        return added;
    }

    // The driver lives on its own persistent GameObject, created the first
    // time a rig exists. Returns false when it cannot be injected.
    private bool EnsureDriver()
    {
        if (_driverFailed)
            return false;
        try
        {
            if (_driver != null)
                return true;
            if (!_driverRegistered)
            {
                ClassInjector.RegisterTypeInIl2Cpp<SpringBoneDriver>();
                _driverRegistered = true;
            }
            _driver = new GameObject("WOMENACE.SpringBones");
            UnityEngine.Object.DontDestroyOnLoad(_driver);
            _driver.AddComponent(Il2CppType.Of<SpringBoneDriver>());
            Context.Log.Debug("springs: LateUpdate driver injected");
            return true;
        }
        catch (Exception ex)
        {
            _driverFailed = true;
            Context.Log.Warn($"springs: driver injection failed, falling back to a coroutine: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private readonly List<IntPtr> _dead = new();

    // Solver cost per frame, averaged, for the Springs.Status dev verb.
    internal double TickMilliseconds { get; private set; }

    private readonly System.Diagnostics.Stopwatch _tickWatch = new();

    // Frames between checks for destroyed bodies. A destroyed body throws on
    // its next touch and is dropped there anyway, so the check only has to
    // catch the rest eventually.
    private const int DeadCheckFrames = 30;
    private int _deadCheck;

    internal void TickAll()
    {
        if (_rigs.Count == 0)
            return;
        _tickWatch.Restart();
        var dt = Time.deltaTime;
        var checkDead = ++_deadCheck >= DeadCheckFrames;
        if (checkDead)
            _deadCheck = 0;
        var resets = 1;
        foreach (var pair in _rigs)
        {
            var rig = pair.Value;
            if (checkDead && rig.IsDead)
            {
                _dead.Add(pair.Key);
                continue;
            }
            try
            {
                rig.Tick(dt, ref resets);
            }
            catch (Exception ex)
            {
                // A destroyed body throws on its next touch, which is routine
                // whenever a stage is rebuilt.
                _dead.Add(pair.Key);
                if (!rig.IsDead)
                    Context.Log.Warn($"springs: '{rig.Profile.LodMesh}' dropped: {ex.GetType().Name}: {ex.Message}");
            }
        }
        foreach (var key in _dead)
            _rigs.Remove(key);
        _dead.Clear();
        TickMilliseconds = TickMilliseconds * 0.95 + _tickWatch.Elapsed.TotalMilliseconds * 0.05;
    }

    private IEnumerator SimulateLoop()
    {
        while (_rigs.Count > 0)
        {
            TickAll();
            yield return null;
        }
        _loopHandle = null;
    }

    private void StopRoutine(ref object handle)
    {
        if (handle != null)
            try { Context.Coroutines.Stop(handle); } catch { }
        handle = null;
    }
}

// Ticks the spring rigs in LateUpdate. Injected into the IL2CPP type system at
// runtime, so it needs the pointer constructor Il2CppInterop builds instances
// through.
public sealed class SpringBoneDriver : MonoBehaviour
{
    public SpringBoneDriver(IntPtr pointer) : base(pointer) { }

    public void LateUpdate()
    {
        try { SpringBoneSystem.Instance?.TickAll(); }
        catch { }
    }
}
