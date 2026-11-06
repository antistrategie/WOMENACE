using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Attributes a Doll never grows. UnitLeaderAttributes.RollForAttributeIncreases rolls each attribute
// once per trigger it earned in the mission (m_Triggers), and every roll applies its gain through
// TryIncrease on the spot. Clearing a fixed attribute's triggers before the roll means it is never
// rolled, so its value and its shown increase both stay put. Welrod's Case Controller removes her
// critical chance, so Precision would grow for nothing.
public sealed class FixedAttributesSystem : JiangyuSystem
{
    private static readonly Dictionary<string, UnitLeaderAttribute[]> Fixed = new()
    {
        ["squad_leader.welrod"] = new[] { UnitLeaderAttribute.Precision },
    };

    public override void OnInit()
    {
        Context.Patches.Prefix("Il2CppMenace.Strategy.UnitLeaderAttributes", "RollForAttributeIncreases", OnRolling);
    }

    private void OnRolling(PatchInfo info)
    {
        try
        {
            var attributes = (info.Instance as Il2CppObjectBase)?.TryCast<UnitLeaderAttributes>();
            var id = attributes?.LeaderTemplate?.GetID();
            if (id == null || !Fixed.TryGetValue(id, out var locked))
                return;
            var triggers = attributes.m_Triggers;
            if (triggers == null)
                return;
            foreach (var attribute in locked)
                if ((int)attribute < triggers.Length)
                    triggers[(int)attribute] = 0;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"fixed attributes: roll filter failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
