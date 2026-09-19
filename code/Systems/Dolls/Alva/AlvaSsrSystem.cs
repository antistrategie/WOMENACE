using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Alva's SSR kit (6P33). Frost Echo plants an ice shard whose field lives for
// a few rounds and feeds Freeze build-up to every enemy that ends a turn
// inside it. The field is a ledger here rather than a vanilla tile effect:
// the shard the skill spawns is the visual, and this system owns the radius,
// the lifetime and the per-turn Freeze, all read off the FrostEchoZone
// handler the skill carries (so KDL keeps the numbers and the tooltips bind
// to them).
//
// The imprint: a field Alva fired also marks the enemies it ticks with
// Hypothermia (effect.alva_frost_echo), refreshed on every tick and outliving
// the field by its own lifetime. A marked unit takes the FrostEchoMark
// handler's DamageMult from any skill that deals Freeze damage (any skill
// carrying an ElementalDamage handler for Freeze, whoever fires it). The
// multiplier is registered with ElementsSystem so the hit path here (the
// Actor.OnDamageReceived prefix the Vector kit uses) and the hover preview
// (ElementalDamageHandler's expected-damage contributor) read the same number.
public sealed class AlvaSsrSystem : JiangyuSystem
{
    public const string FrostEchoSkillId = "active.alva_ssr_frost_echo";
    public const string MarkId = "effect.alva_frost_echo";

    private SkillTemplate _mark;
    private int _freeze = -2;

    // The field's shape, shared by the Frost Echo template's preview, its application (the per-tile
    // frost spawns on exactly these tiles) and this system's occupancy test. Its Radius is the
    // FrostEchoZone handler's authored value, published when the handler is instantiated: the
    // CustomAoEShape field is Odin-serialised and cannot be authored in KDL, and a handler
    // template's fields read back as their initialisers, so the instance is the one trustworthy
    // source. Until a skill instance exists the shape falls back to the default radius.
    private static FrostFieldAoEShape _shape;
    private bool _shapeAssigned;

    internal static void PublishRadius(int radius)
    {
        if (_shape != null && radius > 0 && _shape.Radius != radius)
            _shape.Radius = radius;
    }

    private void AssignShape()
    {
        if (_shapeAssigned)
            return;
        try
        {
            var template = Templates.ById<SkillTemplate>(FrostEchoSkillId, msg => Context.Log.Debug($"alva ssr: {msg}"));
            if (template == null)
                return;
            _shape ??= new FrostFieldAoEShape();
            template.CustomAoEShape = _shape.Cast<ICustomAoEShape>();
            template.UseCustomAoEShape = true;
            template.AoEType = SkillAoEType.AllTiles;
            _shapeAssigned = true;
            Context.Log.Debug($"alva ssr: field shape assigned to '{FrostEchoSkillId}' (radius {_shape.Radius})");
        }
        catch (Exception ex)
        {
            _shapeAssigned = true;
            Context.Log.Warn($"alva ssr: field shape assignment failed, the parent's 3x3 grid stays: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class Field
    {
        public Tile Centre;
        // The skill's own affected tiles at cast time (what the hover preview highlighted), so the
        // field and the preview can never disagree. Radius is the fallback when the engine gives
        // none back.
        public List<Tile> Tiles = new();
        public int Radius;
        public float FreezePerTurn;
        // The last round the field ticks in (inclusive).
        public int LastRound;
        // Retained for the hostility test; the faction outlives the caster.
        public Actor Caster;
        public bool OwnerShot;
    }

    private readonly List<Field> _fields = new();

    // Skill template pointer -> whether the skill carries Freeze build-up.
    private readonly Dictionary<IntPtr, bool> _dealsFreeze = new();

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnSkillUse", OnSkillUse);
        // Actor.OnTurnEnd runs SetTurnDone (which raises TacticalManager.InvokeOnTurnEnd) and THEN
        // the actor's SkillContainer.OnTurnEnd, where LifetimeLimit counts the mark down. A postfix
        // on the whole routine puts the refresh after that countdown; hooked on InvokeOnTurnEnd it
        // would land before it and the mark would expire a turn early.
        Context.Patches.Postfix("Il2CppMenace.Tactical.Actor", "OnTurnEnd", OnTurnEnd);
        ElementsSystem.RegisterVictimDamageMult((victim, element) => element == _freeze ? MarkMult(victim) : 1f);
        // The 3-arg overload is the concrete Actor override, same binding as
        // MeleeVsFliersSystem and VectorSsrSystem.
        Context.Patches.Prefix("Il2CppMenace.Tactical.Actor", "OnDamageReceived", 3, OnDamageReceived);
    }

    public override void OnTemplatesApplied()
    {
        _mark = Templates.ById<SkillTemplate>(MarkId, msg => Context.Log.Warn($"alva ssr: {msg}"));
        _freeze = ElementsSystem.ElementIndex("Freeze");
        _dealsFreeze.Clear();
        _shapeAssigned = false;
        AssignShape();
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        _fields.Clear();
        _dealsFreeze.Clear();
        AssignShape();
    }

    private static int CurrentRound()
        => TacticalManager.Get()?.GetRound() ?? 0;

    // A Frost Echo use opens a field at the aimed tile, sized by the skill's
    // own FrostEchoZone handler. Owner-fired fields carry the mark.
    private void OnSkillUse(PatchInfo info)
    {
        try
        {
            if (info.Args == null || info.Args.Count < 3)
                return;
            var actor = info.Args[0] as Actor;
            var skill = info.Args[1] as Skill;
            var tile = info.Args[2] as Tile;
            if (actor == null || skill == null || tile == null)
                return;
            if (skill.GetTemplate()?.GetID() != FrostEchoSkillId)
                return;

            FrostEchoZoneHandler zone = null;
            var handlers = skill.GetSkillEventHandlers();
            for (var i = 0; handlers != null && i < handlers.Length; i++)
            {
                zone = handlers[i]?.TryCast<FrostEchoZoneHandler>();
                if (zone != null)
                    break;
            }
            if (zone == null)
            {
                Context.Log.Warn("alva ssr: Frost Echo carries no FrostEchoZone handler, no field opened");
                return;
            }

            var round = CurrentRound();
            var field = new Field
            {
                Centre = tile,
                Radius = Math.Max(0, zone.Radius),
                FreezePerTurn = zone.FreezePerTurn,
                LastRound = round + Math.Max(1, zone.Rounds) - 1,
                Caster = actor,
                OwnerShot = SsrImprintSystem.IsOwnerWielding(skill),
            };
            try
            {
                var into = new Il2CppSystem.Collections.Generic.List<Tile>();
                skill.GetAoeTiles(actor.GetTile(), tile, into, false);
                for (var i = 0; i < into.Count; i++)
                    if (into[i] != null)
                        field.Tiles.Add(into[i]);
            }
            catch (Exception ex)
            {
                Context.Log.Warn($"alva ssr: reading the field's tiles failed, falling back to radius {field.Radius}: {ex.GetType().Name}: {ex.Message}");
            }
            _fields.Add(field);
            Context.Log.Debug($"alva ssr: Frost Echo field at ({tile.GetX()}, {tile.GetZ()}) {field.Tiles.Count} tile(s), rounds {round}..{field.LastRound}, freeze {field.FreezePerTurn}/turn, mark {field.OwnerShot}");

            // The shard lands on whoever is already inside: the first tick is the cast itself, the
            // rest come at each enemy's turn end while the field lives.
            foreach (var victim in Occupants(field))
                if (Tick(field, victim))
                    ApplyMark(victim);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"alva ssr: field open failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Every actor's turn end: each live field the actor stands in feeds its
    // Freeze, and an owner-fired one marks the actor (one mark refresh per
    // turn end however many fields overlap).
    private void OnTurnEnd(PatchInfo info)
    {
        try
        {
            if (_fields.Count == 0)
                return;
            var round = CurrentRound();
            _fields.RemoveAll(f => f.LastRound < round || f.Centre == null);
            if (_fields.Count == 0)
                return;

            var actor = info.Instance as Actor;
            if (actor == null || !actor.IsAlive())
                return;

            var marked = false;
            foreach (var field in _fields)
                marked |= Tick(field, actor);
            if (marked)
                ApplyMark(actor);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"alva ssr: field tick failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Whether the actor stands in the field: any of the field's tiles reports it as its actor (a
    // squad spread over several tiles counts wherever one of its soldiers stands, which is how the
    // engine's own AoE application reads a tile). Falls back to the anchor tile's distance when the
    // cast gave no tile list back.
    private static bool Inside(Field field, Actor actor)
    {
        if (field.Tiles.Count > 0)
        {
            foreach (var t in field.Tiles)
                if (t.HasActor() && t.GetActor()?.Pointer == actor.Pointer)
                    return true;
            return false;
        }
        var tile = actor.GetTile();
        return tile != null && field.Centre != null && Pierce.Distance(tile, field.Centre) <= field.Radius;
    }

    // Every distinct actor standing in the field right now.
    private static List<Actor> Occupants(Field field)
    {
        var seen = new HashSet<IntPtr>();
        var result = new List<Actor>();
        if (field.Tiles.Count > 0)
        {
            foreach (var t in field.Tiles)
            {
                if (!t.HasActor())
                    continue;
                var a = t.GetActor();
                if (a != null && a.IsAlive() && seen.Add(a.Pointer))
                    result.Add(a);
            }
            return result;
        }
        var factions = TacticalManager.Get()?.GetFactions();
        for (var i = 0; factions != null && i < factions.Length; i++)
        {
            var actors = factions[i]?.GetActors();
            for (var j = 0; actors != null && j < actors.Count; j++)
            {
                var a = actors[j];
                if (a != null && a.IsAlive() && Inside(field, a) && seen.Add(a.Pointer))
                    result.Add(a);
            }
        }
        return result;
    }

    // One field's effect on one actor: Freeze build-up when the actor is a hostile inside it.
    // Returns whether the field's mark applies to this actor, so the caller can refresh it once
    // however many fields overlap.
    private bool Tick(Field field, Actor actor)
    {
        if (!Pierce.IsHostileTo(field.Caster, actor))
            return false;
        if (!Inside(field, actor))
            return false;
        if (_freeze >= 0 && field.FreezePerTurn > 0f)
            ElementsSystem.AddBuildUp(actor, _freeze, field.FreezePerTurn);
        return field.OwnerShot;
    }

    // Add the mark, or refresh it: the effect does not stack, so a second Add
    // of a live instance runs the container's refresh path and the
    // LifetimeLimit's ResetLifetimeOnRefresh restarts the countdown.
    private void ApplyMark(Actor victim)
    {
        if (_mark == null || victim == null)
            return;
        var skills = victim.GetSkills();
        if (skills == null)
            return;
        var instance = _mark.CreateSkill(new Il2CppSystem.Nullable<Il2CppMenace.Strategy.Origin>());
        if (instance == null)
        {
            Context.Log.Warn($"alva ssr: CreateSkill returned null for '{MarkId}'");
            return;
        }
        var added = skills.Add(instance);
        Context.Log.Debug($"alva ssr: mark {(added ? "applied to" : "refreshed on")} '{victim.GetTemplate()?.GetID()}'");
    }

    // Whether a skill deals Freeze damage: it carries an ElementalDamage
    // handler for Freeze with a positive amount. Cached per template.
    private bool DealsFreeze(Skill skill)
    {
        var template = skill?.GetTemplate();
        if (template == null)
            return false;
        if (_dealsFreeze.TryGetValue(template.Pointer, out var known))
            return known;
        var result = false;
        var handlers = skill.GetSkillEventHandlers();
        for (var i = 0; handlers != null && i < handlers.Length; i++)
        {
            var elemental = handlers[i]?.TryCast<ElementalDamageHandler>();
            if (elemental != null && elemental.AmountPerHit > 0f
                && string.Equals(elemental.Element, "Freeze", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                break;
            }
        }
        _dealsFreeze[template.Pointer] = result;
        return result;
    }

    // The multiplier a marked victim takes from Freeze-dealing skills, read off
    // the live mark's own handler so the authored KDL value is the single
    // source. 1 when the victim carries no mark.
    private float MarkMult(Actor victim)
    {
        if (_mark == null || victim == null)
            return 1f;
        var mark = SkillEffects.FindInstance(victim.GetSkills(), _mark);
        if (mark == null)
            return 1f;
        var handlers = mark.GetSkillEventHandlers();
        for (var i = 0; handlers != null && i < handlers.Length; i++)
        {
            var h = handlers[i]?.TryCast<FrostEchoMarkHandler>();
            if (h != null)
                return h.DamageMult > 0f ? h.DamageMult : 1f;
        }
        return 1f;
    }

    // A marked victim takes the mark's DamageMult from any Freeze-dealing
    // skill. Read through ElementsSystem's registry, the same lookup the hover
    // preview makes, so the two cannot disagree.
    private void OnDamageReceived(PatchInfo info)
    {
        try
        {
            if (_mark == null || _freeze < 0 || info.Instance is not Actor victim)
                return;
            var damageInfo = (info.Args is { Count: > 2 } ? info.Args[2] : null) as DamageInfo;
            if (damageInfo == null || damageInfo.Damage <= 0)
                return;
            var skill = (info.Args is { Count: > 1 } ? info.Args[1] : null) as Skill;
            if (skill == null || !DealsFreeze(skill))
                return;
            var mult = ElementsSystem.VictimDamageMult(victim, _freeze);
            if (mult <= 0f || Mathf.Approximately(mult, 1f))
                return;
            damageInfo.Damage = Mathf.RoundToInt(damageInfo.Damage * mult);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"alva ssr: mark bonus failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// Rides Frost Echo in KDL (type="WOMENACE:FrostEchoZone"). Carries the field's
// numbers so the skill's tooltip can bind to them; AlvaSsrSystem reads them
// off the skill instance when the field opens.
[JiangyuType("FrostEchoZone")]
public sealed partial class FrostEchoZone : SkillEventHandlerTemplate
{
    // Tiles from the shard the field reaches (1 = the 3x3 around it).
    public int Radius = 1;
    // Rounds the field lives, counting the round it is planted in.
    public int Rounds = 4;
    // Freeze build-up fed to each enemy that ends a turn inside it.
    public float FreezePerTurn = 100f;

    public override SkillEventHandler Create()
    {
        AlvaSsrSystem.PublishRadius(Radius);
        return new FrostEchoZoneHandler { Radius = Radius, Rounds = Rounds, FreezePerTurn = FreezePerTurn };
    }
}

[JiangyuType("FrostEchoZoneHandler")]
public sealed partial class FrostEchoZoneHandler : SkillEventHandler
{
    public int Radius = 1;
    public int Rounds = 4;
    public float FreezePerTurn = 100f;
}

// Rides effect.alva_frost_echo (Hypothermia) in KDL (type="WOMENACE:FrostEchoMark").
// Holds the mark's damage multiplier; AlvaSsrSystem's damage prefix reads it off the
// bearer's live effect.
[JiangyuType("FrostEchoMark")]
public sealed partial class FrostEchoMark : SkillEventHandlerTemplate
{
    public float DamageMult = 1.4f;

    public override SkillEventHandler Create()
        => new FrostEchoMarkHandler { DamageMult = DamageMult };
}

[JiangyuType("FrostEchoMarkHandler")]
public sealed partial class FrostEchoMarkHandler : SkillEventHandler
{
    public float DamageMult = 1.4f;
}

// The Frost Echo field as a native AoE shape: every tile within Radius steps (eight directions,
// so a square) of the aimed tile. With this on the template the game previews and applies the same
// square, so the hover highlight, the per-tile frost and the field's Freeze ticks cannot drift
// apart. Ground effect, not a strike: allies and bystanders are not filtered out of the tiles.
[JiangyuType("FrostFieldAoEShape", Interfaces = new[] { typeof(ICustomAoEShape) })]
public sealed partial class FrostFieldAoEShape : Il2CppSystem.Object
{
    public int Radius = 1;

    // Zero suppresses the generic red radius ring around the hovered tile; the tiles themselves
    // highlight through GetAffectedTiles.
    public int GetAoERadius() => 0;

    public Tile GetOverrideTargetTile(Tile _origin, Tile _target) => _target;

    public void GetAffectedTiles(Tile _origin, Tile _target, Il2CppSystem.Collections.Generic.List<Tile> _into, bool _lineOfFireNeeded, bool _skipEmptyTiles)
    {
        try
        {
            if (_target == null || _into == null)
                return;
            var seen = new HashSet<IntPtr> { _target.Pointer };
            var ring = new List<Tile> { _target };
            var all = new List<Tile> { _target };
            for (var step = 0; step < Radius; step++)
            {
                var next = new List<Tile>();
                foreach (var tile in ring)
                {
                    for (var d = 0; d < 8; d++)
                    {
                        var neighbour = tile.GetNextTile((Direction)d);
                        if (neighbour == null || !seen.Add(neighbour.Pointer))
                            continue;
                        next.Add(neighbour);
                        all.Add(neighbour);
                    }
                }
                ring = next;
            }
            foreach (var tile in all)
            {
                if (_skipEmptyTiles && tile.GetEntity() == null)
                    continue;
                _into.Add(tile);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[FrostField] GetAffectedTiles failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
