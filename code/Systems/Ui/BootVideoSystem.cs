using System.IO;
using Il2CppMenace.Tools;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.Video;

namespace WOMENACE.Code;

// The film the game opens on and the scene behind the title menu are GFL2's. A template field
// cannot name a video clip as an asset, so each clip rides on a prefab whose video player holds
// it (unity/Assets/Editor/BuildVideos.cs), and is given to vanilla's own video template once
// templates are in. A template holds a clip for either decoder the game may pick: both are given
// the one clip, which is VP8 and plays on both.
//
// Each title scene is one loop of a GFL2 main menu background, cropped so the doll stands right
// of the menu, with a still of its first frame for the moment before it plays, and vanilla's
// template plays it looped. The scenes are whatever clips are under
// unity/Assets/Authored/video/title/. Each addition prefab ships as its own bundle named after
// its path, so the set is read from the bundle files, one is drawn at random each launch, and
// only that one is loaded.
public sealed class BootVideoSystem : JiangyuSystem
{
    private const string TitleBundlePrefix = "video__title__";
    private const string BundleSuffix = "__main.bundle";

    public override void OnTemplatesApplied()
    {
        Give("video.splash", "video/intro/main");
        var scenes = Directory.GetFiles(Path.Combine(Context.ModFolder, "bundles"), $"{TitleBundlePrefix}*{BundleSuffix}");
        if (scenes.Length == 0)
        {
            Context.Log.Warn("boot video: no title scenes are deployed.");
            return;
        }
        var bundle = Path.GetFileName(scenes[System.Random.Shared.Next(scenes.Length)]);
        var scene = bundle[TitleBundlePrefix.Length..^BundleSuffix.Length];
        Give("video.title", $"video/title/{scene}/main");
    }

    private void Give(string id, string asset)
    {
        var template = Templates.ById<VideoTemplate>(id, message => Context.Log.Warn($"boot video: {message}"));
        var holder = Context.Assets.Load<GameObject>(asset);
        var clip = holder != null ? holder.GetComponent<VideoPlayer>()?.clip : null;
        if (template == null || clip == null)
        {
            Context.Log.Warn($"boot video: '{asset}' is unavailable for '{id}'.");
            return;
        }
        template.m_VideoClip = clip;
        template.m_VideoClipVP8 = clip;
        // Until a clip plays, the game shows the template's still of its first frame. Vanilla's own
        // shows for a moment before ours plays, so a clip that carries a still of its own takes
        // vanilla's material with that still in it.
        var still = holder.GetComponent<MeshRenderer>()?.sharedMaterial?.GetTexture(StillTexture);
        if (still != null && template.m_FirstFrameMaterial != null)
        {
            var first = new Material(template.m_FirstFrameMaterial) { name = $"WOMENACE_{holder.name}_first_frame" };
            first.SetTexture(StillTexture, still);
            template.m_FirstFrameMaterial = first;
        }
    }

    private const string StillTexture = "_MainTex";
}
