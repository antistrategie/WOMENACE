using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.UI;
using Jiangyu.Game;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Fairy uses are a per-mission budget. MENACE keeps one pool of offmap uses per operation on
// OffmapAbilityInstance.m_RemainingUses and refills it only in Operation.StartOperation,
// Operation.EndOperation and after the ship-upgrades dialog closes, so nothing runs between
// missions. Operation.EndMission is the per-mission funnel (aborted missions included), so the
// fairy instances refill there and the next prep screen and HUD read a full budget.
public sealed class FairyUsesSystem : JiangyuSystem
{
    // The engine's usage line, translated through the tooltip's own key at build time. The
    // "more" form shows once uses have been spent.
    private static readonly (string From, string To)[] UsageLines =
    [
        ("Is limited to {0} uses per operation", "Is limited to {0} uses per mission"),
        ("Is limited to {0} more uses per operation", "Is limited to {0} more uses per mission"),
    ];

    private readonly HashSet<string> _abilities = new(StringComparer.Ordinal);
    private readonly HashSet<string> _skills = new(StringComparer.Ordinal);

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Strategy.Operation", "EndMission", OnMissionEnded);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.SkillTemplate", "AppendUsageInfoTooltipData", OnUsageTooltip);
    }

    public override void OnTemplatesApplied()
    {
        try
        {
            _abilities.Clear();
            _skills.Clear();
            var lodge = Templates.ById<ShipUpgradeTemplate>(FairyLodgeSystem.LodgeId,
                msg => Context.Log.Warn($"fairy uses: {msg}"));
            Collect(lodge, 0);
            Context.Log.Debug($"fairy uses: {_abilities.Count} fairy abilit(ies) refill every mission");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"fairy uses: setup failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Every ability the lodge tree grants, found through the modules' own GrantOffmapAbilityEffect
    // entries so a new fairy joins by being registered under the lodge. Depth cap is a cycle guard.
    private void Collect(ShipUpgradeTemplate node, int depth)
    {
        if (node == null || depth > 4)
            return;
        var effects = node.Effects;
        for (var i = 0; i < (effects?.Length ?? 0); i++)
        {
            var ability = effects[i]?.TryCast<GrantOffmapAbilityEffect>()?.OffmapAbility;
            if (ability == null)
                continue;
            _abilities.Add(ability.GetID());
            if (ability.SkillTemplate != null)
                _skills.Add(ability.SkillTemplate.GetID());
        }
        var children = node.ChildUpgrades;
        for (var i = 0; i < (children?.Length ?? 0); i++)
            Collect(children[i], depth + 1);
    }

    private void OnMissionEnded(PatchInfo info)
    {
        try
        {
            var instances = StrategyState.Get()?.ActiveOffmapAbilities?.m_OffmapAbilityInstances;
            if (instances == null || _abilities.Count == 0)
                return;
            var refilled = 0;
            for (var i = 0; i < instances.Count; i++)
            {
                var instance = instances[i];
                if (instance == null || !_abilities.Contains(instance.TemplateId ?? ""))
                    continue;
                instance.RefillRemainingUses();
                refilled++;
            }
            if (refilled > 0)
                Context.Log.Info($"fairy uses: refilled {refilled} fairy abilit(ies) for the next mission");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"fairy uses: refill failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // A limited-use skill card ends its usage block with the engine's "Is limited to N uses per
    // operation" line, shared by every offmap ability through one localisation key. Fairy cards
    // say per mission; the line is matched on its translated shape so the number can be whatever
    // the engine printed.
    private void OnUsageTooltip(PatchInfo info)
    {
        try
        {
            if (info.Args.Count < 7 || info.Args[6] is not true)
                return;
            var skill = As<SkillTemplate>(info.Instance);
            var tooltip = As<TooltipData>(info.Args[0]);
            if (skill == null || tooltip == null || !_skills.Contains(skill.GetID()))
                return;
            var replaced = 0;
            foreach (var (from, to) in UsageLines)
                replaced += Replace(tooltip.m_Elements, Translate(tooltip, from), Translate(tooltip, to));
            if (replaced == 0)
                Context.Log.Debug($"fairy uses: no usage line to reword on {skill.GetID()}");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"fairy uses: tooltip reword failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Translate(TooltipData tooltip, string text)
    {
        try
        {
            return tooltip.Translate(text, false) is { Length: > 0 } translated ? translated : text;
        }
        catch
        {
            return text;
        }
    }

    // Rewrites every text element whose content fits the "from" format, keeping whatever the
    // engine substituted for {0}. Containers are walked so a row or column layout still matches.
    private static int Replace(Il2CppSystem.Collections.Generic.List<BaseTooltipElement> elements, string from, string to)
    {
        var count = 0;
        for (var i = 0; i < (elements?.Count ?? 0); i++)
        {
            var element = elements[i];
            if (element == null)
                continue;
            if (element.TryCast<TooltipText>() is { } text)
            {
                if (Reformat(text.m_Text, from, to) is { } reworded)
                {
                    text.m_Text = reworded;
                    count++;
                }
                continue;
            }
            if (element.TryCast<TooltipRow>() is { } row)
                count += Replace(row.m_Elements, from, to);
            else if (element.TryCast<TooltipColumn>() is { } column)
                count += Replace(column.m_Elements, from, to);
            else if (element.TryCast<TooltipInteractiveContainer>() is { } container)
                count += Replace(container.m_BaseTooltipElements, from, to);
            else if (element.TryCast<TooltipGrid>() is { } grid)
                for (var c = 0; c < (grid.m_Columns?.Length ?? 0); c++)
                    count += Replace(grid.m_Columns[c]?.m_Elements, from, to);
        }
        return count;
    }

    private static string Reformat(string text, string from, string to)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var slot = from.IndexOf("{0}", StringComparison.Ordinal);
        if (slot < 0)
            return text == from ? to : null;
        var prefix = from[..slot];
        var suffix = from[(slot + 3)..];
        if (text.Length < prefix.Length + suffix.Length
            || !text.StartsWith(prefix, StringComparison.Ordinal)
            || !text.EndsWith(suffix, StringComparison.Ordinal))
            return null;
        var value = text[prefix.Length..^suffix.Length];
        return to.Replace("{0}", value, StringComparison.Ordinal);
    }

    private static T As<T>(object value) where T : Il2CppObjectBase
        => (value as Il2CppObjectBase)?.TryCast<T>();
}
