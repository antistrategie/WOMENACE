using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tools;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Game.Ui.Components;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

public sealed class AffinitySystem : JiangyuSystem
{

    private sealed class Gift
    {
        public CommodityTemplate Template;
        public string Name;
        public int Affinity;
    }

    private sealed class Box
    {
        public Gift Gift;
        public ItemTile Slot;
    }

    private readonly List<Gift> _gifts = [];
    private readonly List<Box> _boxes = [];
    private readonly Dictionary<Gift, int> _chosen = [];

    // Resolved gated-skin armour templates, cached so the unlock pass does not rescan the template
    // loader on every window refresh.
    private readonly Dictionary<string, BaseItemTemplate> _armorCache = new(StringComparer.Ordinal);

    // The game's rarity palette, read once from UIConfig (with the shipped values as a fallback) so
    // gift tiles in the picker show the same common/uncommon/rare colours the game uses elsewhere.
    private bool _rarityLoaded;
    private int _uncommonMinRarity = 33;
    private int _rareMinRarity = 66;
    private UnityEngine.Color _commonRarity = new(0.455f, 0.424f, 0.294f, 1f);
    private UnityEngine.Color _uncommonRarity = new(0.239f, 0.459f, 0.533f, 1f);
    private UnityEngine.Color _rareRarity = new(0.741f, 0.192f, 0.192f, 1f);

    // Modal state, captured when it is injected / opened.
    private VisualElement _modalRoot;
    private VisualElement _grid;
    private VisualElement _previewFill;
    private VisualElement _previewTemp;
    private Label _levelCurrent;
    private Label _levelNext;
    private VisualElement _activeWindow;
    private int _activeLeaderKey;
    private int _baseAffinity;
    private bool _modalOpen;

    public override void OnInit()
    {
        // Give the shared model a warn sink so its otherwise-silent leader-resolution catches are
        // diagnosable in the player log.
        Affinity.Warn = msg => Context.Log.Warn(msg);

        // The Armory and mission-prep screens both host the same UnitWindow leader section, so each gets
        // the same affinity injections (badge, gift button, gift flyout).
        RegisterScreen<ArmoryUIScreen>();
        RegisterScreen<MissionPrepUIScreen>();

        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "SetLeader", OnWindowChanged);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "Refresh", OnWindowChanged);
    }

    public override void OnUnload()
    {
        // Drop the warn sink so the shared static does not keep this (torn-down) system's Context
        // alive, mirroring how VoymastinaFormSwapSystem unsubscribes Affinity.Changed.
        Affinity.Warn = null;
    }

    // Inject the affinity badge, gift button, and gift flyout onto every UnitWindow of the given screen.
    private void RegisterScreen<TScreen>() where TScreen : Il2CppMenace.UI.UIScreen
    {
        // The affinity badge, pinned to the top-left of the leader portrait (LeaderMask holds the render).
        UI.InjectEach(
            UiTarget.Screen<TScreen>()
                .Each(UiSelector.TypeName("UnitWindow"))
                .AppendTo(UiSelector.Name("LeaderMask")),
            "affinity/affinity-badge",
            BindBadge);

        // The gift button (a native-style IconButton). OpenModal resolves the flyout from the clicked window.
        UI.InjectEach(
            UiTarget.Screen<TScreen>()
                .Each(UiSelector.TypeName("UnitWindow"))
                .After(UiSelector.Name("UnitWindowButton")),
            BuildGiftButton,
            (_, window) => UpdateGiftButton(window));

        // The gift flyout, one per window.
        UI.InjectEach(
            UiTarget.Screen<TScreen>()
                .Each(UiSelector.TypeName("UnitWindow"))
                .Before(UiSelector.Name("EquipmentAlternatives")),
            "affinity/gift-modal",
            WireModal);
    }

    private void OnWindowChanged(PatchInfo info)
    {
        if (info.Instance is VisualElement window)
        {
            UpdateBadge(window);
            UpdateGiftButton(window);
        }
    }

    private void BindBadge(VisualElement badge, VisualElement window)
    {
        try
        {
            badge.style.position = new StyleEnum<Position>(Position.Absolute);
            badge.style.left = new StyleLength(10f);
            badge.style.top = new StyleLength(72f);
        }
        catch { }

        // The unlock list rides the game's native tooltip via the SDK Tooltip component, which sticks
        // to the mouse and matches the game's look. Rebuilt on each hover so it reflects the level.
        Tooltip.OnHover(badge, () => BuildAffinityTooltip(window));

        UpdateBadge(window);
    }

    // Build the affinity tooltip for the leader on a window: a heading then one row per level, the
    // milestone levels labelled with what they unlock (from the Unlocks registry) and the rest left
    // as a dim placeholder. The leader's current level is highlighted, rows above it read as locked.
    // Returns null when the window is not one of ours (no tooltip shown).
    private Tooltip BuildAffinityTooltip(VisualElement window)
    {
        var key = Affinity.KeyFor(window);
        if (key == 0)
            return null;

        var current = Affinity.LevelFor(Context, key);
        var characterTag = Affinity.CharacterTag(Affinity.LeaderOf(window));

        var tooltip = new Tooltip("wm-affinity", 230)
            .Subheading(Locale.Text("WOMENACE::ui/affinity_unlocks", "AFFINITY UNLOCKS"))
            .Line();

        foreach (var row in Unlocks.RowsFor(characterTag))
        {
            var text = $"{row.Level:00}    {(string.IsNullOrEmpty(row.Text) ? "·" : row.Text)}";
            // Every reached level (current and below) is highlighted, including ones that grant
            // nothing. Levels still to come read as locked.
            var style = row.Level <= current ? Tooltip.Style.Positive : Tooltip.Style.Disabled;
            tooltip.Paragraph(text, style);
        }
        return tooltip;
    }

    private void UpdateBadge(VisualElement window)
    {
        var badge = UI.Find(window, UiSelector.Name("affinity-badge"));
        if (badge == null)
            return;

        var key = Affinity.KeyFor(window);
        badge.SetVisible(key != 0);

        if (key == 0)
            return;

        var level = Affinity.LevelFor(Context, key);
        var label = UI.Find(badge, UiSelector.Name("affinity-level"))?.TryCast<Label>();
        label?.text = level.ToString("00");

        var frame = UI.Find(badge, UiSelector.Name("affinity-frame"));
        if (frame != null)
            frame.SetVisible(level >= Affinity.MaxLevel);

        // Apply any level-gated unlocks the leader now qualifies for. Showing the window is the
        // moment to reconcile a save that is already past a threshold (e.g. loaded at level 2+), and
        // the grant is idempotent, so re-running it on every refresh is harmless.
        ApplyUnlocks(key, Affinity.LeaderOf(window));
    }

    // Grant the character's level-gated skins into the shared inventory once they are unlocked, so
    // they appear in the armour picker (its alternatives list is driven by owned instances). Keyed
    // off the Unlocks registry and idempotent: a skin already owned is skipped.
    private void ApplyUnlocks(int key, BaseUnitLeader leader)
    {
        try
        {
            if (key == 0 || leader == null || !leader.IsAlive())
                return;

            var level = Affinity.LevelFor(Context, key);
            var owned = Owned();
            if (owned == null)
                return;

            foreach (var id in Unlocks.UnlockedSkinArmors(Affinity.CharacterTag(leader), level))
            {
                var template = ResolveArmor(id);
                if (template == null || owned.GetInstanceCount(template) > 0)
                    continue;
                owned.AddItem(template, false, false);
                Context.Log.Info($"affinity: unlocked skin '{id}' (level {level})");
            }
        }
        catch (Exception ex) { Context.Log.Warn($"affinity: skin unlock failed: {ex.Message}"); }
    }

    // An armour template by id, cached after the first resolve.
    private BaseItemTemplate ResolveArmor(string id)
    {
        if (_armorCache.TryGetValue(id, out var cached))
            return cached;
        var found = Templates.ById<ArmorTemplate>(id, msg => Context.Log.Warn($"affinity: {msg}"));
        // Cache only a hit. Caching a miss would pin the skin as unresolvable for the session if the
        // lookup happened before the armour template was registered, so the unlock would never land.
        if (found != null)
            _armorCache[id] = found;
        return found;
    }

    // Read the rarity brackets and colours from the game's UIConfig once. Falls back to the shipped
    // values if the config is unavailable, so the borders always render.
    private void EnsureRarityPalette()
    {
        if (_rarityLoaded)
            return;
        _rarityLoaded = true;
        try
        {
            var all = DataTemplateLoader.GetAll<Il2CppMenace.UI.UIConfig>();
            var list = all?.TryCast<Il2CppSystem.Collections.Generic.IReadOnlyList<Il2CppMenace.UI.UIConfig>>();
            var config = list != null && all.Count > 0 ? list[0] : null;
            if (config == null || !config.IsAlive())
                return;
            _uncommonMinRarity = config.UncommonMinRarity;
            _rareMinRarity = config.RareMinRarity;
            _commonRarity = config.ColorCommonRarity;
            _uncommonRarity = config.ColorUncommonRarity;
            _rareRarity = config.ColorRareRarity;
        }
        catch (Exception ex) { Context.Log.Warn($"affinity: rarity palette read failed: {ex.Message}"); }
    }

    private UnityEngine.Color RarityColor(int rarity)
    {
        EnsureRarityPalette();
        if (rarity >= _rareMinRarity)
            return _rareRarity;
        if (rarity >= _uncommonMinRarity)
            return _uncommonRarity;
        return _commonRarity;
    }

    // Frame a gift tile in its rarity colour, matching the game's common/uncommon/rare palette.
    private void ApplyRarityBorder(VisualElement element, int rarity)
    {
        if (element == null)
            return;
        try
        {
            var colour = new StyleColor(RarityColor(rarity));
            element.style.borderTopColor = colour;
            element.style.borderBottomColor = colour;
            element.style.borderLeftColor = colour;
            element.style.borderRightColor = colour;
            var width = new StyleFloat(2f);
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
        }
        catch { }
    }

    private VisualElement BuildGiftButton(VisualElement window)
    {
        var button = new IconButton();
        button.Root.name = "gift-open";
        button.SetSize(22f, 22f);

        var icon = Context.Assets.Load<UnityEngine.Texture2D>("gift_icon");
        if (icon != null)
            button.SetIcon(icon);
        else
            Context.Log.Warn("gift button: 'gift_icon' texture not found in mod bundles");

        button.OnClick(() =>
        {
            try
            {
                // Toggle this window's flyout. Close whatever is open first, then open this one unless
                // it was already the open flyout, so clicking a second window's button switches to it
                // rather than only dismissing the first.
                bool reopenSameWindow = _modalOpen && ReferenceEquals(_activeWindow, window);
                CloseModal();
                if (!reopenSameWindow)
                    OpenModal(window);
            }
            catch (Exception ex)
            {
                Context.Log.Warn($"[gift] click failed: {ex.GetType().Name}: {ex.Message}");
            }
        });
        return button.Root;
    }

    private void UpdateGiftButton(VisualElement window)
    {
        var button = UI.Find(window, UiSelector.Name("gift-open"));
        if (button != null)
            button.SetVisible(Affinity.KeyFor(window) != 0);
    }

    private static int PercentBetween(int value, int floor, int ceiling)
    {
        if (ceiling <= floor)
            return 100;
        var pct = (value - floor) * 100 / (ceiling - floor);
        return pct < 0 ? 0 : pct > 100 ? 100 : pct;
    }

    // Point the modal-state fields at a specific flyout. The gift button exists on more than one
    // screen, each with its own flyout instance, so this rebinds to the clicked window's flyout.
    private void BindModalFields(VisualElement flyout)
    {
        _modalRoot = flyout;
        _grid = UI.Find(flyout, UiSelector.Name("gift-grid"));
        _previewFill = UI.Find(flyout, UiSelector.Name("preview-fill"));
        _previewTemp = UI.Find(flyout, UiSelector.Name("preview-temp"));
        _levelCurrent = UI.Find(flyout, UiSelector.Name("level-current"))?.TryCast<Label>();
        _levelNext = UI.Find(flyout, UiSelector.Name("level-next"))?.TryCast<Label>();
    }

    private void WireModal(VisualElement root, VisualElement window)
    {
        BindModalFields(root);

        // The confirm button is a TextButton built into the actions row, so it carries
        // the game's hover glow and click sound. Guarded so a re-injected modal adds it
        // once.
        var actions = UI.Find(root, UiSelector.Name("gift-actions"));
        if (actions != null && UI.Find(actions, UiSelector.Name("gift-confirm")) == null)
        {
            var confirm = new TextButton(Locale.Text("WOMENACE::ui/give", "Give"));
            confirm.Root.name = "gift-confirm";
            confirm.Root.AddToClassList("wm-gift-btn");
            confirm.OnClick(ConfirmGifts);
            actions.Add(confirm.Root);
        }

        // Dismiss on any outside click (the gift button aside, since it toggles the flyout). Hooked
        // per flyout so both the Armory and prep flyouts dismiss. Guarded against a re-injected flyout.
        if (root != null && root.panel != null && !root.ClassListContains("wm-dismiss-hooked"))
        {
            root.AddToClassList("wm-dismiss-hooked");
            UI.CloseOnOutsideClick(root, CloseModal, "gift-open");
        }
        root.SetVisible(false);
    }

    private void OpenModal(VisualElement window)
    {
        // Rebind to this window's own flyout (the gift button lives on multiple screens). WireModal
        // toggles the injection's TemplateContainer wrapper, so bind to the flyout's parent. Showing
        // the inner flyout alone leaves the wrapper hidden and nothing renders.
        var flyout = UI.Find(window, UiSelector.Name("gift-flyout"));
        var modal = flyout?.parent ?? flyout;
        if (modal != null)
            BindModalFields(modal);

        var key = Affinity.KeyFor(window);
        if (key == 0 || _modalRoot == null || _grid == null)
            return;

        _activeLeaderKey = key;
        _activeWindow = window;
        _baseAffinity = Context.State.Get<AffinityState>().ForLeader(key).Affinity;
        BuildBoxes();
        UpdatePreview();
        _modalRoot.SetVisible(true);
        _modalOpen = true;
    }

    // Fill the grid with one ItemTile per owned gift type. The component renders the native
    // loot slot, the game's hover glow, the selected border and the chosen-count badge. The
    // WOMENACE classes restyle its tile and badge.
    private void BuildBoxes()
    {
        _grid.Clear();
        _boxes.Clear();
        _chosen.Clear();
        ResolveGifts();

        var owned = Owned();
        if (owned == null)
            return;

        foreach (var gift in _gifts)
        {
            var count = owned.GetInstanceCount(gift.Template);
            if (count <= 0)
                continue;

            var slot = new ItemTile(gift.Template, count);
            slot.Root.AddToClassList("wm-gift-box");
            slot.Badge.AddToClassList("wm-gift-box__badge");
            ApplyRarityBorder(slot.Root, gift.Template.Rarity);

            var box = new Box { Gift = gift, Slot = slot };
            _boxes.Add(box);

            // Left-click adds one (left-click sound), right-click removes one (right-click sound).
            slot.OnAdjust(delta =>
            {
                if (delta < 0)
                    Sound.RightClick();
                else
                    Sound.Click();
                AdjustBox(box, delta);
            });
            UpdateBox(box);
            _grid.Add(slot.Root);
        }
    }

    // Adjust a box's chosen count, clamped to [0, owned]. Left-click adds one (stopping
    // at the owned max, so clicking a maxed stack no longer unselects it). Right-click
    // removes one (stopping at none).
    private void AdjustBox(Box box, int delta)
    {
        _chosen.TryGetValue(box.Gift, out var n);
        n += delta;
        if (n < 0)
            n = 0;
        if (n > box.Slot.Owned)
            n = box.Slot.Owned;
        if (n == 0)
            _chosen.Remove(box.Gift);
        else
            _chosen[box.Gift] = n;
        UpdateBox(box);
        UpdatePreview();
    }

    private void UpdateBox(Box box)
    {
        _chosen.TryGetValue(box.Gift, out var n);
        box.Slot.SetChosen(n);
    }

    // Frame the bar on the level the selected gifts would land the leader in, so the
    // preview shows how far into that level the points reach. The darker temp fill is the
    // projected total. The bright fill is where they sit now (it reads empty once the
    // gifts carry them past the current level into a higher one).
    private void UpdatePreview()
    {
        var gain = 0;
        foreach (var kv in _chosen)
            gain += kv.Key.Affinity * kv.Value;

        var projected = _baseAffinity + gain;
        var level = Affinity.LevelForPoints(projected);

        // Numbers flanking the bar: the level now, and the level the gifts would reach.
        _levelCurrent?.text = Affinity.LevelForPoints(_baseAffinity).ToString("00");
        _levelNext?.text = level.ToString("00");

        // Frame the bar on the projected level so the fill reads as how far into that
        // level the points land. The bright fill is where they are now (empty once the
        // gifts carry them up into this higher level). The temp fill is the projection.
        if (level >= Affinity.MaxLevel)
        {
            _previewFill.SetWidthPercent(100);
            _previewTemp.SetWidthPercent(100);
            return;
        }
        var floor = level >= 2 ? Affinity.StepThresholds[level - 2] : 0;
        var next = Affinity.StepThresholds[level - 1];
        _previewFill.SetWidthPercent(PercentBetween(_baseAffinity, floor, next));
        _previewTemp.SetWidthPercent(PercentBetween(projected, floor, next));
    }

    private void ConfirmGifts()
    {
        var owned = Owned();
        if (owned != null && _activeLeaderKey != 0)
        {
            var gained = 0;
            foreach (var kv in _chosen)
            {
                // Never consume a gift that grants nothing: removing it would destroy the item for
                // zero affinity. (No gift ships with Affinity 0 today, so this is a guard.)
                if (kv.Key.Affinity <= 0)
                    continue;
                for (var i = 0; i < kv.Value; i++)
                    if (owned.RemoveItem(kv.Key.Template))
                        gained += kv.Key.Affinity;
            }

            if (gained > 0)
            {
                var state = Context.State.Get<AffinityState>().ForLeader(_activeLeaderKey);
                state.Affinity += gained;
                // Crossing a threshold here unlocks content (e.g. skins at level 2). UpdateBadge runs
                // the unlock pass, so do it before logging the new level. Raising Changed lets the
                // form-swap button ungrey on the spot if this gift reached the mech level.
                if (_activeWindow != null)
                {
                    UpdateBadge(_activeWindow);
                    Affinity.RaiseChanged(_activeWindow);
                }
                Context.Log.Info($"gift: gave to leader {_activeLeaderKey}, points -> {state.Affinity} (level {Affinity.LevelForPoints(state.Affinity)})");
            }
        }
        CloseModal();
    }

    private void CloseModal()
    {
        _modalOpen = false;
        _chosen.Clear();
        _grid?.Clear();
        _boxes.Clear();

        if (_modalRoot != null)
            _modalRoot.SetVisible(false);
    }

    // Project the shared gift catalogue into the modal's gift rows. Name comes from the template's
    // Title and the affinity points from its TradeValue, so the whole gift roster lives in KDL.
    // Re-runs while empty (GiftCatalog does not cache an empty scan), so a modal opened before the
    // commodity templates are registered fills in once they are.
    private void ResolveGifts()
    {
        if (_gifts.Count > 0)
            return;
        foreach (var template in GiftCatalog.All())
            _gifts.Add(new Gift
            {
                Template = template,
                Name = GiftName(template),
                Affinity = template.TradeValue,
            });
    }

    private static string GiftName(CommodityTemplate template)
    {
        try
        {
            var title = template.Title;
            var text = title?.m_DefaultTranslation;
            if (!string.IsNullOrEmpty(text))
                return text;
        }
        catch { }
        return template.name;
    }

    private static OwnedItems Owned()
    {
        try
        {
            var state = StrategyState.Get();
            return state?.OwnedItems;
        }
        catch { return null; }
    }

}
