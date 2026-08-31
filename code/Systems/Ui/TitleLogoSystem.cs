using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using Il2CppMenace.UI.Menu;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Puts the WOMENACE logo on the main menu.
//
// The vanilla logo is a UXML element named MenaceLogo in the title screen's ButtonColumn, carrying
// an inline background-image that resolves through Resources. A style.backgroundImage write from
// code overwrites that inline property, so the swap is a direct assignment on the live element
// rather than a rewrite of the texture behind it.
//
// Scoped through the injection system rather than a Harmony patch. UiTarget.Screen<TitleUIScreen>()
// resolves against the live tree on every apply, and the SDK re-applies on screen activation and on
// the screen root's GeometryChangedEvent, which is both narrower and more robust than hooking
// UIManager.OpenScreen: that fires for every screen in the game and still misses a title screen
// whose content lays out after activation.
//
// Deliberately not an assets/replacements/ texture. A replacement mutates the shared Texture2D by
// sweeping every loaded texture and GPU-copying into the one that matches by name, which is the
// mechanism for a consumer the mod cannot reach: a material buried in a prefab, an icon the engine
// resolves itself. A named element in a UXML the mod can already find is not that case.
public sealed class TitleLogoSystem : JiangyuSystem
{
    private const string LogoElement = "MenaceLogo";
    private const string LogoTexture = "womenace_logo";

    private Texture2D _logo;
    private bool _warnedMissingTexture;

    public override void OnInit()
    {
        // Each() scopes the injection to the logo element itself, so a title screen that has not
        // built it yet simply does not match and the next apply picks it up. The injected element
        // is an inert marker: the swap is the restyle of the scope, which is the vanilla element
        // whose size, position and scale mode we want to keep.
        UI.InjectEach(
            UiTarget.Screen<TitleUIScreen>().Each(UiSelector.Name(LogoElement)),
            SwapLogo);
    }

    private VisualElement SwapLogo(VisualElement slot)
    {
        var marker = new VisualElement { name = "wm-title-logo", pickingMode = PickingMode.Ignore };
        try
        {
            var texture = Logo();
            if (texture != null)
                slot.style.backgroundImage = new StyleBackground(texture);
        }
        catch (Exception ex) { Context.Log.Warn($"title logo: swap failed: {ex.Message}"); }
        return marker;
    }

    private Texture2D Logo()
    {
        if (_logo != null)
            return _logo;
        _logo = Context.Assets.Load<Texture2D>(LogoTexture);
        if (_logo == null && !_warnedMissingTexture)
        {
            // Latched: the title screen reopens on every return to the main menu.
            _warnedMissingTexture = true;
            Context.Log.Warn($"title logo: '{LogoTexture}' missing from the bundle");
        }
        return _logo;
    }
}
