using Il2CppMenace.UI;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The shell: a strip of contents along the top and bottom of a strategy screen, over the room. The
// top one holds the title and the resources. The bottom one holds the destinations: every screen
// in one row, grouped by the room it is in.
//
// The shell is ours (diorama/shell), added to the vanilla screen's own root so the game's dialogs
// and tooltips stay above it. Everything it does is vanilla's: a destination clicks the vanilla tab
// bar's button for that screen, and the resources are the vanilla displays, moved into the top bar
// by shell.uss, which is attached to the screen root and switched on by a class. Taking the class
// off returns the screen to vanilla.
//
// The home screens (mission select during an operation, the system map outside one) have two views.
// Home is the room from its own camera with the vanilla panels put away. Operations is the holotable
// with them shown.
public sealed partial class StrategyDiorama
{
    private static readonly HashSet<string> HomeScreens = new() { VanillaUi.Screens.MissionSelect, VanillaUi.Screens.SystemMap };

    // What the shell shows for a screen: the room and, where the room holds more than one screen,
    // the screen.
    private readonly record struct ShellLook(string RoomKey, string Room, string ScreenKey, string Screen);

    private static readonly HashSet<string> ShellScreens = new()
    {
        VanillaUi.Screens.MissionSelect, VanillaUi.Screens.SystemMap, VanillaUi.Screens.Armory, VanillaUi.Screens.Hiring,
        VanillaUi.Screens.BlackMarket, VanillaUi.Screens.Workshop, VanillaUi.Screens.ShipUpgrades,
        VanillaUi.Screens.OperationSelect, VanillaUi.Screens.MissionPrep,
    };

    // A screen's own actions on the bottom bar: the vanilla buttons the two actions press, their
    // labels, whether the lesser one is a retreat or a loss, the vanilla label that names what they
    // act on, and whether the main one goes out through the bars.
    private readonly record struct ShellActions(string Primary, string PrimaryKey, string PrimaryLabel,
        string Secondary, string SecondaryKey, string SecondaryLabel, bool SecondaryDanger, string Info, bool ThroughBars);

    private static ShellActions? ActionsFor(string screenName, bool onOperations) => screenName switch
    {
        VanillaUi.Screens.MissionSelect when onOperations => new(VanillaUi.Elements.PlanMissionButton, "WOMENACE::ui/shell/plan_mission", "PLAN MISSION",
            VanillaUi.Elements.AbortButton, "WOMENACE::ui/shell/abort_operation", "ABORT OPERATION", true, VanillaUi.Elements.MissionName, false),
        VanillaUi.Screens.OperationSelect => new(VanillaUi.Elements.StartOperationButton, "WOMENACE::ui/shell/start_operation", "START OPERATION",
            VanillaUi.Elements.CloseButton, "WOMENACE::ui/shell/back", "BACK", false, VanillaUi.Elements.OperationNameRow, false),
        VanillaUi.Screens.MissionPrep => new(VanillaUi.Elements.StartMissionButton, "WOMENACE::ui/shell/start_mission", "START MISSION",
            VanillaUi.Elements.AbortButton, "WOMENACE::ui/shell/back", "BACK", false, null, true),
        _ => null,
    };

    private const string OpsRoomKey = "WOMENACE::ui/shell/ops_room";
    private const string WeaponRoomKey = "WOMENACE::ui/shell/weapon_room";
    private const string RefittingRoomKey = "WOMENACE::ui/shell/refitting_room";

    private static ShellLook LookFor(string screenName) => screenName switch
    {
        VanillaUi.Screens.Armory or VanillaUi.Screens.Hiring => new(RefittingRoomKey, "REFITTING ROOM", null, null),
        VanillaUi.Screens.BlackMarket => new(OpsRoomKey, "OPS ROOM", "WOMENACE::ui/shell/market", "MARKET"),
        // Kalina's shop is hosted on the workshop screen and belongs to the market.
        VanillaUi.Screens.Workshop when ShopSystem.IsShopOpen => new(OpsRoomKey, "OPS ROOM", "WOMENACE::ui/shell/market", "MARKET"),
        VanillaUi.Screens.Workshop => new(WeaponRoomKey, "WEAPON ROOM", null, null),
        VanillaUi.Screens.ShipUpgrades => new(OpsRoomKey, "OPS ROOM", "WOMENACE::ui/shell/oci", "O.C.I."),
        VanillaUi.Screens.OperationSelect => new(OpsRoomKey, "OPS ROOM", "WOMENACE::ui/shell/operations_title", "OPERATIONS"),
        VanillaUi.Screens.MissionPrep => new(OpsRoomKey, "OPS ROOM", "WOMENACE::ui/shell/mission_prep", "DEPLOYMENT"),
        _ => new(OpsRoomKey, "OPS ROOM", null, null),
    };

    // A screen that is stepped into and backed out of (an operation's briefing, mission
    // preparation) has its own two actions along the bottom and no destinations.
    private static bool ShowsDestinations(string screenName) =>
        screenName is not (VanillaUi.Screens.OperationSelect or VanillaUi.Screens.MissionPrep);

    // Set by the Operations button on another screen: the home screen it goes to opens on Operations.
    private bool _arriveOnOperations;
    // The running operation's name, as mission select's header shows it.
    private string _operationName;
    private const string ShellUxml = "diorama/shell";
    private const string ShellOnClass = "wm-shell-on";
    private const string HomeViewClass = "wm-view-home";
    private const string OperationsViewClass = "wm-view-operations";
    private const string CaptionClass = "wui-res-caption";

    private enum HomeView { Home, Operations }

    private HomeView _homeView;
    private string _policyScreen;
    private VisualElement _shell;
    private VisualElement _shellRoot;

    private static readonly (string Element, string Key, string Fallback)[] ResourceCaptions =
    {
        (VanillaUi.Elements.PromotionPoints, "WOMENACE::ui/shell/promotion", "PROMOTION"),
        (VanillaUi.Elements.OciPoints, "WOMENACE::ui/shell/oci_points", "O.C.I."),
        (VanillaUi.Elements.Authority, "WOMENACE::ui/shell/authority", "AUTHORITY"),
        (VanillaUi.Elements.Squaddies, "WOMENACE::ui/shell/dummies", "DUMMIES"),
    };

    // A destination on the bottom row: its button, the room whose group it stands in, where it
    // goes, and for the Workshop, which is one screen doing three things, the station.
    private readonly record struct Destination(string Element, string RoomKey, NavigationMenu.Target Target, int Station = -1);

    private static readonly Destination[] Destinations =
    {
        new("wm-nav-oci", OpsRoomKey, NavigationMenu.Target.Oci),
        new("wm-nav-market", OpsRoomKey, NavigationMenu.Target.Market),
        new("wm-nav-kalina", OpsRoomKey, NavigationMenu.Target.Shop),
        new("wm-nav-roster", RefittingRoomKey, NavigationMenu.Target.Armory),
        new("wm-nav-recruit", RefittingRoomKey, NavigationMenu.Target.Hiring),
        new("wm-nav-assembly", WeaponRoomKey, NavigationMenu.Target.Workshop, AssembleStation),
        new("wm-nav-disassembly", WeaponRoomKey, NavigationMenu.Target.Workshop, DisassembleStation),
        new("wm-nav-calibration", WeaponRoomKey, NavigationMenu.Target.Workshop, CalibrateStation),
    };

    private static readonly (string Element, string RoomKey)[] DestinationGroups =
    {
        ("wm-nav-group-ops", OpsRoomKey),
        ("wm-nav-group-refitting", RefittingRoomKey),
        ("wm-nav-group-weapon", WeaponRoomKey),
    };

    private static bool IsHere(Destination destination, string screenName) => destination.Target switch
    {
        NavigationMenu.Target.Oci => screenName == VanillaUi.Screens.ShipUpgrades,
        NavigationMenu.Target.Market => screenName == VanillaUi.Screens.BlackMarket,
        NavigationMenu.Target.Shop => screenName == VanillaUi.Screens.Workshop && ShopSystem.IsShopOpen,
        NavigationMenu.Target.Armory => screenName == VanillaUi.Screens.Armory,
        NavigationMenu.Target.Hiring => screenName == VanillaUi.Screens.Hiring,
        NavigationMenu.Target.Workshop => screenName == VanillaUi.Screens.Workshop && !ShopSystem.IsShopOpen
            && WorkshopStation == destination.Station,
        _ => false,
    };

    // The station asked for from another screen, and until when: the Workshop takes it up once
    // its panels are built, which is some frames after it is the screen.
    private int? _arriveAtStation;
    private float _arriveAtStationUntil;
    private const float ArriveAtStationSeconds = 5f;
    // The Workshop's own switches, which are there once its panels are.
    private const string WorkshopPanelsName = "wm-workshop-tabs";

    private void GoTo(Destination destination)
    {
        _arriveAtStation = null;
        if (destination.Station < 0)
        {
            // Kalina's shop opens over the Workshop's own screen, where calibration's panels
            // would stay under it.
            if (destination.Target == NavigationMenu.Target.Shop && _policyScreen == VanillaUi.Screens.Workshop)
                CalibrationUISystem.Instance?.Close();
            GoTo(destination.Target);
            return;
        }
        if (_policyScreen == VanillaUi.Screens.Workshop && !ShopSystem.IsShopOpen)
        {
            GoToStation(destination.Station);
            return;
        }
        _arriveAtStation = destination.Station;
        _arriveAtStationUntil = UnityEngine.Time.unscaledTime + ArriveAtStationSeconds;
        GoTo(NavigationMenu.Target.Workshop);
    }

    // Arriving on a home screen shows Home. Closing an operation's briefing returns to the map it
    // was opened from.
    private void TrackHomeView(string screenName)
    {
        var arriving = screenName != _policyScreen && screenName != null && HomeScreens.Contains(screenName);
        // The view is kept where the home screen was only stepped away from: a briefing or mission
        // preparation backed out of, or screens without the shell that covered it (the pause menu).
        // Judged by the last screen that wore the shell, since vanilla passes through others between.
        var steppedBack = _lastShellScreen == VanillaUi.Screens.OperationSelect || _lastShellScreen == VanillaUi.Screens.MissionPrep
            || _lastShellScreen == screenName;
        if (arriving && (!steppedBack || _arriveOnOperations))
        {
            _homeView = _arriveOnOperations ? HomeView.Operations : HomeView.Home;
            _arriveOnOperations = false;
        }
        if (arriving && _intelOnReturn)
        {
            _intelOnReturn = false;
            _homeView = HomeView.Home;
            Context.Coroutines.Start(OpenIntelOnArrival());
        }
        if (screenName != null && ShellScreens.Contains(screenName))
            _lastShellScreen = screenName;
        _policyScreen = screenName;
    }

    // The last screen that wore the shell in this scene.
    private string _lastShellScreen;

    private bool OnHomeView(string screenName) =>
        screenName != null && HomeScreens.Contains(screenName) && _homeView == HomeView.Home;

    private void SetHomeView(HomeView view)
    {
        if (_homeView == view)
            return;
        _homeView = view;
        Sound.Click();
        // Both views are in the ops room, so the camera travels between them (see Frame).
        ApplyScreenPolicy();
    }

    private const float GlideSeconds = 0.9f;

    private const string PanHintName = "wm-pan-hint";

    // A view the player can turn says so at the foot of the frame, midway between the panels:
    // the Squads hall, mission preparation's map, mission select's planet and O.C.I.'s ship.
    // Not while the equipment picker is open over that part of the frame.
    private void DrivePanHint(VisualElement shell, bool covered)
    {
        var on = !covered && !_squadPickerOpen && ((_policyScreen == VanillaUi.Screens.MissionPrep && _prepOrbitOn) || ((_policyScreen == ArmoryScreen || _policyScreen == VanillaUi.Screens.Hiring) && _stage != null)
            || _planetOrbitOn || _ociOn);
        // Asked every frame: the hint is found once a shell, and its style touched on a change.
        if (_panHint?.parent != shell)
            (_panHint, _panHintOn) = (null, false);
        if (on == _panHintOn)
            return;
        _panHintOn = on;
        var hint = _panHint;
        if (hint == null)
        {
            hint = new VisualElement { name = PanHintName, pickingMode = PickingMode.Ignore };
            hint.AddToClassList("wm-pan-hint");
            foreach (var (key, fallbackKey, action, fallbackAction) in new[]
            {
                ("WOMENACE::ui/shell/pan_key", "MMB", "WOMENACE::ui/shell/pan_action", "ROTATE"),
                ("WOMENACE::ui/shell/zoom_key", "SCROLL", "WOMENACE::ui/shell/zoom_action", "ZOOM"),
            })
            {
                var input = new Label(Locale.Text(key, fallbackKey)) { pickingMode = PickingMode.Ignore };
                input.AddToClassList("wm-pan-hint__key");
                var does = new Label(Locale.Text(action, fallbackAction)) { pickingMode = PickingMode.Ignore };
                does.AddToClassList("wm-pan-hint__action");
                hint.Add(input);
                hint.Add(does);
            }
            shell.Add(hint);
            _panHint = hint;
            PlacePanHint();
        }
        hint.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private VisualElement _panHint;
    private bool _panHintOn;

    // The screen's root keeps to 16:9 in the middle of a wider or a taller screen, as vanilla lays
    // it out. The shell covers the whole screen, so what it draws over the room (the tags, the
    // cards' click targets, the black) is placed in the screen's own coordinates and reaches its
    // edges, while the bars' contents keep to the root. These are how far the root stands in
    // from the screen's left, top, right and bottom.
    private (float Left, float Top, float Right, float Bottom)? _shellInset;
    private const float BarPadding = 32f;

    private void PlaceShell(VisualElement shell)
    {
        var root = shell.parent;
        var whole = root?.panel?.visualTree;
        if (whole == null)
            return;
        var (inner, outer) = (root.worldBound, whole.worldBound);
        if (!(inner.width > 1f) || !(outer.width > 1f))
            return;
        var inset = (MathF.Max(inner.xMin - outer.xMin, 0f), MathF.Max(inner.yMin - outer.yMin, 0f),
            MathF.Max(outer.xMax - inner.xMax, 0f), MathF.Max(outer.yMax - inner.yMax, 0f));
        if (_shellInset is { } was && MathF.Abs(was.Left - inset.Item1) + MathF.Abs(was.Top - inset.Item2)
            + MathF.Abs(was.Right - inset.Item3) + MathF.Abs(was.Bottom - inset.Item4) < 0.5f)
            return;
        _shellInset = inset;
        var (left, top, right, bottom) = inset;
        shell.style.left = -left;
        shell.style.top = -top;
        shell.style.right = -right;
        shell.style.bottom = -bottom;
        foreach (var (name, above) in new[] { ("wm-shell-top", true), ("wm-shell-bottom", false) })
        {
            var bar = UI.Find(shell, UiSelector.Name(name));
            if (bar == null)
                continue;
            bar.style.paddingLeft = BarPadding + left;
            bar.style.paddingRight = BarPadding + right;
            if (above)
                bar.style.marginTop = top;
            else
                bar.style.marginBottom = bottom;
        }
        PlaceActions();
        PlacePanHint();
        // The black is half the shell's height each, and stands by the shell's edges.
        if (!shell.ClassListContains(BarsClosedClass))
            MoveBars(shell, shell.ClassListContains(BarsEventClass) ? BarEventHeight : BarsOpen);
        // A screen's own layout reaches the screen's edges where it has to (see LayOutMarket).
        ApplyHome(_homeScreen, _homeRoomShown);
    }

    // Raised over the destinations, the actions stand at the root's two sides.
    private void PlaceActions()
    {
        var row = _shell != null ? UI.Find(_shell, UiSelector.Name("wm-shell-actions")) : null;
        if (row == null)
            return;
        var raised = row.ClassListContains("wui-act-row--raised");
        row.style.left = raised && _shellInset is { } a ? BarPadding + a.Left : StyleKeyword.Null;
        row.style.right = raised && _shellInset is { } b ? BarPadding + b.Right : StyleKeyword.Null;
    }

    // The hint is placed by its rules from the root's left and foot.
    private void PlacePanHint()
    {
        if (_panHint == null || _shellInset is not { } inset)
            return;
        _panHint.style.marginLeft = inset.Left;
        _panHint.style.marginBottom = inset.Bottom;
    }

    private static readonly string[] OperationsPanels = { VanillaUi.Elements.Hud, VanillaUi.Elements.MissionPois, VanillaUi.Elements.MissionWindow, VanillaUi.Elements.TopRightCorner };

    // How far the Operations view has arrived, 0 to 1: the hologram's strength and its panels' opacity.
    private void SetOperationsFade(float k)
    {
        try
        {
            SetMarkersFade(k);
            if (_shellRoot == null)
                return;
            foreach (var name in OperationsPanels)
            {
                var panel = UI.Find(_shellRoot, UiSelector.Name(name));
                if (panel != null && panel.parent == _shellRoot)
                    panel.style.opacity = k >= 1f ? StyleKeyword.Null : k;
            }
        }
        catch { }
    }

    private bool _homePending;
    private string _homeScreen;
    private bool _homeRoomShown;

    // The shell goes on in the frame the screen opens, so the screen never shows without it. The
    // screen policy runs inside the UI manager's own calls, and a panel in the middle of a layout
    // pass refuses hierarchy changes: then the shell is applied on the next frame instead.
    private void ApplyHome(string screenName, bool roomShown)
    {
        _homeScreen = screenName;
        _homeRoomShown = roomShown;
        if (_homePending)
            return;
        try
        {
            ApplyHomeNow();
        }
        catch (Exception)
        {
            _homePending = true;
            Context.Coroutines.Start(ApplyHomeNextFrame());
        }
    }

    private System.Collections.IEnumerator ApplyHomeNextFrame()
    {
        yield return null;
        _homePending = false;
        try
        {
            ApplyHomeNow();
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy UI: shell failed, the screen stays vanilla: {ex.GetType().Name}: {ex.Message}");
            ClearHome();
        }
    }

    private void ApplyHomeNow()
    {
        if (!Enabled)
        {
            ClearHome();
            return;
        }
        var active = UIManager.Get()?.GetActiveScreen();
        var screenName = active != null ? active.GetIl2CppType().Name : null;
        // A screen without the shell (the pause menu, a conversation) covers the one that wears it.
        // The shell stays on, so the screen underneath comes back as it was left.
        if (!_homeRoomShown || screenName != _homeScreen || screenName == null || !ShellScreens.Contains(screenName))
            return;
        if (!ShowShell(screenName))
        {
            ClearHome();
            return;
        }
        var home = HomeScreens.Contains(screenName);
        SetHomeLayout(home && _homeView == HomeView.Operations, screenName);
        LayOutScreen(screenName, _shellRoot);
    }

    private bool ShowShell(string screenName)
    {
        var document = UIManager.Get()?.GetActiveScreen()?.m_Container?.rootVisualElement;
        var root = document != null ? UI.Find(document, UiSelector.Class(VanillaUi.Elements.ScreenRootClass)) : null;
        // Mission preparation has no tab bar, and so no resources on its top bar, as in vanilla.
        var bar = NavigationMenu.ActiveTabBar();
        if (root == null || (bar == null && screenName != VanillaUi.Screens.MissionPrep)
            || (bar != null && !VanillaUi.Require(Context, screenName, bar, VanillaUi.Elements.TabBarButtons, VanillaUi.Elements.TabBarResources)))
            return false;

        if (_shell == null || _shellRoot != root || _shell.parent != root)
        {
            RemoveShell();
            var tree = Context.Assets.Load<VisualTreeAsset>(ShellUxml);
            FontWarmupSystem.WarmLoaded();
            if (tree == null)
                return false;
            _shell = tree.Instantiate();
            _shell.pickingMode = PickingMode.Ignore;
            _shell.style.position = Position.Absolute;
            _shell.style.left = 0;
            _shell.style.top = 0;
            _shell.style.right = 0;
            _shell.style.bottom = 0;
            _shellInset = null;
            UI.Localise(_shell);
            // MENACE's bold face (Jura-Bold), set outright as the shell's font.
            NavigationMenu.ApplyGameFont(_shell);
            root.Add(_shell);
            _shellRoot = root;
            ShareStyles(root);
            // Before the shell is wired: wiring it runs its first frame at once, which places
            // the shell and applies the screen again, and that may shut the bars to open them
            // (the branch below). Moved after it, the bars would stand open while shut.
            MoveBars(_shell, BarsOpen);
            WireShell();
            if (bar != null)
            {
                AddResourceCaptions(bar);
                LayOutResources(bar);
            }
            // The bars open on arriving in another room, and on the first arrival of all.
            var arrivedIn = LookFor(screenName).RoomKey;
            if (_arrivedThroughBars || _shellRoom != arrivedIn)
                Context.Coroutines.Start(OpenBars(_shell));
            _arrivedThroughBars = false;
            _shellRoom = arrivedIn;
        }
        else if (_arrivedThroughBars)
        {
            // A cut that stayed on this screen (Kalina's shop to the workshop under it): the
            // bars it shut open here.
            _arrivedThroughBars = false;
            _shellRoom = LookFor(screenName).RoomKey;
            Context.Coroutines.Start(OpenBars(_shell));
        }
        // The bars cover the screen's panels, and the vanilla resources sit on the top bar.
        // and the Intel panel's vanilla content over the shell.
        var messages = UI.Find(root, UiSelector.TypeName(VanillaUi.Elements.MessagesPanel));
        var factions = UI.Find(root, UiSelector.TypeName(VanillaUi.Elements.StoryFactionList));
        if (messages != null && messages.parent != root)
            messages = null;
        if (factions != null && factions.parent != root)
            factions = null;
        RememberOrder(root, bar, messages, factions);
        OrderLast(root, _shell, bar, messages, factions);
        RefreshIntel(root, messages, factions);

        root.EnableInClassList(ShellOnClass, true);
        var homeScreen = HomeScreens.Contains(screenName);
        root.EnableInClassList(HomeViewClass, homeScreen && _homeView == HomeView.Home);
        root.EnableInClassList(OperationsViewClass, homeScreen && _homeView == HomeView.Operations);
        RefreshShell(document, screenName);
        return true;
    }

    private bool _shellShop;

    // The vanilla buttons the bottom bar's two actions press, by name, on the Operations view.
    private string _actionPrimary;
    private string _actionSecondary;

    private void ClickVanilla(string name)
    {
        var button = name != null && _shellRoot != null ? UI.Find(_shellRoot, UiSelector.Name(name))?.TryCast<InteractiveElement>() : null;
        if (button == null || !button.IsInteractable())
            return;
        button.OnClicked(MouseButton.LeftMouse);
    }

    private bool _actionThroughBars;

    // The text of a vanilla label by name, or of the first label inside the element of that name.
    private string VanillaText(string name)
    {
        var element = _shellRoot != null ? UI.Find(_shellRoot, UiSelector.Name(name)) : null;
        if (element == null)
            return "";
        var label = element.TryCast<Label>() ?? UI.Find(element, UiSelector.Type<Label>())?.TryCast<Label>();
        return label?.text ?? "";
    }

    // A screen's own actions, each a press of the vanilla button. They stand along the bottom
    // where the screen has no destinations, and over the destinations where it has.
    private void RefreshActions(string screenName, bool onOperations)
    {
        var actions = ActionsFor(screenName, onOperations);
        Show("wm-shell-actions", actions != null);
        UI.Find(_shell, UiSelector.Name("wm-shell-actions"))?.EnableInClassList("wui-act-row--raised", ShowsDestinations(screenName));
        PlaceActions();
        _actionPrimary = actions?.Primary;
        _actionSecondary = actions?.Secondary;
        _actionThroughBars = actions?.ThroughBars ?? false;
        if (actions is not { } a || _shellRoot == null)
            return;
        SetText("wm-act-primary-label", Locale.Text(a.PrimaryKey, a.PrimaryLabel));
        SetText("wm-act-secondary-label", Locale.Text(a.SecondaryKey, a.SecondaryLabel));
        UI.Find(_shell, UiSelector.Name("wm-act-secondary"))?.EnableInClassList("wui-act--danger", a.SecondaryDanger);
        SetText("wm-act-info", a.Info != null ? VanillaText(a.Info).ToUpperInvariant() : "");
        foreach (var (element, vanilla) in new[] { ("wm-act-primary", a.Primary), ("wm-act-secondary", a.Secondary) })
        {
            var button = UI.Find(_shellRoot, UiSelector.Name(vanilla))?.TryCast<InteractiveElement>();
            var usable = button != null && button.IsInteractable();
            UI.Find(_shell, UiSelector.Name(element))?.EnableInClassList("wui-act--off", !usable);
        }
    }

    private readonly List<(VisualElement Child, VisualElement After)> _shellOrder = new();

    // Where vanilla's children stood among their siblings before the shell reordered them, so they
    // go back there when it comes off. Their vanilla layout depends on it.
    private void RememberOrder(VisualElement root, params VisualElement[] children)
    {
        foreach (var child in children)
        {
            if (child == null || child.parent != root || _shellOrder.Any(o => o.Child == child))
                continue;
            var index = root.IndexOf(child);
            _shellOrder.Add((child, index > 0 ? root.ElementAt(index - 1) : null));
        }
    }

    private void RestoreOrder()
    {
        // Twice: a child placed after a sibling that itself moves later lands right on the second pass.
        foreach (var (child, after) in _shellOrder.Concat(_shellOrder).ToList())
        {
            try
            {
                if (child?.parent == null)
                    continue;
                if (after != null && after.parent == child.parent)
                    child.PlaceInFront(after);
                else
                    child.SendToBack();
            }
            catch { }
        }
        _shellOrder.Clear();
    }

    // Puts the given children last in the root, in this order, and only when they are not already.
    private static void OrderLast(VisualElement root, params VisualElement[] children)
    {
        var wanted = children.Where(c => c != null).ToList();
        var first = root.childCount - wanted.Count;
        var ordered = first >= 0;
        for (var i = 0; ordered && i < wanted.Count; i++)
            ordered = root.ElementAt(first + i) == wanted[i];
        if (ordered)
            return;
        foreach (var child in wanted)
            child.BringToFront();
    }

    private bool _intelOpen;

    // Applies an inline style to a vanilla element and remembers how to take it off again.
    private void Inline(VisualElement element, Action<VisualElement> apply, Action<VisualElement> undo)
    {
        if (element == null)
            return;
        apply(element);
        // Once for each style on each element, however often the layout is applied: an element
        // styled from several places has each of them taken off.
        if (_inlinedStyles.Add((element.Pointer, undo.Method)))
            _shellUndo.Add(() => undo(element));
        // Layouts that take a style's value before they change it ask this set whether
        // the value is still vanilla's.
        _inlined.Add(element.Pointer);
    }

    // Inline, for a layout written against an element's style alone.
    private void InlineStyle(VisualElement element, Action<IStyle> apply, Action<IStyle> undo)
    {
        if (element == null)
            return;
        apply(element.style);
        if (_inlinedStyles.Add((element.Pointer, undo.Method)))
            _shellUndo.Add(() => undo(element.style));
        _inlined.Add(element.Pointer);
    }

    private readonly HashSet<IntPtr> _inlined = new();
    private readonly HashSet<(IntPtr, System.Reflection.MethodInfo)> _inlinedStyles = new();

    // The Dinergate's tag follows it on screen. It shows on the Home view while the board is shut,
    // and lights up while the pointer is over the Dinergate.
    private System.Collections.IEnumerator FollowDinergate(VisualElement shell)
    {
        var tag = UI.Find(shell, UiSelector.Name("wm-dg-tag"));
        var tableTag = _tableTag = UI.Find(shell, UiSelector.Name("wm-table-tag"));
        var restyle = 0f;
        var events = 0f;
        var actions = 0f;
        var placed = 0f;
        var eventPlaying = false;
        var resumeIntel = false;
        var recount = 0f;
        var boardScan = UI.Find(shell, UiSelector.Class("wui-board__scan"));
        while (tag != null && _shell == shell)
        {
            var camera = RoomCamera;
            var covered = shell.ClassListContains(BarsClosedClass);
            var visible = !eventPlaying && _dinergate != null && _dinergate.activeInHierarchy && camera != null && camera.isActiveAndEnabled
                && _policyScreen != null && HomeScreens.Contains(_policyScreen) && _homeView == HomeView.Home && !_intelOpen
                && !covered;
            tag.EnableInClassList("wui-tag--off", !visible);
            tag.EnableInClassList("wui-tag--hover", visible && _dinergateHover);
            // The holotable's tag: a click on the table goes to Operations.
            var tableVisible = !eventPlaying && camera != null && camera.isActiveAndEnabled && _room != null
                && OnHomeView(_policyScreen) && !_intelOpen && !covered;
            if (tableTag != null)
            {
                tableTag.EnableInClassList("wui-tag--off", !tableVisible);
                tableTag.EnableInClassList("wui-tag--hover", tableVisible && _tableHover);
                if (tableVisible && shell.panel != null)
                {
                    var at = camera.WorldToScreenPoint(_room.transform.TransformPoint(TableTagPoint));
                    var place = RuntimePanelUtils.ScreenToPanel(shell.panel, new UnityEngine.Vector2(at.x, UnityEngine.Screen.height - at.y));
                    tableTag.style.left = place.x - tableTag.resolvedStyle.width / 2f;
                    tableTag.style.top = place.y - tableTag.resolvedStyle.height;
                }
            }
            DriveLand();
            DriveTableGrid();
            DriveRoomShade();
            DrivePlanet();
            DriveStarMap();
            DriveSquads();
            DriveRecruit();
            DrivePrepMap();
            DrivePrep();
            DrivePrepFlat();
            DrivePrepCamera();
            DrivePlanetCamera();
            DriveOci();
            DriveMarket();
            DriveShop();
            DriveMusic();
            DriveTableSounds();
            DriveRoomCamera();
            DriveRoomDolls();
            DrivePanHint(shell, covered);
            // The screen's size can change under the shell (a resolution chosen in the options).
            if ((placed -= UnityEngine.Time.unscaledDeltaTime) <= 0f)
            {
                placed = 0.25f;
                PlaceShell(shell);
            }
            DrivePrepInput();
            DrivePrepTokens();
            // The table's markers follow the camera and the selection.
            if (_markers.Count > 0)
                RefreshMarkers(false);
            // Vanilla places its faction window again as it opens it.
            if (_intelOpen && _factionWindow?.panel != null && _factionWindow.resolvedStyle.display != DisplayStyle.None)
                PlaceFactionWindow();
            if (_intelOpen)
            {
                DriftScan(boardScan, UnityEngine.Time.unscaledTime);
                DriftScan(_factionScan, UnityEngine.Time.unscaledTime);
            }
            // Vanilla shows its lists again when messages arrive. Off the board they stay put away.
            if (!(_intelOpen && _intelListsShown == _intelSeq))
                foreach (var list in new[] { _boardMessages, _boardFactions })
                    if (list != null && list.panel != null && list.style.display.value != DisplayStyle.None)
                        list.style.display = DisplayStyle.None;
            // The actions follow the vanilla buttons they press (the selected mission, what is allowed).
            if (_policyScreen != null && (actions -= UnityEngine.Time.unscaledDeltaTime) <= 0f)
            {
                actions = 0.25f;
                var operations = HomeScreens.Contains(_policyScreen) && _homeView == HomeView.Operations;
                if (ActionsFor(_policyScreen, operations) != null)
                    RefreshActions(_policyScreen, operations);
            }
            // Messages arrive after the screen opens and are read while it shows.
            if ((recount -= UnityEngine.Time.unscaledDeltaTime) <= 0f)
            {
                recount = 0.5f;
                if (CountMessages(_boardMessages) != (_intelListed, _intelMessages))
                    ApplyHome(_homeScreen, _homeRoomShown);
            }
            // A story event playing over the screen.
            if ((events -= UnityEngine.Time.unscaledDeltaTime) <= 0f)
            {
                events = 0.15f;
                var layers = UIManager.Get()?.GetPermanentLayersRoot();
                var playing = layers != null && UI.Find(layers, UiSelector.TypeName(VanillaUi.Elements.EventDialog)) != null;
                // The bars come in for it, as for a cutscene. Their contents stay where they are.
                if (playing != eventPlaying && !covered)
                {
                    eventPlaying = playing;
                    MoveBars(shell, playing ? BarEventHeight : BarsOpen);
                    // The bars are bare for it: their contents and the vanilla resources go.
                    shell.EnableInClassList(BarsEventClass, playing);
                    shell.parent?.EnableInClassList(BarsEventClass, playing);
                }
                // The event takes the middle of the screen, so the board it was opened from steps aside.
                if (playing && _intelOpen)
                {
                    resumeIntel = true;
                    SetIntel(false);
                }
                // A message read from the board returns to it.
                else if (!playing && resumeIntel)
                {
                    resumeIntel = false;
                    if (_homeView == HomeView.Home && _policyScreen != null && HomeScreens.Contains(_policyScreen))
                        SetIntel(true);
                }
            }
            // Kalina's shop opens and shuts on the workshop screen without a screen change.
            if (_policyScreen == VanillaUi.Screens.Workshop && _shellShop != ShopSystem.IsShopOpen)
            {
                ApplyScreenPolicy();
                ApplyHome(_homeScreen, _homeRoomShown);
            }
            // Its stations change what it is doing without a screen change too, and the camera
            // goes between them.
            else if (_policyScreen == VanillaUi.Screens.Workshop && _workshopStation != WorkshopStation)
            {
                ApplyScreenPolicy();
                ApplyHome(_homeScreen, _homeRoomShown);
            }
            // Arrived from another screen by a station's button: the Workshop turns to that station.
            if (_arriveAtStation is { } station)
            {
                if (UnityEngine.Time.unscaledTime > _arriveAtStationUntil)
                    _arriveAtStation = null;
                else if (_policyScreen == VanillaUi.Screens.Workshop && !ShopSystem.IsShopOpen
                    && UI.Find(_shellRoot, UiSelector.Name(WorkshopPanelsName))?.panel != null)
                {
                    if (WorkshopStation == station)
                        _arriveAtStation = null;
                    else
                        SetStation(station);
                }
            }
            // What it shows follows what is picked, looked at a few times a second.
            if (_policyScreen == VanillaUi.Screens.Workshop && _active == _weaponSet && !ShopSystem.IsShopOpen
                && UnityEngine.Time.frameCount % WorkshopLookFrames == 0)
            {
                ShowWorkedOnWeapon();
                ShowCalibrationDoll();
            }
            if (_intelOpen && _intelListsShown == _intelSeq && (restyle -= UnityEngine.Time.unscaledDeltaTime) <= 0f)
            {
                restyle = 0.3f;
                var root = _shellRoot;
                if (root != null)
                {
                    var messages = UI.Find(root, UiSelector.TypeName(VanillaUi.Elements.MessagesPanel));
                    var factions = UI.Find(root, UiSelector.TypeName(VanillaUi.Elements.StoryFactionList));
                    StyleLists(messages, factions);
                    // The lists as they really laid out, once they show.
                    var rows = messages != null ? UI.Find(messages, UiSelector.Name(VanillaUi.Elements.SlotsContainer)) : null;
                    var cards = factions != null ? UI.Find(factions, UiSelector.Name(VanillaUi.Elements.FactionsContainer)) : null;
                    var measured = Math.Max(rows != null ? rows.layout.height : 0f, cards != null ? cards.layout.height : 0f);
                    if (measured > 1f && Math.Abs(Math.Clamp(measured, BoardListsMin, BoardListsMax) - _boardLists) > 1f)
                    {
                        _boardMeasured = measured;
                        ApplyHome(_homeScreen, _homeRoomShown);
                    }
                }
            }
            if (visible && shell.panel != null)
            {
                // Over the Dinergate, centred on its eye.
                var top = camera.WorldToScreenPoint(_dinergate.transform.position + UnityEngine.Vector3.up * DinergateTagHeight);
                var eye = camera.WorldToScreenPoint(DinergateEye);
                var point = RuntimePanelUtils.ScreenToPanel(shell.panel, new UnityEngine.Vector2(eye.x, UnityEngine.Screen.height - top.y));
                tag.style.left = point.x - tag.resolvedStyle.width / 2f;
                tag.style.top = point.y - tag.resolvedStyle.height;
            }
            yield return null;
        }
    }

    // The shell's stylesheets, attached to the screen root so shell.uss can restyle the vanilla
    // elements beside the shell. A stylesheet is not loadable on its own, and the shell's template
    // carries them on its top element.
    private void ShareStyles(VisualElement root)
    {
        // Through the elements' own lists: styleSheets is a value type the interop boxes, which
        // is not safe to compare or pass about.
        var sheets = _shell.styleSheetList;
        if ((sheets == null || sheets.Count == 0) && _shell.childCount > 0)
            sheets = _shell.ElementAt(0).styleSheetList;
        if (sheets == null || sheets.Count == 0)
        {
            Context.Log.Warn("strategy UI: the shell carries no stylesheet, vanilla panels keep their own layout");
            return;
        }
        var target = root.styleSheetList;
        if (target == null)
        {
            target = new Il2CppSystem.Collections.Generic.List<StyleSheet>();
            root.styleSheetList = target;
        }
        foreach (var sheet in sheets)
            if (sheet != null && !target.Contains(sheet))
                target.Add(sheet);
        root.IncrementVersion(VersionChangeType.StyleSheet);
    }

    private void WireShell()
    {
        // On a home screen these switch its view. From another screen they go to the home screen,
        // through the vanilla tab bar, which then opens on that view.
        Click("wm-shell-operations", () =>
        {
            if (_policyScreen != null && HomeScreens.Contains(_policyScreen))
            {
                SetHomeView(HomeView.Operations);
                return;
            }
            _arriveOnOperations = true;
            GoTo(NavigationMenu.Target.Bridge);
        });
        Click("wm-shell-back", () =>
        {
            if (_policyScreen != null && HomeScreens.Contains(_policyScreen))
                SetHomeView(HomeView.Home);
            else
                GoTo(NavigationMenu.Target.Bridge);
        });
        Click("wm-act-primary", () =>
        {
            if (_actionThroughBars)
                Context.Coroutines.Start(LaunchMission(_shell));
            else
                ClickVanilla(_actionPrimary);
        });
        Click("wm-act-secondary", () => ClickVanilla(_actionSecondary));
        Click("wm-board-close", () => SetIntel(false));
        Click("wm-board-blocker", StepBackFromIntel);
        Context.Coroutines.Start(FollowDinergate(_shell));
        foreach (var destination in Destinations)
            Click(destination.Element, () => GoTo(destination));
    }

    private void Click(string name, Action onClick)
    {
        var item = UI.Find(_shell, UiSelector.Name(name));
        item?.RegisterCallback((EventCallback<ClickEvent>)(_ =>
        {
            if (!NavigationMenu.IsOpen)
                onClick();
        }));
    }

    private readonly List<Action> _shellUndo = new();

    private void RefreshShell(VisualElement document, string screenName)
    {
        var homeScreen = HomeScreens.Contains(screenName);
        var onOperations = homeScreen && _homeView == HomeView.Operations;
        Show("wm-shell-destinations", ShowsDestinations(screenName));
        // Home is the first of the destinations, lit on the Home view.
        var home = UI.Find(_shell, UiSelector.Name("wm-shell-back"));
        if (home != null)
        {
            var onHome = homeScreen && !onOperations;
            home.EnableInClassList("wui-nav-btn--here", onHome);
            home.pickingMode = onHome ? PickingMode.Ignore : PickingMode.Position;
        }

        var screen = LookFor(screenName);
        _shellShop = ShopSystem.IsShopOpen;
        var room = Locale.Text(screen.RoomKey, screen.Room);
        var within = onOperations ? Locale.Text("WOMENACE::ui/shell/operations_title", "OPERATIONS")
            : screen.Screen != null ? Locale.Text(screen.ScreenKey, screen.Screen) : null;
        SetText("wm-shell-title", within != null ? room + "  //  " + within : room);
        RefreshActions(screenName, onOperations);

        // During an operation the button continues it, by the name mission select's header shows.
        if (screenName == VanillaUi.Screens.MissionSelect)
            _operationName = UI.Find(document, UiSelector.Name(VanillaUi.Elements.Headline1))?.TryCast<Label>()?.text;
        else if (screenName == VanillaUi.Screens.SystemMap)
            _operationName = null;
        SetText("wm-shell-operations-sub", string.IsNullOrEmpty(_operationName)
            ? Locale.Text("WOMENACE::ui/shell/select_operation", "SELECT AN OPERATION")
            : Locale.Format("WOMENACE::ui/shell/continue_operation", "CONTINUE {0}", _operationName));

        UI.Find(_shell, UiSelector.Name("wm-shell-operations"))?.EnableInClassList("wui-nav-btn--here", onOperations);
        // A destination that is not open (the Workshop's stations and Kalina's shop before the
        // Workshop unlocks, one the campaign has closed) shows dimmed.
        foreach (var destination in Destinations)
        {
            var item = UI.Find(_shell, UiSelector.Name(destination.Element));
            if (item == null)
                continue;
            var here = IsHere(destination, screenName);
            var available = here || NavigationMenu.Available(destination.Target);
            item.EnableInClassList("wui-nav-btn--off", !available);
            item.EnableInClassList("wui-nav-btn--here", here);
            item.pickingMode = available && !here ? PickingMode.Position : PickingMode.Ignore;
        }
        foreach (var (element, roomKey) in DestinationGroups)
        {
            UI.Find(_shell, UiSelector.Name(element))?.EnableInClassList("wui-nav-group--here", roomKey == screen.RoomKey);
        }
    }

    private void Show(string name, bool show)
    {
        var element = UI.Find(_shell, UiSelector.Name(name));
        if (element != null)
            element.style.display = show ? StyleKeyword.Null : DisplayStyle.None;
    }

    private void SetText(string name, string text)
    {
        var label = UI.Find(_shell, UiSelector.Name(name))?.TryCast<Label>();
        if (label != null)
            label.text = text;
    }

    private void RemoveShell()
    {
        foreach (var undo in _shellUndo)
        {
            try { undo(); }
            catch { }
        }
        _shellUndo.Clear();
        _intelPlaced.Clear();
        _inlined.Clear();
        _inlinedStyles.Clear();
        try
        {
            if (_shellRoot != null)
            {
                _shellRoot.EnableInClassList(ShellOnClass, false);
                _shellRoot.EnableInClassList(HomeViewClass, false);
                _shellRoot.EnableInClassList(OperationsViewClass, false);
                CoverVanilla(_shellRoot, false);
                _shellRoot.EnableInClassList(BarsEventClass, false);
                foreach (var caption in UI.FindAll(_shellRoot, UiSelector.Class(CaptionClass)))
                    caption.RemoveFromHierarchy();
            }
            _shell?.RemoveFromHierarchy();
            RestoreOrder();
        }
        catch { }
        _shell = null;
        _shellRoot = null;
    }

    private void ClearHome()
    {
        SetHomeLayout(false, null);
        RemoveShell();
    }

    // The vanilla F7 bug report sends the developers a report of a heavily modded game. The button
    // is hidden and the request to open the dialog, which the key shares, is dropped.
    private void HookBugReport()
    {
        var target = VanillaUi.Targets.BugReportRequestOpen;
        Context.Patches.Prefix(target.Type, target.Method, info => info.Skip = true);
    }

    // Escape opens vanilla's menu from a home screen and steps back to the home screen from the
    // others. Where the view has a way back of its own (the Operations view, an operation's
    // briefing, mission preparation) Escape takes that, and the menu is left for the Home view,
    // where there is nowhere further back to go.
    private void HookEscape()
    {
        var target = VanillaUi.Targets.OpenScreen;
        Context.Patches.Prefix(target.Type, target.Method, info =>
        {
            var name = info.Args is { Count: > 0 } ? info.Args[0] as string : null;
            if (name != Il2CppMenace.UI.Menu.TitleUIScreen.PREFAB_NAME || !EscapeUsed() || !StepBackOnEscape())
                return;
            info.Skip = true;
        });
    }

    private static bool EscapeUsed()
    {
#if JIANGYU_DEV
        if (_devEscape)
            return true;
#endif
        var key = UnityEngine.InputSystem.Keyboard.current?.escapeKey;
        return key != null && (key.wasReleasedThisFrame || key.wasPressedThisFrame || key.isPressed);
    }

    private bool StepBackOnEscape()
    {
        if (!Enabled || _shell == null || _policyScreen == null || NavigationMenu.IsOpen)
            return false;
        // The Intel board steps back on Escape by its own watch.
        if (_intelOpen)
            return true;
        Action back = null;
        if (HomeScreens.Contains(_policyScreen))
        {
            if (_homeView == HomeView.Operations)
                back = () => SetHomeView(HomeView.Home);
        }
        else if (ActionsFor(_policyScreen, false) is { SecondaryDanger: false } actions)
            back = () => ClickVanilla(actions.Secondary);
        if (back == null)
            return false;
        // Taken on the next frame: this is inside the UI manager's own handling of the key.
        Context.Coroutines.Start(NextFrame(back));
        return true;
    }

    private static System.Collections.IEnumerator NextFrame(Action action)
    {
        yield return null;
        action();
    }

    private static void HideBugReportButton()
    {
        var layers = UIManager.Get()?.GetPermanentLayersRoot();
        var button = layers != null ? UI.Find(layers, UiSelector.TypeName(VanillaUi.Elements.BugReportButton)) : null;
        if (button != null)
            button.style.display = DisplayStyle.None;
        // Vanilla's help overlay explains vanilla's layout of each screen, which is not this one.
        var help = layers != null ? UI.Find(layers, UiSelector.Name(VanillaUi.Elements.HelpButton)) : null;
        // Vanilla shows it again by its display as each screen opens, so it is hidden by its
        // visibility, which vanilla leaves alone and which also takes it out of the pointer's way.
        if (help != null)
            help.style.visibility = Visibility.Hidden;
    }
}
