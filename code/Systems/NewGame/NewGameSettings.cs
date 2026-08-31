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
    public static NewGameOptions Pending { get; } = new() { DisableVanillaLeaders = true, LimitDollSquadSize = true };

    // The committed, per-campaign values, read by mid-campaign effects (survive save/load via the
    // per-save-slot Context.State).
    public static bool DisableVanillaLeaders(ModContext context)
        => context.State.Get<NewGameOptions>().DisableVanillaLeaders;

    public static bool LimitDollSquadSize(ModContext context)
        => context.State.Get<NewGameOptions>().LimitDollSquadSize;

    // One toggle in the WOMENACE section: its label and how it reads and writes an option. Label is a
    // LocalisedText because the compiler extracts translatable strings from literal declarations: a
    // key and an English fallback held in separate properties and handed to Locale.Text as variables
    // reads fine but never reaches the POT, so the label would ship untranslatable.
    public sealed class Setting
    {
        public LocalisedText Label { get; set; }
        public Func<NewGameOptions, bool> Get { get; set; }
        public Action<NewGameOptions, bool> Set { get; set; }
    }

    // The section's toggles, in display order.
    public static readonly Setting[] Registry =
    {
        new Setting
        {
            Label = new LocalisedText(
                "WOMENACE::ui/newgame/disable_vanilla_leaders", "Disable vanilla squad leaders and pilots"),
            Get = o => o.DisableVanillaLeaders,
            Set = (o, v) => o.DisableVanillaLeaders = v,
        },
        new Setting
        {
            Label = new LocalisedText(
                "WOMENACE::ui/newgame/show_all_dolls", "Show all Dolls in new game list"),
            Get = o => o.ShowAllDolls,
            Set = (o, v) => o.ShowAllDolls = v,
        },
        new Setting
        {
            Label = new LocalisedText(
                "WOMENACE::ui/newgame/limit_doll_squad_size", "Limit max number of dummy links to 5"),
            Get = o => o.LimitDollSquadSize,
            Set = (o, v) => o.LimitDollSquadSize = v,
        },
    };
}

// Per-campaign WOMENACE options, one instance per save slot via Context.State.Get<NewGameOptions>().
// Add a property here for each new toggle and extend CopyFrom.
public sealed class NewGameOptions
{
    // When true, the vanilla squad leaders and pilots are removed from the new-game initial pick
    // and from the dossier hiring pools. Every mod-added leader stays offered, WOMENACE dolls and
    // other mods' custom leaders alike: see VanillaLeadersSystem for how vanilla is recognised.
    public bool DisableVanillaLeaders { get; set; }

    // When true, the new-game pick list offers every WOMENACE doll (the union of the dossier
    // rosters), not just the leaders strategy_config registers as initially pickable.
    public bool ShowAllDolls { get; set; }

    // When true, a WOMENACE leader's squad is capped at five bodies (the doll plus at most four
    // squaddie copies).
    public bool LimitDollSquadSize { get; set; }

    public void CopyFrom(NewGameOptions other)
    {
        DisableVanillaLeaders = other.DisableVanillaLeaders;
        ShowAllDolls = other.ShowAllDolls;
        LimitDollSquadSize = other.LimitDollSquadSize;
    }
}
