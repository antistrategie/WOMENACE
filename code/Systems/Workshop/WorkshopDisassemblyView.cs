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

internal sealed class WorkshopDisassemblyView(ModContext context)
{
    private readonly ModContext _context = context;
    private readonly Random _random = new();
    private VisualElement _root, _disassembly, _tabs;
    private Item _selected;
    private bool _disassemble;
    private string _feedback;
    public bool IsActive => _disassemble;

    public void Reset()
    {
        _disassemble = false;
        _selected = null;
        _feedback = null;
    }

    public void Bind(VisualElement root)
    {
        _root = root;
        UiLayout.Fill(root);
        root.pickingMode = PickingMode.Ignore;
        _tabs = UI.Find(root, UiSelector.Name("wm-workshop-tabs"));
        _disassembly = UI.Find(root, UiSelector.Name("wm-disassembly"));
        foreach (var disassemble in new[] { false, true })
        {
            var button = new TextButton(disassemble
                ? Locale.Text("WOMENACE::ui/workshop/disassemble", "DISASSEMBLE")
                : Locale.Text("WOMENACE::ui/workshop/assemble", "ASSEMBLE"));
            button.Root.AddToClassList("wm-workshop-tab");
            button.OnClick(() => SetMode(disassemble));
            _tabs.Add(button.Root);
        }
        _disassembly.SetVisible(false);
    }

    public void SetMode(bool disassemble)
    {
        _disassemble = disassemble;
        _feedback = null;
        Screen()?.UpdateWindow();
    }

    private static WorkshopUIScreen Screen() => UIManager.Get()?.GetActiveScreen()?.TryCast<WorkshopUIScreen>();

    public List<Item> Disassemblable() => WeaponDisassembly.Available();

    public (bool ok, string error) Disassemble(Item item)
    {
        var result = WeaponDisassembly.Run(item, _random, _context.Log);
        if (result.ok)
        {
            _selected = null;
            _feedback = string.Join(", ", result.Parts.Select(id => "+1 " + Templates.DefaultText(Templates.ById<CommodityTemplate>(id)?.Title)));
        }
        return (result.ok, result.error);
    }

    public void Refresh(WorkshopUIScreen screen)
    {
        if (_root == null || _root.panel == null)
            return;
        var centre = UI.Find(screen.GetRootElement(), UiSelector.Name(WorkshopUi.Centre));
        centre?.SetVisible(!_disassemble);
        _disassembly.SetVisible(_disassemble);
        if (_tabs != null)
            for (var i = 0; i < _tabs.childCount; i++)
                _tabs.ElementAt(i).EnableInClassList("wm-workshop-tab-active", (i == 1) == _disassemble);
        if (!_disassemble)
            return;

        var weapons = Disassemblable();
        if (_selected == null || !weapons.Any(item => item.Pointer == _selected.Pointer))
            _selected = weapons.FirstOrDefault();
        var view = screen.m_ProjectsView;
        view.ClearSlots();
        view.AddLabelRow(Locale.Text("WOMENACE::ui/workshop/your_weapons", "Your Weapons"));
        foreach (var group in weapons.GroupBy(item => item.GetTemplate().GetID()))
        {
            var item = group.First();
            var slot = view.AddSlot<BlackMarketItemSlot>(5);
            slot.Init(item.GetTemplate(), group.Count());
            slot.SetCrossVisibility(false);
            slot.SetSelected(_selected?.GetTemplate()?.GetID() == group.Key);
            slot.SetOnLeftClickedAction(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<InteractiveElement>>(
                (Action<InteractiveElement>)(_ => { _selected = item; _feedback = null; Refresh(screen); })));
        }
        BuildDisassembly();
    }

    private void BuildDisassembly()
    {
        _disassembly.Clear();
        if (_selected == null)
        {
            var empty = new Label(Locale.Text("WOMENACE::ui/workshop/no_weapons", "No weapons available."));
            empty.AddToClassList("wm-disassembly-empty");
            _disassembly.Add(empty);
            return;
        }
        var template = _selected.GetTemplate().TryCast<WeaponTemplate>();
        var banner = new VisualElement();
        banner.AddToClassList("wm-disassembly-banner");
        var image = new VisualElement();
        image.AddToClassList("wm-disassembly-image");
        image.style.backgroundImage = new StyleBackground(template.IconEquipment ?? template.Icon);
        banner.Add(image);
        var title = new Label(Templates.DefaultText(template.Title));
        title.AddToClassList("wm-disassembly-title");
        banner.Add(title);
        var subtitle = new Label(Templates.DefaultText(template.ShortName));
        subtitle.AddToClassList("wm-disassembly-subtitle");
        banner.Add(subtitle);
        _disassembly.Add(banner);
        var weaponClass = WeaponParts.DisassemblyClass(template);
        var heading = new Label(Locale.Format("WOMENACE::ui/workshop/random_parts", "1–2 RANDOM {0} PARTS", WeaponClasses.Code(weaponClass)));
        heading.AddToClassList("wm-disassembly-heading");
        _disassembly.Add(heading);
        var slots = new VisualElement();
        slots.AddToClassList("wm-disassembly-parts");
        foreach (var id in WeaponParts.ForClass(weaponClass))
        {
            var part = Templates.ById<CommodityTemplate>(id);
            if (part != null)
            {
                var tile = new ItemTile(part, 1);
                ItemTileStyle.Align(tile);
                WorkshopItemStyle.Apply(tile.Root, part);
                slots.Add(tile.Root);
            }
        }
        _disassembly.Add(slots);
        var button = new TextButton(Locale.Text("WOMENACE::ui/workshop/disassemble", "DISASSEMBLE"));
        button.Root.AddToClassList("wm-disassembly-action");
        button.OnClick(() =>
        {
            var result = Disassemble(_selected);
            if (!result.ok)
                _feedback = result.error;
            Screen()?.UpdateWindow();
        });
        _disassembly.Add(button.Root);
        var feedback = new Label(_feedback ?? "");
        feedback.AddToClassList("wm-disassembly-feedback");
        _disassembly.Add(feedback);
    }
}
