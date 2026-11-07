using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.TileEffects;
using UnityEngine;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Joint Investigation, Welrod's tier 2 perk, in two parts.
//
// The intel: every mission of an operation she deploys on raises the Intelligence strategy
// variable (the "Intelligence level" mission prep reads) by IntelPerMission, and the operation's
// end takes back everything she added. JointInvestigationSystem counts what it added in the save.
//
// The questioning: the perk hands her active.welrod_joint_investigation each mission. Aimed at a
// civilian or neutral unit within the skill's range that has not been questioned this mission,
// it reveals Reveals random enemies the player has not detected: each one's tile gets the reveal
// that Show of Force spreads over an area (tile_effect.reveal_map, two rounds), and the camera
// glides to the first of them.
[JiangyuType("JointInvestigation")]
public sealed partial class JointInvestigation : SkillEventHandlerTemplate
{
    public int IntelPerMission = 2;

    public override SkillEventHandler Create() => new JointInvestigationHandler();
}

[JiangyuType("JointInvestigationHandler")]
public sealed partial class JointInvestigationHandler : SkillEventHandler
{
}

[JiangyuType("Interrogate")]
public sealed partial class Interrogate : SkillEventHandlerTemplate
{
    public int Reveals = 2;

    public override SkillEventHandler Create() => new InterrogateHandler { Reveals = Reveals };
}

[JiangyuType("InterrogateHandler")]
public sealed partial class InterrogateHandler : SkillEventHandler
{
    public int Reveals = 2;

    public override bool OnVerifyTarget(Tile _originTile, Tile _targetTile)
    {
        try
        {
            return InReach(_targetTile) && JointInvestigationSystem.CanQuestion(TileActors.TargetOn(_targetTile));
        }
        catch
        {
            return false;
        }
    }

    // The skill template's MaxRange from the questioner. The skill is cloned from an offmap ability,
    // which the game lets target anywhere on the map, so the reach is checked here against the
    // template's number (an offmap skill instance may not report it).
    private bool InReach(Tile target)
    {
        var from = GetActor()?.GetTile();
        var range = (ParentSkill?.GetTemplate()?.TryCast<SkillTemplate>()?.MaxRange) ?? 0;
        return from != null && target != null && Pierce.Distance(from, target) <= range;
    }

    public override void OnUse(Actor _user, Tile _targetTile, UsageParameter _usageParams, ref bool _applyToTile)
    {
        try
        {
            var witness = TileActors.TargetOn(_targetTile);
            if (_user == null || !InReach(_targetTile) || !JointInvestigationSystem.CanQuestion(witness))
                return;
            JointInvestigationSystem.Question(witness, _user, Reveals);
        }
        catch (Exception ex)
        {
            Log.Warn($"joint investigation: questioning failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// What her missions have added to Intelligence this operation.
public sealed class JointInvestigationState
{
    public int IntelAdded { get; set; }
}

public sealed class JointInvestigationSystem : JiangyuSystem
{
    private const string RevealEffectId = "tile_effect.reveal_map";
    // How long the camera follows the first revealed enemy before handing control back.
    private const float FollowSeconds = 2f;

    private static JointInvestigationSystem _instance;
    private static readonly HashSet<IntPtr> Questioned = new();
    private static readonly System.Random Dice = new();

    private JointInvestigationState State => Context.State.Get<JointInvestigationState>();

    public override void OnInit()
    {
        _instance = this;
        Context.Patches.Postfix("Il2CppMenace.Strategy.Operation", "EndMission", OnMissionEnded);
        Context.Patches.Postfix("Il2CppMenace.Strategy.Roster", "OnOperationFinished", OnOperationFinished);
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName) => Questioned.Clear();

    public override void OnUnload()
    {
        Questioned.Clear();
        _instance = null;
    }

    // A living civilian or neutral unit not questioned this mission.
    internal static bool CanQuestion(Actor witness)
    {
        if (witness == null || !witness.IsAlive() || Questioned.Contains(witness.Pointer))
            return false;
        var faction = witness.GetFaction();
        return faction == FactionType.Civilian || faction == FactionType.Neutral;
    }

    internal static void Question(Actor witness, Actor questioner, int reveals)
    {
        Questioned.Add(witness.Pointer);
        var player = TacticalManager.Get()?.GetPlayerFaction();
        if (player == null)
            return;
        var hidden = new List<Actor>();
        TacticalManager.Get().ForEachActor((Il2CppSystem.Action<Actor>)(Action<Actor>)(a =>
        {
            if (a != null && a.IsAlive() && Pierce.IsHostileTo(questioner, a) && !a.IsDetectedByFaction(player.GetIndex()))
                hidden.Add(a);
        }));
        var reveal = Templates.ById<TileEffectTemplate>(RevealEffectId);
        var revealed = new List<Actor>();
        for (var i = 0; i < reveals && hidden.Count > 0; i++)
        {
            var pick = hidden[Dice.Next(hidden.Count)];
            hidden.Remove(pick);
            if (pick.GetTile() is not { } tile)
                continue;
            if (reveal?.Create(0f) is { } effect)
                tile.AddEffect(effect);
            else
                pick.SetDetectedByFaction(player.GetIndex());
            revealed.Add(pick);
        }
        Show(revealed);
    }

    // Glides the camera to the first revealed enemy the way the game pans to units on the AI's
    // turn (TacticalCamera.FollowActor drives the auto camera), then lets go so the player has the
    // camera back. Focus would snap there instead.
    private static void Show(List<Actor> revealed)
    {
        try
        {
            var camera = TacticalManager.Get()?.GetCamera();
            if (camera == null || revealed.Count == 0 || _instance == null)
                return;
            camera.FollowActor(revealed[0]);
            _instance.Context.Coroutines.Start(Release(camera, revealed[0]));
        }
        catch (Exception ex)
        {
            _instance?.Context.Log.Warn($"joint investigation: camera failed: {ex.Message}");
        }
    }

    private static System.Collections.IEnumerator Release(TacticalCamera camera, Actor followed)
    {
        yield return SpecialWeaponCarry.Wait(FollowSeconds);
        try
        {
            if (camera != null && camera.GetFollowedActor()?.Pointer == followed.Pointer)
                camera.CancelAutoCamera();
        }
        catch { }
    }

    // The intel per mission from the perk's handler, when the leader has it.
    private static int? IntelFor(BaseUnitLeader leader)
    {
        var perks = leader?.m_Perks;
        for (var i = 0; perks != null && i < perks.Count; i++)
        {
            var handlers = perks[i]?.EventHandlers;
            for (var j = 0; handlers != null && j < handlers.Count; j++)
                if (handlers[j]?.TryCast<JointInvestigation>() is { } perk)
                    return perk.IntelPerMission;
        }
        return null;
    }

    private void OnMissionEnded(PatchInfo info)
    {
        try
        {
            var strategy = StrategyState.Get();
            var roster = strategy?.Roster;
            var hired = roster?.m_HiredLeaders;
            for (var i = 0; hired != null && i < hired.Count; i++)
            {
                var leader = hired[i];
                if (leader == null || IntelFor(leader) is not { } intel || !roster.WasLeaderDeployed(leader))
                    continue;
                // What the change actually added, so a capped variable is not over-refunded later.
                var before = strategy.GetVarRaw(StrategyVars.Intelligence);
                strategy.ChangeVar(StrategyVars.Intelligence, intel, true, true, false);
                var added = strategy.GetVarRaw(StrategyVars.Intelligence) - before;
                State.IntelAdded += added;
                Context.Log.Info($"joint investigation: {leader.LeaderTemplate?.GetID()} added {added} intelligence ({State.IntelAdded} this operation)");
            }
        }
        catch (Exception ex) { Context.Log.Warn($"joint investigation: mission end failed: {ex.Message}"); }
    }

    private void OnOperationFinished(PatchInfo info)
    {
        try
        {
            if (State.IntelAdded == 0)
                return;
            StrategyState.Get()?.ChangeVar(StrategyVars.Intelligence, -State.IntelAdded, false, true, false);
            Context.Log.Info($"joint investigation: operation over, {State.IntelAdded} intelligence taken back");
        }
        catch (Exception ex) { Context.Log.Warn($"joint investigation: operation end failed: {ex.Message}"); }
        finally { State.IntelAdded = 0; }
    }
}
