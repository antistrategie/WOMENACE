using System.Globalization;
using Il2CppInterop.Runtime;
using Il2CppMenace.Items;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Game.Ui.Components;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;
using TextButton = Jiangyu.Game.Ui.Components.TextButton;

namespace WOMENACE.Code;

[DependsOn(typeof(ProcurementSystem))]
public sealed partial class ShopSystem : JiangyuSystem
{
    public static ShopSystem Instance { get; private set; }

    // Opens the shop from outside the workshop (the ship's navigation screen).
    internal static bool OpenFromNavigation() => Instance?._host.Open() ?? false;

    // Whether the workshop screen is showing Kalina's shop right now.
    internal static bool IsShopOpen => Instance?._host?.IsOpen ?? false;

    // What is in the basket on the tab that is showing: each item and how many of it. The
    // holotable shows it.
    internal static void Basket(List<(BaseItemTemplate Template, int Count)> into)
    {
        var shop = Instance;
        if (!IsShopOpen || shop._procurementActive || shop._catalogue == null)
            return;
        foreach (var (id, count) in shop.Selection)
            if (count > 0 && shop._catalogue.Items.TryGetValue(id, out var entry))
                into.Add((entry.Template, count));
    }

    // Whether the shop is open on its Procurement view.
    internal static bool IsProcurementOpen => IsShopOpen && Instance._procurementActive;
    public KalinaState State => Context.State.Get<KalinaState>();

    private VisualElement _root, _grid, _portrait, _outfits, _outfitGrid;
    private ScrollView _scroll;
    private IVisualElementScheduledItem _scrollRestore;
    private Label _total, _feedback, _empty;
    private TextButton _checkout;
    private ShopHost _host;
    private ShopCatalogue _catalogue;
    private ShopArtwork _artwork;
    private ShopCurrency _currency;
    private ProcurementView _procurement;
    private bool _procurementActive;
    // The basket of the tab that is showing: how many of each item. Leaving a tab empties it.
    private Dictionary<string, int> Selection { get; } = new(StringComparer.Ordinal);
    private readonly Vector2[] _scrollOffsets = new Vector2[2];
    private bool _buy = true;
    private int _holdEpoch, _scrollEpoch, _lastBalance = -1, _lastPieces = -1;

    // The shop's element in the workshop screen's root, which the strategy shell keeps under its shutters.
    internal const string RootName = "wm-shop-root";

    public override void OnInit()
    {
        Instance = this;
        _host = new ShopHost(Context, SetHostVisible, () => _greetingPending = true);
        UI.Inject(UiTarget.Screen<WorkshopUIScreen>().AppendTo(UiSelector.Name(WorkshopUi.Root)),
            "shop/kalina-shop", Bind);
        Context.Patches.Prefix("Il2CppMenace.UI.UIManager", "Update", BeforeUiUpdate);
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        _host.Reset();
        Selection.Clear();
        _currentDialogue = null;
        _greetingPending = false;
        _procurementActive = false;
        Array.Clear(_scrollOffsets, 0, _scrollOffsets.Length);
    }

    public override void OnUnload()
    {
        _host.Dispose();
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    public bool Open() => _host.Open();

    public void ShowWorkshop() => _host.ShowWorkshop();

    // The shop steps out of sight while a pull runs at the capsule machine, and back after it.
    internal void ShowDuringPull(bool show)
    {
        if (_root != null)
            _root.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
    }

    private void SetHostVisible(bool show)
    {
        _holdEpoch++;
        CancelScrollRestore();
        _root?.SetVisible(show);
        _procurement?.SetVisible(show && _procurementActive);
        HideOutfits();
        if (show && _grid != null)
        {
            BuildCatalogue();
            RefreshPortrait();
            RefreshLevel();
            RefreshDialogue();
            SetTradeVisibility();
        }
    }

    private void Bind(VisualElement root)
    {
        _root = root;
        root.name = RootName;
        UiLayout.Fill(root);
        root.pickingMode = PickingMode.Ignore;
        // Only detaching this injection ends the shop view. Removing a child tile must not
        // hide the whole shop.
        root.RegisterCallback<DetachFromPanelEvent>(DelegateSupport.ConvertDelegate<EventCallback<DetachFromPanelEvent>>(
            (Action<DetachFromPanelEvent>)(evt =>
            {
                if (_root?.Pointer == root.Pointer && evt.target?.TryCast<VisualElement>()?.Pointer == root.Pointer)
                    SetHostVisible(false);
            })));
        _grid = UI.Find(root, UiSelector.Name("wm-shop-grid"));
        _scroll = UI.Find(root, UiSelector.Name("wm-shop-scroll"))?.TryCast<ScrollView>();
        // What the list says with nothing to sell: in the list's own element and beside what
        // scrolls, so it is hidden with the list. It goes in the viewport's holder, reached by
        // name: the scroll view's hierarchy is a value type the interop boxes, which a collection
        // can free while it is in use.
        _empty = new Label(Locale.Text("WOMENACE::ui/kalina/no_stock", "No items to sell.").ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
        _empty.AddToClassList("wm-shop-empty");
        _empty.SetVisible(false);
        (_scroll.contentViewport.parent ?? _scroll.contentViewport).Add(_empty);
        ConstrainVerticalScroll(_scroll);
        _artwork = new ShopArtwork(Context);
        _currency = new ShopCurrency(_artwork);
        _total = _currency.Amount(UI.Find(root, UiSelector.Name("wm-shop-total")), ShopCurrency.Gold, "0");
        _feedback = UI.Find(root, UiSelector.Name("wm-shop-feedback"))?.TryCast<Label>();
        _portrait = UI.Find(root, UiSelector.Name("wm-kalina-portrait"));
        _outfits = UI.Find(root, UiSelector.Name("wm-kalina-outfits"));
        _outfitGrid = UI.Find(root, UiSelector.Name("wm-kalina-outfit-grid"));
        var tabs = UI.Find(root, UiSelector.Name("wm-shop-tabs"));
        foreach (var buy in new[] { true, false })
        {
            var button = new TextButton(buy ? BuyLabel() : SellLabel());
            button.Root.name = buy ? "wm-shop-buy" : "wm-shop-sell";
            button.Root.AddToClassList("wm-shop-tab");
            button.OnClick(() => SetTab(buy));
            tabs.Add(button.Root);
        }
        var procurementTab = new TextButton(Locale.Text("WOMENACE::ui/procurement/tab", "PROCUREMENT"));
        procurementTab.Root.name = "wm-shop-procurement";
        procurementTab.Root.AddToClassList("wm-shop-tab");
        procurementTab.OnClick(ShowProcurement);
        tabs.Add(procurementTab.Root);
        _checkout = new TextButton(BuyLabel()).OnClick(Checkout);
        _checkout.Root.name = "wm-shop-checkout-button";
        _checkout.Root.AddToClassList("wm-shop-primary");
        ShopVisuals.Button(_checkout.Root);
        UI.Find(root, UiSelector.Name("wm-shop-checkout"))?.Add(_checkout.Root);
        _scroll.RegisterCallback<WheelEvent>(DelegateSupport.ConvertDelegate<EventCallback<WheelEvent>>(
            (Action<WheelEvent>)(_ => { _holdEpoch++; CancelScrollRestore(); })), TrickleDown.TrickleDown);
        BindKalina();
        _procurement = new ProcurementView(Context, root, RefreshCheckout, _currency, _artwork);
        _currency.BuildWallet(UI.Find(root, UiSelector.Name("wm-shop-wallet")), () => _procurement.OpenExchange(50));
        UI.Localise(root);
        root.SetVisible(false);
        // Native rewards can change currencies outside the trade path. Read template counts
        // without walking item instances or rebuilding the catalogue.
        root.schedule.Execute(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
        {
            if (_host.IsOpen && _root.IsVisible()
                && (ShopTrade.Balance != _lastBalance || ProcurementSystem.Pieces != _lastPieces))
            {
                RefreshCheckout();
                _procurement.Refresh();
            }
        })).Every(250);
        _host.Refresh();
    }

    public void SetTab(bool buy)
    {
        SaveScrollOffset();
        Selection.Clear();
        _procurementActive = false;
        _procurement.SetVisible(false);
        _buy = buy;
        SetTradeVisibility();
        _feedback.text = "";
        BuildCatalogue();
    }

    public void ShowProcurement()
    {
        SaveScrollOffset();
        CancelScrollRestore();
        Selection.Clear();
        _procurementActive = true;
        SetTradeVisibility();
        _procurement.SetVisible(true);
    }

    private void SaveScrollOffset()
    {
        if (!_procurementActive && _scrollRestore == null)
            _scrollOffsets[_buy ? 0 : 1] = _scroll.scrollOffset;
    }

    private void RestoreScrollOffset()
    {
        CancelScrollRestore();
        var scroll = _scroll;
        var offset = _scrollOffsets[_buy ? 0 : 1];
        var epoch = _scrollEpoch;
        // Queue from a scheduled callback so a layout pass separates the rebuild from restoration.
        _scrollRestore = scroll.schedule.Execute(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
        {
            if (epoch != _scrollEpoch)
                return;
            if (_procurementActive)
            {
                _scrollRestore = null;
                return;
            }
            _scrollRestore = scroll.schedule.Execute(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
            {
                if (epoch != _scrollEpoch)
                    return;
                _scrollRestore = null;
                if (!_procurementActive)
                    scroll.scrollOffset = offset;
            }));
        }));
    }

    private void CancelScrollRestore()
    {
        _scrollEpoch++;
        try { _scrollRestore?.Pause(); }
        catch { }
        _scrollRestore = null;
    }

    private void SetTradeVisibility()
    {
        _scroll.SetVisible(!_procurementActive);
        UI.Find(_root, UiSelector.Name("wm-shop-checkout"))?.SetVisible(!_procurementActive);
        foreach (var name in new[] { "wm-shop-buy", "wm-shop-sell", "wm-shop-procurement" })
        {
            var tab = UI.Find(_root, UiSelector.Name(name));
            if (tab == null)
                continue;
            tab.EnableInClassList("wm-shop-tab-active", name == "wm-shop-procurement"
                ? _procurementActive : !_procurementActive && (name == "wm-shop-buy") == _buy);
            ShopVisuals.Refresh(tab);
        }
    }

    private void BuildCatalogue()
    {
        _catalogue = ShopCatalogue.Create(_buy);
        _holdEpoch++;
        _grid.Clear();
        foreach (var id in Selection.Keys.Where(id => !_catalogue.Items.ContainsKey(id)).ToList())
            Selection.Remove(id);
        var stock = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!_buy)
        {
            var inventory = new InventoryStock();
            foreach (var entry in _catalogue.Items.Values)
                stock[entry.Template.GetID()] = inventory.Available(entry.Template).Count();
            foreach (var id in Selection.Keys.ToList())
                Selection[id] = Math.Min(Selection[id], stock.GetValueOrDefault(id));
        }
        // With nothing at all to sell the list says so, once, in the middle of its space.
        var nothing = !_buy && stock.Values.All(count => count == 0);
        _empty.SetVisible(nothing);
        if (nothing)
        {
            RefreshCheckout();
            return;
        }
        foreach (var category in _catalogue.Categories)
        {
            var heading = new Label(category.Name.Resolve().ToUpperInvariant());
            heading.AddToClassList("wm-shop-section");
            if (_buy)
                _grid.Add(heading);
            else
            {
                // What is sold is sold by the heap: everything under the heading at once, or none of it.
                var row = new VisualElement();
                row.AddToClassList("wm-shop-section-row");
                row.Add(heading);
                var owned = new List<string>();
                foreach (var group in category.Groups)
                    foreach (var entry in group.Items)
                        if (stock.GetValueOrDefault(entry.Template.GetID()) > 0)
                            owned.Add(entry.Template.GetID());
                var all = owned.Count > 0 && owned.All(id => Selection.GetValueOrDefault(id) >= stock[id]);
                var select = new Button
                {
                    name = "wm-shop-select-all",
                    text = all
                    ? Locale.Text("WOMENACE::ui/kalina/select_none", "SELECT NONE")
                    : Locale.Text("WOMENACE::ui/kalina/select_all", "SELECT ALL")
                };
                select.AddToClassList("wm-shop-text-button");
                select.SetEnabled(owned.Count > 0);
                select.clickable.clicked += (Action)(() => SelectAll(owned, stock, !all));
                row.Add(select);
                _grid.Add(row);
            }
            foreach (var group in category.Groups)
                AddGroup(group, stock);
        }
        RestoreScrollOffset();
        RefreshCheckout();
    }

    private void SelectAll(List<string> ids, Dictionary<string, int> stock, bool all)
    {
        Sound.Click();
        foreach (var id in ids)
            Selection[id] = all ? stock[id] : 0;
        SaveScrollOffset();
        BuildCatalogue();
    }

    private void AddGroup(ShopCatalogue.Group definition, Dictionary<string, int> stock)
    {
        var group = new VisualElement();
        group.AddToClassList(_buy ? "wm-shop-group" : "wm-shop-sell-group");
        if (!string.IsNullOrEmpty(definition.Name))
        {
            var label = new Label(definition.Name);
            label.AddToClassList("wm-shop-category");
            group.Add(label);
        }
        var slots = new VisualElement();
        slots.AddToClassList("wm-shop-slots");
        group.Add(slots);
        foreach (var entry in definition.Items)
        {
            var template = entry.Template;
            var id = template.GetID();
            var price = entry.Price;
            var count = _buy ? int.MaxValue : stock.GetValueOrDefault(id);
            if (price <= 0 || count == 0)
                continue;
            var tile = new ItemTile(template, count);
            tile.Root.name = "wm-shop-item-" + id;
            tile.Root.AddToClassList("wm-shop-item");
            ItemTileStyle.Align(tile);
            tile.Badge.AddToClassList("wm-shop-chosen");
            // Parts use their Sardis price. Buy stock has no artificial supply limit to display.
            foreach (var element in UI.FindAll(tile.Root, UiSelector.Type<Label>()))
                element.SetVisible(false);
            _currency.Amount(tile.Root, ShopCurrency.Gold, price.ToString("N0", CultureInfo.InvariantCulture), "wm-shop-price");
            if (!_buy)
            {
                var owned = new Label(count.ToString(CultureInfo.InvariantCulture));
                owned.AddToClassList("wm-shop-owned");
                owned.pickingMode = PickingMode.Ignore;
                tile.Root.Add(owned);
            }
            WorkshopItemStyle.Apply(tile.Root, template);
            tile.SetChosen(Selection.GetValueOrDefault(id));
            var epoch = _holdEpoch;
            tile.OnAdjust((delta, repeat) =>
            {
                if (repeat && epoch != _holdEpoch)
                    return;
                if (!repeat)
                    epoch = _holdEpoch;
                var current = Selection.GetValueOrDefault(id);
                if (delta > 0 && (_buy ? _catalogue.Total(Selection) + price > ShopTrade.Balance : current >= count))
                    return;
                Selection[id] = Math.Clamp(current + delta, 0, count);
                tile.SetChosen(Selection[id]);
                if (!repeat)
                    Sound.Click();
                _feedback.text = "";
                RefreshCheckout();
            });
            slots.Add(tile.Root);
        }
        // A group with nothing in it to sell is left out.
        if (_buy || slots.childCount > 0)
            _grid.Add(group);
    }

    private void RefreshCheckout()
    {
        _lastBalance = ShopTrade.Balance;
        _lastPieces = ProcurementSystem.Pieces;
        _currency.Refresh(_lastBalance, _lastPieces);
        var total = _catalogue.Total(Selection);
        _total.text = Math.Max(0, total).ToString("N0", CultureInfo.InvariantCulture);
        UI.Find(_checkout.Root, UiSelector.Class("text-button-label"))?.TryCast<Label>()?.text = _buy ? BuyLabel() : SellLabel();
        _checkout.Root.SetEnabled(total > 0 && (!_buy || total <= _lastBalance));
    }

    private void Checkout()
    {
        var result = Trade(Selection, _buy);
        if (result.ok)
            Selection.Clear();
        SaveScrollOffset();
        BuildCatalogue();
        _feedback.text = result.ok ? "" : result.error;
    }

    public (bool ok, string error) Trade(IReadOnlyDictionary<string, int> selection, bool buy)
    {
        var previousLevel = State.Level;
        var result = ShopTrade.Run(selection, buy, State, Context.Log);
        if (result.ok && buy && State.Level > previousLevel)
            Speak(previousLevel);
        if (result.ok && _root != null)
            RefreshLevel();
        return result;
    }

    private static string BuyLabel() => Locale.Text("WOMENACE::ui/kalina/buy", "BUY");
    private static string SellLabel() => Locale.Text("WOMENACE::ui/kalina/sell", "SELL");

    private static void ConstrainVerticalScroll(ScrollView scroll)
    {
        scroll.mode = ScrollViewMode.Vertical;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        // Native ScrollView styles give their content an intrinsic width. Bind it to the actual
        // viewport so percentage-width groups wrap before they reach Kalina or the panel edge.
        scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(
            DelegateSupport.ConvertDelegate<EventCallback<GeometryChangedEvent>>((Action<GeometryChangedEvent>)(evt =>
            {
                if (evt.newRect.width > 0)
                    scroll.contentContainer.style.width = new StyleLength(evt.newRect.width);
            })));
    }
}
