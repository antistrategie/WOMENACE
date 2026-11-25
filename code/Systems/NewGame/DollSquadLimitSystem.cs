using Jiangyu.Sdk;

namespace WOMENACE.Code;

// New-game option "Limit doll squad size to 5": while on for the campaign, a WOMENACE leader's
// squad is capped at five bodies (the doll plus at most four squaddie copies). SquaddieCap holds
// the valid-squaddie range, the squaddies already assigned and every add to that cap.
//
// The cap only ever lowers a count (a smaller squad is left alone) and composes with the solo
// squad's 0 (min wins, so a solo doll stays solo). Only WOMENACE leaders (speaker carries the
// shared marker) are affected, so vanilla squads are untouched.
public sealed class DollSquadLimitSystem : JiangyuSystem
{
    private const int MaxSquadSize = 5;
    private const int MaxSquaddies = MaxSquadSize - 1;

    private SquaddieCap _cap;

    public override void OnInit()
    {
        _cap = new SquaddieCap("doll squad limit",
            leader => NewGameSettings.LimitDollSquadSize(Context) && Affinity.OurSpeakerTags(leader) != null ? MaxSquaddies : null);
        _cap.Install(Context);
    }
}
