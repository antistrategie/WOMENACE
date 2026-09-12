using Il2CppInterop.Runtime;
using Il2CppMenace.PlayerSettings;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Game.Ui.Components;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

public sealed partial class ShopSystem
{
    private IVisualElementScheduledItem _affinityHover;
    private readonly System.Random _dialogueRandom = new();
    private Label _dialogue;
    private VisualElement _affinityContent;
    private KalinaDialogue.Line _currentDialogue;
    private bool _greetingPending;

    private void BindKalina()
    {
        _dialogue = UI.Find(_root, UiSelector.Name("wm-kalina-dialogue"))?.TryCast<Label>();
        _dialogue?.SetVisible(false);
        ShopVisuals.Surface(_affinity, ShopSurface.Dialog);
        _affinityContent = new VisualElement { pickingMode = PickingMode.Ignore };
        _affinity.Add(_affinityContent);
        ShopVisuals.Surface(UI.Find(_root, UiSelector.Class("wm-kalina-outfit-panel")), ShopSurface.Dialog);
        ConstrainVerticalScroll(UI.Find(_root, UiSelector.Name("wm-kalina-outfit-scroll"))?.TryCast<ScrollView>());
        _portrait.RegisterCallback<PointerEnterEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerEnterEvent>>(
            (Action<PointerEnterEvent>)(_ => _affinityHover = HoverDelay.Schedule(_portrait, ShowAffinity))));
        _portrait.RegisterCallback<PointerLeaveEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerLeaveEvent>>(
            (Action<PointerLeaveEvent>)(_ => { HoverDelay.Cancel(ref _affinityHover); _affinity.SetVisible(false); })));
        foreach (var element in new[] { _portrait, _affinity })
            element.RegisterCallback<GeometryChangedEvent>(DelegateSupport.ConvertDelegate<EventCallback<GeometryChangedEvent>>(
                (Action<GeometryChangedEvent>)(_ => PositionAffinity())));
        _portrait.RegisterCallback<PointerDownEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerDownEvent>>(
            (Action<PointerDownEvent>)(evt =>
            {
                if (evt.button == 0)
                {
                    Sound.Click();
                    ShowOutfits();
                }
            })));
        var close = UI.Find(_root, UiSelector.Name("wm-kalina-outfits-close"))?.TryCast<Button>();
        if (close != null)
            close.clickable.clicked += (Action)(() => { Sound.Click(); HideOutfits(); });
        UI.Find(_root, UiSelector.Name("wm-kalina-outfits-blocker"))?.RegisterCallback<PointerDownEvent>(
            DelegateSupport.ConvertDelegate<EventCallback<PointerDownEvent>>((Action<PointerDownEvent>)(_ => HideOutfits())));
        _outfits.SetVisible(false);
        _affinity.SetVisible(false);
    }

    private void BeforeUiUpdate(PatchInfo info)
    {
        if (!_host.IsOpen || _root?.IsVisible() != true)
            return;
        var manager = (info.Instance as Il2CppSystem.Object)?.TryCast<UIManager>();
        if (manager == null || manager.GetCurrentDialog() != null
            || manager.GetActiveScreen()?.TryCast<WorkshopUIScreen>() == null
            || !KeyBindPlayerSetting.Back.Get().IsJustReleased())
            return;
        // UIManager.Update is non-virtual (RVA 0x8214D0). Dismissing this modal consumes
        // one UI update so native Back cannot also close its host. All later updates run
        // normally, and native dialogs above the shop retain their own input handling.
        if (_outfits?.IsVisible() == true)
        {
            HideOutfits();
            info.Skip = true;
        }
        else if (_procurement?.Back() == true)
            info.Skip = true;
    }

    private void RefreshDialogue()
    {
        if (_dialogue == null)
            return;
        if (_greetingPending || _currentDialogue == null)
            Speak();
        else
        {
            _dialogue.text = _currentDialogue.Text.Resolve();
            _dialogue.SetVisible(true);
        }
    }

    private void Speak(int previousLevel = 0)
    {
        if (!_host.IsOpen || _dialogue == null)
            return;
        _currentDialogue = KalinaDialogue.Choose(State.Level, Kalina.MaxLevel, DateTime.Now.Hour,
            _dialogueRandom, _currentDialogue?.Text.Fallback, previousLevel);
        _greetingPending = false;
        _dialogue.text = _currentDialogue?.Text.Resolve() ?? "";
        _dialogue.SetVisible(_currentDialogue != null);
    }

    private void RefreshPortrait()
    {
        var outfit = Kalina.Outfits.FirstOrDefault(outfit => outfit.Id == State.OutfitId
            && outfit.IsUnlocked(State, ProcurementSystem.Instance.State)) ?? Kalina.AffinityOutfits[0];
        _portrait.Clear();
        ShopVisuals.Draw(_portrait, context =>
        {
            var p = context.painter2D;
            var centre = new Vector2(170, 160);
            ShopVisuals.Glow(context, centre, 175, new Color(.58f, .44f, .22f, .18f));
            ShopVisuals.Arc(p, centre, 116, 0, 360, new Color(.54f, .47f, .31f, .12f), 1);
        });
        var outline = ShopVisuals.Element("wm-kalina-outline", _portrait, PickingMode.Ignore);
        foreach (var offset in new[] { new Vector2(-1.5f, 0), new Vector2(1.5f, 0), new Vector2(0, -1.5f), new Vector2(0, 1.5f) })
        {
            var edge = Art(outfit, 1f);
            edge.style.position = Position.Absolute;
            edge.style.left = offset.x;
            edge.style.top = offset.y;
            outline.Add(edge);
        }
        _portrait.Add(Art(outfit, 1f));
        ShopVisuals.Draw(_portrait, context =>
        {
            var rect = context.visualElement.contentRect;
            ShopVisuals.Gradient(context, new Rect(0, rect.height - 65f, rect.width, 65f),
                new Color(.025f, .035f, .03f, 0), new Color(.025f, .035f, .03f, .9f), true);
        }, false);
        var hint = ShopVisuals.Element("wm-kalina-outfit-hint", _portrait, PickingMode.Ignore);
        var icon = ShopVisuals.Element("wm-kalina-outfit-hint-icon", hint, PickingMode.Ignore);
        ShopVisuals.Draw(icon, context =>
        {
            var p = context.painter2D;
            var gold = ShopVisuals.Gold;
            ShopVisuals.Arc(p, new Vector2(9, 5), 2.5f, 180, 360, gold, 1.2f);
            ShopVisuals.Arc(p, new Vector2(9, 5), 2.5f, 0, 90, gold, 1.2f);
            ShopVisuals.Line(p, gold, 1.2f, new Vector2(9, 7.5f), new Vector2(9, 9));
            ShopVisuals.Line(p, gold, 1.2f, stackalloc Vector2[]
            {
                new(9, 9), new(16, 13.5f), new(16, 15), new(2, 15), new(2, 13.5f), new(9, 9),
            });
        });
        var label = new Label(Locale.Text("WOMENACE::ui/kalina/outfit", "Outfit")) { pickingMode = PickingMode.Ignore };
        label.AddToClassList("wm-kalina-outfit-hint-label");
        hint.Add(label);
    }

    // Crop to visible artwork before scaling. The source canvases range from 672 x 868 to 1920 x
    // 1920 and have different transparent margins. This keeps faces at the approved common height.
    private VisualElement Art(Kalina.Outfit outfit, float scale)
    {
        var frame = new VisualElement { pickingMode = PickingMode.Ignore };
        frame.style.width = new StyleLength(300f * scale);
        frame.style.height = new StyleLength(432f * scale);
        frame.style.overflow = Overflow.Hidden;
        var crop = new VisualElement { pickingMode = PickingMode.Ignore };
        crop.style.position = Position.Absolute;
        crop.style.left = new StyleLength(outfit.Left * scale);
        crop.style.top = new StyleLength(outfit.Top * scale);
        crop.style.width = new StyleLength(outfit.Width * scale);
        crop.style.height = new StyleLength(outfit.Height * scale);
        crop.style.overflow = Overflow.Hidden;
        // The runtime asset registry indexes image additions by their flattened bundle name.
        // Read the full texture canvas because the authored crop coordinates include its margins.
        var texture = _artwork.Texture(outfit.Asset);
        var image = new VisualElement { pickingMode = PickingMode.Ignore };
        var imageScale = scale * outfit.Width / (outfit.CropRight - outfit.CropLeft);
        image.style.position = Position.Absolute;
        image.style.left = new StyleLength(-outfit.CropLeft * imageScale);
        image.style.top = new StyleLength(-outfit.CropTop * imageScale);
        image.style.width = new StyleLength(outfit.SourceWidth * imageScale);
        image.style.height = new StyleLength(outfit.SourceHeight * imageScale);
        image.style.backgroundImage = new StyleBackground(texture);
        image.AddToClassList("wm-kalina-art");
        crop.Add(image);
        frame.Add(crop);
        return frame;
    }

    public void ShowAffinity()
    {
        if (!_host.IsOpen)
            return;
        _affinityContent.Clear();
        var head = new VisualElement();
        head.AddToClassList("wm-aff-head");
        var title = new Label(Locale.Text("WOMENACE::ui/affinity", "AFFINITY"));
        title.AddToClassList("wm-aff-title");
        head.Add(title);
        var level = new Label(Locale.Format("WOMENACE::ui/affinity/level", "LEVEL {0:00}", State.Level));
        level.AddToClassList("wm-aff-level");
        head.Add(level);
        _affinityContent.Add(head);
        for (var i = 1; i <= Kalina.MaxLevel; i++)
        {
            var rewards = Kalina.AffinityOutfits.Where(outfit => outfit.Level == i)
                .Select(outfit => new AffinityTooltip.Reward(i, outfit.Name.Resolve(), AffinityTooltip.RewardKind.Outfit)).ToList();
            _affinityContent.Add(AffinitySystem.BuildLevelRow(i, State.Level, rewards, Kalina.MaxLevel));
        }
        UiLayout.IgnorePicking(_affinity);
        _affinity.SetVisible(true);
        PositionAffinity();
    }

    private void PositionAffinity()
    {
        if (_affinity?.IsVisible() != true || _affinity.parent == null || _portrait?.panel == null)
            return;
        var portrait = _portrait.worldBound;
        var size = _affinity.worldBound;
        var width = size.width > 1f ? size.width : 330f;
        var height = size.height > 1f ? size.height : 420f;
        var bounds = _root.worldBound;
        var left = Math.Max(bounds.xMin + 12f, portrait.xMin - width - 12f);
        var top = Math.Clamp(portrait.center.y - height / 2f, bounds.yMin + 12f,
            Math.Max(bounds.yMin + 12f, bounds.yMax - height - 12f));
        var local = _affinity.parent.WorldToLocal(new Vector2(left, top));
        _affinity.style.left = new StyleLength(local.x);
        _affinity.style.top = new StyleLength(local.y);
    }

    public void ShowOutfits()
    {
        if (!_host.IsOpen)
            return;
        _holdEpoch++;
        HoverDelay.Cancel(ref _affinityHover);
        _affinity.SetVisible(false);
        BuildOutfits();
        _outfits.SetVisible(true);
    }

    private void BuildOutfits()
    {
        var scroll = UI.Find(_root, UiSelector.Name("wm-kalina-outfit-scroll"))?.TryCast<ScrollView>();
        var offset = scroll.scrollOffset;
        _outfitGrid.Clear();
        foreach (var outfit in Kalina.VisibleOutfits(ProcurementSystem.Instance.State))
        {
            var locked = !outfit.IsUnlocked(State, ProcurementSystem.Instance.State);
            var card = new Button { name = "wm-kalina-" + outfit.Id };
            card.AddToClassList("wm-kalina-outfit");
            ShopVisuals.Surface(card, ShopSurface.Card);
            card.EnableInClassList("wm-kalina-outfit-selected", outfit.Id == State.OutfitId);
            card.EnableInClassList("wm-locked", locked);
            card.SetEnabled(!locked);
            var art = Art(outfit, .5f);
            art.style.position = Position.Absolute;
            art.style.right = new StyleLength(3f);
            art.style.top = new StyleLength(outfit.ThumbnailTop);
            card.Add(art);
            var name = new Label(outfit.Name.Resolve());
            name.AddToClassList("wm-kalina-outfit-name");
            card.Add(name);
            var status = new Label(locked
                ? Locale.Format("WOMENACE::ui/kalina/outfit_level", "Affinity Lv{0}", outfit.Level)
                : outfit.Id == State.OutfitId ? Locale.Text("WOMENACE::ui/kalina/wearing", "Wearing") : "");
            status.AddToClassList("wm-kalina-outfit-status");
            card.Add(status);
            card.clickable.clicked += (Action)(() =>
            {
                Sound.Click();
                if (SelectOutfit(outfit.Id))
                    HideOutfits();
            });
            _outfitGrid.Add(card);
        }
        scroll.scrollOffset = offset;
    }

    public bool SelectOutfit(string id)
    {
        var outfit = Kalina.Outfits.FirstOrDefault(outfit => outfit.Id == id);
        if (!WorkshopAccess.IsUnlocked || outfit == null || !outfit.IsUnlocked(State, ProcurementSystem.Instance.State))
            return false;
        State.OutfitId = id;
        if (_portrait != null)
            RefreshPortrait();
        return true;
    }

    private void HideOutfits()
    {
        _outfits?.SetVisible(false);
        _holdEpoch++;
    }

}
