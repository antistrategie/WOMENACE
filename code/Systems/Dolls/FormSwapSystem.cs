using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tools;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game;
using Jiangyu.Game.Ui;
using Jiangyu.Game.Ui.Components;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The form swap: a button on the Armory (squad-menu) leader panel that converts a doll between her
// two forms. SquadLeader and Pilot are separate runtime classes, so the "swap" replaces her entry in
// the roster's hired list with the other form. Which dolls swap, and between which forms, is the
// Pairs table: Voymastina is a squad leader with a mech-pilot alternate, Papasha a squad leader with
// a vanilla-walker-pilot alternate.
//
// Each pair names its BASE form (the acquirable one: pickable at new game and dossier-granted) and
// its ALT form (reachable only through this swap). The alt form is affinity-gated exactly when the
// character has a Feature.Mech entry in Unlocks; a pair without one swaps freely. Returning to the
// base form is always free.
//
// What carries vs. what is per-form:
//   - Attributes are PER-FORM. A Pilot and a SquadLeader have different attribute sets, so each form
//     keeps its own grown values, snapshotted on the way out and restored on the way in. They are
//     never copied between forms.
//   - Statistics (one merged lifetime total, e.g. kill count) and emotional state carry across forms.
//   - Affinity carries via the shared speaker (see AffinitySystem).
//
// Both forms are kept alive and reused within a session (see DoSwap): the swap hands the roster slot
// the OTHER form's live object and stashes the outgoing one. Reusing the live object mints nothing and
// preserves everything (attributes, equipped gear, a pilot's chassis + icon) exactly. The stashes are
// session-only, so the FIRST swap to a form after a reload has no stash and rebuilds it from template +
// the persisted snapshot.
//
// Rebuilding is where care is needed. Roster.CreateUnitLeader mints a default loadout, and the items it
// equips are not the player's owned, registered instances. So ApplyForm re-equips from the global
// OwnedItems inventory instead:
//   - INFANTRY equipment binds EXISTING owned instances (OwnedItems.GetUnusedInstance), never minting.
//     Minting (ItemTemplate.CreateItem) makes an item that is not registered as owned: it equips
//     invisibly while the real owned instance still shows unequipped (e.g. 0/1) in the choose-armour
//     list. Items live in the single global inventory shared by every leader, so this never snapshots
//     or dedupes item counts.
//   - A pilot's chassis is restored the same way, but the policy depends on the pair. A pair with
//     RestrictedChassisIds (Voymastina's mech) owns a private chassis set: bind an existing owned
//     instance, grant one if none exists, and collapse duplicates. A pair without (Papasha) rides the
//     player's regular vehicles: rebind the saved choice only if an owned instance is free, never
//     grant, never dedupe, and riding none is a valid state (the armoury assigns one).
public sealed class FormSwapSystem : JiangyuSystem
{
    private sealed class FormPair
    {
        // Log/diagnostic label.
        public string Character;
        // The acquirable form: pickable at new game and granted by its dossier.
        public string BaseFormId;
        // The swap-only form: never in a pick pool or dossier.
        public string AltFormId;
        // Non-empty for a pilot form with a private chassis set (granted, deduped, never shared).
        // Empty for a pilot form that rides the player's regular vehicles.
        public string[] RestrictedChassisIds = [];
        // The chassis the pilot template's InitialVehicleItem mints, kept on a fresh rebuild.
        public string DefaultChassisId;
        public Func<string> ToAltLabel;
        // Label while the alt form is affinity-locked, formatted with the unlock level. Null for a
        // pair whose alt form is never gated.
        public Func<int, string> ToAltLockedLabel;
        public Func<string> ToBaseLabel;
        // Role names for the stats-preview tab row on hiring info panels (see OnHiringInfoInit).
        public Func<string> BaseTabLabel;
        public Func<string> AltTabLabel;
    }

    private static readonly FormPair[] Pairs =
    [
        new FormPair
        {
            Character = "voymastina",
            BaseFormId = "squad_leader.voymastina",
            AltFormId = "pilot.voymastina_mech",
            RestrictedChassisIds = ["vehicle.voymastina_mech", "vehicle.voymastina_mech_erwin"],
            DefaultChassisId = "vehicle.voymastina_mech",
            ToAltLabel = () => Locale.Text("WOMENACE::ui/deploy_sinbreaker", "DEPLOY SINBREAKER"),
            ToAltLockedLabel = lv => string.Format(
                Locale.Text("WOMENACE::ui/deploy_sinbreaker_locked", "SINBREAKER (LV.{0})"), lv),
            ToBaseLabel = () => Locale.Text("WOMENACE::ui/deploy_infantry", "DEPLOY INFANTRY"),
            BaseTabLabel = () => Locale.Text("WOMENACE::ui/form_tab_sl", "SQUAD LEADER"),
            AltTabLabel = () => Locale.Text("WOMENACE::ui/form_tab_sinbreaker", "SINBREAKER"),
        },
        new FormPair
        {
            Character = "papasha",
            BaseFormId = "squad_leader.papasha_foot",
            AltFormId = "pilot.papasha",
            ToAltLabel = () => Locale.Text("WOMENACE::ui/deploy_pilot", "DEPLOY PILOT"),
            ToBaseLabel = () => Locale.Text("WOMENACE::ui/deploy_infantry", "DEPLOY INFANTRY"),
            BaseTabLabel = () => Locale.Text("WOMENACE::ui/form_tab_sl", "SQUAD LEADER"),
            AltTabLabel = () => Locale.Text("WOMENACE::ui/form_tab_pilot", "PILOT"),
        },
    ];

    // Both forms of a pair are kept alive between swaps so each preserves its own state exactly
    // (attributes, perks, squaddies, equipment, a pilot's equipped vehicle + icon), keyed by form
    // template id. Reusing the live object avoids rebuilding it, which would mint fresh gear into the
    // global owned-items list (duplicates) and reset the form to template defaults. Session-only:
    // empty after a reload, where the first swap to a form rebuilds it from template + snapshot.
    private readonly Dictionary<string, BaseUnitLeader> _stashed = new(StringComparer.Ordinal);

    // Base-form template ids a dossier redeem is currently masking as already-hirable
    // (see OnDossierRedeemPre), so the postfix knows what to unmask.
    private readonly List<string> _maskedBaseIds = [];

    // Stats-preview leaders built for hiring info panels, keyed by form template id. Never hired,
    // never in the roster: they exist so UnitStatsAndAttributesPanel has a leader to derive stat
    // values from. Session-only.
    private readonly Dictionary<string, BaseUnitLeader> _previewLeaders = new(StringComparer.Ordinal);

    // The last leader instance a hiring info panel showed for each form template id (real roster or
    // pick-dialog instances and previews alike), so flipping back to a form re-shows the instance the
    // game itself rendered rather than a fresh preview.
    private readonly Dictionary<string, BaseUnitLeader> _shownLeaders = new(StringComparer.Ordinal);

    // Context of the most recent hiring-info render, for the tab-click re-render. One set suffices:
    // only one info panel is interactable at a time.
    private int _lastHiringStatus;
    private string _shownFormId;

    // The StrategyState instance the session caches were built against. BaseUnitLeader is
    // Il2CppSystem.Object-rooted, so IsAlive is only a collected/pointer check, and the cache
    // dictionaries themselves root the wrappers: an entry from a PREVIOUS save passes every liveness
    // test, and a swap could install a leader whose items, squaddies and statistics belong to another
    // campaign. A save load builds a new StrategyState, so its identity names the session: whenever it
    // changes, every session cache is dropped (see EnsureSession).
    private IntPtr _sessionState = IntPtr.Zero;

    public static FormSwapSystem Instance { get; private set; }

    public override void OnInit()
    {
        Instance = this;
        // The swap button sits after the native unit-window button on the Armory (squad-menu) screen
        // only. It is deliberately kept off the mission-prep screen: swapping a deployed leader's
        // instance there fights the deployment system, so the form is chosen between missions instead.
        UI.InjectEach(
            UiTarget.Screen<ArmoryUIScreen>()
                .Each(UiSelector.TypeName("UnitWindow"))
                .After(UiSelector.Name("UnitWindowButton")),
            BuildSwapButton,
            (_, window) => UpdateSwapButton(window));

        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "SetLeader", OnWindowChanged);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "Refresh", OnWindowChanged);

        // Stats preview on hiring info panels (the initial-leaders pick dialog and the hiring screen
        // share HiringUnitInfo): a SQUAD LEADER / PILOT tab row above the native STATS / ATTRIBUTES
        // tabs flips the displayed template between a pair doll's two forms. Display-only: the pick
        // and hire selections are slot-based, so re-rendering the info panel changes nothing they act on.
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.HiringUnitInfo", "Init", OnHiringInfoInit);

        // Stop a dossier from rolling a duplicate of a doll whose base form is stashed out of the
        // roster while her alt form is active (the stashed form reads as unacquired then).
        // See OnDossierRedeemPre.
        Context.Patches.Prefix("Il2CppMenace.Items.DossierItemTemplate", "Redeem", OnDossierRedeemPre);
        Context.Patches.Postfix("Il2CppMenace.Items.DossierItemTemplate", "Redeem", OnDossierRedeemPost);

        // Re-evaluate the button when affinity changes (a gift), so reaching a gated form's level
        // ungreys it without leaving and re-entering the screen.
        Affinity.Changed += OnAffinityChanged;
    }

    public override void OnUnload()
    {
        Affinity.Changed -= OnAffinityChanged;
        if (Instance == this)
            Instance = null;
    }

    // Item template ids a swapped-out form re-equips on swap back: for every pair with one form on
    // the roster, the other (stashed) form's snapshot loadout. The matching owned-but-unequipped
    // instances are reserved: anything that consumes unequipped stock (the calibration merge) must
    // leave one instance per id alone, or ApplyLoadout finds nothing to re-equip on the swap back.
    public IEnumerable<string> StashedItemTemplateIds()
    {
        foreach (var pair in Pairs)
        {
            foreach (var (stashedId, activeId) in new[] { (pair.BaseFormId, pair.AltFormId), (pair.AltFormId, pair.BaseFormId) })
            {
                if (stashedId == null || activeId == null || !FormActive(activeId) || FormActive(stashedId))
                    continue;
                if (!SwapState.Forms.TryGetValue(stashedId, out var snap) || snap?.EquippedItemTemplateIds == null)
                    continue;
                foreach (var id in snap.EquippedItemTemplateIds)
                    if (id != null)
                        yield return id;
            }
        }
    }

    private void OnAffinityChanged(VisualElement window) => UpdateSwapButton(window);

    // Drop the session caches when the live StrategyState is not the one they were built against
    // (a save load or a return to the menu). Runs at every entry point that reads or writes them.
    private void EnsureSession()
    {
        try
        {
            var state = StrategyState.Get();
            var ptr = state != null ? state.Pointer : IntPtr.Zero;
            if (ptr == _sessionState)
                return;
            _sessionState = ptr;
            if (_stashed.Count > 0 || _previewLeaders.Count > 0 || _shownLeaders.Count > 0)
                Context.Log.Info("form swap: new session, dropping stashed/preview leader caches");
            _stashed.Clear();
            _previewLeaders.Clear();
            _shownLeaders.Clear();
            _shownFormId = null;
        }
        catch (Exception ex) { Context.Log.Warn($"form swap: session check failed: {ex.Message}"); }
    }

    private void OnWindowChanged(PatchInfo info)
    {
        if (info.Instance is VisualElement window)
            UpdateSwapButton(window);
    }

    // The pair a leader template id belongs to, or null.
    private static FormPair PairFor(string templateId)
    {
        if (templateId == null)
            return null;
        foreach (var pair in Pairs)
            if (pair.BaseFormId == templateId || pair.AltFormId == templateId)
                return pair;
        return null;
    }

    // The persisted per-form snapshots. Saves recorded when the swap was Voymastina-only store them
    // under the legacy VoymastinaFormSwapSystem+FormSwapState identity: fold that blob in on first
    // access, then leave it empty.
    // TODO: remove this migration (the fold-in below and the VoymastinaFormSwapSystem holder at the
    // bottom of the file) once released saves have had a release or two to fold in.
    private FormSwapState SwapState
    {
        get
        {
            var state = Context.State.Get<FormSwapState>();
            var legacy = Context.State.Get<VoymastinaFormSwapSystem.FormSwapState>();
            if (legacy.Forms.Count > 0)
            {
                foreach (var kv in legacy.Forms)
                {
                    if (kv.Value == null || state.Forms.ContainsKey(kv.Key))
                        continue;
                    state.Forms[kv.Key] = new FormSnapshot
                    {
                        Attributes = kv.Value.Attributes ?? [],
                        Perks = kv.Value.Perks ?? [],
                        SquaddieIds = kv.Value.SquaddieIds ?? [],
                        EquippedItemTemplateIds = kv.Value.EquippedItemTemplateIds ?? [],
                        VehicleTemplateId = kv.Value.VehicleTemplateId,
                    };
                }
                legacy.Forms.Clear();
                Context.Log.Info("form swap: migrated legacy Voymastina snapshots");
            }
            return state;
        }
    }

    // A dossier redeems a random not-yet-acquired leader. While a doll is in her alt form, her base
    // form is stashed OUT of the roster (DoSwap replaces her roster slot with the alt), so the game
    // reads it as never-acquired and the dossier could roll it, handing out a SECOND copy alongside
    // the alt form.
    //
    // To exclude her from the roll WITHOUT wasting the dossier, mask the base form as already-hirable
    // for the duration of the redeem: add its template to the roster's hirable list so the pick treats
    // it as already available and skips it, then remove it again in the postfix. The add + remove are
    // fully contained in the synchronous redeem, so she never actually surfaces in the recruit pool.
    private void OnDossierRedeemPre(PatchInfo info)
    {
        try
        {
            EnsureSession();
            _maskedBaseIds.Clear();
            var hirable = StrategyState.Get()?.Roster?.m_HirableLeaders;
            if (hirable == null)
                return;

            foreach (var pair in Pairs)
            {
                var baseTemplate = ResolveTemplate<UnitLeaderTemplate>(pair.BaseFormId);
                if (baseTemplate == null)
                    continue;

                // Heal a leaked mask first: if the redeem that added it threw, the postfix never ran
                // and the mask stayed in the hirable list, where the old membership check would have
                // declined to re-record it, making the leak permanent and the doll hireable as a
                // duplicate. While the doll is acquired IN EITHER FORM she is never legitimately
                // hirable, so every copy found then is a leak. While she is unacquired, a hirable
                // entry is a real dossier grant and stays.
                bool altActive = FormActive(pair.AltFormId);
                if (altActive || FormActive(pair.BaseFormId))
                    while (hirable.Contains(baseTemplate))
                        hirable.Remove(baseTemplate);

                if (!altActive)
                    continue;
                hirable.Add(baseTemplate);
                _maskedBaseIds.Add(pair.BaseFormId);
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"dossier guard (pre) failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnDossierRedeemPost(PatchInfo info)
    {
        try
        {
            if (_maskedBaseIds.Count == 0)
                return;

            var hirable = StrategyState.Get()?.Roster?.m_HirableLeaders;
            foreach (var baseId in _maskedBaseIds)
            {
                var baseTemplate = ResolveTemplate<UnitLeaderTemplate>(baseId);
                if (hirable == null || baseTemplate == null)
                    continue;

                hirable.Remove(baseTemplate);

                // Tripwire: after removing our mask the form should be gone. If it is still hirable,
                // the redeem granted it despite the mask, i.e. the game no longer skips
                // already-hirable leaders when it rolls (a behavioural change from a game update).
                // Warn loudly so it surfaces in the log rather than as a silent duplicate, and scrub
                // the extra so the failure degrades to a wasted roll, never a duplicate doll.
                if (hirable.Contains(baseTemplate))
                {
                    Context.Log.Warn($"dossier guard: {baseId} still hirable after unmask - the redeem "
                        + "picked it despite the mask. The 'skip already-hirable' assumption has broken "
                        + "(game update?). Scrubbing the duplicate; the guard needs revisiting.");
                    while (hirable.Contains(baseTemplate))
                        hirable.Remove(baseTemplate);
                }
            }
            _maskedBaseIds.Clear();
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"dossier guard (post) failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // True when this template is a pair's base form and the doll is currently wearing her alt form
    // (the base is stashed out of the roster, so the game's status lookup reads it as never
    // acquired). VanillaLeadersSystem folds this into dossier grantability, so a swapped doll is
    // treated as acquired rather than offered again.
    public static bool BaseFormStashed(string templateId)
    {
        var pair = PairFor(templateId);
        return pair != null && pair.BaseFormId == templateId && FormActive(pair.AltFormId);
    }

    // True when the form with this template id currently occupies a roster slot.
    private static bool FormActive(string templateId)
    {
        var hired = StrategyState.Get()?.Roster?.m_HiredLeaders;
        if (hired == null)
            return false;
        for (int i = 0; i < hired.Count; i++)
        {
            var t = hired[i]?.LeaderTemplate;
            if (t.IsAlive() && t.GetID() == templateId)
                return true;
        }
        return false;
    }

    // A doll with a form pair gets a SQUAD LEADER / PILOT tab row on hiring info panels, above the
    // native STATS / ATTRIBUTES tabs, flipping the DISPLAYED template between her two forms. The pick
    // dialog and hiring screen act on slot selections, never on what the info panel renders, so the
    // flip is pure preview.
    private void OnHiringInfoInit(PatchInfo info)
    {
        try
        {
            EnsureSession();
            var panel = (info.Instance as VisualElement)?.TryCast<HiringUnitInfo>();
            var leader = (info.Args is { Count: > 0 } ? info.Args[0] : null) as BaseUnitLeader;
            if (panel == null || leader == null || !leader.IsAlive())
                return;

            if (info.Args is { Count: > 1 } && info.Args[1] != null)
            {
                try { _lastHiringStatus = Convert.ToInt32(info.Args[1]); }
                catch { }
            }

            string id = leader.LeaderTemplate.IsAlive() ? leader.LeaderTemplate.GetID() : null;
            var pair = PairFor(id);

            var row = UI.Find(panel, UiSelector.Name("wm-formtabs"))?.TryCast<VisualElement>();
            if (pair == null)
            {
                if (row != null)
                    SetVisible(row, false);
                return;
            }

            _shownFormId = id;
            _shownLeaders[id] = leader;

            row ??= BuildFormTabs(panel);
            if (row == null)
                return;
            SetVisible(row, true);

            SelectTab(row, "wm-formtab-base", pair.BaseTabLabel(), id == pair.BaseFormId);
            SelectTab(row, "wm-formtab-alt", pair.AltTabLabel(), id == pair.AltFormId);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"form tabs: init failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Build the tab row and insert it directly above the native stats/attributes panel.
    private VisualElement BuildFormTabs(HiringUnitInfo panel)
    {
        try
        {
            var stats = panel.m_StatsAndAttributesPanel;
            var parent = stats?.parent;
            if (parent == null)
                return null;

            var row = new VisualElement { name = "wm-formtabs" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = new StyleLength(2f);
            // The row's parent is the info Container, which is 20px wider than the stats panel below
            // (the panel carries a 10px inset each side). Match it so the tabs line up.
            row.style.marginLeft = new StyleLength(10f);
            row.style.marginRight = new StyleLength(10f);
            row.Add(BuildFormTab(panel, "wm-formtab-base", isBase: true));
            row.Add(BuildFormTab(panel, "wm-formtab-alt", isBase: false));

            parent.Insert(parent.IndexOf(stats), row);
            return row;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"form tabs: build failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // One tab: the game's own TabButton, so the row inherits the native tab look. The parameterless
    // ctor loads the default tab UXML (the string overload treats its argument as a UXML path). Its
    // Pickable child is the clickable Button.
    private VisualElement BuildFormTab(HiringUnitInfo panel, string name, bool isBase)
    {
        var tab = new Il2CppMenace.UI.TabButton();
        tab.name = name;
        tab.style.flexGrow = 1f;
        var button = UI.Find(tab, UiSelector.TypeName("Button"))?.TryCast<Button>();
        if (button != null)
        {
            button.clickable.clicked += (Action)Jiangyu.Game.Audio.Sound.Click;
            button.clickable.clicked += (Action)(() => ShowForm(panel, isBase));
        }
        return tab;
    }

    // Re-render an info panel with the requested form. The instance shown is the one the game last
    // rendered for that form when one exists (the pick dialog's own leader, the hired roster copy),
    // else a preview minted just for display.
    private void ShowForm(HiringUnitInfo panel, bool baseForm)
    {
        try
        {
            EnsureSession();
            var pair = PairFor(_shownFormId);
            if (pair == null)
                return;
            string targetId = baseForm ? pair.BaseFormId : pair.AltFormId;
            if (targetId == _shownFormId)
                return;

            _shownLeaders.TryGetValue(targetId, out var target);
            if (target == null || !target.IsAlive())
                target = PreviewFor(targetId);
            if (target == null)
                return;

            var status = (UnitLeaderStatus)_lastHiringStatus;

            // The perk tree and the standing image on these screens are SIBLINGS of the info panel,
            // owned by the dialog/screen, so re-render through the game's shared helper that fills
            // all three (the same one the slot-click path uses).
            var ui = Il2CppMenace.UI.UIManager.Get();
            var dialog = ui?.GetCurrentDialog()?.TryCast<Il2CppMenace.UI.PickInitialLeadersDialog>();
            if (dialog != null)
            {
                HiringUIScreen.InitSelectedLeaderHiringScreenElements(
                    target, status, dialog.m_UnitInfo,
                    dialog.m_LeaderElement, dialog.m_LeaderBackgroundElement, dialog.m_Perks);
                return;
            }
            var hiring = ui?.GetActiveScreen()?.TryCast<HiringUIScreen>();
            if (hiring != null)
            {
                HiringUIScreen.InitSelectedLeaderHiringScreenElements(
                    target, status, hiring.m_UnitInfo,
                    hiring.m_LeaderElement, hiring.m_LeaderBackgroundElement, hiring.m_Perks);
                return;
            }

            // No known host: refresh the info panel alone.
            panel.Init(target, status);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"form tabs: show failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SelectTab(VisualElement row, string name, string label, bool selected)
    {
        var tab = UI.Find(row, UiSelector.Name(name))?.TryCast<Il2CppMenace.UI.TabButton>();
        if (tab == null)
            return;
        // ButtonText is a bare auto-property: it never reaches the label child, so set the text
        // element directly.
        var text = UI.Find(tab, UiSelector.Name("Text"))?.TryCast<Label>();
        if (text != null)
            text.text = label;
        tab.SetSelected(selected);
    }

    // A display-only leader for a form template: built once per session, never hired, never in the
    // roster. It exists so UnitStatsAndAttributesPanel has a leader to derive stat values from.
    // CreateUnitLeader mints a default loadout into the shared inventory, so the surplus is reconciled
    // straight away (the preview keeps referencing the item objects for its stat display). A
    // vehicle-form preview also mints its chassis as an owned vehicle: that record is removed the same
    // way, the preview never deploys.
    private BaseUnitLeader PreviewFor(string templateId)
    {
        try
        {
            if (_previewLeaders.TryGetValue(templateId, out var cached) && cached != null && cached.IsAlive())
                return cached;

            var template = ResolveTemplate<UnitLeaderTemplate>(templateId);
            if (template == null)
                return null;

            var before = CaptureOwnedItemCounts();
            var vehiclesBefore = OwnedVehicleGuids();
            var leader = Roster.CreateUnitLeader(template, false);
            ReconcileMintedItems(before);
            RemoveMintedVehicles(vehiclesBefore);
            if (leader == null || !leader.IsAlive())
                return null;
            _previewLeaders[templateId] = leader;
            return leader;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"form tabs: preview build failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private HashSet<string> OwnedVehicleGuids()
    {
        var guids = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var vehicles = StrategyState.Get()?.OwnedItems?.GetVehicles();
            if (vehicles != null)
                for (int i = 0; i < vehicles.Count; i++)
                    if (vehicles[i] != null && vehicles[i].IsAlive())
                        guids.Add(vehicles[i].GetItemGuid());
        }
        catch { }
        return guids;
    }

    // Remove owned vehicle records that appeared since the snapshot (a preview pilot's minted chassis).
    private void RemoveMintedVehicles(HashSet<string> before)
    {
        try
        {
            var owned = StrategyState.Get()?.OwnedItems;
            var vehicles = owned?.GetVehicles();
            if (vehicles == null)
                return;
            var minted = new List<string>();
            for (int i = 0; i < vehicles.Count; i++)
            {
                var v = vehicles[i];
                if (v != null && v.IsAlive() && !before.Contains(v.GetItemGuid()))
                    minted.Add(v.GetItemGuid());
            }
            foreach (var guid in minted)
            {
                var item = owned.GetItemByGuid(guid);
                if (item != null && item.IsAlive())
                    owned.TryRemoveVehicle(item);
            }
        }
        catch (Exception ex) { Context.Log.Warn($"form tabs: vehicle cleanup failed: {ex.Message}"); }
    }

    private VisualElement BuildSwapButton(VisualElement window)
    {
        var button = new TextButton(Locale.Text("WOMENACE::ui/swap_form", "SWAP FORM"));
        button.Root.name = "wm-formswap";
        button.Root.style.marginRight = new StyleLength(4f);
        button.OnClick(() => DoSwap(window));
        return button.Root;
    }

    // Show the button only on a doll with a form pair (either form), and label it for the target form.
    private void UpdateSwapButton(VisualElement window)
    {
        var button = UI.Find(window, UiSelector.Name("wm-formswap"))?.TryCast<VisualElement>();
        if (button == null)
            return;

        string id = CurrentTemplateId(window);
        var pair = PairFor(id);
        SetVisible(button, pair != null);
        if (pair == null)
            return;

        var label = UI.Find(button, UiSelector.TypeName("Label"))?.TryCast<Label>();

        // Returning to the base form is always allowed once the alt form is active.
        if (id == pair.AltFormId)
        {
            SetLocked(button, false);
            label?.text = pair.ToBaseLabel();
            return;
        }

        // Base form: entering the alt form is gated behind the affinity unlock level when the
        // character has one (Unlocks Feature.Mech). Below it the button is locked (greyed and
        // non-clickable) and labelled with the level it needs, so the unlock advertises itself.
        // A pair without an unlock entry swaps freely.
        var leader = window.TryCast<UnitWindow>()?.m_CurrentLeader;
        var characterTag = Affinity.CharacterTag(leader);
        if (!Unlocks.HasMech(characterTag) || Unlocks.MechUnlocked(characterTag, Affinity.LevelFor(Context, leader)))
        {
            SetLocked(button, false);
            label?.text = pair.ToAltLabel();
        }
        else
        {
            SetLocked(button, true);
            int level = Unlocks.MechLevel(characterTag);
            label?.text = pair.ToAltLockedLabel != null
                ? pair.ToAltLockedLabel(level)
                : string.Format(Locale.Text("WOMENACE::ui/swap_form_locked", "FORM LOCKED (LV.{0})"), level);
        }
    }

    // Toggle the button's locked look: SetEnabled(false) genuinely blocks the click and tags it
    // :disabled, and the wm-locked class our affinity.uss greys gives the appearance deterministically
    // (the game's theme is not relied on to style :disabled for .text-button).
    private static void SetLocked(VisualElement button, bool locked)
    {
        try
        {
            button.SetEnabled(!locked);
            if (locked)
                button.AddToClassList("wm-locked");
            else
                button.RemoveFromClassList("wm-locked");
        }
        catch { }
    }

    private void DoSwap(VisualElement window)
    {
        try
        {
            EnsureSession();
            var unitWindow = window.TryCast<UnitWindow>();
            var leader = unitWindow?.m_CurrentLeader;
            if (leader == null || !leader.IsAlive())
            {
                Context.Log.Warn("form swap: no live leader on the window");
                return;
            }

            string curId = leader.LeaderTemplate.IsAlive() ? leader.LeaderTemplate.GetID() : null;
            var pair = PairFor(curId);
            if (pair == null)
                return;
            string targetId = curId == pair.AltFormId ? pair.BaseFormId : pair.AltFormId;

            // Gate the base -> alt direction by affinity when the character has an unlock entry. The
            // button is disabled while locked, so this only matters if the swap is reached another
            // way. Returning to the base form is free.
            if (targetId == pair.AltFormId)
            {
                var characterTag = Affinity.CharacterTag(leader);
                if (Unlocks.HasMech(characterTag)
                    && !Unlocks.MechUnlocked(characterTag, Affinity.LevelFor(Context, leader)))
                {
                    Context.Log.Info($"form swap: {pair.AltFormId} locked (needs affinity level {Unlocks.MechLevel(characterTag)})");
                    return;
                }
            }

            var hired = StrategyState.Get()?.Roster?.m_HiredLeaders;
            if (hired == null)
            {
                Context.Log.Warn("form swap: no roster / hired list");
                return;
            }

            int idx = -1;
            for (int i = 0; i < hired.Count; i++)
            {
                var t = hired[i]?.LeaderTemplate;
                if (t.IsAlive() && t.GetID() == curId)
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0)
            {
                Context.Log.Warn("form swap: current leader not in hired list");
                return;
            }

            // Snapshot the outgoing form's per-form state (perks, squaddies, equipment) while it is
            // still live in the roster, so the form can be rebuilt faithfully when swapped back to.
            SnapshotForm(curId, leader);

            // Keep the outgoing form alive for reuse.
            _stashed[curId] = leader;

            // Reuse the stashed target if it is still alive this session, so nothing is minted or
            // reset. Otherwise build it fresh from template and reapply its snapshot (first swap of a
            // session, or after a reload, when no stash exists). Reusing the infantry form is safe
            // because the loader guards the off-mission SuppressionHandler null-deref that its stale
            // squad would otherwise hit when re-shown.
            _stashed.TryGetValue(targetId, out var target);
            bool reused = target != null && target.IsAlive();
            // Owned-item counts before a rebuild, so the default loadout CreateUnitLeader mints can be
            // reconciled away afterwards (see ReconcileMintedItems). Null when reusing (nothing minted).
            Dictionary<string, int> ownedBefore = null;
            if (reused)
            {
                _stashed.Remove(targetId);
            }
            else
            {
                var template = ResolveTemplate<UnitLeaderTemplate>(targetId);
                if (template == null)
                {
                    Context.Log.Warn($"form swap: target template '{targetId}' not found");
                    return;
                }
                // Snapshot the shared inventory before the build so the minted default loadout can be
                // reconciled away once the saved loadout is equipped.
                ownedBefore = CaptureOwnedItemCounts();
                target = Roster.CreateUnitLeader(template, false);
                if (target == null || !target.IsAlive())
                {
                    Context.Log.Warn("form swap: failed to obtain target leader");
                    return;
                }
                ApplyForm(targetId, target);
            }

            // Carry the shared state (the merged statistics total + emotional state) onto the
            // incoming form. Attributes, perks, squaddies and equipment stay per-form.
            CarrySharedState(leader, target);

            // Commit the roster swap first, so the swap takes effect even if a later UI step throws.
            hired[idx] = target;

            // Move any mission deployment from the outgoing form to the incoming one. Otherwise the
            // swapped-out form lingers in the battle plan and deploys alongside the new form (doubling
            // the loadout cost), with no UI handle to undeploy it since it is no longer in the roster.
            TransferDeployment(leader, target);

            // Equip the rebuilt form's saved loadout now that it is COMMITTED TO THE ROSTER. Equipping a
            // leader that is not yet hired does not register its items as in use: the equipped copy reads
            // as an unowned ghost and the real owned instance shows 0/1 in the list. Doing it after the
            // roster commit makes it stick. A reused form already carries its loadout, so skip it.
            if (!reused)
            {
                ApplyLoadout(pair, targetId, target);
                // CreateUnitLeader minted a fresh default loadout into the shared inventory. Now that the
                // saved loadout is equipped from existing instances, drop the minted surplus so the swap
                // leaves the inventory count unchanged (the leftover free copies are the duplicates).
                ReconcileMintedItems(ownedBefore);
            }
            else
            {
                ValidateReusedChassis(pair, target);
            }

            Context.Log.Info($"form swap: {curId} -> {targetId}");

            // Refresh the UI. Each step is independent: a failure in one must not strand the others.
            TryUi(() => unitWindow.SetLeader(target), "set leader");
            TryUi(() => unitWindow.Refresh(), "refresh window");
            RefreshActiveScreen(target);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"form swap failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void TryUi(Action step, string what)
    {
        try { step(); }
        catch (Exception ex) { Context.Log.Warn($"form swap: {what} failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    // The battle plan (StrategyState.BattlePlan) holds one DeployedEntity per deployed leader, keyed
    // by the leader object. ReplaceUnitInSlot moves the outgoing form's slot to the incoming form, so
    // exactly the active form deploys. The incoming form is freshly built and never deployed, so a
    // plain replace cannot duplicate it. If the outgoing form was not deployed, there is nothing to
    // move and the incoming form is left undeployed (swapping does not auto-deploy).
    private void TransferDeployment(BaseUnitLeader from, BaseUnitLeader to)
    {
        try
        {
            var plan = StrategyState.Get()?.BattlePlan;
            if (plan != null && plan.ContainsUnit(from))
                plan.ReplaceUnitInSlot(from, to);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"form swap: deployment transfer failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The leader window + 3D portrait are driven by the Armory selector, not UnitWindow.SetLeader, so
    // the swap leaves them (and the selector's m_CurrentLeader) stale. Re-point the selected slot to
    // the new leader and re-select. Only the Armory screen carries the swap button.
    private void RefreshActiveScreen(BaseUnitLeader leader)
    {
        try
        {
            var screen = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen();
            var armory = screen?.TryCast<ArmoryUIScreen>();
            if (armory == null)
                return;

            var sel = armory.m_UnitSelector;
            var slot = sel?.GetSelected();
            if (slot != null)
            {
                slot.Init(leader);
                sel.SetSelectedUnit(slot);
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"form swap: screen refresh failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Carry the state that is shared across both forms. Attributes are NOT shared: a Pilot and a
    // SquadLeader have different attribute sets, so each form keeps its own (snapshotted and restored
    // per form, see SnapshotForm/ApplyForm). Statistics share one object, so kill counts and the like
    // are a single merged lifetime total following the active form. Emotional state carries too.
    private void CarrySharedState(BaseUnitLeader from, BaseUnitLeader to)
    {
        try
        {
            // Share the statistics object so kill counts and the like are one merged lifetime total
            // following the active form.
            if (from.m_Statistics != null)
                to.m_Statistics = from.m_Statistics;

            // Carry the emotional states (and the morale buffs/debuffs they apply) onto the incoming
            // form and re-own them, so a swap does not reset her mood. The container also holds the
            // deployed-with-other / consecutive-mission counters that drive future emotion triggers.
            var emotions = from.GetEmotionalStates();
            if (emotions != null)
            {
                emotions.SetOwner(to);
                to.m_EmotionalStates = emotions;
                // The container is moved, not shared: drop the outgoing (now-stashed) form's reference
                // so it is not left holding a container owned by the incoming form. It is restored the
                // same way on the swap back.
                from.m_EmotionalStates = null;
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"form swap: shared-state carry failed: {ex.Message}");
        }
    }

    // Record a form's per-form state (attributes, perks, squaddies, equipped item templates) under its
    // template id, in persistent state, so the form can be rebuilt with its own grown state intact.
    private void SnapshotForm(string templateId, BaseUnitLeader leader)
    {
        try
        {
            var snap = SwapState.For(templateId);

            var perks = new List<string>();
            var pl = leader.m_Perks;
            if (pl != null)
                for (int i = 0; i < pl.Count; i++)
                    if (pl[i].IsAlive())
                        perks.Add(pl[i].GetID());
            snap.Perks = perks;

            var squaddies = new List<int>();
            var sl = leader.m_SquaddieIds;
            if (sl != null)
                for (int i = 0; i < sl.Count; i++)
                    squaddies.Add(sl[i]);
            snap.SquaddieIds = squaddies;

            // Equipped items only for non-vehicle forms. A vehicle form's loadout is its chassis,
            // recorded below as VehicleTemplateId. It has no item slots, so capturing (and later trying
            // to re-equip) items for it would be a snapshot that ApplyLoadout never applies.
            snap.EquippedItemTemplateIds = leader.IsVehicle()
                ? new List<string>()
                : CurrentItemTemplateIds(leader.GetItems());

            // A pilot's selected chassis is its equipment. Record the chassis item template id so a
            // rebuilt Pilot rebinds the same owned chassis rather than a minted or arbitrary one.
            // Chassis-less is a real state (vanilla-chassis pilots): record it as null rather than
            // keeping a stale previous ride that a later rebuild would silently re-bind.
            if (leader.IsVehicle())
            {
                var pilot = leader.TryCast<Pilot>();
                var veh = pilot?.GetVehicle();
                snap.VehicleTemplateId = veh != null && veh.IsAlive()
                    ? VehicleTemplateId(StrategyState.Get().OwnedItems, veh)
                    : null;
            }

            // Attributes: this form's own grown values. Pilot and SquadLeader have different attribute
            // sets, so each form's are saved and restored separately, never copied between forms.
            var attrValues = new List<float>();
            var attrs = leader.GetAttributes();
            var values = attrs != null ? attrs.GetAllCopy() : null;
            if (values != null)
                for (int i = 0; i < values.Length; i++)
                    attrValues.Add(values[i]);
            snap.Attributes = attrValues;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"form swap: snapshot failed: {ex.Message}");
        }
    }

    // Replace a freshly built form's per-form state (attributes, perks, squaddies) with the saved
    // snapshot for its template id. Equipment is applied later by ApplyLoadout (after the roster commit).
    private void ApplyForm(string templateId, BaseUnitLeader leader)
    {
        try
        {
            if (!SwapState.Forms.TryGetValue(templateId, out var snap) || snap == null)
                return;

            // Attributes: a fresh build starts at the template base, so restore this form's own saved
            // values in place (the array length matches the form's own attribute set). Pilot and
            // SquadLeader have different sets, so each form's attributes are kept separate.
            if (snap.Attributes != null && snap.Attributes.Count > 0)
            {
                var attrs = leader.GetAttributes();
                var values = attrs?.m_Values;
                if (values != null)
                {
                    int n = System.Math.Min(values.Length, snap.Attributes.Count);
                    for (int i = 0; i < n; i++)
                        values[i] = snap.Attributes[i];
                    leader.UpdatePropertiesBasedOnAttributes();
                }
            }

            // Perks: clear the template defaults, then apply the saved set (no points spent). The clear
            // loop stops at the first perk it cannot drop, so only add perks not already present, to
            // avoid stacking the saved set on top of an incompletely-cleared default set.
            while (leader.GetPerkCount() > 0 && leader.TryRemoveLastPerk()) { }
            var presentPerks = new HashSet<string>(StringComparer.Ordinal);
            var perks = leader.m_Perks;
            if (perks != null)
                for (int i = 0; i < perks.Count; i++)
                    if (perks[i].IsAlive())
                        presentPerks.Add(perks[i].GetID());
            foreach (var id in snap.Perks)
            {
                if (!presentPerks.Add(id))
                    continue;
                var perk = ResolveTemplate<PerkTemplate>(id);
                if (perk != null)
                    leader.AddPerk(perk, false);
            }

            // Squaddies: reset the roster to the saved set, then revalidate against the form's limits.
            var current = leader.m_SquaddieIds;
            if (current != null)
            {
                var existing = new List<int>();
                for (int i = 0; i < current.Count; i++)
                    existing.Add(current[i]);
                foreach (var sid in existing)
                    leader.TryRemoveSquaddie(sid);
            }
            foreach (var sid in snap.SquaddieIds)
                leader.TryAddSquaddie(sid);
            leader.ValidateSquaddies();

            // Equipment (items + a pilot's chassis) is applied SEPARATELY, after the leader is committed to
            // the roster (DoSwap -> ApplyLoadout). Equipping mid-rebuild does not register as in use.
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"form swap: restore failed: {ex.Message}");
        }
    }

    // Equip a rebuilt form's saved loadout from EXISTING owned instances. MUST run after the leader is in
    // the roster (see DoSwap): a leader equipped before it is hired does not register its items as in use,
    // so the owned copy shows 0/1 and an unowned ghost rides the slot. Items bind via GetUnusedInstance
    // (never minted). A pilot's chassis is restored the same way.
    private void ApplyLoadout(FormPair pair, string templateId, BaseUnitLeader leader)
    {
        try
        {
            SwapState.Forms.TryGetValue(templateId, out var snap);

            // A pilot's loadout is its chassis. Restore policy depends on the pair (see RestoreVehicle).
            if (leader.IsVehicle())
            {
                RestoreVehicle(pair, leader, snap);
                return;
            }

            var owned = StrategyState.Get()?.OwnedItems;
            var container = leader.GetItems();
            if (container == null || owned == null)
                return;

            // The loadout to equip: the saved snapshot, or (on the first-ever swap to this form, with no
            // snapshot yet) the form's freshly-built default items (captured before RemoveAll). Both
            // are re-equipped from OWNED instances AFTER the roster commit so they register as equipped.
            // CreateUnitLeader equips defaults pre-roster, where they read as 0/1 ghosts.
            var itemIds = snap != null && snap.EquippedItemTemplateIds != null && snap.EquippedItemTemplateIds.Count > 0
                ? snap.EquippedItemTemplateIds
                : CurrentItemTemplateIds(container);

            // container.Add appends to a slot, so clear the freshly-built defaults first to avoid
            // stacking the saved set on top of them.
            container.RemoveAll();
            foreach (var itemId in itemIds)
            {
                var tmpl = ResolveTemplate<ItemTemplate>(itemId);
                // GetUnusedInstance hands back an owned-but-unequipped instance: equipping it shows as
                // equipped in the list and changes no owned count. CreateItem would make an unregistered
                // ghost that equips invisibly (the real owned copy then shows 0/1 in the list).
                var inst = tmpl != null ? owned.GetUnusedInstance(tmpl, false) : null;
                if (inst == null)
                    Context.Log.Warn($"form swap: no owned instance to equip for '{itemId}' on restore");
                else if (!container.Add(inst, true))
                    Context.Log.Warn($"form swap: equip rejected for '{itemId}' on restore (no matching slot?)");
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"form swap: loadout apply failed: {ex.Message}");
        }
    }

    // The item-template ids currently in a leader's container, captured before a RemoveAll.
    private List<string> CurrentItemTemplateIds(ItemContainer container)
    {
        var ids = new List<string>();
        var all = container?.GetAllItems();
        if (all != null)
            for (int i = 0; i < all.Count; i++)
            {
                var it = all[i];
                var tmpl = it != null && it.IsAlive() ? it.GetTemplate() : null;
                if (tmpl != null && tmpl.IsAlive())
                    ids.Add(tmpl.GetID());
            }
        return ids;
    }

    // Owned-item instance counts keyed by item-template id, across the whole shared inventory.
    private Dictionary<string, int> CaptureOwnedItemCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var owned = StrategyState.Get()?.OwnedItems;
            if (owned == null)
                return counts;
            var buffer = new Il2CppSystem.Collections.Generic.List<BaseItem>();
            owned.GetInstances(buffer);
            for (int i = 0; i < buffer.Count; i++)
            {
                var it = buffer[i];
                var tmpl = it != null && it.IsAlive() ? it.GetBaseItemTemplate() : null;
                var id = tmpl != null && tmpl.IsAlive() ? tmpl.GetID() : null;
                if (id == null)
                    continue;
                counts[id] = counts.TryGetValue(id, out var c) ? c + 1 : 1;
            }
        }
        catch (Exception ex) { Context.Log.Warn($"form swap: owned-count snapshot failed: {ex.Message}"); }
        return counts;
    }

    // Drop the surplus instances CreateUnitLeader minted: for each item template whose owned count rose
    // above the pre-build snapshot, remove that many UNUSED instances so the count returns to before. The
    // equipped loadout (used instances) is untouched. Only free minted copies go. Vehicle templates are
    // skipped here (a pilot's chassis is reconciled in RestoreVehicle).
    private void ReconcileMintedItems(Dictionary<string, int> before)
    {
        if (before == null)
            return;
        try
        {
            var owned = StrategyState.Get()?.OwnedItems;
            if (owned == null)
                return;
            var after = CaptureOwnedItemCounts();
            foreach (var kv in after)
            {
                int surplus = kv.Value - (before.TryGetValue(kv.Key, out var b) ? b : 0);
                if (surplus <= 0)
                    continue;
                var tmpl = ResolveTemplate<ItemTemplate>(kv.Key);
                if (tmpl == null)
                    continue;   // not a regular item (e.g. a vehicle chassis), reconciled elsewhere
                for (int k = 0; k < surplus; k++)
                {
                    var unused = owned.GetUnusedInstance(tmpl, false);
                    if (unused == null)
                        break;   // only equipped copies remain, nothing more to drop
                    owned.RemoveItem(unused);
                }
            }
        }
        catch (Exception ex) { Context.Log.Warn($"form swap: mint reconcile failed: {ex.Message}"); }
    }

    // Restore a rebuilt pilot's chassis. Two policies:
    //
    // RESTRICTED (pair.RestrictedChassisIds non-empty, Voymastina's mech): the chassis set is private
    // to the doll. CreateUnitLeader mints the default chassis named by the pilot template's
    // InitialVehicleItem. Like a minted item it is not the player's owned, registered instance, so it
    // rides invisibly while the owned chassis shows unequipped in the list. Bind an existing owned
    // chassis of the saved choice instead, granting one if none is owned, then collapse duplicates.
    //
    // VANILLA (empty set, Papasha): the chassis pool is the player's regular vehicles. Rebind the
    // saved choice only from an owned instance no other pilot is riding. Never grant (that would
    // fabricate a free walker), never dedupe (players legitimately own multiples), and riding none is
    // a valid state (the armoury's native chassis picker assigns one).
    private void RestoreVehicle(FormPair pair, BaseUnitLeader leader, FormSnapshot snap)
    {
        try
        {
            var pilot = leader.TryCast<Pilot>();
            var owned = StrategyState.Get()?.OwnedItems;
            if (pilot == null || owned == null)
                return;

            if (pair.RestrictedChassisIds.Length == 0)
            {
                RestoreVanillaVehicle(pilot, owned, snap);
                return;
            }

            var minted = pilot.GetVehicle();
            string mintedGuid = minted != null && minted.IsAlive() ? minted.GetItemGuid() : null;
            string mintedId = minted != null && minted.IsAlive() ? VehicleTemplateId(owned, minted) : null;

            // The chassis to ride: the saved choice, or the default chassis on the first-ever swap
            // (no snapshot yet).
            var wantedId = snap != null && !string.IsNullOrEmpty(snap.VehicleTemplateId)
                ? snap.VehicleTemplateId
                : pair.DefaultChassisId;

            // CreateUnitLeader already minted the chassis named by the pilot template's InitialVehicleItem,
            // and the game set it up in full (modular slots + visual). When that IS the chassis we want
            // (the default on a fresh swap), keep it. Destroying it to rebind a separate owned instance
            // that was only UNLOCKED (never initialised as a modular vehicle) leaves the mech with no
            // chassis to display.
            if (mintedId != null && mintedId == wantedId)
            {
                DedupeRestrictedVehicles(pair, owned, pilot);   // collapse the spare owned copy (e.g. from InitialAdditionalUnlockedItems)
                return;
            }

            // The wanted chassis differs from the minted one (e.g. the erwin skin). Bind an owned
            // instance of the wanted choice (else any owned restricted chassis), excluding the minted one.
            var existing = FindOwnedRestrictedVehicle(pair, owned, wantedId, mintedGuid);

            // No owned instance of the wanted chassis: grant one the same way skins are granted
            // (OwnedItems.AddItem returns the new owned instance).
            if (existing == null)
            {
                var template = ResolveTemplate<VehicleItemTemplate>(wantedId);
                if (template != null)
                {
                    existing = owned.AddItem(template, false, false)?.TryCast<Vehicle>();
                    Context.Log.Info($"form swap: granted chassis '{wantedId}' (none owned)");
                }
            }

            if (existing != null)
            {
                // FindOwnedRestrictedVehicle falls back to any owned restricted chassis when the wanted
                // one is not found. Surface that divergence rather than silently binding the wrong
                // skin/stats.
                var boundId = VehicleTemplateId(owned, existing);
                if (boundId != wantedId)
                    Context.Log.Warn($"form swap: chassis '{wantedId}' not owned, binding '{boundId}' instead");

                // Bind the owned chassis WITHOUT first destroying the engine-minted default, so the
                // default survives as a fallback (DedupeRestrictedVehicles collapses the surplus after).
                // The minted default is the only chassis the game fully sets up (modular slots +
                // visual). A non-default chassis selected this way may not display until a chassis
                // picker that initialises it properly exists, so never leave the mech chassis-less.
                pilot.SetVehicle(existing);
                var riding = pilot.GetVehicle();
                if ((riding == null || !riding.IsAlive()) && minted != null && minted.IsAlive())
                {
                    Context.Log.Warn($"form swap: chassis '{wantedId}' did not bind, keeping the default chassis");
                    pilot.SetVehicle(minted);
                }
            }
            else
            {
                Context.Log.Warn($"form swap: no chassis to bind for '{wantedId}' (minted present: {mintedGuid != null})");
            }

            DedupeRestrictedVehicles(pair, owned, pilot);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"form swap: vehicle restore failed: {ex.Message}");
        }
    }

    // A vehicle form reused from the stash still holds the chassis binding it went to sleep with,
    // but a stashed pilot is invisible to the armoury's chassis picker, so another pilot may have
    // taken that walker in the meantime. Riding a taken chassis would put two pilots on one vehicle:
    // rebind a free owned instance of the same template, else ride nothing (the armoury assigns one).
    // Restricted pairs are exempt, their chassis never enter the shared pool.
    private void ValidateReusedChassis(FormPair pair, BaseUnitLeader leader)
    {
        try
        {
            if (pair.RestrictedChassisIds.Length > 0 || !leader.IsVehicle())
                return;
            var pilot = leader.TryCast<Pilot>();
            var owned = StrategyState.Get()?.OwnedItems;
            var riding = pilot?.GetVehicle();
            if (pilot == null || owned == null || riding == null || !riding.IsAlive())
                return;
            var inUse = VehicleGuidsInUse(pilot);
            if (!inUse.Contains(riding.GetItemGuid()))
                return;

            var wantedId = VehicleTemplateId(owned, riding);
            var vehicles = owned.GetVehicles();
            if (vehicles != null)
                for (int i = 0; i < vehicles.Count; i++)
                {
                    var v = vehicles[i];
                    if (v == null || !v.IsAlive() || inUse.Contains(v.GetItemGuid()))
                        continue;
                    if (VehicleTemplateId(owned, v) != wantedId)
                        continue;
                    Context.Log.Info($"form swap: chassis '{wantedId}' was taken while stashed, rebinding a free instance");
                    pilot.SetVehicle(v);
                    return;
                }
            Context.Log.Info($"form swap: chassis '{wantedId}' was taken while stashed, leaving the pilot unassigned");
            pilot.SetVehicle(null);
        }
        catch (Exception ex) { Context.Log.Warn($"form swap: reused-chassis check failed: {ex.Message}"); }
    }

    // Rebind a vanilla-chassis pilot's saved choice from an owned instance no other pilot is riding.
    // A rebuilt pilot from a template without InitialVehicleItem starts chassis-less, which is also
    // the correct end state when the saved chassis is gone or taken.
    private void RestoreVanillaVehicle(Pilot pilot, OwnedItems owned, FormSnapshot snap)
    {
        var wantedId = snap != null ? snap.VehicleTemplateId : null;
        if (string.IsNullOrEmpty(wantedId))
            return;

        var inUse = VehicleGuidsInUse(pilot);
        var vehicles = owned.GetVehicles();
        if (vehicles == null)
            return;
        for (int i = 0; i < vehicles.Count; i++)
        {
            var v = vehicles[i];
            if (v == null || !v.IsAlive() || inUse.Contains(v.GetItemGuid()))
                continue;
            if (VehicleTemplateId(owned, v) != wantedId)
                continue;
            pilot.SetVehicle(v);
            return;
        }
        Context.Log.Info($"form swap: saved chassis '{wantedId}' not free, leaving the pilot unassigned");
    }

    // Guids of vehicles currently ridden by any hired or stashed pilot other than the one given.
    private HashSet<string> VehicleGuidsInUse(Pilot except)
    {
        var guids = new HashSet<string>(StringComparer.Ordinal);
        void Collect(BaseUnitLeader leader)
        {
            var p = leader?.TryCast<Pilot>();
            if (p == null || p.Equals(except))
                return;
            var v = p.GetVehicle();
            if (v != null && v.IsAlive())
                guids.Add(v.GetItemGuid());
        }
        try
        {
            var hired = StrategyState.Get()?.Roster?.m_HiredLeaders;
            if (hired != null)
                for (int i = 0; i < hired.Count; i++)
                    Collect(hired[i]);
            foreach (var stashed in _stashed.Values)
                if (stashed != null && stashed.IsAlive())
                    Collect(stashed);
        }
        catch (Exception ex) { Context.Log.Warn($"form swap: in-use scan failed: {ex.Message}"); }
        return guids;
    }

    // An owned Vehicle from the pair's restricted set whose item template matches wantedTemplateId
    // (else any owned restricted chassis), excluding the vehicle with excludeGuid (the freshly-minted
    // one). Only the pair's own chassis are candidates, so a regular owned vehicle is never bound here.
    private Vehicle FindOwnedRestrictedVehicle(FormPair pair, OwnedItems owned, string wantedTemplateId, string excludeGuid)
    {
        var vehicles = owned.GetVehicles();
        if (vehicles == null)
            return null;
        Vehicle fallback = null;
        for (int i = 0; i < vehicles.Count; i++)
        {
            var v = vehicles[i];
            if (v == null || !v.IsAlive())
                continue;
            if (excludeGuid != null && v.GetItemGuid() == excludeGuid)
                continue;
            string id = VehicleTemplateId(owned, v);
            if (Array.IndexOf(pair.RestrictedChassisIds, id) < 0)
                continue;
            if (id == wantedTemplateId)
                return v;
            fallback ??= v;
        }
        return fallback;
    }

    // Keep at most one owned instance of each restricted chassis. Pre-fix swaps (and a rebuild) can
    // leave duplicate default chassis piled up in the shared inventory. Collapse them so the list shows
    // one of each. The chassis the Pilot is riding is always kept. Surplus copies are removed.
    private void DedupeRestrictedVehicles(FormPair pair, OwnedItems owned, Pilot pilot)
    {
        try
        {
            var riding = pilot.GetVehicle();
            string ridingGuid = riding != null && riding.IsAlive() ? riding.GetItemGuid() : null;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (ridingGuid != null)
            {
                var ridingId = VehicleTemplateId(owned, riding);
                if (ridingId != null)
                    seen.Add(ridingId);   // the riding chassis is the keeper for its template
            }

            var vehicles = owned.GetVehicles();
            if (vehicles == null)
                return;
            // Collect duplicate guids first. Do not mutate the list while iterating it.
            var duplicates = new List<string>();
            for (int i = 0; i < vehicles.Count; i++)
            {
                var v = vehicles[i];
                if (v == null || !v.IsAlive())
                    continue;
                string guid = v.GetItemGuid();
                if (guid == ridingGuid)
                    continue;
                string id = VehicleTemplateId(owned, v);
                if (Array.IndexOf(pair.RestrictedChassisIds, id) < 0)
                    continue;
                if (seen.Add(id))
                    continue;             // first of this template: keep
                duplicates.Add(guid);     // already have one: this is surplus
            }
            foreach (var guid in duplicates)
            {
                var item = owned.GetItemByGuid(guid);
                if (item != null && item.IsAlive())
                    owned.TryRemoveVehicle(item);
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"form swap: vehicle dedupe failed: {ex.Message}");
        }
    }

    // The item-template id of an owned Vehicle (e.g. vehicle.voymastina_mech), via its owned item.
    private string VehicleTemplateId(OwnedItems owned, Vehicle v)
    {
        try
        {
            var item = owned.GetItemByGuid(v.GetItemGuid());
            var tmpl = item != null && item.IsAlive() ? item.GetBaseItemTemplate() : null;
            return tmpl != null && tmpl.IsAlive() ? tmpl.GetID() : null;
        }
        catch { return null; }
    }

    // Resolve a DataTemplate by id via the shared resolver (see Templates).
    private T ResolveTemplate<T>(string id) where T : DataTemplate
        => Templates.ById<T>(id, msg => Context.Log.Warn($"form swap: {msg}"));

    private string CurrentTemplateId(VisualElement window)
    {
        try
        {
            var leader = window.TryCast<UnitWindow>()?.m_CurrentLeader;
            if (leader == null || !leader.IsAlive() || !leader.LeaderTemplate.IsAlive())
                return null;
            return leader.LeaderTemplate.GetID();
        }
        catch { return null; }
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        try { element.style.display = new StyleEnum<DisplayStyle>(visible ? DisplayStyle.Flex : DisplayStyle.None); }
        catch { }
    }

    // Persistent per-LEADER state, keyed by leader template id, so a form rebuilt after a reload
    // restores its own grown state. Items are NOT here: they live in the single global OwnedItems
    // inventory shared by every squad leader, so the swap never snapshots or recreates them. Statistics
    // and emotional state are shared across forms (carried on swap), not snapshotted.
    public sealed class FormSwapState
    {
        public Dictionary<string, FormSnapshot> Forms { get; set; } = [];

        public FormSnapshot For(string templateId)
        {
            if (!Forms.TryGetValue(templateId, out var snap) || snap == null)
                Forms[templateId] = snap = new FormSnapshot();
            return snap;
        }
    }

    public sealed class FormSnapshot
    {
        public List<float> Attributes { get; set; } = [];
        public List<string> Perks { get; set; } = [];
        public List<int> SquaddieIds { get; set; } = [];
        // The item-template ids equipped on this form. Items themselves live in the shared global
        // inventory. This records the equip CHOICE so a rebuilt form re-equips the same loadout from
        // the existing owned instances (rather than reverting to the template default).
        public List<string> EquippedItemTemplateIds { get; set; } = [];
        // A pilot form's selected chassis item template id. Null for infantry forms.
        public string VehicleTemplateId { get; set; }
    }
}

// Save-file identity for snapshots recorded when the form swap was Voymastina-only. Persisted state is
// keyed by type full name, so this holder keeps those blobs readable: FormSwapSystem.SwapState folds
// them into the multi-character FormSwapState on first access and leaves this one empty.
public sealed class VoymastinaFormSwapSystem
{
    public sealed class FormSwapState
    {
        public Dictionary<string, FormSnapshot> Forms { get; set; } = [];
    }

    public sealed class FormSnapshot
    {
        public List<float> Attributes { get; set; } = [];
        public List<string> Perks { get; set; } = [];
        public List<int> SquaddieIds { get; set; } = [];
        public List<string> EquippedItemTemplateIds { get; set; } = [];
        public string VehicleTemplateId { get; set; }
    }
}
