using UnityEngine;

namespace Womenace.EditorTools
{
    public static class TransformSearch
    {
        // The first transform of the name at or under node, depth first, inactive ones included.
        public static Transform FindDeep(Transform node, string name)
        {
            if (node.name == name)
                return node;
            for (int i = 0; i < node.childCount; i++)
            {
                var hit = FindDeep(node.GetChild(i), name);
                if (hit != null)
                    return hit;
            }
            return null;
        }
    }
}
