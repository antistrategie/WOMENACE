using Il2CppMenace.Items;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

internal static class WeaponDisassembly
{
    public static List<Item> Available()
        => new InventoryStock().Weapons(weapon => WeaponParts.DisassemblyClass(weapon) != WeaponClass.None).ToList();

    public static (bool ok, string error, IReadOnlyList<string> Parts) Run(Item item, Random random, IModLog log)
    {
        var weaponClass = item == null ? WeaponClass.None : WeaponParts.DisassemblyClass(item.GetTemplate()?.TryCast<WeaponTemplate>());
        if (!WorkshopAccess.IsUnlocked || weaponClass == WeaponClass.None)
            return (false, "Weapon is no longer available.", Array.Empty<string>());
        var parts = WeaponParts.Roll(weaponClass, random);
        var outputs = parts.Select(id => ((BaseItemTemplate)Templates.ById<CommodityTemplate>(id), 1)).ToList();
        var result = InventoryExchange.Run(new BaseItem[] { item }, outputs, log);
        if (result.ok)
            log.Info($"workshop: disassembled '{item.GetTemplate()?.GetID()}' into {string.Join(", ", parts)}");
        return (result.ok, result.error, parts);
    }
}
