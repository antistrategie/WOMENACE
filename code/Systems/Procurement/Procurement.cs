namespace WOMENACE.Code;

// Section names key saved counters. Append new sections to preserve existing order,
// which controls presentation, seeded selection and rarest-last pity priority.
public enum ProcurementSection { Parts, Materials, Equipment, Special, Dossiers, Curios }

public static class Procurement
{
    public const string PieceId = "commodity.wmgfl_collapse_piece";
    public const string DossierTag = "wmgfl_procurement_dossier";
    public const string ExcludedEquipmentTag = "wmgfl_procurement_excluded";
    public const string SoundBankId = "wmgfl_procurement_soundbank";
    public const int PiecePrice = 10;
    public const int PiecesPerPull = 5;
    public static readonly ProcurementSection[] Sections = Enum.GetValues<ProcurementSection>();

    public static int Weight(ProcurementSection section) => section switch
    {
        ProcurementSection.Parts => 8500,
        ProcurementSection.Materials => 1000,
        ProcurementSection.Equipment => 350,
        ProcurementSection.Special => 100,
        ProcurementSection.Dossiers => 40,
        ProcurementSection.Curios => 10,
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    public static int Pity(ProcurementSection section) => section switch
    {
        ProcurementSection.Equipment => 60,
        ProcurementSection.Special => 100,
        ProcurementSection.Dossiers => 300,
        _ => 0,
    };

    public static int TotalWeight => Sections.Sum(Weight);

    public static int EffectiveWeight(ProcurementSection section, Func<ProcurementSection, bool> available)
    {
        if (!available(section))
            return 0;
        return Weight(section) + (section == ProcurementSection.Parts
            ? Sections.Where(candidate => !available(candidate)).Sum(Weight) : 0);
    }

    // The plan owns a copy of the ledger. A failed inventory exchange cannot spend a claim,
    // advance pity or change the next random result.
    public static ProcurementPlan Plan(ProcurementState state, IReadOnlyList<ProcurementReward> catalogue, int count, int seed)
    {
        if (count != 1 && count != 10)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (!catalogue.Any(item => item.Section == ProcurementSection.Parts && item.Limit == 0))
            throw new InvalidOperationException("Weapon parts are unavailable.");
        var next = state.Copy();
        if (next.RandomState == 0)
        {
            var mixedSeed = unchecked((uint)seed ^ 0x9E3779B9u);
            next.RandomState = mixedSeed == 0 ? 1u : mixedSeed;
        }
        var rewards = new List<ProcurementReward>();
        for (var pull = 0; pull < count; pull++)
        {
            var pools = Sections.ToDictionary(section => section,
                section => catalogue.Where(item => item.Section == section && next.Available(item)).ToList());
            var chosen = ProcurementSection.Parts;
            foreach (var section in Sections)
            {
                var pity = Pity(section);
                if (pity == 0 || pools[section].Count == 0)
                    continue;
                next.Counters[section] = Math.Min(pity, next.Counter(section) + 1);
                // Rarity order gives a due section one normal slot. Other due counters stay
                // capped until a subsequent paid pull awards their section.
                if (next.Counter(section) == pity)
                    chosen = section;
            }
            if (chosen == ProcurementSection.Parts)
            {
                var roll = Next(next, TotalWeight);
                Func<ProcurementSection, bool> available = section => pools[section].Count > 0;
                foreach (var section in Sections)
                {
                    roll -= EffectiveWeight(section, available);
                    if (roll < 0)
                    {
                        chosen = section;
                        break;
                    }
                }
            }
            var pool = pools[chosen];
            var reward = pool[Next(next, pool.Count)];
            if (reward.Limit > 0)
                next.Claims[reward.Id] = next.Claimed(reward.Id) + 1;
            if (Pity(chosen) > 0)
                next.Counters[chosen] = 0;
            rewards.Add(reward);
        }
        return new ProcurementPlan(next, rewards);
    }

    private static int Next(ProcurementState state, int maximum)
    {
        var value = state.RandomState;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        state.RandomState = value;
        return (int)(value / 4294967296d * maximum);
    }
}

public sealed class ProcurementReward(string id, ProcurementSection section, int limit = 0)
{
    public readonly string Id = id;
    public readonly ProcurementSection Section = section;
    public readonly int Limit = limit;
    public bool ReturnsOnLoss => Limit > 0 && Section is ProcurementSection.Equipment or ProcurementSection.Special;
}

public sealed class ProcurementPlan(ProcurementState state, IReadOnlyList<ProcurementReward> rewards)
{
    public readonly ProcurementState State = state;
    public readonly IReadOnlyList<ProcurementReward> Rewards = rewards;
}

public sealed class ProcurementState
{
    public uint RandomState { get; set; }
    public Dictionary<ProcurementSection, int> Counters { get; set; } = [];
    public Dictionary<string, int> Claims { get; set; } = new(StringComparer.Ordinal);
    // Retain origin after loss so a sold copy bought back still counts towards its limit.
    public Dictionary<string, string> EquipmentItems { get; set; } = new(StringComparer.Ordinal);

    public int Counter(ProcurementSection section) => Counters.GetValueOrDefault(section);
    public int Claimed(string id) => Claims.GetValueOrDefault(id);
    public bool Available(ProcurementReward reward) => reward.Limit == 0 || Claimed(reward.Id) < reward.Limit;

    public void TrackEquipment(ProcurementReward reward, string itemGuid)
    {
        if (!reward.ReturnsOnLoss)
            return;
        if (string.IsNullOrEmpty(itemGuid))
            throw new ArgumentException("A Procurement equipment reward needs an item GUID.", nameof(itemGuid));
        EquipmentItems.Add(itemGuid, reward.Id);
    }

    public void RefreshEquipmentClaims(Func<string, bool> ownsItem)
    {
        // Finish the inventory read before changing any claims. An unreadable inventory
        // cannot reopen part of the pool, and affinity copies never enter this ledger.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (guid, id) in EquipmentItems)
            counts[id] = counts.GetValueOrDefault(id) + (ownsItem(guid) ? 1 : 0);
        foreach (var (id, count) in counts)
            Claims[id] = count;
    }

    public ProcurementState Copy() => new()
    {
        RandomState = RandomState,
        Counters = new(Counters),
        Claims = new(Claims, StringComparer.Ordinal),
        EquipmentItems = new(EquipmentItems, StringComparer.Ordinal),
    };

    public void Apply(ProcurementState state)
    {
        RandomState = state.RandomState;
        Counters = state.Counters;
        Claims = state.Claims;
        EquipmentItems = state.EquipmentItems;
    }
}
