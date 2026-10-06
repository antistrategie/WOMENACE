using Il2CppInterop.Runtime;
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

// Outfits and weapon skins share one picker per UnitWindow. Their choices and eligibility
// come from their own models, while tiles, cards, dismissal and preview refresh are common.
// The outfit picker dresses the squad leader on left click and her dummy links (squaddies) on
// right click, and each click changes only its own side. Each card carries a badge for
// whichever of the two wears it. Dummy links render the leader's outfit until either side is
// first changed, and solo dolls have none, so right click does nothing for them.
public sealed class TransmogPickerSystem : JiangyuSystem
{
    private const string ModalName = "AppearanceAlternatives";
    private readonly Dictionary<string, ArmorTemplate> _armorCache = new(StringComparer.Ordinal);
    private Action<VisualElement> _onAffinityChanged;

    // Set while a pick refreshes its own window, so that refresh keeps the picker open for the
    // next pick. Any other window refresh still closes it.
    private bool _keepOpen;
    internal static TransmogPickerSystem Instance { get; private set; }

    private sealed class Choice(string id, string name, string subtitle, StyleBackground art,
        bool unlocked = true, string description = null, string lockedMessage = null)
    {
        public readonly string Id = id, Name = name, Subtitle = subtitle;
        public readonly StyleBackground Art = art;
        public readonly bool Unlocked = unlocked;
        public readonly string Description = description, LockedMessage = lockedMessage;
    }

    private sealed class Appearance(string title, string heading, string selection, StyleBackground tileArt,
        string imageClass, IReadOnlyList<Choice> choices, string squadSelection = null, bool hasSquad = false)
    {
        public readonly string Title = title, Heading = heading, Selection = selection, ImageClass = imageClass;
        public readonly string SquadSelection = squadSelection;
        public readonly bool HasSquad = hasSquad;
        public readonly StyleBackground TileArt = tileArt;
        public readonly IReadOnlyList<Choice> Choices = choices;
    }

    public override void OnInit()
    {
        Instance = this;
        RegisterScreen<ArmoryUIScreen>();
        RegisterScreen<MissionPrepUIScreen>();
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "SetLeader", OnWindowChanged);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "Refresh", OnWindowChanged);
        _onAffinityChanged = window => EnsureUi(window, close: false);
        Affinity.Changed += _onAffinityChanged;
    }

    public override void OnUnload()
    {
        Instance = null;
        Affinity.Changed -= _onAffinityChanged;
    }

    private void RegisterScreen<TScreen>() where TScreen : Il2CppMenace.UI.UIScreen
    {
        UI.InjectEach(UiTarget.Screen<TScreen>().Each(UiSelector.TypeName("UnitWindow"))
            .Before(UiSelector.Name("EquipmentAlternatives")), "transmog/appearance-modal", (root, window) =>
        {
            root.SetVisible(false);
            EnsureUi(window);
        });
    }

    private static VisualElement ModalRoot(VisualElement window)
    {
        var inner = UI.Find(window, UiSelector.Name(ModalName));
        return inner?.parent ?? inner;
    }

    private static string TileName(ItemSlot slot)
        => slot == ItemSlot.InfantryArmor ? "transmog-tile" : "wm-weapon-skin-" + slot;

    private void OnWindowChanged(PatchInfo info)
    {
        if (info.Instance is VisualElement window)
            EnsureUi(window, close: !_keepOpen);
    }

    private void EnsureUi(VisualElement window, bool close = true)
    {
        try
        {
            if (close)
                AppearanceSlotUi.ClosePickers(window);
            foreach (var tile in UI.FindAll(window, UiSelector.Class("wm-appearance-tile")))
                tile.SetVisible(false);
            var modal = ModalRoot(window);
            var leader = Affinity.LeaderOf(window);
            if (modal == null || Affinity.KeyFor(leader) == 0)
                return;

            // Slot names are known even when a compatible weapon is not equipped yet.
            // The outside-click handler must keep future tiles clickable as toggles too.
            if (modal.panel != null && !modal.ClassListContains("wm-dismiss-hooked"))
            {
                modal.AddToClassList("wm-dismiss-hooked");
                UI.CloseOnOutsideClick(modal, () => AppearanceSlotUi.ClosePickers(window),
                    WeaponSkinSystem.Slots.Prepend(ItemSlot.InfantryArmor).Select(TileName).ToArray());
                AddCloseButton(window);
            }
            foreach (var element in UI.FindAll(window, UiSelector.TypeName("EquipmentSlot")))
            {
                var slot = element.TryCast<EquipmentSlot>();
                if (slot == null || AppearanceSlotUi.InsideAlternatives(element, window))
                    continue;
                var itemSlot = slot.GetItemSlotType();
                var appearance = ChoicesFor(window, itemSlot);
                if (appearance == null)
                    continue;
                var tileName = TileName(itemSlot);
                var tile = UI.Find(window, UiSelector.Name(tileName));
                if (tile == null || tile.parent != element.parent)
                {
                    tile?.RemoveFromHierarchy();
                    tile = AppearanceSlotUi.Tile(tileName, () => Toggle(window, itemSlot, tileName));
                    tile.EnableInClassList("wm-weapon-skin-tile", itemSlot != ItemSlot.InfantryArmor);
                    element.parent.Add(tile);
                    Tooltip.OnHover(tile, () =>
                    {
                        var current = ChoicesFor(window, itemSlot);
                        var selected = current?.Choices.FirstOrDefault(choice => choice.Id == current.Selection);
                        return current == null || selected == null ? null : ChoiceTooltip(selected, current.Heading);
                    });
                }
                AppearanceSlotUi.Artwork(tile, appearance.TileArt, itemSlot != ItemSlot.InfantryArmor && appearance.Selection == null);
                AppearanceSlotUi.Position(tile, element, "wm-appearance-anchor");
                tile.SetVisible(true);
                if (modal.IsVisible() && UI.Find(tile, UiSelector.Name("Selected"))?.IsVisible() == true)
                    FillModal(window, itemSlot, appearance);
            }
        }
        catch (Exception ex) { Context.Log.Warn($"appearance picker: refresh failed: {ex.Message}"); }
    }

    private Appearance ChoicesFor(VisualElement window, ItemSlot slot)
    {
        var leader = Affinity.LeaderOf(window);
        var tag = Affinity.CharacterTag(leader);
        if (tag == null)
            return null;
        if (slot == ItemSlot.InfantryArmor)
        {
            var selection = Transmog.SelectionFor(Context, tag);
            if (selection == null)
                return null;
            var level = Affinity.LevelFor(Context, leader);
            var choices = new List<Choice>();
            foreach (var option in Transmog.OptionsFor(tag))
            {
                var template = OutfitTemplate(option.ArmorId);
                if (template == null)
                    continue;
                choices.Add(new Choice(option.ArmorId, Templates.DefaultText(template.Title),
                    Templates.DefaultText(template.ShortName), new StyleBackground(template.IconEquipment),
                    option.UnlockLevel <= level, Templates.DefaultText(template.Description),
                    Locale.Format("WOMENACE::ui/transmog_locked", "Unlocks at affinity level {0}", option.UnlockLevel)));
            }
            var hasSquad = !SoloSquadSystem.IsSolo(leader);
            return new Appearance(Locale.Text("WOMENACE::ui/select_outfit", "Select Outfit"),
                Locale.Text("WOMENACE::ui/transmog", "OUTFIT"), selection,
                new StyleBackground(OutfitTemplate(selection)?.IconSkillBar), "wm-fill", choices,
                hasSquad ? Transmog.SquadSelectionFor(Context, tag) : null, hasSquad);
        }
        if (!WeaponSkinSystem.Slots.Contains(slot) || WeaponSkinSystem.Instance is not { } system)
            return null;
        var items = leader.GetItems();
        var weapon = WeaponSkinSystem.WeaponAt(items, slot);
        var skins = system.Available(weapon);
        if (skins.Count == 0)
            return null;
        var selectedSkin = system.SelectionFor(items, slot, weapon);
        var code = WeaponClasses.Code(WeaponClasses.Classify(weapon));
        var defaultArt = new StyleBackground(Context.Assets.Load<Texture2D>("weapon_skins__default_" + code.ToLowerInvariant()));
        var weaponChoices = new List<Choice>
        {
            new(null, Locale.Text("WOMENACE::ui/weapon_skins/default", "Default"), code, defaultArt),
        };
        foreach (var skin in skins)
            weaponChoices.Add(new Choice(skin.Id, skin.Name, code, SkinArt(skin)));
        return new Appearance(Locale.Text("WOMENACE::ui/weapon_skins/select", "Select Weapon Skin"),
            Locale.Text("WOMENACE::ui/weapon_skins/title", "WEAPON SKIN"), selectedSkin?.Id,
            selectedSkin == null ? defaultArt : SkinArt(selectedSkin),
            "wm-weapon-skin-art", weaponChoices);
    }

    private void Toggle(VisualElement window, ItemSlot slot, string tileName)
    {
        try
        {
            Sound.Click();
            var modal = ModalRoot(window);
            var tile = UI.Find(window, UiSelector.Name(tileName));
            var highlight = UI.Find(tile, UiSelector.Name("Selected"));
            var close = modal?.IsVisible() == true && highlight?.IsVisible() == true;
            AppearanceSlotUi.ClosePickers(window);
            if (close || modal == null || ChoicesFor(window, slot) is not { } appearance)
                return;
            FillModal(window, slot, appearance);
            modal.SetVisible(true);
            highlight?.SetVisible(true);
        }
        catch (Exception ex) { Context.Log.Warn($"appearance picker: open failed: {ex.Message}"); }
    }

    private void FillModal(VisualElement window, ItemSlot slot, Appearance appearance)
    {
        var modal = ModalRoot(window);
        if (UI.Find(modal, UiSelector.Name("appearance-title"))?.TryCast<Label>() is { } heading)
            heading.text = appearance.Title;
        var list = UI.Find(modal, UiSelector.Name("appearance-list"));
        list.Clear();
        foreach (var choice in appearance.Choices)
        {
            var card = new Button { name = "appearance-option-" + (choice.Id ?? "default"), focusable = false };
            card.AddToClassList("unit-equipment-slot");
            card.AddToClassList("wm-outfit-card");
            card.EnableInClassList("wm-outfit-card-locked", !choice.Unlocked);
            var image = new VisualElement { name = "Image", pickingMode = PickingMode.Ignore };
            image.AddToClassList(appearance.ImageClass);
            AppearanceSlotUi.Artwork(image, choice.Art, slot != ItemSlot.InfantryArmor && choice.Id == null);
            card.Add(image);
            AppearanceSlotUi.Overlay(card, "Border", "unit-equipment-slot-border");
            AppearanceSlotUi.Overlay(card, "Selected", "slot-selected-border").SetVisible(choice.Id == appearance.Selection);
            var title = new Label(choice.Name) { name = "ItemName", pickingMode = PickingMode.Ignore };
            title.AddToClassList("wm-outfit-card-name");
            card.Add(title);
            var subtitle = new Label(choice.Subtitle) { name = "ShortItemName", pickingMode = PickingMode.Ignore };
            subtitle.AddToClassList("wm-outfit-card-shortname");
            card.Add(subtitle);
            card.clickable.clicked += (Action)(() => Pick(window, slot, choice, squad: false));
            if (appearance.HasSquad)
            {
                // Clickable answers the left button only, so the squaddie pick listens for the
                // right one itself. Stopping it keeps the window's own right click from firing.
                card.RegisterCallback<PointerDownEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerDownEvent>>(
                    (Action<PointerDownEvent>)(evt =>
                    {
                        if (evt.button != 1)
                            return;
                        evt.StopPropagation();
                        Pick(window, slot, choice, squad: true);
                    })));
            }
            if (slot == ItemSlot.InfantryArmor)
                card.Add(Badges(choice.Id == appearance.Selection, appearance.HasSquad && choice.Id == appearance.SquadSelection));
            Tooltip.OnHover(card, () => ChoiceTooltip(choice, hint: appearance.HasSquad));
            list.Add(card);
        }
    }

    // The game's own close button, as on the bug report dialog, at the end of the title row so
    // it centres on the title.
    private void AddCloseButton(VisualElement window)
    {
        var header = UI.Find(window, UiSelector.Name("appearance-header"));
        if (header == null || UI.Find(header, UiSelector.Name("appearance-close")) != null)
            return;
        var close = new Il2CppMenace.UI.CloseButton { name = "appearance-close" };
        close.AddToClassList("wm-outfit-close");
        close.SetOnLeftClickedAction(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Il2CppMenace.UI.InteractiveElement>>(
            (Action<Il2CppMenace.UI.InteractiveElement>)(_ => AppearanceSlotUi.ClosePickers(window))));
        header.Add(close);
    }

    private void Pick(VisualElement window, ItemSlot slot, Choice choice, bool squad)
    {
        if (!choice.Unlocked)
        {
            Sound.RightClick();
            return;
        }
        Sound.Click();
        Select(window, slot, choice.Id, squad);
    }

    // The labels in a card's bottom-left corner, under its name, saying who wears it: a rank
    // chevron for the squad leader and a chain link for her dummy links.
    private VisualElement Badges(bool leader, bool squad)
    {
        var badges = new VisualElement { name = "wm-outfit-badges", pickingMode = PickingMode.Ignore };
        badges.AddToClassList("wm-outfit-badges");
        if (!leader && !squad)
            return badges;
        if (leader)
            badges.Add(Badge(Context.Assets.Load<Texture2D>("transmog__badge_leader"), Locale.Text("WOMENACE::ui/transmog_badge_leader", "SL")));
        if (squad)
            badges.Add(Badge(Context.Assets.Load<Texture2D>("transmog__badge_links"), Locale.Text("WOMENACE::ui/transmog_badge_links", "LINKS")));
        return badges;
    }

    private static VisualElement Badge(Texture2D glyph, string text)
    {
        var badge = new VisualElement { pickingMode = PickingMode.Ignore };
        badge.AddToClassList("wm-outfit-badge");
        if (glyph != null)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("wm-outfit-badge-icon");
            icon.style.backgroundImage = new StyleBackground(glyph);
            badge.Add(icon);
        }
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList("wm-outfit-badge-label");
        badge.Add(label);
        return badge;
    }

    private static Tooltip ChoiceTooltip(Choice choice, string heading = null, bool hint = false)
    {
        var tooltip = new Tooltip("wm-appearance", 230).Subheading(heading ?? choice.Name);
        if (heading != null)
            tooltip.Line().Paragraph(choice.Name);
        if (!string.IsNullOrEmpty(choice.Description))
            tooltip.Line().Paragraph(choice.Description);
        if (hint && choice.Unlocked)
            tooltip.Line().Paragraph(Locale.Text("WOMENACE::ui/transmog_click_hint",
                "Left-click to dress the squad leader. Right-click to dress the dummy links."), Tooltip.Style.Hint);
        if (!choice.Unlocked)
            tooltip.Line().Paragraph(choice.LockedMessage, Tooltip.Style.Disabled);
        return tooltip;
    }

    private bool Select(VisualElement window, ItemSlot slot, string id, bool squad = false)
    {
        try
        {
            var leader = Affinity.LeaderOf(window);
            var appearance = ChoicesFor(window, slot);
            if (appearance?.Choices.Any(choice => choice.Id == id && choice.Unlocked) != true)
                return false;
            var tag = Affinity.CharacterTag(leader);
            if (slot == ItemSlot.InfantryArmor && squad)
            {
                if (!appearance.HasSquad)
                    return false;
                Transmog.SetSquadSelection(Context, tag, id);
            }
            else if (slot == ItemSlot.InfantryArmor)
            {
                // Each button dresses only its own side. Dummy links still following the leader
                // keep the outfit they were wearing, so a left click never changes them.
                if (appearance.HasSquad && Transmog.SavedSquadSelection(Context, tag) == null)
                    Transmog.SetSquadSelection(Context, tag, appearance.Selection);
                Transmog.SetSelection(Context, tag, id);
            }
            else if (WeaponSkinSystem.Instance?.Select(leader, slot, id) != true)
                return false;
            Context.Coroutines.Start(RefreshNextFrame(window, slot, leader.Pointer));
            return true;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"appearance picker: selection failed: {ex.Message}");
            return false;
        }
    }

    // Delay rebuilding until the card's click has finished dispatching, since the rebuild clears
    // the card that was clicked. SetLeader refreshes the window and the open picker is refilled
    // after it. The dirty visual alterations flag, which ArmoryUnitSelector polls, rebuilds the
    // armoury's 3D stage, which SetLeader alone does not.
    private System.Collections.IEnumerator RefreshNextFrame(VisualElement window, ItemSlot slot, IntPtr leaderPointer)
    {
        yield return null;
        var unitWindow = window.TryCast<UnitWindow>();
        var leader = unitWindow?.m_CurrentLeader;
        if (leader == null || !leader.IsAlive() || leader.Pointer != leaderPointer)
            yield break;
        try
        {
            _keepOpen = true;
            try { unitWindow.SetLeader(leader); }
            finally { _keepOpen = false; }
            // The window refresh does not reliably refill the open picker, so the badges and
            // selected border are refreshed here directly.
            if (ModalRoot(window)?.IsVisible() == true && ChoicesFor(window, slot) is { } appearance)
                FillModal(window, slot, appearance);
            var items = leader.GetItems();
            if (items != null)
                items.VisualAlterationsDirty = true;
        }
        catch (Exception ex) { Context.Log.Warn($"appearance picker: preview refresh failed: {ex.Message}"); }
    }

    internal bool DevSelect(string characterTag, string armorId) => DevSelect(characterTag, ItemSlot.InfantryArmor, armorId);

    internal void RefreshVisible()
    {
        var root = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.GetRootElement();
        if (root != null)
            foreach (var window in UI.FindAll(root, UiSelector.TypeName("UnitWindow")))
                EnsureUi(window);
    }

    internal bool DevSelect(string characterTag, ItemSlot slot, string id)
    {
        var root = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.GetRootElement();
        if (root == null)
            return false;
        foreach (var window in UI.FindAll(root, UiSelector.TypeName("UnitWindow")))
            if (Affinity.CharacterTag(Affinity.LeaderOf(window)) == characterTag)
                return Select(window, slot, id);
        return false;
    }

    private ArmorTemplate OutfitTemplate(string id)
        => Templates.Resolve<ArmorTemplate>(id, _armorCache, msg => Context.Log.Warn($"appearance picker: {msg}"));

    private StyleBackground SkinArt(WeaponSkin skin) => new(Context.Assets.Load<Texture2D>(skin.IconAsset));
}
