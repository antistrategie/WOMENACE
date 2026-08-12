using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WOMENACE.Editor
{
    // The face SDF lookup set for FBX-sourced models, as a two-step bake. The
    // glTF dolls get TEXCOORD_2 written straight into their binary by
    // scripts/doll/bake_face_sdf_uv.py; an FBX cannot be edited in place, so the
    // coordinates travel beside it as sidecar text files instead:
    //
    //   1. Dump writes each face mesh's rest positions to <srcDir>/face_sdf/
    //      <mesh>.pos.txt (one "x y z" line per vertex).
    //   2. scripts/doll/transfer_face_sdf_uv.py turns each into <mesh>.uv2.txt
    //      with the game's own lookup coordinates, via the same capture-cloud
    //      transfer the glTF path uses.
    //   3. WithBakedUv, called from a model's build, swaps in a mesh copy
    //      carrying the coordinates in UV channel 2, which is the TEXCOORD2
    //      the shader's SDF path reads.
    //
    // The sidecars are committed, so a rebuild needs no Python; the dump and
    // transfer rerun only when the FBX itself changes.
    public static class FaceSdfUv
    {
        // Batch entry:
        //   Unity -batchmode ... -executeMethod WOMENACE.Editor.FaceSdfUv.Dump
        //     -fbxPath Assets/Authored/x/model.fbx [-meshSubstring slg_face]
        public static void Dump()
        {
            string fbx = ArgAfter("-fbxPath");
            string substring = ArgAfter("-meshSubstring") ?? "slg_face";
            if (fbx == null) { Debug.LogError("FaceSdfUv.Dump: -fbxPath required"); EditorApplication.Exit(1); return; }

            string outDir = System.IO.Path.GetDirectoryName(fbx) + "/face_sdf";
            System.IO.Directory.CreateDirectory(outDir);
            int dumped = 0;
            foreach (var mesh in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>())
            {
                if (!mesh.name.Contains(substring)) continue;
                var sb = new System.Text.StringBuilder();
                foreach (var v in mesh.vertices)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:R} {1:R} {2:R}", v.x, v.y, v.z));
                System.IO.File.WriteAllText(outDir + "/" + mesh.name + ".pos.txt", sb.ToString());
                Debug.Log($"FaceSdfUv dumped {mesh.name}: {mesh.vertexCount} vertices");
                dumped++;
            }
            Debug.Log($"FaceSdfUv.Dump done: {dumped} mesh(es) -> {outDir}");
            EditorApplication.Exit(dumped > 0 ? 0 : 1);
        }

        // Swap a mesh for a copy carrying its transferred SDF coordinates in UV
        // channel 2. Returns the input untouched when no sidecar exists, so a
        // build can call this on every mesh it meets.
        public static Mesh WithBakedUv(Mesh mesh, string sidecarDir, string bakeDir)
        {
            if (mesh == null) return null;
            // A build may hand in a repaired copy of the FBX mesh; the sidecar is
            // named for the original, and a copy keeps its vertex order.
            string baseName = mesh.name.EndsWith("_reskin")
                ? mesh.name.Substring(0, mesh.name.Length - "_reskin".Length)
                : mesh.name;
            string sidecar = sidecarDir + "/" + baseName + ".uv2.txt";
            if (!System.IO.File.Exists(sidecar)) return mesh;

            string bakedPath = bakeDir + "/" + mesh.name + "_sdfuv.asset";
            var lines = System.IO.File.ReadAllLines(sidecar);
            var uv = new List<Vector2>(lines.Length);
            foreach (var line in lines)
            {
                if (line.Length == 0) continue;
                var parts = line.Split(' ');
                uv.Add(new Vector2(
                    float.Parse(parts[0], CultureInfo.InvariantCulture),
                    float.Parse(parts[1], CultureInfo.InvariantCulture)));
            }
            if (uv.Count != mesh.vertexCount)
            {
                Debug.LogError($"FaceSdfUv: {sidecar} has {uv.Count} rows for {mesh.vertexCount} "
                    + "vertices. The FBX changed after the dump: rerun Dump and the transfer.");
                return mesh;
            }
            var copy = Object.Instantiate(mesh);
            copy.name = mesh.name + "_sdfuv";
            copy.SetUVs(2, uv);
            AssetDatabase.CreateAsset(copy, bakedPath);
            Debug.Log($"FaceSdfUv: {mesh.name} -> {bakedPath} ({uv.Count} coordinates)");
            return copy;
        }

        private static string ArgAfter(string flag)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }
    }
}
