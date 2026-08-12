using UnityEditor;
using UnityEngine;

// Reports the frame each skinned renderer actually renders in.
//
// The face SDF's lookup coordinates are baked from the source mesh's rest
// positions, so the sweep is only correct if the object space the shader sees has
// the same axes as that mesh. A skinned renderer's object matrix is its own
// transform, and the bake retargets onto a reference rig and inserts a root, so a
// rotation anywhere along that chain silently rotates the frame the light
// direction is resolved into. That fails as a wrongly-placed shadow rather than as
// an error, which is why it is worth printing rather than assuming.
public static class PrefabFrameCheck
{
    public static void Run()
    {
        const string path = "Assets/Prefabs/makiatto/default/main.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.Log("FRAME missing " + path);
            EditorApplication.Exit(2);
            return;
        }

        Debug.Log($"FRAME prefab root '{prefab.name}' localRot={prefab.transform.localRotation.eulerAngles} "
                  + $"localScale={prefab.transform.localScale}");

        foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var t = smr.transform;
            // Accumulated rotation from the prefab root down to this renderer.
            var acc = Quaternion.identity;
            for (var w = t; w != null && w != prefab.transform.parent; w = w.parent)
                acc = w.localRotation * acc;

            var root = smr.rootBone;
            Debug.Log($"FRAME smr '{smr.name}' path='{GetPath(prefab.transform, t)}' "
                      + $"selfRot={t.localRotation.eulerAngles} accumRot={acc.eulerAngles} "
                      + $"rootBone='{(root != null ? root.name : "<none>")}'"
                      + (root != null ? $" rootBoneRot={root.localRotation.eulerAngles}" : ""));

            // Where do the mesh's own axes point once the renderer's frame is applied?
            Debug.Log($"FRAME   axes under accumRot: +X -> {acc * Vector3.right}, "
                      + $"+Z -> {acc * Vector3.forward}");
        }
        EditorApplication.Exit(0);
    }

    private static string GetPath(Transform root, Transform t)
    {
        var s = t.name;
        for (var w = t.parent; w != null && w != root.parent; w = w.parent) s = w.name + "/" + s;
        return s;
    }
}
