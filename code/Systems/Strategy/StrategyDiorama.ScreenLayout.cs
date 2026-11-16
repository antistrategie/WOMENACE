using Il2CppMenace.UI;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Vanilla's windows fitted into the space between the shell's bars, screen by screen.
public sealed partial class StrategyDiorama
{
    // Vanilla's windows are taller than the space between the bars. Until a screen is rebuilt for
    // that space, its windows are scaled to fit it from their top.
    private void LayOutScreen(string screenName, VisualElement root)
    {
        if (root == null)
            return;
        switch (screenName)
        {
            case VanillaUi.Screens.Armory:
                LayOutSquads(root);
                break;
            case VanillaUi.Screens.Hiring:
                LayOutRecruit(root);
                break;
            case VanillaUi.Screens.BlackMarket:
                LayOutMarket(root);
                break;
            case VanillaUi.Screens.Workshop:
                LayOutWorkshop(root);
                LayOutShop(root);
                break;
            case VanillaUi.Screens.OperationSelect:
                LayOutBriefing(UI.Find(root, UiSelector.Name(VanillaUi.Elements.OperationDescriptionPanel)));
                Fit(UI.Find(root, UiSelector.Name(VanillaUi.Elements.Planet)), BriefingScale, 50, 0);
                HideInline(UI.Find(root, UiSelector.Name(VanillaUi.Elements.ButtonContainer)));
                break;
            case VanillaUi.Screens.MissionPrep:
                // The title, the mission's name and the two buttons are the shell's, and the loadout
                // bar sits on the bottom bar between the actions.
                LayOutPrep(root);
                HideInline(UI.Find(root, UiSelector.Name(VanillaUi.Elements.ScreenHeader)));
                HideInline(UI.Find(root, UiSelector.Name(VanillaUi.Elements.StartMissionButton)));
                HideInline(UI.Find(root, UiSelector.Name(VanillaUi.Elements.AbortButton)));
                HideInline(UI.Find(root, UiSelector.Name(VanillaUi.Elements.MissionName)));
                MoveToBottomBar(root, UI.Find(root, UiSelector.Name(VanillaUi.Elements.SuppliesPanel)));
                break;
            case VanillaUi.Screens.ShipUpgrades:
                LayOutOci(root);
                break;
        }
    }

    // A vanilla element whose job the shell has taken: out of sight, still there to be pressed.
    private void HideInline(VisualElement element) =>
        Inline(element, e => e.style.visibility = Visibility.Hidden, e => e.style.visibility = StyleKeyword.Null);

    // Moves a vanilla panel out of its column to sit on the bottom bar, between the actions, at a
    // capped width. It goes back where it was when the shell comes off.
    private void MoveToBottomBar(VisualElement root, VisualElement panel)
    {
        if (panel == null || panel.parent == null)
            return;
        if (panel.parent == root)
        {
            // Over the shell, which the shell's own ordering puts last again on every apply.
            panel.BringToFront();
            return;
        }
        var parent = panel.parent;
        var index = parent.IndexOf(panel);
        var was = panel.resolvedStyle.position;
        root.Add(panel);
        panel.style.position = Position.Absolute;
        panel.style.left = Length.Percent(50);
        panel.style.marginLeft = -BottomPanelWidth / 2f;
        panel.style.width = BottomPanelWidth;
        panel.style.bottom = BottomPanelBottom;
        panel.style.top = StyleKeyword.Auto;
        _shellUndo.Add(() =>
        {
            panel.style.position = was;
            panel.style.left = StyleKeyword.Null;
            panel.style.marginLeft = StyleKeyword.Null;
            panel.style.width = StyleKeyword.Null;
            panel.style.bottom = StyleKeyword.Null;
            panel.style.top = StyleKeyword.Null;
            if (parent != null)
                parent.Insert(Math.Min(index, parent.childCount), panel);
        });
    }

    private const float BottomPanelWidth = 420f;
    private const float BottomPanelBottom = 7f;
    private const float BriefingScale = 0.87f;

    // The briefing's box: its left edge and width in the panel's units, and its inset. It is
    // wider than vanilla's column by BriefingWiden, taken to the left, and as tall as the space
    // between the bars less the same gap under it as over it.
    private const float BriefingWiden = 74f;
    private const float BriefingLeft = 783f - BriefingWiden * BriefingScale;
    private const float BriefingWidth = 314f + BriefingWiden;
    private const float BriefingPad = 12f;
    private const float BriefingInner = BriefingWidth - 2f * BriefingPad;
    private const float BriefingHeight = (720f - 2f * OpsColumnTop) / BriefingScale;
    private const float BriefingTagsTop = 46f;
    // Vanilla's planet picture and the space it stands in, and how much taller both are once the
    // picture is as wide as the box.
    private const float MoodImageWidth = 280f;
    private const float MoodImageHeight = 77.6f;
    private const float MoodHeight = 107.2f;
    private const float MoodGrow = MoodImageHeight * BriefingWiden / MoodImageWidth;
    // The planet strip's height, with room under its tags, and the gap under the picture for the
    // menace's caption.
    private const float BriefingHeaderHeight = 69.6f;
    private const float MoodGap = 18.4f;
    // The frame round the leader's message stands this far in from the window's sides.
    private const float IntroInset = 8f;
    // Vanilla's window height, which its leader, their name and the reward are placed against.
    private const float VanillaWindowHeight = 380f;

    // Vanilla's briefing is a planet strip standing out to the left, a framed planet picture and
    // a see-through window, with the faction's leader standing over its right side. Here the
    // three are one flat box in the side boxes' style, the leader still standing beside it.
    private void LayOutBriefing(VisualElement panel)
    {
        if (panel == null)
            return;
        Inline(panel, e =>
        {
            var s = e.style;
            s.scale = new Scale(new UnityEngine.Vector3(BriefingScale, BriefingScale, 1f));
            s.transformOrigin = new TransformOrigin(Length.Percent(0), Length.Percent(0), 0f);
            s.left = BriefingLeft;
            // Vanilla hangs the panel from the screen's bottom. Its top is level with the other
            // boxes' and it stands as far over the bottom bar as under the top one.
            s.top = OpsColumnTop;
            s.bottom = StyleKeyword.Auto;
            s.right = StyleKeyword.Auto;
            s.width = BriefingWidth;
            s.height = BriefingHeight;
            s.backgroundColor = OpsBoxFill;
            Border(s, 0.8f, OpsBoxLine);
            Pad(s, BriefingPad);
        }, e =>
        {
            var s = e.style;
            s.scale = StyleKeyword.Null;
            s.transformOrigin = StyleKeyword.Null;
            s.left = StyleKeyword.Null;
            s.top = StyleKeyword.Null;
            s.bottom = StyleKeyword.Null;
            s.right = StyleKeyword.Null;
            s.width = StyleKeyword.Null;
            s.height = StyleKeyword.Null;
            s.backgroundColor = StyleKeyword.Null;
            Border(s, null, null);
            Pad(s, null);
        });
        var header = UI.Find(panel, UiSelector.Name(VanillaUi.Elements.PlanetHeader));
        Place(header, 0f, null, BriefingInner, BriefingHeaderHeight);
        Rigid(header);
        // The shell's Back closes the briefing.
        if (header != null)
            HideInline(UI.Find(header, UiSelector.Name(VanillaUi.Elements.CloseButton)));
        // The terrain tags run under the planet's name across the box, sharing its width.
        var tags = header != null ? UI.Find(header, UiSelector.Name(VanillaUi.Elements.PlanetTags)) : null;
        Inline(tags, ShareTags, e =>
        {
            e.style.left = StyleKeyword.Null;
            e.style.right = StyleKeyword.Null;
            e.style.width = StyleKeyword.Null;
            e.style.top = StyleKeyword.Null;
            for (var i = 0; i < e.childCount; i++)
            {
                var tag = e.ElementAt(i).style;
                tag.flexGrow = StyleKeyword.Null;
                tag.flexShrink = StyleKeyword.Null;
                tag.flexBasis = StyleKeyword.Null;
                tag.width = StyleKeyword.Null;
                tag.minWidth = StyleKeyword.Null;
                tag.marginRight = StyleKeyword.Null;
            }
        });
        // Vanilla fills the tags in after the screen opens, so they are shared out again then.
        if (tags != null)
            Context.Coroutines.Start(ShareTagsLater(tags));
        // Vanilla's drawn frames and the window's own fill go: the box is the frame.
        var mood = UI.Find(panel, UiSelector.Name(VanillaUi.Elements.PlanetMoodHeader));
        var window = UI.Find(panel, UiSelector.Name(VanillaUi.Elements.OperationWindow));
        if (mood != null)
        {
            HideInline(UI.Find(mood, UiSelector.Name(VanillaUi.Elements.MoodLines)));
            HideInline(UI.Find(mood, UiSelector.Name(VanillaUi.Elements.WindowBorder)));
            WidenMood(mood);
        }
        if (window != null)
        {
            HideInline(UI.Find(window, UiSelector.Name(VanillaUi.Elements.WindowBackground)));
            HideInline(UI.Find(window, UiSelector.Name(VanillaUi.Elements.WindowBorder)));
            WidenWindow(window);
        }
    }

    // Vanilla lays the planet picture's parts out at fixed places for its own width. The picture
    // grows to the box's width at its own proportions, what hangs on its right edge moves out
    // with it, and what stands under it moves down.
    private void WidenMood(VisualElement mood)
    {
        Place(mood, null, null, BriefingInner, MoodHeight + MoodGrow);
        Rigid(mood);
        mood.style.marginBottom = MoodGap;
        for (var i = 0; i < mood.childCount; i++)
        {
            var part = mood.ElementAt(i);
            if (part.name == VanillaUi.Elements.PlanetMoodImage)
            {
                Place(part, null, null, MoodImageWidth + BriefingWiden, MoodImageHeight + MoodGrow);
                continue;
            }
            if (!MoodParts.TryGetValue(part.name ?? "", out var at))
                continue;
            Place(part, at.Left + BriefingWiden, at.Top + (at.Under ? MoodGrow : 0f), null, null);
        }
    }

    // Vanilla's places for the picture's right-hand parts: the faction's icon over its corner,
    // and the menace's boxes, share and caption (which has no name) under it.
    private static readonly Dictionary<string, (float Left, float Top, bool Under)> MoodParts = new()
    {
        [VanillaUi.Elements.PlanetFactionIcon] = (249.6f, -3.2f, false),
        [VanillaUi.Elements.MenacePresenceBoxes] = (153.6f, 108f, true),
        [VanillaUi.Elements.MenacePresenceValue] = (274.4f, 106.4f, true),
        [""] = (172.8f, 115.2f, true),
    };

    // The window takes what is left of the box under the picture. Its text runs the box's width,
    // and the leader, their name and the reward stand on its bottom edge as they do in vanilla,
    // the leader keeping their place beside the box.
    private void WidenWindow(VisualElement window)
    {
        Inline(window, e =>
        {
            // Exactly the height left over, whatever its text would ask for.
            e.style.height = StyleKeyword.Auto;
            e.style.minHeight = 0;
            e.style.flexBasis = 0;
            e.style.flexGrow = 1;
            e.style.flexShrink = 1;
        }, e =>
        {
            e.style.height = StyleKeyword.Null;
            e.style.minHeight = StyleKeyword.Null;
            e.style.flexBasis = StyleKeyword.Null;
            e.style.flexGrow = StyleKeyword.Null;
            e.style.flexShrink = StyleKeyword.Null;
        });
        // The text's column ends over the reward as the window does, and the frame round the
        // leader's message, which vanilla runs on behind the leader, ends inside the box.
        Inline(UI.Find(window, UiSelector.Name(VanillaUi.Elements.TextContainer)), e =>
        {
            e.style.height = StyleKeyword.Auto;
            e.style.minHeight = 0;
            e.style.flexBasis = 0;
            e.style.flexGrow = 1;
            e.style.flexShrink = 1;
        }, e =>
        {
            e.style.height = StyleKeyword.Null;
            e.style.minHeight = StyleKeyword.Null;
            e.style.flexBasis = StyleKeyword.Null;
            e.style.flexGrow = StyleKeyword.Null;
            e.style.flexShrink = StyleKeyword.Null;
        });
        Place(UI.Find(window, UiSelector.Name(VanillaUi.Elements.OperationIntroContainer)), null, null, BriefingInner - 2f * IntroInset, null);
        Place(UI.Find(window, UiSelector.Name(VanillaUi.Elements.OperationNameRow)), null, null, 257.6f + BriefingWiden, null);
        Place(UI.Find(window, UiSelector.Name(VanillaUi.Elements.GoalDescription)), null, null, 252.8f + BriefingWiden, null);
        Place(UI.Find(window, UiSelector.Name(VanillaUi.Elements.OperationIntro)), null, null, 252.8f + BriefingWiden, null);
        Stand(UI.Find(window, UiSelector.Name(VanillaUi.Elements.ClientRepImage)), 253.6f + BriefingWiden, VanillaWindowHeight - (-120f + 495.2f));
        Stand(UI.Find(window, UiSelector.Name(VanillaUi.Elements.ClientRepName)), 295.2f + BriefingWiden, VanillaWindowHeight - (350.4f + 9.6f));
        Stand(UI.Find(window, UiSelector.Name(VanillaUi.Elements.ClientRepTitle)), 295.2f + BriefingWiden, VanillaWindowHeight - (362.4f + 9.6f));
        // The reward's own margin holds it off the window's edge.
        Stand(UI.Find(window, UiSelector.Name(VanillaUi.Elements.TrustChanges)), null, 0f);
    }

    // Stands a vanilla element on its parent's bottom edge, this far up, and at this left edge.
    private void Stand(VisualElement element, float? left, float bottom)
    {
        Inline(element, e =>
        {
            if (left is { } l)
            {
                e.style.left = l;
                e.style.right = StyleKeyword.Auto;
            }
            e.style.top = StyleKeyword.Auto;
            e.style.bottom = bottom;
        }, e =>
        {
            e.style.left = StyleKeyword.Null;
            e.style.right = StyleKeyword.Null;
            e.style.top = StyleKeyword.Null;
            e.style.bottom = StyleKeyword.Null;
        });
    }

    // A part of the box that keeps its height whatever else is in the column.
    private static void Rigid(VisualElement element)
    {
        if (element != null)
            element.style.flexShrink = 0;
    }

    // Sets the given edges and sizes of a vanilla element inline, leaving the others as they are.
    private void Place(VisualElement element, float? left, float? top, float? width, float? height, bool fromTop = false)
    {
        Inline(element, e =>
        {
            var s = e.style;
            // A height that is to hold from the top, on an element vanilla also anchors below.
            if (fromTop)
                s.bottom = StyleKeyword.Auto;
            if (left is { } l)
            {
                s.left = l;
                s.right = StyleKeyword.Auto;
            }
            if (top is { } t)
            {
                s.top = t;
                s.bottom = StyleKeyword.Auto;
            }
            if (width is { } w)
                s.width = w;
            if (height is { } h)
                s.height = h;
        }, e =>
        {
            var s = e.style;
            s.left = StyleKeyword.Null;
            s.right = StyleKeyword.Null;
            s.top = StyleKeyword.Null;
            s.bottom = StyleKeyword.Null;
            s.width = StyleKeyword.Null;
            s.height = StyleKeyword.Null;
            s.flexShrink = StyleKeyword.Null;
            s.marginBottom = StyleKeyword.Null;
        });
    }

    private static void ShareTags(VisualElement tags)
    {
        tags.style.left = 0;
        tags.style.right = 0;
        tags.style.width = StyleKeyword.Auto;
        tags.style.top = BriefingTagsTop;
        for (var i = 0; i < tags.childCount; i++)
        {
            var tag = tags.ElementAt(i).style;
            tag.flexGrow = 1;
            tag.flexShrink = 1;
            tag.flexBasis = 0;
            tag.width = StyleKeyword.Auto;
            tag.minWidth = 0;
            tag.marginRight = i == tags.childCount - 1 ? 0 : StyleKeyword.Null;
        }
    }

    private System.Collections.IEnumerator ShareTagsLater(VisualElement tags)
    {
        var shell = _shell;
        for (var frame = 0; frame < 3; frame++)
        {
            yield return null;
            if (_shell != shell || tags.panel == null || _policyScreen != VanillaUi.Screens.OperationSelect)
                yield break;
            ShareTags(tags);
        }
    }

    private void Fit(VisualElement element, float scale, float originX, float originY)
    {
        Inline(element, e =>
        {
            e.style.scale = new Scale(new UnityEngine.Vector3(scale, scale, 1f));
            e.style.transformOrigin = new TransformOrigin(Length.Percent(originX), Length.Percent(originY), 0f);
        }, e =>
        {
            e.style.scale = StyleKeyword.Null;
            e.style.transformOrigin = StyleKeyword.Null;
        });
    }

    // Vanilla's 620-high windows, top at 91, into the 540 between the bars.
    private const float WindowScale = 0.86f;
}
