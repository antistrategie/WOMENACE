using Jiangyu.Sdk;

namespace WOMENACE.Code;

public static class KalinaDialogue
{
    public static readonly Line[] Greetings =
    {
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/welcome", "Welcome back, Commander! I've listed out everything that needs to be done today! Please have a look!")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/agenda", "Morning, Commander! Your agenda for today is on your desk. Feel free to ask me for help anytime!"), startHour: 5, endHour: 12),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/break", "Take a short break, Commander. At least preserve some energy for your work in the afternoon, or your body won't be able to take it!"), startHour: 10, endHour: 14),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/nap", "Commander, it's already afternoon? Yaaaaaawn~ I wish my nap could've been longer..."), startHour: 12, endHour: 17),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/late_work", "It's already pretty late, Commander. You aren't planning to spend the night in the command room again, are you?"), startHour: 21, endHour: 5),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/dry_eyes", "Ugh... Commander, don't your eyes ever get dry?")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/breakfast", "I know I'm being long-winded, Commander, but breakfast is really important. Don't make your body suffer!"), startHour: 5, endHour: 11),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/lunch", "Commander, it's time for lunch~ I've prepared a bountiful lunchbox~ Heheh, wanna try?"), startHour: 11, endHour: 14),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/dinner", "A good dinner is the best way to treat your body after a hard day at work, don't you think?"), startHour: 17, endHour: 21),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/supper", "I always feel as if I hadn't eaten anything since lunch at this hour. Shall I prepare some supper, Commander?"), startHour: 21, endHour: 3),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/cats", "Ah, Commander~ I'm just about to go feed the cats. Wanna come along?")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/dolls", "The new Dolls have completed their registration~ Do your best, everyone!")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/buying", "Commander, what are you buying today?")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_00_hungry", "I'm starving, Commander…")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_00_breakfast", "Morning, Commander. Had breakfast, yet?"), startHour: 5, endHour: 12),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_00_laugh", "Ehehehehehe…")),
    };

    // Array order determines progression. Unlock levels scale with the current level cap.
    public static readonly Line[] AffinityLines =
    {
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_01", "Wanna buy something, Commander? I might give you some discount~")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_02", "There will be miracles, Commander. All we need is the magic of my innocence… and a bit of moolah.")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_03", "Ahahahahaha…")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_04", "Buying something again today, Commander? I'll give you discounts!")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_05", "Heheheh… so much dough… Ack! Didn't see you there, Commander!")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_06", "Take your time, take your pick.")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_07", "Yay!")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_08", "Our stock has been piling up lately… Ah, Commander! Right on time. I'll give you special discounts, today!")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_09", "Hum hum hum… Ah, Commander. I'm in a jolly mood today, so you're getting discounts~")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_10", "Hahahahahahaha!")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_11", "If you keep being so charitable, Commander, I'm gonna fall head over heels… with the cash, of course!")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_12", "You're so generous, Commander, I… still won't give you discounts!")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_13", "Heheheh…")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_14", "To be honest… I don't like money that much… Not that there's anything I like better.")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_15", "The sun's gone down, Commander. Don't work too late, and don't hesitate to spend a bit to sooth yourself."), 18, 5),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_16", "Except for these, those and those over there, everything is sold at the import price. Cross my heart.")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_17", "Wow!")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_18", "Wanna know me better…? Should I sell you access authorization to my secrets? Sadly I've got no such thing.")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_19", "What a beautiful afternoon. Why not spend a little money to make it even more beautiful?"), 12, 18),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_20", "What? Running low on cash? …Can't help it then. Special discounts just for today then.")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_21", "You can chitchat with me. Seeing that you're an old customer, I'll make it free just this once.")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_22", "Muahahahahaha!")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_23", "It's really late, Commander. Want some midnight snack?"), 22, 5),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_24", "I'm running out of space… Take some of these away, Commander! I'm selling them at cost price!")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_25", "The adage may go, \"Don't let money control you. You control money.\" But I won't control you, dear Commander!")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_26", "Ehehehehehe…")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_27", "Let's play a little game. If you win, you give me a treat. If you lose, you gotta buy something. How's that?")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_28", "Commander! Make another purchase and you'll get a special surprise!")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_29", "I've made so many exceptions and given you so many discounts, but never on my love for you.")),
        new(new LocalisedText("WOMENACE::ui/kalina/dialogue/affinity_30", "Don't forget our special agreement, 'kay? Money is just part of the deal.")),
    };

    public static int UnlockLevel(Line line, int maximumLevel)
    {
        var step = Array.IndexOf(AffinityLines, line) + 1;
        if (step == 0)
            return 1;
        var count = AffinityLines.Length;
        return 1 + (int)(((long)step * (Math.Max(1, maximumLevel) - 1) + count - 1) / count);
    }

    public static IReadOnlyList<Line> Available(int level, int maximumLevel, int hour)
        => Greetings.Concat(AffinityLines)
            .Where(line => UnlockLevel(line, maximumLevel) <= Math.Max(1, level) && line.MatchesHour(hour)).ToList();

    public static Line Choose(int level, int maximumLevel, int hour, Random random,
        string previousText = null, int previousLevel = 0)
    {
        var pool = Available(level, maximumLevel, hour).ToList();
        if (previousLevel > 0 && level > previousLevel)
        {
            var unlocked = pool.Where(line => UnlockLevel(line, maximumLevel) > previousLevel).ToList();
            if (unlocked.Count > 0)
                pool = unlocked;
        }
        var different = pool.Where(line => line.Text.Fallback != previousText).ToList();
        if (different.Count > 0)
            pool = different;
        return pool.Count > 0 ? pool[random.Next(pool.Count)] : null;
    }

    public sealed class Line(LocalisedText text, int startHour = 0, int endHour = 24)
    {
        public readonly LocalisedText Text = text;

        public bool MatchesHour(int hour)
            => startHour <= endHour ? hour >= startHour && hour < endHour : hour >= startHour || hour < endHour;
    }
}
