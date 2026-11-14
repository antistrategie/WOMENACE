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
    private readonly System.Random _dialogueRandom = new();
    private Label _dialogue;
    private KalinaDialogue.Line _currentDialogue;
    private bool _greetingPending;

    private void BindKalina()
    {
        _dialogue = UI.Find(_root, UiSelector.Name("wm-kalina-dialogue"))?.TryCast<Label>();
        _dialogue?.SetVisible(false);
        ShopVisuals.Brackets(UI.Find(_root, UiSelector.Class("wm-kalina-outfit-panel")));
        ConstrainVerticalScroll(UI.Find(_root, UiSelector.Name("wm-kalina-outfit-scroll"))?.TryCast<ScrollView>());
        var open = UI.Find(_root, UiSelector.Name("wm-kalina-outfits-open"))?.TryCast<Button>();
        if (open != null)
            open.clickable.clicked += (Action)(() => { Sound.Click(); ShowOutfits(); });
        // She is a picture: her wardrobe opens from its button.
        UiLayout.IgnorePicking(_portrait);
        var close = UI.Find(_root, UiSelector.Name("wm-kalina-outfits-close"))?.TryCast<Button>();
        if (close != null)
            close.clickable.clicked += (Action)(() => { Sound.Click(); HideOutfits(); });
        UI.Find(_root, UiSelector.Name("wm-kalina-outfits-blocker"))?.RegisterCallback<PointerDownEvent>(
            DelegateSupport.ConvertDelegate<EventCallback<PointerDownEvent>>((Action<PointerDownEvent>)(_ => HideOutfits())));
        _outfits.SetVisible(false);
    }

    // Kalina's affinity as Squads shows a doll's: the level in large figures, how far along it
    // is to the next, and a bar of the same. What each level unlocks is in her outfits.
    private void RefreshLevel()
    {
        var level = State.Level;
        var top = level >= Kalina.MaxLevel;
        var share = top ? 1f : State.Affinity % Kalina.AffinityPerLevel / (float)Kalina.AffinityPerLevel;
        AffinitySystem.ShowLevel(_root, "wm-kalina-level", level, top, share);
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
        _portrait.Add(Art(outfit, 1f));
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
        // The whole picture is drawn because the authored crop coordinates include its margins.
        var image = new VisualElement { pickingMode = PickingMode.Ignore };
        var imageScale = scale * outfit.Width / (outfit.CropRight - outfit.CropLeft);
        image.style.position = Position.Absolute;
        image.style.left = new StyleLength(-outfit.CropLeft * imageScale);
        image.style.top = new StyleLength(-outfit.CropTop * imageScale);
        image.style.width = new StyleLength(outfit.SourceWidth * imageScale);
        image.style.height = new StyleLength(outfit.SourceHeight * imageScale);
        _artwork.Draw(image, outfit.Asset);
        image.AddToClassList("wm-kalina-art");
        crop.Add(image);
        frame.Add(crop);
        return frame;
    }

    public void ShowOutfits()
    {
        if (!_host.IsOpen)
            return;
        _holdEpoch++;
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
            ShopVisuals.Card(card);
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
