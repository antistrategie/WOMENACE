using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The KDL-facing map-wide applicator, built for the Peace Fairy. A skill declares
//
//     append "EventHandlers" type="WOMENACE:BlanketEffect" {
//         set "EffectId" "effect.wmgfl_peace"
//     }
//
// and using it hands the named status effect to BlanketWindowSystem, which lays it on
// every living actor at the next round boundary and strips it at the boundary after. The
// effect therefore covers exactly one whole round, the same round for both sides, and
// never starts mid-turn where the user who called it has already acted.
[JiangyuType("BlanketEffect")]
public sealed partial class BlanketEffect : SkillEventHandlerTemplate
{
    public string EffectId = "";

    public override SkillEventHandler Create() => new BlanketEffectHandler { EffectId = EffectId };
}

[JiangyuType("BlanketEffectHandler")]
public sealed partial class BlanketEffectHandler : SkillEventHandler
{
    public string EffectId = "";

    private SkillTemplate _effect;
    private bool _resolved;

    public override void OnUse(Actor _user, Tile _targetTile, UsageParameter _usageParams, ref bool _applyToTile)
    {
        try
        {
            if (!_resolved)
            {
                _resolved = true;
                _effect = Templates.ById<SkillTemplate>(EffectId, msg => Log.Warn($"blanket: {msg}"));
            }
            if (_effect != null)
                BlanketWindowSystem.Schedule(_effect);
        }
        catch (Exception ex)
        {
            Log.Warn($"blanket: schedule failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

// Owns the round window a blanket effect covers. Both edges sit on the round boundary, so
// the effect's own LifetimeLimit is only a backstop: this decides when it starts and ends,
// and neither edge depends on where in a turn the ability was used.
public sealed class BlanketWindowSystem : JiangyuSystem
{
    private static readonly List<SkillTemplate> _pending = new();
    private static readonly List<SkillTemplate> _active = new();
    private static IntPtr _mission;

    public override void OnInit()
        => Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "NextRound", OnNextRound);

    internal static void Schedule(SkillTemplate effect)
    {
        // A blanket rolled in a mission that ends without another round boundary must not
        // carry into the next mission, so each queue belongs to the mission that filled it.
        var mission = TacticalManager.Get()?.GetMission()?.Pointer ?? IntPtr.Zero;
        if (mission != _mission)
        {
            _pending.Clear();
            _active.Clear();
            _mission = mission;
        }
        _pending.Add(effect);
        Log.Debug($"blanket: '{effect.GetID()}' starts at the next round");
    }

    private void OnNextRound(PatchInfo info)
    {
        try
        {
            var mission = TacticalManager.Get()?.GetMission()?.Pointer ?? IntPtr.Zero;
            if (mission != _mission)
            {
                _pending.Clear();
                _active.Clear();
                _mission = mission;
                return;
            }
            // Yesterday's window closes before today's opens, so a blanket scheduled while
            // one is running still gets its own full round.
            foreach (var effect in _active)
                Sweep(effect, false);
            _active.Clear();
            foreach (var effect in _pending)
            {
                Sweep(effect, true);
                _active.Add(effect);
            }
            _pending.Clear();
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"blanket: round boundary failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Add or strip the effect on every living actor on the map.
    private void Sweep(SkillTemplate effect, bool add)
    {
        var touched = 0;
        TacticalManager.Get()?.ForEachActor((Il2CppSystem.Action<Actor>)(Action<Actor>)(actor =>
        {
            try
            {
                if (actor == null || !actor.IsAlive())
                    return;
                var skills = actor.GetSkills();
                if (skills == null)
                    return;
                if (add)
                {
                    if (SkillEffects.CountInstances(skills, effect) == 0
                        && SkillEffects.TryAddEffect(actor, effect, msg => Context.Log.Warn($"blanket: {msg}")))
                        touched++;
                    return;
                }
                var stripped = false;
                while (skills.Remove(effect))
                    stripped = true;
                if (!stripped)
                    return;
                touched++;
                // Remove(SkillTemplate) is overloaded and unpatchable, so the overhead icon
                // mirror cannot see this path on its own.
                EffectHudIconSystem.Resync(actor);
            }
            catch
            {
                // one broken actor must not end the sweep
            }
        }));
        Context.Log.Debug($"blanket: {(add ? "applied" : "stripped")} '{effect.GetID()}' on {touched} actor(s)");
    }
}
