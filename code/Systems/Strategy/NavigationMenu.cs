using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Game.Audio;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace WOMENACE.Code;

// The ship's navigation screen the Dinergate opens: a holographic deck plan over the blurred ops
// room. Every destination is the vanilla tab bar's own button for that screen (StrategyNavigation,
// by NavigationButtonType), clicked as the player would click it, so each screen opens exactly as
// it does in vanilla. A destination whose tab is hidden right now (mission select outside an
// operation, the workshop before it unlocks) shows dimmed and does nothing. Kalina's shop opens
// through the shop's own entry point.
internal static partial class NavigationMenu
{
    private const string Uxml = "diorama/navigation";

    private static VisualElement _root;
    private static RenderTexture _backdrop;
    private static ModContext _context;

    public static bool IsOpen => _root != null;

    internal enum Target { Bridge, Armory, Hiring, Oci, Market, Workshop, Shop }

    private static readonly (string Room, string Button, Target Target)[] Destinations =
    {
        ("wm-nav-room-bridge", "wm-nav-btn-bridge", Target.Bridge),
        ("wm-nav-room-armory", "wm-nav-btn-armory", Target.Armory),
        ("wm-nav-room-hiring", "wm-nav-btn-hiring", Target.Hiring),
        ("wm-nav-room-oci", "wm-nav-btn-oci", Target.Oci),
        ("wm-nav-room-market", "wm-nav-btn-market", Target.Market),
        ("wm-nav-room-workshop", null, Target.Workshop),
        ("wm-nav-room-shop", null, Target.Shop),
    };

    public static bool Open(ModContext context, Camera roomCamera)
    {
        if (IsOpen)
            return false;
        try
        {
            var host = UIManager.Get()?.GetLayer(PermanentUILayers.Dialogs) ?? UIManager.Get()?.GetPermanentLayersRoot();
            var tree = context.Assets.Load<VisualTreeAsset>(Uxml);
            FontWarmupSystem.WarmLoaded();
            if (host == null || tree == null)
            {
                context.Log.Warn($"navigation: cannot open (host {(host != null)}, uxml {(tree != null)})");
                return false;
            }
            _context = context;
            var container = tree.Instantiate();
            container.style.position = Position.Absolute;
            container.style.left = 0;
            container.style.top = 0;
            container.style.right = 0;
            container.style.bottom = 0;
            UI.Localise(container);
            ApplyGameFont(container);
            host.Add(container);
            _root = container;

            Blur(roomCamera);
            Wire();
            UI.Find(container, UiSelector.Name("wm-nav-close"))?.TryCast<Button>()?.clickable.add_clicked((Il2CppSystem.Action)(Action)Close);
            context.Coroutines.Start(WatchEscape(container));
            context.Log.Info("navigation: opened");
            return true;
        }
        catch (Exception ex)
        {
            context.Log.Warn($"navigation: open failed: {ex.GetType().Name}: {ex.Message}");
            Close();
            return false;
        }
    }

    public static void Close()
    {
        try { _root?.RemoveFromHierarchy(); } catch { }
        _root = null;
        if (_backdrop != null)
        {
            _backdrop.Release();
            UnityEngine.Object.Destroy(_backdrop);
            _backdrop = null;
        }
    }

    // The dialogs layer does not inherit the screens' font, so the menu would fall back to the
    // panel default. MENACE's UI face is Jura Bold, which the mod ships (UI/fonts) and wui.uss gives
    // this class, so nothing here holds a font of the game's.
    internal const string GameFontClass = "wm-game-font";

    internal static void ApplyGameFont(VisualElement root) => root.AddToClassList(GameFontClass);

    // The room behind the menu, rendered small and smoothed up a step: GFL2's menu blur.
    private static void Blur(Camera roomCamera)
    {
        if (roomCamera == null || _root == null)
            return;
        var w = Mathf.Max(Screen.width / 10, 16);
        var h = Mathf.Max(Screen.height / 10, 9);
        var small = RenderTexture.GetTemporary(w, h, 24);
        var previous = roomCamera.targetTexture;
        roomCamera.targetTexture = small;
        roomCamera.Render();
        roomCamera.targetTexture = previous;
        var tiny = RenderTexture.GetTemporary(w / 2, h / 2);
        _backdrop = new RenderTexture(w, h, 0) { filterMode = FilterMode.Bilinear };
        Graphics.Blit(small, tiny);
        Graphics.Blit(tiny, _backdrop);
        RenderTexture.ReleaseTemporary(tiny);
        RenderTexture.ReleaseTemporary(small);
        var backdrop = UI.Find(_root, UiSelector.Name("wm-nav-backdrop"));
        if (backdrop != null)
            backdrop.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_backdrop));
    }

    private static void Wire()
    {
        var navigation = FindNavigation();
        foreach (var (roomName, buttonName, target) in Destinations)
        {
            var available = IsAvailable(target, navigation);
            var room = UI.Find(_root, UiSelector.Name(roomName));
            if (room != null)
            {
                room.EnableInClassList("wui-room--off", !available);
                if (available)
                    room.RegisterCallback((EventCallback<ClickEvent>)(_ => Go(target)));
            }
            var button = buttonName != null ? UI.Find(_root, UiSelector.Name(buttonName))?.TryCast<Button>() : null;
            if (button != null)
            {
                button.text = Locale.Text($"WOMENACE::ui/nav/btn_{target.ToString().ToLowerInvariant()}", button.text);
                button.EnableInClassList("wui-holo-btn--off", !available);
                if (available)
                    button.clickable.add_clicked((Il2CppSystem.Action)(Action)(() => Go(target)));
            }
        }
    }

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
        var bar = UI.Find(documentRoot, UiSelector.Type<StrategyNavigation>())?.TryCast<StrategyNavigation>();
        if (bar != null)
            return bar;
        return null;
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
            Close();
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

    private static System.Collections.IEnumerator WatchEscape(VisualElement root)
    {
        while (_root == root)
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null ? keyboard.escapeKey.wasPressedThisFrame : Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                yield break;
            }
            yield return null;
        }
    }

    // Whether a destination can be opened from the active screen right now.
    internal static bool Available(Target target) => IsAvailable(target, FindNavigation());

    // The active screen's tab bar, for screens that hand navigation to the diorama.
    internal static StrategyNavigation ActiveTabBar() => FindNavigation();

}
