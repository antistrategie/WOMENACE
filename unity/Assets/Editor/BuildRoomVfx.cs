using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Rebuilds a GFL2 scene's effects (particle systems and effect meshes) as a prefab, from the
    // plain text scripts/diorama/vfx_to_unity.py writes under Assets/Authored/environment/<name>/:
    // the node hierarchy with local transforms and active flags, effect meshes, materials on the
    // Womenace/GfVfx port of GFL2's effect shaders, and every particle system and particle renderer
    // property set through SerializedObject by its serialized path, so the particles are GFL2's own
    // (emission, shapes, curves, gradients, texture sheets, ...). The prefab sits in the source
    // scene's space, which is the diorama room's.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.BuildRoomVfx.Run -name ops_room_vfx
    public static class BuildRoomVfx
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            var name = "ops_room_vfx";
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-name") name = args[i + 1];
            Build(name);
            EditorApplication.Exit(0);
        }

        private static float F(string s) => float.Parse(s, Inv);

        public static void Build(string name)
        {
            var src = $"Assets/Authored/environment/{name}";
            var dst = $"Assets/Prefabs/environment/{name}";
            Directory.CreateDirectory(dst);
            Directory.CreateDirectory($"{dst}/meshes");
            Directory.CreateDirectory($"{dst}/materials");
            AssetDatabase.Refresh();

            var meshes = new Dictionary<string, Mesh>();
            foreach (var file in Directory.GetFiles($"{src}/meshes", "*.txt"))
            {
                var meshName = Path.GetFileNameWithoutExtension(file);
                var mesh = ReadMesh(file, meshName);
                var path = $"{dst}/meshes/{meshName}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                meshes[meshName] = mesh;
            }

            var materials = ReadMaterials($"{src}/materials.txt", $"{src}/textures", $"{dst}/materials");
            ApplyOverrides($"{src}/overrides.txt", $"{src}/textures", materials);

            var root = new GameObject(name);
            var nodes = new Dictionary<string, GameObject>();
            var parents = new Dictionary<string, string>();
            var inactive = new List<GameObject>();
            var pending = new Dictionary<(string, string), SerializedObject>();
            int set = 0, missed = 0;
            var missedPaths = new HashSet<string>();
            foreach (var raw in File.ReadAllLines($"{src}/nodes.txt"))
            {
                var f = raw.Split('\t');
                switch (f[0])
                {
                    case "NODE":
                    {
                        var go = new GameObject(f[3]);
                        nodes[f[1]] = go;
                        parents[f[1]] = f[2];
                        go.transform.SetParent(root.transform, false);
                        go.transform.localPosition = new Vector3(F(f[5]), F(f[6]), F(f[7]));
                        go.transform.localRotation = new Quaternion(F(f[8]), F(f[9]), F(f[10]), F(f[11]));
                        go.transform.localScale = new Vector3(F(f[12]), F(f[13]), F(f[14]));
                        if (f[4] == "0")
                            inactive.Add(go);
                        break;
                    }
                    case "MESH":
                    {
                        var m = f[2].StartsWith("builtin:") ? BuiltinMesh(f[2].Substring(8)) : meshes.GetValueOrDefault(f[2]);
                        if (m != null)
                            nodes[f[1]].AddComponent<MeshFilter>().sharedMesh = m;
                        break;
                    }
                    case "MATS":
                    {
                        var renderer = nodes[f[1]].AddComponent<MeshRenderer>();
                        renderer.sharedMaterials = f[2].Split(',').Select(n => materials.GetValueOrDefault(n)).ToArray();
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        break;
                    }
                    case "COMP":
                    {
                        var go = nodes[f[1]];
                        // Unity's destroyed-object null defeats ??, so test explicitly.
                        Component component;
                        if (f[2] == "ParticleSystem")
                        {
                            var ps = go.GetComponent<ParticleSystem>();
                            component = ps != null ? ps : go.AddComponent<ParticleSystem>();
                        }
                        else
                        {
                            var pr = go.GetComponent<ParticleSystemRenderer>();
                            component = pr != null ? pr : go.AddComponent<ParticleSystemRenderer>();
                        }
                        pending[(f[1], f[2])] = new SerializedObject(component);
                        break;
                    }
                    case "P":
                    {
                        var so = pending[(f[1], f[2])];
                        var prop = so.FindProperty(f[3]);
                        if (prop == null || !Apply(prop, f[4], f[5], materials, meshes))
                        {
                            missed++;
                            missedPaths.Add(f[2] + ":" + System.Text.RegularExpressions.Regex.Replace(f[3], @"\[\d+\]", "[]"));
                            break;
                        }
                        set++;
                        // Arrays resize at once so their elements resolve on the next lines.
                        if (f[4] == "size")
                            so.ApplyModifiedPropertiesWithoutUndo();
                        break;
                    }
                }
            }
            foreach (var so in pending.Values)
                so.ApplyModifiedPropertiesWithoutUndo();
            // A particle drawn as a mesh is a card fixed in the room (a window's light shaft), and
            // its material fades it where a camera GFL2 never had would see it edge-on.
            foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                if (renderer.renderMode == ParticleSystemRenderMode.Mesh)
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null)
                            material.SetFloat("_CardFade", 1f);
            // Parent after building, so local transforms stay as authored.
            foreach (var (key, go) in nodes)
                if (!string.IsNullOrEmpty(parents[key]) && nodes.TryGetValue(parents[key], out var parent))
                    go.transform.SetParent(parent.transform, false);
            foreach (var go in inactive)
                go.SetActive(false);

            var prefabPath = $"{dst}/main.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"BuildRoomVfx: {nodes.Count} nodes, {materials.Count} materials, {meshes.Count} meshes, {set} properties set, {missed} missed -> {prefabPath}");
            foreach (var p in missedPaths.OrderBy(p => p).Take(60))
                Debug.Log($"BuildRoomVfx: MISSED {p}");
        }

        private static bool Apply(SerializedProperty prop, string kind, string value,
            Dictionary<string, Material> materials, Dictionary<string, Mesh> meshes)
        {
            switch (kind)
            {
                case "size":
                    if (!prop.isArray)
                        return false;
                    prop.arraySize = int.Parse(value, Inv);
                    return true;
                case "number":
                {
                    var v = double.Parse(value, Inv);
                    switch (prop.propertyType)
                    {
                        case SerializedPropertyType.Float: prop.floatValue = (float)v; return true;
                        case SerializedPropertyType.Integer:
                        case SerializedPropertyType.LayerMask:
                        case SerializedPropertyType.Character: prop.intValue = (int)System.Math.Round(v); return true;
                        case SerializedPropertyType.Enum: prop.intValue = (int)System.Math.Round(v); return true;
                        case SerializedPropertyType.Boolean: prop.boolValue = v != 0; return true;
                        default: return false;
                    }
                }
                case "colour":
                {
                    var c = value.Split(',').Select(F).ToArray();
                    if (prop.propertyType != SerializedPropertyType.Color)
                        return false;
                    prop.colorValue = new Color(c[0], c[1], c[2], c[3]);
                    return true;
                }
                case "vector":
                {
                    var c = value.Split(',').Select(F).ToArray();
                    switch (prop.propertyType)
                    {
                        case SerializedPropertyType.Vector2: prop.vector2Value = new Vector2(c[0], c[1]); return true;
                        case SerializedPropertyType.Vector3: prop.vector3Value = new Vector3(c[0], c[1], c.Length > 2 ? c[2] : 0); return true;
                        case SerializedPropertyType.Vector4: prop.vector4Value = new Vector4(c[0], c[1], c.Length > 2 ? c[2] : 0, c.Length > 3 ? c[3] : 0); return true;
                        case SerializedPropertyType.Quaternion: prop.quaternionValue = new Quaternion(c[0], c[1], c[2], c[3]); return true;
                        default: return false;
                    }
                }
                case "curve":
                {
                    if (prop.propertyType != SerializedPropertyType.AnimationCurve)
                        return false;
                    var parts = value.Split(';');
                    var keys = new List<Keyframe>();
                    if (parts.Length > 2 && parts[2].Length > 0)
                        foreach (var k in parts[2].Split('|'))
                        {
                            var v = k.Split(',').Select(F).ToArray();
                            keys.Add(new Keyframe(v[0], v[1], v[2], v[3], v[5], v[6]) { weightedMode = (WeightedMode)(int)v[4] });
                        }
                    prop.animationCurveValue = new AnimationCurve(keys.ToArray())
                    {
                        preWrapMode = Wrap(int.Parse(parts[0], Inv)),
                        postWrapMode = Wrap(int.Parse(parts[1], Inv)),
                    };
                    return true;
                }
                case "gradient":
                {
                    if (prop.propertyType != SerializedPropertyType.Gradient)
                        return false;
                    var parts = value.Split(';');
                    var colours = parts[1].Length > 0 ? parts[1].Split('|').Select(k => { var v = k.Split(',').Select(F).ToArray(); return new GradientColorKey(new Color(v[0], v[1], v[2]), v[3]); }).ToArray() : new GradientColorKey[0];
                    var alphas = parts[2].Length > 0 ? parts[2].Split('|').Select(k => { var v = k.Split(',').Select(F).ToArray(); return new GradientAlphaKey(v[0], v[1]); }).ToArray() : new GradientAlphaKey[0];
                    var g = new Gradient { mode = int.Parse(parts[0], Inv) == 1 ? GradientMode.Fixed : GradientMode.Blend };
                    g.SetKeys(colours, alphas);
                    prop.gradientValue = g;
                    return true;
                }
                case "ref":
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference)
                        return false;
                    var split = value.Split(':');
                    Object target = split[0] switch
                    {
                        "material" => materials.GetValueOrDefault(split[1]),
                        "mesh" => meshes.GetValueOrDefault(split[1]),
                        "builtin" => BuiltinMesh(split[1]),
                        _ => null,
                    };
                    prop.objectReferenceValue = target;
                    return target != null;
                }
            }
            return false;
        }

        // A curve's serialised m_PreInfinity/m_PostInfinity is Unity's internal wrap mode: 0 ping-pong,
        // 1 repeat, 2 clamp.
        private static WrapMode Wrap(int infinity) => infinity switch
        {
            0 => WrapMode.PingPong,
            1 => WrapMode.Loop,
            _ => WrapMode.ClampForever,
        };

        private static Mesh ReadMesh(string file, string name)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var c = new List<Color>();
            var subs = new List<int[]>();
            foreach (var line in File.ReadLines(file))
            {
                var f = line.Split(' ');
                switch (f[0])
                {
                    case "v": v.Add(new Vector3(F(f[1]), F(f[2]), F(f[3]))); break;
                    case "n": n.Add(new Vector3(F(f[1]), F(f[2]), F(f[3]))); break;
                    case "u": u.Add(new Vector2(F(f[1]), F(f[2]))); break;
                    case "c": c.Add(new Color(F(f[1]), F(f[2]), F(f[3]), F(f[4]))); break;
                    case "s": subs.Add(f.Skip(1).Select(int.Parse).ToArray()); break;
                }
            }
            var mesh = new Mesh { name = name, indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(v);
            if (n.Count == v.Count) mesh.SetNormals(n);
            if (u.Count == v.Count) mesh.SetUVs(0, u);
            if (c.Count == v.Count) mesh.SetColors(c);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++)
                mesh.SetTriangles(subs[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Unity's built-in meshes by their fixed ids, which the player ships. The plane, cylinder
        // and sphere go by "New-" names: the plain names are older meshes under other ids (the
        // old plane is 10204, a fraction of the 10 m one's size, on which GFL2's window light
        // shafts are specks outside the windows).
        private static Mesh BuiltinMesh(string id) => id switch
        {
            "10209" => Resources.GetBuiltinResource<Mesh>("New-Plane.fbx"),
            "10210" => Resources.GetBuiltinResource<Mesh>("Quad.fbx"),
            "10202" => Resources.GetBuiltinResource<Mesh>("Cube.fbx"),
            "10206" => Resources.GetBuiltinResource<Mesh>("New-Cylinder.fbx"),
            "10207" => Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx"),
            _ => null,
        };

        // GFL2's effect shader families as Womenace/GfVfx numbers them, and each one's pass state:
        // its blend (a fixed factor or the material property GFL2 reads it from), cull and depth
        // write, from GFL2's compiled shaders.
        private sealed class Family
        {
            public readonly int Index;
            public readonly string Src, Dst, Cull, ZWrite;

            public Family(int index, string src, string dst, string cull, string zWrite) =>
                (Index, Src, Dst, Cull, ZWrite) = (index, src, dst, cull, zWrite);
        }

        private static readonly Dictionary<string, Family> Families = new()
        {
            ["UV_Effect_UVAll_Fast"] = new(0, "5", "_BlendMode", "_CullMode", "_DepthMode"),
            ["UV_Effect_UVAll_Fast_2"] = new(1, "5", "_BlendMode", "_CullMode", "_DepthMode"),
            // No port of its own: drawn as the family it is a later version of.
            ["UV_Effect_UVAll_Fast_2_New"] = new(1, "5", "_BlendMode", "_CullMode", "_DepthMode"),
            ["Particle_Base_Bland_Fast"] = new(2, "SrcBlend", "DstBlend", "0", "0"),
            ["UV_Effect_Base_Fast"] = new(3, "5", "10", "0", "0"),
            ["UV_Effect_Base_Mask_Fast"] = new(4, "5", "10", "0", "0"),
            ["add"] = new(5, "1", "1", "0", "0"),
            ["gf_sfx_holographic3"] = new(6, "_SrcBlendMode", "_DstBlendMode", "_CullMode", "_DepthMode"),
            ["gf_vfx_rgb_Glitch_Noise_Fast"] = new(7, "5", "_BlendMode", "0", "0"),
            ["gf_vfx_pixelization"] = new(8, "_SrcBlendMode", "_DstBlendMode", "_CullMode", "0"),
        };

        // GFL2's keywords, as the switches Womenace/GfVfx reads.
        private static readonly Dictionary<string, string> KeywordSwitches = new()
        {
            ["_ALPHAR_ON"] = "_AlphaR", ["_ALPHA_IS_R"] = "_AlphaR",
            ["_USEMASK_ON"] = "_UseMask", ["_USE_MASK"] = "_UseMask",
            ["_USETURBULENCE_ON"] = "_UseTurbulence", ["_USE_TURBULENCE"] = "_UseTurbulence",
            ["_USECLIP_ON"] = "_UseClip", ["_USE_DISSOLVE"] = "_UseClip",
            ["_USEBACKCOLOR_ON"] = "_UseBackColor", ["_USE_BACK_COLOR"] = "_UseBackColor",
            ["_SOFTPARTICLES_ON"] = "_SoftParticles",
            ["HORIZONTAL"] = "_Horizontal",
        };

        // The effect textures with GFL2's own import: wrap mode and colour space.
        private static void ImportTextures(string src)
        {
            var list = $"{src}/textures.txt";
            if (!File.Exists(list))
                return;
            foreach (var line in File.ReadAllLines(list))
            {
                var f = line.Split('\t');
                if (f.Length < 3)
                    continue;
                if (AssetImporter.GetAtPath($"{src}/textures/{f[0]}.png") is not TextureImporter importer)
                    continue;
                var wrap = f[1] == "1" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                var srgb = f[2] != "0";
                if (importer.wrapMode == wrap && importer.sRGBTexture == srgb)
                    continue;
                importer.wrapMode = wrap;
                importer.sRGBTexture = srgb;
                importer.SaveAndReimport();
            }
        }

        // The mod's own changes to GFL2's materials, kept apart from materials.txt so a fresh dump
        // does not lose them. One line per change, tab separated:
        //   TEX <material> <property> <texture name under textures/>
        private static void ApplyOverrides(string file, string textures, Dictionary<string, Material> materials)
        {
            if (!File.Exists(file))
                return;
            foreach (var raw in File.ReadAllLines(file))
            {
                var f = raw.Split('\t');
                if (f.Length < 4 || f[0] != "TEX")
                    continue;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{textures}/{f[3]}.png");
                if (!materials.TryGetValue(f[1], out var material) || texture == null || !material.HasProperty(f[2]))
                {
                    Debug.LogWarning($"BuildRoomVfx: override not applied: {raw}");
                    continue;
                }
                material.SetTexture(f[2], texture);
                EditorUtility.SetDirty(material);
                Debug.Log($"BuildRoomVfx: {f[1]} {f[2]} -> {f[3]}");
            }
        }

        // Every GFL2 material on Womenace/GfVfx: its values copied across under their own names, its
        // keywords as switches, its family and its family's pass state.
        private static Dictionary<string, Material> ReadMaterials(string file, string textures, string outDir)
        {
            ImportTextures(Path.GetDirectoryName(textures).Replace('\\', '/'));
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/GfVfx.shader");
            var result = new Dictionary<string, Material>();
            Material current = null;
            Family family = null;
            var floats = new Dictionary<string, float>();
            void Finish()
            {
                if (current == null) return;
                if (family == null)
                    Debug.LogWarning($"BuildRoomVfx: {current.name} uses a shader with no port, drawn as UV_Effect_UVAll_Fast");
                family ??= Families["UV_Effect_UVAll_Fast"];
                float State(string source) => float.TryParse(source, NumberStyles.Float, Inv, out var v) ? v : floats.TryGetValue(source, out var m) ? m : 0f;
                current.SetFloat("_Family", family.Index);
                current.SetFloat("_WmSrcBlend", State(family.Src));
                current.SetFloat("_WmDstBlend", State(family.Dst));
                current.SetFloat("_WmCull", State(family.Cull));
                current.SetFloat("_WmZWrite", State(family.ZWrite));
                current.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                var path = $"{outDir}/{current.name}.mat";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(current, path);
                result[current.name] = current;
            }
            foreach (var raw in File.ReadAllLines(file))
            {
                var f = raw.Split('\t');
                switch (f[0])
                {
                    case "MAT":
                        Finish();
                        current = new Material(shader) { name = f[1] };
                        Families.TryGetValue(f[2].Substring(f[2].LastIndexOf('/') + 1), out family);
                        floats.Clear();
                        break;
                    case "TEX":
                        if (!current.HasProperty(f[1]))
                            break;
                        current.SetTexture(f[1], AssetDatabase.LoadAssetAtPath<Texture2D>($"{textures}/{f[2]}.png"));
                        current.SetTextureScale(f[1], new Vector2(F(f[3]), F(f[4])));
                        current.SetTextureOffset(f[1], new Vector2(F(f[5]), F(f[6])));
                        break;
                    case "F":
                        floats[f[1]] = F(f[2]);
                        if (current.HasProperty(f[1]))
                            current.SetFloat(f[1], F(f[2]));
                        break;
                    case "C":
                        if (current.HasProperty(f[1]))
                            current.SetVector(f[1], new Vector4(F(f[2]), F(f[3]), F(f[4]), F(f[5])));
                        break;
                    case "KW":
                    {
                        // Switches come from the keywords alone: a toggle's float can disagree with them.
                        foreach (var name in KeywordSwitches.Values.Distinct())
                            current.SetFloat(name, 0f);
                        foreach (var kw in f.Length > 1 ? f[1].Split(' ') : System.Array.Empty<string>())
                            if (KeywordSwitches.TryGetValue(kw, out var name))
                                current.SetFloat(name, 1f);
                        break;
                    }
                }
            }
            Finish();
            return result;
        }
    }
}
