using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WOMENACE.Editor
{
    // Imports the Voymastina mech FBX (model + 44 GFL2 combat takes), builds a custom
    // AnimatorController wiring the clips to MENACE's driver contract (Speed,
    // Aiming, Hit), and bakes the erwin + default skin prefabs. Run via batchmode:
    //   -executeMethod WOMENACE.Editor.BuildVoymastinaMech.Build
    public static class BuildVoymastinaMech
    {
        private const string SrcDir = "Assets/Authored/voymastina_mech";

        private const string AnimDir = "Assets/Prefabs/voymastina_mech/_bake";
        private const string ControllerPath = AnimDir + "/voymastina_mech.controller";

        private const string DefaultFbx = SrcDir + "/voymastina_default.fbx";
        private const string ErwinFbx = SrcDir + "/voymastina_erwin.fbx";

        private const string DefaultPrefabPath = "Assets/Prefabs/voymastina_mech/default/main.prefab";
        private const string ErwinPrefabPath = "Assets/Prefabs/voymastina_mech/erwin/main.prefab";

        private const float ModelScale = 125f;
        private const float FbxFileScale = 0.01f;

        // The doll shading. Ramps are per character with a global set in shared;
        // see the doll-shading skill for the split and why it follows the game's.
        private const string VoyRamps = "Assets/Authored/voymastina/ramps";
        private const string SharedRamps = "Assets/Authored/shared/ramps";
        private const string SharedFaceSdf = "Assets/Authored/shared/face_sdf.png";
        private const string FaceSdfSidecars = SrcDir + "/face_sdf";

        private const string ThrusterVfxDir = "Assets/Imported/vfx_jetpack_walker_thruster_animated";
        private const string ThrusterVfxPrefab = ThrusterVfxDir + "/GameObject/vfx_jetpack_walker_thruster_animated.prefab";
        private const string ThrusterLoopClip = ThrusterVfxDir + "/AnimationClip/ac_vfx_jetpack_thruster_loop.anim";
        private const string ThrusterLoopCtrlPath = AnimDir + "/voymastina_thruster_loop.controller";

        private const float ThrusterVfxScale = 1f;
        private static readonly Vector3 ThrusterVfxEuler = new Vector3(90f, 0f, 0f);

        public static void Build()
        {
            bool ok = BuildController();     // the shared controller + clips, from the erwin FBX
            if (ok) ok = BuildErwin();       // the erwin (SSR0101) skin prefab
            if (ok) ok = BuildDefault();     // the default (SSR01) skin prefab
            EditorApplication.Exit(ok ? 0 : 1);
        }

        // One-off: log world positions of candidate muzzle-anchor bones (and the AK-15 gun mesh
        // bounds) so the projectile-origin transforms can be parented onto real bones rather than
        // guessed positions. Run via -executeMethod WOMENACE.Editor.BuildVoymastinaMech.DumpMuzzleCandidates
        public static void DumpMuzzleCandidates()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ErwinFbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            var all = inst.GetComponentsInChildren<Transform>(true);
            string[] names = {
                "lgp_muzzle", "Gun", "Char_Wrist_R", "Char_Wrist_L", "Weapon_Socket",
                "Char_Shoulder_R", "Char_Shoulder_L", "Shoulder_R", "Shoulder_L",
                "VM_BackJet_up_Node", "VM_BackJet_down_Node", "Char_Spine2_M", "Char_Chest_M",
                "VM_WL01_Node", "VM_WL02_Node",
            };
            foreach (var n in names)
            {
                var t = all.FirstOrDefault(x => x.name == n);
                Debug.Log(t != null
                    ? $"BONE {n}: worldPos={t.position} parent={(t.parent ? t.parent.name : "-")}"
                    : $"BONE {n}: NOT FOUND");
            }
            var gunMesh = AssetDatabase.LoadAllAssetsAtPath(SrcDir + "/voymastina_weapon.obj").OfType<Mesh>().FirstOrDefault();
            if (gunMesh != null)
                Debug.Log($"GUN MESH bounds: center={gunMesh.bounds.center} size={gunMesh.bounds.size} extents={gunMesh.bounds.extents}");
            else Debug.Log("GUN MESH: not found");
            UnityEngine.Object.DestroyImmediate(inst);
            EditorApplication.Exit(0);
        }

        // One-off: enumerate every mesh in the FBX (flagging unskinned rigid meshes) and
        // every renderer GameObject with its parent, to see what actually imported.
        public static void DumpFbx()
        {
            var meshes = AssetDatabase.LoadAllAssetsAtPath(ErwinFbx).OfType<Mesh>().ToList();
            Debug.Log($"FBX meshes ({meshes.Count}):");
            foreach (var m in meshes)
            {
                // UV channel census: Unity's uv2 is the TEXCOORD1 semantic, uv3 is
                // TEXCOORD2. A ripped game mesh may carry the real TEXCOORD1 the
                // hair specular needs, which a PMX source never can.
                var u1 = m.uv2; var u2 = m.uv3;
                string uvInfo = $"uv1={(u1 != null ? u1.Length : 0)} uv2={(u2 != null ? u2.Length : 0)}";
                if (u1 != null && u1.Length > 0)
                {
                    Vector2 lo = u1[0], hi = u1[0];
                    foreach (var v in u1) { lo = Vector2.Min(lo, v); hi = Vector2.Max(hi, v); }
                    uvInfo += $" uv1Range=({lo.x:F3},{lo.y:F3})..({hi.x:F3},{hi.y:F3})";
                }
                Debug.Log($"  mesh {m.name} verts={m.vertexCount} sub={m.subMeshCount} "
                    + $"bindposes={m.bindposes.Length} {uvInfo}");
                for (int s = 0; s < m.subMeshCount; s++)
                    Debug.Log($"    submesh {s}: {m.GetSubMesh(s).indexCount} indices");
            }
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ErwinFbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
            // weight content: is the skin real (blended, varied bone indices) or degenerate
            // (every vertex pinned to one bone = effectively rigid)?
            foreach (var m in meshes)
            {
                if (!m.name.Contains("Mech_slg_G1") && !m.name.Contains("Mech_slg_G2")
                    && !m.name.Equals("c_VoymastinaSSR01_Mech_slg_lod0") && !m.name.Contains("body_lod0")) continue;
                var bw = m.boneWeights;
                if (bw == null || bw.Length == 0) { Debug.Log($"WEIGHTS {m.name}: NONE"); continue; }
                var idx = new HashSet<int>(); int blended = 0;
                foreach (var w in bw)
                {
                    idx.Add(w.boneIndex0);
                    if (w.weight1 > 0.001f) blended++;
                }
                var sample = bw[0];
                Debug.Log($"WEIGHTS {m.name}: verts={bw.Length} distinctIndex0={idx.Count} ({string.Join(",", idx.OrderBy(x => x))}) blended(>1 infl)={blended} sample0=[i{sample.boneIndex0}:{sample.weight0:F2} i{sample.boneIndex1}:{sample.weight1:F2}]");
            }
            Debug.Log("FBX renderer hierarchy:");
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            {
                var smr = t.GetComponent<SkinnedMeshRenderer>();
                var mf = t.GetComponent<MeshFilter>();
                var mr = t.GetComponent<MeshRenderer>();
                if (smr == null && mr == null) continue;
                var meshName = smr?.sharedMesh?.name ?? mf?.sharedMesh?.name ?? "-";
                Debug.Log($"  GO={t.name} parent={(t.parent ? t.parent.name : "-")} type={(smr ? "SMR bones=" + (smr.bones?.Length ?? 0) : "MeshRenderer")} mesh={meshName}");
            }
            UnityEngine.Object.DestroyImmediate(inst);
            EditorApplication.Exit(0);
        }

        // Builds the shared AnimatorController and the in-place locomotion clips it references, from
        // the erwin FBX's GFL2 takes. Both skins reuse this, so it runs first and is independent of
        // either prefab. The locomotion hover is baked into the shared clips here.
        private static bool BuildController()
        {
            try
            {
                EnsureFolder(AnimDir);

                // Import: generic rig, animations on, skip GFL2 materials (they
                // would render magenta; re-authored later).
                var imp = (ModelImporter)AssetImporter.GetAtPath(ErwinFbx);
                if (imp == null) { Debug.LogError("FBX not found at " + ErwinFbx); return false; }
                imp.animationType = ModelImporterAnimationType.Generic;
                imp.importAnimation = true;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                // Bake the up-scale into the mesh + skeleton bind pose at import (vanilla vehicles are
                // authored at true world size with a scale-1 root). The prefab root then stays at scale
                // 1, so the squad-bay viewer's root-scale normalisation is a no-op and the model shows
                // full size there, exactly as in-mission. Clips import from this same FBX, so they scale
                // in lockstep with the skeleton.
                imp.useFileScale = false;
                imp.globalScale = ModelScale * FbxFileScale;
                AssetDatabase.ImportAsset(ErwinFbx, ImportAssetOptions.ForceUpdate);

                // Loop the cyclic clips (Idle/Walk/Run) so they don't play once and freeze.
                var defs = imp.defaultClipAnimations;
                foreach (var c in defs)
                    if (c.name.EndsWith("slg_Idle", StringComparison.OrdinalIgnoreCase)
                        || c.name.EndsWith("slg_Walk", StringComparison.OrdinalIgnoreCase)
                        || c.name.EndsWith("slg_Run", StringComparison.OrdinalIgnoreCase))
                        c.loopTime = true;
                imp.clipAnimations = defs;
                AssetDatabase.ImportAsset(ErwinFbx, ImportAssetOptions.ForceUpdate);

                var clips = AssetDatabase.LoadAllAssetsAtPath(ErwinFbx)
                    .OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview", StringComparison.Ordinal))
                    .ToList();
                Debug.Log("Voymastina FBX clips: " + clips.Count);
                foreach (var c in clips.Take(60)) Debug.Log("  clip: " + c.name);

                AnimationClip Find(string suffix) =>
                    clips.FirstOrDefault(c => c.name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

                var idleC = Find("slg_Idle"); var walkC = Find("slg_Walk");
                var runC = Find("slg_Run"); var fireC = Find("slg_Fire");
                var normalSkillC = Find("slg_NormalSkill"); var ultraSkillC = Find("slg_UltraSkill");
                var ultraHitC = Find("slg_UltraSkill2_Hit");
                var hitC = Find("slg_KnockBack");
                var vaultC = Find("slg_MoveOverCover"); var stunC = Find("slg_Stun");
                var dieC = Find("slg_Die"); var dieFlyC = Find("slg_DieFly");
                var turnLC = Find("slg_TurnL_90"); var turnRC = Find("slg_TurnR_90");
                Debug.Log($"mapped: idle={idleC?.name} fire={fireC?.name} hit={hitC?.name} vault={vaultC?.name} stun={stunC?.name} die={dieC?.name} turnL={turnLC?.name} turnR={turnRC?.name}");
                if (idleC == null) { Debug.LogError("no idle clip"); return false; }

                // GFL2 Walk/Run carry forward travel keyed on a skeleton bone (not the
                // avatar root-motion node, so applyRootMotion=false can't suppress it).
                // The clip would slide her past the tile MENACE moves her to, then snap
                // back (rubber-band). Make in-place copies with planar (XZ) translation
                // stripped so the game owns movement and the clip only animates limbs.
                var walkInPlace = MakeInPlace(walkC, "voymastina_walk_inplace", true);
                var runInPlace = MakeInPlace(runC, "voymastina_run_inplace", true);

                // GFL2 locomotion is jump-up (RunStart) -> hover (Run, looping) -> land
                // (RunStop). Strip only horizontal travel (MENACE owns the tile move); the
                // vertical jump/land stays. Start/Stop play once (no loop).
                var runStartC = Find("slg_RunStart"); var runStopC = Find("slg_RunStop");
                var startInPlace = MakeInPlace(runStartC, "voymastina_runstart_inplace", false);
                var stopInPlace = MakeInPlace(runStopC, "voymastina_runstop_inplace", false);
                // The UltraSkill (drill) clip carries forward dash travel on the root bone; played as-is
                // the model slides several tiles then snaps back to the unit's real (adjacent) tile.
                // Strip the planar travel so the drill animates in place (no rubber-band). A true
                // forward dash needs the unit to actually relocate (gap-closer follow-up).
                var ultraInPlace = MakeInPlace(ultraSkillC, "voymastina_ultraskill_inplace", false);
                var ultraHitInPlace = MakeInPlace(ultraHitC, "voymastina_ultrahit_inplace", false);
                Debug.Log($"locomotion clips: start={runStartC?.name} run={runC?.name} stop={runStopC?.name}");
                void DumpY(AnimationClip clip, string tag)
                {
                    if (clip == null) return;
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (b.type != typeof(Transform) || b.propertyName != "m_LocalPosition.y") continue;
                        var c = AnimationUtility.GetEditorCurve(clip, b);
                        if (c == null || c.length == 0) continue;
                        float mn = float.MaxValue, mx = float.MinValue;
                        foreach (var k in c.keys) { mn = Mathf.Min(mn, k.value); mx = Mathf.Max(mx, k.value); }
                        if (mx - mn > 0.002f) Debug.Log($"  Ycurve[{tag}] {b.path} range={mx - mn:F4} ({mn:F4}..{mx:F4})");
                    }
                }
                DumpY(runStartC, "start"); DumpY(runC, "run"); DumpY(runStopC, "stop");

                var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
                ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
                ac.AddParameter("Aiming", AnimatorControllerParameterType.Bool);
                ac.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
                ac.AddParameter("Vault", AnimatorControllerParameterType.Trigger);
                ac.AddParameter("Overloaded", AnimatorControllerParameterType.Bool);
                ac.AddParameter("Rotation", AnimatorControllerParameterType.Float);
                ac.AddParameter("Shoot_Single", AnimatorControllerParameterType.Trigger);
                // Skill animations: the driver pulses these for skills with AnimationType
                // SpecialAttack1/2. Names are a best guess at the driver's contract (the construct
                // soldier ships no special-attack params, so they can't be confirmed offline) — if a
                // clip doesn't play in-game, the trigger name is what to retune.
                ac.AddParameter("SpecialAttack1", AnimatorControllerParameterType.Trigger);
                ac.AddParameter("SpecialAttack2", AnimatorControllerParameterType.Trigger);
                var sm = ac.layers[0].stateMachine;

                var idle = sm.AddState("Idle"); idle.motion = idleC; sm.defaultState = idle;
                var runStart = sm.AddState("RunStart"); runStart.motion = startInPlace ?? runInPlace ?? idleC;
                var run = sm.AddState("Run"); run.motion = runInPlace ?? walkInPlace ?? idleC;
                var runStop = sm.AddState("RunStop"); runStop.motion = stopInPlace ?? idleC;
                // Aim is a LOOPING ready pose (idle), not the one-shot Fire clip. Skills carry
                // AimingType=Aiming, so merely *selecting* one sets Aiming=true; a one-shot fire clip
                // here would play once and freeze on its last frame (looks like firing when idle).
                // The actual shot plays the Fire clip via the Shoot_Single trigger -> Fire state.
                var aim = sm.AddState("Aim"); aim.motion = idleC;
                var fire = sm.AddState("Fire"); fire.motion = fireC ?? idleC;
                var normalSkill = sm.AddState("NormalSkill"); normalSkill.motion = normalSkillC ?? fireC ?? idleC;
                var ultraSkill = sm.AddState("UltraSkill"); ultraSkill.motion = ultraInPlace ?? ultraSkillC ?? fireC ?? idleC;
                var ultraHit = sm.AddState("UltraSkillHit"); ultraHit.motion = ultraHitInPlace ?? ultraHitC ?? idleC;
                var hit = sm.AddState("Hit"); hit.motion = hitC ?? idleC;
                var overload = sm.AddState("Overload"); overload.motion = stunC ?? idleC;
                // Death state driven by DeathBehaviour=DeathAnimations (driver plays it on death).
                var death = sm.AddState("Death"); death.motion = dieC ?? idleC;
                // Turn-in-place, driven by the Rotation parameter (Normalized angle mapping).
                var turnL = sm.AddState("TurnL"); turnL.motion = turnLC ?? idleC;
                var turnR = sm.AddState("TurnR"); turnR.motion = turnRC ?? idleC;

                // condition transition (no exit time)
                void T(AnimatorState a, AnimatorState b, AnimatorConditionMode m, float v, string p)
                {
                    var t = a.AddTransition(b); t.hasExitTime = false; t.duration = 0.08f;
                    t.AddCondition(m, v, p);
                }
                // play-through transition (fires after a fraction of the clip, no condition)
                void TExit(AnimatorState a, AnimatorState b, float exit)
                {
                    var t = a.AddTransition(b); t.hasExitTime = true; t.exitTime = exit; t.duration = 0.1f;
                }

                // jump up -> hover -> land
                T(idle, runStart, AnimatorConditionMode.Greater, 0.1f, "Speed");
                TExit(runStart, run, 0.7f);
                T(runStart, runStop, AnimatorConditionMode.Less, 0.1f, "Speed"); // stopped mid-jump
                T(run, runStop, AnimatorConditionMode.Less, 0.1f, "Speed");
                TExit(runStop, idle, 0.8f);

                T(idle, aim, AnimatorConditionMode.If, 0, "Aiming");
                T(aim, idle, AnimatorConditionMode.IfNot, 0, "Aiming");
                // The shot itself: the driver pulses Shoot_Single when a shot fires. Play the Fire
                // clip from any state, then fall back to the aim/ready pose (which drops to idle once
                // Aiming clears). This is what actually animates the gun firing.
                var toFire = sm.AddAnyStateTransition(fire); toFire.hasExitTime = false; toFire.duration = 0.05f;
                toFire.AddCondition(AnimatorConditionMode.If, 0, "Shoot_Single");
                var fireOut = fire.AddTransition(aim); fireOut.hasExitTime = true; fireOut.exitTime = 0.9f; fireOut.duration = 0.1f;
                // Rocket (NormalSkill clip) and drill (UltraSkill clip), each fired by its skill trigger.
                var toNormal = sm.AddAnyStateTransition(normalSkill); toNormal.hasExitTime = false; toNormal.duration = 0.05f;
                toNormal.AddCondition(AnimatorConditionMode.If, 0, "SpecialAttack1");
                var normalOut = normalSkill.AddTransition(aim); normalOut.hasExitTime = true; normalOut.exitTime = 0.95f; normalOut.duration = 0.1f;
                var toUltra = sm.AddAnyStateTransition(ultraSkill); toUltra.hasExitTime = false; toUltra.duration = 0.05f;
                toUltra.AddCondition(AnimatorConditionMode.If, 0, "SpecialAttack2");
                // Drill plays its windup/drill clip, then the impact clip (UltraSkill2_Hit) at the end,
                // then settles back to the aim/ready pose.
                var ultraOut = ultraSkill.AddTransition(ultraHit); ultraOut.hasExitTime = true; ultraOut.exitTime = 0.92f; ultraOut.duration = 0.05f;
                var ultraHitOut = ultraHit.AddTransition(aim); ultraHitOut.hasExitTime = true; ultraHitOut.exitTime = 0.9f; ultraHitOut.duration = 0.1f;
                var toHit = sm.AddAnyStateTransition(hit); toHit.hasExitTime = false; toHit.duration = 0.05f;
                toHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
                var hitOut = hit.AddTransition(idle); hitOut.hasExitTime = true; hitOut.exitTime = 0.9f; hitOut.duration = 0.1f;

                // No Vault state: the mech flies (its locomotion "run" is a hover), so it never
                // traverses cover on foot. The driver still pulses the Vault trigger when a move
                // crosses a cover tile, but with no state consuming it the mech stays in its hover
                // rather than cutting to the MoveOverCover clip, which yanked it out of locomotion
                // and stranded the animator in the run pose afterwards. The Vault parameter is kept
                // so the driver's SetTrigger finds it (no missing-parameter warning).
                T(idle, overload, AnimatorConditionMode.If, 0, "Overloaded");
                T(overload, idle, AnimatorConditionMode.IfNot, 0, "Overloaded");

                // Turn-in-place when the driver sets a large Rotation, return on completion.
                T(idle, turnR, AnimatorConditionMode.Greater, 0.5f, "Rotation");
                T(idle, turnL, AnimatorConditionMode.Less, -0.5f, "Rotation");
                TExit(turnR, idle, 0.8f);
                TExit(turnL, idle, 0.8f);

                // Bake the locomotion hover into the shared in-place clips: jump up (RunStart), hold
                // the hover (Run), land (RunStop). The lift is in WORLD units mapped into the root
                // node's local space, so it stays visible at any model scale. Measure that scale off a
                // throwaway instance. Both skins' no-root-motion avatars play it as pose motion.
                {
                    var hoverInst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ErwinFbx));
                    try
                    {
                        var hSkel = FindSkeletonRoot(hoverInst);
                        float rootBaseY = hSkel != null ? hSkel.localPosition.y : 0f;
                        string rootPath = hSkel != null ? hSkel.name : "root";
                        const float HoverWorld = 3.5f;
                        float instScaleY = Mathf.Max(0.0001f, hoverInst.transform.lossyScale.y);
                        float hover = HoverWorld / instScaleY;
                        Debug.Log($"hover: world={HoverWorld} instScaleY={instScaleY} local={hover:F4} rootBaseY={rootBaseY:F4}");
                        AddHover(startInPlace, rootPath, rootBaseY, rootBaseY + hover);
                        AddHover(runInPlace, rootPath, rootBaseY + hover, rootBaseY + hover);
                        AddHover(stopInPlace, rootPath, rootBaseY + hover, rootBaseY);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(hoverInst); }
                }

                AssetDatabase.SaveAssets();
                Debug.Log("controller: wrote " + ControllerPath);
                return true;
            }
            catch (Exception ex) { Debug.LogError("BuildController failed: " + ex); return false; }
        }

        // The erwin (SSR0101) skin prefab. Loads the shared controller built by BuildController.
        private static bool BuildErwin()
        {
            try
            {
                EnsureFolder("Assets/Prefabs/voymastina_mech/erwin");
                var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
                if (ac == null) { Debug.LogError("erwin: shared controller missing"); return false; }

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ErwinFbx);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.name = "voymastina_mech_erwin";
                var anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                anim.runtimeAnimatorController = ac;
                anim.applyRootMotion = false;

                // Skeleton root: the child subtree with the most transforms.
                var skelRoot = FindSkeletonRoot(inst);
                // Build a generic avatar with NO root-motion node ("" instead of the skeleton
                // root). With a root-motion node, that node's animation (including the jump's
                // vertical lift) is extracted as root motion and discarded here, so she never
                // rises. With none, the "root" node's motion plays as ordinary pose animation
                // and the jump/land show. Horizontal travel is already stripped, so no drift. The
                // synthesized hover arc lives on the shared clips (baked in BuildController).
                var avatar = AvatarBuilder.BuildGenericAvatar(inst, "");
                avatar.name = "voymastina_avatar";
                AssetDatabase.CreateAsset(avatar, AnimDir + "/voymastina_avatar.asset");
                anim.avatar = avatar;
                Debug.Log("avatar: " + (avatar != null && avatar.isValid ? avatar.name + " (valid)" : "INVALID") + " skelRoot=" + (skelRoot != null ? skelRoot.name : "?"));

                // Repair skins ModelConverter collapsed to bone 0. For any SMR whose mesh has a
                // .skin sidecar (true per-vertex bone index from the source bundle), rebuild the
                // weights. The bones list + bindposes are already correct, only indices were lost.
                foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = smr.sharedMesh;
                    if (mesh == null) continue;
                    var skinPath = SrcDir + "/" + mesh.name + ".skin";
                    if (!System.IO.File.Exists(skinPath)) continue;
                    var fixedMesh = ReskinFromSidecar(mesh, skinPath);
                    if (fixedMesh != null) smr.sharedMesh = fixedMesh;
                }

                // Per-mesh Menace/character materials with the real GFL2 textures (albedo +
                // normal; neutral mask for now). Mapped by mesh name; multi-set meshes (mech,
                // weapons) assign texture set 01/02 to submeshes in order. The loader rebinds
                // Menace/character to the game's real shader by name at load.
                var lit = Shader.Find("Menace/character") ?? Shader.Find("Standard");
                var placeholder = new Material(lit) { name = "voymastina_placeholder" };
                AssetDatabase.CreateAsset(placeholder, AnimDir + "/voymastina_placeholder.mat");
                var matCache = new Dictionary<string, Material>();
                int smrCount = 0, texCount = 0;
                var lodDrop = new List<GameObject>();
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    var rmesh = (r as SkinnedMeshRenderer)?.sharedMesh ?? (r.GetComponent<MeshFilter>()?.sharedMesh);
                    // Drop duplicate lower LODs (e.g. face_lod1) that render inside lod0.
                    if (rmesh != null && IsDropLod(rmesh.name))
                    {
                        lodDrop.Add(r.gameObject);
                        Debug.Log("dropping duplicate LOD mesh " + rmesh.name);
                        continue;
                    }
                    // The face's SDF lookup coordinates, from the committed sidecars.
                    if (r is SkinnedMeshRenderer sdfSmr && rmesh != null)
                    {
                        var withUv = FaceSdfUv.WithBakedUv(rmesh, FaceSdfSidecars, AnimDir);
                        if (withUv != rmesh) { sdfSmr.sharedMesh = withUv; rmesh = withUv; }
                    }
                    int n = rmesh != null ? Mathf.Max(1, rmesh.subMeshCount) : 1;
                    var sets = TextureSetsFor(rmesh != null ? rmesh.name : r.name);
                    var arr = new Material[n];
                    for (int i = 0; i < n; i++)
                    {
                        var baseName = (sets != null && sets.Length > 0) ? sets[Mathf.Min(i, sets.Length - 1)] : null;
                        if (baseName != null) { arr[i] = GetOrBuildMaterial(baseName, lit, matCache); texCount++; }
                        else arr[i] = placeholder;
                    }
                    r.sharedMaterials = arr;
                    // Ripped skinned meshes carry bad localBounds -> frustum culled
                    // (invisible at all camera distances). Recompute bounds each frame.
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                    smrCount++;
                }
                foreach (var go in lodDrop) UnityEngine.Object.DestroyImmediate(go);
                Debug.Log($"textured {smrCount} renderers, {texCount} submesh materials (shader {lit.name}); dropped {lodDrop.Count} duplicate LODs");

                AttachEyes(inst, matCache);
                AddOutlines(inst, matCache);

                // DIAGNOSTIC: scale + bounds (why is she invisible in-game?)
                var imp2 = (ModelImporter)AssetImporter.GetAtPath(ErwinFbx);
                Debug.Log($"IMPORT globalScale={imp2.globalScale} useFileScale={imp2.useFileScale} rootScale={inst.transform.localScale}");
                bool firstB = true; var wb = new Bounds(inst.transform.position, Vector3.zero);
                foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var m = smr.sharedMesh;
                    if (m != null && (m.name.Contains("body") || m.name.Contains("Mech_slg_lod0")))
                        Debug.Log($"SMR {m.name}: meshBoundsSize={m.bounds.size} lossyScale={smr.transform.lossyScale} rootBone={(smr.rootBone ? smr.rootBone.name : "null")} bones={(smr.bones != null ? smr.bones.Length : 0)}");
                    if (firstB) { wb = smr.bounds; firstB = false; } else wb.Encapsulate(smr.bounds);
                }
                Debug.Log($"COMBINED world bounds size={wb.size} center={wb.center}");
                float minY = float.MaxValue, maxY = float.MinValue, maxXZ = 0f;
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                {
                    minY = Mathf.Min(minY, t.position.y); maxY = Mathf.Max(maxY, t.position.y);
                    maxXZ = Mathf.Max(maxXZ, Mathf.Abs(t.position.x), Mathf.Abs(t.position.z));
                }
                Debug.Log($"SKELETON real height (bone Y span) = {maxY - minY} ; max |x|/|z| = {maxXZ}");

                // BINDING CHECK: at rest, bone[i].localToWorld * bindpose[i] must map back to
                // the renderer's own transform, so residual = rendererW2L * boneL2W * bindpose
                // is ~identity (translation ~0). A large residual = the mesh expects that bone
                // somewhere it isn't = effectively weighted to the wrong bone.
                void CheckBinding(SkinnedMeshRenderer s, string[] only)
                {
                    if (s == null || s.sharedMesh == null) return;
                    var bp = s.sharedMesh.bindposes; var bones = s.bones;
                    var w2l = s.transform.localToWorldMatrix.inverse;
                    Debug.Log($"BIND {s.sharedMesh.name}: bones={bones.Length} bindposes={bp.Length} rendererScale={s.transform.lossyScale}");
                    for (int i = 0; i < bones.Length && i < bp.Length; i++)
                    {
                        if (bones[i] == null) { Debug.Log($"   [{i}] NULL bone"); continue; }
                        if (only != null && !only.Contains(bones[i].name)) continue;
                        var resid = w2l * bones[i].localToWorldMatrix * bp[i];
                        var t = resid.GetColumn(3);
                        Debug.Log($"   [{i}] {bones[i].name} residual|t|={new Vector3(t.x, t.y, t.z).magnitude:F5}");
                    }
                }
                var legBones = new[] { "Knee_L", "Knee_R", "Rump2_L", "Rump2_R", "Root_M" };
                CheckBinding(inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => (s.sharedMesh?.name ?? "").Contains("Mech_slg_G1")), null);
                CheckBinding(inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => (s.sharedMesh?.name ?? "").Contains("Mech_slg_G2")), null);
                CheckBinding(inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => (s.sharedMesh?.name ?? "").Contains("Mech_slg_lod0")), legBones);

                // Size is baked into the mesh/skeleton at import (globalScale = ModelScale), so the
                // root stays at scale 1 like a vanilla vehicle. Nothing to scale here.
                Debug.Log($"Root scale {inst.transform.localScale} (skeleton Y span now {maxY - minY})");
                LogLogoBone(inst, "suit");

                // Doll's handheld gun: a separate GFL2 attachment. The extracted OBJ verts are
                // in source (authored) space, 100x larger than the FBX bind space, so a plain
                // attach renders a giant gun far off-screen. Place it via a matrix that maps the
                // source-space position into the attach bone's local space (x0.01 = 1/100), then
                // it sits in her hand AND follows that bone. Computed after the root scale.
                var gunMesh = AssetDatabase.LoadAllAssetsAtPath(SrcDir + "/voymastina_weapon.obj").OfType<Mesh>().FirstOrDefault();
                if (gunMesh != null)
                {
                    // Seat the SR01 AK-15 on GFL2's dedicated weapon socket so it inherits the
                    // hand's animation. The socket's transform is the game's intended weapon
                    // placement, so identity local pos/rot should orient it; only scale needs
                    // setting (the centred gun mesh is ~real-metre, the float test showed
                    // lossyScale ~1.5 looks right). Fall back to the wrist if no socket.
                    var allBones = inst.GetComponentsInChildren<Transform>(true);
                    var attach = allBones.FirstOrDefault(t => t.name == "Weapon_Socket")
                        ?? allBones.FirstOrDefault(t => t.name == "Char_Wrist_R")
                        ?? allBones.FirstOrDefault(t => t.name == "Weapon")
                        ?? skelRoot;
                    var gun = new GameObject("voymastina_gun");
                    gun.transform.SetParent(attach, false);
                    gun.transform.localScale = Vector3.one * (1.5f / Mathf.Max(0.0001f, attach.lossyScale.x));
                    // Grip sits forward of the socket; pull the gun back along its barrel axis
                    // so the grip lands in her hand. Tune.
                    // Tuned grip offset is ~-0.125 world units back; express it relative to the
                    // socket's world scale so it holds whether the 125x is on the root or baked in.
                    gun.transform.localPosition = new Vector3(-0.125f / Mathf.Max(0.0001f, attach.lossyScale.x), 0f, 0f);
                    gun.transform.localEulerAngles = Vector3.zero;
                    gun.AddComponent<MeshFilter>().sharedMesh = gunMesh;
                    gun.AddComponent<MeshRenderer>().sharedMaterial = GetOrBuildMaterial("cw_VoymastinaSR01_WL", lit, matCache);
                    Debug.Log($"seated gun on '{attach.name}' worldPos={gun.transform.position} localScale={gun.transform.localScale} lossyScale={gun.transform.lossyScale}");
                }
                else Debug.LogWarning("gun mesh not found at Assets/Source/voymastina_weapon.obj");

                AddMuzzleAnchors(inst);
                AttachThrusterVfx(inst);

                // Match native units' rendering layer mask (1) on the body meshes. The character import
                // leaves some at 257 (bit 8 = an HDRP decal layer), which projects road and other ground
                // decals onto the mech. rmc_default_female_soldier renderers all sit at 1.
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    r.renderingLayerMask = 1;

                PrefabUtility.SaveAsPrefabAsset(inst, ErwinPrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("erwin: wrote " + ErwinPrefabPath);
                return true;
            }
            catch (Exception ex) { Debug.LogError("BuildVoymastina failed: " + ex); return false; }
        }

        // Add the named projectile-origin transforms MENACE fire/launch skills resolve via the
        // MuzzleType enum (Muzzle->"muzzle", Muzzle2->"muzzle2", Muzzle3->"muzzle3"). The GFL2 rig
        // has no such names, so parent zero-offset children onto the existing weapon-tip bones; each
        // child then inherits its bone's animated world position/orientation:
        //   muzzle  <- lgp_muzzle   (the AK-15 barrel)
        //   muzzle2 <- VM_WL02_Node (the rocket-launcher muzzle)
        //   muzzle3 <- VM_WL01_Node (the drill tip)
        private static void AddMuzzleAnchors(GameObject inst)
        {
            var all = inst.GetComponentsInChildren<Transform>(true);
            void Anchor(string boneName, string muzzleName)
            {
                var parent = all.FirstOrDefault(t => t.name == boneName);
                if (parent == null) { Debug.LogWarning($"muzzle anchor: bone '{boneName}' not found for '{muzzleName}'"); return; }
                if (parent.Find(muzzleName) != null) return;
                var m = new GameObject(muzzleName);
                m.transform.SetParent(parent, worldPositionStays: false);
                Debug.Log($"muzzle '{muzzleName}' on '{boneName}' worldPos={m.transform.position}");
            }
            Anchor("lgp_muzzle", "muzzle");
            Anchor("VM_WL02_Node", "muzzle2");
            Anchor("VM_WL01_Node", "muzzle3");
        }

        // Attach MENACE's own jetpack-walker thruster jet at each thruster mesh (G1, G2). Every
        // ParticleSystem is set to loop + play-on-awake so it emits continuously (she hovers on
        // her jets), and the MENACE JetpackPrefabController (a script this project can't author)
        // is stripped, leaving plain Unity ParticleSystems whose HDRP shaders rebind at load.
        private static void AttachThrusterVfx(GameObject inst)
        {
            var vfx = AssetDatabase.LoadAssetAtPath<GameObject>(ThrusterVfxPrefab);
            if (vfx == null) { Debug.LogWarning("thruster vfx prefab missing at " + ThrusterVfxPrefab); return; }
            // The jet's emission is keyed by the effect's Animator, which idles at zero. Build a
            // controller that plays the loop clip continuously so the thrust stays on.
            var loopCtrl = BuildThrusterLoopController();
            // Her GFL2 rig has dedicated jet bones grouped per nozzle (VM_BackJet_up/down,
            // VM_LegJet_L/R, VM_WaistJet_L/R), each a main "_Node" plus Child* animation sub-nodes.
            // Attach one jet at each main nozzle node (the six "_Node" roots, excluding the Child
            // sub-nodes). Case-sensitive "Jet" avoids catching the lowercase "jetpack" effect nodes.
            var thrusters = inst.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.Contains("Jet") && t.name.EndsWith("_Node") && !t.name.Contains("Child"))
                .ToList();
            foreach (var b in thrusters) Debug.Log($"JET NOZZLE {b.name} worldPos={b.position}");
            if (thrusters.Count == 0) { Debug.LogWarning("thruster vfx: no *Jet*_Node nozzles found in rig"); return; }
            var jetRoots = new List<Transform>();
            int n2 = 0;
            foreach (var t in thrusters)
            {
                var fx = (GameObject)PrefabUtility.InstantiatePrefab(vfx);
                PrefabUtility.UnpackPrefabInstance(fx, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                fx.transform.SetParent(t, false);
                fx.transform.localPosition = Vector3.zero;
                fx.transform.localRotation = Quaternion.Euler(ThrusterVfxEuler);
                // Jet world size is fixed (ThrusterVfxScale); divide by the nozzle's actual world
                // scale so it holds whether the 125x is on an ancestor transform or baked into the rig.
                fx.transform.localScale = Vector3.one * (ThrusterVfxScale / Mathf.Max(0.0001f, t.lossyScale.x));
                // Keep the effect's animator on the loop clip so its (animation-keyed) emission runs
                // whenever the effect is active.
                if (loopCtrl != null)
                    foreach (var anim in fx.GetComponentsInChildren<Animator>(true))
                        anim.runtimeAnimatorController = loopCtrl;
                var dust = new List<GameObject>();
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                {
                    // Drop the ground-dust burst: it reads as noise on a hovering mech.
                    if (ps.name.IndexOf("dust", StringComparison.OrdinalIgnoreCase) >= 0) { dust.Add(ps.gameObject); continue; }
                    var main = ps.main;
                    main.playOnAwake = true; // plays when the effect is switched on by the clip
                    main.loop = true;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                }
                foreach (var go in dust) UnityEngine.Object.DestroyImmediate(go);
                foreach (var tr in fx.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(tr.gameObject);
                // Whole effect off by default; her locomotion clips switch it on so it loops the
                // entire jump-up -> hover -> landing, off in Idle.
                fx.SetActive(false);
                jetRoots.Add(fx.transform);
                n2++;
            }
            DriveJetsFromClips(inst, jetRoots);
            Debug.Log($"thruster vfx: {n2} nozzle(s); jetRoots={jetRoots.Count}; euler={ThrusterVfxEuler}");
        }

        // Switch the whole jet effect on for the entire locomotion (RunStart, Run, RunStop), so it
        // loops continuously from the jump-up through the landing. Idle has no curve, so the states'
        // Write Defaults return the effect to its off (inactive) default there.
        private static void DriveJetsFromClips(GameObject inst, List<Transform> jetRoots)
        {
            var run = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir + "/voymastina_run_inplace.anim");
            var runStart = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir + "/voymastina_runstart_inplace.anim");
            var runStop = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir + "/voymastina_runstop_inplace.anim");
            // The drill reuses the locomotion jets: switch them on for the windup clip, so she spins
            // up thrust as she plants for the drill. Write Defaults turns them back off once the
            // drill exits to Aim.
            var ultra = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir + "/voymastina_ultraskill_inplace.anim");
            foreach (var root in jetRoots)
            {
                var path = AnimationUtility.CalculateTransformPath(root, inst.transform);
                SetActiveCurve(run, path);
                SetActiveCurve(runStart, path);
                SetActiveCurve(runStop, path);
                SetActiveCurve(ultra, path);
            }
            Debug.Log($"jets-from-clips: run={(run != null)} runStart={(runStart != null)} runStop={(runStop != null)} ultra={(ultra != null)}");
        }

        // Add a constant "GameObject active = 1" curve for a transform path to a clip. Uses
        // clip.SetCurve (writes the runtime float curve) rather than SetEditorCurve with a
        // DiscreteCurve binding, which wrote an empty runtime curve that never applied in-game.
        private static void SetActiveCurve(AnimationClip clip, string path)
        {
            if (clip == null) return;
            float len = Mathf.Max(1f / 60f, clip.length);
            clip.SetCurve(path, typeof(GameObject), "m_IsActive", new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(len, 1f)));
        }

        // A one-state controller that plays the thruster loop clip on a loop, so a standalone copy
        // of the effect keeps its jet emission running (no jump trigger needed).
        private static UnityEditor.Animations.AnimatorController BuildThrusterLoopController()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ThrusterLoopClip);
            if (clip == null) { Debug.LogWarning("thruster loop clip missing at " + ThrusterLoopClip); return null; }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (!settings.loopTime) { settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, settings); }
            if (AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ThrusterLoopCtrlPath) != null)
                AssetDatabase.DeleteAsset(ThrusterLoopCtrlPath);
            return UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPathWithClip(ThrusterLoopCtrlPath, clip);
        }

        // The default FBX export collapses the logo decal's skin to one bone (Root_M), so it
        // flails during animation. The suit FBX kept the real multi-bone skin (dominant
        // ScapulaArmor_L). Both prefabs share one skeleton, so transplant the suit logo's mesh
        // (geometry + weights + bindposes) onto the default, rebinding its bone list to the
        // default rig's transforms by name.
        private static void TransplantLogoSkin(GameObject defInst, GameObject suitAsset)
        {
            var suitLogo = suitAsset.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(s => (s.sharedMesh?.name ?? "").IndexOf("Mech_slg_logo", StringComparison.OrdinalIgnoreCase) >= 0);
            var defLogo = defInst.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(s => (s.sharedMesh?.name ?? "").IndexOf("Mech_slg_logo", StringComparison.OrdinalIgnoreCase) >= 0);
            if (suitLogo == null || defLogo == null) { Debug.LogWarning("transplant: missing logo SMR"); return; }

            var byName = new Dictionary<string, Transform>();
            foreach (var t in defInst.GetComponentsInChildren<Transform>(true)) byName[t.name] = t;
            var suitBones = suitLogo.bones;
            var newBones = new Transform[suitBones.Length];
            for (int i = 0; i < suitBones.Length; i++)
            {
                if (suitBones[i] != null && byName.TryGetValue(suitBones[i].name, out var tb)) newBones[i] = tb;
                else { Debug.LogWarning($"transplant: default rig missing bone {suitBones[i]?.name}"); return; }
            }

            var copy = UnityEngine.Object.Instantiate(suitLogo.sharedMesh);
            copy.name = suitLogo.sharedMesh.name;
            // The two FBX exports can carry slightly different bind transforms, so recompute the
            // bindposes against THIS rig at its bind pose: bindpose[i] = bone[i].worldToLocal * renderer.l2w.
            var bp = copy.bindposes;
            for (int i = 0; i < newBones.Length && i < bp.Length; i++)
                if (newBones[i] != null) bp[i] = newBones[i].worldToLocalMatrix * defLogo.transform.localToWorldMatrix;
            copy.bindposes = bp;
            // Keep "Mech_slg_logo" in the asset name (CreateAsset renames the mesh to the file
            // stem) so the material loop + LOD checks still recognise it as the logo.
            AssetDatabase.CreateAsset(copy, AnimDir + "/c_VoymastinaSSR01_Mech_slg_logo_lod0_default.asset");
            defLogo.sharedMesh = copy;
            defLogo.bones = newBones;
            if (suitLogo.rootBone != null && byName.TryGetValue(suitLogo.rootBone.name, out var rb)) defLogo.rootBone = rb;
            Debug.Log($"transplant: default logo <- suit logo skin (bones={newBones.Length})");
        }

        // Report which bone the logo decal rides (it collapses to one bone on export, but the
        // two FBX exports order that bone list differently, so the same collapse lands on a
        // stable bone for one skin and a flailing bone for the other).
        private static void LogLogoBone(GameObject inst, string label)
        {
            var smr = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(s => (s.sharedMesh?.name ?? "").IndexOf("Mech_slg_logo", StringComparison.OrdinalIgnoreCase) >= 0);
            if (smr == null || smr.sharedMesh == null) { Debug.Log($"LOGO-BONE [{label}] no logo SMR"); return; }
            var bones = smr.bones; var bw = smr.sharedMesh.boneWeights;
            var counts = new Dictionary<int, int>();
            foreach (var w in bw) { counts.TryGetValue(w.boneIndex0, out int c); counts[w.boneIndex0] = c + 1; }
            int dom = -1, bestN = -1;
            foreach (var kv in counts) if (kv.Value > bestN) { bestN = kv.Value; dom = kv.Key; }
            string domName = (dom >= 0 && bones != null && dom < bones.Length && bones[dom]) ? bones[dom].name : "?";
            string b0 = (bones != null && bones.Length > 0 && bones[0]) ? bones[0].name : "?";
            Debug.Log($"LOGO-BONE [{label}] verts={bw.Length} bones={bones?.Length} bones[0]={b0} dominant=idx{dom}({domName}) cnt={bestN} rootBone={(smr.rootBone ? smr.rootBone.name : "null")}");
        }

        // Rebuild a mesh's single-influence skin from a sidecar (int32 count, count bytes of
        // boneIndex0, bool hasPos, count*3 floats of source vertex positions). Matches by
        // vertex order when source positions line up, otherwise by quantised position.
        private static Mesh ReskinFromSidecar(Mesh mesh, string skinPath)
        {
            int vc; byte[] idx; float[] pos = null; bool hasPos;
            using (var br = new System.IO.BinaryReader(System.IO.File.OpenRead(skinPath)))
            {
                vc = br.ReadInt32();
                idx = br.ReadBytes(vc);
                hasPos = br.ReadBoolean();
                if (hasPos) { pos = new float[vc * 3]; for (int i = 0; i < vc * 3; i++) pos[i] = br.ReadSingle(); }
            }
            if (mesh.vertexCount != vc)
            {
                Debug.LogError($"Reskin {mesh.name}: vertex count mismatch fbx={mesh.vertexCount} src={vc}, skipping");
                return null;
            }
            var fv = mesh.vertices;
            // FBX = source / uniform-scale, same vertex order. Derive the scale from the
            // largest-magnitude vertex (avoids division noise near zero), then confirm the
            // order holds by checking fbx[i]*scale matches source[i].
            float scale = 1f;
            if (hasPos)
            {
                int bi = 0; float bm = 0f;
                for (int i = 0; i < vc; i++)
                {
                    float m = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]).magnitude;
                    if (m > bm && fv[i].magnitude > 1e-5f) { bm = m; bi = i; }
                }
                scale = bm / Mathf.Max(1e-6f, fv[bi].magnitude);
            }
            int ok = 0, checkN = Mathf.Min(300, vc);
            float tol = 0.02f * scale;
            for (int k = 0; k < checkN; k++)
            {
                int i = k * (vc / Mathf.Max(1, checkN));
                var sp = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                if ((fv[i] * scale - sp).magnitude <= tol) ok++;
            }
            Debug.Log($"Reskin {mesh.name}: derivedScale={scale:F2} orderMatch={ok}/{checkN}");
            var bw = new BoneWeight[vc];
            for (int i = 0; i < vc; i++) { bw[i].boneIndex0 = idx[i]; bw[i].weight0 = 1f; }
            var verify = new HashSet<int>(); foreach (var w in bw) verify.Add(w.boneIndex0);
            Debug.Log($"Reskin {mesh.name}: applied {vc} weights, distinctBones now {verify.Count} ({string.Join(",", verify.OrderBy(x => x))})");
            var copy = UnityEngine.Object.Instantiate(mesh);
            copy.name = mesh.name + "_reskin";
            copy.boneWeights = bw;
            AssetDatabase.CreateAsset(copy, AnimDir + "/" + copy.name + ".asset");
            return copy;
        }

        // Bake the default (SSR01 tactical) skin into a second prefab, reusing the controller
        // and skin-repair from the formal build. Only the doll outfit meshes + cloth textures
        // differ; mech/rig/gun/scale are identical.
        private static bool BuildDefault()
        {
            try
            {
                EnsureFolder("Assets/Prefabs/voymastina_mech/default");
                var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
                if (ac == null) { Debug.LogError("default: shared controller missing"); return false; }
                var imp = (ModelImporter)AssetImporter.GetAtPath(DefaultFbx);
                if (imp == null) { Debug.LogError("default FBX not found at " + DefaultFbx); return false; }
                imp.animationType = ModelImporterAnimationType.Generic;
                imp.importAnimation = false;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                // Same true-size bake as the erwin FBX: 125x into the mesh/skeleton, root stays 1.
                // Both FBXs share one scale so the default skeleton and the shared (erwin-built)
                // controller/clips stay consistent.
                imp.useFileScale = false;
                imp.globalScale = ModelScale * FbxFileScale;
                AssetDatabase.ImportAsset(DefaultFbx, ImportAssetOptions.ForceUpdate);

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultFbx);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.name = "voymastina_mech_default";
                var anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                anim.runtimeAnimatorController = ac;
                anim.applyRootMotion = false;

                var skelRoot = FindSkeletonRoot(inst);
                var avatar = AvatarBuilder.BuildGenericAvatar(inst, "");
                avatar.name = "voymastina_default_avatar";
                AssetDatabase.CreateAsset(avatar, AnimDir + "/voymastina_default_avatar.asset");
                anim.avatar = avatar;

                foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = smr.sharedMesh; if (mesh == null) continue;
                    var skinPath = SrcDir + "/" + mesh.name + ".skin";
                    if (!System.IO.File.Exists(skinPath)) continue;
                    var fixedMesh = ReskinFromSidecar(mesh, skinPath);
                    if (fixedMesh != null) smr.sharedMesh = fixedMesh;
                }

                // The default FBX export collapsed the logo decal's skin to one bone (it floats). The
                // erwin FBX exported the same logo mesh correctly on the same skeleton, so transplant
                // its logo skin onto the default, rebound to this rig by name. Source it from the erwin
                // FBX (a source asset) rather than the built erwin prefab, so default has no dependency
                // on the erwin prefab build and the two skins can bake in any order.
                var suitModel = AssetDatabase.LoadAssetAtPath<GameObject>(ErwinFbx);
                var suitInst = suitModel != null ? (GameObject)PrefabUtility.InstantiatePrefab(suitModel) : null;
                if (suitInst != null)
                {
                    try { TransplantLogoSkin(inst, suitInst); }
                    finally { UnityEngine.Object.DestroyImmediate(suitInst); }
                }
                else Debug.LogWarning("default: erwin FBX not loadable for logo transplant");

                var lit = Shader.Find("Menace/character") ?? Shader.Find("Standard");
                var placeholder = AssetDatabase.LoadAssetAtPath<Material>(AnimDir + "/voymastina_placeholder.mat");
                var matCache = new Dictionary<string, Material>();
                int sc = 0, tc = 0; var drop = new List<GameObject>();
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    var rmesh = (r as SkinnedMeshRenderer)?.sharedMesh ?? (r.GetComponent<MeshFilter>()?.sharedMesh);
                    if (rmesh != null && IsDropLod(rmesh.name)) { drop.Add(r.gameObject); continue; }
                    // The face's SDF lookup coordinates, from the committed sidecars.
                    if (r is SkinnedMeshRenderer sdfSmr && rmesh != null)
                    {
                        var withUv = FaceSdfUv.WithBakedUv(rmesh, FaceSdfSidecars, AnimDir);
                        if (withUv != rmesh) { sdfSmr.sharedMesh = withUv; rmesh = withUv; }
                    }
                    if (rmesh != null && rmesh.boneWeights != null && rmesh.boneWeights.Length > 0)
                    {
                        var di = new HashSet<int>(); int bl = 0;
                        foreach (var w in rmesh.boneWeights) { di.Add(w.boneIndex0); if (w.weight1 > 0.001f) bl++; }
                        Debug.Log($"WT {rmesh.name}: distinctIndex0={di.Count} blended={bl}/{rmesh.boneWeights.Length}");
                    }
                    int n = rmesh != null ? Mathf.Max(1, rmesh.subMeshCount) : 1;
                    var sets = TextureSetsFor(rmesh != null ? rmesh.name : r.name);
                    var arr = new Material[n];
                    for (int i = 0; i < n; i++)
                    {
                        var b = (sets != null && sets.Length > 0) ? sets[Mathf.Min(i, sets.Length - 1)] : null;
                        arr[i] = b != null ? GetOrBuildMaterial(b, lit, matCache) : placeholder;
                        if (b != null) tc++;
                    }
                    r.sharedMaterials = arr;
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                    sc++;
                }
                foreach (var go in drop) UnityEngine.Object.DestroyImmediate(go);
                Debug.Log($"default: textured {sc} renderers, {tc} mats, dropped {drop.Count} LODs");

                AttachEyes(inst, matCache);
                AddOutlines(inst, matCache);

                // Size baked into the mesh/skeleton at import; root stays at scale 1.

                // Per-mesh world bounds: a floating mesh shows up as a position outlier.
                foreach (var rr in inst.GetComponentsInChildren<Renderer>(true))
                {
                    var rm = (rr as SkinnedMeshRenderer)?.sharedMesh ?? (rr.GetComponent<MeshFilter>()?.sharedMesh);
                    var bb = rr.bounds;
                    Debug.Log($"BOUNDS {rm?.name}: center=({bb.center.x:F2},{bb.center.y:F2},{bb.center.z:F2}) size=({bb.size.x:F2},{bb.size.y:F2},{bb.size.z:F2})");
                }
                LogLogoBone(inst, "default");

                var gunMesh = AssetDatabase.LoadAllAssetsAtPath(SrcDir + "/voymastina_weapon.obj").OfType<Mesh>().FirstOrDefault();
                if (gunMesh != null && skelRoot != null)
                {
                    var allBones = inst.GetComponentsInChildren<Transform>(true);
                    var attach = allBones.FirstOrDefault(t => t.name == "Weapon_Socket")
                        ?? allBones.FirstOrDefault(t => t.name == "Char_Wrist_R") ?? skelRoot;
                    var gun = new GameObject("voymastina_gun");
                    gun.transform.SetParent(attach, false);
                    gun.transform.localScale = Vector3.one * (1.5f / Mathf.Max(0.0001f, attach.lossyScale.x));
                    // Tuned grip offset is ~-0.125 world units back; express it relative to the
                    // socket's world scale so it holds whether the 125x is on the root or baked in.
                    gun.transform.localPosition = new Vector3(-0.125f / Mathf.Max(0.0001f, attach.lossyScale.x), 0f, 0f);
                    gun.AddComponent<MeshFilter>().sharedMesh = gunMesh;
                    gun.AddComponent<MeshRenderer>().sharedMaterial = GetOrBuildMaterial("cw_VoymastinaSR01_WL", lit, matCache);
                }

                AddMuzzleAnchors(inst);
                AttachThrusterVfx(inst);

                // Match native units' rendering layer mask (1) on the body meshes. The character import
                // leaves some at 257 (bit 8 = an HDRP decal layer), which projects road and other ground
                // decals onto the mech. rmc_default_female_soldier renderers all sit at 1.
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    r.renderingLayerMask = 1;

                PrefabUtility.SaveAsPrefabAsset(inst, DefaultPrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("default: wrote " + DefaultPrefabPath);
                return true;
            }
            catch (Exception ex) { Debug.LogError("BuildDefault failed: " + ex); return false; }
        }

        private const string TexDir = SrcDir + "/textures";
        private static Texture2D _matteMask;

        // A small linear matte mask: HDRP MaskMap layout R=metallic(0), G=AO(1), B=detail(0),
        // A=smoothness(low). Kills the default chrome look.
        private static Texture2D GetMatteMask()
        {
            if (_matteMask != null) return _matteMask;
            const string path = AnimDir + "/voymastina_matte_mask.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) { _matteMask = existing; return _matteMask; }
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 255, 0, 30);
            t.SetPixels32(px); t.Apply();
            AssetDatabase.CreateAsset(t, path);
            _matteMask = t;
            return t;
        }

        // Map a mesh name to its GFL2 texture-set base name(s), one per submesh in order.
        private static string[] TextureSetsFor(string meshName)
        {
            string m = meshName ?? "";
            // Mech + weapons: shared between both skins.
            if (m.Contains("Mech_slg_lod0")) return new[] { "c_VoymastinaSSR01_Mech_slg_01", "c_VoymastinaSSR01_Mech_slg_02" };
            if (m.Contains("Mech_slg_logo")) return new[] { "c_VoymastinaSSR01_Mech_slg_logo" };
            if (m.Contains("Mech_slg_G1")) return new[] { "c_VoymastinaSSR01_Mech_slg_01" };
            if (m.Contains("Mech_slg_G2")) return new[] { "c_VoymastinaSSR01_Mech_slg_02" };
            if (m.Contains("WL01")) return new[] { "cw_VoymastinaSSR01_Mech_WL01_slg_01", "cw_VoymastinaSSR01_Mech_WL01_slg_02" };
            if (m.Contains("WL02")) return new[] { "cw_VoymastinaSSR01_Mech_WL02_slg_01", "cw_VoymastinaSSR01_Mech_WL02_slg_02" };
            // Doll outfit: distinguish the formal (SSR0101) suit from the default (SSR01)
            // tactical gear. Check SSR0101 first (it is not a substring of the SSR01 forms).
            if (m.Contains("SSR0101_slg_cloth1")) return new[] { "c_VoymastinaSSR0101_slg_cloth1" };
            if (m.Contains("SSR0101_slg_cloth2")) return new[] { "c_VoymastinaSSR0101_slg_cloth2" };
            if (m.Contains("SSR0101_slg_hair")) return new[] { "c_VoymastinaSSR0101_slg_hair" };
            if (m.Contains("SSR01_slg_cloth1")) return new[] { "c_VoymastinaSSR01_slg_cloth1" };
            if (m.Contains("SSR01_slg_cloth2")) return new[] { "c_VoymastinaSSR01_slg_cloth2" };
            if (m.Contains("SSR01_slg_hair")) return new[] { "c_Voymastina_slg_hair" };
            if (m.Contains("slg_face")) return new[] { "c_Voymastina_slg_face" };
            // Body skin approximated with the face texture, but as its own material:
            // the face's material carries the SDF sweep, whose lookup coordinates
            // only exist on the face mesh. The |body marker splits the material
            // while the texture prefix stays shared.
            if (m.Contains("slg_body")) return new[] { "c_Voymastina_slg_face|body" };
            return null;
        }

        // True for non-lod0 LOD copies that would render inside lod0 (lod1/2/3, lodm0).
        private static bool IsDropLod(string name) =>
            name != null && (name.IndexOf("_lod1", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("_lod2", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("_lod3", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("_lodm0", StringComparison.OrdinalIgnoreCase) >= 0);

        // Import a texture (base+suffix.png) with the right colour-space/type, return it.
        private static Texture2D LoadTex(string baseName, string[] suffixes, bool sRGB, bool isNormal)
        {
            foreach (var sfx in suffixes)
                foreach (var ext in new[] { ".png", ".tga" })
                {
                    var path = TexDir + "/" + baseName + sfx + ext;
                    if (!System.IO.File.Exists(path)) continue;
                    if (AssetImporter.GetAtPath(path) is TextureImporter imp)
                    {
                        bool changed = false;
                        var tt = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                        if (imp.textureType != tt) { imp.textureType = tt; changed = true; }
                        if (imp.sRGBTexture != sRGB) { imp.sRGBTexture = sRGB; changed = true; }
                        if (changed) imp.SaveAndReimport();
                    }
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
            return null;
        }

        // The pilot's eye stack, transplanted from her infantry doll by
        // scripts/doll/transplant_eyes.py: the battle rip ships no eye geometry
        // (the game attaches its own at runtime), so the doll's four layers are
        // aligned onto the FBX face and attached here as skinned renderers
        // sharing the face's bones, every vertex on the face's dominant bone.
        // Draw order and blending live in the layer shaders themselves, exactly
        // as they do on the dolls.
        private const string EyesDir = SrcDir + "/eyes";

        private static readonly (string layer, string obj)[] EyeLayers =
        {
            ("EyeWhite", "eye_EyeWhite.obj"),
            ("Eyes", "eye_Eyes.obj"),
            ("EyeShadow", "eye_EyeShadow.obj"),
            ("Eyes+", "eye_Eyes_hl.obj"),
        };

        private static void AttachEyes(GameObject inst, Dictionary<string, Material> matCache)
        {
            SkinnedMeshRenderer face = null;
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null && smr.sharedMesh.name.Contains("slg_face")
                    && !IsDropLod(smr.sharedMesh.name)) { face = smr; break; }
            if (face == null) { Debug.LogError("AttachEyes: no face renderer"); return; }

            // The face's dominant bone, which is the head: the eyes ride it rigidly.
            var faceWeights = face.sharedMesh.boneWeights;
            var totals = new float[face.bones.Length];
            foreach (var w in faceWeights)
            {
                totals[w.boneIndex0] += w.weight0;
                totals[w.boneIndex1] += w.weight1;
                totals[w.boneIndex2] += w.weight2;
                totals[w.boneIndex3] += w.weight3;
            }
            int head = 0;
            for (int i = 1; i < totals.Length; i++) if (totals[i] > totals[head]) head = i;

            foreach (var (layer, objName) in EyeLayers)
            {
                var source = AssetDatabase.LoadAllAssetsAtPath(EyesDir + "/" + objName)
                    .OfType<Mesh>().FirstOrDefault();
                if (source == null) { Debug.LogError($"AttachEyes: missing {objName}"); continue; }
                var mesh = UnityEngine.Object.Instantiate(source);
                mesh.name = "voy_" + layer.Replace("+", "_hl");
                var weights = new BoneWeight[mesh.vertexCount];
                for (int i = 0; i < weights.Length; i++)
                    weights[i] = new BoneWeight { boneIndex0 = head, weight0 = 1f };
                mesh.boneWeights = weights;
                mesh.bindposes = face.sharedMesh.bindposes;
                AssetDatabase.CreateAsset(mesh, AnimDir + "/" + mesh.name + ".asset");

                var go = new GameObject(mesh.name);
                go.transform.SetParent(face.transform.parent, false);
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = face.bones;
                smr.rootBone = face.rootBone;
                smr.updateWhenOffscreen = true;
                smr.sharedMaterials = new[] { EyeLayerMaterial(layer, matCache) };
            }
            Debug.Log("AttachEyes: four layers on bone " + face.bones[head].name);
        }

        private static Material EyeLayerMaterial(string layer, Dictionary<string, Material> cache)
        {
            string key = "eye|" + layer;
            if (cache.TryGetValue(key, out var cached)) return cached;
            Material mat;
            switch (layer)
            {
                // The backing takes the face's own material sans SDF: same skin,
                // same ramp, and the sweep's coordinates exist only on the face.
                case "EyeWhite":
                    return GetOrBuildMaterial("c_Voymastina_slg_face|body",
                        Shader.Find("Menace/character"), cache);
                case "Eyes":
                    mat = new Material(Shader.Find("Womenace/DollEye")) { name = "vmat_eye" };
                    mat.SetTexture("_BaseMap",
                        LoadTex("c_Voymastina_slg_eye", new[] { "_d" }, sRGB: true, isNormal: false));
                    break;
                case "EyeShadow":
                    mat = new Material(Shader.Find("Womenace/DollEyeShadow")) { name = "vmat_eyemul" };
                    mat.SetTexture("_BaseMap",
                        LoadTex("c_Voymastina_slg_eyeblend", new[] { "" }, sRGB: true, isNormal: false));
                    break;
                default:
                    mat = new Material(Shader.Find("Womenace/DollEyeHighlight")) { name = "vmat_eyeadd" };
                    mat.SetTexture("_BaseMap",
                        LoadTex("c_Voymastina_slg_eyeblend", new[] { "" }, sRGB: true, isNormal: false));
                    break;
            }
            AssetDatabase.CreateAsset(mat, AnimDir + "/" + mat.name + ".mat");
            cache[key] = mat;
            return mat;
        }

        // The inverted-hull outline, as the dolls have it: the same geometry drawn
        // again under DollOutline. On the glTF dolls that is a duplicated submesh;
        // here it is a duplicated renderer, which is the same draw. The face is
        // excluded (its open boundary edges draw rims without a per-vertex width),
        // and so are the weapons and the logo, which the game leaves outline-free.
        private static bool OutlinedMesh(string meshName)
        {
            if (meshName == null) return false;
            if (meshName.Contains("logo") || meshName.StartsWith("cw_")) return false;
            return meshName.Contains("slg_hair") || meshName.Contains("slg_cloth")
                || meshName.Contains("slg_body") || meshName.Contains("Mech_slg");
        }

        private static void AddOutlines(GameObject inst, Dictionary<string, Material> matCache)
        {
            var outlined = new List<(Renderer r, Mesh mesh)>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = (r as SkinnedMeshRenderer)?.sharedMesh
                    ?? r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh != null && OutlinedMesh(mesh.name) && !r.name.EndsWith("_outline"))
                    outlined.Add((r, mesh));
            }
            foreach (var (r, mesh) in outlined)
            {
                var copy = UnityEngine.Object.Instantiate(r.gameObject, r.transform.parent);
                copy.name = r.gameObject.name + "_outline";
                // The copy keeps only its renderer; children would double whatever
                // hangs below the original.
                for (int i = copy.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(copy.transform.GetChild(i).gameObject);
                var cr = copy.GetComponent<Renderer>();
                var sets = TextureSetsFor(mesh.name);
                int n = Mathf.Max(1, mesh.subMeshCount);
                var mats = new Material[n];
                for (int i = 0; i < n; i++)
                {
                    var baseName = (sets != null && sets.Length > 0)
                        ? sets[Mathf.Min(i, sets.Length - 1)] : null;
                    mats[i] = OutlineMaterial(baseName, matCache);
                }
                cr.sharedMaterials = mats;
                if (cr is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            Debug.Log($"AddOutlines: {outlined.Count} renderer(s) duplicated under DollOutline");
        }

        private static Material OutlineMaterial(string baseName, Dictionary<string, Material> cache)
        {
            string key = "outline|" + (baseName ?? "plain");
            if (cache.TryGetValue(key, out var cached)) return cached;
            var mat = new Material(Shader.Find("Womenace/DollOutline"))
            { name = "vmat_outline_" + (baseName ?? "plain") };
            if (baseName != null)
            {
                var alb = LoadTex(baseName, new[] { "_d", "_da" }, sRGB: true, isNormal: false);
                if (alb != null) mat.SetTexture("_BaseMap", alb);
            }
            AssetDatabase.CreateAsset(mat, AnimDir + "/" + mat.name.Replace('|', '_') + ".mat");
            cache[key] = mat;
            return mat;
        }

        // GFL2 texture-set base name -> doll shader + ramp. A null shader keeps the
        // Menace/character fallback. Everything mech and weapon takes the shared
        // weapon ramp, which is what the capture's binding map shows on the weapon
        // and the large draws; the doll parts take her own extracted ramps.
        //
        // cloth1 is the cool suit and pairs with the xizhuang (suit) ramp, cloth2
        // the warm underlayer with the cloth ramp. If a garment's shadow hue reads
        // wrong in game, these two are the pair to swap.
        private static (string shader, string ramp) DollRouteFor(string baseName)
        {
            if (baseName.Contains("Mech_slg_logo")) return ("Womenace/DollToonTrans", SharedRamps + "/ramp_weapon.png");
            if (baseName.Contains("_Mech_") || baseName.StartsWith("cw_")) return ("Womenace/DollToon", SharedRamps + "/ramp_weapon.png");
            if (baseName.Contains("SSR0101_slg_cloth1")) return ("Womenace/DollToon", VoyRamps + "/ramp_suit.png");
            if (baseName.Contains("SSR0101_slg_cloth2")) return ("Womenace/DollToon", VoyRamps + "/ramp_cloth_formal.png");
            if (baseName.Contains("SSR01_slg_cloth")) return ("Womenace/DollToon", VoyRamps + "/ramp_cloth_main.png");
            if (baseName.Contains("SSR0101_slg_hair")) return ("Womenace/DollToon", VoyRamps + "/ramp_hair_formal.png");
            if (baseName.Contains("_slg_hair")) return ("Womenace/DollToon", VoyRamps + "/ramp_hair.png");
            if (baseName.Contains("_slg_face")) return ("Womenace/DollToon", SharedRamps + "/ramp_skin.png");
            return (null, null);
        }

        // Build (and cache) the material for a texture-set base name: the doll
        // shaders where the route above names one, the Menace/character fallback
        // otherwise. A "|marker" suffix on the base name splits the material
        // without splitting the texture set, e.g. body skin sharing the face's
        // texture but not its SDF sweep.
        private static Material GetOrBuildMaterial(string baseName, Shader shader, Dictionary<string, Material> cache)
        {
            if (cache.TryGetValue(baseName, out var cached)) return cached;
            int marker = baseName.IndexOf('|');
            string texBase = marker < 0 ? baseName : baseName.Substring(0, marker);
            var route = DollRouteFor(texBase);
            var mat = route.shader != null
                ? BuildDollMaterial(baseName, texBase, route.shader, route.ramp)
                : BuildMenaceMaterial(texBase, shader);
            AssetDatabase.CreateAsset(mat, AnimDir + "/vmat_" + baseName.Replace('|', '_') + ".mat");
            cache[baseName] = mat;
            return mat;
        }

        // A material on the doll shaders, textured with the GFL2 set as shipped:
        // albedo, tangent normal, RMO in the game's own packing (the shader reads
        // R rough, G metal, B occlusion directly, so the HDRP repack and the matte
        // placeholder both stay out of this path), and the ramp. The doll shaders
        // sample no decal buffer, so they need no decal opt-out to keep road
        // decals off the mech.
        private static Material BuildDollMaterial(string baseName, string texBase, string shaderName, string rampPath)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError("missing shader " + shaderName + ", falling back to Standard");
                shader = Shader.Find("Standard");
            }
            var mat = new Material(shader) { name = "vmat_" + baseName.Replace('|', '_') };
            var alb = LoadTex(texBase, new[] { "_d", "_da" }, sRGB: true, isNormal: false);
            if (alb != null) mat.SetTexture("_BaseMap", alb);
            var nrm = LoadTex(texBase, new[] { "_n" }, sRGB: false, isNormal: true);
            if (nrm != null) mat.SetTexture("_NormalMap", nrm);
            // The GFL2 _rmo is data, not colour: linear, and bound to the slot the
            // shader reads in the game's packing.
            var rmo = LoadTex(texBase, new[] { "_rmo" }, sRGB: false, isNormal: false);
            if (rmo != null) mat.SetTexture("_MaskMap", rmo);
            var spc = LoadTex(texBase, new[] { "_spc" }, sRGB: true, isNormal: false);
            if (spc != null)
            {
                mat.SetTexture("_SpecularMap", spc);
                // The hair path, on. The doll bakes keep this at 0 because a PMX
                // source has no real TEXCOORD1 to sample the streak with, but the
                // ripped game meshes here carry the game's own strip UV (verified:
                // the hair's TEXCOORD1 spans strip coordinates well outside 0..1).
                // A non-zero intensity is also what routes the material off GGX
                // and onto the anisotropic hair term.
                mat.SetFloat("_MatCapIntensity", 1f);
            }
            var ramp = AssetDatabase.LoadAssetAtPath<Texture2D>(rampPath);
            if (ramp == null) Debug.LogError("missing ramp " + rampPath);
            else mat.SetTexture("_RampMap", ramp);
            // The SDF sweep belongs to the face mesh alone: its lookup coordinates
            // exist only there, which is what the |body split guards.
            if (baseName.Contains("_slg_face") && !baseName.Contains("|"))
            {
                mat.SetFloat("_UseBlendTex", 1f);
                var sdf = AssetDatabase.LoadAssetAtPath<Texture2D>(SharedFaceSdf);
                if (sdf == null) Debug.LogError("missing face SDF map " + SharedFaceSdf);
                else mat.SetTexture("_SdfMap", sdf);
            }
            Debug.Log($"doll material {baseName}: shader={shaderName} ramp={System.IO.Path.GetFileName(rampPath)} "
                + $"albedo={(alb ? alb.name : "-")} normal={(nrm ? nrm.name : "-")} rmo={(rmo ? rmo.name : "-")}");
            return mat;
        }

        // The Menace/character fallback for anything the doll route does not
        // claim. Sets every candidate property name so the runtime shader rebind
        // picks up whichever it uses.
        private static Material BuildMenaceMaterial(string baseName, Shader shader)
        {
            var alb = LoadTex(baseName, new[] { "_d", "_da", "_spc" }, sRGB: true, isNormal: false);
            var nrm = LoadTex(baseName, new[] { "_n" }, sRGB: false, isNormal: true);
            var mat = new Material(shader) { name = "vmat_" + baseName };
            if (alb != null) foreach (var p in new[] { "_BaseMap", "_BaseColorMap", "_MainTex" }) if (mat.HasProperty(p)) mat.SetTexture(p, alb);
            if (nrm != null) foreach (var p in new[] { "_NormalMap", "_BumpMap", "_Normal" }) if (mat.HasProperty(p)) mat.SetTexture(p, nrm);
            // Without a mask the shader defaults to a white mask = full metallic + full
            // smoothness = chrome. Assign a matte mask (HDRP layout R=metallic 0, G=AO 1,
            // A=smoothness low) so surfaces read matte.
            var mask = GetMatteMask();
            foreach (var p in new[] { "_MaskMap", "_Mask", "_MetallicGlossMap" }) if (mat.HasProperty(p)) mat.SetTexture(p, mask);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.15f);
            // Match native units: opt out of receiving decals so road and other ground decals do not
            // project onto the mech. The native character material carries _SupportDecals 0 and the
            // _DISABLE_DECALS shader keyword, so set both for the serialised material to match.
            if (mat.HasProperty("_SupportDecals")) mat.SetFloat("_SupportDecals", 0f);
            mat.EnableKeyword("_DISABLE_DECALS");
            Debug.Log($"material {baseName}: albedo={(alb ? alb.name : "-")} normal={(nrm ? nrm.name : "-")}");
            return mat;
        }

        // Overlay a smooth vertical (Y) curve on a transform path, from startY at t=0 to
        // endY at the clip end. Used to synthesise the jump/hover/land vertical arc.
        // The skeleton root is the top-level child whose subtree holds the most transforms.
        private static Transform FindSkeletonRoot(GameObject inst)
        {
            Transform best = null;
            int bestCount = -1;
            for (int i = 0; i < inst.transform.childCount; i++)
            {
                var c = inst.transform.GetChild(i);
                int n = c.GetComponentsInChildren<Transform>(true).Length;
                if (n > bestCount) { bestCount = n; best = c; }
            }
            return best;
        }

        private static void AddHover(AnimationClip clip, string path, float startY, float endY)
        {
            if (clip == null) return;
            var curve = AnimationCurve.EaseInOut(0f, startY, Mathf.Max(0.01f, clip.length), endY);
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.y");
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            Debug.Log($"AddHover {clip.name}: {path}.y {startY:F4}->{endY:F4} over {clip.length:F2}s");
        }

        // Duplicate a clip into an editable .anim, strip its planar (XZ) translation
        // curves so it animates in place, and mark it looping.
        private static AnimationClip MakeInPlace(AnimationClip src, string assetName, bool loop)
        {
            if (src == null) return null;
            var copy = UnityEngine.Object.Instantiate(src);
            copy.name = assetName;
            int removed = 0;
            foreach (var b in AnimationUtility.GetCurveBindings(copy))
            {
                if (b.type != typeof(Transform)) continue;
                if (b.propertyName != "m_LocalPosition.x" && b.propertyName != "m_LocalPosition.z") continue;
                var curve = AnimationUtility.GetEditorCurve(copy, b);
                if (curve == null || curve.length == 0) continue;
                float min = float.MaxValue, max = float.MinValue;
                foreach (var k in curve.keys) { if (k.value < min) min = k.value; if (k.value > max) max = k.value; }
                if (max - min > 0.001f)
                {
                    AnimationUtility.SetEditorCurve(copy, b, null);
                    removed++;
                    Debug.Log($"  strip travel {b.path}.{b.propertyName} range={max - min:F4}");
                }
            }
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            AssetDatabase.CreateAsset(copy, AnimDir + "/" + assetName + ".anim");
            Debug.Log($"in-place {assetName}: stripped {removed} planar-travel curves, loop={loop}");
            return copy;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
