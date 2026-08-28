using UnityEngine;

namespace WOMENACE.Code;

// Deep name lookup under a scene object, inactive children included and the
// root itself eligible. One definition: four systems had grown identical
// private copies of it.
internal static class SceneQuery
{
    internal static Transform FindNamed(GameObject root, string name)
        => root == null ? null : FindNamed(root.transform, name);

    internal static Transform FindNamed(Transform root, string name)
    {
        if (root == null)
            return null;
        foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
            if (t.name == name)
                return t;
        return null;
    }
}
