using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Raises import quality on the dolls' surface textures.
//
// Two settings, both left at Unity's defaults until now and both costing detail at
// close range:
//
//   compression  Default block compression is BC1/BC3, which correlates the colour
//                channels and bands across smooth gradients. Skin is nothing but
//                smooth gradients, and an RMO map is three independent data channels
//                that have no business being correlated at all. CompressedHQ gives
//                BC7 at the same eight bits per pixel as BC3, so this is close to
//                free.
//
//   aniso        Off by default, which means the sampler cannot stretch its
//                footprint on a surface seen at an angle. Cheeks, the side of the
//                head and the length of a limb all blur for that reason, and the
//                detail those maps carry never reaches the screen.
//
// Trilinear alongside them, so mip transitions stop showing as a visible band
// across a surface that recedes.
//
// The lookup maps are deliberately excluded. A ramp atlas and the face SDF are data
// read at exact coordinates, not images: compressing them would quantise the very
// values the shader thresholds against, and filtering across their rows would blend
// bands that mean different things.
public static class DollTextureQuality
{
    private const string Root = "Assets/Authored";
    private static readonly string[] Excluded = { "/ramps/", "/shared/" };

    [MenuItem("Jiangyu/Raise Doll Texture Quality")]
    public static void Run()
    {
        var changed = new List<string>();
        var skipped = 0;

        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var normalised = path.Replace('\\', '/');
            var excluded = false;
            foreach (var fragment in Excluded)
                if (normalised.Contains(fragment)) excluded = true;
            if (excluded) { skipped++; continue; }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            var before = $"{importer.textureCompression}/{importer.anisoLevel}/{importer.filterMode}";
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.anisoLevel = 8;
            importer.filterMode = FilterMode.Trilinear;
            var after = $"{importer.textureCompression}/{importer.anisoLevel}/{importer.filterMode}";
            if (before == after) continue;

            importer.SaveAndReimport();
            changed.Add($"{Path.GetFileName(path)} {before} -> {after}");
        }

        Debug.Log($"TEXQUALITY raised {changed.Count} texture(s), skipped {skipped} lookup map(s)");
        foreach (var line in changed) Debug.Log("TEXQUALITY   " + line);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
