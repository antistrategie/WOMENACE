using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Sets the owner's critical chance to a fixed value. Welrod's Case Controller sets it to zero. The
// handler sets it as her properties are rebuilt, and SetCriticalChanceSystem sets it again on each
// attack once every handler has run, so attack-time bonuses (Handgun Mastery) cannot add to it.
[JiangyuType("SetCriticalChance")]
public sealed partial class SetCriticalChance : SkillEventHandlerTemplate
{
    public int Value;

    public override SkillEventHandler Create() => new SetCriticalChanceHandler { Value = Value };
}

[JiangyuType("SetCriticalChanceHandler")]
public sealed partial class SetCriticalChanceHandler : SkillEventHandler
{
    public int Value;

    public override void OnUpdate(EntityProperties _properties)
    {
        if (_properties != null)
            _properties.CriticalChance = Value;
    }
}

// SkillContainer.BuildPropertiesForUse runs every handler's OnBeforeAnySkillUsed, so a postfix on it
// sees each attack's finished properties, for the hit preview and the shot alike.
public sealed class SetCriticalChanceSystem : JiangyuSystem
{
    private int _frame = -1;
    private readonly Dictionary<IntPtr, int?> _valueByActor = new();

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.SkillContainer", "BuildPropertiesForUse", OnAttackBuilt);
    }

    private void OnAttackBuilt(PatchInfo info)
    {
        try
        {
            if (info.Result is not EntityProperties props || info.Args is not { Count: > 0 } args
                || (args[0] as Il2CppSystem.Object)?.TryCast<Skill>() is not { } skill
                || !skill.IsAttack())
                return;
            // Only the player's side can field a doll, so every AI evaluation stops here.
            var actor = skill.GetActor();
            if (actor == null || !actor.IsPlayerOrPlayerAI())
                return;
            // Attacks are built for every hovered tile, so each actor's perks are read once a frame.
            var frame = UnityEngine.Time.frameCount;
            if (frame != _frame)
            {
                _frame = frame;
                _valueByActor.Clear();
            }
            if (!_valueByActor.TryGetValue(actor.Pointer, out var value))
                _valueByActor[actor.Pointer] = value = ValueFor(actor.TryCast<UnitActor>()?.GetLeader());
            if (value is { } fixedChance)
                props.CriticalChance = fixedChance;
        }
        catch (Exception ex) { Context.Log.Warn($"set critical chance: attack props failed: {ex.Message}"); }
    }

    private static int? ValueFor(BaseUnitLeader leader)
    {
        var perks = leader?.m_Perks;
        for (var i = 0; perks != null && i < perks.Count; i++)
        {
            var handlers = perks[i]?.EventHandlers;
            for (var j = 0; handlers != null && j < handlers.Count; j++)
                if (handlers[j]?.TryCast<SetCriticalChance>() is { } set)
                    return set.Value;
        }
        return null;
    }
}
