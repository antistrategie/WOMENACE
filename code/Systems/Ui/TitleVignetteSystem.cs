using Il2CppMenace.UI.Menu;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Darkens the edges of the title screen's video, behind the menu. Vanilla plays the video under
// the whole title UI, so a picture standing behind the title layout's Root sits over the video and
// under everything on it. The vignette is drawn here, not in the videos: a smooth dark gradient is
// what video compression bands worst, and here its strength can change without encoding again.
//
// Its falloff is a lens's natural one, as ffmpeg's vignette filter draws it: the light at a point
// is cos(Angle * d)^4, where d is the point's distance from the middle over the half-diagonal.
public sealed class TitleVignetteSystem : JiangyuSystem
{
    private const string TitleRoot = "Root";
    private const float Angle = 1.0f;
    private const int Width = 512, Height = 288;

    private Texture2D _vignette;

    public override void OnInit()
    {
        UI.Inject(UiTarget.Screen<TitleUIScreen>().Before(UiSelector.Name(TitleRoot)), Build);
    }

    private VisualElement Build()
    {
        var element = new VisualElement { name = "wm-title-vignette", pickingMode = PickingMode.Ignore };
        element.style.position = Position.Absolute;
        element.style.left = 0;
        element.style.top = 0;
        element.style.right = 0;
        element.style.bottom = 0;
        element.style.backgroundImage = new StyleBackground(Vignette());
        element.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(Length.Percent(100), Length.Percent(100)));
        return element;
    }

    private Texture2D Vignette()
    {
        if (_vignette != null)
            return _vignette;
        var pixels = new Color32[Width * Height];
        var (cx, cy) = ((Width - 1) * 0.5f, (Height - 1) * 0.5f);
        var halfDiagonal = MathF.Sqrt(cx * cx + cy * cy);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / halfDiagonal;
                var c = MathF.Cos(Angle * d);
                var light = c * c * c * c;
                // A step of noise under one level, so the gradient does not band over a dark scene.
                var noise = (((x * 73856093) ^ (y * 19349663)) & 255) / 255f - 0.5f;
                var alpha = (byte)Math.Clamp((int)MathF.Round((1f - light) * 255f + noise), 0, 255);
                pixels[y * Width + x] = new Color32(0, 0, 0, alpha);
            }
        }
        _vignette = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = "WOMENACE_TitleVignette", wrapMode = TextureWrapMode.Clamp };
        _vignette.SetPixels32(pixels);
        _vignette.Apply(false, true);
        return _vignette;
    }
}
