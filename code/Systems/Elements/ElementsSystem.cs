using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// The Phase damage build-up ledger. Phase damage is exclusive to SSR
// weapons: their skills carry a WOMENACE:ElementalDamage handler naming an
// element and a per-hit amount. Every landed hit feeds the victim's gauge
// for that element, and when the gauge reaches the unit's proc threshold
// (20% of its max hitpoints, summed across its elements) the gauge resets
// and the element's status effect (effect.wmgfl_*) is applied. While the
// effect is live further build-up is swallowed, so nothing stacks and the
// gauge only starts refilling once the effect has expired.
//
// Gauges live per actor per element and last the whole mission (no decay).
// The overhead unit HUD draws them via EffectHudIconSystem as a greyscale
// icon that fills with the element's colour from the bottom.
public sealed class ElementsSystem : JiangyuSystem
{
    public const float ThresholdPctMaxHp = 0.2f;

    // A big unit needs proportionally more build-up to proc than a small one.
    // The fallback covers an actor whose element list resolves to no
    // hitpoints (never seen in practice), where a zero threshold would proc
    // an effect off any stray hit.
    public static float ThresholdFor(Actor actor)
    {
        var maxHp = RallyBarsSystem.SumHitpointsMax(actor);
        return maxHp > 0 ? maxHp * ThresholdPctMaxHp : 100f;
    }

    // index order is the Element wire format: KDL authors the element by name
    internal static readonly string[] ElementNames =
    {
        "Burn", "Shock", "Freeze", "Corrosion", "Hydro",
    };

    // EffectHudIconSystem tracks these for overhead icons, so the list lives
    // once here rather than as a hand-synced copy there
    internal static readonly string[] EffectIds =
    {
        "effect.wmgfl_burn",
        "effect.wmgfl_shock",
        "effect.wmgfl_freeze",
        "effect.wmgfl_corrosion",
        "effect.wmgfl_hydro",
    };

    // Leaf names of the greyscale gauge textures under unity/Assets/UI/Icons/
    // elements/, bundled together as icons__elements.bundle. They live there
    // because the template sprite tree under assets/additions/sprites is NOT
    // reachable through Context.Assets.
    private static readonly string[] GaugeTextures =
    {
        "burn_bw",
        "shock_bw",
        "freeze_bw",
        "corrosion_bw",
        "hydro_bw",
    };

    private static ElementsSystem _instance;

    // One victim's gauges. The Actor wrapper is retained deliberately: its
    // strong Il2Cpp GC handle keeps the native object alive, so the pointer
    // key can never be recycled to a different actor mid-mission (a dead
    // actor's entry goes inert instead of leaking its build-up to a
    // reinforcement allocated at the same address).
    private sealed class GaugeEntry
    {
        public Actor Actor;
        public float[] Gauges;
    }

    private readonly SkillTemplate[] _effects = new SkillTemplate[EffectIds.Length];
    private readonly Texture2D[] _gaugeTextures = new Texture2D[GaugeTextures.Length];
    private readonly Dictionary<IntPtr, GaugeEntry> _gauges = new();

    public override void OnInit()
    {
        _instance = this;
        // Thresholds move when squad members die (SumHitpointsMax shrinks), and
        // Accumulate only tests the proc on a fresh hit: a gauge banked below
        // the old threshold would otherwise sit at 100% forever. Re-test every
        // banked gauge at each turn boundary.
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnTurnEnd", _ => ReevaluateProcs());
    }

    public override void OnTemplatesApplied()
    {
        for (var i = 0; i < EffectIds.Length; i++)
            _effects[i] = Templates.ById<SkillTemplate>(EffectIds[i], msg => Context.Log.Warn($"elements: {msg}"));
    }

    // Actors are per-mission objects, so a scene change orphans every key.
    // Clearing keeps a recycled pointer in the next mission from inheriting a
    // stale gauge.
    public override void OnSceneLoaded(int buildIndex, string sceneName)
        => _gauges.Clear();

    internal static void Warn(string message)
        => _instance?.Context.Log.Warn(message);

    internal static void Debug(string message)
        => _instance?.Context.Log.Debug(message);

    public static int ElementIndex(string name)
        => Array.FindIndex(ElementNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    internal static void AddBuildUp(Actor victim, int element, float amount)
        => _instance?.Accumulate(victim, element, amount);

    // The victim's gauges, or null when every gauge is empty. The HUD reads
    // this to draw fill icons.
    internal static float[] GaugesFor(Actor actor)
    {
        if (_instance == null || actor == null)
            return null;
        return _instance._gauges.TryGetValue(actor.Pointer, out var entry) ? entry.Gauges : null;
    }

    // Queue-aware presence check: ApplyEffect adds through the container's
    // add queue, so a settled-list-only check would let the rest of the
    // proccing volley keep feeding the gauge (and at worst stack a second
    // copy of the effect).
    internal static bool HasLiveEffect(Actor actor, int element)
    {
        var template = _instance != null && element >= 0 && element < _instance._effects.Length
            ? _instance._effects[element]
            : null;
        return SkillEffects.CountInstances(actor?.GetSkills(), template) > 0;
    }

    // The colour icon is the effect template's own sprite. The greyscale
    // base for the gauge loads from the mod bundle on first use.
    internal static Sprite EffectSprite(int element)
        => _instance != null && element >= 0 && element < _instance._effects.Length
            ? _instance._effects[element]?.Icon
            : null;

    internal static Texture2D GaugeTexture(int element)
    {
        if (_instance == null || element < 0 || element >= GaugeTextures.Length)
            return null;
        _instance._gaugeTextures[element] ??= _instance.Context.Assets.Load<Texture2D>(GaugeTextures[element]);
        return _instance._gaugeTextures[element];
    }

    private void Accumulate(Actor victim, int element, float amount)
    {
        if (victim == null || element < 0 || element >= _effects.Length || amount <= 0f)
            return;
        // no stacking: a live effect swallows build-up outright
        if (HasLiveEffect(victim, element))
            return;

        if (!_gauges.TryGetValue(victim.Pointer, out var entry))
        {
            entry = new GaugeEntry { Actor = victim, Gauges = new float[EffectIds.Length] };
            _gauges[victim.Pointer] = entry;
        }
        var gauges = entry.Gauges;
        var threshold = ThresholdFor(victim);
        gauges[element] = Mathf.Min(threshold, gauges[element] + amount);
        Debug($"elements: {ElementNames[element]} build-up {gauges[element]:0}/{threshold:0} on '{victim.GetTemplate()?.GetID()}'");
        // only a successful application spends the gauge: a failed apply
        // (missing template, rejecting container) leaves it full to retry on
        // the next hit instead of destroying the build-up. A successful add
        // already redraws the row through the SkillContainer.Add postfix, so
        // only the no-proc path needs the explicit resync.
        if (gauges[element] >= threshold && ApplyEffect(victim, element))
            gauges[element] = 0f;
        else
            EffectHudIconSystem.Resync(victim);
    }

    private bool ApplyEffect(Actor victim, int element)
    {
        var template = _effects[element];
        if (!SkillEffects.TryAddEffect(victim, template, msg => Context.Log.Warn($"elements: {msg}")))
            return false;
        Debug($"elements: '{template.GetID()}' applied to '{victim.GetTemplate()?.GetID()}'");
        return true;
    }

    // Apply an element's proc effect outside the gauge path (e.g. Vector's
    // Overburn spread ignites the target directly). Same no-stacking rule as
    // a gauge proc: a live copy of the effect swallows the application.
    internal static bool ApplyEffectTo(Actor victim, int element)
    {
        if (_instance == null || victim == null || element < 0 || element >= _instance._effects.Length)
            return false;
        if (HasLiveEffect(victim, element))
            return false;
        return _instance.ApplyEffect(victim, element);
    }

    // Turn-boundary sweep: casualties can shrink a unit's threshold below its
    // already-banked build-up between hits. Clamp each gauge to the current
    // threshold and proc any that now sit at it, exactly as a fresh hit would.
    private void ReevaluateProcs()
    {
        try
        {
            foreach (var entry in _gauges.Values)
            {
                var victim = entry.Actor;
                if (victim == null || !victim.IsAlive())
                    continue;
                var gauges = entry.Gauges;
                float threshold = 0f;
                for (var element = 0; element < gauges.Length; element++)
                {
                    if (gauges[element] <= 0f || HasLiveEffect(victim, element))
                        continue;
                    if (threshold <= 0f)
                        threshold = ThresholdFor(victim);
                    if (gauges[element] < threshold)
                        continue;
                    gauges[element] = threshold;
                    if (ApplyEffect(victim, element))
                        gauges[element] = 0f;
                    else
                        EffectHudIconSystem.Resync(victim);
                }
            }
        }
        catch (Exception ex)
        {
            Warn($"elements: proc sweep failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
