using System.IO;
using UnityEditor;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Carries GFL2's ops room colour lookup (its URP ColorLookup strip, 32 slices of 32x32, sRGB
    // display values) into a bundle as an uncompressed, readable texture. The runtime turns it into
    // HDRP's external tonemapping LUT, so the strip must keep its exact 8-bit values: an imported
    // PNG would be compressed, a texture asset written here keeps its format. A disabled renderer's
    // material holds the reference so the prefab loads like any other diorama part.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.BuildRoomGrade.Run
    public static class BuildRoomGrade
    {
        public static void Run()
        {
            Build();
            EditorApplication.Exit(0);
        }

        public static void Build()
        {
            const string src = "Assets/Authored/environment/ops_room_grade/gfl2_lut.png";
            const string dst = "Assets/Prefabs/environment/ops_room_grade";
            Directory.CreateDirectory(dst);
            AssetDatabase.Refresh();

            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            decoded.LoadImage(File.ReadAllBytes(src));
            var lut = new Texture2D(decoded.width, decoded.height, TextureFormat.RGBA32, false, true)
            {
                name = "gfl2_lut",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            lut.SetPixels32(decoded.GetPixels32());
            lut.Apply(false, false);
            Object.DestroyImmediate(decoded);
            var lutPath = $"{dst}/gfl2_lut.asset";
            AssetDatabase.DeleteAsset(lutPath);
            AssetDatabase.CreateAsset(lut, lutPath);

            var material = new Material(Shader.Find("Womenace/GfVfx")) { name = "grade" };
            material.SetTexture("_MainTex", lut);
            var matPath = $"{dst}/grade.mat";
            AssetDatabase.DeleteAsset(matPath);
            AssetDatabase.CreateAsset(material, matPath);

            var root = new GameObject("ops_room_grade");
            var holder = root.AddComponent<MeshRenderer>();
            holder.sharedMaterial = material;
            holder.enabled = false;
            PrefabUtility.SaveAsPrefabAsset(root, $"{dst}/main.prefab");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"BuildRoomGrade: {lut.width}x{lut.height} lut -> {dst}/main.prefab");
        }
    }
}
