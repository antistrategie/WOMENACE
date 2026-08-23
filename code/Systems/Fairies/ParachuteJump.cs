using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The Parachute Fairy's jump. The offmap skill aims at a destination tile; the handler
// teleports the currently selected Doll squad there (selecting the doll is the "first
// click") and applies the drop-shock debuff. Each element rides Element.TeleportTo, so the
// squad lands in formation; the actor's own tile, cached average position and vision then
// follow by hand, as they do after the mech's drill dash.
[JiangyuType("ParachuteJump")]
public sealed partial class ParachuteJump : SkillEventHandlerTemplate
{
    public string EffectId = "";

    public override SkillEventHandler Create() => new ParachuteJumpHandler { EffectId = EffectId };
}

[JiangyuType("ParachuteJumpHandler")]
public sealed partial class ParachuteJumpHandler : SkillEventHandler
{
    public string EffectId = "";

    private SkillTemplate _effect;
    private bool _resolved;

    public override bool OnVerifyTarget(Tile _originTile, Tile _targetTile)
    {
        try
        {
            return ActiveDoll() != null && _targetTile != null && _targetTile.GetEntity() == null;
        }
        catch
        {
            return false;
        }
    }

    public override void OnUse(Actor _user, Tile _targetTile, UsageParameter _usageParams, ref bool _applyToTile)
    {
        try
        {
            var doll = ActiveDoll();
            if (doll == null || _targetTile == null || _targetTile.GetEntity() != null)
                return;
            if (!_resolved)
            {
                _resolved = true;
                _effect = Templates.ById<SkillTemplate>(EffectId, msg => Log.Warn($"parachute: {msg}"));
            }

            var elements = doll.GetElements();
            for (var i = 0; i < (elements?.Count ?? 0); i++)
            {
                // The engine's own element teleport. The cover-mask argument is an override
                // whose "unset" value is 0, NOT -1: only a zero makes TeleportTo read the
                // destination's own cover mask, and only that mask yields the formation
                // offsets that spread the squad. Any other value is used verbatim, and a mask
                // with no offset table drops every element on the bare tile position.
                elements[i]?.TeleportTo(_targetTile, 0, MovementAction.Teleport, null);
            }
            doll.SetTile(_targetTile);
            doll.UpdateAveragePosition();
            doll.VisionDirty = true;
            if (_effect != null)
                SkillEffects.TryAddEffect(doll, _effect, msg => Log.Warn($"parachute: {msg}"));
            Log.Debug($"parachute: dropped '{doll.GetTemplate()?.GetID()}' at ({_targetTile.GetX()},{_targetTile.GetZ()})");
        }
        catch (Exception ex)
        {
            Log.Warn($"parachute: jump failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The squad that jumps is the one the player has selected, and the fairy serves our
    // Dolls only (the wmgfl speaker tag marks them).
    private static Actor ActiveDoll()
    {
        var actor = TacticalManager.Get()?.GetActiveActor();
        var unit = actor?.TryCast<UnitActor>();
        if (unit == null || !actor.IsAlive())
            return null;
        return Affinity.CharacterTag(unit.GetLeader()) != null ? actor : null;
    }
}
