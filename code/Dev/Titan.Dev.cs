using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Game.Tactical;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Spawns a Titan a few tiles out from the squad in a live mission. With player set
// it joins the squad's side instead, so its skills can be fired by hand. The AI
// Titan keeps to Pierce range and shoots from beyond the squad's sight.
//
//   {"verb":"Titan.Spawn","args":[],"mutate":true}
//   {"verb":"Titan.Spawn","args":["enemy.wmgfl_titan_support", 4, true],"mutate":true}
[DevVerb]
public static class Titan
{
    [MutatingVerb]
    public static object Spawn(string templateId = VehicleModels.TitanSupport, int distance = 4, bool player = false)
    {
        if (!Mission.InMission)
            return new { error = "not in a mission" };
        var template = Templates.ById<EntityTemplate>(templateId);
        if (template == null)
            return new { error = $"no EntityTemplate '{templateId}'" };
        var anchor = FirstLivingPlayerActor();
        if (anchor == null)
            return new { error = "no living player unit to spawn beside" };

        var faction = player ? FactionType.Player : HostileFaction();
        for (var dir = 0; dir < 8; dir++)
            for (var step = distance; step >= 1; step--)
            {
                var tile = anchor.GetTile();
                for (var i = 0; tile != null && i < step; i++)
                    tile = Tiles.Next(tile, (Direction)dir);
                if (tile == null)
                    continue;
                var spawned = Units.Spawn(template, faction, tile);
                if (spawned != null)
                    return new { spawned, faction = faction.ToString(), direction = dir, tiles = step };
            }
        return new { error = "no tile around the squad accepted the spawn" };
    }

    // Every field on each handler of the Titan's skills, read off the live templates.
    // Handler fields are Odin-serialised, so offline inspection cannot show them.
    //   {"verb":"Titan.Stats","args":[]}
    public static string Stats(string templateId = VehicleModels.TitanSupport)
    {
        var template = Templates.ById<EntityTemplate>(templateId);
        if (template == null)
            return $"no EntityTemplate '{templateId}'";
        var lines = new List<string>();
        for (var i = 0; template.Skills != null && i < template.Skills.Count; i++)
            if (template.Skills[i] != null)
                DescribeSkill(template.Skills[i], lines);
        return string.Join("\n", lines);
    }

    // The same dump for any one skill, such as a vanilla weapon's to compare against.
    //   {"verb":"Titan.Skill","args":["active.fire_heavy_auto_repeater"]}
    public static string Skill(string skillId)
    {
        var skill = Templates.ById<SkillTemplate>(skillId);
        if (skill == null)
            return $"no SkillTemplate '{skillId}'";
        var lines = new List<string>();
        DescribeSkill(skill, lines);
        return string.Join("\n", lines);
    }

    private static void DescribeSkill(SkillTemplate skill, List<string> lines)
    {
        var uses = skill.IsLimitedUses ? skill.Uses.ToString() : "unlimited";
        lines.Add($"{skill.name}: AP {skill.ActionPointCost}, range {skill.MinRange}-{skill.IdealRange}-{skill.MaxRange}, repetitions {skill.Repetitions}, uses {uses}");
        var flags = Il2CppSystem.Reflection.BindingFlags.Instance | Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic;
        for (var h = 0; skill.EventHandlers != null && h < skill.EventHandlers.Count; h++)
        {
            var handler = skill.EventHandlers[h];
            if (handler == null)
                continue;
            var type = handler.GetIl2CppType();
            var fields = new List<string>();
            var seen = new HashSet<string>();
            for (var t = type; t != null && t.Name != "Object"; t = t.BaseType)
                foreach (var field in t.GetFields(flags))
                {
                    if (!seen.Add(field.Name))
                        continue;
                    string value;
                    try
                    {
                        value = field.GetValue(handler)?.ToString() ?? "null";
                    }
                    catch (Exception ex)
                    {
                        value = "<" + ex.GetType().Name + ">";
                    }
                    fields.Add($"{field.Name}={value}");
                }
            lines.Add($"  [{h}] {type.Name}: {string.Join(", ", fields)}");
        }
    }

    private static Actor FirstLivingPlayerActor()
    {
        var actors = Mission.Actors(FactionType.Player);
        for (var i = 0; actors != null && i < actors.Count; i++)
            if (actors[i] != null && actors[i].IsAlive() && actors[i].GetTile() != null)
                return actors[i];
        return null;
    }

    // A faction the mission actually fields, so the spawn is not refused for a faction
    // slot this map never set up.
    private static FactionType HostileFaction()
    {
        var factions = Mission.Manager?.GetFactions();
        for (var i = 0; factions != null && i < factions.Length; i++)
        {
            var faction = factions[i];
            if (faction == null)
                continue;
            var type = faction.GetFactionType();
            if (type is FactionType.Player or FactionType.PlayerAI or FactionType.Neutral or FactionType.Civilian)
                continue;
            var actors = faction.GetActors();
            if (actors != null && actors.Count > 0)
                return type;
        }
        return FactionType.Pirates;
    }
}
