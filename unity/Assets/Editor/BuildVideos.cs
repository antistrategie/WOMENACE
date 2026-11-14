using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace Womenace.EditorTools
{
    // Carries the mod's videos (Assets/Authored/video/<name>.webm, VP8 with Vorbis sound, the
    // one format the game plays on every platform it runs on) into bundles. A template field
    // cannot name a video clip as an asset, so each clip rides on a prefab whose video player
    // holds it, and the runtime takes the clip from there. The clips are kept as they are
    // authored: transcoding is off.
    //
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.BuildVideos.Run
    public static class BuildVideos
    {
        public static void Run()
        {
            Build();
            EditorApplication.Exit(0);
        }

        public static void Build()
        {
            const string sources = "Assets/Authored/video";
            AssetDatabase.Refresh();
            foreach (var source in Directory.GetFiles(sources, "*.webm"))
            {
                var path = source.Replace('\\', '/');
                var name = Path.GetFileNameWithoutExtension(path);
                var importer = (VideoClipImporter)AssetImporter.GetAtPath(path);
                var settings = importer.defaultTargetSettings;
                if (settings.enableTranscoding)
                {
                    settings.enableTranscoding = false;
                    importer.defaultTargetSettings = settings;
                    importer.SaveAndReimport();
                }
                var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(path);
                if (clip == null)
                    throw new InvalidDataException($"'{path}' did not import as a video clip");

                var folder = $"Assets/Prefabs/video/{name}";
                Directory.CreateDirectory(folder);
                var holder = new GameObject(name);
                var player = holder.AddComponent<VideoPlayer>();
                player.playOnAwake = false;
                player.source = VideoSource.VideoClip;
                player.clip = clip;
                player.enabled = false;
                PrefabUtility.SaveAsPrefabAsset(holder, $"{folder}/main.prefab");
                Object.DestroyImmediate(holder);
                Debug.Log($"[BuildVideos] {name}: {clip.width}x{clip.height}, {clip.length:0.0}s, {clip.audioTrackCount} audio track(s)");
            }
            AssetDatabase.SaveAssets();
        }
    }
}
