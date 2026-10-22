using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WOMENACE.Editor
{
    // Bakes GFL2's Oljefat field boss into a prefab on the construct animator contract,
    // the same way BuildTitan bakes the Titans, and through the same helpers.
    //
    // The source FBX is the cleaned VoyExport take set: lod0 only, one take per clip.
    // The flamethrower is a second submesh of the body, skinned whole to Wrist1_R_skin.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity \
    //     -executeMethod WOMENACE.Editor.BuildOljefat.Build
    public static class BuildOljefat
    {
        // The bind pose's own height, the boss at life size, times the dolls' 1.2 world
        // factor.
        private const float CanonHeight = 1.78f;
        private const float WorldFactor = 1.2f;
        private const string GunBone = "Wrist1_R_skin";
        private const string ThrowHand = "Wrist_L";

        public static void Build() => EditorApplication.Exit(Bake() ? 0 : 1);

        private static bool Bake()
        {
            const string srcDir = "Assets/Authored/oljefat/default";
            const string fbx = srcDir + "/oljefat_default.fbx";
            const string animDir = "Assets/Prefabs/oljefat/_bake/default";
            const string prefabPath = "Assets/Prefabs/oljefat/default/main.prefab";
            try
            {
                BuildTitan.EnsureFolder(animDir);
                BuildTitan.EnsureFolder("Assets/Prefabs/oljefat/default");

                var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
                if (imp == null) { Debug.LogError("oljefat: FBX not found at " + fbx); return false; }
                imp.animationType = ModelImporterAnimationType.Generic;
                imp.importAnimation = true;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.useFileScale = true;
                imp.globalScale = 1f;
                AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceUpdate);

                var measured = BuildTitan.MeasureHeight(fbx);
                var target = CanonHeight * WorldFactor;
                imp.globalScale = target / measured;
                var defs = imp.defaultClipAnimations;
                foreach (var c in defs)
                {
                    var key = BuildTitan.ClipKey(c.name);
                    if (key.EndsWith("slg_Idle", StringComparison.OrdinalIgnoreCase) || key.EndsWith("slg_Walk", StringComparison.OrdinalIgnoreCase))
                        c.loopTime = true;
                }
                imp.clipAnimations = defs;
                AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceUpdate);
                Debug.Log($"oljefat: measured {measured:F3} at scale 1, target {target:F3}, globalScale {imp.globalScale:F4}, baked height {BuildTitan.MeasureHeight(fbx):F3}");

                var clips = AssetDatabase.LoadAllAssetsAtPath(fbx)
                    .OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview", StringComparison.Ordinal))
                    .ToList();
                AnimationClip Find(string suffix) =>
                    clips.FirstOrDefault(c => BuildTitan.ClipKey(c.name).EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

                var idleC = Find("slg_Idle");
                if (idleC == null)
                {
                    Debug.LogError("oljefat: no idle clip among " + string.Join(", ", clips.Select(c => c.name)));
                    return false;
                }
                // GFL2's BattleSkillData gives Flashpoint Overdrive animation slot 1 (Fire1,
                // a raised-arm throw) and Thermal Sterilization slot 2 (NormalSkill, the
                // braced spray).
                var flameC = Find("slg_NormalSkill");
                var throwC = Find("slg_Fire1");
                var walk = BuildTitan.MakeInPlace(Find("slg_Walk"), animDir, "oljefat_walk_inplace", true);
                var flame = BuildTitan.MakeInPlace(flameC, animDir, "oljefat_flame_inplace", false);
                var toss = BuildTitan.MakeInPlace(throwC, animDir, "oljefat_throw_inplace", false);
                var die = BuildTitan.MakeInPlace(Find("slg_Die"), animDir, "oljefat_die_inplace", false);
                var stunC = Find("slg_Stun");
                var turnLC = Find("slg_TurnL_90"); var turnRC = Find("slg_TurnR_90");
                Debug.Log($"oljefat: {clips.Count} clips, flame={flameC?.name} throw={throwC?.name}");
                if (walk != null) BuildTitan.AddFootsteps(fbx, walk);

                var ac = AnimatorController.CreateAnimatorControllerAtPath($"{animDir}/oljefat.controller");
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
                // DeathBehaviour DeathAnimations fires this trigger and nothing else
                // (ElementAnimator.OnDeath, the ON_DEATH_ID hash).
                ac.AddParameter("OnDeath", AnimatorControllerParameterType.Trigger);
                var sm = ac.layers[0].stateMachine;

                var idle = sm.AddState("Idle"); idle.motion = idleC; sm.defaultState = idle;
                var move = sm.AddState("Walk"); move.motion = walk ?? idleC;
                var aim = sm.AddState("Aim"); aim.motion = idleC;
                var flameS = sm.AddState("Flame"); flameS.motion = flame ?? idleC;
                var throwS = sm.AddState("Throw"); throwS.motion = toss ?? idleC;
                var overload = sm.AddState("Overload"); overload.motion = stunC ?? idleC;
                var death = sm.AddState("Death"); death.motion = die ?? idleC;
                var turnL = sm.AddState("TurnL"); turnL.motion = turnLC ?? idleC;
                var turnR = sm.AddState("TurnR"); turnR.motion = turnRC ?? idleC;

                void T(AnimatorState a, AnimatorState b, AnimatorConditionMode m, float v, string p, float blend = 0.15f)
                {
                    var t = a.AddTransition(b); t.hasExitTime = false; t.duration = blend;
                    t.AddCondition(m, v, p);
                }
                void TExit(AnimatorState a, AnimatorState b, float exit)
                {
                    var t = a.AddTransition(b); t.hasExitTime = true; t.exitTime = exit; t.duration = 0.15f;
                }
                void Any(AnimatorState s, string trigger, AnimatorState after, float exit)
                {
                    var t = sm.AddAnyStateTransition(s); t.hasExitTime = false; t.duration = 0.08f;
                    t.AddCondition(AnimatorConditionMode.If, 0, trigger);
                    TExit(s, after, exit);
                }

                T(idle, move, AnimatorConditionMode.Greater, 0.1f, "Speed", 0.25f);
                T(move, idle, AnimatorConditionMode.Less, 0.1f, "Speed", 0.25f);
                T(idle, aim, AnimatorConditionMode.If, 0, "Aiming");
                T(aim, idle, AnimatorConditionMode.IfNot, 0, "Aiming");
                Any(flameS, "Special_Attack_1", aim, 0.95f);
                Any(throwS, "Special_Attack_2", aim, 0.95f);
                T(idle, overload, AnimatorConditionMode.If, 0, "Overloaded");
                T(overload, idle, AnimatorConditionMode.IfNot, 0, "Overloaded");
                T(idle, turnR, AnimatorConditionMode.Greater, 0.5f, "Rotation");
                T(idle, turnL, AnimatorConditionMode.Less, -0.5f, "Rotation");
                TExit(turnR, idle, 0.85f);
                TExit(turnL, idle, 0.85f);
                var dying = sm.AddAnyStateTransition(death);
                dying.hasExitTime = false; dying.duration = 0.1f; dying.canTransitionToSelf = false;
                dying.AddCondition(AnimatorConditionMode.If, 0, "OnDeath");

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
                inst.name = "oljefat_default";
                var anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                anim.runtimeAnimatorController = ac;
                anim.applyRootMotion = false;
                var avatar = AvatarBuilder.BuildGenericAvatar(inst, "");
                avatar.name = "oljefat_default_avatar";
                AssetDatabase.CreateAsset(avatar, $"{animDir}/oljefat_default_avatar.asset");
                anim.avatar = avatar;

                var shader = Shader.Find("Menace/character") ?? Shader.Find("Standard");
                var mask = BuildTitan.MatteMask(animDir);
                var body = BuildMaterial(srcDir, animDir, "c_FB_Oljefat_slg", shader, mask);
                var weapon = BuildMaterial(srcDir, animDir, "cw_FB_Oljefat_Weapon", shader, mask);
                var bones = inst.GetComponentsInChildren<Transform>(true);
                var gun = bones.FirstOrDefault(t => t.name == GunBone);
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    var smr = r as SkinnedMeshRenderer;
                    var mesh = smr != null ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    var count = Mathf.Max(1, mesh != null ? mesh.subMeshCount : 1);
                    var arr = new Material[count];
                    var gunIndex = smr != null && gun != null ? Array.IndexOf(smr.bones, gun) : -1;
                    for (var s = 0; s < count; s++)
                    {
                        var isGun = mesh != null && gunIndex >= 0 && SubmeshOnBone(mesh, s, gunIndex);
                        arr[s] = isGun ? weapon : body;
                        Debug.Log($"oljefat: {r.name} submesh {s} -> {(isGun ? "weapon" : "body")}");
                    }
                    r.sharedMaterials = arr;
                    if (smr != null) smr.updateWhenOffscreen = true;
                    r.renderingLayerMask = 1;
                }

                if (gun != null) BuildTitan.AddMuzzle(inst, gun, "Wrist_R");
                else Debug.LogWarning($"oljefat: no {GunBone} for the muzzle anchor");
                // The incendiary leaves the raised left hand (MuzzleType Muzzle2), since the
                // throw clip points the flamer at the ground.
                var hand = bones.FirstOrDefault(t => t.name == ThrowHand);
                if (hand != null) new GameObject("muzzle2").transform.SetParent(hand, false);
                else Debug.LogWarning($"oljefat: no {ThrowHand} for the throw anchor");

                var hull = BuildTitan.BindPoseBounds(inst);
                var crush = new GameObject("ColliderDestroyProps");
                crush.transform.SetParent(inst.transform, false);
                var box = crush.AddComponent<BoxCollider>();
                box.center = hull.center;
                box.size = hull.size;
                Debug.Log($"oljefat: ColliderDestroyProps center={hull.center} size={hull.size}");

                PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                UnityEngine.Object.DestroyImmediate(inst);
                if (flame != null) LogNozzle(prefabPath, flame);
                if (toss != null) LogNozzle(prefabPath, toss);
                AssetDatabase.SaveAssets();
                Debug.Log("oljefat: wrote " + prefabPath);
                return true;
            }
            catch (Exception ex) { Debug.LogError("BuildOljefat failed: " + ex); return false; }
        }

        private static bool SubmeshOnBone(Mesh mesh, int submesh, int boneIndex)
        {
            var weights = mesh.boneWeights;
            var tris = mesh.GetTriangles(submesh);
            if (tris.Length == 0) return false;
            var on = tris.Count(i => weights[i].boneIndex0 == boneIndex && weights[i].weight0 > 0.5f);
            return on > tris.Length * 0.9f;
        }

        private static Material BuildMaterial(string srcDir, string animDir, string baseName, Shader shader, Texture2D mask)
        {
            var alb = BuildTitan.LoadTex($"{srcDir}/textures/{baseName}_d.png", sRGB: true, isNormal: false);
            var nrm = BuildTitan.LoadTex($"{srcDir}/textures/{baseName}_n.png", sRGB: false, isNormal: true);
            var mat = new Material(shader) { name = "oljefat_" + baseName };
            if (alb != null) foreach (var p in new[] { "_BaseMap", "_BaseColorMap", "_MainTex" }) if (mat.HasProperty(p)) mat.SetTexture(p, alb);
            if (nrm != null) foreach (var p in new[] { "_NormalMap", "_BumpMap", "_Normal" }) if (mat.HasProperty(p)) mat.SetTexture(p, nrm);
            foreach (var p in new[] { "_MaskMap", "_Mask", "_MetallicGlossMap" }) if (mat.HasProperty(p)) mat.SetTexture(p, mask);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_SupportDecals")) mat.SetFloat("_SupportDecals", 0f);
            mat.EnableKeyword("_DISABLE_DECALS");
            AssetDatabase.CreateAsset(mat, $"{animDir}/{mat.name}.mat");
            return mat;
        }

        // Each skill's DelayAfterAnimationTrigger has to land where the nozzle is held
        // level and forward, or where the throwing hand is at its peak, so log both
        // through the clip.
        private static void LogNozzle(string prefabPath, AnimationClip clip)
        {
            var check = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            try
            {
                var all = check.GetComponentsInChildren<Transform>(true);
                var muzzle = all.FirstOrDefault(t => t.name == "muzzle");
                var hand = all.FirstOrDefault(t => t.name == "muzzle2");
                for (var time = 0f; time <= clip.length; time += 0.1f)
                {
                    clip.SampleAnimation(check, time);
                    Debug.Log($"oljefat: {clip.name} t={time:0.00} muzzle at {muzzle?.position} facing {muzzle?.forward} hand at {hand?.position}");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(check); }
        }
    }
}
