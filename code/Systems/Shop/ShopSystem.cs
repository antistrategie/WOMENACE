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

public sealed partial class ShopSystem : JiangyuSystem
{
    public static ShopSystem Instance { get; private set; }
    public KalinaState State => Context.State.Get<KalinaState>();

    private VisualElement _root, _grid, _portrait, _affinity, _outfits, _outfitGrid;
    private ScrollView _scroll;
    private Label _balance, _total, _feedback;
    private TextButton _checkout;
    private ShopHost _host;
    private ShopCatalogue _catalogue;
    private readonly Dictionary<string, int> _buySelection = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _sellSelection = new(StringComparer.Ordinal);
    private readonly Vector2[] _scrollOffsets = new Vector2[2];
    private bool _buy = true;
    private int _holdEpoch, _lastBalance = -1;
    private Dictionary<string, int> Selection => _buy ? _buySelection : _sellSelection;

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
        _buySelection.Clear();
        _sellSelection.Clear();
        _currentDialogue = null;
        _greetingPending = false;
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

    private void SetHostVisible(bool show)
    {
        _holdEpoch++;
        if (!show)
            HoverDelay.Cancel(ref _affinityHover);
        _root?.SetVisible(show);
        _affinity?.SetVisible(false);
        HideOutfits();
        if (show && _grid != null)
        {
            BuildCatalogue();
            RefreshPortrait();
            RefreshDialogue();
        }
    }

    private void Bind(VisualElement root)
    {
        _root = root;
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
        ConstrainVerticalScroll(_scroll);
        _balance = UI.Find(root, UiSelector.Name("wm-shop-balance"))?.TryCast<Label>();
        _total = UI.Find(root, UiSelector.Name("wm-shop-total"))?.TryCast<Label>();
        _feedback = UI.Find(root, UiSelector.Name("wm-shop-feedback"))?.TryCast<Label>();
        _portrait = UI.Find(root, UiSelector.Name("wm-kalina-portrait"));
        _affinity = UI.Find(root, UiSelector.Name("wm-kalina-affinity"));
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
        _checkout = new TextButton(BuyLabel()).OnClick(Checkout);
        _checkout.Root.name = "wm-shop-checkout-button";
        UI.Find(root, UiSelector.Name("wm-shop-checkout"))?.Add(_checkout.Root);
        _scroll.RegisterCallback<WheelEvent>(DelegateSupport.ConvertDelegate<EventCallback<WheelEvent>>(
            (Action<WheelEvent>)(_ => _holdEpoch++)), TrickleDown.TrickleDown);
        BindKalina();
        UI.Localise(root);
        root.SetVisible(false);
        // Native rewards and bridge grants can change Sardis outside the trade path. This reads
        // one template count, without walking coin instances or rebuilding the catalogue.
        root.schedule.Execute(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
        {
            if (_host.IsOpen && _root.IsVisible() && ShopTrade.Balance != _lastBalance)
                RefreshCheckout();
        })).Every(250);
        _host.Refresh();
    }

    public void SetTab(bool buy)
    {
        _scrollOffsets[_buy ? 0 : 1] = _scroll.scrollOffset;
        _buy = buy;
        _feedback.text = "";
        BuildCatalogue();
    }

    private void BuildCatalogue()
    {
        _catalogue = ShopCatalogue.Create(_buy);
        _holdEpoch++;
        _grid.Clear();
        foreach (var buy in new[] { true, false })
            UI.Find(_root, UiSelector.Name(buy ? "wm-shop-buy" : "wm-shop-sell"))
                ?.EnableInClassList("wm-shop-tab-active", buy == _buy);
        var stock = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!_buy)
        {
            var inventory = new InventoryStock();
            foreach (var entry in _catalogue.Items.Values)
                stock[entry.Template.GetID()] = inventory.Available(entry.Template).Count();
            foreach (var id in Selection.Keys.ToList())
                Selection[id] = Math.Min(Selection[id], stock.GetValueOrDefault(id));
        }
        foreach (var category in _catalogue.Categories)
        {
            var heading = new Label(category.Name.Resolve());
            heading.AddToClassList("wm-shop-section");
            _grid.Add(heading);
            foreach (var group in category.Groups)
                AddGroup(group, stock);
        }
        _scroll.scrollOffset = _scrollOffsets[_buy ? 0 : 1];
        RefreshCheckout();
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
            tile.Badge.AddToClassList("wm-gift-box__badge");
            // Parts use their Sardis price. Buy stock has no artificial supply limit to display.
            foreach (var element in UI.FindAll(tile.Root, UiSelector.Type<Label>()))
                element.SetVisible(false);
            var priceLabel = new Label(price.ToString());
            priceLabel.AddToClassList("wm-shop-price");
            priceLabel.pickingMode = PickingMode.Ignore;
            tile.Root.Add(priceLabel);
            if (!_buy)
            {
                var owned = new Label(count.ToString());
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
        if (!_buy && slots.childCount == 0)
            slots.Add(new Label(Locale.Text("WOMENACE::ui/kalina/no_stock", "No items to sell.")));
        _grid.Add(group);
    }

    private void RefreshCheckout()
    {
        _lastBalance = ShopTrade.Balance;
        _balance.text = Locale.Format("WOMENACE::ui/kalina/balance", "{0:N0} Sardis Gold", _lastBalance);
        var total = _catalogue.Total(Selection);
        _total.text = Locale.Format("WOMENACE::ui/kalina/total", "{0:N0} Sardis", Math.Max(0, total));
        UI.Find(_checkout.Root, UiSelector.Class("text-button-label"))?.TryCast<Label>()?.text = _buy ? BuyLabel() : SellLabel();
        _checkout.Root.SetEnabled(total > 0 && (!_buy || total <= _lastBalance));
    }

    private void Checkout()
    {
        var result = Trade(Selection, _buy);
        if (result.ok)
            Selection.Clear();
        _scrollOffsets[_buy ? 0 : 1] = _scroll.scrollOffset;
        BuildCatalogue();
        _feedback.text = result.ok ? "" : result.error;
    }

    public (bool ok, string error) Trade(IReadOnlyDictionary<string, int> selection, bool buy)
    {
        var previousLevel = State.Level;
        var result = ShopTrade.Run(selection, buy, State, Context.Log);
        if (result.ok && buy && State.Level > previousLevel)
            Speak(previousLevel);
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
