using System.Globalization;
using System.Text;
using Il2CppInterop.Runtime;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Dev verbs that say what is held in memory, for finding what a boot or a screen loads.
[DevVerb]
public static class Memory
{
    // Writes every texture, mesh and audio clip that is loaded to a tab-separated file: its
    // kind, name, size as Unity reports it, and for a texture its dimensions, format and mips.
    public static object Census(string path)
    {
        var lines = new StringBuilder();
        var totals = new Dictionary<string, (int Count, long Bytes)>();
        foreach (var (kind, type) in new[]
        {
            ("Texture2D", Il2CppType.Of<Texture2D>()),
            ("RenderTexture", Il2CppType.Of<RenderTexture>()),
            ("Cubemap", Il2CppType.Of<Cubemap>()),
            ("Texture3D", Il2CppType.Of<Texture3D>()),
            ("Texture2DArray", Il2CppType.Of<Texture2DArray>()),
            ("Mesh", Il2CppType.Of<Mesh>()),
            ("AudioClip", Il2CppType.Of<AudioClip>()),
        })
        {
            foreach (var found in Resources.FindObjectsOfTypeAll(type))
            {
                var bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(found);
                var detail = "";
                if (found.TryCast<Texture>() is { } texture)
                    detail = string.Create(CultureInfo.InvariantCulture, $"{texture.width}\t{texture.height}\t{texture.graphicsFormat}\t{texture.mipmapCount}");
                lines.Append(kind).Append('\t').Append(found.name).Append('\t').Append(bytes).Append('\t').Append(detail).Append('\n');
                var (count, sum) = totals.TryGetValue(kind, out var had) ? had : (0, 0L);
                totals[kind] = (count + 1, sum + bytes);
            }
        }
        File.WriteAllText(path, lines.ToString());
        return new { path, totals = string.Join(", ", totals.Select(t => $"{t.Key} {t.Value.Count} {t.Value.Bytes / 1048576} MB")) };
    }
}
