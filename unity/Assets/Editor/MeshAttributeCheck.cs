using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Reports the vertex attributes a source model actually carries, per mesh.
//
// The doll shaders read more channels than a mesh necessarily has, and a missing
// one does not fail: it reads as zero and shades wrong. Tangents are the sharp
// case. DollToon builds its tangent frame from TANGENT to apply the normal map, and
// a mesh with no tangents feeds it a zero vector, which makes every normal it
// derives garbage and now writes that garbage into the normal buffer too. A glTF
// exporter that omits tangents is common, so this is worth checking before a bake
// rather than diagnosing from the screen afterwards.
//
//   Unity -batchmode -quit -projectPath unity \
//     -executeMethod MeshAttributeCheck.Run -assetPath Assets/Authored/.../raw.glb
public static class MeshAttributeCheck
{
    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        string path = null;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-assetPath") path = args[i + 1];

        if (string.IsNullOrEmpty(path))
        {
            Debug.Log("MESHATTR needs -assetPath <Assets/...>");
            EditorApplication.Exit(2);
            return;
        }

        AssetDatabase.ImportAsset(path,
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
        if (meshes.Length == 0)
        {
            // A ScriptedImporter can keep its meshes off the top level, so fall back
            // to walking the imported prefab's renderers.
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root != null)
            {
                meshes = root.GetComponentsInChildren<Renderer>(true)
                    .Select(r => r is SkinnedMeshRenderer smr
                        ? smr.sharedMesh
                        : r.GetComponent<MeshFilter>()?.sharedMesh)
                    .Where(m => m != null)
                    .Distinct()
                    .ToArray();
            }
        }

        if (meshes.Length == 0)
        {
            Debug.Log("MESHATTR no meshes found at " + path);
            EditorApplication.Exit(1);
            return;
        }

        foreach (var mesh in meshes)
        {
            var tangents = mesh.tangents;
            // Present but all-zero is the same defect as absent, and is what an
            // importer that allocated the channel without filling it leaves behind.
            var degenerate = tangents.Length == 0
                || tangents.All(t => new Vector3(t.x, t.y, t.z).sqrMagnitude < 1e-8f);

            var uvs = string.Join(",", Enumerable.Range(0, 8)
                .Where(c => mesh.HasVertexAttribute(
                    (UnityEngine.Rendering.VertexAttribute)((int)UnityEngine.Rendering.VertexAttribute.TexCoord0 + c)))
                .Select(c => "uv" + c));

            Debug.Log($"MESHATTR '{mesh.name}' verts={mesh.vertexCount} subMeshes={mesh.subMeshCount}"
                      + $" normals={(mesh.normals.Length > 0 ? "yes" : "NO")}"
                      + $" tangents={(degenerate ? "MISSING/ZERO" : "yes")}"
                      + $" colours={(mesh.colors.Length > 0 ? "yes" : "no")}"
                      + $" [{uvs}]");
        }

        EditorApplication.Exit(0);
    }
}
