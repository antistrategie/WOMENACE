using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The shared affinity model: the single thing every WOMENACE system reads to coordinate on
// affinity. AffinitySystem owns the points (it is the only writer). Any system that gates on
// affinity (the Sinbreaker form swap, the transmog picker) reads the level through here, and the
// badge popover lists its unlocks from the same Unlocks table. The systems never call each
// other directly. They share one Context.State (Get<AffinityState>() hands the same live
// instance to all of them) plus these rules, so they cannot drift out of step.
public static class Affinity
{
    // The marker carried in a leader's speaker Tags string that flags one of our characters.
    public const string Tag = "wmgfl";

    // Optional warn sink, wired by AffinitySystem. This static model has no Context of its own, so
    // without it a marshalling failure in leader resolution would treat a valid character as "not
    // ours" with no trace. Null until set.
    public static Action<string> Warn;

    // Cumulative points to reach each level above 1: index 0 is the level-2 floor, the last index is
    // the top level's floor. Editing this array is all it takes to retune the curve and its length.
    public static readonly int[] StepThresholds =
        { 100, 200, 300, 500, 800, 1300, 2100, 3400, 5500 };

    // The top level, derived from the curve: level 1 plus one level per threshold. Property (not a
    // field) so it reads StepThresholds lazily, after that array's initialiser has run.
    public static int MaxLevel => StepThresholds.Length + 1;

    public static int LevelForPoints(int points)
    {
        var level = 1;
        foreach (var threshold in StepThresholds)
        {
            if (points >= threshold)
                level++;
            else
                break;
        }
        return level > MaxLevel ? MaxLevel : level;
    }

    // Deterministic FNV-1a hash: a stable key across runs (string.GetHashCode is randomised per
    // process and would break save persistence).
    public static int StableHash(string s)
    {
        unchecked
        {
            var h = (int)2166136261;
            foreach (var c in s)
                h = (h ^ c) * 16777619;
            return h;
        }
    }

    // The speaker's Tags string for a leader, or null if the leader is not one of ours.
    public static string OurSpeakerTags(BaseUnitLeader leader)
    {
        try
        {
            if (leader == null || !leader.IsAlive())
                return null;
            var speaker = leader.GetSpeakerTemplate();
            var tags = speaker.IsAlive() ? speaker.Tags : null;
            return string.IsNullOrEmpty(tags) || !tags.Contains(Tag) ? null : tags;
        }
        catch (Exception ex)
        {
            Warn?.Invoke($"affinity: speaker-tag resolve failed: {ex.Message}");
            return null;
        }
    }

    // Stable per-character key. Keyed by the character's own tag (e.g. "wmgfl_voymastina"), not the
    // whole speaker Tags string and not the leader template: that token is the character's identity,
    // so affinity is shared across a character's forms (Voymastina's squad-leader and pilot share one
    // speaker) and stays put when unrelated tags change (a trait added, a tag renamed). Hashing the
    // full Tags string would move the key whenever any token changed, orphaning saved progress. 0 is
    // reserved for "not ours", so a tag that happens to hash to 0 is nudged to a non-zero key.
    public static int KeyFor(BaseUnitLeader leader) => KeyForTag(CharacterTag(leader));

    // The stable key for a character tag directly, for callers that already hold the tag
    // (the transmog model keys its selections the same way affinity keys its points).
    public static int KeyForTag(string characterTag)
    {
        if (characterTag == null)
            return 0;
        var key = StableHash(characterTag);
        return key == 0 ? 1 : key;
    }

    public static int KeyFor(VisualElement window)
    {
        try { return KeyFor(LeaderOf(window)); }
        catch { return 0; }
    }

    // The leader currently bound to a UnitWindow, or null.
    public static BaseUnitLeader LeaderOf(VisualElement window)
    {
        try { return window.TryCast<UnitWindow>()?.m_CurrentLeader; }
        catch { return null; }
    }

    // The character's own tag (e.g. "wmgfl_voymastina"), parsed out of the speaker Tags string so
    // the Unlocks registry can be keyed by character. Null if the leader is not one of ours.
    public static string CharacterTag(BaseUnitLeader leader) => ParseCharacterTag(OurSpeakerTags(leader));

    // The character's own tag parsed from an in-combat entity. Combat entities do NOT carry the doll's
    // EntityTemplate tag, but they keep their SpeakerTemplate (Tags "wmgfl_<name> ..."), so this is the
    // reliable in-mission identity: the SSR imprint system reads it to tell whose weapon just fired.
    public static string CharacterTag(Entity entity)
    {
        try
        {
            var speaker = entity != null ? entity.GetSpeakerTemplate() : null;
            var tags = speaker.IsAlive() ? speaker.Tags : null;
            return string.IsNullOrEmpty(tags) || !tags.Contains(Tag) ? null : ParseCharacterTag(tags);
        }
        catch (Exception ex)
        {
            Warn?.Invoke($"affinity: entity speaker-tag resolve failed: {ex.Message}");
            return null;
        }
    }

    // The "wmgfl_<name>" identity token out of a speaker Tags string, or null if none is present.
    private static string ParseCharacterTag(string tags)
    {
        if (tags == null)
            return null;
        foreach (var token in tags.Split(' '))
            if (token.StartsWith(Tag + "_", StringComparison.Ordinal))
                return token;
        return null;
    }

    // Raised when a leader's affinity changes (a gift confirmed). Systems showing persistent
    // affinity-gated UI (the Sinbreaker swap button) subscribe to refresh it for the affected window
    // right away, instead of waiting for the next screen rebuild. The argument is the UnitWindow the
    // change happened on, which also hosts those systems' injected controls.
    public static event Action<VisualElement> Changed;

    public static void RaiseChanged(VisualElement window)
    {
        try { Changed?.Invoke(window); }
        catch { }
    }

    public static int PointsFor(ModContext context, int key)
        => context.State.Get<AffinityState>().ForLeader(key).Affinity;

    public static int LevelFor(ModContext context, int key) => LevelForPoints(PointsFor(context, key));

    // The level of the leader on a window, or 0 when the leader is not one of ours.
    public static int LevelFor(ModContext context, BaseUnitLeader leader)
    {
        var key = KeyFor(leader);
        return key == 0 ? 0 : LevelFor(context, key);
    }
}

// Persisted affinity points per character key. Shared by every system through
// Context.State.Get<AffinityState>(), which returns the same live instance to all of them.
public sealed class AffinityState
{
    public Dictionary<int, LeaderState> Leaders { get; set; } = [];

    public LeaderState ForLeader(int key)
    {
        if (!Leaders.TryGetValue(key, out var state))
            Leaders[key] = state = new LeaderState();
        return state;
    }
}

public sealed class LeaderState
{
    public int Affinity { get; set; }
}
