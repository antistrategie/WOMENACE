using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace WOMENACE.Code;

// A room's solid surfaces as points, for the maps the cameras are kept clear of them by: the
// refitting room's (StrategyDiorama.Armory) and the ops room's round the holotable
// (StrategyDiorama.PlanetCamera).
public sealed partial class StrategyDiorama
{
    // Calls back with points spread across every face of every solid mesh under a root, in the
    // root's own space, close enough together that no cell of the given size a face crosses is
    // missed, and whether the face lies level. Returns how many meshes and faces were walked.
    private static (int Pieces, int Faces) WalkSolidFaces(Transform root, float cell, int mostSteps, Action<NVector3, bool> point)
    {
        var toRoot = root.worldToLocalMatrix;
        var meshes = new Dictionary<IntPtr, (Vector3[] Vertices, int[] Triangles)>();
        var (pieces, faces) = (0, 0);
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
        {
            var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            var material = renderer.sharedMaterial;
            // Holograms, light shafts, glows and decals are drawn in the air and on walls, and
            // stop nothing.
            if (!renderer.enabled || mesh == null || !mesh.isReadable || material == null || material.renderQueue > 2450)
                continue;
            if (!meshes.TryGetValue(mesh.Pointer, out var data))
                meshes[mesh.Pointer] = data = (mesh.vertices, mesh.triangles);
            var m = toRoot * renderer.transform.localToWorldMatrix;
            var (m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23) =
                (m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13, m.m20, m.m21, m.m22, m.m23);
            var placed = new NVector3[data.Vertices.Length];
            for (var i = 0; i < placed.Length; i++)
            {
                var v = data.Vertices[i];
                placed[i] = new NVector3(
                    m00 * v.x + m01 * v.y + m02 * v.z + m03,
                    m10 * v.x + m11 * v.y + m12 * v.z + m13,
                    m20 * v.x + m21 * v.y + m22 * v.z + m23);
            }
            pieces++;
            for (var t = 0; t + 2 < data.Triangles.Length; t += 3)
            {
                var (a, b, c) = (placed[data.Triangles[t]], placed[data.Triangles[t + 1]], placed[data.Triangles[t + 2]]);
                var normal = NVector3.Cross(b - a, c - a);
                var level = MathF.Abs(normal.Y) > 0.7f * normal.Length();
                var steps = Math.Clamp((int)MathF.Ceiling(MathF.Max((b - a).Length(), (c - a).Length()) / (cell * 0.6f)), 1, mostSteps);
                faces++;
                for (var i = 0; i <= steps; i++)
                    for (var j = 0; j <= steps - i; j++)
                        point(a + (b - a) * ((float)i / steps) + (c - a) * ((float)j / steps), level);
            }
        }
        return (pieces, faces);
    }
}
