using Il2CppMenace.Audio;
using Il2CppMenace.UI;
using Il2CppStem;

namespace WOMENACE.Code;

public sealed partial class StrategyDiorama
{
    // What the table has to say this frame. Several things change at once when a screen does (one
    // hologram goes, another comes, the camera sets off), and one sound is played for all of it:
    // the one that ranks highest here.
    private enum TableCue { None, Travel, Vanish, Appear }

    private TableCue _tableCue;
    // The ops room's bed, looped for as long as a room is showing.
    private SoundInstance _roomBed;
    // When the bed was last asked for. A bed that will not play is asked for again a second on,
    // never every frame.
    private float _roomBedAskedAt = float.NegativeInfinity;

    private void Cue(TableCue cue)
    {
        if (cue > _tableCue)
            _tableCue = cue;
    }

    private void DriveTableSounds()
    {
        var cue = _tableCue;
        _tableCue = TableCue.None;
        var listening = _room != null && _roomShown && !_tableVideo;
        try
        {
            if (listening && cue != TableCue.None)
                TableSounds.Play(cue == TableCue.Appear ? TableSounds.Appear : cue == TableCue.Vanish ? TableSounds.Vanish : TableSounds.Travel);
            if (!listening)
                StopRoomBed();
            else if ((_roomBed == null || (!_roomBed.Playing && !_roomBed.Paused)) && UnityEngine.Time.unscaledTime - _roomBedAskedAt > 1f)
            {
                _roomBedAskedAt = UnityEngine.Time.unscaledTime;
                _roomBed = TableSounds.Play(TableSounds.Room);
                if (_roomBed != null)
                    _roomBed.Looped = true;
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: table sounds failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The instance goes back to Stem's pool, so it is handed back as it came: not looped.
    private void StopRoomBed()
    {
        if (_roomBed != null && _roomBed.Playing)
        {
            _roomBed.Looped = false;
            _roomBed.Stop();
        }
        _roomBed = null;
    }

    // Vanilla starts a screen's own ambient sounds as the screen opens (UIScreen.AmbientSounds,
    // from UIManager.ApplyPermanentElements). A screen that shows the room has the room's bed, so
    // its own are taken off it and whatever is still sounding is stopped.
    private static void QuietVanillaAmbience(UIScreen screen)
    {
        if (screen.AmbientSounds is { Length: > 0 })
            screen.AmbientSounds = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<ID>(0);
        var audio = AudioManager.Get();
        if (audio != null && audio.m_CurrentAmbientSoundInstances is { Count: > 0 })
            audio.StopAmbientSounds();
    }
}
