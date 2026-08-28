using Il2CppInterop.Runtime;
using Jiangyu.Game.Ui.Components;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Delays a hover-triggered show by the game's own tooltip-delay player
// setting, so mod tooltips appear on the same rhythm as vanilla ones. An
// instant tooltip beside delayed native ones reads as a glitch.
internal static class HoverDelay
{
    // Schedules `show` after the player's tooltip delay and returns the
    // pending item. The caller pauses it on leave (Cancel) so a touch-and-go
    // hover shows nothing.
    internal static IVisualElementScheduledItem Schedule(VisualElement anchor, Action show)
    {
        var delay = Tooltip.HoverDelayMs();
        if (delay <= 0)
        {
            show();
            return null;
        }
        var pending = anchor.schedule.Execute(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(show));
        pending.ExecuteLater(delay);
        return pending;
    }

    internal static void Cancel(ref IVisualElementScheduledItem pending)
    {
        try { pending?.Pause(); } catch { }
        pending = null;
    }
}
