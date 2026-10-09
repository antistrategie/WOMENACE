using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Game;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Souvenir: when a mission ends with the owner alive, she has Chance of bringing home one
// commodity taken from the enemies the player killed. The pool is every commodity in the killed
// enemies' own loot tables (EntityTemplate.Loot), so each faction yields its own trash, alien claws
// from aliens and construct optics from constructs, and a new or modded faction works with no
// change here. Commodities worth MinValue to MaxValue are preferred. When the kills carry none in
// that band, the one nearest it is taken, and when they carry no commodity at all, Fallback is.
//
// The roll happens on the perk's own handler as the mission finishes, and the item rides the
// mission-result loot list the way GiftDropSystem delivers gifts, so the native result flow both
// shows it and banks it.
[JiangyuType("Souvenir")]
public sealed partial class Souvenir : SkillEventHandlerTemplate
{
    public float Chance = 0.2f;
    public int MinValue = 30;
    public int MaxValue = 40;
    public CommodityTemplate Fallback;

    public override SkillEventHandler Create() => new SouvenirHandler
    {
        Chance = Chance,
        MinValue = MinValue,
        MaxValue = MaxValue,
        Fallback = Fallback,
    };
}

[JiangyuType("SouvenirHandler")]
public sealed partial class SouvenirHandler : SkillEventHandler
{
    public float Chance = 0.2f;
    public int MinValue = 30;
    public int MaxValue = 40;
    public CommodityTemplate Fallback;

    public override void OnMissionFinished()
    {
        try
        {
            var actor = GetActor();
            if (actor == null || !actor.IsAlive())
                return;
            SouvenirSystem.Roll(actor.Pointer, this);
        }
        catch (Exception ex)
        {
            Log.Warn($"souvenir: roll failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

public sealed class SouvenirSystem : JiangyuSystem
{
    private static SouvenirSystem _instance;

    // Owners already rolled this mission: a perk can carry more than one handler instance, and
    // each one sees OnMissionFinished.
    private readonly HashSet<IntPtr> _rolled = new();
    // Templates of the enemies the player killed this mission, by id.
    private readonly Dictionary<string, EntityTemplate> _killed = new();
    private readonly List<CommodityTemplate> _pending = new();

    public override void OnInit()
    {
        _instance = this;
        Context.Patches.Postfix("Il2CppMenace.Strategy.Operation", "OnMissionStarted", OnMissionStarted);
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnDeath", OnActorDied);
        Context.Patches.Prefix("Il2CppMenace.UI.MissionResult.MissionResultUIScreen", "ShowMissionWindow", OnShowMissionResult);
    }

    internal static void Roll(IntPtr owner, SouvenirHandler handler)
    {
        var system = _instance;
        if (system == null || !system._rolled.Add(owner) || system._killed.Count == 0)
            return;
        if (UnityEngine.Random.value >= handler.Chance)
            return;
        var pick = system.Pick(handler.MinValue, handler.MaxValue) ?? handler.Fallback;
        if (pick == null)
            return;
        system._pending.Add(pick);
        system.Context.Log.Debug($"souvenir: '{pick.GetID()}' (value {pick.TradeValue}) from {system._killed.Count} killed enemy type(s)");
    }

    // A random commodity from the killed enemies' loot inside the value band, else the one
    // nearest the band, else null.
    private CommodityTemplate Pick(int minValue, int maxValue)
    {
        var pool = new Dictionary<string, CommodityTemplate>();
        foreach (var enemy in _killed.Values)
        {
            var loot = enemy.Loot;
            for (var i = 0; loot != null && i < loot.Count; i++)
            {
                var commodity = (loot[i]?.Item as Il2CppObjectBase)?.TryCast<CommodityTemplate>();
                if (commodity != null)
                    pool[commodity.GetID()] = commodity;
            }
        }
        if (pool.Count == 0)
            return null;
        var inBand = pool.Values.Where(c => c.TradeValue >= minValue && c.TradeValue <= maxValue).ToList();
        if (inBand.Count > 0)
            return inBand[UnityEngine.Random.Range(0, inBand.Count)];
        int Distance(CommodityTemplate c) => c.TradeValue < minValue ? minValue - c.TradeValue : c.TradeValue - maxValue;
        return pool.Values.OrderBy(Distance).First();
    }

    // A mission attempt that ends without a result screen (abort, quit) leaves nothing for the
    // next one.
    private void OnMissionStarted(PatchInfo info)
    {
        _rolled.Clear();
        _killed.Clear();
        _pending.Clear();
    }

    // The same kill test GiftDropSystem uses: an enemy finished off by a player unit or its
    // allied AI.
    private void OnActorDied(PatchInfo info)
    {
        try
        {
            if (info.Args == null || info.Args.Count < 2)
                return;
            var target = (info.Args[0] as Il2CppObjectBase)?.TryCast<Actor>();
            var killer = (info.Args[1] as Il2CppObjectBase)?.TryCast<Actor>();
            if (target == null || killer == null || !killer.IsPlayerOrPlayerAI() || target.IsPlayerOrPlayerAI())
                return;
            var template = target.GetTemplate();
            if (template != null)
                _killed[template.GetID()] = template;
        }
        catch (Exception ex) { Context.Log.Warn($"souvenir: kill record failed: {ex.Message}"); }
    }

    private void OnShowMissionResult(PatchInfo info)
    {
        try
        {
            _rolled.Clear();
            _killed.Clear();
            if (_pending.Count == 0)
                return;
            var result = StrategyState.Get()?.GetLastMissionResult();
            if (result == null || !result.IsAlive())
                return;
            var loot = result.m_Loot;
            if (loot == null)
            {
                loot = new Il2CppSystem.Collections.Generic.List<BaseItemTemplate>();
                result.m_Loot = loot;
            }
            foreach (var item in _pending)
                loot.Add(item);
            Context.Log.Info($"souvenir: delivered {_pending.Count} item(s) via the mission-result screen");
            _pending.Clear();
        }
        catch (Exception ex) { Context.Log.Warn($"souvenir: result delivery failed: {ex.Message}"); }
    }
}
