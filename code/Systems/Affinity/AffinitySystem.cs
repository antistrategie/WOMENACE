using Il2CppInterop.Runtime;
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

    // Resolved SSR-weapon templates, cached so the unlock pass does not rescan the template
    // loader on every window refresh.
    private readonly Dictionary<string, WeaponTemplate> _weaponCache = new(StringComparer.Ordinal);

    // Resolved vehicle-item templates for Vehicle unlocks, cached on the same basis.
    private readonly Dictionary<string, VehicleItemTemplate> _vehicleCache = new(StringComparer.Ordinal);

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
    private Label _previewCount;
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

        // The built-in popover reward providers. Other systems (CalibrationSystem) register their own.
        AffinityTooltip.Register("proficiency", ProficiencyRewards);
        AffinityTooltip.Register("unlocks", UnlockRewards);

        ValidateUnlockEntries();
    }

    // One populated-array check per unlock entry at startup: a Skins entry without Armors (or a
    // Vehicle entry without Items, or data on an entry whose Feature never reads it) compiles fine
    // and silently unlocks nothing, so say so in the log instead.
    private void ValidateUnlockEntries()
    {
        foreach (var (tag, entries) in Unlocks.ByCharacter)
            foreach (var entry in entries)
            {
                var wantsArmors = entry.Feature == Unlocks.Feature.Skins;
                var wantsItems = entry.Feature is Unlocks.Feature.Vehicle or Unlocks.Feature.Mech;
                if (wantsArmors != entry.Armors.Length > 0 || wantsItems != entry.Items.Length > 0)
                    Context.Log.Warn($"affinity: unlock entry {tag} lv{entry.Level} ({entry.Feature}) has mismatched data arrays");
            }
    }

    public override void OnUnload()
    {
        // Drop the warn sink so the shared static does not keep this (torn-down) system's Context
        // alive, mirroring how FormSwapSystem unsubscribes Affinity.Changed.
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

        // The affinity popover, one per window: hidden until the badge is hovered, then positioned
        // beside the badge. The wrapper (the injection's TemplateContainer) is the positioned anchor.
        UI.InjectEach(
            UiTarget.Screen<TScreen>()
                .Each(UiSelector.TypeName("UnitWindow"))
                .Before(UiSelector.Name("EquipmentAlternatives")),
            "affinity/affinity-popover",
            (wrapper, _) =>
            {
                wrapper.style.position = new StyleEnum<Position>(Position.Absolute);
                wrapper.pickingMode = PickingMode.Ignore;
                wrapper.SetVisible(false);
            });
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

        // The unlock roadmap rides a custom popover (a filled rank rail with each level's rewards to
        // its right), shown while the badge is hovered and hidden on leave. Rebuilt on each hover so
        // it reflects the live level.
        WireBadgeHover(badge, window);

        UpdateBadge(window);
    }

    // Weapon proficiency rewards: the accuracy the doll gains at each affinity level (the step, not
    // the running total). None for non-proficiency dolls (no class tag).
    private static IEnumerable<AffinityTooltip.Reward> ProficiencyRewards(AffinityTooltip.Info info)
    {
        var cls = Proficiency.ClassFromSpeakerTags(info.SpeakerTags);
        if (cls == Proficiency.WeaponClass.None)
            yield break;
        for (var lvl = 1; lvl <= Affinity.MaxLevel; lvl++)
        {
            var step = Proficiency.AccuracyBonusForLevel(lvl) - Proficiency.AccuracyBonusForLevel(lvl - 1);
            if (step <= 0)
                continue;
            yield return new AffinityTooltip.Reward(lvl, $"+{step} accuracy (proficiency)", AffinityTooltip.RewardKind.Proficiency);
        }
    }

    // Unlock rewards: the doll's outfits, SSR weapon and mech form, each at the level it lands.
    private static IEnumerable<AffinityTooltip.Reward> UnlockRewards(AffinityTooltip.Info info)
    {
        foreach (var entry in Unlocks.RewardEntries(info.CharacterTag))
            yield return new AffinityTooltip.Reward(entry.Level, entry.Title.Resolve(), KindOf(entry.Feature));
    }

    private static AffinityTooltip.RewardKind KindOf(Unlocks.Feature feature) => feature switch
    {
        Unlocks.Feature.Skins => AffinityTooltip.RewardKind.Outfit,
        Unlocks.Feature.Weapon => AffinityTooltip.RewardKind.Weapon,
        Unlocks.Feature.Mech => AffinityTooltip.RewardKind.Mech,
        Unlocks.Feature.Vehicle => AffinityTooltip.RewardKind.Vehicle,
        _ => AffinityTooltip.RewardKind.Other,
    };

    // --- affinity popover -----------------------------------------------------------------------

    // Show this window's popover while the badge is hovered, hide it on leave. Guarded so a screen
    // rebuild re-binding the same badge does not stack handlers.
    private void WireBadgeHover(VisualElement badge, VisualElement window)
    {
        if (badge.ClassListContains("wm-aff-hover-hooked"))
            return;
        badge.AddToClassList("wm-aff-hover-hooked");
        var pendingPopover = new IVisualElementScheduledItem[1];
        badge.RegisterCallback<PointerEnterEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerEnterEvent>>(
            (Action<PointerEnterEvent>)(_ =>
            {
                HoverDelay.Cancel(ref pendingPopover[0]);
                pendingPopover[0] = HoverDelay.Schedule(badge, () => ShowPopover(badge, window));
            })));
        badge.RegisterCallback<PointerLeaveEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerLeaveEvent>>(
            (Action<PointerLeaveEvent>)(_ =>
            {
                HoverDelay.Cancel(ref pendingPopover[0]);
                HidePopover(window);
            })));
    }

    private void ShowPopover(VisualElement badge, VisualElement window)
    {
        try
        {
            var popover = UI.Find(window, UiSelector.Name("affinity-popover"));
            var wrapper = popover?.parent;
            if (popover == null || wrapper == null)
                return;
            if (!Populate(popover, window))
            {
                wrapper.SetVisible(false);
                return;
            }
            wrapper.style.position = new StyleEnum<Position>(Position.Absolute);
            wrapper.pickingMode = PickingMode.Ignore;

            // Reposition now (best effort) and again after layout, when the popover's real size is
            // known, so the left-of-badge and on-screen clamps use the true width and height.
            if (!popover.ClassListContains("wm-aff-geom-hooked"))
            {
                popover.AddToClassList("wm-aff-geom-hooked");
                popover.RegisterCallback<GeometryChangedEvent>(DelegateSupport.ConvertDelegate<EventCallback<GeometryChangedEvent>>(
                    (Action<GeometryChangedEvent>)(_ => Reposition(wrapper, popover, badge))));
            }
            Reposition(wrapper, popover, badge);
            wrapper.SetVisible(true);
            wrapper.BringToFront();
        }
        catch (Exception ex) { Context.Log.Warn($"affinity popover: show failed: {ex.Message}"); }
    }

    private void HidePopover(VisualElement window)
    {
        var popover = UI.Find(window, UiSelector.Name("affinity-popover"));
        popover?.parent?.SetVisible(false);
    }

    // Place the popover to the LEFT of the badge, clamped so both edges stay on screen. Uses the
    // popover's laid-out size once known (this fires again on GeometryChanged), with a sane fallback
    // before the first layout. Positions the wrapper (a 0x0 absolute anchor) in its parent's space.
    private static void Reposition(VisualElement wrapper, VisualElement popover, VisualElement badge)
    {
        try
        {
            var parent = wrapper.parent;
            if (parent == null)
                return;
            var screen = wrapper.panel?.visualTree?.worldBound ?? new UnityEngine.Rect(0f, 0f, 1920f, 1080f);
            var b = badge.worldBound;
            var size = popover.worldBound;
            var w = size.width > 1f ? size.width : 340f;
            var h = size.height > 1f ? size.height : 420f;

            var left = b.xMin - 8f - w;
            if (left < 8f)
                left = 8f;
            var top = b.yMin;
            if (top + h > screen.height - 8f)
                top = screen.height - 8f - h;
            if (top < 8f)
                top = 8f;

            var local = parent.WorldToLocal(new UnityEngine.Vector2(left, top));
            wrapper.style.left = new StyleLength(local.x);
            wrapper.style.top = new StyleLength(local.y);
        }
        catch { }
    }

    // Make the whole popover transparent to pointer picking, so the left-clamped position overlapping
    // the badge still lets the badge underneath keep its hover (no flicker) and no click is eaten.
    private static void IgnorePicking(VisualElement element)
    {
        if (element == null)
            return;
        element.pickingMode = PickingMode.Ignore;
        for (var i = 0; i < element.childCount; i++)
            IgnorePicking(element.ElementAt(i));
    }

    // Fill the popover for the window's leader: the header level, then one row per affinity level
    // (1..max) carrying that level's rewards. Returns false (no popover) when the window is not one
    // of ours or the doll earns nothing at any level.
    private bool Populate(VisualElement popover, VisualElement window)
    {
        var speakerTags = Affinity.OurSpeakerTags(Affinity.LeaderOf(window));
        var characterTag = Affinity.ParseCharacterTag(speakerTags);
        var key = Affinity.KeyForTag(characterTag);
        if (key == 0)
            return false;
        var level = Affinity.LevelFor(Context, key);

        var levelLabel = UI.Find(popover, UiSelector.Name("pop-level"))?.TryCast<Label>();
        if (levelLabel != null)
            levelLabel.text = level >= Affinity.MaxLevel ? $"LEVEL {level:00}  ·  MAX" : $"LEVEL {level:00}";

        var track = UI.Find(popover, UiSelector.Name("pop-track"));
        if (track == null)
            return false;
        track.Clear();

        var byLevel = new Dictionary<int, List<AffinityTooltip.Reward>>();
        var any = false;
        foreach (var reward in AffinityTooltip.All(new AffinityTooltip.Info(Context, characterTag, speakerTags, level)))
        {
            if (reward.Level < 1 || reward.Level > Affinity.MaxLevel)
                continue;
            if (!byLevel.TryGetValue(reward.Level, out var list))
                byLevel[reward.Level] = list = [];
            list.Add(reward);
            any = true;
        }
        if (!any)
            return false;

        for (var lvl = 1; lvl <= Affinity.MaxLevel; lvl++)
        {
            byLevel.TryGetValue(lvl, out var rewards);
            rewards?.Sort((a, b) => a.Kind.CompareTo(b.Kind));
            track.Add(BuildLevelRow(lvl, level, rewards));
        }
        IgnorePicking(popover);
        return true;
    }

    // One level's row: a rail cell (a continuous line, a gold fill up to the reached point, and a
    // node), the level number in its own fixed column so every reward line shares one indent, then
    // the level's rewards stacked to the right. The current level's fill stops AT its node, so 0
    // progress reads as "at this level", not "almost at the next".
    private static VisualElement BuildLevelRow(int lvl, int current, List<AffinityTooltip.Reward> rewards)
    {
        var state = lvl < current ? "done" : lvl == current ? "current" : "locked";
        var row = new VisualElement();
        row.AddToClassList("wm-aff-lvl-row");
        row.AddToClassList("wm-aff-lvl-row--" + state);
        if (lvl >= Affinity.MaxLevel)
            row.AddToClassList("wm-aff-lvl-row--last");

        var rail = new VisualElement();
        rail.AddToClassList("wm-aff-rail");
        var line = new VisualElement();
        line.AddToClassList("wm-aff-rail-line");
        rail.Add(line);
        if (state != "locked")
        {
            var fill = new VisualElement();
            fill.AddToClassList("wm-aff-rail-fill");
            if (state == "current")
                fill.AddToClassList("wm-aff-rail-fill--current");
            rail.Add(fill);
        }
        var node = new VisualElement();
        node.AddToClassList("wm-aff-node");
        rail.Add(node);
        row.Add(rail);

        var numLabel = new Label(lvl.ToString("00"));
        numLabel.AddToClassList("wm-aff-num");
        row.Add(numLabel);

        var body = new VisualElement();
        body.AddToClassList("wm-aff-body");
        var count = rewards?.Count ?? 0;
        for (var i = 0; i < count; i++)
            body.Add(UnlockLabel(rewards[i]));
        row.Add(body);
        return row;
    }

    private static Label UnlockLabel(AffinityTooltip.Reward reward)
    {
        var label = new Label(reward.Text);
        label.enableRichText = true;
        label.AddToClassList("wm-aff-unlock");
        if (reward.Kind == AffinityTooltip.RewardKind.Proficiency)
            label.AddToClassList("wm-aff-unlock--acc");
        return label;
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

    // Grant the character's level-gated SSR weapons into the shared inventory once they are
    // unlocked. Keyed off the Unlocks registry and idempotent: a weapon already owned is skipped.
    // Skins need no grant: they are transmog outfits, not items, and the transmog picker reads
    // their unlock level straight from Unlocks.
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

            var characterTag = Affinity.CharacterTag(leader);

            foreach (var id in Unlocks.UnlockedWeapons(characterTag, level))
            {
                var template = Templates.Resolve<WeaponTemplate>(id, _weaponCache, msg => Context.Log.Warn($"affinity: {msg}"));
                // Ownership must count EVERY calibration rank, not just the base R0: once the player
                // calibrates, they own a ranked clone (r1-r6) and no base R0, so a base-only check
                // would think the weapon is missing and re-grant a fresh R0 on every window refresh.
                if (template == null || OwnsInstance(owned, itemId => Calibration.TryParseRank(itemId, id, out _)))
                    continue;
                owned.AddItem(template, false, false);
                Context.Log.Info($"affinity: unlocked weapon '{id}' (level {level})");
            }

            // A character's signature vehicle is theirs for as long as they have the level: a
            // Sinner or a Sinbreaker chassis lost in combat is replaced free, so losing a mission
            // never costs a doll her defining kit. The ownership check is the whole gate, so the
            // replacement lands only once the wreck is actually gone from the inventory.
            foreach (var id in Unlocks.UnlockedItems(characterTag, level))
            {
                var template = Templates.Resolve<VehicleItemTemplate>(id, _vehicleCache, msg => Context.Log.Warn($"affinity: {msg}"));
                if (template == null || OwnsInstance(owned, itemId => itemId == id))
                    continue;
                owned.AddItem(template, false, false);
                Context.Log.Info($"affinity: granted vehicle '{id}' (level {level})");
            }
        }
        catch (Exception ex) { Context.Log.Warn($"affinity: unlock failed: {ex.Message}"); }
    }

    // Whether the player owns any instance whose template id satisfies the match. The weapon path
    // matches every calibration rank of a base id (a ranked SSR's clone is not even in
    // GetAll<WeaponTemplate>, so the scan goes by id), the vehicle path matches the exact id.
    private static bool OwnsInstance(OwnedItems owned, Func<string, bool> matches)
    {
        var all = new Il2CppSystem.Collections.Generic.List<BaseItem>();
        owned.GetInstances(all);
        for (var i = 0; i < all.Count; i++)
        {
            var itemId = all[i]?.TryCast<Item>()?.GetTemplate()?.GetID();
            if (itemId != null && matches(itemId))
                return true;
        }
        return false;
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
        _previewCount = UI.Find(flyout, UiSelector.Name("preview-count"))?.TryCast<Label>();
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
            // Holding either button repeats the step, accelerating, for picking a big stack apart
            // without clicking once per gift. The click sound stays on the press: at the speed the
            // repeat reaches, one sound per step is a machine gun.
            slot.OnAdjust((delta, repeat) =>
            {
                if (!repeat)
                {
                    if (delta < 0)
                        Sound.RightClick();
                    else
                        Sound.Click();
                }
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
            _previewCount?.text = Locale.Text("WOMENACE::ui/gift_level_max", "MAX");
            return;
        }
        var floor = level >= 2 ? Affinity.StepThresholds[level - 2] : 0;
        var next = Affinity.StepThresholds[level - 1];
        _previewFill.SetWidthPercent(PercentBetween(_baseAffinity, floor, next));
        _previewTemp.SetWidthPercent(PercentBetween(projected, floor, next));

        // Centred count: points into this level over the level's span, tracking the projected
        // (post-gift) total so it climbs live as gifts are selected. e.g. 5/100.
        var span = next - floor;
        var into = projected - floor;
        if (into < 0)
            into = 0;
        else if (into > span)
            into = span;
        _previewCount?.text = $"{into}/{span}";
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

    private static string GiftName(CommodityTemplate template) => Templates.DefaultText(template?.Title, template?.name);

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
