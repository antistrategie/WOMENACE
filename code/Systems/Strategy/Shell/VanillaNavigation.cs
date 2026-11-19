using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Game.Audio;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Going to the strategy layer's screens through the vanilla tab bar. Every destination is the bar's
// own button for that screen (StrategyNavigation, by NavigationButtonType), clicked as the player
// would click it, so each screen opens exactly as it does in vanilla. Kalina's shop opens through
// the shop's own entry point.
internal static partial class VanillaNavigation
{
    internal enum Target { Bridge, Armory, Hiring, Oci, Market, Workshop, Shop }

    // MENACE's UI face for an element of the mod's that does not inherit the screens' font. It is
    // Jura Bold, which the mod ships (UI/fonts) and wui.uss gives this class, so nothing here holds
    // a font of the game's.
    internal const string GameFontClass = "wm-game-font";

    internal static void ApplyGameFont(VisualElement root) => root.AddToClassList(GameFontClass);

    // The tab bar on the screen that is open now.
    private static StrategyNavigation FindNavigation()
    {
        // The screen's live document root. GetRootElement() caches its root, and a screen that
        // is re-enabled keeps the detached previous one there (the armoury does), so the bar is
        // looked up from the document the screen renders now (m_Container), as the SDK does.
        var screen = UIManager.Get()?.GetActiveScreen();
        var documentRoot = screen?.m_Container?.rootVisualElement;
        if (documentRoot == null)
            return null;
        // By type: the bar is named "StrategyNavigation" on some screens and unnamed on others
        // (the armoury).
        return UI.Find(documentRoot, UiSelector.Type<StrategyNavigation>())?.TryCast<StrategyNavigation>();
    }

    // The vanilla button that opens a target: the bridge goes to mission select during an
    // operation and to the system map outside one, whichever the tab bar is showing.
    private static NavigationButton ButtonFor(Target target, StrategyNavigation navigation)
    {
        if (navigation == null)
            return null;
        NavigationButton Shown(NavigationButtonType type)
        {
            var b = navigation.GetButton(type);
            return b != null && b.resolvedStyle.display != DisplayStyle.None && b.visible ? b : null;
        }
        return target switch
        {
            Target.Bridge => Shown(NavigationButtonType.MissionSelect) ?? Shown(NavigationButtonType.SystemMap),
            Target.Armory => Shown(NavigationButtonType.Armory),
            Target.Hiring => Shown(NavigationButtonType.Hiring),
            Target.Oci => Shown(NavigationButtonType.OCI),
            Target.Market => Shown(NavigationButtonType.BlackMarket),
            Target.Workshop => Shown(NavigationButtonType.Workshop),
            _ => null,
        };
    }

    // Whether the campaign has a destination open, by vanilla's own rules for its tab bar: the
    // workshop and the shop wait for the workshop's unlock, and the campaign's settings can
    // grey out hiring, the black market and the O.C.I. A greyed-out vanilla button is still on
    // the bar but does nothing, and some screens' bars carry the workshop's while it is locked,
    // so a bar's buttons say only how to get somewhere, never whether it is open.
    private static bool Unlocked(Target target)
    {
        var config = Il2CppMenace.Strategy.StrategyConfig.Current;
        return target switch
        {
            Target.Workshop or Target.Shop => WorkshopAccess.IsUnlocked,
            Target.Hiring => config == null || !config.GrayOutHiring,
            Target.Market => config == null || !config.GrayOutBlackMarket,
            Target.Oci => config == null || !config.GrayOutOCI,
            _ => true,
        };
    }

    private static bool IsAvailable(Target target, StrategyNavigation navigation) =>
        Unlocked(target) && (target == Target.Shop || ButtonFor(target, navigation) != null);

    internal static void Go(Target target)
    {
        try
        {
            var navigation = FindNavigation();
            if (target == Target.Shop)
            {
                ShopSystem.OpenFromNavigation();
                return;
            }
            var button = ButtonFor(target, navigation);
            if (button == null)
            {
                Log.Warn($"navigation: no vanilla button for {target} right now");
                return;
            }
            Sound.Click();
            button.OnClicked(MouseButton.LeftMouse);
            Log.Info($"navigation: opened {target}");
        }
        catch (Exception ex)
        {
            Log.Warn($"navigation: {target} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Whether a destination can be opened from the active screen right now.
    internal static bool Available(Target target) => IsAvailable(target, FindNavigation());

    // The active screen's tab bar, for screens that hand navigation to the diorama.
    internal static StrategyNavigation ActiveTabBar() => FindNavigation();
}
