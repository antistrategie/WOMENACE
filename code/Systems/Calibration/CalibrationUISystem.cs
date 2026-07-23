using Il2CppInterop.Runtime;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Game.Ui.Components;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The weapon calibration screen: a fullscreen dialog opened from the Workshop. Layout is UXML
// (calibration/calibration-modal) + USS (calibration.uss), so it wears the game's own window frame
// and styling; this code only fills the dynamic parts (the weapon list and the selected weapon's
// detail) with elements carrying the USS classes. Weapons are instances, not doll-bound, so the same
// weapon can appear more than once at different ranks.
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
            UiTarget.Screen<WorkshopUIScreen>().AppendTo(UiSelector.Name("ProjectsPanel")),
            BuildOpenButton);
        UI.Inject(
            UiTarget.Screen<WorkshopUIScreen>().AppendTo(UiSelector.Name("Root")),
            "calibration/calibration-modal",
            WireModal);

        // A weapon's duplicate blueprint is offered by the workshop once the player OWNS that weapon
        // (any rank): the normal weapon comes with the doll, the SSR unlocks at affinity Lv3. We add
        // the dupe blueprints for owned calibratable weapons into the workshop's available list before
        // it renders its projects (prefix UpdateWindow), and again on open in case OnOpened renders
        // without an UpdateWindow of its own.
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.WorkshopUIScreen", "UpdateWindow", OnWorkshopUpdate);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.WorkshopUIScreen", "OnOpened", OnWorkshopOpened);
        // The workshop's project-preview name is an ItemName label with rich text off, so a ranked
        // result weapon's marker shows as raw tags. Enable rich text on it after each render/selection.
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.WorkshopUIScreen", "UpdateWindow", OnWorkshopRendered);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.WorkshopUIScreen", "SetSelectedBlueprint", OnWorkshopRendered);
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

    // Ensure every owned calibratable weapon's duplicate blueprint is in the workshop's available
    // list, then let the game render. Idempotent (skips blueprints already offered).
    private void OnWorkshopUpdate(PatchInfo info)
    {
        InjectOwnedBlueprints((info.Instance as Il2CppSystem.Object)?.TryCast<WorkshopUIScreen>());
    }

    // On open, inject then force a render so the recipes show immediately even if OnOpened built its
    // projects list without calling UpdateWindow.
    private void OnWorkshopOpened(PatchInfo info)
    {
        var workshop = (info.Instance as Il2CppSystem.Object)?.TryCast<WorkshopUIScreen>();
        if (workshop != null && InjectOwnedBlueprints(workshop))
        {
            try { workshop.UpdateWindow(); }
            catch (Exception ex) { Context.Log.Warn($"calibration: workshop refresh failed: {ex.Message}"); }
        }
    }

    // Add the dupe blueprint for each owned calibratable weapon the workshop is not already offering.
    // Returns whether anything was added.
    private bool InjectOwnedBlueprints(WorkshopUIScreen workshop)
    {
        try
        {
            var list = workshop?.m_SortedAvailableBlueprints;
            if (list == null)
                return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var added = false;
            foreach (var inst in CalibrationSystem.Instance?.Instances() ?? [])
            {
                if (!seen.Add(inst.BaseWeaponId))
                    continue;
                var blueprint = Templates.ById<Il2CppMenace.Items.BlueprintTemplate>(Calibration.BlueprintIdFor(inst.BaseWeaponId));
                if (blueprint == null || ListHasBlueprint(list, blueprint))
                    continue;
                list.Add(blueprint);
                added = true;
            }
            return added;
        }
        catch (Exception ex) { Context.Log.Warn($"calibration: blueprint inject failed: {ex.Message}"); return false; }
    }

    private static bool ListHasBlueprint(Il2CppSystem.Collections.Generic.List<Il2CppMenace.Items.BlueprintTemplate> list, Il2CppMenace.Items.BlueprintTemplate blueprint)
    {
        var id = blueprint.GetID();
        for (var i = 0; i < list.Count; i++)
            if (list[i]?.GetID() == id)
                return true;
        return false;
    }

    public override void OnUnload()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    private VisualElement BuildOpenButton()
    {
        var button = new TextButton(Locale.Text("WOMENACE::ui/calibrate_weapons", "CALIBRATE WEAPONS"));
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
        FillParent(root);
        _list = UI.Find(root, UiSelector.Name("wm-cal-list"));
        _detail = UI.Find(root, UiSelector.Name("wm-cal-detail"));
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
        _instances = CalibrationSystem.Instance?.Instances() ?? [];
        _selected = key != null ? _instances.FindIndex(i => SelectionKey(i) == key) : -1;
        if (_selected < 0 && _instances.Count > 0)
            _selected = 0;
        RebuildList();
        RebuildDetail();
        _modal.SetVisible(true);
    }

    private void Hide() => _modal?.SetVisible(false);

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
        var row = new VisualElement();
        row.AddToClassList("wm-cal-row");
        if (index == _selected)
            row.AddToClassList("wm-cal-row--active");

        var banner = new VisualElement();
        banner.AddToClassList("wm-cal-banner");
        var sprite = CalibrationSystem.Instance?.BannerSprite(inst.BaseWeaponId);
        if (sprite != null)
            banner.style.backgroundImage = new StyleBackground(sprite);
        var name = new Label(inst.WeaponName);
        name.AddToClassList("wm-cal-name");
        banner.Add(name);
        var rank = new Label($"R{inst.Rank}");
        rank.AddToClassList("wm-cal-rank");
        if (inst.Rank == 0)
            rank.AddToClassList("wm-cal-rank--base");
        banner.Add(rank);
        row.Add(banner);

        var who = new Label(inst.Holder ?? Locale.Text("WOMENACE::ui/stock", "Stock"));
        who.AddToClassList("wm-cal-who");
        row.Add(who);

        row.RegisterCallback<PointerDownEvent>(
            DelegateSupport.ConvertDelegate<EventCallback<PointerDownEvent>>((Action<PointerDownEvent>)(_ => { Sound.Click(); Select(index); })));
        return row;
    }

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
        title.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row);
        title.style.alignItems = new StyleEnum<Align>(Align.Center);
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
        var rankLabel = new Label($"RANK {inst.Rank} / {Calibration.MaxRank}");
        rankLabel.AddToClassList("wm-cal-detailrank");
        head.Add(rankLabel);
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
        var atMax = inst.Rank >= Calibration.MaxRank;
        var label = new Label(atMax ? $"RANK {inst.Rank}  ·  MAX" : $"RANK {inst.Rank}  →  R{inst.Rank + 1}");
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

        var dupes = CalibrationSystem.Instance.DuplicateCount(inst.BaseWeaponId, inst.Item);
        var canMerge = inst.Rank < Calibration.MaxRank && dupes > 0;
        var canRevert = inst.Rank >= 1;

        var actions = new VisualElement();
        actions.AddToClassList("wm-cal-actions");
        actions.Add(ActionButton(Locale.Text("WOMENACE::ui/calibrate_action", "CALIBRATE"), canMerge, () => Act(CalibrationSystem.Instance.Merge, 1)));
        actions.Add(ActionButton(Locale.Text("WOMENACE::ui/revert", "REVERT"), canRevert, () => Act(CalibrationSystem.Instance.Revert, -1)));
        foot.Add(actions);

        var tally = new Label($"Duplicates {dupes}");
        tally.AddToClassList("wm-cal-tally");
        foot.Add(tally);
        return foot;
    }

    private static void FillParent(VisualElement e)
    {
        e.style.position = new StyleEnum<Position>(Position.Absolute);
        e.style.left = e.style.right = e.style.top = e.style.bottom = new StyleLength(0f);
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
