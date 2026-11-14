using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;

namespace WOMENACE.Code;

// Hooks for the navigation dev verbs (code/Dev/Diorama.Dev.cs). *.Dev.cs files stay out of release builds.
internal static partial class NavigationMenu
{
    // Dev: the destinations and whether each is available right now.
    internal static string Describe()
    {
        var navigation = FindNavigation();
        return navigation == null ? "no tab bar on the active screen"
            : string.Join(", ", Destinations.Select(d => $"{d.Target}={(IsAvailable(d.Target, navigation) ? "on" : "off")}"));
    }

    internal static string GoFromDev(string target) =>
        Enum.TryParse<Target>(target, true, out var t) ? GoAndReport(t) : "unknown target";

    private static string GoAndReport(Target t)
    {
        var navigation = FindNavigation();
        var button = ButtonFor(t, navigation);
        var found = $"bar {(navigation != null)}, button {(button != null ? button.name : "none")}";
        Go(t);
        return found + ", " + ReportScreen(t);
    }

    private static string ReportScreen(Target t)
    {
        return "went " + t + ", active screen now " + UIManager.Get()?.GetActiveScreen()?.GetIl2CppType().Name;
    }
}
