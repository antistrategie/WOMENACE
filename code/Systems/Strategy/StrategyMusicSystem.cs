using Il2CppMenace.Audio;
using Il2CppStem;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The music of the title screen and the strategy layer is the mod's own (ModMusic). Vanilla
// plays whatever its audio config names for the title, the system map and mission prep, so
// the title's name is given the mod's track and vanilla's own call plays it.
// In the strategy layer vanilla chooses by whether an operation is on, not by what is on
// screen: whenever a screen opens it asks for the system map's track with none, and for
// mission prep's on every screen of one. The diorama says what is showing (Drive): mission
// prep's track plays while a mission is planned, and otherwise the track of the room. Both of
// vanilla's names are kept on that track, so vanilla asks for what is already playing, which
// changes nothing. A request for any other track would break the music at every screen, for
// the frame it took to ask for the right one again.
public sealed class StrategyMusicSystem : JiangyuSystem
{
    internal enum Room { Ops, Refitting, Workshop }

    private const string Title = "title";
    private const string MissionPrep = "mission_prep";
    private static readonly string[] RoomTracks = { "ops_room", "refitting", "workshop" };

    private static StrategyMusicSystem _instance;
    // The rooms' tracks, each loaded the first time its room is shown, and whether that has
    // been tried: a clip that is missing is looked for once.
    private readonly ID?[] _rooms = new ID?[RoomTracks.Length];
    private readonly bool[] _tried = new bool[RoomTracks.Length];
    private ID? _prep;
    // Vanilla's audio config, held once the ops room's track is in it. Null leaves the music
    // to vanilla.
    private AudioConfig _config;
    // The track both of vanilla's strategy names are on, and the one last asked for here.
    private ID? _named;

    public override void OnInit() => _instance = this;

    public override void OnUnload() => _instance = null;

    public override void OnTemplatesApplied()
    {
        Array.Clear(_rooms);
        Array.Clear(_tried);
        (_prep, _config, _named) = (null, null, null);
        var config = Templates.ById<AudioConfig>("audio_config", message => Context.Log.Warn($"strategy music: {message}"));
        if (config == null)
            return;
        // A track that is missing keeps vanilla's.
        if (ModMusic.Load(Context, Title) is { } title)
            config.MusicTitleScreen = title;
        if (ModMusic.Load(Context, MissionPrep) is { } prep)
        {
            _prep = prep;
            config.MusicMissionPrep = prep;
        }
        if (Track(Room.Ops) is { } ops)
        {
            config.MusicSystemMap = ops;
            _config = config;
        }
    }

    // Plays the track for what the strategy layer is showing. It is asked every frame and does
    // something when the answer changes: vanilla's names are put on the track, and it is
    // asked for when another of the strategy layer's tracks is playing or about to. Anything
    // else that is playing (a result's music) is vanilla's, and vanilla asks for the named
    // track itself when that is over.
    internal static void Drive(bool planning, Room room)
    {
        var system = _instance;
        if (system?._config == null)
            return;
        if ((planning ? system._prep : system.Track(room)) is not { } wanted || (system._named is { } named && Same(named, wanted)))
            return;
        var audio = AudioManager.Get();
        if (audio == null)
            return;
        system.Name(wanted);
        var scheduled = audio.m_ScheduledMusicId;
        var playing = scheduled.IsValid() ? scheduled : audio.m_CurrentMusicId;
        if (system.IsStrategyTrack(playing) && !Same(wanted, playing))
            audio.PlayMusic(wanted, true);
    }

    // With no room showing, both of vanilla's names are on the ops room's track: the strategy
    // layer opens on that room, so what vanilla asks for as the next scene comes in is what
    // the room will want.
    internal static void Rest()
    {
        var system = _instance;
        if (system?._config != null && system._rooms[(int)Room.Ops] is { } ops)
            system.Name(ops);
    }

    private void Name(ID track)
    {
        _named = track;
        _config.MusicSystemMap = track;
        _config.MusicMissionPrep = track;
    }

    private ID? Track(Room room)
    {
        var index = (int)room;
        if (!_tried[index])
        {
            _tried[index] = true;
            _rooms[index] = ModMusic.Load(Context, RoomTracks[index]);
        }
        return _rooms[index];
    }

    private bool IsStrategyTrack(ID id)
    {
        if (_prep is { } prep && Same(prep, id))
            return true;
        foreach (var room in _rooms)
            if (room is { } track && Same(track, id))
                return true;
        return false;
    }

    private static bool Same(ID a, ID b) => a.bankId == b.bankId && a.itemId == b.itemId;
}
