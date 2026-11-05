using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Pistol Mastery, the handgun Dolls' perk: an attack made with a handgun (a weapon of the
// handgun class, WeaponClasses) gains critical chance, damage and armour penetration. It applies
// in OnBeforeAnySkillUsed, the window each attack's properties are built in, so the bonus reaches
// the hit chance preview and the shot and nothing else.
[JiangyuType("PistolMastery")]
public sealed partial class PistolMastery : SkillEventHandlerTemplate
{
    public int CriticalChanceBonus = 30;
    public float DamageMult = 1.5f;
    public float ArmorPenetrationMult = 2f;

    public override SkillEventHandler Create() => new PistolMasteryHandler
    {
        CriticalChanceBonus = CriticalChanceBonus,
        DamageMult = DamageMult,
        ArmorPenetrationMult = ArmorPenetrationMult,
    };
}

[JiangyuType("PistolMasteryHandler")]
public sealed partial class PistolMasteryHandler : SkillEventHandler
{
    public int CriticalChanceBonus = 30;
    public float DamageMult = 1.5f;
    public float ArmorPenetrationMult = 2f;

    public override void OnBeforeAnySkillUsed(Skill _skill, Tile _fromTile, Tile _targetTile, EntityProperties _properties, Entity _overrideTargetEntity)
    {
        try
        {
            if (_skill == null || _properties == null || !_skill.IsAttack())
                return;
            var weapon = _skill.GetItem()?.GetTemplate()?.TryCast<WeaponTemplate>();
            if (weapon == null || WeaponClasses.Classify(weapon) != WeaponClass.Handgun)
                return;
            _properties.CriticalChance += CriticalChanceBonus;
            _properties.DamageMult *= DamageMult;
            _properties.ArmorPenetrationMult *= ArmorPenetrationMult;
        }
        catch (Exception ex)
        {
            Log.Warn($"pistol mastery: pre-attack failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
