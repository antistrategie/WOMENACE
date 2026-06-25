using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Tactical;
using Jiangyu.Game;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Drops gifts from enemies the player kills, in code, on its own roll. It hooks the game's death
// event (TacticalManager.InvokeOnDeath, which carries the killer) rather than the data-driven
// EntityTemplate.Loot path: that path is gated by the global MaxLootableEnemies cap and shares the
// gear-loot gather, so it both throttles gifts and perturbs the gear economy. Rolling here is
// independent of that cap and touches nothing else: every player kill of an enemy gets its own gift
// roll, weighted by the enemy's tier, granted straight to the shared inventory the gift modal reads.
public sealed class GiftDropSystem : JiangyuSystem
{
    // An enemy is matched to the first tier whose MaxCost is >= its ArmyPointCost (the game's own
    // difficulty/value number, ~60 for weak grunts up past ~250 for elites). On a player kill it
    // rolls every gift whose Rarity falls in [MinRarity, MaxRarity], each at DropChance percent. The
    // rarity bands match the game's own common/uncommon/rare brackets.
    private static readonly Tier[] Tiers =
    {
        new Tier(name: "grunt", maxCost: 120, minRarity: 0, maxRarity: 32, dropChance: 40),
        new Tier(name: "tough", maxCost: 220, minRarity: 33, maxRarity: 65, dropChance: 30),
        new Tier(name: "elite", maxCost: int.MaxValue, minRarity: 66, maxRarity: 100, dropChance: 20),
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

    // Gifts rolled during the current mission, awaiting delivery on the mission-result screen. They are
    // appended to MissionResult.m_Loot as the window builds (see OnShowMissionResult), where the native
    // flow both shows and banks them. Flushed and cleared there.
    private readonly List<CommodityTemplate> _pendingGifts = new();

    // The mission the pending gifts were rolled in. A mission that ends without the result screen
    // (abort, retreat, quit) never flushes _pendingGifts, so the queue is reset the moment a
    // different mission starts rolling, rather than leaking the previous mission's gifts into this
    // one's result.
    private System.IntPtr _rollingMission;

    public override void OnInit()
    {
        // Fires on every actor death with (target, killer, killerFaction). Single overload, so it
        // resolves cleanly for patching.
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnDeath", OnActorDied);

        // Gifts are delivered through the mission-result loot list: rolled on the kill (below), then
        // appended to MissionResult.m_Loot as the result window builds. The native end-mission flow
        // both displays that list (as loot slots) and banks it to the inventory, so this is the single
        // delivery point. Banking on the kill too would double the grant.
        Context.Patches.Prefix("Il2CppMenace.UI.MissionResult.MissionResultUIScreen", "ShowMissionWindow", OnShowMissionResult);
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
            var tier = TierFor(template.ArmyPointCost);

            // Only roll within a campaign (a tactical mission always is). Gifts are not banked here:
            // they ride the result-screen loot list (see OnShowMissionResult), which the native flow
            // banks. Banking here too would double the grant.
            if (StrategyState.Get() == null)
                return;

            // Drop any gifts left queued by a mission that ended without a result screen, so they
            // cannot leak into this mission's result.
            var missionPtr = TacticalManager.Get()?.GetMission()?.Pointer ?? System.IntPtr.Zero;
            if (missionPtr != _rollingMission)
            {
                _pendingGifts.Clear();
                _rollingMission = missionPtr;
            }

            Context.Log.Debug($"gift roll: enemy '{template.GetID()}' (cost {template.ArmyPointCost}, tier {tier.Name}) killed by player");

            foreach (var gift in GiftCatalog.All())
            {
                if (gift.Rarity < tier.MinRarity || gift.Rarity > tier.MaxRarity)
                    continue;
                if (UnityEngine.Random.Range(0, 100) >= tier.DropChance)
                    continue;
                // Queue it for delivery on the mission-result screen (display + native banking there).
                _pendingGifts.Add(gift);
                Context.Log.Info($"gift drop: '{gift.GetID()}' from '{template.GetID()}' (tier {tier.Name})");
            }
        }
        catch (Exception ex) { Context.Log.Warn($"gift drop failed: {ex.Message}"); }
    }

    // Deliver this mission's rolled gifts by appending them to the result's loot list, as the window
    // builds and before the slots are created from MissionResult.m_Loot. The native end-mission flow
    // shows the list as loot slots AND banks it to the inventory, so this is the single delivery point.
    // The list is cleared after, making it idempotent if the window rebuilds.
    private void OnShowMissionResult(PatchInfo info)
    {
        try
        {
            if (_pendingGifts.Count == 0)
                return;

            // Drop the queue if a DIFFERENT mission is positively running: that means the gifts were
            // rolled in a mission that ended without a result screen (abort/retreat) and these belong
            // to a stale run, not the one whose result is showing. When the mission is gone or unknown
            // (the normal end-of-mission case), deliver, so a legitimate result is never skipped.
            var missionPtr = TacticalManager.Get()?.GetMission()?.Pointer ?? System.IntPtr.Zero;
            if (missionPtr != System.IntPtr.Zero && missionPtr != _rollingMission)
            {
                _pendingGifts.Clear();
                return;
            }

            // The screen shows the campaign's last mission result, where its loot list lives.
            // (The screen's own `result` is a lambda capture, not a member, so read it from state.)
            var result = StrategyState.Get()?.GetLastMissionResult();
            if (result == null || !result.IsAlive())
                return;

            var loot = result.m_Loot;
            if (loot == null)
            {
                loot = new Il2CppSystem.Collections.Generic.List<BaseItemTemplate>();
                result.m_Loot = loot;
            }

            foreach (var gift in _pendingGifts)
                loot.Add(gift);
            Context.Log.Info($"gift drop: delivered {_pendingGifts.Count} gift(s) via the mission-result screen");
            _pendingGifts.Clear();
        }
        catch (Exception ex) { Context.Log.Warn($"gift result-delivery failed: {ex.Message}"); }
    }

    private static Tier TierFor(int armyPointCost)
    {
        foreach (var tier in Tiers)
            if (armyPointCost <= tier.MaxCost)
                return tier;
        return Tiers[Tiers.Length - 1];
    }
}
