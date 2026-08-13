using Il2CppMenace.Tactical;
using UnityEngine;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Cheyanne's SSR kit: one shot that ricochets between enemies.
//
// The chain is a native AoE shape, the same seam Sextans' pierce swathe uses.
// With it assigned the game itself computes the affected tiles, so the
// targeting UI highlights the whole ricochet on hover and the application pass
// hits it, with no manual re-application anywhere.
//
// The chain walks nearest-hostile-first from the aimed tile: each link looks
// for the closest living hostile within BounceRange of the previous link that
// the chain has not already touched. Ties break toward the weaker target, so a
// crowded chain finishes wounded enemies rather than spreading evenly.
//
// Visibility is deliberately NOT a condition. The round ricochets into the fog
// and hits what it finds there, which is the point: a shot into a dark doorway
// that goes on to hurt three things you cannot see is the fantasy, and the
// damage numbers coming back out of the dark are the reward for taking it.
//
// How far that chain reaches depends on who is holding the rifle, and the two
// cases do not share a floor. Anyone else gets the weapon's own fixed ricochet.
// In Cheyanne's hands the ricochet IS her aim-trainer score: it starts at zero
// bounces and she earns every one of them, so a bad round is a plain single
// shot and a perfect round beats what the weapon does for anybody else.
//
// Nothing here rolls to hit and nor does the engine: the skill is
// IsAlwaysHitting. The aim trainer already asked whether the shot was lined up,
// and answering that twice would only take away a shot the player earned. So
// the shape decides where the round GOES and the trainer decides how far, and
// neither of them decides whether it lands, because it always does.
internal static class BounceChain
{
    // The fixed ricochet anyone else carrying the rifle gets. The weapon does
    // this much on its own, with no trainer and nothing to earn.
    public const int BaseBounces = 2;
    public const int BaseRange = 3;

    // The aim trainer's exchange rate. Every hit is worth PointsPerHit, a
    // bounce costs PointsPerBounce and a tile of reach costs PointsPerTile,
    // and there is NO ceiling: a monster round buys a monster shot. In her
    // hands the ricochet is the trainer's points and nothing else, scaled
    // from zero, so a round she scores nothing on ricochets nowhere.
    // The trainer's exchange rate, tuned against real play so an average
    // round beats the loaner rifle's fixed chain and a bad one does not.
    // Bounces and reach scale at the same rate, uncapped.
    public const int PointsPerHit = 5;
    public const int PointsPerBounce = 10;
    public const int PointsPerTile = 10;

    internal readonly record struct Shot(int Bounces, int Range);

    public static Shot For(Actor attacker)
    {
        // Ownership, not the score, is what picks the model. Reading a zero
        // score as "the base shot" is what wrongly gave her a floor of three
        // free bounces before the trainer had said anything.
        if (!CheyanneAimSystem.IsOwner(attacker))
            return new Shot(BaseBounces, BaseRange);
        return ForPoints(CheyanneAimSystem.PointsFor(attacker));
    }

    // What a round's points buy, with no actor to read them off. The trainer's
    // results card shows the player exactly this before the shot goes off.
    // Reach floors at one tile the moment there is any bounce at all, because
    // a bounce with zero reach could never find a target.
    public static Shot ForPoints(int points)
    {
        var bounces = Math.Max(0, points) / PointsPerBounce;
        var range = Math.Max(0, points) / PointsPerTile;
        if (bounces > 0 && range < 1)
            range = 1;
        return new Shot(bounces, range);
    }

    // Walks the chain from the aimed tile and hands every link to `add`,
    // returning how many links there are, the primary included.
    //
    // Preview and application both come through here, so the chain the player
    // sees highlighted on hover and the chain that gets hit cannot disagree.
    public static int Walk(Actor attacker, Tile target, Shot shot, Action<Tile, bool> add)
    {
        if (target == null)
            return 0;
        var visited = new HashSet<IntPtr>();
        var primary = target.GetEntity()?.TryCast<Actor>();
        if (primary != null)
            visited.Add(primary.Pointer);

        add?.Invoke(target, true);
        var links = 1;

        var current = target;
        for (var bounce = 1; bounce <= shot.Bounces; bounce++)
        {
            var next = NextLink(attacker, current, shot.Range, visited);
            if (next == null)
                break;
            visited.Add(next.Pointer);
            var tile = next.GetTile();
            if (tile == null)
                break;
            add?.Invoke(tile, false);
            links++;
            current = tile;
        }
        return links;
    }

    // The closest living hostile to `from` within `range`, skipping any tile
    // the chain already holds. Ties on distance break toward lower hitpoints.
    public static Actor NextLink(Actor attacker, Tile from, int range, HashSet<IntPtr> visited)
    {
        if (from == null)
            return null;
        Actor best = null;
        var bestDistance = int.MaxValue;
        var bestHp = int.MaxValue;
        var factions = TacticalManager.Get()?.GetFactions();
        for (var i = 0; factions != null && i < factions.Length; i++)
        {
            var actors = factions[i]?.GetActors();
            for (var j = 0; actors != null && j < actors.Count; j++)
            {
                var candidate = actors[j];
                if (candidate == null || !candidate.IsAlive() || visited.Contains(candidate.Pointer))
                    continue;
                // Fail closed on an unresolvable attacker: an unknown shooter
                // must ricochet into nobody rather than everybody.
                if (attacker == null || !Pierce.IsHostileTo(attacker, candidate))
                    continue;
                var tile = candidate.GetTile();
                if (tile == null)
                    continue;
                var distance = Pierce.Distance(from, tile);
                if (distance > range)
                    continue;
                var hp = RallyBarsSystem.SumHitpoints(candidate);
                if (distance > bestDistance || (distance == bestDistance && hp >= bestHp))
                    continue;
                best = candidate;
                bestDistance = distance;
                bestHp = hp;
            }
        }
        return best;
    }
}

// The ricochet as a native AoE shape. Assigned to the skill by
// CheyanneSsrShapeSystem below, because CustomAoEShape is an Odin-serialised
// interface field KDL cannot author.
[JiangyuType("BounceChainAoEShape", Interfaces = new[] { typeof(ICustomAoEShape) })]
public sealed partial class BounceChainAoEShape : Il2CppSystem.Object
{
    // Not the chain's reach: GetAffectedTiles carries that. The radius only
    // feeds the generic circle indicator drawn around the hovered target, which
    // reads as a ring of red tiles and means nothing for a ricochet. Zero
    // suppresses it.
    public int GetAoERadius() => 0;

    // The aim point is a real target, never a snapped direction, so the clicked
    // tile stands.
    public Tile GetOverrideTargetTile(Tile _origin, Tile _target) => _target;

    // While the native Use is committing, every shape query answers with just
    // the aimed tile. The commit runs one more preview-style query and cues
    // per-victim reactions (the under-fire flinch and hurt bark) off its
    // result, so a full-chain answer had every victim squealing at the start
    // while the damage arrived one by one. Set by the Use prefix, cleared by
    // the OnUse postfix before the sequence begins; hover previews between
    // uses see it null and keep the full chain highlight.
    internal static Tile UseCommitTile;

    // While the ricochet sequence is replaying the chain link by link, this
    // holds the one tile the in-flight ApplyToTile is meant to strike.
    // ApplyToTile re-queries the shape, so without this the sequence's
    // single-link application would recompute a whole fresh chain centred on
    // the link and hit everything again.
    internal static Tile ApplyExactly;

    public void GetAffectedTiles(Tile _origin, Tile _target, Il2CppSystem.Collections.Generic.List<Tile> _into, bool _lineOfFireNeeded, bool _skipEmptyTiles)
    {
        try
        {
            if (_target == null || _into == null)
                return;

            // A sequenced strike owns EVERY pass while it is in flight, the
            // preview-style one included: ApplyToTile expands its tiles
            // through the pass with _skipEmptyTiles false (the instrumented
            // run showed no application-pass query at strike time at all), so
            // answering only the application pass handed each strike the full
            // chain and every link's damage landed on the first arrival.
            if (ApplyExactly != null)
            {
                _into.Add(ApplyExactly);
                return;
            }

            // The native use's own application applies nothing: the ricochet
            // sequence replays the chain itself, one link at a time, with the
            // round's travel between them.
            if (_skipEmptyTiles)
                return;

            if (UseCommitTile != null)
            {
                _into.Add(UseCommitTile);
                return;
            }

            var attacker = _origin?.GetEntity()?.TryCast<Actor>();
            BounceChain.Walk(attacker, _target, BounceChain.For(attacker), (tile, _) => _into.Add(tile));
        }
        catch (Exception ex)
        {
            Log.Error($"[BounceChain] GetAffectedTiles failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// The round in flight. The native use is a blank (the shape's application
// pass yields nothing), so the shot fires its animation and pays its costs
// and hits nobody; this system then replays the chain the preview promised,
// one link at a time with a delay between hops, so the impacts read as one
// round travelling target to target. Each link is dealt through the skill's
// own ApplyToTile with the full native pipeline (damage numbers, reactions,
// impact effects, ElementsHit), Free so nothing is paid twice and
// InstantResolve so the hop rhythm is ours alone. A link whose victim died
// to an earlier link still spends its place in the chain.
public sealed class CheyanneRicochetSystem : JiangyuSystem
{
    private const float MuzzleDelaySeconds = 0.35f;
    // The round travels at a speed, not a schedule: each leg takes its tile
    // distance over these, floored so even an adjacent hop reads as movement.
    // The first leg is the rifle shot and moves like one; the ricochets are
    // slower so the chain can be read.
    private const float FirstLegTilesPerSecond = 45f;
    private const float TracerTilesPerSecond = 14f;
    private const float MinHopSeconds = 0.14f;
    private const float TracerHeight = 1.1f;

    private bool _sequencing;

    public override void OnInit()
        => Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "OnUse", 3, OnSkillUsed);

    private void OnSkillUsed(PatchInfo info)
    {
        try
        {
            var skill = (info.Instance as Il2CppSystem.Object)?.TryCast<Skill>();
            var id = skill?.GetID();
            if (id == null || !Calibration.TryParseRank(id, CheyanneSsrShapeSystem.SkillId, out _))
                return;
            // The commit is over: whatever happens next, later hovers must see
            // the full chain again. Logged before the guards so a commit the
            // guards swallow still leaves a trace: a double fire in the field
            // shows up here as two commits for one trigger pull.
            BounceChainAoEShape.UseCommitTile = null;
            Context.Log.Debug($"cheyanne ricochet: commit (result={info.Result ?? "null"}, sequencing={_sequencing})");
            if (_sequencing || info.Result is false)
                return;
            var user = (info.Args is { Count: > 0 } ? info.Args[0] : null) as Il2CppSystem.Object;
            var target = (info.Args is { Count: > 1 } ? info.Args[1] : null) as Il2CppSystem.Object;
            var tile = target?.TryCast<Tile>();
            if (tile == null)
                return;
            Context.Coroutines.Start(Sequence(skill, user?.TryCast<Actor>(), tile));
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"cheyanne ricochet: sequence start failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private System.Collections.IEnumerator Sequence(Skill skill, Actor attacker, Tile aimed)
    {
        _sequencing = true;
        try
        {
            // The chain is walked over VICTIMS, and each strike re-reads its
            // victim's tile on arrival: a target that shuffles between commit
            // and impact (the under-fire flinch moved three of four once) is
            // chased to where it stands, not missed at where it stood.
            var links = new List<(Actor Victim, Tile Tile)>();
            var primary = aimed.GetEntity()?.TryCast<Actor>();
            links.Add((primary, aimed));
            var visited = new HashSet<IntPtr>();
            if (primary != null)
                visited.Add(primary.Pointer);
            var shot = BounceChain.For(attacker);
            var walker = aimed;
            for (var bounce = 1; bounce <= shot.Bounces; bounce++)
            {
                var next = BounceChain.NextLink(attacker, walker, shot.Range, visited);
                if (next == null)
                    break;
                visited.Add(next.Pointer);
                var tile = next.GetTile();
                if (tile == null)
                    break;
                links.Add((next, tile));
                walker = tile;
            }
            Context.Log.Debug($"cheyanne ricochet: {links.Count} link(s) in flight");

            var waited = 0f;
            while (waited < MuzzleDelaySeconds)
            {
                waited += UnityEngine.Time.deltaTime;
                yield return null;
            }

            // Every impact waits for the round to ARRIVE, the first included.
            // Striking the first link with the engine's own presentation put
            // the damage and the bullet out of sync: InstantResolve lands the
            // hit at once while the native projectile only then leaves the
            // rifle. So the flight is ours end to end, one tracer from the
            // rifle to the first target and on down the chain, every strike
            // quiet, damage only on arrival.
            var herTile = attacker?.GetTile();
            var first = CurrentTile(links[0]);
            if (herTile != null)
            {
                var firstLeg = Mathf.Max(MinHopSeconds,
                    Pierce.Distance(herTile, first) / FirstLegTilesPerSecond);
                yield return Fly(attacker, herTile, first, firstLeg);
            }
            Strike(skill, CurrentTile(links[0]));

            // The chain's rhythm lives entirely in flight time: the round
            // never sits still, it just takes long enough between targets to
            // be watched. A stationary dwell at each victim read as stutter.
            var previous = first;
            for (var i = 1; i < links.Count; i++)
            {
                var link = CurrentTile(links[i]);
                var hop = Mathf.Max(MinHopSeconds,
                    Pierce.Distance(previous, link) / TracerTilesPerSecond);
                yield return Fly(attacker, previous, link, hop);
                // Re-read on arrival too: the flight itself takes time.
                var landing = CurrentTile(links[i]);
                Strike(skill, landing);
                previous = landing;
            }
        }
        finally
        {
            _sequencing = false;
            BounceChainAoEShape.ApplyExactly = null;
        }
    }

    // Where a link's victim stands right now, falling back to where the walk
    // found them when they are gone entirely.
    private static Tile CurrentTile((Actor Victim, Tile Tile) link)
    {
        if (link.Victim != null && link.Victim.IsAlive())
            return link.Victim.GetTile() ?? link.Tile;
        return link.Tile;
    }

    // The ricochet in the air: the skill's own projectile prefab (cached off
    // the template, which no longer carries it), spawned at the tile the
    // round just left and flown to the next by hand. The WAIT is the load-
    // bearing part: it happens whether or not a tracer could be drawn, or the
    // impacts collapse into one frame, which is exactly what a null element
    // once caused.
    private System.Collections.IEnumerator Fly(Actor attacker, Tile fromTile, Tile toTile, float seconds)
    {
        GameObject tracer = null;
        var from = Vector3.zero;
        var to = Vector3.zero;
        var havePositions = false;
        try
        {
            var element = attacker?.GetElement(0);
            if (element != null)
            {
                from = element.GetTargetPosOnTile(fromTile, 0) + Vector3.up * TracerHeight;
                to = element.GetTargetPosOnTile(toTile, 0) + Vector3.up * TracerHeight;
                havePositions = (to - from).sqrMagnitude > 0.001f;
            }
            else
            {
                Context.Log.Debug("cheyanne ricochet: no element for tracer positions, flying blind");
            }
            var prefab = CheyanneSsrShapeSystem.TracerPrefab;
            if (havePositions && prefab != null)
            {
                tracer = UnityEngine.Object.Instantiate(prefab, from, Quaternion.LookRotation(to - from));
                var flight = tracer.GetComponent<Projectile>();
                if (flight != null)
                    flight.enabled = false;
                tracer.SetActive(true);
            }
        }
        catch (Exception ex)
        {
            Context.Log.Debug($"cheyanne ricochet: tracer unavailable: {ex.GetType().Name}: {ex.Message}");
        }

        var t = 0f;
        while (t < seconds)
        {
            t += UnityEngine.Time.deltaTime;
            if (tracer != null)
                tracer.transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(t / seconds));
            yield return null;
        }
        if (tracer != null)
            UnityEngine.Object.Destroy(tracer);
    }

    // Every strike is quiet at the rifle: the round is already in the air, so
    // re-firing from the muzzle would be a second shot on screen. The
    // projectile is already off the template for good (CheyanneSsrShapeSystem
    // holds it for the tracer), so only the muzzle flash needs stripping for
    // the instant of the application.
    private void Strike(Skill skill, Tile tile)
    {
        // An empty link (the victim died to an earlier hop, or the aimed tile
        // never held anyone) still spends its place in the chain but strikes
        // nothing.
        if (tile?.GetEntity() == null)
            return;
        var template = skill.GetTemplate();
        GameObject muzzle = null;
        try
        {
            if (template != null)
            {
                muzzle = template.MuzzleEffect;
                template.MuzzleEffect = null;
            }
            BounceChainAoEShape.ApplyExactly = tile;
            skill.ApplyToTile(tile, UsageParameter.Free | UsageParameter.InstantResolve);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"cheyanne ricochet: strike failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            BounceChainAoEShape.ApplyExactly = null;
            if (template != null)
                template.MuzzleEffect = muzzle;
        }
    }
}

// Assigns the ricochet shape to the SSR skill once templates are applied,
// retried on scene load until every calibration rank clone is registered. Same
// shape of system as SextansPierceShapeSystem, and for the same reason: the
// CustomAoEShape field cannot be authored in KDL.
public sealed class CheyanneSsrShapeSystem : JiangyuSystem
{
    public const string SkillId = "active.cheyanne_ssr_ricochet";

    // The tracer the ricochet sequence flies between targets, taken OFF the
    // skill template at assignment time. The native use fires its projectile
    // at the aimed tile even with an empty application, which put a second,
    // slower bullet in the air next to the sequence's own; with the template
    // field null the engine has nothing to launch, and every bullet on screen
    // is the sequence's.
    public static GameObject TracerPrefab { get; private set; }

    private bool _assigned;

    public override void OnTemplatesApplied() => Assign();

    public override void OnSceneLoaded(int buildIndex, string sceneName) => Assign();

    // The base skill is the only one there is. Calibrating a gun clones the
    // WEAPON per rank (weapon.cheyanne_ssr_r1..r6, in weapon_ranks.kdl) and
    // every clone inherits SkillsGranted, so all seven ranks fire this one
    // skill. Only Sextans has per-rank skill clones, because her melee damage
    // lives on the skill's Attack handler rather than on WeaponTemplate.Damage.
    // Walking ranks here would chase six ids that never exist and leave the
    // system re-running its whole loop on every scene load.
    private void Assign()
    {
        if (!_assigned)
            _assigned = AssignOne(SkillId);
    }

    private bool AssignOne(string skillId)
    {
        try
        {
            var template = Templates.ById<SkillTemplate>(skillId, msg => Context.Log.Debug($"cheyanne ssr: {msg}"));
            if (template == null)
            {
                Context.Log.Debug($"cheyanne ssr: skill template '{skillId}' not registered yet");
                return false;
            }
            template.CustomAoEShape = new BounceChainAoEShape().Cast<ICustomAoEShape>();
            template.UseCustomAoEShape = true;
            template.AoEType = SkillAoEType.AllTiles;
            if (template.ProjectileData != null)
            {
                TracerPrefab = template.ProjectileData.Prefab;
                template.ProjectileData = null;
            }
            template.SecondaryProjectileData = null;
            Context.Log.Debug($"cheyanne ssr: ricochet shape assigned to '{skillId}'");
            return true;
        }
        catch (Exception ex)
        {
            // A throw is not a missing template: report assigned so the retry
            // loop does not warn every scene against a broken skill.
            Context.Log.Warn($"cheyanne ssr: shape assignment failed for '{skillId}': {ex.GetType().Name}: {ex.Message}");
            return true;
        }
    }
}
