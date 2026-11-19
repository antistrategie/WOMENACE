using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Batchmode utility: copies the rest transforms of the extra bones a doll's arms carry (sleeve
    // and ribbon chains) from her model.gltf into her baked prefab.
    //
    // A reconversion that changes how the arms are posed moves those bones in the glTF. The
    // prefab's meshes are the glTF's own and follow it, but its bone transforms were written by
    // the bake, and a whole re-bake rewrites far more than these. The arms' own bones and the
    // hands' sockets keep their frames and are not touched.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.SyncArmBones.Run -logFile sync_arm_bones.log
    public static class SyncArmBones
    {
        private static readonly string[] Chain = { "UpperArm", "LowerArm", "Hand" };

        public static void Run()
        {
            foreach (var model in Directory.GetFiles("Assets/Authored", "model.gltf", SearchOption.AllDirectories))
            {
                var outfit = Path.GetDirectoryName(model).Replace('\\', '/').Substring("Assets/Authored/".Length);
                var prefabPath = $"Assets/Prefabs/{outfit}/main.prefab";
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(model.Replace('\\', '/'));
                if (source == null || !File.Exists(prefabPath)) continue;
                if (TransformSearch.FindDeep(source.transform, "UpperArm_L") == null) continue;

                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                int moved = 0, missing = 0;
                var furthest = 0f;
                foreach (var side in new[] { "_L", "_R" })
                {
                    var from = TransformSearch.FindDeep(source.transform, Chain[0] + side);
                    var to = TransformSearch.FindDeep(root.transform, Chain[0] + side);
                    if (from == null || to == null) continue;
                    var targets = to.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                    foreach (var bone in from.GetComponentsInChildren<Transform>(true))
                    {
                        if (Chain.Any(part => bone.name == part + side) || bone.name.EndsWith("_Socket")) continue;
                        if (!targets.TryGetValue(bone.name, out var target)) { missing++; continue; }
                        var off = Mathf.Max((target.localPosition - bone.localPosition).magnitude * 1000f,
                            Quaternion.Angle(target.localRotation, bone.localRotation));
                        if (off < 0.01f) continue;
                        furthest = Mathf.Max(furthest, off);
                        target.localPosition = bone.localPosition;
                        target.localRotation = bone.localRotation;
                        moved++;
                    }
                }
                if (moved > 0) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                PrefabUtility.UnloadPrefabContents(root);
                Debug.Log($"SyncArmBones: {outfit} moved {moved} bone(s), furthest {furthest:F1} (mm or degrees), {missing} not in the prefab.");
            }
            AssetDatabase.SaveAssets();
        }
    }
}
