using System.Globalization;
using System.IO;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Player-facing preferences, read from UserData/womenace-settings at load. Not
// campaign state: these are per-player, persist across every save, and touch
// presentation only, so they live beside the loader's jiangyu-flags in
// UserData and speak the same grammar: one `key=value` per line, blank lines
// and # comments ignored, keys case-insensitive.
//
// A missing file is every default, and the defaults are written back on first
// run so a player looking for a knob finds a file naming it with its current
// value. A key added in a later version is appended to an existing file the
// same way. An unreadable file logs and keeps the defaults rather than
// failing the mod.
public sealed class PlayerSettingsSystem : JiangyuSystem
{
    // Mouse feel inside Cheyanne's aim trainer, multiplying the raw mouse
    // delta. Raw desktop speed reads far too fast for a scoped rifle.
    private const float AimSensitivityDefault = 0.4f;
    public static float AimSensitivity { get; private set; } = AimSensitivityDefault;

    private const string AimSensitivityKey = "cheyanne-ssr-sens";
    private static readonly string AimSensitivityBlock =
        "\n# Mouse sensitivity in Cheyanne's SSR aim trainer. 1.0 is raw desktop speed.\n"
        + FormattableString.Invariant($"{AimSensitivityKey}={AimSensitivityDefault}\n");

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

    private static bool ReadBool(string path, string key, bool fallback)
    {
        var value = Find(path, key);
        if (value == null)
            return fallback;
        return !value.Equals("false", System.StringComparison.OrdinalIgnoreCase)
            && value != "0" && !value.Equals("off", System.StringComparison.OrdinalIgnoreCase);
    }

    private static float ReadFloat(string path, string key, float fallback)
    {
        var value = Find(path, key);
        return value != null
            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0f
            ? parsed
            : fallback;
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
            {
                outlines = ReadBool(path, "outlines", outlines);
                if (Find(path, AimSensitivityKey) == null)
                    File.AppendAllText(path, AimSensitivityBlock);
                AimSensitivity = ReadFloat(path, AimSensitivityKey, AimSensitivity);
            }
            else
            {
                File.WriteAllText(path,
                    "# WOMENACE player settings. One key=value per line.\n"
                    + "\n"
                    + "# The cel outline on dolls, the mech and vehicles. false turns it off.\n"
                    + "outlines=true\n"
                    + AimSensitivityBlock);
            }
        }
        catch (System.Exception e)
        {
            Log.Warn($"player settings unreadable, using defaults: {e.Message}");
        }

        Shader.SetGlobalFloat("_WomenaceOutlinesOff", outlines ? 0f : 1f);
        Log.Info($"player settings: outlines {(outlines ? "on" : "off")}, aim sensitivity {AimSensitivity} ({path})");
    }
}
