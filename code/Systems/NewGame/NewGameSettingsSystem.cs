using System.Collections;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Menu;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Renders the WOMENACE section in the new-game box (a subheading plus a toggle per
// NewGameSettings.Registry entry) and commits the player's choices to the new campaign's
// NewGameOptions when a campaign is created. The section is injected into the new-game window's
// SettingsContainer on the title screen, and the injection re-applies automatically whenever that
// screen relays out (the box opening counts), skipping the site while the section is already there.
public sealed class NewGameSettingsSystem : JiangyuSystem
{
    private const string SectionName = "wm-newgame-section";
    private const string ScrollName = "wm-settings-scroll";

    // Cap for the scrollable settings area. Turning on "Customize Difficulty" adds a long stack of
    // rows that, with the WOMENACE section below them, runs the box off the bottom of the 720-tall
    // UI panel. Capping the settings list keeps the header and START GAME button fixed, and the
    // scrollbar only appears once the rows exceed this.
    private const float ScrollMaxHeight = 600f;

    // A snapshot of the box choices, taken at CreateNewGame and written to the new campaign's state
    // one frame later. Snapshotting binds the commit to THIS new game: a later scene load (e.g.
    // loading an unrelated campaign) can never route the current box value into the wrong save.
    private NewGameOptions _pendingCommit;

    private UiInjection _injection;

    public override void OnInit()
    {
        _injection = UI.Inject(
            UiTarget.Screen<TitleUIScreen>().AppendTo(UiSelector.Name("SettingsContainer")),
            BuildSection);

        // The SDK re-applies injections on screen activation and on the screen root's geometry
        // change. Neither fires when the new-game box opens: it is a sub-panel of the already-active
        // title screen and the root's own rect never changes. So re-apply explicitly when the box
        // opens, once its SettingsContainer is in the tree (a frame later, once it has settled).
        Context.Patches.Postfix("Il2CppMenace.UI.Menu.TitleUIScreen", "OpenWindow", OnTitleWindowOpened);

        Context.Patches.Postfix("Il2CppMenace.States.StrategyState", "CreateNewGame", OnCreateNewGame);
    }

    // A new game resets all mod state (the loader's ResetAll runs on CreateNewGame), so the box
    // choice is snapshotted here and written to the new campaign's state one frame later
    // (unambiguously after every CreateNewGame postfix, including the reset). The next-frame
    // coroutine is the commit path: a scene load is deliberately not used, since it fires for a
    // later load of a different campaign too and would write this snapshot to the wrong save.
    private void OnCreateNewGame(PatchInfo info)
    {
        var snapshot = new NewGameOptions();
        snapshot.CopyFrom(NewGameSettings.Pending);
        _pendingCommit = snapshot;
        Context.Log.Info($"new game: scheduling options commit (disableVanillaLeaders={snapshot.DisableVanillaLeaders})");
        try { Context.Coroutines.Start(CommitNextFrame()); }
        catch (Exception ex) { Context.Log.Warn($"new game options: commit schedule failed: {ex.Message}"); }
    }

    private IEnumerator CommitNextFrame()
    {
        yield return null;
        var pending = _pendingCommit;
        _pendingCommit = null;
        if (pending == null)
            yield break;
        try
        {
            Context.State.Get<NewGameOptions>().CopyFrom(pending);
            Context.Log.Info($"new game options committed: disableVanillaLeaders={pending.DisableVanillaLeaders}");
        }
        catch (Exception ex) { Context.Log.Warn($"new game options: commit failed: {ex.Message}"); }
    }

    private void OnTitleWindowOpened(PatchInfo info)
    {
        try
        {
            var window = (info.Args.Count > 0 ? info.Args[0] as Il2CppObjectBase : null)?.TryCast<NewGameWindow>();
            if (window == null)
                return;
            EnsureScrollable(window);
            _injection?.Refresh();
            Context.Coroutines.Start(RefreshNextFrame());
        }
        catch (Exception ex) { Context.Log.Warn($"new game options: window-open refresh failed: {ex.Message}"); }
    }

    private IEnumerator RefreshNextFrame()
    {
        yield return null;
        try
        {
            var window = UI.Find(GameRoot(), UiSelector.TypeName("NewGameWindow"));
            if (window != null)
                EnsureScrollable(window);
            _injection?.Refresh();
        }
        catch (Exception ex) { Context.Log.Warn($"new game options: deferred refresh failed: {ex.Message}"); }
    }

    private static VisualElement GameRoot()
    {
        try { return UIManager.Get()?.GetActiveScreen()?.GetRootElement(); }
        catch { return null; }
    }

    // Wrap the difficulty/settings list in a height-capped ScrollView so the box stays on screen
    // when Customize Difficulty expands it. The header and START GAME button stay outside the scroll
    // (they are siblings of SettingsContainer), so only the settings list scrolls. The game keeps
    // its reference to SettingsContainer, so re-parenting it does not disturb how difficulty rows
    // are added.
    private void EnsureScrollable(VisualElement window)
    {
        try
        {
            var settings = UI.Find(window, UiSelector.Name("SettingsContainer"));
            var parent = settings?.parent;
            if (settings == null || parent == null)
                return;

            // Skip if the settings list is already inside a ScrollView: our own wrapper on a repeat
            // call (so this is idempotent), or a native one should the game add scrolling here in a
            // future update, in which case ours would be redundant and would fight theirs.
            if (IsInsideScrollView(settings))
                return;

            var index = parent.IndexOf(settings);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = ScrollName };
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.maxHeight = new StyleLength(ScrollMaxHeight);
            scroll.style.flexShrink = 0f;

            parent.Insert(index, scroll);
            settings.RemoveFromHierarchy();
            scroll.Add(settings);
        }
        catch (Exception ex) { Context.Log.Warn($"new game options: scroll wrap failed: {ex.Message}"); }
    }

    // Whether any ancestor of the element is a ScrollView (ours from a prior wrap, or a native one).
    private static bool IsInsideScrollView(VisualElement element)
    {
        for (var e = element.parent; e != null; e = e.parent)
            if (e.TryCast<ScrollView>() != null)
                return true;
        return false;
    }

    // The WOMENACE section: a thin separator off the difficulty rows above, a heading, then one
    // native LabeledToggle per registered setting, each bound to the pending choice.
    private VisualElement BuildSection()
    {
        var section = new VisualElement { name = SectionName };
        section.style.marginTop = new StyleLength(10f);

        var separator = new VisualElement { name = "wm-newgame-separator", pickingMode = PickingMode.Ignore };
        separator.style.height = new StyleLength(1f);
        separator.style.marginBottom = new StyleLength(8f);
        separator.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.14f));
        section.Add(separator);

        var heading = new Label("WOMENACE") { name = "wm-newgame-heading", pickingMode = PickingMode.Ignore };
        heading.AddToClassList("field-label");
        heading.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
        heading.style.fontSize = new StyleLength(13f);
        heading.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f, 1f));
        heading.style.marginBottom = new StyleLength(6f);
        section.Add(heading);

        foreach (var setting in NewGameSettings.Registry)
            section.Add(BuildToggle(setting));

        return section;
    }

    private VisualElement BuildToggle(NewGameSettings.Setting setting)
    {
        var toggle = new LabeledToggle(
            setting.Label.Resolve(),
            setting.Get(NewGameSettings.Pending),
            true);

        var set = setting.Set;
        toggle.SetOnValueChangedAction(
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(
                (Action<bool>)(value => set(NewGameSettings.Pending, value))));

        return toggle.TryCast<VisualElement>();
    }
}
