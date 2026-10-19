using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Game.Tactical;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// A disc of tiles around either the user (a pulse) or the aimed tile (a blast),
// measured with the game's own tile distance so it matches the range circles.
// Occupied tiles only count when the occupant is hostile to the user, so the
// Titan never catches itself or its allies. Empty tiles stay in, which is what
// lets a blast's SpawnTileEffect set fire across the whole disc.
[JiangyuType("TitanRadiusAoEShape", Interfaces = new[] { typeof(ICustomAoEShape) })]
public sealed partial class TitanRadiusAoEShape : Il2CppSystem.Object
{
    public int Radius = 1;
    public bool AroundUser;

    // The shape carries its own reach. A non-zero radius draws the generic red
    // ring around the hovered tile on top of it.
    public int GetAoERadius() => 0;

    public Tile GetOverrideTargetTile(Tile _origin, Tile _target) => AroundUser ? _origin : _target;

    public void GetAffectedTiles(Tile _origin, Tile _target, Il2CppSystem.Collections.Generic.List<Tile> _into, bool _lineOfFireNeeded, bool _skipEmptyTiles)
    {
        try
        {
            var centre = AroundUser ? _origin : _target;
            if (centre == null || _into == null)
                return;
            // The AI scores tiles it has not moved to yet, so the origin can be empty.
            // The acting unit is then the one being planned for.
            var user = _origin?.GetEntity()?.TryCast<Actor>() ?? TacticalManager.Get()?.GetActiveActor();
            for (var x = centre.GetX() - Radius; x <= centre.GetX() + Radius; x++)
                for (var z = centre.GetZ() - Radius; z <= centre.GetZ() + Radius; z++)
                {
                    if (!Tiles.InBounds(x, z))
                        continue;
                    var tile = Tiles.At(x, z);
                    if (tile == null || Tiles.Distance(centre, tile) > Radius)
                        continue;
                    var occupant = tile.GetEntity()?.TryCast<Actor>();
                    // With no known attacker every occupant counts, so planning never
                    // scores a reachable stomp as hitting nobody.
                    if (occupant != null && user != null && !Pierce.IsHostileTo(user, occupant))
                        continue;
                    if (occupant == null && (_skipEmptyTiles || AroundUser))
                        continue;
                    _into.Add(tile);
                }
        }
        catch (Exception ex)
        {
            Log.Error($"[TitanShape] GetAffectedTiles failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// Assigns the Titans' AoE shapes and AI weighting. CustomAoEShape and AIConfig are
// Odin fields KDL cannot author, so both live here.
public sealed class TitanAoESystem : JiangyuSystem
{
    internal readonly record struct Shape(int Radius, bool AroundUser);

    internal static readonly IReadOnlyDictionary<string, Shape> Shapes = new Dictionary<string, Shape>(StringComparer.Ordinal)
    {
        // Every enemy within 2 tiles of the Titan, where Pierce cannot reach.
        ["active.wmgfl_titan_strobe"] = new Shape(2, true),
        // GFL2: the target and everything within 2 tiles, which then burns.
        ["active.wmgfl_titan_thermal_overload"] = new Shape(2, false),
    };

    internal readonly record struct AiWeight(float ScoreMult, bool InfluencesPositioning);

    // The AI plans one turn at a time: a move, then an attack it can afford after it.
    // From afar no stomp fits in one turn, so Pierce (3 to 4 tiles) draws the Titan
    // in, and from there a 2-tile walk plus Strobe fits the AP. Strobe outweighs
    // Pierce so the stomp wins whenever it can land, and Pierce's reduced weight keeps
    // it from backing off to its own range after a stomp. The scarecrow pulse Strobe
    // clones has positioning off. Fresh configs leave the vanilla skills untouched.
    internal static readonly IReadOnlyDictionary<string, AiWeight> AiWeights = new Dictionary<string, AiWeight>(StringComparer.Ordinal)
    {
        ["active.wmgfl_titan_strobe"] = new AiWeight(2f, true),
        ["active.wmgfl_titan_pierce"] = new AiWeight(0.75f, true),
    };

    private readonly List<string> _pendingShapes = new(Shapes.Keys);
    private readonly List<string> _pendingWeights = new(AiWeights.Keys);

    public override void OnTemplatesApplied() => AssignPending();

    public override void OnSceneLoaded(int buildIndex, string sceneName) => AssignPending();

    private void AssignPending()
    {
        for (var i = _pendingShapes.Count - 1; i >= 0; i--)
        {
            var id = _pendingShapes[i];
            var template = Templates.ById<SkillTemplate>(id);
            if (template == null)
                continue;
            var shape = Shapes[id];
            try
            {
                template.CustomAoEShape = new TitanRadiusAoEShape { Radius = shape.Radius, AroundUser = shape.AroundUser }.Cast<ICustomAoEShape>();
                template.UseCustomAoEShape = true;
                template.AoEType = SkillAoEType.AllTiles;
                // The blast can be dropped on open ground beside a target, like a grenade.
                if (!shape.AroundUser)
                    template.TargetsAllowed |= SkillTarget.EmptyTile;
                Context.Log.Debug($"titan shape: assigned to '{id}' (radius={shape.Radius}, aroundUser={shape.AroundUser})");
            }
            catch (Exception ex)
            {
                Context.Log.Error($"titan shape: assigning '{id}' failed: {ex.GetType().Name}: {ex.Message}");
            }
            _pendingShapes.RemoveAt(i);
        }

        for (var i = _pendingWeights.Count - 1; i >= 0; i--)
        {
            var id = _pendingWeights[i];
            var template = Templates.ById<SkillTemplate>(id);
            if (template == null)
                continue;
            var weight = AiWeights[id];
            try
            {
                template.AIConfig = new Il2CppMenace.Tactical.AI.Behaviors.Data.InflictDamage
                {
                    ScoreMult = weight.ScoreMult,
                    InfluencesPositioning = weight.InfluencesPositioning,
                }.Cast<Il2CppMenace.Tactical.AI.Data.SkillBehavior>();
                Context.Log.Debug($"titan ai: '{id}' scoreMult={weight.ScoreMult} positioning={weight.InfluencesPositioning}");
            }
            catch (Exception ex)
            {
                Context.Log.Error($"titan ai: assigning '{id}' failed: {ex.GetType().Name}: {ex.Message}");
            }
            _pendingWeights.RemoveAt(i);
        }
    }
}
