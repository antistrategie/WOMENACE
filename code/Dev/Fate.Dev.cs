using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verb over Divination.
//   {"verb":"Fate.Preview","args":["nemesis"]}
[DevVerb]
public static class Fate
{
    // Divination's forecast for the seer's selected skill (or her first attack) against every
    // enemy the player can see, to compare with what a shot then does.
    public static string Preview(string unit = "nemesis")
    {
        var tm = TacticalManager.Get();
        var player = tm?.GetPlayerFaction();
        if (player == null)
            return "no mission";
        Actor seer = null;
        foreach (var actor in player.GetActors())
            if (actor?.GetTemplate()?.GetID() is { } id && (id == unit || id.EndsWith("." + unit, StringComparison.Ordinal)))
                seer = actor;
        if (seer == null)
            return $"no player unit matching '{unit}'";
        var skill = Il2CppMenace.States.TacticalState.Get()?.GetSelectedSkill();
        if (skill == null || !skill.IsAttack() || skill.GetEntity()?.Pointer != seer.Pointer)
            foreach (var candidate in seer.GetSkills().GetAllSkills())
                if (candidate?.TryCast<Skill>() is { } s && s.IsAttack())
                {
                    skill = s;
                    break;
                }
        if (skill == null)
            return "no attack skill";
        var lines = new List<string> { $"{seer.GetTemplate()?.GetID()} with {skill.GetID()}:" };
        foreach (var baseFaction in tm.GetFactions())
        {
            if (baseFaction == null || baseFaction.GetIndex() == player.GetIndex() || baseFaction.IsAlliedWithPlayer())
                continue;
            foreach (var enemy in baseFaction.GetActors())
            {
                if (enemy == null || !enemy.IsDetectedByFaction(player.GetIndex()) || enemy.GetTile() is not { } tile)
                    continue;
                try
                {
                    var f = DivinationSystem.Foresee(seer, skill, tile);
                    lines.Add($"  {StartrailInsightSystem.Label(enemy)}: {f?.ToString() ?? "no forecast"}");
                }
                catch (Exception ex) { lines.Add($"  {StartrailInsightSystem.Label(enemy)}: failed {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        return string.Join("\n", lines);
    }
}
