using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Womenace.EditorTools
{
    // Bakes a GFL2 room for the strategy diorama from the export in
    // Assets/Authored/environment/<room>/ (scene.gltf from ~/gfl2-extract/scenes/scene_gltf.py,
    // bake.json from scene_bake_sidecar.py) into Assets/Prefabs/environment/<room>/main.prefab,
    // which loads as "environment/<room>/main".
    //
    // Everything GFL2's data decides is settled here, so the runtime takes the room as it comes:
    // - Materials go on Womenace/BakedScene with GFL2's own textures and values (see the shader).
    // - Each renderer reflects the probe GFL2 would give it, through its own material variant.
    // - Shadow casting follows GFL2's per-renderer mode, shadow-only renderers included: GFL2's
    //   live lights (a realtime sun, realtime and mixed spots) shadow the room's own geometry.
    // - Only the lights GFL2 renders live are kept: its sun and its Realtime or Mixed lights. Baked
    //   lights are already in the lightmap.
    //
    //   Unity -batchmode -nographics -quit -buildTarget StandaloneWindows64 -projectPath unity \
    //     -executeMethod Womenace.EditorTools.BakeRoom.Run -room ops_room
    public static class BakeRoom
    {
        [Serializable]
        private class Sidecar
        {
            public MaterialEntry[] materials;
            public RendererEntry[] renderers;
            public LightEntry[] lights;
            public ProbeEntry[] probes;
            public LightProbeEntry[] lightProbes;
            public MirrorBoxEntry mirrorBox;
            public Pose camera;
            public string lightmap;
            public string shadowmask;
        }

        [Serializable]
        private class MaterialEntry
        {
            public string name;
            public string @base;
            public string smo;
            public string normal;
            public float[] baseColor;
            public float[] emissionColor;
            public float glowIntensity = 1f;
            public float smoothness = 1f;
            public float metallic = 1f;
            public float occlusionStrength = 1f;
            public float bumpScale = 1f;
            public bool unlit;
            public bool transparent;
            public bool doubleSided;
            public float alphaClip;
            // Another GFL2 shader's raw values, for a material that is not GFL2 scenery.
            public string gfl2Shader;
            public NamedFloat[] gfl2Floats;
            public NamedColor[] gfl2Colors;
            public NamedTexture[] gfl2Textures;
        }

        [Serializable] private class NamedFloat { public string name; public float value; }
        [Serializable] private class NamedColor { public string name; public float[] value; }
        [Serializable] private class NamedTexture { public string name; public string file; public float[] st; }

        // GFL2 shaders ported by Womenace/GfVfx rather than BakedScene: the effect family, and the pass
        // state of GFL2's compiled shader (blend, cull, depth write).
        private static readonly Dictionary<string, (int Family, float Src, float Dst, float Cull, float ZWrite)> EffectShaders = new()
        {
            ["scene/Neonlight/Billboard"] = (9, 1f, 0f, 2f, 1f),
        };

        [Serializable]
        private class RendererEntry
        {
            // Node names from the glTF scene root, for the bake's report.
            public string path;
            public int castShadows;
            public bool reflectionProbes = true;
            public bool lightmapped = true;
        }

        [Serializable]
        private class LightEntry
        {
            public string name;
            public float[] position;
            public float[] rotation;
            public int type;
            public float[] color;
            public float intensity;
            public float range;
            public float spotAngle;
            public float innerSpotAngle;
            public float[] areaSize;
            public bool shadows;
            public int bakeType;
            public int cullingMask = -1;
            public float shadowNearPlane = 0.2f;
        }

        [Serializable]
        private class ProbeEntry
        {
            public string name;
            public string file;
            public int format;
            public int size;
            public int mips;
            public float[] centre;
            public float[] boxSize;
            public float intensity;
            public int importance;
            public float blendDistance;
        }

        // The pre-baked box a scene's floor mirror shows in place of the live room.
        [Serializable]
        private class MirrorBoxEntry
        {
            public string name;
            public string file;
            public int format;
            public int size;
            public int mips;
            public float[] position;
            public float[] rotation;
            public float[] scale;
        }

        [Serializable]
        private class LightProbeEntry
        {
            public float[] position;
            // Unity's seven per-renderer SH shader constants (unity_SHAr..unity_SHC), flattened.
            public float[] sh;
            public float sunOcclusion = 1f;
        }

        [Serializable]
        private class Pose
        {
            public float[] position;
            public float[] rotation;
        }

        // Unity's LightmapBakeType values as GFL2's data stores them.
        private const int Mixed = 1, Baked = 2;
        // The rendering layer the room's geometry is on (StrategyDiorama.Shadows.cs carries the same
        // value): a light's shadow layers say whether the room casts for it.
        private const uint RoomRenderingLayer = 1u << 8;

        // Rows of scenery that GFL2 laid only as wide as its own shot sees, by room: the material
        // the row's pieces wear and how many more of the end pieces the row runs on by, each way.
        // The refitting room's ground outside its windows is four pieces, and the armoury's
        // camera turns far enough to see past both ends of it through the glass.
        private static readonly Dictionary<string, (string Material, int EachWay)> RunOnRows = new()
        {
            ["refit_room"] = ("s_gacha_canyonfloor", 4),
        };

        public static void Run()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-room")
                        Bake(args[i + 1]);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("BakeRoom failed: " + ex);
                EditorApplication.Exit(1);
            }
        }

        private sealed class Probe
        {
            public ProbeEntry Entry;
            public Cubemap Cube;
            public Bounds Reach;
        }

        public static void Bake(string room)
        {
            var src = $"Assets/Authored/environment/{room}";
            var dst = $"Assets/Prefabs/environment/{room}";
            var sidecar = JsonUtility.FromJson<Sidecar>(File.ReadAllText(src + "/bake.json"));
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/BakedScene.shader")
                ?? throw new InvalidOperationException("Assets/Shaders/BakedScene.shader not found");

            // Stale assets from earlier runs are where machine-local stub-shader GUIDs dangle.
            if (AssetDatabase.IsValidFolder(dst + "/materials"))
                AssetDatabase.DeleteAsset(dst + "/materials");
            Directory.CreateDirectory(dst + "/materials");
            // The probe cubes are kept and written over in place, so their GUIDs survive a rebake:
            // a prop baked apart from the room (BakeAnimatedProp) reflects one of them.
            Directory.CreateDirectory(dst + "/probes");
            ProbesWritten.Clear();
            AssetDatabase.Refresh();

            foreach (var m in sidecar.materials)
            {
                ConfigureTexture(src, m.@base, srgb: true);
                ConfigureTexture(src, m.smo, srgb: false);
                ConfigureTexture(src, m.normal, srgb: false);
            }
            var lightmap = ImportBakedMap(src, sidecar.lightmap, singleChannel: false);
            var shadowmask = ImportBakedMap(src, sidecar.shadowmask, singleChannel: true);
            var probes = sidecar.probes.Select(p => new Probe
            {
                Entry = p,
                Cube = BuildCube(src, dst, p),
                Reach = new Bounds(V3(p.centre), V3(p.boxSize)),
            }).Where(p => p.Cube != null).ToList();
            // A renderer no probe box holds reflects the room's widest probe.
            var roomProbe = probes.OrderByDescending(p => Volume(p.Reach)).FirstOrDefault();

            var gltfPath = src + "/scene.gltf";
            AssetDatabase.ImportAsset(gltfPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var gltf = AssetDatabase.LoadAssetAtPath<GameObject>(gltfPath)
                ?? throw new InvalidOperationException("glTF did not import as a GameObject: " + gltfPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(gltf);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            try
            {
                instance.name = room;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                // glTFast builds one renderer per mesh node, in glTF order, which is the sidecar's,
                // and tells same-named siblings apart with a "_<n>" suffix.
                var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length != sidecar.renderers.Length)
                    throw new InvalidOperationException($"{renderers.Length} renderers imported, {sidecar.renderers.Length} in the sidecar");
                var misnamed = new List<string>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    var leaf = sidecar.renderers[i].path.Substring(sidecar.renderers[i].path.LastIndexOf('/') + 1);
                    var name = renderers[i].name;
                    if (name != leaf && !(name.StartsWith(leaf + "_") && int.TryParse(name.Substring(leaf.Length + 1), out _)) && misnamed.Count < 5)
                        misnamed.Add($"{name} / {leaf}");
                }
                if (misnamed.Count > 0)
                    Debug.LogWarning("BakeRoom: renderer order differs from the sidecar's: " + string.Join(", ", misnamed));

                var row = RunOnRows.TryGetValue(room, out var runOn)
                    ? renderers.Where(r => r.sharedMaterial != null && r.sharedMaterial.name.StartsWith(runOn.Material)).Select(r => r.transform).ToList()
                    : new List<Transform>();

                var entries = sidecar.materials.GroupBy(m => m.name).ToDictionary(g => g.Key, g => g.First());
                var built = new Dictionary<(string, Probe), Material>();
                int replaced = 0, unknown = 0;
                var reflecting = new Dictionary<string, int>();
                for (int index = 0; index < renderers.Length; index++)
                {
                    var renderer = renderers[index];
                    renderer.shadowCastingMode = (ShadowCastingMode)sidecar.renderers[index].castShadows;
                    renderer.renderingLayerMask = RoomRenderingLayer;

                    var probe = sidecar.renderers[index].reflectionProbes ? PickProbe(probes, renderer.bounds.center) ?? roomProbe : null;
                    var slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        var name = slots[i] != null ? slots[i].name.Replace(" (Instance)", "") : null;
                        if (name == null || !entries.TryGetValue(name, out var entry))
                        {
                            unknown++;
                            continue;
                        }
                        var key = (name, entry.unlit ? null : probe);
                        var probeLit = !entry.unlit && !sidecar.renderers[index].lightmapped && sidecar.lightProbes is { Length: > 0 };
                        if (probeLit)
                        {
                            // GFL2 lights a renderer it does not lightmap from its light probes: each
                            // such renderer takes the probes' light at its centre, in a material of its own.
                            var lit = BuildMaterial(shader, entry, src, lightmap, shadowmask, key.Item2);
                            ApplyLightProbes(lit, sidecar.lightProbes, renderer.bounds.center);
                            AssetDatabase.CreateAsset(lit, $"{dst}/materials/{Sanitise(name)}__probelit{index}.mat");
                            slots[i] = lit;
                            replaced++;
                            continue;
                        }
                        if (!built.TryGetValue(key, out var mat))
                        {
                            mat = BuildMaterial(shader, entry, src, lightmap, shadowmask, key.Item2);
                            var file = key.Item2 == null || key.Item2 == roomProbe ? Sanitise(name) : Sanitise(name) + "__" + key.Item2.Entry.name;
                            AssetDatabase.CreateAsset(mat, $"{dst}/materials/{file}.mat");
                            built[key] = mat;
                        }
                        slots[i] = mat;
                        replaced++;
                    }
                    renderer.sharedMaterials = slots;
                    if (probe != null)
                        reflecting[probe.Entry.name] = reflecting.TryGetValue(probe.Entry.name, out var n) ? n + 1 : 1;
                }
                if (unknown > 0)
                    Debug.LogWarning($"BakeRoom: {unknown} material slot(s) had no sidecar entry and keep the glTF material.");

                var ranOn = RunRowOn(instance, row, runOn.EachWay);

                var lightCount = AddLights(instance, sidecar.lights);
                AddLightProbes(instance, dst, shader, sidecar.lightProbes);
                AddReflectionProbes(instance, dst, shader, probes);
                AddMirrorBox(instance, src, dst, sidecar.mirrorBox);
                AddHoloScreen(instance, dst);
                if (sidecar.camera?.position is { Length: 3 })
                {
                    var anchor = new GameObject("_CameraAnchor");
                    anchor.transform.SetParent(instance.transform, false);
                    anchor.transform.SetPositionAndRotation(V3(sidecar.camera.position), Q(sidecar.camera.rotation));
                }

                PrefabUtility.SaveAsPrefabAsset(instance, dst + "/main.prefab");
                // A probe the room no longer has.
                foreach (var guid in AssetDatabase.FindAssets("t:Cubemap", new[] { dst + "/probes" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!ProbesWritten.Contains(path))
                        AssetDatabase.DeleteAsset(path);
                }
                AssetDatabase.SaveAssets();
                Debug.Log($"BakeRoom: wrote {dst}/main.prefab: {built.Count} materials over {replaced} slots, "
                    + $"{lightCount} live lights, {ranOn} pieces run on, probes {string.Join(", ", reflecting.Select(kv => $"{kv.Key} {kv.Value}"))}");
                ReportTangents(instance);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        // Runs a row of like pieces on at both ends by copies of its end pieces, baked materials
        // and all, at the row's own spacing. Returns how many pieces it added.
        private static int RunRowOn(GameObject instance, List<Transform> row, int eachWay)
        {
            if (row.Count < 2 || eachWay < 1)
                return 0;
            // The row's two ends are the pieces furthest apart, and its step the gap between neighbours.
            var (first, last) = (row[0], row[1]);
            foreach (var a in row)
                foreach (var b in row)
                    if ((a.position - b.position).sqrMagnitude > (first.position - last.position).sqrMagnitude)
                        (first, last) = (a, b);
            var step = (last.position - first.position) / (row.Count - 1);
            var more = new GameObject("_RunOn").transform;
            more.SetParent(instance.transform, false);
            for (int i = 1; i <= eachWay; i++)
                foreach (var (end, to) in new[] { (last, last.position + step * i), (first, first.position - step * i) })
                {
                    var copy = UnityEngine.Object.Instantiate(end.gameObject, to, end.rotation, more);
                    copy.name = end.name;
                    copy.transform.localScale = end.lossyScale;
                }
            return eachWay * 2;
        }

        // Unity's choice for a renderer (without probe blending): the most important probe whose box
        // holds the renderer's centre, the smaller box on a tie.
        private static Probe PickProbe(List<Probe> probes, Vector3 centre)
        {
            Probe best = null;
            foreach (var p in probes)
            {
                if (!p.Reach.Contains(centre))
                    continue;
                if (best == null || p.Entry.importance > best.Entry.importance
                    || (p.Entry.importance == best.Entry.importance && Volume(p.Reach) < Volume(best.Reach)))
                    best = p;
            }
            return best;
        }

        private static Material BuildMaterial(Shader shader, MaterialEntry e, string src, Texture2D lightmap, Texture2D shadowmask, Probe probe)
        {
            if (e.gfl2Shader != null && EffectShaders.TryGetValue(e.gfl2Shader, out var port))
                return BuildEffectMaterial(e, src, port);
            var mat = new Material(shader) { name = e.name };
            mat.SetTexture("_BaseMap", Load(src, e.@base));
            mat.SetColor("_BaseColor", Colour(e.baseColor, Color.white));
            mat.SetFloat("_Unlit", e.unlit ? 1f : 0f);
            if (e.unlit)
            {
                mat.SetFloat("_GlowIntensity", e.glowIntensity);
            }
            else
            {
                mat.SetTexture("_Lightmap", lightmap);
                mat.SetTexture("_ShadowMask", shadowmask);
                var smo = Load(src, e.smo);
                if (smo != null)
                    mat.SetTexture("_SMO", smo);
                var normal = Load(src, e.normal);
                if (normal != null)
                {
                    mat.SetTexture("_NormalMap", normal);
                    mat.SetFloat("_BumpScale", e.bumpScale);
                }
                mat.SetFloat("_Smoothness", e.smoothness);
                mat.SetFloat("_Metallic", e.metallic);
                mat.SetFloat("_OcclusionStrength", e.occlusionStrength);
                mat.SetColor("_EmissionColor", Colour(e.emissionColor, Color.black));
                if (probe != null)
                {
                    mat.SetTexture("_ReflectionCube", probe.Cube);
                    mat.SetFloat("_ReflectionIntensity", probe.Entry.intensity);
                }
            }
            mat.SetFloat("_CullMode", e.doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            if (e.transparent)
            {
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            else if (e.alphaClip > 0f)
            {
                mat.SetFloat("_Cutoff", e.alphaClip);
                mat.renderQueue = (int)RenderQueue.AlphaTest;
            }
            return mat;
        }

        // The scene's light probes at a point: the four nearest, weighted by inverse square distance
        // (in place of Unity's tetrahedral interpolation), as SH constants and sun occlusion.
        private static (Vector4[] Sh, float Occlusion) SampleLightProbes(LightProbeEntry[] probes, Vector3 point)
        {
            var nearest = probes.Select(p => (p, d: (V3(p.position) - point).sqrMagnitude)).OrderBy(x => x.d).Take(4).ToList();
            var sh = new Vector4[7];
            float total = 0f, occlusion = 0f;
            foreach (var (p, d) in nearest)
            {
                var w = 1f / Mathf.Max(d, 1e-4f);
                for (int k = 0; k < 7; k++)
                    sh[k] += new Vector4(p.sh[k * 4], p.sh[k * 4 + 1], p.sh[k * 4 + 2], p.sh[k * 4 + 3]) * w;
                occlusion += p.sunOcclusion * w;
                total += w;
            }
            for (int k = 0; k < 7; k++)
                sh[k] /= total;
            return (sh, occlusion / total);
        }

        private static void ApplyLightProbes(Material mat, LightProbeEntry[] probes, Vector3 point)
        {
            var (sh, occlusion) = SampleLightProbes(probes, point);
            mat.SetFloat("_UseProbes", 1f);
            for (int k = 0; k < 7; k++)
                mat.SetVector("_ProbeSH" + k, sh[k]);
            mat.SetFloat("_ProbeOcclusion", occlusion);
        }

        // The light probes for the runtime (the characters standing in the room): a float texture,
        // one row per probe, holding its position and sun occlusion, then its seven SH constants,
        // on a disabled "_LightProbes" renderer's material.
        private static void AddLightProbes(GameObject instance, string dst, Shader shader, LightProbeEntry[] probes)
        {
            if (probes is not { Length: > 0 })
                return;
            var tex = new Texture2D(8, probes.Length, TextureFormat.RGBAFloat, false, true)
            {
                name = "lightprobes",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int i = 0; i < probes.Length; i++)
            {
                var p = probes[i];
                tex.SetPixel(0, i, new Color(p.position[0], p.position[1], p.position[2], p.sunOcclusion));
                for (int k = 0; k < 7; k++)
                    tex.SetPixel(k + 1, i, new Color(p.sh[k * 4], p.sh[k * 4 + 1], p.sh[k * 4 + 2], p.sh[k * 4 + 3]));
            }
            tex.Apply(false, false);
            var texPath = dst + "/lightprobes.asset";
            AssetDatabase.DeleteAsset(texPath);
            AssetDatabase.CreateAsset(tex, texPath);
            var mat = new Material(shader) { name = "_LightProbes" };
            mat.SetTexture("_BaseMap", tex);
            AssetDatabase.CreateAsset(mat, dst + "/materials/_LightProbes.mat");
            var go = new GameObject("_LightProbes");
            go.transform.SetParent(instance.transform, false);
            var holder = go.AddComponent<MeshRenderer>();
            holder.sharedMaterial = mat;
            holder.enabled = false;
        }

        // The reflection probes for what the room spawns at runtime (its characters): one disabled
        // renderer per probe under "_ReflectionProbes", its box as the child's position and scale, its
        // importance in its name, and its cube on its material.
        private static void AddReflectionProbes(GameObject instance, string dst, Shader shader, List<Probe> probes)
        {
            var root = new GameObject("_ReflectionProbes");
            root.transform.SetParent(instance.transform, false);
            foreach (var p in probes)
            {
                var mat = new Material(shader) { name = "_Reflection_" + p.Entry.name };
                mat.SetTexture("_ReflectionCube", p.Cube);
                AssetDatabase.CreateAsset(mat, $"{dst}/materials/_Reflection_{p.Entry.name}.mat");
                var go = new GameObject(p.Entry.importance.ToString());
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = p.Reach.center;
                go.transform.localScale = p.Reach.size;
                var holder = go.AddComponent<MeshRenderer>();
                holder.sharedMaterial = mat;
                holder.enabled = false;
            }
        }

        // GFL2's pre-baked mirror box (gf_shader/scene/prebaked_cubemap_scene on a unit cube) as a
        // disabled "_MirrorBox" renderer. It is never drawn: StrategyDiorama hands its cube and
        // its transform to the floor shader, which reads the cube where the mirror would see it.
        private static void AddMirrorBox(GameObject instance, string src, string dst, MirrorBoxEntry box)
        {
            if (string.IsNullOrEmpty(box?.file))
                return;
            var cube = BuildCube(src, dst, new ProbeEntry { name = box.name, file = box.file, format = box.format, size = box.size, mips = box.mips });
            if (cube == null)
                return;
            var mat = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/PrebakedCube.shader")) { name = "_MirrorBox" };
            mat.SetTexture("_Cubemap", cube);
            AssetDatabase.CreateAsset(mat, dst + "/materials/_MirrorBox.mat");
            var mesh = UnitCube();
            AssetDatabase.DeleteAsset(dst + "/mirror_box.asset");
            AssetDatabase.CreateAsset(mesh, dst + "/mirror_box.asset");
            var go = new GameObject("_MirrorBox");
            go.transform.SetParent(instance.transform, false);
            go.transform.SetLocalPositionAndRotation(V3(box.position), Q(box.rotation));
            go.transform.localScale = V3(box.scale);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;
        }

        // Unity's built-in cube (unit size, outward faces), as an asset of the room's own.
        private static Mesh UnitCube()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (var n in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
            {
                var u = n.y != 0f ? Vector3.right : Vector3.up;
                var v = Vector3.Cross(n, u);
                var start = vertices.Count;
                vertices.Add((n - u - v) * 0.5f);
                vertices.Add((n + u - v) * 0.5f);
                vertices.Add((n + u + v) * 0.5f);
                vertices.Add((n - u + v) * 0.5f);
                // Clockwise seen from outside, Unity's front face.
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            var mesh = new Mesh { name = "mirror_box" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // A material on one of GFL2's effect shaders: its values copied across under their own names.
        private static Material BuildEffectMaterial(MaterialEntry e, string src, (int Family, float Src, float Dst, float Cull, float ZWrite) port)
        {
            var mat = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/GfVfx.shader")) { name = e.name };
            foreach (var f in e.gfl2Floats ?? Array.Empty<NamedFloat>())
                if (mat.HasProperty(f.name))
                    mat.SetFloat(f.name, f.value);
            foreach (var c in e.gfl2Colors ?? Array.Empty<NamedColor>())
                if (mat.HasProperty(c.name) && c.value is { Length: >= 4 })
                    mat.SetVector(c.name, new Vector4(c.value[0], c.value[1], c.value[2], c.value[3]));
            foreach (var t in e.gfl2Textures ?? Array.Empty<NamedTexture>())
            {
                if (!mat.HasProperty(t.name))
                    continue;
                ConfigureTexture(src, t.file, srgb: true);
                mat.SetTexture(t.name, Load(src, t.file));
                mat.SetTextureScale(t.name, new Vector2(t.st[0], t.st[1]));
                mat.SetTextureOffset(t.name, new Vector2(t.st[2], t.st[3]));
            }
            mat.SetFloat("_Family", port.Family);
            mat.SetFloat("_WmSrcBlend", port.Src);
            mat.SetFloat("_WmDstBlend", port.Dst);
            mat.SetFloat("_WmCull", port.Cull);
            mat.SetFloat("_WmZWrite", port.ZWrite);
            mat.renderQueue = port.ZWrite > 0.5f ? (int)RenderQueue.Geometry : (int)RenderQueue.Transparent;
            return mat;
        }

        // GFL2's characters' layer.
        private const int CharacterLayer = 13;

        // The lights GFL2 renders live. Their values are URP's, whose punctual diffuse has no 1/pi,
        // so a punctual light lands in candela at pi times its URP intensity to read the same
        // through HDRP's Lambert. The sun keeps its URP value: Womenace/BakedScene evaluates it as
        // GFL2 does, and the light itself stays off at runtime. In Shadowmask mode a Mixed light's
        // live shadow holds only what moves (the shadowmask holds the room's), so its shadow layers
        // leave the room out, and the shader reads that as "apply the shadowmask". A Realtime light
        // shadows everything.
        private static int AddLights(GameObject instance, LightEntry[] lights)
        {
            var root = new GameObject("_Lights");
            root.transform.SetParent(instance.transform, false);
            var count = 0;
            foreach (var l in lights)
            {
                if (l.bakeType == Baked)
                    continue;
                var type = l.type switch
                {
                    0 => LightType.Spot,
                    1 => LightType.Directional,
                    _ => LightType.Point,
                };
                var go = new GameObject(l.name);
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(V3(l.position), Q(l.rotation));
                var hd = go.AddHDLight(type);
                var light = go.GetComponent<Light>();
                light.color = new Color(l.color[0], l.color[1], l.color[2]);
                light.range = l.range;
                if (type == LightType.Spot)
                {
                    light.spotAngle = l.spotAngle;
                    light.innerSpotAngle = Mathf.Min(l.innerSpotAngle, l.spotAngle);
                    light.enableSpotReflector = false;
                }
                light.lightUnit = type == LightType.Directional ? LightUnit.Lux : LightUnit.Candela;
                light.intensity = type == LightType.Directional ? l.intensity : l.intensity * Mathf.PI;
                light.lightmapBakeType = LightmapBakeType.Realtime;
                var shadows = l.shadows && type != LightType.Directional;
                hd.EnableShadows(shadows);
                light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                light.shadowNearPlane = l.shadowNearPlane;
                // GFL2 limits some lights to its characters' layer (the refitting room's sun),
                // and StrategyDiorama keeps those off the room's surfaces.
                light.cullingMask = l.cullingMask;
                hd.linkShadowLayers = false;
                light.renderingLayerMask = l.bakeType == Mixed ? unchecked((int)~RoomRenderingLayer) : -1;
                // HDRP ignores the culling mask, so a light GFL2 keeps off its characters' layer
                // takes the room's rendering layer alone (Womenace/DollToon skips it). The room's own
                // shading reads its lights as globals and ignores this.
                hd.lightlayersMask = (UnityEngine.Rendering.HighDefinition.RenderingLayerMask)((l.cullingMask & (1 << CharacterLayer)) != 0 ? uint.MaxValue : RoomRenderingLayer);
                count++;
            }
            return count;
        }

        // GFL2's baked cubemap as it ships: the bytes go into a Unity cube of the same format and
        // mip count unchanged, so its HDR range and its own prefiltered mips survive.
        private static Cubemap BuildCube(string src, string dst, ProbeEntry p)
        {
            var format = (TextureFormat)p.format;
            var data = File.ReadAllBytes(src + "/" + p.file);
            var perFace = 0;
            for (int m = 0; m < p.mips; m++)
                perFace += MipBytes(format, p.size >> m);
            if (data.Length < perFace * 6)
            {
                Debug.LogWarning($"BakeRoom: probe {p.name} has {data.Length} bytes, {perFace * 6} expected");
                return null;
            }
            var cube = new Cubemap(p.size, format, p.mips) { name = p.name };
            for (int face = 0; face < 6; face++)
            {
                var offset = face * perFace;
                for (int m = 0; m < p.mips; m++)
                {
                    var bytes = MipBytes(format, p.size >> m);
                    cube.SetPixelData(data, m, (CubemapFace)face, offset);
                    offset += bytes;
                }
            }
            cube.Apply(false, true);
            var path = $"{dst}/probes/{p.name}.asset";
            ProbesWritten.Add(path);
            var existing = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(cube, path);
                return cube;
            }
            EditorUtility.CopySerialized(cube, existing);
            existing.name = p.name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(cube);
            return existing;
        }

        // The probe cubes this bake has written, by asset path.
        private static readonly HashSet<string> ProbesWritten = new();

        private static int MipBytes(TextureFormat format, int size)
        {
            var blocks = Mathf.Max(1, (Mathf.Max(1, size) + 3) / 4);
            return format switch
            {
                TextureFormat.BC6H or TextureFormat.BC7 or TextureFormat.DXT5 or TextureFormat.BC5 => blocks * blocks * 16,
                TextureFormat.DXT1 or TextureFormat.BC4 => blocks * blocks * 8,
                TextureFormat.RGBAHalf => Mathf.Max(1, size) * Mathf.Max(1, size) * 8,
                _ => throw new NotSupportedException("probe cube format " + format),
            };
        }

        // The lightmap (linear HDR) and shadowmask (one channel), both sampled on UV1 at the atlas
        // layout the export keeps, square and clamped.
        private static Texture2D ImportBakedMap(string src, string relative, bool singleChannel)
        {
            if (string.IsNullOrEmpty(relative))
                return null;
            var path = src + "/" + relative;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = singleChannel ? TextureImporterType.SingleChannel : TextureImporterType.Default;
                if (singleChannel)
                {
                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                    importer.SetTextureSettings(settings);
                }
                importer.sRGBTexture = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 4096;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                Debug.LogWarning("BakeRoom: " + path + " did not import.");
            return texture;
        }

        // Scenery is mostly seen at grazing angles (floors, walls, ceilings), where bilinear,
        // non-anisotropic sampling smears the texture into a low-resolution look. _SMO and the normal
        // map are data, read raw as GFL2 reads them.
        private static void ConfigureTexture(string dir, string relative, bool srgb)
        {
            if (string.IsNullOrEmpty(relative)) return;
            var path = dir + "/" + relative;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            if (importer.textureType == TextureImporterType.Default && importer.sRGBTexture == srgb
                && importer.anisoLevel == 8 && importer.filterMode == FilterMode.Trilinear) return;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.anisoLevel = 8;
            importer.filterMode = FilterMode.Trilinear;
            importer.SaveAndReimport();
        }

        // Check that the imported tangents run along +u, as GFL2's do: a failed handedness
        // conversion shows as a share near zero, and every normal map then shades mirrored.
        private static void ReportTangents(GameObject instance)
        {
            int along = 0, total = 0;
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || mesh.tangents.Length == 0 || mesh.uv.Length == 0)
                    continue;
                var v = mesh.vertices;
                var uv = mesh.uv;
                var tangents = mesh.tangents;
                var tris = mesh.triangles;
                for (int i = 0; i + 2 < tris.Length && total < 20000; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    Vector3 e1 = v[b] - v[a], e2 = v[c] - v[a];
                    Vector2 d1 = uv[b] - uv[a], d2 = uv[c] - uv[a];
                    var det = d1.x * d2.y - d2.x * d1.y;
                    if (Mathf.Abs(det) < 1e-8f)
                        continue;
                    var dPdu = (e1 * d2.y - e2 * d1.y) / det;
                    if (dPdu.sqrMagnitude < 1e-10f)
                        continue;
                    total++;
                    if (Vector3.Dot(dPdu, tangents[a]) > 0f)
                        along++;
                }
            }
            Debug.Log($"BakeRoom: tangents along +u on {along} of {total} sampled triangles");
        }

        // An inactive screen-filling quad with the Womenace/HoloScreen material, so the shader
        // ships in the room's bundle. At runtime it is parented to the room camera and shows
        // another camera's render as an additive hologram (the holotable's system map).
        private static void AddHoloScreen(GameObject instance, string dst)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/HoloScreen.shader");
            if (shader == null)
            {
                Debug.LogWarning("BakeRoom: Assets/Shaders/HoloScreen.shader not found, no holo screen.");
                return;
            }
            var mat = new Material(shader) { name = "_HoloScreen" };
            AssetDatabase.CreateAsset(mat, dst + "/materials/_HoloScreen.mat");

            var meshPath = dst + "/holo_quad.asset";
            AssetDatabase.DeleteAsset(meshPath);
            var mesh = new Mesh { name = "holo_quad" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1000, 1000, 1000));
            AssetDatabase.CreateAsset(mesh, meshPath);

            var go = new GameObject("_HoloScreen");
            go.transform.SetParent(instance.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
        }

        // GFL2's colours as authored. Unity converts material colours from gamma to linear in both
        // games, so the values carry over unchanged, HDR ones included.
        private static Color Colour(float[] c, Color fallback) =>
            c is { Length: >= 3 } ? new Color(c[0], c[1], c[2], c.Length > 3 ? c[3] : 1f) : fallback;

        private static Texture2D Load(string dir, string relative) =>
            string.IsNullOrEmpty(relative) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + relative);

        private static float Volume(Bounds b) => b.size.x * b.size.y * b.size.z;
        private static Vector3 V3(float[] v) => new(v[0], v[1], v[2]);
        private static Quaternion Q(float[] q) => new(q[0], q[1], q[2], q[3]);

        private static string Sanitise(string s) =>
            new(s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
    }
}
