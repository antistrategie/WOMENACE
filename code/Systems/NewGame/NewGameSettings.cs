using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The WOMENACE new-game settings, shared like Affinity: the new-game box writes the player's
// pending choices here, the effect systems read them, and the choices are committed to the
// per-campaign NewGameOptions when a campaign is created. To grow the WOMENACE section in the
// new-game box, add a property to NewGameOptions (and extend CopyFrom) and a Setting to Registry.
public static class NewGameSettings
{
    // The choices being edited in the new-game box, before a campaign (and its state) exists. Not
    // persisted: it seeds the per-campaign NewGameOptions at creation, and remembers the last choice
    // across box openings within a session. Box defaults are set here (they are the state a fresh
    // new game commits when the box is left untouched); the NewGameOptions class defaults stay off so
    // a pre-existing save with no sidecar is never changed by a default flipping on.
    public static NewGameOptions Pending { get; } = new() { LimitDollSquadSize = true };

    // The committed, per-campaign values, read by mid-campaign effects (survive save/load via the
    // per-save-slot Context.State).
    public static bool DisableVanillaLeaders(ModContext context)
        => context.State.Get<NewGameOptions>().DisableVanillaLeaders;

    public static bool LimitDollSquadSize(ModContext context)
        => context.State.Get<NewGameOptions>().LimitDollSquadSize;

    // One toggle in the WOMENACE section: its label and how it reads and writes an option.
    public sealed class Setting
    {
        public string LabelKey { get; set; }
        public string LabelFallback { get; set; }
        public Func<NewGameOptions, bool> Get { get; set; }
        public Action<NewGameOptions, bool> Set { get; set; }
    }

    // The section's toggles, in display order.
    public static readonly Setting[] Registry =
    {
        new Setting
        {
            LabelKey = "WOMENACE::ui/newgame/disable_vanilla_leaders",
            LabelFallback = "Disable vanilla squad leaders and pilots",
            Get = o => o.DisableVanillaLeaders,
            Set = (o, v) => o.DisableVanillaLeaders = v,
        },
        new Setting
        {
            LabelKey = "WOMENACE::ui/newgame/limit_doll_squad_size",
            LabelFallback = "Limit max number of dummy links to 5",
            Get = o => o.LimitDollSquadSize,
            Set = (o, v) => o.LimitDollSquadSize = v,
        },
    };
}

// Per-campaign WOMENACE options, one instance per save slot via Context.State.Get<NewGameOptions>().
// Add a property here for each new toggle and extend CopyFrom.
public sealed class NewGameOptions
{
    // When true, only WOMENACE (Girls' Frontline) leaders are offered: the vanilla squad leaders
    // and pilots are removed from the new-game initial pick and from the dossier hiring pools.
    public bool DisableVanillaLeaders { get; set; }

    // When true, a WOMENACE leader's squad is capped at five bodies (the doll plus at most four
    // squaddie copies).
    public bool LimitDollSquadSize { get; set; }

    public void CopyFrom(NewGameOptions other)
    {
        DisableVanillaLeaders = other.DisableVanillaLeaders;
        LimitDollSquadSize = other.LimitDollSquadSize;
    }
}
