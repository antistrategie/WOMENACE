using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Dev hooks for mission preparation (code/Dev/Diorama.Dev.cs). *.Dev.cs files stay out of release builds.
public sealed partial class StrategyDiorama
{
    // A flat view held part way, for looking at one moment of the move between the views.
    private float? _prepFlatHeld;
    // How often the flat map has been dressed in the hologram's colours.
    private int _prepFlatDressings;
    // Vanilla's flat map shown faintly over the hologram, to line the two up by.
    private bool _prepFlatMapShown;
    // A stand-in for the pointer, in screen pixels from the bottom left, for driving the map
    // without a mouse.
    private Vector2? _prepPointerOverride;
    // Every squad standing as its badge, as a squad with no chibi stands.
    private bool _prepChibisOff;

    partial void HoldPrepFlat(ref float? held) => held = _prepFlatHeld ?? held;

    partial void CountPrepFlatDressing() => _prepFlatDressings++;

    partial void StandInPrepPointer(ref Vector2 aim) => aim = _prepPointerOverride ?? aim;

    partial void ShowPrepFlatMap(ref float opacity) => opacity = _prepFlatMapShown ? 0.55f : opacity;

    partial void ShowPrepChibis(ref bool shown) => shown = shown && !_prepChibisOff;

    internal static string PrepOrbitFromDev(float yaw, float pitch, float distance)
    {
        var d = _instance;
        if (d == null)
            return "no diorama";
        d._prep.YawTarget = yaw;
        d._prep.PitchTarget = Mathf.Clamp(pitch, PrepOrbitPitchMin, PrepOrbitPitchMax);
        d._prep.DistanceTarget = Mathf.Clamp(distance, PrepOrbitDistanceMin, d.PrepDistanceMax);
        return $"orbit to bearing {yaw:0}, tilt {d._prep.PitchTarget:0}, distance {d._prep.DistanceTarget:0.0}";
    }

    // Builds the hologram map again from nothing, to watch it come up.
    internal static string PrepRebuildFromDev()
    {
        if (_instance == null)
            return "no diorama";
        _instance._prep.MapOf = IntPtr.Zero;
        return "the map builds again";
    }

    // Stands every squad as its badge, as a squad with no chibi stands.
    internal static string PrepChibisFromDev(bool shown)
    {
        if (_instance == null)
            return "no diorama";
        _instance._prepChibisOff = !shown;
        return shown ? "chibis shown" : "badges in place of chibis";
    }

    // Puts every squad of the roster on the mission as a player would: each squad's tile is
    // pressed, then vanilla's own button in the unit window. To see the loadout past its limit.
    internal static string PrepDeployAllFromDev() => PrepDeployFromDev(null);

    // Adds the squads not in the mission, or with character tags given (comma separated) adds
    // those and takes every other squad out, through the squad window's own button.
    internal static string PrepDeployFromDev(string only)
    {
        var wanted = only?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<MissionPrepUIScreen>();
        var selector = screen?.m_UnitSelector;
        var d = _instance;
        if (selector == null || d?._shellRoot == null || d._squadWindow == null)
            return "mission preparation is not open";
        var added = 0;
        foreach (var element in UI.FindAll(d._shellRoot, UiSelector.TypeName(VanillaUi.Elements.PrepUnitSlot)).ToList())
        {
            var slot = element.TryCast<MissionPrepUnitSelectSlot>();
            var leader = slot?.GetLeader();
            if (leader == null || !slot.IsInteractable())
                continue;
            var inMission = selector.GetDeployedEntity(leader) != null;
            if (inMission == (wanted == null || wanted.Contains(Affinity.CharacterTag(leader))))
                continue;
            slot.OnClicked(MouseButton.LeftMouse);
            var button = UI.Find(d._squadWindow, UiSelector.Name(VanillaUi.Elements.UnitWindowButton))?.TryCast<InteractiveElement>();
            if (button == null || !button.IsInteractable())
                continue;
            button.OnClicked(MouseButton.LeftMouse);
            added++;
        }
        return $"pressed the squad window's button for {added} squads";
    }

    // The structures on the open mission's map, by template, with how many tiles each covers and
    // what can be entered (towers, bunkers, forest).
    internal static string PrepStructuresFromDev()
    {
        var preview = UIManager.Get()?.GetActiveScreen()?.TryCast<MissionPrepUIScreen>()?.GetMission()?.GetPreview();
        if (preview == null)
            return "no preview";
        var counts = new Dictionary<string, int>();
        for (var z = 0; z < preview.SizeZ; z++)
            for (var x = 0; x < preview.SizeX; x++)
            {
                var tile = preview.Tiles.GetTile(x, z);
                var template = tile.m_EntityTemplate;
                if (template == null)
                    continue;
                var key = $"{template.name} [{tile.GetEntityType()}, container {template.ContainerType}]";
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        return $"{preview.SizeX}x{preview.SizeZ}\n" + string.Join("\n", counts.OrderByDescending(c => c.Value).Select(c => $"{c.Value,4} {c.Key}"));
    }

    internal static string PrepFlatMapFromDev(bool shown)
    {
        var d = _instance;
        if (d == null)
            return "no diorama";
        d._prepFlatMapShown = shown;
        if (d._shellRoot != null && UI.Find(d._shellRoot, UiSelector.Name(VanillaUi.Elements.MapColumn)) is { } column)
            column.style.opacity = shown ? 0.55f : 0f;
        return shown ? "flat map shown" : "flat map unseen";
    }

    // Points the stand-in pointer at a place on the screen, in pixels from the top left, or lets
    // the real pointer back in with a negative place.
    internal static string PrepPointerFromDev(float x, float y)
    {
        var d = _instance;
        if (d == null)
            return "no diorama";
        d._prepPointerOverride = x < 0f ? null : new Vector2(x, Screen.height - y);
        // The real pointer is sent there too, through the input system, so vanilla's own
        // reading of the mouse is what gets driven.
        if (x >= 0f)
            QueueMouse(new Vector2(x, Screen.height - y));
        return x < 0f ? "real pointer" : $"pointer at ({x}, {y})";
    }

    // Presses or releases the left button as vanilla's own input does: the screen is told what is
    // under the pointer, or is given a deployed squad by its number to pick up wherever it stands.
    internal static string PrepPressFromDev(bool down, int squad)
    {
        var d = _instance;
        var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<MissionPrepUIScreen>();
        if (d?._shellRoot?.panel == null || screen == null)
            return "mission preparation is not open";
        var real = RealPointer();
        var at = RuntimePanelUtils.ScreenToPanel(d._shellRoot.panel, new Vector2(real.x, Screen.height - real.y));
        VisualElement raw = null;
        InteractiveElement hovered = null;
        var deployed = screen.m_Deployed;
        for (var i = 0; deployed != null && i < deployed.childCount && hovered == null; i++)
        {
            var entity = deployed.ElementAt(i).TryCast<MissionPrepDeployedEntity>();
            var pickable = entity?.GetPickableElement();
            if (entity != null && (squad == i || (squad < 0 && pickable != null && pickable.worldBound.Contains(at))))
                (hovered, raw) = (entity, pickable);
        }
        if (hovered == null)
        {
            raw = d._shellRoot.panel.Pick(at);
            for (var e = raw; e != null && hovered == null; e = e.parent)
                hovered = e.TryCast<InteractiveElement>();
        }
        if (down)
            screen.OnPressStart(MouseButton.LeftMouse, hovered, raw);
        else
            screen.OnPressEnd(MouseButton.LeftMouse, hovered, raw);
        return $"{(down ? "pressed" : "released")} at panel ({at.x:0}, {at.y:0}) over {(hovered != null ? hovered.GetIl2CppType().Name : "nothing")}; "
            + $"dragged {(screen.m_DraggedEntity != null ? "yes" : "no")}";
    }

    private static bool _devLeftHeld;

    // The real mouse's state as the input system holds it: where it is, and the left button.
    private static string QueueMouse(Vector2 position)
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null)
            return "no mouse";
        try
        {
            var state = new UnityEngine.InputSystem.LowLevel.MouseState { position = position };
            state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, _devLeftHeld);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, state);
            return $"mouse at {position}, left button {(_devLeftHeld ? "held" : "up")}";
        }
        catch (Exception ex)
        {
            return $"could not queue: {ex.GetType().Name}: {ex.Message}";
        }
    }

    // Holds or lets go of the real left button, where the pointer is.
    internal static string MouseButtonFromDev(bool down)
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null)
            return "no mouse";
        _devLeftHeld = down;
        return QueueMouse(mouse.position.ReadValue());
    }

    internal static string PrepStateFromDev()
    {
        var d = _instance;
        var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<MissionPrepUIScreen>();
        if (d == null || screen == null)
            return "mission preparation is not open";
        var mouse = UnityEngine.InputSystem.Mouse.current;
        var lines = new List<string>
        {
            $"left button held {(mouse != null && mouse.leftButton.isPressed)}",
            $"vanilla's pointer {RealPointer()}, input system's {(mouse != null ? mouse.position.ReadValue().ToString() : "no mouse")}, stand-in {d._prepPointerOverride?.ToString() ?? "none"}, tile {d._prep.PointerTile?.ToString("0.00") ?? "none"}",
            $"dragged {(screen.m_DraggedEntity != null ? "yes" : "no")}, now {screen.m_DragCurrentPos}, from {screen.m_DragStartPos}, valid {screen.m_IsValidDragPos}",
        };
        if (screen.m_Deployed != null)
            for (var i = 0; i < screen.m_Deployed.childCount; i++)
                if (screen.m_Deployed.ElementAt(i).TryCast<MissionPrepDeployedEntity>() is { } squad)
                    lines.Add($"squad {i}: tile {squad.GetEntity()?.GetTilePosition()}, element at {squad.layout.center}");
        return string.Join("\n", lines);
    }

    internal static string PrepFlatFromDev(bool flat, float held)
    {
        var d = _instance;
        if (d == null)
            return "no diorama";
        d._prep.Flat = flat;
        d._prepFlatHeld = held < 0f ? null : held;
        return $"{(flat ? "flat" : "hologram")} view{(held < 0f ? "" : $", held at {held:0.00}")}, now at {d._prep.FlatAmount:0.00}, dressed {d._prepFlatDressings} times, shot {(d._prep.FlatShot is { } shot ? $"fov {shot.Fov:0.0}" : "none")}";
    }
}
