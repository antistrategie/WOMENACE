using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WOMENACE.Editor
{
    // The inverted-hull outline for baked vehicle prefabs, as the dolls and the
    // mech carry it: the outlined slots drawn a second time under DollOutline.
    // Runs as a post-pass over the baked prefab rather than inside Jiangyu's
    // bake, because the contour is WOMENACE shading and the bake tool stays
    // free of model flavour.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity \
    //     -executeMethod WOMENACE.Editor.VehicleOutlineHulls.Add \
    //     -prefabPath Assets/Prefabs/koleda_car/default/main.prefab \
    //     -outlineSlots supercar_sub0,supercar_sub1,supercar_sub2,supercar_sub3
    //
    // Idempotent: hull renderers from an earlier run are removed first, and the
    // hull assets live beside the prefab under a purged name prefix.
    public static class VehicleOutlineHulls
    {
        public static void Add()
        {
            string prefabPath = ArgAfter("-prefabPath");
            var slots = (ArgAfter("-outlineSlots") ?? "").Split(',')
                .Select(s => s.Trim()).Where(s => s.Length > 0)
                .Select(s => "baked_" + s).ToHashSet();
            if (prefabPath == null || slots.Count == 0)
            {
                Debug.LogError("VehicleOutlineHulls: -prefabPath and -outlineSlots required");
                EditorApplication.Exit(1);
                return;
            }
            string dir = System.IO.Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            foreach (var stale in System.IO.Directory.GetFiles(dir)
                .Where(f => System.IO.Path.GetFileName(f).StartsWith("outline_hull_")))
                AssetDatabase.DeleteAsset(stale.Replace('\\', '/'));

            var outlineShader = Shader.Find("Womenace/DollOutline");
            if (outlineShader == null)
            {
                Debug.LogError("VehicleOutlineHulls: Womenace/DollOutline not found");
                EditorApplication.Exit(1);
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            int hulls = 0;
            try
            {
                foreach (var old in root.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.EndsWith("_outline")).ToList())
                    Object.DestroyImmediate(old.gameObject);

                foreach (var r in root.GetComponentsInChildren<Renderer>(true).ToList())
                {
                    var mesh = (r as SkinnedMeshRenderer)?.sharedMesh
                        ?? r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    var picks = Enumerable.Range(0, r.sharedMaterials.Length)
                        .Where(i => r.sharedMaterials[i] != null
                            && slots.Contains(r.sharedMaterials[i].name)
                            && i < mesh.subMeshCount)
                        .ToList();
                    if (picks.Count == 0) continue;

                    var indexSets = picks.Select(i => mesh.GetIndices(i)).ToList();
                    var hull = Object.Instantiate(mesh);
                    hull.name = "outline_hull_" + hulls;
                    hull.subMeshCount = picks.Count;
                    for (int k = 0; k < picks.Count; k++)
                        hull.SetIndices(indexSets[k], MeshTopology.Triangles, k);
                    AssetDatabase.CreateAsset(hull, dir + "/" + hull.name + ".asset");

                    var mats = new Material[picks.Count];
                    for (int k = 0; k < picks.Count; k++)
                    {
                        var src = r.sharedMaterials[picks[k]];
                        var m = new Material(outlineShader) { name = "outline_hull_" + src.name };
                        var albedo = src.GetTexture("_BaseMap") ?? src.GetTexture("_BaseColorMap");
                        if (albedo != null) m.SetTexture("_BaseMap", albedo);
                        AssetDatabase.CreateAsset(m, dir + "/" + m.name + ".mat");
                        mats[k] = m;
                    }

                    var go = Object.Instantiate(r.gameObject, r.transform.parent);
                    go.name = r.gameObject.name + "_outline";
                    for (int c = go.transform.childCount - 1; c >= 0; c--)
                        Object.DestroyImmediate(go.transform.GetChild(c).gameObject);
                    var cr = go.GetComponent<Renderer>();
                    if (cr is SkinnedMeshRenderer smr)
                    {
                        smr.sharedMesh = hull;
                        smr.updateWhenOffscreen = true;
                    }
                    else
                    {
                        go.GetComponent<MeshFilter>().sharedMesh = hull;
                    }
                    cr.sharedMaterials = mats;
                    hulls++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log($"VehicleOutlineHulls: {hulls} hull(s) on {prefabPath}");
            EditorApplication.Exit(0);
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
