using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace Womenace.EditorTools
{
    /// <summary>
    /// Bakes an animated scenery prop (a skinned GLB with clips) into a prefab for a baked-lighting
    /// room: Womenace/BakedScene material lit by a flat ambient level (it has no lightmap, where GFL2
    /// lights such a prop from its light probes) and one of the room's reflection probes, an
    /// AnimatorController whose default state loops and whose other states play once and return to
    /// it (an Animator.Play("Click") from code), and a BoxCollider around the mesh so the prop can be
    /// clicked.
    ///
    /// Batchmode:
    ///   -executeMethod Womenace.EditorTools.BakeAnimatedProp.BakeBatch
    ///   -glbPath Assets/Authored/environment/dinergate/dinergate.glb
    ///   -outputDir Assets/Prefabs -outputName environment/dinergate
    ///   -base .../d.png -normal .../n.png [-emission .../emission.png -emissionStrength 4]
    ///   -smoothness 0.63 -metallic 0.7
    ///   -reflection Assets/Prefabs/environment/ops_room/probes/shandiangou.asset -reflectionIntensity 0.38
    ///   -ambient 0.45 -defaultClip Idle
    /// </summary>
    public static class BakeAnimatedProp
    {
        private static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return fallback;
        }

        private static float FloatArg(string name, float fallback) =>
            float.TryParse(Arg(name, null), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public static void BakeBatch()
        {
            try
            {
                Bake();
                Debug.Log("BakeAnimatedProp: success.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("BakeAnimatedProp failed: " + ex);
                EditorApplication.Exit(1);
            }
        }

        private static void Bake()
        {
            var glbPath = Arg("-glbPath", null) ?? throw new ArgumentException("-glbPath is required");
            var outputName = Arg("-outputName", null) ?? throw new ArgumentException("-outputName is required");
            var outDir = (Arg("-outputDir", "Assets/Prefabs").TrimEnd('/') + "/" + outputName).Replace('\\', '/');
            Directory.CreateDirectory(outDir);

            var baseTex = Texture(Arg("-base", null), srgb: true, normal: false);
            var normalTex = Texture(Arg("-normal", null), srgb: false, normal: true);
            var emissionTex = Texture(Arg("-emission", null), srgb: true, normal: false);
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(Arg("-reflection", ""));

            AssetDatabase.ImportAsset(glbPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath)
                ?? throw new InvalidOperationException("GLB did not import as a GameObject: " + glbPath);
            var clips = AssetDatabase.LoadAllAssetsAtPath(glbPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            if (clips.Length == 0)
                throw new InvalidOperationException("GLB has no animation clips: " + glbPath);

            // Controller: the default clip loops, and every other clip plays once and returns to it.
            var defaultName = Arg("-defaultClip", "Idle");
            var controllerPath = outDir + "/controller.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            AnimatorState home = null;
            foreach (var clip in clips)
            {
                var state = machine.AddState(clip.name);
                state.motion = clip;
                if (clip.name == defaultName)
                    home = state;
            }
            if (home == null)
                throw new InvalidOperationException("No clip named " + defaultName + " among " + string.Join(", ", clips.Select(c => c.name)));
            machine.defaultState = home;
            foreach (var child in machine.states)
            {
                if (child.state == home) continue;
                var back = child.state.AddTransition(home);
                back.hasExitTime = true;
                back.exitTime = 0.95f;
                back.duration = 0.15f;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            try
            {
                instance.name = Path.GetFileName(outputName);
                foreach (var stray in instance.GetComponentsInChildren<Animation>(true))
                    UnityEngine.Object.DestroyImmediate(stray);
                var animator = instance.GetComponentInChildren<Animator>(true) ?? instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/BakedScene.shader")
                    ?? throw new InvalidOperationException("Assets/Shaders/BakedScene.shader not found");
                var mat = new Material(shader) { name = instance.name };
                mat.SetTexture("_BaseMap", baseTex);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetTexture("_Lightmap", Texture2D.whiteTexture);
                mat.SetFloat("_LightmapScale", FloatArg("-ambient", 0.45f));
                mat.SetFloat("_Smoothness", FloatArg("-smoothness", 0.63f));
                mat.SetFloat("_Metallic", FloatArg("-metallic", 0.7f));
                if (normalTex != null)
                {
                    mat.SetTexture("_NormalMap", normalTex);
                    mat.SetFloat("_BumpScale", 1f);
                }
                if (cube != null)
                {
                    mat.SetTexture("_ReflectionCube", cube);
                    mat.SetFloat("_ReflectionIntensity", FloatArg("-reflectionIntensity", 0.38f));
                }
                if (emissionTex != null)
                {
                    mat.SetTexture("_GlowMap", emissionTex);
                    mat.SetColor("_GlowColor", Color.white * FloatArg("-emissionStrength", 4f));
                }
                mat.SetFloat("_CullMode", (float)CullMode.Back);
                var matPath = outDir + "/material.mat";
                AssetDatabase.DeleteAsset(matPath);
                AssetDatabase.CreateAsset(mat, matPath);

                var bounds = new Bounds();
                var first = true;
                foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var slots = r.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++) slots[i] = mat;
                    r.sharedMaterials = slots;
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                    if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
                }
                // Click target around the whole prop, in the root's space.
                var box = instance.AddComponent<BoxCollider>();
                box.center = instance.transform.InverseTransformPoint(bounds.center);
                box.size = bounds.size;

                var prefabPath = outDir + "/main.prefab";
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("BakeAnimatedProp: wrote " + prefabPath + " (clips: " + string.Join(", ", clips.Select(c => c.name))
                    + "; bounds " + bounds.size + ").");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Texture2D Texture(string path, bool srgb, bool normal)
        {
            if (string.IsNullOrEmpty(path)) return null;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = srgb && !normal;
                importer.anisoLevel = 8;
                importer.filterMode = FilterMode.Trilinear;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
