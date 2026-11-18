using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.UI.Strategy;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The system map says how each client stands with the player before an operation is picked. A
// planet's name is laid out as a card of vanilla's faction list is: the faction's picture at the
// left, the name over it, and under the name the trust level and how far along that level is,
// with a row for every faction offering an operation there. The parts are children of vanilla's
// own name, so they follow it wherever the map puts it.
public sealed partial class StrategyDiorama
{
    private const string PlanetRepName = "wm-planet-rep";
    private const string PlanetPlateName = "wm-planet-rep-plate";
    // The parts are laid out some frames after they are made, so the plates are fitted for this
    // many frames after the names are read and then left alone.
    private const int PlanetPlateFrames = 30;
    private int _planetPlateFrames;
    private const float PlatePadX = 4f, PlatePadY = 2f;
    private const float RepRowHeight = 11f, RepRowGap = 2f, RepPictureGap = 4f;
    private static readonly Color RepPlate = new(0.015f, 0.03f, 0.05f, 0.82f);
    private static readonly Color RepTrack = new(0.13f, 0.15f, 0.18f, 0.9f);
    private static readonly Color RepFill = new(0.47f, 0.91f, 0.96f);
    // The level's box as vanilla's faction list draws it: gold figures in its bold face on a
    // near-black square.
    private static readonly Color RepLevelInk = new(221f / 255f, 190f / 255f, 98f / 255f);
    private static readonly Color RepLevelBox = new(28f / 255f, 28f / 255f, 28f / 255f);

    // Set where the styles a stylesheet would carry are not known to reach vanilla's map names.
    private void ShowPlanetRep(VisualElement name)
    {
        try
        {
            var hud = name.TryCast<PlanetHUD>();
            var title = hud?.m_NameLabel;
            if (title == null)
                return;
            // Each part takes itself out. The parent's RemoveAt goes through its hierarchy, a value
            // type the interop boxes, and a collection can free the box while it is in use.
            for (var i = name.childCount - 1; i >= 0; i--)
                if (name.ElementAt(i) is { name: PlanetPlateName or PlanetRepName } part)
                    part.RemoveFromHierarchy();
            // The room behind the map is bright in places, so every name stands on a dark plate,
            // and its parts with it.
            var plate = new VisualElement { name = PlanetPlateName, pickingMode = PickingMode.Ignore };
            plate.style.position = Position.Absolute;
            plate.style.backgroundColor = RepPlate;
            name.Insert(0, plate);
            _planetPlateFrames = PlanetPlateFrames;
            var planet = hud.m_Planet?.GetTemplate();
            var state = StrategyState.Get();
            var operations = state?.Operations?.m_AvailableOperations;
            // Where vanilla's layout stands the name's words, which the parts are placed round.
            var words = name.WorldToLocal(title.worldBound);
            if (planet == null || operations == null || state.StoryFactions == null || float.IsNaN(words.width) || words.width <= 0f)
                return;
            var rows = new VisualElement { name = PlanetRepName, pickingMode = PickingMode.Ignore };
            rows.style.position = Position.Absolute;
            rows.style.left = words.xMin;
            rows.style.top = words.yMax + RepRowGap;
            Sprite picture = null;
            var shown = new HashSet<IntPtr>();
            for (var i = 0; i < operations.Count; i++)
            {
                var operation = operations[i];
                if (operation?.GetPlanetTemplate()?.Pointer != planet.Pointer || operation.GetClientFaction() is not { } client)
                    continue;
                if (!shown.Add(client.Pointer) || state.StoryFactions.GetFaction(client) is not { } faction)
                    continue;
                // The first faction's picture stands beside the name. Another's heads its own row.
                rows.Add(RepRow(faction, client, rows.childCount > 0 ? client.Icon : null));
                picture ??= client.Icon;
            }
            if (rows.childCount == 0)
                return;
            name.Add(rows);
            if (picture == null)
                return;
            // As tall as the name and its first row together.
            var side = words.height + RepRowGap + RepRowHeight;
            var beside = new VisualElement { name = PlanetRepName, pickingMode = PickingMode.Ignore };
            beside.style.position = Position.Absolute;
            beside.style.left = words.xMin - RepPictureGap - side;
            beside.style.top = words.yMin;
            beside.style.width = side;
            beside.style.height = side;
            beside.style.backgroundImage = new StyleBackground(picture);
            name.Add(beside);
        }
        catch (Exception ex) { Context.Log.Warn($"strategy diorama: planet standing failed: {ex}"); }
    }

    // The plate takes in the name and every part beside and under it.
    private static void FitPlanetPlate(VisualElement name)
    {
        var label = name.TryCast<PlanetHUD>()?.m_NameLabel;
        if (label == null)
            return;
        VisualElement plate = null;
        for (var i = 0; i < name.childCount && plate == null; i++)
            if (name.ElementAt(i).name == PlanetPlateName)
                plate = name.ElementAt(i);
        if (plate == null)
            return;
        var box = name.WorldToLocal(label.worldBound);
        if (float.IsNaN(box.width) || box.width <= 0f)
            return;
        for (var i = 0; i < name.childCount; i++)
            if (name.ElementAt(i).name == PlanetRepName)
                TakeIn(name, name.ElementAt(i), ref box);
        plate.style.left = box.xMin - PlatePadX;
        plate.style.top = box.yMin - PlatePadY;
        plate.style.width = box.width + 2f * PlatePadX;
        plate.style.height = box.height + 2f * PlatePadY;
    }

    private static void TakeIn(VisualElement name, VisualElement part, ref Rect box)
    {
        var also = name.WorldToLocal(part.worldBound);
        if (!float.IsNaN(also.width) && also.width > 0f && also.height > 0f)
            box = Rect.MinMaxRect(MathF.Min(box.xMin, also.xMin), MathF.Min(box.yMin, also.yMin), MathF.Max(box.xMax, also.xMax), MathF.Max(box.yMax, also.yMax));
        for (var i = 0; i < part.childCount; i++)
            TakeIn(name, part.ElementAt(i), ref box);
    }

    private void FitPlanetPlates()
    {
        if (_planetPlateFrames <= 0)
            return;
        _planetPlateFrames--;
        foreach (var (hud, _, _, _, _) in _starMapNames)
            FitPlanetPlate(hud);
    }

    // As vanilla's faction list has it: the level in Roman figures, then the trust earned within
    // it, which is what the bar is a share of.
    private static VisualElement RepRow(StoryFaction faction, StoryFactionTemplate template, Sprite picture)
    {
        var row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.height = RepRowHeight;
        row.style.marginBottom = RepRowGap;
        if (picture != null)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.style.width = RepRowHeight;
            icon.style.height = RepRowHeight;
            icon.style.marginRight = 3;
            icon.style.flexShrink = 0;
            icon.style.backgroundImage = new StyleBackground(picture);
            row.Add(icon);
        }
        var level = faction.GetTrustLevel();
        var (points, span) = LevelProgress(faction, template, level);
        row.Add(RepLevel(level));
        var earned = new TextElement { text = points.ToString(), pickingMode = PickingMode.Ignore };
        earned.style.color = RepFill;
        earned.style.fontSize = 8;
        earned.style.letterSpacing = 0.6f;
        earned.style.marginRight = 5;
        earned.style.whiteSpace = WhiteSpace.NoWrap;
        row.Add(earned);
        var track = new VisualElement { pickingMode = PickingMode.Ignore };
        track.style.width = 40;
        track.style.height = 3;
        track.style.flexShrink = 0;
        track.style.backgroundColor = RepTrack;
        var fill = new VisualElement { pickingMode = PickingMode.Ignore };
        fill.style.height = Length.Percent(100);
        fill.style.width = Length.Percent(span > 0 ? 100f * Math.Clamp(points / (float)span, 0f, 1f) : 100f);
        fill.style.backgroundColor = RepFill;
        track.Add(fill);
        row.Add(track);
        return row;
    }

    private static VisualElement RepLevel(int level)
    {
        var box = new VisualElement { pickingMode = PickingMode.Ignore };
        box.style.height = RepRowHeight;
        box.style.flexShrink = 0;
        box.style.marginRight = 3;
        box.style.paddingLeft = box.style.paddingRight = 2;
        box.style.justifyContent = Justify.Center;
        box.style.backgroundColor = RepLevelBox;
        var figures = new TextElement { text = Roman(level), pickingMode = PickingMode.Ignore };
        NavigationMenu.ApplyGameFont(figures);
        figures.style.color = RepLevelInk;
        figures.style.fontSize = 9;
        figures.style.unityTextAlign = TextAnchor.MiddleCenter;
        figures.style.whiteSpace = WhiteSpace.NoWrap;
        box.Add(figures);
        return box;
    }

    // The trust earned within the faction's level, and how much the level takes (0 at the last
    // one). The totals a level needs are asked of the template one by one: the highest that still
    // gives this level starts it, and the lowest that gives a higher one ends it.
    private static (int Points, int Span) LevelProgress(StoryFaction faction, StoryFactionTemplate template, int level)
    {
        var needed = template.RequiredTotalTrustForLevel;
        var total = faction.GetTotalTrust();
        if (needed == null)
            return (total, 0);
        var (from, to) = (0, int.MaxValue);
        for (var i = 0; i < needed.Length; i++)
        {
            var at = needed[i];
            if (template.GetTrustLevel(at) <= level)
                from = Math.Max(from, at);
            else
                to = Math.Min(to, at);
        }
        return (Math.Max(0, total - from), to == int.MaxValue ? 0 : to - from);
    }

    private static readonly string[] RomanFigures = { "0", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII", "XIV", "XV" };

    private static string Roman(int level) => level >= 0 && level < RomanFigures.Length ? RomanFigures[level] : level.ToString();
}
