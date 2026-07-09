using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The WOMENACE new-game settings, shared like Affinity: the new-game box writes the player's
// pending choices here, the effect systems read them, and the choices are committed to the
// per-campaign NewGameOptions when a campaign is created. To grow the WOMENACE section in the
// new-game box, add a property to NewGameOptions (and extend CopyFrom) and a Setting to Registry.
public static class NewGameSettings
{
    // The choices being edited in the new-game box, before a campaign (and its state) exists. Not
    // persisted: it seeds the per-campaign NewGameOptions at creation. Defaults to all-off (vanilla
    // behaviour) and remembers the last choice across box openings within a session.
    public static NewGameOptions Pending { get; } = new();

    // The committed, per-campaign value, read by mid-campaign effects (survives save/load via the
    // per-save-slot Context.State).
    public static bool DisableVanillaLeaders(ModContext context)
        => context.State.Get<NewGameOptions>().DisableVanillaLeaders;

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
    };
}

// Per-campaign WOMENACE options, one instance per save slot via Context.State.Get<NewGameOptions>().
// Add a property here for each new toggle and extend CopyFrom.
public sealed class NewGameOptions
{
    // When true, only WOMENACE (Girls' Frontline) leaders are offered: the vanilla squad leaders
    // and pilots are removed from the new-game initial pick and from the dossier hiring pools.
    public bool DisableVanillaLeaders { get; set; }

    public void CopyFrom(NewGameOptions other) => DisableVanillaLeaders = other.DisableVanillaLeaders;
}
