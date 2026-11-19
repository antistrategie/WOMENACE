using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// What every camera the player turns shares: the armoury's, mission preparation's, the planet's
// and O.C.I.'s. Each reads its own drag and wheel, and turns, zooms and settles by these.
public sealed partial class StrategyDiorama
{
    private const float OrbitDegreesPerPixel = 0.25f;
    private const float OrbitZoomPerNotch = 0.1f;
    // How fast a turned camera closes on where it was asked to be: the part of the way left
    // that it covers in a second, as an exponent.
    private const float OrbitSettle = 12f;

    // The distance's factor for a roll of the wheel, and a frame's part of the way to where
    // the camera was asked to be. Every camera the player turns shares these.
    private static float OrbitZoom(float notches) => MathF.Exp(-notches * OrbitZoomPerNotch * 2f);

    private static float OrbitSmoothing() => 1f - MathF.Exp(-OrbitSettle * Time.unscaledDeltaTime);

    // A wheel's turn in notches. The input system reports a notch as 120 on Windows and as 1
    // elsewhere (and through some layers between), so a small figure is already in notches.
    internal static float WheelNotches(float raw) => Mathf.Abs(raw) >= 20f ? raw / 120f : raw;

    // Whether a screen point lies over a drawn UI panel (anything with a background, or a scroll
    // view) rather than over the empty, see-through part of the screen.
    private static bool PointerOverUi(Vector2 screen)
    {
        foreach (var document in UnityEngine.Object.FindObjectsOfType<UnityEngine.UIElements.UIDocument>())
        {
            var root = document.rootVisualElement;
            var panel = root?.panel;
            if (panel == null || root.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None)
                continue;
            var point = UnityEngine.UIElements.RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            for (var element = panel.Pick(point); element != null && element != root; element = element.parent)
            {
                var style = element.resolvedStyle;
                if (style.backgroundColor.a > 0.05f || style.backgroundImage.texture != null
                    || style.backgroundImage.sprite != null || element.TryCast<UnityEngine.UIElements.ScrollView>() != null)
                    return true;
            }
        }
        return false;
    }
}
