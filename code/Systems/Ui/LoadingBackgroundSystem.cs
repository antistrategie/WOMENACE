using Jiangyu.Sdk;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Shows a GFL2 CG behind every loading screen in place of vanilla's backgrounds.
//
// LoadingOverlay.Show gives m_Background one of vanilla's loading_bg textures each time the
// overlay opens, and nothing sets it again before the overlay hides. The postfix paints over
// that choice, so vanilla's pick never reaches the screen. Show is non-virtual and takes only
// an int, which makes it safe to detour.
//
// The CGs are 2:1 and the screen is usually 16:9, so the image covers the element and loses a
// little off each side rather than leaving bars.
//
// Only the picture on screen is held. Each texture is loaded when it is shown, and the previous
// one is left for the next unload of unused assets, so the set costs one texture of memory.
public sealed class LoadingBackgroundSystem : JiangyuSystem
{
    private static readonly string[] Backgrounds =
    [
        "loading__cg_02_nemertea_attack",
        "loading__cg_17_fiercebattle_fight",
        "loading__cg_27_prototype",
        "loading__cg_45_thebarricade",
        "loading__cg_48_monster",
        "loading__cg_53_whalefall_01",
        "loading__cg_57_jupiter_02",
        "loading__peri_cg_01_obliterate",
        "loading__cheetapercg_01_stubborncheeta",
        "loading__lenecg_01_butterfly",
        "loading__lenecg_02_yesterdayoncemore",
        "loading__macqiatocg_03_snipe",
        "loading__bg_none_gf1cg_02",
        "loading__bg_none_gf1cg_03",
    ];

    // Shuffled and dealt in turn, so every picture shows once before any shows twice.
    private readonly List<int> _deck = [];
    private readonly System.Random _random = new();
    // Latched per picture: the overlay opens on every scene change.
    private readonly HashSet<int> _warnedMissing = [];
    private int _last = -1;
    private Texture2D _current;

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.UI.LoadingOverlay", "Show", OnShow);
    }

    private void OnShow(PatchInfo info)
    {
        try
        {
            var background = (info.Instance as Il2CppObjectBase)?.TryCast<LoadingOverlay>()?.m_Background;
            if (background == null)
                return;
            var texture = Next();
            if (texture == null)
                return;
            background.style.backgroundImage = new StyleBackground(texture);
            background.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Cover));
            background.style.backgroundPositionX = new StyleBackgroundPosition(new BackgroundPosition(BackgroundPositionKeyword.Center));
            background.style.backgroundPositionY = new StyleBackgroundPosition(new BackgroundPosition(BackgroundPositionKeyword.Center));
        }
        catch (Exception ex) { Context.Log.Warn($"loading background: swap failed: {ex}"); }
    }

    private Texture2D Next()
    {
        // A missing picture is skipped, so one bad asset cannot stop the rest from showing.
        for (var tries = 0; tries < Backgrounds.Length; tries++)
        {
            if (_deck.Count == 0)
                Shuffle();
            var index = _deck[^1];
            _deck.RemoveAt(_deck.Count - 1);
            var texture = Context.Assets.Load<Texture2D>(Backgrounds[index]);
            if (texture != null)
            {
                _last = index;
                return _current = texture;
            }
            if (_warnedMissing.Add(index))
                Context.Log.Warn($"loading background: '{Backgrounds[index]}' missing from the bundle");
        }
        return _current;
    }

    private void Shuffle()
    {
        for (var i = 0; i < Backgrounds.Length; i++)
            _deck.Add(i);
        for (var i = _deck.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (_deck[i], _deck[j]) = (_deck[j], _deck[i]);
        }
        // The deck is dealt from its end, so the new deck must not end on the picture just shown.
        if (_deck.Count > 1 && _deck[^1] == _last)
            (_deck[^1], _deck[0]) = (_deck[0], _deck[^1]);
    }
}
