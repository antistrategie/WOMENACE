using Il2CppMenace.Audio;
using Il2CppStem;

namespace WOMENACE.Code;

// Kalina's own music while the shop is showing. The workshop screen requests
// the strategy music as it opens and the shop is shown straight after, in the
// same frame, so the shop track replaces it before AudioManager.Update starts
// anything. AudioManager keeps the displaced track paused as its previous
// instance, and requesting that id again on hide resumes it where it was.
public sealed partial class ShopSystem
{
    private const string ShopTrack = "kalina_shop";

    private ID? _musicBeforeShop;

    // Set when the shop track failed to load, so a missing clip warns once rather than
    // on every refresh of the shop.
    private bool _shopTrackMissing;

    private void SetShopMusic(bool show)
    {
        try
        {
            var audio = AudioManager.Get();
            if (audio == null)
                return;
            if (show)
            {
                if (_musicBeforeShop != null || _shopTrackMissing)
                    return;
                if (ModMusic.Load(Context, ShopTrack) is not { } track)
                {
                    _shopTrackMissing = true;
                    return;
                }
                // A request still waiting for Update is the music that would have played.
                var scheduled = audio.m_ScheduledMusicId;
                _musicBeforeShop = scheduled.IsValid() ? scheduled : audio.m_CurrentMusicId;
                audio.PlayMusic(track, true);
            }
            else if (_musicBeforeShop is { } previous)
            {
                _musicBeforeShop = null;
                if (previous.IsValid())
                    audio.PlayMusic(previous, true);
            }
        }
        catch (Exception ex)
        {
            _musicBeforeShop = null;
            Context.Log.Error($"shop music: {ex}");
        }
    }
}
