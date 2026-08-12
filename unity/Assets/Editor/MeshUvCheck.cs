using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Reports which UV channels survived import into a baked prefab's mesh.
//
// The face SDF is sampled with a reconstructed planar projection baked into the
// source glTF's TEXCOORD_2. A glTF importer is free to drop UV sets it does not
// recognise, and a dropped set reads as zeros rather than as an error, so the
// face would shade from one corner texel of the map and look merely wrong. This
// makes that visible.
public static class MeshUvCheck
{
    public static void Run()
    {
        const string path = "Assets/Prefabs/makiatto/default/main.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.Log("UVCHECK missing " + path);
            EditorApplication.Exit(2);
            return;
        }

        var uvs = new List<Vector2>();
        var seen = new HashSet<Mesh>();
        foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null || !seen.Add(mesh)) continue;
            var b = mesh.bounds;
            Debug.Log($"UVCHECK bounds '{mesh.name}' "
                + $"X [{b.min.x:F4},{b.max.x:F4}] "
                + $"Y [{b.min.y:F4},{b.max.y:F4}] "
                + $"Z [{b.min.z:F4},{b.max.z:F4}]");
            var line = $"UVCHECK mesh '{mesh.name}' verts={mesh.vertexCount}";
            for (int channel = 0; channel < 4; channel++)
            {
                mesh.GetUVs(channel, uvs);
                if (uvs.Count == 0) { line += $" | uv{channel}: absent"; continue; }
                Vector2 lo = uvs[0], hi = uvs[0];
                foreach (var uv in uvs)
                {
                    lo = Vector2.Min(lo, uv);
                    hi = Vector2.Max(hi, uv);
                }
                line += $" | uv{channel}: [{lo.x:F3},{hi.x:F3}]x[{lo.y:F3},{hi.y:F3}]";
            }
            Debug.Log(line);
        }
        EditorApplication.Exit(0);
    }
}
