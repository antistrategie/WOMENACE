using Il2CppStem;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// The mod's music tracks, sounds in wmgfl_music_bank. Their variations are
// authored without a clip, because a clip referenced from the bank preloads
// its audio data at boot. A track's clip (music/<name> in the mod bundles) is
// loaded into its sound the first time it is played, so only a track a
// session actually hears costs memory.
internal static class ModMusic
{
    internal const string BankId = "wmgfl_music_bank";

    private static readonly int Bank = SoundIds.FromName(BankId);

    // The track's id once its clip is in place, or null with a warning when the
    // sound or clip is missing, so the caller keeps the vanilla music.
    internal static ID? Load(ModContext context, string name)
    {
        var track = new ID(Bank, SoundIds.FromName(name));
        var variations = SoundManager.GetSound(track)?.variations;
        if (variations is not { Count: > 0 })
        {
            context.Log.Warn($"music: no {name} sound in {BankId}, keeping the vanilla track");
            return null;
        }
        var variation = variations[0];
        if (variation.clip != null)
            return track;
        var clip = context.Assets.Load<AudioClip>("music__" + name);
        if (clip == null)
        {
            context.Log.Warn($"music: clip music/{name} is not in the mod bundles, keeping the vanilla track");
            return null;
        }
        variation.clip = clip;
        return track;
    }
}
