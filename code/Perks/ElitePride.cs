using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Elite Doll's Pride: every visible enemy within Range tiles of the owner adds AccuracyPerEnemy
// Accuracy and DamagePerEnemy damage to her attacks, up to MaxEnemies, and she is never Pinned
// Down. It rides the status effect the perk adds, via KDL type="WOMENACE:ElitePride", which shows
// the enemy count on its icon.
//
// The bonus is counted from the tile the attack is made from, in OnBeforeAnySkillUsed, the window
// ChangePropertyConditional uses for No Mercy, so it lands on that one attack's property snapshot.
[JiangyuType("ElitePride")]
public sealed partial class ElitePride : SkillEventHandlerTemplate
{
    public int AccuracyPerEnemy = 5;
    public float DamagePerEnemy = 0.05f;
    public int Range = 6;
    public int MaxEnemies = 4;

    public override SkillEventHandler Create() => new ElitePrideHandler
    {
        AccuracyPerEnemy = AccuracyPerEnemy,
        DamagePerEnemy = DamagePerEnemy,
        Range = Range,
        MaxEnemies = MaxEnemies,
    };
}

[JiangyuType("ElitePrideHandler")]
public sealed partial class ElitePrideHandler : SkillEventHandler
{
    public int AccuracyPerEnemy = 5;
    public float DamagePerEnemy = 0.05f;
    public int Range = 6;
    public int MaxEnemies = 4;

    private int _enemies;
    // GetActor can be null at mission boundaries, so the actor registered is the one removed
    private Actor _actor;

    public override bool HasStackCountDisplayed() => true;

    public override bool IsHidden() => _enemies <= 0;

    public override void OnAdded()
    {
        Refresh();
    }

    public override void OnMissionFinished()
    {
        ElitePrideSystem.Unregister(_actor);
        _actor = null;
        _enemies = 0;
    }

    public override void OnDeath() => ElitePrideSystem.Unregister(_actor);

    public override void OnRoundStart() => Refresh();

    public override void OnTurnStart() => Refresh();

    public override void OnMovementFinished(Tile _tile) => Refresh();

    public override void OnBeforeAnySkillUsed(Skill _skill, Tile _fromTile, Tile _targetTile, EntityProperties _properties, Entity _overrideTargetEntity)
    {
        try
        {
            if (_skill == null || !_skill.IsAttack() || _properties == null)
                return;
            var enemies = CountEnemies(GetActor(), _fromTile);
            if (enemies <= 0)
                return;
            _properties.Accuracy += AccuracyPerEnemy * enemies;
            _properties.DamageMult *= 1f + DamagePerEnemy * enemies;
        }
        catch (Exception ex)
        {
            Log.Warn($"elite pride: pre-attack failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Recounts from the owner's current tile and repaints the icon when the count moves.
    internal void Refresh()
    {
        try
        {
            var actor = GetActor();
            if (actor != null)
            {
                _actor = actor;
                ElitePrideSystem.Register(actor, this);
            }
            var enemies = CountEnemies(actor, null);
            if (enemies == _enemies)
                return;
            _enemies = enemies;
            var parent = ParentSkill;
            if (parent != null)
                parent.StackCount = enemies;
            RallyBarsSystem.RefreshStatusIcons(actor);
        }
        catch (Exception ex)
        {
            Log.Warn($"elite pride: refresh failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Living hostile units within Range of the tile, counted only when the owner's side can see
    // their tile, so the bonus never reveals a hidden enemy.
    private int CountEnemies(Actor owner, Tile from)
    {
        if (owner == null || !owner.IsAlive())
            return 0;
        from ??= owner.GetTile();
        if (from == null)
            return 0;
        var faction = owner.GetFactionID();
        var count = 0;
        TacticalManager.Get()?.ForEachActor((Il2CppSystem.Action<Actor>)(Action<Actor>)(other =>
        {
            if (other == null || !other.IsAlive() || !Pierce.IsHostileTo(owner, other))
                return;
            var tile = other.GetTile();
            if (tile != null && tile.IsVisibleToFaction(faction) && Pierce.Distance(from, tile) <= Range)
                count++;
        }));
        return Math.Min(count, MaxEnemies);
    }
}

// Holds the owners of Elite Doll's Pride for the mission. It keeps them from being Pinned Down and
// refreshes their enemy count when other units move, die or change visibility, which their own
// handlers never hear about.
public sealed class ElitePrideSystem : JiangyuSystem
{
    private static ElitePrideSystem _instance;

    // Actor.GetSuppressionState (RVA 0x608D40) reads Pinned Down at m_Suppression * 0.01 >= 0.666.
    private const float PinnedDownAt = 66.6f;
    private const float Ceiling = 66.5f;

    private readonly Dictionary<IntPtr, ElitePrideHandler> _owners = new();
    private bool _clamping;

    public override void OnInit()
    {
        _instance = this;
        // An attempt that ends without OnMissionFinished (abort, quit) must not leave stale owners.
        Context.Patches.Postfix("Il2CppMenace.Strategy.Operation", "OnMissionStarted", _ => _owners.Clear());
        // Every suppression write goes through SetSuppression (ApplySuppression and
        // ChangeSuppressionAndUpdateAP both call it). The per-turn decay in Actor.OnTurnEnd writes
        // the field directly but only ever lowers it.
        Context.Patches.Prefix("Il2CppMenace.Tactical.Actor", "SetSuppression", OnSetSuppression);
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnMovementFinished", RefreshAll);
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnDeath", RefreshAll);
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnVisibleToPlayer", RefreshAll);
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnHiddenToPlayer", RefreshAll);
    }

    internal static void Register(Actor actor, ElitePrideHandler handler)
    {
        if (_instance == null || actor == null || handler == null)
            return;
        _instance._owners[actor.Pointer] = handler;
    }

    internal static void Unregister(Actor actor)
    {
        if (_instance == null || actor == null)
            return;
        _instance._owners.Remove(actor.Pointer);
    }

    // Holds an owner's suppression just under Pinned Down. The write is replaced by one at the
    // ceiling, so suppression still builds up to Suppressed and the AP update runs as normal.
    private void OnSetSuppression(PatchInfo info)
    {
        if (_clamping || _owners.Count == 0 || info.Args == null || info.Args.Count < 1)
            return;
        try
        {
            if (info.Args[0] is not float value || value < PinnedDownAt)
                return;
            var actor = (info.Instance as Il2CppObjectBase)?.TryCast<Actor>();
            if (actor == null || !_owners.ContainsKey(actor.Pointer))
                return;
            info.Skip = true;
            _clamping = true;
            try { actor.SetSuppression(Ceiling); }
            finally { _clamping = false; }
            Context.Log.Debug($"elite pride: suppression {value:0.#} held at {Ceiling}");
        }
        catch (Exception ex) { Context.Log.Warn($"elite pride: suppression clamp failed: {ex.Message}"); }
    }

    private void RefreshAll(PatchInfo info)
    {
        if (_owners.Count == 0)
            return;
        foreach (var handler in _owners.Values.ToList())
            handler.Refresh();
    }
}
