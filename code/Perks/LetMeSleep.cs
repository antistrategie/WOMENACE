using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Let Me Sleep, Mechty's starting perk. Every turn she ends without having moved earns
// a stack, up to MaxStacks. Each stack is +BonusPerStack damage and Accuracy and
// -DetectionPerStack Detection, so a Doll left dozing in one spot shoots harder and
// notices less. Moving clears every stack at once and leaves her slacking off (MoveEffect,
// a short accuracy penalty). Slotted onto the perk via KDL type="WOMENACE:StationaryStacks".
//
// The stat changes ride this handler's own OnUpdate, the way a vanilla ChangeProperty
// does, because no vanilla handler scales a property by "turns spent stationary". The
// StackEffect exists only to show the count: it carries a StackBadge handler and this
// handler writes its StackCount.
[JiangyuType("StationaryStacks")]
public sealed partial class StationaryStacks : SkillEventHandlerTemplate
{
    public int MaxStacks = 3;
    public float BonusPerStack = 0.1f;
    public int DetectionPerStack = 1;
    public SkillTemplate StackEffect;
    public SkillTemplate MoveEffect;

    public override SkillEventHandler Create()
        => new StationaryStacksHandler
        {
            MaxStacks = MaxStacks,
            BonusPerStack = BonusPerStack,
            DetectionPerStack = DetectionPerStack,
            StackEffect = StackEffect,
            MoveEffect = MoveEffect,
        };
}

[JiangyuType("StationaryStacksHandler")]
public sealed partial class StationaryStacksHandler : SkillEventHandler
{
    public int MaxStacks = 3;
    public float BonusPerStack = 0.1f;
    public int DetectionPerStack = 1;
    public SkillTemplate StackEffect;
    public SkillTemplate MoveEffect;

    private int _stacks;
    private bool _movedThisTurn;

    // A perk can instantiate its handler more than once for one owner. The first
    // instance to see the owner in a mission does the counting and the property
    // changes, every other instance stands down, or the bonus would multiply per copy.
    private static readonly Dictionary<IntPtr, StationaryStacksHandler> Owners = [];

    public override void OnBeforeMissionStarted() => Owners.Clear();

    public override void OnMissionStarted()
    {
        _stacks = 0;
        _movedThisTurn = false;
        Claim();
    }

    public override void OnMissionFinished()
    {
        _stacks = 0;
        var actor = GetActor();
        if (actor != null && Owners.TryGetValue(actor.Pointer, out var owner) && ReferenceEquals(owner, this))
            Owners.Remove(actor.Pointer);
    }

    public override void OnTurnStart()
    {
        if (!IsOwner())
            return;
        _movedThisTurn = false;
    }

    // A turn that ends where it began earns a stack.
    public override void OnTurnEnd()
    {
        if (!IsOwner() || _movedThisTurn || _stacks >= MaxStacks)
            return;
        _stacks++;
        SyncBadge();
    }

    // Her own movement only. Being carried in a vehicle or knocked back changes the tile
    // without a movement order, and neither is her slacking off.
    public override void OnMovementFinished(Tile _tile)
    {
        if (!IsOwner())
            return;
        _movedThisTurn = true;
        if (_stacks > 0)
        {
            _stacks = 0;
            SyncBadge();
        }
        ApplyMoveEffect();
    }

    public override void OnUpdate(EntityProperties properties)
    {
        if (!Claim() || _stacks <= 0 || properties == null)
            return;
        var bonus = 1f + BonusPerStack * _stacks;
        properties.DamageMult *= bonus;
        properties.AccuracyMult *= bonus;
        properties.Detection -= DetectionPerStack * _stacks;
    }

    private bool IsOwner()
    {
        var actor = GetActor();
        return actor != null && Owners.TryGetValue(actor.Pointer, out var owner) && ReferenceEquals(owner, this);
    }

    // Registers this instance as the owner's counting handler unless another already is.
    // Called from OnUpdate as well as OnMissionStarted because GetActor can still be null
    // at mission start and a mid-mission spawn never sees that event. A fresh owner adopts
    // the count the badge effect carries: a mid-mission save restores that effect with its
    // StackCount while this handler starts from zero, and the two must not disagree.
    private bool Claim()
    {
        var actor = GetActor();
        if (actor == null)
            return false;
        if (Owners.TryGetValue(actor.Pointer, out var owner))
            return ReferenceEquals(owner, this);
        Owners[actor.Pointer] = this;
        AdoptBadge(actor);
        return true;
    }

    private void AdoptBadge(Actor actor)
    {
        try
        {
            if (StackEffect == null)
                return;
            var badge = actor.GetSkills()?.GetSkillByTemplate(StackEffect, null)?.TryCast<Skill>();
            if (badge == null)
                return;
            _stacks = Math.Clamp(badge.StackCount, 0, MaxStacks);
        }
        catch (Exception ex)
        {
            Log.Warn($"let me sleep: badge adopt failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void SyncBadge()
    {
        try
        {
            var actor = GetActor();
            if (actor == null || StackEffect == null)
                return;
            if (_stacks <= 0)
            {
                // Bounded removal: this runs inside the container's OnMovementFinished dispatch,
                // where Remove(SkillTemplate) only flags and would report success forever.
                if (SkillEffects.RemoveInstances(actor.GetSkills(), StackEffect) > 0)
                    EffectHudIconSystem.Resync(actor);
                return;
            }
            var existing = actor.GetSkills()?.GetSkillByTemplate(StackEffect, null)?.TryCast<Skill>()
                ?? AddEffect(actor, StackEffect)?.TryCast<Skill>();
            if (existing == null)
                return;
            existing.StackCount = _stacks;
            // a StackCount write fires no container event, so the panel keeps the old number
            // until something else redraws it
            RallyBarsSystem.RefreshStatusIcons(actor);
        }
        catch (Exception ex)
        {
            Log.Warn($"let me sleep: badge sync failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Fresh carriers get the effect. A carrier only has its remaining lifetime wound back
    // up, so a second move in one turn does not stack a second penalty.
    private void ApplyMoveEffect()
    {
        try
        {
            var actor = GetActor();
            var skills = actor?.GetSkills();
            if (skills == null || MoveEffect == null)
                return;
            var existing = skills.GetSkillByTemplate(MoveEffect, null);
            if (existing == null)
            {
                AddEffect(actor, MoveEffect);
                return;
            }
            var handlers = existing.TryCast<Skill>()?.GetSkillEventHandlers();
            for (var i = 0; handlers != null && i < handlers.Length; i++)
            {
                var lifetime = handlers[i]?.TryCast<LifetimeLimitHandler>();
                if (lifetime != null)
                    lifetime.m_TurnsLeft = lifetime.m_Template?.Lifetime ?? lifetime.m_TurnsLeft;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"let me sleep: slack off failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static BaseSkill AddEffect(Actor actor, SkillTemplate effect)
    {
        var skills = actor?.GetSkills();
        if (skills == null || effect == null)
            return null;
        // a boxed EMPTY nullable, not managed null: null for an Il2CppSystem.Nullable proxy
        // misbehaves in the interop marshalling
        var instance = effect.CreateSkill(new Il2CppSystem.Nullable<Il2CppMenace.Strategy.Origin>());
        if (instance == null || !skills.Add(instance))
        {
            Log.Warn($"let me sleep: could not add '{effect.GetID()}'");
            return null;
        }
        return instance;
    }
}
