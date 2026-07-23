using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Jiangyu.Game.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Blades (Sextans' Night Snake and Twilight Rose) deal their per-hit stats from the skill Attack
// handlers, not the weapon template, and the weapon tooltip SUMS weapon.Damage with the granted
// skill's Attack.Damage, so any non-zero weapon field would double the number the player sees. The
// blade weapon fields therefore stay 0 (see weapon.kdl) and calibration is driven entirely here: a
// per-rank multiplier scales the blade skills' Attack handlers (hitpoint damage, armour penetration
// and armour-durability damage). Because the tooltip reads the (scaled) handler, that same multiplier
// shows up in the weapon tooltip; the calibration screen reads it through BladeDeltas. One curve, one
// set of numbers everywhere.
public sealed class BladeCalibrationSystem : JiangyuSystem
{
    public static BladeCalibrationSystem Instance { get; private set; }

    // Per-rank multiplier on the blade's Attack-handler stats, indexed by rank 0..MaxRank. Roughly
    // +10% compounding per rank (r6 ~1.77x). This is the one tunable knob for blade calibration.
    private static readonly float[] RankScale = { 1f, 1.10f, 1.21f, 1.33f, 1.46f, 1.61f, 1.77f };

    // Each blade weapon and the skills its rank scales. Skills[0] is the primary attack (the one the
    // calibration screen and weapon tooltip show). The ult and its riders are shared by both swords
    // (only Sextans wields either), so they list under both; scaling the whole kit on any blade use
    // keeps the ult's riders (applied later by SextansUltSystem, never through Skill.Use) scaled
    // before their delayed payoff reads them.
    private static readonly (string BaseId, string[] Skills)[] Blades =
    {
        ("weapon.sextans", new[]
        {
            "active.sextans_slash", "active.sextans_thrust",
            "active.sextans_ult", "active.sextans_ult_strike", "active.sextans_ult_rend",
        }),
        ("weapon.sextans_ssr", new[]
        {
            "active.sextans_ssr_slash", "active.sextans_ssr_thrust",
            "active.sextans_ult", "active.sextans_ult_strike", "active.sextans_ult_rend",
        }),
    };

    // Rank-0 Attack-handler stats per blade skill, captured before any scaling so our writes never
    // compound (every rescale multiplies the authored value) and BladeDeltas reports true base values.
    private struct AuthoredAttack
    {
        public float Damage;
        public float ArmorPenetration;
        public float ArmorDamage;
    }

    private readonly Dictionary<string, AuthoredAttack> _authored = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SkillTemplate> _skills = new(StringComparer.Ordinal);

    public override void OnInit()
    {
        Instance = this;
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.Skill", "Use", OnSkillUse);
    }

    public override void OnUnload()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    // Capture every blade skill's authored Attack stats while the templates are pristine (before any
    // combat use could scale them), so BladeDeltas and ScaleKit always work off the true base.
    public override void OnTemplatesApplied()
    {
        foreach (var (_, skills) in Blades)
            foreach (var id in skills)
                Capture(id);
    }

    // The scaled templates are shared, process-global state, so a scale left behind by the last use
    // must be reconciled whenever the truth changes without a Use: scene load covers save switches
    // (the last save's rank would otherwise show in this save's tooltips) and CalibrationSystem calls
    // ReconcileEquipped after a workshop merge or revert.
    public override void OnSceneLoaded(int buildIndex, string sceneName) => ReconcileEquipped();

    private void OnSkillUse(PatchInfo info)
    {
        try
        {
            var skill = (info.Instance as Il2CppSystem.Object)?.TryCast<Skill>();
            var weaponId = skill?.GetItem()?.GetTemplate()?.GetID();
            if (weaponId == null)
                return;

            foreach (var (baseId, skills) in Blades)
            {
                if (!Calibration.TryParseRank(weaponId, baseId, out var rank))
                    continue;
                ScaleKit(rank, skills);
                return;
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"blade calibration: scale failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Re-scale every blade kit to the rank of the sword its doll actually has equipped right now
    // (rank 0 when unequipped). Shared skills (the ult and its riders appear under both swords) take
    // the highest equipped rank so reconciling the unequipped sword can never reset the equipped one.
    public void ReconcileEquipped()
    {
        try
        {
            var desired = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (baseId, skills) in Blades)
            {
                var rank = EquippedRank(baseId);
                foreach (var id in skills)
                    desired[id] = desired.TryGetValue(id, out var seen) && seen > rank ? seen : rank;
            }
            foreach (var pair in desired)
                ScaleSkill(pair.Key, pair.Value);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"blade calibration: reconcile failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The calibration rank of the equipped instance of a blade across all hired dolls, 0 when nobody
    // has one equipped.
    private static int EquippedRank(string baseId)
    {
        var hired = Leaders.Hired();
        for (var i = 0; hired != null && i < hired.Count; i++)
        {
            var items = Leaders.EquippedItems(hired[i]);
            for (var j = 0; items != null && j < items.Count; j++)
                if (Calibration.TryParseRank(items[j]?.GetTemplate()?.GetID(), baseId, out var rank))
                    return rank;
        }
        return 0;
    }

    // Scale every Attack handler in the kit by the rank multiplier. Rank 0 gives factor 1, which
    // restores the authored values (so swapping to an uncalibrated sword resets it).
    private void ScaleKit(int rank, string[] skills)
    {
        foreach (var id in skills)
            ScaleSkill(id, rank);
    }

    private void ScaleSkill(string id, int rank)
    {
        if (!TryAuthored(id, out var authored))
            return;
        var attack = FirstAttack(Templates.Resolve<SkillTemplate>(id, _skills));
        if (attack == null)
            return;
        var scale = Scale(rank);
        attack.Damage = authored.Damage * scale;
        attack.ArmorPenetration = authored.ArmorPenetration * scale;
        attack.DamageToArmorDurability = authored.ArmorDamage * scale;
    }

    // The calibration screen's stat rows for a blade: the primary attack's real stats at this rank and
    // the next, read from the skill Attack handler (never the inert weapon fields). Null when the id is
    // not a blade, so CalibrationSystem.Deltas falls through to its normal weapon-field path.
    public List<StatDelta> BladeDeltas(string baseWeaponId, int rank)
    {
        foreach (var (baseId, skills) in Blades)
        {
            if (baseId != baseWeaponId)
                continue;
            if (!TryAuthored(skills[0], out var authored))
                return null;
            var now = Scale(rank);
            var next = Scale(rank + 1);
            return new List<StatDelta>
            {
                new() { Name = "DAMAGE", Current = authored.Damage * now, Next = authored.Damage * next },
                new() { Name = "ARMOR PEN", Current = authored.ArmorPenetration * now, Next = authored.ArmorPenetration * next },
                new() { Name = "ARMOR DMG", Current = authored.ArmorDamage * now, Next = authored.ArmorDamage * next },
            };
        }
        return null;
    }

    private void Capture(string skillId) => TryAuthored(skillId, out _);

    // A skill's authored Attack stats, captured once on first sight. False if the skill or its Attack
    // handler is not resolvable yet.
    private bool TryAuthored(string skillId, out AuthoredAttack authored)
    {
        if (_authored.TryGetValue(skillId, out authored))
            return true;
        var attack = FirstAttack(Templates.Resolve<SkillTemplate>(skillId, _skills));
        if (attack == null)
            return false;
        authored = new AuthoredAttack
        {
            Damage = attack.Damage,
            ArmorPenetration = attack.ArmorPenetration,
            ArmorDamage = attack.DamageToArmorDurability,
        };
        _authored[skillId] = authored;
        return true;
    }

    private static float Scale(int rank)
        => RankScale[rank < 0 ? 0 : rank >= RankScale.Length ? RankScale.Length - 1 : rank];

    private static Attack FirstAttack(SkillTemplate skill)
    {
        var handlers = skill?.EventHandlers;
        for (var i = 0; handlers != null && i < handlers.Count; i++)
        {
            var attack = handlers[i]?.TryCast<Attack>();
            if (attack != null)
                return attack;
        }
        return null;
    }
}
