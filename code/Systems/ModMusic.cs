using Il2CppStem;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// The mod's music tracks, sounds in wmgfl_music_bank. Their variations are
// authored without a clip, because a clip referenced from the bank preloads
// its audio data at boot. A track's clip (music/<name> in the mod bundles) is
// loaded into its sound the first time it is played, so only a track a
// session actually hears costs memory.
// A track is played looped, which repeats its whole clip end to start. A track in Loops opens
// on its own beginning and then goes round a part of itself: its clip is the track up to the
// end of the loop's first pass and then a tail, the loop's first ten seconds a second time
// (scripts/music/loop_track.py). Once playback is into the tail it is moved back by one pass,
// onto the same samples it was playing, so the clip's end is never reached.
internal static class ModMusic
{
    internal const string BankId = "wmgfl_music_bank";

    // One pass of each looping track, in samples of its clip, and the clip's whole length,
    // which a clip that was resampled or cut differently would not have.
    private static readonly Dictionary<string, (int Pass, int Length)> Loops = new(StringComparer.Ordinal)
    {
        { "title", (2646003, 3417753) },
        { "ops_room", (5527201, 8415751) },
        { "mission_prep", (4097032, 4714432) },
        { "refitting", (5527201, 6298951) },
        { "workshop", (3072000, 3816000) },
    };

    private const int LengthSlack = 64;
    private const int TailSeconds = 10;
    // How far into the tail playback is before it is moved back. What is left of the tail
    // is how long a frame may stall before the clip's end would be heard.
    private const int TailLeadSeconds = 2;

    // The pass of each looping clip that is in place, by the clip's pointer.
    private static readonly Dictionary<IntPtr, int> Passes = new();

    private static readonly int Bank = SoundIds.FromName(BankId);

    // Moves a looping track that has played into its tail back by one pass.
    internal static void KeepLooping(AudioSource source)
    {
        var clip = source != null ? source.clip : null;
        if (clip == null || !Passes.TryGetValue(clip.Pointer, out var pass))
            return;
        var at = source.timeSamples;
        if (at >= clip.samples - (TailSeconds - TailLeadSeconds) * clip.frequency)
            source.timeSamples = at - pass;
    }

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
        if (Loops.TryGetValue(name, out var loop))
        {
            // The importer may pad a clip by a sample or two. The pass is what has to be exact.
            if (Math.Abs(clip.samples - loop.Length) <= LengthSlack)
                Passes[clip.Pointer] = loop.Pass;
            else
                context.Log.Warn($"music: clip music/{name} is {clip.samples} samples where its loop was cut for {loop.Length}, so it repeats whole");
        }
        return track;
    }
}
