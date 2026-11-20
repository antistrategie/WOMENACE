using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Every vanilla name the strategy code looks up: screen types, UXML element names and the
// string-registered patch targets. A MENACE update that renames one shows up in two places: the
// boot self-check lists patch targets that no longer resolve, and Require reports a screen whose
// required elements are gone, after which that screen is left vanilla. A name that is not passed
// to Require, and any name shell.uss selects by, is not reported: what it styles or places stays
// as vanilla has it.
internal static class VanillaUi
{
    internal static class Screens
    {
        internal const string MissionSelect = "MissionSelectUIScreen";
        internal const string SystemMap = "SystemMapUIScreen";
        internal const string OperationSelect = "OperationSelectUIScreen";
        internal const string OperationResult = "OperationResultUIScreen";
        internal const string Hiring = "HiringUIScreen";
        internal const string BlackMarket = "BlackMarketUIScreen";
        internal const string Armory = "ArmoryUIScreen";
        internal const string MissionPrep = "MissionPrepUIScreen";
        internal const string ShipUpgrades = "ShipUpgradesUIScreen";
        internal const string Workshop = "WorkshopUIScreen";
    }

    internal static class Elements
    {
        // A screen document's own root, under which the screen's panels sit.
        internal const string ScreenRootClass = "screen-root";
        // The tab bar, by the class vanilla's rules know it by.
        internal const string TabBarClass = "strategy-navigation";
        // Mission preparation's header, which that screen shows by its own inline style.
        internal const string ScreenHeader = "ScreenHeader";
        // The tab bar (StrategyNavigation) and its parts.
        internal const string TabBarButtons = "Buttons";
        internal const string TabBarResources = "Resources";
        internal const string PromotionPoints = "PromotionPoints";
        internal const string OciPoints = "OCIPoints";
        internal const string Authority = "Authority";
        internal const string Squaddies = "Squaddies";
        // The screen header's operation name.
        internal const string Headline1 = "Headline1";
        // Home screen panels.
        internal const string MissionWindow = "MissionWindow";
        internal const string MissionPois = "MissionPois";
        // The selected mission window's two parts.
        internal const string MissionWindowHeader = "Header";
        internal const string MissionWindowBody = "MainPanel";
        internal const string SelectedMissionCategory = "SelectedMissionCategory";
        // The operation strip's displays, by type, the progress display first, and their captions.
        internal static readonly string[] OperationDisplays = { "OperationProgressDisplay", "FactionPlanetDisplay", "LightConditionDisplay", "ActiveAssetsDisplay" };
        internal const string OperationPlanetDisplay = "FactionPlanetDisplay";
        internal const string LightConditionIcons = "LightConditions";
        internal const string OperationFactionIcon = "FactionIcon";
        // A plain row inside a vanilla display.
        internal const string Row = "Row";
        internal static readonly string[] OperationCaptions = { "OperationProgressLabel", "LightConditionLabel" };
        // The operation strip's progress bar.
        internal const string OperationProgressBar = "ProgressBar";
        // A mission's marker on that board, by type.
        internal const string MissionPoi = "MissionPoi";
        // The messages panel, a message in it and the story faction list, by type.
        internal const string MessagesPanel = "EventMessagesPanel";
        internal const string MessageElement = "EventMessageElement";
        internal const string StoryFactionList = "StoryFactionList";
        // Their inner containers and headings, by name.
        internal const string MessagesInner = "EventMessagesPanel";
        internal const string MessagesHeader = "Header";
        internal const string StoryFactionsInner = "StoryFactions";
        internal const string StoryFactionsHeadline = "FactionsHeadline";
        // The permanent layers' bug report button, by type.
        internal const string BugReportButton = "BugReportButton";
        internal const string HelpButton = "OnboardingOverlayButton";
        // A story event's dialog, by type, and the parts of it that reach furthest left, by name:
        // its window, the stage picture behind it, and the left speaker's picture with the mask
        // that clips it.
        internal const string EventDialog = "EventDialog";
        internal const string EventWindow = "Window";
        // The tab bar's outer box and a resource display's amount.
        internal const string TabBarContainer = "Container";
        internal const string ResourceAmount = "Amount";
        // Home screen panels and the buttons and labels the bottom bar's actions press and read.
        internal const string TopRightCorner = "TopRightCorner";
        internal const string PlanMissionButton = "PlanMissionButton";
        internal const string AbortButton = "AbortButton";
        internal const string MissionName = "MissionName";
        internal const string Hud = "HUD";
        // The operation briefing.
        internal const string OperationDescriptionPanel = "OperationDescriptionPanel";
        internal const string Planet = "Planet";
        internal const string ButtonContainer = "ButtonContainer";
        internal const string StartOperationButton = "StartOperationButton";
        internal const string CloseButton = "CloseButton";
        internal const string OperationNameRow = "OperationNameRow";
        internal const string PlanetHeader = "PlanetHeader";
        internal const string PlanetTags = "PlanetTags";
        internal const string PlanetMoodHeader = "PlanetMoodHeader";
        internal const string MoodLines = "Lines";
        internal const string OperationWindow = "OperationWindow";
        internal const string WindowBackground = "WindowBackground";
        internal const string PlanetMoodImage = "PlanetMoodImage";
        internal const string PlanetFactionIcon = "PlanetFactionIcon";
        internal const string MenacePresenceBoxes = "MenacePresenceBoxes";
        internal const string MenacePresenceValue = "MenacePresenceValue";
        internal const string GoalDescription = "GoalDescription";
        internal const string OperationIntro = "OperationIntro";
        internal const string TextContainer = "TextContainer";
        internal const string OperationIntroContainer = "OperationIntroContainer";
        internal const string ClientRepImage = "ClientRepImage";
        internal const string ClientRepName = "ClientRepName";
        internal const string ClientRepTitle = "ClientRepTitle";
        internal const string TrustChanges = "TrustChanges";
        // Vanilla's drawn frame round a window, by the name of its template's instance.
        internal const string WindowBorder = "border_new";
        // Mission preparation.
        internal const string MapColumn = "MapColumn";
        internal const string PrepPreview = "Preview";
        internal const string PrepCoordinates = "CoordinatesLabel";
        // The flat map's layers, each the map's size: objective areas, the squads placed, enemy
        // contacts and objective markers, and the parts of each marker.
        internal const string PrepObjectiveAreas = "ObjectiveAreas";
        // The flat map's frame, its picture of the ground (which holds a box for each structure)
        // and its deployment zones' boxes.
        internal const string PrepMapBorder = "MapBorder";
        internal const string PrepRects = "Rects";
        internal const string PrepDeploymentZones = "DeploymentZones";
        internal const string PrepDeployed = "Deployed";
        internal const string PrepDeployedImage = "SmallImage";
        internal const string PrepUnitPois = "UnitPois";
        internal const string PrepUnitIcon = "UnitIcon";
        internal const string PrepUnitText = "UnitText";
        internal const string PrepObjectivePois = "ObjectivePois";
        internal const string PrepObjectiveIcon = "ObjectiveIcon";
        internal const string PrepObjectiveLabel = "ObjectiveLabel";
        internal const string PrepInfoOverlay = "MissionPrepInfoOverlay";
        internal const string PrepInfoContainer = "Container";
        internal const string PrepInfoBorder = "Border";
        internal const string PrepInfoFold = "FoldButton";
        internal const string PrepDetailsHeadline = "DetailsHeadline";
        internal const string PrepDetails = "Details";
        internal const string PrepObjectivesHeadline = "ObjectivesHeadline";
        internal const string PrepObjectives = "Objectives";
        internal const string PrepObjectiveMark = "Icon";
        internal const string PrepUnitSlot = "MissionPrepUnitSelectSlot";
        internal const string PrepSelectedColumn = "SelectedUnitColumn";
        internal const string PrepAbilitiesPanel = "OffmapAbilitiesPanel";
        internal const string PrepAbilities = "OffmapAbilities";
        internal const string PrepAbilityUses = "UsesLabel";
        internal const string UnitsContainer = "UnitsContainer";
        internal const string StartMissionButton = "StartMissionButton";
        internal const string SuppliesPanel = "SuppliesPanel";
        // The loadout box's parts.
        internal const string SuppliesIcon = "SuppliesIcon";
        internal const string SuppliesLoadout = "LoadoutLabel";
        internal const string SuppliesCaption = "SuppliesLabel";
        internal const string SuppliesValue = "SuppliesValue";
        internal const string SuppliesDelta = "SuppliesDelta";
        internal const string SuppliesBarBacking = "SuppliesBarBackground";
        internal const string SuppliesBar = "SuppliesBar";
        internal const string SuppliesSelected = "SelectedValue";
        internal const string SuppliesStripes = "SuppliesStripes";
        // The side screens' windows. UnitWindow is a type.
        internal const string UnitsScrollView = "UnitsScrollView";
        // The armoury's roster: the living squads' holder (UnitsContainer) and the dead squads'.
        internal const string DeadUnitsContainer = "PermanentlyDeadUnitsContainer";
        // The unit window's frame and its drawn backing and border, and its header's labels.
        internal const string UnitWindowFrame = "Window";
        internal const string UnitWindowBackground = "Background";
        internal const string UnitWindowBorder = "Border";
        internal const string LeaderRank = "LeaderRank";
        internal const string LeaderName = "LeaderName";
        internal const string RankIcon = "RankIcon";
        internal const string UnitTypeIcon = "UnitTypeIcon";
        // The unit window's header, its deployment button (a type's instance name) and statistics
        // button, and the equipment column. The squad size control in its first row is Squaddies.
        internal const string UnitWindowHeader = "Header";
        internal const string UnitWindowButton = "UnitWindowButton";
        internal const string StatisticsButton = "StatisticsButton";
        internal const string EquipmentColumn = "EquipmentColumn";
        // The loadout's slots and their holder, by type.
        internal const string EquipmentSlots = "EquipmentSlotsContainer";
        internal const string StatsColumn = "StatsColumn";
        // An equipment slot's label ("SQUAD WEAPON X5").
        internal const string SlotHeaderText = "SlotHeaderText";
        // The unit window's equipment picker, its title and its two small labels.
        internal const string EquipmentPicker = "EquipmentAlternatives";
        internal const string PickerTitle = "ItemTypeLabel";
        internal const string PickerFilterLabel = "FilterLabel";
        internal const string PickerSortLabel = "SortLabel";
        // An equipment slot's item name and kind, the squad type row's two labels and squad size,
        // and the accessories' count (a type).
        internal const string ItemName = "ItemName";
        internal const string ShortItemName = "ShortItemName";
        internal const string UnitTypeHeadline = "UnitTypeHeadline";
        internal const string UnitTypeName = "UnitTypeName";
        internal const string SquaddiesCount = "SquaddiesCountLabel";
        // The squad type row's rule and its icon's box, and a slot's label holder (a class).
        internal const string TypeRowLine = "Line";
        internal const string UnitTypeIconBig = "UnitTypeIconBig";
        internal const string SlotHeaderClass = "unit-equipment-slot-header";
        internal const string AccessoriesCount = "EquipmentAccessoriesCount";
        // The four stats over the portrait, and a stat's number and icon.
        internal const string AdditionalStats = "AdditionalStats";
        internal const string StatValue = "Value";
        internal const string StatIcon = "Icon";
        // The unit window's portrait (clipped by its mask) and the bars and states drawn over it.
        internal const string LeaderMask = "LeaderMask";
        internal const string LeaderElements = "LeaderElements";
        internal const string UnitName = "UnitName";
        internal const string UnitWindow = "UnitWindow";
        internal const string HiringWindow = "HiringWindow";
        // The hiring window's columns (who is hired, who can be, the leader's info and her
        // portrait), its Hire button and a slot in its lists (a type).
        internal const string HiredColumn = "HiredColumn";
        internal const string HirableColumn = "HirableColumn";
        internal const string UnitInfoColumn = "UnitInfoColumn";
        internal const string UnitLeaderColumn = "UnitLeaderColumn";
        internal const string HireButton = "HireButton";
        // The hiring info's unit badge, and what a candidate costs (on her portrait and on her tile).
        internal const string UnitBadge = "Badge";
        internal const string HiringCosts = "HiringCosts";
        // The hiring info's holder and description, and the box her portrait stands in.
        internal const string Container = "Container";
        internal const string Description = "Description";
        internal const string LeaderBackground = "LeaderBackground";
        // The hiring window's perk tree, its stats panel (a type) with its tabs, the leader's
        // first perk, and a slot's two bars.
        internal const string PerkTree = "Perks";
        internal const string StatsPanel = "UnitStatsAndAttributesPanel";
        internal const string StatsTabs = "TabsHeader";
        internal const string InitialPerk = "InitialPerk";
        internal const string ArmorBar = "ArmorBar";
        internal const string HealthBar = "HealthBar";
        internal const string HirableScrollView = "HirableScrollView";
        internal const string HiredScrollView = "HiredScrollView";
        internal const string HiringUnitSlot = "HiringUnitSlot";
        // On a hiring slot: the unit badge's holder and the stars of her quality.
        internal const string SlotOverlay = "Overlay";
        internal const string QualityLevel = "QualityLevel";
        // A hiring slot's mark of being chosen and its wash for a leader too dear to hire.
        internal const string SlotSelected = "Selected";
        internal const string TooExpensiveOverlay = "TooExpensiveOverlay";
        // A hiring slot's picture and the two marks of her kind, and a text button's word.
        internal const string SlotPortrait = "Portrait";
        internal const string InfantryIcon = "InfantryIcon";
        internal const string VehicleIcon = "VehicleIcon";
        internal const string ButtonText = "Text";
        internal const string Panels = "Panels";
        // The black market's three panels, and its balance's picture and number.
        internal const string MarketPlayerPanel = "PlayerPanel";
        internal const string MarketTradePanel = "TradePanel";
        // The black market's shadow up from the foot of the screen, as wide as the root.
        internal const string MarketShadow = "Shadow";
        internal const string MarketMerchantPanel = "MerchantPanel";
        internal const string MarketBalanceIcon = "BalanceIcon";
        internal const string MarketBalanceValue = "BalanceValue";
        internal const string MarketPlayerOffer = "PlayerOffer";
        internal const string MarketTradeButton = "TradeButton";
        internal const string ShipInfoWindow = "ShipInfoWindow";
        // The ship upgrades screen's holder of its module slots.
        internal const string Slots = "Slots";
        // The ship window's name of the ship, and the box a module slot's name stands in.
        internal const string ShipName = "ShipName";
        internal const string SlotLabelContainer = "LabelContainer";
        // Inside the messages panel: its scroll view, the rows' holders and a row's parts.
        internal const string EventsScrollView = "EventsScrollView";
        internal const string SlotsContainer = "SlotsContainer";
        internal const string SlotsScrollViewRow = "SlotsScrollViewRow";
        internal const string Pickable = "Pickable";
        internal const string SenderLabel = "SenderLabel";
        internal const string MessageLabel = "MessageLabel";
        internal const string DeleteButton = "DeleteButton";
        // Inside the story faction list: the cards' holder, a card and the faction window, the last two by type.
        internal const string FactionsContainer = "FactionsContainer";
        internal const string StoryFactionSlot = "StoryFactionSlot";
        internal const string StoryFactionWindow = "StoryFactionWindow";
        // An operation's marker in the system map's scene, by the start of its object's name.
        internal const string OperationMarkerPrefix = "operation_hud";
        // A planet's name on the system map, by type.
        internal const string PlanetHud = "PlanetHUD";
        // A window's backing, by class, and a dialog's confirm button.
        internal const string WindowBackgroundClass = "window-background";
        internal const string YesButton = "YesButton";
    }

    internal readonly record struct Target(string Type, string Method);

    internal static class Targets
    {
        internal static readonly Target UpdateActiveCamera = new("Il2CppMenace.UI.UIManager", "UpdateActiveCamera");
        internal static readonly Target UpdateBackgroundScene = new("Il2CppMenace.UI.UIManager", "UpdateBackgroundScene");
        internal static readonly Target BugReportRequestOpen = new("Il2CppMenace.UI.BugReportDialog", "RequestOpen");
        internal static readonly Target OpenScreen = new("Il2CppMenace.UI.UIManager", "OpenScreen");

        internal static readonly Target HighlightMandatoryEvents = new("Il2CppMenace.UI.Strategy.EventMessagesPanel", "HighlightUnfinishedMandatoryEvents");

        internal static readonly Target FactionSlotClicked = new("Il2CppMenace.UI.Strategy.StoryFactionList", "OnFactionSlotClicked");

        internal const string UnitWindowType = "Il2CppMenace.UI.Strategy.UnitWindow";
        internal static readonly Target UnitWindowSetLeader = new(UnitWindowType, "SetLeader");
        internal static readonly Target UnitWindowRefresh = new(UnitWindowType, "Refresh");

        internal static readonly Target EquipmentAlternativePicked = new("Il2CppMenace.UI.Strategy.UnitWindowEquipment", "OnAlternativeEquipmentItemClickedAction");

        internal static readonly Target MissionBoardInit = new("Il2CppMenace.UI.Strategy.MissionPoisContainer", "Init");

        internal static readonly Target SystemMapOpened = new("Il2CppMenace.UI.Strategy.SystemMapUIScreen", "OnOpened");

        internal static readonly Target HiringUpdateWindow = new("Il2CppMenace.UI.Strategy.HiringUIScreen", "UpdateWindow");
        internal static readonly Target HiringSlotClicked = new("Il2CppMenace.UI.Strategy.HiringUIScreen", "OnUnitSlotClicked");

        internal static readonly Target PrepUpdatePreview = new("Il2CppMenace.UI.Strategy.MissionPrepUIScreen", "UpdatePreview");
        internal static readonly Target PrepUpdateZones = new("Il2CppMenace.UI.Strategy.MissionPrepUIScreen", "UpdateDeploymentZones");

        internal static readonly Target ArmoryPressEnd = new("Il2CppMenace.UI.Strategy.ArmoryUIScreen", "OnPressEnd");

        internal static readonly Target[] All = { ArmoryPressEnd, PrepUpdatePreview, PrepUpdateZones, UpdateActiveCamera, UpdateBackgroundScene, BugReportRequestOpen, OpenScreen, HighlightMandatoryEvents, FactionSlotClicked, MissionBoardInit, UnitWindowSetLeader, UnitWindowRefresh, EquipmentAlternativePicked, SystemMapOpened, HiringUpdateWindow, HiringSlotClicked };
    }

    // Resolves every patch target against the loaded game assemblies and logs the ones that are gone.
    internal static void SelfCheck(ModContext context)
    {
        var missing = new List<string>();
        foreach (var target in Targets.All)
            if (!Resolves(target))
                missing.Add($"{target.Type}.{target.Method}");
        if (missing.Count == 0)
            context.Log.Info($"strategy UI self-check: {Targets.All.Length} patch targets resolve");
        else
            context.Log.Warn($"strategy UI self-check: {missing.Count} of {Targets.All.Length} patch targets are missing: {string.Join(", ", missing)}");
    }

    private static bool Resolves(Target target)
    {
        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try { type = assembly.GetType(target.Type, false); }
            catch { continue; }
            if (type != null)
                return type.GetMethods(all).Any(m => m.Name == target.Method);
        }
        return false;
    }

    private static readonly HashSet<string> Reported = new();

    // Whether a screen still has every named element. A screen that does not is reported once.
    internal static bool Require(ModContext context, string screen, VisualElement root, params string[] names)
    {
        var missing = names.Where(n => UI.Find(root, UiSelector.Name(n)) == null).ToList();
        if (missing.Count == 0)
            return true;
        if (Reported.Add(screen + ":" + string.Join(",", missing)))
            context.Log.Warn($"strategy UI: {screen} has no {string.Join(", ", missing)}, the screen stays vanilla");
        return false;
    }
}
