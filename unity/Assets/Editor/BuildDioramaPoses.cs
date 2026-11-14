using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Builds the strategy diorama's doll poses: each doll's GFL2 lobby loops as
    // muscle clips, wired into one AnimatorController whose states are named
    // "<doll>-<pose>", and a prefab whose Animator carries it so the mod can load
    // the controller as environment/doll_poses/main. The room hands that
    // controller to every doll it stands up and plays her own state for the spot.
    //
    // Donors come from scripts/diorama/export_lobby_clips.py: one FBX per doll in
    // Assets/Authored/diorama/_donor, with a .prefix file holding her take prefix.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.BuildDioramaPoses.Run [-dolls lenna,leva] [-skipExtract]
    public static class BuildDioramaPoses
    {
        private const string DonorDir = "Assets/Authored/diorama/_donor";
        private const string ClipsDir = "Assets/Authored/diorama/clips";
        private const string HumanMap = "Assets/Authored/diorama/humanmap.txt";
        private const string OutDir = "Assets/Prefabs/environment/doll_poses";

        // Lobby take -> pose state name.
        private static readonly (string Take, string Pose)[] Poses =
        {
            ("Idle_0_Loop", "Idle"),
            ("LongIdle_0_Loop", "LongIdle"),
            ("Sit_0_Loop", "Sit"),
        };

        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == name) return args[i + 1];
                return null;
            }
            var only = Arg("-dolls")?.Split(',').Select(s => s.Trim()).ToHashSet();
            var skipExtract = args.Contains("-skipExtract");

            if (!skipExtract)
            {
                var takes = Poses.Select(p => p.Take).ToList();
                foreach (var fbx in Directory.GetFiles(DonorDir, "*.fbx").OrderBy(f => f))
                {
                    var doll = Path.GetFileNameWithoutExtension(fbx);
                    if (only != null && !only.Contains(doll))
                        continue;
                    var prefixFile = Path.Combine(DonorDir, doll + ".prefix");
                    var prefix = File.Exists(prefixFile) ? File.ReadAllText(prefixFile).Trim() : string.Empty;
                    var outDir = $"{ClipsDir}/{doll}";
                    if (!ExtractHumanoidClips.Extract(fbx.Replace('\\', '/'), outDir, prefix, HumanMap, null, takes))
                    {
                        Debug.LogError($"BuildDioramaPoses: extraction failed for {doll}.");
                        EditorApplication.Exit(1);
                        return;
                    }
                }
            }

            Build();
            EditorApplication.Exit(0);
        }

        private static void Build()
        {
            if (!Directory.Exists(OutDir))
                Directory.CreateDirectory(OutDir);
            var controllerPath = $"{OutDir}/poses.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;

            var states = 0;
            foreach (var dir in Directory.GetDirectories(ClipsDir).OrderBy(d => d))
            {
                var doll = Path.GetFileName(dir);
                var made = new Dictionary<string, AnimatorState>();
                foreach (var (take, pose) in Poses)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipsDir}/{doll}/{take}.anim");
                    if (clip == null)
                        continue;
                    var state = machine.AddState($"{doll}-{pose}");
                    state.motion = clip;
                    made[pose] = state;
                    states++;
                }
                // Her fidget plays once and gives way to her standing idle. The room starts it,
                // at times of each doll's own.
                if (made.TryGetValue("Idle", out var idle) && made.TryGetValue("LongIdle", out var fidget))
                {
                    var back = fidget.AddTransition(idle);
                    back.hasExitTime = true;
                    back.exitTime = 1f;
                    back.duration = 0.4f;
                }
                Debug.Log($"BuildDioramaPoses: {doll}: {string.Join(", ", made.Keys)}");
            }
            AssetDatabase.SaveAssets();

            var root = new GameObject("doll_poses");
            try
            {
                root.AddComponent<Animator>().runtimeAnimatorController = controller;
                // The contact shadow every doll stands on: a unit floor quad (in XZ, facing up) with
                // the blob shadow material, cloned under each doll at runtime and sized there.
                var shadow = new GameObject("shadow");
                shadow.transform.SetParent(root.transform, false);
                shadow.AddComponent<MeshFilter>().sharedMesh = ShadowQuad();
                var renderer = shadow.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = ShadowMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                PrefabUtility.SaveAsPrefabAsset(root, $"{OutDir}/main.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"BuildDioramaPoses: {states} state(s) in {controllerPath}.");
        }

        private static Mesh ShadowQuad()
        {
            var path = $"{OutDir}/shadow_quad.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null)
                return mesh;
            mesh = new Mesh
            {
                name = "shadow_quad",
                vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) },
                normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
                triangles = new[] { 0, 2, 1, 2, 3, 1 },
            };
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Material ShadowMaterial()
        {
            var path = $"{OutDir}/shadow.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            material = new Material(Shader.Find("Womenace/BlobShadow")) { name = "shadow" };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
