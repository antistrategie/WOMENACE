using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The Recruit screen: vanilla's hiring screen in the Squads frame. Its list of who can be hired
// is the grid at the left, its leader info and portrait are the panel at the right, and the
// candidate stands in the refitting room as a hologram of her own model, where the squad stands
// on Squads. Vanilla's list of who is already hired is not shown: that is the Roster tab.
public sealed partial class StrategyDiorama
{
    private const string RecruitClass = "wm-recruit";
    private const string RecruitEmptyClass = "wm-recruit--empty";
    private const string RecruitEmptyName = "wm-recruit-empty";
    // The two forms' tabs the form swap puts over the stats.
    private const string FormTabsName = "wm-formtabs";
    private const string HologramAsset = "effects/doll_hologram/main";

    // The panel's portrait column, and the strip its header takes across both columns.
    // The one measure the panel is spaced by.
    private const float RecruitPad = 8f;
    private const float RecruitPortraitWidth = 222f;
    private const float RecruitHeaderHeight = 40f;
    private const float RecruitBadgeWidth = 96f;
    private const float RecruitPortraitHeight = 268f;
    // Her name fills the header's height with the small line under it.
    private const float RecruitNameSize = 20f;
    private const float RecruitMetaHeight = 12f;
    private const float RecruitDescriptionSize = 8.8f;
    private const float RecruitDescriptionHeight = 120f;
    // The chosen tile's gold edge, as wide as the roster's.
    private const float CandidateSelectedEdge = 1.9f;
    // The panel's hairline, which its contents start inside.
    private const float RecruitPanelEdge = 0.64f;
    // The framing shot stands this near a lone candidate.
    private const float RecruitDistance = 4.4f;
    // Where she stands across the screen, in the panel's units from its left edge: between the
    // middle of the space the tiles' column and the panel leave, and the middle of all that is
    // left of the panel. The tiles take a corner of their column and no more.
    private const float RecruitScreenX = 427f;
    // One doll is looked at from her own eye level, not from over a squad's heads: the camera
    // aims at her chest from a little above it, and turns down to level and no further.
    private const float RecruitPitch = 5f;
    private const float RecruitPitchMin = 0f;
    private const float RecruitAimHeight = 0.95f;
    // A candidate stands alone: the whole rig, and a nearer shot than a squad's.
    private static readonly StageShot CandidateShot = new(1f, RecruitDistance, ArmoryPan, RecruitPitch, RecruitPitchMin, RecruitAimHeight, RecruitScreenX);
    // How long a candidate takes to be drawn in from the floor up.
    private const float CandidateDrawSeconds = 0.7f;
    // A frame the game spent loading her model counts as this long, so the drawing in is seen.
    private const float CandidateLongestStep = 1f / 20f;

    // Her lines go by the screen's rows, this many to its height: she is seen from where
    // lines fixed to the room would be finer than a pixel, and flicker.
    private const float HologramRows = 340f;
    private const float HologramLineGap = 0.28f;
    private const float HologramRim = 0.45f;
    private const float HologramOwnColour = 0.4f;
    private static readonly int HologramRevealId = Shader.PropertyToID("_Reveal");
    private static readonly int HologramFloorId = Shader.PropertyToID("_FloorY");
    private static readonly int HologramHeightId = Shader.PropertyToID("_Height");
    private static readonly int HologramOpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    // Parts of a doll that are tricks of her own shading and no part of her shape.
    private static readonly string[] HologramSkips = { "Outline", "EyeShadow", "EyeHighlight" };


    private static readonly string[] TileKindMarks = { VanillaUi.Elements.InfantryIcon, VanillaUi.Elements.VehicleIcon };
    private static readonly string[] TileCovers = { VanillaUi.Elements.SlotSelected, VanillaUi.Elements.TooExpensiveOverlay };
    private static readonly string[] TileHiddenParts = { VanillaUi.Elements.ArmorBar, VanillaUi.Elements.HealthBar, VanillaUi.Elements.QualityLevel };

    private void HookRecruit()
    {
        foreach (var target in new[] { VanillaUi.Targets.HiringUpdateWindow, VanillaUi.Targets.HiringSlotClicked })
            Context.Patches.Postfix(target.Type, target.Method, _ => RefreshRecruit());
    }

    private void LayOutRecruit(VisualElement root)
    {
        var window = UI.Find(root, UiSelector.Name(VanillaUi.Elements.HiringWindow));
        if (window == null)
            return;
        if (!VanillaUi.Require(Context, VanillaUi.Screens.Hiring, window, VanillaUi.Elements.HiredColumn, VanillaUi.Elements.HirableColumn,
            VanillaUi.Elements.UnitInfoColumn, VanillaUi.Elements.UnitLeaderColumn, VanillaUi.Elements.HireButton))
        {
            Fit(window, WindowScale, 50, 0);
            return;
        }
        if (!root.ClassListContains(RecruitClass))
        {
            root.AddToClassList(RecruitClass);
            _shellUndo.Add(() =>
            {
                root.RemoveFromClassList(RecruitClass);
                window.RemoveFromClassList(RecruitEmptyClass);
            });
        }
        // The window is the panel: at the squad panel's place, its two columns side by side with
        // the portrait at the left. On the elements, since vanilla's own rules outrank shell.uss.
        Inline(window, e =>
        {
            var s = e.style;
            s.position = Position.Absolute;
            s.left = SquadPanelLeft;
            s.top = SquadsTop;
            s.right = StyleKeyword.Auto;
            s.bottom = StyleKeyword.Auto;
            s.width = SquadPanelWidth;
            s.height = SquadsBottom - SquadsTop;
            s.marginLeft = 0;
            s.marginTop = 0;
            s.marginRight = 0;
            s.marginBottom = 0;
            Pad(s, RecruitPad);
            s.flexDirection = FlexDirection.RowReverse;
        }, e =>
        {
            var s = e.style;
            s.position = StyleKeyword.Null;
            s.left = StyleKeyword.Null;
            s.top = StyleKeyword.Null;
            s.right = StyleKeyword.Null;
            s.bottom = StyleKeyword.Null;
            s.width = StyleKeyword.Null;
            s.height = StyleKeyword.Null;
            s.marginLeft = StyleKeyword.Null;
            s.marginTop = StyleKeyword.Null;
            s.marginRight = StyleKeyword.Null;
            s.marginBottom = StyleKeyword.Null;
            Pad(s, null);
            s.flexDirection = StyleKeyword.Null;
        });
        // The grid of who can be hired stands where the roster does, outside the panel.
        var grid = UI.Find(window, UiSelector.Name(VanillaUi.Elements.HirableColumn));
        Inline(grid, e =>
        {
            var s = e.style;
            s.position = Position.Absolute;
            s.left = RosterLeft - SquadPanelLeft;
            s.top = -RecruitPanelEdge;
            s.width = RosterWidth;
            s.height = SquadsBottom - SquadsTop;
        }, e =>
        {
            var s = e.style;
            s.position = StyleKeyword.Null;
            s.left = StyleKeyword.Null;
            s.top = StyleKeyword.Null;
            s.width = StyleKeyword.Null;
            s.height = StyleKeyword.Null;
        });
        // One measure spaces the panel: its edge to its contents, the header to the columns, the
        // portrait's column to the info's, and each part of a column to the next. Vanilla's own
        // margins and paddings, each a little different, come off.
        var portraitColumn = UI.Find(window, UiSelector.Name(VanillaUi.Elements.UnitLeaderColumn));
        Spaced(portraitColumn, 0f, 0f, RecruitPad, 0f, RecruitHeaderHeight + RecruitPad);
        Inline(portraitColumn, e =>
        {
            e.style.width = RecruitPortraitWidth;
            e.style.flexShrink = 0;
        }, e =>
        {
            e.style.width = StyleKeyword.Null;
            e.style.flexShrink = StyleKeyword.Null;
        });
        var info = UI.Find(window, UiSelector.Name(VanillaUi.Elements.UnitInfoColumn));
        Spaced(info, 0f, 0f, 0f, 0f, RecruitHeaderHeight + RecruitPad);
        Inline(info, e =>
        {
            e.style.width = StyleKeyword.Auto;
            e.style.flexGrow = 1;
            e.style.flexBasis = 0;
        }, e =>
        {
            e.style.width = StyleKeyword.Null;
            e.style.flexGrow = StyleKeyword.Null;
            e.style.flexBasis = StyleKeyword.Null;
        });
        // Vanilla's holder of the info is a fixed width, wider than the column is here.
        var holder = info != null ? UI.Find(info, UiSelector.Name(VanillaUi.Elements.Container)) : null;
        Spaced(holder?.parent != info ? holder?.parent : null, 0f, 0f, 0f, 0f, 0f);
        Spaced(holder, 0f, 0f, 0f, 0f, 0f);
        Inline(holder, e => e.style.width = Length.Percent(100), e => e.style.width = StyleKeyword.Null);
        // Her portrait's box is the column's width with the panel's hairline round it, and
        // shorter than vanilla's, which leaves the perks under it their four rows.
        var portrait = portraitColumn != null ? UI.Find(portraitColumn, UiSelector.Name(VanillaUi.Elements.LeaderBackground)) : null;
        Spaced(portrait, 0f, 0f, 0f, 0f, null);
        Place(portrait, null, null, RecruitPortraitWidth, RecruitPortraitHeight);
        Inline(portrait, e => Border(e.style, RecruitPanelEdge, SquadPanelLine), e => Border(e.style, null, null));
        Spaced(portraitColumn != null ? UI.Find(portraitColumn, UiSelector.Name(VanillaUi.Elements.PerkTree)) : null, 0f, RecruitPad, 0f, 0f, 0f);
        var description = holder != null ? UI.Find(holder, UiSelector.Name(VanillaUi.Elements.Description)) : null;
        Spaced(description, 0f, 0f, 0f, RecruitPad, 0f);
        Inline(description, e =>
        {
            e.style.fontSize = RecruitDescriptionSize;
            e.style.height = StyleKeyword.Auto;
            e.style.maxHeight = RecruitDescriptionHeight;
        }, e =>
        {
            e.style.fontSize = StyleKeyword.Null;
            e.style.height = StyleKeyword.Null;
            e.style.maxHeight = StyleKeyword.Null;
        });
        if (holder != null)
        {
            Spaced(UI.Find(holder, UiSelector.TypeName(VanillaUi.Elements.StatsPanel)), 0f, 0f, 0f, 0f, 0f);
            Spaced(UI.Find(holder, UiSelector.Name(VanillaUi.Elements.StatsTabs)), 0f, 0f, 0f, RecruitPad, 0f);
            Spaced(UI.Find(holder, UiSelector.Name(VanillaUi.Elements.InitialPerk)), 0f, RecruitPad, 0f, 0f, 0f);
        }
        // The header runs across the top of both columns, as the squad panel's does: the unit's
        // badge and her name over her rank and unit.
        // It is placed from the info's holder, whose right edge is the panel's own.
        var header = holder != null ? UI.Find(holder, UiSelector.Name(VanillaUi.Elements.UnitWindowHeader)) : null;
        Spaced(header, 0f, 0f, 0f, 0f, 0f);
        Inline(header, e =>
        {
            var s = e.style;
            s.position = Position.Absolute;
            s.left = -(RecruitPortraitWidth + RecruitPad);
            s.right = 0;
            s.top = -(RecruitHeaderHeight + RecruitPad);
            s.width = StyleKeyword.Auto;
            s.height = RecruitHeaderHeight;
            s.flexDirection = FlexDirection.Row;
            s.alignItems = Align.Center;
            s.borderBottomWidth = 0;
        }, e =>
        {
            var s = e.style;
            s.borderBottomWidth = StyleKeyword.Null;
            s.position = StyleKeyword.Null;
            s.left = StyleKeyword.Null;
            s.right = StyleKeyword.Null;
            s.top = StyleKeyword.Null;
            s.width = StyleKeyword.Null;
            s.height = StyleKeyword.Null;
            s.flexDirection = StyleKeyword.Null;
            s.alignItems = StyleKeyword.Null;
        });
        if (header != null)
        {
            HeaderText(UI.Find(header, UiSelector.Name(VanillaUi.Elements.LeaderName)), RecruitNameSize, UnityEngine.Color.white);
            HeaderText(UI.Find(header, UiSelector.Name(VanillaUi.Elements.LeaderRank)), SquadMetaSize, SquadMetaInk);
            HeaderText(UI.Find(header, UiSelector.Name(VanillaUi.Elements.UnitName)), SquadMetaSize, SquadMetaInk);
            var badge = UI.Find(header, UiSelector.Name(VanillaUi.Elements.UnitBadge));
            Spaced(badge, 0f, 0f, RecruitPad, 0f, null);
            Place(badge, null, null, RecruitBadgeWidth, RecruitHeaderHeight);
            var names = UI.Find(header, UiSelector.Name(VanillaUi.Elements.Row));
            Spaced(names, 0f, 0f, 0f, 0f, 0f);
            Inline(names, e => e.style.flexGrow = 1, e => e.style.flexGrow = StyleKeyword.Null);
            LayOutRecruitNames(header);
            // What she costs is shown in the Hire button. Vanilla's own chip stays in its column,
            // out of sight: vanilla looks for it there each time the screen opens, which can be
            // after this has run.
            HideInline(portraitColumn != null ? UI.Find(portraitColumn, UiSelector.Name(VanillaUi.Elements.HiringCosts)) : null);
            // Vanilla's own Hire button, with what it checks and asks, stands at the panel's
            // bottom right: where reading her down the panel ends.
            var hire = UI.Find(window, UiSelector.Name(VanillaUi.Elements.HireButton));
            MoveInto(window, hire);
            Spaced(hire, 0f, 0f, 0f, 0f, null);
            Inline(hire, e =>
            {
                var s = e.style;
                s.position = Position.Absolute;
                s.left = StyleKeyword.Auto;
                s.top = StyleKeyword.Auto;
                s.right = RecruitPad;
                s.bottom = RecruitPad;
                s.width = StyleKeyword.Auto;
                s.flexShrink = 0;
            }, e =>
            {
                var s = e.style;
                s.position = StyleKeyword.Null;
                s.left = StyleKeyword.Null;
                s.top = StyleKeyword.Null;
                s.right = StyleKeyword.Null;
                s.bottom = StyleKeyword.Null;
                s.width = StyleKeyword.Null;
                s.flexShrink = StyleKeyword.Null;
            });
            var face = hire != null ? UI.Find(hire, UiSelector.Name(VanillaUi.Elements.Pickable)) : null;
            if (face != null && UI.Find(face, UiSelector.Name(HireCostName)) == null)
            {
                var cost = new VisualElement { name = HireCostName, pickingMode = PickingMode.Ignore };
                cost.AddToClassList("wm-recruit__cost");
                var icon = new VisualElement { name = HireCostName + "-icon", pickingMode = PickingMode.Ignore };
                icon.AddToClassList("wm-recruit__cost-icon");
                var value = new Label { name = HireCostName + "-value", pickingMode = PickingMode.Ignore };
                value.AddToClassList("wm-recruit__cost-value");
                cost.Add(icon);
                cost.Add(value);
                face.Add(cost);
                Inline(face, e =>
                {
                    e.style.flexDirection = FlexDirection.Row;
                    e.style.alignItems = Align.Center;
                    e.style.justifyContent = Justify.Center;
                }, e =>
                {
                    e.style.flexDirection = StyleKeyword.Null;
                    e.style.alignItems = StyleKeyword.Null;
                    e.style.justifyContent = StyleKeyword.Null;
                });
                _shellUndo.Add(cost.RemoveFromHierarchy);
            }
        }
        // The list starts at the frame's top and takes the column's width.
        Inline(UI.Find(window, UiSelector.Name(VanillaUi.Elements.HirableScrollView)), e =>
        {
            var s = e.style;
            s.marginLeft = 0;
            s.marginTop = 0;
            s.marginRight = 0;
            s.marginBottom = 0;
            s.flexGrow = 1;
        }, e =>
        {
            var s = e.style;
            s.marginLeft = StyleKeyword.Null;
            s.marginTop = StyleKeyword.Null;
            s.marginRight = StyleKeyword.Null;
            s.marginBottom = StyleKeyword.Null;
            s.flexGrow = StyleKeyword.Null;
        });
        // Said across the middle of the screen, which is empty when there is nobody to hire.
        if (UI.Find(root, UiSelector.Name(RecruitEmptyName)) == null)
        {
            var empty = new Label(Locale.Text("WOMENACE::ui/recruit/empty", "NO CANDIDATES")) { name = RecruitEmptyName, pickingMode = PickingMode.Ignore };
            empty.AddToClassList("wm-recruit__empty");
            // Across the whole screen with its words in the middle. On the element, since the
            // screen's own rules for a label outrank shell.uss.
            empty.style.position = Position.Absolute;
            empty.style.left = 0;
            empty.style.right = 0;
            empty.style.top = 0;
            empty.style.bottom = 0;
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            root.Insert(0, empty);
            _shellUndo.Add(empty.RemoveFromHierarchy);
        }
        RefreshRecruit();
    }

    private const string HireCostName = "wm-recruit-cost";
    private const string RecruitMetaName = "wm-recruit-meta";

    // The header reads as the squad panel's does: her name, and under it one small line of her
    // rank's insignia, her rank and her unit. Vanilla stacks unit, name and rank with the
    // insignia beside them.
    private void LayOutRecruitNames(VisualElement header)
    {
        var name = UI.Find(header, UiSelector.Name(VanillaUi.Elements.LeaderName));
        var rank = UI.Find(header, UiSelector.Name(VanillaUi.Elements.LeaderRank));
        var unit = UI.Find(header, UiSelector.Name(VanillaUi.Elements.UnitName));
        var insignia = UI.Find(header, UiSelector.Name(VanillaUi.Elements.RankIcon));
        var column = name?.parent;
        if (column == null || rank == null || unit == null || UI.Find(header, UiSelector.Name(RecruitMetaName)) != null)
            return;
        var meta = new VisualElement { name = RecruitMetaName };
        meta.style.flexDirection = FlexDirection.Row;
        meta.style.alignItems = Align.Center;
        // The insignia is taller than the line's words. The line keeps the words' height, so it
        // sits close under the name.
        meta.style.height = RecruitMetaHeight;
        meta.style.flexShrink = 0;
        // Taken out before the parts go home and again after, whichever way the undoing runs.
        _shellUndo.Add(meta.RemoveFromHierarchy);
        column.Add(meta);
        if (insignia != null)
        {
            MoveInto(meta, insignia);
            Inline(insignia, e =>
            {
                e.style.width = RankInsigniaSize;
                e.style.height = RankInsigniaSize;
                e.style.marginRight = 3.2f;
                // The picture has blank space round it, so it is pulled left to start under the name.
                e.style.marginLeft = -4f;
            }, e =>
            {
                e.style.width = StyleKeyword.Null;
                e.style.height = StyleKeyword.Null;
                e.style.marginRight = StyleKeyword.Null;
                e.style.marginLeft = StyleKeyword.Null;
            });
        }
        MoveInto(meta, rank);
        var divider = new Label("//");
        divider.AddToClassList("wm-squads__divider");
        divider.style.fontSize = SquadMetaSize;
        divider.style.color = SquadMetaInk;
        meta.Add(divider);
        MoveInto(meta, unit);
        Inline(rank, e => e.style.marginRight = 4f, e => e.style.marginRight = StyleKeyword.Null);
        _shellUndo.Add(meta.RemoveFromHierarchy);
    }

    // Gives a vanilla element these margins and, when asked, this padding on every side but the
    // top, which takes its own.
    private void Spaced(VisualElement element, float left, float top, float right, float bottom, float? paddingTop)
    {
        Inline(element, e =>
        {
            var s = e.style;
            s.marginLeft = left;
            s.marginTop = top;
            s.marginRight = right;
            s.marginBottom = bottom;
            if (paddingTop is { } over)
            {
                Pad(s, 0f);
                s.paddingTop = over;
            }
        }, e =>
        {
            var s = e.style;
            s.marginLeft = StyleKeyword.Null;
            s.marginTop = StyleKeyword.Null;
            s.marginRight = StyleKeyword.Null;
            s.marginBottom = StyleKeyword.Null;
            Pad(s, null);
        });
    }

    // Moves a vanilla element to the end of another, and back where it was when the shell comes off.
    private void MoveInto(VisualElement to, VisualElement element)
    {
        if (element?.parent == null || element.parent == to)
            return;
        _shellUndo.Add(PutBack(element));
        to.Add(element);
    }

    // Shows whoever vanilla has selected: a candidate as a hologram with her panel, the doll just
    // hired as herself, and nobody for a leader who was already on the roster. Runs whenever
    // vanilla fills its window or a slot is clicked.
    private void RefreshRecruit()
    {
        if (_staging.RecruitRefreshing || _shellRoot == null || !_shellRoot.ClassListContains(RecruitClass))
            return;
        _staging.RecruitRefreshing = true;
        try
        {
            var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<HiringUIScreen>();
            var roster = StrategyState.Get()?.Roster;
            var window = UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.HiringWindow));
            if (screen == null || roster == null || window == null)
                return;
            // The one on show has just been hired: she stays, as herself, behind the bars, whoever
            // vanilla selects while it fills its lists again.
            if (_staging.Candidate != null && !_staging.CandidateReal && !_staging.CurrentHiring && StatusOf(roster, _staging.CandidateLeader) == UnitLeaderStatus.Hired)
                Context.Coroutines.Start(Hire(_shell));
            // While the bars shut the panel is put away: vanilla fills it with someone else.
            if (_staging.CurrentHiring)
            {
                window.AddToClassList(RecruitEmptyClass);
                return;
            }
            var (template, status) = Selected(screen, roster);
            var kept = template != null && template.Pointer == _staging.CandidateTemplate && _staging.CandidateReal;
            // Vanilla opens on the first leader already hired. Here that is nobody to show, so
            // the first candidate is picked for it.
            if (status != UnitLeaderStatus.Hirable && !kept && FirstCandidate() is { } first)
            {
                screen.OnUnitSlotClicked(first);
                (template, status) = Selected(screen, roster);
            }
            SizeCandidateTiles();
            ShowHireCost(window);
            if (UI.Find(window, UiSelector.Name(FormTabsName)) is { } forms)
            {
                var row = forms.style;
                row.marginLeft = 0;
                row.marginRight = 0;
                row.marginTop = 0;
                row.marginBottom = RecruitPad;
            }
            var shown = status == UnitLeaderStatus.Hirable || kept;
            window.EnableInClassList(RecruitEmptyClass, !shown);
            if (UI.Find(_shellRoot, UiSelector.Name(RecruitEmptyName)) is { } empty)
                empty.style.display = !shown && FirstCandidate() == null ? DisplayStyle.Flex : DisplayStyle.None;
            if (!kept)
                ShowCandidate(shown ? template : null, roster);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: recruit refresh failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _staging.RecruitRefreshing = false;
        }
    }

    private static (UnitLeaderTemplate Template, UnitLeaderStatus Status) Selected(HiringUIScreen screen, Roster roster)
    {
        var template = screen.m_SelectedSlot?.GetLeaderTemplate();
        return (template, StatusOf(roster, template));
    }

    private static UnitLeaderStatus StatusOf(Roster roster, UnitLeaderTemplate template)
    {
        var status = UnitLeaderStatus.Unknown;
        if (template != null)
            roster.GetLeaderByTemplate(template, out status);
        return status;
    }

    // Vanilla's tile holds its picture at a fixed size, wider than the roster's tiles. Here it
    // fills the tile. On the elements, since vanilla makes them anew as the list changes.
    private void SizeCandidateTiles()
    {
        var list = _shellRoot != null ? UI.Find(_shellRoot, UiSelector.Name(VanillaUi.Elements.HirableScrollView)) : null;
        if (list == null)
            return;
        foreach (var slot in UI.FindAll(list, UiSelector.TypeName(VanillaUi.Elements.HiringUnitSlot)))
            if (slot.childCount > 0)
            {
                var picture = slot.ElementAt(0).style;
                picture.width = Length.Percent(100);
                picture.height = Length.Percent(100);
                picture.marginLeft = 0;
                picture.marginTop = 0;
                picture.marginRight = 0;
                picture.marginBottom = 0;
                // Her unit's badge is in the panel's header, and covers her face at this size.
                if (UI.Find(slot, UiSelector.Name(VanillaUi.Elements.SlotOverlay)) is { } badge)
                    badge.style.display = DisplayStyle.None;
                // Vanilla draws her picture wider than the tile and hung off its left, which leaves
                // a gap at the right here.
                if (UI.Find(slot, UiSelector.Name(VanillaUi.Elements.SlotPortrait)) is { } face)
                {
                    face.style.left = 0;
                    face.style.right = 0;
                    face.style.marginLeft = 0;
                    face.style.width = Length.Percent(100);
                }
                // Her kind (on foot or in a vehicle) sits at the tile's foot, where vanilla holds
                // it over the bars.
                foreach (var kind in TileKindMarks)
                    if (UI.Find(slot, UiSelector.Name(kind)) is { } mark)
                    {
                        mark.style.position = Position.Absolute;
                        mark.style.left = 2.4f;
                        mark.style.top = StyleKeyword.Auto;
                        mark.style.bottom = 2.4f;
                        mark.style.marginLeft = 0;
                        mark.style.marginBottom = 0;
                    }
                // Vanilla draws its mark of the chosen tile and its red wash over one too dear a
                // little larger than the tile, which here covers the gaps between tiles and
                // leaves the mark showing on two sides. Both lie on the tile exactly, and the
                // mark is the roster's: a thin gold edge.
                foreach (var over in TileCovers)
                    if (UI.Find(slot, UiSelector.Name(over)) is { } cover)
                    {
                        var c = cover.style;
                        c.position = Position.Absolute;
                        c.left = 0;
                        c.top = 0;
                        c.right = 0;
                        c.bottom = 0;
                        c.width = StyleKeyword.Auto;
                        c.height = StyleKeyword.Auto;
                        c.marginLeft = 0;
                        c.marginTop = 0;
                        c.marginRight = 0;
                        c.marginBottom = 0;
                        if (over == VanillaUi.Elements.SlotSelected)
                        {
                            c.backgroundImage = StyleKeyword.None;
                            Border(c, CandidateSelectedEdge, Wui.Gold);
                        }
                    }
                // The tile is her face and her cost: no bars, and no stars.
                foreach (var part in TileHiddenParts)
                    if (UI.Find(slot, UiSelector.Name(part)) is { } drawn)
                        drawn.style.display = DisplayStyle.None;
            }
    }

    // The Hire button carries what she costs: the number and picture of vanilla's own chip.
    private static void ShowHireCost(VisualElement window)
    {
        var column = UI.Find(window, UiSelector.Name(VanillaUi.Elements.UnitLeaderColumn));
        var chip = column != null ? UI.Find(column, UiSelector.Name(VanillaUi.Elements.HiringCosts)) : null;
        var cost = UI.Find(window, UiSelector.Name(HireCostName));
        if (chip == null || cost == null)
            return;
        // Vanilla puts its chip away for a leader who is not for hire.
        cost.style.display = chip.resolvedStyle.display == DisplayStyle.None ? DisplayStyle.None : DisplayStyle.Flex;
        // The button keeps vanilla's colours, which change with its state, and the cost is
        // written in the colour its word has.
        var word = cost.parent != null ? UI.Find(cost.parent, UiSelector.Name(VanillaUi.Elements.ButtonText)) : null;
        if (UI.Find(cost, UiSelector.Name(HireCostName + "-value"))?.TryCast<Label>() is { } value)
        {
            value.text = UI.Find(chip, UiSelector.Name(VanillaUi.Elements.StatValue))?.TryCast<Label>()?.text ?? "";
            if (word != null)
                value.style.color = word.resolvedStyle.color;
        }
        if (UI.Find(cost, UiSelector.Name(HireCostName + "-icon")) is { } icon && UI.Find(chip, UiSelector.Name(VanillaUi.Elements.StatIcon)) is { } drawn)
        {
            icon.style.backgroundImage = drawn.resolvedStyle.backgroundImage;
            if (word != null)
                icon.style.unityBackgroundImageTintColor = word.resolvedStyle.color;
        }
    }

    private HiringUnitSlot FirstCandidate() => SlotOf(VanillaUi.Elements.HirableScrollView, IntPtr.Zero);

    // The slot of a leader's template in one of vanilla's two lists, or the list's first slot
    // when no template is asked for.
    private HiringUnitSlot SlotOf(string listName, IntPtr template)
    {
        var list = _shellRoot != null ? UI.Find(_shellRoot, UiSelector.Name(listName)) : null;
        if (list == null)
            return null;
        foreach (var element in UI.FindAll(list, UiSelector.TypeName(VanillaUi.Elements.HiringUnitSlot)))
            if (element.TryCast<HiringUnitSlot>() is { } slot && (template == IntPtr.Zero || slot.GetLeaderTemplate()?.Pointer == template))
                return slot;
        return null;
    }

    // The refitting room with an empty stand where the squad stands on Squads, and the same
    // camera round it.
    private void StageRecruit()
    {
        if (_room == null || _camera == null)
            return;
        if (_staging.RecruitStand != null)
            return;
        var from = StagePose();
        UnstageArmory();
        TravelFrom(from);
        try
        {
            var refit = SpawnSet(_refitSet);
            if (refit != null)
            {
                ActivateSet(_refitSet);
                MapHall(refit.transform);
            }
            _staging.Stage = (refit ?? _room).transform;
            _staging.RecruitStand = new GameObject("WOMENACE_Candidate");
            _staging.RecruitStand.transform.SetParent(_staging.Stage, false);
            _staging.RecruitStand.transform.localPosition = ArmoryStand;
            // Facing the camera's starting place.
            _staging.RecruitStand.transform.localRotation = Quaternion.Euler(0f, ArmoryYaw + 180f, 0f);
            var slot = new GameObject("Slot");
            slot.transform.SetParent(_staging.RecruitStand.transform, false);
            _staging.RecruitSlot = slot.transform;
            var root = _staging.RecruitStand.transform;
            _staging.StagedRoots.Add(new StagedRoot { Root = root, Position = root.position, Rotation = root.rotation, Scale = root.localScale });
            _staging.CurrentStageShot = CandidateShot;
            _staging.OrbitYaw = _staging.OrbitYawTarget = ArmoryYaw;
            _staging.OrbitPitch = _staging.OrbitPitchTarget = RecruitPitch;
            _staging.OrbitDistance = _staging.OrbitDistanceTarget = RecruitDistance;
            _staging.OrbitRoutine = Context.Coroutines.Start(Orbit());
            Context.Log.Info("strategy diorama: recruit staged");
            RefreshRecruit();
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: recruit staging failed: {ex.GetType().Name}: {ex.Message}");
            UnstageArmory();
        }
    }

    private void ClearRecruitStand()
    {
        ClearCandidate();
        if (_staging.RecruitStand != null)
            UnityEngine.Object.Destroy(_staging.RecruitStand);
        _staging.RecruitStand = null;
        _staging.RecruitSlot = null;
        _staging.CurrentHiring = false;
    }

    private void ClearCandidate()
    {
        DropHolograms();
        _staging.CandidateOwn.Clear();
        if (_staging.Candidate != null)
        {
            _life.CrewFidgets.Remove(_staging.Candidate);
            UnityEngine.Object.Destroy(_staging.Candidate);
        }
        _staging.Candidate = null;
        _staging.CandidateTemplate = IntPtr.Zero;
        _staging.CandidateLeader = null;
        _staging.CandidateReal = false;
    }

    private void DropHolograms()
    {
        foreach (var material in _staging.CandidateHolograms)
            if (material != null)
                UnityEngine.Object.Destroy(material);
        _staging.CandidateHolograms.Clear();
    }

    // Stands the leader's own model on the stand as a hologram. A leader with no model of ours
    // leaves the stand empty.
    private void ShowCandidate(UnitLeaderTemplate template, Roster roster)
    {
        var pointer = template != null ? template.Pointer : IntPtr.Zero;
        if (pointer == _staging.CandidateTemplate || _staging.RecruitSlot == null)
            return;
        ClearCandidate();
        _staging.CandidateTemplate = pointer;
        _staging.CandidateLeader = template;
        if (template == null)
            return;
        var leader = roster.GetLeaderByTemplate(template, out _) ?? roster.GetTempLeader(template);
        var tag = leader != null ? Affinity.CharacterTag(leader) : null;
        var doll = tag != null ? SpawnDoll(tag, "candidate") : null;
        if (doll == null)
            return;
        doll.transform.SetParent(_staging.RecruitSlot, false);
        doll.transform.localPosition = Vector3.zero;
        doll.transform.localRotation = Quaternion.identity;
        Pose(doll, "Idle");
        _staging.Candidate = doll;
        WearHologram(doll);
        _staging.CandidateDrawn = 0f;
    }

    // Every renderer of the doll wears the hologram, with its own texture's light and dark.
    private void WearHologram(GameObject doll)
    {
        if (HologramMaterial() == null)
            return;
        var floor = doll.transform.position.y;
        var top = floor + 1f;
        foreach (var renderer in doll.GetComponentsInChildren<Renderer>(true))
        {
            // Her contact shadow is no part of a thing made of light.
            if (renderer.name == ContactShadowName)
            {
                renderer.enabled = false;
                continue;
            }
            top = Mathf.Max(top, renderer.bounds.max.y);
            _staging.CandidateOwn.Add((renderer, renderer.sharedMaterials));
            WearHolograms(renderer, _staging.CandidateHolograms, (worn, source) =>
            {
                var shader = source != null && source.shader != null ? source.shader.name : "";
                if (HologramSkips.Any(skip => shader.Contains(skip, StringComparison.Ordinal)))
                    worn.SetFloat(HologramOpacityId, 0f);
            });
        }
        foreach (var material in _staging.CandidateHolograms)
        {
            material.SetFloat(HologramFloorId, floor);
            material.SetFloat(HologramHeightId, top - floor);
            material.SetFloat(HologramRevealId, 0f);
        }
    }

    // The hologram comes off and the doll stands in the room as herself.
    private void WearOwn()
    {
        foreach (var (renderer, own) in _staging.CandidateOwn)
            if (renderer != null)
                renderer.sharedMaterials = own;
        _staging.CandidateOwn.Clear();
        DropHolograms();
        if (_staging.Candidate != null)
            foreach (var renderer in _staging.Candidate.GetComponentsInChildren<Renderer>(true))
                if (renderer.name == ContactShadowName)
                    renderer.enabled = true;
        _staging.CandidateReal = true;
    }

    // The bars shut on a hire, and open on the doll herself where her hologram stood.
    private System.Collections.IEnumerator Hire(VisualElement shell)
    {
        _staging.CurrentHiring = true;
        var hired = _staging.Candidate;
        if (shell != null)
            ShutBars(shell, BarsCutClass);
        for (var t = 0f; t < BarsCutSeconds + BarsHeldSeconds; t += Time.unscaledDeltaTime)
            yield return null;
        // The screen may have been left, or left and come back to, while the bars were shut.
        // The candidate standing there now is another, and still a hologram.
        if (!_staging.CurrentHiring || hired == null || _staging.Candidate != hired)
            yield break;
        WearOwn();
        // Vanilla moves its selection off her as it hires her. It is put back on her place among
        // the hired, so the panel that opens with her is hers.
        try
        {
            var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<HiringUIScreen>();
            if (screen != null && SlotOf(VanillaUi.Elements.HiredScrollView, _staging.CandidateTemplate) is { } hers)
            {
                _staging.RecruitRefreshing = true;
                screen.OnUnitSlotClicked(hers);
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: selecting the hired leader failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _staging.RecruitRefreshing = false;
        }
        _staging.CurrentHiring = false;
        RefreshRecruit();
        if (shell != null && _shell == shell && shell.panel != null)
            OpenShutBars(shell, BarsCutClass);
    }

    // A candidate is drawn in from the floor up.
    private void DriveRecruit()
    {
        if (_staging.Candidate == null || _staging.CandidateReal || _staging.CandidateDrawn >= 1f)
            return;
        _staging.CandidateDrawn = Mathf.Min(1f, _staging.CandidateDrawn + HitchStep(CandidateLongestStep) / CandidateDrawSeconds);
        // Past the top of her, so the bright line leaves her head.
        var reveal = Ease.OutCubic(_staging.CandidateDrawn) * 1.1f;
        foreach (var material in _staging.CandidateHolograms)
            if (material != null)
                material.SetFloat(HologramRevealId, reveal);
    }
}
