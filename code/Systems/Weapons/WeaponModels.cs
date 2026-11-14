namespace WOMENACE.Code;

// Which prefabs a Doll weapon's template is given when it is first held. A Doll's weapon is
// weapon.<name> with its model at weapon/<name>/main, and a calibration rank of it has the same
// one: Calibration knows a Doll's weapon by her tag, so a new Doll's needs nothing here. A
// special weapon carried in two parts names both.
internal static class WeaponModels
{
    public static (string Model, string Secondary) AssetsFor(string templateId)
    {
        switch (templateId)
        {
            case "specialweapon.asteria_railgun":
                return ("weapon/asteria_railgun_bracket/main", "weapon/asteria_railgun/main");
            case "specialweapon.welrod_umbrella":
                return ("weapon/welrod_umbrella_back/main", "weapon/welrod_umbrella/main");
        }
        if (!Calibration.TryResolveWeaponId(templateId, out var baseId, out _))
            return default;
        var name = baseId["weapon.".Length..];
        // OTs-14's rifle has her arms as its second part.
        return ($"weapon/{name}/main", baseId == Bay.WeaponId ? "weapon/ots14_arms/main" : null);
    }
}
