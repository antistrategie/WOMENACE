using UnityEngine;

namespace WOMENACE.Code;

// Text the dev verbs share, so two verbs that report the same thing report it in one form.
internal static class DevText
{
    // A transform's path of names from below an ancestor, or from its scene root without one.
    internal static string PathOf(Transform t, Transform under = null)
    {
        if (t == under)
            return ".";
        var path = t.name;
        for (var up = t.parent; up != null && up != under; up = up.parent)
            path = up.name + "/" + path;
        return path;
    }

    // One renderer as a line: whether it shows, its bounds in a space, its material and where it
    // is drawn in the order.
    internal static string Describe(Renderer r, Transform space)
    {
        var material = r.sharedMaterial;
        return $"{r.name} {r.GetIl2CppType().Name} enabled {r.enabled} active {r.gameObject.activeInHierarchy} visible {r.isVisible} centre {space.InverseTransformPoint(r.bounds.center)} size {r.bounds.size} mat {material?.name} shader {material?.shader?.name} queue {material?.renderQueue} order {r.sortingOrder} sorting layer {r.sortingLayerID} priority {r.rendererPriority} layer {r.gameObject.layer}";
    }
}
