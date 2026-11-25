using Il2CppMenace.Tactical;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// What a support team looks like and does in a mission.
//
// Body: EntityVisuals.DetermineArmorPrefab is the body-prefab pick for the tactical spawn and the
// armoury preview. A team's element renders its registered body, loaded from the mod's bundles the
// first time the team is shown, so a team nobody fields costs no memory. TransmogSystem patches
// the same method for Dolls and returns early for any entity that is not one, so the two never
// both write a result.
public sealed class SupportTeamSystem : JiangyuSystem
{
    private readonly Dictionary<string, GameObject> _bodies = new(StringComparer.Ordinal);

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Tactical.EntityVisuals", "DetermineArmorPrefab", OnDetermineArmorPrefab);
    }

    // Args: EntityTemplate, Squaddie, elementIndex, Gender, ItemContainer, UnitLeaderTemplate,
    // PseudoRandom.
    private void OnDetermineArmorPrefab(PatchInfo info)
    {
        try
        {
            var team = SupportTeams.Of(info.Args is { Count: > 0 } ? info.Args[0] as EntityTemplate : null);
            if (team == null)
                return;
            var body = Body(team);
            if (body == null)
                return;
            info.Result = body;
            Context.Log.Debug($"support team: '{team.Name}' renders '{team.BodyAsset}'");
        }
        catch (Exception ex) { Context.Log.Warn($"support team: body pick failed: {ex.Message}"); }
    }

    private GameObject Body(SupportTeams.Team team)
    {
        if (_bodies.TryGetValue(team.Name, out var cached) && cached != null)
            return cached;
        var body = Context.Assets.Load<GameObject>(team.BodyAsset);
        if (body == null)
        {
            Context.Log.Warn($"support team: no body '{team.BodyAsset}' in the bundles");
            return null;
        }
        _bodies[team.Name] = body;
        return body;
    }
}
