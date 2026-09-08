using Il2CppInterop.Runtime;
using Il2CppMenace.Items;
using Il2CppMenace.Tools;
using UnityEngine;

namespace WOMENACE.Code;

internal static class BaseGameWeapons
{
    private static readonly Dictionary<string, WeaponTemplate> Assets = new(StringComparer.Ordinal);
    private static bool _loaded;

    public static void Reset()
    {
        Assets.Clear();
        WeaponClasses.ClearCache();
        _loaded = false;
    }

    public static bool Contains(WeaponTemplate weapon)
    {
        if (weapon == null)
            return false;
        if (!_loaded)
        {
            _loaded = true;
            // Resources reads the game's packaged assets. DataTemplateLoader.GetAll also contains
            // runtime mod clones, so it cannot establish whether a weapon belongs to the base game.
            var folder = DataTemplateLoader.GetBaseFolder(Il2CppType.Of<WeaponTemplate>());
            foreach (var asset in Resources.LoadAll<WeaponTemplate>(folder))
                Assets[asset.GetID()] = asset;
        }
        return Assets.TryGetValue(weapon.GetID(), out var original) && original != null && original.Pointer == weapon.Pointer;
    }
}
