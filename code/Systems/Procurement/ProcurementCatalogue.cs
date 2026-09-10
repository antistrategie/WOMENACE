using Il2CppMenace.Items;
using Il2CppMenace.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

public sealed class ProcurementCatalogue
{
    public const string TypeWeapon = "weapon";
    public const string TypeArmour = "armour";
    public const string TypeVehicle = "vehicle";

    // Limits are lifetime Procurement claims. Affinity grants have their own ledger.
    private static readonly (string Id, int Limit)[] SpecialItems =
    {
        ("specialweapon.asteria_railgun", 1),
        ("vehicle.koleda_car", 1),
    };

    public IReadOnlyList<Entry> Entries { get; }
    public IReadOnlyList<ProcurementReward> Rewards { get; }
    public IReadOnlyDictionary<string, Entry> ById { get; }

    private ProcurementCatalogue(IEnumerable<Entry> entries)
    {
        // Sextans leads the displayed Doll lists.
        Entries = entries.DistinctBy(entry => entry.Reward.Id).OrderBy(entry => entry.Reward.Section)
            .ThenBy(entry => entry.Leader?.GetID() == "squad_leader.sextans" ? 0 : 1)
            .ThenBy(entry => entry.Reward.Id, StringComparer.Ordinal).ToList();
        // Seeded selection depends on reward order, independently of the display priority.
        Rewards = Entries.Select(entry => entry.Reward).OrderBy(reward => reward.Section)
            .ThenBy(reward => reward.Id, StringComparer.Ordinal).ToList();
        var byId = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var entry in Entries)
            byId.Add(entry.Reward.Id, entry);
        ById = byId;
    }

    public static ProcurementCatalogue Create(IModLog log)
    {
        try
        {
            var entries = new List<Entry>();
            foreach (var weaponClass in WeaponClasses.All)
                foreach (var id in WeaponParts.ForClass(weaponClass))
                    Add(entries, id, ProcurementSection.Parts, log);
            foreach (var id in new[] { "commodity.construct_alloys", "commodity.construct_weapon_parts",
                "commodity.construct_advanced_alloys", "commodity.construct_cybernetic_optics" })
                Add(entries, id, ProcurementSection.Materials, log);
            // The first entry wins deduplication, so explicit limits precede standard stock.
            foreach (var (id, limit) in SpecialItems)
                Add(entries, id, ProcurementSection.Special, log, limit);
            AddOptional(entries, EquipmentEntries<WeaponTemplate>(), nameof(WeaponTemplate), log);
            AddOptional(entries, EquipmentEntries<ArmorTemplate>(), nameof(ArmorTemplate), log);
            AddOptional(entries, EquipmentEntries<VehicleItemTemplate>(), nameof(VehicleItemTemplate), log);
            AddOptional(entries, DossierEntries(log), nameof(DossierItemTemplate), log);
            foreach (var outfit in Kalina.CurioOutfits)
                entries.Add(new Entry(null, new ProcurementReward(outfit.RewardId, ProcurementSection.Curios, 1), outfit: outfit));
            if (!entries.Any(entry => entry.Reward.Section == ProcurementSection.Parts))
                throw new InvalidOperationException("Weapon parts are unavailable.");
            var catalogue = new ProcurementCatalogue(entries);
            foreach (var section in Procurement.Sections)
            {
                var count = catalogue.Rewards.Count(reward => reward.Section == section);
                if (count == 0)
                    log.Warn($"Procurement catalogue: no {section} rewards are available.");
                else
                    log.Debug($"Procurement catalogue: {section} has {count} rewards.");
            }
            return catalogue;
        }
        catch (Exception ex)
        {
            // Cache fatal catalogue failures until templates or the scene reload. The shop
            // still binds, but cannot accept payment without a usable fallback pool of parts.
            log.Error($"Procurement catalogue is unavailable: {ex}");
            return new ProcurementCatalogue([]);
        }
    }

    private static void Add(List<Entry> entries, string id, ProcurementSection section, IModLog log, int limit = 0)
    {
        if (Templates.ById<BaseItemTemplate>(id) is { } template)
            entries.Add(new Entry(template, new ProcurementReward(id, section, limit)));
        else
            log.Warn($"Procurement catalogue: {section} reward '{id}' is unavailable.");
    }

    private static void AddOptional(List<Entry> entries, IEnumerable<Entry> source, string name, IModLog log)
    {
        try
        {
            // Finish each optional source before adding it, so a failure cannot leave a
            // partial subset of that source or prevent unrelated rewards from loading.
            var loaded = source.ToList();
            entries.AddRange(loaded);
            log.Debug($"Procurement catalogue: {name} yielded {loaded.Count} entries.");
        }
        catch (Exception ex)
        {
            log.Warn($"Procurement catalogue: {name} rewards are unavailable: {ex}");
        }
    }

    private static IEnumerable<Entry> EquipmentEntries<T>() where T : BaseItemTemplate
    {
        foreach (var item in Templates.Packaged<T>())
            if (IsStandardEquipment(item))
                yield return new Entry(item, new ProcurementReward(item.GetID(), ProcurementSection.Equipment));
    }

    private static IEnumerable<Entry> DossierEntries(IModLog log)
    {
        foreach (var dossier in Templates.All<DossierItemTemplate>().Where(IsProcurementDossier))
        {
            if (dossier.m_UnlockedLeaders is { Length: 1 } leaders && leaders[0] != null)
                yield return new Entry(dossier, new ProcurementReward(dossier.GetID(), ProcurementSection.Dossiers, 1), leaders[0]);
            else
                log.Warn($"Procurement dossier {dossier.GetID()} requires exactly one unlocked leader and is excluded from the pool.");
        }
    }

    private static bool IsStandardEquipment(BaseItemTemplate item)
    {
        // BlackMarket.AddStacks (RVA 0x56AD20) requires positive stock quantity and
        // GetTradeValue(), then applies rarity and campaign progress. Procurement covers
        // the full campaign, but never includes templates outside that market catalogue.
        if (item.BlackMarketMaxQuantity <= 0 || item.GetTradeValue() <= 0
            || item.m_IsGarbage || item.MinCampaignProgress > 100)
            return false;
        // A market flag alone is insufficient for the named unfinished DMR in the shipped data.
        var comment = item.m_GameDesignComment ?? "";
        if (comment.Contains("not done", StringComparison.OrdinalIgnoreCase)
            || comment.Contains("not implemented", StringComparison.OrdinalIgnoreCase)
            || comment.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
            return false;
        if (item.TryCast<VehicleItemTemplate>() is { } vehicle && vehicle.EntityTemplate == null)
            return false;
        var name = DisplayName(item);
        return !string.IsNullOrWhiteSpace(name) && name != item.GetID()
            && !name.Contains("placeholder", StringComparison.OrdinalIgnoreCase)
            && (item.TryCast<ItemTemplate>()?.IconEquipment ?? item.Icon) != null;
    }

    private static string DisplayName(BaseItemTemplate item)
    {
        // VehicleItemTemplate.GetName (RVA 0x565B00) returns EntityTemplate.Title.
        // Its own Title is empty even for released black market vehicles.
        return Templates.DefaultText(item.GetName());
    }

    public static bool IsProcurementDossier(DossierItemTemplate template)
    {
        var tags = template?.Tags;
        if (tags != null)
            for (var i = 0; i < tags.Count; i++)
                if (tags[i]?.GetID() == Procurement.DossierTag)
                    return true;
        return false;
    }

    public sealed class Entry(BaseItemTemplate template, ProcurementReward reward, UnitLeaderTemplate leader = null, Kalina.Outfit outfit = null)
    {
        public readonly BaseItemTemplate Template = template;
        public readonly ProcurementReward Reward = reward;
        public readonly UnitLeaderTemplate Leader = leader;
        public readonly Kalina.Outfit Outfit = outfit;
        public bool IsUnlock => Leader != null || Outfit != null;
        public string Name => Outfit?.Name.Resolve() ?? DisplayName(Template);
        public string DollName => Templates.DefaultText(Template.ShortName, Name);
        public Texture2D Art => Portraits.GetStanding(Leader?.SpeakerTemplate, StandingPortrait.Left);
        public Sprite Icon => Template?.TryCast<ItemTemplate>()?.IconEquipment ?? Template?.Icon;
        public string Type => Template?.TryCast<VehicleItemTemplate>() != null ? TypeVehicle
            : Template?.TryCast<ArmorTemplate>() != null ? TypeArmour : TypeWeapon;
    }

    public static string SectionName(ProcurementSection section) => section switch
    {
        ProcurementSection.Parts => Locale.Text("WOMENACE::ui/procurement/parts", "Weapon parts"),
        ProcurementSection.Materials => Locale.Text("WOMENACE::ui/procurement/materials", "Core materials"),
        ProcurementSection.Equipment => Locale.Text("WOMENACE::ui/procurement/equipment", "Standard equipment"),
        ProcurementSection.Special => Locale.Text("WOMENACE::ui/procurement/special", "Special equipment"),
        ProcurementSection.Dossiers => Locale.Text("WOMENACE::ui/procurement/dossiers", "Third-generation dossiers"),
        ProcurementSection.Curios => Locale.Text("WOMENACE::ui/procurement/curios", "Curios"),
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };
}
