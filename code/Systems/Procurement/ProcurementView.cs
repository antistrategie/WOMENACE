using System.Globalization;
using Il2CppInterop.Runtime;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;
using static WOMENACE.Code.ShopVisuals;

namespace WOMENACE.Code;

internal sealed partial class ProcurementView
{
    private readonly ModContext _context;
    private readonly ShopCurrency _currency;
    private readonly VisualElement _shop, _home, _pool, _exchange, _results, _transfer, _reveal;
    private readonly Action _refreshShop;
    private readonly ShopArtwork _artwork;
    private VisualElement _featured, _specials, _guarantees, _poolItems;
    private Label _message, _pageLabel;
    private Button _previous, _next, _one, _ten;
    private int _homePage, _poolPage;
    private ProcurementSection _section = ProcurementSection.Dossiers;
    private string _filter = "";
    private bool _visible;
    private ProcurementSystem System => ProcurementSystem.Instance;
    private ProcurementState State => System.State;
    private ProcurementCatalogue Catalogue => System.Catalogue;

    public ProcurementView(ModContext context, VisualElement shop, Action refreshShop, ShopCurrency currency, ShopArtwork artwork)
    {
        _context = context;
        _currency = currency;
        _artwork = artwork;
        _shop = shop;
        _refreshShop = refreshShop;
        _home = UI.Find(shop, UiSelector.Name("wm-procurement"));
        _home.pickingMode = PickingMode.Ignore;
        BuildHome();
        _pool = Element("wm-proc-pool-backdrop", shop);
        _exchange = Element("wm-proc-modal", shop);
        _transfer = Element("wm-proc-transfer", shop);
        _results = Element("wm-proc-results-backdrop", shop);
        _reveal = Element("wm-proc-reveal", shop);
        BindShipmentSkip();
        BindResultsContinue();
        _pool.name = "wm-procurement-pool";
        _exchange.name = "wm-procurement-exchange";
        _transfer.name = "wm-procurement-transfer";
        _results.name = "wm-procurement-results";
        _reveal.name = "wm-procurement-dossier";
        foreach (var overlay in new[] { _pool, _exchange, _transfer, _results, _reveal })
            overlay.SetVisible(false);
        _home.SetVisible(false);
    }

    private void BuildHome()
    {
        var content = Element("wm-proc-content", _home);
        var heading = Element("wm-proc-row wm-proc-heading", content);
        Text(Locale.Text("WOMENACE::ui/procurement/title", "Special Procurement"), "wm-proc-title", heading);
        heading.Add(Button(Locale.Text("WOMENACE::ui/procurement/pool", "REWARD POOL") + " ↗", () => OpenPool(ProcurementSection.Dossiers), "wm-proc-link"));
        var hero = Element("wm-proc-hero", content);
        BuildAmbientBackdrop(hero);
        Text("III", "wm-proc-roman", hero);
        var copy = Element("wm-proc-hero-copy", hero);
        Text(Locale.Text("WOMENACE::ui/procurement/third_generation", "THIRD GENERATION"), "wm-proc-generation-kicker", copy);
        Text(Locale.Text("WOMENACE::ui/procurement/generation_dolls", "DOLLS"), "wm-proc-generation", copy);
        Text(Limited(), "wm-proc-limited", copy);
        _featured = Element("wm-proc-featured", hero);
        var pager = Element("wm-proc-hero-footer wm-proc-row", hero);
        _pageLabel = Text("", "wm-proc-muted", pager);
        var controls = Element("wm-proc-row", pager);
        _previous = Button("‹", () => { _homePage--; RefreshHome(); }, "wm-proc-page");
        _next = Button("›", () => { _homePage++; RefreshHome(); }, "wm-proc-page");
        controls.Add(_previous);
        controls.Add(_next);
        BuildAmbientParticles(hero);
        Text(ProcurementCatalogue.SectionName(ProcurementSection.Special).ToUpperInvariant(), "wm-proc-subheading", content);
        _specials = Element("wm-proc-row wm-proc-specials", content);
        _guarantees = Element("wm-proc-guarantees wm-proc-row", _home);
        var footer = Element("wm-proc-footer wm-proc-row", _home);
        _message = Text("", "wm-proc-feedback", footer);
        _one = PullButton(1);
        _ten = PullButton(10);
        footer.Add(_one);
        footer.Add(_ten);
    }

    private Button PullButton(int count)
    {
        var button = Button("", () => Pull(count), "wm-proc-pull wm-proc-row" + (count == 10 ? " wm-shop-primary" : ""));
        button.name = "wm-procurement-pull-" + count;
        Text(Locale.Format("WOMENACE::ui/procurement/procure", "PROCURE ×{0}", count), "wm-proc-pull-label", button);
        _currency.Amount(button, ShopCurrency.Pieces, (count * Procurement.PiecesPerPull).ToString(CultureInfo.InvariantCulture));
        return button;
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        _home.SetVisible(visible);
        if (visible)
            _ambientTimer?.Resume();
        else
            _ambientTimer?.Pause();
        if (!visible)
        {
            StopSequence();
            CloseOverlay(_pool);
            CloseOverlay(_exchange);
        }
        else
            Refresh();
    }

    public void Refresh()
    {
        _refreshExchange?.Invoke();
        if (!_visible)
            return;
        RefreshHome();
        var available = Catalogue.Rewards.Count > 0;
        _one.SetEnabled(!_busy && available);
        _ten.SetEnabled(!_busy && available);
        _message.text = available ? "" : Locale.Text("WOMENACE::ui/procurement/unavailable", "Procurement is unavailable.");
    }

    private void RefreshHome()
    {
        var dossiers = Catalogue.Entries.Where(entry => entry.Leader != null).ToList();
        var pages = Math.Max(1, (dossiers.Count + 2) / 3);
        _homePage = Math.Clamp(_homePage, 0, pages - 1);
        _featured.Clear();
        foreach (var entry in dossiers.Skip(_homePage * 3).Take(3))
        {
            var card = Button("", () => OpenPool(ProcurementSection.Dossiers), "wm-proc-doll");
            card.name = "wm-procurement-featured-" + entry.Reward.Id;
            var art = _artwork.Create(entry.Art, "wm-proc-doll-art", card);
            var claimed = !State.Available(entry.Reward);
            card.EnableInClassList("wm-proc-claimed", claimed);
            art.style.opacity = claimed ? .25f : 1f;
            ShopVisuals.Draw(card, context => ShopVisuals.Gradient(context, context.visualElement.contentRect,
                new Color(.04f, .06f, .05f, 0), new Color(.04f, .06f, .05f, .6f), true), false);
            Text(Claim(entry), "wm-proc-claim", card);
            Text(entry.DollName.ToUpperInvariant(), "wm-proc-doll-name", card);
            _featured.Add(card);
        }
        _pageLabel.text = pages > 1 ? FormattableString.Invariant($"{_homePage + 1} / {pages}") : "";
        _previous.SetVisible(pages > 1);
        _next.SetVisible(pages > 1);
        _previous.SetEnabled(_homePage > 0);
        _next.SetEnabled(_homePage + 1 < pages);
        _specials.Clear();
        foreach (var entry in Catalogue.Entries.Where(entry => entry.Reward.Section == ProcurementSection.Special))
        {
            var card = Button("", () => OpenPool(ProcurementSection.Special), "wm-proc-special-card wm-proc-row");
            ShopVisuals.Surface(card, ShopSurface.Card, Colour(ProcurementSection.Special));
            Icon(entry, card);
            var copy = Element("wm-proc-item-copy", card);
            Text(entry.Name, "wm-proc-item-name", copy);
            Text(Claim(entry), "wm-proc-muted", copy);
            card.EnableInClassList("wm-proc-claimed", !State.Available(entry.Reward));
            _specials.Add(card);
        }
        _guarantees.Clear();
        foreach (var section in Procurement.Sections.Where(section => Procurement.Pity(section) > 0))
        {
            var counter = Button("", () => OpenPool(section), "wm-proc-guarantee");
            var labels = Element("wm-proc-row wm-proc-between", counter);
            Text(ProcurementCatalogue.SectionName(section).ToUpperInvariant(), "wm-proc-counter-label", labels);
            Text(Eligible(section) ? FormattableString.Invariant($"{State.Counter(section)} / {Procurement.Pity(section)}") : Complete(), "wm-proc-counter-label wm-proc-counter-value", labels).style.color = Colour(section);
            Progress(counter, section);
            _guarantees.Add(counter);
        }
    }

    private bool Eligible(ProcurementSection section) => Catalogue.Rewards.Any(reward => reward.Section == section && State.Available(reward));
    private double Rate(ProcurementSection section) => Procurement.EffectiveWeight(section, Eligible) * 100d / Procurement.TotalWeight;

    private void Progress(VisualElement parent, ProcurementSection section)
    {
        var track = Element("wm-proc-track", parent);
        var fill = Element("wm-proc-fill", track);
        fill.style.width = new StyleLength(Length.Percent(Eligible(section)
            ? Mathf.Clamp01(State.Counter(section) / (float)Procurement.Pity(section)) * 100f : 100f));
        fill.style.backgroundColor = Colour(section);
        ShopVisuals.Draw(fill, context => ShopVisuals.Gradient(context, context.visualElement.contentRect,
            new Color(1, 1, 1, .24f), new Color(1, 1, 1, 0)));
    }

    private string Claim(ProcurementCatalogue.Entry entry) => Locale.Format("WOMENACE::ui/procurement/claimed", "{0} / {1} CLAIMED", State.Claimed(entry.Reward.Id), entry.Reward.Limit);
    private static string Limited() => Locale.Text("WOMENACE::ui/procurement/limited", "LIMITED");
    private static string Complete() => Locale.Text("WOMENACE::ui/procurement/complete", "COMPLETE");
    private static string Continue() => Locale.Text("WOMENACE::ui/procurement/continue", "CONTINUE  →");

    private static Label Text(string text, string classes, VisualElement parent)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        Classes(label, "wm-proc-text " + classes);
        parent.Add(label);
        return label;
    }

    private static Button Button(string text, Action action, string classes = "")
    {
        var button = new Button();
        Classes(button, "wm-proc-button " + classes);
        if (text.Length > 0)
            Text(text, "wm-proc-button-caption", button);
        if (button.ClassListContains("wm-proc-pull") || button.ClassListContains("wm-shop-primary"))
            ShopVisuals.Surface(button, ShopSurface.Button);
        button.clickable.clicked += (Action)(() => { Sound.Click(); action(); });
        return button;
    }

    private VisualElement Portrait(ProcurementCatalogue.Entry entry, string classes, VisualElement parent) => entry.Outfit != null
        ? _artwork.Create(entry.Outfit.Asset, classes + " wm-proc-outfit-art", parent)
        : _artwork.Create(entry.Art, classes, parent);

    private VisualElement Icon(ProcurementCatalogue.Entry entry, VisualElement parent)
    {
        if (entry.Outfit != null)
            return Portrait(entry, "wm-proc-item-icon", parent);
        var image = Element("wm-proc-item-icon", parent, PickingMode.Ignore);
        image.style.backgroundImage = new StyleBackground(entry.Icon);
        return image;
    }

    private static Color Colour(ProcurementSection section) => section switch
    {
        ProcurementSection.Materials => new Color(.55f, .73f, .71f),
        ProcurementSection.Equipment => new Color(.69f, .64f, .76f),
        ProcurementSection.Special => new Color(.85f, .71f, .48f),
        ProcurementSection.Dossiers => new Color(.92f, .78f, .46f),
        ProcurementSection.Curios => new Color(.86f, .65f, .80f),
        _ => new Color(.69f, .74f, .55f),
    };

    private void CloseOverlay(VisualElement overlay)
    {
        if (ReferenceEquals(overlay, _exchange))
            _refreshExchange = null;
        overlay.SetVisible(false);
        // Detaching the content also stops its scheduled cosmetics. Every overlay
        // rebuilds its content before opening.
        overlay.Clear();
    }

    public bool Back()
    {
        if (_reveal.IsVisible()) DismissUnlock();
        else if (_transfer.IsVisible()) ShowResults();
        else if (_results.IsVisible())
        {
            if (_resultsReady)
                Finish();
        }
        else if (_exchange.IsVisible()) CloseOverlay(_exchange);
        else if (_pool.IsVisible()) CloseOverlay(_pool);
        else return false;
        return true;
    }
}
