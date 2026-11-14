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
// bay weapon's banner art, name and rank. Clicking a tile opens vanilla's own
// list of alternatives for that special-weapon slot, which has one row for
// each kind of weapon with how many are owned and in use, however many copies
// there are. A row picked while a tile is open goes to the bay and not to her
// own slot: vanilla's handler is stopped, a free copy of that kind is found
// and Bay.TrySetSlot takes it. A right click on a tile empties it, as it does
// a vanilla slot. The 3D preview respawns through the container's
// visual-alteration event so the weapon lands on the arm immediately. While a
// tile's list is open the preview grows that arm
// (Ots14BayRevealSystem.ArmouryFocus): a filled slot grows arm and weapon, an
// empty slot grows nothing until a weapon is picked, at which point both
// appear together.
//
// The grid is a SIBLING of the slot floated over it, never a child: the game
// resolves clicked InteractiveElements from ancestry, and a child would open
// the vanilla list for her own slot underneath (the transmog tile lesson).
public sealed class BayEquipUISystem : JiangyuSystem
{
    private const string GridName = "wmgfl-bay-grid";
    private const float TileGap = 4f;

    internal static BayEquipUISystem Instance { get; private set; }

    private string _vanillaHeader;
    private int _pickSlot = -1;
    // The window whose list is open for a tile, and a count that ends the watch of a list
    // another has replaced.
    private VisualElement _pickWindow;
    private int _pickEpoch;

    public override void OnInit()
    {
        Instance = this;
        Context.Patches.Prefix(VanillaUi.Targets.EquipmentAlternativePicked.Type, VanillaUi.Targets.EquipmentAlternativePicked.Method, OnAlternativePicked);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "SetLeader", OnWindowChanged);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "Refresh", OnWindowChanged);
    }

    public override void OnUnload()
    {
        Instance = null;
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName) => ForgetPick();

    // No tile's list is open, and whatever was watching one stops.
    private void ForgetPick()
    {
        _pickSlot = -1;
        _pickWindow = null;
        _pickEpoch++;
    }

    private void OnWindowChanged(PatchInfo info)
    {
        if (info.Instance is VisualElement window)
            EnsureUi(window);
    }

    private void EnsureUi(VisualElement window)
    {
        try
        {
            var leader = Affinity.LeaderOf(window);
            var characterTag = Affinity.CharacterTag(leader);
            var slot = FindSpecialSlot(window);
            var grid = UI.Find(window, UiSelector.Name(GridName));

            // Sextans carries no special weapon at all (blades live in the
            // weapon slot and jy_special_restricted empties the dropdown), so
            // her window hides the whole special-weapon box, header included,
            // rather than showing a dead slot. The window is reused across
            // leaders, so everyone else gets it shown back.
            slot?.SetVisible(characterTag != "wmgfl_sextans");

            if (characterTag != Bay.CharacterTag || slot == null)
            {
                grid?.SetVisible(false);
                if (characterTag != Bay.CharacterTag)
                    RestoreHeader(slot);
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
            grid.SetVisible(true);
            RebuildTiles(grid, window);
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

    // Every tile style is inline: the tiles are code-built in the equipment
    // column, where no stylesheet of the mod's is attached.
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
            // A right click empties the slot, as it does a vanilla one.
            tile.RegisterCallback(
                Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<EventCallback<PointerDownEvent>>(
                    (Action<PointerDownEvent>)(evt =>
                    {
                        if (evt.button == 1)
                            OnTileCleared(window, slotIndex);
                    })));
            // Hovering a filled tile grows its arm on the preview (an empty
            // slot's arm never grows, so hovering one is harmless), and raises
            // the weapon's native item popover, the same panel every other
            // equipment slot shows.
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
        // The pickable area starts in from the slot's left and top and runs to its right
        // and bottom edges. The grid keeps the left's margin on every side but the top,
        // which has the area's own, so it sits evenly in the slot's frame.
        var width = Mathf.Max(0f, rect.width - 2f * inner.x);
        var height = Mathf.Max(0f, rect.height - inner.y - inner.x);
        grid.style.left = new StyleLength(rect.x + inner.x);
        grid.style.top = new StyleLength(rect.y + inner.y);
        grid.style.width = new StyleLength(width);
        grid.style.height = new StyleLength(height);
        var tileWidth = (width - TileGap) / 2f;
        var tileHeight = (height - TileGap) / 2f;
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
        // painting, so a gone weapon stops answering a right click on an
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

    // Vanilla's equipment panel of a unit window, and her own special-weapon slot in it.
    private static (UnitWindowEquipment Equipment, EquipmentSlot Slot) Vanilla(VisualElement window)
        => (window.TryCast<UnitWindow>()?.m_Equipment, FindSpecialSlot(window)?.TryCast<EquipmentSlot>());

    private void OnTileClicked(VisualElement window, int slotIndex)
    {
        try
        {
            var (equipment, slot) = Vanilla(window);
            if (equipment == null || slot == null)
            {
                Context.Log.Warn("bay ui: the unit window has no equipment panel or special-weapon slot to open a list for");
                return;
            }
            Sound.Click();
            // The tile whose list is open closes it.
            if (_pickSlot == slotIndex)
            {
                equipment.HideEquipmentAlternatives();
                ClosePicker(window);
                return;
            }
            _pickSlot = slotIndex;
            _pickWindow = window;
            Ots14BayRevealSystem.ArmouryFocus = slotIndex;
            SyncTiles(window);
            Context.Coroutines.Start(OpenPicker(window, equipment, slot, ++_pickEpoch));
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay ui: tile click failed: {ex.Message}");
        }
    }

    private static bool ListOpen(UnitWindowEquipment equipment)
        => equipment.m_EquipmentAlternativesContainer is { } list && list.visible;

    // Opens vanilla's list for her special-weapon slot, then watches for vanilla closing it.
    // The list is opened on the frame after the click: vanilla closes an open list on any
    // press that ends outside it and outside its slot, and the click on a tile is one, so a
    // list opened during the click is closed by the same click.
    private System.Collections.IEnumerator OpenPicker(VisualElement window, UnitWindowEquipment equipment, EquipmentSlot slot, int epoch)
    {
        yield return null;
        if (epoch != _pickEpoch || window.panel == null)
            yield break;
        try
        {
            // All four tiles are that one slot to vanilla, whose click on a slot with an
            // open list closes it.
            if (ListOpen(equipment))
                equipment.HideEquipmentAlternatives();
            equipment.OnEquipmentSlotLeftClick(slot);
            if (!ListOpen(equipment))
                Context.Log.Warn("bay ui: vanilla did not open its list for the special-weapon slot");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay ui: opening the list failed: {ex.GetType().Name}: {ex.Message}");
        }
        // Vanilla closes its list itself, on a click outside it or a change of unit. The
        // tile stops being the open one when it has.
        yield return null;
        while (epoch == _pickEpoch)
        {
            if (window.panel == null || !ListOpen(equipment))
            {
                ClosePicker(window);
                yield break;
            }
            yield return null;
        }
    }

    private void OnTileCleared(VisualElement window, int slotIndex)
    {
        try
        {
            if (Bay.Loadout(Context)[slotIndex] == null)
                return;
            Sound.RightClick();
            Vanilla(window).Equipment?.HideEquipmentAlternatives();
            if (Bay.TrySetSlot(Context, slotIndex, null, out _))
                Context.Coroutines.Start(RefreshAfterEquip(window, slotIndex));
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay ui: clearing a tile failed: {ex.Message}");
        }
    }

    // A row of vanilla's list picked while a tile's list is open. The row is a kind of
    // weapon: the copy that goes to the bay is one no unit holds and no other bay slot has.
    private void OnAlternativePicked(PatchInfo info)
    {
        try
        {
            var equipment = (info.Instance as Il2CppObjectBase)?.TryCast<UnitWindowEquipment>();
            var row = (info.Args[0] as Il2CppObjectBase)?.TryCast<EquipmentSlot>();
            if (equipment == null || row == null || row.GetItemSlotType() != ItemSlot.InfantrySpecial
                || Affinity.CharacterTag(equipment.m_Leader) != Bay.CharacterTag)
                return;
            // Her special weapons are the bay's and never her own slot's: vanilla's handler,
            // which would put the weapon in her hands, does not run for her, whatever comes
            // of the pick. Her list is only ever opened by a tile, which covers the slot.
            info.Skip = true;
            var window = _pickWindow;
            // The list that was picked from is the one a tile opened.
            if (_pickSlot < 0 || window?.TryCast<UnitWindow>()?.m_Equipment?.Pointer != equipment.Pointer)
                return;
            // The row is a kind of weapon. What goes to the bay is a copy of it that is free:
            // none when every copy is in someone's hands or a slot, this slot's own included.
            var free = Bay.FreeCopy(row.GetItem()?.GetTemplate(), Bay.Loadout(Context));
            if (free == null || !Bay.TrySetSlot(Context, _pickSlot, free, out _))
            {
                Sound.RightClick();
                return;
            }
            Sound.Click();
            equipment.HideEquipmentAlternatives();
            Context.Coroutines.Start(RefreshAfterEquip(window, _pickSlot));
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay ui: pick failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ClosePicker(VisualElement window)
    {
        ForgetPick();
        Ots14BayRevealSystem.ArmouryFocus = -1;
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

    // How long the equipped arm stays grown after the list closes, so the
    // weapon landing is watched unobstructed.
    private const float LingerSeconds = 4f;

    // Rebuild the window's tiles and respawn the 3D preview so the weapon
    // lands on the arm. Deferred a frame: vanilla's row whose click is still
    // dispatching must not be destroyed under it. The list CLOSES on equip
    // and the arm lingers grown: with the list covering the preview, staying
    // open would hide the very thing just picked.
    private System.Collections.IEnumerator RefreshAfterEquip(VisualElement window, int equippedSlot)
    {
        yield return null;
        try
        {
            var unitWindow = window.TryCast<UnitWindow>();
            var leader = unitWindow?.m_CurrentLeader;
            if (unitWindow == null || leader == null || !leader.IsAlive())
                yield break;
            unitWindow.SetLeader(leader);
            // The armoury's 3D squad preview does not rebuild off the
            // window: flag the container's visual alterations dirty (the
            // ArmoryUnitSelector polls it, the transmog route) so the mounts
            // rebuild with the new loadout.
            var container = leader.GetItems();
            if (container != null)
                container.VisualAlterationsDirty = true;
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

    // Keep the just-equipped arm grown for a moment after the list closes;
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
