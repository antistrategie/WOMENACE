using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace Womenace.EditorTools
{
    // Bakes the mod's UI fonts (Assets/UI/fonts/<name>.ttf) into signed distance field font
    // assets beside them (<name>-SDF.asset), which the stylesheets name. The mod draws its own UI in
    // MENACE's face, Jura Bold, and ships it rather than borrow the game's copy, whose life is the
    // game's to end.
    //
    // The atlas is baked whole and static: a dynamic one adds a glyph the first time text needs it,
    // which UI Toolkit can ask for from its job threads. A character outside the set falls back to
    // the game's own fonts.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.BuildFonts.Run
    public static class BuildFonts
    {
        private const string Folder = "Assets/UI/fonts";
        private const int SamplingPointSize = 48;
        private const int AtlasPadding = 6;
        private const int AtlasSize = 1024;

        public static void Run()
        {
            Build();
            EditorApplication.Exit(0);
        }

        public static void Build()
        {
            AssetDatabase.Refresh();
            foreach (var source in Directory.GetFiles(Folder, "Jura-*.ttf"))
                Bake(source.Replace('\\', '/'));
            AssetDatabase.SaveAssets();
        }

        // Latin with its accents, the punctuation the mod's text uses, and Cyrillic.
        private static string Characters()
        {
            var text = new StringBuilder();
            void Range(int from, int to)
            {
                for (var c = from; c <= to; c++)
                    text.Append((char)c);
            }
            Range(0x20, 0x7E);
            Range(0xA0, 0xFF);
            Range(0x100, 0x17F);
            Range(0x400, 0x45F);
            text.Append("–—‘’‚“”„•…‰‹›€™←↑→↓·");
            return text.ToString();
        }

        private static void Bake(string source)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(source);
            if (font == null)
                throw new InvalidDataException($"'{source}' did not import as a font");
            var target = $"{Folder}/{Path.GetFileNameWithoutExtension(source)}-SDF.asset";
            AssetDatabase.DeleteAsset(target);

            var asset = FontAsset.CreateFontAsset(font, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, true);
            if (asset == null)
                throw new InvalidDataException($"no font asset could be made from '{source}'");
            asset.name = Path.GetFileNameWithoutExtension(target);
            if (!asset.TryAddCharacters(Characters(), out var missing))
                Debug.Log($"BuildFonts: {source} has no glyph for {missing.Length} of the characters asked for");
            asset.atlasPopulationMode = AtlasPopulationMode.Static;

            AssetDatabase.CreateAsset(asset, target);
            for (var i = 0; i < asset.atlasTextures.Length; i++)
            {
                var atlas = asset.atlasTextures[i];
                atlas.name = $"{asset.name} Atlas {i}";
                AssetDatabase.AddObjectToAsset(atlas, asset);
            }
            asset.material.name = $"{asset.name} Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            EditorUtility.SetDirty(asset);
            Debug.Log($"BuildFonts: {target}, {asset.characterTable.Count} characters on {asset.atlasTextures.Length} atlas(es)");
        }
    }
}
