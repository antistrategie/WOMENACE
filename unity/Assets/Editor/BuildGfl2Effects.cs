using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Womenace.EditorTools
{
    // Builds particle-effect prefabs from the source.json that
    // scripts/vfx/extract_gfl2_effects.py writes beside an effect spec. Each
    // effect becomes <output>/<name>.prefab; its materials and meshes are saved
    // under the source directory.
    //
    // Particle systems keep GFL2's native curves, gradients, bursts and
    // timings. Every particle material moves onto Womenace/WeaponParticles,
    // which covers GFL2's texture, mask, scroll, distortion-noise and
    // dissolve inputs but not its screen-space refraction, so refraction
    // renderers stay disabled with a valid material (MENACE inspects every
    // renderer slot when it creates a tactical element). Colours are display
    // colours, so exposure is off and the tint times brightness is capped to
    // keep GFL2's bloom-scale HDR values from washing out to white.
    //
    // Each root carries an emitter with no emission and a disabled renderer,
    // so Play, Stop and IsAlive on the root reach the whole effect, and the
    // root scale maps GFL2 world units onto MENACE's (1.2, the doll scale).
    //
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.BuildGfl2Effects.Build \
    //     -source Assets/Authored/springfield/taryz/effects \
    //     -output Assets/Prefabs/springfield/taryz -scale 1.2
    public static class BuildGfl2Effects
    {
        private const string ShaderName = "Womenace/WeaponParticles";
        private const float MaxIntensity = 8f;

        [Serializable] private sealed class Source
        {
            public Node[] effects;
            public MaterialSource[] materials;
            public MeshSource[] meshes;
            public TextureSource[] textures;
        }

        [Serializable] private sealed class Node
        {
            public string name;
            public string path;
            public bool active;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public string particleJson;
            public string rendererJson;
            public string[] materials;
            public string[] meshes;
            public string[] shapeMeshes;
            public SubEmitter[] subEmitters;
            public Node[] children;
        }

        [Serializable] private sealed class SubEmitter
        {
            public string path;
            public int type;
            public int properties;
            public float probability;
        }

        [Serializable] private sealed class MaterialSource
        {
            public string name;
            public string shader;
            public string keywords;
            public FloatValue[] floats;
            public ColourValue[] colours;
            public TextureValue[] textures;
        }

        [Serializable] private sealed class FloatValue { public string name; public float value; }
        [Serializable] private sealed class ColourValue { public string name; public Color value; }
        [Serializable] private sealed class TextureValue
        {
            public string name;
            public string texture;
            public Vector2 scale;
            public Vector2 offset;
        }

        [Serializable] private sealed class MeshSource
        {
            public string name;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Color[] colours;
            public Vector2[] uv;
            public Vector2[] uv2;
            public Submesh[] submeshes;
        }

        [Serializable] private sealed class Submesh { public int[] triangles; }

        [Serializable] private sealed class TextureSource
        {
            public string name;
            public bool sRGB;
            public bool mipmaps;
            public int filterMode;
            public int aniso;
            public int wrapU;
            public int wrapV;
            public int wrapW;
        }

        [Serializable] private sealed class ParticleCheck { public float lengthInSec; public bool looping; }

        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string key)
            {
                var i = Array.IndexOf(args, key);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            var sourceDir = Arg("-source") ?? throw new ArgumentException("-source is required.");
            var outputDir = Arg("-output") ?? throw new ArgumentException("-output is required.");
            var scale = float.Parse(Arg("-scale") ?? "1", CultureInfo.InvariantCulture);
            Build(sourceDir.TrimEnd('/'), outputDir.TrimEnd('/'), scale);
        }

        private static void Build(string sourceDir, string outputDir, float scale)
        {
            var source = JsonUtility.FromJson<Source>(File.ReadAllText(sourceDir + "/source.json"));
            var shader = Shader.Find(ShaderName) ?? throw new InvalidOperationException("Missing shader " + ShaderName);
            EnsureFolder(sourceDir + "/materials");
            EnsureFolder(sourceDir + "/meshes");
            EnsureFolder(outputDir);

            foreach (var texture in source.textures)
            {
                var path = sourceDir + "/textures/" + texture.name + ".png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path)
                    ?? throw new InvalidOperationException("Missing texture " + path);
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.sRGBTexture = texture.sRGB;
                importer.mipmapEnabled = texture.mipmaps;
                importer.filterMode = (FilterMode)texture.filterMode;
                importer.anisoLevel = texture.aniso;
                importer.wrapModeU = (TextureWrapMode)texture.wrapU;
                importer.wrapModeV = (TextureWrapMode)texture.wrapV;
                importer.wrapModeW = (TextureWrapMode)texture.wrapW;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }

            var meshes = new Dictionary<string, Mesh>();
            foreach (var item in source.meshes)
            {
                var mesh = new Mesh { name = item.name, vertices = item.vertices };
                if (item.normals.Length == item.vertices.Length) mesh.normals = item.normals;
                if (item.colours.Length == item.vertices.Length) mesh.colors = item.colours;
                if (item.uv.Length == item.vertices.Length) mesh.uv = item.uv;
                if (item.uv2.Length == item.vertices.Length) mesh.uv2 = item.uv2;
                mesh.subMeshCount = item.submeshes.Length;
                for (int i = 0; i < item.submeshes.Length; i++) mesh.SetTriangles(item.submeshes[i].triangles, i);
                mesh.RecalculateBounds();
                meshes[item.name] = SaveAsset(mesh, sourceDir + "/meshes/" + item.name + ".asset");
            }
            meshes["builtin:Quad"] = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            meshes["builtin:Cube"] = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            meshes["builtin:Sphere"] = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");

            var materials = new Dictionary<string, Material>();
            var refraction = new HashSet<string>();
            foreach (var item in source.materials)
            {
                materials[item.name] = SaveAsset(ConvertMaterial(item, shader, sourceDir), sourceDir + "/materials/" + item.name + ".mat");
                if (item.shader.Contains("distortion")) refraction.Add(item.name);
            }
            // Empty slots (an emitter that only draws trails) still need a material.
            var placeholder = new Material(shader) { name = "empty_slot" };
            placeholder.SetFloat("_Alpha", 0f);
            placeholder.SetFloat("_ExposureWeight", 0f);
            materials[""] = SaveAsset(placeholder, sourceDir + "/materials/empty_slot.mat");

            foreach (var effect in source.effects)
            {
                var root = CreateNode(effect, materials, meshes, refraction);
                try
                {
                    WireSubEmitters(root, effect);
                    AddRootEmitter(root, materials[""]);
                    root.transform.localScale = Vector3.Scale(root.transform.localScale, Vector3.one * scale);
                    foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var main = particles.main;
                        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    }
                    var renderers = root.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Any(r => r.sharedMaterials.Length == 0 || r.sharedMaterials.Any(m => m == null)))
                        throw new InvalidOperationException("Null renderer material in " + effect.name);
                    var path = outputDir + "/" + effect.name + ".prefab";
                    PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                    if (!success) throw new IOException("Could not save " + path);
                    var systems = root.GetComponentsInChildren<ParticleSystem>(true);
                    Debug.Log($"Womenace BuildGfl2Effects: {path} from {effect.name}, {systems.Length} particle systems, "
                        + $"{renderers.Count(r => !r.enabled)} disabled renderers, lasts {systems.Max(p => p.main.startDelay.constantMax + p.main.duration + MaxLifetime(p)):F2} s");
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
        }

        private static float MaxLifetime(ParticleSystem particles)
        {
            var lifetime = particles.main.startLifetime;
            return lifetime.mode switch
            {
                ParticleSystemCurveMode.Constant => lifetime.constant,
                ParticleSystemCurveMode.TwoConstants => lifetime.constantMax,
                _ => lifetime.curveMultiplier,
            };
        }

        private static Material ConvertMaterial(MaterialSource item, Shader shader, string sourceDir)
        {
            var floats = item.floats.GroupBy(f => f.name).ToDictionary(g => g.Key, g => g.First().value);
            var colours = item.colours.GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First().value);
            var keywords = new HashSet<string>((item.keywords ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
            float F(float fallback, params string[] names)
            {
                foreach (var n in names) if (floats.TryGetValue(n, out var v)) return v;
                return fallback;
            }
            bool On(string property, string keyword) => F(0f, property) > 0.5f || keywords.Contains(keyword);

            var material = new Material(shader) { name = item.name };
            foreach (var texture in item.textures)
            {
                var target = texture.name switch
                {
                    "_MainTex" => "_MainTex",
                    "_MaskTex" => "_MaskTex",
                    "_MaskTex2" => "_MaskTex2",
                    "_TurbulenceTex" => "_TurbulenceTex",
                    "_DissolveTex" => "_DissolveTex",
                    _ => null,
                };
                if (target == null || string.IsNullOrEmpty(texture.texture)) continue;
                var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(sourceDir + "/textures/" + texture.texture + ".png")
                    ?? throw new InvalidOperationException("Missing texture " + texture.texture);
                material.SetTexture(target, asset);
                material.SetTextureScale(target, texture.scale);
                material.SetTextureOffset(target, texture.offset);
            }

            // GFL2 picks its tint by shader mode: the fixed colour in mode 1, the main colour otherwise.
            // An unlit scene shader (a bullet casing) has only its base colour.
            Color tint = Color.white;
            bool fixedMode = F(item.shader.Contains("Particle_Base_Bland") ? 1f : 0f, "_ShaderMode") > 0.5f;
            if (fixedMode && colours.TryGetValue("_FixColor", out var fix)) tint = fix;
            else if (colours.TryGetValue("_MainColor", out var main)) tint = main;
            else if (colours.TryGetValue("_BaseColor", out var baseColour)) tint = baseColour;
            if (colours.TryGetValue("_TintColor", out var tintColour)) tint *= tintColour * 2f;
            float brightness = F(1f, "_Brightness");
            float peak = Mathf.Max(tint.r, tint.g, tint.b) * brightness;
            if (peak > MaxIntensity) brightness *= MaxIntensity / peak;
            tint.a = 1f;
            material.SetColor("_MainColor", tint);
            material.SetFloat("_Brightness", brightness);
            material.SetFloat("_Contrast", F(1f, "_Contrast"));
            material.SetFloat("_Alpha", F(1f, "_Alpha") * F(1f, "_AdditionalAlpha"));
            material.SetFloat("_ExposureWeight", 0f);
            material.SetFloat("_AlphaFromRed", F(0f, "_AlphaIsR", "_AlphaR") > 0.5f ? 1f : 0f);
            material.SetFloat("_UseMask", On("_UseMask", "_USEMASK_ON") ? 1f : 0f);
            material.SetFloat("_UseMask2", On("_UseMask2", "_USEMASK2_ON") ? 1f : 0f);
            material.SetFloat("_PanX", F(0f, "_MainTexPannerX", "_MainPannerX"));
            material.SetFloat("_PanY", F(0f, "_MainTexPannerY", "_MainPannerY"));
            material.SetFloat("_MaskPanX", F(0f, "_MaskPannerX", "_MaskTexPannerX"));
            material.SetFloat("_MaskPanY", F(0f, "_MaskPannerY", "_MaskTexPannerY"));
            material.SetFloat("_MaskPanX2", F(0f, "_MaskPannerX2"));
            material.SetFloat("_MaskPanY2", F(0f, "_MaskPannerY2"));
            material.SetFloat("_UseTurbulence", On("_UseTurbulence", "_USETURBULENCE_ON") ? 1f : 0f);
            material.SetFloat("_TurbulencePower", F(0f, "_TurbulencePower", "_DistortPower"));
            material.SetFloat("_TurbulencePanX", F(0f, "_TurbulencePannerX"));
            material.SetFloat("_TurbulencePanY", F(0f, "_TurbulencePannerY"));
            material.SetFloat("_UseDissolve", On("_UseDissolve", "_USE_DISSOLVE") ? 1f : 0f);
            material.SetFloat("_Dissolve", F(0f, "_Dissolve"));
            material.SetFloat("_DissolveHardness", F(0.5f, "_DissolveHardness"));
            material.SetFloat("_DissolvePanX", F(0f, "_DissolvePannerX"));
            material.SetFloat("_DissolvePanY", F(0f, "_DissolvePannerY"));
            bool opaque = item.shader.Contains("/scene/");
            material.SetFloat("_SourceBlend", opaque ? 1f : F(5f, "SrcBlend"));
            material.SetFloat("_DestinationBlend", opaque ? 0f : F(1f, "DstBlend"));
            return material;
        }

        private static GameObject CreateNode(Node source, Dictionary<string, Material> materials,
            Dictionary<string, Mesh> meshes, HashSet<string> refraction)
        {
            var go = new GameObject(source.name);
            go.SetActive(false);
            go.transform.localPosition = source.position;
            go.transform.localRotation = source.rotation;
            go.transform.localScale = source.scale;
            if (!string.IsNullOrEmpty(source.particleJson))
            {
                var particles = go.AddComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                EditorJsonUtility.FromJsonOverwrite("{\"ParticleSystem\":" + source.particleJson + "}", particles);
                var expected = JsonUtility.FromJson<ParticleCheck>(source.particleJson);
                if (!Mathf.Approximately(particles.main.duration, expected.lengthInSec) || particles.main.loop != expected.looping)
                    throw new InvalidOperationException("Particle settings did not import for " + source.path);
                if (source.shapeMeshes.Length > 0)
                {
                    var shape = particles.shape;
                    shape.mesh = meshes.TryGetValue(source.shapeMeshes[0], out var shapeMesh)
                        ? shapeMesh : throw new InvalidOperationException("Missing shape mesh " + source.shapeMeshes[0]);
                }
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                if (!string.IsNullOrEmpty(source.rendererJson))
                {
                    EditorJsonUtility.FromJsonOverwrite("{\"ParticleSystemRenderer\":" + source.rendererJson + "}", renderer);
                    renderer.sharedMaterials = source.materials.Select(name => materials.TryGetValue(name ?? "", out var m)
                        ? m : throw new InvalidOperationException("Missing material " + name)).ToArray();
                    var particleMeshes = source.meshes.Where(n => !string.IsNullOrEmpty(n)).Select(n => meshes.TryGetValue(n, out var m)
                        ? m : throw new InvalidOperationException("Missing mesh " + n)).ToArray();
                    if (particleMeshes.Length > 0) renderer.SetMeshes(particleMeshes);
                    if (source.materials.Any(refraction.Contains)) renderer.enabled = false;
                }
                else
                {
                    renderer.sharedMaterial = materials[""];
                    renderer.enabled = false;
                }
            }
            foreach (var child in source.children)
                CreateNode(child, materials, meshes, refraction).transform.SetParent(go.transform, false);
            go.SetActive(source.active);
            return go;
        }

        // Sub-emitters are references between systems of one effect, rebuilt after every node exists.
        private static void WireSubEmitters(GameObject root, Node source)
        {
            void Visit(Node node)
            {
                if (node.subEmitters.Length > 0)
                {
                    var host = Find(root, node.path).GetComponent<ParticleSystem>();
                    var module = host.subEmitters;
                    module.enabled = true;
                    foreach (var sub in node.subEmitters)
                    {
                        var target = Find(root, sub.path).GetComponent<ParticleSystem>()
                            ?? throw new InvalidOperationException("Sub-emitter " + sub.path + " has no particle system");
                        module.AddSubEmitter(target, (ParticleSystemSubEmitterType)sub.type,
                            (ParticleSystemSubEmitterProperties)sub.properties, sub.probability);
                    }
                }
                foreach (var child in node.children) Visit(child);
            }
            Visit(source);
        }

        private static Transform Find(GameObject root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root.transform;
            // Sibling names repeat in GFL2 effects, so walk by name in child order.
            var current = root.transform;
            foreach (var part in path.Split('/'))
            {
                current = Enumerable.Range(0, current.childCount).Select(current.GetChild).FirstOrDefault(c => c.name == part)
                    ?? throw new InvalidOperationException("No node " + path + " under " + root.name);
            }
            return current;
        }

        private static void AddRootEmitter(GameObject root, Material placeholder)
        {
            // A source root that already emits keeps its own system and drives the effect itself.
            if (root.GetComponent<ParticleSystem>() != null) return;
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            var particles = root.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.duration = systems.Length == 0 ? 1f : systems.Max(p => p.main.duration);
            main.loop = systems.Any(p => p.main.loop);
            main.playOnAwake = true;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = placeholder;
            renderer.enabled = false;
        }

        private static T SaveAsset<T>(T value, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(value, path);
                return value;
            }
            EditorUtility.CopySerialized(value, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(value);
            return existing;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }
    }
}
