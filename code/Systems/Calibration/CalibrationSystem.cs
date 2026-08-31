using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Game.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Weapon calibration: a doll's weapon ranks up (r1 to r6) by merging in duplicates crafted at the
// workshop from finite affinity-granted components. This system owns the component grants (an
// idempotent per-level ledger, reconciled whenever a doll's window shows or her affinity changes,
// which also back-fills saves already past component levels) and the craft, merge and split
// operations the dev verbs drive today and the calibration UI will drive later.
public sealed class CalibrationSystem : JiangyuSystem
{
    public static CalibrationSystem Instance { get; private set; }

    private readonly Dictionary<string, WeaponTemplate> _weaponCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CommodityTemplate> _componentCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BlueprintTemplate> _blueprintCache = new(StringComparer.Ordinal);

    public override void OnInit()
    {
        Instance = this;
        // Component grants reconcile on the same triggers AffinitySystem uses for its unlock pass:
        // a doll's window binding or refreshing (covers loading a save already past component
        // levels) and a live affinity change from a gift.
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "SetLeader", OnWindowChanged);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "Refresh", OnWindowChanged);
        // The weapon-select flyout builds each row fresh with rich text off, so a ranked weapon's
        // marker shows as raw tags there until we turn it on per created slot. The builder lives on
        // UnitWindowEquipment (not UnitWindow), which owns the equipment slots and their alternatives.
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindowEquipment", "CreateEquipmentAlternativeSlot", OnAlternativeSlotCreated);
        Affinity.Changed += OnAffinityChanged;
        AffinityTooltip.Register("components", ComponentRewards);
    }

    // Grant components for every hired calibratable doll as soon as a strategy scene loads, so a
    // player never has to open a specific doll's window to receive what her affinity already earned
    // (which was the "the workshop wants a component I don't have" trap).
    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        // Retry rank-name decoration here: special-weapon rank clones are not in the WeaponTemplate
        // collection yet at OnTemplatesApplied. DecorateRankNames catches any that resolve via ById;
        // DecorateFromItems catches the rest off the owned item instances (special-weapon ranks).
        DecorateRankNames();
        DecorateFromItems();

        var hired = Leaders.Hired();
        for (var i = 0; hired != null && i < hired.Count; i++)
            Reconcile(hired[i]);
    }

    // Runs after the clone templates exist. Stamp each rank weapon's name with its gold "R<N>"
    // marker (read from the base weapon's name, so it is never hand-authored per rank and the
    // marker shows everywhere the game renders the item name: tooltip heading, slot, list).
    public override void OnTemplatesApplied() => DecorateRankNames();

    // Special-weapon rank clones register a little after OnTemplatesApplied (they miss the first
    // pass), so this retries on every scene load until every rank resolves, then stops.
    private bool _ranksDecorated;

    private void DecorateRankNames()
    {
        if (_ranksDecorated)
            return;
        var allResolved = true;
        foreach (var weapon in Templates.All<WeaponTemplate>())
        {
            // Base (rank 0) doll weapons only: each DecorateWeaponRanks call covers a weapon's whole
            // rank line, SSRs included (they resolve as their own base id).
            if (Calibration.TryResolveWeaponId(weapon.GetID(), out var baseId, out var rank)
                && rank == 0 && !DecorateWeaponRanks(baseId))
                allResolved = false;
        }
        _ranksDecorated = allResolved;
    }

    // Returns whether every rank clone resolved (so the caller knows if a retry is still needed).
    private bool DecorateWeaponRanks(string baseWeaponId)
    {
        try
        {
            var baseTemplate = Weapon(baseWeaponId);
            if (baseTemplate == null)
                return false;
            // The clean base name, with any prior marker stripped so re-running never double-appends.
            var baseName = Calibration.CleanName(Templates.DefaultText(baseTemplate.Title, baseWeaponId));

            // R0 marker on the base, then r1-r6 on the clones. Clones share the base's Title
            // LocalizedLine instance, so give each its OWN fresh line rather than mutating the shared
            // one (which would rename them all). The line has no registered loca key, so GetTranslated
            // falls back to its m_DefaultTranslation.
            baseTemplate.Title = RankLine(baseWeaponId, Calibration.RankedName(baseName, 0));
            var allResolved = true;
            for (var rank = 1; rank <= Calibration.MaxRank; rank++)
            {
                var rankId = Calibration.RankId(baseWeaponId, rank);
                var template = Weapon(rankId, quiet: true);
                if (template == null)
                {
                    allResolved = false;   // clone not registered yet; retried on the next scene load
                    continue;
                }
                template.Title = RankLine(rankId, Calibration.RankedName(baseName, rank));
            }
            return allResolved;
        }
        catch (Exception ex) { Context.Log.Warn($"calibration: rank-name decorate failed for '{baseWeaponId}': {ex.Message}"); return false; }
    }

    // A fresh LocalizedLine carrying a rank name (no registered loca key, so GetTranslated falls back
    // to m_DefaultTranslation). Fresh per template so ranks never share one Title instance.
    private static Il2CppMenace.Tools.LocalizedLine RankLine(string id, string text)
        => new(Il2CppMenace.Tools.LocaCategory.Items, id, false) { m_DefaultTranslation = text };

    // Decorate rank templates via the live item instances the player owns. Special-weapon rank clones
    // are not reliably in DataTemplateLoader.GetAll<WeaponTemplate>() (so DecorateWeaponRanks' ById
    // lookup can't find them and they render as the base name), but every owned rank item references
    // its own template directly. Idempotent: skips a template whose Title already reads correctly.
    private void DecorateFromItems()
    {
        try
        {
            foreach (var inst in Instances())
            {
                if (inst.Rank <= 0 || inst.Item == null)
                    continue;
                var template = inst.Item.GetTemplate()?.TryCast<WeaponTemplate>();
                if (template == null)
                    continue;
                var baseName = Calibration.CleanName(Templates.DefaultText(Weapon(inst.BaseWeaponId, quiet: true)?.Title, inst.BaseWeaponId));
                var wanted = Calibration.RankedName(baseName, inst.Rank);
                if (Templates.DefaultText(template.Title, null) != wanted)
                    template.Title = RankLine(Calibration.RankId(inst.BaseWeaponId, inst.Rank), wanted);
            }
        }
        catch (Exception ex) { Context.Log.Warn($"calibration: item-based rank decorate failed: {ex.Message}"); }
    }

    public override void OnUnload()
    {
        Affinity.Changed -= OnAffinityChanged;
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    // Affinity-popover rewards: the weapon component the doll earns at each of its schedule levels
    // (normal at 1-6, SSR at 4-9). This is what tells the player where components come from as her
    // affinity climbs.
    private IEnumerable<AffinityTooltip.Reward> ComponentRewards(AffinityTooltip.Info info)
    {
        var normalName = ComponentName(Calibration.WeaponIdFor(info.CharacterTag));
        foreach (var lvl in Calibration.NormalComponentLevels)
            yield return new AffinityTooltip.Reward(lvl, normalName, AffinityTooltip.RewardKind.Component);
        var ssrId = Unlocks.SsrWeaponFor(info.CharacterTag);
        if (ssrId != null)
        {
            var ssrName = ComponentName(ssrId);
            foreach (var lvl in Calibration.SsrComponentLevels)
                yield return new AffinityTooltip.Reward(lvl, ssrName, AffinityTooltip.RewardKind.Component);
        }
    }

    // A component's display name (its commodity Title, e.g. "WA 2000 Weapon Component"), falling back
    // to the weapon name plus "Component" if the commodity carries no title. The fallback strips the
    // baked rank marker: a doll without calibration content yet reaches it with a decorated weapon
    // Title, and no other doll's modal shows ranks in component names.
    private string ComponentName(string weaponId)
        => Templates.DefaultText(Component(weaponId)?.Title,
            Locale.Format("WOMENACE::ui/calibration/component_of", "{0} Component",
                Calibration.CleanName(Templates.DefaultText(Weapon(weaponId, quiet: true)?.Title, weaponId))));

    private void OnWindowChanged(PatchInfo info)
    {
        if (info.Instance is not VisualElement window)
            return;
        Reconcile(Affinity.LeaderOf(window));
        DecorateFromItems();
        EnableItemNameRichText(window);
    }

    // A weapon-select flyout row, freshly built with rich text off. Turn it on so the ranked marker
    // renders instead of showing as raw tags.
    private void OnAlternativeSlotCreated(PatchInfo info)
    {
        if (info.Result is VisualElement slot)
            EnableItemNameRichText(slot);
    }

    // Item-name labels ship with rich text off, so a ranked weapon's <color> marker shows as raw
    // tags. Turn it on for every ItemName label under root, so the gold marker renders everywhere the
    // game shows the name (equipped slot, tooltip, weapon-select flyout, workshop preview: the
    // workshop hook in CalibrationUISystem shares this).
    internal static void EnableItemNameRichText(VisualElement root)
    {
        try
        {
            foreach (var e in UI.FindAll(root, UiSelector.Name("ItemName")))
            {
                var label = e?.TryCast<Label>();
                if (label != null)
                    label.enableRichText = true;
            }
        }
        catch { }
    }

    private void OnAffinityChanged(VisualElement window) => Reconcile(Affinity.LeaderOf(window));

    // Grant every component the leader's affinity level entitles her to and the ledger has not
    // recorded yet. Idempotent through the ledger alone: components are consumables, so ownership
    // can never stand in for "already granted".
    private void Reconcile(BaseUnitLeader leader)
    {
        try
        {
            var tag = Affinity.CharacterTag(leader);
            var key = Affinity.KeyForTag(tag);
            if (key == 0 || StrategyState.Get() == null)
                return;

            var level = Affinity.LevelFor(Context, key);
            var state = Context.State.Get<CalibrationState>().ForCharacter(key);
            GrantRun(Calibration.WeaponIdFor(tag), Calibration.NormalComponentLevels, state.NormalComponentLevels, level, tag);
            var ssrId = Unlocks.SsrWeaponFor(tag);
            if (ssrId != null)
                GrantRun(ssrId, Calibration.SsrComponentLevels, state.SsrComponentLevels, level, tag);
        }
        catch (Exception ex) { Context.Log.Warn($"calibration: grant reconcile failed: {ex.Message}"); }
    }

    // Weapon ids already reported as having no component template, so a real
    // typo surfaces once instead of once per window refresh.
    private readonly HashSet<string> _warnedMissingComponent = new(StringComparer.Ordinal);

    private void GrantRun(string weaponId, int[] schedule, List<int> granted, int level, string tag)
    {
        foreach (var lvl in schedule)
        {
            if (lvl > level || granted.Contains(lvl))
                continue;
            var component = Component(weaponId);
            // No component template means the weapon has no calibration
            // content yet, which is a legitimate state for a doll whose ranks
            // have not landed. It is also exactly what a mistyped component id
            // looks like, and staying silent there leaves the rank quietly
            // uncraftable with nothing in the log to explain why. Warn once per
            // weapon rather than on every window refresh.
            if (component == null)
            {
                if (_warnedMissingComponent.Add(weaponId))
                    Context.Log.Warn($"calibration: no component template for '{weaponId}', its ranks cannot be granted");
                return;
            }
            if (Inventory.AddItem(component) == null)
            {
                Context.Log.Warn($"calibration: component grant failed for '{weaponId}' at level {lvl}");
                return;
            }
            granted.Add(lvl);
            Context.Log.Info($"calibration: granted component for '{weaponId}' to {tag} (affinity level {lvl})");
        }
    }

    // Set a character's affinity points to a level's floor and re-run the grant reconcile, so the
    // retroactive back-fill (a save loaded already past component levels) is testable without gift
    // grinding. Dev-only: reachable solely through the dev-loader Weapons verb.
    public object DevSetAffinityLevel(string characterTag, int level)
    {
        var key = Affinity.KeyForTag(characterTag);
        if (key == 0)
            return new { error = $"unknown character '{characterTag}'" };
        if (StrategyState.Get() == null)
            return new { error = "no strategy state (load a campaign save first)" };

        var points = level <= 1 ? 0 : Affinity.StepThresholds[System.Math.Min(level, Affinity.MaxLevel) - 2];
        Context.State.Get<AffinityState>().ForLeader(key).Affinity = points;

        var hired = Leaders.Hired();
        for (var i = 0; hired != null && i < hired.Count; i++)
            if (Affinity.CharacterTag(hired[i]) == characterTag)
                Reconcile(hired[i]);
        return new { ok = true, level = Affinity.LevelFor(Context, key), points };
    }

    // --- core operations (the UI and the dev verbs share these) ---------------------------------

    // Every calibratable weapon the player owns, as individual instances: each equipped weapon on a
    // hired doll that is a registered weapon (any rank), plus every unequipped stock copy. Weapons
    // are not doll-bound, so the same weapon can appear more than once at different ranks.
    public List<CalibrationInstance> Instances()
    {
        var result = new List<CalibrationInstance>();
        var owned = Inventory.Owned;
        if (owned == null)
            return result;

        var equippedPtrs = new HashSet<System.IntPtr>();
        var hired = Leaders.Hired();
        for (var i = 0; hired != null && i < hired.Count; i++)
        {
            var leader = hired[i];
            var items = Leaders.EquippedItems(leader);
            for (var j = 0; items != null && j < items.Count; j++)
            {
                var item = items[j];
                if (item == null || !Calibration.TryResolveWeaponId(item.GetTemplate()?.GetID(), out var baseId, out var rank))
                    continue;
                equippedPtrs.Add(item.Pointer);
                result.Add(MakeInstance(item, baseId, rank, leader));
            }
        }

        var reserved = StashedReservations();
        var all = new Il2CppSystem.Collections.Generic.List<BaseItem>();
        owned.GetInstances(all);
        for (var i = 0; i < all.Count; i++)
        {
            var item = all[i]?.TryCast<Item>();
            if (item == null || equippedPtrs.Contains(item.Pointer) || !Calibration.TryResolveWeaponId(item.GetTemplate()?.GetID(), out var baseId, out var rank))
                continue;
            // A swapped-out form's personal weapon is owned-but-unequipped, not stock: it is
            // re-equipped on the swap back, so it must not be offered for calibration (merging would
            // swap its template identity out from under the form snapshot) or consumed as fodder.
            if (TakeReservation(reserved, item.GetTemplate()?.GetID()))
                continue;
            result.Add(MakeInstance(item, baseId, rank, null));
        }
        return result;
    }

    // One reservation per stashed-loadout entry: the weapons FormSwapSystem will re-equip when a
    // swapped-out form returns. Only that many instances are protected; extra copies are real stock.
    private static Dictionary<string, int> StashedReservations()
    {
        var reserved = new Dictionary<string, int>(StringComparer.Ordinal);
        var swap = FormSwapSystem.Instance;
        if (swap == null)
            return reserved;
        foreach (var id in swap.StashedItemTemplateIds())
            reserved[id] = reserved.TryGetValue(id, out var n) ? n + 1 : 1;
        return reserved;
    }

    private static bool TakeReservation(Dictionary<string, int> reserved, string id)
    {
        if (id == null || reserved.Count == 0 || !reserved.TryGetValue(id, out var n) || n <= 0)
            return false;
        if (n == 1)
            reserved.Remove(id);
        else
            reserved[id] = n - 1;
        return true;
    }

    private CalibrationInstance MakeInstance(Item item, string baseId, int rank, BaseUnitLeader leader)
        => new()
        {
            Item = item,
            BaseWeaponId = baseId,
            WeaponName = Calibration.CleanName(Templates.DefaultText(Weapon(baseId, quiet: true)?.Title, baseId)),
            Rank = rank,
            Holder = leader != null ? Calibration.HolderName(Affinity.CharacterTag(leader)) : null,
            Leader = leader,
        };

    // The R0 stock copies of a weapon available as merge fodder: unequipped base instances other
    // than the one being calibrated.
    public int DuplicateCount(string baseWeaponId, Item exclude = null)
        => StockDuplicates(baseWeaponId, exclude).Count;

    private List<Item> StockDuplicates(string baseWeaponId, Item exclude)
    {
        var dupes = new List<Item>();
        var owned = Inventory.Owned;
        var baseTemplate = Weapon(baseWeaponId, quiet: true);
        if (owned == null || baseTemplate == null)
            return dupes;

        var reserved = StashedReservations();
        var equippedPtrs = EquippedPointers();
        var all = new Il2CppSystem.Collections.Generic.List<BaseItem>();
        owned.GetInstances(all);
        for (var i = 0; i < all.Count; i++)
        {
            var item = all[i]?.TryCast<Item>();
            if (item == null || (exclude != null && item.Pointer == exclude.Pointer) || equippedPtrs.Contains(item.Pointer))
                continue;
            if (item.GetTemplate()?.GetID() != baseWeaponId)
                continue;
            // A swapped-out form's stashed personal weapon is reserved, never merge fodder.
            if (TakeReservation(reserved, baseWeaponId))
                continue;
            dupes.Add(item);
        }
        return dupes;
    }

    private HashSet<System.IntPtr> EquippedPointers()
    {
        var set = new HashSet<System.IntPtr>();
        var hired = Leaders.Hired();
        for (var i = 0; hired != null && i < hired.Count; i++)
        {
            var items = Leaders.EquippedItems(hired[i]);
            for (var j = 0; items != null && j < items.Count; j++)
                if (items[j] != null)
                    set.Add(items[j].Pointer);
        }
        return set;
    }

    // The stat rows that change from a rank to the next (an upgrade preview). At max rank every stat
    // is returned as its final value with no delta.
    public List<StatDelta> Deltas(string baseWeaponId, int rank)
    {
        var current = Weapon(Calibration.RankId(baseWeaponId, rank), quiet: true);
        var next = rank < Calibration.MaxRank ? Weapon(Calibration.RankId(baseWeaponId, rank + 1), quiet: true) : null;
        var rows = new List<StatDelta>();
        if (current == null)
            return rows;

        // Blades keep their real stats on the granted skills' Attack handlers and the weapon fields
        // at 0 (the tooltip SUMS weapon.Damage with the granted skill's Attack.Damage, so a non-zero
        // weapon field would double the shown number). Their rank rows diff the first granted skill's
        // Attack handler between the rank templates, which carry per-rank skill clones in KDL.
        if (current.Damage == 0f && current.SkillsGranted != null && current.SkillsGranted.Count > 0)
        {
            var attack = FirstAttack(current.SkillsGranted[0]);
            if (attack != null)
            {
                var nextAttack = next != null && next.SkillsGranted != null && next.SkillsGranted.Count > 0
                    ? FirstAttack(next.SkillsGranted[0])
                    : null;
                rows.Add(new StatDelta { Name = DamageLabel.Resolve(), Current = attack.Damage, Next = nextAttack?.Damage ?? attack.Damage });
                rows.Add(new StatDelta { Name = ArmorPenLabel.Resolve(), Current = attack.ArmorPenetration, Next = nextAttack?.ArmorPenetration ?? attack.ArmorPenetration });
                rows.Add(new StatDelta { Name = ArmorDmgLabel.Resolve(), Current = attack.DamageToArmorDurability, Next = nextAttack?.DamageToArmorDurability ?? attack.DamageToArmorDurability });
            }
            return rows;
        }

        foreach (var (label, get) in StatFields)
        {
            var now = get(current);
            var then = next != null ? get(next) : now;
            if (next == null || System.Math.Abs(then - now) > 0.001f)
                rows.Add(new StatDelta { Name = label.Resolve(), Current = now, Next = then });
        }
        return rows;
    }

    private static Il2CppMenace.Tactical.Skills.Effects.Attack FirstAttack(SkillTemplate skill)
    {
        var handlers = skill?.EventHandlers;
        for (var i = 0; handlers != null && i < handlers.Count; i++)
        {
            var attack = handlers[i]?.TryCast<Il2CppMenace.Tactical.Skills.Effects.Attack>();
            if (attack != null)
                return attack;
        }
        return null;
    }

    // The rank-comparison row labels. Declared as LocalisedText so the compiler extracts them into
    // the POT: a bare literal handed to a Label never reaches a translator. The type has to be named
    // in the expression, because extraction scans for a LocalisedText construction with its two
    // string literals present, and a target-typed `new(...)` reads identically to a C# compiler but
    // not to that scan. Do not write that construction out in full anywhere but real code: the
    // extractor reads raw source and does not skip comments, so an illustrative one is collected as
    // a real entry and shipped to translators.
    private static readonly LocalisedText DamageLabel =
        new LocalisedText("WOMENACE::ui/calibration/stat_damage", "DAMAGE");
    private static readonly LocalisedText ArmorPenLabel =
        new LocalisedText("WOMENACE::ui/calibration/stat_armor_pen", "ARMOR PEN");
    private static readonly LocalisedText ArmorDmgLabel =
        new LocalisedText("WOMENACE::ui/calibration/stat_armor_dmg", "ARMOR DMG");

    private static readonly (LocalisedText Label, Func<WeaponTemplate, float> Get)[] StatFields =
    {
        (DamageLabel, w => w.Damage),
        (ArmorPenLabel, w => w.ArmorPenetration),
        (ArmorDmgLabel, w => w.DamageToArmorDurability),
        (new LocalisedText("WOMENACE::ui/calibration/stat_accuracy", "ACCURACY"), w => w.AccuracyBonus),
        (new LocalisedText("WOMENACE::ui/calibration/stat_ideal_range", "IDEAL RANGE"), w => w.IdealRange),
        (new LocalisedText("WOMENACE::ui/calibration/stat_max_range", "MAX RANGE"), w => w.MaxRange),
        (new LocalisedText("WOMENACE::ui/calibration/stat_suppression", "SUPPRESSION"), w => w.Suppression),
    };

    // Calibrate one weapon instance up a rank, consuming one R0 stock duplicate. The equipped case
    // swaps the new rank into the doll's hands; the stock case swaps the inventory instance. Either
    // way the new rank is placed before the old instance and the duplicate are destroyed.
    public (bool ok, string error) Merge(CalibrationInstance target)
    {
        if (target?.Item == null)
            return (false, "no weapon selected");
        if (target.Rank >= Calibration.MaxRank)
            return (false, "already at max rank");

        var nextTemplate = Weapon(Calibration.RankId(target.BaseWeaponId, target.Rank + 1));
        if (nextTemplate == null)
            return (false, "rank template missing");

        var dupes = StockDuplicates(target.BaseWeaponId, target.Item);
        if (dupes.Count == 0)
            return (false, "no duplicate to consume");

        if (!Replace(target, nextTemplate, out var error))
            return (false, error);
        Inventory.RemoveItem(dupes[0]);
        Context.Log.Info($"calibration: merged '{target.BaseWeaponId}' r{target.Rank} -> r{target.Rank + 1}");
        return (true, null);
    }

    // Revert one weapon instance down a rank, returning an R0 duplicate to stock (so component
    // capacity is conserved and reverting never mints value).
    public (bool ok, string error) Revert(CalibrationInstance target)
    {
        if (target?.Item == null)
            return (false, "no weapon selected");
        if (target.Rank < 1)
            return (false, "already at rank 0");

        var prevTemplate = Weapon(Calibration.RankId(target.BaseWeaponId, target.Rank - 1));
        var baseTemplate = Weapon(target.BaseWeaponId);
        if (prevTemplate == null || baseTemplate == null)
            return (false, "rank template missing");

        if (!Replace(target, prevTemplate, out var error))
            return (false, error);
        Inventory.AddItem(baseTemplate);
        Context.Log.Info($"calibration: reverted '{target.BaseWeaponId}' r{target.Rank} -> r{target.Rank - 1}");
        return (true, null);
    }

    // Replace a weapon instance with another rank template. Stock instances just swap in inventory.
    // Equipped instances rebuild the holder's whole loadout (RemoveAll + re-add, substituting the one
    // weapon), the way FormSwapSystem does: unequipping a squad leader's weapon on its own makes the
    // game auto-fill the empty slot with a default carbine, so the atomic rebuild is what keeps the
    // swap clean.
    private bool Replace(CalibrationInstance target, WeaponTemplate replacement, out string error)
    {
        error = null;
        if (target.Leader == null)
        {
            Inventory.RemoveItem(target.Item);
            Inventory.AddItem(replacement);
            return true;
        }

        var container = target.Leader.GetItems();
        var owned = Inventory.Owned;
        if (container == null || owned == null)
        {
            error = "no item container";
            return false;
        }

        // Mint an owned instance of the replacement to equip from.
        Inventory.AddItem(replacement);
        var targetId = target.Item.GetTemplate()?.GetID();

        // Snapshot the loadout as template REFERENCES (not ids), substituting the one target weapon
        // with the replacement rank. Re-resolving ids would silently drop any template not in the
        // loader's collection (special-weapon rank clones register late), and a dropped slot here
        // means an equipped weapon vanishes in the rebuild below.
        var templates = new List<ItemTemplate>();
        var substituted = false;
        var all = container.GetAllItems();
        for (var i = 0; all != null && i < all.Count; i++)
        {
            var tmpl = all[i]?.GetTemplate()?.TryCast<ItemTemplate>();
            if (tmpl == null)
                continue;
            if (!substituted && tmpl.GetID() == targetId)
            {
                templates.Add(replacement);
                substituted = true;
            }
            else
            {
                templates.Add(tmpl);
            }
        }

        // Rebuild the container from owned instances, so no slot is ever left empty to auto-fill.
        container.RemoveAll();
        foreach (var tmpl in templates)
        {
            var inst = owned.GetUnusedInstance(tmpl, false);
            if (inst != null)
                container.Add(inst, true);
            else
                Context.Log.Warn($"calibration: no owned instance of '{tmpl.GetID()}' to re-equip");
        }

        // The old target rank is now an unequipped owned copy; destroy it.
        Inventory.RemoveItem(target.Item);
        return true;
    }

    // --- dev verbs ------------------------------------------------------------------------------

    public object DevStatus()
    {
        if (StrategyState.Get() == null)
            return new { error = "no strategy state (load a campaign save first)" };
        var lines = new List<string>();
        foreach (var inst in Instances())
            lines.Add($"{inst.WeaponName} r{inst.Rank} [{inst.Holder ?? "stock"}]");
        return new { ok = true, instances = string.Join(" | ", lines) };
    }

    // Craft an R0 duplicate through the blueprint (the workshop's job), for testing without the
    // workshop UI. Consumes the component + salvage.
    public object DevCraft(string characterTag)
    {
        if (Affinity.KeyForTag(characterTag) == 0)
            return new { error = $"no calibratable weapon for '{characterTag}'" };
        var blueprint = Blueprint(Calibration.WeaponIdFor(characterTag));
        var owned = Inventory.Owned;
        if (blueprint == null || owned == null)
            return new { error = "no blueprint or inventory" };

        var materials = blueprint.GetCraftingMaterials();
        for (var i = 0; materials != null && i < materials.Count; i++)
            if (owned.GetInstanceCount(materials[i].Template) < materials[i].Count)
                return new { error = $"missing {materials[i].Template?.GetID()}" };
        for (var i = 0; materials != null && i < materials.Count; i++)
            for (var k = 0; k < materials[i].Count; k++)
                owned.RemoveItem(materials[i].Template);

        var result = blueprint.GetCraftingResult();
        return new { ok = Inventory.AddItem(result) != null, crafted = result?.GetID() };
    }

    // Merge/revert the calibratable weapon held by the tagged doll (the equipped instance).
    public object DevMerge(string characterTag) => DevRun(characterTag, Merge);
    public object DevRevert(string characterTag) => DevRun(characterTag, Revert);

    private object DevRun(string characterTag, Func<CalibrationInstance, (bool ok, string error)> op)
    {
        var target = Instances().Find(i => i.Leader != null && Affinity.CharacterTag(i.Leader) == characterTag);
        if (target == null)
            return new { error = $"no equipped calibratable weapon on '{characterTag}'" };
        var (ok, err) = op(target);
        return ok ? new { ok = true } : new { error = err };
    }

    // The wide equipment banner sprite for a weapon (the armoury weapon-select art), falling back to
    // its small icon. Used for the calibration list rows.
    public UnityEngine.Sprite BannerSprite(string baseWeaponId)
    {
        var w = Weapon(baseWeaponId, quiet: true);
        return w?.IconEquipment ?? w?.Icon;
    }

    private WeaponTemplate Weapon(string id, bool quiet = false)
        => Templates.Resolve<WeaponTemplate>(id, _weaponCache, quiet ? null : msg => Context.Log.Warn($"calibration: {msg}"));

    private CommodityTemplate Component(string weaponId)
        => Templates.Resolve<CommodityTemplate>(Calibration.ComponentIdFor(weaponId), _componentCache, msg => Context.Log.Warn($"calibration: {msg}"));

    private BlueprintTemplate Blueprint(string weaponId)
        => Templates.Resolve<BlueprintTemplate>(Calibration.BlueprintIdFor(weaponId), _blueprintCache, msg => Context.Log.Warn($"calibration: {msg}"));
}
