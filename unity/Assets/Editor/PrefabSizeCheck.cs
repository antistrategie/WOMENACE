using System.Linq;
using UnityEditor;
using UnityEngine;

// Reports the world-space size a prefab actually renders at.
//
// Worth measuring rather than deriving. A model's final size is the product of
// the import scale, every transform down the chain and, for a skinned renderer,
// the bind pose, and any one of those can carry a factor that the others hide.
// Instantiating and reading Renderer.bounds is the only reading that accounts
// for all of them at once.
//
//   Unity -batchmode -nographics -quit -projectPath unity \
//     -executeMethod WOMENACE.Editor.PrefabSizeCheck.Run \
//     -prefabs Assets/Prefabs/voymastina_mech/default/main.prefab,...
namespace WOMENACE.Editor
{
    public static class PrefabSizeCheck
    {
        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            string list = null;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-prefabs") list = args[i + 1];
            if (string.IsNullOrEmpty(list))
            {
                Debug.LogError("PrefabSizeCheck: -prefabs required");
                EditorApplication.Exit(1);
                return;
            }

            foreach (var path in list.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.Log("SIZE missing " + path);
                    continue;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                var renderers = instance.GetComponentsInChildren<Renderer>(includeInactive: true)
                    .Where(r => r.enabled && !(r is ParticleSystemRenderer))
                    .ToArray();
                if (renderers.Length == 0)
                {
                    Debug.Log("SIZE " + path + " has no renderer");
                    Object.DestroyImmediate(instance);
                    continue;
                }
                // A skinned renderer's bounds are its bind-pose box, which is what
                // the game culls and draws against before any clip plays.
                var box = renderers[0].bounds;
                foreach (var r in renderers.Skip(1)) box.Encapsulate(r.bounds);

                Debug.Log(string.Format(
                    "SIZE {0} width={1:F3} height={2:F3} depth={3:F3} groundY={4:F3} renderers={5}",
                    path, box.size.x, box.size.y, box.size.z, box.min.y, renderers.Length));

                // The tallest single renderer too, so one stray box cannot inflate
                // the total without saying so.
                var tallest = renderers.OrderByDescending(r => r.bounds.size.y).First();
                Debug.Log(string.Format("  tallest renderer '{0}' height={1:F3}",
                    tallest.name, tallest.bounds.size.y));
                Object.DestroyImmediate(instance);
            }
            EditorApplication.Exit(0);
        }
    }
}
