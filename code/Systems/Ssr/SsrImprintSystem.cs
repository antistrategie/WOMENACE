using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.UI;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// General SSR-weapon "Imprint Boost" system. Each SSR weapon is a special weapon anyone can equip
// (unlocked at its owner doll's affinity Lv3 via Unlocks/AffinitySystem). While the owning doll
// wields it she gets per-skill combat bonuses, and the weapon tooltip shows a "<Doll> Imprint Boost"
// section (highlighted green for the owner, greyed for everyone else).
//
// Adding another doll's SSR weapon is one Entry below. Shot-count and on-hit bonuses are PER-SKILL
// (a weapon may grant several skills); damage lives on the weapon (a single field) so it is per-weapon.
public sealed class SsrImprintSystem : JiangyuSystem
{
    // Per-skill owner bonus. Repetitions (the skill's shot count) is a skill field, so it belongs here.
    public sealed class SkillImprint
    {
        public string SkillId;
        public int OwnerRepetitions;      // owner's Repetitions for this skill (0 = never change it). The
                                          // base comes from the skill template's authored Repetitions (KDL).
        public float OwnerElementalMult;  // owner's multiplier on the skill's elemental build-up per hit
                                          // (0 = no boost). Read by ElementalDamageHandler at hit time.
    }

    public sealed class Entry
    {
        public string OwnerTag;
        public string OwnerName;
        public string BonusText;
        // The SSR weapon's template id, derived from the owner tag by the Calibration convention
        // (weapon.<doll>_ssr), so the id is never hand-authored in two places.
        public string WeaponId => Calibration.SsrWeaponIdFor(OwnerTag);
        public int OwnerDamage;         // owner's boosted per-shot damage (0 = never change Damage). The
                                        // base is read from the weapon template's authored Damage (KDL),
                                        // so only this owner-only override lives in code. Damage is a
                                        // single weapon field, hence per-weapon, not per-skill.
        public SkillImprint[] Skills = Array.Empty<SkillImprint>();
    }

    private static readonly List<Entry> Registry = new()
    {
        new Entry
        {
            OwnerTag = "wmgfl_makiatto",
            OwnerName = "Makiatto",
            BonusText = "Fires twice, hits harder, and builds Freeze faster.",
            OwnerDamage = 30,
            Skills = new[]
            {
                new SkillImprint { SkillId = "active.makiatto_ssr_freeze", OwnerRepetitions = 2, OwnerElementalMult = 1.5f },
            },
        },
        new Entry
        {
            OwnerTag = "wmgfl_soppo",
            OwnerName = "Soppo",
            BonusText = "Hits harder, builds Freeze and Burn faster, and unlocks her stances.",
            OwnerDamage = 15,
            Skills = new[]
            {
                new SkillImprint { SkillId = "active.soppo_ssr_pursuit", OwnerElementalMult = 1.25f },
                new SkillImprint { SkillId = "active.soppo_ssr_bite", OwnerElementalMult = 1.25f },
            },
        },
        new Entry
        {
            // Sextans' SSR sword. Unlike Makiatto/Soppo it is OnlyEquipableBy its owner, so there is no
            // owner-vs-other split to gate. The extra damage lives on the skills' Attack handler and the
            // Shock build-up on their ElementalDamage handler, both always on (see weapon.kdl). This entry
            // carries no owner bonus: the melee hit reads the Attack handler, not the weapon Damage
            // field, so an OwnerDamage boost would never touch it. It exists only for the "Sextans
            // Imprint Boost" tooltip and IsImprintWeapon (SSR-weapon status, out of the proficiency bonus).
            OwnerTag = "wmgfl_sextans",
            OwnerName = "Sextans",
            BonusText = "Builds Shock on every hit.",
        },
    };

    // Match by base id OR any calibration rank of it (base_rN), so a calibrated SSR is still treated as
    // its imprint weapon (tooltip section, proficiency exclusion). The fire-time bonuses key off the
    // skill instead, which every rank inherits, so those need no rank handling.
    private static Entry ByWeapon(string id)
        => id == null ? null : Registry.Find(e => Calibration.TryParseRank(id, e.WeaponId, out _));

    // Whether a weapon is one of the SSR imprint weapons (any rank): an SSR is exactly a doll
    // weapon whose base id carries the _ssr suffix (the Calibration id convention). The single
    // source of truth for "is this an SSR weapon", so other systems (e.g. WeaponProficiencySystem,
    // which excludes SSR from its weapon-type bonus) never keep a second list.
    public static bool IsImprintWeapon(string weaponId)
        => Calibration.TryResolveWeaponId(weaponId, out var baseId, out _)
            && baseId.EndsWith("_ssr", StringComparison.Ordinal);

    private static (Entry entry, SkillImprint skill) BySkill(string id)
    {
        if (id != null)
            foreach (var e in Registry)
                foreach (var s in e.Skills)
                    if (s.SkillId == id)
                        return (e, s);
        return (null, null);
    }

    private readonly Dictionary<string, WeaponTemplate> _weapons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SkillTemplate> _skills = new(StringComparer.Ordinal);

    // The KDL-authored base Damage (per weapon) and Repetitions (per skill), captured on first resolve
    // BEFORE any hook mutates those shared fields. These are the base everyone gets; the owner's boosted
    // values are OwnerDamage / OwnerRepetitions. Captured (not re-read) because our hooks overwrite the
    // live fields at runtime, so re-reading them would pick up the last firer's values.
    private readonly Dictionary<string, float> _authoredDamage = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ushort> _authoredReps = new(StringComparer.Ordinal);

    // The character tag of the unit-window leader the player is viewing. The tooltip gate uses this in
    // the armory/loadout, where the hovered item has no combat wielder to identify (null on a screen
    // with no leader, e.g. the shop catalogue -> greyed section). In a mission the gate instead reads
    // the actual wielder off the item.
    private string _currentLeaderTag;

    public override void OnInit()
    {
        // Owner damage boost: applied per shot to the DamageInfo the game just built from the weapon, so
        // the shared WeaponTemplate.Damage is NEVER mutated and can't leak a stale buffed value to other
        // readers (non-owner loadout panel / damage preview). Also covers the owner's own damage preview,
        // which fills a DamageInfo the same way.
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "FillDamageInfo", OnFillDamageInfo);

        // Owner extra shots: Repetitions has no per-hit lever, so it must be set on the shared skill
        // template before the shot loop reads it. The loop reads it asynchronously after Use returns, so
        // we cannot restore in a postfix (that reset it before the shots read it -> single-shot bug).
        // Every actor's own Use re-sets it first (owner 2 / others base), and the tooltip Pre/Post set
        // then restore it, so those surfaces are correct. The only residual is a non-owner's damage
        // PREVIEW showing the owner's shot count in the brief window after the owner fires and before the
        // non-owner acts; it self-corrects on their next action and never affects damage (that is the
        // DamageInfo boost above). Acceptable for a single signature weapon almost always on its owner.
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.Skill", "Use", OnSkillUsePre);

        // Weapon tooltip: set the shared fields to the VIEWER's values BEFORE the game reads them into
        // stat rows (owner sees boosted, everyone else base), append the Imprint section AFTER, then
        // restore the fields to base so the resting template never carries a buffed value.
        Context.Patches.Prefix("Il2CppMenace.Items.ItemTemplate", "AppendTooltipData", OnItemTooltipPre);
        Context.Patches.Postfix("Il2CppMenace.Items.ItemTemplate", "AppendTooltipData", OnItemTooltipPost);

        // Track the current unit-window leader for the tooltip's highlight-vs-grey gate.
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "SetLeader", OnWindowChanged);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "Refresh", OnWindowChanged);
    }

    private void OnSkillUsePre(PatchInfo info)
    {
        try
        {
            var skill = (info.Instance as Il2CppSystem.Object)?.TryCast<Skill>();
            var (entry, imp) = BySkill(skill?.GetID());
            if (entry == null)
                return;

            if (imp.OwnerRepetitions <= 0)
                return;
            var owner = SkillOwnedBy(skill, entry.OwnerTag);
            var stmpl = ResolveSkill(imp.SkillId);   // resolve (captures authored Repetitions) then override
            if (stmpl != null)
                stmpl.Repetitions = (ushort)(owner ? imp.OwnerRepetitions : BaseRepetitionsOf(imp.SkillId));
        }
        catch (Exception ex) { Context.Log.Warn($"ssr: imprint fire failed: {ex.Message}"); }
    }

    // Add the owner's damage bonus to the DamageInfo the skill just built (per shot), rather than
    // mutating the shared weapon template. Only the owning doll's shots are boosted; everyone else deals
    // the weapon's authored damage untouched.
    private void OnFillDamageInfo(PatchInfo info)
    {
        try
        {
            var skill = (info.Instance as Il2CppSystem.Object)?.TryCast<Skill>();
            var (entry, _) = BySkill(skill?.GetID());
            if (entry == null || entry.OwnerDamage <= 0 || !SkillOwnedBy(skill, entry.OwnerTag))
                return;

            var damageInfo = info.Args != null && info.Args.Count > 0
                ? (info.Args[0] as Il2CppSystem.Object)?.TryCast<DamageInfo>()
                : null;
            if (damageInfo == null)
                return;

            // Flat bonus over the authored base, so crit / dropoff / penetration modifiers already
            // folded into DamageInfo.Damage are preserved.
            var bonus = entry.OwnerDamage - (int)BaseDamageOf(entry.WeaponId);
            if (bonus > 0)
                damageInfo.Damage += bonus;
        }
        catch (Exception ex) { Context.Log.Warn($"ssr: damage boost failed: {ex.Message}"); }
    }

    // The owner's multiplier on a skill's elemental build-up, 1 for everyone else and for skills with
    // no imprint. ElementalDamageHandler calls this per hit.
    public static float ElementalMultiplier(BaseSkill skill)
    {
        var (entry, imp) = BySkill(skill?.GetID());
        if (entry == null || imp == null || imp.OwnerElementalMult <= 0f)
            return 1f;
        return SkillOwnedBy(skill, entry.OwnerTag) ? imp.OwnerElementalMult : 1f;
    }

    // Set the hovered SSR weapon's Damage + its skills' Repetitions to the VIEWER's values before the
    // game reads them into stat rows: the owning doll sees her buffed numbers, everyone else sees base.
    // This also stops the shared template fields from showing whatever the last firer left behind.
    private void OnItemTooltipPre(PatchInfo info)
    {
        try
        {
            var tmpl = (info.Instance as Il2CppSystem.Object)?.TryCast<ItemTemplate>();
            var hoveredId = tmpl?.GetID();
            var entry = ByWeapon(hoveredId);
            if (entry == null)
                return;

            var owned = TooltipOwnedBy(info, entry);

            if (entry.OwnerDamage > 0)
            {
                // The game builds the stat rows from the HOVERED template, which for a calibrated
                // weapon is the rank clone, not the base, so the substitution must land on the
                // hovered id. The owner's boost is the flat bonus over the BASE weapon (exactly what
                // OnFillDamageInfo adds per shot), applied on top of the hovered rank's authored value.
                var w = ResolveWeapon(hoveredId);   // resolve (not the raw cast) so the authored base is captured first
                if (w != null)
                    w.Damage = owned
                        ? BaseDamageOf(hoveredId) + (entry.OwnerDamage - BaseDamageOf(entry.WeaponId))
                        : BaseDamageOf(hoveredId);
            }
            foreach (var s in entry.Skills)
            {
                if (s.OwnerRepetitions <= 0)
                    continue;
                var st = ResolveSkill(s.SkillId);
                if (st != null)
                    st.Repetitions = (ushort)(owned ? s.OwnerRepetitions : BaseRepetitionsOf(s.SkillId));
            }
        }
        catch (Exception ex) { Context.Log.Warn($"ssr: tooltip stats failed: {ex.Message}"); }
    }

    private void OnItemTooltipPost(PatchInfo info)
    {
        try
        {
            var tmpl = (info.Instance as Il2CppSystem.Object)?.TryCast<ItemTemplate>();
            var hoveredId = tmpl?.GetID();
            var entry = ByWeapon(hoveredId);
            if (entry == null)
                return;

            var data = info.Args != null && info.Args.Count > 0
                ? (info.Args[0] as Il2CppSystem.Object)?.TryCast<TooltipData>()
                : null;
            if (data == null)
                return;

            var owned = TooltipOwnedBy(info, entry);

            // Subheading (11px) + a manual bottom border + top margin: the section-divider look at the
            // body text size (AddSectionHeading is oversized; AddSubheading alone drops the divider).
            var heading = data.AddSubheading($"{entry.OwnerName} Imprint Boost", null, NoIconSize, NoIconColour, true);
            heading?.SetBorderBottom(true);
            heading?.SetMarginTop(6);
            var para = data.AddParagraph(
                entry.BonusText, owned ? ParagraphStyle.Positive : ParagraphStyle.Default, null, NoIconSize, NoIconColour, true, false);
            if (!owned)
            {
                var grey = new UnityEngine.Color(0.45f, 0.45f, 0.45f, 1f);
                heading?.SetColor(grey);
                para?.SetColor(grey);
            }

            // The stat rows have been built (between Pre and here), so return the shared fields to base:
            // the resting template must not carry the viewer's buffed values for another reader to pick up.
            ResetToBase(entry, hoveredId);
        }
        catch (Exception ex) { Context.Log.Warn($"ssr: tooltip append failed: {ex.Message}"); }
    }

    // Return the shared weapon/skill fields (that Pre set to the viewer's values for the stat rows) to
    // their authored base: the hovered template (a rank clone when calibrated) back to its own authored
    // Damage. Damage is only ever set transiently for the tooltip now (the fire path boosts DamageInfo
    // instead), so after this the only field a fire can leave raised is Repetitions.
    private void ResetToBase(Entry entry, string hoveredId)
    {
        if (entry.OwnerDamage > 0)
        {
            var w = ResolveWeapon(hoveredId);
            if (w != null)
                w.Damage = BaseDamageOf(hoveredId);
        }
        foreach (var s in entry.Skills)
        {
            if (s.OwnerRepetitions <= 0)
                continue;
            var st = ResolveSkill(s.SkillId);
            if (st != null)
                st.Repetitions = BaseRepetitionsOf(s.SkillId);
        }
    }

    private void OnWindowChanged(PatchInfo info)
    {
        try
        {
            if (info.Instance is VisualElement window)
            {
                var leader = Affinity.LeaderOf(window);
                _currentLeaderTag = leader != null ? Affinity.CharacterTag(leader) : null;
            }
        }
        catch (Exception ex) { Context.Log.Warn($"ssr: window track failed: {ex.Message}"); }
    }

    // Every entity handle reachable from a skill at fire time. Different accessors can surface different
    // objects, so owner detection reads identity (SkillOwnedBy) off each and takes the first that
    // resolves to a doll, rather than betting on one accessor.
    private static Entity[] WielderCandidates(BaseSkill skill)
    {
        if (skill == null)
            return Array.Empty<Entity>();
        return new Entity[]
        {
            (skill.GetItem()?.GetContainer()?.GetOwner() as Il2CppObjectBase)?.TryCast<Entity>(),
            skill.GetEntity(),
            (skill.GetOwner() as Il2CppObjectBase)?.TryCast<Entity>(),
            skill.GetActor(),
        };
    }

    // True if any handle reachable from the skill resolves to the doll whose character tag is `tag`.
    // Identity comes from the entity's SpeakerTemplate (via Affinity.CharacterTag), not its plain tag
    // list: combat entities drop the doll's EntityTemplate tag but keep their speaker.
    private static bool SkillOwnedBy(BaseSkill skill, string tag)
    {
        foreach (var e in WielderCandidates(skill))
            if (e != null && Affinity.CharacterTag(e) == tag)
                return true;
        return false;
    }

    // Whether the viewer of this weapon's tooltip is its owning doll. Shared by the Pre hook (which sets
    // the stat rows to the viewer's numbers) and the Post hook (which highlights the section).
    //
    // The actual entity holding the weapon is authoritative when it exists (in a mission, or an equipped
    // loadout): its identity is trusted outright, so a stale _currentLeaderTag can never override it. We
    // only fall back to the tracked unit-window leader when there is NO combat wielder to read - the
    // armory/loadout, where the item's owner is the (non-Entity) leader we cannot tag off directly.
    private bool TooltipOwnedBy(PatchInfo info, Entry entry)
    {
        var item = info.Args != null && info.Args.Count > 1
            ? (info.Args[1] as Il2CppSystem.Object)?.TryCast<BaseItem>()
            : null;
        var wielder = (item?.GetContainer()?.GetOwner() as Il2CppObjectBase)?.TryCast<Entity>();
        if (wielder != null)
            return Affinity.CharacterTag(wielder) == entry.OwnerTag;
        return _currentLeaderTag != null && _currentLeaderTag == entry.OwnerTag;
    }

    // Resolve + memoise via the shared Templates.Resolve, then capture the authored base ONCE on the
    // first resolve (before any hook overwrites the live field). Callers resolve before mutating, so the
    // captured value is always the KDL-authored base.
    private WeaponTemplate ResolveWeapon(string id)
    {
        var found = Templates.Resolve<WeaponTemplate>(id, _weapons, msg => Context.Log.Warn($"ssr: {msg}"));
        if (found != null && !_authoredDamage.ContainsKey(id))
            _authoredDamage[id] = found.Damage;
        return found;
    }

    private SkillTemplate ResolveSkill(string id)
    {
        var found = Templates.Resolve<SkillTemplate>(id, _skills, msg => Context.Log.Warn($"ssr: {msg}"));
        if (found != null && !_authoredReps.ContainsKey(id))
            _authoredReps[id] = found.Repetitions;
        return found;
    }

    // The KDL-authored base per-shot Damage / Repetitions, captured on first resolve. Callers resolve
    // first (which captures), so these read the captured value, never the field our hooks mutate.
    private float BaseDamageOf(string id)
    {
        ResolveWeapon(id);
        return _authoredDamage.TryGetValue(id, out var d) ? d : 0f;
    }

    private ushort BaseRepetitionsOf(string id)
    {
        ResolveSkill(id);
        return _authoredReps.TryGetValue(id, out var r) ? r : (ushort)1;
    }

    // Boxed-empty Il2Cpp nullable: a C# null default throws in the tooltip's nullable marshalling.
    private static readonly Il2CppSystem.Nullable<int> NoIconSize = new();
    private static readonly Il2CppSystem.Nullable<UnityEngine.Color> NoIconColour = new();
}
