using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Turns a BakeVehicle prefab into a hovering summon that speaks the vanilla
    // recon skybot's animator contract (aco.recon_skybot), for a summoned
    // companion that stands in for the Falcon skybot. Run after BakeVehicle.
    //
    // The skybot's driver only sets parameters, it never asks for a state: the
    // controller ships every parameter aco.recon_skybot declares so no Set call
    // misses, and plays the summon's own arrival, hover and flight clips off
    // Speed. The arrival is the entry state rather than a transition on
    // IsEjected, because nothing clears IsEjected after the spawn and a
    // transition on it would replay the arrival forever.
    //
    // Like the skybot, the model hangs under a scale-1 root at a fixed hover
    // height, with a box collider on the model node for hits and selection.
    //
    // A summon that attacks gets an -attackClip: the skill's Shoot_Single
    // trigger plays it once from hover or flight, then the summon returns to
    // its hover. -muzzleBone names the rig bone the shot leaves from, and
    // -muzzleOffset places the "muzzle" transform in that bone's local space.
    // The muzzle faces the summon's forward in the hover pose, so a muzzle
    // flash and a projectile leave towards the target the summon faces.
    //
    //   Unity -batchmode -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.BuildSummonPrefab.Build \
    //     -prefab Assets/Prefabs/springfield/taryz/main.prefab \
    //     -fbxPath Assets/Authored/springfield/taryz/raw.fbx \
    //     -arriveClip c_SpringfieldSSR01_Eagle_slg_Born -idleClip c_SpringfieldSSR01_Eagle_slg_Idle \
    //     -moveClip c_SpringfieldSSR01_Eagle_slg_Run -hoverHeight 2.2 \
    //     -attackClip c_SpringfieldSSR01_Eagle_slg_StandFire \
    //     -muzzleBone HeadEnd_M -muzzleOffset -0.0673,0.0464,0
    public static class BuildSummonPrefab
    {
        // aco.recon_skybot's parameters, names and types as the game's driver writes them.
        private static readonly (string name, AnimatorControllerParameterType type, bool defaultBool)[] SkybotParams =
        {
            ("Expand", AnimatorControllerParameterType.Bool, false),
            ("Flying", AnimatorControllerParameterType.Bool, true),
            ("IsEjected", AnimatorControllerParameterType.Bool, false),
            ("Looking", AnimatorControllerParameterType.Bool, false),
            ("Movement_Initialized", AnimatorControllerParameterType.Bool, false),
            ("MoveX", AnimatorControllerParameterType.Float, false),
            ("MoveY", AnimatorControllerParameterType.Float, false),
            ("Speed", AnimatorControllerParameterType.Float, false),
            ("Steering_Direction", AnimatorControllerParameterType.Float, false),
            ("Acceleration_Sign", AnimatorControllerParameterType.Float, false),
        };

        // The skybot's own movement threshold (BT_Leaning_Movement leaves below it).
        private const float MoveSpeedThreshold = 0.2f;

        // Skill.PlayAnimation fires this trigger for AnimationType ShootSingle.
        private const string AttackTrigger = "Shoot_Single";

        // The engine finds a shot's origin by this exact transform name.
        private const string MuzzleName = "muzzle";
        private const string DustName = "dust01";

        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string key)
            {
                var i = Array.IndexOf(args, key);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            var prefabPath = Arg("-prefab") ?? throw new InvalidOperationException("-prefab is required.");
            var fbxPath = Arg("-fbxPath") ?? throw new InvalidOperationException("-fbxPath is required.");
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            var hover = float.Parse(Arg("-hoverHeight") ?? "2.2", invariant);
            var muzzleBone = Arg("-muzzleBone");
            var muzzleOffset = Vector3.zero;
            if (Arg("-muzzleOffset") is { } offset)
            {
                var parts = offset.Split(',').Select(p => float.Parse(p, invariant)).ToArray();
                if (parts.Length != 3) throw new InvalidOperationException("-muzzleOffset takes x,y,z.");
                muzzleOffset = new Vector3(parts[0], parts[1], parts[2]);
            }
            Build(prefabPath, fbxPath, Arg("-arriveClip"), Arg("-idleClip"), Arg("-moveClip"), Arg("-attackClip"),
                hover, muzzleBone, muzzleOffset);
        }

        private static void Build(string prefabPath, string fbxPath, string arriveName, string idleName, string moveName,
            string attackName, float hover, string muzzleBone, Vector3 muzzleOffset)
        {
            LoopClips(fbxPath, idleName, moveName);
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview", StringComparison.Ordinal)).ToList();
            AnimationClip Find(string n) => n == null ? null
                : clips.FirstOrDefault(c => c.name == n || c.name.EndsWith("|" + n, StringComparison.Ordinal))
                  ?? throw new InvalidOperationException("clip '" + n + "' not in " + fbxPath + ": " + string.Join(", ", clips.Select(c => c.name)));
            var arrive = Find(arriveName);
            var idle = Find(idleName) ?? throw new InvalidOperationException("-idleClip is required.");
            var move = Find(moveName) ?? idle;
            var attack = Find(attackName);

            var dir = System.IO.Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            var controller = BuildController(dir + "/summon.controller", arrive, idle, move, attack);

            // Edited as an unpacked scene instance: an AnimationClip sampled onto
            // LoadPrefabContents' preview copy does not survive the save.
            var loaded = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            PrefabUtility.UnpackPrefabInstance(loaded, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            // BakeVehicle's root is the model itself, with the Animator on it. A
            // prefab this pass already wrapped keeps its model under "model", so
            // a rerun rebuilds the same shape.
            var model = loaded.GetComponent<Animator>() == null && loaded.transform.Find("model") is { } inner ? inner.gameObject : loaded;
            var wrapper = new GameObject(loaded.name);
            try
            {
                // Rehang the model under a fresh scale-1 root at the hover height.
                model.name = "model";
                model.transform.SetParent(wrapper.transform, false);
                if (loaded != model) UnityEngine.Object.DestroyImmediate(loaded);
                model.transform.localPosition = new Vector3(0f, hover, 0f);

                // Unity's missing-component fake null defeats ??, so these check explicitly.
                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                // The FBX's node defaults hold whatever frame the exporter
                // evaluated last (a GFL2 arrival starts high in the sky), so the
                // saved pose is the hover idle's first frame.
                idle.SampleAnimation(model, 0f);

                // A rerun replaces the socket rather than stacking a second one,
                // which the engine's first-match lookup would never reach.
                foreach (var stale in model.GetComponentsInChildren<Transform>(true).Where(t => t.name == MuzzleName).ToArray())
                    UnityEngine.Object.DestroyImmediate(stale.gameObject);
                if (muzzleBone != null)
                {
                    var bone = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == muzzleBone)
                        ?? throw new InvalidOperationException("-muzzleBone '" + muzzleBone + "' not in " + prefabPath);
                    var muzzle = new GameObject(MuzzleName).transform;
                    muzzle.SetParent(bone, false);
                    muzzle.localPosition = muzzleOffset;
                    muzzle.rotation = model.transform.rotation;
                    muzzle.localScale = new Vector3(1f / bone.lossyScale.x, 1f / bone.lossyScale.y, 1f / bone.lossyScale.z);
                }

                // The skybot's element animator spawns its movement dust at a "dust01"
                // child (DustEffectsAnimatorComp) and logs an error when there is none,
                // so the summon carries one on the ground under its hover.
                foreach (var stale in wrapper.GetComponentsInChildren<Transform>(true).Where(t => t.name == DustName).ToArray())
                    UnityEngine.Object.DestroyImmediate(stale.gameObject);
                new GameObject(DustName).transform.SetParent(wrapper.transform, false);

                // Skinned bounds only refresh on render, so the box is measured
                // off the posed vertices.
                {
                    Bounds? bounds = null;
                    foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var baked = new Mesh();
                        smr.BakeMesh(baked, true);
                        foreach (var v in baked.vertices)
                        {
                            var local = model.transform.InverseTransformPoint(smr.transform.TransformPoint(v));
                            if (bounds is { } b) { b.Encapsulate(local); bounds = b; }
                            else bounds = new Bounds(local, Vector3.zero);
                        }
                        UnityEngine.Object.DestroyImmediate(baked);
                    }
                    if (bounds is { } box)
                    {
                        var collider = model.GetComponent<BoxCollider>();
                        if (collider == null) collider = model.AddComponent<BoxCollider>();
                        collider.center = box.center;
                        collider.size = box.size;
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(wrapper, prefabPath);
                var socket = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == MuzzleName);
                Debug.Log($"Womenace BuildSummonPrefab: {prefabPath} hovering at {hover} m, controller {AssetDatabase.GetAssetPath(controller)}, clips arrive={arrive?.name} idle={idle.name} move={move.name} attack={attack?.name}"
                    + (socket != null ? $", muzzle under {socket.parent.name} at {wrapper.transform.InverseTransformPoint(socket.position)} (root space)" : ""));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wrapper);
            }
            // BakeVehicle's vehicle-contract controller is replaced, not used.
            AssetDatabase.DeleteAsset(dir + "/vehicle.controller");
            AssetDatabase.SaveAssets();
        }

        // BakeVehicle loops only its move clip. The hover idle loops too.
        private static void LoopClips(string fbxPath, params string[] names)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            var all = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
            var changed = false;
            foreach (var c in all)
                if (names.Any(n => n != null && (c.name == n || c.name.EndsWith("|" + n, StringComparison.Ordinal))) && !c.loopTime)
                {
                    c.loopTime = true;
                    changed = true;
                }
            if (!changed) return;
            imp.clipAnimations = all;
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        private static AnimatorController BuildController(string path, AnimationClip arrive, AnimationClip idle, AnimationClip move,
            AnimationClip attack)
        {
            AssetDatabase.DeleteAsset(path);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            foreach (var (name, type, defaultBool) in SkybotParams)
            {
                ac.AddParameter(name, type);
                if (defaultBool)
                {
                    var ps = ac.parameters;
                    ps.First(p => p.name == name).defaultBool = true;
                    ac.parameters = ps;
                }
            }

            var sm = ac.layers[0].stateMachine;
            var hoverState = sm.AddState("Idle");
            hoverState.motion = idle;
            var moveState = sm.AddState("Move");
            moveState.motion = move;
            sm.defaultState = hoverState;
            if (arrive != null)
            {
                var arriveState = sm.AddState("Arrive");
                arriveState.motion = arrive;
                sm.defaultState = arriveState;
                var landed = arriveState.AddTransition(hoverState);
                landed.hasExitTime = true; landed.exitTime = 1f; landed.duration = 0.2f;
            }

            var toMove = hoverState.AddTransition(moveState);
            toMove.hasExitTime = false; toMove.duration = 0.2f;
            toMove.AddCondition(AnimatorConditionMode.Greater, MoveSpeedThreshold, "Speed");
            var toHover = moveState.AddTransition(hoverState);
            toHover.hasExitTime = false; toHover.duration = 0.25f;
            toHover.AddCondition(AnimatorConditionMode.Less, MoveSpeedThreshold, "Speed");

            if (attack != null)
            {
                ac.AddParameter(AttackTrigger, AnimatorControllerParameterType.Trigger);
                var attackState = sm.AddState("Attack");
                attackState.motion = attack;
                foreach (var from in new[] { hoverState, moveState })
                {
                    var strike = from.AddTransition(attackState);
                    strike.hasExitTime = false; strike.duration = 0.1f;
                    strike.AddCondition(AnimatorConditionMode.If, 0f, AttackTrigger);
                }
                var done = attackState.AddTransition(hoverState);
                done.hasExitTime = true; done.exitTime = 1f; done.duration = 0.2f;
            }
            return ac;
        }
    }
}
