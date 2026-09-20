using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Jiangyu.Game;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Drops gifts from enemies the player kills, in code, on its own roll. It hooks the game's death
// event (TacticalManager.InvokeOnDeath, which carries the killer) rather than the data-driven
// EntityTemplate.Loot path: that path shares the gear-loot gather, so gifts there would perturb the
// gear economy and compete with gear for the lootable-enemy cap. Kills are recorded during the
// mission and rolled as the mission-result screen builds, under the same lootable-enemy cap and
// loot-chance bonus vanilla applies to its own loot (see LootableEnemyCap and LootDropChanceBonus),
// weighted by the enemy's tier. The gifts are appended to the result's loot list, which the native
// flow shows and banks into the inventory the gift modal reads.
public sealed class GiftDropSystem : JiangyuSystem
{
    private ShipUpgradeTemplate _enhancedRescueFairy;

    // An enemy is matched to the first tier whose MaxCost is >= its ArmyPointCost (the game's own
    // difficulty/value number, ~60 for weak grunts up past ~250 for elites). A kill that rolls tries
    // every gift whose Rarity falls in [MinRarity, MaxRarity], each at DropChance percent plus the
    // flat bonuses added at roll time (Enhanced Rescue Fairy installs, the loot-chance difficulty
    // slider). The rarity bands match the game's own common/uncommon/rare brackets. The base chance
    // rises with the tier, and stays under one in two to keep the expected number of rare gifts
    // per elite kill, before bonuses, at two of the five.
    private static readonly Tier[] Tiers =
    {
        new Tier(name: "grunt", maxCost: 120, minRarity: 0, maxRarity: 32, dropChance: 30),
        new Tier(name: "tough", maxCost: 220, minRarity: 33, maxRarity: 65, dropChance: 35),
        new Tier(name: "elite", maxCost: int.MaxValue, minRarity: 66, maxRarity: 100, dropChance: 40),
    };

    private readonly struct Tier
    {
        public readonly string Name;
        public readonly int MaxCost;
        public readonly int MinRarity;
        public readonly int MaxRarity;
        public readonly int DropChance;

        public Tier(string name, int maxCost, int minRarity, int maxRarity, int dropChance)
        {
            Name = name;
            MaxCost = maxCost;
            MinRarity = minRarity;
            MaxRarity = maxRarity;
            DropChance = dropChance;
        }
    }

    // A player kill of an enemy, awaiting its gift roll on the mission-result screen.
    private readonly struct Kill
    {
        public readonly string EnemyId;
        public readonly Tier Tier;

        public Kill(string enemyId, Tier tier)
        {
            EnemyId = enemyId;
            Tier = tier;
        }
    }

    // Kills recorded during the current mission attempt. Rolled and delivered as the mission-result
    // window builds (see OnShowMissionResult) and consumed there. They are not persisted: the game
    // saves on the strategy layer, so a save cannot sit between a kill and its result screen, and
    // a reload of an attempt replays it from the start.
    private readonly List<Kill> _kills = new();

    // The strategy-layer Mission object the kills belong to. Set when the operation starts a
    // mission and adopted lazily on the first kill, so an attempt that ends without a result
    // screen (abort, quit) can never hand its kills to a later mission: the next launch resets the
    // list, and the result screen only rolls kills whose mission is the one the operation is
    // showing the result for.
    private System.IntPtr _rollingMission;

    public override void OnTemplatesApplied()
        => _enhancedRescueFairy = Templates.ById<ShipUpgradeTemplate>("oci.wmgfl_enhanced_rescue_fairy",
            message => Context.Log.Warn($"gift drops: {message}"));

    public override void OnInit()
    {
        // The operation's mission-launch hook (called from the mission-preparation screen and the
        // new-campaign flow) marks the start of a fresh attempt.
        Context.Patches.Postfix("Il2CppMenace.Strategy.Operation", "OnMissionStarted", OnMissionStarted);

        // Fires on every actor death with (target, killer, killerFaction). Single overload, so it
        // resolves cleanly for patching.
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnDeath", OnActorDied);

        // Gifts are delivered through the mission-result loot list: rolled from the recorded kills
        // and appended to MissionResult.m_Loot as the result window builds. The native end-mission
        // flow both displays that list (as loot slots) and banks it to the inventory, so this is the
        // single delivery point. Banking on the kill too would double the grant.
        Context.Patches.Prefix("Il2CppMenace.UI.MissionResult.MissionResultUIScreen", "ShowMissionWindow", OnShowMissionResult);
    }

    private void OnMissionStarted(PatchInfo info)
    {
        try
        {
            _kills.Clear();
            _rollingMission = (info.Instance as Operation)?.GetCurrentMission()?.Pointer ?? System.IntPtr.Zero;
        }
        catch (Exception ex) { Context.Log.Warn($"gift mission start failed: {ex.Message}"); }
    }

    private void OnActorDied(PatchInfo info)
    {
        try
        {
            if (info.Args == null || info.Args.Count < 2)
                return;
            // The args are typed Entity, so re-wrap via TryCast rather than `as Actor` (which would
            // not downcast the Entity wrapper).
            var target = (info.Args[0] as Il2CppObjectBase)?.TryCast<Actor>();
            var killer = (info.Args[1] as Il2CppObjectBase)?.TryCast<Actor>();
            if (target == null || killer == null)
                return;
            // An enemy (not player-controlled) finished off by a player unit (or its allied AI).
            if (!killer.IsPlayerControlled(true) || target.IsPlayerControlled(true))
                return;

            var template = target.GetTemplate();
            if (template == null || !template.IsAlive())
                return;

            // Only record within a campaign (a tactical mission always is).
            if (StrategyState.Get() == null)
                return;

            // Adopt the running mission if the launch hook did not see it, dropping anything left
            // from another attempt.
            var missionPtr = TacticalManager.Get()?.GetMission()?.Pointer ?? System.IntPtr.Zero;
            if (missionPtr != _rollingMission)
            {
                _kills.Clear();
                _rollingMission = missionPtr;
            }

            var tier = TierFor(template.ArmyPointCost);
            _kills.Add(new Kill(template.GetID(), tier));
            Context.Log.Debug($"gift kill: enemy '{template.GetID()}' (cost {template.ArmyPointCost}, tier {tier.Name}) killed by player");
        }
        catch (Exception ex) { Context.Log.Warn($"gift kill record failed: {ex.Message}"); }
    }

    // Roll this mission's recorded kills and deliver the gifts by appending them to the result's
    // loot list, as the window builds and before the slots are created from MissionResult.m_Loot.
    // The native end-mission flow shows the list as loot slots AND banks it to the inventory, so
    // this is the single delivery point. The rolls are staged locally and the kills consumed before
    // the native list is touched, so a failure part-way through can lose gifts but never grant
    // them twice on a rebuilt window.
    private void OnShowMissionResult(PatchInfo info)
    {
        try
        {
            if (_kills.Count == 0)
                return;

            var state = StrategyState.Get();
            if (state == null)
                return;

            // The window is built from the result screen's open handler, while the operation still
            // points at the mission just played (it is cleared later, by the operation-result
            // screen). Kills from any other attempt are discarded rather than delivered here.
            var mission = state.GetCurrentOperation()?.GetCurrentMission();
            if (mission == null || mission.Pointer != _rollingMission)
            {
                Context.Log.Info($"gift drop: discarded {_kills.Count} kill(s) from another mission attempt");
                _kills.Clear();
                return;
            }

            // The screen shows the campaign's last mission result, where its loot list lives.
            // (The screen's own `result` is a lambda capture, not a member, so read it from state.)
            var result = state.GetLastMissionResult();
            if (result == null || !result.IsAlive())
                return;

            // Vanilla discards random entries from its lootable list until the cap remains, rather
            // than keeping the first kills, so a late kill has the same odds as an early one.
            var recorded = _kills.Count;
            var rolling = new List<Kill>(_kills);
            var cap = LootableEnemyCap(state);
            while (rolling.Count > cap)
                rolling.RemoveAt(UnityEngine.Random.Range(0, rolling.Count));

            // Both bonuses are flat percentage points, as vanilla adds its own loot-chance bonus to
            // every loot entry's chance.
            var rescueFairies = _enhancedRescueFairy == null ? 0
                : state.ShipUpgrades?.GetInstallsCount(_enhancedRescueFairy) ?? 0;
            var chanceBonus = 10 * rescueFairies + LootDropChanceBonus(state);

            var gifts = new List<CommodityTemplate>();
            foreach (var kill in rolling)
            {
                var dropChance = Math.Min(100, kill.Tier.DropChance + chanceBonus);
                foreach (var gift in GiftCatalog.All())
                {
                    if (gift.Rarity < kill.Tier.MinRarity || gift.Rarity > kill.Tier.MaxRarity)
                        continue;
                    if (UnityEngine.Random.Range(0, 100) >= dropChance)
                        continue;
                    gifts.Add(gift);
                    Context.Log.Debug($"gift drop: '{gift.GetID()}' from '{kill.EnemyId}' (tier {kill.Tier.Name})");
                }
            }
            _kills.Clear();

            var loot = result.m_Loot;
            if (loot == null)
            {
                loot = new Il2CppSystem.Collections.Generic.List<BaseItemTemplate>();
                result.m_Loot = loot;
            }
            foreach (var gift in gifts)
                loot.Add(gift);
            Context.Log.Info($"gift drop: rolled {rolling.Count} of {recorded} kill(s) (cap {(cap == int.MaxValue ? "unlimited" : cap.ToString())}, bonus {chanceBonus}%), delivered {gifts.Count} gift(s) via the mission-result screen");
        }
        catch (Exception ex) { Context.Log.Warn($"gift result-delivery failed: {ex.Message}"); }
    }

    // How many kills roll, mirroring the cap vanilla puts on its own gear loot. At mission end
    // (TacticalState.OnFinished) vanilla gathers every dead enemy with a loot table and, unless the
    // new-game "unlimited lootable enemies" custom-difficulty toggle is on
    // (GlobalDifficulty.LimitedLootableEnemies false), randomly discards entries until
    // CampaignProgressConfig.MaxLootableEnemies remain: a curve over campaign progress, 15 at the
    // start rising to 30, rounded to the nearest integer. The two populations are selected
    // independently (vanilla's is every dead enemy with a loot table whoever killed it, this one
    // is every player kill) and share only the number.
    private static int LootableEnemyCap(StrategyState state)
    {
        var difficulty = state.GetGlobalDifficulty();
        if (difficulty != null && !difficulty.LimitedLootableEnemies)
            return int.MaxValue;
        return UnityEngine.Mathf.RoundToInt(state.GetCampaignProgress(CampaignProgressType.MaxLootableEnemies));
    }

    // The new-game "loot drop chance bonus" custom-difficulty slider (0 to 100), which vanilla adds
    // as flat percentage points to each loot entry's drop chance before rolling it.
    private static int LootDropChanceBonus(StrategyState state)
        => state.GetGlobalDifficulty()?.LootDropChanceBonus ?? 0;

    private static Tier TierFor(int armyPointCost)
    {
        foreach (var tier in Tiers)
            if (armyPointCost <= tier.MaxCost)
                return tier;
        return Tiers[Tiers.Length - 1];
    }
}
