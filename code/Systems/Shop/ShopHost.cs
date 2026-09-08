using Il2CppInterop.Runtime;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Owns the native workshop screen while the shop is open. The catalogue and portrait views
// receive visibility changes without taking responsibility for native navigation callbacks.
internal sealed class ShopHost : IDisposable
{
    private readonly Action<bool> _setVisible;
    private readonly Action _onEntering;
    private readonly UiInjection _navigation;
    private readonly Dictionary<IntPtr, NavigationBinding> _wrappedWorkshopButtons = [];
    private bool _wantShop;
    private Label _nativeHeading;
    private string _nativeHeadingText;
    public bool IsOpen => _wantShop;

    private sealed class NavigationBinding(NavigationButton button,
        Il2CppSystem.Action<InteractiveElement> original, Il2CppSystem.Action<InteractiveElement> wrapper)
    {
        public readonly NavigationButton Button = button;
        public readonly Il2CppSystem.Action<InteractiveElement> Original = original;
        public readonly Il2CppSystem.Action<InteractiveElement> Wrapper = wrapper;
    }

    public ShopHost(ModContext context, Action<bool> setVisible, Action onEntering)
    {
        _setVisible = setVisible;
        _onEntering = onEntering;
        _navigation = UI.Inject(UiTarget.Screen<StrategyUIScreen>().After(UiSelector.Name(WorkshopUi.Navigation)), BuildNavigation);
        context.Patches.Postfix(WorkshopUi.ScreenType, "OnOpened", _ => Refresh());
        // Jiangyu restores injections on activation. Refresh this host's mode and selection too.
        context.Patches.Postfix("Il2CppMenace.UI.UIScreen", "Activate", _ => OnScreenActivated());
        context.Patches.Postfix("Il2CppMenace.UI.Strategy.StrategyNavigation", "InitWorkshopButton", _ => RefreshNavigation());
        context.Patches.Postfix("Il2CppMenace.States.StrategyState", "SetConversationVarValue", info =>
        {
            if (info.Args[0]?.ToString() == Il2CppMenace.States.StrategyState.CONV_VAR_WORKSHOP_UNLOCKED)
            {
                if (!WorkshopAccess.IsUnlocked)
                    _wantShop = false;
                RefreshNavigation();
                Refresh();
            }
        });
    }

    public void Reset()
    {
        _wantShop = false;
        RestoreNavigation();
        if (_nativeHeading != null)
            _nativeHeading.text = _nativeHeadingText;
        _nativeHeading = null;
        _nativeHeadingText = null;
        _setVisible(false);
    }

    private void RestoreNavigation()
    {
        foreach (var binding in _wrappedWorkshopButtons.Values)
            if (binding.Button.GetOnLeftClickedAction()?.Pointer == binding.Wrapper.Pointer)
                binding.Button.SetOnLeftClickedAction(binding.Original);
        _wrappedWorkshopButtons.Clear();
    }

    public void Dispose()
    {
        ShowWorkshop();
        RestoreNavigation();
        _navigation.Remove();
    }

    private VisualElement BuildNavigation()
    {
        var button = new NavigationButton { name = "wm-kalina-shop-button" };
        button.ButtonText = Locale.Text("WOMENACE::ui/kalina/shop", "KALINA'S SHOP");
        button.m_Label?.SetText(button.ButtonText, false);
        button.SetSelectable(true);
        button.SetSelected(_wantShop && UIManager.Get()?.GetActiveScreen()?.TryCast<WorkshopUIScreen>() != null);
        button.SetOnLeftClickedAction(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<InteractiveElement>>(
            (Action<InteractiveElement>)(_ => Open())));
        button.SetVisible(WorkshopAccess.IsUnlocked);
        return button;
    }

    private void OnScreenActivated()
    {
        var screen = UIManager.Get()?.GetActiveScreen();
        if (screen?.TryCast<WorkshopUIScreen>() != null)
            Refresh();
        else
        {
            _setVisible(false);
            if (screen?.TryCast<StrategyUIScreen>() != null)
            {
                _wantShop = false;
                RefreshNavigation();
            }
        }
    }

    private void RefreshNavigation()
    {
        var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<StrategyUIScreen>();
        var root = screen?.GetRootElement();
        if (root == null)
            return;
        var shop = UI.Find(root, UiSelector.Name("wm-kalina-shop-button"))?.TryCast<NavigationButton>();
        var workshop = UI.Find(root, UiSelector.Name(WorkshopUi.Navigation))?.TryCast<NavigationButton>();
        shop?.SetVisible(WorkshopAccess.IsUnlocked);
        shop?.SetSelected(_wantShop && screen.TryCast<WorkshopUIScreen>() != null);
        if (workshop == null)
            return;
        if (_wantShop)
            workshop.SetSelected(false);
        // Opening the already active native host need not call OnOpened. Its normal navigation
        // button must still switch back to Assemble when the shop occupies that host.
        var current = workshop.GetOnLeftClickedAction();
        if (!_wrappedWorkshopButtons.TryGetValue(workshop.Pointer, out var installed) || current?.Pointer != installed.Wrapper.Pointer)
        {
            var original = current;
            var wrapper = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<InteractiveElement>>(
                (Action<InteractiveElement>)(element =>
                {
                    ShowWorkshop();
                    original?.Invoke(element);
                }));
            _wrappedWorkshopButtons[workshop.Pointer] = new NavigationBinding(workshop, original, wrapper);
            workshop.SetOnLeftClickedAction(wrapper);
        }
    }

    public bool Open()
    {
        if (!WorkshopAccess.IsUnlocked)
            return false;
        var manager = UIManager.Get();
        if (!_wantShop || manager?.GetActiveScreen()?.TryCast<WorkshopUIScreen>() == null)
            _onEntering();
        _wantShop = true;
        if (manager?.GetActiveScreen()?.TryCast<WorkshopUIScreen>() == null)
            manager?.OpenScreen(WorkshopUIScreen.PREFAB_NAME);
        Refresh();
        return true;
    }

    public void ShowWorkshop()
    {
        _wantShop = false;
        Refresh();
    }

    public void Refresh()
    {
        var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<WorkshopUIScreen>();
        var root = screen?.GetRootElement();
        if (root == null)
            return;
        var show = _wantShop && WorkshopAccess.IsUnlocked;
        UI.Find(root, UiSelector.Name(WorkshopUi.Panels))?.SetVisible(!show);
        var heading = UI.Find(root, UiSelector.Name(WorkshopUi.Heading))?.TryCast<Label>();
        if (heading != null)
        {
            if (show)
            {
                if (_nativeHeading == null || _nativeHeading.Pointer != heading.Pointer)
                {
                    _nativeHeading = heading;
                    _nativeHeadingText = heading.text;
                }
                heading.text = Locale.Text("WOMENACE::ui/kalina/shop", "KALINA'S SHOP");
            }
            else if (_nativeHeading?.Pointer == heading.Pointer)
            {
                heading.text = _nativeHeadingText;
                _nativeHeading = null;
                _nativeHeadingText = null;
            }
        }
        _setVisible(show);
        if (!show)
            UI.Find(root, UiSelector.Name(WorkshopUi.Navigation))?.TryCast<NavigationButton>()?.SetSelected(true);
        RefreshNavigation();
    }
}
