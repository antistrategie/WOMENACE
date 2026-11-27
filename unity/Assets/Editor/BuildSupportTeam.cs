using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WOMENACE.Editor
{
    // Bakes the body of an Armed Echelon (support team): the crew and their props under one root
    // with one Animator on the construct contract.
    //
    // Each state is its own FBX from scripts/support_team/build_team_states.py (prepare_team.py
    // installs them in Assets/Authored/<team>/default), all on the same bone paths:
    // <team>_default.fbx is the body with the Idle take, and <team>_<state>.fbx holds each other
    // state's take. A "motion" node under the root carries the team's travel and is the avatar's
    // root-motion node: the vault's crossing is on it, and it stands still everywhere else.
    //
    // The states, their loop flags and which of them travels come from the team's states.json,
    // the spec build_team_states.py bakes from. What only this bake needs is in bake.json beside
    // it (BakeSpec below).
    //
    // Deployed, a team either holds one Kneel take, cross-faded in and out, or goes along a line of
    // still states joined by bridges ("line" in bake.json): Fabric carries its mortar, puts it down
    // (Deploy, IdleDeployed) and mans it (Man, SetUp). A bridge is a states.json state "between"
    // two of the line's, and played backwards (Un<bridge>) it is the way back.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity \
    //     -executeMethod WOMENACE.Editor.BuildSupportTeam.Build -team limonad
    public static class BuildSupportTeam
    {
        // GFL2 world sizes times this keep a team at its canon size next to a doll.
        private const float WorldFactor = 1.2f;
        private const string MotionNode = "motion";
        private const string HeadBone = "Head_M";
        // The controller's states the takes must fill, on top of the body's Idle, and Kneel for a
        // team without a line.
        private static readonly string[] ControllerStates = { "Run", "AimIn", "AimHold", "Fire", "Hit", "Vault", "Death" };
        // The takes in which the team aims and fires. AimOut is AimIn reversed, so it inherits
        // whatever AimIn carries.
        private static readonly string[] AimStates = { "AimIn", "AimHold", "Fire" };

#pragma warning disable 0649 // assigned by JsonUtility
        [Serializable]
        private class StatesFile
        {
            public string team;
            public string[] crew;
            public StateRow[] states;
        }

        [Serializable]
        private class StateRow
        {
            public string name;
            public bool loop;
            // The crew member whose travel the state carries, empty when it stays in place.
            public string travel;
            // A bridge's two still states, the first it starts on and the second it ends on.
            public string[] between;
        }

        [Serializable]
        private class BakeSpec
        {
            // The crew's full height in GFL2 metres (the merged scene's bounds) and the height of
            // the head bone under it. The FBX's head bones give the measured height, and their
            // ratio scales it up to the top of the crew.
            public float canon_height;
            public float head_bone_height;
            // The marker build_team_states.py puts on the weapon's tip, which skills fire from.
            public string muzzle_marker;
            // Where self-centred skills place their effects, the crew's chest height in game metres.
            public float damage_height;
            // A prop put away while the team aims and fires (its renderer's name), or empty.
            public string stowed_while_aiming;
            // Texture-set names of the props' materials. A material that starts with one takes it.
            public string[] prop_texture_sets;
            // The ramp each texture set takes by the end of its name, first match first, and the
            // ramp of every set none of them names.
            public RampRule[] ramps;
            public string default_ramp;
            // Vanilla clips whose events a state's take carries, so the game hears the sprint's
            // footsteps or the vault's completion.
            public EventSource[] events;
            // The deployed line, Idle first: each later still state is where the team goes while
            // its condition holds and no later one's does. Empty for a team that kneels.
            public LineStep[] line;
            // The line's still state AimIn starts on and AimOut ends on (the last by default).
            public string aim_from;
            // Where Fire hands back to the held aim, and how long that blend takes.
            public float fire_exit = 0.9f;
            public float fire_blend = 0.15f;
        }

        [Serializable]
        private class LineStep
        {
            public string state;
            // The Animator parameter that asks for this state, "if" (a bool set) or "greater"
            // (above value).
            public string param;
            public string mode;
            public float value;
        }

        [Serializable]
        private class RampRule
        {
            public string part;
            public string ramp;
        }

        [Serializable]
        private class EventSource
        {
            public string state;
            public string clip;
        }
#pragma warning restore 0649

        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-team");
            if (i < 0 || i + 1 >= args.Length)
            {
                Debug.LogError("BuildSupportTeam: pass -team <team>");
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(Bake(args[i + 1]) ? 0 : 1);
        }

        // The paths one team's bake reads and writes.
        private sealed class Team
        {
            public string Name, SrcDir, Fbx, AnimDir, PrefabDir, PrefabPath;
            public StatesFile States;
            public BakeSpec Spec;
            public string Log(string message) => $"{Name}: {message}";
        }

        private static bool Bake(string name)
        {
            Team team;
            try
            {
                var specDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../scripts/support_team/teams", name));
                team = new Team
                {
                    Name = name,
                    SrcDir = $"Assets/Authored/{name}/default",
                    Fbx = $"Assets/Authored/{name}/default/{name}_default.fbx",
                    AnimDir = $"Assets/Prefabs/{name}/_bake/default",
                    PrefabDir = $"Assets/Prefabs/{name}/default",
                    PrefabPath = $"Assets/Prefabs/{name}/default/main.prefab",
                    States = JsonUtility.FromJson<StatesFile>(File.ReadAllText(Path.Combine(specDir, "states.json"))),
                    Spec = JsonUtility.FromJson<BakeSpec>(File.ReadAllText(Path.Combine(specDir, "bake.json"))),
                };
            }
            catch (Exception ex) { Debug.LogError($"BuildSupportTeam: no spec for '{name}': {ex.Message}"); return false; }
            try { return Bake(team); }
            catch (Exception ex) { Debug.LogError("BuildSupportTeam failed: " + ex); return false; }
        }

        private static bool Bake(Team team)
        {
            var spec = team.Spec;
            BakeAssets.EnsureFolder(team.AnimDir);
            BakeAssets.EnsureFolder(team.PrefabDir);

            var imp = (ModelImporter)AssetImporter.GetAtPath(team.Fbx);
            if (imp == null) { Debug.LogError(team.Log("FBX not found at " + team.Fbx)); return false; }
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.importAnimation = true;
            // Imported for their names only: each submesh's material names its texture set.
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            imp.useFileScale = true;
            imp.globalScale = 1f;
            AssetDatabase.ImportAsset(team.Fbx, ImportAssetOptions.ForceUpdate);
            // BuildTitan.MeasureHeight reads skinned vertices through BakeMesh, which misses the
            // 0.01 scale Blender writes on these armatures, so the height comes from the head
            // bones instead.
            var measured = HeadHeight(team);
            var scale = spec.canon_height * WorldFactor / measured;
            AssetDatabase.ImportAsset(team.Fbx, ImportAssetOptions.ForceUpdate);
            var idle = ImportTake(team, team.Fbx, scale, loop: true, eventSource: null, travels: false, keepMaterials: true);
            if (idle == null) { Debug.LogError(team.Log("no take in the FBX")); return false; }
            Debug.Log(team.Log($"head height {measured:F3} at scale 1, {HeadHeight(team):F3} baked"));

            var takes = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            var bodyPaths = BodyPaths(team);
            foreach (var state in team.States.states.Where(s => s.name != "Idle"))
            {
                var file = $"{team.Name}_{state.name.ToLowerInvariant()}.fbx";
                var events = spec.events?.FirstOrDefault(e => e.state == state.name)?.clip;
                var travels = !string.IsNullOrEmpty(state.travel);
                var imported = ImportTake(team, $"{team.SrcDir}/{file}", scale, state.loop, events, travels, keepMaterials: false);
                if (imported == null) { Debug.LogError(team.Log($"no take in {file}")); return false; }
                var clip = OnBodyPaths(team, imported, state.name, bodyPaths);
                takes[state.name] = clip;
                Debug.Log(team.Log($"{state.name} <- {file}: {clip.length:0.00}s loop={state.loop} events={AnimationUtility.GetAnimationEvents(clip).Length}"
                          + $" motionNode='{((ModelImporter)AssetImporter.GetAtPath($"{team.SrcDir}/{file}")).motionNodeName}'"));
            }
            var needed = ControllerStates.Concat(spec.line is { Length: > 0 }
                ? spec.line.Skip(1).Select(l => l.state).Concat(team.States.states.Where(s => s.between is { Length: 2 }).Select(s => s.name))
                : new[] { "Kneel" });
            var absent = needed.Where(s => !takes.ContainsKey(s)).ToList();
            if (absent.Count > 0) { Debug.LogError(team.Log("states.json has no " + string.Join(", ", absent))); return false; }

            ForwardTravel(team, takes["Vault"]);
            // A stowed prop's renderer is switched off in the aim takes. Every state writes
            // defaults, so the others show it again.
            if (!string.IsNullOrEmpty(spec.stowed_while_aiming))
            {
                var stowed = spec.stowed_while_aiming;
                var path = bodyPaths.FirstOrDefault(p => p == stowed || p.EndsWith("/" + stowed, StringComparison.Ordinal));
                if (path == null) Debug.LogError(team.Log($"no {stowed} renderer path in the body"));
                else
                    foreach (var state in AimStates)
                    {
                        var clip = takes[state];
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "m_Enabled"),
                            AnimationCurve.Constant(0f, clip.length, 0f));
                        EditorUtility.SetDirty(clip);
                    }
            }
            // The travel the vault hands the mover: its root-motion curve from start to end.
            foreach (var b in AnimationUtility.GetCurveBindings(takes["Vault"]))
                if (b.propertyName.StartsWith("MotionT", StringComparison.Ordinal))
                {
                    var curve = AnimationUtility.GetEditorCurve(takes["Vault"], b);
                    Debug.Log(team.Log($"vault {b.propertyName} {curve.Evaluate(0f):0.000} -> {curve.Evaluate(takes["Vault"].length):0.000}"));
                }

            var ac = BuildController(team, idle, takes);
            if (ac == null) return false;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(team.Fbx));
            inst.name = $"{team.Name}_default";
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = ac;
            // The vault's crossing is its root motion, and the game moves the team over cover only
            // as the clip carries it. Every other take locks its root, so it moves nothing.
            anim.applyRootMotion = true;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var avatar = AvatarBuilder.BuildGenericAvatar(inst, MotionNode);
            avatar.name = $"{team.Name}_default_avatar";
            AssetDatabase.CreateAsset(avatar, $"{team.AnimDir}/{avatar.name}.asset");
            anim.avatar = avatar;

            AssignMaterials(team, inst);

            // The muzzle, under the name skills resolve, sits on the marker so it follows the
            // weapon through every take. A muzzle placed anywhere else would be wrong in every
            // take but one, so a missing marker fails the bake.
            var bones = inst.GetComponentsInChildren<Transform>(true);
            var tip = bones.FirstOrDefault(t => t.name == spec.muzzle_marker);
            if (tip == null)
            {
                Debug.LogError(team.Log($"no {spec.muzzle_marker} marker in the FBX"));
                UnityEngine.Object.DestroyImmediate(inst);
                return false;
            }
            var muzzle = new GameObject("muzzle").transform;
            muzzle.SetParent(tip, false);
            Debug.Log(team.Log($"muzzle under {PathOf(inst.transform, tip)} at {muzzle.position}"));
            var damage = new GameObject("damage").transform;
            damage.SetParent(inst.transform, false);
            damage.localPosition = new Vector3(0f, spec.damage_height, 0f);
            // The element animator spawns its movement dust at a "dust01" child
            // (DustEffectsAnimatorComp) and logs an error at every spawn without one.
            new GameObject("dust01").transform.SetParent(inst.transform, false);

            var renderers = inst.GetComponentsInChildren<Renderer>(true);
            var hull = renderers[0].bounds;
            foreach (var r in renderers) hull.Encapsulate(r.bounds);
            Debug.Log(team.Log($"renderer bounds centre={hull.center} size={hull.size}"));
            foreach (var a in inst.GetComponentsInChildren<Animator>(true))
                if (a != anim) Debug.LogWarning(team.Log($"extra Animator on {a.name}"));

            // Every take's curves must land on the prefab's own bones: the state takes come from
            // other FBXs, so a path that differs there animates nothing.
            var checks = new List<KeyValuePair<string, AnimationClip>> { new("Idle", idle) };
            checks.AddRange(takes);
            foreach (var (state, clip) in checks)
            {
                var bindings = AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform)).ToList();
                var missing = bindings.Where(b => inst.transform.Find(b.path) == null).Select(b => b.path).Distinct().ToList();
                Debug.Log(team.Log($"{state} binds {bindings.Count - bindings.Count(b => missing.Contains(b.path))}/{bindings.Count} transform curves"
                          + (missing.Count > 0 ? $", missing e.g. '{missing[0]}' (prefab has e.g. '{PathOf(inst.transform, inst.GetComponentsInChildren<Transform>(true).First(t => t.name == "Root_M"))}')" : "")));
            }

            PrefabUtility.SaveAsPrefabAsset(inst, team.PrefabPath);
            UnityEngine.Object.DestroyImmediate(inst);
            AssetDatabase.SaveAssets();
            Debug.Log(team.Log("wrote " + team.PrefabPath));
            return true;
        }

        private static AnimatorController BuildController(Team team, AnimationClip idle, Dictionary<string, AnimationClip> takes)
        {
            var ac = AnimatorController.CreateAnimatorControllerAtPath($"{team.AnimDir}/{team.Name}.controller");
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
            // The actor's stance as vanilla's soldier controller reads it (ActorStance: 0 default,
            // 1 deployed, 2 pinned down).
            ac.AddParameter("Stance", AnimatorControllerParameterType.Int);
            var sm = ac.layers[0].stateMachine;

            var idleS = sm.AddState("Idle"); idleS.motion = idle; sm.defaultState = idleS;
            var run = sm.AddState("Run"); run.motion = takes["Run"];
            var aimIn = sm.AddState("AimIn"); aimIn.motion = takes["AimIn"];
            var aimHold = sm.AddState("AimHold"); aimHold.motion = takes["AimHold"];
            var aimOut = sm.AddState("AimOut"); aimOut.motion = Reversed(team, takes["AimIn"], "aimout");
            var fire = sm.AddState("Fire"); fire.motion = takes["Fire"];
            var hit = sm.AddState("Hit"); hit.motion = takes["Hit"];
            var vaultS = sm.AddState("Vault"); vaultS.motion = takes["Vault"];
            var death = sm.AddState("Death"); death.motion = takes["Death"];

            void T(AnimatorState a, AnimatorState b, AnimatorConditionMode m, float v, string p)
            {
                var t = a.AddTransition(b); t.hasExitTime = false; t.duration = 0.2f;
                t.AddCondition(m, v, p);
            }
            void Back(AnimatorState a, AnimatorState b, float exit, float blend)
            {
                var t = a.AddTransition(b); t.hasExitTime = true; t.exitTime = exit; t.duration = blend;
            }
            void Any(AnimatorState s, string trigger, AnimatorState after, float exit, float blend = 0.15f, (AnimatorConditionMode Mode, float Value, string Param)[] also = null)
            {
                var t = sm.AddAnyStateTransition(s); t.hasExitTime = false; t.duration = 0.08f; t.canTransitionToSelf = false;
                t.AddCondition(AnimatorConditionMode.If, 0, trigger);
                foreach (var (mode, value, param) in also ?? Array.Empty<(AnimatorConditionMode, float, string)>()) t.AddCondition(mode, value, param);
                if (after != null) Back(s, after, exit, blend);
            }

            T(idleS, run, AnimatorConditionMode.Greater, 0.1f, "Speed");
            T(run, idleS, AnimatorConditionMode.Less, 0.1f, "Speed");
            // Aiming raises the weapon (AimIn) into the held aim, a shot plays Fire and comes back
            // to the hold, and lowering it plays AimIn backwards (AimOut). build_team_states.py
            // cuts AimIn, AimHold and Fire so they meet on one pose, so that hand-over has no blend.
            var raised = aimIn.AddTransition(aimHold); raised.hasExitTime = true; raised.exitTime = 1f; raised.duration = 0f;
            T(aimIn, aimOut, AnimatorConditionMode.IfNot, 0, "Aiming");
            T(aimHold, aimOut, AnimatorConditionMode.IfNot, 0, "Aiming");
            T(aimHold, run, AnimatorConditionMode.Greater, 0.1f, "Speed");
            // The conditions under which the team stands at rest in Idle, the only stance its Hit take
            // starts from. None for a team without a line, which is always at rest.
            (AnimatorConditionMode Mode, float Value, string Param)[] atRest = null;
            if (team.Spec.line is { Length: > 0 })
            {
                if (!BuildLine(team, ac, sm, idleS, run, aimIn, aimOut, takes, out atRest)) return null;
            }
            else
            {
                T(idleS, aimIn, AnimatorConditionMode.If, 0, "Aiming");
                // Deployed (or pinned down) the team holds its Kneel take, and the cross-fade is the
                // move into it and back out. Kneel starts on AimIn's first pose, so aiming from it
                // moves only what AimIn moves, and lowering the aim settles back into whichever stance
                // the team is in.
                void Stance(AnimatorState a, AnimatorState b, AnimatorConditionMode m)
                {
                    var t = a.AddTransition(b); t.hasExitTime = false; t.duration = 0.45f;
                    t.AddCondition(m, 0.5f, "Stance");
                }
                var kneelS = sm.AddState("Kneel"); kneelS.motion = takes["Kneel"];
                Stance(idleS, kneelS, AnimatorConditionMode.Greater);
                Stance(kneelS, idleS, AnimatorConditionMode.Less);
                T(kneelS, aimIn, AnimatorConditionMode.If, 0, "Aiming");
                T(kneelS, run, AnimatorConditionMode.Greater, 0.1f, "Speed");
                foreach (var (after, mode) in new[] { (kneelS, AnimatorConditionMode.Greater), (idleS, AnimatorConditionMode.Less) })
                {
                    var t = aimOut.AddTransition(after); t.hasExitTime = true; t.exitTime = 0.95f; t.duration = 0.15f;
                    t.AddCondition(mode, 0.5f, "Stance");
                }
            }
            Any(fire, "Shoot_Single", aimHold, team.Spec.fire_exit, team.Spec.fire_blend);
            Any(fire, "Special_Attack_1", aimHold, team.Spec.fire_exit, team.Spec.fire_blend);
            Any(hit, "Hit", idleS, 0.95f, also: atRest);
            Any(vaultS, "Vault", run, 0.95f);
            Any(death, "OnDeath", null, 0f);
            return ac;
        }

        // The deployed line: still states L0 (Idle) to Ln, each next pair joined by a bridge and
        // its reverse. The team wants the last state whose condition holds (L0 when none does) and
        // goes along the line towards it. A still state goes into a bridge at any point of its loop,
        // which a short blend covers. A bridge starts on the last frame of the state before it and
        // ends on the first frame of the next, so at its end it hands over with no blend: on along
        // the line while the team still wants further, into the still state it reached, or back the
        // way it came.
        private static bool BuildLine(Team team, AnimatorController ac, AnimatorStateMachine sm, AnimatorState idleS,
            AnimatorState run, AnimatorState aimIn, AnimatorState aimOut, Dictionary<string, AnimationClip> takes, out (AnimatorConditionMode Mode, float Value, string Param)[] atRest)
        {
            atRest = null;
            var line = team.Spec.line;
            if (line[0].state != "Idle") { Debug.LogError(team.Log("the line must start at Idle")); return false; }
            var n = line.Length;
            var stills = new AnimatorState[n];
            stills[0] = idleS;
            for (var k = 1; k < n; k++) { stills[k] = sm.AddState(line[k].state); stills[k].motion = takes[line[k].state]; }
            var ahead = new AnimatorState[n - 1];
            var back = new AnimatorState[n - 1];
            for (var k = 0; k < n - 1; k++)
            {
                var row = team.States.states.FirstOrDefault(s => s.between is { Length: 2 } && s.between[0] == line[k].state && s.between[1] == line[k + 1].state);
                if (row == null) { Debug.LogError(team.Log($"no state between {line[k].state} and {line[k + 1].state}")); return false; }
                ahead[k] = sm.AddState(row.name); ahead[k].motion = takes[row.name];
                var reverse = "Un" + row.name.ToLowerInvariant();
                back[k] = sm.AddState(reverse); back[k].motion = Reversed(team, takes[row.name], reverse.ToLowerInvariant());
            }
            foreach (var step in line.Skip(1))
                if (!ac.parameters.Any(p => p.name == step.param))
                    ac.AddParameter(step.param, step.mode == "if" ? AnimatorControllerParameterType.Bool : AnimatorControllerParameterType.Float);

            (AnimatorConditionMode Mode, float Value, string Param) Asks(int k, bool holds)
            {
                var step = line[k];
                return step.mode == "if"
                    ? (holds ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, step.param)
                    : (holds ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, step.value, step.param);
            }
            // Wanting state k or further: any of their conditions, one transition each.
            IEnumerable<(AnimatorConditionMode, float, string)[]> AtLeast(int k) =>
                Enumerable.Range(k, n - k).Select(j => new[] { Asks(j, true) });
            // Wanting state k or nearer: none of the further conditions.
            (AnimatorConditionMode, float, string)[] AtMost(int k) =>
                Enumerable.Range(k + 1, n - k - 1).Reverse().Select(j => Asks(j, false)).ToArray();
            (AnimatorConditionMode, float, string)[] Exactly(int k) =>
                AtMost(k).Concat(k > 0 ? new[] { Asks(k, true) } : Array.Empty<(AnimatorConditionMode, float, string)>()).ToArray();

            void Go(AnimatorState src, AnimatorState dst, bool atEnd, (AnimatorConditionMode Mode, float Value, string Param)[] conditions)
            {
                var t = src.AddTransition(dst);
                t.hasExitTime = atEnd; t.duration = atEnd ? 0f : 0.15f;
                if (atEnd) t.exitTime = 1f;
                foreach (var (mode, value, param) in conditions) t.AddCondition(mode, value, param);
            }
            for (var k = 0; k < n; k++)
            {
                if (k < n - 1) foreach (var c in AtLeast(k + 1)) Go(stills[k], ahead[k], false, c);
                if (k > 0) Go(stills[k], back[k - 1], false, AtMost(k - 1));
            }
            for (var k = 0; k < n - 1; k++)
            {
                // Forward, ending on state k + 1.
                if (k + 1 < n - 1) foreach (var c in AtLeast(k + 2)) Go(ahead[k], ahead[k + 1], true, c);
                Go(ahead[k], stills[k + 1], true, Exactly(k + 1));
                Go(ahead[k], back[k], true, AtMost(k));
                // Back, ending on state k.
                if (k > 0) Go(back[k], back[k - 1], true, AtMost(k - 1));
                Go(back[k], stills[k], true, Exactly(k));
                foreach (var c in AtLeast(k + 1)) Go(back[k], ahead[k], true, c);
            }

            void T(AnimatorState src, AnimatorState dst, AnimatorConditionMode m, float v, string p)
            {
                var t = src.AddTransition(dst); t.hasExitTime = false; t.duration = 0.2f;
                t.AddCondition(m, v, p);
            }
            // Moving off never waits for a bridge: the game moves the team at once.
            foreach (var st in stills.Skip(1).Concat(ahead).Concat(back))
                T(st, run, AnimatorConditionMode.Greater, 0.1f, "Speed");
            // AimOut ends where AimIn starts, so it hands back to that still state or goes along the
            // line from there.
            var aimAt = string.IsNullOrEmpty(team.Spec.aim_from) ? n - 1 : Array.FindIndex(line, l => l.state == team.Spec.aim_from);
            if (aimAt < 0) { Debug.LogError(team.Log($"aim_from {team.Spec.aim_from} is not on the line")); return false; }
            // AimIn starts on that state's pose, so the team aims only from there. Aiming elsewhere on
            // the line waits until the line has brought the team to it.
            T(stills[aimAt], aimIn, AnimatorConditionMode.If, 0, "Aiming");
            // The Hit take starts from Idle's carry pose, so further along the line a hit plays nothing.
            // Each still state there takes the trigger itself, which a trigger left set would
            // otherwise keep until the team is back in Idle and play late.
            atRest = AtMost(0);
            foreach (var still in stills.Skip(1))
            {
                var t = still.AddTransition(still); t.hasExitTime = false; t.duration = 0.1f;
                t.AddCondition(AnimatorConditionMode.If, 0, "Hit");
            }
            void Out(AnimatorState after, (AnimatorConditionMode Mode, float Value, string Param)[] conditions)
            {
                var t = aimOut.AddTransition(after); t.hasExitTime = true; t.exitTime = 0.95f; t.duration = 0.15f;
                foreach (var (mode, value, param) in conditions) t.AddCondition(mode, value, param);
            }
            Out(stills[aimAt], Exactly(aimAt));
            if (aimAt > 0) Out(back[aimAt - 1], AtMost(aimAt - 1));
            if (aimAt < n - 1) foreach (var c in AtLeast(aimAt + 1)) Out(ahead[aimAt], c);
            return true;
        }

        // The highest head bone over the FBX root, scaled up by the head's share of the crew's
        // full height.
        private static float HeadHeight(Team team)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(team.Fbx));
            try
            {
                var heads = inst.GetComponentsInChildren<Transform>(true).Where(t => t.name == HeadBone).ToList();
                var top = heads.Count > 0 ? heads.Max(t => inst.transform.InverseTransformPoint(t.position).y) : 0f;
                return top * (team.Spec.canon_height / team.Spec.head_bone_height);
            }
            finally { UnityEngine.Object.DestroyImmediate(inst); }
        }

        // Every transform path in the body FBX, the paths the prefab's Animator resolves.
        private static HashSet<string> BodyPaths(Team team)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(team.Fbx));
            try { return new HashSet<string>(inst.GetComponentsInChildren<Transform>(true).Select(t => PathOf(inst.transform, t)), StringComparer.Ordinal); }
            finally { UnityEngine.Object.DestroyImmediate(inst); }
        }

        // A state FBX carries no meshes, so Unity collapses its single root node, while the body
        // FBX keeps it ("<team>/<member>/..." against "<member>/..."). The take is copied into a
        // clip of its own with every path moved onto the body's, keeping its settings, its events
        // and its root-motion curves (which are on the Animator, path "").
        private static AnimationClip OnBodyPaths(Team team, AnimationClip take, string state, HashSet<string> bodyPaths)
        {
            var bindings = AnimationUtility.GetCurveBindings(take);
            var prefix = "";
            var probe = bindings.Select(b => b.path).FirstOrDefault(p => p.Length > 0 && !bodyPaths.Contains(p));
            if (probe != null)
            {
                var match = bodyPaths.FirstOrDefault(p => p.EndsWith("/" + probe, StringComparison.Ordinal));
                if (match == null) { Debug.LogError(team.Log($"{state} path '{probe}' is nowhere in the body")); return take; }
                prefix = match.Substring(0, match.Length - probe.Length);
            }
            var clip = new AnimationClip { name = $"{team.Name}_{state.ToLowerInvariant()}", frameRate = take.frameRate };
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
            var path = $"{team.AnimDir}/{clip.name}.anim";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // Unity reads a Generic clip's root motion (MotionT) in the motion node's own frame, and
        // that node keeps the FBX's Z-up turn: its +Z is the world's down and its +Y the team's
        // forward. Stepping the Animator showed the vault's crossing going straight down, which
        // the game discards when it puts the element on the ground (ElementAnimator.OnAnimatorMove
        // keeps the root position's X and Z and takes the height from the map). So the travel
        // moves onto MotionT.y, the vertical axis (MotionT.z) is cleared, and the bake flags
        // follow: the node's Y (the forward travel) is kept as motion and its XZ plane (sideways
        // and vertical) is baked into the pose.
        private static void ForwardTravel(Team team, AnimationClip clip)
        {
            EditorCurveBinding? Find(string prop) =>
                AnimationUtility.GetCurveBindings(clip).Cast<EditorCurveBinding?>().FirstOrDefault(b => b.Value.propertyName == prop);
            var y = Find("MotionT.y");
            var z = Find("MotionT.z");
            if (y == null || z == null) { Debug.LogError(team.Log("vault has no MotionT curves")); return; }
            var travel = AnimationUtility.GetEditorCurve(clip, z.Value);
            AnimationUtility.SetEditorCurve(clip, y.Value, travel);
            AnimationUtility.SetEditorCurve(clip, z.Value, AnimationCurve.Constant(0f, clip.length, 0f));
            // A donor take's sideways sway would carry the whole team off its path, so the
            // crossing is straight ahead.
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
        private static AnimationClip Reversed(Team team, AnimationClip source, string state)
        {
            var clip = new AnimationClip { name = $"{team.Name}_{state}", frameRate = source.frameRate };
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
            var path = $"{team.AnimDir}/{clip.name}.anim";
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

        // Imports one FBX's take at the body's scale with "motion" as its root node. A take that
        // travels keeps the motion node's ground travel as root motion, and every other take bakes
        // it into the pose (it stands still there anyway).
        private static AnimationClip ImportTake(Team team, string path, float scale, bool loop, string eventSource, bool travels, bool keepMaterials)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (imp == null) { Debug.LogError(team.Log("FBX not found at " + path)); return null; }
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.importAnimation = true;
            if (!keepMaterials) imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.useFileScale = true;
            imp.globalScale = scale;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            // The root node is named by its path as the importer lists it.
            var motionPath = imp.transformPaths.FirstOrDefault(p => p == MotionNode || p.EndsWith("/" + MotionNode, StringComparison.Ordinal));
            if (motionPath == null) Debug.LogWarning(team.Log($"no {MotionNode} node in {path}"));
            imp.motionNodeName = motionPath ?? "";

            var vanilla = eventSource != null ? AssetDatabase.LoadAssetAtPath<AnimationClip>(eventSource) : null;
            if (eventSource != null && vanilla == null) Debug.LogWarning(team.Log("no vanilla clip at " + eventSource));
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
        // The sets go on the Doll shaders, because the Menace/character stub draws them white in
        // game.
        private static void AssignMaterials(Team team, GameObject inst)
        {
            var built = new Dictionary<string, Material>(StringComparer.Ordinal);
            var crew = new Regex($@"^(c_(?:{string.Join("|", team.States.crew.Select(Regex.Escape))})_slg)_(\w+?)(_?uber)?$");

            // Every material is built before any is assigned. Building one imports its textures,
            // and the FBX's own materials search for those textures by name, so an import can
            // reimport the FBX and revert this instance's earlier assignments.
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    var set = TextureSet(team, crew, Regex.Replace(m != null ? m.name : "", @"\.\d+$", ""));
                    if (set != null && !built.ContainsKey(set))
                        built[set] = BuildMaterial(team, set);
                }

            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                {
                    var name = Regex.Replace(mats[i] != null ? mats[i].name : "", @"\.\d+$", "");
                    var set = TextureSet(team, crew, name);
                    if (set != null)
                    {
                        if (!built.TryGetValue(set, out var mat))
                            built[set] = mat = BuildMaterial(team, set);
                        mats[i] = mat;
                    }
                    else Debug.LogWarning(team.Log($"no texture set for material '{name}' on {r.name}"));
                    Debug.Log(team.Log($"{r.name}[{i}] {name} -> {(mats[i] != null ? mats[i].name : "none")}"));
                }
                r.sharedMaterials = mats;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                r.renderingLayerMask = 1;
            }
        }

        // GFL2 names a crew material c_<member>_slg_<part>. The eye layers' parts (eyenew,
        // eyenewadd, eyenewmul) and the lash, face, fringe and hair parts share texture sets the
        // rip ships under other names.
        private static string TextureSet(Team team, Regex crew, string material)
        {
            var prop = team.Spec.prop_texture_sets?.FirstOrDefault(p => material.StartsWith(p, StringComparison.Ordinal));
            if (prop != null) return prop;
            var m = crew.Match(material);
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

        // The eye layers take the Doll eye shaders and everything else the Doll toon shader.
        private static Material BuildMaterial(Team team, string set)
        {
            var texDir = $"{team.SrcDir}/textures";
            var name = $"{team.Name}_{set}";
            bool Is(string part) => set.EndsWith(part, StringComparison.Ordinal);

            Material mat;
            if (Is("_eyeadd") || Is("_eyemul"))
            {
                var blend = BakeAssets.LoadTex(texDir, set.Substring(0, set.Length - "_eyeadd".Length) + "_eyeblend", new[] { "" }, sRGB: true, isNormal: false);
                mat = Gfl2Materials.Eye(Is("_eyeadd") ? "Womenace/DollEyeHighlight" : "Womenace/DollEyeShadow", name, blend);
            }
            else if (Is("_eye"))
                mat = Gfl2Materials.Eye("Womenace/DollEye", name, BakeAssets.LoadTex(texDir, set, new[] { "_d" }, sRGB: true, isNormal: false));
            else
            {
                var ramp = team.Spec.ramps?.FirstOrDefault(r => Is(r.part))?.ramp ?? team.Spec.default_ramp;
                mat = Gfl2Materials.Doll("Womenace/DollToon", name, texDir, set, ramp, Is("_face") ? Gfl2Materials.SharedFaceSdf : null);
            }
            var path = $"{team.AnimDir}/{mat.name}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
