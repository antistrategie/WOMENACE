using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Strategy;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The unit window's squad type ("INFANTRY" and its icon) names a support team's kind instead:
// "ARMED ECHELON", with the team's skill icon in black and white (<team>/unit_type).
//
// Vanilla writes the type in UnitLeaderUIExtensions.InitUnitTypeUIElements, which is given only
// the unit's ActorType, so the team is known one step out: InitUnitWindowHeader (the leader and
// the header it passes on) and UnitWindowEquipment.UpdateSquaddiesElements (the squad type row
// shares its line with the squad size). A row this system wrote is marked, and the next leader
// who is not a team has vanilla's type written back into it, in case vanilla leaves that row as
// it was.
public sealed class SupportTeamUnitTypeSystem : JiangyuSystem
{
    private const string TypeName = "UnitTypeName";
    private static readonly string[] TypeIcons = { "UnitTypeIcon", "UnitTypeIconBig" };
    // How far out the search for the type row may go: the header, the squad size and the row are
    // all inside the unit window.
    private const int WindowDepth = 6;
    private const string WrittenClass = "wmgfl-team-type";

    private readonly Dictionary<string, Sprite> _icons = new(StringComparer.Ordinal);

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.UI.UnitLeaderUIExtensions", "InitUnitWindowHeader", OnHeader);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindowEquipment", "UpdateSquaddiesElements", OnSquaddiesRow);
    }

    // Args: BaseUnitLeader, VisualElement header.
    private void OnHeader(PatchInfo info)
    {
        try
        {
            if (info.Args is not { Count: > 1 })
                return;
            if (info.Args[1] is VisualElement header)
                Write(Around(header), info.Args[0] as BaseUnitLeader);
        }
        catch (Exception ex) { Context.Log.Warn($"support team: unit type in header failed: {ex.Message}"); }
    }

    private void OnSquaddiesRow(PatchInfo info)
    {
        try
        {
            var equipment = (info.Instance as Il2CppObjectBase)?.TryCast<UnitWindowEquipment>();
            if (equipment != null)
                Write(Around(equipment.m_SquaddiesContainer), equipment.m_Leader);
        }
        catch (Exception ex) { Context.Log.Warn($"support team: unit type row failed: {ex.Message}"); }
    }

    // The nearest element holding the type row, out from one inside the unit window.
    private static VisualElement Around(VisualElement inside)
    {
        var window = inside;
        for (var i = 0; window != null && i < WindowDepth && window.parent != null && UI.Find(window, UiSelector.Name(TypeName)) == null; i++)
            window = window.parent;
        return window;
    }

    private void Write(VisualElement root, BaseUnitLeader leader)
    {
        if (root == null || leader == null)
            return;
        var team = SupportTeams.Of(leader);
        if (team != null)
        {
            Apply(root, team);
            return;
        }
        var written = UI.FindAll(root, UiSelector.Class(WrittenClass)).ToList();
        if (written.Count == 0)
            return;
        foreach (var element in written)
            element.RemoveFromClassList(WrittenClass);
        var template = leader.LeaderTemplate;
        if (template != null)
            UnitLeaderUIExtensions.InitUnitTypeUIElements(root, template.UnitActorType);
    }

    private void Apply(VisualElement root, SupportTeams.Team team)
    {
        // The type name is a game label type, which a C# type test does not see through, so it
        // is cast through the interop.
        foreach (var element in UI.FindAll(root, UiSelector.Name(TypeName)))
            if (element.TryCast<TextElement>() is { } label)
            {
                label.text = Locale.Text("WOMENACE::ui/support_team_type", "Armed Echelon").ToUpperInvariant();
                label.AddToClassList(WrittenClass);
            }
        var icon = Icon(team);
        if (icon == null)
            return;
        foreach (var name in TypeIcons)
            foreach (var element in UI.FindAll(root, UiSelector.Name(name)))
            {
                element.style.backgroundImage = new StyleBackground(icon);
                element.AddToClassList(WrittenClass);
            }
    }

    private Sprite Icon(SupportTeams.Team team)
    {
        if (_icons.TryGetValue(team.Name, out var cached) && cached != null)
            return cached;
        var icon = Context.Assets.Load<Sprite>(team.TypeIconAsset);
        if (icon == null)
            Context.Log.Warn($"support team: no type icon '{team.TypeIconAsset}'");
        return _icons[team.Name] = icon;
    }
}
