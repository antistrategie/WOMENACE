using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Changes the owner's attacks made with a weapon of one class (WeaponClass, a WeaponClasses code
// such as HG, AR or SMG), or with Invert, with any weapon outside it. Slot, when set, limits it to
// weapons that equip into that slot (InfantryWeapon for the squad weapon, InfantrySpecial for the
// special weapon), so vehicle weapons fall outside either. Attacks that use no weapon
// (grenades and other accessories) are never touched. It applies in OnBeforeAnySkillUsed, the
// window each attack's properties are built in, so the change reaches the hit chance preview and
// the shot and nothing else.
[JiangyuType("WeaponClassBonus")]
public sealed partial class WeaponClassBonus : SkillEventHandlerTemplate
{
    public string WeaponClass = "";
    public bool Invert;
    public ItemSlot Slot = ItemSlot.None;
    public float AccuracyMult = 1f;
    public float DamageMult = 1f;
    public float ArmorPenetrationMult = 1f;
    public int CriticalChanceBonus;

    public override SkillEventHandler Create() => new WeaponClassBonusHandler
    {
        WeaponClass = WeaponClass,
        Invert = Invert,
        Slot = Slot,
        AccuracyMult = AccuracyMult,
        DamageMult = DamageMult,
        ArmorPenetrationMult = ArmorPenetrationMult,
        CriticalChanceBonus = CriticalChanceBonus,
    };
}

[JiangyuType("WeaponClassBonusHandler")]
public sealed partial class WeaponClassBonusHandler : SkillEventHandler
{
    public string WeaponClass = "";
    public bool Invert;
    public ItemSlot Slot = ItemSlot.None;
    public float AccuracyMult = 1f;
    public float DamageMult = 1f;
    public float ArmorPenetrationMult = 1f;
    public int CriticalChanceBonus;

    public override void OnBeforeAnySkillUsed(Skill _skill, Tile _fromTile, Tile _targetTile, EntityProperties _properties, Entity _overrideTargetEntity)
    {
        try
        {
            if (_skill == null || _properties == null || !_skill.IsAttack())
                return;
            var weapon = _skill.GetItem()?.GetTemplate()?.TryCast<WeaponTemplate>();
            if (weapon == null || (Slot != ItemSlot.None && weapon.SlotType != Slot))
                return;
            var inClass = WeaponClasses.Classify(weapon) == WeaponClasses.FromCode(WeaponClass);
            if (inClass == Invert)
                return;
            _properties.AccuracyMult *= AccuracyMult;
            _properties.DamageMult *= DamageMult;
            _properties.ArmorPenetrationMult *= ArmorPenetrationMult;
            _properties.CriticalChance += CriticalChanceBonus;
        }
        catch (Exception ex)
        {
            Log.Warn($"weapon class bonus: pre-attack failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
