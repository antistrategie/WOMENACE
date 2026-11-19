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


    private void Cue(TableCue cue)
    {
        if (cue > _life.CurrentTableCue)
            _life.CurrentTableCue = cue;
    }

    private void DriveTableSounds()
    {
        var cue = _life.CurrentTableCue;
        _life.CurrentTableCue = TableCue.None;
        var listening = _scene.Room != null && _roomShown && !_tableVideo;
        try
        {
            if (listening && cue != TableCue.None)
                TableSounds.Play(cue == TableCue.Appear ? TableSounds.Appear : cue == TableCue.Vanish ? TableSounds.Vanish : TableSounds.Travel);
            if (!listening)
                StopRoomBed();
            else if ((_life.RoomBed == null || (!_life.RoomBed.Playing && !_life.RoomBed.Paused)) && UnityEngine.Time.unscaledTime - _life.RoomBedAskedAt > 1f)
            {
                _life.RoomBedAskedAt = UnityEngine.Time.unscaledTime;
                _life.RoomBed = TableSounds.Play(TableSounds.Room);
                if (_life.RoomBed != null)
                    _life.RoomBed.Looped = true;
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
        // A paused bed is stopped too: dropped while paused, its looped instance would be held by
        // nothing and could be resumed by the game's own unpausing.
        if (_life.RoomBed != null && (_life.RoomBed.Playing || _life.RoomBed.Paused))
        {
            _life.RoomBed.Looped = false;
            _life.RoomBed.Stop();
        }
        _life.RoomBed = null;
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
