using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Builds Sextans' AnimatorController: a copy of the vanilla humanoid
    // soldier controller (aco.human_soldier) with her GFL2 muscle clips
    // swapped into the melee-relevant states and an added ultimate state
    // chain. The swap is CLIP-NAME based: every state or blend-tree child
    // whose motion is a clip named in the swap table gets the replacement,
    // wherever it appears in the 195-state machine.
    //
    // Dump mode (inventory of state -> motion wiring, no changes):
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.BuildSextansController.Dump
    public static class BuildSextansController
    {
        private const string VanillaControllerPath =
            "Assets/Imported/rmc_default_female_soldier_2/AnimatorController/aco.human_soldier.controller";

        private const string ClipsDir = "Assets/Authored/sextans/clips";
        private const string OutputControllerPath = "Assets/Prefabs/sextans/_bake/sextans.controller";
        private const string PrefabPath = "Assets/Prefabs/sextans/default/main.prefab";

        // Vanilla clip name -> Sextans clip name. Name-based so the swap
        // lands in every state and blend-tree slot referencing the clip.
        // Everything absent here stays vanilla (crouch, prone, stun,
        // grenade, mounted, hit reactions): those retarget through her
        // humanoid avatar.
        private static readonly Dictionary<string, string> ClipSwap = new()
        {
            // idle + the standing pose used inside locomotion blends
            { "ac_human_combat_idle_base", "Idle" },
            { "ac_human_combat_idle_base_var_01", "Idle" },
            { "ac_human_standing_aim", "Idle" },
            { "ac_human_standing_aim_idle", "Idle" },
            { "ac_human_standing_aim_idle_10f", "Idle" },
            { "ac_human_standing_aim_idle_aim_r", "Idle" },
            { "ac_human_standing_aim_idle_aim_l", "Idle" },
            // locomotion
            { "Rifle_WalkFwdLoop", "Walk" },
            { "Rifle_SprintLoop", "Run" },
            { "Rifle_WalkFwdStart", "RunStart" },
            { "Rifle_SprintStart", "RunStart" },
            { "Rifle_WalkFwdStart90_L", "RunStart" },
            { "Rifle_WalkFwdStart90_R", "RunStart" },
            { "Rifle_WalkFwdStart180_L", "StartBackL" },
            { "ac_human_rifle_walk_start_180_r", "StartBackR" },
            // turns: the root-motion states get her clips as-is, the _no_rm
            // states (in-place turning, rotation baked) get generated
            // in-place variants: one clip cannot carry both root-handling
            // conventions
            { "ac_human_standing_aim_turn_90_l", "TurnL_90" },
            { "ac_human_standing_aim_turn_90_r", "TurnR_90" },
            { "ac_human_standing_aim_turn_180_l", "TurnL_180" },
            { "ac_human_standing_aim_turn_180_r", "TurnR_180" },
            { "ac_human_standing_aim_turn_90_l_no_rm", "TurnL_90_inplace" },
            { "ac_human_standing_aim_turn_90_r_no_rm", "TurnR_90_inplace" },
            { "ac_human_standing_aim_turn_180_l_no_rm", "TurnL_180_inplace" },
            { "ac_human_standing_aim_turn_180_r_no_rm", "TurnR_180_inplace" },
            // crouch: her low-cover idle becomes the crouch stance, the
            // low-cover entry becomes the crouch transition. No GFL2 clip
            // exists for standing back up or crouch-walking, those stay
            // vanilla (they retarget through her avatar).
            { "ac_human_crouching_aim_idle", "LowCoverIdle_L" },
            { "ac_human_crouching_aim", "LowCoverIdle_L" },
            { "ac_human_crouching_aim_90_r", "LowCoverIdle_L" },
            { "ac_human_crouching_aim_90_l", "LowCoverIdle_L" },
            { "ac_human_idle2crouch", "IdleToLC_L" },
            // suppressed + vault
            { "ac_human_suppressed_idle", "SupressedLoop" },
            { "ac_human_suppressed_idle_var01", "SupressedLoop" },
            { "AC_Human_Suppressed_Var02", "SupressedLoop" },
            { "AC_Human_Suppressed_Var03", "SupressedLoop" },
            { "AC_Human_Suppressed_Var04", "SupressedLoop" },
            { "AC_Human_Vault1mShort", "MoveOverCover" },
        };

        // Each replacement clip inherits root-handling flags, loop flag, and
        // animation events (footsteps, the vault-complete signal) from ONE
        // canonical vanilla source. First-encountered-in-the-walk stamping
        // would be a race: several vanilla clips with mutually exclusive
        // settings map onto one replacement (looping combat idle vs
        // non-looping aim pose both become Idle).
        private static readonly Dictionary<string, string> TruthSource = new()
        {
            { "Idle", "ac_human_combat_idle_base" },
            { "Walk", "Rifle_WalkFwdLoop" },
            { "Run", "Rifle_SprintLoop" },
            { "RunStart", "Rifle_SprintStart" },
            { "StartBackL", "Rifle_WalkFwdStart180_L" },
            { "StartBackR", "ac_human_rifle_walk_start_180_r" },
            { "TurnL_90", "ac_human_standing_aim_turn_90_l" },
            { "TurnR_90", "ac_human_standing_aim_turn_90_r" },
            { "TurnL_180", "ac_human_standing_aim_turn_180_l" },
            { "TurnR_180", "ac_human_standing_aim_turn_180_r" },
            { "TurnL_90_inplace", "ac_human_standing_aim_turn_90_l_no_rm" },
            { "TurnR_90_inplace", "ac_human_standing_aim_turn_90_r_no_rm" },
            { "TurnL_180_inplace", "ac_human_standing_aim_turn_180_l_no_rm" },
            { "TurnR_180_inplace", "ac_human_standing_aim_turn_180_r_no_rm" },
            { "LowCoverIdle_L", "ac_human_crouching_aim_idle" },
            { "IdleToLC_L", "ac_human_idle2crouch" },
            { "SupressedLoop", "ac_human_suppressed_idle" },
            { "MoveOverCover", "AC_Human_Vault1mShort" },
        };

        private const string ImportedClipsDir = "Assets/Imported/rmc_default_female_soldier_2/AnimationClip";

        public static void Build()
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var swapTarget in ClipSwap.Values.Distinct()
                         .Where(n => !n.EndsWith("_inplace"))
                         .Concat(new[] { "UltraSkill_God_Pre", "UltraSkill_God_Main", "NormalSkill1", "Fire", "KnockBack", "RunStop" }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipsDir}/{swapTarget}.anim");
                if (clip == null)
                {
                    Debug.LogError($"BuildSextansController: missing clip {ClipsDir}/{swapTarget}.anim");
                    EditorApplication.Exit(1);
                    return;
                }
                clips[swapTarget] = clip;
            }

            // In-place turn variants: the _no_rm states need rotation baked
            // into the pose while the root-motion turn states need it real,
            // and one clip cannot carry both conventions. Her clips are
            // healthy Unity assets, so Instantiate round-trips safely.
            foreach (var turn in new[] { "TurnL_90", "TurnR_90", "TurnL_180", "TurnR_180" })
            {
                var variantName = turn + "_inplace";
                var variant = Object.Instantiate(clips[turn]);
                variant.name = variantName;
                AssetDatabase.CreateAsset(variant, $"{ClipsDir}/{variantName}.anim");
                clips[variantName] = variant;
            }

            // stamp truth settings + events from each clip's canonical source
            foreach (var (replacement, vanillaName) in TruthSource)
            {
                if (!clips.TryGetValue(replacement, out var clip))
                    continue;
                var truth = TruthFor(vanillaName);
                if (truth != null)
                    ApplyTruthSettings(clip, truth, keepTiming: true);
                var vanillaClip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ImportedClipsDir}/{vanillaName}.anim");
                if (vanillaClip != null)
                    CopyEventsScaled(vanillaClip, clip);
                EditorUtility.SetDirty(clip);
            }

            var outDir = System.IO.Path.GetDirectoryName(OutputControllerPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(outDir))
                System.IO.Directory.CreateDirectory(outDir);
            AssetDatabase.Refresh();
            AssetDatabase.DeleteAsset(OutputControllerPath);
            if (!AssetDatabase.CopyAsset(VanillaControllerPath, OutputControllerPath))
            {
                Debug.LogError("BuildSextansController: controller copy failed.");
                EditorApplication.Exit(1);
                return;
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(OutputControllerPath);

            // The AssetRipper rip ZEROES m_AnimationClipSettings on every
            // vanilla clip (loop flags, root-Y handling, stop time). Playing
            // those clips through the controller sinks crouches through the
            // floor and breaks exit-time transitions (vault slow-motion).
            // Every Imported clip the controller references is replaced by a
            // repaired local copy with reconstructed settings.
            _repairedClips.Clear();
            int repaired = 0;
            foreach (var layer in controller.layers)
                repaired += RepairInStateMachine(layer.stateMachine);
            Debug.Log($"BuildSextansController: localised {repaired} motion slot(s) onto {_repairedClips.Count} repaired clip(s).");

            int swapped = 0;
            foreach (var layer in controller.layers)
                swapped += SwapInStateMachine(layer.stateMachine, clips);
            Debug.Log($"BuildSextansController: swapped {swapped} motion slot(s).");

            // Run-stop blending: her RunStop clip plays between the run cycle
            // and idle instead of a straight cross-fade. The existing
            // BT_Run Fast -> SM_Idle transition (Speed falling) is rerouted
            // through a new stop state that keeps the original conditions,
            // exits into SM_Idle near clip end, and aborts back into the run
            // if the unit is ordered to move again mid-stop. The stop-to-aim
            // transition stays direct: combat stops want snap.
            var baseLayerSm = controller.layers[0].stateMachine;
            var runState = baseLayerSm.states.FirstOrDefault(s => s.state.name == "BT_Run Fast").state;
            var idleSm = baseLayerSm.stateMachines.FirstOrDefault(s => s.stateMachine.name == "SM_Idle").stateMachine;
            if (runState != null && idleSm != null)
            {
                var original = runState.transitions.FirstOrDefault(t => t.destinationStateMachine == idleSm);
                if (original != null)
                {
                    var stop = baseLayerSm.AddState("SextansRunStop");
                    stop.motion = clips["RunStop"];

                    var intoStop = runState.AddTransition(stop);
                    foreach (var c in original.conditions)
                        intoStop.AddCondition(c.mode, c.threshold, c.parameter);
                    intoStop.hasExitTime = false;
                    intoStop.duration = 0.1f;

                    var stopToIdle = stop.AddTransition(idleSm);
                    stopToIdle.hasExitTime = true;
                    stopToIdle.exitTime = 0.85f;
                    stopToIdle.duration = 0.2f;

                    var backToRun = stop.AddTransition(runState);
                    backToRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                    backToRun.hasExitTime = false;
                    backToRun.duration = 0.15f;

                    runState.RemoveTransition(original);
                    Debug.Log("BuildSextansController: rerouted run-to-idle through SextansRunStop.");
                }
            }

            // The rip loses additive reference poses, so every ADDITIVE layer
            // (aim-turn counters, neck-lean correctives, rifle recoil, hit
            // flinch, tripod/MG/jetpack machinery) adds distorted deltas: the
            // owl-head during turns. A melee doll needs none of it. Stripping
            // the motions leaves the layers inert regardless of the weights
            // the driver sets (empty states write no curves). The lost hit
            // flinch is replaced by her own KnockBack on the action layer.
            int strippedStates = 0;
            foreach (var layer in controller.layers)
            {
                if (layer.blendingMode != AnimatorLayerBlendingMode.Additive)
                    continue;
                strippedStates += StripMotions(layer.stateMachine);
            }
            Debug.Log($"BuildSextansController: stripped motions from {strippedStates} additive-layer state(s).");

            // The WeaponSocket layer ships at weight 1 playing the full-body
            // weapon-calibration clip. In vanilla, StateMachineBehaviours on
            // the logic layers manage it at runtime, but the rip loses all
            // behaviours, so in driver-less contexts (the armoury preview)
            // it overrides the whole body with the calibration pose. A melee
            // doll needs no rifle socket calibration: weight 0 and no motion.
            var socketLayers = controller.layers;
            for (int li = 0; li < socketLayers.Length; li++)
            {
                if (socketLayers[li].name != "WeaponSocket_Layer")
                    continue;
                socketLayers[li].defaultWeight = 0f;
                StripMotions(socketLayers[li].stateMachine);
                Debug.Log("BuildSextansController: neutralised WeaponSocket_Layer (weight 0, motions stripped).");
            }
            controller.layers = socketLayers;

            // Action states (slash, thrust, ultimate) live on a NEW topmost
            // override layer at weight 1. On the base layer they get diluted:
            // the Aiming_Layer sits above base (override, full body) and the
            // game ramps its weight through every attack. A topmost layer
            // whose default state is EMPTY contributes nothing at rest (empty
            // states animate no curves) and fully owns the body during an
            // action. Slash keys off the native Shoot_Single trigger the game
            // fires per shot; thrust and ultimate use custom triggers the code
            // mod fires (the mech precedent).
            controller.AddParameter("SextansUltra", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("SextansSkill1", AnimatorControllerParameterType.Trigger);
            controller.AddLayer("Sextans_Actions");
            var allLayers = controller.layers;
            var actionLayer = allLayers[allLayers.Length - 1];
            actionLayer.defaultWeight = 1f;
            actionLayer.blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = allLayers;
            var actionSm = actionLayer.stateMachine;

            var rest = actionSm.AddState("None");
            actionSm.defaultState = rest;

            AnimatorState AddAction(string name, AnimationClip motion, string trigger, float outExit, float outDuration)
            {
                var state = actionSm.AddState(name);
                state.motion = motion;
                var enter = actionSm.AddAnyStateTransition(state);
                enter.AddCondition(AnimatorConditionMode.If, 0, trigger);
                enter.hasExitTime = false;
                enter.duration = 0.05f;
                enter.canTransitionToSelf = false;
                var exit = state.AddTransition(rest);
                exit.hasExitTime = true;
                exit.exitTime = outExit;
                exit.duration = outDuration;
                return state;
            }

            // Action clips: strip the neck/head muscle curves. The donor's
            // synthetic T-pose calibrates the neck slightly off, and these
            // clips swing it hard: in-game the head snaps to face straight up
            // during the slash. With the curves gone the head rides the body.
            foreach (var actionClip in new[] { "Fire", "NormalSkill1", "UltraSkill_God_Pre", "UltraSkill_God_Main" })
            {
                int strippedCurves = 0;
                foreach (var binding in AnimationUtility.GetCurveBindings(clips[actionClip]))
                {
                    if (binding.propertyName.StartsWith("Neck ") || binding.propertyName.StartsWith("Head "))
                    {
                        AnimationUtility.SetEditorCurve(clips[actionClip], binding, null);
                        strippedCurves++;
                    }
                }
                if (strippedCurves > 0)
                {
                    EditorUtility.SetDirty(clips[actionClip]);
                    Debug.Log($"BuildSextansController: stripped {strippedCurves} neck/head curve(s) from {actionClip}.");
                }
            }

            AddAction("SextansSlash", clips["Fire"], "Shoot_Single", 0.9f, 0.2f);
            AddAction("SextansThrust", clips["NormalSkill1"], "SextansSkill1", 0.95f, 0.25f);
            AddAction("SextansKnockback", clips["KnockBack"], "Hit", 0.85f, 0.2f);
            var ultraPre = AddAction("SextansUltraPre", clips["UltraSkill_God_Pre"], "SextansUltra", 0.97f, 0.05f);
            var ultraMain = actionSm.AddState("SextansUltraMain");
            ultraMain.motion = clips["UltraSkill_God_Main"];
            // re-route the pre state's exit to the main state instead of rest
            var preTransitions = ultraPre.transitions;
            foreach (var t in preTransitions)
                ultraPre.RemoveTransition(t);
            var preToMain = ultraPre.AddTransition(ultraMain);
            preToMain.hasExitTime = true;
            preToMain.exitTime = 0.97f;
            preToMain.duration = 0.05f;
            var mainOut = ultraMain.AddTransition(rest);
            mainOut.hasExitTime = true;
            mainOut.exitTime = 0.95f;
            mainOut.duration = 0.25f;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            // Wire the prefab's Animator to the new controller. BakeHumanoid
            // copies the vanilla controller on every bake, so this step must
            // re-run after any re-bake.
            var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var animator = prefabRoot.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    Debug.LogError("BuildSextansController: prefab has no Animator.");
                    EditorApplication.Exit(1);
                    return;
                }
                animator.runtimeAnimatorController = controller;
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            Debug.Log($"BuildSextansController: wrote {OutputControllerPath} and wired {PrefabPath}.");
            EditorApplication.Exit(0);
        }

        private const string RepairedClipsDir = "Assets/Prefabs/sextans/_bake/vanilla_clips";
        private static readonly Dictionary<string, AnimationClip> _repairedClips = new();

        private static bool IsLoopClip(string name)
        {
            var n = name.ToLowerInvariant();
            return n.Contains("idle") || n.Contains("loop") || n.Contains("deployed_shooting")
                || name == "Rifle_Prone";
        }

        private static AnimationClip RepairClip(AnimationClip source)
        {
            if (_repairedClips.TryGetValue(source.name, out var cached))
                return cached;
            if (!AssetDatabase.IsValidFolder(RepairedClipsDir))
            {
                // CopyAsset into a folder the asset database has not imported
                // yet fails silently on a fresh checkout
                System.IO.Directory.CreateDirectory(RepairedClipsDir);
                AssetDatabase.Refresh();
            }

            // FILE-level copy. Object.Instantiate + CreateAsset silently
            // drops the muscle curves of these ripped clips (the copies came
            // out as empty shells: settings, no animation). CopyAsset clones
            // the serialised file byte-for-byte, then the settings are edited
            // in place.
            // FBX take names can carry path-hostile characters ("model|take").
            // Path.GetInvalidFileNameChars is OS-specific (misses '|' on
            // Linux), and Unity rejects such asset paths on every OS, so the
            // allow-list is explicit.
            var fileName = new string(source.name
                .Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_')
                .ToArray());
            var sourcePath = AssetDatabase.GetAssetPath(source);
            var destPath = $"{RepairedClipsDir}/{fileName}.anim";
            AssetDatabase.DeleteAsset(destPath);
            if (!AssetDatabase.CopyAsset(sourcePath, destPath))
            {
                Debug.LogError($"BuildSextansController: CopyAsset failed for {sourcePath}.");
                _repairedClips[source.name] = source;
                return source;
            }
            // Patch the settings TEXTUALLY in the copied file. Any editor-API
            // write (SetAnimationClipSettings and friends) re-serialises the
            // ripped clip and silently DISCARDS its muscle curves (the rip's
            // curve encoding does not round-trip), leaving an empty shell.
            // Textual YAML field replacement keeps the curve data untouched.
            var truth = TruthFor(source.name);
            if (truth != null)
            {
                var text = System.IO.File.ReadAllText(destPath);
                foreach (var pair in truth)
                {
                    var value = pair.Key is "StartTime" or "StopTime" or "OrientationOffsetY" or "Level" or "CycleOffset"
                        ? pair.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                        : ((int)pair.Value).ToString();
                    text = System.Text.RegularExpressions.Regex.Replace(
                        text, $@"(m_{pair.Key}): [^\r\n]+", $"$1: {value}");
                }
                System.IO.File.WriteAllText(destPath, text);
                AssetDatabase.ImportAsset(destPath);
            }
            else
            {
                Debug.LogWarning($"BuildSextansController: no truth settings for '{source.name}', copied verbatim.");
            }

            var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(destPath);
            _repairedClips[source.name] = copy;
            return copy;
        }

        // Root-motion flags per clip family. loopBlend* == "Bake Into Pose":
        // TRUE keeps that motion in the body, FALSE applies it to the
        // transform as root motion. Ground rules reconstructed from in-game
        // behaviour: vault/climb/cover-crossing clips genuinely displace (all
        // real: baking them breaks the traversal), root-motion turns rotate
        // the actor (orientation real), everything else is in-place (fully
        // baked: a real-Y crouch transition lowers the TRANSFORM as well as
        // the hips and sinks the character through the floor).
        private static void ApplyRootMotionFlags(AnimationClip clip, bool loop, float stopTime)
        {
            var n = clip.name.ToLowerInvariant();
            bool displaces = n.Contains("vault") || n.Contains("climb") || n.Contains("overcover");
            bool rotates = (n.Contains("turn") && !n.Contains("counter") && !n.Contains("no_rm"))
                || n.Contains("start_180") || n.Contains("start180") || n.Contains("startback");

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (stopTime > 0f)
                settings.stopTime = stopTime;
            settings.loopTime = loop;
            settings.loopBlend = false;
            settings.loopBlendOrientation = !(displaces || rotates);
            settings.loopBlendPositionY = !displaces;
            settings.loopBlendPositionXZ = !displaces;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            settings.level = 0f;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }

        // Original per-clip muscle settings, extracted with UnityPy from the
        // game's own AnimationClip assets (the rip zeroes the whole block):
        // clip name TAB Field=value;Field=value;...
        private const string TruthPath = "Assets/Prefabs/sextans/_bake/clip_settings_truth.txt";
        private static Dictionary<string, Dictionary<string, float>> _truth;

        private static Dictionary<string, float> TruthFor(string clipName)
        {
            if (_truth == null)
            {
                _truth = new Dictionary<string, Dictionary<string, float>>();
                foreach (var line in System.IO.File.ReadAllLines(TruthPath))
                {
                    var tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    var fields = new Dictionary<string, float>();
                    foreach (var pair in line[(tab + 1)..].Split(';'))
                    {
                        var kv = pair.Split('=');
                        fields[kv[0]] = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
                    }
                    _truth[line[..tab]] = fields;
                }
            }
            _truth.TryGetValue(clipName, out var result);
            return result;
        }

        private static void ApplyTruthSettings(AnimationClip clip, Dictionary<string, float> t, bool keepTiming)
        {
            var s = AnimationUtility.GetAnimationClipSettings(clip);
            if (!keepTiming)
            {
                s.startTime = t["StartTime"];
                s.stopTime = t["StopTime"];
                s.cycleOffset = t["CycleOffset"];
            }
            s.orientationOffsetY = t["OrientationOffsetY"];
            s.level = t["Level"];
            s.loopTime = t["LoopTime"] != 0f;
            s.loopBlend = t["LoopBlend"] != 0f;
            s.loopBlendOrientation = t["LoopBlendOrientation"] != 0f;
            s.loopBlendPositionY = t["LoopBlendPositionY"] != 0f;
            s.loopBlendPositionXZ = t["LoopBlendPositionXZ"] != 0f;
            s.keepOriginalOrientation = t["KeepOriginalOrientation"] != 0f;
            s.keepOriginalPositionY = t["KeepOriginalPositionY"] != 0f;
            s.keepOriginalPositionXZ = t["KeepOriginalPositionXZ"] != 0f;
            s.heightFromFeet = t["HeightFromFeet"] != 0f;
            s.mirror = t["Mirror"] != 0f;
            AnimationUtility.SetAnimationClipSettings(clip, s);
        }

        private static bool NeedsRepair(Motion motion, out AnimationClip clip)
        {
            clip = motion as AnimationClip;
            if (clip == null)
                return false;
            var path = AssetDatabase.GetAssetPath(clip);
            return path != null && path.StartsWith("Assets/Imported/");
        }

        private static int RepairInStateMachine(AnimatorStateMachine sm)
        {
            int repaired = 0;
            foreach (var child in sm.states)
            {
                // Foot IK everywhere OFF: the rip zeroes the clips' foot IK
                // goal curves, so the solver pulls the feet toward degenerate
                // goals and drags the hips to ground level (the crouch sink,
                // and the strange leg motion during full-body actions).
                child.state.iKOnFeet = false;

                if (child.state.motion is BlendTree tree)
                {
                    repaired += RepairInBlendTree(tree);
                }
                else if (NeedsRepair(child.state.motion, out var clip))
                {
                    child.state.motion = RepairClip(clip);
                    repaired++;
                }
            }
            foreach (var sub in sm.stateMachines)
                repaired += RepairInStateMachine(sub.stateMachine);
            return repaired;
        }

        private static int RepairInBlendTree(BlendTree tree)
        {
            int repaired = 0;
            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion is BlendTree nested)
                {
                    repaired += RepairInBlendTree(nested);
                }
                else if (NeedsRepair(children[i].motion, out var clip))
                {
                    children[i].motion = RepairClip(clip);
                    repaired++;
                }
            }
            tree.children = children;
            return repaired;
        }

        // The game's contracts ride ANIMATION EVENTS on the clips: footstep
        // sounds on locomotion, and the vault-complete signal the mover waits
        // for (OnAnimationEvent, without which movement goes slow-motion after
        // a vault). Her GFL2 clips carry none, so the canonical vanilla
        // source's events are copied across, times rescaled to her clip
        // length. SetAnimationEvents replaces, so re-runs are idempotent.
        private static void CopyEventsScaled(AnimationClip vanilla, AnimationClip replacement)
        {
            var sourceEvents = AnimationUtility.GetAnimationEvents(vanilla);
            if (sourceEvents.Length == 0)
                return;
            float scale = vanilla.length > 0.001f ? replacement.length / vanilla.length : 1f;
            foreach (var e in sourceEvents)
                e.time *= scale;
            AnimationUtility.SetAnimationEvents(replacement, sourceEvents);
            Debug.Log($"BuildSextansController: copied {sourceEvents.Length} event(s) {vanilla.name} -> {replacement.name} (scale {scale:0.00}).");
        }

        private static int StripMotions(AnimatorStateMachine sm)
        {
            int stripped = 0;
            foreach (var child in sm.states)
            {
                if (child.state.motion != null)
                {
                    child.state.motion = null;
                    stripped++;
                }
            }
            foreach (var sub in sm.stateMachines)
                stripped += StripMotions(sub.stateMachine);
            return stripped;
        }

        private static int SwapInStateMachine(AnimatorStateMachine sm, Dictionary<string, AnimationClip> clips)
        {
            int swapped = 0;
            foreach (var child in sm.states)
            {
                if (child.state.motion is BlendTree tree)
                {
                    swapped += SwapInBlendTree(tree, clips);
                }
                else if (child.state.motion is AnimationClip clip
                         && ClipSwap.TryGetValue(clip.name, out var replacement))
                {
                    child.state.motion = clips[replacement];
                    swapped++;
                }
            }
            foreach (var sub in sm.stateMachines)
                swapped += SwapInStateMachine(sub.stateMachine, clips);
            return swapped;
        }

        private static int SwapInBlendTree(BlendTree tree, Dictionary<string, AnimationClip> clips)
        {
            int swapped = 0;
            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion is BlendTree nested)
                {
                    swapped += SwapInBlendTree(nested, clips);
                }
                else if (children[i].motion is AnimationClip clip
                         && ClipSwap.TryGetValue(clip.name, out var replacement))
                {
                    children[i].motion = clips[replacement];
                    swapped++;
                }
            }
            tree.children = children;
            return swapped;
        }

        public static void Dump()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(VanillaControllerPath);
            if (controller == null)
            {
                Debug.LogError($"BuildSextansController: controller not found at {VanillaControllerPath}");
                EditorApplication.Exit(1);
                return;
            }
            foreach (var layer in controller.layers)
            {
                Debug.Log($"DUMP LAYER {layer.name} weight={layer.defaultWeight} mask={(layer.avatarMask ? layer.avatarMask.name : "none")}");
                DumpStateMachine(layer.stateMachine, layer.name);
            }
            EditorApplication.Exit(0);
        }

        private static void DumpStateMachine(AnimatorStateMachine sm, string path)
        {
            foreach (var child in sm.states)
            {
                var motion = child.state.motion;
                Debug.Log($"DUMP STATE {path}/{child.state.name} motion={Describe(motion)}");
                foreach (var t in child.state.transitions)
                {
                    var dest = t.destinationState != null ? t.destinationState.name
                        : t.destinationStateMachine != null ? $"SM:{t.destinationStateMachine.name}"
                        : t.isExit ? "EXIT" : "?";
                    var conds = string.Join(" & ", t.conditions.Select(c => $"{c.parameter}{c.mode}{c.threshold}"));
                    Debug.Log($"DUMP TRANS {path}/{child.state.name} -> {dest} exitTime={(t.hasExitTime ? t.exitTime.ToString("0.00") : "no")} dur={t.duration:0.00} conds=[{conds}]");
                }
            }
            foreach (var sub in sm.stateMachines)
                DumpStateMachine(sub.stateMachine, $"{path}/{sub.stateMachine.name}");
        }

        private static string Describe(Motion motion)
        {
            if (motion == null)
                return "(none)";
            if (motion is BlendTree tree)
            {
                var children = string.Join("|", tree.children.Select(c => Describe(c.motion)));
                return $"BT[{tree.blendParameter}/{tree.blendParameterY}]({children})";
            }
            return motion.name;
        }
    }
}
