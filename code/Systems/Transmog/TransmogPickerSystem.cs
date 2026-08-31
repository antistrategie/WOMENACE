using Il2CppMenace.Items;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Game.Ui.Components;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The transmog picker on a doll's unit window: a small IconSkillBar tile overlaying the armour
// slot's bottom-right corner, opening a modal (the transmog/outfit-modal UXML, styled by
// transmog.uss like the armour alternatives window) that lists the character's outfits: the
// default plus each affinity skin. Clicking an unlocked outfit makes it the rendered transmog,
// while locked ones sit greyed with their unlock level on hover. The modal is injected once per
// UnitWindow on the armoury and mission-prep screens. The tile and the modal's cards ride
// UnitWindow.SetLeader/Refresh (the equipment column is rebuilt there), so they self-heal when
// the column is rebuilt. The tile stays code-built: it anchors to the armour slot's live
// layout, outside any template subtree.
public sealed class TransmogPickerSystem : JiangyuSystem
{
    private const string TileName = "transmog-tile";
    private const string ModalName = "TransmogAlternatives";

    // The tile matches the IconSkillBar sprite ratio (261x117) and floats inside the armour
    // slot with a little padding off its bottom-right corner (more on the right, so it clears
    // the slot's border art).
    private const float TileWidth = 80f;
    private const float TileHeight = 36f;
    private const float TilePaddingRight = 8f;
    private const float TilePaddingBottom = 6f;

    // The equipment boxes show their item's rarity colour as a resting border. The common band's
    // olive (UIConfig.ColorCommonRarity, #746C4B) is the military green those boxes use.
    private static readonly Color TileBorderColour = new(116f / 255f, 108f / 255f, 75f / 255f);

    private readonly Dictionary<string, ArmorTemplate> _armorCache = new(StringComparer.Ordinal);
    private Action<VisualElement> _onAffinityChanged;

    // Dev seam: lets the bridge verbs drive the picker's Select path without a mouse.
    internal static TransmogPickerSystem Instance { get; private set; }

    public override void OnInit()
    {
        Instance = this;

        // The outfit modal, one per UnitWindow, beside the armour alternatives window it
        // mimics. Both screens host the same window, like the affinity injections.
        RegisterScreen<ArmoryUIScreen>();
        RegisterScreen<MissionPrepUIScreen>();

        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "SetLeader", OnWindowChanged);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "Refresh", OnWindowChanged);

        // A gift can unlock a skin while the modal is on screen: rebuild it so the card ungreys.
        _onAffinityChanged = window => EnsureUi(window);
        Affinity.Changed += _onAffinityChanged;
    }

    public override void OnUnload()
    {
        Instance = null;
        Affinity.Changed -= _onAffinityChanged;
        _onAffinityChanged = null;
    }

    private void RegisterScreen<TScreen>() where TScreen : Il2CppMenace.UI.UIScreen
    {
        UI.InjectEach(
            UiTarget.Screen<TScreen>()
                .Each(UiSelector.TypeName("UnitWindow"))
                .Before(UiSelector.Name("EquipmentAlternatives")),
            "transmog/outfit-modal",
            WireModal);
    }

    // Start hidden and wire the outside-click dismiss. The injection binds once, and the panel
    // may not be attached yet at bind time, so the actual hookup is EnsureDismissHooked, which
    // EnsureUi retries on later passes once the modal is on a panel.
    //
    // Also build the tile here: on the first squad-menu open the window's SetLeader can run
    // before this injection lands, so that OnWindowChanged pass bails at modal == null and no
    // tile appears until a later SetLeader (e.g. selecting another doll). Running EnsureUi once
    // the modal is in the tree heals that first-open case; the leader is already set by then.
    private void WireModal(VisualElement root, VisualElement window)
    {
        root.SetVisible(false);
        EnsureDismissHooked(root, window);
        EnsureUi(window);
    }

    // Hook the modal's outside-click dismiss once it is on a panel, guarded so it runs a single
    // time per modal (the marker is only set on the success path, so a bind before the modal is
    // laid out heals on the next EnsureUi). The tile is kept open-on-click since it toggles.
    private void EnsureDismissHooked(VisualElement modal, VisualElement window)
    {
        if (modal == null || modal.panel == null || modal.ClassListContains("wm-dismiss-hooked"))
            return;
        modal.AddToClassList("wm-dismiss-hooked");
        var dismissable = modal;
        var dismissWindow = window;
        UI.CloseOnOutsideClick(dismissable, () =>
        {
            dismissable.SetVisible(false);
            SyncTileHighlight(dismissWindow);
        }, TileName);
    }

    // Run the exact path an outfit-card click runs, against the UnitWindow bound to this
    // character (not merely the first on screen: mission prep hosts several).
    internal bool DevSelect(string characterTag, string armorId)
    {
        var root = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.GetRootElement();
        if (root == null)
            return false;
        foreach (var window in UI.FindAll(root, UiSelector.TypeName("UnitWindow")))
        {
            if (Affinity.CharacterTag(Affinity.LeaderOf(window)) != characterTag)
                continue;
            Select(window, characterTag, armorId);
            return true;
        }
        return false;
    }

    private void OnWindowChanged(PatchInfo info)
    {
        if (info.Instance is VisualElement window)
            EnsureUi(window);
    }

    // The injected modal's toggle target: the injection's wrapper around the UXML root, the
    // same element WireModal binds (showing only the inner root would leave a hidden wrapper).
    private static VisualElement ModalRoot(VisualElement window)
    {
        var inner = UI.Find(window, UiSelector.Name(ModalName));
        return inner?.parent ?? inner;
    }

    private void EnsureUi(VisualElement window)
    {
        try
        {
            var leader = Affinity.LeaderOf(window);
            var characterTag = Affinity.CharacterTag(leader);

            var slot = FindArmourSlot(window);
            var modal = ModalRoot(window);
            var tile = UI.Find(window, UiSelector.Name(TileName));

            // Not a doll (or nothing to anchor on / no modal on this screen): nothing to show.
            if (characterTag == null || Transmog.DefaultFor(characterTag) == null || slot == null || modal == null)
            {
                tile?.SetVisible(false);
                modal?.SetVisible(false);
                return;
            }

            // The equipment column is rebuilt across refreshes, so (re)create the tile in
            // place. It is a SIBLING of the armour slot, floated over its bottom-right corner:
            // a child of the slot would re-trigger the slot's own click handling (the game
            // resolves the clicked InteractiveElement from ancestry), opening the armour
            // dropdown too.
            if (tile == null || tile.parent != slot.parent)
            {
                tile?.RemoveFromHierarchy();
                tile = BuildTile(window);
                slot.parent.Add(tile);
            }
            PositionTile(tile, slot);
            EnsureDismissHooked(modal, window);

            tile.SetVisible(true);
            UpdateTile(tile, characterTag);
            RebuildCards(window, characterTag);
            SyncTileHighlight(window);
        }
        catch (Exception ex) { Context.Log.Warn($"transmog picker: ui update failed: {ex.Message}"); }
    }

    // The equipped-armour EquipmentSlot in the window's loadout column. The alternatives list
    // reuses the same element type for its entries, so anything under EquipmentAlternatives is
    // skipped.
    private static VisualElement FindArmourSlot(VisualElement window)
    {
        foreach (var element in UI.FindAll(window, UiSelector.TypeName("EquipmentSlot")))
        {
            var slot = element.TryCast<EquipmentSlot>();
            if (slot == null || slot.GetItemSlotType() != ItemSlot.InfantryArmor)
                continue;
            if (!InsideAlternatives(element, window))
                return element;
        }
        return null;
    }

    private static bool InsideAlternatives(VisualElement element, VisualElement window)
    {
        for (var parent = element.parent; parent != null && parent != window; parent = parent.parent)
            if (parent.name == "EquipmentAlternatives")
                return true;
        return false;
    }

    // Absolute fill, matching how the vanilla slots layer their Border/Selected overlays.
    private static void Fill(VisualElement element)
    {
        element.style.position = new StyleEnum<Position>(Position.Absolute);
        element.style.left = new StyleLength(0f);
        element.style.top = new StyleLength(0f);
        element.style.right = new StyleLength(0f);
        element.style.bottom = new StyleLength(0f);
    }

    // Float the tile over the slot's bottom-right corner (both share a parent, so the slot's
    // layout rect is in the tile's coordinate space). The slot's layout is unresolved right
    // after a rebuild, so a geometry callback on the slot re-anchors once it lands.
    private void PositionTile(VisualElement tile, VisualElement slot)
    {
        Reposition(tile, slot);
        if (slot.ClassListContains("wm-transmog-anchor"))
            return;
        slot.AddToClassList("wm-transmog-anchor");
        var host = slot.parent;
        slot.RegisterCallback(
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<EventCallback<GeometryChangedEvent>>(
                (Action<GeometryChangedEvent>)(_ =>
                {
                    var current = host != null ? UI.Find(host, UiSelector.Name(TileName)) : null;
                    if (current != null)
                        Reposition(current, slot);
                })));
    }

    private static void Reposition(VisualElement tile, VisualElement slot)
    {
        var rect = slot.layout;
        if (float.IsNaN(rect.x) || float.IsNaN(rect.width))
            return;
        tile.style.left = new StyleLength(rect.xMax - TileWidth - TilePaddingRight);
        tile.style.top = new StyleLength(rect.yMax - TileHeight - TilePaddingBottom);
    }

    // The tile wears the gold selected border (like the armour slot does for its own dropdown)
    // exactly while the outfit modal is open.
    private void SyncTileHighlight(VisualElement window)
    {
        try
        {
            var tile = UI.Find(window, UiSelector.Name(TileName));
            if (tile == null)
                return;
            var open = ModalRoot(window)?.IsVisible() ?? false;
            UI.Find(tile, UiSelector.Name("Selected"))?.SetVisible(open);
        }
        catch { }
    }

    // The corner tile: the current outfit's skill-bar art on a translucent black card with the
    // equipment boxes' resting military-green border. Code-built (its anchor and sprite are
    // dynamic), so its few styles live here rather than in transmog.uss.
    private VisualElement BuildTile(VisualElement window)
    {
        var button = new Button { name = TileName, focusable = false };
        button.style.position = new StyleEnum<Position>(Position.Absolute);
        button.style.width = new StyleLength(TileWidth);
        button.style.height = new StyleLength(TileHeight);
        button.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.94f));
        var borderColour = new StyleColor(TileBorderColour);
        button.style.borderLeftColor = borderColour;
        button.style.borderRightColor = borderColour;
        button.style.borderTopColor = borderColour;
        button.style.borderBottomColor = borderColour;
        var borderWidth = new StyleFloat(1f);
        button.style.borderLeftWidth = borderWidth;
        button.style.borderRightWidth = borderWidth;
        button.style.borderTopWidth = borderWidth;
        button.style.borderBottomWidth = borderWidth;

        // The gold highlight the armour slot shows while its own dropdown is open, shown here
        // while the outfit modal is open.
        var selected = new VisualElement { name = "Selected", pickingMode = PickingMode.Ignore };
        selected.AddToClassList("slot-selected-border");
        Fill(selected);
        selected.SetVisible(false);
        button.Add(selected);

        button.clickable.clicked += (Action)(() =>
        {
            try
            {
                Sound.Click();
                var modal = ModalRoot(window);
                if (modal == null)
                    return;
                modal.SetVisible(!modal.IsVisible());
                SyncTileHighlight(window);
            }
            catch (Exception ex) { Context.Log.Warn($"transmog picker: toggle failed: {ex.Message}"); }
        });

        Tooltip.OnHover(button, () =>
        {
            var characterTag = Affinity.CharacterTag(Affinity.LeaderOf(window));
            if (characterTag == null)
                return null;
            var template = OutfitTemplate(Transmog.SelectionFor(Context, characterTag));
            var tooltip = new Tooltip("wm-transmog", 230)
                .Subheading(Locale.Text("WOMENACE::ui/transmog", "OUTFIT"))
                .Line()
                .Paragraph(OutfitName(template));
            var description = OutfitDescription(template);
            if (!string.IsNullOrEmpty(description))
                tooltip.Paragraph(description, Tooltip.Style.Disabled);
            return tooltip;
        });
        return button;
    }

    private void UpdateTile(VisualElement tile, string characterTag)
    {
        var icon = OutfitTemplate(Transmog.SelectionFor(Context, characterTag))?.IconSkillBar;
        if (icon != null)
            tile.style.backgroundImage = new StyleBackground(icon);
    }

    // One card per outfit into the modal's list, in the armour alternatives' card style: item
    // art, slot border, the selected border on the current choice, name and short name. Cards
    // are per character, so they are built here, but all their styling lives in transmog.uss.
    private void RebuildCards(VisualElement window, string characterTag)
    {
        var list = UI.Find(window, UiSelector.Name("outfit-list"));
        if (list == null)
            return;
        list.Clear();

        var level = Affinity.LevelFor(Context, Affinity.KeyForTag(characterTag));
        var selection = Transmog.SelectionFor(Context, characterTag);

        foreach (var option in Transmog.OptionsFor(characterTag))
        {
            var template = OutfitTemplate(option.ArmorId);
            if (template == null)
                continue;
            var unlocked = option.UnlockLevel <= level;
            list.Add(BuildCard(window, characterTag, template, option, unlocked, option.ArmorId == selection));
        }
    }

    private VisualElement BuildCard(
        VisualElement window, string characterTag, ArmorTemplate template, Transmog.Option option, bool unlocked, bool selected)
    {
        var card = new Button { name = "transmog-option", focusable = false };
        card.AddToClassList("unit-equipment-slot");
        card.AddToClassList("wm-outfit-card");
        if (!unlocked)
            card.AddToClassList("wm-outfit-card-locked");

        var image = new VisualElement { name = "Image", pickingMode = PickingMode.Ignore };
        image.AddToClassList("wm-fill");
        var art = template.IconEquipment;
        if (art != null)
            image.style.backgroundImage = new StyleBackground(art);
        card.Add(image);

        var border = new VisualElement { name = "Border", pickingMode = PickingMode.Ignore };
        border.AddToClassList("unit-equipment-slot-border");
        border.AddToClassList("wm-fill");
        card.Add(border);

        var selectedBorder = new VisualElement { name = "Selected", pickingMode = PickingMode.Ignore };
        selectedBorder.AddToClassList("slot-selected-border");
        selectedBorder.AddToClassList("wm-fill");
        selectedBorder.SetVisible(selected);
        card.Add(selectedBorder);

        var name = new Label(OutfitName(template)) { name = "ItemName", pickingMode = PickingMode.Ignore };
        name.AddToClassList("wm-outfit-card-name");
        card.Add(name);

        var shortName = new Label(OutfitShortName(template)) { name = "ShortItemName", pickingMode = PickingMode.Ignore };
        shortName.AddToClassList("wm-outfit-card-shortname");
        card.Add(shortName);

        var armorId = option.ArmorId;
        var unlockLevel = option.UnlockLevel;
        card.clickable.clicked += (Action)(() =>
        {
            if (!unlocked)
            {
                Sound.RightClick();
                return;
            }
            Sound.Click();
            Select(window, characterTag, armorId);
        });

        Tooltip.OnHover(card, () =>
        {
            var tooltip = new Tooltip("wm-transmog", 230)
                .Subheading(OutfitName(template));
            var description = OutfitDescription(template);
            if (!string.IsNullOrEmpty(description))
                tooltip.Line().Paragraph(description);
            if (!unlocked)
                tooltip.Line().Paragraph(
                    Locale.Format("WOMENACE::ui/transmog_locked", "Unlocks at affinity level {0}", unlockLevel),
                    Tooltip.Style.Disabled);
            return tooltip;
        });

        return card;
    }

    // Apply a selection and re-run SetLeader on the window: the equipment column, the tile, and
    // the modal's cards all rebuild off it. The rebuild is deferred a frame: it would destroy
    // the card whose click event is still being dispatched.
    private void Select(VisualElement window, string characterTag, string armorId)
    {
        try
        {
            Transmog.SetSelection(Context, characterTag, armorId);
            Context.Log.Info($"transmog: '{characterTag}' now renders '{armorId}'");
            Context.Coroutines.Start(RefreshWindowNextFrame(window));
        }
        catch (Exception ex) { Context.Log.Warn($"transmog picker: select failed: {ex.Message}"); }
    }

    private System.Collections.IEnumerator RefreshWindowNextFrame(VisualElement window)
    {
        yield return null;
        var unitWindow = window.TryCast<UnitWindow>();
        var leader = unitWindow?.m_CurrentLeader;
        if (unitWindow == null || leader == null || !leader.IsAlive())
            yield break;
        unitWindow.SetLeader(leader);
        // Picking an outfit closes the modal. SetLeader re-wires it hidden anyway; keep it that
        // way and clear the tile's open highlight.
        ModalRoot(window)?.SetVisible(false);
        SyncTileHighlight(window);
        // The armoury's 3D squad preview does not rebuild off the window. Equipping armour
        // rebuilds it through the item container's visual-alteration event (the armoury's unit
        // selector subscribes to the selected unit's container), so raise the same event for
        // the equipped armour item and let the vanilla handler respawn the stage.
        try
        {
            var container = leader.GetItems();
            var item = container?.GetItemAtSlot(ItemSlot.InfantryArmor);
            container?.OnVisualAlterationChanged?.Invoke(container.GetOwner(), item);
        }
        catch (Exception ex) { Context.Log.Warn($"transmog picker: preview refresh failed: {ex.Message}"); }
    }

    private ArmorTemplate OutfitTemplate(string armorId)
        => Templates.Resolve<ArmorTemplate>(armorId, _armorCache, msg => Context.Log.Warn($"transmog picker: {msg}"));

    private static string OutfitName(ArmorTemplate template) => Templates.DefaultText(template?.Title, template?.name ?? "?");

    private static string OutfitShortName(ArmorTemplate template) => Templates.DefaultText(template?.ShortName);

    private static string OutfitDescription(ArmorTemplate template) => Templates.DefaultText(template?.Description);
}
