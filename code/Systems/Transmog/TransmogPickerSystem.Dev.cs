using Il2CppMenace.Items;
using Jiangyu.Game.Ui;

namespace WOMENACE.Code;

// Hooks for the outfit and weapon skin dev verbs. *.Dev.cs files stay out of release builds.
public sealed partial class TransmogPickerSystem
{
    internal bool DevSelect(string characterTag, string armorId) => DevSelect(characterTag, ItemSlot.InfantryArmor, armorId);

    internal void RefreshVisible()
    {
        var root = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.GetRootElement();
        if (root != null)
            foreach (var window in UI.FindAll(root, UiSelector.TypeName("UnitWindow")))
                EnsureUi(window);
    }

    internal bool DevSelect(string characterTag, ItemSlot slot, string id)
    {
        var root = Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.GetRootElement();
        if (root == null)
            return false;
        foreach (var window in UI.FindAll(root, UiSelector.TypeName("UnitWindow")))
            if (Affinity.CharacterTag(Affinity.LeaderOf(window)) == characterTag)
                return Select(window, slot, id);
        return false;
    }
}
