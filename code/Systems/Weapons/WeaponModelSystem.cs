using Il2CppMenace.Items;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// A Doll weapon's template carries no model of its own: it keeps the one of the vanilla weapon
// it is cloned from, and its own is installed the first time a unit's items are asked for what
// they look like. A weapon nobody holds keeps its model and textures unloaded.
// ElementAttachments..ctor asks GetVisualAlteration for each socket before it spawns anything,
// in a mission and in the armoury alike, and the container answers from a list it keeps. The
// list is made again here, by marking the container dirty and asking for the update, so a
// model just installed is in the answer.
public sealed class WeaponModelSystem : JiangyuSystem
{
    private static WeaponModelSystem _instance;
    private readonly HashSet<IntPtr> _checked = [];

    public override void OnInit()
    {
        _instance = this;
        Context.Patches.Prefix("Il2CppMenace.Items.ItemContainer", "GetVisualAlteration", BeforeVisualAlteration);
        Context.Patches.Prefix("Il2CppMenace.Tactical.Element", "CreateAttachments", BeforeCreateAttachments);
    }

    public override void OnUnload()
    {
        _instance = null;
        _checked.Clear();
    }

    private void BeforeVisualAlteration(PatchInfo info)
    {
        if (info.Instance is not ItemContainer items)
            return;
        var installed = false;
        foreach (var slot in WeaponSkinSystem.Slots)
            installed |= Install(WeaponSkinSystem.WeaponAt(items, slot));
        if (installed)
        {
            items.VisualAlterationsDirty = true;
            items.UpdateVisualAlterations();
        }
    }

    // A mission's units are given their attachments once, from a container whose answers were
    // made before any model was installed, and often after another container has had the
    // weapon installed for it. So the container a unit is built from is made to answer again
    // whenever it holds a weapon that has a model of its own. UpdateVisualAlterations makes
    // the answers again only for a container marked dirty.
    private void BeforeCreateAttachments(PatchInfo info)
    {
        if ((info.Args is { Count: > 1 } ? info.Args[1] : null) is not ItemContainer items)
            return;
        var own = false;
        foreach (var slot in WeaponSkinSystem.Slots)
        {
            var weapon = WeaponSkinSystem.WeaponAt(items, slot);
            if (weapon == null)
                continue;
            Install(weapon);
            own |= WeaponModels.AssetsFor(weapon.GetID()).Model != null;
        }
        if (own)
        {
            items.VisualAlterationsDirty = true;
            items.UpdateVisualAlterations();
        }
    }

    // Makes sure a weapon's own model is on its template, for code that reads the template.
    internal static void Ensure(WeaponTemplate weapon) => _instance?.Install(weapon);

    private bool Install(WeaponTemplate weapon)
    {
        if (weapon == null || !_checked.Add(weapon.Pointer))
            return false;
        var (model, secondary) = WeaponModels.AssetsFor(weapon.GetID());
        var first = Load(model);
        var second = Load(secondary);
        if (first != null)
            weapon.Model = first;
        if (second != null)
            weapon.ModelSecondary = second;
        return first != null || second != null;
    }

    private GameObject Load(string asset)
    {
        if (asset == null)
            return null;
        var model = Context.Assets.Load<GameObject>(asset);
        if (model == null)
            Context.Log.Warn($"Weapon model '{asset}' is unavailable.");
        return model;
    }
}
