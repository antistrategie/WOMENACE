using System.Collections;
using Il2CppMenace.Tactical;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Turns OTs-14's weapon-bay arms toward wherever she is looking.
//
// The arm assembly rides the Back_Special socket rigidly, and the soldier's
// aim stance twists her torso side-on to the target, which points the bay at
// nothing. Each ARM is its own servo: all four track the head's yaw, but
// every scapula runs a different smoothing time, so a turn sweeps the arms
// around one after another instead of slewing the rack as one welded block.
// The breathing clip ships without the scapulas' rotation channels, so the
// Animator cannot stomp the per-frame writes.
//
// Axis conventions are never assumed: at registration, while the freshly
// created element still faces its own forward, the head's and the arms'
// yaw offsets from the element are captured, and the servo only ever
// reproduces those offsets. Tracking is smoothed so the bay reads as heavy
// servo-driven hardware rather than snapping.
public sealed class Ots14AimFollowSystem : JiangyuSystem
{
    private const string ArmMeshNode = "c_OTs14SSR01_Arm_slg_1_lod0";

    // Registration is a direct call from BayMountSystem, never a sibling
    // postfix on Element.CreateAttachments: the mount system can SPAWN the
    // arms inside its own postfix (when her primary is not her rifle), and
    // sibling patch order would decide whether this system ever saw them.
    internal static Ots14AimFollowSystem Instance { get; private set; }

    // One servo per arm, each with its own lag. Left and right are deliberately
    // not mirrored so the pairs never move in lockstep.
    private static readonly string[] ServoBones = { "Scapula1_L", "Scapula1_R", "Scapula_L", "Scapula_R" };
    private static readonly float[] ServoSmoothTimes = { 0.12f, 0.18f, 0.24f, 0.32f };

    // Spawn-anchored: the arms' spawn rotation is the reference, and only the
    // head's yaw CHANGE since spawn is ever applied on top of it. Nothing is
    // measured back from the arms at runtime, so the servo cannot feed on its
    // own output and drift (the first build did, walking left a little
    // further left on every aim).
    private sealed class Carrier
    {
        public Element Element;
        public Transform Head;
        public Transform[] Scapulas;
        public Quaternion[] SpawnRotations;
        public float[] Applied;
        public float[] Velocities;
        public float SpawnHeadYaw;
    }

    private readonly List<Carrier> _carriers = new();
    private object _loopHandle;

    public override void OnInit()
    {
        Instance = this;
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        _carriers.Clear();
        // Stopped explicitly: mod coroutines survive scene loads, so nulling
        // the handle alone left an orphan loop ticking beside the fresh one.
        if (_loopHandle != null)
            try { Context.Coroutines.Stop(_loopHandle); } catch { }
        _loopHandle = null;
    }

    // Called by BayMountSystem once per element mount, with the arms host it
    // resolved (vanilla-attached or bay-spawned). Idempotent: re-mounting a
    // live element replaces its carrier.
    internal void Register(Element element, GameObject back)
    {
        try
        {
            if (element == null || back == null)
                return;
            var head = SceneQuery.FindNamed(element.gameObject, "Head");
            if (head == null || SceneQuery.FindNamed(back, ArmMeshNode) == null)
                return;
            var scapulas = new Transform[ServoBones.Length];
            var spawnRots = new Quaternion[ServoBones.Length];
            for (var j = 0; j < ServoBones.Length; j++)
            {
                scapulas[j] = SceneQuery.FindNamed(back, ServoBones[j]);
                if (scapulas[j] != null)
                    spawnRots[j] = scapulas[j].rotation;
            }

            // Drop the dead carriers AND any carrier already tracking this
            // element: re-equipping runs CreateAttachments again on a live
            // element, and two entries driving one rig would fight over it.
            _carriers.RemoveAll(c => c.Element == null || c.Element.WasCollected || c.Head == null
                || c.Element.Pointer == element.Pointer);
            _carriers.Add(new Carrier
            {
                Element = element,
                Head = head,
                Scapulas = scapulas,
                SpawnRotations = spawnRots,
                Applied = new float[ServoBones.Length],
                Velocities = new float[ServoBones.Length],
                SpawnHeadYaw = YawOf(head.rotation),
            });
            if (_loopHandle == null)
                _loopHandle = Context.Coroutines.Start(ServoLoop());
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"ots14 aim follow: register failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private IEnumerator ServoLoop()
    {
        while (true)
        {
            for (var i = _carriers.Count - 1; i >= 0; i--)
            {
                var c = _carriers[i];
                try
                {
                    if (c.Element == null || c.Element.WasCollected || c.Head == null)
                    {
                        _carriers.RemoveAt(i);
                        continue;
                    }
                    // How far the head has turned since spawn is how far every
                    // arm should turn from its spawn rotation, each at its own
                    // pace.
                    var targetDelta = Mathf.DeltaAngle(c.SpawnHeadYaw, YawOf(c.Head.rotation));
                    for (var j = 0; j < c.Scapulas.Length; j++)
                    {
                        var scap = c.Scapulas[j];
                        if (scap == null)
                            continue;
                        // A retracted arm (the reveal system's scale-hide) has
                        // nothing to steer: its servo state resets so the arm
                        // sweeps from its spawn yaw onto the head's the moment
                        // it grows out. The armoury never retracts, so its
                        // arms track as always.
                        if (scap.localScale.x < 0.5f)
                        {
                            c.Applied[j] = 0f;
                            c.Velocities[j] = 0f;
                            continue;
                        }
                        c.Applied[j] = Mathf.SmoothDampAngle(c.Applied[j], targetDelta, ref c.Velocities[j], ServoSmoothTimes[j]);
                        scap.rotation = Quaternion.AngleAxis(c.Applied[j], Vector3.up) * c.SpawnRotations[j];
                    }
                }
                catch
                {
                    _carriers.RemoveAt(i);
                }
            }
            if (_carriers.Count == 0)
            {
                _loopHandle = null;
                yield break;
            }
            yield return null;
        }
    }

    private static float YawOf(Quaternion rotation)
    {
        var f = rotation * Vector3.forward;
        var flat = new Vector3(f.x, 0f, f.z);
        // A straight-down forward has no yaw: fall back to the up vector's heading.
        if (flat.sqrMagnitude < 1e-6f)
        {
            var u = rotation * Vector3.up;
            flat = new Vector3(u.x, 0f, u.z);
        }
        return Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
    }

}
