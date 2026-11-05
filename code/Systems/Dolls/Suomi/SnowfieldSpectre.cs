using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Snowfield Spectre, Suomi's starting perk, carried by a status effect the perk adds at mission
// start. Snow weather (effect.weather.snowfall and snowstorm, the vision, detection, accuracy and
// movement penalties a snow map puts on every unit) never takes hold on her: the status strips
// any she already carries and SnowfieldSpectreSystem refuses the weather when the game adds it
// later. Attacking from a snow tile raises her accuracy and damage, and the status shows while
// she stands on snow.
[JiangyuType("SnowfieldSpectre")]
public sealed partial class SnowfieldSpectre : SkillEventHandlerTemplate
{
    public float AccuracyMult = 1.15f;
    public float DamageMult = 1.15f;

    public override SkillEventHandler Create()
        => new SnowfieldSpectreHandler { AccuracyMult = AccuracyMult, DamageMult = DamageMult };
}

[JiangyuType("SnowfieldSpectreHandler")]
public sealed partial class SnowfieldSpectreHandler : SkillEventHandler
{
    public float AccuracyMult = 1.15f;
    public float DamageMult = 1.15f;
    private Actor _actor;

    public override bool IsHidden() => !OnSnow(GetActor()?.GetTile());

    public override void OnUpdate(EntityProperties _properties)
    {
        if (_actor != null)
            return;
        var actor = GetActor();
        if (actor == null || !actor.IsAlive())
            return;
        _actor = actor;
        SnowfieldSpectreSystem.Register(actor);
    }

    // Registering again is harmless, and keeps the weather off if a scene change cleared the
    // registration after the first update.
    public override void OnTurnStart()
    {
        if (_actor != null && _actor.IsAlive())
            SnowfieldSpectreSystem.Register(_actor);
    }

    public override void OnMissionFinished()
    {
        SnowfieldSpectreSystem.Unregister(_actor);
        _actor = null;
    }

    public override void OnBeforeAnySkillUsed(Skill _skill, Tile _fromTile, Tile _targetTile, EntityProperties _properties, Entity _overrideTargetEntity)
    {
        try
        {
            if (_skill == null || _properties == null || !_skill.IsAttack() || !OnSnow(_fromTile ?? GetActor()?.GetTile()))
                return;
            _properties.AccuracyMult *= AccuracyMult;
            _properties.DamageMult *= DamageMult;
        }
        catch (Exception ex)
        {
            Log.Warn($"snowfield spectre: pre-attack failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static bool OnSnow(Tile tile) => tile != null && tile.GetSurfaceType() == SurfaceType.Snow;
}

public sealed class SnowfieldSpectreSystem : JiangyuSystem
{
    private static readonly string[] SnowWeatherIds = { "effect.weather.snowfall", "effect.weather.snowstorm" };

    private static SnowfieldSpectreSystem _instance;
    // The skill containers of the units the weather cannot touch, by pointer.
    private static readonly HashSet<IntPtr> Spared = new();
    private static readonly List<SkillTemplate> SnowWeather = new();

    public override void OnInit()
    {
        _instance = this;
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.SkillContainer", "Add", OnAdd);
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName) => Spared.Clear();

    public override void OnUnload()
    {
        Spared.Clear();
        _instance = null;
    }

    private static List<SkillTemplate> Weather()
    {
        if (SnowWeather.Count == 0)
            foreach (var id in SnowWeatherIds)
                if (Templates.ById<SkillTemplate>(id) is { } template)
                    SnowWeather.Add(template);
        return SnowWeather;
    }

    internal static void Register(Actor actor)
    {
        var skills = actor?.GetSkills();
        if (skills == null)
            return;
        Spared.Add(skills.Pointer);
        foreach (var weather in Weather())
            SkillEffects.RemoveInstances(skills, weather);
    }

    internal static void Unregister(Actor actor)
    {
        if (actor?.GetSkills() is { } skills)
            Spared.Remove(skills.Pointer);
    }

    private static void OnAdd(PatchInfo info)
    {
        if (Spared.Count == 0 || info.Instance is not Il2CppObjectBase container || !Spared.Contains(container.Pointer))
            return;
        try
        {
            var template = (info.Args is { Count: > 0 } args ? args[0] as Il2CppObjectBase : null)
                ?.TryCast<BaseSkill>()?.GetTemplate();
            if (template == null)
                return;
            foreach (var weather in Weather())
                if (weather.Pointer == template.Pointer)
                {
                    info.Skip = true;
                    info.Result = false;
                    return;
                }
        }
        catch (Exception ex)
        {
            _instance?.Context.Log.Warn($"snowfield spectre: weather check failed: {ex.Message}");
        }
    }
}
