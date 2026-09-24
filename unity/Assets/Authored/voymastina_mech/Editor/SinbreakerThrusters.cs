using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace WOMENACE.Editor
{
    public static class SinbreakerThrusters
    {
        private const string DirectoryPath = "Assets/Authored/voymastina_mech/thrusters";
        private const string AnimationPath = "Assets/Prefabs/voymastina_mech/_bake";
        private const string ShaderName = "Womenace/MechThrusters";

        [Serializable] private sealed class Source
        {
            public Node[] effects;
            public Mount[] mounts;
            public MaterialSource[] materials;
            public MeshSource[] meshes;
            public TextureSource[] textures;
            public GeometrySource[] geometry;
        }

        [Serializable] private sealed class Node
        {
            public string name;
            public bool active;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public string particleJson;
            public string rendererJson;
            public string[] materials;
            public string[] meshes;
            public Node[] children;
        }

        [Serializable] private sealed class Mount
        {
            public string bone;
            public string effect;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        [Serializable] private sealed class MaterialSource
        {
            public string name;
            public int shaderVariant;
            public FloatValue[] floats;
            public ColourValue[] colours;
            public TextureValue[] textures;
        }

        [Serializable] private sealed class FloatValue { public string name; public float value; }
        [Serializable] private sealed class ColourValue { public string name; public Color value; }
        [Serializable] private sealed class ParticleCheck { public float lengthInSec; public bool looping; }
        [Serializable] private sealed class GeometrySource { public string name; public bool active; }
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
            public Vector2[] uv;
            public int[] triangles;
        }

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

        private static Source ReadSource()
        {
            var source = JsonUtility.FromJson<Source>(File.ReadAllText(DirectoryPath + "/source.json"));
            if (source.effects.Length != 8 || source.mounts.Length != 24
                || source.materials.Length != 10 || source.meshes.Length != 2 || source.textures.Length != 10
                || source.geometry == null || source.geometry.Length != 2)
                throw new InvalidDataException("Incomplete Sinbreaker thruster source");
            return source;
        }

        // Rebuild only VFX and their clip bindings without rebaking body meshes or materials.
        public static void Rebuild()
        {
            BuildResources();
            foreach (string outfit in new[] { "erwin", "default" })
            {
                string path = "Assets/Prefabs/voymastina_mech/" + outfit + "/main.prefab";
                var model = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var old in model.GetComponentsInChildren<Transform>(true)
                        .Where(t => t.name == "vfx_jetpack_walker_thruster_animated").ToArray())
                        Object.DestroyImmediate(old.gameObject);
                    Attach(model, 1.25f);
                    PrefabUtility.SaveAsPrefabAsset(model, path, out bool success);
                    if (!success) throw new IOException("Could not save " + path);
                }
                finally { PrefabUtility.UnloadPrefabContents(model); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Sinbreaker thrusters: rebuilt both outfits");
        }

        public static void BuildResources()
        {
            var source = ReadSource();
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException("Missing shader " + ShaderName);
            EnsureFolder(DirectoryPath + "/materials");
            EnsureFolder(DirectoryPath + "/meshes");
            EnsureFolder(DirectoryPath + "/prefabs");
            foreach (var texture in source.textures)
            {
                string path = DirectoryPath + "/textures/" + texture.name + ".png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
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
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            foreach (var item in source.meshes)
            {
                var mesh = new Mesh { name = item.name, vertices = item.vertices, normals = item.normals, uv = item.uv };
                mesh.triangles = item.triangles;
                mesh.RecalculateBounds();
                SaveAsset(mesh, DirectoryPath + "/meshes/" + item.name + ".asset");
            }

            foreach (var item in source.materials)
            {
                var material = new Material(shader) { name = item.name };
                foreach (var value in item.floats)
                    if (material.HasProperty(value.name)) material.SetFloat(value.name, value.value);
                foreach (var value in item.colours)
                    if (material.HasProperty(value.name)) material.SetColor(value.name, value.value);
                foreach (var value in item.textures)
                {
                    if (!material.HasProperty(value.name) || string.IsNullOrEmpty(value.texture)) continue;
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(DirectoryPath + "/textures/" + value.texture + ".png");
                    if (texture == null) throw new InvalidOperationException("Missing thruster texture " + value.texture);
                    material.SetTexture(value.name, texture);
                    material.SetTextureScale(value.name, value.scale);
                    material.SetTextureOffset(value.name, value.offset);
                }
                int variant = item.shaderVariant;
                material.SetFloat("_SourceVariant", variant);
                if (variant != 2)
                {
                    float blend = item.floats.Single(value => value.name == "_BlendMode").value;
                    material.SetFloat("SrcBlend", 5);
                    material.SetFloat("DstBlend", blend);
                    material.SetFloat("SrcAlphaBlend", 5);
                    material.SetFloat("DstAlphaBlend", blend);
                }
                else
                {
                    material.SetFloat("SrcAlphaBlend", 1);
                    material.SetFloat("DstAlphaBlend", 10);
                }
                SaveAsset(material, DirectoryPath + "/materials/" + item.name + ".mat");
            }

            foreach (var effect in source.effects)
            {
                var root = CreateNode(effect);
                try
                {
                    if (root.GetComponentsInChildren<ParticleSystem>(true).Any(p => p.main.maxParticles <= 0))
                        throw new InvalidOperationException("Particle data did not import for " + effect.name);
                    PrefabUtility.SaveAsPrefabAsset(root, EffectPath(effect.name), out bool success);
                    if (!success) throw new IOException("Could not save thruster effect " + effect.name);
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Sinbreaker thrusters: built eight GFL2 phase prefabs");
        }

        private static GameObject CreateNode(Node source)
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
                // LoopUntilReplaced keeps the source flame alive. Repeating its one-shot emitter
                // spawns additional retained particles instead of sustaining the existing one.
                EditorJsonUtility.FromJsonOverwrite("{\"ParticleSystem\":" + source.particleJson + "}", particles);
                var expected = JsonUtility.FromJson<ParticleCheck>(source.particleJson);
                if (!Mathf.Approximately(particles.main.duration, expected.lengthInSec)
                    || particles.main.loop != expected.looping)
                    throw new InvalidOperationException("Particle settings did not import for " + source.name);
                // The effect follows the mech's imported model scale, not a GFL2 scene root.
                var main = particles.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                EditorJsonUtility.FromJsonOverwrite("{\"ParticleSystemRenderer\":" + source.rendererJson + "}", renderer);
                renderer.sharedMaterials = source.materials.Select(name =>
                    AssetDatabase.LoadAssetAtPath<Material>(DirectoryPath + "/materials/"
                        + (string.IsNullOrEmpty(name) ? "glow_add_7001" : name) + ".mat")).ToArray();
                if (renderer.sharedMaterials.Length == 0 || renderer.sharedMaterials.Any(m => m == null))
                    throw new InvalidOperationException("Null particle material on " + source.name);
                var meshes = source.meshes.Where(name => !string.IsNullOrEmpty(name))
                    .Select(name => AssetDatabase.LoadAssetAtPath<Mesh>(DirectoryPath + "/meshes/" + name + ".asset"))
                    .ToArray();
                if (meshes.Length > 0)
                {
                    if (meshes.Any(mesh => mesh == null)) throw new InvalidOperationException("Missing particle mesh");
                    renderer.SetMeshes(meshes);
                }
                renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
                {
                    ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal,
                    ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.UV2,
                    ParticleSystemVertexStream.Custom1XYZW, ParticleSystemVertexStream.Custom2XYZW
                });
            }
            foreach (var child in source.children)
                CreateNode(child).transform.SetParent(go.transform, false);
            go.SetActive(source.active);
            return go;
        }

        public static void Attach(GameObject model, float sourceToWorldScale)
        {
            var source = ReadSource();
            // FBX loses GameObject visibility. G1 and G2 are alternative nozzle geometry,
            // so showing both puts G2's three-port housings over G1's active exhausts.
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var geometry in source.geometry)
            {
                if (!renderers.Any(r => r.name == geometry.name))
                    throw new InvalidOperationException("Missing thruster geometry " + geometry.name);
                foreach (var renderer in renderers.Where(r => r.name == geometry.name || r.name == geometry.name + "_outline"))
                    renderer.gameObject.SetActive(geometry.active);
            }
            var names = new HashSet<string>(source.mounts.Select(m => m.bone));
            var bones = model.GetComponentsInChildren<Transform>(true)
                .Where(t => names.Contains(t.name)).ToDictionary(t => t.name, t => t);
            var roots = new Dictionary<Transform, string>();
            foreach (var mount in source.mounts)
            {
                if (!bones.TryGetValue(mount.bone, out var bone))
                    throw new InvalidOperationException("Missing thruster attachment bone " + mount.bone);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EffectPath(mount.effect));
                if (prefab == null) throw new InvalidOperationException("Missing thruster prefab " + mount.effect);
                var existing = bone.Find(mount.effect);
                GameObject effect;
                if (existing != null && PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject) == prefab)
                    effect = existing.gameObject;
                else
                {
                    // FBX keeps empty effect transforms but cannot carry their particle components.
                    if (existing != null) Object.DestroyImmediate(existing.gameObject);
                    effect = (GameObject)PrefabUtility.InstantiatePrefab(prefab, bone);
                }
                effect.name = mount.effect;
                effect.transform.localPosition = mount.position;
                effect.transform.localRotation = mount.rotation;
                var scale = bone.lossyScale;
                if (scale.x <= 0f || !Mathf.Approximately(scale.x, scale.y) || !Mathf.Approximately(scale.x, scale.z))
                    throw new InvalidOperationException("Non-uniform thruster attachment scale on " + mount.bone);
                effect.transform.localScale = mount.scale * (sourceToWorldScale / Mathf.Abs(bone.lossyScale.x));
                string phase = mount.effect.EndsWith("_idle") ? "idle"
                    : mount.effect.EndsWith("_start") ? "start"
                    : mount.effect.EndsWith("_end") ? "end" : "thrust";
                effect.SetActive(phase == "idle");
                roots.Add(effect.transform, phase);
            }
            DrivePhases(model, roots);
            if (model.GetComponentsInChildren<ParticleSystem>(true).Length != 194)
                throw new InvalidOperationException("Sinbreaker must contain exactly 194 source particle systems");
        }

        private static void DrivePhases(GameObject model, Dictionary<Transform, string> roots)
        {
            var controller = model.GetComponent<Animator>().runtimeAnimatorController as AnimatorController;
            if (controller == null) throw new InvalidOperationException("Sinbreaker has no authored controller");
            var phases = new Dictionary<string, AnimationClip>();
            foreach (string phase in new[] { "idle", "start", "thrust", "end", "off" })
            {
                var clip = new AnimationClip { name = "sinbreaker_thrusters_" + phase, frameRate = 60f };
                foreach (var root in roots)
                {
                    string path = AnimationUtility.CalculateTransformPath(root.Key, model.transform);
                    clip.SetCurve(path, typeof(GameObject), "m_IsActive",
                        AnimationCurve.Constant(0f, 1f, root.Value == phase ? 1f : 0f));
                }
                phases.Add(phase, SaveAsset(clip, AnimationPath + "/" + clip.name + ".anim"));
            }
            // GFL2 switches phases in code. Sync with the existing controller's states without
            // copying its large skeletal clips or maintaining a second set of transitions.
            var layer = new AnimatorControllerLayer
            {
                name = "Thrusters",
                syncedLayerIndex = 0,
                syncedLayerAffectsTiming = false,
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override
            };
            foreach (var child in controller.layers[0].stateMachine.states)
            {
                var state = child.state;
                var clip = state.motion as AnimationClip;
                if (clip == null) throw new InvalidOperationException("Sinbreaker state has no clip: " + state.name);
                layer.SetOverrideMotion(state, phases[PhaseForState(state.name)]);
            }
            controller.layers = controller.layers.Where(l => l.name != layer.name).Append(layer).ToArray();
            EditorUtility.SetDirty(controller);
        }

        private static string PhaseForState(string state) =>
            state == "RunStart" ? "start"
            : state == "Run" || state == "UltraSkill" ? "thrust"
            : state == "RunStop" || state == "UltraSkillHit" ? "end"
            : state == "Death" ? "off" : "idle";

        public static void Validate()
        {
            var source = ReadSource();
            var names = new HashSet<string>(source.effects.Select(e => e.name));
            foreach (string outfit in new[] { "default", "erwin" })
            {
                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/voymastina_mech/" + outfit + "/main.prefab"));
                try
                {
                    var particles = model.GetComponentsInChildren<ParticleSystem>(true);
                    var roots = model.GetComponentsInChildren<Transform>(true).Where(t => names.Contains(t.name)).ToArray();
                    if (particles.Length != 194 || roots.Length != 24)
                        throw new InvalidOperationException("Wrong thruster counts on " + outfit);
                    if (model.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m == null)))
                        throw new InvalidOperationException("Null renderer material on " + outfit);
                    if (model.GetComponentsInChildren<Transform>(true)
                        .Any(t => t.name.Contains("jetpack")
                            || PrefabUtility.GetPrefabInstanceStatus(t.gameObject) == PrefabInstanceStatus.MissingAsset
                            || GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0))
                        throw new InvalidOperationException("Vanilla thruster, missing prefab or missing script on " + outfit);
                    var animator = model.GetComponent<Animator>();
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var controller = (AnimatorController)animator.runtimeAnimatorController;
                    foreach (var child in controller.layers[0].stateMachine.states)
                    {
                        string state = child.state.name;
                        animator.Play(state, 0, 0f);
                        animator.Update(0f);
                        string phase = PhaseForState(state);
                        var active = roots.Where(t => t.gameObject.activeInHierarchy).ToArray();
                        if (active.Length != (phase == "off" ? 0 : 6)
                            || active.Any(t => EffectPhase(t.name) != phase))
                            throw new InvalidOperationException(outfit + " has incorrect active jets in " + state);
                        foreach (var geometry in source.geometry)
                        {
                            var body = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                                .Where(r => r.name == geometry.name || r.name == geometry.name + "_outline").ToArray();
                            if (!body.Any(r => r.name == geometry.name)
                                || body.Any(r => r.gameObject.activeInHierarchy != geometry.active))
                                throw new InvalidOperationException(outfit + " has incorrect nozzle geometry in " + state);
                        }
                        Debug.Log($"Sinbreaker check: {outfit} {state} -> {phase}, {active.Length} active roots");
                    }
                    void Advance(float seconds)
                    {
                        for (int frame = 0; frame < Mathf.CeilToInt(seconds * 60f); frame++)
                            animator.Update(1f / 60f);
                    }
                    void ExpectPhase(string phase)
                    {
                        var active = roots.Where(t => t.gameObject.activeInHierarchy).ToArray();
                        if (active.Length != 6 || active.Any(t => EffectPhase(t.name) != phase))
                            throw new InvalidOperationException(outfit + " failed controller transition to " + phase);
                    }
                    animator.Rebind();
                    animator.Update(0f);
                    animator.SetFloat("Speed", 1f);
                    Advance(0.2f);
                    ExpectPhase("start");
                    Advance(1f);
                    ExpectPhase("thrust");
                    animator.SetFloat("Speed", 0f);
                    Advance(0.2f);
                    ExpectPhase("end");
                    Advance(1.2f);
                    ExpectPhase("idle");
                    animator.SetTrigger("SpecialAttack2");
                    Advance(0.2f);
                    ExpectPhase("thrust");
                    animator.SetTrigger("Hit");
                    Advance(0.2f);
                    ExpectPhase("idle");
                    Debug.Log($"Sinbreaker check: {outfit} movement cycle and interrupted drill passed");
                    // Sweep full cycles, including their late halves where short bursts can leave gaps.
                    foreach (string state in new[] { "Idle", "Run", "UltraSkill" })
                    {
                        animator.Play(state, 0, 0f);
                        animator.Update(0f);
                        var layers = particles.Where(p => p.gameObject.activeInHierarchy
                            && (p.name.StartsWith("fire_wei", StringComparison.Ordinal)
                                || p.name.StartsWith("Glow", StringComparison.Ordinal))).ToArray();
                        if (layers.Length != (state == "Idle" ? 24 : 30))
                            throw new InvalidOperationException("Missing sustained flame or glow layers in " + outfit + "/" + state);
                        foreach (int frameRate in new[] { 30, 60, 144 })
                        {
                            foreach (var layer in layers)
                            {
                                var failure = FindSustainedFailure(layer, frameRate, 13);
                                if (failure.HasValue)
                                    throw new InvalidOperationException(
                                        $"Sustained layer failure: {outfit}/{state}/{layer.name} at {failure.Value.Time:F3}s"
                                        + $" ({frameRate} fps, particles={failure.Value.Count}, visible={failure.Value.Visible})");
                            }
                            Debug.Log($"Sinbreaker check: {outfit} {state}, {layers.Length} flame/glow layers visible with one particle each for 13s at {frameRate} fps");
                        }
                    }
                    // A non-retaining copy must expose the nominal lifetime gap, otherwise the
                    // continuity test is not actually exercising particle expiry.
                    var probe = particles.First(p => p.gameObject.activeInHierarchy && p.name == "Glow");
                    var main = probe.main;
                    main.ringBufferMode = ParticleSystemRingBufferMode.Disabled;
                    var expectedFailure = FindSustainedFailure(probe, 60, 2);
                    if (!expectedFailure.HasValue || expectedFailure.Value.Count != 0 || expectedFailure.Value.Visible
                        || expectedFailure.Value.Time < 0.45f || expectedFailure.Value.Time > 0.7f)
                        throw new InvalidOperationException("Continuity negative control did not detect particle expiry on " + outfit);
                    Debug.Log($"Sinbreaker check: {outfit} negative control detected expiry at {expectedFailure.Value.Time:F3}s");
                }
                finally { Object.DestroyImmediate(model); }
            }
            Debug.Log("Sinbreaker thrusters: validation passed");
        }

        private static (float Time, int Count, bool Visible)? FindSustainedFailure(
            ParticleSystem layer, int frameRate, int duration)
        {
            var buffer = new ParticleSystem.Particle[layer.main.maxParticles];
            layer.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            layer.useAutoRandomSeed = false;
            layer.randomSeed = 1;
            for (int frame = 1; frame <= frameRate * duration; frame++)
            {
                layer.Simulate(1f / frameRate, false, frame == 1, false);
                float seconds = (float)frame / frameRate;
                if (seconds < 0.1f) continue; // allow the authored ignition fade
                int count = layer.GetParticles(buffer);
                bool visible = false;
                for (int i = 0; i < count; i++)
                {
                    var size = buffer[i].GetCurrentSize3D(layer);
                    if (buffer[i].GetCurrentColor(layer).a > 0 && size.x > 0f && size.y > 0f)
                    {
                        visible = true;
                        break;
                    }
                }
                if (!visible || count != 1)
                    return (seconds, count, visible);
            }
            return null;
        }

        private static string EffectPhase(string name) => name.EndsWith("_idle") ? "idle"
            : name.EndsWith("_start") ? "start" : name.EndsWith("_end") ? "end" : "thrust";

        // Uses the actual HDRP materials and controller. Shader panners use editor render time.
        public static void Preview()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore
                || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Thruster preview needs HDRP graphics. On Linux use -force-vulkan, not -nographics.");
            var args = Environment.GetCommandLineArgs();
            string Arg(string name, string fallback)
            {
                int index = Array.IndexOf(args, name);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
            }
            string outfit = Arg("-outfit", "default");
            string state = Arg("-state", "Run");
            string output = Arg("-out", null) ?? throw new ArgumentException("-out is required");
            float seconds = float.Parse(Arg("-seconds", "0.5"), System.Globalization.CultureInfo.InvariantCulture);
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f || seconds > 60f)
                throw new ArgumentException("-seconds must be between 0 and 60");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/voymastina_mech/" + outfit + "/main.prefab");
            var model = Object.Instantiate(prefab);
            var animator = model.GetComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.SetFloat("Speed", state.StartsWith("Run") && state != "RunStop" ? 1f : 0f);
            animator.Play(state, 0, 0f);
            animator.Update(0f);
            var particles = model.GetComponentsInChildren<ParticleSystem>(true);
            var active = new bool[particles.Length];
            for (int i = 0; i < particles.Length; i++)
            {
                var ps = particles[i];
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.useAutoRandomSeed = false;
                ps.randomSeed = (uint)(i + 1);
            }
            for (int frame = 0; frame < Mathf.CeilToInt(seconds * 60f); frame++)
            {
                float delta = Mathf.Min(1f / 60f, seconds - frame / 60f);
                animator.Update(delta);
                for (int i = 0; i < particles.Length; i++)
                {
                    var ps = particles[i];
                    bool enabled = ps.gameObject.activeInHierarchy;
                    if (enabled != active[i])
                    {
                        ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                        if (enabled) ps.Play(false);
                        active[i] = enabled;
                    }
                    if (enabled) ps.Simulate(delta, false, false, false);
                }
            }
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled).ToArray();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var camera = new GameObject("Thruster preview camera").AddComponent<Camera>();
            var cameraData = camera.gameObject.AddComponent<HDAdditionalCameraData>();
            cameraData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            cameraData.backgroundColorHDR = new Color(0.04f, 0.04f, 0.06f);
            camera.fieldOfView = 35f;
            camera.transform.position = bounds.center + Quaternion.Euler(12f, 155f, 0f) * Vector3.forward * bounds.extents.magnitude * 3.4f;
            camera.transform.LookAt(bounds.center);
            var light = new GameObject("Thruster preview light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.gameObject.AddComponent<HDAdditionalLightData>();
            light.intensity = 10000f;
            light.transform.rotation = camera.transform.rotation * Quaternion.Euler(20f, -30f, 0f);
            var target = new RenderTexture(1000, 1000, 24);
            target.Create();
            camera.targetTexture = target;
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(output, image.EncodeToPNG());
            Debug.Log($"Sinbreaker preview: {outfit} {state} at {seconds:F3}s, {particles.Sum(p => p.particleCount)} particles -> {output}");
            RenderTexture.active = null;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(camera.gameObject);
            Object.DestroyImmediate(light.gameObject);
            Object.DestroyImmediate(model);
        }

        private static string EffectPath(string name) => DirectoryPath + "/prefabs/" + name + ".prefab";

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
