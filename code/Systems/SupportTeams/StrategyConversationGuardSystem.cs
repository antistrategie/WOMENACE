using Il2CppMenace.Conversations;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The strategy conversation manager plays its conversations on ConversationUIScreen, the
// full-screen stage. Vanilla calls it with the screen it returns to after an operation ends
// (StrategyState.EndOperation, then OpenScreenAfterConversation), and plays whatever it finds
// for that screen's trigger. Its constructor keeps only active StrategyConversation templates,
// and vanilla has none (its ten are all inactive), so it is built empty.
//
// The game builds its conversation managers before Jiangyu's clones exist, and Jiangyu's
// ConversationManagerRegistry adds each active cloned conversation to the managers afterwards. It
// learns a manager's type from the first template the manager holds, and puts a clone into any
// manager whose type it cannot learn. The empty strategy manager is such a manager, so every
// cloned event lands in its trigger lists too. A cloned event triggered on the system map then
// plays on the stage as the operation ends, with its speaker and no text or way out, and the game
// is stuck until a reload.
//
// So before each pick, the manager's trigger lists lose every template that is not a strategy
// conversation. An event then plays only as vanilla plays it, from its message on the home screen.
public sealed class StrategyConversationGuardSystem : JiangyuSystem
{
    public override void OnInit()
    {
        Context.Patches.Prefix("Il2CppMenace.Conversations.StrategyConversationsManager", "GetAvailableConversationForTrigger", OnPick);
    }

    private void OnPick(PatchInfo info)
    {
        try
        {
            var manager = (info.Instance as Il2CppSystem.Object)?.TryCast<StrategyConversationsManager>();
            var byTrigger = manager?.m_AvailableTemplatesByTriggerType;
            if (byTrigger == null)
                return;
            foreach (var pair in byTrigger)
            {
                var templates = pair.Value;
                if (templates == null)
                    continue;
                for (var i = templates.Count - 1; i >= 0; i--)
                {
                    var template = templates[i];
                    if (template == null || template.ConversationType == ConversationType.StrategyConversation)
                        continue;
                    Context.Log.Info($"strategy conversations: '{template.name}' ({template.ConversationType}) left out of {pair.Key}");
                    templates.RemoveAt(i);
                }
            }
        }
        catch (Exception ex) { Context.Log.Warn($"strategy conversation guard failed: {ex.Message}"); }
    }
}
