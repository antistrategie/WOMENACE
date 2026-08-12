using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Dev verbs for the player settings, so a value can be flipped on a running game
// without editing UserData/WOMENACE.settings.json and restarting. The flip is
// session-only: the file, read at load by PlayerSettingsSystem, stays the truth.
[DevVerb]
public static class PlayerSettings
{
    [MutatingVerb]
    public static object Outlines(bool on = true)
    {
        Shader.SetGlobalFloat("_WomenaceOutlinesOff", on ? 0f : 1f);
        return new { outlines = on, note = "session only, the settings file decides at next load" };
    }
}
