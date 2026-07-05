using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// PierceLine: the tuning-data carrier for the piercing skills (thrust and
// ult). It holds the swathe's reach (Tiles), its width, and the thrust's
// per-row accuracy falloff. The pierce itself is driven natively by
// PierceAoEShape, which SextansPierceShapeSystem assigns to the skill after
// reading these fields off the template; the ult sequence reads Tiles/Width
// too. Slotted onto a skill via KDL type="WOMENACE:PierceLine".
[JiangyuType("PierceLine")]
public sealed partial class PierceLine : SkillEventHandlerTemplate
{
    public int Tiles = 4;
    public int Width = 1;

    // Hit chance lost per row past the aimed tile, applied on top of the
    // native accuracy roll (PierceAoEShape rolls it per victim during the
    // application pass). Zero keeps every row equally certain.
    public float FalloffPerTile;

    public override SkillEventHandler Create()
        => new PierceLineHandler();
}

// No runtime behaviour: PierceLine's data is consumed by PierceAoEShape and
// the ult sequence, not by a per-instance handler. This exists only so the
// template's Create returns a valid registered handler.
[JiangyuType("PierceLineHandler")]
public sealed partial class PierceLineHandler : SkillEventHandler
{
}

// The blade only bites hostiles: allies of the attacker are spared, and so
// are the bystander factions. There is no IsHostileTo in the game API, so
// the test is assembled from IsAlliedWith plus the innocent faction types.
internal static class Pierce
{
    public static bool IsHostileTo(Actor attacker, Actor other)
    {
        if (attacker == null || other == null)
            return false;
        var faction = other.GetFaction();
        if (faction == FactionType.Neutral || faction == FactionType.Civilian)
            return false;
        return !other.IsAlliedWith(attacker);
    }

    // The swathe's geometry, shared by the AoE shape (preview + application)
    // and the ult sequence (teleport landing + payoff sweep): rows walk from
    // the aimed tile away from the origin, centre plus both perpendicular
    // flanks when width is 3. Cardinal thrusts flank at 90 degrees, diagonal
    // ones at 45: a diagonal's 90-degree neighbours are diagonal steps too,
    // which leaves a gapped, two-tile-wide checkerboard instead of a solid
    // three-wide band. The callback also receives the row's step (0 at the
    // aimed tile, growing away from the attacker; flanks share their row's
    // step) so callers can weight tiles by depth.
    public static void WalkSwathe(Tile origin, Tile target, int tiles, int width, Action<Tile, int> add)
    {
        if (target == null || add == null)
            return;
        add(target, 0);
        if (origin == null)
            return;
        var direction = origin.GetDirectionTo(target);
        var flankStep = (int)direction % 2 == 0 ? 2 : 1;
        var left = (Direction)(((int)direction + 8 - flankStep) % 8);
        var right = (Direction)(((int)direction + flankStep) % 8);
        var flanks = width >= 3;

        var tile = target;
        for (var step = 0; step <= tiles; step++)
        {
            if (step > 0)
            {
                tile = tile.GetNextTile(direction);
                if (tile == null)
                    break;
                add(tile, step);
            }
            if (flanks)
            {
                add(tile.GetNextTile(left), step);
                add(tile.GetNextTile(right), step);
            }
        }
    }

    // The dash variant: rows run FROM the origin TO the target instead of
    // onward past it. The clicked tile may sit off the eight compass rays, so
    // the walk follows the nearest compass direction for the target's
    // distance: the swathe (and the landing) snap to the ray. Same flank
    // rule as WalkSwathe.
    public static void WalkBetween(Tile origin, Tile target, int maxTiles, int width, Action<Tile, int> add)
    {
        if (origin == null || target == null || add == null)
            return;
        var direction = origin.GetDirectionTo(target);
        var steps = Math.Min(Distance(origin, target), maxTiles);
        var flankStep = (int)direction % 2 == 0 ? 2 : 1;
        var left = (Direction)(((int)direction + 8 - flankStep) % 8);
        var right = (Direction)(((int)direction + flankStep) % 8);
        var flanks = width >= 3;

        var tile = origin;
        for (var step = 1; step <= steps; step++)
        {
            tile = tile.GetNextTile(direction);
            if (tile == null)
                break;
            add(tile, step);
            if (flanks)
            {
                add(tile.GetNextTile(left), step);
                add(tile.GetNextTile(right), step);
            }
        }
    }

    // Where the dash actually ends: the last tile of the snapped centre row.
    public static Tile SnappedEnd(Tile origin, Tile target, int maxTiles)
    {
        if (origin == null || target == null)
            return target;
        var direction = origin.GetDirectionTo(target);
        var steps = Math.Min(Distance(origin, target), maxTiles);
        var tile = origin;
        for (var step = 1; step <= steps; step++)
        {
            var next = tile.GetNextTile(direction);
            if (next == null)
                break;
            tile = next;
        }
        return tile;
    }

    // Chebyshev distance: diagonal steps count as one, matching GetNextTile
    // walks.
    public static int Distance(Tile a, Tile b)
        => Math.Max(Math.Abs(a.GetX() - b.GetX()), Math.Abs(a.GetZ() - b.GetZ()));
}

// The pierce swathe as a native AoE shape. With this assigned to the skill
// (UseCustomAoEShape), the game itself computes the affected tiles: the
// targeting UI highlights them on hover and the application pass hits them,
// so the pierce needs no manual re-application at all. The shape walks rows
// from the aimed tile away from the attacker (the origin tile), centre plus
// both perpendicular flanks when Width is 3. Tiles holding an ally of the
// attacker are excluded: they neither highlight nor get hit.
[JiangyuType("PierceAoEShape", Interfaces = new[] { typeof(ICustomAoEShape) })]
public sealed partial class PierceAoEShape : Il2CppSystem.Object
{
    public int Tiles = 4;
    public int Width = 1;

    // false: the thrust's swathe, running from the aimed tile onward, away
    // from the attacker. true: the ult's dash lane, running from the
    // attacker to the aimed tile (snapped to the compass ray).
    public bool ToTarget;

    // Hit chance lost per row past the aimed tile: victims at row step s
    // are dropped from the application with probability FalloffPerTile * s,
    // a miss on top of the native accuracy roll. Only the application pass
    // rolls, so the preview stays stable.
    public float FalloffPerTile;

    // Not the shape's reach (GetAffectedTiles carries that): the radius only
    // feeds the generic circle indicator drawn around the hovered target,
    // which reads as a ring of red tiles. Zero suppresses it.
    public int GetAoERadius() => 0;

    // For the dash lane the effective target is the snapped end of the ray,
    // so aim, preview and landing all agree even on off-axis clicks.
    public Tile GetOverrideTargetTile(Tile _origin, Tile _target)
        => ToTarget ? Pierce.SnappedEnd(_origin, _target, Tiles) : _target;

    public void GetAffectedTiles(Tile _origin, Tile _target, Il2CppSystem.Collections.Generic.List<Tile> _into, bool _lineOfFireNeeded, bool _skipEmptyTiles)
    {
        try
        {
            if (_target == null || _into == null)
                return;
            // The dash's APPLICATION pass (the skipEmptyTiles caller) gets
            // nothing: it resolves after Sextans has moved onto the lane and
            // must stay a blank so nobody on it is touched by the native
            // use. SextansUltSystem deals the wounds itself. The preview
            // caller keeps the full lane.
            if (ToTarget && _skipEmptyTiles)
            {
                Log.Debug($"[PierceShape] dash application pass suppressed at ({_target.GetX()},{_target.GetZ()})");
                return;
            }
            var attacker = _origin?.GetEntity()?.TryCast<Actor>();
            // The dash lane never includes its own landing tile: the tile
            // list is applied AFTER Sextans has moved onto it, and even a
            // zero-damage application there makes her flinch mid-strike.
            var landing = ToTarget ? Pierce.SnappedEnd(_origin, _target, Tiles) : null;

            void Add(Tile tile, int step)
            {
                if (tile == null)
                    return;
                if (landing != null && tile.GetX() == landing.GetX() && tile.GetZ() == landing.GetZ())
                    return;
                // occupied tiles only make the cut when the occupant is a
                // PROVEN hostile: the attacker herself, allies and
                // bystanders neither highlight nor get hit (empty tiles stay
                // in so the swathe previews whole). Fail closed: the
                // application pass can re-query after the ult's dash, when
                // the origin tile is empty and the attacker unresolvable,
                // and an unknown attacker must hit nobody rather than
                // everybody.
                var occupant = tile.GetEntity()?.TryCast<Actor>();
                if (occupant != null
                    && (attacker == null || !Pierce.IsHostileTo(attacker, occupant)))
                    return;
                if (_skipEmptyTiles && tile.GetEntity() == null)
                    return;
                // the blade's bite fades with distance: victims deep in the
                // swathe can slip the application entirely. Rolled only in
                // the application pass so targeting previews stay stable.
                if (_skipEmptyTiles && occupant != null && FalloffPerTile > 0f && step > 0
                    && UnityEngine.Random.value < FalloffPerTile * step)
                {
                    Log.Debug($"[PierceShape] falloff miss at row {step} ({tile.GetX()},{tile.GetZ()})");
                    return;
                }
                _into.Add(tile);
            }

            if (ToTarget)
                Pierce.WalkBetween(_origin, _target, Tiles, Width, Add);
            else
                Pierce.WalkSwathe(_origin, _target, Tiles, Width, Add);
        }
        catch (Exception ex)
        {
            Log.Error($"[PierceShape] GetAffectedTiles failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// Assigns the pierce shape to the piercing skills (thrust and ult) once
// templates are applied. The CustomAoEShape field is an Odin-serialised
// interface, which KDL cannot author, so the KDL-authored PierceLine handler
// stays the tuning source: its Tiles/Width are read here, and the handler
// itself goes dormant (UseCustomAoEShape gates its OnApply) as the fallback
// path.
public sealed class SextansPierceShapeSystem : JiangyuSystem
{
    public override void OnTemplatesApplied()
    {
        // the thrust aims a direction: enemy or empty tile, swathe runs onward
        Assign("active.sextans_thrust", toTarget: false);
        // the ult aims a destination: empty tiles only (she has to be able to
        // stand there), swathe runs from her to it
        Assign("active.sextans_ult", toTarget: true);
    }

    private void Assign(string skillId, bool toTarget)
    {
        try
        {
            var template = Templates.ById<SkillTemplate>(skillId, msg => Context.Log.Warn($"pierce shape: {msg}"));
            if (template == null)
            {
                Context.Log.Warn($"pierce shape: skill template '{skillId}' not found; pierce stays handler-driven");
                return;
            }

            // Aimable without a victim: empty tiles become valid targets, so
            // the skill can be loosed with nothing in melee range and the
            // swathe still previews and hits whatever stands along it. Set
            // here rather than in KDL because the loader's strict enum
            // membership check rejects flag combinations that are not
            // themselves named members. The dash additionally DROPS enemies
            // as targets: its aim point is the landing tile, and she cannot
            // land on an occupied one.
            template.TargetsAllowed |= SkillTarget.EmptyTile;
            if (toTarget)
                template.TargetsAllowed &= ~SkillTarget.EnemyActor;

            var tiles = 4;
            var width = 1;
            var falloff = 0f;
            var handlers = template.EventHandlers;
            for (var i = 0; handlers != null && i < handlers.Count; i++)
            {
                var pierce = handlers[i]?.TryCast<PierceLine>();
                if (pierce == null)
                    continue;
                tiles = pierce.Tiles;
                width = pierce.Width;
                falloff = pierce.FalloffPerTile;
                break;
            }

            var shape = new PierceAoEShape { Tiles = tiles, Width = width, ToTarget = toTarget, FalloffPerTile = falloff };
            template.CustomAoEShape = shape.Cast<ICustomAoEShape>();
            template.UseCustomAoEShape = true;
            template.AoEType = SkillAoEType.AllTiles;
            Context.Log.Debug($"pierce shape: assigned to '{skillId}' (tiles={tiles}, width={width}, toTarget={toTarget})");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"pierce shape: assignment failed for '{skillId}', pierce stays handler-driven: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
