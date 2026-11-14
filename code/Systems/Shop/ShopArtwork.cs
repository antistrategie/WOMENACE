using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

internal sealed class ShopArtwork(ModContext context)
{
    // Kept for the session, by asset: the bundles keep what they have loaded, and an asset that
    // is only a texture costs a search of every bundle for the sprite it has not got.
    private static readonly Dictionary<string, (Sprite Sprite, Texture2D Texture)> Pictures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _missingUntil = new(StringComparer.Ordinal);

    // Draws an asset's picture as an element's background. A sprite is drawn as the sprite,
    // whose texture may be larger than it: one whose size does not divide by four is padded
    // to be compressed. An asset that is only a texture is drawn whole.
    public void Draw(VisualElement image, string asset)
    {
        var (sprite, texture) = Picture(asset);
        if (sprite != null)
            image.style.backgroundImage = new StyleBackground(sprite);
        else if (texture != null)
            image.style.backgroundImage = new StyleBackground(texture);
    }

    private static bool Found((Sprite Sprite, Texture2D Texture) picture) => picture.Sprite != null || picture.Texture != null;

    private (Sprite Sprite, Texture2D Texture) Picture(string asset)
    {
        if (string.IsNullOrEmpty(asset))
            return default;
        if (Pictures.TryGetValue(asset, out var picture) && Found(picture))
            return picture;
        // Missing bundle scans are costly per tile. A short delay still allows late assets to recover.
        var wasMissing = _missingUntil.TryGetValue(asset, out var retryAt);
        if (wasMissing && Time.realtimeSinceStartup < retryAt)
            return default;
        var sprite = context.Assets.Load<Sprite>(asset);
        picture = (sprite, sprite != null ? null : context.Assets.Load<Texture2D>(asset));
        Pictures[asset] = picture;
        if (!Found(picture))
        {
            _missingUntil[asset] = Time.realtimeSinceStartup + 1f;
            if (!wasMissing)
                context.Log.Warn($"Shop artwork '{asset}' is unavailable.");
        }
        return picture;
    }

    public VisualElement Create(string asset, string classes, VisualElement parent)
    {
        var image = ShopVisuals.Element(classes, parent, PickingMode.Ignore);
        Draw(image, asset);
        return image;
    }

    public VisualElement Create(Texture2D texture, string classes, VisualElement parent)
    {
        var image = ShopVisuals.Element(classes, parent, PickingMode.Ignore);
        if (texture != null)
            image.style.backgroundImage = new StyleBackground(texture);
        return image;
    }

    public bool HasPicture(string asset) => Pictures.TryGetValue(asset, out var picture) && Found(picture);
}
