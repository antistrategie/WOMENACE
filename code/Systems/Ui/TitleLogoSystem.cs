using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using Il2CppMenace.UI.Menu;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Puts the WOMENACE logo on the main menu, and the credits' logos beside vanilla's.
//
// The vanilla logo is a UXML container named MenaceLogo on the title screen. Three stacked children
// (LogoBottomLayer, LogoMiddleLayer, LogoTopLayer) paint the artwork through inline background-images
// that resolve through Resources, and a LogoDropshadow elsewhere on the screen repeats the middle layer
// faintly. The swap paints our logo on the container and hides those layers, keeping the container's
// size and position and its Version label.
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
//
// Vanilla's Logos layer stands Hooded Horse's horse over Overhype's name at the bottom right, in
// one column at 39% white. The credits go on down that column: Sunborn's logo under Overhype's,
// MICA Team's under Sunborn's, and ANTISTRATÉGIE's name at the bottom, each on the column's right
// edge, as vanilla's are.
// The horse and Overhype's name are raised to make the room, and the welcome panel over the column
// with them. The sizes are in the title layout's units, as vanilla's own are.
public sealed class TitleLogoSystem : JiangyuSystem
{
    private const string LogoElement = "MenaceLogo";
    private static readonly string[] VanillaLayers = ["LogoBottomLayer", "LogoMiddleLayer", "LogoTopLayer"];
    private const string VanillaDropshadow = "LogoDropshadow";
    private const string LogoTexture = "womenace_logo";

    private Texture2D _logo;
    private bool _warnedMissingTexture;

    private const string LogosElement = "Logos";
    private const string HorseElement = "HoodedHorseLogo", OverhypeElement = "OverhypeLogo";
    private static readonly string[] WelcomePanels = ["WelcomePanel", "WelcomePanelMS"];
    // Vanilla's column: how far it stands from the right, and Overhype's height.
    private const float ColumnRight = 10f, OverhypeHeight = 42f;
    // The room between the column's rows, and between the horse and Overhype's name as vanilla
    // has it.
    private const float RowGap = 12f, HorseGap = 8f;
    // From the bottom up: the name, MICA Team, then Sunborn.
    private const float NameHeight = 14f, NameBottom = 20f;
    private const float MicaHeight = 42f, SunbornHeight = 54f;
    // The welcome panel's bottom, over the column's top.
    private const float WelcomeBottom = 270f;
    private static readonly Color Tint = new(1f, 1f, 1f, 0.39f);
    private readonly Dictionary<string, Texture2D> _credits = new();

    public override void OnInit()
    {
        // Each() scopes the injection to the logo element itself, so a title screen that has not
        // built it yet simply does not match and the next apply picks it up. The injected element
        // is an inert marker: the swap is the restyle of the scope, which is the vanilla element
        // whose size, position and scale mode we want to keep.
        UI.InjectEach(
            UiTarget.Screen<TitleUIScreen>().Each(UiSelector.Name(LogoElement)),
            SwapLogo);
        UI.InjectEach(
            UiTarget.Screen<TitleUIScreen>().Each(UiSelector.Name(LogosElement)),
            AddCredits);
    }

    private VisualElement AddCredits(VisualElement slot)
    {
        var credits = new VisualElement { name = "wm-title-credits", pickingMode = PickingMode.Ignore };
        credits.style.position = Position.Absolute;
        credits.style.left = credits.style.right = credits.style.top = credits.style.bottom = 0f;
        try
        {
            var (sunborn, mica, name) = (Credit("sunborn"), Credit("mica_team"), Credit("antistrategie"));
            if (sunborn == null || mica == null || name == null)
                return credits;
            credits.Add(CreditImage(name, ColumnRight, NameBottom, NameHeight));
            var micaBottom = NameBottom + NameHeight + RowGap;
            credits.Add(CreditImage(mica, ColumnRight, micaBottom, MicaHeight));
            var sunbornBottom = micaBottom + MicaHeight + HorseGap;
            credits.Add(CreditImage(sunborn, ColumnRight, sunbornBottom, SunbornHeight));
            var overhypeBottom = sunbornBottom + SunbornHeight + RowGap;
            Raise(slot, OverhypeElement, overhypeBottom);
            Raise(slot, HorseElement, overhypeBottom + OverhypeHeight + HorseGap);
            var root = slot;
            while (root.parent != null)
                root = root.parent;
            foreach (var panel in WelcomePanels)
                Raise(root, panel, WelcomeBottom);
        }
        catch (Exception ex) { Context.Log.Warn($"title logo: credits failed: {ex.Message}"); }
        return credits;
    }

    private static void Raise(VisualElement within, string element, float bottom)
    {
        var found = UI.Find(within, UiSelector.Name(element));
        if (found != null)
            found.style.bottom = bottom;
    }

    private static VisualElement CreditImage(Texture2D texture, float right, float bottom, float height)
    {
        var image = new VisualElement { pickingMode = PickingMode.Ignore };
        image.style.position = Position.Absolute;
        image.style.right = right;
        image.style.bottom = bottom;
        image.style.height = height;
        image.style.width = height * texture.width / texture.height;
        image.style.backgroundImage = new StyleBackground(texture);
        image.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Contain));
        image.style.unityBackgroundImageTintColor = Tint;
        return image;
    }

    private Texture2D Credit(string name)
    {
        if (_credits.TryGetValue(name, out var known))
            return known;
        var texture = Context.Assets.Load<Texture2D>(name);
        // Kept missing too: the title screen reopens on every return to the main menu.
        _credits[name] = texture;
        if (texture == null)
            Context.Log.Warn($"title logo: '{name}' missing from the bundle");
        return texture;
    }

    private VisualElement SwapLogo(VisualElement slot)
    {
        var marker = new VisualElement { name = "wm-title-logo", pickingMode = PickingMode.Ignore };
        try
        {
            var texture = Logo();
            if (texture == null)
                return marker;
            slot.style.backgroundImage = new StyleBackground(texture);
            slot.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Contain));
            foreach (var layer in VanillaLayers)
                UI.Find(slot, UiSelector.Name(layer))?.SetVisible(false);
            var root = slot;
            while (root.parent != null)
                root = root.parent;
            UI.Find(root, UiSelector.Name(VanillaDropshadow))?.SetVisible(false);
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
