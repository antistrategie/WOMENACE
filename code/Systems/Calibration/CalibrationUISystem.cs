using Il2CppInterop.Runtime;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Game.Ui.Components;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Weapons are individual instances, so one weapon type can appear at several ranks or on
// different holders. Stock copies at the same rank share a row.
public sealed class CalibrationUISystem : JiangyuSystem
{
    public static CalibrationUISystem Instance { get; private set; }

    private VisualElement _modal;   // the wm-cal-screen root (fullscreen, hidden until opened)
    private VisualElement _list;    // wm-cal-list container
    private VisualElement _detail;  // wm-cal-detail container
    private List<CalibrationInstance> _instances = [];
    private int _selected = -1;
    private string _pendingSelectKey;  // the acted-on weapon's new key, so it stays selected after merge/revert

    public override void OnInit()
    {
        Instance = this;
        // The Workshop entry: a button under the projects list opens the calibration screen. The
        // screen itself is injected once into the screen root, hidden, and toggled by the button.
        UI.Inject(
            UiTarget.Screen<WorkshopUIScreen>().AppendTo(UiSelector.Name(WorkshopUi.Projects)),
            BuildOpenButton);
        UI.Inject(
            UiTarget.Screen<WorkshopUIScreen>().AppendTo(UiSelector.Name(WorkshopUi.Root)),
            "calibration/calibration-modal",
            WireModal);

        // The workshop's project-preview name is an ItemName label with rich text off, so a ranked
        // result weapon's marker shows as raw tags. Enable rich text on it after each render/selection.
        Context.Patches.Postfix(WorkshopUi.ScreenType, "UpdateWindow", OnWorkshopRendered);
        Context.Patches.Postfix(WorkshopUi.ScreenType, "SetSelectedBlueprint", OnWorkshopRendered);
    }

    private void OnWorkshopRendered(PatchInfo info) => EnableWorkshopItemNameRichText();

    // Turn rich text on for every ItemName label in the workshop panel (reached via the injected
    // modal's panel), so the project-preview result name renders its rank marker instead of raw tags.
    private void EnableWorkshopItemNameRichText()
    {
        var root = _modal?.panel?.visualTree;
        if (root != null)
            CalibrationSystem.EnableItemNameRichText(root);
    }

    public override void OnUnload()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    private VisualElement BuildOpenButton()
    {
        var button = new TextButton(Locale.Text("WOMENACE::ui/calibrate_weapons", "T-DOLL WEAPON CALIBRATION"));
        button.Root.name = "wm-calibrate-open";
        button.Root.style.marginTop = new StyleLength(8f);
        button.OnClick(Open);
        return button.Root;
    }

    private void WireModal(VisualElement root)
    {
        // root is the injected TemplateContainer, which Unity sizes 0x0. Stretch it to fill the
        // screen so the absolute fullscreen modal inside it has a full-size containing block, and
        // toggle THIS element's visibility (its child wm-cal-screen fills it via USS).
        _modal = root;
        UiLayout.Fill(root);
        _list = UI.Find(root, UiSelector.Name("wm-cal-list"));
        _detail = UI.Find(root, UiSelector.Name("wm-cal-detail"));
        var scroll = _list?.TryCast<ScrollView>();
        if (scroll != null)
        {
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            // The native scroll content uses a row independently of its scrolling direction.
            scroll.contentContainer.style.flexDirection = FlexDirection.Column;
            scroll.contentContainer.style.flexWrap = Wrap.NoWrap;
        }
        UI.Localise(root);

        // The close X is a real Button: wire its clickable (the proven click path), not a
        // PointerDown callback which the Button's own Clickable can swallow.
        var close = UI.Find(root, UiSelector.Name("wm-cal-close"))?.TryCast<Button>();
        if (close != null)
            close.clickable.clicked += (Action)(() => { Sound.Click(); Hide(); });
        // The blocker is a plain element; a PointerDown outside the panel dismisses.
        var blocker = UI.Find(root, UiSelector.Name("wm-cal-blocker"));
        blocker?.RegisterCallback<PointerDownEvent>(
            DelegateSupport.ConvertDelegate<EventCallback<PointerDownEvent>>((Action<PointerDownEvent>)(_ => Hide())));

        root.SetVisible(false);
    }

    // --- open / populate ------------------------------------------------------------------------

    public object DevOpen()
    {
        Open();
        return _modal != null ? new { ok = true, instances = _instances.Count } : new { error = "modal not injected (open the Workshop first)" };
    }

    public void Open()
    {
        if (_modal == null)
            return;
        // Keep the acted-on weapon selected across a merge/revert (its rank changed, so its own key
        // changed): prefer the pending key set by Act, else re-find the currently selected weapon.
        var key = _pendingSelectKey
            ?? (_selected >= 0 && _selected < _instances.Count ? SelectionKey(_instances[_selected]) : null);
        _instances = GroupStock(CalibrationSystem.Instance?.Instances() ?? []);
        _selected = key != null ? _instances.FindIndex(i => SelectionKey(i) == key) : -1;
        if (_selected < 0 && _instances.Count > 0)
            _selected = 0;
        RebuildList();
        RebuildDetail();
        _modal.SetVisible(true);
    }

    private void Hide() => _modal?.SetVisible(false);

    // Collapse stock copies of the same weapon at the same rank into one card (its Count carries
    // the stack size): five stock R0 Twilight Roses are one row, not five. Equipped weapons are
    // per-holder and never grouped. The group's first instance is the representative any
    // merge/revert acts on.
    private static List<CalibrationInstance> GroupStock(List<CalibrationInstance> instances)
    {
        var grouped = new List<CalibrationInstance>();
        var stockByKey = new Dictionary<string, CalibrationInstance>(StringComparer.Ordinal);
        foreach (var inst in instances)
        {
            if (inst.Leader != null)
            {
                grouped.Add(inst);
                continue;
            }
            var key = $"{inst.BaseWeaponId}|{inst.Rank}";
            if (stockByKey.TryGetValue(key, out var first))
                first.Count++;
            else
            {
                stockByKey[key] = inst;
                grouped.Add(inst);
            }
        }
        return grouped;
    }

    private static string SelectionKey(CalibrationInstance i) => $"{i.BaseWeaponId}|{i.Holder ?? "stock"}|{i.Rank}";

    private void Select(int index)
    {
        _selected = index;
        RebuildList();
        RebuildDetail();
    }

    private void Act(Func<CalibrationInstance, (bool ok, string error)> op, int rankDelta)
    {
        if (_selected < 0 || _selected >= _instances.Count)
            return;
        var target = _instances[_selected];
        var (ok, error) = op(target);
        if (ok)
            // The acted-on weapon keeps its base and holder but changes rank; re-select it so the
            // upgraded/reverted weapon stays selected instead of the list jumping to another entry.
            _pendingSelectKey = $"{target.BaseWeaponId}|{target.Holder ?? "stock"}|{target.Rank + rankDelta}";
        else
            Context.Log.Info($"calibration ui: action rejected: {error}");
        Open(); // re-enumerate + rebuild, selection restored by key
        _pendingSelectKey = null;
    }

    private void RebuildList()
    {
        if (_list == null)
            return;
        _list.Clear();
        if (_instances.Count == 0)
        {
            var empty = new Label(Locale.Text("WOMENACE::ui/no_weapons", "No calibratable weapons."));
            empty.AddToClassList("wm-cal-empty");
            _list.Add(empty);
            return;
        }
        for (var i = 0; i < _instances.Count; i++)
            _list.Add(BuildRow(i, _instances[i]));
    }

    private VisualElement BuildRow(int index, CalibrationInstance inst)
    {
        var row = new Button { focusable = false };
        row.AddToClassList("unit-equipment-slot");
        row.AddToClassList("wm-cal-row");
        if (index == _selected)
            row.AddToClassList("wm-cal-row--active");

        var banner = new VisualElement { name = "Image", pickingMode = PickingMode.Ignore };
        banner.AddToClassList("wm-cal-fill");
        var sprite = CalibrationSystem.Instance?.BannerSprite(inst.BaseWeaponId);
        if (sprite != null)
            banner.style.backgroundImage = new StyleBackground(sprite);
        row.Add(banner);
        var border = new VisualElement { pickingMode = PickingMode.Ignore };
        border.AddToClassList("unit-equipment-slot-border");
        border.AddToClassList("wm-cal-fill");
        row.Add(border);
        var selected = new VisualElement { pickingMode = PickingMode.Ignore };
        selected.AddToClassList("slot-selected-border");
        selected.AddToClassList("wm-cal-fill");
        selected.SetVisible(index == _selected);
        row.Add(selected);

        var name = new Label(inst.WeaponName) { pickingMode = PickingMode.Ignore };
        name.AddToClassList("wm-cal-name");
        row.Add(name);
        var rank = new Label($"R{inst.Rank}") { pickingMode = PickingMode.Ignore };
        rank.AddToClassList("wm-cal-rank");
        if (inst.Rank == 0)
            rank.AddToClassList("wm-cal-rank--base");
        row.Add(rank);

        var who = new Label(inst.Holder ?? StockLabel(inst)) { pickingMode = PickingMode.Ignore };
        who.AddToClassList("wm-cal-who");
        row.Add(who);

        row.clickable.clicked += (Action)(() => { Sound.Click(); Select(index); });
        return row;
    }

    private static string StockLabel(CalibrationInstance inst)
        => inst.Count > 1
            ? Locale.Format("WOMENACE::ui/stock_count", "Stock x{0}", inst.Count)
            : Locale.Text("WOMENACE::ui/stock", "Stock");

    private void RebuildDetail()
    {
        if (_detail == null)
            return;
        _detail.Clear();
        if (_selected < 0 || _selected >= _instances.Count)
            return;
        var inst = _instances[_selected];

        var head = new VisualElement();
        head.AddToClassList("wm-cal-detailhead");
        var title = new VisualElement();
        title.AddToClassList("wm-cal-detailtitle");
        var name = new Label(inst.WeaponName);
        name.AddToClassList("wm-cal-detailname");
        title.Add(name);
        if (inst.Holder != null)
        {
            var who = new Label(inst.Holder);
            who.AddToClassList("wm-cal-detailwho");
            title.Add(who);
        }
        head.Add(title);
        _detail.Add(head);

        _detail.Add(BuildTrack(inst.Rank));
        _detail.Add(BuildStats(inst));
        _detail.Add(BuildFoot(inst));
    }

    private VisualElement BuildTrack(int rank)
    {
        var track = new VisualElement();
        track.AddToClassList("wm-cal-track");
        for (var r = 0; r <= Calibration.MaxRank; r++)
        {
            var node = new Label($"R{r}");
            node.AddToClassList("wm-cal-node");
            if (r == Calibration.MaxRank)
                node.AddToClassList("wm-cal-node--last");
            if (r == rank)
                node.AddToClassList("wm-cal-node--current");
            else if (r < rank)
                node.AddToClassList("wm-cal-node--done");
            track.Add(node);
        }
        return track;
    }

    private VisualElement BuildStats(CalibrationInstance inst)
    {
        var box = new VisualElement();
        box.AddToClassList("wm-cal-stats");
        var atMax = inst.Rank >= Calibration.MaxRank;
        // Both forms are one key each, rank prefix and separator included, so the whole line is
        // the translator's to reorder rather than a fragment glued to hard-coded punctuation.
        var label = new Label(atMax
            ? Locale.Format("WOMENACE::ui/calibration/rank_max", "R{0}  ·  MAX", inst.Rank)
            : Locale.Format(
                "WOMENACE::ui/calibration/rank_step", "R{0}  →  R{1}", inst.Rank, inst.Rank + 1));
        label.AddToClassList("wm-cal-sectionlabel");
        box.Add(label);

        foreach (var d in CalibrationSystem.Instance.Deltas(inst.BaseWeaponId, inst.Rank))
        {
            var row = new VisualElement();
            row.AddToClassList("wm-cal-statrow");
            var statName = new Label(d.Name);
            statName.AddToClassList("wm-cal-statname");
            row.Add(statName);

            var vals = new VisualElement();
            vals.AddToClassList("wm-cal-statvals");
            var now = new Label(Num(d.Current));
            now.AddToClassList("wm-cal-statnow");
            vals.Add(now);
            if (d.Changed)
            {
                var arrow = new Label("→");
                arrow.AddToClassList("wm-cal-statarrow");
                vals.Add(arrow);
                var next = new Label(Num(d.Next));
                next.AddToClassList("wm-cal-statnext");
                vals.Add(next);
            }
            row.Add(vals);
            box.Add(row);
        }
        return box;
    }

    private VisualElement BuildFoot(CalibrationInstance inst)
    {
        var foot = new VisualElement();
        foot.AddToClassList("wm-cal-foot");

        var system = CalibrationSystem.Instance;
        var materials = system.UpgradeMaterials(inst);
        var canMerge = materials.Count > 0 && materials.All(material => material.Stock.Count > 0);
        var canRevert = inst.Rank >= 1;

        var costs = new VisualElement();
        costs.AddToClassList("wm-cal-costs");
        foreach (var (material, stock) in materials)
        {
            if (material == null)
                continue;
            var cost = new VisualElement();
            cost.AddToClassList("wm-cal-cost");
            var tile = new ItemTile(material, 1);
            tile.Root.name = "wm-cal-material-" + material.GetID();
            tile.Root.AddToClassList("wm-cal-material-icon");
            ItemTileStyle.Align(tile);
            foreach (var nativeLabel in UI.FindAll(tile.Root, UiSelector.Type<Label>()))
                nativeLabel.SetVisible(false);
            WorkshopItemStyle.Apply(tile.Root, material);
            cost.Add(tile.Root);
            var text = new VisualElement();
            text.AddToClassList("wm-cal-material-text");
            var name = new Label(Templates.DefaultText(material.Title)) { enableRichText = true };
            name.AddToClassList("wm-cal-material-name");
            text.Add(name);
            var owned = new Label(Locale.Format("WOMENACE::ui/calibration/material_count", "{0} / 1", stock.Count));
            owned.AddToClassList("wm-cal-material-count");
            if (stock.Count == 0)
                owned.AddToClassList("wm-cal-material-missing");
            text.Add(owned);
            cost.Add(text);
            costs.Add(cost);
        }
        if (costs.childCount > 0)
            foot.Add(costs);

        var actions = new VisualElement();
        actions.AddToClassList("wm-cal-actions");
        actions.Add(ActionButton(Locale.Text("WOMENACE::ui/calibrate_action", "CALIBRATE"), canMerge, () => Act(CalibrationSystem.Instance.Merge, 1)));
        actions.Add(ActionButton(Locale.Text("WOMENACE::ui/revert", "REVERT"), canRevert, () => Act(CalibrationSystem.Instance.Revert, -1)));
        foot.Add(actions);

        if (canRevert)
        {
            var refund = new Label(Locale.Format("WOMENACE::ui/calibration/refund", "Revert: +1 {0} R0", inst.WeaponName));
            refund.AddToClassList("wm-cal-tally");
            foot.Add(refund);
        }
        return foot;
    }

    private VisualElement ActionButton(string text, bool enabled, Action onClick)
    {
        var button = new TextButton(text, enabled);
        if (enabled)
            button.OnClick(onClick);
        else
            button.Root.AddToClassList("wm-cal-btn-disabled");
        return button.Root;
    }

    private static string Num(float v) => v == (int)v ? ((int)v).ToString() : v.ToString("0.0");
}
