using Il2CppMenace.States;
using Il2CppMenace.Strategy.Missions;
using Il2CppStem;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// A 3rd-generation doll deployed with no other unit plays her own combat
// music instead of the mission's BackgroundMusic. Her track is the
// "<doll>_solo" track in ModMusic, named after her unit template
// (player_squad.<doll>). A doll whose track is missing keeps the vanilla
// music rather than silencing the mission.
// Leaders outside the solo-squad rules that still get a track, such as
// Voymastina's Sinbreaker pilot form, are listed in ExtraTracks.
//
// Vanilla starts combat music in TacticalState.OnMapReady with
// AudioManager.PlayMusic(mission BackgroundMusic, loop: true), and
// TacticalState.SetPaused requests the same id again when the pause menu
// closes. AudioManager.Update unpauses the current instance only when the
// requested id is the one already playing, so a track played over the
// mission's own id would be displaced and restarted on every resume. The
// solo track is therefore swapped into the mission template's
// BackgroundMusic for this mission, and both vanilla calls play and resume it
// as their own. The template is shared, so the swap is undone when the scene
// changes or the next mission starts.
//
// Looping is Stem's SoundInstance.Looped, which sets AudioSource.loop: the
// whole clip repeats end to start with no separate intro or loop points.
public sealed class SoloMusicSystem : JiangyuSystem
{
    private const string UnitTemplatePrefix = "player_squad.";

    // Leader template id to track stem (the track is "<stem>_solo"), for leaders
    // SoloSquadSystem does not classify.
    private static readonly Dictionary<string, string> ExtraTracks = new(StringComparer.Ordinal)
    {
        { "pilot.voymastina_mech", "voymastina_mech" },
    };

    private MissionTemplate _swappedTemplate;
    private ID _originalMusic;

    public override void OnInit()
    {
        Context.Patches.Prefix("Il2CppMenace.States.TacticalState", "OnMapReady", OnMapReady);
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName) => RestoreTemplate();

    private void RestoreTemplate()
    {
        if (_swappedTemplate == null)
            return;
        try
        {
            _swappedTemplate.BackgroundMusic = _originalMusic;
        }
        catch (Exception ex)
        {
            Context.Log.Error($"solo music: restoring the mission music failed: {ex}");
        }
        _swappedTemplate = null;
    }

    private void OnMapReady(PatchInfo info)
    {
        try
        {
            RestoreTemplate();
            var doll = LoneSoloDoll();
            if (doll == null)
                return;
            var template = StrategyState.Get()?.GetCurrentOperation()?.GetCurrentMission()?.GetTemplate();
            if (template == null)
                return;
            if (ModMusic.Load(Context, doll + "_solo") is not { } track)
                return;
            _originalMusic = template.BackgroundMusic;
            _swappedTemplate = template;
            template.BackgroundMusic = track;
            Context.Log.Info($"solo music: {doll} deployed alone");
        }
        catch (Exception ex)
        {
            RestoreTemplate();
            Context.Log.Error($"solo music: OnMapReady failed: {ex}");
        }
    }

    // The battle plan holds one DeployedEntity per deployed leader, vehicles included.
    // Returns the track name when the only leader deployed has one.
    private static string LoneSoloDoll()
    {
        var deployed = StrategyState.Get()?.BattlePlan?.m_EntitiesToDeploy;
        if (deployed is not { Count: 1 })
            return null;
        var leader = deployed[0]?.GetUnitLeader();
        if (leader == null)
            return null;
        if (ExtraTracks.TryGetValue(leader.LeaderTemplate?.GetID() ?? "", out var extra))
            return extra;
        if (!SoloSquadSystem.IsSolo(leader))
            return null;
        var template = leader.GetTemplate();
        var id = template.GetID() ?? template.name;
        return id.StartsWith(UnitTemplatePrefix, StringComparison.Ordinal) ? id[UnitTemplatePrefix.Length..] : null;
    }
}
