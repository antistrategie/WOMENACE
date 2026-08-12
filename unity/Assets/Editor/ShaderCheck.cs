using System.IO;
using UnityEditor;
using UnityEngine;

// Batch-mode compile check for the authored doll shaders.
//
// Two things this has to do that a naive version does not. It forces a reimport,
// because GetShaderMessages returns whatever the last import produced and reports
// a clean shader if nothing recompiled. And it must be run with -buildTarget
// StandaloneWindows64, because a shader can compile for the editor's graphics API
// and fail for D3D11, which is the only one the shipped bundle carries.
//
// Every shader in the folder, not one named file: they share DollMotionVectors.hlsl,
// so a change there reaches all of them and checking one would pass while another
// was broken.
//
// Even so this is an early signal, not the ground truth. Variant-level errors surface
// when the AssetBundle is built, so the build log is still worth reading. It is
// unity/build.log, the prefab bundle build, which is what a doll's shaders compile
// into. .jiangyu/unity_build_mesh.log is the raw-GLB mesh stage and goes stale across
// incremental builds, reporting a clean shader that was never rebuilt.
//   grep -E "Shader (error|warning)" unity/build.log
public static class ShaderCheck
{
    private const string Folder = "Assets/Shaders";

    public static void Run()
    {
        var failed = 0;
        var checked_ = 0;

        foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { Folder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetExtension(path) != ".shader") continue;

            AssetDatabase.ImportAsset(path,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                Debug.Log("CHECK missing " + path);
                failed++;
                continue;
            }

            checked_++;
            var messages = ShaderUtil.GetShaderMessageCount(shader);
            Debug.Log($"CHECK {Path.GetFileName(path)} isSupported={shader.isSupported} " +
                      $"passCount={shader.passCount} messages={messages}");
            if (messages > 0)
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    Debug.Log($"CHECK   [{m.severity}] line {m.line}: {m.message} {m.messageDetails}");
            }

            if (!shader.isSupported || messages > 0) failed++;
        }

        // The build target is reported so a run against the wrong one is visible
        // rather than silently checking the wrong platform.
        Debug.Log($"CHECK done shaders={checked_} failed={failed} " +
                  $"target={EditorUserBuildSettings.activeBuildTarget}");
        EditorApplication.Exit(failed == 0 && checked_ > 0 ? 0 : 1);
    }
}
