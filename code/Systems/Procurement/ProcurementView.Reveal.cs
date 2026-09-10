using Il2CppInterop.Runtime;
using Il2CppStem;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;
using static WOMENACE.Code.ShopVisuals;

namespace WOMENACE.Code;

internal sealed partial class ProcurementView
{
    private static readonly int SoundBank = SoundIds.FromName(Procurement.SoundBankId);
    private IReadOnlyList<ProcurementCatalogue.Entry> _shipment;
    private readonly List<RewardCard> _cards = [];
    private int _epoch, _nextCard;
    private bool _busy, _resultsReady;
    private Action _afterUnlock;
    private Label _continueHint;
    private SoundInstance _cue;
    private int _cueId;
    private bool _soundWarning;

    private sealed class RewardCard(VisualElement root, VisualElement front, VisualElement back)
    {
        public readonly VisualElement Root = root, Front = front, Back = back;
    }

    private void Later(Action action, long milliseconds)
    {
        var epoch = _epoch;
        _shop.schedule.Execute(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
        {
            if (epoch == _epoch)
                action();
        })).StartingIn(milliseconds);
    }

    private void BindShipmentSkip()
    {
        _transfer.RegisterCallback<PointerDownEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerDownEvent>>(
            (Action<PointerDownEvent>)(evt =>
            {
                if (evt.button != 0 || !_transfer.IsVisible())
                    return;
                evt.StopImmediatePropagation();
                ShowResults();
            })), TrickleDown.TrickleDown);
    }

    private void BindResultsContinue()
    {
        _results.RegisterCallback<PointerDownEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerDownEvent>>(
            (Action<PointerDownEvent>)(evt =>
            {
                if (evt.button == 0)
                    ContinueResults(evt);
            })));
        _results.RegisterCallback<KeyDownEvent>(DelegateSupport.ConvertDelegate<EventCallback<KeyDownEvent>>(
            (Action<KeyDownEvent>)(evt =>
            {
                if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter or KeyCode.Space)
                    ContinueResults(evt);
            })));
    }

    private void ContinueResults(EventBase evt)
    {
        if (!_resultsReady || !_results.IsVisible() || _reveal.IsVisible())
            return;
        // Unlock cards keep their own action after the reveal finishes.
        for (var target = evt.target?.TryCast<VisualElement>(); target != null; target = target.parent)
            if (target.TryCast<Button>() != null)
                return;
        evt.StopImmediatePropagation();
        Jiangyu.Game.Audio.Sound.Click();
        Finish();
    }

    public void Pull(int count)
    {
        if (_busy)
            return;
        if (ProcurementSystem.Pieces < count * Procurement.PiecesPerPull)
        {
            OpenExchange(count * Procurement.PiecesPerPull - ProcurementSystem.Pieces);
            return;
        }
        var result = System.Pull(count);
        if (!result.ok)
        {
            _message.text = result.error;
            return;
        }
        _shipment = result.rewards;
        _refreshShop();
        StartTransfer();
    }

    private void StartTransfer()
    {
        _epoch++;
        _busy = true;
        _message.text = "";
        CloseOverlay(_pool);
        CloseOverlay(_exchange);
        Refresh();
        _transfer.Clear();
        var rare = _shipment.Any(entry => entry.Reward.Section is ProcurementSection.Special or ProcurementSection.Dossiers or ProcurementSection.Curios);
        BuildTransferBackdrop(rare);
        Text(Locale.Text("WOMENACE::ui/procurement/logistics", "ELMO LOGISTICS / PROCUREMENT"), "wm-proc-transfer-caption", _transfer);
        var cargo = BuildCargo(rare);
        var status = Text(Locale.Text("WOMENACE::ui/procurement/connecting", "CONNECTING"), "wm-proc-transfer-status", _transfer);
        Text(Locale.Text("WOMENACE::ui/procurement/shipment", "Incoming shipment"), "wm-proc-transfer-title", _transfer);
        var progressTrack = Element("wm-proc-transfer-progress", _transfer);
        var progressFill = Element("wm-proc-transfer-progress-fill", progressTrack);
        Text("→ " + Locale.Text("WOMENACE::ui/procurement/skip", "Skip"), "wm-proc-skip-hint", _transfer);
        _transfer.SetVisible(true);
        _transfer.focusable = true;
        _transfer.Focus();
        PlaySound("shipment", true);
        Animate(3200, progress =>
        {
            _cargoProgress = progress;
            cargo.style.opacity = Mathf.Min(1f, progress * 6f);
            progressFill.style.width = new StyleLength(Length.Percent(progress * 100f));
            cargo.MarkDirtyRepaint();
        });
        Sparks(_transfer, 36);
        Later(() => status.text = Locale.Text("WOMENACE::ui/procurement/manifest", "MANIFEST RECEIVED"), 1000);
        Later(() =>
        {
            status.text = rare ? Locale.Text("WOMENACE::ui/procurement/special_cargo", "SPECIAL CARGO DETECTED")
                : Locale.Text("WOMENACE::ui/procurement/verified", "CONTENTS VERIFIED");
            PlaySound(rare ? "special" : "confirm");
        }, 2100);
        Later(ShowResults, 3300);
    }

    private void ShowResults()
    {
        if (_shipment == null)
            return;
        _epoch++;
        StopCue();
        _afterUnlock = null;
        _resultsReady = false;
        CloseOverlay(_transfer);
        CloseOverlay(_reveal);
        _results.Clear();
        _cards.Clear();
        _nextCard = 0;
        var dialog = Element("wm-proc-results", _results);
        ShopVisuals.Surface(dialog, ShopSurface.Dialog);
        var header = Element("wm-proc-row wm-proc-between wm-proc-results-heading", dialog);
        Text(Locale.Text("WOMENACE::ui/procurement/results", "Procurement complete"), "wm-proc-title", header);
        Text(_shipment.Count == 1 ? Locale.Text("WOMENACE::ui/procurement/single_pull", "1 PULL")
            : Locale.Format("WOMENACE::ui/procurement/pulls", "{0} PULLS", _shipment.Count), "wm-proc-muted", header);
        var body = Element("wm-proc-results-body", dialog);
        var grid = Element("wm-proc-results-grid", body);
        grid.EnableInClassList("wm-proc-single", _shipment.Count == 1);
        for (var i = 0; i < _shipment.Count; i++)
        {
            var entry = _shipment[i];
            var root = ResultCard(entry);
            root.name = "wm-procurement-result-" + i;
            var front = Element("wm-proc-reward-front", root);
            front.SetVisible(false);
            var colour = Colour(entry.Reward.Section);
            ShopVisuals.Surface(root, ShopSurface.Card, new Color(.5f, .58f, .42f));
            ShopVisuals.Surface(front, ShopSurface.Card, colour);
            Text(ProcurementCatalogue.SectionName(entry.Reward.Section).ToUpperInvariant(), "wm-proc-reward-category", front).style.color = colour;
            Icon(entry, front);
            Text(entry.Name, "wm-proc-reward-name", front);
            Text(entry.IsUnlock ? Locale.Text("WOMENACE::ui/procurement/new", "NEW")
                : Locale.Format("WOMENACE::ui/procurement/quantity", "×{0}", 1), "wm-proc-reward-quantity", front);
            var back = Text("ELMO", "wm-proc-reward-back", root);
            grid.Add(root);
            _cards.Add(new RewardCard(root, front, back));
        }
        var footer = Element("wm-proc-row wm-proc-results-footer", dialog);
        _continueHint = Text(Locale.Text("WOMENACE::ui/procurement/click_to_continue", "Click to continue"), "wm-proc-results-hint", footer);
        _continueHint.name = "wm-procurement-results-continue";
        _continueHint.SetVisible(false);
        _results.SetVisible(true);
        ShopVisuals.Enter(dialog, 4);
        _results.focusable = true;
        _results.Focus();
        Later(RevealNext, 150);
    }

    private void RevealNext()
    {
        if (_nextCard >= _shipment.Count)
        {
            foreach (var card in _cards)
                if (card.Root.TryCast<Button>() is { } button)
                    button.focusable = true;
            _resultsReady = true;
            _continueHint.SetVisible(true);
            _results.Focus();
            return;
        }
        var entry = _shipment[_nextCard];
        if (entry.IsUnlock)
        {
            // Keep this card and every following card concealed until its full-screen
            // presentation is dismissed. Each unlock pauses independently.
            _afterUnlock = TurnNext;
            ShowUnlock(entry);
            return;
        }
        TurnNext();
    }

    private VisualElement ResultCard(ProcurementCatalogue.Entry entry)
    {
        if (!entry.IsUnlock)
            return Element("wm-proc-button wm-proc-reward");
        var button = new Button { focusable = false };
        Classes(button, "wm-proc-button wm-proc-reward");
        button.clickable.clicked += (Action)(() =>
        {
            if (!_resultsReady)
                return;
            Jiangyu.Game.Audio.Sound.Click();
            ShowUnlock(entry);
        });
        return button;
    }

    private void TurnNext()
    {
        var index = _nextCard++;
        var card = _cards[index];
        var turned = false;
        Animate(180, progress =>
        {
            card.Root.style.scale = new StyleScale(new Scale(new Vector3(Mathf.Max(.035f, Mathf.Abs(progress * 2f - 1f)), 1f, 1f)));
            if (progress >= .5f && !turned)
            {
                turned = true;
                card.Back.SetVisible(false);
                card.Front.SetVisible(true);
                PaintCard(index);
                PlaySound(_shipment[index].Reward.Section is ProcurementSection.Special or ProcurementSection.Curios ? "special" : "flip");
            }
        });
        Later(RevealNext, 220);
    }

    private void PaintCard(int index)
    {
        var card = _cards[index].Root;
        var section = _shipment[index].Reward.Section;
        var colour = Colour(section);
        card.style.borderTopColor = card.style.borderRightColor = card.style.borderBottomColor = card.style.borderLeftColor = colour;
        card.style.backgroundColor = new Color(colour.r * .17f, colour.g * .17f, colour.b * .17f, 1f);
        ShopVisuals.Glint(_cards[index].Front);
    }

    public void ShowUnlock(ProcurementCatalogue.Entry entry)
    {
        var curio = entry.Outfit != null;
        _reveal.Clear();
        _reveal.EnableInClassList("wm-proc-reveal-curio", curio);
        BuildUnlockBackdrop();
        Text(curio ? "?" : "III", "wm-proc-reveal-roman", _reveal);
        var art = Portrait(entry, "wm-proc-reveal-art", _reveal);
        var text = Element("wm-proc-reveal-copy", _reveal);
        Text(curio ? Locale.Text("WOMENACE::ui/procurement/curio_discovered", "CURIO DISCOVERED")
            : Locale.Text("WOMENACE::ui/procurement/new_dossier", "NEW DOSSIER"), "wm-proc-new-dossier", text);
        Text(curio ? Locale.Text("WOMENACE::ui/procurement/kalina_outfit", "KALINA'S OUTFIT")
            : Locale.Text("WOMENACE::ui/procurement/third_generation", "THIRD GENERATION"), "wm-proc-reveal-generation", text);
        Text((curio ? entry.Name : entry.DollName).ToUpperInvariant(), "wm-proc-reveal-name", text);
        if (!curio)
        {
            Text("◆ ◆ ◆", "wm-proc-reveal-stars", text);
            Text(Locale.Text("WOMENACE::ui/procurement/recruitment_available", "Recruitment available."), "wm-proc-reveal-description", text);
        }
        var proceed = Button(Continue(), DismissUnlock, "wm-shop-primary wm-proc-reveal-continue");
        proceed.name = curio ? "wm-procurement-curio-continue" : "wm-procurement-dossier-continue";
        text.Add(proceed);
        Text(curio ? Locale.Text("WOMENACE::ui/procurement/outfit_unlocked", "OUTFIT UNLOCKED")
            : Locale.Text("WOMENACE::ui/procurement/dossier_acquired", "DOSSIER ACQUIRED"), "wm-proc-reveal-caption", _reveal);
        _reveal.SetVisible(true);
        proceed.Focus();
        PlaySound("dossier", true);
        Animate(900, progress =>
        {
            art.style.left = new StyleLength((curio ? -110f : 52f) - 42f * Mathf.Pow(1f - progress, 3f));
            art.style.opacity = Mathf.Min(1f, progress * 2f);
            text.style.opacity = Mathf.Clamp01((progress - .15f) * 2f);
        });
        Sparks(_reveal, 42);
    }

    private void DismissUnlock()
    {
        StopCue();
        CloseOverlay(_reveal);
        var resume = _afterUnlock;
        _afterUnlock = null;
        if (resume != null)
            resume();
        else
            _results.Focus();
    }

    private void Finish()
    {
        StopSequence();
        Refresh();
        _ten.Focus();
    }

    private void StopSequence()
    {
        _epoch++;
        StopCue();
        _busy = false;
        _resultsReady = false;
        _afterUnlock = null;
        _shipment = null;
        _cards.Clear();
        _continueHint = null;
        CloseOverlay(_transfer);
        CloseOverlay(_results);
        CloseOverlay(_reveal);
    }

    private void Animate(float milliseconds, Action<float> frame)
    {
        var epoch = _epoch;
        var start = Time.unscaledTime;
        IVisualElementScheduledItem timer = null;
        timer = _shop.schedule.Execute(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
        {
            if (epoch != _epoch)
            {
                timer.Pause();
                return;
            }
            var progress = Mathf.Clamp01((Time.unscaledTime - start) * 1000f / milliseconds);
            frame(progress);
            if (progress >= 1f)
                timer.Pause();
        })).Every(16);
    }

    private void PlaySound(string name, bool cue = false)
    {
        try
        {
            if (cue)
                StopCue();
            var id = SoundIds.FromName(name);
            var sound = new ID(SoundBank, id).Play(1f, 1f);
            if (sound == null)
                throw new InvalidOperationException("Procurement sound bank is unavailable");
            if (cue)
            {
                _cue = sound;
                _cueId = id;
            }
        }
        catch (Exception ex)
        {
            if (!_soundWarning)
                _context.Log.Warn($"Procurement audio: {ex.Message}");
            _soundWarning = true;
        }
    }

    private void StopCue()
    {
        // Stem reuses finished instances. Only stop an instance still carrying our cue.
        if (_cue?.Sound?.id == _cueId && _cue.Playing)
            _cue.Stop();
        _cue = null;
    }

}
