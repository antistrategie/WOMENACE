using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The KDL-facing Reinforcement Fairy handler. The offmap skill declares
//
//     append "EventHandlers" type="WOMENACE:ReinforceSquaddie" {}
//
// and using it on an allied Doll squad walks one squaddie into the line: a downed
// roster member first, otherwise an unassigned squaddie from the barracks
// pool. The game's own RefillSquaddies machinery picks the body and
// CreateElementFromSquaddie fields it, so save-side bookkeeping matches the
// vanilla mid-combat reinforcement perk.
[JiangyuType("ReinforceSquaddie")]
public sealed partial class ReinforceSquaddie : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create() => new ReinforceSquaddieHandler();
}

[JiangyuType("ReinforceSquaddieHandler")]
public sealed partial class ReinforceSquaddieHandler : SkillEventHandler
{
    public override bool OnVerifyTarget(Tile _originTile, Tile _targetTile)
    {
        try
        {
            return PickSquaddie(_targetTile, out _, out _) != Squaddies.INVALID_SQUADDIE_ID;
        }
        catch (Exception ex)
        {
            Log.Warn($"reinforce fairy: target check failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public override void OnUse(Actor _user, Tile _targetTile, UsageParameter _usageParams, ref bool _applyToTile)
    {
        try
        {
            var id = PickSquaddie(_targetTile, out var actor, out var fromPool);
            if (id == Squaddies.INVALID_SQUADDIE_ID || actor == null)
                return;
            var leader = actor.GetLeader();
            // a pool squaddie joins the roster first, so they persist with the
            // squad after the mission exactly like a vanilla refill
            if (fromPool && !leader.TryAddSquaddie(id))
            {
                Log.Warn($"reinforce fairy: roster refused squaddie {id} on '{leader.GetTemplate()?.GetID()}'");
                return;
            }
            var element = actor.CreateElementFromSquaddie(id);
            if (element == null)
            {
                Log.Warn($"reinforce fairy: no element created for squaddie {id} on '{leader.GetTemplate()?.GetID()}'");
                return;
            }
            // the vanilla refill handler's post-create sequence (disassembled from
            // RefillSquaddiesHandler.OnApply): sync the newcomer to the squad's stance,
            // recount hitpoints, and fire the event the overhead HP bar listens to
            element.OnStanceChanged(actor.GetStance(), true, false);
            actor.UpdateHitpoints();
            TacticalManager.Get()?.InvokeOnHitpointsChanged(actor, actor.GetHitpointsPct(), 0);
            Log.Debug($"reinforce fairy: squaddie {id} joined '{leader.GetTemplate()?.GetID()}' ({(fromPool ? "pool" : "downed")})");
        }
        catch (Exception ex)
        {
            Log.Warn($"reinforce fairy: use failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Choose who steps in on the given tile, without mutating anything: a
    // downed member of the squad's own roster first, then an unassigned
    // barracks squaddie if the roster still has room. INVALID_SQUADDIE_ID
    // means the tile holds no reinforceable squad.
    private static int PickSquaddie(Tile tile, out UnitActor actor, out bool fromPool)
    {
        actor = null;
        fromPool = false;
        var candidate = tile?.GetActor()?.TryCast<UnitActor>();
        var leader = candidate?.GetLeader();
        if (candidate == null || leader == null || !candidate.IsAlive())
            return Squaddies.INVALID_SQUADDIE_ID;
        // the fairy serves our Dolls only (a wmgfl speaker tag marks them),
        // solo dolls stay solo, and only infantry squads carry squaddies
        if (Affinity.CharacterTag(leader) == null)
            return Squaddies.INVALID_SQUADDIE_ID;
        if (leader.TryCast<SquadLeader>() == null || SoloSquadSystem.IsSolo(leader))
            return Squaddies.INVALID_SQUADDIE_ID;
        if ((candidate.GetElements()?.Count ?? int.MaxValue) >= Entity.MAX_ELEMENTS)
            return Squaddies.INVALID_SQUADDIE_ID;

        var downed = RefillSquaddiesHandler.GetNextDownedSquaddie(leader);
        if (downed != Squaddies.INVALID_SQUADDIE_ID)
        {
            actor = candidate;
            return downed;
        }

        var squaddies = StrategyState.Get()?.Squaddies;
        if (squaddies == null || (leader.m_SquaddieIds?.Count ?? int.MaxValue) >= leader.GetMaxValidSquaddies())
            return Squaddies.INVALID_SQUADDIE_ID;
        var pooled = RefillSquaddiesHandler.GetUnusedSquaddieId(leader, squaddies);
        if (pooled == Squaddies.INVALID_SQUADDIE_ID)
            return Squaddies.INVALID_SQUADDIE_ID;
        actor = candidate;
        fromPool = true;
        return pooled;
    }
}
