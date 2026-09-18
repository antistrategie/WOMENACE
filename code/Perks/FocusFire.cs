using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Focus Fire's stack tracker: every consecutive attack against the same target adds a
// stack, up to MaxStacks, and each stack multiplies the next such attack's damage by
// (1 + BonusPerStack). Attacking anything else drops the count back to one. It rides the
// status effect itself, via KDL type="WOMENACE:ConsecutiveDamage", the way vanilla's
// ChangePropertyConsecutive rides effect.solid_grouping.
//
// A separate handler because the vanilla one feeds Amount x stacks and AmountMult x stacks
// to the property, which suits an additive stat like Accuracy and turns a multiplier into
// x1.1, x2.2, x3.3 per stack. The attack's damage is scaled in OnBeforeAnySkillUsed, the
// same window ChangePropertyConditional uses for Ambush and Assassin, so the bonus applies
// to that one attack's property snapshot and nothing else.
[JiangyuType("ConsecutiveDamage")]
public sealed partial class ConsecutiveDamage : SkillEventHandlerTemplate
{
    public float BonusPerStack = 0.1f;
    public int MaxStacks = 5;

    public override SkillEventHandler Create()
        => new ConsecutiveDamageHandler { BonusPerStack = BonusPerStack, MaxStacks = MaxStacks };
}

[JiangyuType("ConsecutiveDamageHandler")]
public sealed partial class ConsecutiveDamageHandler : SkillEventHandler
{
    public float BonusPerStack = 0.1f;
    public int MaxStacks = 5;

    // Il2Cpp objects carry no stable managed identity, so targets are remembered by pointer,
    // valid for the mission the entities live in. Kept as long rather than IntPtr so the
    // injected instance serialises cleanly: a stale value after a load matches nothing and
    // the next attack simply starts a fresh count.
    private long _lastTarget;
    private int _stacks;
    // The use being resolved: OnBeforeAnySkillUsed fires once per repetition of a burst, so a
    // use is recognised by its skill, its uses counter and a time window, and the stacks
    // that use pays for are fixed on its first repetition.
    private long _useSkill;
    private int _useCounter;
    private double _useSeen;
    private int _useBonusStacks;

    public override void OnMissionStarted() => Reset();

    public override void OnMissionFinished() => Reset();

    // The vanilla tracker shows its count on the status icon and hides the icon at zero.
    public override bool HasStackCountDisplayed() => true;

    public override bool IsHidden() => _stacks <= 0;

    // The only attack callback that reliably reaches the attacker's own effect: the engine
    // does not raise OnAnySkillUsed on it (verified live), so the commit happens here on the
    // first repetition of a use and the bonus fixed then applies to every repetition.
    public override void OnBeforeAnySkillUsed(Skill _skill, Tile _fromTile, Tile _targetTile, EntityProperties _properties, Entity _overrideTargetEntity)
    {
        try
        {
            if (_skill == null || !_skill.IsAttack())
                return;
            var target = _overrideTargetEntity ?? _targetTile?.GetEntity();
            if (target == null)
                return;
            var skillPtr = _skill.Pointer.ToInt64();
            var counter = _skill.GetUses();
            var now = Time.realtimeSinceStartupAsDouble;
            var sameUse = skillPtr == _useSkill && counter == _useCounter && now - _useSeen < 1.5;
            _useSeen = now;
            if (!sameUse)
            {
                _useSkill = skillPtr;
                _useCounter = counter;
                var targetPtr = target.Pointer.ToInt64();
                _useBonusStacks = targetPtr == _lastTarget ? _stacks : 0;
                if (targetPtr == _lastTarget)
                    _stacks = Math.Min(MaxStacks, _stacks + 1);
                else
                {
                    _lastTarget = targetPtr;
                    _stacks = 1;
                }
                var parent = ParentSkill;
                if (parent != null)
                    parent.StackCount = _stacks;
                RallyBarsSystem.RefreshStatusIcons(GetActor());
                Log.Debug($"focus fire: {_skill.GetID()} on {targetPtr} pays {_useBonusStacks} stack(s) (damage x{1f + BonusPerStack * _useBonusStacks:0.00}), now {_stacks}");
            }
            if (_properties != null && _useBonusStacks > 0)
                _properties.DamageMult *= 1f + BonusPerStack * _useBonusStacks;
        }
        catch (Exception ex)
        {
            Log.Warn($"focus fire: pre-attack failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Reset()
    {
        _lastTarget = 0;
        _stacks = 0;
        _useSkill = 0;
        _useCounter = 0;
        _useSeen = 0;
        _useBonusStacks = 0;
    }
}
