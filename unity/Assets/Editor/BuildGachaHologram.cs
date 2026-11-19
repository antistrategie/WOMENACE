using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Batchmode utility: GFL2's capsule machine (the dorm playroom's clothes gacha) as a hologram,
    // environment/gacha_machine/main, from the GLB the extraction writes with the machine's
    // crank, drum balls and capsules as moving parts and its two pull clips. Each part wears
    // Womenace/DollHologram with its own texture, for its light and dark, and the balls and
    // capsules, which have none here, a plain one. Its glass shell is left out. A node named
    // Camera carries GFL2's camera for the pull: where it stands and looks, and its lens's focal
    // length in millimetres as the node's scale along x. The clips play once from a state each (One,
    // Ten) and the machine rests in an empty state. The root is the middle of the machine's foot.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.BuildGachaHologram.Run -logFile gacha.log
    public static class BuildGachaHologram
    {
        private const string Authored = "Assets/Authored/environment/gacha_machine";
        private const string Model = Authored + "/gacha_machine.glb";
        private const string Output = "Assets/Prefabs/environment/gacha_machine";
        // The machine's glass shell: clear all over, so a hologram of it would hide the drum.
        private const string Glass = "gacha_03";

        // The texture each of the model's materials wears, by the material's name.
        private static readonly Dictionary<string, string> Textures = new Dictionary<string, string>
        {
            ["gacha_01"] = "gacha_01", ["gacha_04"] = "gacha_04",
            ["gacha_plain"] = "gacha_plain", ["gacha_egg"] = "gacha_plain",
        };

        public static void Run()
        {
            var shader = Shader.Find("Womenace/DollHologram") ?? throw new System.Exception("no shader Womenace/DollHologram");
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Model) ?? throw new System.Exception("the model did not import: " + Model);
            System.IO.Directory.CreateDirectory(Output);

            // The clips as assets of their own, played once.
            var controllerPath = Output + "/controller.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            machine.defaultState = machine.AddState("Rest");
            var named = new List<string>();
            foreach (var imported in AssetDatabase.LoadAllAssetsAtPath(Model))
            {
                if (!(imported is AnimationClip clip) || clip.name.StartsWith("__preview__"))
                    continue;
                var copy = Object.Instantiate(clip);
                copy.name = clip.name;
                var settings = AnimationUtility.GetAnimationClipSettings(copy);
                settings.loopTime = false;
                AnimationUtility.SetAnimationClipSettings(copy, settings);
                var clipPath = $"{Output}/{clip.name.ToLowerInvariant()}.anim";
                AssetDatabase.DeleteAsset(clipPath);
                AssetDatabase.CreateAsset(copy, clipPath);
                machine.AddState(clip.name).motion = copy;
                named.Add(clip.name);
            }
            if (named.Count == 0)
                throw new System.Exception("the model has no clips");

            var root = new GameObject("gacha_machine");
            var body = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            body.name = "body";
            body.transform.SetParent(root.transform, false);
            foreach (var stray in body.GetComponentsInChildren<Animation>(true))
                Object.DestroyImmediate(stray);
            if (!body.TryGetComponent<Animator>(out var animator))
                animator = body.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.name == Glass)
                    Object.DestroyImmediate(renderer.gameObject);
            var materials = new Dictionary<string, Material>();
            var renderers = body.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var worn = renderer.sharedMaterials;
                for (var i = 0; i < worn.Length; i++)
                {
                    var key = worn[i] != null && Textures.TryGetValue(worn[i].name, out var found) ? found : "gacha_plain";
                    if (!materials.TryGetValue(key, out var material))
                    {
                        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Authored}/{key}.png") ?? throw new System.Exception($"no texture {key}");
                        var path = $"{Authored}/{key}.mat";
                        material = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (material == null)
                        {
                            material = new Material(shader);
                            AssetDatabase.CreateAsset(material, path);
                        }
                        material.shader = shader;
                        material.SetTexture("_BaseMap", texture);
                        EditorUtility.SetDirty(material);
                        materials[key] = material;
                    }
                    worn[i] = material;
                }
                renderer.sharedMaterials = worn;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            body.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            PrefabUtility.SaveAsPrefabAsset(root, Output + "/main.prefab");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"BuildGachaHologram: wrote {Output}/main.prefab, {renderers.Length} parts, clips {string.Join(", ", named)}, size {bounds.size}, middle {bounds.center}.");
        }
    }
}
