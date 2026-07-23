using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The reward model behind the affinity badge popover: every feature declares what a doll earns at
// each affinity level, and the popover renders one unified per-level list (a filled rank rail on the
// left, that level's rewards to the right). AffinitySystem contributes the proficiency accuracy
// steps and the outfit / SSR weapon / mech unlocks, CalibrationSystem contributes the weapon
// components, and a future feature adds a provider without touching the renderer. Providers run on each
// hover, so the list always reflects live state.
public static class AffinityTooltip
{
    // What a provider is handed: the resolved character and its live affinity level, plus the raw
    // speaker Tags (some providers, e.g. weapon proficiency, key off a tag the character tag alone
    // does not carry).
    public readonly struct Info
    {
        public readonly ModContext Context;
        public readonly string CharacterTag;
        public readonly string SpeakerTags;
        public readonly int Level;

        public Info(ModContext context, string characterTag, string speakerTags, int level)
        {
            Context = context;
            CharacterTag = characterTag;
            SpeakerTags = speakerTags;
            Level = level;
        }
    }

    // The kind of reward, used only to order rewards within a level and to tint the row (accuracy
    // reads gold like the game's proficiency line; the rest read as plain reward text).
    public enum RewardKind { Proficiency, Component, Outfit, Weapon, Mech, Other }

    // One reward earned at one affinity level: the level it lands at, the line to show, and its kind.
    public sealed class Reward
    {
        public int Level;
        public string Text;
        public RewardKind Kind;

        public Reward(int level, string text, RewardKind kind = RewardKind.Other)
        {
            Level = level;
            Text = text;
            Kind = kind;
        }
    }

    public delegate IEnumerable<Reward> Provider(Info info);

    // Ordered, keyed so re-registration (a system re-initialising) replaces rather than duplicates.
    private static readonly List<(string Key, Provider Provider)> Providers = [];

    public static void Register(string key, Provider provider)
    {
        Providers.RemoveAll(p => p.Key == key);
        Providers.Add((key, provider));
    }

    // Every reward for a character across all providers, unordered. The renderer buckets these by
    // level and orders within a level by RewardKind. A provider that throws contributes nothing:
    // providers are iterators, so their exceptions surface during enumeration, and the enumeration
    // must happen inside the try or one broken provider would take the whole popover down.
    public static IEnumerable<Reward> All(Info info)
    {
        var all = new List<Reward>();
        foreach (var (_, provider) in Providers)
        {
            try
            {
                foreach (var reward in provider(info))
                    if (reward != null)
                        all.Add(reward);
            }
            catch (Exception ex) { info.Context?.Log.Warn($"affinity popover: provider failed: {ex.Message}"); }
        }
        return all;
    }
}
