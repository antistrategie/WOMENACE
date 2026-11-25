using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WOMENACE.Editor
{
    // Bakes the body of Team Limonad (GFL2's ATG01 Armed Echelon): the three crew, the launcher
    // and the rocket under one root with one Animator on the construct contract.
    //
    // Each state is its own FBX from scripts/support_team/build_team_states.py (prepare_team.py
    // installs them here), all on the same bone paths: limonad_default.fbx is the body with the
    // native idle, and limonad_<state>.fbx holds a take each: run, hit, death and vault on a
    // donor Doll's clips with the props carried in hand, aim-in, aim-hold and fire cut from the
    // team's own GFL2 ult, and the deployed stance (the idle with Bakhmaro kneeling in AimIn's
    // first pose). A "motion" node under the root carries the team's travel and is the avatar's
    // root-motion node: the vault's crossing is on it, and it stands still everywhere else.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity \
    //     -executeMethod WOMENACE.Editor.BuildLimonad.Build
    public static class BuildLimonad
    {
        // The crew's own height in GFL2 metres (the merged scene's bounds), times the dolls'
        // 1.2 world factor.
        private const float CanonHeight = 1.40f;
        private const float WorldFactor = 1.2f;
        private const string SrcDir = "Assets/Authored/limonad/default";
        private const string Fbx = SrcDir + "/limonad_default.fbx";
        private const string AnimDir = "Assets/Prefabs/limonad/_bake/default";
        private const string PrefabPath = "Assets/Prefabs/limonad/default/main.prefab";
        // The vault the mover waits on: its clip's events end the crossing.
        private const string VanillaVault = "Assets/Imported/rmc_default_female_soldier_2/AnimationClip/AC_Human_Vault1mShort.anim";
        // The crew's own ramps are not extracted, so they borrow the shared skin and weapon ramps
        // and Voymastina's hair and cloth ramps, the nearest GFL2 sets in the repository.
        private const string SharedRamps = "Assets/Authored/shared/ramps";
        private const string VoyRamps = "Assets/Authored/voymastina/ramps";
        private const string FaceSdf = "Assets/Authored/shared/face_sdf.png";

        public static void Build() => EditorApplication.Exit(Bake() ? 0 : 1);

        // The highest head bone over the FBX root, scaled up by the head's share of the crew's
        // full height (the helmet tops out at 1.40 over a head bone at 1.21 in GFL2 metres).
        private static float HeadHeight()
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx));
            try
            {
                var heads = inst.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Head_M").ToList();
                var top = heads.Count > 0 ? heads.Max(t => inst.transform.InverseTransformPoint(t.position).y) : 0f;
                return top * (1.40f / 1.21f);
            }
            finally { UnityEngine.Object.DestroyImmediate(inst); }
        }

        // Which way each crew member faces in the prefab: her face mesh's centre seen from her
        // head bone. MENACE expects a unit to face +Z.
        public static void LogFacing()
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                var all = inst.GetComponentsInChildren<Transform>(true);
                foreach (var member in new[] { "ATG01_MemberA", "ATG01_MemberB", "ATG01_MemberC" })
                {
                    var root = all.FirstOrDefault(t => t.name == member);
                    var head = root?.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Head_M");
                    var face = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == $"c_{member}_slg_face_lod0");
                    if (head == null || face == null) { Debug.Log($"limonad: facing of {member} unknown"); continue; }
                    var toFace = face.bounds.center - head.position;
                    Debug.Log($"limonad: {member} head {head.position} face-from-head {toFace}");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(inst); EditorApplication.Exit(0); }
        }

        private static bool Bake()
        {
            try
            {
                BuildTitan.EnsureFolder(AnimDir);
                BuildTitan.EnsureFolder("Assets/Prefabs/limonad/default");

                var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx);
                if (imp == null) { Debug.LogError("limonad: FBX not found at " + Fbx); return false; }
                imp.animationType = ModelImporterAnimationType.Generic;
                imp.importAnimation = true;
                // Imported for their names only: each submesh's material names its texture set.
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                imp.useFileScale = true;
                imp.globalScale = 1f;
                AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate);
                // BuildTitan.MeasureHeight reads skinned vertices through BakeMesh, which misses
                // the 0.01 scale Blender writes on these armatures, so the height comes from the
                // head bones instead.
                var measured = HeadHeight();
                var scale = CanonHeight * WorldFactor / measured;
                AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate);
                var idle = ImportTake(Fbx, scale, loop: true, eventSource: null, travels: false, keepMaterials: true);
                if (idle == null) { Debug.LogError("limonad: no take in the FBX"); return false; }
                Debug.Log($"limonad: head height {measured:F3} at scale 1, {HeadHeight():F3} baked");

                var takes = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
                var bodyPaths = BodyPaths();
                foreach (var (state, file, loop, events, travels) in States)
                {
                    var imported = ImportTake($"{SrcDir}/{file}", scale, loop, events, travels, keepMaterials: false);
                    if (imported == null) { Debug.LogError($"limonad: no take in {file}"); return false; }
                    var clip = OnBodyPaths(imported, state, bodyPaths);
                    takes[state] = clip;
                    Debug.Log($"limonad: {state} <- {file}: {clip.length:0.00}s loop={loop} events={AnimationUtility.GetAnimationEvents(clip).Length}"
                              + $" motionNode='{((ModelImporter)AssetImporter.GetAtPath($"{SrcDir}/{file}")).motionNodeName}'");
                }
                ForwardTravel(takes["Vault"]);
                // While the team aims and fires the laptop is put away: its renderer is switched
                // off in those takes (AimOut is AimIn reversed, so it carries the key too). Every
                // state writes defaults, so the others show it again.
                var laptop = bodyPaths.FirstOrDefault(p => p == "Laptop" || p.EndsWith("/Laptop", StringComparison.Ordinal));
                if (laptop == null) Debug.LogError("limonad: no Laptop renderer path in the body");
                else
                    foreach (var state in new[] { "AimIn", "AimHold", "Fire" })
                    {
                        var clip = takes[state];
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(laptop, typeof(SkinnedMeshRenderer), "m_Enabled"),
                            AnimationCurve.Constant(0f, clip.length, 0f));
                        EditorUtility.SetDirty(clip);
                    }
                // The travel the vault hands the mover: its root-motion curve from start to end.
                foreach (var b in AnimationUtility.GetCurveBindings(takes["Vault"]))
                    if (b.propertyName.StartsWith("MotionT", StringComparison.Ordinal))
                    {
                        var curve = AnimationUtility.GetEditorCurve(takes["Vault"], b);
                        Debug.Log($"limonad: vault {b.propertyName} {curve.Evaluate(0f):0.000} -> {curve.Evaluate(takes["Vault"].length):0.000}");
                    }

                var ac = AnimatorController.CreateAnimatorControllerAtPath($"{AnimDir}/limonad.controller");
                // Every parameter the construct driver writes, so no Set call misses.
                ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
                ac.AddParameter("Movement_Initialized", AnimatorControllerParameterType.Bool);
                ac.AddParameter("Aiming", AnimatorControllerParameterType.Bool);
                ac.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
                ac.AddParameter("Vault", AnimatorControllerParameterType.Trigger);
                ac.AddParameter("Overloaded", AnimatorControllerParameterType.Bool);
                ac.AddParameter("Rotation", AnimatorControllerParameterType.Float);
                ac.AddParameter("Shoot_Single", AnimatorControllerParameterType.Trigger);
                ac.AddParameter("Special_Attack_1", AnimatorControllerParameterType.Trigger);
                ac.AddParameter("Special_Attack_2", AnimatorControllerParameterType.Trigger);
                ac.AddParameter("OnDeath", AnimatorControllerParameterType.Trigger);
                // The actor's stance as vanilla's soldier controller reads it (ActorStance:
                // 0 default, 1 deployed, 2 pinned down).
                ac.AddParameter("Stance", AnimatorControllerParameterType.Int);
                var sm = ac.layers[0].stateMachine;

                var idleS = sm.AddState("Idle"); idleS.motion = idle; sm.defaultState = idleS;
                var run = sm.AddState("Run"); run.motion = takes["Run"];
                var kneelS = sm.AddState("Kneel"); kneelS.motion = takes["Kneel"];
                var aimIn = sm.AddState("AimIn"); aimIn.motion = takes["AimIn"];
                var aimHold = sm.AddState("AimHold"); aimHold.motion = takes["AimHold"];
                var aimOut = sm.AddState("AimOut"); aimOut.motion = Reversed(takes["AimIn"], "aimout");
                var fire = sm.AddState("Fire"); fire.motion = takes["Fire"];
                var hit = sm.AddState("Hit"); hit.motion = takes["Hit"];
                var vaultS = sm.AddState("Vault"); vaultS.motion = takes["Vault"];
                var death = sm.AddState("Death"); death.motion = takes["Death"];

                void T(AnimatorState a, AnimatorState b, AnimatorConditionMode m, float v, string p)
                {
                    var t = a.AddTransition(b); t.hasExitTime = false; t.duration = 0.2f;
                    t.AddCondition(m, v, p);
                }
                void Back(AnimatorState a, AnimatorState b, float exit)
                {
                    var t = a.AddTransition(b); t.hasExitTime = true; t.exitTime = exit; t.duration = 0.15f;
                }
                void Any(AnimatorState s, string trigger, AnimatorState after, float exit)
                {
                    var t = sm.AddAnyStateTransition(s); t.hasExitTime = false; t.duration = 0.08f; t.canTransitionToSelf = false;
                    t.AddCondition(AnimatorConditionMode.If, 0, trigger);
                    if (after != null) Back(s, after, exit);
                }

                T(idleS, run, AnimatorConditionMode.Greater, 0.1f, "Speed");
                T(run, idleS, AnimatorConditionMode.Less, 0.1f, "Speed");
                // Aiming raises the launcher (AimIn) into the held aim, a shot plays Fire and comes
                // back to the hold, and lowering it plays AimIn backwards (AimOut). AimIn and
                // AimHold meet on one pose, so that hand-over has no blend.
                T(idleS, aimIn, AnimatorConditionMode.If, 0, "Aiming");
                var raised = aimIn.AddTransition(aimHold); raised.hasExitTime = true; raised.exitTime = 1f; raised.duration = 0f;
                T(aimIn, aimOut, AnimatorConditionMode.IfNot, 0, "Aiming");
                T(aimHold, aimOut, AnimatorConditionMode.IfNot, 0, "Aiming");
                T(aimHold, run, AnimatorConditionMode.Greater, 0.1f, "Speed");
                // Deployed (or pinned down) Bakhmaro kneels with the launcher down while the other
                // two idle. The cross-fade is her kneeling down and getting up. Her pose is
                // AimIn's first, so aiming from it only moves the other two, and lowering the aim
                // settles back into whichever stance the team is in.
                void Stance(AnimatorState a, AnimatorState b, AnimatorConditionMode m)
                {
                    var t = a.AddTransition(b); t.hasExitTime = false; t.duration = 0.45f;
                    t.AddCondition(m, 0.5f, "Stance");
                }
                Stance(idleS, kneelS, AnimatorConditionMode.Greater);
                Stance(kneelS, idleS, AnimatorConditionMode.Less);
                T(kneelS, aimIn, AnimatorConditionMode.If, 0, "Aiming");
                T(kneelS, run, AnimatorConditionMode.Greater, 0.1f, "Speed");
                foreach (var (after, mode) in new[] { (kneelS, AnimatorConditionMode.Greater), (idleS, AnimatorConditionMode.Less) })
                {
                    var t = aimOut.AddTransition(after); t.hasExitTime = true; t.exitTime = 0.95f; t.duration = 0.15f;
                    t.AddCondition(mode, 0.5f, "Stance");
                }
                Any(fire, "Shoot_Single", aimHold, 0.9f);
                Any(fire, "Special_Attack_1", aimHold, 0.9f);
                Any(hit, "Hit", idleS, 0.95f);
                Any(vaultS, "Vault", run, 0.95f);
                Any(death, "OnDeath", null, 0f);

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx));
                inst.name = "limonad_default";
                var anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                anim.runtimeAnimatorController = ac;
                // The vault's crossing is its root motion, and the game moves the team over cover
                // only as the clip carries it. Every other take locks its root, so it moves nothing.
                anim.applyRootMotion = true;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                // The "motion" node is the root-motion source.
                var avatar = AvatarBuilder.BuildGenericAvatar(inst, MotionNode);
                avatar.name = "limonad_default_avatar";
                AssetDatabase.CreateAsset(avatar, $"{AnimDir}/limonad_default_avatar.asset");
                anim.avatar = avatar;

                AssignMaterials(inst);

                // The launcher's muzzle, under the name skills resolve: on the "launcher_muzzle"
                // marker the Blender export puts at the tube's tip on the launcher's Prop bone, so
                // it follows the launcher through every take. Damage sits at the team's chest
                // height for self-centred skills.
                var bones = inst.GetComponentsInChildren<Transform>(true);
                var tip = bones.FirstOrDefault(t => t.name == "launcher_muzzle");
                if (tip == null) Debug.LogWarning("limonad: no launcher_muzzle marker in the FBX, muzzle left at the root");
                var muzzle = new GameObject("muzzle").transform;
                muzzle.SetParent(tip != null ? tip : inst.transform, false);
                if (tip == null) muzzle.localPosition = new Vector3(0f, 1.2f, 0.5f);
                Debug.Log($"limonad: muzzle under {(tip != null ? PathOf(inst.transform, tip) : "root")} at {muzzle.position}");
                var damage = new GameObject("damage").transform;
                damage.SetParent(inst.transform, false);
                damage.localPosition = new Vector3(0f, 1.0f, 0f);
                // The element animator spawns its movement dust at a "dust01" child
                // (DustEffectsAnimatorComp) and logs an error at every spawn without one.
                new GameObject("dust01").transform.SetParent(inst.transform, false);

                var renderers = inst.GetComponentsInChildren<Renderer>(true);
                var hull = renderers[0].bounds;
                foreach (var r in renderers) hull.Encapsulate(r.bounds);
                Debug.Log($"limonad: renderer bounds centre={hull.center} size={hull.size}");
                foreach (var a in inst.GetComponentsInChildren<Animator>(true))
                    if (a != anim) Debug.LogWarning($"limonad: extra Animator on {a.name}");

                // Every take's curves must land on the prefab's own bones: the state takes come
                // from other FBXs, so a path that differs there animates nothing.
                var checks = new List<KeyValuePair<string, AnimationClip>> { new("Idle", idle) };
                checks.AddRange(takes);
                foreach (var (state, clip) in checks)
                {
                    var bindings = AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform)).ToList();
                    var missing = bindings.Where(b => inst.transform.Find(b.path) == null).Select(b => b.path).Distinct().ToList();
                    Debug.Log($"limonad: {state} binds {bindings.Count - bindings.Count(b => missing.Contains(b.path))}/{bindings.Count} transform curves"
                              + (missing.Count > 0 ? $", missing e.g. '{missing[0]}' (prefab has e.g. '{PathOf(inst.transform, inst.GetComponentsInChildren<Transform>(true).First(t => t.name == "Root_M"))}')" : ""));
                }

                PrefabUtility.SaveAsPrefabAsset(inst, PrefabPath);
                UnityEngine.Object.DestroyImmediate(inst);
                AssetDatabase.SaveAssets();
                Debug.Log("limonad: wrote " + PrefabPath);
                return true;
            }
            catch (Exception ex) { Debug.LogError("BuildLimonad failed: " + ex); return false; }
        }

        // Every transform path in the body FBX, the paths the prefab's Animator resolves.
        private static HashSet<string> BodyPaths()
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx));
            try { return new HashSet<string>(inst.GetComponentsInChildren<Transform>(true).Select(t => PathOf(inst.transform, t)), StringComparer.Ordinal); }
            finally { UnityEngine.Object.DestroyImmediate(inst); }
        }

        // A state FBX carries no meshes, so Unity collapses its single root node, while the body
        // FBX keeps it ("limonad/ATG01_MemberA/..." against "ATG01_MemberA/..."). The take is
        // copied into a clip of its own with every path moved onto the body's, keeping its
        // settings, its events and its root-motion curves (which are on the Animator, path "").
        private static AnimationClip OnBodyPaths(AnimationClip take, string state, HashSet<string> bodyPaths)
        {
            var bindings = AnimationUtility.GetCurveBindings(take);
            var prefix = "";
            var probe = bindings.Select(b => b.path).FirstOrDefault(p => p.Length > 0 && !bodyPaths.Contains(p));
            if (probe != null)
            {
                var match = bodyPaths.FirstOrDefault(p => p.EndsWith("/" + probe, StringComparison.Ordinal));
                if (match == null) { Debug.LogError($"limonad: {state} path '{probe}' is nowhere in the body"); return take; }
                prefix = match.Substring(0, match.Length - probe.Length);
            }
            var clip = new AnimationClip { name = $"limonad_{state.ToLowerInvariant()}", frameRate = take.frameRate };
            foreach (var b in bindings)
            {
                // The take files' root node carries Blender's unit conversion as a constant scale
                // of 100, which the unsimplified export keeps as a curve. On the prefab it would
                // scale the whole team a hundredfold, and the root's transform is the game's anyway
                // (root motion rides on the Animator's MotionT and MotionQ).
                if (b.path.Length == 0 && b.type == typeof(Transform)) continue;
                var moved = b;
                if (b.path.Length > 0) moved.path = prefix + b.path;
                AnimationUtility.SetEditorCurve(clip, moved, AnimationUtility.GetEditorCurve(take, b));
            }
            AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(take));
            AnimationUtility.SetAnimationEvents(clip, AnimationUtility.GetAnimationEvents(take));
            var path = $"{AnimDir}/{clip.name}.anim";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // Unity reads a Generic clip's root motion (MotionT) in the motion node's own frame, and
        // that node keeps the FBX's Z-up turn: its +Z is the world's down and its +Y the team's
        // forward. Stepping the Animator showed the vault's 2.33 m going straight down, which
        // the game discards when it puts the element on the ground (ElementAnimator.OnAnimatorMove
        // keeps the root position's X and Z and takes the height from the map). So the travel
        // moves onto MotionT.y, the vertical axis (MotionT.z) is cleared, and the bake flags
        // follow: the node's Y (the forward travel) is kept as motion and its XZ plane (sideways
        // and vertical) is baked into the pose.
        private static void ForwardTravel(AnimationClip clip)
        {
            EditorCurveBinding? Find(string prop) =>
                AnimationUtility.GetCurveBindings(clip).Cast<EditorCurveBinding?>().FirstOrDefault(b => b.Value.propertyName == prop);
            var y = Find("MotionT.y");
            var z = Find("MotionT.z");
            if (y == null || z == null) { Debug.LogError("limonad: vault has no MotionT curves"); return; }
            var travel = AnimationUtility.GetEditorCurve(clip, z.Value);
            AnimationUtility.SetEditorCurve(clip, y.Value, travel);
            AnimationUtility.SetEditorCurve(clip, z.Value, AnimationCurve.Constant(0f, clip.length, 0f));
            // The take's sideways sway (up to 16 cm mid-crossing) would carry the whole team
            // off its path, so the crossing is straight ahead.
            var x = Find("MotionT.x");
            if (x != null) AnimationUtility.SetEditorCurve(clip, x.Value, AnimationCurve.Constant(0f, clip.length, 0f));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopBlendPositionY = false;
            settings.loopBlendPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
        }

        // A clip played backwards, saved beside the takes. A state at speed -1 starts at the
        // clip's start and clamps there, and its exit time counts the wrong way, so the reverse
        // is baked as a clip of its own.
        private static AnimationClip Reversed(AnimationClip source, string state)
        {
            var clip = new AnimationClip { name = $"limonad_{state}", frameRate = source.frameRate };
            var length = source.length;
            foreach (var b in AnimationUtility.GetCurveBindings(source))
            {
                var keys = AnimationUtility.GetEditorCurve(source, b).keys
                    .Select(k => new Keyframe(length - k.time, k.value, -k.outTangent, -k.inTangent))
                    .OrderBy(k => k.time).ToArray();
                AnimationUtility.SetEditorCurve(clip, b, new AnimationCurve(keys));
            }
            var settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            var path = $"{AnimDir}/{clip.name}.anim";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private static string PathOf(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x != null && x != root; x = x.parent) parts.Insert(0, x.name);
            return string.Join("/", parts);
        }

        // The team's state takes, one FBX each (scripts/support_team/build_team_states.py), all on
        // the main body's bone paths. Events come from the vanilla clip whose job each take does: the run
        // gets the sprint's footsteps, the vault the crossing's vault-complete signal.
        private const string VanillaSprint = "Assets/Imported/rmc_default_female_soldier_2/AnimationClip/Rifle_SprintLoop.anim";
        private const string MotionNode = "motion";

        // AimIn, AimHold and Fire are cut from the ult so all three meet on one pose: AimIn ends
        // where AimHold (a ping-pong loop) starts and ends, and Fire starts there too.
        private static readonly (string State, string File, bool Loop, string Events, bool Travels)[] States =
        {
            ("Run", "limonad_run.fbx", true, VanillaSprint, false),
            ("Kneel", "limonad_kneel.fbx", true, null, false),
            ("AimIn", "limonad_aimin.fbx", false, null, false),
            ("AimHold", "limonad_aimhold.fbx", true, null, false),
            ("Fire", "limonad_fire.fbx", false, null, false),
            ("Hit", "limonad_hit.fbx", false, null, false),
            ("Death", "limonad_death.fbx", false, null, false),
            ("Vault", "limonad_vault.fbx", false, VanillaVault, true),
        };

        // Imports one FBX's take at the body's scale with "motion" as its root node. A take that
        // travels keeps the motion node's ground travel as root motion; every other take bakes it
        // into the pose (it stands still there anyway).
        private static AnimationClip ImportTake(string path, float scale, bool loop, string eventSource, bool travels, bool keepMaterials)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (imp == null) { Debug.LogError("limonad: FBX not found at " + path); return null; }
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.importAnimation = true;
            if (!keepMaterials) imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.useFileScale = true;
            imp.globalScale = scale;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            // The root node is named by its path as the importer lists it.
            var motionPath = imp.transformPaths.FirstOrDefault(p => p == MotionNode || p.EndsWith("/" + MotionNode, StringComparison.Ordinal));
            if (motionPath == null) Debug.LogWarning($"limonad: no {MotionNode} node in {path}");
            imp.motionNodeName = motionPath ?? "";

            var vanilla = eventSource != null ? AssetDatabase.LoadAssetAtPath<AnimationClip>(eventSource) : null;
            if (eventSource != null && vanilla == null) Debug.LogWarning("limonad: no vanilla clip at " + eventSource);
            var defs = imp.defaultClipAnimations;
            foreach (var c in defs)
            {
                c.loopTime = loop;
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = !travels;
                if (vanilla != null)
                {
                    // Importer events are placed in normalised time.
                    var events = AnimationUtility.GetAnimationEvents(vanilla);
                    foreach (var e in events) e.time = vanilla.length > 0.001f ? e.time / vanilla.length : 0f;
                    c.events = events;
                }
            }
            imp.clipAnimations = defs;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview", StringComparison.Ordinal))
                .OrderByDescending(c => c.length).FirstOrDefault();
        }

        // Each imported material names its member and part, and the part picks the texture set.
        // The sets go on the Doll shaders as Voymastina's mech does, because the Menace/character
        // stub draws them white in game.
        private static void AssignMaterials(GameObject inst)
        {
            var built = new Dictionary<string, Material>(StringComparer.Ordinal);

            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                {
                    var name = Regex.Replace(mats[i] != null ? mats[i].name : "", @"\.\d+$", "");
                    var set = TextureSet(name);
                    if (set != null)
                    {
                        if (!built.TryGetValue(set, out var mat))
                            built[set] = mat = BuildMaterial(set);
                        mats[i] = mat;
                    }
                    else Debug.LogWarning($"limonad: no texture set for material '{name}' on {r.name}");
                    Debug.Log($"limonad: {r.name}[{i}] {name} -> {(mats[i] != null ? mats[i].name : "none")}");
                }
                r.sharedMaterials = mats;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                r.renderingLayerMask = 1;
            }
        }

        private static string TextureSet(string material)
        {
            if (material.StartsWith("cw_ATG01_WL", StringComparison.Ordinal)) return "cw_ATG01_WL";
            if (material.StartsWith("Laptop", StringComparison.Ordinal)) return "Laptop";
            var m = Regex.Match(material, @"^(c_ATG01_Member[ABC]_slg)_(\w+?)(_?uber)?$");
            if (!m.Success) return null;
            var (prefix, part) = (m.Groups[1].Value, m.Groups[2].Value);
            return part switch
            {
                "eyenewadd" => prefix + "_eyeadd",
                "eyenewmul" => prefix + "_eyemul",
                "eyenew" => prefix + "_eye",
                "eyelash" or "face" => prefix + "_face",
                "fringe" or "hair" => prefix + "_hair",
                _ => prefix + "_" + part,
            };
        }

        // The eye layers take the Doll eye shaders, everything else the Doll toon shader with the
        // GFL2 set as shipped. The rmo is data in the game's own packing, which the shader reads.
        private static Material BuildMaterial(string set)
        {
            Texture2D Tex(string name, bool sRGB, bool isNormal = false) =>
                BuildTitan.LoadTex($"{SrcDir}/textures/{name}.png", sRGB, isNormal);
            Material New(string shaderName)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null) Debug.LogError("limonad: missing shader " + shaderName);
                return new Material(shader) { name = "limonad_" + set };
            }

            Material mat;
            if (set.EndsWith("_eyeadd", StringComparison.Ordinal) || set.EndsWith("_eyemul", StringComparison.Ordinal))
            {
                var add = set.EndsWith("_eyeadd", StringComparison.Ordinal);
                mat = New(add ? "Womenace/DollEyeHighlight" : "Womenace/DollEyeShadow");
                var blend = Tex(set.Substring(0, set.Length - "_eyeadd".Length) + "_eyeblend", sRGB: true);
                if (blend != null) mat.SetTexture("_BaseMap", blend);
                else Debug.LogWarning($"limonad: no eye blend for {set}");
            }
            else if (set.EndsWith("_eye", StringComparison.Ordinal))
            {
                mat = New("Womenace/DollEye");
                var alb = Tex(set + "_d", sRGB: true);
                if (alb != null) mat.SetTexture("_BaseMap", alb);
            }
            else
            {
                mat = New("Womenace/DollToon");
                var alb = Tex(set + "_d", sRGB: true);
                if (alb != null) mat.SetTexture("_BaseMap", alb);
                else Debug.LogWarning($"limonad: no albedo for {set}");
                var nrm = Tex(set + "_n", sRGB: false, isNormal: true);
                if (nrm != null) mat.SetTexture("_NormalMap", nrm);
                var rmo = Tex(set + "_rmo", sRGB: false);
                if (rmo != null) mat.SetTexture("_MaskMap", rmo);
                var spc = Tex(set + "_spc", sRGB: true);
                if (spc != null)
                {
                    // The ripped hair carries GFL2's strip UV, so its streak can be drawn.
                    mat.SetTexture("_SpecularMap", spc);
                    mat.SetFloat("_MatCapIntensity", 1f);
                }
                bool Is(string part) => set.EndsWith(part, StringComparison.Ordinal);
                var rampPath = Is("_face") ? SharedRamps + "/ramp_skin.png"
                    : Is("_hair") ? VoyRamps + "/ramp_hair.png"
                    : Is("_upper") || Is("_lower") ? VoyRamps + "/ramp_cloth_main.png"
                    : SharedRamps + "/ramp_weapon.png";
                var ramp = AssetDatabase.LoadAssetAtPath<Texture2D>(rampPath);
                if (ramp != null) mat.SetTexture("_RampMap", ramp);
                else Debug.LogError("limonad: missing ramp " + rampPath);
                if (Is("_face"))
                {
                    mat.SetFloat("_UseBlendTex", 1f);
                    var sdf = AssetDatabase.LoadAssetAtPath<Texture2D>(FaceSdf);
                    if (sdf != null) mat.SetTexture("_SdfMap", sdf);
                    else Debug.LogError("limonad: missing face SDF " + FaceSdf);
                }
            }
            var path = $"{AnimDir}/{mat.name}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
