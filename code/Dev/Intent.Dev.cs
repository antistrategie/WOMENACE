using Il2CppMenace.Tactical;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Dev verbs over Star Rail Insight (StartrailInsightSystem), for testing the read without
// waiting for an allied turn to start.
//   {"verb":"Intent.Peek","args":[8],"mutate":true}        read, with diagnostics
//   {"verb":"Intent.ShowGhosts","args":[8],"mutate":true}  read and show the vision
//   {"verb":"Intent.ClearGhosts","args":[],"mutate":true}
// The anchor is Nemesis when she is deployed, otherwise the first player unit.
[DevVerb]
public static class Intent
{
    [MutatingVerb]
    public static string Peek(int count = 8) => string.Join("\n", Read(count, out _));

    [MutatingVerb]
    public static string ShowGhosts(int count = 8)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var log = Read(count, out var visions);
        var readMs = clock.Elapsed.TotalMilliseconds;
        var spawned = StartrailInsightSystem.Show(visions);
        log.Insert(0, $"read {readMs:F0} ms, show {clock.Elapsed.TotalMilliseconds - readMs:F0} ms, {spawned} vision object(s)");
        return string.Join("\n", log);
    }

    [MutatingVerb]
    public static string ClearGhosts()
    {
        StartrailInsightSystem.ClearAll();
        return "cleared";
    }

    private static List<string> Read(int count, out List<Vision> visions)
    {
        var log = new List<string>();
        visions = new List<Vision>();
        var player = TacticalManager.Get()?.GetPlayerFaction();
        if (player == null)
        {
            log.Add("no tactical mission");
            return log;
        }
        Actor anchor = null;
        foreach (var unit in player.GetActors())
            if (unit != null && (anchor == null || unit.GetTemplate()?.GetID() == "player_squad.nemesis"))
                anchor = unit;
        if (anchor == null)
        {
            log.Add("no player units");
            return log;
        }
        visions = StartrailInsightSystem.Read(anchor.GetPos(), count, 0.25f, log);
        return log;
    }
}
