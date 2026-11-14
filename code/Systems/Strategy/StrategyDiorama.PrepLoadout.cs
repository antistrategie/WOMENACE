using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Mission preparation's loadout bar, a section a squad: each deployed squad's share of the
// loadout is marked off along the bar in the roster's order, with the squad's badge in the middle
// of a share wide enough to hold it. The open squad's share is lit, and one that is not deployed
// is shown after the others as what deploying it would add. Vanilla still fills the bar (gold up to
// the limit, red past it). It draws the open squad's share at the end of the fill, which is not
// where that squad's section is, so its share is put out of sight and the section is lit.
// Vanilla sets that share's opacity and display itself, so it is hidden by its visibility.
public sealed partial class StrategyDiorama
{
    private const string PrepSectionsName = "wm-prep-supplies-sections";
    // A badge's side, and how much wider than it a section is before it holds one.
    private const float PrepBadge = 18f;
    private const float PrepBadgeRoom = 2f;
    private string _prepSectionsOf;

    private void LayOutPrepSections(VisualElement panel)
    {
        if (UI.Find(panel, UiSelector.Name(PrepSectionsName)) == null)
        {
            var sections = new VisualElement { name = PrepSectionsName, pickingMode = PickingMode.Ignore };
            sections.AddToClassList("wm-prep__sections");
            sections.style.top = PrepSuppliesBarTop;
            sections.style.width = PrepSuppliesWidth;
            sections.style.height = PrepSuppliesBarHeight;
            panel.Add(sections);
            _shellUndo.Add(() =>
            {
                sections.RemoveFromHierarchy();
                _prepSectionsOf = null;
            });
        }
        foreach (var name in new[] { "SelectedDark", "SelectedBright", "ExceedSelectedDark", "ExceedSelectedBright" })
            Inline(UI.Find(panel, UiSelector.Name(name)), e => e.style.visibility = Visibility.Hidden, e => e.style.visibility = StyleKeyword.Null);
        // The fill is as tall as the bar, which is taller here than vanilla's.
        foreach (var name in new[] { "Spent", "Exceed" })
            Inline(UI.Find(panel, UiSelector.Name(name)), e => e.style.height = PrepSuppliesBarHeight, e => e.style.height = StyleKeyword.Null);
    }

    private void RefreshPrepSections()
    {
        var sections = _shellRoot != null ? UI.Find(_shellRoot, UiSelector.Name(PrepSectionsName)) : null;
        var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<MissionPrepUIScreen>();
        if (sections == null || screen == null)
            return;
        // The limit as vanilla prints it ("used / limit"), which is the one its bar is filled
        // against: the mission's own figure does not count what the operation adds to it.
        var printed = System.Text.RegularExpressions.Regex.Matches(
            System.Text.RegularExpressions.Regex.Replace(screen.m_SuppliesValue?.text ?? "", "<[^>]*>", ""), "[0-9]+");
        var limit = printed.Count >= 2 ? int.Parse(printed[1].Value) : 0;
        var open = _squadWindow != null ? Affinity.LeaderOf(_squadWindow) : null;
        var selector = screen.m_UnitSelector;
        // In the roster's order, which does not change: vanilla reorders the squads on the map as
        // one is picked. The open squad, if it is not deployed, comes last as what it would add.
        var squads = new List<(IntPtr Leader, int Cost, UnityEngine.Sprite Badge, bool Would)>();
        var openDeployed = false;
        foreach (var element in UI.FindAll(_shellRoot, UiSelector.TypeName(VanillaUi.Elements.PrepUnitSlot)))
        {
            var leader = element.TryCast<MissionPrepUnitSelectSlot>()?.GetLeader();
            if (leader == null || selector == null || selector.GetDeployedEntity(leader) == null)
                continue;
            squads.Add((leader.Pointer, leader.GetDeployCosts().GetAmount(), leader.GetTemplate()?.Badge, false));
            openDeployed |= open != null && open.Pointer == leader.Pointer;
        }
        if (open != null && !openDeployed)
            squads.Add((open.Pointer, open.GetDeployCosts().GetAmount(), open.GetTemplate()?.Badge, true));
        var signature = $"{limit}:{open?.Pointer}:{string.Join(",", squads.Select(s => $"{s.Leader}={s.Cost}{(s.Would ? "?" : "")}"))}";
        if (signature == _prepSectionsOf)
            return;
        _prepSectionsOf = signature;
        sections.Clear();
        if (limit <= 0)
            return;
        // Vanilla's own measure: the limit is always this far along the bar.
        var perSupply = PrepSuppliesLimitAt / limit;
        var at = 0f;
        for (var i = 0; i < squads.Count; i++)
        {
            var (leader, cost, badge, would) = squads[i];
            var width = cost * perSupply;
            var isOpen = open != null && open.Pointer == leader;
            var section = new VisualElement { pickingMode = PickingMode.Ignore };
            section.AddToClassList("wm-prep__section");
            section.EnableInClassList("wm-prep__section--open", isOpen && !would);
            section.style.left = at;
            section.style.width = width;
            if (would)
            {
                // What deploying her would add: pale up to the limit and red past it.
                var within = Math.Clamp(PrepSuppliesLimitAt - at, 0f, width);
                foreach (var (style, from, across) in new[] { ("wm-prep__would", 0f, within), ("wm-prep__would--over", within, width - within) })
                {
                    if (across <= 0f)
                        continue;
                    var fill = new VisualElement { pickingMode = PickingMode.Ignore };
                    fill.AddToClassList("wm-prep__would");
                    fill.AddToClassList(style);
                    fill.style.left = from;
                    fill.style.width = across;
                    section.Add(fill);
                }
            }
            // A line where one squad's share ends and the next begins, and none after the last:
            // vanilla's fill ends a fraction of a unit off the sum of the shares. It is a child
            // and not the section's border, which would set everything in the section in by its
            // width.
            if (i > 0)
            {
                var line = new VisualElement { pickingMode = PickingMode.Ignore };
                line.AddToClassList("wm-prep__section-line");
                section.Add(line);
            }
            // A share too narrow for a badge has none, but for the open squad's, which shows over
            // its neighbours' edges.
            if (badge != null && (isOpen || width >= PrepBadge + PrepBadgeRoom))
            {
                var picture = new VisualElement { pickingMode = PickingMode.Ignore };
                picture.AddToClassList("wm-prep__section-badge");
                picture.style.backgroundImage = new StyleBackground(badge);
                picture.style.left = (width - PrepBadge) / 2f;
                section.Add(picture);
            }
            sections.Add(section);
            at += width;
        }
    }
}
