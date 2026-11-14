using UnityEditor;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Batchmode utility: the hologram a doll's model wears in a room, effects/doll_hologram/main.
    // The root only carries the material into the bundle, on an empty MeshFilter. The runtime
    // copies it onto each of the doll's renderers with that renderer's own texture.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.BuildDollHologram.Run -logFile doll_hologram.log
    public static class BuildDollHologram
    {
        private const string Authored = "Assets/Authored/effects/doll_hologram";
        private const string PrefabPath = "Assets/Prefabs/effects/doll_hologram/main.prefab";

        public static void Run()
        {
            var shader = Shader.Find("Womenace/DollHologram") ?? throw new System.Exception("no shader Womenace/DollHologram");
            System.IO.Directory.CreateDirectory(Authored);
            AssetDatabase.Refresh();
            var path = $"{Authored}/hologram.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            AssetDatabase.SaveAssets();

            var root = new GameObject("doll_hologram");
            root.AddComponent<MeshFilter>();
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"BuildDollHologram: wrote {PrefabPath}.");
        }
    }
}
