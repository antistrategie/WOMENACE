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
    private const string OurTag = "wmgfl";
    private const string GiftTag = "wmgfl_gift";
    private const int MaxLevel = 9;

    private static readonly int[] LevelStepThresholds =
        { 100, 200, 300, 500, 800, 1300, 2100, 3400, 5500 };

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
        // The Armory and mission-prep screens both host the same UnitWindow leader section, so each gets
        // the same affinity injections (badge, gift button, gift flyout).
        RegisterScreen<ArmoryUIScreen>();
        RegisterScreen<MissionPrepUIScreen>();

        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "SetLeader", OnWindowChanged);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "Refresh", OnWindowChanged);
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
        UpdateBadge(window);
    }

    private void UpdateBadge(VisualElement window)
    {
        var badge = UI.Find(window, UiSelector.Name("affinity-badge"));
        if (badge == null)
            return;

        var key = OurLeaderKey(window);
        SetVisible(badge, key != 0);

        if (key == 0)
            return;

        var level = LevelForPoints(Context.State.Get<AffinityState>().ForLeader(key).Affinity);
        var label = UI.Find(badge, UiSelector.Name("affinity-level"))?.TryCast<Label>();
        label?.text = level.ToString("00");

        var frame = UI.Find(badge, UiSelector.Name("affinity-frame"));
        if (frame != null)
            SetVisible(frame, level >= MaxLevel);
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
                {
                    SeedGifts();
                    OpenModal(window);
                }
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
            SetVisible(button, OurLeaderKey(window) != 0);
    }

    // Deterministic FNV-1a hash so the affinity key is stable across runs (string.GetHashCode is
    // randomised per process and would break save persistence).
    private static int StableHash(string s)
    {
        unchecked
        {
            var h = (int)2166136261;
            foreach (var c in s)
                h = (h ^ c) * 16777619;
            return h;
        }
    }

    private static int LevelForPoints(int points)
    {
        var level = 1;
        foreach (var threshold in LevelStepThresholds)
        {
            if (points >= threshold)
                level++;
            else
                break;
        }
        return level > MaxLevel ? MaxLevel : level;
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
        SetVisible(root, false);
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

        var key = OurLeaderKey(window);
        if (key == 0 || _modalRoot == null || _grid == null)
            return;

        _activeLeaderKey = key;
        _activeWindow = window;
        _baseAffinity = Context.State.Get<AffinityState>().ForLeader(key).Affinity;
        BuildBoxes();
        UpdatePreview();
        SetVisible(_modalRoot, true);
        _modalOpen = true;
    }

    // Temporary test scaffolding: top the player's inventory up to a few of each gift
    // commodity when the picker is opened, so it always has stock to try.
    private void SeedGifts()
    {
        var owned = Owned();
        if (owned == null)
            return;
        ResolveGifts();
        foreach (var gift in _gifts)
        {
            try
            {
                for (var have = owned.GetInstanceCount(gift.Template); have < 5; have++)
                    owned.AddItem(gift.Template, false);
            }
            catch (Exception ex) { Context.Log.Warn($"seed gift '{gift.Name}' failed: {ex.Message}"); }
        }
        Context.Log.Info("seeded gift commodities for testing");
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
        var level = LevelForPoints(projected);

        // Numbers flanking the bar: the level now, and the level the gifts would reach.
        _levelCurrent?.text = LevelForPoints(_baseAffinity).ToString("00");
        _levelNext?.text = level.ToString("00");

        // Frame the bar on the projected level so the fill reads as how far into that
        // level the points land. The bright fill is where they are now (empty once the
        // gifts carry them up into this higher level). The temp fill is the projection.
        if (level >= MaxLevel)
        {
            SetWidth(_previewFill, 100);
            SetWidth(_previewTemp, 100);
            return;
        }
        var floor = level >= 2 ? LevelStepThresholds[level - 2] : 0;
        var next = LevelStepThresholds[level - 1];
        SetWidth(_previewFill, PercentBetween(_baseAffinity, floor, next));
        SetWidth(_previewTemp, PercentBetween(projected, floor, next));
    }

    private void ConfirmGifts()
    {
        var owned = Owned();
        if (owned != null && _activeLeaderKey != 0)
        {
            var gained = 0;
            foreach (var kv in _chosen)
                for (var i = 0; i < kv.Value; i++)
                    if (owned.RemoveItem(kv.Key.Template))
                        gained += kv.Key.Affinity;

            if (gained > 0)
            {
                var state = Context.State.Get<AffinityState>().ForLeader(_activeLeaderKey);
                state.Affinity += gained;
                if (_activeWindow != null)
                    UpdateBadge(_activeWindow);
                Context.Log.Info($"gift: gave to leader {_activeLeaderKey}, points -> {state.Affinity} (level {LevelForPoints(state.Affinity)})");
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
            SetVisible(_modalRoot, false);
    }

    // Discover every gift commodity once, by the GiftTag tag. Name comes from the
    // template's Title and the affinity points from its TradeValue, so the whole gift
    // roster lives in KDL.
    private void ResolveGifts()
    {
        if (_gifts.Count > 0)
            return;
        try
        {
            var all = DataTemplateLoader.GetAll<CommodityTemplate>();
            // GetAll is array-backed, so it is an IReadOnlyList: index it. The Il2Cpp
            // enumerator path does not advance (its boxed struct enumerator stays put).
            var list = all?.TryCast<Il2CppSystem.Collections.Generic.IReadOnlyList<CommodityTemplate>>();
            if (list == null)
            {
                Context.Log.Warn($"gift discovery: not index-able ({(all == null ? "null" : all.GetType().FullName)})");
                return;
            }
            var count = all.Count;
            for (var i = 0; i < count; i++)
            {
                var template = list[i];
                if (template == null || !template.IsAlive() || !HasGiftTag(template))
                    continue;
                _gifts.Add(new Gift
                {
                    Template = template,
                    Name = GiftName(template),
                    Affinity = template.TradeValue,
                });
            }
            Context.Log.Info($"gift discovery: scanned {count} commodity template(s), matched {_gifts.Count} gift(s)");
        }
        catch (Exception ex) { Context.Log.Warn($"gift discovery failed: {ex}"); }
    }

    private static bool HasGiftTag(CommodityTemplate template)
    {
        try
        {
            var tags = template.Tags;
            if (tags == null)
                return false;
            for (var i = 0; i < tags.Count; i++)
            {
                var name = tags[i]?.name;
                if (!string.IsNullOrEmpty(name) && name.Contains(GiftTag))
                    return true;
            }
        }
        catch { }
        return false;
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

    private int OurLeaderKey(VisualElement window)
    {
        try
        {
            var leader = window.TryCast<UnitWindow>()?.m_CurrentLeader;
            if (!leader.IsAlive())
                return 0;

            var speaker = leader.GetSpeakerTemplate();
            var tags = speaker.IsAlive() ? speaker.Tags : null;
            if (string.IsNullOrEmpty(tags) || !tags.Contains(OurTag))
                return 0;

            // Key by the speaker's tags (the character), not the leader template, so affinity is
            // shared across a character's forms (Voymastina's squad-leader and pilot share one
            // speaker, so identical tags) and stable across saves. 0 is reserved for "not ours", so a
            // tags string that happens to hash to 0 is nudged to a non-zero key.
            var key = StableHash(tags);
            return key == 0 ? 1 : key;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"relationship: leader key failed: {ex.Message}");
            return 0;
        }
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        try { element.style.display = new StyleEnum<DisplayStyle>(visible ? DisplayStyle.Flex : DisplayStyle.None); }
        catch { }
    }

    private static void SetWidth(VisualElement element, int percent)
    {
        if (element == null)
            return;
        try { element.style.width = new StyleLength(Length.Percent(percent)); }
        catch { }
    }

    public sealed class AffinityState
    {
        public Dictionary<int, LeaderState> Leaders { get; set; } = [];

        public LeaderState ForLeader(int templateGuid)
        {
            if (!Leaders.TryGetValue(templateGuid, out var state))
                Leaders[templateGuid] = state = new LeaderState();
            return state;
        }
    }

    public sealed class LeaderState
    {
        public int Affinity { get; set; }
    }
}
