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

// The Voymastina form swap: a button on her Armory (squad-menu) leader panel that converts her
// between her squad-leader form and her mech-pilot form. SquadLeader and Pilot are separate runtime
// classes, so the "swap" replaces her entry in the roster's hired list with the other form.
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
// preserves everything (attributes, equipped gear, the mech's chassis + icon) exactly. The stashes are
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
//   - The MECH's chassis is restored the same way: bind an existing owned Vehicle (default or the erwin
//     skin, each separately owned) and discard the freshly-minted ghost chassis, so the equipped
//     chassis shows in the list. Duplicate owned mech chassis (from pre-fix swaps) are collapsed to one
//     of each.
public sealed class VoymastinaFormSwapSystem : JiangyuSystem
{
    private const string HumanTemplateId = "squad_leader.voymastina";
    private const string MechTemplateId = "pilot.voymastina_mech";

    // The mech's two switchable chassis (default + erwin skin). Each is a separately-owned vehicle item.
    // Exactly one of each should exist in the shared inventory.
    private const string MechDefaultVehicleId = "vehicle.voymastina_mech";
    private const string MechErwinVehicleId = "vehicle.voymastina_mech_erwin";

    // Both forms are kept alive between swaps so each preserves its own state exactly (attributes,
    // perks, squaddies, equipment, the mech's equipped vehicle + icon). Reusing the live object avoids
    // rebuilding it, which would mint fresh gear into the global owned-items list (duplicates) and
    // reset the form to template defaults. Session-only: null after a reload, where the first swap to
    // a form rebuilds it from template + snapshot.
    private BaseUnitLeader _stashedMech;
    private BaseUnitLeader _stashedHuman;

    // Set while a dossier redeem is masking Voymastina's stashed squad-leader form as already-hirable
    // (see OnDossierRedeemPre), so the postfix knows to unmask it.
    private bool _maskedHumanInHirable;

    public override void OnInit()
    {
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

        // Stop a squad-leader dossier from rolling a duplicate Voymastina while she is in her mech
        // form (her squad-leader form is stashed out of the roster then, so it reads as unacquired).
        // See OnDossierRedeemPre.
        Context.Patches.Prefix("Il2CppMenace.Items.DossierItemTemplate", "Redeem", OnDossierRedeemPre);
        Context.Patches.Postfix("Il2CppMenace.Items.DossierItemTemplate", "Redeem", OnDossierRedeemPost);

        // Re-evaluate the button when affinity changes (a gift), so reaching the mech level ungreys
        // it without leaving and re-entering the screen.
        Affinity.Changed += OnAffinityChanged;
    }

    public override void OnUnload()
    {
        Affinity.Changed -= OnAffinityChanged;
    }

    private void OnAffinityChanged(VisualElement window) => UpdateSwapButton(window);

    private void OnWindowChanged(PatchInfo info)
    {
        if (info.Instance is VisualElement window)
            UpdateSwapButton(window);
    }

    // A squad-leader dossier redeems a random not-yet-acquired squad leader. While Voymastina is in
    // her mech form, her squad-leader form (squad_leader.voymastina) is stashed OUT of the roster
    // (DoSwap replaces her roster slot with the pilot), so the game reads it as never-acquired and the
    // dossier could roll it, handing out a SECOND Voymastina squad leader alongside the mech.
    //
    // To exclude her from the roll WITHOUT wasting the dossier, mask her form as already-hirable for
    // the duration of the redeem: add her template to the roster's hirable list so the pick treats her
    // as already available and skips her, then remove it again in the postfix. The add + remove are
    // fully contained in the synchronous redeem, so she never actually surfaces in the recruit pool.
    private void OnDossierRedeemPre(PatchInfo info)
    {
        try
        {
            _maskedHumanInHirable = false;
            if (!MechFormActive())
                return;

            var hirable = StrategyState.Get()?.Roster?.m_HirableLeaders;
            var human = ResolveTemplate<UnitLeaderTemplate>(HumanTemplateId);
            if (hirable == null || human == null || hirable.Contains(human))
                return;

            hirable.Add(human);
            _maskedHumanInHirable = true;
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
            if (!_maskedHumanInHirable)
                return;
            _maskedHumanInHirable = false;

            var hirable = StrategyState.Get()?.Roster?.m_HirableLeaders;
            var human = ResolveTemplate<UnitLeaderTemplate>(HumanTemplateId);
            if (hirable == null || human == null)
                return;

            hirable.Remove(human);

            // Tripwire: after removing our mask she should be gone. If she is still hirable, the
            // redeem granted her despite the mask, i.e. the game no longer skips already-hirable
            // leaders when it rolls (a behavioural change from a game update). Warn loudly so it
            // surfaces in the log rather than as a silent duplicate, and scrub the extra so the
            // failure degrades to a wasted roll, never a duplicate Voymastina.
            if (hirable.Contains(human))
            {
                Context.Log.Warn("dossier guard: squad_leader.voymastina still hirable after unmask - "
                    + "the redeem picked her despite the mask. The 'skip already-hirable' assumption has "
                    + "broken (game update?). Scrubbing the duplicate; the guard needs revisiting.");
                while (hirable.Contains(human))
                    hirable.Remove(human);
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"dossier guard (post) failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // True when Voymastina is currently in her mech form: the pilot occupies her roster slot, so her
    // squad-leader form is stashed out of the roster and would otherwise read as unacquired.
    private bool MechFormActive()
    {
        var hired = StrategyState.Get()?.Roster?.m_HiredLeaders;
        if (hired == null)
            return false;
        for (int i = 0; i < hired.Count; i++)
        {
            var t = hired[i]?.LeaderTemplate;
            if (t.IsAlive() && t.GetID() == MechTemplateId)
                return true;
        }
        return false;
    }

    private VisualElement BuildSwapButton(VisualElement window)
    {
        var button = new TextButton(Locale.Text("WOMENACE::ui/swap_form", "SWAP FORM"));
        button.Root.name = "voymastina-formswap";
        button.Root.style.marginRight = new StyleLength(4f);
        button.OnClick(() => DoSwap(window));
        return button.Root;
    }

    // Show the button only on Voymastina (either form), and label it for the target form.
    private void UpdateSwapButton(VisualElement window)
    {
        var button = UI.Find(window, UiSelector.Name("voymastina-formswap"))?.TryCast<VisualElement>();
        if (button == null)
            return;

        string id = CurrentTemplateId(window);
        bool ours = id == HumanTemplateId || id == MechTemplateId;
        SetVisible(button, ours);
        if (!ours)
            return;

        var label = UI.Find(button, UiSelector.TypeName("Label"))?.TryCast<Label>();

        // Returning to the infantry form is always allowed once she is the mech.
        if (id == MechTemplateId)
        {
            SetLocked(button, false);
            label?.text = Locale.Text("WOMENACE::ui/deploy_on_foot", "DEPLOY ON FOOT");
            return;
        }

        // Infantry form: deploying the Sinbreaker is gated behind the affinity mech-unlock level.
        // Below it the button is locked (greyed and non-clickable) and labelled with the level it
        // needs, so the unlock advertises itself.
        var leader = window.TryCast<UnitWindow>()?.m_CurrentLeader;
        var characterTag = Affinity.CharacterTag(leader);
        if (Unlocks.MechUnlocked(characterTag, Affinity.LevelFor(Context, leader)))
        {
            SetLocked(button, false);
            label?.text = Locale.Text("WOMENACE::ui/deploy_sinbreaker", "DEPLOY SINBREAKER");
        }
        else
        {
            SetLocked(button, true);
            label?.text = string.Format(
                Locale.Text("WOMENACE::ui/deploy_sinbreaker_locked", "SINBREAKER (LV.{0})"),
                Unlocks.MechLevel(characterTag));
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
            var unitWindow = window.TryCast<UnitWindow>();
            var leader = unitWindow?.m_CurrentLeader;
            if (leader == null || !leader.IsAlive())
            {
                Context.Log.Warn("form swap: no live leader on the window");
                return;
            }

            string curId = leader.LeaderTemplate.IsAlive() ? leader.LeaderTemplate.GetID() : null;
            if (curId != HumanTemplateId && curId != MechTemplateId)
                return;
            string targetId = curId == MechTemplateId ? HumanTemplateId : MechTemplateId;

            // Gate the infantry -> mech direction by affinity. The button is disabled while locked, so
            // this only matters if the swap is reached another way. Returning to infantry is free.
            if (targetId == MechTemplateId)
            {
                var characterTag = Affinity.CharacterTag(leader);
                if (!Unlocks.MechUnlocked(characterTag, Affinity.LevelFor(Context, leader)))
                {
                    Context.Log.Info($"form swap: Sinbreaker locked (needs affinity level {Unlocks.MechLevel(characterTag)})");
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
            if (curId == MechTemplateId)
                _stashedMech = leader;
            else
                _stashedHuman = leader;

            // Reuse the stashed target if it is still alive this session, so nothing is minted or
            // reset. Otherwise build it fresh from template and reapply its snapshot (first swap of a
            // session, or after a reload, when no stash exists). Reusing the infantry form is safe
            // because the loader guards the off-mission SuppressionHandler null-deref that its stale
            // squad would otherwise hit when re-shown.
            BaseUnitLeader target = targetId == MechTemplateId ? _stashedMech : _stashedHuman;
            bool reused = target != null && target.IsAlive();
            // Owned-item counts before a rebuild, so the default loadout CreateUnitLeader mints can be
            // reconciled away afterwards (see ReconcileMintedItems). Null when reusing (nothing minted).
            Dictionary<string, int> ownedBefore = null;
            if (reused)
            {
                if (targetId == MechTemplateId)
                    _stashedMech = null;
                else
                    _stashedHuman = null;
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

            // Carry the shared state (attribute growth + the merged statistics total) onto the
            // incoming form. Perks, squaddies and equipment stay per-form.
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
                ApplyLoadout(targetId, target);
                // CreateUnitLeader minted a fresh default loadout into the shared inventory. Now that the
                // saved loadout is equipped from existing instances, drop the minted surplus so the swap
                // leaves the inventory count unchanged (the leftover free copies are the duplicates).
                ReconcileMintedItems(ownedBefore);
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
            var snap = Context.State.Get<FormSwapState>().For(templateId);

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

            // The mech's selected chassis (default vs erwin) is its equipment. Record the chassis item
            // template id so a rebuilt Pilot rebinds the same owned chassis rather than the minted default.
            if (leader.IsVehicle())
            {
                var pilot = leader.TryCast<Pilot>();
                var veh = pilot?.GetVehicle();
                if (veh != null && veh.IsAlive())
                    snap.VehicleTemplateId = VehicleTemplateId(StrategyState.Get().OwnedItems, veh);
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
            if (!Context.State.Get<FormSwapState>().Forms.TryGetValue(templateId, out var snap) || snap == null)
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

            // Equipment (items + the mech chassis) is applied SEPARATELY, after the leader is committed to
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
    // (never minted). The mech chassis is restored the same way.
    private void ApplyLoadout(string templateId, BaseUnitLeader leader)
    {
        try
        {
            Context.State.Get<FormSwapState>().Forms.TryGetValue(templateId, out var snap);

            // The mech's loadout is its chassis. Bind an owned chassis (the saved choice, or the
            // default on the first-ever swap) so the pilot rides a registered instance that shows as
            // selected, rather than the unregistered ghost CreateUnitLeader mints. The mech has no
            // item slots beyond that.
            if (leader.IsVehicle())
            {
                RestoreVehicle(leader, snap);
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
    // skipped here (the mech chassis is reconciled in RestoreVehicle).
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

    // Restore the mech's selected chassis after a rebuild. CreateUnitLeader mints a fresh default
    // chassis. Like a minted item it is not the player's owned, registered instance, so it rides
    // invisibly while the owned chassis shows unequipped in the list. Bind an existing owned chassis of
    // the saved choice instead, discarding the minted one, then collapse any duplicate owned chassis.
    private void RestoreVehicle(BaseUnitLeader leader, FormSnapshot snap)
    {
        try
        {
            var pilot = leader.TryCast<Pilot>();
            var owned = StrategyState.Get()?.OwnedItems;
            if (pilot == null || owned == null)
                return;

            var minted = pilot.GetVehicle();
            string mintedGuid = minted != null && minted.IsAlive() ? minted.GetItemGuid() : null;
            string mintedId = minted != null && minted.IsAlive() ? VehicleTemplateId(owned, minted) : null;

            // The chassis to ride: the saved choice, or the default chassis on the first-ever swap
            // (no snapshot yet).
            var wantedId = snap != null && !string.IsNullOrEmpty(snap.VehicleTemplateId)
                ? snap.VehicleTemplateId
                : MechDefaultVehicleId;

            // CreateUnitLeader already minted the chassis named by the pilot template's InitialVehicleItem,
            // and the game set it up in full (modular slots + visual). When that IS the chassis we want
            // (the default on a fresh swap), keep it. Destroying it to rebind a separate owned instance
            // that was only UNLOCKED (never initialised as a modular vehicle) leaves the mech with no
            // chassis to display.
            if (mintedId != null && mintedId == wantedId)
            {
                DedupeMechVehicles(owned, pilot);   // collapse the spare owned copy (e.g. from InitialAdditionalUnlockedItems)
                return;
            }

            // The wanted chassis differs from the minted one (e.g. the erwin skin). Bind an owned
            // instance of the wanted choice (else any owned mech chassis), excluding the minted one.
            var existing = FindOwnedVehicle(owned, wantedId, mintedGuid);

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
                // FindOwnedVehicle falls back to any owned mech chassis when the wanted one is not
                // found. Surface that divergence rather than silently binding the wrong skin/stats.
                var boundId = VehicleTemplateId(owned, existing);
                if (boundId != wantedId)
                    Context.Log.Warn($"form swap: chassis '{wantedId}' not owned, binding '{boundId}' instead");

                // Bind the owned chassis WITHOUT first destroying the engine-minted default, so the
                // default survives as a fallback (DedupeMechVehicles collapses the surplus after).
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

            DedupeMechVehicles(owned, pilot);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"form swap: vehicle restore failed: {ex.Message}");
        }
    }

    // An owned mech Vehicle whose item template matches wantedTemplateId (else any owned mech chassis),
    // excluding the vehicle with excludeGuid (the freshly-minted one). Only the two WOMENACE mech chassis
    // are candidates, so a non-mech owned vehicle is never bound here.
    private Vehicle FindOwnedVehicle(OwnedItems owned, string wantedTemplateId, string excludeGuid)
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
            if (id != MechDefaultVehicleId && id != MechErwinVehicleId)
                continue;
            if (id == wantedTemplateId)
                return v;
            fallback ??= v;
        }
        return fallback;
    }

    // Keep at most one owned instance of each mech chassis. Pre-fix swaps (and a rebuild) can leave
    // duplicate default chassis piled up in the shared inventory. Collapse them so the list shows one of
    // each. The chassis the Pilot is riding is always kept. Surplus copies are removed.
    private void DedupeMechVehicles(OwnedItems owned, Pilot pilot)
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
                if (id != MechDefaultVehicleId && id != MechErwinVehicleId)
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
        // The mech form's selected chassis item template id (default vs erwin skin). Null for infantry.
        public string VehicleTemplateId { get; set; }
    }
}
