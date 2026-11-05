using UnityEditor;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Batchmode utility: Nemesis's foresight visuals, effects/vision_ghost/main. The root
    // carries the ghost material on an empty MeshFilter, which the runtime fills with a still
    // copy of the enemy's body. The disabled "trail" child only carries the ribbon material
    // into the bundle, for the paths and attack threads the runtime builds. The disabled "label"
    // child carries the foresight text material (Womenace/VisionText over the glyph atlas from
    // scripts/vfx/make_vision_glyphs.py); the runtime builds the letter mesh.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.BuildVisionGhost.Run -logFile vision_ghost.log
    public static class BuildVisionGhost
    {
        private const string Authored = "Assets/Authored/effects/vision_ghost";
        private const string PrefabPath = "Assets/Prefabs/effects/vision_ghost/main.prefab";

        public static void Run()
        {
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Authored}/textures/T_Turbulence_025.png");
            if (noise == null)
                throw new System.Exception($"missing textures under {Authored}/textures");

            var ghost = Material($"{Authored}/ghost.mat", "Womenace/VisionGhost");
            ghost.SetTexture("_NoiseTex", noise);

            ghost.SetColor("_RimColor", new Color(1.3f, 0.55f, 2.4f, 1f));
            ghost.SetColor("_CoreColor", new Color(0.45f, 0.12f, 0.85f, 1f));
            ghost.SetFloat("_ExposureWeight", 0f);

            var trail = Material($"{Authored}/trail.mat", "Womenace/VisionTrail");
            trail.SetTexture("_NoiseTex", noise);
            trail.SetColor("_Color", new Color(1.1f, 0.45f, 2.0f, 1f));

            const string glyphPath = Authored + "/textures/glyphs.png";
            if (AssetImporter.GetAtPath(glyphPath) is TextureImporter importer)
            {
                // Distance values, not colour: linear and uncompressed so the edge stays at 0.5.
                importer.sRGBTexture = false;
                importer.alphaIsTransparency = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            var glyphs = AssetDatabase.LoadAssetAtPath<Texture2D>(glyphPath)
                ?? throw new System.Exception($"no glyph atlas at {glyphPath}");
            var text = Material($"{Authored}/text.mat", "Womenace/VisionText");
            text.SetTexture("_MainTex", glyphs);
            text.SetTexture("_NoiseTex", noise);
            text.renderQueue = -1;
            AssetDatabase.SaveAssets();

            var root = new GameObject("vision_ghost");
            root.AddComponent<MeshFilter>();
            var body = root.AddComponent<MeshRenderer>();
            body.sharedMaterial = ghost;
            body.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            body.receiveShadows = false;

            var carrier = new GameObject("trail");
            carrier.transform.SetParent(root.transform, false);
            carrier.AddComponent<MeshFilter>();
            var trailRenderer = carrier.AddComponent<MeshRenderer>();
            trailRenderer.sharedMaterial = trail;
            trailRenderer.enabled = false;
            carrier.SetActive(false);

            var label = new GameObject("label");
            label.transform.SetParent(root.transform, false);
            label.AddComponent<MeshFilter>();
            var labelRenderer = label.AddComponent<MeshRenderer>();
            labelRenderer.sharedMaterial = text;
            labelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            labelRenderer.receiveShadows = false;
            label.SetActive(false);

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"BuildVisionGhost: wrote {PrefabPath}.");
        }

        private static Material Material(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName) ?? throw new System.Exception($"no shader {shaderName}");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            return material;
        }
    }
}
