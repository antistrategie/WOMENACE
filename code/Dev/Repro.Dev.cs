using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Game.Tactical;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for putting a campaign into the state a bug report describes: a leader hired by
// template, an item of any kind on a hired leader, and the elements a squad's skills would be
// carried out by.
[DevVerb]
public static class Repro
{
    [MutatingVerb]
    public static object Hire(string leaderTemplateId)
    {
        var roster = StrategyState.Get()?.Roster;
        var template = Templates.ById<UnitLeaderTemplate>(leaderTemplateId);
        if (roster == null || template == null)
            return new { error = $"no roster, or no leader template '{leaderTemplateId}'" };
        var leader = roster.HireLeader(template);
        return new { ok = leader != null, leader = leader?.GetTemplate()?.GetID() };
    }

    // Makes the item and puts it on the leader with this character tag, in the slot its
    // template names, in place of whatever was there.
    [MutatingVerb]
    public static object Equip(string characterTag, string itemId)
    {
        var leaders = StrategyState.Get()?.Roster?.m_HiredLeaders;
        for (var i = 0; leaders != null && i < leaders.Count; i++)
        {
            var leader = leaders[i];
            if (leader == null || Affinity.CharacterTag(leader) != characterTag)
                continue;
            var template = Templates.ById<BaseItemTemplate>(itemId);
            var item = template?.CreateItem(BaseItemTemplate.CreateGuid())?.TryCast<Item>();
            if (item == null)
                return new { error = $"cannot make '{itemId}'" };
            var items = leader.GetItems();
            var slot = item.GetTemplate().SlotType;
            var held = items.GetItemAtSlot(slot);
            if (held != null)
                items.Remove(held, default);
            return new { ok = items.Equip(item, 0, default), itemId, slot = slot.ToString(), leader = leader.GetTemplate()?.GetID() };
        }
        return new { error = $"no hired leader tagged '{characterTag}'" };
    }

    // For every player squad in the mission: each skill that comes from an item, the item's
    // slot, and the elements vanilla's AllButSpecial roster gives it.
    public static object Rosters()
    {
        var rows = new List<string>();
        var actors = Jiangyu.Game.Tactical.Mission.Actors(FactionType.Player);
        for (var a = 0; actors != null && a < actors.Count; a++)
        {
            var actor = actors[a];
            var skills = actor?.GetSkills()?.GetAllSkills();
            for (var i = 0; skills != null && i < skills.Count; i++)
            {
                var skill = skills[i]?.TryCast<Skill>();
                var slot = skill?.GetItem()?.GetTemplate()?.SlotType;
                if (slot == null)
                    continue;
                var elements = skill.GetExecutingElements(ExecutingElementType.AllButSpecial);
                var listed = new List<int>();
                for (var e = 0; elements != null && e < elements.Count; e++)
                    listed.Add(elements[e]);
                rows.Add($"{actor.GetTemplate()?.GetID()} {skill.GetID()} [{slot}] elements {string.Join(",", listed)}");
            }
        }
        return new { rows = string.Join(" | ", rows) };
    }
}
