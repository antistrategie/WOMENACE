using Il2CppMenace.Items;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

[DevVerb]
public static class WeaponSkinsDev
{
    [MutatingVerb]
    public static object Select(string characterTag, string skin = "bang_bang_branch", string slot = "InfantryWeapon")
    {
        if (!Enum.TryParse<ItemSlot>(slot, true, out var itemSlot) || !WeaponSkinSystem.Slots.Contains(itemSlot))
            return new { error = "unknown weapon slot" };
        var id = string.IsNullOrEmpty(skin) || skin == "default" ? null : skin;
        return new { ok = TransmogPickerSystem.Instance?.DevSelect(characterTag, itemSlot, id) == true };
    }
}
