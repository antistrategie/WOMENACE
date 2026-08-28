using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for the weapon calibration loop, invoked over the dev-loader bridge as e.g.
// {verb: "Weapons.Merge", mutate: true}. The bridge and verb runner live only in the dev loader,
// so these are unreachable in a shipped mod, and the *.Dev.cs name keeps them out of releases.
[DevVerb]
public static class Weapons
{
    private static object NoSystem => new { error = "calibration system not initialised" };

    private static readonly Dictionary<string, Il2CppMenace.Items.WeaponTemplate> GiveCache = new(StringComparer.Ordinal);

    // Mint owned instances of any weapon into shared stock, for testing equip
    // surfaces without the black market.
    [MutatingVerb]
    public static object Give(string weaponId, int count = 1)
    {
        var context = BayMountSystem.Instance?.Context;
        if (context == null)
            return new { error = "no mod context" };
        var owned = Jiangyu.Game.Strategy.Inventory.Owned;
        if (owned == null)
            return new { error = "no strategy state / owned items" };
        var template = Templates.Resolve<Il2CppMenace.Items.WeaponTemplate>(
            weaponId, GiveCache, msg => context.Log.Warn($"weapons: {msg}"));
        if (template == null)
            return new { error = $"unknown weapon template '{weaponId}'" };
        var minted = 0;
        for (var i = 0; i < Math.Clamp(count, 0, 20); i++)
            if (owned.AddItem(template, false, false) != null)
                minted++;
        return new { ok = true, weaponId, minted };
    }

    // Every calibratable weapon instance the player owns: weapon, rank and holder.
    public static object Status()
        => CalibrationSystem.Instance?.DevStatus() ?? NoSystem;

    // Craft one R0 duplicate of the doll's base weapon through its blueprint (consumes the component
    // and the salvage materials), as assembling at the workshop bench would.
    [MutatingVerb]
    public static object Craft(string characterTag = "wmgfl_makiatto")
        => CalibrationSystem.Instance?.DevCraft(characterTag) ?? NoSystem;

    // Merge the doll's equipped weapon with one duplicate, raising its calibration rank.
    [MutatingVerb]
    public static object Merge(string characterTag = "wmgfl_makiatto")
        => CalibrationSystem.Instance?.DevMerge(characterTag) ?? NoSystem;

    // Revert the doll's equipped weapon back one rank, returning an R0 duplicate to stock.
    [MutatingVerb]
    public static object Revert(string characterTag = "wmgfl_makiatto")
        => CalibrationSystem.Instance?.DevRevert(characterTag) ?? NoSystem;

    // Open the calibration screen, for testing without the workshop button.
    [MutatingVerb]
    public static object OpenCalibration()
        => CalibrationUISystem.Instance?.DevOpen() ?? NoSystem;

    // Set the doll's affinity to a level and re-run the component grant, to test the retroactive
    // back-fill (a save already past component levels grants everything owed at once).
    [MutatingVerb]
    public static object SetAffinityLevel(string characterTag = "wmgfl_makiatto", int level = 6)
        => CalibrationSystem.Instance?.DevSetAffinityLevel(characterTag, level) ?? NoSystem;

    // Top up components directly, bypassing the affinity schedule (and its ledger), for testing
    // the craft and merge loop without gift grinding.
    [MutatingVerb]
    public static object GrantComponents(string characterTag = "wmgfl_makiatto", int count = 1)
    {
        if (Affinity.KeyForTag(characterTag) == 0)
            return new { error = $"unknown character '{characterTag}'" };
        var componentId = Calibration.ComponentIdFor(Calibration.WeaponIdFor(characterTag));
        var component = Templates.ById<Il2CppMenace.Items.CommodityTemplate>(componentId);
        if (component == null)
            return new { error = $"component template '{componentId}' not registered" };
        var granted = 0;
        for (var i = 0; i < count; i++)
            if (Inventory.AddItem(component) != null)
                granted++;
        return new { ok = true, granted, component = component.GetID() };
    }

    // Open the workshop screen and report which blueprints its projects list actually offers, to
    // verify our duplicate recipe surfaces there (and diagnose the unlock path if it does not).
    [MutatingVerb]
    public static object OpenWorkshop()
    {
        var manager = UIManager.Get();
        if (manager == null)
            return new { error = "no ui manager" };
        var workshop = manager.OpenScreen(WorkshopUIScreen.PREFAB_NAME)?.TryCast<WorkshopUIScreen>();
        if (workshop == null)
            return new { error = "workshop screen did not open" };

        var available = new List<string>();
        var list = workshop.m_SortedAvailableBlueprints;
        for (var i = 0; list != null && i < list.Count; i++)
            available.Add(list[i]?.GetID());
        // Joined into one string: the verb runner's JSON layer renders arrays as their type name.
        return new
        {
            ok = true,
            count = available.Count,
            available = string.Join(", ", available),
            selected = workshop.m_SelectedBlueprint?.GetID(),
        };
    }

}
