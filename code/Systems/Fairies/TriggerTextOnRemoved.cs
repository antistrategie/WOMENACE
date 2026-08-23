using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Fires the carrying skill's Event=Triggered DisplayText as the skill is removed, since the
// vanilla DisplayEvent enum has no OnRemoved. The Ceasefire effect uses it to announce expiry.
[JiangyuType("TriggerTextOnRemoved")]
public sealed partial class TriggerTextOnRemoved : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create() => new TriggerTextOnRemovedHandler();
}

[JiangyuType("TriggerTextOnRemovedHandler")]
public sealed partial class TriggerTextOnRemovedHandler : SkillEventHandler
{
    public override void OnRemoved()
    {
        try
        {
            // Removal also fires on death and on mission teardown: only a living carrier has
            // an expiry worth announcing.
            var actor = GetActor();
            if (actor != null && actor.IsAlive())
                ParentSkill?.TriggerDisplayText(actor);
        }
        catch (Exception ex)
        {
            Log.Warn($"trigger text: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
