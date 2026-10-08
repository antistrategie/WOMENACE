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
    //   Unity -batchmode -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.BuildSummonPrefab.Build \
    //     -prefab Assets/Prefabs/springfield/taryz/main.prefab \
    //     -fbxPath Assets/Authored/springfield/taryz/raw.fbx \
    //     -arriveClip c_SpringfieldSSR01_Eagle_slg_Born -idleClip c_SpringfieldSSR01_Eagle_slg_Idle \
    //     -moveClip c_SpringfieldSSR01_Eagle_slg_Run -hoverHeight 2.2
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
            var hover = float.Parse(Arg("-hoverHeight") ?? "2.2", System.Globalization.CultureInfo.InvariantCulture);
            Build(prefabPath, fbxPath, Arg("-arriveClip"), Arg("-idleClip"), Arg("-moveClip"), hover);
        }

        private static void Build(string prefabPath, string fbxPath, string arriveName, string idleName, string moveName, float hover)
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

            var dir = System.IO.Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            var controller = BuildController(dir + "/summon.controller", arrive, idle, move);

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
                Debug.Log($"Womenace BuildSummonPrefab: {prefabPath} hovering at {hover} m, controller {AssetDatabase.GetAssetPath(controller)}, clips arrive={arrive?.name} idle={idle.name} move={move.name}");
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

        private static AnimatorController BuildController(string path, AnimationClip arrive, AnimationClip idle, AnimationClip move)
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
            return ac;
        }
    }
}
