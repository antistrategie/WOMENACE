using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;

namespace WOMENACE.Code;

// GFL2's Armed Echelons ("SupportTeam" in the client), fielded as roster leaders whose one element
// is the whole crew. A team is not a Doll: its speaker's Tags carry no wmgfl token, so every system
// keyed on Affinity's marker skips it, and team identity comes from this registry instead.
//
// A team is named by its GFL2 code (ATG01). Its leader and squad templates are
// squad_leader.<name> and player_squad.<name>, and its body is <name>/default/main. Its own tag
// is wmgfl_team_<name>, never wmgfl_<name>: Calibration takes a wmgfl_<name> tag beside a
// weapon.<name> for a Doll and her calibratable weapon.
public static class SupportTeams
{
    public sealed class Team
    {
        public Team(string code, string name, params Member[] crew)
        {
            Code = code;
            Name = name;
            Crew = crew;
        }

        public string Code { get; }
        public string Name { get; }
        // The crew standing in the team's one element, leader first.
        public Member[] Crew { get; }
        public int CrewCount => Crew.Length;
        public string LeaderId => "squad_leader." + Name;
        public string SquadId => "player_squad." + Name;
        public string BodyAsset => Name + "/default/main";
        // The squad type row's icon: the team's skill icon in black and white.
        public string TypeIconAsset => Name + "__unit_type";
        // The team's own weapon and outfit, which it is hired with and never parts with.
        public string WeaponId => "weapon." + Name;
        public string ArmorId => "armor." + Name + "_default";
        // On the team's squad, and on an item made for this team alone, as a tag or in its
        // OnlyEquipableBy.
        public string Tag => "wmgfl_team_" + Name;

        // Whether the team's slots list an item: one for every team, or one for this team.
        public bool Accepts(Il2CppMenace.Items.BaseItemTemplate template)
            => HasTag(template?.Tags, SupportItemTag) || HasTag(template?.Tags, Tag)
               || HasTag(template?.TryCast<Il2CppMenace.Items.ItemTemplate>()?.OnlyEquipableBy, Tag);
    }

    // A crew member: her node in the team's body (GFL2's member rig, which holds her Root_M) and
    // the name the roster shows for her.
    public sealed class Member
    {
        public Member(string node, string name)
        {
            Node = node;
            Name = name;
        }

        public string Node { get; }
        public string Name { get; }
    }

    // An item any team may take.
    public const string SupportItemTag = "wmgfl_support_item";

    private static bool HasTag(Il2CppSystem.Collections.Generic.List<Il2CppMenace.Tags.TagTemplate> tags, string id)
    {
        if (tags == null)
            return false;
        for (var i = 0; i < tags.Count; i++)
            if (tags[i]?.GetID() == id)
                return true;
        return false;
    }

    public static readonly Team[] All =
    {
        new("ATG01", "limonad",
            new Member("ATG01_MemberA", "Tarkhun"),
            new Member("ATG01_MemberC", "Citro"),
            new Member("ATG01_MemberB", "Bakhmaro")),
    };

    private static readonly Dictionary<string, Team> BySquadId = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Team> ByLockedItemId = new(StringComparer.Ordinal);

    static SupportTeams()
    {
        foreach (var team in All)
        {
            BySquadId[team.SquadId] = team;
            ByLockedItemId[team.WeaponId] = team;
            ByLockedItemId[team.ArmorId] = team;
        }
    }

    // Verdicts by template pointer, one cache for each id map: these sit under every squaddie
    // validation pass, every body pick and every item removal, and the id string otherwise
    // marshals each time. Il2cpp does not move objects, so a pointer stays a stable key until the
    // templates are rebuilt.
    private static readonly Dictionary<IntPtr, Team> SquadByTemplate = new();
    private static readonly Dictionary<IntPtr, Team> LockedByTemplate = new();

    public static void ClearCache()
    {
        SquadByTemplate.Clear();
        LockedByTemplate.Clear();
    }

    // Set by SupportTeamSystem, so a failed lookup is reported.
    internal static Jiangyu.Sdk.IModLog Log;
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    // A strategy-layer leader. GetTemplate returns the unit template (player_squad.<name>).
    public static Team Of(BaseUnitLeader leader)
    {
        try
        {
            var template = leader?.GetTemplate();
            return template == null ? null : Lookup(template.Pointer, () => template.GetID() ?? template.name, BySquadId, SquadByTemplate);
        }
        catch (Exception ex) { return Failed("leader lookup", ex); }
    }

    public static Team Of(EntityTemplate template)
    {
        try
        {
            return template == null ? null : Lookup(template.Pointer, () => template.GetID() ?? template.name, BySquadId, SquadByTemplate);
        }
        catch (Exception ex) { return Failed("entity lookup", ex); }
    }

    public static bool IsTeam(BaseUnitLeader leader) => Of(leader) != null;

    // A team's own weapon or outfit in that team's own container, which no removal may take off
    // it. The same item anywhere else (another leader's container, should it ever get there) is
    // an ordinary item. Item removals run for every item in the game, so the template verdict is
    // cached and the container's owner is only read for a team's own items.
    public static bool IsLocked(Il2CppMenace.Items.ItemContainer container, Il2CppMenace.Items.Item item)
    {
        try
        {
            var template = item?.GetTemplate();
            var team = template == null ? null : Lookup(template.Pointer, () => template.GetID(), ByLockedItemId, LockedByTemplate);
            return team != null && OwnerTeam(container) == team;
        }
        catch (Exception ex)
        {
            Failed("item lock", ex);
            return false;
        }
    }

    // The team a container belongs to: a strategy leader's, or a team's entity in a mission.
    private static Team OwnerTeam(Il2CppMenace.Items.ItemContainer container)
    {
        var owner = container?.GetOwner() as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
        if (owner == null)
            return null;
        if (owner.TryCast<BaseUnitLeader>() is { } leader)
            return Of(leader);
        return owner.TryCast<Entity>() is { } entity ? Of(entity.GetTemplate()) : null;
    }

    private static Team Lookup(IntPtr pointer, Func<string> id, Dictionary<string, Team> map, Dictionary<IntPtr, Team> cache)
    {
        if (cache.TryGetValue(pointer, out var known))
            return known;
        map.TryGetValue(id() ?? "", out var team);
        cache[pointer] = team;
        return team;
    }

    // Reported once for each kind of lookup, since a lookup that throws throws on every call.
    private static Team Failed(string what, Exception ex)
    {
        if (Reported.Add(what))
            Log?.Warn($"support team: {what} failed: {ex.GetType().Name}: {ex.Message}");
        return null;
    }
}
