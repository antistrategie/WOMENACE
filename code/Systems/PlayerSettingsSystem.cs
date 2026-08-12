using System.IO;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Player-facing visual preferences, read from UserData/womenace-settings at
// load. Not campaign state: these are per-player, persist across every save,
// and touch rendering only, so they live beside the loader's jiangyu-flags in
// UserData and speak the same grammar: one `key=value` per line, blank lines
// and # comments ignored, keys case-insensitive.
//
// A missing file is every default, and the defaults are written back on first
// run so a player looking for the knob finds a file naming it with its current
// value. An unreadable file logs and keeps the defaults rather than failing
// the mod.
public sealed class PlayerSettingsSystem : JiangyuSystem
{
    private static bool ReadBool(string path, string key, bool fallback)
    {
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var separator = line.IndexOf('=');
            if (separator < 0) continue;
            if (!line.Substring(0, separator).Trim().Equals(key, System.StringComparison.OrdinalIgnoreCase))
                continue;
            var value = line.Substring(separator + 1).Trim();
            return !value.Equals("false", System.StringComparison.OrdinalIgnoreCase)
                && value != "0" && !value.Equals("off", System.StringComparison.OrdinalIgnoreCase);
        }
        return fallback;
    }

    public override void OnInit()
    {
        var path = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "UserData", "womenace-settings"));
        // The doll and mech contour. Applied as the inverted shader global
        // _WomenaceOutlinesOff, which the outline hull collapses on, so an
        // environment that never runs this code shows outlines unchanged.
        bool outlines = true;
        try
        {
            if (File.Exists(path))
                outlines = ReadBool(path, "outlines", outlines);
            else
                File.WriteAllText(path,
                    "# WOMENACE player settings. One key=value per line.\n"
                    + "\n"
                    + "# The cel outline on dolls, the mech and vehicles. false turns it off.\n"
                    + "outlines=true\n");
        }
        catch (System.Exception e)
        {
            Log.Warn($"player settings unreadable, using defaults: {e.Message}");
        }

        Shader.SetGlobalFloat("_WomenaceOutlinesOff", outlines ? 0f : 1f);
        Log.Info($"player settings: outlines {(outlines ? "on" : "off")} ({path})");
    }
}
