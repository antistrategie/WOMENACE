using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Constellation, Nemesis's tier 3 perk: while she is alive in a mission, every rifle Doll on her
// side (Affinity speaker tag wmgfl_class_rifle) carries the Constellation status, which raises
// their attacks' Accuracy and damage for each other living rifle Doll in the battle. The perk's
// handler only names the status. ConstellationSystem hands it to every rifle Doll on her side
// when she is registered and again each round, so one deployed later picks it up too.
[JiangyuType("Constellation")]
public sealed partial class Constellation : SkillEventHandlerTemplate
{
    public SkillTemplate Effect;

    public override SkillEventHandler Create() => new ConstellationHandler { Effect = Effect };
}

[JiangyuType("ConstellationHandler")]
public sealed partial class ConstellationHandler : SkillEventHandler
{
    public SkillTemplate Effect;
    private Actor _actor;

    // GetActor can be null when the perk is added at mission start, so she is registered on the
    // first update with a live actor.
    public override void OnUpdate(EntityProperties _properties)
    {
        if (_actor != null)
            return;
        var actor = GetActor();
        if (actor == null || !actor.IsAlive())
            return;
        _actor = actor;
        ConstellationSystem.Register(actor, Effect);
    }

    public override void OnRoundStart() => ConstellationSystem.Spread();

    public override void OnDeath() => ConstellationSystem.Unregister(_actor);

    public override void OnMissionFinished()
    {
        ConstellationSystem.Unregister(_actor);
        _actor = null;
    }
}

// The status every rifle Doll carries. The bonus is counted when an attack is built, so it always
// matches the battle at that moment, and the icon's stack count is refreshed as turns start.
[JiangyuType("ConstellationBonus")]
public sealed partial class ConstellationBonus : SkillEventHandlerTemplate
{
    public int AccuracyPerDoll = 5;
    public float DamagePerDoll = 0.1f;

    public override SkillEventHandler Create()
        => new ConstellationBonusHandler { AccuracyPerDoll = AccuracyPerDoll, DamagePerDoll = DamagePerDoll };
}

[JiangyuType("ConstellationBonusHandler")]
public sealed partial class ConstellationBonusHandler : SkillEventHandler
{
    public int AccuracyPerDoll = 5;
    public float DamagePerDoll = 0.1f;
    private int _dolls = -1;

    public override bool HasStackCountDisplayed() => true;

    public override bool IsHidden() => _dolls <= 0;

    public override void OnAdded() => Refresh();

    public override void OnRoundStart() => Refresh();

    public override void OnTurnStart() => Refresh();

    public override void OnBeforeAnySkillUsed(Skill _skill, Tile _fromTile, Tile _targetTile, EntityProperties _properties, Entity _overrideTargetEntity)
    {
        try
        {
            if (_skill == null || !_skill.IsAttack() || _properties == null)
                return;
            var dolls = ConstellationSystem.OtherRifleDolls(GetActor());
            if (dolls <= 0)
                return;
            _properties.Accuracy += AccuracyPerDoll * dolls;
            _properties.DamageMult *= 1f + DamagePerDoll * dolls;
        }
        catch (Exception ex)
        {
            ConstellationSystem.Warn($"pre-attack failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Refresh()
    {
        try
        {
            var actor = GetActor();
            var dolls = ConstellationSystem.OtherRifleDolls(actor);
            if (dolls == _dolls)
                return;
            _dolls = dolls;
            if (ParentSkill is { } parent)
                parent.StackCount = dolls;
            RallyBarsSystem.RefreshStatusIcons(actor);
        }
        catch (Exception ex)
        {
            ConstellationSystem.Warn($"refresh failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

public sealed class ConstellationSystem : JiangyuSystem
{
    private static ConstellationSystem _instance;
    // The Nemeses carrying the perk this mission, and the status they hand out.
    private static readonly Dictionary<IntPtr, Actor> Sources = new();
    private static SkillTemplate _effect;

    public override void OnInit() => _instance = this;

    // The sources are this mission's actors. A scene change (a mission ending, quitting to the
    // menu, loading a save) leaves their pointers stale, so they go.
    public override void OnSceneLoaded(int buildIndex, string sceneName) => Sources.Clear();

    public override void OnUnload()
    {
        Sources.Clear();
        _effect = null;
        _instance = null;
    }

    internal static void Warn(string message) => _instance?.Context.Log.Warn($"constellation: {message}");

    internal static void Register(Actor source, SkillTemplate effect)
    {
        if (source == null || effect == null)
        {
            Warn("perk has no status to hand out");
            return;
        }
        Sources[source.Pointer] = source;
        _effect = effect;
        Spread();
    }

    internal static void Unregister(Actor source)
    {
        if (source != null)
            Sources.Remove(source.Pointer);
    }

    // A living source on this faction, or null.
    private static Actor SourceFor(int faction)
    {
        foreach (var source in Sources.Values)
            if (source != null && source.IsAlive() && source.GetFactionID() == faction)
                return source;
        return null;
    }

    internal static bool IsRifleDoll(Actor actor)
        => actor != null && WeaponClasses.ClassFromTags(Affinity.OurSpeakerTags(actor)) == WeaponClass.Rifle;

    // Living rifle Dolls on the actor's side other than the actor itself, while a Nemesis with the
    // perk is alive on that side. Zero otherwise.
    internal static int OtherRifleDolls(Actor actor)
    {
        if (actor == null || !actor.IsAlive())
            return 0;
        // Attacks are built for every hovered tile and preview, so the count is taken once a
        // frame per actor.
        var frame = UnityEngine.Time.frameCount;
        if (frame != _countFrame)
        {
            _countFrame = frame;
            Counts.Clear();
        }
        if (!Counts.TryGetValue(actor.Pointer, out var cached))
            Counts[actor.Pointer] = cached = Count(actor);
        return cached;
    }

    private static int _countFrame = -1;
    private static readonly Dictionary<IntPtr, int> Counts = new();

    private static int Count(Actor actor)
    {
        var faction = actor.GetFactionID();
        if (SourceFor(faction) == null)
            return 0;
        var count = 0;
        TacticalManager.Get()?.ForEachActor((Il2CppSystem.Action<Actor>)(Action<Actor>)(other =>
        {
            if (other != null && other.Pointer != actor.Pointer && other.IsAlive()
                && other.GetFactionID() == faction && IsRifleDoll(other))
                count++;
        }));
        return count;
    }

    // Every living rifle Doll on a source's side that lacks the status is given it.
    internal static void Spread()
    {
        if (_effect == null || Sources.Count == 0)
            return;
        try
        {
            var factions = new HashSet<int>();
            foreach (var source in Sources.Values)
                if (source != null && source.IsAlive())
                    factions.Add(source.GetFactionID());
            TacticalManager.Get()?.ForEachActor((Il2CppSystem.Action<Actor>)(Action<Actor>)(actor =>
            {
                if (actor == null || !actor.IsAlive() || !factions.Contains(actor.GetFactionID()) || !IsRifleDoll(actor))
                    return;
                if (SkillEffects.CountInstances(actor.GetSkills(), _effect) == 0)
                    SkillEffects.TryAddEffect(actor, _effect, Warn);
            }));
        }
        catch (Exception ex)
        {
            Warn($"spread failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
