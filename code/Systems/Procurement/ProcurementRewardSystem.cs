using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.Strategy.Missions;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;
using PseudoRandom = Il2CppMenace.Tools.PseudoRandom;

namespace WOMENACE.Code;

public sealed class ProcurementRewardSystem : JiangyuSystem
{
    private const string MissionRewardId = "wmgfl_collapse_piece_reward";
    private const int OfferChance = 20;
    private bool _initialising;

    public override void OnInit()
    {
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.RewardSelection", "Init", OnRewardSelection);
        Context.Patches.Postfix("Il2CppMenace.UI.RewardSlot", "Init", 1, OnMissionRewardSlot);
        Context.Patches.Prefix("Il2CppMenace.Strategy.OwnedItems", "AddItem", 3, OnAddItem);
    }

    public override void OnTemplatesApplied()
    {
        var reward = Templates.ById<StrategicAssetTemplate>(MissionRewardId);
        if (reward == null)
            return;

        // JIANGYU-CONTRACT: Operation.GenerateMissions (RVA 0x5982F0) assigns fixed
        // operation assets first, then draws from each remaining mission's difficulty-filtered
        // PotentialStrategicAssets. Adding here preserves fixed and story rewards.
        foreach (var mission in Templates.All<MissionTemplate>())
        {
            var existing = mission.PotentialStrategicAssets;
            if (existing == null || existing.Length == 0)
                continue;
            var options = existing.Where(option => option?.StrategicAsset?.GetID() != MissionRewardId).ToList();
            var additions = new List<MissionStrategicAssetTemplate>();
            foreach (var difficulty in Enum.GetValues<MissionDifficultyFlag>())
            {
                var totalWeight = options.Where(option => option?.StrategicAsset != null
                    && (option.ReqMissionDifficulty & difficulty) != 0)
                    .Sum(option => (long)Math.Max(0, option.Weight));
                if (totalWeight == 0)
                    continue;
                additions.Add(new MissionStrategicAssetTemplate
                {
                    StrategicAsset = reward,
                    ReqMissionDifficulty = difficulty,
                    Weight = (int)Math.Clamp((totalWeight * OfferChance + (100 - OfferChance) / 2)
                        / (100 - OfferChance), 1, int.MaxValue),
                });
            }
            if (additions.Count > 0 || options.Count != existing.Length)
                mission.PotentialStrategicAssets = options.Concat(additions).ToArray();
        }
    }

    private void OnRewardSelection(PatchInfo info)
    {
        var campaign = StrategyState.Get();
        var operation = campaign?.GetCurrentOperation();
        if (_initialising || operation?.GetResult()?.GetOperationStatus() != OperationStatus.Completed
            || info.Instance is not RewardSelection selection
            || info.Args[0] is not Il2CppSystem.Collections.Generic.List<IRewardable> options
            || options.Count == 0 || info.Args[1] is not Il2CppSystem.Action<IRewardable> onContinue)
            return;
        var pieces = Templates.ById<CommodityTemplate>(Procurement.PieceId);
        if (pieces == null)
            return;

        // The native operation choices use a fresh RNG seeded from the operation. A separate
        // stream keeps this offer stable on reopening without advancing any campaign rolls.
        var random = new PseudoRandom(unchecked(operation.GetSeed() ^ 0x57C011A9));
        if (random.Next(100) >= OfferChance)
            return;
        var index = random.Next(options.Count);

        var rewardAmount = OperationAmount(operation);
        var completed = false;
        var claimKey = $"{operation.GetTemplate().GetID()}:{operation.GetSeed()}";
        var callback = (Il2CppSystem.Action<IRewardable>)(Action<IRewardable>)(selected =>
        {
            if (completed || StrategyState.Get()?.Pointer != campaign.Pointer
                || campaign.GetCurrentOperation()?.Pointer != operation.Pointer)
                return;
            var ledger = Context.State.Get<ProcurementRewardState>();
            if (ledger.LastOperationClaim == claimKey || selected?.GetID() == Procurement.PieceId)
            {
                if (ledger.LastOperationClaim != claimKey && !Grant(pieces, rewardAmount))
                    return;
                ledger.LastOperationClaim = claimKey;
                // JIANGYU-CONTRACT: OperationResultUIScreen's continuation (RVA 0x52D220)
                // grants one item for a non-null choice, then ends the operation. Null advances
                // normally after this transaction grants the entire bundle.
                selected = null;
            }
            completed = true;
            onContinue.Invoke(selected);
        });

        options[index] = pieces.Cast<IRewardable>();
        // These IL2CPP calls do not copy ordinary __args edits back to the native arguments.
        // Re-enter with the explicit callback, skipping only this intercepted initialisation.
        info.Skip = true;
        _initialising = true;
        try { selection.Init(options, callback); }
        finally { _initialising = false; }

        // JIANGYU-CONTRACT: RewardSelection.Init (RVA 0x84E940) rebuilds its slots.
        // Style the new bundle card without affecting other rewards on a later opening.
        SetAmount(selection.m_RewardSlots[index], rewardAmount);
    }

    private static void OnMissionRewardSlot(PatchInfo info)
    {
        // JIANGYU-CONTRACT: RewardSlot.Init(OperationAssetTemplate) shows both icons
        // on every initialisation. Keep Icon for other screens, but hide the duplicate
        // overlay on this reward card.
        if (info.Instance is RewardSlot slot && info.Args[0] is OperationAssetTemplate asset
            && asset.GetID() == MissionRewardId)
            slot.m_SmallIcon.SetVisible(false);
    }

    private static void SetAmount(RewardSlot slot, int count)
    {
        var amount = slot.m_AmountLabel;
        amount.text = $"×{count}";
        amount.pickingMode = PickingMode.Ignore;
        var style = amount.style;
        style.position = Position.Absolute;
        style.left = style.top = new(StyleKeyword.Auto);
        style.right = 1;
        style.bottom = 0;
        style.width = style.height = new(StyleKeyword.Auto);
        style.marginLeft = style.marginRight = style.marginTop = style.marginBottom = 0;
        style.paddingLeft = style.paddingRight = 2;
        style.paddingTop = style.paddingBottom = 0;
        style.fontSize = 9;
        style.unityFontStyleAndWeight = FontStyle.Normal;
        style.unityTextAlign = TextAnchor.MiddleRight;
        style.color = new(new Color(222 / 255f, 208 / 255f, 154 / 255f));
        style.backgroundColor = new(new Color(0, 0, 0, .6f));
        amount.SetVisible(true);
    }

    private static int OperationAmount(Operation operation)
        => operation.GetDuration().GetLength() switch
        {
            <= 3 => 50,
            4 => 75,
            _ => 100,
        };

    private static void OnAddItem(PatchInfo info)
    {
        // The mission reward already displays the bundle. Native item effects request a
        // pickup dialog for each piece, so currency grants suppress those individual dialogs.
        if (info.Args[0] is not BaseItemTemplate item || item.GetID() != Procurement.PieceId
            || info.Args[1] is not true || info.Instance is not OwnedItems owned)
            return;
        info.Skip = true;
        info.Result = owned.AddItem(item, false, (bool)info.Args[2]);
    }

    private bool Grant(CommodityTemplate pieces, int amount)
    {
        var result = InventoryExchange.Run([], [(pieces, amount)], Context.Log);
        if (!result.ok)
            Context.Log.Error($"Collapse Piece reward failed: {result.error}");
        return result.ok;
    }
}

public sealed class ProcurementRewardState
{
    public string LastOperationClaim { get; set; }
}
