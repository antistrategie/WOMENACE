using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.AI;
using Il2CppMenace.Tactical.AI.Behaviors;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Star Rail Insight, Nemesis's foresight perk: at the start of every allied unit's turn she reads
// the next action of the enemies closest to her that her side has detected, and shows it as a
// vision. A purple afterimage stands where an enemy is about to move, a smoke trail follows its
// route, an enemy that stays put glows where it stands, and a unit the player can see that an
// enemy is inclined to attack carries a red-violet doom mark with a thread back to the attacker.
// The vision is not redrawn as the player acts, and it clears when the player's unit ends its turn.
//
// The read borrows each enemy's own planner: Evaluate and PickBehavior, run as if its turn had
// started (the AP its turn grants, fresh turn flags, the faction's turn-start Think, and every
// unit hostile to it that it has line of sight to counted as detected), then every agent field it
// touched is put back, its RNG and the mission generator included. The faction's Think state is
// not put back: its real turn runs Think again and rebuilds it.
[JiangyuType("StartrailInsight")]
public sealed partial class StartrailInsight : SkillEventHandlerTemplate
{
    public int MaxEnemies = 8;
    public float AttackShare = 0.25f;

    public override SkillEventHandler Create() => new StartrailInsightHandler
    {
        MaxEnemies = MaxEnemies,
        AttackShare = AttackShare,
    };
}

[JiangyuType("StartrailInsightHandler")]
public sealed partial class StartrailInsightHandler : SkillEventHandler
{
    public int MaxEnemies = 8;
    public float AttackShare = 0.25f;
    private Actor _actor;

    // GetActor can be null when the perk is added at mission start, so she is registered on the
    // first update with a live actor. The read itself runs from StartrailInsightSystem at the start
    // of every allied unit's turn.
    public override void OnUpdate(EntityProperties _properties)
    {
        if (_actor != null)
            return;
        var actor = GetActor();
        if (actor == null || !actor.IsAlive())
            return;
        _actor = actor;
        StartrailInsightSystem.Register(actor, MaxEnemies, AttackShare);
    }

    public override void OnDeath()
    {
        StartrailInsightSystem.Unregister(_actor);
        StartrailInsightSystem.ClearAll();
    }

    public override void OnMissionFinished()
    {
        StartrailInsightSystem.Unregister(_actor);
        StartrailInsightSystem.ClearAll();
        _actor = null;
    }
}

// What one enemy is about to do, as the read saw it.
public sealed class Vision
{
    public Actor Enemy;
    public string Summary;
    public Vector3? Destination;
    public List<Vector3> Path;
    public Actor Victim;
    public string Weighed;
    // How strongly the AI wants this enemy to act next: its score after the read times
    // GetScoreMultForPickingThisAgent, as AIFaction.Process compares them.
    public float PickScore;
}

public sealed class StartrailInsightSystem : JiangyuSystem
{
    private const string PrefabAsset = "effects/vision_ghost/main";

    private static StartrailInsightSystem _instance;
    // Every object of the vision on screen, and the units already carrying a doom mark.
    private static readonly List<GameObject> Parts = new();
    private static readonly HashSet<IntPtr> Doomed = new();
    private static Material _doomMaterial;
    private static Material _trailMaterial;
    private static Material _threadMaterial;

    // Set while the read runs, so the board-change hooks ignore the borrowed planning.
    internal static bool Reading { get; private set; }

    // The seer whose vision it is, and how much she reads.
    private static Actor _owner;
    private static int _maxEnemies = 8;
    private static float _attackShare = 0.25f;

    public override void OnInit()
    {
        _instance = this;
        // TacticalState.OnTurnStart handles the game's per-unit turn start event. Player units have
        // no fixed order, so selecting one starts its turn, and flipping between units without
        // acting starts several. The vision is only read again once something has moved or acted.
        Context.Patches.Postfix("Il2CppMenace.States.TacticalState", "OnTurnStart", OnUnitTurnStart);
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnMovementFinished", OnBoardChanged);
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnSkillUse", OnBoardChanged);
        // Ending a unit's turn spends the vision: it clears, and the next turn start reads afresh.
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnTurnEnd", OnUnitTurnEnd);
    }

    private static void OnUnitTurnEnd(PatchInfo info)
    {
        try
        {
            var unit = info.Args?.Count > 0 ? (info.Args[0] as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<Actor>() : null;
            if (unit == null || !unit.IsPlayerOrPlayerAI())
                return;
            ClearAll();
            _stale = true;
        }
        catch { }
    }

    private static bool _stale = true;

    private static void OnBoardChanged(PatchInfo info)
    {
        if (!Reading)
            _stale = true;
    }

    internal static void Register(Actor owner, int maxEnemies, float attackShare)
    {
        _stale = true;
        _owner = owner;
        _maxEnemies = maxEnemies;
        _attackShare = attackShare;
    }

    internal static void Unregister(Actor owner)
    {
        if (owner != null && _owner != null && owner.Pointer == _owner.Pointer)
            _owner = null;
    }

    // The vision is read afresh as each allied unit's turn starts, from where the seer stands.
    private static void OnUnitTurnStart(PatchInfo info)
    {
        try
        {
            var unit = info.Args?.Count > 0 ? (info.Args[0] as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<Actor>() : null;
            if (_owner == null || unit == null || !unit.IsPlayerOrPlayerAI() || !_owner.IsAlive() || !_stale)
                return;
            _stale = false;
            Show(Read(_owner.GetPos(), _maxEnemies, _attackShare, null));
        }
        catch (Exception ex) { Warn($"read failed: {ex.Message}"); }
    }

    // The seer is one of this mission's actors. A scene change (a mission ending, quitting to the
    // menu, loading a save) leaves her pointer stale, so she goes.
    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        _owner = null;
        _stale = true;
        Parts.Clear();
        Doomed.Clear();
    }

    public override void OnUnload()
    {
        ClearAll();
        _instance = null;
    }

    internal static void Warn(string message) => _instance?.Context.Log.Warn($"startrail insight: {message}");

    // Reads the `count` detected enemies closest to `anchor`. `log`, when given, collects the
    // diagnostics the dev verbs print.
    internal static List<Vision> Read(Vector3 anchor, int count, float attackShare, List<string> log)
    {
        var visions = new List<Vision>();
        var tm = TacticalManager.Get();
        var player = tm?.GetPlayerFaction();
        if (player == null)
            return visions;

        // A crew riding in a vehicle is skipped: it cannot act until it is unloaded. The link is
        // checked from both sides, and a crew standing on its vehicle's tile counts as riding.
        var riding = new HashSet<IntPtr>();
        var vehicleTiles = new HashSet<IntPtr>();
        foreach (var baseFaction in tm.GetFactions())
            if (baseFaction != null)
                foreach (var entity in baseFaction.GetActors())
                {
                    if (entity == null)
                        continue;
                    if (entity.GetContainedEntity() is { } crew)
                        riding.Add(crew.Pointer);
                    if (entity.IsVehicle() && entity.GetTile() is { } tile)
                        vehicleTiles.Add(tile.Pointer);
                }
        bool Riding(Actor actor) => actor.IsContainedWithinAnotherEntity() || actor.GetContainerEntity() != null
            || riding.Contains(actor.Pointer)
            || (!actor.IsVehicle() && actor.GetTile() is { } tile && vehicleTiles.Contains(tile.Pointer));

        if (log != null)
            foreach (var baseFaction in tm.GetFactions())
            {
                var f = baseFaction?.TryCast<AIFaction>();
                if (f == null || f.IsAlliedWithPlayer())
                    continue;
                foreach (var a in f.GetActors())
                    if (a != null)
                        log.Add($"  {Label(a)}: detected {a.IsDetectedByFaction(player.GetIndex())}, on screen {a.Is3DModelVisibleToPlayer()}, agent {a.GetAgent() != null}, vehicle {a.IsVehicle()}, "
                            + $"contained {a.IsContainedWithinAnotherEntity()}, container {a.GetContainerEntity()?.GetTemplate()?.GetID() ?? "none"}, "
                            + $"listed by a vehicle {riding.Contains(a.Pointer)}, on a vehicle tile {(!a.IsVehicle() && a.GetTile() is { } t && vehicleTiles.Contains(t.Pointer))}");
            }

        var candidates = new List<(float Distance, Actor Actor, AIFaction Faction)>();
        foreach (var baseFaction in tm.GetFactions())
        {
            var faction = baseFaction?.TryCast<AIFaction>();
            if (faction == null || faction.IsAlliedWithPlayer())
                continue;
            // Seen by the player: detected by a unit's vision, or drawn on screen (an enemy revealed
            // some other way is visible without counting as detected).
            foreach (var actor in faction.GetActors())
                if (actor?.GetAgent() != null && (actor.IsDetectedByFaction(player.GetIndex()) || actor.Is3DModelVisibleToPlayer()) && !Riding(actor))
                    candidates.Add((Vector3.Distance(anchor, actor.GetPos()), actor, faction));
        }
        var chosen = candidates.OrderBy(c => c.Distance).Take(count).ToList();
        var factions = chosen.Select(c => c.Faction).Distinct().ToList();
        log?.Add($"reading {chosen.Count} of {candidates.Count} detected enemies");

        // An enemy faction re-detects other units only as its turn starts, and an attack only
        // collects detected targets. For the read, a unit not allied with a faction (the player's,
        // allied NPCs, civilians) counts as detected by it when any of that faction's units has
        // line of sight to it, by the game's own check.
        var occupied = new List<Tile>();
        var others = new List<Actor>();
        foreach (var baseFaction in tm.GetFactions())
            if (baseFaction != null && !factions.Any(f => f.Pointer == baseFaction.Pointer))
                foreach (var unit in baseFaction.GetActors())
                    if (unit != null && unit.IsAlive())
                        others.Add(unit);
        var masks = others.Select(unit => (Unit: unit, Mask: unit.m_DetectedMask)).ToList();
        // Planning can draw from the mission generator as well as each agent's own, so its state
        // is put back too and the read never moves the real sequence.
        var missionRng = TacticalManager.GetRandom();
        var missionState = missionRng == null ? default : (missionRng.m_S0, missionRng.m_S1, missionRng.m_S2, missionRng.m_S3);
        Reading = true;
        try
        {
            foreach (var faction in factions)
            {
                var watchers = faction.GetActors();
                foreach (var unit in others)
                {
                    if (unit.IsDetectedByFaction(faction.GetIndex()))
                        continue;
                    foreach (var watcher in watchers)
                        if (watcher != null && !unit.IsAlliedWith(watcher)
                            && watcher.HasLineOfSightTo(unit, watcher.GetTile(), unit.GetTile()))
                        {
                            unit.SetDetectedByFaction(faction.GetIndex());
                            break;
                        }
                }
            }

            // Each faction thinks once, as its own turn starts with: strategy, opponent
            // assessment and every agent's pre-processing. Its real turn rebuilds all of it.
            foreach (var faction in factions)
            {
                try
                {
                    faction.Think();
                    BaseFaction.WaitUntilAllTasksAreDone();
                }
                catch (Exception ex)
                {
                    log?.Add($"think failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // The AI acts one enemy at a time: it evaluates every agent, the one whose plan scores
            // highest (score x GetScoreMultForPickingThisAgent, AIFaction.Process) acts, and the
            // rest plan again around what it did. The read does the same. Each round keeps the
            // best-scoring remaining enemy's vision and marks its destination occupied (Move
            // scores only tiles whose Tile.IsEmpty, m_EntitySegment == null). Taking a tile only
            // removes an option, so the enemies read again are the ones whose plan ended on it.
            // The tiles are put back afterwards.
            var remaining = chosen.Select(c => (c.Distance, c.Actor, Vision: ReadOne(c.Actor.GetAgent(), attackShare))).ToList();
            var order = 0;
            while (remaining.Count > 0)
            {
                var bestIndex = 0;
                for (var i = 1; i < remaining.Count; i++)
                    if (remaining[i].Vision.PickScore > remaining[bestIndex].Vision.PickScore)
                        bestIndex = i;
                var (distance, actor, best) = remaining[bestIndex];
                remaining.RemoveAt(bestIndex);
                visions.Add(best);
                order++;
                log?.Add($"{order}. {Label(actor)} [{distance:F0} m, pick {best.PickScore:F0}]: {best.Summary}");
                if (best.Weighed != null)
                    log?.Add($"    weighed: {best.Weighed}");
                if (best.Destination is { } destination && actor.GetTile() is { } from
                    && TacticalManager.Get()?.GetMap()?.GetTileAtPos(destination) is { } tile
                    && tile.Pointer != from.Pointer && tile.m_EntitySegment == null)
                {
                    occupied.Add(tile);
                    tile.m_EntitySegment = from.m_EntitySegment;
                    for (var i = 0; i < remaining.Count; i++)
                        if (remaining[i].Vision.Destination is { } taken
                            && TacticalManager.Get()?.GetMap()?.GetTileAtPos(taken)?.Pointer == tile.Pointer)
                            remaining[i] = (remaining[i].Distance, remaining[i].Actor, ReadOne(remaining[i].Actor.GetAgent(), attackShare));
                }
            }
        }
        finally
        {
            foreach (var (unit, mask) in masks)
                unit.m_DetectedMask = mask;
            if (missionRng != null)
            {
                missionRng.m_S0 = missionState.Item1;
                missionRng.m_S1 = missionState.Item2;
                missionRng.m_S2 = missionState.Item3;
                missionRng.m_S3 = missionState.Item4;
            }
            foreach (var tile in occupied)
                tile.m_EntitySegment = null;
            Reading = false;
        }
        return visions;
    }

    private static Vision ReadOne(Agent agent, float attackShare)
    {
        var actor = agent.GetActor();
        var vision = new Vision { Enemy = actor };
        var rng = agent.m_Random;
        var (s0, s1, s2, s3) = rng == null ? (0u, 0u, 0u, 0u) : (rng.m_S0, rng.m_S1, rng.m_S2, rng.m_S3);
        var active = agent.m_ActiveBehavior;
        var score = agent.m_Score;
        var agentDone = agent.IsTurnDone;
        var move = FindMove(agent);
        var reserved = move?.m_ReservedTile;
        var target = move?.m_TargetTile;
        var ap = actor.m_ActionPoints;
        var turnDone = actor.m_IsTurnDone;
        var turnStarted = actor.m_IsTurnStarted;
        var hasActed = actor.m_HasActed;
        // The AP its next turn grants: the current properties' AP, which the turn start sets the
        // purse to. m_ActionPointsAtTurnStart is only filled once a unit has had a turn.
        var props = actor.TryCast<UnitActor>()?.GetCurrentProperties() ?? actor.TryCast<TransientActor>()?.GetCurrentProperties();
        var apStart = props?.GetActionPoints() ?? 0;
        if (apStart <= 0)
            apStart = Math.Max(ap, actor.m_ActionPointsAtTurnStart);
        try
        {
            // Its turn has not started, so its agent and unit still carry last turn's spent
            // state: done flags set and the purse what it had left.
            agent.IsTurnDone = false;
            actor.m_ActionPoints = apStart;
            actor.m_IsTurnDone = false;
            actor.m_IsTurnStarted = true;
            actor.m_HasActed = false;
            agent.Evaluate();
            BaseFaction.WaitUntilAllTasksAreDone();
            var picked = agent.PickBehavior();
            vision.Summary = Describe(picked);
            vision.PickScore = agent.GetScore() * agent.GetScoreMultForPickingThisAgent();

            if (picked?.TryCast<Move>()?.m_TargetTile is { Tile: { } destination } plan)
            {
                vision.Destination = destination.GetPos();
                vision.Path = new List<Vector3> { actor.GetPos() };
                // The plan's path runs on toward its UltimateTile, the longer goal past this turn's
                // stop, so it is followed only until it reaches the stop. A path that never passes
                // the stop is dropped for a straight line, as is a vehicle's, which drives straight.
                var stop = destination.GetPos();
                if (plan.Path != null && !actor.IsVehicle())
                {
                    var route = new List<Vector3>();
                    var reached = false;
                    for (var i = 0; i < plan.Path.Count && !reached; i++)
                    {
                        route.Add(plan.Path[i]);
                        var flat = plan.Path[i] - stop;
                        flat.y = 0;
                        reached = flat.magnitude < 2f;
                    }
                    if (reached)
                        vision.Path.AddRange(route);
                }
                vision.Path.Add(stop);
                // Route points sit at whatever height the mover's own path keeps (a vehicle's is
                // not its wheels), so each is dropped onto its tile's ground.
                var map = TacticalManager.Get()?.GetMap();
                if (map != null)
                    for (var i = 0; i < vision.Path.Count; i++)
                        if (map.GetTileAtPos(vision.Path[i]) is { } tile)
                            vision.Path[i] = new Vector3(vision.Path[i].x, tile.GetPos().y, vision.Path[i].z);
                vision.Summary += $" [path {vision.Path.Count} pts, y {vision.Path.Min(p => p.y):F1}..{vision.Path.Max(p => p.y):F1}]";
            }

            // The doom mark follows the strongest attack the enemy weighed, not only a winning
            // one: enemies mix moving and shooting in a turn, and their real turn rates attacks
            // above what a read from the player's turn gives them. An attack counts once it
            // reaches attackShare of the winning option's score.
            var winner = picked?.GetScore() ?? 0;
            Attack strongest = null;
            var weighed = new List<string>();
            foreach (var b in agent.GetBehaviors())
            {
                if (b == null)
                    continue;
                weighed.Add($"{b.GetName()}={b.GetScore()}");
                if (b.TryCast<Attack>() is { } option && (option.m_Candidates?.Count ?? 0) > 0
                    && option.GetScore() >= attackShare * winner
                    && (strongest == null || option.GetScore() > strongest.GetScore()))
                    strongest = option;
            }
            vision.Weighed = string.Join(", ", weighed);
            // A picked skill marks its target only when it is an attack: a heal or buff on an
            // ally is no threat.
            var pickedAttack = picked?.TryCast<SkillBehavior>() is { } sb && sb.m_Skill != null && sb.m_Skill.IsAttack() ? sb : null;
            var victimTile = pickedAttack?.m_TargetTile ?? strongest?.GetHighestScoredTarget().Target;
            if (TileActors.TargetOn(victimTile) is { } victim && victim.Pointer != actor.Pointer)
            {
                vision.Victim = victim;
                if (pickedAttack == null && strongest != null)
                    vision.Summary += $" | threat: {strongest.GetName()} ({strongest.GetScore()}) on {victim.GetTemplate()?.GetID()}";
            }
        }
        catch (Exception ex)
        {
            vision.Summary = $"read failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            agent.IsTurnDone = agentDone;
            actor.m_ActionPoints = ap;
            actor.m_IsTurnDone = turnDone;
            actor.m_IsTurnStarted = turnStarted;
            actor.m_HasActed = hasActed;
            if (rng != null)
            {
                rng.m_S0 = s0;
                rng.m_S1 = s1;
                rng.m_S2 = s2;
                rng.m_S3 = s3;
            }
            agent.m_ActiveBehavior = active;
            agent.m_Score = score;
            if (move != null)
            {
                move.m_ReservedTile = reserved;
                move.m_TargetTile = target;
            }
        }
        return vision;
    }

    // Replaces whatever vision is up with these.
    internal static int Show(List<Vision> visions)
    {
        ClearAll();
        var prefab = _instance?.Context.Assets.Load<GameObject>(PrefabAsset);
        if (prefab == null)
        {
            Warn($"{PrefabAsset} is not loaded");
            return 0;
        }
        var spawned = 0;
        foreach (var vision in visions)
        {
            var enemy = vision.Enemy;
            if (enemy == null || vision.Summary == null || vision.Summary.StartsWith("read failed"))
                continue;
            // An afterimage stands where the enemy will move. One that stays put glows on its
            // own body instead, carried by it as it animates.
            var offset = vision.Destination is { } destination ? destination - enemy.GetPos() : Vector3.zero;
            var parts = vision.Destination != null
                ? Shell(enemy, offset, prefab, null)
                : Overlay(enemy, prefab.GetComponent<MeshRenderer>().sharedMaterial);
            if (vision.Path != null && Ribbon(vision.Path, 0.12f, 0.45f, TrailMaterial(prefab)) is { } trail)
                parts.Add(trail);
            if (vision.Victim is { } victim && SeenByPlayer(victim))
            {
                // A frozen copy where the unit stood when the vision was read, so it stays
                // joined to the threads that point at it.
                if (Doomed.Add(victim.Pointer))
                    parts.AddRange(Shell(victim, Vector3.zero, prefab, DoomMaterial(prefab)));
                // The thread arcs from where the enemy will stand to the unit it may attack.
                var from = enemy.GetPos() + offset;
                var to = victim.GetPos();
                var thread = new List<Vector3>();
                for (var i = 0; i <= 12; i++)
                {
                    var f = i / 12f;
                    thread.Add(Vector3.Lerp(from, to, f) + Vector3.up * (1.1f + 0.5f * Mathf.Sin(f * Mathf.PI)));
                }
                if (Ribbon(thread, 0f, 0.3f, ThreadMaterial(prefab)) is { } line)
                    parts.Add(line);
            }
            Parts.AddRange(parts);
            spawned += parts.Count;
        }
        return spawned;
    }

    internal static void ClearAll()
    {
        Destroy(Parts);
        Parts.Clear();
        Doomed.Clear();
    }

    // The player's own side, or a unit their side has detected or can see on screen. A doom mark
    // on anything else would give its position away.
    private static bool SeenByPlayer(Actor unit)
    {
        var player = TacticalManager.Get()?.GetPlayerFaction();
        return unit.IsPlayerOrPlayerAI()
            || (player != null && unit.IsDetectedByFaction(player.GetIndex()))
            || unit.Is3DModelVisibleToPlayer();
    }

    private static readonly System.Text.RegularExpressions.Regex LowerLod =
        new("_LOD[1-9]", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Meshes built for a vision carry this name and go with it. A rigid part's shared mesh
    // belongs to the game and stays.
    private const string BuiltMesh = "vision_built";

    private static void Destroy(List<GameObject> objects)
    {
        foreach (var o in objects)
        {
            if (o == null)
                continue;
            if (o.GetComponent<MeshFilter>()?.sharedMesh is { } mesh && mesh.name == BuiltMesh)
                UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(o);
        }
    }

    // A still copy of every full-detail part of the actor, moved by offset, in the ghost
    // material or the one given.
    private static List<GameObject> Shell(Actor actor, Vector3 offset, GameObject prefab, Material material)
    {
        var shells = new List<GameObject>();
        foreach (var element in actor.m_Elements)
        {
            if (element == null)
                continue;
            // The full-detail parts only: lower LODs are enabled components drawn only at range,
            // and a hidden crew rides with rendering forced off. Not isVisible, which is false
            // for anything off camera.
            foreach (var renderer in element.GetComponentsInChildren<Renderer>(false))
            {
                if (renderer == null || !renderer.enabled || renderer.forceRenderingOff
                    || LowerLod.IsMatch(renderer.gameObject.name))
                    continue;
                Mesh mesh = null;
                if (renderer.TryCast<SkinnedMeshRenderer>() is { } smr)
                {
                    mesh = new Mesh { name = BuiltMesh };
                    smr.BakeMesh(mesh, true);
                }
                else if (renderer.TryCast<MeshRenderer>() != null && renderer.GetComponent<MeshFilter>()?.sharedMesh is { } shared)
                    mesh = shared;
                if (mesh == null)
                    continue;
                var t = renderer.transform;
                var shell = UnityEngine.Object.Instantiate(prefab);
                // Both the baked skinned mesh and the rigid one come out in the part's local
                // units, so the copy takes the part's world scale. Some bodies are imported at 1%
                // and shrunk by their transform (the pirate outcasts).
                shell.transform.SetPositionAndRotation(t.position + offset, t.rotation);
                shell.transform.localScale = t.lossyScale;
                shell.GetComponent<MeshFilter>().sharedMesh = mesh;
                // One material per submesh: a doll's body is a single mesh of many submeshes (face,
                // hair, body, outfit), and only the first would draw otherwise.
                var shellRenderer = shell.GetComponent<MeshRenderer>();
                shellRenderer.sharedMaterials = Fill(material ?? shellRenderer.sharedMaterial, mesh.subMeshCount);
                shells.Add(shell);
            }
        }
        return shells;
    }

    // The actor's own full-detail parts drawn again in `material`, as children that share its
    // bones and transforms, so the glow moves and animates with the unit: an enemy that stays put
    // keeps its glow lined up with its idle animation.
    private static List<GameObject> Overlay(Actor actor, Material material)
    {
        var overlays = new List<GameObject>();
        if (material == null)
            return overlays;
        foreach (var element in actor.m_Elements)
        {
            if (element == null)
                continue;
            foreach (var renderer in element.GetComponentsInChildren<Renderer>(false))
            {
                if (renderer == null || !renderer.enabled || renderer.forceRenderingOff
                    || LowerLod.IsMatch(renderer.gameObject.name))
                    continue;
                var go = new GameObject("vision_overlay");
                if (renderer.TryCast<SkinnedMeshRenderer>() is { } smr && smr.sharedMesh != null)
                {
                    go.transform.SetParent(smr.transform.parent, false);
                    go.transform.localPosition = smr.transform.localPosition;
                    go.transform.localRotation = smr.transform.localRotation;
                    go.transform.localScale = smr.transform.localScale;
                    var copy = go.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = smr.sharedMesh;
                    copy.bones = smr.bones;
                    copy.rootBone = smr.rootBone;
                    copy.localBounds = smr.localBounds;
                    copy.updateWhenOffscreen = true;
                    copy.sharedMaterials = Fill(material, smr.sharedMesh.subMeshCount);
                    copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    copy.receiveShadows = false;
                }
                else if (renderer.TryCast<MeshRenderer>() != null && renderer.GetComponent<MeshFilter>()?.sharedMesh is { } mesh)
                {
                    go.transform.SetParent(renderer.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var copy = go.AddComponent<MeshRenderer>();
                    copy.sharedMaterials = Fill(material, mesh.subMeshCount);
                    copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    copy.receiveShadows = false;
                }
                else
                {
                    UnityEngine.Object.Destroy(go);
                    continue;
                }
                overlays.Add(go);
            }
        }
        return overlays;
    }

    private static Material[] Fill(Material material, int count)
    {
        var materials = new Material[Math.Max(1, count)];
        for (var i = 0; i < materials.Length; i++)
            materials[i] = material;
        return materials;
    }

    // A flat strip of smoke along the points, lifted off them by lift: UV0 is metres along by
    // 0..1 across and UV1.x is 0..1 along, which is what VisionTrail reads.
    private static GameObject Ribbon(List<Vector3> points, float lift, float width, Material material)
    {
        var pts = new List<Vector3>();
        foreach (var p in points)
            if (pts.Count == 0 || (p - pts[^1]).sqrMagnitude > 0.01f)
                pts.Add(p);
        if (pts.Count < 2 || material == null)
            return null;
        var n = pts.Count;
        var distance = new float[n];
        for (var i = 1; i < n; i++)
            distance[i] = distance[i - 1] + Vector3.Distance(pts[i], pts[i - 1]);
        var total = Mathf.Max(0.01f, distance[n - 1]);
        var vertices = new Vector3[n * 2];
        var uv = new Vector2[n * 2];
        var along = new Vector2[n * 2];
        for (var i = 0; i < n; i++)
        {
            var dir = pts[Math.Min(i + 1, n - 1)] - pts[Math.Max(i - 1, 0)];
            dir.y = 0;
            var side = Vector3.Cross(Vector3.up, dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward) * (width / 2);
            var centre = pts[i] + Vector3.up * lift;
            vertices[i * 2] = centre - side;
            vertices[i * 2 + 1] = centre + side;
            uv[i * 2] = new Vector2(distance[i], 0);
            uv[i * 2 + 1] = new Vector2(distance[i], 1);
            along[i * 2] = along[i * 2 + 1] = new Vector2(distance[i] / total, 0);
        }
        var triangles = new int[(n - 1) * 6];
        for (var i = 0; i < n - 1; i++)
        {
            var v = i * 2;
            triangles[i * 6] = v;
            triangles[i * 6 + 1] = v + 2;
            triangles[i * 6 + 2] = v + 1;
            triangles[i * 6 + 3] = v + 1;
            triangles[i * 6 + 4] = v + 2;
            triangles[i * 6 + 5] = v + 3;
        }
        var mesh = new Mesh { name = BuiltMesh, vertices = vertices, uv = uv, uv2 = along, triangles = triangles };
        mesh.RecalculateBounds();
        var go = new GameObject("vision_ribbon");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
    }

    private static Material TrailMaterial(GameObject prefab)
    {
        if (_trailMaterial == null)
            _trailMaterial = prefab.transform.Find("trail")?.GetComponent<MeshRenderer>()?.sharedMaterial;
        return _trailMaterial;
    }

    // The attack thread: the trail in the doom marks' red-violet.
    private static Material ThreadMaterial(GameObject prefab)
    {
        if (_threadMaterial == null && TrailMaterial(prefab) is { } trail)
        {
            _threadMaterial = new Material(trail);
            _threadMaterial.SetColor("_Color", new Color(2.0f, 0.25f, 0.6f, 1f));
        }
        return _threadMaterial;
    }

    // The doom mark: the ghost material in a hotter red-violet.
    private static Material DoomMaterial(GameObject prefab)
    {
        if (_doomMaterial == null)
        {
            _doomMaterial = new Material(prefab.GetComponent<MeshRenderer>().sharedMaterial);
            _doomMaterial.SetColor("_RimColor", new Color(2.0f, 0.25f, 0.6f, 1f));
            _doomMaterial.SetColor("_CoreColor", new Color(0.7f, 0.05f, 0.25f, 1f));
            // Left standing alone once the unit moves on, a mark needs a body, not only a rim:
            // a firmer core and fraying only near the head keep the whole figure readable.
            _doomMaterial.SetFloat("_CoreAlpha", 0.38f);
            _doomMaterial.SetFloat("_FrayStart", 0.82f);
        }
        return _doomMaterial;
    }

    private static Move FindMove(Agent agent)
    {
        foreach (var b in agent.GetBehaviors())
            if (b?.TryCast<Move>() is { } move)
                return move;
        return null;
    }

    internal static string Label(Actor actor) => $"{actor.GetTemplate()?.GetID() ?? "?"}@{Pos(actor.GetTile())}";

    internal static string Pos(Tile tile)
    {
        if (tile == null)
            return "-";
        var p = tile.GetPos();
        return $"({p.x:0},{p.z:0})";
    }

    internal static string Describe(Behavior b)
    {
        if (b == null)
            return "none";
        var name = b.GetName();
        if (b.TryCast<SkillBehavior>() is { } sb)
        {
            var tile = sb.m_TargetTile;
            var who = TileActors.TargetOn(tile);
            return $"{name} {sb.m_Skill?.GetID() ?? "?"} at {Pos(tile)}{(who != null ? " on " + who.GetTemplate()?.GetID() : "")}";
        }
        if (b.TryCast<Move>() is { } move)
            return $"{name} to {Pos(move.m_TargetTile?.Tile)}";
        return name;
    }
}
