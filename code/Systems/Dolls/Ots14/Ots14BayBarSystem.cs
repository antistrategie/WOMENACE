using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.UI.Tactical;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Puts the bay weapons on the tactical skill bar. The vanilla bar draws one
// weapon slot per item in the actor's equipment container, which the bay
// items are deliberately not in, so a second ROW of vanilla weapon slots is
// added for them: stacked on the primary weapon's slot rather than extending
// the bar sideways (four more boxes would not fit the row). The slots are
// real SkillBarSlotWeapon elements fed the bay items, so icons, name labels,
// skill buttons, AP greying and uses bars all behave like the vanilla bar.
public sealed class Ots14BayBarSystem : JiangyuSystem
{
    private SkillBarSlotWeapon[] _slots;
    private VisualElement _row;
    // Whether the slots joined the bar's own element list (the bar then
    // handles their updates, selection highlights and key threading itself;
    // the forwarding postfixes below stand in when the join failed).
    private bool _inBarList;
    // Whether the row is showing. The per-frame button re-anchor only runs
    // while it is, since a hidden row has no geometry to measure.
    private bool _rowShown;
    // The bar the row lives in, so a link toggle can redraw it at once rather
    // than waiting for the next thing that happens to call UpdateSkills.
    private SkillBar _bar;

    // Lets the skill system ask for a redraw after it rewrites counters, from
    // code that has no reference to this system.
    internal static Action RequestRefresh;

    public override void OnInit()
    {
        const string barType = "Il2CppMenace.UI.Tactical.SkillBar";
        Context.Patches.Postfix(barType, "UpdateSkills", OnBarUpdated);
        // Our slots live outside the bar's own element list, so the signals
        // it fans out to that list are forwarded by hand.
        Context.Patches.Postfix(barType, "SetActiveSkill", OnBarActiveSkill);
        Context.Patches.Postfix(barType, "OnUpdate", OnBarTick);
        Context.Patches.Postfix(barType, "OnPreviewApChanged", OnBarPreviewAp);
        RequestRefresh = () => _bar?.UpdateSkills();
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        _slots = null;
        _row = null; // died with the tactical screen
        _bar = null;
        _linkButtons.Clear();
        _inBarList = false;
        _rowShown = false;
    }

    private void OnBarUpdated(PatchInfo info)
    {
        try
        {
            var bar = (info.Instance as Il2CppObjectBase)?.TryCast<SkillBar>();
            if (bar == null)
                return;
            var active = TacticalManager.Get()?.GetActiveActor();
            if (active == null || !BaySkillSystem.IsBayActor(active.Pointer))
            {
                _rowShown = false;
                if (_row != null)
                    _row.style.display = DisplayStyle.None;
                // The slots themselves hide too: they sit in the bar's own
                // element list, and the native keybind pass gates on each
                // slot's OWN visibility, so a merely hidden row left stale
                // bay skills hotkey-armed while another unit was selected.
                if (_slots != null)
                    foreach (var slot in _slots)
                        try { slot?.Hide(); } catch { }
                return;
            }
            if (!EnsureRow(bar))
                return;
            var ap = active.GetActionPoints();
            var shown = 0;
            for (var i = 0; i < Bay.SlotCount; i++)
            {
                // What was GRANTED this mission, not the live loadout: an
                // equip made after her element spawned only takes effect
                // next spawn, and the bar must agree with the arms.
                var item = BaySkillSystem.GrantedItemFor(active.Pointer, i);
                var group = BayLink.LinkedGroupOf(i);
                if (group >= 0)
                {
                    // A linked group presents as ONE weapon. Its first slot
                    // carries it and the rest hide, exactly as an empty slot
                    // does, so the row closes the gap on its own and a
                    // top-linked, bottom-loose bay reads 01 | 03 | 04 with no
                    // hole in it, while a quad reads as a single tile.
                    if (i != BayLink.Groups[group][0])
                    {
                        _slots[i].Hide();
                        continue;
                    }
                    item = BaySkillSystem.LinkedItemFor(active.Pointer, group) ?? item;
                }
                if (item == null)
                {
                    _slots[i].Hide();
                    continue;
                }
                _slots[i].SetItem(item, ap);
                _slots[i].Show();
                shown++;
            }
            UpdateLinkButtons(active.Pointer);
            _rowShown = shown > 0;
            _row.style.display = _rowShown ? DisplayStyle.Flex : DisplayStyle.None;
            AssignKeyBinds(bar);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay bar failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The bay row parents into the vanilla Weapons container, anchored to
    // its top edge: the bar hugs the bottom of the screen, so stacking
    // upward off the primary weapon slot is the vertical direction that
    // exists.
    private bool EnsureRow(SkillBar bar)
    {
        // The bar is minted per screen-open, so a cached row is only good
        // while it belongs to THIS bar: after a close and reopen the old row
        // hangs in a dead panel and the bay would go invisible and keyless
        // for the rest of the mission.
        if (_row != null && _slots != null && _bar != null && _bar.Pointer == bar.Pointer)
            return true;
        if (_row != null)
        {
            try { _row.RemoveFromHierarchy(); } catch { }
            _row = null;
            _slots = null;
            _linkButtons.Clear();
            _inBarList = false;
            _rowShown = false;
        }
        var weaponSlots = bar.m_WeaponSlots;
        if (weaponSlots == null || weaponSlots.Count == 0)
            return false;
        _bar = bar;
        var anchor = weaponSlots[0];
        var parent = anchor?.parent;
        if (parent == null)
            return false;
        _row = new VisualElement { name = "wmgfl-bay-row" };
        _row.style.flexDirection = FlexDirection.Row;
        _row.style.position = Position.Absolute;
        _row.style.bottom = Length.Percent(100);
        _row.style.left = 0f;
        // The vanilla slot art (keybind label, uses pips) overflows about
        // 20px above its layout box, so the row needs clearance beyond the
        // container's top edge or the two rows interleave.
        _row.style.marginBottom = 24f;
        parent.Add(_row);
        _slots = new SkillBarSlotWeapon[Bay.SlotCount];
        // Raised only once every slot is genuinely in the list. Claiming it up
        // front is what hid the failure: a null m_AllElements short-circuits
        // the null-conditional Add without throwing, so nothing reached the
        // catch and the flag stayed true while the slots sat in nobody's list,
        // which also left the manual forwarders disabled for the whole
        // mission.
        _inBarList = false;
        var joined = 0;
        for (var i = 0; i < Bay.SlotCount; i++)
        {
            var slot = new SkillBarSlotWeapon();
            slot.Init(bar.m_Screen);
            _row.Add(slot);
            _slots[i] = slot;
            // Joining the bar's own element list makes the slots first-class
            // bar citizens: selection highlights, per-frame updates and the
            // hotkey threading all reach them through the vanilla loops.
            try
            {
                var all = bar.m_AllElements;
                if (all != null)
                {
                    all.Add(slot.Cast<ISkillBarElement>());
                    joined++;
                }
            }
            catch
            {
                // the forwarders below take over
            }
        }
        _inBarList = joined == Bay.SlotCount;
        if (!_inBarList)
            Context.Log.Debug($"bay bar: joined {joined}/{Bay.SlotCount} slot(s) to the bar list, forwarding manually");
        return true;
    }

    // ----- link toggles ----------------------------------------------------

    // Group -> its link button. Buttons ride a carrier tile rather than a
    // fixed offset in the row: the row reflows whenever a slot is empty or a
    // linked partner hides, so a button pinned to the row would drift as soon
    // as the groups were in different states. Riding a tile makes every
    // combination correct without a layout mode.
    private readonly Dictionary<int, Button> _linkButtons = new();

    // The tile a group's button rides: the first of its slots that is
    // showing. With the quad linked that is slot 0 for every group, but the
    // pair buttons are hidden then, so nothing collides.
    private SkillBarSlotWeapon ButtonHost(int group)
    {
        foreach (var slot in BayLink.Groups[group])
        {
            var linkedGroup = BayLink.LinkedGroupOf(slot);
            if (linkedGroup >= 0 && slot != BayLink.Groups[linkedGroup][0])
                continue; // hidden under a linked carrier
            return _slots[slot];
        }
        return _slots[BayLink.Groups[group][0]];
    }

    // The X of the slot's VISIBLE weapon box in row space. The slot element
    // is wider than the box it draws (the hotkey label rides above it), so
    // anchoring to the element's own layout left the button hanging left of
    // the box.
    private float ButtonLeft(SkillBarSlotWeapon host)
    {
        try
        {
            var box = host.m_Background;
            if (box != null && _row != null)
            {
                var left = box.worldBound.x - _row.worldBound.x;
                if (!float.IsNaN(left) && !float.IsInfinity(left))
                    return left;
            }
        }
        catch
        {
            // un-laid-out geometry reads as NaN, the fallback holds a frame
        }
        return host.layout.x;
    }

    private void UpdateLinkButtons(IntPtr actorPtr)
    {
        for (var group = 0; group < BayLink.Groups.Length; group++)
        {
            try
            {
                if (_row == null || _slots == null)
                    return;
                var grouped = BayLink.IsGrouped(Context, group);
                if (!_linkButtons.TryGetValue(group, out var button) || button == null)
                {
                    if (!grouped)
                        continue;
                    button = BuildLinkButton(group);
                    // A SIBLING of the tiles, not a child of one. The bar
                    // resolves a click to the nearest interactive ancestor,
                    // so a button inside the tile fired the tile's first
                    // skill as well, and swallowing the pointer press to stop
                    // that also disarmed the button's own Clickable, which
                    // arms on the same event. The transmog tile sits beside
                    // the armour slot for this reason.
                    _row.Add(button);
                    _linkButtons[group] = button;
                }
                // While the quad is on offer there is ONE button: the pair
                // toggles only come back when the quad cannot (an arm ran
                // dry and the quad degraded into pairs). A pair whose slots
                // fire inside another linked group stays hidden too.
                var quadOffered = group != BayLink.QuadGroup
                    && BayLink.IsGrouped(Context, BayLink.QuadGroup)
                    && BaySkillSystem.HasLinkedSkills(BayLink.QuadGroup)
                    && (BayLink.IsLinked(BayLink.QuadGroup)
                        || BayLink.CanToggle(Context, actorPtr, BayLink.QuadGroup));
                var subsumed = quadOffered
                    || (!BayLink.IsLinked(group)
                        && BayLink.Groups[group].Any(slot =>
                        {
                            var g = BayLink.LinkedGroupOf(slot);
                            return g >= 0 && g != group;
                        }));
                if (!grouped || subsumed)
                {
                    button.style.display = DisplayStyle.None;
                    continue;
                }
                // Out of flow, so it has to be told where its tile ended up.
                // Both live in the row, so the tile's own x IS the offset,
                // and the row reflows whenever a slot hides.
                button.style.left = ButtonLeft(ButtonHost(group));
                var linked = BayLink.IsLinked(group);
                var usable = BayLink.CanToggle(Context, actorPtr, group);
                button.style.display = DisplayStyle.Flex;
                button.SetEnabled(usable);
                var icon = LinkIcon(linked, usable);
                if (icon != null)
                    button.style.backgroundImage = new StyleBackground(icon);
            }
            catch (Exception ex)
            {
                Context.Log.Warn($"bay bar: link button failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    // The icons carry the full vanilla skill-icon treatment themselves
    // (opaque #422109 ground, one-pixel gold frame, #FFD67E glyph, with the
    // disabled variants using vanilla's inactive grey mapping), so the button
    // is nothing but the icon. The glyph is the state: chain when linked,
    // slashed chain when not.
    private readonly Dictionary<string, UnityEngine.Texture2D> _linkIcons = new(StringComparer.Ordinal);

    // Resting the icon slightly dim is what leaves headroom for a hover
    // state on an opaque image: enter lifts the tint to pure white.
    private static readonly UnityEngine.Color GlyphRestTint = new(0.86f, 0.86f, 0.86f, 1f);

    private UnityEngine.Texture2D LinkIcon(bool linked, bool usable)
    {
        var name = $"bay_link{(linked ? "" : "_slash")}{(usable ? "" : "_disabled")}";
        if (!_linkIcons.TryGetValue(name, out var icon) || icon == null)
            _linkIcons[name] = icon = Context.Assets.Load<UnityEngine.Texture2D>(name);
        return icon;
    }

    private Button BuildLinkButton(int group)
    {
        var button = new Button { name = $"wmgfl-bay-link-{group}", focusable = false };
        button.text = "";
        button.style.position = Position.Absolute;
        button.style.bottom = Length.Percent(100);
        button.style.left = 0f;
        // The tile's own skill buttons overhang its top edge by about 24px
        // (the same overhang the row itself needed clearance for), so a 3px
        // offset put this straight on top of them.
        button.style.marginBottom = 26f;
        button.style.width = 18f;
        button.style.height = 18f;
        // The icon is the whole look, frame included, so the Button's own
        // chrome is stripped to nothing.
        button.style.backgroundColor = new StyleColor(UnityEngine.Color.clear);
        button.style.borderTopWidth = 0f;
        button.style.borderBottomWidth = 0f;
        button.style.borderLeftWidth = 0f;
        button.style.borderRightWidth = 0f;
        button.style.borderTopLeftRadius = 0f;
        button.style.borderTopRightRadius = 0f;
        button.style.borderBottomLeftRadius = 0f;
        button.style.borderBottomRightRadius = 0f;
        button.style.paddingTop = 0f;
        button.style.paddingBottom = 0f;
        button.style.paddingLeft = 0f;
        button.style.paddingRight = 0f;
        button.style.unityBackgroundImageTintColor = new StyleColor(GlyphRestTint);
        button.RegisterCallback(
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<EventCallback<MouseEnterEvent>>(
                (Action<MouseEnterEvent>)(_ =>
                {
                    if (button.enabledSelf)
                        button.style.unityBackgroundImageTintColor = new StyleColor(UnityEngine.Color.white);
                })));
        button.RegisterCallback(
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<EventCallback<MouseLeaveEvent>>(
                (Action<MouseLeaveEvent>)(_ =>
                    button.style.unityBackgroundImageTintColor = new StyleColor(GlyphRestTint))));
        button.clickable.clicked += (Action)(() => OnLinkClicked(group));
        return button;
    }

    private void OnLinkClicked(int group)
    {
        try
        {
            var active = TacticalManager.Get()?.GetActiveActor();
            if (active == null || !BayLink.CanToggle(Context, active.Pointer, group))
                return;
            var linked = !BayLink.IsLinked(group);
            BayLink.SetLinked(group, linked);
            Jiangyu.Game.Audio.Sound.Click();
            BaySkillSystem.SyncLinkedUses(active.Pointer);
            // Redraw now. Nothing else asks the bar to rebuild on its own, so
            // without this the row keeps the tiles it had until something
            // unrelated refreshes it, which is why re-selecting the unit was
            // needed to see the toggle take effect.
            _bar?.UpdateSkills();
            Context.Log.Debug($"bay: group {group} link {(linked ? "on" : "off")}");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay bar: link toggle failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Re-run the bar's hotkey assignment with the bay slots' buttons in
    // their final state: the vanilla pass ran before this postfix set the
    // items, so without it every bay button keeps the stale special-weapon
    // key. The fold is the vanilla algorithm (each element consumes item
    // keys and returns the continuation), so re-running it re-assigns the
    // vanilla buttons identically before threading into the bay's.

    private void AssignKeyBinds(SkillBar bar)
    {
        try
        {
            var next = Il2CppMenace.PlayerSettings.KeyBindPlayerSetting.SelectItemSkill1;
            var all = bar.m_AllElements;
            for (var i = 0; all != null && i < all.Count; i++)
            {
                var element = all[i];
                if (element != null)
                    next = element.UpdateKeyBinds(next);
            }
            if (!_inBarList)
                foreach (var slot in _slots)
                    if (slot != null)
                        next = slot.UpdateKeyBinds(next);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay bar keybinds failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnBarActiveSkill(PatchInfo info)
    {
        if (_slots == null || _inBarList)
            return;
        var skill = (info.Args is { Count: > 0 } args ? args[0] as Il2CppObjectBase : null)?.TryCast<Skill>();
        foreach (var slot in _slots)
            try
            {
                slot?.OnSelectedSkillChanged(skill);
            }
            catch
            {
                // a slot the screen already tore down
            }
    }

    private void OnBarTick(PatchInfo info)
    {
        // The row's layout settles a frame after the rebuild, so the buttons
        // are re-anchored on the tick as well as on the rebuild, while the
        // row is showing.
        try
        {
            if (_rowShown && _row != null && _slots != null)
                foreach (var (group, button) in _linkButtons)
                {
                    var host = ButtonHost(group);
                    if (button == null || host == null)
                        continue;
                    var left = ButtonLeft(host);
                    if (button.style.left != left)
                        button.style.left = left;
                }
        }
        catch
        {
            // a row mid-rebuild re-anchors on the next tick
        }

        if (_slots == null || _inBarList)
            return;
        var delta = info.Args is { Count: > 0 } args && args[0] is float f ? f : 0f;
        foreach (var slot in _slots)
            try
            {
                slot?.Update(delta, false);
            }
            catch
            {
                // a slot the screen already tore down
            }
    }

    private void OnBarPreviewAp(PatchInfo info)
    {
        if (_slots == null || _inBarList)
            return;
        if (info.Args is not { Count: > 0 } args || args[0] is not int ap)
            return;
        foreach (var slot in _slots)
            try
            {
                slot?.OnPreviewApChanged(ap);
            }
            catch
            {
                // a slot the screen already tore down
            }
    }
}
