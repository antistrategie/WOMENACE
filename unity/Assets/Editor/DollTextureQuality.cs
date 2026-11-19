using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Sets the import quality of every texture under Assets/Authored outside the excluded
    // folders, which are mostly the dolls' surface textures. Each setting keeps detail at close
    // range:
    //
    //   compression  CompressedHQ gives BC7 at the same eight bits per pixel as BC3. BC1 and
    //                BC3 correlate the colour channels and band across smooth gradients, which
    //                skin is made of, and an RMO map is three independent data channels.
    //
    //   aniso        Lets the sampler stretch its footprint on a surface seen at an angle, so
    //                cheeks, the side of the head and the length of a limb keep their maps'
    //                detail.
    //
    //   trilinear    Keeps mip transitions from showing as a band across a receding surface.
    //
    // The lookup maps are left alone. A ramp atlas and the face SDF are data read at exact
    // coordinates: compression would quantise the values the shader thresholds against, and
    // filtering across their rows would blend bands that mean different things.
    public static class DollTextureQuality
    {
        private const string Root = "Assets/Authored";
        // Scenery and effects set their own sampling from GFL2 (BakeRoom, BuildGfl2Effects).
        private static readonly string[] Excluded = { "/ramps/", "/shared/", "/environment/", "/effects/" };

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

                var before = $"{importer.textureCompression}/{importer.anisoLevel}/{importer.filterMode}/{importer.textureType}/{importer.sRGBTexture}";
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.anisoLevel = 8;
                importer.filterMode = FilterMode.Trilinear;
                // GFL2's maps, by suffix wherever they sit (normalmap/ or beside a sheet, the two places
                // doll_shading looks): the normal as a normal map, the RMO or SMO mask as linear data,
                // the hair specular in sRGB, as GFL2 flags each.
                var name = Path.GetFileNameWithoutExtension(normalised);
                if (name.EndsWith("_n"))
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.sRGBTexture = false;
                }
                else if (name.EndsWith("_rmo") || name.EndsWith("_smo"))
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = false;
                }
                else if (name.EndsWith("_spc"))
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = true;
                }
                var after = $"{importer.textureCompression}/{importer.anisoLevel}/{importer.filterMode}/{importer.textureType}/{importer.sRGBTexture}";
                if (before == after) continue;

                importer.SaveAndReimport();
                changed.Add($"{Path.GetFileName(path)} {before} -> {after}");
            }

            Debug.Log($"TEXQUALITY raised {changed.Count} texture(s), left {skipped} lookup map(s), scenery and effect texture(s) alone");
            foreach (var line in changed) Debug.Log("TEXQUALITY   " + line);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
