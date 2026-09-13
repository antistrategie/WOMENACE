using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

public sealed class WeaponSkinSystem : JiangyuSystem
{
    internal static WeaponSkinSystem Instance { get; private set; }
    internal static readonly ItemSlot[] Slots = { ItemSlot.InfantryWeapon, ItemSlot.InfantrySpecial };
    private WeaponSkinState State => Context.State.Get<WeaponSkinState>();
    private ProcurementState Unlocks => Context.State.Get<ProcurementState>();

    private readonly HashSet<string> _missingAssets = new(StringComparer.Ordinal);

    public override void OnInit()
    {
        Instance = this;
        Context.Patches.Postfix("Il2CppMenace.Items.ItemContainer", "GetVisualAlteration", OnVisualAlteration);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "GetMuzzle", 7, OnMuzzle);
    }

    public override void OnUnload()
    {
        Instance = null;
        _missingAssets.Clear();
    }

    // UnitActor.GetItems (RVA 0x665200) returns the strategy leader's container,
    // constructed with that leader as owner (BaseUnitLeader..ctor, 0x5BF060).
    // Its identity is available even before Element.GetEntity is wired at spawn.
    private static int CharacterKey(ItemContainer items)
    {
        if (items == null)
            return 0;
        var owner = items.GetOwner() as Il2CppObjectBase;
        var tag = Affinity.CharacterTag(owner?.TryCast<BaseUnitLeader>())
            ?? Affinity.CharacterTag(owner?.TryCast<Entity>());
        return Affinity.KeyForTag(tag);
    }

    internal WeaponSkin SelectionFor(ItemContainer items, ItemSlot slot, WeaponTemplate weapon)
        => WeaponSkins.SelectionFor(State, Unlocks, CharacterKey(items), slot.ToString(), WeaponClasses.Classify(weapon));

    internal IReadOnlyList<WeaponSkin> Available(WeaponTemplate weapon)
        => WeaponSkins.Available(Unlocks, WeaponClasses.Classify(weapon)).ToArray();

    internal bool Select(BaseUnitLeader leader, ItemSlot slot, string id)
    {
        if (!Slots.Contains(slot))
            return false;
        var weapon = WeaponAt(leader?.GetItems(), slot);
        return weapon != null && WeaponSkins.Select(State, Unlocks, Affinity.KeyFor(leader),
            slot.ToString(), WeaponClasses.Classify(weapon), id);
    }

    internal static WeaponTemplate WeaponAt(ItemContainer items, ItemSlot slot)
        => items?.GetItemAtSlot(slot)?.GetTemplate()?.TryCast<WeaponTemplate>();

    // GetMuzzle prepares the flash for the engine's timed shot. Changing only that
    // result preserves burst timing and animation speed-up without editing shared skills.
    private void OnMuzzle(PatchInfo info)
    {
        if (State.Selections.Count == 0 || info.Instance is not Skill skill
            || info.Result is not Muzzle muzzle || muzzle.Prefab == null)
            return;
        var item = skill.GetItem();
        var items = skill.GetEntity()?.GetItems();
        if (item == null || items == null)
            return;
        foreach (var slot in Slots)
        {
            // A grenade or secondary weapon must not inherit the primary weapon's flash.
            if (items.GetItemAtSlot(slot)?.Pointer != item.Pointer)
                continue;
            var weapon = item.GetTemplate()?.TryCast<WeaponTemplate>();
            var skin = weapon == null ? null : SelectionFor(items, slot, weapon);
            if (skin != null && LoadPrefab(skin.ModelAsset) != null
                && LoadPrefab(skin.MuzzleEffectAsset) is { } prefab && prefab != null)
            {
                muzzle.Prefab = prefab;
                info.Result = muzzle;
            }
            return;
        }
    }

    // ElementAttachments..ctor (RVA 0x639FC0) calls GetVisualAlteration for each socket
    // before spawning its prefab. Both Element.CreateAttachments (0x607260) and
    // ArmoryElement.RefreshAttachments (0x4E7B90) use that constructor. Overriding the
    // returned prefab leaves the shared item template and the container's cache intact,
    // and lets native attachment creation collect the skin's muzzle, grip and animators.
    private void OnVisualAlteration(PatchInfo info)
    {
        if (State.Selections.Count == 0 || info.Instance is not ItemContainer items
            || info.Result is not GameObject original || original == null)
            return;
        try
        {
            var visualSlot = info.Args[0] is VisualAlterationSlot slot ? slot : (VisualAlterationSlot)(int)info.Args[0];
            foreach (var itemSlot in Slots)
            {
                var weapon = WeaponAt(items, itemSlot);
                if (weapon == null)
                    continue;
                // Matching both the socket and source prefab preserves another item's
                // visual precedence, including paired primary and special weapons.
                if (!(weapon.VisualAlterationSlot == visualSlot && weapon.Model == original)
                    && !(weapon.VisualAlterationSlotSecondary == visualSlot && weapon.ModelSecondary == original))
                    continue;
                var skin = SelectionFor(items, itemSlot, weapon);
                var model = LoadModel(skin);
                if (model != null)
                    info.Result = model;
                return;
            }
        }
        catch (Exception ex) { Context.Log.Warn($"weapon skin: model lookup failed: {ex.Message}"); }
    }

    // Bespoke carries can mount the primary rifle directly instead of asking the container.
    internal static GameObject ModelFor(ItemContainer items, ItemSlot slot, WeaponTemplate weapon)
    {
        var skin = Instance?.SelectionFor(items, slot, weapon);
        return Instance?.LoadModel(skin) ?? weapon?.Model;
    }

    private GameObject LoadModel(WeaponSkin skin)
    {
        if (skin == null)
            return null;
        var model = LoadPrefab(skin.ModelAsset);
        // Warm the flash alongside the equipped model so its first shot only hits the cache.
        if (model != null)
            LoadPrefab(skin.MuzzleEffectAsset);
        return model;
    }

    private GameObject LoadPrefab(string asset)
    {
        if (asset == null || _missingAssets.Contains(asset))
            return null;
        var prefab = Context.Assets.Load<GameObject>(asset);
        if (prefab == null && _missingAssets.Add(asset))
            Context.Log.Warn($"weapon skin: asset '{asset}' is unavailable");
        return prefab;
    }
}
