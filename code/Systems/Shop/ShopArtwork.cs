using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

internal sealed class ShopArtwork(ModContext context)
{
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _missingUntil = new(StringComparer.Ordinal);

    public Texture2D Texture(string asset)
    {
        if (string.IsNullOrEmpty(asset))
            return null;
        if (_textures.TryGetValue(asset, out var texture) && texture != null)
            return texture;
        // Missing bundle scans are costly per tile. A short delay still allows late assets to recover.
        var wasMissing = _missingUntil.TryGetValue(asset, out var retryAt);
        if (wasMissing && Time.realtimeSinceStartup < retryAt)
            return null;
        _textures[asset] = texture = context.Assets.Load<Texture2D>(asset);
        if (texture == null)
        {
            _missingUntil[asset] = Time.realtimeSinceStartup + 1f;
            if (!wasMissing)
                context.Log.Warn($"Shop artwork '{asset}' is unavailable.");
        }
        return texture;
    }

    public VisualElement Create(string asset, string classes, VisualElement parent) => Create(Texture(asset), classes, parent);

    public VisualElement Create(Texture2D texture, string classes, VisualElement parent)
    {
        var image = ShopVisuals.Element(classes, parent, PickingMode.Ignore);
        if (texture != null)
            image.style.backgroundImage = new StyleBackground(texture);
        return image;
    }

    public bool HasTexture(string asset) => _textures.GetValueOrDefault(asset) != null;
}
