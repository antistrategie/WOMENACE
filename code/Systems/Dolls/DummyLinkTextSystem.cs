using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The game's interface calls the bodies in a squad squaddies. Here they are a Doll's dummy links,
// so its strings that name them say dummies, word for word otherwise. These belong to no template
// a KDL patch could reach, so they are replaced by the game's own keys. The text the game's
// templates carry is patched in templates/dummy_links.kdl and the keyword tooltips in
// templates/text_tooltips.kdl.
public sealed class DummyLinkTextSystem : JiangyuSystem
{
    public override void OnInit()
    {
        Locale.Replace("Enums/RewardType/squaddie", new LocalisedText("WOMENACE::ui/game/rewardtype_squaddie", "Dummy"));
        Locale.Replace("Enums/UnitStatistic/elements_lost", new LocalisedText("WOMENACE::ui/game/unitstatistic_elements_lost", "Dummies Lost"));
        Locale.Replace("Dialogs/install_oci/MaxSquaddiesReductionWarning", new LocalisedText("WOMENACE::ui/game/install_oci_maxsquaddiesreductionwarning", "This will reduce your max. dummies by {0} and might result in a loss of dummies."));
        Locale.Replace("Dialogs/squaddies_edit/available_squaddies_0", new LocalisedText("WOMENACE::ui/game/squaddies_edit_available_squaddies_0", "Available Dummies: {0}"));
        Locale.Replace("Dialogs/squaddies_edit/deceased_squaddies_0", new LocalisedText("WOMENACE::ui/game/squaddies_edit_deceased_squaddies_0", "Deceased Dummies: {0}"));
        Locale.Replace("Dialogs/squaddies_edit/Headline", new LocalisedText("WOMENACE::ui/game/squaddies_edit_headline", "Assign Dummies: {0} ({1}/{2})"));
        Locale.Replace("Dialogs/squaddies_edit/wounded_squaddies_0", new LocalisedText("WOMENACE::ui/game/squaddies_edit_wounded_squaddies_0", "Wounded Dummies: {0}"));
        Locale.Replace("DropDownTexts/Effects/buyout_squaddie", new LocalisedText("WOMENACE::ui/game/effects_buyout_squaddie", "+1 Dummy"));
        Locale.Replace("DropDownTexts/Effects/out_of_squaddies", new LocalisedText("WOMENACE::ui/game/effects_out_of_squaddies", "Out of dummies!"));
        Locale.Replace("DropDownTexts/Text/squaddie_down", new LocalisedText("WOMENACE::ui/game/text_squaddie_down", "Dummy down: {0}"));
        Locale.Replace("Tooltips/actor/squaddies", new LocalisedText("WOMENACE::ui/game/actor_squaddies", "Dummies"));
        Locale.Replace("UI/common/squaddies", new LocalisedText("WOMENACE::ui/game/common_squaddies", "Dummies"));
        Locale.Replace("UI/mission_result/killed_squaddies", new LocalisedText("WOMENACE::ui/game/mission_result_killed_squaddies", "Killed Dummies: "));
        Locale.Replace("UI/mission_result/rescued_squaddies", new LocalisedText("WOMENACE::ui/game/mission_result_rescued_squaddies", "Rescued Dummies: "));
        Locale.Replace("UI/mission_result/revived_squaddies", new LocalisedText("WOMENACE::ui/game/mission_result_revived_squaddies", "Rescued Dummies: "));
        Locale.Replace("UI/mission_result/wounded_squaddies", new LocalisedText("WOMENACE::ui/game/mission_result_wounded_squaddies", "Wounded Dummies: "));
        Locale.Replace("UI/notification/deploy_leader_needs_squaddies", new LocalisedText("WOMENACE::ui/game/notification_deploy_leader_needs_squaddies", "{0} needs at least {1} dummies."));
        Locale.Replace("UI/notification/no_free_squaddies_left", new LocalisedText("WOMENACE::ui/game/notification_no_free_squaddies_left", "No free dummies left."));
        Locale.Replace("UI/selected_unit_panel/SquaddiesHeadline", new LocalisedText("WOMENACE::ui/game/selected_unit_panel_squaddiesheadline", "DUMMIES"));
        Locale.Replace("UI/ship_info/max_squaddies", new LocalisedText("WOMENACE::ui/game/ship_info_max_squaddies", "Max. Dummies"));
        Locale.Replace("UI/ship_info/squaddie_rescue_chance", new LocalisedText("WOMENACE::ui/game/ship_info_squaddie_rescue_chance", "Dummy Rescue Chance"));
        Locale.Replace("UI/ship_info/squaddies_per_mission", new LocalisedText("WOMENACE::ui/game/ship_info_squaddies_per_mission", "Dummies per Mission"));
    }
}
