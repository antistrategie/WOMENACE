using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Il2CppMenace.Tactical.Skills.SkillFilters;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Makes an entity-granted vehicle gun count as a real weapon skill for the vanilla
// ammo and weapon-skill mechanics.
//
// Our vehicles carry their guns on the EntityTemplate's Skills list rather than as
// equipped weapon items (the modular weapon slots are cleared, so there is nothing to
// equip a turret into). Every vanilla mechanic that identifies a vehicle weapon skill
// through its granting item therefore rejects them:
//   - Vehicle Ammo Cases' AmmoPouch requires the granting item's type to be Weapon,
//   - the dropship supply drop refills through an IsItemSkillFilter ("is an item behind
//     this skill"),
//   - Drive By's AP discount and effect consumption gate on an ItemSlotFilter of the
//     vehicle weapon slots.
// (The scavenger drop needs no help: its filter matches tags.)
//
// Scope is the wmgfl_entity_weapon tag, so a vehicle opts its guns in from KDL and no
// skill id lives here. Currently the Sinner's salvo and the mech's gun and rocket.
public sealed class EntityWeaponParitySystem : JiangyuSystem
{
    private const string EntityWeaponTag = "wmgfl_entity_weapon";
    private const string AmmoCasesPassiveId = "passive.ammo_case";

    // Which skill templates carry the tag, memoised by template pointer. These hooks run
    // for every skill filter check in the game and a tag walk marshals a managed string
    // per entry, so each template is judged once. Il2cpp's GC does not move objects, so a
    // template pointer is a stable key for as long as the templates live, and
    // OnTemplatesApplied drops the map whenever they are rebuilt.
    private readonly Dictionary<IntPtr, bool> _tagged = new();

    // Which (skill, pouch) pairs have already had their bonus applied. Keyed by the PAIR,
    // not by the skill: a vehicle with two ammo cases carries two AmmoPouch handlers and
    // each one owes the skill its own bonus, exactly as it would for an item-backed gun.
    // Pointer keys are bounded by the scene, which clears both maps.
    private readonly HashSet<(IntPtr Skill, IntPtr Pouch)> _boosted = new();

    // The dropship supply drop is the only vanilla consumer of IsItemSkillFilter, and it
    // asks from inside Actor.RefillAmmo, so answering for it is scoped to that call
    // rather than to every consumer of a general predicate.
    private bool _inRefill;

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.SkillFilters.ItemSlotFilter", "Matches", OnItemSlotFilterMatches);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.SkillFilters.IsItemSkillFilter", "Matches", OnItemSkillFilterMatches);
        Context.Patches.Prefix("Il2CppMenace.Tactical.Actor", "RefillAmmo", 3, OnRefillStart);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Actor", "RefillAmmo", 3, OnRefillEnd);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Effects.AmmoPouchHandler", "OnMissionStarted", OnAmmoPouchMissionStarted);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Effects.AmmoPouchHandler", "OnAnySkillAdded", OnAmmoPouchSkillAdded);
    }

    // A template rebuild invalidates every cached pointer, so the verdicts are re-derived
    // on the next lookup rather than left keyed on freed templates, which would silently
    // stop matching our guns.
    public override void OnTemplatesApplied()
    {
        _tagged.Clear();
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        ResetSceneState();
    }

    public override void OnUnload()
    {
        ResetSceneState();
    }

    private void ResetSceneState()
    {
        _boosted.Clear();
        _inRefill = false;
    }

    // Whether a skill is one of our entity-granted vehicle guns. Shared by the filter
    // overrides and the ammo pouch mirror so they cannot disagree.
    private bool IsEntityWeapon(Skill skill)
    {
        try
        {
            var template = skill?.GetTemplate();
            if (template == null)
                return false;
            if (_tagged.TryGetValue(template.Pointer, out var cached))
                return cached;
            var tagged = false;
            var tags = template.Tags;
            for (var i = 0; !tagged && tags != null && i < tags.Count; i++)
                tagged = tags[i]?.name == EntityWeaponTag;
            _tagged[template.Pointer] = tagged;
            return tagged;
        }
        catch { return false; }
    }

    // Our guns pass an ItemSlotFilter whenever the filter targets the ModularVehicleLight
    // slot, exactly as if a turret item still backed them. Filters aimed at other slots
    // (infantry ammo bags, heavy turret perks) stay rejected.
    private void OnItemSlotFilterMatches(PatchInfo info)
    {
        try
        {
            if (info.Result is true)
                return;
            var skill = (info.Args is { Count: > 0 } ? info.Args[0] : null) as Skill;
            if (!IsEntityWeapon(skill))
                return;
            var slots = (info.Instance as Il2CppObjectBase)?.TryCast<ItemSlotFilter>()?.ItemSlots;
            if (slots == null)
                return;
            foreach (var slot in slots)
            {
                if (slot != ItemSlot.ModularVehicleLight)
                    continue;
                info.Result = true;
                return;
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"entity weapon: item-slot filter postfix failed: {ex.Message}");
        }
    }

    private void OnRefillStart(PatchInfo info) => _inRefill = true;

    private void OnRefillEnd(PatchInfo info) => _inRefill = false;

    // IsItemSkillFilter is a general "is an item behind this skill" predicate, so
    // answering yes everywhere would reach consumers that have nothing to do with
    // resupply. Confined to the refill call the supply drop asks from.
    private void OnItemSkillFilterMatches(PatchInfo info)
    {
        try
        {
            if (!_inRefill || info.Result is true)
                return;
            var skill = (info.Args is { Count: > 0 } ? info.Args[0] : null) as Skill;
            if (IsEntityWeapon(skill))
                info.Result = true;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"entity weapon: item-skill filter postfix failed: {ex.Message}");
        }
    }

    private static bool IsAmmoCases(AmmoPouchHandler handler)
        => handler?.ParentSkill?.GetTemplate()?.GetID() == AmmoCasesPassiveId;

    // The mission-start sweep: this pouch owes a bonus to every tagged gun on the entity
    // carrying it. All of them, not the first one found, so the mech's gun and rocket are
    // both raised.
    private void OnAmmoPouchMissionStarted(PatchInfo info)
    {
        try
        {
            var handler = (info.Instance as Il2CppObjectBase)?.TryCast<AmmoPouchHandler>();
            if (!IsAmmoCases(handler))
                return;
            var skills = handler.GetEntity()?.GetSkills()?.GetAllSkills();
            if (skills == null)
                return;
            for (var i = 0; i < skills.Count; i++)
            {
                var skill = skills[i]?.TryCast<Skill>();
                if (IsEntityWeapon(skill))
                    BoostUses(skill, handler);
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"entity weapon: ammo pouch mission-start postfix failed: {ex.Message}");
        }
    }

    private void OnAmmoPouchSkillAdded(PatchInfo info)
    {
        try
        {
            var skill = (info.Args is { Count: > 0 } ? info.Args[0] : null) as Skill;
            if (!IsEntityWeapon(skill))
                return;
            var handler = (info.Instance as Il2CppObjectBase)?.TryCast<AmmoPouchHandler>();
            if (IsAmmoCases(handler))
                BoostUses(skill, handler);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"entity weapon: ammo pouch skill-added postfix failed: {ex.Message}");
        }
    }

    // Defers the arithmetic to the pouch's own GetNewSkillUses rather than restating it:
    // that method truncates where a re-derivation is tempted to round, it enforces the
    // pouch's own gates (limited uses, ApplyToType, SkillFilter), and it reads the CURRENT
    // max, so a second pouch compounds off the first one's result the same way it would on
    // an item-backed gun. The item type it is handed is Weapon, matching how the game
    // would present a real vehicle turret.
    //
    // The (skill, pouch) ledger is the only idempotence guard, and it is claimed only once
    // the bonus actually lands: a sweep that arrives before the skill exists leaves the
    // later skill-added hook free to retry, and both hooks firing for the same pair apply
    // the bonus once.
    private void BoostUses(Skill skill, AmmoPouchHandler handler)
    {
        var pair = (skill.Pointer, handler.Pointer);
        if (_boosted.Contains(pair))
            return;
        var template = skill.GetTemplate();
        if (handler.m_Template == null || template == null)
            return;
        var max = skill.GetMaxUses();
        if (max <= 0)
            return;

        var raised = handler.GetNewSkillUses(max, template, new Il2CppSystem.Nullable<ItemType>(ItemType.Weapon));
        var bonus = raised - max;
        if (bonus <= 0)
            return;
        _boosted.Add(pair);
        skill.SetMaxUses(raised);
        skill.SetUses(skill.GetUses() + bonus);
        Context.Log.Debug($"entity weapon: ammo cases raised '{template.GetID()}' uses by {bonus} to {raised}");
    }
}
