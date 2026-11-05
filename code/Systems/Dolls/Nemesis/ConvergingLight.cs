using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.UI;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Converging Light, Nemesis's tier 4 perk: when she dies for good she has a Chance in a hundred
// to be back on the roster once the operation ends, with no limit on how often. The perk's
// handler only carries the chance. ConvergingLightSystem does the rest on the strategy layer.
[JiangyuType("ConvergingLight")]
public sealed partial class ConvergingLight : SkillEventHandlerTemplate
{
    public int Chance = 60;

    public override SkillEventHandler Create() => new ConvergingLightHandler();
}

[JiangyuType("ConvergingLightHandler")]
public sealed partial class ConvergingLightHandler : SkillEventHandler
{
}

// Leaders who died this operation with the perk, by leader template id: their chance, and the
// weapon they carried when they fell.
public sealed class ConvergingLightState
{
    public Dictionary<string, int> Pending { get; set; } = [];
    public Dictionary<string, string> Weapons { get; set; } = [];
}

// A leader dies for good through SquadLeader.OnPermanentDeath (bleeding out, or still bleeding
// when a mission is lost): her items go back to stock and she moves from the hired list to the
// unburied one, then to the buried one at her funeral. A death with the perk is noted, and when
// StrategyState.EndOperation runs Roster.OnOperationFinished each noted death is rolled. A
// success takes her off the unburied or buried list and back onto the hired one, alive and with
// no bleed-out left. Her weapon is equipped again when an unused one is in stock. The rest of her
// loadout stays there.
public sealed class ConvergingLightSystem : JiangyuSystem
{
    private ConvergingLightState State => Context.State.Get<ConvergingLightState>();

    public override void OnInit()
    {
        Context.Patches.Prefix("Il2CppMenace.Strategy.SquadLeader", "OnPermanentDeath", OnPermanentDeath);
        Context.Patches.Postfix("Il2CppMenace.Strategy.Roster", "OnOperationFinished", OnOperationFinished);
    }

    private void OnPermanentDeath(PatchInfo info)
    {
        try
        {
            var leader = (info.Instance as Il2CppObjectBase)?.TryCast<BaseUnitLeader>();
            var id = leader?.LeaderTemplate?.GetID();
            if (id == null || ChanceFor(leader) is not { } chance)
                return;
            State.Pending[id] = chance;
            // Read before the death strips her items back to stock.
            var weapon = leader.GetItems()?.GetItemAtSlot(ItemSlot.InfantryWeapon)?.GetTemplate()?.GetID();
            if (weapon != null)
                State.Weapons[id] = weapon;
            else
                State.Weapons.Remove(id);
            Context.Log.Info($"converging light: {id} fell, {chance}% to return when the operation ends");
        }
        catch (Exception ex) { Context.Log.Warn($"converging light: death check failed: {ex.Message}"); }
    }

    // The perk's chance when the leader has it.
    private static int? ChanceFor(BaseUnitLeader leader)
    {
        var perks = leader.m_Perks;
        for (var i = 0; perks != null && i < perks.Count; i++)
        {
            var handlers = perks[i]?.EventHandlers;
            for (var j = 0; handlers != null && j < handlers.Count; j++)
                if (handlers[j]?.TryCast<ConvergingLight>() is { } perk)
                    return perk.Chance;
        }
        return null;
    }

    private void OnOperationFinished(PatchInfo info)
    {
        var pending = State.Pending;
        if (pending.Count == 0)
            return;
        try
        {
            var roster = (info.Instance as Il2CppObjectBase)?.TryCast<Roster>();
            if (roster == null)
                return;
            foreach (var (id, chance) in pending.ToList())
            {
                var roll = UnityEngine.Random.Range(0, 100);
                if (roll >= chance)
                {
                    Context.Log.Info($"converging light: {id} stays fallen (rolled {roll} against {chance})");
                    continue;
                }
                if (Revive(roster, id) is { } leader)
                {
                    Context.Log.Info($"converging light: {id} returns (rolled {roll} against {chance})");
                    if (State.Weapons.TryGetValue(id, out var weapon))
                        Rearm(leader, weapon);
                    Notify(leader);
                }
                else
                    Context.Log.Warn($"converging light: {id} rolled a return but is not among the fallen");
            }
        }
        catch (Exception ex) { Context.Log.Warn($"converging light: operation end failed: {ex.Message}"); }
        finally
        {
            pending.Clear();
            State.Weapons.Clear();
        }
    }

    private static BaseUnitLeader Revive(Roster roster, string id)
    {
        foreach (var list in new[] { roster.m_UnburiedLeaders, roster.m_BuriedLeaders })
        {
            for (var i = 0; list != null && i < list.Count; i++)
            {
                var leader = list[i];
                if (leader?.LeaderTemplate?.GetID() != id)
                    continue;
                list.RemoveAt(i);
                roster.m_HiredLeaders.Add(leader);
                leader.SetHealthStatus(LeaderHealthStatus.Alive);
                leader.SetRemainingBleedOutRounds(0);
                leader.ValidateSquaddies();
                return leader;
            }
        }
        return null;
    }

    // An owned, unused copy of the weapon goes back in her hand. It runs once she is on the hired
    // list again, since a leader off the roster does not register what she holds as in use.
    private void Rearm(BaseUnitLeader leader, string weaponId)
    {
        try
        {
            var owned = StrategyState.Get()?.OwnedItems;
            var items = leader.GetItems();
            var template = Templates.ById<ItemTemplate>(weaponId);
            if (owned == null || items == null || template == null)
                return;
            if (items.GetItemAtSlot(ItemSlot.InfantryWeapon) != null)
                return;
            var weapon = owned.GetUnusedInstance(template, false);
            if (weapon == null)
            {
                Context.Log.Info($"converging light: no unused {weaponId} in stock to give back");
                return;
            }
            if (!items.Add(weapon, true))
                Context.Log.Warn($"converging light: {weaponId} would not equip");
        }
        catch (Exception ex) { Context.Log.Warn($"converging light: rearm failed: {ex.Message}"); }
    }

    private void Notify(BaseUnitLeader leader)
    {
        try
        {
            string name = null;
            try { name = leader.GetNickname()?.GetTranslated(null); }
            catch { }
            if (string.IsNullOrEmpty(name))
                name = Templates.DefaultText(leader.GetNickname(), leader.LeaderTemplate?.GetID() ?? "");
            UIManager.Get()?.ShowNotification(
                Locale.Format("WOMENACE::ui/converging_light/returned", "{0} has returned to the roster.", name), false, 6f);
        }
        catch (Exception ex) { Context.Log.Warn($"converging light: notification failed: {ex.Message}"); }
    }
}
