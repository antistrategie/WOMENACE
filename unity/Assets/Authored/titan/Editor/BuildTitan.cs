using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WOMENACE.Editor
{
    // Bakes a GFL2 Titan boss (the Mecha_* family) into a prefab that runs on a
    // cloned construct animator, like the Sinbreaker. The walker animator drives
    // locomotion partly in code against the vanilla walker rig, so a differently
    // shaped body has to speak the construct contract instead.
    //
    // The source FBX is the cleaned export from VoyExport + the name-keyed weight
    // rebuild: lod0 only, one take per clip, rigid parts already skinned.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity \
    //     -executeMethod WOMENACE.Editor.BuildTitan.Build -variant support
    public static class BuildTitan
    {
        // GFL2's Titans stand at their life size times the 1.2 world factor the dolls
        // use, so they keep their canon size next to a doll. The bind-pose heights are
        // the source meshes' own extents, top of the highest part to the feet.
        private static readonly Dictionary<string, float> CanonHeight = new()
        {
            ["support"] = 5.112f,
        };
        private const float WorldFactor = 1.2f;

        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-variant");
            var variant = i >= 0 && i + 1 < args.Length ? args[i + 1] : "support";
            EditorApplication.Exit(Build(variant) ? 0 : 1);
        }

        private static bool Build(string variant)
        {
            if (!CanonHeight.TryGetValue(variant, out var canon))
            {
                Debug.LogError($"titan: no canon height for '{variant}'");
                return false;
            }
            var srcDir = $"Assets/Authored/titan/{variant}";
            var fbx = $"{srcDir}/titan_{variant}.fbx";
            var animDir = $"Assets/Prefabs/titan/_bake/{variant}";
            var prefabPath = $"Assets/Prefabs/titan/{variant}/main.prefab";
            try
            {
                EnsureFolder(animDir);
                EnsureFolder($"Assets/Prefabs/titan/{variant}");

                var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
                if (imp == null) { Debug.LogError("titan: FBX not found at " + fbx); return false; }
                imp.animationType = ModelImporterAnimationType.Generic;
                imp.importAnimation = true;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.useFileScale = true;
                imp.globalScale = 1f;
                AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceUpdate);

                // Solve the import scale from the measured bind pose, then bake it into
                // the mesh and skeleton so the prefab root stays at scale 1 like a
                // vanilla vehicle. The clips import from the same FBX and scale with it.
                var measured = MeasureHeight(fbx);
                var target = canon * WorldFactor;
                imp.globalScale = target / measured;
                var defs = imp.defaultClipAnimations;
                foreach (var c in defs)
                    if (ClipKey(c.name).EndsWith("slg_Idle", StringComparison.OrdinalIgnoreCase)
                        || ClipKey(c.name).EndsWith("slg_Walk", StringComparison.OrdinalIgnoreCase)
                        || ClipKey(c.name).EndsWith("slg_Run", StringComparison.OrdinalIgnoreCase))
                        c.loopTime = true;
                imp.clipAnimations = defs;
                AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceUpdate);
                var check = MeasureHeight(fbx);
                Debug.Log($"titan {variant}: measured {measured:F3} at scale 1, target {target:F3}, globalScale {imp.globalScale:F4}, baked height {check:F3}");

                var clips = AssetDatabase.LoadAllAssetsAtPath(fbx)
                    .OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview", StringComparison.Ordinal))
                    .ToList();
                AnimationClip Find(string suffix) =>
                    clips.FirstOrDefault(c => ClipKey(c.name).EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

                var idleC = Find("slg_Idle");
                if (idleC == null)
                {
                    Debug.LogError("titan: no idle clip among " + string.Join(", ", clips.Select(c => c.name)));
                    return false;
                }
                // GFL2 numbers each skill's animation slot in BattleSkillData, and the slots
                // run Fire, Fire2, NormalSkill, NormalSkill1, UltraSkill. The Temblor's basic
                // attack is slot 1 (Strobe, the pulse) and 2 (Pierce, the shot), and Thermal
                // Overload is slot 4.
                var walkC = Find("slg_Walk");
                var pierceC = Find("slg_Fire2"); var strobeC = Find("slg_Fire");
                var thermalC = Find("slg_NormalSkill1");
                var stunC = Find("slg_Stun"); var dieC = Find("slg_Die");
                var turnLC = Find("slg_TurnL_90"); var turnRC = Find("slg_TurnR_90");
                Debug.Log($"titan {variant}: {clips.Count} clips, pierce={pierceC?.name} strobe={strobeC?.name} thermal={thermalC?.name}");

                // The Titan's travel is keyed on the top skeleton bone, so stripping its
                // planar curves leaves MENACE in charge of the tile move. Every other bone
                // keeps its translation, which carries the hull's sway and stomp.
                var walk = MakeInPlace(walkC, animDir, $"titan_{variant}_walk_inplace", true);
                var pierce = MakeInPlace(pierceC, animDir, $"titan_{variant}_pierce_inplace", false);
                var strobe = MakeInPlace(strobeC, animDir, $"titan_{variant}_strobe_inplace", false);
                var thermal = MakeInPlace(thermalC, animDir, $"titan_{variant}_thermal_inplace", false);
                var die = MakeInPlace(dieC, animDir, $"titan_{variant}_die_inplace", false);
                if (walk != null) AddFootsteps(fbx, walk);

                var controllerPath = $"{animDir}/titan_{variant}.controller";
                var ac = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                // Every parameter the construct driver writes, so no Set call misses and
                // leaves the unit frozen.
                ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
                ac.AddParameter("Movement_Initialized", AnimatorControllerParameterType.Bool);
                ac.AddParameter("Aiming", AnimatorControllerParameterType.Bool);
                ac.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
                ac.AddParameter("Vault", AnimatorControllerParameterType.Trigger);
                ac.AddParameter("Overloaded", AnimatorControllerParameterType.Bool);
                ac.AddParameter("Rotation", AnimatorControllerParameterType.Float);
                ac.AddParameter("Shoot_Single", AnimatorControllerParameterType.Trigger);
                // Skills with AnimationType SpecialAttack1/2 pull these natively.
                ac.AddParameter("Special_Attack_1", AnimatorControllerParameterType.Trigger);
                ac.AddParameter("Special_Attack_2", AnimatorControllerParameterType.Trigger);
                // DeathBehaviour DeathAnimations fires this trigger and nothing else
                // (ElementAnimator.OnDeath, the ON_DEATH_ID hash).
                ac.AddParameter("OnDeath", AnimatorControllerParameterType.Trigger);
                var sm = ac.layers[0].stateMachine;

                var idle = sm.AddState("Idle"); idle.motion = idleC; sm.defaultState = idle;
                var move = sm.AddState("Walk"); move.motion = walk ?? idleC;
                // Selecting a skill sets Aiming, so the ready pose has to loop.
                var aim = sm.AddState("Aim"); aim.motion = idleC;
                var pierceS = sm.AddState("Pierce"); pierceS.motion = pierce ?? idleC;
                var strobeS = sm.AddState("Strobe"); strobeS.motion = strobe ?? idleC;
                var thermalS = sm.AddState("ThermalOverload"); thermalS.motion = thermal ?? idleC;
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
                Any(pierceS, "Shoot_Single", aim, 0.9f);
                Any(strobeS, "Special_Attack_1", aim, 0.95f);
                Any(thermalS, "Special_Attack_2", aim, 0.95f);
                // No Hit or Vault state: the Titan shrugs off hits and never vaults, and
                // a state on either trigger would yank it out of its walk.
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
                inst.name = $"titan_{variant}";
                var anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                anim.runtimeAnimatorController = ac;
                anim.applyRootMotion = false;
                var avatar = AvatarBuilder.BuildGenericAvatar(inst, "");
                avatar.name = $"titan_{variant}_avatar";
                AssetDatabase.CreateAsset(avatar, $"{animDir}/titan_{variant}_avatar.asset");
                anim.avatar = avatar;

                var shader = Shader.Find("Menace/character") ?? Shader.Find("Standard");
                var mask = MatteMask(animDir);
                var cache = new Dictionary<string, Material>();
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    var mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    var baseName = TextureSet(mesh != null ? mesh.name : r.name);
                    if (!cache.TryGetValue(baseName, out var mat))
                        cache[baseName] = mat = BuildMaterial(srcDir, animDir, baseName, shader, mask);
                    var arr = new Material[Mathf.Max(1, mesh != null ? mesh.subMeshCount : 1)];
                    for (var s = 0; s < arr.Length; s++) arr[s] = mat;
                    r.sharedMaterials = arr;
                    // Ripped skins carry bad local bounds and get frustum culled.
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                    // The character import leaves some renderers on an HDRP decal layer,
                    // which projects ground decals onto the hull.
                    r.renderingLayerMask = 1;
                }

                var bones = inst.GetComponentsInChildren<Transform>(true);
                var gun = bones.FirstOrDefault(t => t.name == "Gun_Jnt");
                if (gun != null) AddMuzzle(inst, gun);
                else Debug.LogWarning("titan: no Gun_Jnt for the muzzle anchor");

                // Self-centred pulses (MuzzleType Damage, as on the vanilla scarecrow
                // attack) apply from a child named "damage". Without it the skill logs a
                // missing muzzle and deals nothing. It rides the hull so the pulse leaves
                // the body.
                var hull = bones.FirstOrDefault(t => t.name == "Root_M");
                var pulse = new GameObject("damage").transform;
                pulse.SetParent(hull != null ? hull : inst.transform, false);
                Debug.Log($"titan {variant}: damage anchor at {pulse.position}");

                // Vanilla walkers crush props through a solid box named ColliderDestroyProps
                // directly under the root, wrapping the body.
                var body = BindPoseBounds(inst);
                var crush = new GameObject("ColliderDestroyProps");
                crush.transform.SetParent(inst.transform, false);
                var box = crush.AddComponent<BoxCollider>();
                box.center = body.center;
                box.size = body.size;
                Debug.Log($"titan {variant}: ColliderDestroyProps center={body.center} size={body.size}");

                var head = bones.FirstOrDefault(t => t.name == "Head_M");
                Debug.Log($"titan {variant}: facing check head={head?.position} gun={gun?.position} (the gun should sit at +z)");

                PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                UnityEngine.Object.DestroyImmediate(inst);
                if (pierce != null) LogMuzzleMidBurst(prefabPath, pierce);
                AssetDatabase.SaveAssets();
                Debug.Log("titan: wrote " + prefabPath);
                return true;
            }
            catch (Exception ex) { Debug.LogError("BuildTitan failed: " + ex); return false; }
        }

        // World height of the bind pose, from the skinned vertices themselves.
        internal static float MeasureHeight(string fbx)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            try { return BindPoseBounds(inst).size.y; }
            finally { UnityEngine.Object.DestroyImmediate(inst); }
        }

        // Bounds of the skinned vertices in the space of the instance root, which sits
        // at the origin unrotated.
        internal static Bounds BindPoseBounds(GameObject inst)
        {
            var bounds = new Bounds();
            var first = true;
            var baked = new Mesh();
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.BakeMesh(baked, true);
                var m = inst.transform.worldToLocalMatrix
                    * Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                foreach (var v in baked.vertices)
                {
                    var p = m.MultiplyPoint3x4(v);
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
            }
            UnityEngine.Object.DestroyImmediate(baked);
            return bounds;
        }

        // Blender names its FBX takes "<object>|<action>|<layer>", and the action
        // already carries the armature prefix, so the clip is the second-to-last part.
        internal static string ClipKey(string take)
        {
            var parts = take.Split('|');
            return parts.Length > 1 ? parts[^2] : take;
        }

        private static string TextureSet(string meshName)
        {
            var i = meshName.IndexOf("_lod", StringComparison.OrdinalIgnoreCase);
            return i > 0 ? meshName[..i] : meshName;
        }

        private static Material BuildMaterial(string srcDir, string animDir, string baseName, Shader shader, Texture2D mask)
        {
            var alb = LoadTex($"{srcDir}/textures/{baseName}_d.png", sRGB: true, isNormal: false);
            var nrm = LoadTex($"{srcDir}/textures/{baseName}_n.png", sRGB: false, isNormal: true);
            var mat = new Material(shader) { name = "titan_" + baseName };
            if (alb != null) foreach (var p in new[] { "_BaseMap", "_BaseColorMap", "_MainTex" }) if (mat.HasProperty(p)) mat.SetTexture(p, alb);
            if (nrm != null) foreach (var p in new[] { "_NormalMap", "_BumpMap", "_Normal" }) if (mat.HasProperty(p)) mat.SetTexture(p, nrm);
            foreach (var p in new[] { "_MaskMap", "_Mask", "_MetallicGlossMap" }) if (mat.HasProperty(p)) mat.SetTexture(p, mask);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_SupportDecals")) mat.SetFloat("_SupportDecals", 0f);
            mat.EnableKeyword("_DISABLE_DECALS");
            AssetDatabase.CreateAsset(mat, $"{animDir}/{mat.name}.mat");
            Debug.Log($"titan material {baseName}: albedo={(alb ? alb.name : "-")} normal={(nrm ? nrm.name : "-")}");
            return mat;
        }

        // HDRP mask layout R=metallic, G=AO, B=detail, A=smoothness. Without one the
        // shader reads a white mask, which is full chrome.
        internal static Texture2D MatteMask(string animDir)
        {
            var path = $"{animDir}/titan_matte_mask.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
            var px = new Color32[16];
            for (var i = 0; i < px.Length; i++) px[i] = new Color32(0, 255, 0, 40);
            t.SetPixels32(px); t.Apply();
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        internal static Texture2D LoadTex(string path, bool sRGB, bool isNormal)
        {
            if (!System.IO.File.Exists(path)) return null;
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                var tt = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (imp.textureType != tt || imp.sRGBTexture != sRGB)
                {
                    imp.textureType = tt; imp.sRGBTexture = sRGB;
                    imp.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Copy a clip and strip the planar travel on the top skeleton bone only.
        internal static AnimationClip MakeInPlace(AnimationClip src, string animDir, string assetName, bool loop)
        {
            if (src == null) return null;
            var copy = UnityEngine.Object.Instantiate(src);
            copy.name = assetName;
            var removed = 0;
            foreach (var b in AnimationUtility.GetCurveBindings(copy))
            {
                if (b.type != typeof(Transform)) continue;
                if (b.propertyName != "m_LocalPosition.x" && b.propertyName != "m_LocalPosition.z") continue;
                var leaf = b.path[(b.path.LastIndexOf('/') + 1)..];
                if (leaf != "root") continue;
                AnimationUtility.SetEditorCurve(copy, b, null);
                removed++;
            }
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            AssetDatabase.CreateAsset(copy, $"{animDir}/{assetName}.anim");
            Debug.Log($"in-place {assetName}: stripped {removed} root travel curves, loop={loop}");
            return copy;
        }

        // Fire skills spawn their muzzle flash on a child named "muzzle", facing its +Z.
        // The gun bone carries the barrel, so the anchor goes at the far end of the
        // vertices it carries: the barrel's longest local axis, on the side away from the
        // grip.
        internal static void AddMuzzle(GameObject inst, Transform gun, string gripName = "Grip_Jnt")
        {
            var points = new List<Vector3>();
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                var gunIndex = Array.IndexOf(smr.bones, gun);
                if (mesh == null || gunIndex < 0)
                    continue;
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                var verts = baked.vertices;
                var weights = mesh.boneWeights;
                for (var i = 0; i < verts.Length && i < weights.Length; i++)
                    if (weights[i].boneIndex0 == gunIndex && weights[i].weight0 > 0.5f || weights[i].weight0 == 0f && gunIndex == 0)
                        points.Add(gun.InverseTransformPoint(smr.transform.TransformPoint(verts[i])));
                UnityEngine.Object.DestroyImmediate(baked);
            }
            var muzzle = new GameObject("muzzle").transform;
            muzzle.SetParent(gun, false);
            if (points.Count == 0)
            {
                Debug.LogWarning($"muzzle: no vertices on {gun.name}, muzzle left at the pivot");
                return;
            }
            var min = points.Aggregate(Vector3.Min);
            var max = points.Aggregate(Vector3.Max);
            var extent = max - min;
            var axis = extent.x >= extent.y && extent.x >= extent.z ? 0 : extent.y >= extent.z ? 1 : 2;
            // The grip sits at the breech end, so the tip is the end farther from it.
            var grip = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == gripName);
            var gripLocal = grip != null ? gun.InverseTransformPoint(grip.position)[axis] : min[axis];
            var tipAtMax = Mathf.Abs(max[axis] - gripLocal) >= Mathf.Abs(min[axis] - gripLocal);
            var tipEnd = tipAtMax ? max[axis] : min[axis];
            var band = extent[axis] * 0.04f;
            var tip = points.Where(p => Mathf.Abs(p[axis] - tipEnd) <= band).Aggregate(Vector3.zero, (a, p) => a + p);
            tip /= points.Count(p => Mathf.Abs(p[axis] - tipEnd) <= band);
            var dir = Vector3.zero;
            dir[axis] = tipAtMax ? 1f : -1f;
            muzzle.localPosition = tip;
            muzzle.localRotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up);
            Debug.Log($"muzzle: at {muzzle.position} facing {muzzle.forward} ({points.Count} barrel vertices, axis {axis})");
        }

        // The burst fires from 1.27 s, so the muzzle should point level and forward there.
        private static void LogMuzzleMidBurst(string prefabPath, AnimationClip pierce)
        {
            var check = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            try
            {
                var muzzle = check.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "muzzle");
                foreach (var time in new[] { 1.3f, 2.0f, 2.6f })
                {
                    pierce.SampleAnimation(check, time);
                    Debug.Log($"titan: pierce t={time:0.0} muzzle at {muzzle?.position} facing {muzzle?.forward}");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(check); }
        }

        // MovementStep, the event MENACE's own walk clips raise on every foot plant
        // to play the unit's SoundOnMovementStep (Menace.Tactical.AnimationEvent).
        private const int MovementStep = 4;

        // The GFL2 walk carries no events, so a step goes where each ankle comes
        // down to its lowest band after a swing. SetAnimationEvents replaces, so
        // re-runs are idempotent.
        internal static void AddFootsteps(string fbx, AnimationClip walk)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            try
            {
                var bones = inst.GetComponentsInChildren<Transform>(true);
                var events = new List<AnimationEvent>();
                foreach (var name in new[] { "Ankle_L", "Ankle_R" })
                {
                    var ankle = bones.FirstOrDefault(t => t.name == name);
                    if (ankle == null) { Debug.LogWarning($"footsteps: no {name}"); continue; }
                    const int samples = 120;
                    var heights = new float[samples];
                    for (var i = 0; i < samples; i++)
                    {
                        walk.SampleAnimation(inst, walk.length * i / samples);
                        heights[i] = ankle.position.y;
                    }
                    float lo = heights.Min(), hi = heights.Max(), range = hi - lo;
                    if (range < 0.01f) continue;
                    // Start lifted so a plant at frame 0 of the loop is still caught on wrap.
                    var lifted = heights[samples - 1] > lo + range * 0.5f;
                    for (var i = 0; i < samples; i++)
                    {
                        if (heights[i] > lo + range * 0.5f) lifted = true;
                        else if (lifted && heights[i] <= lo + range * 0.08f)
                        {
                            lifted = false;
                            events.Add(new AnimationEvent { time = walk.length * i / samples, functionName = "OnAnimationEvent", intParameter = MovementStep });
                        }
                    }
                }
                events.Sort((a, b) => a.time.CompareTo(b.time));
                AnimationUtility.SetAnimationEvents(walk, events.ToArray());
                EditorUtility.SetDirty(walk);
                Debug.Log($"footsteps: {events.Count} event(s) on {walk.name} at [{string.Join(", ", events.Select(e => e.time.ToString("0.00")))}] of {walk.length:0.00}s");
            }
            finally { UnityEngine.Object.DestroyImmediate(inst); }
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
