using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Conversations;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev builds only: names every conversation as it starts, with its type and the presenter that
// plays it. An event plays through the event dialog and a strategy conversation through its own
// full-screen stage, and a stuck screen does not say which conversation it holds.
public sealed class ConversationTraceSystem : JiangyuSystem
{
    public override void OnInit()
    {
        Context.Patches.Prefix("Il2CppMenace.Conversations.ConversationPresenter", "PlayConversation", OnPlay);
    }

    private void OnPlay(PatchInfo info)
    {
        try
        {
            var conversation = (info.Args is { Count: > 0 } ? info.Args[0] as Il2CppObjectBase : null)?.TryCast<ConversationInstance>();
            var template = conversation?.GetTemplate();
            var presenter = (info.Instance as Il2CppSystem.Object)?.GetIl2CppType()?.Name ?? "?";
            Context.Log.Info($"conversation trace: '{template?.name}' ({template?.ConversationType}) plays on {presenter}");
        }
        catch (Exception ex) { Context.Log.Warn($"conversation trace failed: {ex.Message}"); }
    }
}
