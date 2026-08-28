using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppInterop.Runtime.InteropTypes;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The aim ledger behind Cheyanne's imprint, and the interrupt that puts the aim
// trainer in front of every shot she fires.
//
// The banked value is the last round's POINTS, uncapped, and belongs to one
// actor for one mission. In Cheyanne's hands it is the whole of the ricochet,
// not a bonus on one: zero points means zero bounces. BounceChain reads it
// per shot at 50 points a bounce and 100 a tile.
//
// The score is Cheyanne's alone. It is gated on the wielder's character tag
// rather than on the ledger being empty, so a recycled actor pointer in a later
// mission can never hand somebody else her aim. That same gate is what tells a
// non-owner (who takes the weapon's own fixed ricochet) apart from Cheyanne
// before the trainer has run (who takes none at all).
public sealed class CheyanneAimSystem : JiangyuSystem
{
    public const string OwnerTag = "wmgfl_cheyanne";

    private static CheyanneAimSystem _instance;

    // The Actor wrapper is retained alongside the pointer key so the address
    // cannot be recycled to a different actor mid-mission, the same pattern
    // ElementsSystem's gauges and VectorSsrSystem's Overburn ledger use.
    private sealed class Aim
    {
        public Actor Actor;
        public int Points;
    }

    private readonly Dictionary<IntPtr, Aim> _scores = new();

    public override void OnInit()
    {
        _instance = this;
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.Skill", "Use", OnSkillUsePre);
        // The commit pin must not outlive the Use call that set it. The OnUse
        // postfix clears it on every shot that commits, but a use the engine
        // refuses never reaches OnUse, and a lingering pin would trim every
        // hover preview to a single tile until the next trigger pull.
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "Use", OnSkillUsePost);
        // While the trainer is up the game must not see the mouse at all:
        // this is the exact entry point every tactical click routes through
        // (it is the frame under the old ApplyToTile crash), and skipping it
        // is what keeps a trainer click from also clicking a tile underneath.
        Context.Patches.Prefix("Il2CppMenace.States.TacticalState", "HandleMouseInput", OnTacticalMouse);
    }

    private static void OnTacticalMouse(PatchInfo info)
    {
        if (CheyanneAimTrainer.IsOpen)
            info.Skip = true;
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        _scores.Clear();
        _armed.Clear();
        BounceChainAoEShape.UseCommitTile = null;
        CheyanneAimTrainer.Close();
        CheyanneAimSound.Forget();
    }

    public static int PointsFor(Actor actor)
    {
        var self = _instance;
        if (self == null || actor == null || !IsOwner(actor))
            return 0;
        return self._scores.TryGetValue(actor.Pointer, out var aim) ? aim.Points : 0;
    }

    public static void SetPoints(Actor actor, int points)
    {
        var self = _instance;
        if (self == null || actor == null || !IsOwner(actor))
            return;
        if (!self._scores.TryGetValue(actor.Pointer, out var aim))
            self._scores[actor.Pointer] = aim = new Aim { Actor = actor };
        aim.Points = Math.Max(0, points);
    }

    // Cheyanne herself, or the weapons-bay carrier with this SSR slotted:
    // the imprint follows the weapon into the bay, trainer and all, so a
    // bay shot aims exactly the way Cheyanne's does (zero points, zero
    // bounces, the trainer in front of every trigger pull).
    internal static bool IsOwner(Actor actor)
        => SsrImprintSystem.IsOwningActor(actor, OwnerTag, CheyanneSsrShapeSystem.SkillId);

    // Who is firing. A skill exposes several handles onto its wielder and a
    // combat one does not always answer on the first, so they are tried in
    // turn, the same fallback chain SsrImprintSystem's WielderCandidates walks.
    private static Actor FirerOf(Skill skill)
    {
        if (skill == null)
            return null;
        return skill.GetActor()
            ?? skill.GetEntity()?.TryCast<Actor>()
            ?? (skill.GetOwner() as Il2CppObjectBase)?.TryCast<Actor>();
    }

    // Every Cheyanne on the field, for the dev verbs.
    internal static List<Actor> OwnersOnField()
    {
        var found = new List<Actor>();
        var factions = TacticalManager.Get()?.GetFactions();
        for (var i = 0; factions != null && i < factions.Length; i++)
        {
            var actors = factions[i]?.GetActors();
            for (var j = 0; actors != null && j < actors.Count; j++)
                if (actors[j] != null && actors[j].IsAlive() && IsOwner(actors[j]))
                    found.Add(actors[j]);
        }
        return found;
    }

    internal object DevState()
    {
        var owners = OwnersOnField();
        return new
        {
            trainerOpen = CheyanneAimTrainer.IsOpen,
            cheyannesOnField = owners.Count,
            scores = owners.ConvertAll(a => new
            {
                actor = a.GetTemplate()?.GetID(),
                points = PointsFor(a),
                shot = Describe(BounceChain.For(a)),
            }),
        };
    }

    private static string Describe(BounceChain.Shot shot)
        => $"{shot.Bounces} bounces, {shot.Range} tiles";

    // Two jobs, in order: put the trainer in front of Cheyanne's shot, and pin
    // the commit for any shot that goes ahead. The pin is load-bearing for
    // everyone who fires this weapon, owner or not: without it the commit's
    // own preview-style query walks the whole chain at trigger pull, and every
    // victim takes the hit once at the bang and once more when the sequenced
    // round arrives.
    private void OnSkillUsePre(PatchInfo info)
    {
        try
        {
            var skill = (info.Instance as Il2CppSystem.Object)?.TryCast<Skill>();
            var id = skill?.GetID();
            if (id == null || !Calibration.TryParseRank(id, CheyanneSsrShapeSystem.SkillId, out _))
                return;
            // A use that never reached its OnUse (refused by the engine, or
            // skipped for the trainer) must not leave its pin behind.
            BounceChainAoEShape.UseCommitTile = null;
            var firer = FirerOf(skill);
            if (TryInterrupt(info, skill, firer))
                return;
            var tile = ((info.Args is { Count: > 0 } ? info.Args[0] : null) as Il2CppSystem.Object)?.TryCast<Tile>();
            BounceChainAoEShape.UseCommitTile = tile;
            Context.Log.Debug($"cheyanne ricochet: use by '{firer?.GetTemplate()?.GetID()}' (owner={IsOwner(firer)})");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"cheyanne aim: interrupt failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnSkillUsePost(PatchInfo info)
    {
        try
        {
            var skill = (info.Instance as Il2CppSystem.Object)?.TryCast<Skill>();
            var id = skill?.GetID();
            if (id == null || !Calibration.TryParseRank(id, CheyanneSsrShapeSystem.SkillId, out _))
                return;
            BounceChainAoEShape.UseCommitTile = null;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"cheyanne aim: pin clear failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Set while a trainer-won shot is being re-fired, so the prefix lets that
    // one through instead of opening a second trainer on top of it.
    private bool _refiring;

    // Actors whose trainer round is banked but whose re-fire did not take. They
    // get one free shot: the next Use passes straight through carrying the
    // score they just earned. This is the fallback for the seam that could not
    // be verified offline, the loader's prefix dispatcher having no way to
    // return true from a skipped Use, so a cancelled shot reads to the caller
    // as "the skill did not fire". If the re-fire works nobody ever lands here.
    private readonly HashSet<IntPtr> _armed = new();

    // Cheyanne aims before she shoots. Returns true when the shot has been
    // taken over: cancelled now, re-fired when the round ends.
    private bool TryInterrupt(PatchInfo info, Skill skill, Actor firer)
    {
        if (_refiring || !IsOwner(firer) || CheyanneAimTrainer.IsOpen)
            return false;
        if (_armed.Remove(firer.Pointer))
            return false;   // this is the fallback shot, let it fly

        var tile = ((info.Args is { Count: > 0 } ? info.Args[0] : null) as Il2CppSystem.Object)?.TryCast<Tile>();
        var parameters = info.Args is { Count: > 1 } ? info.Args[1] : null;
        if (tile == null)
            return false;

        var opened = CheyanneAimTrainer.Open(
            Context,
            points =>
            {
                SetPoints(firer, points);
                Refire(skill, tile, parameters);
            },
            () => Context.Log.Info("cheyanne aim: round cancelled, no shot fired"));
        if (!opened)
            return false;   // no screen to draw on: let the shot go unaimed

        info.Skip = true;
        return true;
    }

    // Fire the shot the trainer stood in front of. The guard makes the prefix
    // pass this one through; if the engine refuses it we arm the actor so the
    // player's next click spends the score rather than losing it.
    private void Refire(Skill skill, Tile tile, object parameters)
    {
        var fired = false;
        _refiring = true;
        try
        {
            fired = skill.Use(tile, parameters is UsageParameter p ? p : default);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"cheyanne aim: re-fire threw: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _refiring = false;
        }

        if (fired)
            return;
        var actor = FirerOf(skill);
        if (actor != null)
            _armed.Add(actor.Pointer);
        Context.Log.Warn("cheyanne aim: re-fire refused, next shot passes through with the banked score");
    }

    internal static CheyanneAimSystem Instance => _instance;
}
