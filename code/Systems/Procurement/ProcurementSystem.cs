using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Jiangyu.Game.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

public sealed class ProcurementSystem : JiangyuSystem
{
    public static ProcurementSystem Instance { get; private set; }
    public ProcurementState State => Context.State.Get<ProcurementState>();
    private ProcurementCatalogue _catalogue;
    private bool _trading;
    public ProcurementCatalogue Catalogue => _catalogue ??= ProcurementCatalogue.Create(Context.Log);
    public static int Pieces => Inventory.Owned is { } owned
        && Templates.ById<CommodityTemplate>(Procurement.PieceId) is { } template ? owned.GetInstanceCount(template) : 0;

    public override void OnInit() => Instance = this;
    public override void OnTemplatesApplied() => _catalogue = null;
    public override void OnSceneLoaded(int buildIndex, string sceneName) => _catalogue = null;
    public override void OnUnload()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    public void ReconcileClaims()
    {
        var roster = StrategyState.Get()?.Roster;
        if (roster == null)
            return;
        // Existing campaigns may have obtained these leaders through another route. A known
        // leader cannot use a second dossier, including after dismissal or death.
        foreach (var entry in Catalogue.Entries.Where(entry => entry.Leader != null))
        {
            if (LeaderRecruitment.IsAcquired(entry.Leader, roster) && State.Claimed(entry.Reward.Id) == 0)
                State.Claims[entry.Reward.Id] = entry.Reward.Limit;
        }
        var owned = Inventory.Owned;
        foreach (var (character, unlocks) in Unlocks.ByCharacter)
            if (unlocks.Any(unlock => Unlocks.UsesItemIds(unlock.Feature)))
                Affinity.ReconcileLimitedGrants(Context, character, owned);
    }

    public (bool ok, string error, IReadOnlyList<ProcurementCatalogue.Entry> rewards) Pull(int count)
    {
        if (_trading || !WorkshopAccess.IsUnlocked || count != 1 && count != 10 || Catalogue.Rewards.Count == 0)
            return (false, Locale.Text("WOMENACE::ui/procurement/unavailable", "Procurement is unavailable."), null);
        var cost = count * Procurement.PiecesPerPull;
        var currency = Templates.ById<CommodityTemplate>(Procurement.PieceId);
        var roster = StrategyState.Get()?.Roster;
        if (currency == null || roster == null)
            return (false, Locale.Text("WOMENACE::ui/procurement/unavailable", "Procurement is unavailable."), null);
        if (Pieces < cost)
            return (false, Locale.Text("WOMENACE::ui/procurement/need_pieces", "Not enough Collapse Pieces."), null);
        _trading = true;
        var unlocked = new List<UnitLeaderTemplate>();
        var committed = false;
        try
        {
            ReconcileClaims();
            var plan = Procurement.Plan(State, Catalogue.Rewards, count, StrategyState.Get().GetSeed());
            var rewards = plan.Rewards.Select(reward => Catalogue.ById[reward.Id]).ToList();
            var stock = new InventoryStock();
            var payment = stock.Available(currency, cost).ToList();
            if (payment.Count != cost)
                return (false, Locale.Text("WOMENACE::ui/procurement/need_pieces", "Not enough Collapse Pieces."), null);
            // Roster.AddHirableLeader is a list insertion, not HireLeader. Use the dossier's
            // authored leader without native Redeem's random choice or pickup dialog. Undo
            // these insertions if the item exchange fails in this synchronous transaction.
            foreach (var entry in rewards.Where(entry => entry.Leader != null))
            {
                unlocked.Add(entry.Leader);
                roster.AddHirableLeader(entry.Leader);
            }
            // Cosmetic unlocks are the committed claim itself, with no inventory item to mint.
            var outputs = rewards.Where(entry => !entry.IsUnlock)
                .GroupBy(entry => entry.Reward.Id)
                .Select(group => (group.First().Template, group.Count())).ToList();
            var result = InventoryExchange.Run(payment, outputs, Context.Log, stock);
            if (!result.ok)
                return (false, result.error, null);
            State.Apply(plan.State);
            committed = true;
            return (true, null, rewards);
        }
        catch (Exception ex)
        {
            Context.Log.Error($"Procurement failed: {ex}");
            return (false, Locale.Text("WOMENACE::ui/procurement/failed", "Procurement could not be completed."), null);
        }
        finally
        {
            try
            {
                if (!committed)
                    foreach (var leader in unlocked)
                        try { roster.m_HirableLeaders.Remove(leader); }
                        catch (Exception ex) { Context.Log.Error($"Procurement could not restore recruitment options: {ex}"); }
            }
            // Keep the guard held during rollback, and release it even if native cleanup or logging fails.
            finally { _trading = false; }
        }
    }
}
