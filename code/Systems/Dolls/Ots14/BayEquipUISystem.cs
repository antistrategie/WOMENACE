using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui.Components;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The weapons-bay equip surface on OTs-14's unit window, on the armoury and
// mission-prep screens alike (both host the same UnitWindow).
//
// Her special-weapon slot becomes the bay: the slot header is retitled and a
// 2x2 grid of tiles overlays the slot's pickable area, each tile showing its
// bay weapon's banner art, name and rank. Clicking a tile opens the bay
// flyout (a sibling of the vanilla EquipmentAlternatives, in its style)
// listing every owned special weapon, with rows the bay refuses greyed out
// carrying the reason. Picking routes through Bay.TrySetSlot, and the 3D
// preview respawns through the container's visual-alteration event so the
// weapon lands on the arm immediately. While a tile's picker is open the
// preview grows that arm (Ots14BayRevealSystem.ArmouryFocus): a filled slot
// grows arm and weapon, an empty slot grows nothing until a weapon is
// picked, at which point both appear together.
//
// The grid is a SIBLING of the slot floated over it, never a child: the game
// resolves clicked InteractiveElements from ancestry, and a child would open
// the vanilla special-weapon dropdown underneath (the transmog tile lesson).
public sealed class BayEquipUISystem : JiangyuSystem
{
    private const string GridName = "wmgfl-bay-grid";
    private const string ModalName = "BayAlternatives";
    private const float TileGap = 4f;

    internal static BayEquipUISystem Instance { get; private set; }

    private string _vanillaHeader;
    private int _pickSlot = -1;

    public override void OnInit()
    {
        Instance = this;
        RegisterScreen<ArmoryUIScreen>();
        RegisterScreen<MissionPrepUIScreen>();
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "SetLeader", OnWindowChanged);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "Refresh", OnWindowChanged);
    }

    public override void OnUnload()
    {
        Instance = null;
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        _pickSlot = -1;
    }

    private void RegisterScreen<TScreen>() where TScreen : Il2CppMenace.UI.UIScreen
    {
        UI.InjectEach(
            UiTarget.Screen<TScreen>()
                .Each(UiSelector.TypeName("UnitWindow"))
                .Before(UiSelector.Name("EquipmentAlternatives")),
            "bay/bay-modal",
            WireModal);
    }

    private void WireModal(VisualElement root, VisualElement window)
    {
        root.SetVisible(false);
        EnsureDismissHooked(root, window);
        EnsureUi(window);
    }

    private void EnsureDismissHooked(VisualElement modal, VisualElement window)
    {
        if (modal == null || modal.panel == null || modal.ClassListContains("wm-bay-dismiss-hooked"))
            return;
        modal.AddToClassList("wm-bay-dismiss-hooked");
        var dismissable = modal;
        var dismissWindow = window;
        UI.CloseOnOutsideClick(dismissable, () => ClosePicker(dismissWindow), GridName);
    }

    private void OnWindowChanged(PatchInfo info)
    {
        if (info.Instance is VisualElement window)
            EnsureUi(window);
    }

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
            var slot = FindSpecialSlot(window);
            var modal = ModalRoot(window);
            var grid = UI.Find(window, UiSelector.Name(GridName));

            // Sextans carries no special weapon at all (blades live in the
            // weapon slot and jy_special_restricted empties the dropdown), so
            // her window hides the whole special-weapon box, header included,
            // rather than showing a dead slot. The window is reused across
            // leaders, so everyone else gets it shown back.
            slot?.SetVisible(characterTag != "wmgfl_sextans");

            if (characterTag != Bay.CharacterTag || slot == null || modal == null)
            {
                grid?.SetVisible(false);
                if (characterTag != Bay.CharacterTag)
                {
                    modal?.SetVisible(false);
                    RestoreHeader(slot);
                }
                return;
            }

            RetitleHeader(slot);
            if (grid == null || grid.parent != slot.parent)
            {
                grid?.RemoveFromHierarchy();
                grid = BuildGrid(window);
                slot.parent.Add(grid);
            }
            PositionGrid(grid, slot);
            EnsureDismissHooked(modal, window);
            grid.SetVisible(true);
            RebuildTiles(grid, window);
            if (modal.IsVisible() && _pickSlot >= 0)
                RebuildRows(window);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay ui: update failed: {ex.Message}");
        }
    }

    // The special-weapon EquipmentSlot in the window's loadout column. The
    // alternatives list reuses the same element type for its entries, so
    // anything under EquipmentAlternatives is skipped.
    private static VisualElement FindSpecialSlot(VisualElement window)
    {
        foreach (var element in UI.FindAll(window, UiSelector.TypeName("EquipmentSlot")))
        {
            var slot = element.TryCast<EquipmentSlot>();
            if (slot == null || slot.GetItemSlotType() != ItemSlot.InfantrySpecial)
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

    // Clean display strings for a possibly rank-cloned weapon: calibration
    // bakes a rank marker into every doll weapon's Title (gold R1-R6, grey
    // R0 on the base), so the name is stripped with the purpose-built
    // CleanName and the rank joins the type line in calibration gold
    // ("Sniper Rifle · R1").
    private static string WeaponName(WeaponTemplate weapon)
        => Calibration.CleanName(Templates.DefaultText(weapon.Title, weapon.GetID()));

    private static string WeaponTypeLine(WeaponTemplate weapon)
    {
        var type = Templates.DefaultText(weapon.ShortName);
        return Calibration.TryResolveWeaponId(weapon.GetID(), out _, out var rank) && rank > 0
            ? $"{type} · <color={Calibration.RankMarkerColor}>R{rank}</color>"
            : type;
    }

    private static Label HeaderLabel(VisualElement slot)
        => slot == null ? null : UI.Find(slot, UiSelector.Name("SlotHeaderText"))?.TryCast<Label>();

    private void RetitleHeader(VisualElement slot)
    {
        var label = HeaderLabel(slot);
        if (label == null)
            return;
        _vanillaHeader ??= label.text;
        label.text = Locale.Text("WOMENACE::ui/weapons_bay", "WEAPONS BAY") + $" X{Bay.SlotCount}";
    }

    private void RestoreHeader(VisualElement slot)
    {
        var label = HeaderLabel(slot);
        if (label != null && _vanillaHeader != null)
            label.text = _vanillaHeader;
    }

    // -- the tile grid ------------------------------------------------------

    // Absolute fill, matching how the vanilla slots layer their overlays.
    private static void Fill(VisualElement element)
    {
        element.style.position = new StyleEnum<Position>(Position.Absolute);
        element.style.left = new StyleLength(0f);
        element.style.top = new StyleLength(0f);
        element.style.right = new StyleLength(0f);
        element.style.bottom = new StyleLength(0f);
    }

    private static readonly Color NameColour = new(228f / 255f, 225f / 255f, 180f / 255f);
    private static readonly Color MutedColour = new(184f / 255f, 151f / 255f, 134f / 255f);
    private static readonly Color DimColour = new(140f / 255f, 133f / 255f, 116f / 255f);

    // Every tile style is inline: the tiles live in the equipment column,
    // outside the modal's UXML subtree, so bay.uss never reaches them (the
    // transmog tile learned the same lesson).
    private VisualElement BuildGrid(VisualElement window)
    {
        var grid = new VisualElement { name = GridName };
        grid.style.position = new StyleEnum<Position>(Position.Absolute);
        for (var i = 0; i < Bay.SlotCount; i++)
        {
            var tile = new Button { name = $"wmgfl-bay-tile-{i}", focusable = false };
            tile.AddToClassList("unit-equipment-slot");
            tile.style.position = new StyleEnum<Position>(Position.Absolute);
            tile.style.paddingLeft = new StyleLength(0f);
            tile.style.paddingRight = new StyleLength(0f);
            tile.style.paddingTop = new StyleLength(0f);
            tile.style.paddingBottom = new StyleLength(0f);
            tile.style.marginLeft = new StyleLength(0f);
            tile.style.marginRight = new StyleLength(0f);
            tile.style.marginTop = new StyleLength(0f);
            tile.style.marginBottom = new StyleLength(0f);
            tile.style.alignItems = new StyleEnum<Align>(Align.FlexStart);
            tile.style.justifyContent = new StyleEnum<Justify>(Justify.FlexStart);
            tile.style.overflow = new StyleEnum<Overflow>(Overflow.Hidden);

            var art = new VisualElement { name = "Art", pickingMode = PickingMode.Ignore };
            Fill(art);
            tile.Add(art);

            var border = new VisualElement { name = "Border", pickingMode = PickingMode.Ignore };
            border.AddToClassList("unit-equipment-slot-border");
            Fill(border);
            tile.Add(border);

            var selected = new VisualElement { name = "Selected", pickingMode = PickingMode.Ignore };
            selected.AddToClassList("slot-selected-border");
            Fill(selected);
            selected.SetVisible(false);
            tile.Add(selected);

            var name = new Label { name = "ItemName", pickingMode = PickingMode.Ignore };
            name.style.fontSize = new StyleLength(10f);
            name.style.color = new StyleColor(NameColour);
            name.style.marginLeft = new StyleLength(7f);
            name.style.marginTop = new StyleLength(6f);
            name.style.marginRight = new StyleLength(20f);
            name.style.paddingLeft = new StyleLength(0f);
            name.style.paddingTop = new StyleLength(0f);
            // Long weapon names trim with an ellipsis instead of spilling
            // over the tile edge.
            name.style.whiteSpace = new StyleEnum<WhiteSpace>(WhiteSpace.NoWrap);
            name.style.textOverflow = new StyleEnum<TextOverflow>(TextOverflow.Ellipsis);
            name.style.overflow = new StyleEnum<Overflow>(Overflow.Hidden);
            tile.Add(name);

            var shortName = new Label { name = "ShortItemName", pickingMode = PickingMode.Ignore };
            shortName.enableRichText = true; // the gold rank marker in the type line
            shortName.style.fontSize = new StyleLength(9f);
            shortName.style.color = new StyleColor(MutedColour);
            shortName.style.marginLeft = new StyleLength(7f);
            shortName.style.marginTop = new StyleLength(1f);
            shortName.style.paddingLeft = new StyleLength(0f);
            shortName.style.paddingTop = new StyleLength(0f);
            tile.Add(shortName);

            var plus = new Label("+") { name = "Plus", pickingMode = PickingMode.Ignore };
            Fill(plus);
            plus.style.fontSize = new StyleLength(20f);
            plus.style.color = new StyleColor(DimColour);
            plus.style.unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.MiddleCenter);
            tile.Add(plus);

            var number = new Label($"{i + 1:00}") { name = "Num", pickingMode = PickingMode.Ignore };
            number.style.position = new StyleEnum<Position>(Position.Absolute);
            // Sits on the weapon name's line: the name is fontSize 10 at
            // marginTop 6, so the smaller number drops one extra pixel to
            // share its baseline.
            number.style.top = new StyleLength(7f);
            number.style.right = new StyleLength(5f);
            number.style.fontSize = new StyleLength(9f);
            number.style.color = new StyleColor(DimColour);
            tile.Add(number);

            var slotIndex = i;
            tile.clickable.clicked += (Action)(() => OnTileClicked(window, slotIndex));
            // Hovering a filled tile grows its arm on the preview with no
            // modal in the way (an empty slot's arm never grows, so hovering
            // one is harmless), and raises the weapon's native item popover,
            // the same panel every other equipment slot shows.
            var shownTooltipId = new string[1];
            var pendingTooltip = new IVisualElementScheduledItem[1];
            tile.RegisterCallback(
                Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<EventCallback<MouseEnterEvent>>(
                    (Action<MouseEnterEvent>)(_ =>
                    {
                        // The arm grows at once (that is feedback, not a
                        // tooltip); only the popover waits the vanilla delay.
                        Ots14BayRevealSystem.ArmouryFocus = slotIndex;
                        HoverDelay.Cancel(ref pendingTooltip[0]);
                        pendingTooltip[0] = HoverDelay.Schedule(tile,
                            () => shownTooltipId[0] = ShowWeaponTooltip(tile, slotIndex));
                    })));
            tile.RegisterCallback(
                Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<EventCallback<MouseLeaveEvent>>(
                    (Action<MouseLeaveEvent>)(_ =>
                    {
                        Ots14BayRevealSystem.ArmouryFocus = _pickSlot;
                        HoverDelay.Cancel(ref pendingTooltip[0]);
                        HideWeaponTooltip(shownTooltipId[0]);
                        shownTooltipId[0] = null;
                    })));
            grid.Add(tile);
        }
        // The link badges. INERT eligibility marks, not toggles: the link
        // itself is flipped from the skill bar mid-mission. They look like
        // buttons, so every state answers a hover with a tooltip instead.
        for (var pair = 0; pair < 2; pair++)
            grid.Add(BuildLinkBadge($"LinkBadge{pair}", 11f, pair));
        grid.Add(BuildLinkBadge("LinkBadgeBay", 12f, -1));
        return grid;
    }

    // group >= 0 is a pair badge, -1 the bay-wide badge (grey hint when
    // nothing links, gold when all four match).
    private VisualElement BuildLinkBadge(string name, float size, int group)
    {
        var badge = new VisualElement { name = name };
        badge.style.position = new StyleEnum<Position>(Position.Absolute);
        badge.style.width = new StyleLength(size);
        badge.style.height = new StyleLength(size);
        badge.SetVisible(false);
        Tooltip.OnHover(badge, () =>
        {
            var quad = BayLink.IsGrouped(Context, BayLink.QuadGroup);
            if (group < 0 && quad)
                return new Tooltip("wm-bay-link", 240)
                    .Subheading(Locale.Text("WOMENACE::ui/bay_link_quad", "QUAD LINK READY"))
                    .Line()
                    .Paragraph(Locale.Text("WOMENACE::ui/bay_link_quad_body",
                        "All four weapons can fire together as one action. Toggle the link from the skill bar during a mission."));
            if (group >= 0)
                return new Tooltip("wm-bay-link", 240)
                    .Subheading(Locale.Text("WOMENACE::ui/bay_link_twin", "TWIN LINK READY"))
                    .Line()
                    .Paragraph(Locale.Text("WOMENACE::ui/bay_link_twin_body",
                        "These weapons can fire together as one action. Toggle the link from the skill bar during a mission."));
            return new Tooltip("wm-bay-link", 240)
                .Subheading(Locale.Text("WOMENACE::ui/bay_link_none", "WEAPON LINK"))
                .Line()
                .Paragraph(Locale.Text("WOMENACE::ui/bay_link_none_body",
                    "Arms in the same row holding the same weapon can fire together as one action. Four matching weapons link the whole bay."));
        });
        return badge;
    }

    private UnityEngine.Texture2D _linkBadgeGold;
    private UnityEngine.Texture2D _linkBadgeGrey;

    // Which badges show, and with which face. The rule is bare: the grey
    // slashed hint appears only while NOTHING links, a matched pair lights
    // its seam, and four matching weapons collapse everything to one gold
    // mark at the centre.
    private void UpdateLinkBadges(VisualElement grid)
    {
        try
        {
            _linkBadgeGold ??= Context.Assets.Load<UnityEngine.Texture2D>("bay_link");
            _linkBadgeGrey ??= Context.Assets.Load<UnityEngine.Texture2D>("bay_link_slash_disabled");
            var quad = BayLink.IsGrouped(Context, BayLink.QuadGroup);
            var pair0 = !quad && BayLink.IsGrouped(Context, 0);
            var pair1 = !quad && BayLink.IsGrouped(Context, 1);
            SetBadge(grid, "LinkBadge0", pair0 ? _linkBadgeGold : null);
            SetBadge(grid, "LinkBadge1", pair1 ? _linkBadgeGold : null);
            SetBadge(grid, "LinkBadgeBay", quad ? _linkBadgeGold : !pair0 && !pair1 ? _linkBadgeGrey : null);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay ui: link badges failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SetBadge(VisualElement grid, string name, UnityEngine.Texture2D face)
    {
        var badge = UI.Find(grid, UiSelector.Name(name));
        if (badge == null)
            return;
        badge.SetVisible(face != null);
        if (face != null)
            badge.style.backgroundImage = new StyleBackground(face);
    }

    // Float the grid over the slot's Pickable area (grid and slot share a
    // parent, so the slot's layout rect is in the grid's coordinate space).
    // The layout is unresolved right after a rebuild, so a geometry callback
    // re-anchors once it lands.
    private void PositionGrid(VisualElement grid, VisualElement slot)
    {
        Reposition(grid, slot);
        if (slot.ClassListContains("wm-bay-anchor"))
            return;
        slot.AddToClassList("wm-bay-anchor");
        var host = slot.parent;
        slot.RegisterCallback(
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<EventCallback<GeometryChangedEvent>>(
                (Action<GeometryChangedEvent>)(_ =>
                {
                    var current = host != null ? UI.Find(host, UiSelector.Name(GridName)) : null;
                    if (current != null)
                        Reposition(current, slot);
                })));
    }

    private static void Reposition(VisualElement grid, VisualElement slot)
    {
        var rect = slot.layout;
        if (float.IsNaN(rect.x) || float.IsNaN(rect.width))
            return;
        var pickable = UI.Find(slot, UiSelector.Name("Pickable"));
        var inner = pickable?.layout ?? new Rect(0f, 0f, rect.width, rect.height);
        if (float.IsNaN(inner.width) || inner.width <= 0f)
            inner = new Rect(0f, 0f, rect.width, rect.height);
        grid.style.left = new StyleLength(rect.x + inner.x);
        grid.style.top = new StyleLength(rect.y + inner.y);
        grid.style.width = new StyleLength(inner.width);
        grid.style.height = new StyleLength(inner.height);
        var tileWidth = (inner.width - TileGap) / 2f;
        var tileHeight = (inner.height - TileGap) / 2f;
        // Only the first four children are tiles. The link badges come after
        // them and take their own positions below.
        for (var i = 0; i < Bay.SlotCount && i < grid.childCount; i++)
        {
            var tile = grid.ElementAt(i);
            tile.style.left = new StyleLength(i % 2 * (tileWidth + TileGap));
            tile.style.top = new StyleLength(i / 2 * (tileHeight + TileGap));
            tile.style.width = new StyleLength(tileWidth);
            tile.style.height = new StyleLength(tileHeight);
        }
        // Pair badges sit on the seam between a row's two tiles, the bay
        // badge at the point all four meet.
        var seamX = tileWidth + TileGap / 2f;
        PlaceBadge(grid, "LinkBadge0", seamX, tileHeight / 2f);
        PlaceBadge(grid, "LinkBadge1", seamX, tileHeight + TileGap + tileHeight / 2f);
        PlaceBadge(grid, "LinkBadgeBay", seamX, tileHeight + TileGap / 2f);
    }

    private static void PlaceBadge(VisualElement grid, string name, float cx, float cy)
    {
        var badge = UI.Find(grid, UiSelector.Name(name));
        if (badge == null)
            return;
        var size = badge.style.width.value.value;
        badge.style.left = new StyleLength(cx - size / 2f);
        badge.style.top = new StyleLength(cy - size / 2f);
    }

    private void RebuildTiles(VisualElement grid, VisualElement window)
    {
        // Selling a slotted weapon leaves its guid behind. Clear those before
        // painting, so a gone weapon stops showing an UNEQUIP action on an
        // empty-looking tile.
        var pruned = Bay.Prune(Context);
        if (pruned > 0)
            Context.Log.Debug($"bay: cleared {pruned} slot(s) whose weapon is no longer owned");
        var slots = Bay.Loadout(Context);
        for (var i = 0; i < Bay.SlotCount && i < grid.childCount; i++)
        {
            var tile = grid.ElementAt(i);
            var item = Bay.ResolveItem(slots[i]);
            var weapon = Bay.WeaponOf(item);
            var art = UI.Find(tile, UiSelector.Name("Art"));
            var name = UI.Find(tile, UiSelector.Name("ItemName"))?.TryCast<Label>();
            var shortName = UI.Find(tile, UiSelector.Name("ShortItemName"))?.TryCast<Label>();
            var plus = UI.Find(tile, UiSelector.Name("Plus"));
            var filled = weapon != null;
            if (art != null)
            {
                var banner = weapon?.IconEquipment;
                art.style.backgroundImage = banner != null ? new StyleBackground(banner) : new StyleBackground();
                art.SetVisible(filled);
            }
            if (name != null)
                name.text = filled ? WeaponName(weapon) : string.Empty;
            if (shortName != null)
                shortName.text = filled ? WeaponTypeLine(weapon) : string.Empty;
            plus?.SetVisible(!filled);
            UI.Find(tile, UiSelector.Name("Selected"))?.SetVisible(_pickSlot == i);
        }
        UpdateLinkBadges(grid);
    }

    // The native item popover for a filled tile's weapon, mouse-following
    // like the game's own equipment-slot tooltips. Returns the shown
    // tooltip's id so the leave handler removes only its own panel, never a
    // game tooltip stacked over it (the SDK Tooltip's guard, restated here
    // because this one shows a native TooltipData rather than a wrapper).
    private string ShowWeaponTooltip(VisualElement tile, int slotIndex)
    {
        try
        {
            var slots = Bay.Loadout(Context);
            if (slots == null || slotIndex >= slots.Length)
                return null;
            var weapon = Bay.WeaponOf(Bay.ResolveItem(slots[slotIndex]));
            var data = weapon?.GetSimpleTooltipData(1, 1f, null);
            if (data == null)
                return null;
            var manager = Il2CppMenace.UI.UIManager.Get();
            if (manager == null)
                return null;
            manager.AddTooltip(data, tile, false, true, false);
            return manager.GetActiveTooltipId();
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay ui: weapon tooltip failed: {ex.Message}");
            return null;
        }
    }

    private static void HideWeaponTooltip(string shownId)
    {
        if (shownId == null)
            return;
        try
        {
            var manager = Il2CppMenace.UI.UIManager.Get();
            if (manager != null && manager.GetActiveTooltipId() == shownId && !manager.IsActiveTooltipPinned())
                manager.RemoveActiveTooltip();
        }
        catch { }
    }

    // The native item tooltip for a flyout row, built through a hidden
    // EquipmentSlot: the vanilla dropdown's own tooltip factory, so the
    // stat rows, ordering and the current-vs-hovered comparison columns are
    // pixel-identical to the base game. The slot element is never attached
    // to the panel; only its TooltipData is used. Falls back to the plain
    // weapon tooltip if the factory refuses.
    private string ShowRowTooltip(VisualElement row, Item item)
    {
        try
        {
            var manager = Il2CppMenace.UI.UIManager.Get();
            if (manager == null || item == null)
                return null;
            Il2CppMenace.UI.TooltipData data = null;
            try
            {
                var factory = new EquipmentSlot(Il2CppMenace.Items.ItemSlot.InfantrySpecial, 1, null);
                factory.Init(true, item, 1, false, 0, null, true, false, 1f);
                var slots = Bay.Loadout(Context);
                var equipped = _pickSlot >= 0 && _pickSlot < slots.Length ? Bay.ResolveItem(slots[_pickSlot]) : null;
                if (equipped != null && equipped.Pointer != item.Pointer)
                    factory.SetItemToCompareWith(equipped, 1f, 1f);
                data = factory.GetTooltipData();
            }
            catch (Exception ex)
            {
                Context.Log.Debug($"bay ui: comparison tooltip fell back: {ex.GetType().Name}: {ex.Message}");
                data = Bay.WeaponOf(item)?.GetSimpleTooltipData(1, 1f, null);
            }
            if (data == null)
                return null;
            manager.AddTooltip(data, row, false, true, false);
            return manager.GetActiveTooltipId();
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay ui: row tooltip failed: {ex.Message}");
            return null;
        }
    }

    private void OnTileClicked(VisualElement window, int slotIndex)
    {
        try
        {
            Sound.Click();
            if (_pickSlot == slotIndex)
            {
                ClosePicker(window);
                return;
            }
            _pickSlot = slotIndex;
            Ots14BayRevealSystem.ArmouryFocus = slotIndex;
            var modal = ModalRoot(window);
            if (modal == null)
                return;
            var title = UI.Find(window, UiSelector.Name("bay-title"))?.TryCast<Label>();
            if (title != null)
                title.text = Locale.Text("WOMENACE::ui/select_bay_weapon", "Select Bay Weapon")
                    + $" | {slotIndex + 1:00}";
            RebuildRows(window);
            modal.SetVisible(true);
            SyncTiles(window);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay ui: tile click failed: {ex.Message}");
        }
    }

    private void ClosePicker(VisualElement window)
    {
        _pickSlot = -1;
        Ots14BayRevealSystem.ArmouryFocus = -1;
        ModalRoot(window)?.SetVisible(false);
        SyncTiles(window);
    }

    private void SyncTiles(VisualElement window)
    {
        var grid = UI.Find(window, UiSelector.Name(GridName));
        if (grid == null)
            return;
        for (var i = 0; i < grid.childCount; i++)
            UI.Find(grid.ElementAt(i), UiSelector.Name("Selected"))?.SetVisible(_pickSlot == i);
    }

    // -- the flyout rows ----------------------------------------------------

    private void RebuildRows(VisualElement window)
    {
        var list = UI.Find(window, UiSelector.Name("bay-list"));
        if (list == null || _pickSlot < 0)
            return;
        list.Clear();

        var slots = Bay.Loadout(Context);
        if (slots[_pickSlot] != null)
            list.Add(BuildClearRow(window));

        var owned = Jiangyu.Game.Strategy.Inventory.Owned;
        if (owned == null)
            return;
        var all = new Il2CppSystem.Collections.Generic.List<BaseItem>();
        owned.GetInstances(all);
        for (var i = 0; i < all.Count; i++)
        {
            var item = all[i]?.TryCast<Item>();
            if (item == null || !Bay.IsBayWeapon(item))
                continue;
            var guid = item.GetGuid();
            var current = slots[_pickSlot] == guid;
            // CanSlot's refusals, restated as row states: an instance some
            // doll has equipped, or one already slotted elsewhere in the bay.
            string blocked = null;
            var inBay = Array.IndexOf(slots, guid);
            if (Bay.IsEquipped(item))
                blocked = Locale.Text("WOMENACE::ui/bay_equipped", "EQUIPPED");
            else if (!current && inBay >= 0)
                blocked = Locale.Text("WOMENACE::ui/bay_in_slot", "IN BAY") + $" {inBay + 1:00}";
            list.Add(BuildRow(window, item, current, blocked));
        }
    }

    // A subtle separator under every flyout row, and a hover tint matching
    // the vanilla dropdown feel. Inline (the injected-USS lesson): the rows
    // are code-built, so classes from bay.uss never reach them.
    private static void StyleRowChrome(VisualElement row)
    {
        row.style.borderBottomWidth = new StyleFloat(1f);
        row.style.borderBottomColor = new StyleColor(new Color(1f, 1f, 1f, 0.10f));
        row.RegisterCallback(
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<EventCallback<MouseEnterEvent>>(
                (Action<MouseEnterEvent>)(_ => row.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.07f)))));
        row.RegisterCallback(
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<EventCallback<MouseLeaveEvent>>(
                (Action<MouseLeaveEvent>)(_ => row.style.backgroundColor = new StyleColor(StyleKeyword.Null))));
    }

    private VisualElement BuildClearRow(VisualElement window)
    {
        var row = new Button { name = "wmgfl-bay-clear", focusable = false };
        row.AddToClassList("unit-equipment-slot");
        row.AddToClassList("wm-bay-row");
        // Fixed inline width: the ScrollView's content container shrink-wraps
        // to its widest row, so a percentage resolves against that shrunk
        // parent (circular). The modal is a fixed 400 (12px padding each
        // side, ~13px scroller): 350 fills the viewport.
        row.style.width = new StyleLength(350f);
        StyleRowChrome(row);
        var name = new Label(Locale.Text("WOMENACE::ui/bay_unequip", "UNEQUIP"))
        {
            pickingMode = PickingMode.Ignore,
        };
        name.AddToClassList("wm-bay-row-name");
        row.Add(name);
        row.clickable.clicked += (Action)(() =>
        {
            Sound.Click();
            if (Bay.TrySetSlot(Context, _pickSlot, null, out _))
                Context.Coroutines.Start(RefreshAfterEquip(window));
        });
        return row;
    }

    private VisualElement BuildRow(VisualElement window, Item item, bool current, string blocked)
    {
        var weapon = Bay.WeaponOf(item);
        var row = new Button { name = "wmgfl-bay-option", focusable = false };
        row.AddToClassList("unit-equipment-slot");
        row.AddToClassList("wm-bay-row");
        row.style.width = new StyleLength(350f);
        StyleRowChrome(row);
        if (blocked != null)
            row.AddToClassList("wm-bay-row-blocked");

        // Hovering a row raises the native item tooltip, comparing against
        // whatever the open slot currently holds, the way the vanilla
        // dropdown does.
        var shownTooltipId = new string[1];
        var pendingTooltip = new IVisualElementScheduledItem[1];
        row.RegisterCallback(
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<EventCallback<MouseEnterEvent>>(
                (Action<MouseEnterEvent>)(_ =>
                {
                    HoverDelay.Cancel(ref pendingTooltip[0]);
                    pendingTooltip[0] = HoverDelay.Schedule(row,
                        () => shownTooltipId[0] = ShowRowTooltip(row, item));
                })));
        row.RegisterCallback(
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<EventCallback<MouseLeaveEvent>>(
                (Action<MouseLeaveEvent>)(_ =>
                {
                    HoverDelay.Cancel(ref pendingTooltip[0]);
                    HideWeaponTooltip(shownTooltipId[0]);
                    shownTooltipId[0] = null;
                })));

        var chip = new VisualElement { pickingMode = PickingMode.Ignore };
        chip.AddToClassList("wm-bay-row-chip");
        var banner = weapon?.IconEquipment;
        if (banner != null)
            chip.style.backgroundImage = new StyleBackground(banner);
        row.Add(chip);

        var name = new Label(weapon != null ? WeaponName(weapon) : "?")
        {
            pickingMode = PickingMode.Ignore,
        };
        name.AddToClassList("wm-bay-row-name");
        row.Add(name);

        var state = new Label(blocked ?? (weapon != null ? WeaponTypeLine(weapon) : string.Empty))
        {
            pickingMode = PickingMode.Ignore,
            enableRichText = true, // the gold rank marker in the type line
        };
        state.AddToClassList("wm-bay-row-state");
        row.Add(state);

        if (current)
        {
            var selected = new VisualElement { name = "Selected", pickingMode = PickingMode.Ignore };
            selected.AddToClassList("slot-selected-border");
            selected.AddToClassList("wm-fill");
            row.Add(selected);
        }

        var chosen = item;
        var isBlocked = blocked != null;
        row.clickable.clicked += (Action)(() =>
        {
            if (isBlocked || current)
            {
                Sound.RightClick();
                return;
            }
            Sound.Click();
            if (Bay.TrySetSlot(Context, _pickSlot, chosen, out var error))
                Context.Coroutines.Start(RefreshAfterEquip(window));
            else
                Context.Log.Warn($"bay ui: equip refused: {error}");
        });
        return row;
    }

    // How long the equipped arm stays grown after the modal closes, so the
    // weapon landing is watched unobstructed.
    private const float LingerSeconds = 4f;

    // Rebuild the window (tiles, rows) and respawn the 3D preview so the
    // weapon lands on the arm. Deferred a frame: the row whose click is
    // still dispatching must not be destroyed under it. The picker CLOSES on
    // equip and the arm lingers grown: with the modal covering the preview,
    // staying open would hide the very thing just picked.
    private System.Collections.IEnumerator RefreshAfterEquip(VisualElement window)
    {
        yield return null;
        var equippedSlot = _pickSlot;
        try
        {
            var unitWindow = window.TryCast<UnitWindow>();
            var leader = unitWindow?.m_CurrentLeader;
            if (unitWindow == null || leader == null || !leader.IsAlive())
                yield break;
            unitWindow.SetLeader(leader);
            // The armoury's 3D squad preview does not rebuild off the
            // window: raise the container's visual-alteration event (the
            // vanilla respawn trigger, the transmog route) so the mounts
            // rebuild with the new loadout.
            var container = leader.GetItems();
            var item = container?.GetItemAtSlot(ItemSlot.InfantryWeapon);
            container?.OnVisualAlterationChanged?.Invoke(container.GetOwner(), item);
            // The mission-prep supplies bar recomputes off its dirty flags,
            // which a bay equip does not raise on its own.
            var prep = (Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen() as Il2CppObjectBase)
                ?.TryCast<MissionPrepUIScreen>();
            if (prep != null)
            {
                prep.m_DeploymentNeedsUpdate = true;
                prep.m_PreviewNeedsUpdate = true;
            }
        }
        finally
        {
            ClosePicker(window);
            if (equippedSlot >= 0)
                Context.Coroutines.Start(LingerReveal(equippedSlot));
        }
    }

    // Keep the just-equipped arm grown for a moment after the modal closes;
    // a hover or a new pick supersedes the linger, ending it early.
    private System.Collections.IEnumerator LingerReveal(int slot)
    {
        Ots14BayRevealSystem.ArmouryFocus = slot;
        var until = Time.time + LingerSeconds;
        while (Time.time < until)
        {
            if (Ots14BayRevealSystem.ArmouryFocus != slot)
                yield break;
            yield return null;
        }
        if (Ots14BayRevealSystem.ArmouryFocus == slot && _pickSlot < 0)
            Ots14BayRevealSystem.ArmouryFocus = -1;
    }
}
