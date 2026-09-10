using Il2CppMenace.Items;

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
            foreach (var asset in Templates.Packaged<WeaponTemplate>())
                Assets[asset.GetID()] = asset;
        }
        return Assets.TryGetValue(weapon.GetID(), out var original) && original != null && original.Pointer == weapon.Pointer;
    }
}
