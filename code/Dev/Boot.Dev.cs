using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for the boot sequence.
[DevVerb]
public static class Boot
{
    // Leaves the opening film for the title screen, as pressing a key does.
    [MutatingVerb]
    public static object Skip()
    {
        var splash = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.TryCast<Il2CppMenace.UI.Menu.SplashUIScreen>();
        if (splash == null)
            return new { ok = false };
        splash.ContinueToTitleScreen();
        return new { ok = true };
    }
}
