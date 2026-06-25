using Il2CppMenace.Strategy;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Hides a character's affinity-locked skins from the armoury's alternate-outfit picker.
//
// The picker (UnitWindowEquipment) is a CATALOGUE, not an owned-items list: it shows every
// equippable armour with an owned-count, and can show ones the player does not own, so a skin
// cannot be hidden by withholding ownership. This postfixes the catalogue build and hides the slots
// whose item is a skin the leader has not unlocked yet (per the shared Unlocks registry). The skin
// reappears the instant the character reaches its unlock level. AffinitySystem separately grants the
// owned instance at that level so the now-visible skin is equippable.
//
// It reads affinity through the shared Affinity API, so this system and AffinitySystem stay in step
// without calling each other.
public sealed class SkinGateSystem : JiangyuSystem
{
    public override void OnInit()
    {
        // Runs after the alternatives are built and added, so the slots exist to hide.
        Context.Patches.Postfix(
            "Il2CppMenace.UI.Strategy.UnitWindowEquipment",
            "UpdateEquipmentAlternatives",
            OnAlternativesBuilt);
    }

    private void OnAlternativesBuilt(PatchInfo info)
    {
        try
        {
            var equipment = info.Instance as UnitWindowEquipment;
            var leader = equipment?.m_Leader;
            if (leader == null || !leader.IsAlive())
                return;

            var locked = LockedSkinIds(leader);
            if (locked.Count == 0)
                return;

            // The alternatives window is a named child of the active screen. Hide each locked skin's
            // slot (display:none collapses it, so no gap in the grid).
            var screen = UIManager.Get()?.GetActiveScreen()?.GetRootElement();
            var window = screen != null ? UI.Find(screen, UiSelector.Name("EquipmentAlternatives")) : null;
            if (window == null)
                return;

            foreach (var element in UI.FindAll(window, UiSelector.TypeName("EquipmentSlot")))
            {
                var slot = element.TryCast<EquipmentSlot>();
                var item = slot?.m_Item;
                var id = item != null && item.IsAlive() ? item.GetBaseItemTemplate()?.GetID() : null;
                if (id != null && locked.Contains(id))
                    element.SetVisible(false);
            }
        }
        catch (Exception ex) { Context.Log.Warn($"skin gate: alternatives filter failed: {ex.Message}"); }
    }

    private HashSet<string> LockedSkinIds(BaseUnitLeader leader)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var characterTag = Affinity.CharacterTag(leader);
        var level = Affinity.LevelFor(Context, leader);
        foreach (var id in Unlocks.LockedSkinArmors(characterTag, level))
            set.Add(id);
        return set;
    }
}
