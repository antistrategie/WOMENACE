using System.Globalization;
using System.IO;
using Jiangyu.Sdk;
using MelonLoader;
using UnityEngine;

namespace WOMENACE.Code;

// Player-facing preferences, kept in MelonLoader's own preferences file
// (UserData/MelonPreferences.cfg) under a [WOMENACE] heading. Not campaign
// state: these are per-player, persist across every save, and touch
// presentation only.
//
// MelonLoader writes each entry with its default the first time it is made,
// so a player looking for a knob finds it in the file with its current value
// and a line saying what it does. A value the game changes itself (a view the
// player chose) is written back as it changes.
//
// A UserData/womenace-settings file, one `key=value` per line, is read once,
// its values carried over, and removed.
public sealed class PlayerSettingsSystem : JiangyuSystem
{
    // Mouse feel inside Cheyanne's aim trainer, multiplying the raw mouse
    // delta. Raw desktop speed reads far too fast for a scoped rifle.
    private const float AimSensitivityDefault = 0.4f;
    public static float AimSensitivity => _aimSensitivity is { Value: > 0f } entry ? entry.Value : AimSensitivityDefault;

    // Whether mission preparation opens on vanilla's flat map in place of the
    // hologram. The player chooses with the view's own button.
    public static bool DeploymentFlatView
    {
        get => _deploymentFlatView?.Value ?? false;
        set
        {
            if (_deploymentFlatView == null || _deploymentFlatView.Value == value)
                return;
            _deploymentFlatView.Value = value;
            _category?.SaveToFile(false);
        }
    }

    private static MelonPreferences_Category _category;
    private static MelonPreferences_Entry<bool> _outlines, _deploymentFlatView;
    private static MelonPreferences_Entry<float> _aimSensitivity;

    private static string Find(string path, string key)
    {
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var separator = line.IndexOf('=');
            if (separator < 0) continue;
            if (!line.Substring(0, separator).Trim().Equals(key, System.StringComparison.OrdinalIgnoreCase))
                continue;
            return line.Substring(separator + 1).Trim();
        }
        return null;
    }

    // Carries a womenace-settings file's values into the preferences and removes it.
    private void CarryOver(string path)
    {
        if (!File.Exists(path))
            return;
        if (Find(path, "outlines") is { } outlines)
            _outlines.Value = !outlines.Equals("false", System.StringComparison.OrdinalIgnoreCase)
                && outlines != "0" && !outlines.Equals("off", System.StringComparison.OrdinalIgnoreCase);
        if (Find(path, "cheyanne-ssr-sens") is { } sensitivity
            && float.TryParse(sensitivity, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0f)
            _aimSensitivity.Value = parsed;
        _category.SaveToFile(false);
        File.Delete(path);
        Log.Info($"player settings: carried {path} over to MelonPreferences.cfg");
    }

    public override void OnInit()
    {
        // The doll and mech contour. Applied as the inverted shader global
        // _WomenaceOutlinesOff, which the outline hull collapses on, so an
        // environment that never runs this code shows outlines unchanged.
        var outlines = true;
        try
        {
            _category = MelonPreferences.CreateCategory("WOMENACE");
            _outlines = _category.CreateEntry("Outlines", true, "Outlines",
                "The cel outline on dolls, the mech and vehicles. false turns it off.");
            _aimSensitivity = _category.CreateEntry("CheyanneSsrSensitivity", AimSensitivityDefault, "Cheyanne SSR sensitivity",
                "Mouse sensitivity in Cheyanne's SSR aim trainer. 1.0 is raw desktop speed, lower is steadier.");
            _deploymentFlatView = _category.CreateEntry("DeploymentFlatView", false, "Deployment flat view",
                "Deployment opens on the flat 2D map in place of the hologram. Set by the view's button on that screen.");
            CarryOver(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserData", "womenace-settings")));
            outlines = _outlines.Value;
        }
        catch (System.Exception e)
        {
            Log.Warn($"player settings unreadable, using defaults: {e.Message}");
        }

        Shader.SetGlobalFloat("_WomenaceOutlinesOff", outlines ? 0f : 1f);
        Log.Info($"player settings: outlines {(outlines ? "on" : "off")}, aim sensitivity {AimSensitivity}, deployment {(DeploymentFlatView ? "flat" : "hologram")} view");
    }
}
