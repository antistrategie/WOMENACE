using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Batchmode utility: save one subtree of an existing prefab as a prefab of its own, under a
    // fresh root named "main". Used to split a VFX prefab into a lighter per-tile version (the
    // frost ground particles of Alva's ice shard, without the shard itself) that a tile effect can
    // spawn on every tile of an area.
    //
    // Invoke:
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.ExtractPrefabSubtree.Run \
    //     -source Assets/Prefabs/effects/frost_echo/main.prefab \
    //     -child VFX/dm \
    //     -out Assets/Prefabs/effects/frost_echo_ground/main.prefab
    public static class ExtractPrefabSubtree
    {
        private static string Arg(string[] args, string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var source = Arg(args, "-source");
            var childPath = Arg(args, "-child");
            var outPath = Arg(args, "-out");
            if (source == null || childPath == null || outPath == null)
                throw new Exception("ExtractPrefabSubtree: -source, -child and -out are required");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (prefab == null)
                throw new Exception($"ExtractPrefabSubtree: no prefab at {source}");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var child = instance.transform.Find(childPath);
            if (child == null)
                throw new Exception($"ExtractPrefabSubtree: no child '{childPath}' in {source}");

            var root = new GameObject("main");
            child.SetParent(root.transform, false);

            var dir = System.IO.Path.GetDirectoryName(outPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir))
            {
                var parts = dir.Split('/');
                var current = parts[0];
                foreach (var part in parts.Skip(1))
                {
                    var next = current + "/" + part;
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, part);
                    current = next;
                }
            }
            PrefabUtility.SaveAsPrefabAsset(root, outPath, out var ok);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            if (!ok)
                throw new Exception($"ExtractPrefabSubtree: saving {outPath} failed");
            var renderers = AssetDatabase.LoadAssetAtPath<GameObject>(outPath).GetComponentsInChildren<ParticleSystem>(true).Length;
            Debug.Log($"ExtractPrefabSubtree: wrote {outPath} ({renderers} particle system(s)) from {source}:{childPath}");
        }
    }
}
