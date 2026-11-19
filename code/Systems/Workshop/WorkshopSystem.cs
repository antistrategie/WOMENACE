using Il2CppInterop.Runtime;
using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Game.Ui.Components;
using Jiangyu.Sdk;
using UnityEngine.UIElements;
using TextButton = Jiangyu.Game.Ui.Components.TextButton;

namespace WOMENACE.Code;

public sealed class WorkshopSystem : JiangyuSystem
{
    public static WorkshopSystem Instance { get; private set; }
    private WorkshopDisassemblyView _disassembly;

    public override void OnInit()
    {
        Instance = this;
        _disassembly = new WorkshopDisassemblyView(Context);
        UI.Inject(UiTarget.Screen<WorkshopUIScreen>().AppendTo(UiSelector.Name(WorkshopUi.Assemble)),
            "workshop/workshop", _disassembly.Bind);
        Context.Patches.Prefix(WorkshopUi.ScreenType, "UpdateWindow", BeforeUpdate);
        Context.Patches.Postfix(WorkshopUi.ScreenType, "UpdateWindow", AfterUpdate);
        Context.Patches.Postfix(WorkshopUi.ScreenType, "SetSelectedBlueprint", info =>
            StyleItems((info.Instance as Il2CppSystem.Object)?.TryCast<WorkshopUIScreen>()));
        Context.Patches.Prefix(WorkshopUi.ScreenType, "OnOpened", OnOpened);
        Context.Patches.Postfix("Il2CppMenace.Strategy.OwnedItems", "AddItem", 3, OnItemAdded);
    }

    // Whether the Workshop is taking weapons apart, and not assembling them.
    internal bool Disassembling => _disassembly.IsActive;

    internal void SetDisassembling(bool on)
    {
        if (_disassembly.IsActive != on)
            _disassembly.SetMode(on);
    }

    // The weapon the Workshop is working on: the one picked to take apart, or what the picked
    // blueprint makes.
    internal Il2CppMenace.Items.BaseItemTemplate WorkedOn(WorkshopUIScreen screen) => _disassembly.IsActive
        ? _disassembly.Selected?.GetTemplate()
        : screen?.m_SelectedBlueprint?.GetCraftingResult();

    public override void OnUnload()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    public override void OnTemplatesApplied() => BaseGameWeapons.Reset();

    public override void OnSceneLoaded(int buildIndex, string sceneName) => BaseGameWeapons.Reset();

    private void OnItemAdded(PatchInfo info)
    {
        if (info.Args[0] is Il2CppSystem.Object argument
            && argument.TryCast<WeaponTemplate>() is { } weapon && info.Result != null)
            Remember(weapon.GetID());
    }

    private void Remember(string id)
    {
        if (!Calibration.TryResolveWeaponId(id, out var baseId, out _))
            return;
        var known = Context.State.Get<WorkshopState>().KnownWeaponIds;
        if (!known.Contains(baseId))
            known.Add(baseId);
    }

    private void ReconcileRecipes()
    {
        if (StrategyState.Get() == null)
            return;
        foreach (var weapon in InventoryStock.WeaponTemplates(Inventory.Owned))
            if (Inventory.Owned.GetInstanceCount(weapon) > 0)
                Remember(weapon.GetID());
        foreach (var leader in Leaders.Hired() ?? [])
        {
            var tag = Affinity.CharacterTag(leader);
            if (tag == null)
                continue;
            Remember(Calibration.WeaponIdFor(tag));
            foreach (var id in Unlocks.UnlockedWeapons(tag, Affinity.LevelFor(Context, leader)))
                Remember(id);
        }
    }

    private void BeforeUpdate(PatchInfo info)
    {
        var screen = (info.Instance as Il2CppSystem.Object)?.TryCast<WorkshopUIScreen>();
        if (screen?.m_SortedAvailableBlueprints == null)
            return;
        // Core blueprints share the source weapon blueprint's native category. Add them in their
        // own section after the native projects render, preserving all other recipe groups.
        for (var i = screen.m_SortedAvailableBlueprints.Count - 1; i >= 0; i--)
            if (CalibrationCores.Contains(screen.m_SortedAvailableBlueprints[i]?.GetCraftingResult()?.GetID()))
                screen.m_SortedAvailableBlueprints.RemoveAt(i);
        var ids = Context.State.Get<WorkshopState>().KnownWeaponIds.Select(Calibration.BlueprintIdFor);
        foreach (var id in ids)
        {
            var blueprint = Templates.ById<BlueprintTemplate>(id);
            var list = screen.m_SortedAvailableBlueprints;
            if (blueprint == null)
                continue;
            var exists = false;
            for (var i = 0; i < list.Count; i++)
                if (list[i]?.GetID() == id)
                    exists = true;
            if (!exists)
                list.Add(blueprint);
        }

        // UpdateWindow adds a heading whenever the crafting result's GetRewardType changes
        // (native RVA 0x52F750). Keep each category contiguous after adding unlocked recipes.
        var available = screen.m_SortedAvailableBlueprints;
        var categories = new Dictionary<int, List<BlueprintTemplate>>();
        var groups = new List<List<BlueprintTemplate>>();
        for (var i = 0; i < available.Count; i++)
        {
            var blueprint = available[i];
            var result = blueprint?.GetCraftingResult();
            if (result == null)
                continue;
            var category = (int)result.GetRewardType();
            if (!categories.TryGetValue(category, out var group))
            {
                group = new List<BlueprintTemplate>();
                categories.Add(category, group);
                groups.Add(group);
            }
            group.Add(blueprint);
        }
        var index = 0;
        foreach (var group in groups)
            foreach (var blueprint in group)
                available[index++] = blueprint;
        while (available.Count > index)
            available.RemoveAt(available.Count - 1);
    }

    private void OnOpened(PatchInfo info)
    {
        _disassembly.Reset();
        ReconcileRecipes();
    }

    private void AfterUpdate(PatchInfo info)
    {
        var screen = (info.Instance as Il2CppSystem.Object)?.TryCast<WorkshopUIScreen>();
        if (screen == null)
            return;
        GroupMaterials(screen);
        if (!_disassembly.IsActive)
            AddCoreProjects(screen);
        _disassembly.Refresh(screen);
        StyleItems(screen);
    }

    // Each material a project takes shows how many are owned against how many it takes, in red
    // where there are too few, so the materials' own list need not be read through for it.
    private static void CountRequired(WorkshopUIScreen screen)
    {
        var owned = Inventory.Owned;
        var container = screen.m_ReqMaterialsContainer;
        if (owned == null || container == null)
            return;
        // Counts come from the blueprint. Vanilla hides the stack label for one and leaves its
        // old text in place, so the label cannot be read for them.
        var takesById = new Dictionary<string, int>();
        var materials = screen.m_SelectedBlueprint?.GetCraftingMaterials();
        for (var i = 0; materials != null && i < materials.Count; i++)
        {
            var id = materials[i].Template?.GetID();
            if (id != null)
                takesById[id] = takesById.GetValueOrDefault(id) + materials[i].Count;
        }
        var slots = 0;
        foreach (var element in UI.FindAll(container, UiSelector.TypeName("BlackMarketItemSlot")))
        {
            var slot = element.TryCast<BlackMarketItemSlot>();
            if (slot?.m_ItemTemplate == null)
                continue;
            slots++;
            var takes = takesById.TryGetValue(slot.m_ItemTemplate.GetID() ?? "", out var needed) && needed > 0 ? needed : 1;
            var have = owned.GetInstanceCount(slot.m_ItemTemplate);
            if (slot.m_StackSize != null)
                slot.m_StackSize.style.display = DisplayStyle.None;
            var count = UI.Find(slot, UiSelector.Name(RequiredCountName))?.TryCast<Label>();
            if (count == null)
            {
                // The slot is vanilla's, outside the Workshop's own stylesheet, so the label is
                // styled here.
                count = new Label { name = RequiredCountName, pickingMode = PickingMode.Ignore };
                count.style.position = Position.Absolute;
                count.style.right = 1f;
                count.style.bottom = 0f;
                count.style.marginLeft = count.style.marginRight = count.style.marginTop = count.style.marginBottom = 0f;
                count.style.paddingTop = count.style.paddingBottom = 0f;
                count.style.paddingLeft = count.style.paddingRight = 2f;
                count.style.fontSize = 9f;
                count.style.backgroundColor = new UnityEngine.Color(0f, 0f, 0f, 0.75f);
                slot.Add(count);
            }
            count.text = $"{have}/{takes}";
            count.style.color = have >= takes ? Enough : ShortOf;
        }
        FitRequired(container, slots);
    }

    // Where the strategy shell gives what a project takes one row of a set width, more slots
    // than the row holds are drawn smaller, so every one of them is still seen at once.
    private static void FitRequired(VisualElement container, int slots)
    {
        var width = RequiredWidth ?? (container.style.width.keyword == StyleKeyword.Undefined ? container.style.width.value.value : 0f);
        var needed = slots * RequiredSlotStep;
        if (width > 0f && needed > width)
        {
            var fit = width / needed;
            container.style.transformOrigin = new TransformOrigin(0f, 0f, 0f);
            container.style.scale = new Scale(new UnityEngine.Vector3(fit, fit, 1f));
        }
        else
        {
            container.style.transformOrigin = StyleKeyword.Null;
            container.style.scale = StyleKeyword.Null;
        }
    }

    // A slot's width with the room after it (shell.uss).
    private const float RequiredSlotStep = 55f;
    // Set by a dev verb to try the fit on a narrower row than any project needs.
    internal static float? RequiredWidth;

    private const string RequiredCountName = "wm-required-count";
    private static readonly UnityEngine.Color Enough = new(222f / 255f, 208f / 255f, 154f / 255f);
    private static readonly UnityEngine.Color ShortOf = new(1f, 123f / 255f, 108f / 255f);

    private static void StyleItems(WorkshopUIScreen screen)
    {
        if (screen == null)
            return;
        foreach (var element in UI.FindAll(screen.GetRootElement(), UiSelector.TypeName("BlackMarketItemSlot")))
        {
            var slot = element.TryCast<BlackMarketItemSlot>();
            WorkshopItemStyle.Apply(slot, slot.m_ItemTemplate);
        }
        var resultId = screen.m_SelectedBlueprint?.GetCraftingResult()?.GetID();
        foreach (var element in UI.FindAll(screen.m_ProjectPreview, UiSelector.Name("Image")))
            element.style.unityBackgroundImageTintColor = WorkshopItemStyle.Tint(resultId);
        // Vanilla fills what a project takes when one is picked as well as when the window is.
        CountRequired(screen);
    }

    private static string CoreHeading() => Locale.Text("WOMENACE::ui/workshop/cores", "Calibration Cores");

    private static void AddCoreProjects(WorkshopUIScreen screen)
    {
        var view = screen.m_ProjectsView;
        if (view == null)
            return;
        view.AddLabelRow(CoreHeading());
        foreach (var id in CalibrationCores.All.Select(CalibrationCores.BlueprintId))
        {
            var blueprint = Templates.ById<BlueprintTemplate>(id);
            if (blueprint == null)
                continue;
            screen.m_SortedAvailableBlueprints.Add(blueprint);
            var slot = view.AddSlot<BlackMarketItemSlot>(5);
            // Native project slots display the crafting result and keep the blueprint in the click action.
            slot.Init(blueprint.GetCraftingResult(), 1);
            slot.SetSelectable(true);
            slot.SetItemToCompareWith(screen.m_SelectedBlueprint?.GetCraftingResult());
            slot.SetCrossVisibility(!blueprint.OwnsPlayerAllCraftingMaterials());
            slot.SetSelected(screen.m_SelectedBlueprint?.GetID() == id);
            slot.SetOnLeftClickedAction(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<InteractiveElement>>(
                (Action<InteractiveElement>)(_ =>
                {
                    screen.SetSelectedBlueprint(blueprint);
                    screen.UpdateWindow();
                })));
        }
    }

    public void SetMode(bool disassemble) => _disassembly.SetMode(disassemble);
    public List<Item> Disassemblable() => WeaponDisassembly.Available();
    public (bool ok, string error) Disassemble(Item item) => _disassembly.Disassemble(item);

    private void GroupMaterials(WorkshopUIScreen screen)
    {
        var view = screen.m_PlayerMaterialsView;
        if (view == null || Inventory.Owned == null)
            return;
        // Retain the native material selection, then give the new ingredient families their own rows.
        var original = UI.FindAll(view, UiSelector.TypeName("BlackMarketItemSlot"))
            .Select(element => element.TryCast<BlackMarketItemSlot>()?.m_ItemTemplate)
            .Where(template => template != null && !WeaponParts.IsPart(template.GetID())
                && !WeaponParts.IsComponent(template.GetID()) && !CalibrationCores.Contains(template.GetID()))
            .GroupBy(template => template.GetID()).Select(group => group.First()).ToList();
        view.ClearSlots();
        AddMaterials(view, Locale.Text("WOMENACE::ui/workshop/materials", "Materials"), original);
        AddMaterials(view, Locale.Text("WOMENACE::ui/workshop/parts", "Weapon Parts"),
            WeaponClasses.All.SelectMany(WeaponParts.ForClass).Select(id => Templates.ById<CommodityTemplate>(id)));
        AddMaterials(view, Locale.Text("WOMENACE::ui/workshop/components", "Weapon Components"),
            Templates.All<CommodityTemplate>().Where(template => WeaponParts.IsComponent(template.GetID())));
        AddMaterials(view, CoreHeading(), new[] { CalibrationCores.StandardId, CalibrationCores.AdvancedId }
            .Select(id => Templates.ById<CommodityTemplate>(id)));
    }

    private static void AddMaterials(SortedFilteredItemList view, string label, IEnumerable<BaseItemTemplate> templates)
    {
        var items = templates.Where(template => template != null && Inventory.Owned.GetInstanceCount(template) > 0).ToList();
        if (items.Count == 0)
            return;
        view.AddLabelRow(label);
        var instances = Inventory.Owned.GetRawInstances();
        foreach (var template in items)
            if (instances.TryGetValue(template, out var stock))
                // UpdateWindow (RVA 0x52F750) initialises material slots from their instances,
                // with a trade multiplier of 1. Template-only Init omits the native item state.
                view.AddSlot<BlackMarketItemSlot>(5).Init(stock, 1f);
    }

}
