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

    // Selects a skin for a hired doll's weapon without her unit window being open, for a
    // screen that shows none (mission preparation).
    [MutatingVerb]
    public static object SelectFor(string characterTag, string skin, string slot = "InfantryWeapon")
    {
        if (!Enum.TryParse<ItemSlot>(slot, true, out var itemSlot) || !WeaponSkinSystem.Slots.Contains(itemSlot))
            return new { error = "unknown weapon slot" };
        var leaders = Il2CppMenace.States.StrategyState.Get()?.Roster?.m_HiredLeaders;
        for (var i = 0; leaders != null && i < leaders.Count; i++)
            if (leaders[i] != null && Affinity.CharacterTag(leaders[i]) == characterTag)
                return new { ok = WeaponSkinSystem.Instance?.Select(leaders[i], itemSlot, skin == "default" ? null : skin) == true };
        return new { error = $"no hired leader tagged '{characterTag}'" };
    }
}
