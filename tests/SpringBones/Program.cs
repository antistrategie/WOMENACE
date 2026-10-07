using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using WOMENACE.Code;

namespace WOMENACE.SpringBones.Tests;

// Replays recorded full-body motion through the spring solver for every
// outfit profile, against each outfit's own baked skeleton, and reports per
// chain how jittery, folded, sunk into the body and lively it was.
//
//   dotnet run --project tests/SpringBones -- <recording.csv> <source outfit> [outfit filter] [--set chain.Field=value ...]
//
// --set retunes every chain spec with that name before the run, the way the
// Springs.Set dev verb does live, e.g. --set skirt.Follow=0.7.
// The recording is a Springs.Dump CSV with the humanoid pose columns, taken
// on the source outfit (e.g. ots14/default). Its motion is retargeted onto
// each outfit by carrying each humanoid bone's rotation away from its bind
// pose, in the root's space. Every Doll is T-posed against the same reference
// avatar by the PMX conversion, so those rotations mean the same thing on
// every skeleton.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: <recording.csv> <source outfit, e.g. ots14/default> [outfit filter] [--set chain.Field=value ...]");
            return 2;
        }
        var repo = FindRepo();
        var clip = Clip.Load(args[0]);
        var source = Skeleton.Load(PrefabPath(repo, args[1]));
        var filter = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : null;
        for (var a = 0; a < args.Length - 1; a++)
            if (args[a] == "--set")
                Override(args[a + 1]);
        Console.WriteLine($"clip: {clip.Frames.Count} frames, {clip.Frames.Sum(f => f.Dt):F1} s, source {args[1]}");

        var failures = 0;
        foreach (var profile in SpringBodyProfiles.All.OrderBy(p => p.LodMesh, StringComparer.Ordinal))
        {
            var outfit = FindOutfit(repo, profile);
            if (outfit == null)
            {
                Console.WriteLine($"{profile.LodMesh}: no baked prefab carries this profile's bones, skipped");
                continue;
            }
            if (filter != null && !outfit.Contains(filter, StringComparison.Ordinal))
                continue;
            var skeleton = Skeleton.Load(PrefabPath(repo, outfit));
            var report = Replay.Run(clip, source, skeleton, profile);
            failures += report.Print(outfit);
        }
        return failures == 0 ? 0 : 1;
    }

    private static void Override(string assignment)
    {
        var dot = assignment.IndexOf('.');
        var eq = assignment.IndexOf('=');
        var chain = assignment[..dot];
        var field = assignment[(dot + 1)..eq];
        var value = assignment[(eq + 1)..];
        var member = typeof(SpringChainSpec).GetField(field) ?? throw new ArgumentException($"no field {field}");
        foreach (var spec in SpringBodyProfiles.All.SelectMany(p => p.Chains).Where(c => c.Name == chain))
        {
            // As Springs.Set takes them: numbers, a bool as non-zero, the
            // collider groups as their flags' sum.
            var number = float.Parse(value, CultureInfo.InvariantCulture);
            object parsed = member.FieldType == typeof(SpringColliderGroup)
                ? (SpringColliderGroup)(int)number
                : member.FieldType == typeof(bool)
                    ? number != 0f
                    : number;
            member.SetValue(spec, parsed);
        }
        Console.WriteLine($"set {assignment}");
    }

    private static string FindRepo()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "WOMENACE.slnx")))
                return dir.FullName;
        throw new InvalidOperationException("run from inside the WOMENACE repository");
    }

    private static string PrefabPath(string repo, string outfit)
        => Path.Combine(repo, "unity", "Assets", "Prefabs", outfit, "main.prefab");

    // The outfit whose baked prefab the profile matches, as the live rig
    // matches it (SpringBodyProfiles.Match).
    private static Dictionary<SpringBodyProfile, string> _outfits;

    private static string FindOutfit(string repo, SpringBodyProfile profile)
    {
        if (_outfits == null)
        {
            _outfits = new Dictionary<SpringBodyProfile, string>();
            var prefabs = Path.Combine(repo, "unity", "Assets", "Prefabs");
            foreach (var path in Directory.GetFiles(prefabs, "main.prefab", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                var names = Regex.Matches(File.ReadAllText(path), @"m_Name: (.*)").Select(m => m.Groups[1].Value.Trim()).ToHashSet(StringComparer.Ordinal);
                var match = SpringBodyProfiles.Match(names);
                if (match != null)
                    _outfits.TryAdd(match, Path.GetRelativePath(prefabs, Path.GetDirectoryName(path)!).Replace('\\', '/'));
            }
        }
        return _outfits.GetValueOrDefault(profile);
    }

}

// One frame of recorded motion: the root in world space and each humanoid
// bone in the root's space.
internal sealed class Frame
{
    public float Dt;
    public Vector3 RootPosition;
    public Quaternion RootRotation;
    public Dictionary<string, (Vector3 Position, Quaternion Rotation)> Bones = new(StringComparer.Ordinal);
}

internal sealed class Clip
{
    internal static string[] HumanoidBones => SpringChainBuilder.HumanoidBones;

    public List<Frame> Frames = new();

    internal static Clip Load(string path)
    {
        var lines = File.ReadAllLines(path);
        var header = lines[0].Split(',');
        var column = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++)
            column[header[i]] = i;
        var posed = column.ContainsKey("Hips_qw");
        if (!posed)
            Console.WriteLine($"warning: {path} has no humanoid pose columns, replaying root travel on a bind-posed body");
        var clip = new Clip();
        for (var l = 1; l < lines.Length; l++)
        {
            if (lines[l].Length == 0)
                continue;
            var cells = lines[l].Split(',');
            float F(string name) => float.Parse(cells[column[name]], CultureInfo.InvariantCulture);
            var frame = new Frame
            {
                Dt = F("dt"),
                RootPosition = new Vector3(F("root_x"), F("root_y"), F("root_z")),
                RootRotation = column.ContainsKey("root_qw")
                    ? Quaternion.Normalize(new Quaternion(F("root_qx"), F("root_qy"), F("root_qz"), F("root_qw")))
                    : Quaternion.Identity,
            };
            foreach (var bone in posed ? HumanoidBones : Array.Empty<string>())
            {
                var q = new Quaternion(F(bone + "_qx"), F(bone + "_qy"), F(bone + "_qz"), F(bone + "_qw"));
                if (q.LengthSquared() < 0.5f)
                    continue;
                frame.Bones[bone] = (new Vector3(F(bone + "_px"), F(bone + "_py"), F(bone + "_pz")), Quaternion.Normalize(q));
            }
            clip.Frames.Add(frame);
        }
        return clip;
    }
}

// A baked prefab's transform hierarchy, read straight from its YAML.
internal sealed class Skeleton
{
    public List<string> Names = new();
    public List<int> Parents = new();
    public List<Vector3> LocalPositions = new();
    public List<Quaternion> LocalRotations = new();
    public List<int> FirstChildren = new();
    public SpringBindPose Bind;

    public Dictionary<string, int> Index => Bind.Index;

    // Bind pose in the root's space.
    public Vector3[] BindPositions => Bind.Positions;
    public Quaternion[] BindRotations => Bind.Rotations;

    private static readonly Regex Document = new(@"^--- !u!(\d+) &(-?\d+)", RegexOptions.Multiline);

    internal static Skeleton Load(string path)
    {
        var text = File.ReadAllText(path);
        var matches = Document.Matches(text);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var transforms = new List<(string Id, string GameObject, string Father, Vector3 Position, Quaternion Rotation, string FirstChild)>();
        for (var m = 0; m < matches.Count; m++)
        {
            var start = matches[m].Index;
            var end = m + 1 < matches.Count ? matches[m + 1].Index : text.Length;
            var body = text[start..end];
            var type = matches[m].Groups[1].Value;
            var id = matches[m].Groups[2].Value;
            if (type == "1")
                names[id] = Regex.Match(body, @"m_Name: (.*)").Groups[1].Value.Trim();
            else if (type == "4")
            {
                var go = Regex.Match(body, @"m_GameObject: \{fileID: (-?\d+)\}").Groups[1].Value;
                var father = Regex.Match(body, @"m_Father: \{fileID: (-?\d+)\}").Groups[1].Value;
                var p = Regex.Match(body, @"m_LocalPosition: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}");
                var r = Regex.Match(body, @"m_LocalRotation: \{x: ([^,]+), y: ([^,]+), z: ([^,]+), w: ([^}]+)\}");
                // The first entry under m_Children: Unity's GetChild(0).
                var child = Regex.Match(body, @"m_Children:\s*\n\s*-\s*\{fileID: (-?\d+)\}");
                float G(Match match, int g) => float.Parse(match.Groups[g].Value, CultureInfo.InvariantCulture);
                transforms.Add((id, go, father,
                    new Vector3(G(p, 1), G(p, 2), G(p, 3)),
                    Quaternion.Normalize(new Quaternion(G(r, 1), G(r, 2), G(r, 3), G(r, 4))),
                    child.Success ? child.Groups[1].Value : null));
            }
        }

        var skeleton = new Skeleton();
        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        // Parents before children, so forward kinematics runs in index order.
        var pending = transforms.ToList();
        var known = new HashSet<string> { "0" };
        while (pending.Count > 0)
        {
            var progressed = false;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var t = pending[i];
                if (!known.Contains(t.Father))
                    continue;
                var index = skeleton.Names.Count;
                var name = names.GetValueOrDefault(t.GameObject, t.Id);
                skeleton.Names.Add(name);
                skeleton.Parents.Add(t.Father == "0" ? -1 : byId[t.Father]);
                skeleton.LocalPositions.Add(t.Position);
                skeleton.LocalRotations.Add(t.Rotation);
                byId[t.Id] = index;
                known.Add(t.Id);
                pending.RemoveAt(i);
                progressed = true;
            }
            if (!progressed)
                throw new InvalidOperationException($"{path}: transforms with unknown parents");
        }
        var firstChildIds = transforms.ToDictionary(t => t.Id, t => t.FirstChild);
        foreach (var id in byId.OrderBy(kv => kv.Value).Select(kv => kv.Key))
            skeleton.FirstChildren.Add(firstChildIds[id] != null && byId.TryGetValue(firstChildIds[id], out var c) ? c : -1);

        skeleton.Bind = SpringBindPose.Create(skeleton.Names.ToArray(), skeleton.Parents.ToArray(), skeleton.FirstChildren.ToArray(),
            skeleton.LocalPositions.ToArray(), skeleton.LocalRotations.ToArray());
        return skeleton;
    }
}

internal sealed class ChainStats
{
    public string Name;
    public string Category;
    public List<float> Jerk = new();
    public List<float> TargetJerk = new();
    public List<float> Bend = new();
    public List<float> Sink = new();
    public List<float> Drift = new();
    public List<float> LayerClip = new();
    public List<Vector3> Tips = new();
}

internal sealed class Report
{
    public List<ChainStats> Chains = new();

    // SPRING_PER_CHAIN=1 lists every chain, not only a failing group's worst.
    private static readonly bool PerChain = Environment.GetEnvironmentVariable("SPRING_PER_CHAIN") == "1";

    // Thresholds the worst chain must stay under at p99, from the live
    // tuning rounds: approved chains read up to ~15 mm of tip jerk and kink
    // no more than ~70 degrees sharper than their target shape, and a fold
    // is well past that.
    private const float JerkLimit = 25f;
    private const float BendLimit = 100f;
    private const float SinkLimit = 30f;

    internal int Print(string outfit)
    {
        Console.WriteLine();
        Console.WriteLine($"== {outfit}");
        var failures = 0;
        foreach (var group in Chains.GroupBy(c => c.Category))
        {
            float P(IEnumerable<float> values, double q)
            {
                var sorted = values.OrderBy(v => v).ToArray();
                return sorted.Length == 0 ? 0f : sorted[(int)Math.Min(sorted.Length - 1, q * sorted.Length)];
            }
            // The worst chain's p99, not the group's pooled p99: a few bad
            // columns among thirty good ones are what the eye catches.
            var jerk = group.Max(c => P(c.Jerk, 0.99));
            var bend = group.Max(c => P(c.Bend, 0.99));
            var sink = group.Max(c => P(c.Sink, 0.99));
            var drift = group.Max(c => P(c.Drift, 0.99));
            var layered = group.Where(c => c.LayerClip.Count > 0).ToList();
            var layerClip = layered.Count == 0 ? 0f : layered.Max(c => P(c.LayerClip, 0.99));
            var swing = group.Average(c => Swing(c.Tips));
            var bad = jerk > JerkLimit || bend > BendLimit || sink > SinkLimit || layerClip > SinkLimit;
            if (bad)
                failures++;
            Console.WriteLine($"  {(bad ? "FAIL" : "ok  ")} {group.Key,-12} {group.Count(),3} chains | worst jerk p99 {jerk,6:F1} mm | kink p99 {bend,5:F0} deg | sink p99 {sink,5:F1} mm | drift p99 {drift,3:F0} deg | layer p99 {layerClip,4:F1} mm | swing {swing * 100f,5:F1} cm");
            if (PerChain)
            {
                foreach (var c in group)
                    Console.WriteLine($"         {c.Name}: jerk p99 {P(c.Jerk, 0.99):F1}, target jerk p99 {P(c.TargetJerk, 0.99):F1}, kink p99 {P(c.Bend, 0.99):F0}, sink p99 {P(c.Sink, 0.99):F1}, drift p99 {P(c.Drift, 0.99):F0}, layer p99 {P(c.LayerClip, 0.99):F1}, swing {Swing(c.Tips) * 100f:F1} cm");
            }
            else if (bad)
            {
                foreach (var c in group.OrderByDescending(c => P(c.Jerk, 0.99) + P(c.Bend, 0.99) + P(c.Drift, 0.99)).Take(3))
                    Console.WriteLine($"         worst {c.Name}: jerk p99 {P(c.Jerk, 0.99):F1}, kink p99 {P(c.Bend, 0.99):F0}, sink p99 {P(c.Sink, 0.99):F1}, drift p99 {P(c.Drift, 0.99):F0}");
            }
        }
        return failures;
    }

    // How far a chain's tip roams from its average, p95, in metres.
    private static float Swing(List<Vector3> tips)
    {
        if (tips.Count == 0)
            return 0f;
        var mean = tips.Aggregate(Vector3.Zero, (a, b) => a + b) / tips.Count;
        var distances = tips.Select(t => Vector3.Distance(t, mean)).OrderBy(d => d).ToArray();
        return distances[(int)(0.95 * (distances.Length - 1))];
    }
}

internal static class Replay
{
    // SPRING_TRACE=<chain root> prints that chain's per-link bends and pushes
    // for the first SPRING_TRACE_FRAMES frames (default 200).
    private static readonly string Trace = Environment.GetEnvironmentVariable("SPRING_TRACE");
    private static readonly int TraceFrames = int.TryParse(Environment.GetEnvironmentVariable("SPRING_TRACE_FRAMES"), out var f) ? f : 200;

    // SPRING_POSE_FRAMES=<frame,frame,...> with SPRING_POSE_OUT=<file.json>
    // writes every bone's root-space position and rotation at those frames,
    // the bind pose, and each chain's targets, for rendering the skinned mesh
    // as replayed.
    private static readonly HashSet<int> PoseFrames = (Environment.GetEnvironmentVariable("SPRING_POSE_FRAMES") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToHashSet();
    private static readonly string PoseOut = Environment.GetEnvironmentVariable("SPRING_POSE_OUT");

    // SPRING_MEASURE_LEGS=1 also measures leg-following chains that do not
    // collide with the legs against them, to see how far legs pass through.
    private static readonly bool MeasureLegs = Environment.GetEnvironmentVariable("SPRING_MEASURE_LEGS") == "1";

    // SPRING_LAYERS=1 lists which garments were found hanging over which,
    // and the panels either side of a split that let the legs through.
    private static readonly bool ListLayers = Environment.GetEnvironmentVariable("SPRING_LAYERS") == "1";

    private sealed class Chain
    {
        public SpringChain Solver;
        public int Parent;
        public int[] Bones;
        public ChainStats Stats;
        public Vector3 TipPrev;
        public Vector3 TipPrevPrev;
        public Vector3 TargetPrev;
        public Vector3 TargetPrevPrev;
        public int Seen;
    }

    internal static Report Run(Clip clip, Skeleton source, Skeleton target, SpringBodyProfile profile)
    {
        var report = new Report();
        var local = target.LocalRotations.ToArray();
        var localPositions = target.LocalPositions.ToArray();
        var n = target.Names.Count;
        var worldPos = new Vector3[n];
        var worldRot = new Quaternion[n];

        // The body, built and stepped as the live rig builds and steps it.
        var missing = new List<string>();
        var body = new SpringBody(profile, target.Bind, 1f, _ => true, missing);
        var reads = body.Bones.Select(b => target.Index[b]).ToArray();
        var chains = body.Chains.Select(solver => new Chain
        {
            Solver = solver,
            Parent = target.Index[solver.Parent],
            Bones = solver.Bones.Select(b => target.Index[b]).ToArray(),
            Stats = new ChainStats { Name = solver.Root, Category = solver.Spec.Name },
        }).ToList();
        if (missing.Count > 0)
            Console.WriteLine($"unresolved: {string.Join(", ", missing)}");
        if (ListLayers)
        {
            foreach (var c in chains.Where(c => c.Solver.Inner != null))
                Console.WriteLine($"layer {c.Solver.Layer} {c.Stats.Name} over {string.Join(" ", c.Solver.Inner.Select(i => i.Root))}");
            foreach (var c in chains.Where(c => c.Solver.Excluded != SpringColliderGroup.None))
                Console.WriteLine($"split edge {c.Stats.Name}");
            foreach (var c in chains.Where(c => c.Solver.Trunk != null))
                Console.WriteLine($"branch {c.Stats.Name} off {c.Solver.Trunk.Root}");
        }
        report.Chains.AddRange(chains.Select(c => c.Stats));

        // Retargeting: hips height ratio for the root-space hips position.
        var srcHips = source.Index["Hips"];
        var dstHips = target.Index["Hips"];
        var heightRatio = target.BindPositions[dstHips].Y / source.BindPositions[srcHips].Y;
        var humanoid = Clip.HumanoidBones
            .Where(b => source.Index.ContainsKey(b) && target.Index.ContainsKey(b))
            .Select(b => (Name: b, Src: source.Index[b], Dst: target.Index[b]))
            .OrderBy(b => b.Dst)
            .ToArray();

        var hipsIndex = target.Index.GetValueOrDefault("Hips", -1);
        var lastRoot = clip.Frames[0].RootPosition;
        var needsReset = true;
        var poses = new List<string>();
        if (PoseOut != null)
            poses.Add(PoseJson("bind", target, target.BindPositions, target.BindRotations));
        var frameIndex = -1;

        foreach (var frame in clip.Frames)
        {
            frameIndex++;
            // Pose the humanoid bones: each carries its recorded rotation away
            // from the source's bind pose, applied to the target's bind pose.
            for (var i = 0; i < n; i++)
            {
                foreach (var h in humanoid)
                {
                    if (h.Dst != i || target.Parents[i] < 0 || !frame.Bones.TryGetValue(h.Name, out var pose))
                        continue;
                    var parent = target.Parents[i];
                    var world = pose.Rotation * Quaternion.Inverse(source.BindRotations[h.Src]) * target.BindRotations[h.Dst];
                    local[i] = Quaternion.Inverse(worldRot[parent]) * (frame.RootRotation * world);
                    if (h.Name == "Hips")
                        localPositions[i] = Vector3.Transform(frame.RootPosition + Vector3.Transform(pose.Position * heightRatio, frame.RootRotation) - worldPos[parent], Quaternion.Inverse(worldRot[parent]));
                }
                Forward(target, i, local, localPositions, worldPos, worldRot, frame);
            }

            for (var i = 0; i < reads.Length; i++)
            {
                body.Positions[i] = worldPos[reads[i]];
                body.Rotations[i] = worldRot[reads[i]];
            }
            // As the rig, a paused frame neither reads nor steps.
            var dt = frame.Dt;
            if (!needsReset && dt <= 0f)
                continue;
            body.Read();
            var bodyPose = body.Pose;

            if (needsReset)
            {
                body.Reset();
                needsReset = false;
            }
            else
            {
                if (SpringSolver.IsTeleport(lastRoot, frame.RootPosition, dt))
                    body.Shift(frame.RootPosition - lastRoot);
                body.Advance(dt);
            }
            lastRoot = frame.RootPosition;

            // Write the chains back as the rig does, then pose the skeleton.
            foreach (var c in chains)
            {
                if (c.Solver.Mounted)
                    localPositions[c.Bones[0]] = Vector3.Transform(c.Solver.Attach.Origin - worldPos[c.Parent], Quaternion.Inverse(worldRot[c.Parent]));
                for (var k = 0; k < c.Bones.Length; k++)
                    local[c.Bones[k]] = c.Solver.Joints[k].LastLocal;
            }
            for (var i = 0; i < n; i++)
                Forward(target, i, local, localPositions, worldPos, worldRot, frame);

            foreach (var c in chains)
                Measure(c, worldPos, worldRot, body.Colliders, hipsIndex, hipsIndex >= 0 ? target.BindRotations[hipsIndex] : Quaternion.Identity, frame.RootRotation, frame.RootPosition, bodyPose);

            if (PoseOut != null && PoseFrames.Contains(frameIndex))
            {
                var inverseRoot = Quaternion.Inverse(frame.RootRotation);
                var positions = worldPos.Select(p => Vector3.Transform(p - frame.RootPosition, inverseRoot)).ToArray();
                var rotations = worldRot.Select(r => inverseRoot * r).ToArray();
                poses.Add(PoseJson(frameIndex.ToString(CultureInfo.InvariantCulture), target, positions, rotations));
                var targets = chains.Select(c => string.Format(CultureInfo.InvariantCulture, "\"{0}\":[{1}]", c.Stats.Name,
                    string.Join(",", c.Solver.Targets.Select(t => Vector3.Transform(t - frame.RootPosition, inverseRoot)).Select(t => string.Format(CultureInfo.InvariantCulture, "[{0},{1},{2}]", t.X, t.Y, t.Z)))));
                poses.Add($"\"targets_{frameIndex}\":{{{string.Join(",", targets)}}}");
            }
        }
        if (PoseOut != null)
            File.WriteAllText(PoseOut, "{" + string.Join(",", poses) + "}");
        return report;
    }

    // One bone's world pose from its parent's, the root placed by the frame.
    private static void Forward(Skeleton s, int i, Quaternion[] local, Vector3[] localPositions, Vector3[] worldPos, Quaternion[] worldRot, Frame frame)
    {
        var parent = s.Parents[i];
        if (parent < 0)
        {
            worldRot[i] = frame.RootRotation;
            worldPos[i] = frame.RootPosition;
            return;
        }
        worldRot[i] = worldRot[parent] * local[i];
        worldPos[i] = worldPos[parent] + Vector3.Transform(localPositions[i], worldRot[parent]);
    }

    private static string PoseJson(string key, Skeleton s, Vector3[] positions, Quaternion[] rotations)
    {
        var bones = new List<string>();
        for (var i = 0; i < s.Names.Count; i++)
        {
            var p = positions[i];
            var r = rotations[i];
            bones.Add(string.Format(CultureInfo.InvariantCulture, "\"{0}\":[{1},{2},{3},{4},{5},{6},{7}]", s.Names[i], p.X, p.Y, p.Z, r.X, r.Y, r.Z, r.W));
        }
        return $"\"{key}\":{{{string.Join(",", bones)}}}";
    }

    private static void Measure(Chain c, Vector3[] worldPos, Quaternion[] worldRot, List<SpringCollider> colliders, int hips, Quaternion bindHips, Quaternion rootRotation, Vector3 rootPosition, in SpringBodyPose bodyPose)
    {
        // Layer clip: how deep this chain's particles sit inside the garment
        // under it, in millimetres, beyond the gap kept between them. The
        // solver holds them out, so this reads what the hold leaves.
        if (c.Solver.Inner != null && bodyPose.Valid)
        {
            var clip = 0f;
            foreach (var j in c.Solver.Joints)
                clip = Math.Max(clip, SpringSolver.LayerClip(c.Solver.Inner, j.Tail, bodyPose, 1f));
            c.Stats.LayerClip.Add(clip * 1000f);
        }

        var joints = c.Solver.Joints;
        // In the frame of the bone the chain hangs from. A hanging chain is
        // measured from the body's root instead: it hangs from the world, and
        // the arm snaps in the aim animations are not the cloth's doing.
        var hanging = c.Solver.Spec.HangDown;
        var inverseParent = Quaternion.Inverse(hanging ? rootRotation : worldRot[c.Parent]);
        var measureFrom = hanging ? rootPosition : worldPos[c.Parent];
        var tip = Vector3.Transform(joints[^1].Tail - measureFrom, inverseParent);
        if (c.Seen >= 2)
            c.Stats.Jerk.Add((tip - 2f * c.TipPrev + c.TipPrevPrev).Length() * 1000f);
        c.TipPrevPrev = c.TipPrev;
        c.TipPrev = tip;
        var targetTip = Vector3.Transform(c.Solver.Targets[^1] - measureFrom, inverseParent);
        if (c.Seen >= 2)
            c.Stats.TargetJerk.Add((targetTip - 2f * c.TargetPrev + c.TargetPrevPrev).Length() * 1000f);
        c.TargetPrevPrev = c.TargetPrev;
        c.TargetPrev = targetTip;
        c.Seen++;
        c.Stats.Tips.Add(tip);

        // Drift: how far a leg-following chain's tip has moved around the
        // body from where it hangs at rest, about the vertical through the
        // hips, turned with the hips' heading but not their tilt. A front
        // panel carried round behind the hips leaves the body bare while every
        // other measure reads clean, since its target went with it. It is
        // reported, not judged: columns near the middle legitimately swing far
        // around it as a leg pushes them aside, so render the frames to check.
        if (c.Solver.HipsRest != null && c.Solver.Spec.LegDrive > 0f && hips >= 0)
        {
            var inverseRoot = Quaternion.Inverse(rootRotation);
            var now = Vector3.Transform(joints[^1].Tail - worldPos[hips], inverseRoot);
            var rest = Vector3.Transform(c.Solver.HipsRest[^1], bindHips);
            var heading = Vector3.Transform(Vector3.Transform(Vector3.UnitZ, Quaternion.Inverse(bindHips)), inverseRoot * worldRot[hips]);
            var turn = MathF.Atan2(heading.X, heading.Z);
            now = Vector3.Transform(now, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -turn));
            now.Y = 0f;
            rest.Y = 0f;
            if (now.Length() > 0.03f && rest.Length() > 0.03f)
                c.Stats.Drift.Add(Angle(now, rest));
        }

        // Kink: how much sharper the chain bends at a joint than its target
        // shape does there. A chain swinging with a leg turns far from its
        // rest pose by design, so its angle from rest says nothing. Folding
        // into a zigzag is what reads as broken.
        var bend = 0f;
        var origin = worldPos[c.Bones[0]];
        for (var k = 1; k < joints.Length; k++)
        {
            var a = joints[k - 1].Tail - (k >= 2 ? joints[k - 2].Tail : origin);
            var b = joints[k].Tail - joints[k - 1].Tail;
            var ta = c.Solver.Targets[k - 1] - (k >= 2 ? c.Solver.Targets[k - 2] : origin);
            var tb = c.Solver.Targets[k] - c.Solver.Targets[k - 1];
            bend = Math.Max(bend, Angle(a, b) - Angle(ta, tb));
        }
        c.Stats.Bend.Add(bend);

        // Measured against the groups the chain collides with, and against the
        // legs too for a leg-following chain under SPRING_MEASURE_LEGS.
        var measured = c.Solver.Colliders | (MeasureLegs && c.Solver.Spec.LegDrive > 0f ? SpringColliderGroup.Legs : SpringColliderGroup.None);
        if (Trace != null && c.Stats.Name == Trace && c.Seen < TraceFrames)
        {
            var bends = joints.Select(j => SpringSolver.AngleDegrees(j.LastLocal, j.RestLocalRotation).ToString("F0", CultureInfo.InvariantCulture));
            var sinks = colliders.Where(col => (col.Spec.Group & measured) != 0).Select(col =>
            {
                var worst = 0f;
                var worstLink = -1;
                for (var k = 0; k < joints.Length; k++)
                {
                    var depth = col.Radius - (joints[k].Tail - SpringSolver.ClosestOnSegment(col.A, col.B, joints[k].Tail)).Length();
                    var rest = Math.Max(0f, col.Radius - (c.Solver.Targets[k] - SpringSolver.ClosestOnSegment(col.A, col.B, c.Solver.Targets[k])).Length());
                    if ((depth - rest) * 1000f > worst)
                    {
                        worst = (depth - rest) * 1000f;
                        worstLink = k;
                    }
                }
                return $"{col.Spec.From}-{col.Spec.To}:{worst:F0}@{worstLink}";
            }).Where(t => !t.Contains(":0@", StringComparison.Ordinal));
            // Kink of the TARGET shape at each joint: the angle between
            // consecutive target links, so a kink the springs merely copy
            // shows here too.
            var tkinks = new List<string>();
            var prevDir = Vector3.Normalize(c.Solver.Targets[0] - worldPos[c.Bones[0]]);
            for (var k = 1; k < joints.Length; k++)
            {
                var dir = Vector3.Normalize(c.Solver.Targets[k] - c.Solver.Targets[k - 1]);
                tkinks.Add((MathF.Acos(Math.Clamp(Vector3.Dot(prevDir, dir), -1f, 1f)) * 180f / MathF.PI).ToString("F0", CultureInfo.InvariantCulture));
                prevDir = dir;
            }
            Console.WriteLine($"trace {c.Stats.Name} f{c.Seen}: hits {c.Solver.Hits} bends [{string.Join(" ", bends)}] targetkinks [{string.Join(" ", tkinks)}] sinks [{string.Join(" ", sinks)}]");
        }
        c.Solver.Hits = 0;

        // How deep the deepest particle sits inside a capsule, against the
        // capsule's own radius (no hit margin), beyond what its own target
        // already overlaps: a skirt waistband resting inside the hip capsule
        // is the rest shape, not a fault.
        var sink = 0f;
        for (var k = 0; k < joints.Length; k++)
        {
            var j = joints[k];
            var target = c.Solver.Targets[k];
            foreach (var col in colliders)
            {
                if ((col.Spec.Group & measured) == 0)
                    continue;
                var depth = col.Radius - (j.Tail - SpringSolver.ClosestOnSegment(col.A, col.B, j.Tail)).Length();
                var restDepth = Math.Max(0f, col.Radius - (target - SpringSolver.ClosestOnSegment(col.A, col.B, target)).Length());
                sink = Math.Max(sink, (depth - restDepth) * 1000f);
            }
        }
        c.Stats.Sink.Add(sink);
    }

    private static float Angle(Vector3 a, Vector3 b)
    {
        var la = a.Length();
        var lb = b.Length();
        if (la < 1e-9f || lb < 1e-9f)
            return 0f;
        return MathF.Acos(Math.Clamp(Vector3.Dot(a, b) / (la * lb), -1f, 1f)) * 180f / MathF.PI;
    }
}
