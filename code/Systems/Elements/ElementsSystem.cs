using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// The elemental build-up ledger. Elemental damage is exclusive to SSR
// weapons: their skills carry a WOMENACE:ElementalDamage handler naming an
// element and a per-hit amount. Every landed hit feeds the victim's gauge
// for that element, and at 100 the gauge resets and the element's status
// effect (effect.wmgfl_*) is applied. While the effect is live further
// build-up is swallowed, so nothing stacks and the gauge only starts
// refilling once the effect has expired.
//
// Gauges live per actor per element and last the whole mission (no decay).
// The overhead unit HUD draws them via EffectHudIconSystem as a greyscale
// icon that fills with the element's colour from the bottom.
public sealed class ElementsSystem : JiangyuSystem
{
    public const float Threshold = 100f;

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
        => _instance = this;

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
        gauges[element] = Mathf.Min(Threshold, gauges[element] + amount);
        Debug($"elements: {ElementNames[element]} build-up {gauges[element]:0}/{Threshold:0} on '{victim.GetTemplate()?.GetID()}'");
        // only a successful application spends the gauge: a failed apply
        // (missing template, rejecting container) leaves it full to retry on
        // the next hit instead of destroying the build-up. A successful add
        // already redraws the row through the SkillContainer.Add postfix, so
        // only the no-proc path needs the explicit resync.
        if (gauges[element] >= Threshold && ApplyEffect(victim, element))
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
}
