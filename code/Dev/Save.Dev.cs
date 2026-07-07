using Il2CppMenace.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for driving saves over the dev-loader bridge, e.g.
// {verb: "Save.LoadLatest", mutate: true}. The bridge and verb runner live only in the dev
// loader, so these are unreachable in a shipped mod, and the *.Dev.cs name keeps them out of
// release builds.
[DevVerb]
public static class Save
{
    // Load the most recent save game, as the title screen's Continue would.
    [MutatingVerb]
    public static object LoadLatest()
    {
        if (!SaveSystem.TryGetLatestSaveState(out var state) || state == null)
            return new { error = "no save states found" };
        SaveSystem.Load(state);
        return new { ok = true, loading = state.GetFilePath() };
    }

    // The save states on disk, newest first, for picking a specific one.
    public static object List()
    {
        var states = SaveSystem.GetSortedSaveStates();
        var result = new List<object>();
        for (var i = 0; states != null && i < states.Count; i++)
        {
            var s = states[i];
            if (s != null)
                result.Add(new { path = s.GetFilePath(), name = s.SaveGameName });
        }
        return result;
    }
}
