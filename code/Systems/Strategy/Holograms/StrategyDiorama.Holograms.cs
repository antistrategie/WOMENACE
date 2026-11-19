using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The holograms every room draws with: the holotable's quad and shader, its panels drawn into
// textures, item pictures on cards, and copies of the hologram material for whole models.
public sealed partial class StrategyDiorama
{
    // The part of a sheet one of its cells is, as _UvRect takes it: cells are counted from the
    // top left, across and then down.
    private static Vector4 SheetCell(int index, int columns, int rows) =>
        new(1f / columns, 1f / rows, index % columns / (float)columns, 1f - (index / columns + 1f) / rows);

    // A hologram quad in the room: the holotable's own quad and shader, showing a texture.
    private GameObject Quad(GameObject template, string name, Texture texture)
    {
        var quad = UnityEngine.Object.Instantiate(template, _room.transform, false);
        quad.name = name;
        quad.SetActive(true);
        var material = quad.GetComponent<MeshRenderer>()?.material;
        if (material != null)
        {
            material.SetTexture("_MainTex", texture);
            material.SetFloat("_Backdrop", 0f);
            // Depth tested: a doll in front of a card hides it.
            material.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
        }
        return quad;
    }

    // A panel of its own that draws into a texture, for what is shown on a quad in the room: it
    // takes the theme and text settings of the panel an element is in, and draws on nothing at
    // a fixed scale.
    private static bool TexturePanel(string name, VisualElement like, int width, int height, float scale,
        out GameObject host, out PanelSettings settings, out RenderTexture texture, out VisualElement root)
    {
        (host, settings, texture, root) = (null, null, null, null);
        UIDocument source = null;
        if (like?.panel != null)
            foreach (var document in UnityEngine.Object.FindObjectsOfType<UIDocument>())
                if (document.rootVisualElement?.panel == like.panel)
                    source = document;
        if (source?.panelSettings == null)
            return false;
        texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = name };
        settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.themeStyleSheet = source.panelSettings.themeStyleSheet;
        settings.textSettings = source.panelSettings.textSettings;
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = scale;
        settings.clearColor = true;
        settings.colorClearValue = new Color(0f, 0f, 0f, 0f);
        settings.targetTexture = texture;
        host = new GameObject(name);
        var panel = host.AddComponent<UIDocument>();
        panel.panelSettings = settings;
        root = panel.rootVisualElement;
        return root != null;
    }

    // A hologram quad that shows its texture as it is: no tint, scan lines, plate or faded edge.
    private static void PlainHologram(GameObject quad, float intensity)
    {
        var material = quad.GetComponent<MeshRenderer>().material;
        material.SetColor("_Tint", Color.white);
        material.SetFloat(IntensityId, intensity);
        material.SetFloat("_ScanDepth", 0f);
        material.SetFloat("_Plate", 0f);
        material.SetFloat("_EdgeFade", 0f);
    }

    // A hologram's material with nothing of the screen's about it: no scan lines, no faded
    // edge, the whole texture, all of it shown.
    private static void PlainHologram(Material material, float plate, float intensity, Color tint)
    {
        material.SetFloat("_ScanDepth", 0f);
        material.SetFloat("_EdgeFade", 0f);
        material.SetFloat("_Reveal", 1f);
        material.SetFloat("_Plate", plate);
        material.SetFloat("_Intensity", intensity);
        material.SetColor("_Tint", tint);
        material.SetVector("_UvRect", new Vector4(1f, 1f, 0f, 0f));
    }

    // A bare card in a room for an item's picture, as large as the picture's own shape allows
    // within a size.
    private static GameObject IconCard(Transform room, Sprite picture, float size)
    {
        var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var collider = card.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.Destroy(collider);
        card.transform.SetParent(room, false);
        var shape = picture.rect;
        var widest = Mathf.Max(shape.width, shape.height);
        card.transform.localScale = new Vector3(size * shape.width / widest, size * shape.height / widest, 1f);
        return card;
    }

    // The hologram of an item's picture: its own part of the sheet it is drawn on, cut by its
    // alpha.
    private Material IconHologram(Sprite picture)
    {
        var material = new Material(HologramMaterial());
        material.SetTexture(BaseMapId, picture.texture);
        var sheet = picture.textureRect;
        material.SetTextureScale(BaseMapId, new Vector2(sheet.width / picture.texture.width, sheet.height / picture.texture.height));
        material.SetTextureOffset(BaseMapId, new Vector2(sheet.x / picture.texture.width, sheet.y / picture.texture.height));
        material.SetFloat("_AlphaClip", 1f);
        return material;
    }

    // The hologram's material, which whatever is shown as one wears a copy of.
    private Material HologramMaterial()
    {
        if (_hologram != null)
            return _hologram;
        _hologram = Context.Assets.Load<GameObject>(HologramAsset)?.GetComponent<MeshRenderer>()?.sharedMaterial;
        if (_hologram == null)
            return null;
        // Set on the loaded material, over whatever it was saved with, and so on every copy
        // of it: finer lines with narrower gaps, a quieter glow along an outline and some of
        // the thing's own colours, so that a face and what is worn can be read.
        _hologram.SetFloat("_ScanRows", HologramRows);
        _hologram.SetFloat("_ScanGap", HologramLineGap);
        _hologram.SetFloat("_RimBoost", HologramRim);
        _hologram.SetFloat("_OwnColour", HologramOwnColour);
        return _hologram;
    }

    // Where a material may keep its picture, whichever shader it is drawn with.
    private static readonly string[] HologramSourceMaps = { "_BaseMap", "_BaseColorMap", "_MainTex" };

    // A copy of the hologram carrying a material's own picture, for its light and dark.
    private Material HologramOf(Material source)
    {
        var worn = new Material(HologramMaterial());
        foreach (var map in HologramSourceMaps)
            if (source != null && source.HasProperty(map) && source.GetTexture(map) != null)
            {
                worn.SetTexture(BaseMapId, source.GetTexture(map));
                break;
            }
        return worn;
    }

    // A renderer's materials, each swapped for a hologram copy with its own picture's light and
    // dark. Each copy is set up as the caller asks, and kept in a list its owner destroys.
    private void WearHolograms(Renderer renderer, List<Material> into, Action<Material, Material> setUp = null)
    {
        Material[] own = renderer.sharedMaterials;
        var worn = new Material[own.Length];
        for (var i = 0; i < own.Length; i++)
        {
            worn[i] = HologramOf(own[i]);
            setUp?.Invoke(worn[i], own[i]);
            into.Add(worn[i]);
        }
        renderer.sharedMaterials = worn;
    }
}
