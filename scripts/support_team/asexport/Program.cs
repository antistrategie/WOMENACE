// Exports GameObjects from GFL2 bundles to FBX through AssetStudioMod's own converter, each with
// the animation clips named for it, so a model and its clips come out bound together. A support
// team's crew, weapon and props are exported this way (scripts/support_team/prepare_team.py).
//
//   asexport <out_dir> <scene_root> <bundle>... -- <object>=<clip>[,<clip>] ...
//
// <scene_root> picks which copy of each object to export when several bundles hold one. Each
// object is written to <out_dir>/<object>/<object>.fbx with its textures beside it.
using AssetStudio;

if (args.Length < 4) { Console.Error.WriteLine("usage: asexport <out> <scene_root> <bundles...> -- <obj>=<clip,...>"); return 1; }
var outDir = args[0];
var sceneRoot = args[1];
var split = Array.IndexOf(args, "--");
var bundles = args[2..split];
var jobs = args[(split + 1)..].Select(a => a.Split('=')).ToDictionary(p => p[0], p => p.Length > 1 ? p[1].Split(',', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>());

var manager = new AssetsManager();
manager.LoadFilesAndFolders(bundles);
var objects = manager.AssetsFileList.SelectMany(f => f.Objects).ToList();
var clips = objects.OfType<AnimationClip>().GroupBy(c => c.m_Name).ToDictionary(g => g.Key, g => g.First());
var gos = objects.OfType<GameObject>().ToList();
Console.WriteLine($"loaded {manager.AssetsFileList.Count} files, {gos.Count} GameObjects, {clips.Count} clips");

string RootName(GameObject go)
{
    var t = go.m_Transform;
    while (t.m_Father.TryGet(out var father)) t = father;
    return t.m_GameObject.TryGet(out var root) ? root.m_Name : "";
}

Directory.CreateDirectory(outDir);
var settings = new Fbx.Settings
{
    EulerFilter = true, FilterPrecision = 0.25f, ExportAllNodes = true, ExportSkins = true,
    ExportAnimations = true, ExportBlendShape = true, CastToBone = false, BoneSize = 10,
    ScaleFactor = 1f, FbxVersionIndex = 3, FbxFormat = 0,
};
var failed = 0;
foreach (var (name, clipNames) in jobs)
{
    var go = gos.FirstOrDefault(g => g.m_Name == name && RootName(g) == sceneRoot)
          ?? gos.FirstOrDefault(g => g.m_Name == name);
    if (go == null) { Console.Error.WriteLine($"no GameObject {name}"); failed++; continue; }
    var list = new List<AnimationClip>();
    foreach (var c in clipNames)
        if (clips.TryGetValue(c, out var clip)) list.Add(clip);
        else Console.Error.WriteLine($"no clip {c}");
    var converter = go.m_Animator != null
        ? new ModelConverter(go.m_Animator, ImageFormat.Png, list)
        : new ModelConverter(go, ImageFormat.Png, list);
    var dir = Path.Combine(outDir, name);
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, name + ".fbx");
    ModelExporter.ExportFbx(path, converter, settings);
    Console.WriteLine($"{name}: root {RootName(go)}, animator {(go.m_Animator != null)}, {converter.MeshList.Count} meshes, {converter.AnimationList.Count} clips -> {path}");
}
return failed;
