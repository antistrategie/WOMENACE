using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// An aura a perk or status carries: every unit allied with the owner within Radius tiles carries Effect, a
// status the aura adds and removes as units move. RequiredSkill, when set, limits it to units that
// carry that skill. LightConditionSkill, when set, limits the whole aura to missions whose light
// condition applies that skill (Suomi's Northern Light works only at night, so it sets the night
// effect). IncludeSelf lets the owner carry it too. AuraSystem keeps every aura's recipients
// current.
[JiangyuType("AuraGrant")]
public sealed partial class AuraGrant : SkillEventHandlerTemplate
{
    public SkillTemplate Effect;
    public int Radius = 3;
    public SkillTemplate RequiredSkill;
    public SkillTemplate LightConditionSkill;
    public bool IncludeSelf = true;

    public override SkillEventHandler Create() => new AuraGrantHandler
    {
        Effect = Effect,
        Radius = Radius,
        RequiredSkill = RequiredSkill,
        LightConditionSkill = LightConditionSkill,
        IncludeSelf = IncludeSelf,
    };
}

[JiangyuType("AuraGrantHandler")]
public sealed partial class AuraGrantHandler : SkillEventHandler
{
    public SkillTemplate Effect;
    public int Radius = 3;
    public SkillTemplate RequiredSkill;
    public SkillTemplate LightConditionSkill;
    public bool IncludeSelf = true;
    private Actor _actor;
    private bool _removed;

    // GetActor can be null when the perk is added at mission start, so the owner is registered
    // on the first update with a live actor.
    public override void OnUpdate(EntityProperties _properties)
    {
        if (_actor != null || _removed)
            return;
        var actor = GetActor();
        if (actor == null || !actor.IsAlive())
            return;
        _actor = actor;
        AuraSystem.Register(actor, this);
    }

    // Registering again is harmless, and keeps the aura alive if a scene change cleared it after
    // the first update.
    public override void OnTurnStart()
    {
        if (_actor != null && !_removed && _actor.IsAlive())
            AuraSystem.Register(_actor, this);
    }

    public override void OnDeath() => AuraSystem.Unregister(this);

    // An aura on a status (the open umbrella) ends with the status. Removal can be deferred while
    // the container dispatches, so the flag keeps a late update from registering it again.
    public override void OnAdded() => _removed = false;

    public override void OnRemoved()
    {
        _removed = true;
        _actor = null;
        AuraSystem.Unregister(this);
    }

    public override void OnMissionFinished()
    {
        AuraSystem.Unregister(this);
        _actor = null;
    }
}

public sealed class AuraSystem : JiangyuSystem
{
    private static AuraSystem _instance;
    private static readonly List<(Actor Owner, AuraGrantHandler Aura)> Auras = new();
    // Every effect an aura has handed out this mission, and which units the auras gave it to. A
    // unit leaving every aura loses only an effect an aura gave it, so the same status granted by
    // something else stays.
    private static readonly Dictionary<IntPtr, SkillTemplate> Managed = new();
    private static readonly HashSet<(IntPtr Effect, IntPtr Unit)> Granted = new();

    public override void OnInit()
    {
        _instance = this;
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnMovementFinished", OnBoardChanged);
        Context.Patches.Postfix("Il2CppMenace.States.TacticalState", "OnTurnStart", OnBoardChanged);
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnElementDeath", OnBoardChanged);
    }

    // The owners are this mission's actors. A scene change leaves their pointers stale.
    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        Auras.Clear();
        Managed.Clear();
        Granted.Clear();
    }

    public override void OnUnload()
    {
        Auras.Clear();
        Managed.Clear();
        Granted.Clear();
        _instance = null;
    }

    internal static void Register(Actor owner, AuraGrantHandler aura)
    {
        if (aura.Effect == null)
        {
            _instance?.Context.Log.Warn("aura: perk has no effect to hand out");
            return;
        }
        var known = Auras.Exists(a => a.Aura == aura && a.Owner?.Pointer == owner.Pointer);
        Auras.RemoveAll(a => a.Aura == aura);
        Auras.Add((owner, aura));
        Managed[aura.Effect.Pointer] = aura.Effect;
        if (!known)
            Refresh();
    }

    internal static void Unregister(AuraGrantHandler aura)
    {
        if (Auras.RemoveAll(a => a.Aura == aura) > 0)
            Refresh();
    }

    private static void OnBoardChanged(PatchInfo info)
    {
        if (Managed.Count > 0)
            Refresh();
    }

    // Works out who should carry each aura's effect and makes it so.
    internal static void Refresh()
    {
        try
        {
            var tm = TacticalManager.Get();
            if (tm == null)
                return;
            var actors = new List<Actor>();
            tm.ForEachActor((Il2CppSystem.Action<Actor>)(Action<Actor>)(a =>
            {
                if (a != null && a.IsAlive())
                    actors.Add(a);
            }));

            // The skill this mission's light condition applies (the night effect at night).
            var light = tm.GetMission()?.GetLightConditionTemplate()?.SkillToApply?.Pointer ?? IntPtr.Zero;
            var wanted = new Dictionary<IntPtr, HashSet<IntPtr>>();
            foreach (var key in Managed.Keys)
                wanted[key] = new HashSet<IntPtr>();
            // Snapshots: adding or removing a status runs container callbacks that can reach an
            // aura's OnUpdate and register it, which would change these collections mid-loop.
            foreach (var (owner, aura) in Auras.ToList())
            {
                if (owner == null || !owner.IsAlive() || owner.GetTile() is not { } centre)
                    continue;
                if (aura.LightConditionSkill != null && aura.LightConditionSkill.Pointer != light)
                    continue;
                var set = wanted[aura.Effect.Pointer];
                foreach (var unit in actors)
                {
                    if (unit.Pointer == owner.Pointer ? !aura.IncludeSelf : !unit.IsAlliedWith(owner))
                        continue;
                    if (unit.GetTile() is not { } tile || Pierce.Distance(centre, tile) > aura.Radius)
                        continue;
                    if (aura.RequiredSkill != null && SkillEffects.CountInstances(unit.GetSkills(), aura.RequiredSkill) == 0)
                        continue;
                    set.Add(unit.Pointer);
                }
            }

            foreach (var (key, effect) in Managed.ToList())
            {
                var set = wanted[key];
                foreach (var unit in actors)
                {
                    var skills = unit.GetSkills();
                    var has = SkillEffects.CountInstances(skills, effect) > 0;
                    if (set.Contains(unit.Pointer) && !has)
                    {
                        if (SkillEffects.TryAddEffect(unit, effect, message => _instance?.Context.Log.Warn($"aura: {message}")))
                            Granted.Add((key, unit.Pointer));
                    }
                    else if (!set.Contains(unit.Pointer) && has && Granted.Remove((key, unit.Pointer)))
                        SkillEffects.RemoveInstances(skills, effect);
                }
            }
        }
        catch (Exception ex)
        {
            _instance?.Context.Log.Warn($"aura: refresh failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// On an aura's status: while the status is on a unit, the unit does not carry Suppressed. Granting
// the status strips the unit's instances of it, and removing the status during a mission gives one
// back if the unit had it, or if the mission's light condition applies it. Northern Light uses it against effect.light.night: the game reads the
// night immunity flag only when it applies the night effect at mission start
// (TacticalManager.ApplyWeatherAndLightEffects), so a unit that walks into the aura already
// carries the effect and the flag alone would never lift it.
[JiangyuType("SuppressSkill")]
public sealed partial class SuppressSkill : SkillEventHandlerTemplate
{
    public SkillTemplate Suppressed;

    public override SkillEventHandler Create() => new SuppressSkillHandler { Suppressed = Suppressed };
}

[JiangyuType("SuppressSkillHandler")]
public sealed partial class SuppressSkillHandler : SkillEventHandler
{
    public SkillTemplate Suppressed;
    private Actor _held;

    public override void OnAdded()
    {
        try
        {
            var actor = GetActor();
            if (actor == null || Suppressed == null)
                return;
            if (SkillEffects.RemoveInstances(actor.GetSkills(), Suppressed) > 0)
                _held = actor;
        }
        catch (Exception ex)
        {
            Log.Warn($"suppress skill: strip failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public override void OnRemoved()
    {
        try
        {
            // A unit that had the skill stripped gets it back. So does one that never had it when
            // the mission's light condition applies it: the status's own immunity flag kept the
            // game from applying night to a unit inside the aura at mission start.
            var held = _held;
            _held = null;
            var actor = held ?? GetActor();
            if (actor == null || !actor.IsAlive() || Suppressed == null || !TacticalManager.IsMissionRunning())
                return;
            var light = TacticalManager.Get()?.GetMission()?.GetLightConditionTemplate()?.SkillToApply;
            if (held == null && light?.Pointer != Suppressed.Pointer)
                return;
            if (SkillEffects.CountInstances(actor.GetSkills(), Suppressed) == 0)
                SkillEffects.TryAddEffect(actor, Suppressed, message => Log.Warn($"suppress skill: {message}"));
        }
        catch (Exception ex)
        {
            Log.Warn($"suppress skill: restore failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
