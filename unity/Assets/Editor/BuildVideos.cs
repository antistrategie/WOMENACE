using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace Womenace.EditorTools
{
    // Carries the mod's videos (Assets/Authored/video/<path>.webm, VP8 with Vorbis sound, the
    // one format the game plays on every platform it runs on) into bundles. A template field
    // cannot name a video clip as an asset, so each clip rides on a prefab whose video player
    // holds it (video/<path>/main), and the runtime takes the clip from there. A folder of clips
    // keeps its path, so the runtime can find a set of them by their bundles. The clips are kept
    // as they are authored: transcoding is off. A clip with a still of its first frame beside it
    // (<path>.png) carries that too.
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
            foreach (var source in Directory.GetFiles(sources, "*.webm", SearchOption.AllDirectories))
            {
                var path = source.Replace('\\', '/');
                var name = path.Substring(sources.Length + 1, path.Length - sources.Length - 1 - ".webm".Length);
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
                var holder = new GameObject(Path.GetFileName(name));
                var player = holder.AddComponent<VideoPlayer>();
                player.playOnAwake = false;
                player.source = VideoSource.VideoClip;
                player.clip = clip;
                player.enabled = false;
                // A still of the clip's first frame (<path>.png beside it) is what the game shows
                // until the clip plays. A prefab holds a texture only through a component, so it
                // rides in a material on a renderer that never draws. The material's shader is any
                // of the mod's own with a _MainTex, so it is in the game to read the texture by.
                var stillPath = Path.ChangeExtension(path, ".png");
                if (File.Exists(stillPath))
                {
                    var stillImporter = (TextureImporter)AssetImporter.GetAtPath(stillPath);
                    if (stillImporter.mipmapEnabled || stillImporter.maxTextureSize < 2048 || stillImporter.textureCompression != TextureImporterCompression.CompressedHQ)
                    {
                        stillImporter.mipmapEnabled = false;
                        stillImporter.maxTextureSize = 2048;
                        stillImporter.textureCompression = TextureImporterCompression.CompressedHQ;
                        stillImporter.SaveAndReimport();
                    }
                    var still = new Material(Shader.Find("Womenace/VisionText")) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(stillPath) };
                    AssetDatabase.CreateAsset(still, $"{folder}/first_frame.mat");
                    var renderer = holder.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = still;
                    renderer.enabled = false;
                }
                PrefabUtility.SaveAsPrefabAsset(holder, $"{folder}/main.prefab");
                Object.DestroyImmediate(holder);
                Debug.Log($"[BuildVideos] {name}: {clip.width}x{clip.height}, {clip.length:0.0}s, {clip.audioTrackCount} audio track(s)");
            }
            AssetDatabase.SaveAssets();
        }
    }
}
