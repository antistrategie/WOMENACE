using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.UI;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Mirrors chosen status effects into the overhead unit HUD's icon row
// (UnitHUD.AddIcon), one icon per live instance, so a stacking effect reads
// as a count above the unit's health bar. The native ShowUnitHUDIcon handler
// cannot do this: it wires itself up at OnMissionStarted only, so an effect
// applied mid-mission never registers an icon.
//
// The same row carries the elemental build-up gauges (ElementsSystem): a
// greyscale element icon filling with colour from the bottom, replaced by
// the full-colour effect icon once the element procs.
//
// The mirror keys off SkillContainer mutations. `Remove` is overloaded and
// therefore unpatchable by name, but natural expiry sweeps go through
// RemoveSkillByIndex, and the mod's own template-based strips call Resync
// directly.
public sealed class EffectHudIconSystem : JiangyuSystem
{
    // effect template ids whose presence shows as overhead icons, on top of
    // the five element effects (ElementsSystem.EffectIds). Enemy marks only:
    // friendly units have the unit window for their effects.
    private static readonly string[] TrackedEffectIds =
    {
        "effect.sextans_blood_kiss",
    };

    private static EffectHudIconSystem _instance;

    private readonly List<SkillTemplate> _tracked = new();

    // Actors we have drawn an icon row for. Lets an irrelevant container
    // mutation (the common case: every unit's cooldowns and buffs game-wide)
    // skip the HUD-list scan when the actor carries no tracked effect and
    // has no row left to clear.
    private readonly HashSet<System.IntPtr> _iconized = new();

    public override void OnInit()
    {
        _instance = this;
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.SkillContainer", "Add", OnContainerChanged);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.SkillContainer", "RemoveByID", OnContainerChanged);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.SkillContainer", "RemoveSkillByIndex", OnContainerChanged);
        // Overhead HUDs are pooled and rebound between units: rebuild the row
        // when a HUD is (re)bound so it never shows the previous occupant's
        // icons (a container mutation on the new occupant would not fire if
        // its skills do not change).
        Context.Patches.Postfix("Il2CppMenace.UI.Tactical.UnitHUD", "SetActor", OnHudRebound);
        Context.Patches.Postfix("Il2CppMenace.UI.Tactical.EntityHUD", "InitBars", OnHudReinit);
    }

    public override void OnTemplatesApplied()
    {
        _tracked.Clear();
        var ids = new List<string>(TrackedEffectIds);
        ids.AddRange(ElementsSystem.EffectIds);
        foreach (var id in ids)
        {
            var template = Templates.ById<SkillTemplate>(id, msg => Context.Log.Warn($"effect icons: {msg}"));
            if (template == null)
                continue;
            if (template.Icon == null)
            {
                Context.Log.Warn($"effect icons: '{id}' has no icon sprite, skipped");
                continue;
            }
            _tracked.Add(template);
        }
    }

    internal static void Resync(Actor actor)
        => _instance?.SyncIcons(actor);

    private void OnContainerChanged(PatchInfo info)
    {
        try
        {
            if (info.Instance is not SkillContainer container)
                return;
            SyncIcons(container.m_Owner?.TryCast<Actor>());
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"effect icons: sync failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnHudRebound(PatchInfo info)
    {
        try
        {
            var hud = (info.Instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<Il2CppMenace.UI.Tactical.UnitHUD>();
            var actor = (info.Args is { Count: > 0 } ? info.Args[0] : null) as Actor;
            if (hud != null)
                DrawRow(hud, actor);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"effect icons: hud rebind failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnHudReinit(PatchInfo info)
    {
        try
        {
            var hud = (info.Instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.TryCast<Il2CppMenace.UI.Tactical.UnitHUD>();
            var actor = ((info.Args is { Count: > 0 } ? info.Args[0] : null) as Entity)?.TryCast<Actor>();
            if (hud != null)
                DrawRow(hud, actor);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"effect icons: hud reinit failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Our own icon row, injected as a sibling right below the hitpoints bar
    // (the native AddIcon row sits next to the unit badge instead).
    private const string IconRowName = "wm-effect-icons";

    // Container-mutation entry: resolve the actor's HUD, then draw. The
    // early-out keeps an irrelevant actor (no tracked effect, no row we ever
    // drew) from paying the HUD-list scan.
    private void SyncIcons(Actor actor)
    {
        if (actor == null || _tracked.Count == 0)
            return;
        if (CountTracked(actor) == 0 && !HasGauges(actor) && !_iconized.Contains(actor.Pointer))
            return;
        // no overhead HUD is normal: units off-screen or not yet detected
        // have none, and the icons re-sync when one next appears
        var hud = FindHud(actor);
        if (hud == null)
        {
            _iconized.Remove(actor.Pointer);
            return;
        }
        DrawRow(hud, actor);
    }

    private int CountTracked(Actor actor)
    {
        var total = 0;
        foreach (var template in _tracked)
            total += CountLive(actor, template);
        return total;
    }

    // Rebuilds the icon row on a known HUD for its current occupant (or
    // clears it when the occupant carries none / the HUD was rebound to
    // nobody). The one place icons are drawn, shared by the container-mutation
    // and HUD-rebind paths.
    private void DrawRow(Il2CppMenace.UI.Tactical.UnitHUD hud, Actor actor)
    {
        var bar = hud.m_HitpointsBar;
        var host = bar?.parent;
        if (host == null)
            return;

        VisualElement row = null;
        for (var i = 0; i < host.childCount; i++)
        {
            var child = host.ElementAt(i);
            if (child != null && child.name == IconRowName)
            {
                row = child;
                break;
            }
        }

        var total = 0;
        var icons = new List<VisualElement>();
        foreach (var template in _tracked)
        {
            var count = actor != null ? CountLive(actor, template) : 0;
            for (var i = 0; i < count; i++)
            {
                var icon = new VisualElement();
                icon.style.width = new StyleLength(10f);
                icon.style.height = new StyleLength(10f);
                icon.style.backgroundImage = new StyleBackground(Background.FromSprite(template.Icon));
                icons.Add(icon);
            }
            total += count;
        }
        total += AppendGaugeIcons(actor, icons);

        if (total == 0)
        {
            // clear any row the previous occupant left; leave the empty
            // element in place for reuse
            row?.Clear();
            if (actor != null)
                _iconized.Remove(actor.Pointer);
            return;
        }

        if (row == null)
        {
            row = new VisualElement { name = IconRowName };
            row.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row);
            row.style.justifyContent = new StyleEnum<Justify>(Justify.FlexStart);
            // the host centres its children, so the shrink-wrapped row must
            // pin itself to the left edge
            row.style.alignSelf = new StyleEnum<Align>(Align.FlexStart);
            row.style.marginTop = new StyleLength(2f);
            host.Insert(host.IndexOf(bar) + 1, row);
        }
        row.Clear();
        foreach (var icon in icons)
            row.Add(icon);
        _iconized.Add(actor.Pointer);
    }

    private static bool HasGauges(Actor actor)
    {
        var gauges = ElementsSystem.GaugesFor(actor);
        for (var i = 0; gauges != null && i < gauges.Length; i++)
            if (gauges[i] > 0f)
                return true;
        return false;
    }

    // The elemental build-up gauges: a greyscale element icon whose colour
    // version rises from the bottom as the gauge fills. Built as a clip
    // reveal, the colour layer bottom-anchored inside a bottom-anchored
    // clipping band whose height is the fill percentage.
    private static int AppendGaugeIcons(Actor actor, List<VisualElement> icons)
    {
        var gauges = actor != null ? ElementsSystem.GaugesFor(actor) : null;
        if (gauges == null)
            return 0;
        var added = 0;
        for (var element = 0; element < gauges.Length; element++)
        {
            if (gauges[element] <= 0f || ElementsSystem.HasLiveEffect(actor, element))
                continue;
            var grey = ElementsSystem.GaugeTexture(element);
            var colour = ElementsSystem.EffectSprite(element);
            if (grey == null || colour == null)
                continue;

            var icon = new VisualElement();
            icon.style.width = new StyleLength(10f);
            icon.style.height = new StyleLength(10f);
            icon.style.backgroundImage = new StyleBackground(grey);

            var pct = Mathf.Clamp01(gauges[element] / ElementsSystem.Threshold) * 100f;
            var clip = new VisualElement();
            clip.style.position = new StyleEnum<Position>(Position.Absolute);
            clip.style.left = new StyleLength(0f);
            clip.style.right = new StyleLength(0f);
            clip.style.bottom = new StyleLength(0f);
            clip.style.height = new StyleLength(new Length(pct, LengthUnit.Percent));
            clip.style.overflow = new StyleEnum<Overflow>(Overflow.Hidden);

            var fill = new VisualElement();
            fill.style.position = new StyleEnum<Position>(Position.Absolute);
            fill.style.left = new StyleLength(0f);
            fill.style.bottom = new StyleLength(0f);
            fill.style.width = new StyleLength(10f);
            fill.style.height = new StyleLength(10f);
            fill.style.backgroundImage = new StyleBackground(Background.FromSprite(colour));

            clip.Add(fill);
            icon.Add(clip);
            icons.Add(icon);
            added++;
        }
        return added;
    }

    // Live instances, including ones still sitting in the container's add
    // queue: the Add postfix fires before the queue is drained.
    private static int CountLive(Actor actor, SkillTemplate template)
        => SkillEffects.CountInstances(actor.GetSkills(), template);

    internal static Il2CppMenace.UI.Tactical.UnitHUD FindHud(Actor actor)
    {
        var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<UITactical>();
        var hudList = screen?.GetHUD()?.m_HUDList;
        for (var i = 0; hudList != null && i < hudList.Count; i++)
        {
            var unitHud = hudList[i]?.TryCast<Il2CppMenace.UI.Tactical.UnitHUD>();
            if (unitHud == null)
                continue;
            var owner = unitHud.GetActor();
            if (owner != null && owner.Pointer == actor.Pointer)
                return unitHud;
        }
        return null;
    }
}
