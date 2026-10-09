using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Live inspection and tuning for the spring-bone rigs.
//
//   scripts/bridge.py verb Springs.Status
//   scripts/bridge.py verb Springs.Scan --mutate                         rescan every Animator now
//   scripts/bridge.py verb Springs.MaxLod --args '[2]' --mutate          simulate bodies drawn at LOD2 or closer
//   scripts/bridge.py verb Springs.Kick --args '[0.02]' --mutate         shove every particle sideways
//   scripts/bridge.py verb Springs.Measure --args '[180]'                sample tip jitter, read it in Status
//   scripts/bridge.py verb Springs.Record --args '[3000]'                keep the last frames in a ring buffer
//   scripts/bridge.py verb Springs.Record --args '[20000, "ots14"]'      only rigs whose LOD0 name starts with "ots14"
//   scripts/bridge.py verb Springs.Dump                                  write the buffer to CSV in UserData/womenace-springs
//   scripts/bridge.py verb Springs.Set --args '["twintails", "Stiffness", 1.2]' --mutate
//
// Set writes the shared chain spec, so it retunes every live rig at once and
// lasts until the game restarts. Copy settled values into SpringPresets or the
// outfit's file under Profiles/.
[DevVerb]
public static class Springs
{
    public static object Status()
    {
        var system = SpringBoneSystem.Instance;
        if (system == null)
            return new { error = "spring system not initialised" };
        if (system.Rigs.Count == 0)
            return "no spring rigs live";
        var rows = new List<string> { $"solver {system.TickMilliseconds:F2} ms per frame across {system.Rigs.Count} rig(s)" };
        foreach (var rig in system.Rigs)
        {
            if (rig.IsDead)
            {
                rows.Add("(dead rig awaiting removal)");
                continue;
            }
            rows.Add($"{Path(rig.Root)} profile={rig.Profile.LodMesh} lod={rig.Lod} simulating={rig.Simulating} chains={rig.ChainCount} joints={rig.JointCount}");
            foreach (var line in rig.Describe())
                rows.Add("  " + line);
            var report = rig.SampleReport();
            if (report != null)
                rows.Add(report);
        }
        return string.Join("\n", rows);
    }

    // Bodies drawn at this LOD or closer simulate.
    [MutatingVerb]
    public static object MaxLod(int lod)
    {
        SpringBoneSystem.MaxSimulatedLod = lod;
        return new { maxSimulatedLod = lod };
    }

    [MutatingVerb]
    public static object Scan()
    {
        var system = SpringBoneSystem.Instance;
        return system == null ? new { error = "spring system not initialised" } : new { added = system.Scan() };
    }

    // Samples tip jitter over the next frames, read back through Status.
    public static object Measure(int frames = 180)
    {
        var system = SpringBoneSystem.Instance;
        if (system == null)
            return new { error = "spring system not initialised" };
        var started = 0;
        foreach (var rig in system.Rigs)
        {
            if (rig.IsDead)
                continue;
            rig.StartSampling(frames);
            started++;
        }
        return new { started, frames };
    }

    // Starts the flight recorder on every live rig, keeping the last `frames`
    // frames, and without a profile filter on every rig built later this
    // session too. Dump writes them to CSV in UserData/womenace-springs, outside
    // the mod folder, since a deploy deletes the mod folder whole.
    public static object Record(int frames = 3000, string profile = null)
    {
        var system = SpringBoneSystem.Instance;
        if (system == null)
            return new { error = "spring system not initialised" };
        system.RecordFrames = profile == null ? frames : 0;
        var started = 0;
        foreach (var rig in system.Rigs)
        {
            if (rig.IsDead || (profile != null && !rig.Profile.LodMesh.StartsWith(profile, StringComparison.Ordinal)))
                continue;
            rig.StartRecording(frames);
            started++;
        }
        return new { started, frames };
    }

    public static object Dump()
    {
        var system = SpringBoneSystem.Instance;
        if (system == null)
            return new { error = "spring system not initialised" };
        var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            UnityEngine.Application.dataPath, "..", "UserData", "womenace-springs"));
        System.IO.Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var files = new List<string>();
        var index = 0;
        foreach (var rig in system.Rigs)
        {
            if (rig.IsDead || !rig.Recording)
                continue;
            var path = System.IO.Path.Combine(folder, $"springs_record_{stamp}_{index++}.csv");
            files.Add($"{path} ({rig.DumpRecording(path)} frames)");
        }
        return files.Count == 0 ? "nothing recording" : string.Join("\n", files);
    }

    [MutatingVerb]
    public static object Kick(float metresPerStep = 0.02f)
    {
        var system = SpringBoneSystem.Instance;
        if (system == null)
            return new { error = "spring system not initialised" };
        var kicked = 0;
        foreach (var rig in system.Rigs)
        {
            if (rig.IsDead)
                continue;
            rig.Kick(rig.Root.right * metresPerStep);
            kicked++;
        }
        return new { kicked };
    }

    // Spec fields read only when a rig is built: changing them live would
    // leave every rig built before the change out of step with its spec.
    // LegDrive is tunable, but whether it is above zero decides layering and
    // splits at build, so crossing zero live does not add or drop them.
    private static readonly HashSet<string> BuildFields = new(StringComparer.Ordinal)
    {
        nameof(SpringChainSpec.Name), nameof(SpringChainSpec.Roots),
        nameof(SpringChainSpec.Anchor), nameof(SpringChainSpec.HangDown),
    };

    // Sets any tunable spec field: numbers as given, flags as 0 or 1, and
    // Colliders as the group flags (Body=2 Legs=4 Pelvis=8 Seat=16).
    [MutatingVerb]
    public static object Set(string chain, string field, float value)
    {
        var member = typeof(SpringChainSpec).GetField(field);
        if (member == null || BuildFields.Contains(field))
        {
            var tunable = typeof(SpringChainSpec).GetFields().Select(f => f.Name).Where(n => !BuildFields.Contains(n));
            return new { error = $"'{field}' cannot be set live. Tunable fields: {string.Join(", ", tunable)}" };
        }
        object parsed = member.FieldType == typeof(bool) ? value != 0f
            : member.FieldType == typeof(SpringColliderGroup) ? (SpringColliderGroup)(int)value
            : value;
        var changed = new List<string>();
        foreach (var profile in SpringBodyProfiles.All)
        {
            foreach (var spec in profile.Chains)
            {
                if (spec.Name != chain)
                    continue;
                member.SetValue(spec, parsed);
                changed.Add($"{profile.LodMesh}/{spec.Name}.{field} = {parsed}");
            }
        }
        return changed.Count == 0 ? new { error = $"no chain named '{chain}'" } : string.Join("\n", changed);
    }

    private static string Path(Transform t)
    {
        var path = t.name;
        for (var p = t.parent; p != null; p = p.parent)
            path = p.name + "/" + path;
        return path;
    }
}
