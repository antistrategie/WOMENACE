using System.Collections;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Extends the magazine pouches accessory to OTs-14's bay weapons. The
// vanilla AmmoPouchHandler identifies a weapon skill through its granting
// item's slot, which a bay-granted skill fails (the bay items sit in the
// special slot and the fire skills are container grants), so the pouch
// never raises them. The handler's arithmetic is restated here rather than
// deferred to GetNewSkillUses, whose internal skill filter is exactly the
// gate that rejects bay skills: bonus = truncate(max * BonusPercentage),
// floored at MinimumBonus, matching the pouch's own maths for a rifle.
//
// Two boost paths, both deduped by (skill, pouch) pair:
//  - the sweep BaySkillSystem invokes right after its grant registers, the
//    authoritative one (the vanilla OnAnySkillAdded event fires INSIDE
//    container.Add, before the bay registry entry exists, so a same-frame
//    listener cannot recognise a bay skill);
//  - the deferred OnAnySkillAdded postfix, kept for a pouch whose handler
//    appears after the grant.
public sealed class Ots14BayAmmoSystem : JiangyuSystem
{
    private const string MagazinePouchesId = "passive.magazine_pouches";

    internal static Ots14BayAmmoSystem Instance { get; private set; }

    // (skill, pouch) pairs already raised, so a re-fired sweep cannot stack.
    // Two pouches each owe their own bonus, hence the pair key. Scene-scoped.
    private readonly HashSet<(IntPtr Skill, IntPtr Pouch)> _boosted = new();

    public override void OnInit()
    {
        Instance = this;
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Effects.AmmoPouchHandler", "OnMissionStarted", OnPouchMissionStarted);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Effects.AmmoPouchHandler", "OnAnySkillAdded", OnPouchSkillAdded);
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        _boosted.Clear();
    }

    // BaySkillSystem calls this once its grants are in the registry: find
    // every magazine-pouch handler on the container and pay out for every
    // bay-granted skill it holds.
    internal static void SweepGranted(SkillContainer container)
    {
        try
        {
            Instance?.Sweep(container);
        }
        catch (Exception ex)
        {
            Instance?.Context.Log.Warn($"bay ammo: grant sweep failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Sweep(SkillContainer container)
    {
        if (container == null)
            return;
        var handlers = new Il2CppSystem.Collections.Generic.List<AmmoPouchHandler>();
        container.CollectHandlersOfType(handlers);
        for (var h = 0; h < handlers.Count; h++)
        {
            var handler = handlers[h];
            if (!IsMagazinePouches(handler))
                continue;
            var skills = container.GetAllSkills();
            for (var i = 0; skills != null && i < skills.Count; i++)
            {
                var skill = skills[i]?.TryCast<Skill>();
                if (skill != null && BaySkillSystem.TryGetGranted(skill.Pointer, out _))
                    BoostUses(skill, handler);
            }
        }
    }

    private static bool IsMagazinePouches(AmmoPouchHandler handler)
        => handler?.ParentSkill?.GetTemplate()?.GetID() == MagazinePouchesId;

    // Mission start: raise every bay skill already granted on the pouch's
    // entity. Usually a no-op (the deferred grant lands after this sweep),
    // but it covers a pouch equipped on an already-granted actor.
    private void OnPouchMissionStarted(PatchInfo info)
    {
        try
        {
            var handler = (info.Instance as Il2CppObjectBase)?.TryCast<AmmoPouchHandler>();
            if (!IsMagazinePouches(handler))
                return;
            var skills = handler.GetEntity()?.GetSkills()?.GetAllSkills();
            for (var i = 0; skills != null && i < skills.Count; i++)
            {
                var skill = skills[i]?.TryCast<Skill>();
                if (skill != null && BaySkillSystem.TryGetGranted(skill.Pointer, out _))
                    BoostUses(skill, handler);
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay ammo: pouch mission-start postfix failed: {ex.Message}");
        }
    }

    // The add event fires inside container.Add, before BaySkillSystem's
    // registry write, so the bay check must wait a frame.
    private void OnPouchSkillAdded(PatchInfo info)
    {
        try
        {
            // The handler declares its parameter as BaseSkill, so a managed
            // `as Skill` against the interop wrapper yields null and the whole
            // deferred path never runs. Unwrap the way every sibling does.
            var skill = (info.Args is { Count: > 0 } ? info.Args[0] as Il2CppObjectBase : null)?.TryCast<Skill>();
            var handler = (info.Instance as Il2CppObjectBase)?.TryCast<AmmoPouchHandler>();
            if (skill == null || !IsMagazinePouches(handler))
                return;
            Context.Coroutines.Start(BoostWhenRegistered(skill, handler));
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay ammo: pouch skill-added postfix failed: {ex.Message}");
        }
    }

    private IEnumerator BoostWhenRegistered(Skill skill, AmmoPouchHandler handler)
    {
        yield return null;
        try
        {
            if (BaySkillSystem.TryGetGranted(skill.Pointer, out _))
                BoostUses(skill, handler);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay ammo: deferred boost failed: {ex.Message}");
        }
    }

    private void BoostUses(Skill skill, AmmoPouchHandler handler)
    {
        var pair = (skill.Pointer, handler.Pointer);
        if (_boosted.Contains(pair))
            return;
        var pouch = handler.m_Template;
        if (pouch == null || !skill.HasLimitedUses())
            return;
        // Derived from the template's AUTHORED count, never the live max: the
        // live value already carries any earlier boost (a skill surviving a
        // mid-mission save/reload arrives boosted while _boosted is empty),
        // and recomputing off it compounded the bonus with a free refund.
        var authored = skill.GetTemplate()?.Uses ?? 0;
        if (authored <= 0)
            return;
        var bonus = (int)(authored * pouch.BonusPercentage);
        if (bonus < pouch.MinimumBonus)
            bonus = pouch.MinimumBonus;
        if (bonus <= 0)
            return;
        _boosted.Add(pair);
        var target = authored + bonus;
        var max = skill.GetMaxUses();
        if (max >= target)
            return; // already boosted, nothing owed
        skill.SetMaxUses(target);
        skill.SetUses(skill.GetUses() + (target - max));
        Context.Log.Debug($"bay ammo: magazine pouches raised '{skill.GetTemplate()?.GetID()}' uses by {target - max} to {target}");
    }
}
