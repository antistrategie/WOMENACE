using UnityEngine;
using Object = UnityEngine.Object;

namespace WOMENACE.Code;

// What a feature makes at run time that Unity does not free with the objects using it: a
// renderer's own copy of its material, a mesh, a texture. Each is registered as it is made and
// destroyed with the rest by Clear().
internal sealed class Owned
{
    private readonly List<Object> _made = new();

    // The renderer's own copy of its material, made on first asking.
    public Material Material(Renderer renderer)
    {
        var material = renderer != null ? renderer.material : null;
        return Add(material);
    }

    public T Add<T>(T made) where T : Object
    {
        if (made != null)
            _made.Add(made);
        return made;
    }

    public void Clear()
    {
        foreach (var made in _made)
        {
            if (made == null)
                continue;
            made.TryCast<RenderTexture>()?.Release();
            Object.Destroy(made);
        }
        _made.Clear();
    }
}
