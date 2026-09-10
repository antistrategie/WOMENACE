using Il2CppMenace.Strategy;

namespace WOMENACE.Code;

internal static class LeaderRecruitment
{
    internal static bool IsAcquired(UnitLeaderTemplate leader, Roster roster)
    {
        // A swapped doll's base is outside the roster while her alternate form is active.
        // It must remain unavailable to both ordinary dossiers and Procurement.
        if (FormSwapSystem.BaseFormStashed(leader.GetID()))
            return true;
        // GetLeaderByTemplate (RVA 0x5B0950) returns null for a hirable template,
        // but its out status is Hirable, so an unlocked dossier counts as acquired.
        roster.GetLeaderByTemplate(leader, out var status);
        return status != UnitLeaderStatus.Unknown;
    }
}
