using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppStem;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Path of Protection, Springfield's Taryz. The deploy skill spawns Taryz as a recon-style drone
// she moves by command (the vanilla Falcon skybot's shape), and this handler, carried by the
// perk, does Taryz's work at the end of each of Springfield's turns: it heals the most wounded
// allied squad near Taryz and marks the nearest enemies in Taryz's sight with the vanilla
// Designated Target effect. The drone itself takes no turns, so its owner's turn drives it.
//
// The handler also counts deploys. The first plants FirstDeploy on Springfield and the next
// plants Spent, which the deploy skill's LimitUsability reads. Both markers are status effects
// in her own container, so the count survives turn resyncs and a mid-mission save.
[JiangyuType("TaryzSupport")]
public sealed partial class TaryzSupport : SkillEventHandlerTemplate
{
    public EntityTemplate Taryz;
    public SkillTemplate Deploy;
    public SkillTemplate Mark;
    public SkillTemplate FirstDeploy;
    public SkillTemplate Spent;

    // share of each element's maximum hitpoints restored per heal
    public float HealFraction = 0.25f;
    public int HealRange = 3;
    public int HealTargets = 1;
    public int MarkRange = 8;
    public int MarkTargets = 2;

    // Sounds in SoundBank: the heal plays on the healed squad, the mark once on Taryz.
    public string SoundBank;
    public string HealSound;
    public string MarkSound;

    public override SkillEventHandler Create() => new TaryzSupportHandler
    {
        Taryz = Taryz,
        Deploy = Deploy,
        Mark = Mark,
        FirstDeploy = FirstDeploy,
        Spent = Spent,
        HealFraction = HealFraction,
        HealRange = HealRange,
        HealTargets = HealTargets,
        MarkRange = MarkRange,
        MarkTargets = MarkTargets,
        SoundBank = SoundBank,
        HealSound = HealSound,
        MarkSound = MarkSound,
    };
}

[JiangyuType("TaryzSupportHandler")]
public sealed partial class TaryzSupportHandler : SkillEventHandler
{
    public EntityTemplate Taryz;
    public SkillTemplate Deploy;
    public SkillTemplate Mark;
    public SkillTemplate FirstDeploy;
    public SkillTemplate Spent;
    public float HealFraction;
    public int HealRange;
    public int HealTargets;
    public int MarkRange;
    public int MarkTargets;
    public string SoundBank;
    public string HealSound;
    public string MarkSound;

    public override void OnMissionStarted() => TaryzLaunchSystem.Ensure(Deploy);

    public override void OnAnySkillUsed(Skill _skill)
    {
        try
        {
            if (Deploy == null || _skill?.GetTemplate()?.Pointer != Deploy.Pointer)
                return;
            var owner = GetActor();
            var skills = owner?.GetSkills();
            if (skills == null)
                return;
            var marker = SkillEffects.FindInstance(skills, FirstDeploy) == null ? FirstDeploy : Spent;
            SkillEffects.TryAddEffect(owner, marker, message => Log.Warn($"taryz: {message}"));
        }
        catch (Exception ex)
        {
            Log.Warn($"taryz: deploy count failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public override void OnTurnEnd()
    {
        try
        {
            var owner = GetActor();
            if (owner == null || !owner.IsAlive())
                return;
            Actor taryz = null;
            var actors = new List<Actor>();
            TacticalManager.Get()?.ForEachActor((Il2CppSystem.Action<Actor>)(Action<Actor>)(actor =>
            {
                if (actor == null || !actor.IsAlive() || actor.GetTile() == null)
                    return;
                // SpawnLimit 1 on the deploy keeps one Taryz on the map
                if (taryz == null && Taryz != null && actor.GetTemplate()?.Pointer == Taryz.Pointer && actor.IsAlliedWith(owner))
                    taryz = actor;
                else
                    actors.Add(actor);
            }));
            var tile = taryz?.GetTile();
            if (tile == null)
                return;
            Heal(owner, tile, actors);
            MarkEnemies(owner, taryz, tile, actors);
        }
        catch (Exception ex)
        {
            Log.Warn($"taryz: turn end failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Infantry only: Taryz patches up people, not hulls, and never another drone.
    private void Heal(Actor owner, Tile taryzTile, List<Actor> actors)
    {
        // Ranked by the living elements alone: a squad whose only losses are its dead reads as
        // wounded by GetHitpointsPct but has nothing left to heal.
        var wounded = actors
            .Where(a => a.IsAlliedWith(owner) && !a.IsMinion()
                && a.GetTemplate()?.ActorType == ActorType.Infantry
                && Pierce.Distance(taryzTile, a.GetTile()) <= HealRange)
            .Select(a => (Ally: a, Health: LivingHealth(a)))
            .Where(x => x.Health < 1f)
            .OrderBy(x => x.Health)
            .Take(HealTargets)
            .Select(x => x.Ally);
        foreach (var ally in wounded)
        {
            var elements = ally.GetElements();
            for (var i = 0; elements != null && i < elements.Count; i++)
            {
                var element = elements[i];
                // the wounded only: a downed element stays down
                if (element == null || element.GetHitpoints() <= 0)
                    continue;
                var max = element.GetHitpointsMax();
                var healed = Math.Min(max, element.GetHitpoints() + (int)Math.Ceiling(max * HealFraction));
                element.SetHitpoints(healed);
            }
            ally.UpdateHitpoints();
            TacticalManager.Get()?.InvokeOnHitpointsChanged(ally, ally.GetHitpointsPct(), 0);
            PlaySound(HealSound, ally);
            Log.Debug($"taryz: healed '{ally.GetTemplate()?.GetID()}' to {ally.GetHitpoints()}/{ally.GetHitpointsMax()}");
        }
    }

    // Hitpoints of the living elements over their maximum, 1 when none is wounded.
    private static float LivingHealth(Actor actor)
    {
        var elements = actor.GetElements();
        int hp = 0, max = 0;
        for (var i = 0; elements != null && i < elements.Count; i++)
        {
            var element = elements[i];
            if (element == null || element.GetHitpoints() <= 0)
                continue;
            hp += element.GetHitpoints();
            max += element.GetHitpointsMax();
        }
        return max > 0 ? hp / (float)max : 1f;
    }

    // Enemies the player's side can see within Taryz's range, unmarked ones first, then the
    // nearest. Designated Target does not stack, so marking a marked enemy refreshes it.
    private void MarkEnemies(Actor owner, Actor taryz, Tile taryzTile, List<Actor> actors)
    {
        if (Mark == null)
            return;
        var faction = taryz.GetFactionID();
        var targets = actors
            .Where(a => Pierce.IsHostileTo(owner, a)
                && a.GetTile().IsVisibleToFaction(faction)
                && Pierce.Distance(taryzTile, a.GetTile()) <= MarkRange)
            .OrderBy(a => SkillEffects.FindInstance(a.GetSkills(), Mark) != null)
            .ThenBy(a => Pierce.Distance(taryzTile, a.GetTile()))
            .Take(MarkTargets);
        var marked = false;
        foreach (var enemy in targets)
            marked |= SkillEffects.TryAddEffect(enemy, Mark, message => Log.Warn($"taryz: {message}"));
        if (marked)
            PlaySound(MarkSound, taryz);
    }

    private void PlaySound(string sound, Actor at)
    {
        if (string.IsNullOrEmpty(SoundBank) || string.IsNullOrEmpty(sound))
            return;
        var anchor = at?.GetElement(0)?.transform;
        var instance = SoundManager.GetSoundInstance(new ID(SoundIds.FromName(SoundBank), SoundIds.FromName(sound)));
        if (instance == null || anchor == null)
        {
            Log.Warn($"taryz: '{sound}' not played (sound={instance != null}, anchor={anchor != null})");
            return;
        }
        instance.Play3D(anchor.position);
    }

}
