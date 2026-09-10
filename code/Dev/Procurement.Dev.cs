using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Jiangyu.Game.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

[DevVerb]
public static class ProcurementDev
{
    [MutatingVerb]
    public static object Open() => OpenShop() ? Status() : new { error = "shop unavailable" };

    private static bool OpenShop()
    {
        if (ProcurementSystem.Instance == null || ShopSystem.Instance?.Open() != true)
            return false;
        ShopSystem.Instance.ShowProcurement();
        return true;
    }

    public static object Status()
    {
        var system = ProcurementSystem.Instance;
        if (system == null)
            return new { error = "procurement unavailable" };
        // VerbRunner reduces managed objects to ToString summaries. JSON text preserves
        // the nested ledgers and diagnostic arrays through that summary conversion.
        return global::System.Text.Json.JsonSerializer.Serialize(new
        {
            pieces = ProcurementSystem.Pieces,
            sardis = ShopTrade.Balance,
            state = system.State,
            sections = Procurement.Sections.Select(section => new
            {
                section = section.ToString(),
                count = system.Catalogue.Rewards.Count(reward => reward.Section == section),
                available = system.Catalogue.Rewards.Count(reward => reward.Section == section && system.State.Available(reward)),
            }).ToArray(),
            dossiers = system.Catalogue.Entries.Where(entry => entry.Leader != null).Select(entry =>
            {
                var status = UnitLeaderStatus.Unknown;
                StrategyState.Get()?.Roster?.GetLeaderByTemplate(entry.Leader, out status);
                return new { id = entry.Reward.Id, leader = entry.Leader.GetID(), status = status.ToString() };
            }).ToArray(),
            curios = system.Catalogue.Entries.Where(entry => entry.Reward.Section == ProcurementSection.Curios)
                .Select(entry => new { id = entry.Reward.Id, name = entry.Name, claimed = system.State.Claimed(entry.Reward.Id) }).ToArray(),
            view = ShopSystem.Instance?.ProcurementDiagnostics(),
        });
    }

    [MutatingVerb]
    public static object Pieces(int count = 100)
    {
        var template = Templates.ById<CommodityTemplate>(Procurement.PieceId);
        if (template == null || Inventory.Owned == null)
            return new { error = "inventory unavailable" };
        for (var i = 0; i < Math.Clamp(count, 0, 10000); i++)
            Inventory.AddItem(template);
        return Status();
    }

    [MutatingVerb]
    public static object Pity(string section, int remaining = 1)
    {
        var system = ProcurementSystem.Instance;
        if (system == null)
            return new { error = "procurement unavailable" };
        if (!Enum.TryParse<ProcurementSection>(section, true, out var value) || Procurement.Pity(value) == 0)
            return new { error = "unknown pity section" };
        system.State.Counters[value] = Math.Clamp(Procurement.Pity(value) - remaining, 0, Procurement.Pity(value));
        return Status();
    }

    [MutatingVerb]
    public static object Curio(string id = "perlica", bool unlocked = true)
    {
        var system = ProcurementSystem.Instance;
        if (system == null || StrategyState.Get()?.Roster == null)
            return new { error = "campaign unavailable" };
        var entry = system.Catalogue.Entries.FirstOrDefault(entry => entry.Outfit != null
            && (entry.Outfit.Id == id || entry.Reward.Id == id));
        if (entry == null)
            return new { error = "unknown curio outfit" };
        if (unlocked)
            system.State.Claims[entry.Reward.Id] = entry.Reward.Limit;
        else
            system.State.Claims.Remove(entry.Reward.Id);
        ShopSystem.Instance?.RefreshCurios();
        return Status();
    }

    [MutatingVerb]
    public static object Click(string name) => new { ok = ShopSystem.Instance?.ProcurementClick(name) == true };

    [MutatingVerb]
    public static object Preview(bool transfer = false)
    {
        if (!OpenShop())
            return new { error = "shop unavailable" };
        ShopSystem.Instance.ProcurementPreview(transfer);
        return Status();
    }
}

public sealed partial class ShopSystem
{
    internal void RefreshCurios()
    {
        if (_portrait != null)
            RefreshPortrait();
        if (_outfits?.IsVisible() == true)
            BuildOutfits();
        _procurement?.RefreshCurios();
    }

    internal object ProcurementDiagnostics() => _procurement?.Diagnostics();
    internal bool ProcurementClick(string name)
    {
        var button = UI.Find(_root, UiSelector.Name(name))?.TryCast<Button>();
        if (button == null || !button.enabledInHierarchy || !button.IsVisible())
            return false;
        button.clickable.SimulateSingleClick(null, 0);
        return true;
    }
    internal void ProcurementPreview(bool transfer) => _procurement.Preview(transfer);
}

internal sealed partial class ProcurementView
{
    internal void RefreshCurios()
    {
        Refresh();
        if (_pool.IsVisible())
            BuildPool();
    }

    internal object Diagnostics() => new
    {
        busy = _busy,
        nextCard = _nextCard,
        rewardCount = _shipment?.Count ?? 0,
        revealed = _cards.Count(card => card.Front.IsVisible()),
        dossier = _reveal.IsVisible(),
        transfer = _transfer.IsVisible(),
        results = _results.IsVisible(),
        home = _home.IsVisible(),
        pool = _pool.IsVisible(),
        piecesTexture = _artwork.HasTexture(ShopCurrency.Pieces),
        dossierTextures = Catalogue.Entries.Where(entry => entry.Leader != null)
            .Select(entry => new { id = entry.Reward.Id, loaded = entry.Art != null }).ToArray(),
        soundWarning = _soundWarning,
        cue = _cue?.Playing == true ? _cue.Clip?.name : null,
        cueMixer = _cue?.Playing == true ? _cue.Source?.outputAudioMixerGroup?.name : null,
        cargoProgress = _cargoProgress,
        cards = _cards.Select(card => new { width = card.Root.layout.width, height = card.Root.layout.height }).ToArray(),
    };

    internal void Preview(bool transfer)
    {
        StopSequence();
        var part = Catalogue.Entries.First(entry => entry.Reward.Section == ProcurementSection.Parts);
        var dossiers = Catalogue.Entries.Where(entry => entry.Leader != null).Take(2).ToArray();
        var rewards = Enumerable.Repeat(part, 10).ToArray();
        for (var i = 0; i < dossiers.Length; i++)
            rewards[3 + i * 3] = dossiers[i];
        if (Catalogue.Entries.FirstOrDefault(entry => entry.Outfit != null) is { } curio)
            rewards[9] = curio;
        _shipment = rewards;
        _busy = true;
        if (transfer)
            StartTransfer();
        else
            ShowResults();
    }
}
