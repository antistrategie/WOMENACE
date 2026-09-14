using Jiangyu.Sdk;

namespace WOMENACE.Code;

public sealed class WeaponSkin(string id, WeaponClass weaponClass, Func<string> name, string muzzleEffect = null)
{
    public string Id { get; } = id;
    public WeaponClass Class { get; } = weaponClass;
    public string RewardId => "weapon_skin.wmgfl_" + Id;
    public string ModelAsset => "weapon/" + Id + "/main";
    public string IconAsset => "weapon_skins__" + Id;
    public string MuzzleEffectAsset { get; } = muzzleEffect;
    public string Name => name();
    public bool IsUnlocked(ProcurementState state) => state.Claimed(RewardId) > 0;
}

public static class WeaponSkins
{
    public static IReadOnlyList<WeaponSkin> All { get; } = new[]
    {
        new WeaponSkin("bang_bang_branch", WeaponClass.Rifle,
            () => Locale.Text("WOMENACE::ui/weapon_skins/bang_bang_branch", "Bang Bang Branch"),
            "weapon/bang_bang_branch/muzzle"),
        new WeaponSkin("lantern_airship", WeaponClass.AssaultRifle,
            () => Locale.Text("WOMENACE::ui/weapon_skins/lantern_airship", "Lantern Airship"),
            "weapon/lantern_airship/muzzle"),
        new WeaponSkin("spiral_commandment", WeaponClass.Smg,
            () => Locale.Text("WOMENACE::ui/weapon_skins/spiral_commandment", "Spiral Commandment")),
        new WeaponSkin("chronometer", WeaponClass.Shotgun,
            () => Locale.Text("WOMENACE::ui/weapon_skins/chronometer", "Chronometer")),
        new WeaponSkin("clockwork_guard", WeaponClass.Blade,
            () => Locale.Text("WOMENACE::ui/weapon_skins/clockwork_guard", "Clockwork Guard")),
        new WeaponSkin("black_mask", WeaponClass.MachineGun,
            () => Locale.Text("WOMENACE::ui/weapon_skins/black_mask", "Black Mask")),
    };

    public static IEnumerable<WeaponSkin> Available(ProcurementState unlocks, WeaponClass weaponClass)
        => All.Where(skin => skin.Class == weaponClass && skin.IsUnlocked(unlocks));

    public static WeaponSkin SelectionFor(WeaponSkinState state, ProcurementState unlocks,
        int characterKey, string slot, WeaponClass weaponClass)
    {
        var id = state.SelectionFor(characterKey, slot);
        return id == null ? null : Available(unlocks, weaponClass).FirstOrDefault(skin => skin.Id == id);
    }

    public static bool Select(WeaponSkinState state, ProcurementState unlocks,
        int characterKey, string slot, WeaponClass weaponClass, string id)
    {
        if (characterKey == 0 || string.IsNullOrEmpty(slot)
            || id != null && !Available(unlocks, weaponClass).Any(skin => skin.Id == id))
            return false;
        state.SetSelection(characterKey, slot, id);
        return true;
    }
}

// Cosmetic choices belong to a Doll's loadout, so replacing or losing an item does not
// consume the unlock. Slot names keep the primary and special weapon choices independent.
public sealed class WeaponSkinState
{
    public Dictionary<int, Dictionary<string, string>> Selections { get; set; } = [];

    public string SelectionFor(int characterKey, string slot)
        => Selections.TryGetValue(characterKey, out var slots) ? slots.GetValueOrDefault(slot) : null;

    internal void SetSelection(int characterKey, string slot, string id)
    {
        if (id == null)
        {
            if (Selections.TryGetValue(characterKey, out var existing))
            {
                existing.Remove(slot);
                if (existing.Count == 0)
                    Selections.Remove(characterKey);
            }
            return;
        }
        if (!Selections.TryGetValue(characterKey, out var slots))
            Selections[characterKey] = slots = new Dictionary<string, string>(StringComparer.Ordinal);
        slots[slot] = id;
    }
}
