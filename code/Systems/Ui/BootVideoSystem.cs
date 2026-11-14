using Il2CppMenace.Tools;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.Video;

namespace WOMENACE.Code;

// The film the game opens on is GFL2's. A template field cannot name a video clip as an asset,
// so the clip rides on a prefab whose video player holds it (unity/Assets/Editor/BuildVideos.cs),
// and is given to vanilla's own video template once templates are in. A template holds a clip
// for either decoder the game may pick: both are given the one clip, which is VP8 and plays on
// both.
public sealed class BootVideoSystem : JiangyuSystem
{
    private const string Template = "video.splash";
    private const string Asset = "video/intro/main";

    public override void OnTemplatesApplied()
    {
        var template = Templates.ById<VideoTemplate>(Template, message => Context.Log.Warn($"boot video: {message}"));
        var holder = Context.Assets.Load<GameObject>(Asset);
        var clip = holder != null ? holder.GetComponent<VideoPlayer>()?.clip : null;
        if (template == null || clip == null)
        {
            Context.Log.Warn($"boot video: '{Asset}' is unavailable for '{Template}'.");
            return;
        }
        template.m_VideoClip = clip;
        template.m_VideoClipVP8 = clip;
    }
}
