using Jiangyu.Sdk;

namespace WOMENACE.Code;

public static class Kalina
{
    public const string CurrencyId = "commodity.wmgfl_sardis_gold";
    public const int SardisPerAffinityPoint = 5;
    public const int AffinityPerLevel = 100;
    public static int MaxLevel => AffinityOutfits.Length;

    public static readonly Outfit[] AffinityOutfits =
    {
        // Character_Profile_Kalina.png
        new("default", new LocalisedText("WOMENACE::ui/kalina/default", "Default"),
            1024.0f, 1024.0f, 292.0f, 35.0f, 743.0f, 936.0f, 10.0f, 28.0f, 280.0f, 559.379f),
        // Character_Profile_Kalina_costume1.png
        new("outfit_01", new LocalisedText("WOMENACE::ui/kalina/outfit_01", "Rays of Youth"),
            1024.0f, 1024.0f, 336.0f, 16.0f, 705.0f, 986.0f, 35.876f, 28.0f, 228.247f, 600.0f),
        // Character_Profile_Kalina_costume2.png
        new("outfit_02", new LocalisedText("WOMENACE::ui/kalina/outfit_02", "Urban Freedom"),
            1024.0f, 1024.0f, 367.0f, 26.0f, 690.0f, 962.0f, 46.474f, 28.0f, 207.051f, 600.0f),
        // Character_Profile_Kalina_costume3.png
        new("outfit_03", new LocalisedText("WOMENACE::ui/kalina/outfit_03", "Rock 'n' Spanner"),
            1024.0f, 1024.0f, 319.0f, 3.0f, 682.0f, 1024.0f, 43.34f, 28.0f, 213.32f, 600.0f),
        // Character_Profile_Kalina_costume4.png
        new("outfit_04", new LocalisedText("WOMENACE::ui/kalina/outfit_04", "Griffin Uniform"),
            1024.0f, 1024.0f, 330.0f, 20.0f, 678.0f, 1022.0f, 45.808f, 28.0f, 208.383f, 600.0f),
        // Character_Profile_Kalina_costume5.png
        new("outfit_05", new LocalisedText("WOMENACE::ui/kalina/outfit_05", "Gems and Fireworks"),
            1024.0f, 1024.0f, 309.0f, 13.0f, 723.0f, 1013.0f, 25.8f, 28.0f, 248.4f, 600.0f),
        // Character_Profile_Kalina_costume6.png
        new("outfit_06", new LocalisedText("WOMENACE::ui/kalina/outfit_06", "Winter Party"),
            1024.0f, 1024.0f, 361.0f, 9.0f, 783.0f, 1017.0f, 24.405f, 28.0f, 251.19f, 600.0f),
        // Character_Profile_Kalina_costume7.png
        new("outfit_07", new LocalisedText("WOMENACE::ui/kalina/outfit_07", "Black Cat"),
            1024.0f, 1024.0f, 279.0f, 3.0f, 763.0f, 1021.0f, 10.0f, 28.0f, 280.0f, 588.926f),
        // Character_Profile_Kalina_costume8.png
        new("outfit_08", new LocalisedText("WOMENACE::ui/kalina/outfit_08", "Lover's Discourse"),
            1024.0f, 1024.0f, 331.0f, 7.0f, 654.0f, 1016.0f, 53.964f, 28.0f, 192.071f, 600.0f),
        // Character_Profile_Kalina_costume10.png
        new("outfit_09", new LocalisedText("WOMENACE::ui/kalina/outfit_09", "Summer Night Dream Giver"),
            1024.0f, 1024.0f, 213.0f, 10.0f, 786.0f, 1017.0f, 10.0f, 28.0f, 280.0f, 492.077f),
        // 1920px-Character_Profile_Kalina_costume11.png
        new("outfit_10", new LocalisedText("WOMENACE::ui/kalina/outfit_10", "Primly Professional"),
            1920.0f, 1920.0f, 687.0f, 18.0f, 1387.0f, 1903.0f, 38.594f, 28.0f, 222.812f, 600.0f),
        // 1920px-Character_Profile_Kalina_costume12.png
        new("outfit_11", new LocalisedText("WOMENACE::ui/kalina/outfit_11", "At Your Service"),
            1920.0f, 1920.0f, 601.0f, 11.0f, 1456.0f, 1920.0f, 15.636f, 28.0f, 268.727f, 600.0f),
        // Armed_Kalina.png
        new("outfit_12", new LocalisedText("WOMENACE::ui/kalina/outfit_12", "Adjutant's Frontline"),
            1024.0f, 1024.0f, 334.0f, 5.0f, 763.0f, 1017.0f, 22.826f, 28.0f, 254.348f, 600.0f),
        // News_Reporter_Kalina.png
        new("outfit_13", new LocalisedText("WOMENACE::ui/kalina/outfit_13", "Hot Scoop"),
            1024.0f, 1024.0f, 341.0f, 44.0f, 683.0f, 1007.0f, 43.458f, 28.0f, 213.084f, 600.0f),
        // 800px-GFL2_Kalina.png
        new("outfit_14", new LocalisedText("WOMENACE::ui/kalina/outfit_14", "Kalina 2"),
            800.0f, 1833.0f, 1.0f, 1.0f, 672.0f, 1831.0f, 40.0f, 28.0f, 220.0f, 600.0f),
        // Kalinyan.png
        new("outfit_15", new LocalisedText("WOMENACE::ui/kalina/outfit_15", "Kalinyan"),
            672.0f, 868.0f, 22.0f, 31.0f, 670.0f, 867.0f, 33.732f, 132.0f, 232.536f, 300.0f, -50f),
    };

    public static readonly Outfit[] CurioOutfits =
    {
        new("perlica", new LocalisedText("WOMENACE::ui/kalina/perlica", "Perlica"),
            1346f, 1060f, 313f, 20f, 1097f, 1060f, -10f, 16f, 320f, 424.49f),
    };

    public static IEnumerable<Outfit> Outfits => AffinityOutfits.Concat(CurioOutfits);
    public static IEnumerable<Outfit> VisibleOutfits(ProcurementState procurement) =>
        AffinityOutfits.Concat(CurioOutfits.Where(outfit => procurement?.Claimed(outfit.RewardId) > 0));

    public sealed class Outfit(string id, LocalisedText name,
        float sourceWidth, float sourceHeight,
        float cropLeft, float cropTop, float cropRight, float cropBottom,
        float left, float top, float width, float height, float thumbnailTop = 0f)
    {
        public readonly string Id = id;
        public readonly LocalisedText Name = name;
        public int Level => Array.IndexOf(AffinityOutfits, this) + 1;
        public string RewardId => "outfit.wmgfl_kalina_" + Id;
        public string Asset => "kalina__" + Id;
        public bool IsUnlocked(KalinaState affinity, ProcurementState procurement) =>
            Level > 0 ? Level <= affinity.Level : procurement?.Claimed(RewardId) > 0;
        public readonly float ThumbnailTop = thumbnailTop;
        public readonly float SourceWidth = sourceWidth, SourceHeight = sourceHeight;
        public readonly float CropLeft = cropLeft, CropTop = cropTop, CropRight = cropRight, CropBottom = cropBottom;
        public readonly float Left = left, Top = top, Width = width, Height = height;
    }
}

public sealed class KalinaState
{
    public long SardisSpent { get; set; }
    public string OutfitId { get; set; } = "default";

    public int Affinity => (int)Math.Min(int.MaxValue, SardisSpent / Kalina.SardisPerAffinityPoint);
    public int Level => (int)Math.Min(Kalina.MaxLevel, 1L + Affinity / Kalina.AffinityPerLevel);
}
