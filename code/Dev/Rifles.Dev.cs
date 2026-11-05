using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verb over the rifle Dolls in a mission: Constellation's count and its effect on an attack's
// properties, and each doll's weapon proficiency bonus.
//   {"verb":"Rifles.Report","args":[]}
[DevVerb]
public static class Rifles
{
    public static string Report()
    {
        var player = TacticalManager.Get()?.GetPlayerFaction();
        if (player == null)
            return "no mission";
        var lines = new List<string>();
        foreach (var actor in player.GetActors())
        {
            if (actor == null || !ConstellationSystem.IsRifleDoll(actor))
                continue;
            var id = actor.GetTemplate()?.GetID();
            var leader = actor.TryCast<UnitActor>()?.GetLeader();
            var level = WeaponProficiencySystem.LevelFor(leader);
            var proficiency = WeaponProficiencySystem.BonusFor(leader);
            var others = ConstellationSystem.OtherRifleDolls(actor);
            var line = $"{id}: affinity Lv{level}, proficiency +{proficiency} ACC, constellation sees {others} other rifle doll(s)";
            line += $", leader accuracy {leader?.GetCurrentProperties()?.Accuracy ?? -1}";
            var skill = PrimaryAttack(actor);
            var tile = actor.GetTile();
            if (skill != null && tile != null)
            {
                var props = actor.GetSkills().BuildPropertiesForUse(skill, tile, tile, null);
                line += $"\n    {skill.GetID()} built: accuracy {props?.Accuracy}, damage mult {props?.DamageMult:F2}, damage {props?.GetDamage():F1}";
            }
            lines.Add(line);
        }
        return lines.Count == 0 ? "no rifle dolls in the mission" : string.Join("\n", lines);
    }

    private static Skill PrimaryAttack(Actor actor)
    {
        var all = actor.GetSkills()?.GetAllSkills();
        for (var i = 0; all != null && i < all.Count; i++)
            if (all[i]?.TryCast<Skill>() is { } s && s.IsAttack() && s.GetID().StartsWith("active.fire_", StringComparison.Ordinal))
                return s;
        return null;
    }
}
