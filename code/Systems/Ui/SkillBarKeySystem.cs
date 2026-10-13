using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.PlayerSettings;
using Il2CppMenace.UI.Tactical;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Keeps skill-bar hotkeys numbering on from a pinned standalone button.
//
// The bar threads keys through m_AllElements (buttons, then weapon slots, then
// accessory slots). A standalone SkillBarButton takes its template's KeyBind,
// None when that is Auto, and returns it. A slot numbers its Auto skills on
// from what it was handed, and restarts at SelectItemSkill1 whenever that is
// not an item key. Vanilla buttons are all Auto, so their None makes the first
// slot start at 1. A unit whose kit lives on the entity pins its buttons to
// item keys instead (the Sinbreaker's 1 to 3), and any unpinned button after
// them, such as a perk active, hands None on and restarts the accessory slot
// at 1 on top of the pinned key.
//
// The fold below is the vanilla one except that a button returning a non-item
// key does not overwrite the last item key, so for an all-Auto vanilla bar it
// assigns exactly what the native pass did.
public sealed class SkillBarKeySystem : JiangyuSystem
{
    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.UI.Tactical.SkillBar", "UpdateSkills", OnBarUpdated);
    }

    private void OnBarUpdated(PatchInfo info)
    {
        try
        {
            var bar = (info.Instance as Il2CppObjectBase)?.TryCast<SkillBar>();
            if (bar != null)
                Assign(bar);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"skill bar keybinds failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Re-threads every element of the bar, then any extra slots that could not
    // join its list. Returns the last item key assigned.
    internal static KeyBindPlayerSetting Assign(SkillBar bar, IEnumerable<BaseSkillBarItemSlot> extra = null)
    {
        var last = KeyBindPlayerSetting.None;
        var all = bar.m_AllElements;
        for (var i = 0; all != null && i < all.Count; i++)
            if (all[i] != null)
                last = Keep(last, all[i].UpdateKeyBinds(last));
        if (extra != null)
            foreach (var slot in extra)
                if (slot != null)
                    last = Keep(last, slot.UpdateKeyBinds(last));
        return last;
    }

    private static KeyBindPlayerSetting Keep(KeyBindPlayerSetting last, KeyBindPlayerSetting returned)
        => IsItemKey(returned) ? returned : last;

    private static bool IsItemKey(KeyBindPlayerSetting key)
        => key is >= KeyBindPlayerSetting.SelectItemSkill1 and <= KeyBindPlayerSetting.SelectItemSkill6
            or >= KeyBindPlayerSetting.SelectItemSkill7 and <= KeyBindPlayerSetting.SelectItemSkill10;
}
