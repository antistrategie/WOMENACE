using System.IO;
using UnityEditor;
using UnityEngine;

namespace WOMENACE.Editor
{
    // Asset helpers the batch-mode bakes share.
    public static class BakeAssets
    {
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // A texture with the import type and colour space its slot reads. A GFL2 rip arrives as
        // plain sRGB images, and its normal and rmo maps are data, so the importer is corrected
        // before the texture is handed out.
        public static Texture2D LoadTex(string path, bool sRGB, bool isNormal)
        {
            if (!File.Exists(path)) return null;
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                var tt = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (imp.textureType != tt || imp.sRGBTexture != sRGB)
                {
                    imp.textureType = tt;
                    imp.sRGBTexture = sRGB;
                    imp.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // The first of <dir>/<baseName><suffix>.png or .tga that exists, suffixes in order.
        public static Texture2D LoadTex(string dir, string baseName, string[] suffixes, bool sRGB, bool isNormal)
        {
            foreach (var sfx in suffixes)
                foreach (var ext in new[] { ".png", ".tga" })
                {
                    var path = $"{dir}/{baseName}{sfx}{ext}";
                    if (File.Exists(path)) return LoadTex(path, sRGB, isNormal);
                }
            return null;
        }
    }
}
