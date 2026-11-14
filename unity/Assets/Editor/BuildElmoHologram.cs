using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Batchmode utility: GFL2's Elmo as the hologram of the ship that stands over the holotable,
    // environment/elmo/main. Each hull part wears Womenace/DollHologram with its own texture, for
    // its light and dark. The root is the middle of the hull's underside, so the ship turns about
    // its own middle and stands on whatever height it is put at.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.BuildElmoHologram.Run -logFile elmo.log
    public static class BuildElmoHologram
    {
        private const string Authored = "Assets/Authored/environment/elmo";
        private const string PrefabPath = "Assets/Prefabs/environment/elmo/main.prefab";
        private static readonly string[] Parts = { "a", "b", "c", "d", "e", "f", "g" };

        public static void Run()
        {
            var shader = Shader.Find("Womenace/DollHologram") ?? throw new System.Exception("no shader Womenace/DollHologram");
            var root = new GameObject("elmo");
            var hull = new GameObject("hull");
            hull.transform.SetParent(root.transform, false);
            var renderers = new List<MeshRenderer>();
            foreach (var part in Parts)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Authored}/elmo_{part}.obj") ?? throw new System.Exception($"no model for part {part}");
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Authored}/elmo_{part}.png") ?? throw new System.Exception($"no texture for part {part}");
                var path = $"{Authored}/elmo_{part}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                material.SetTexture("_BaseMap", texture);
                EditorUtility.SetDirty(material);
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    var piece = new GameObject($"elmo_{part}");
                    piece.transform.SetParent(hull.transform, false);
                    piece.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                    piece.transform.localScale = filter.transform.lossyScale;
                    piece.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    var renderer = piece.AddComponent<MeshRenderer>();
                    var materials = new Material[filter.sharedMesh.subMeshCount];
                    for (var i = 0; i < materials.Length; i++)
                        materials[i] = material;
                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderers.Add(renderer);
                }
            }
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            hull.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"BuildElmoHologram: wrote {PrefabPath}, {renderers.Count} parts, size {bounds.size}, middle {bounds.center}.");
        }
    }
}
