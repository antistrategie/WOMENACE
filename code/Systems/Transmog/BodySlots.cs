using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace WOMENACE.Code;

// Joins a Doll body's submeshes that share a material, once per body prefab, before it is
// first placed.
//
// The PMX conversion keeps one submesh per source part, so a body carries 25 to 60 material
// slots on every LOD while using only 11 to 15 materials: one outline material on up to 24
// slots, one face material on 7 or 8. Every slot is its own draw for each of the shader's
// passes, so a squad of Dolls costs several times the draws its looks need. Joining the slots
// of one material into one submesh draws the same triangles with the same material in one go.
//
// Opaque materials only. Depth testing makes their order within one renderer irrelevant, and
// the face draws after the hair's stencil by its render queue, not its slot. Transparent slots
// keep their own submesh, in their order. Each joined material takes the place of its first
// slot, so the order of different materials is kept as well.
internal static class BodySlots
{
    // A render queue from here up is drawn back to front, so its slots keep their order.
    private const int TransparentQueue = 2500;

    private static readonly HashSet<IntPtr> Joined = new();

    internal static void Join(GameObject prefab, Action<string> warn)
    {
        if (prefab == null || !Joined.Add(prefab.Pointer))
            return;
        foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            try { JoinRenderer(renderer); }
            catch (Exception ex) { warn($"body slots: '{renderer.name}' left as baked: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private static void JoinRenderer(SkinnedMeshRenderer renderer)
    {
        var mesh = renderer.sharedMesh;
        Material[] materials = renderer.sharedMaterials;
        if (mesh == null || !mesh.isReadable || mesh.subMeshCount != materials.Length)
            return;

        // Each output slot, with the source submeshes it draws.
        var slots = new List<(Material Material, List<int> Sources)>();
        var byMaterial = new Dictionary<IntPtr, int>();
        for (var i = 0; i < materials.Length; i++)
        {
            var material = materials[i];
            if (mesh.GetTopology(i) != MeshTopology.Triangles)
                return;
            var shared = material != null && material.renderQueue < TransparentQueue;
            if (shared && byMaterial.TryGetValue(material.Pointer, out var slot))
            {
                slots[slot].Sources.Add(i);
                continue;
            }
            if (shared)
                byMaterial[material.Pointer] = slots.Count;
            slots.Add((material, new List<int> { i }));
        }
        if (slots.Count == materials.Length)
            return;

        // Read every submesh before the count changes, with the base vertex applied, so each
        // joined submesh indexes the vertex buffer from zero.
        var indices = new int[materials.Length][];
        for (var i = 0; i < materials.Length; i++)
        {
            var source = mesh.GetIndices(i, true);
            var copy = new int[source.Length];
            for (var k = 0; k < copy.Length; k++)
                copy[k] = source[k];
            indices[i] = copy;
        }

        var joined = new Material[slots.Count];
        mesh.subMeshCount = slots.Count;
        for (var s = 0; s < slots.Count; s++)
        {
            var sources = slots[s].Sources;
            var total = 0;
            foreach (var i in sources)
                total += indices[i].Length;
            var merged = new Il2CppStructArray<int>(total);
            var at = 0;
            foreach (var i in sources)
                foreach (var index in indices[i])
                    merged[at++] = index;
            mesh.SetIndices(merged, MeshTopology.Triangles, s, false, 0);
            joined[s] = slots[s].Material;
        }
        renderer.sharedMaterials = joined;
    }
}
