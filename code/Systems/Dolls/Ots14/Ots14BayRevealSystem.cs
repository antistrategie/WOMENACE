using System.Collections;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.States;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Grows a bay arm out of her back when its weapon's skill is aimed, and
// retracts it when the aim ends.
//
// In a tactical mission the whole bay spawns concealed (BayMountSystem
// scales the scapula chains to nothing). Selecting a bay skill puts the
// player in the targeting state, and THAT is the arm's cue: the scapula for
// the skill's slot eases up to full size, and once the arm is most of the
// way out the weapon materialises in its fist. Deselecting, firing another
// weapon, or moving retracts it the same way. While the skill is actually
// executing the arm stays out even if the selection has already cleared, so
// the shot never leaves a retracted weapon.
//
// Everything is scale-driven: the game's visibility passes re-enable
// disabled objects but never touch scale, and the arms' export ships
// without scale channels so the Animator cannot stomp the writes.
public sealed class Ots14BayRevealSystem : JiangyuSystem
{
    private const float HiddenArmScale = 0.001f;
    // Deliberately quick: the arm should be at full size before a human
    // finishes picking a target.
    private const float ArmSmoothTime = 0.09f;
    private const float WeaponSmoothTime = 0.05f;
    // How far out the arm must be before the weapon starts materialising.
    private const float WeaponRevealAt = 0.75f;

    internal static Ots14BayRevealSystem Instance { get; private set; }

    private sealed class RevealState
    {
        public Element Element;
        // Resolved lazily: the element's entity is wired up after
        // attachments (and this registration) happen.
        public Actor Actor;
        public bool Tactical;
        public float[] ArmScales;
        public float[] ArmVelocities;
        public float[] WeaponScales;
        public float[] WeaponVelocities;
    }

    // Which bay slot the armoury's equip UI is picking for (-1 none): the
    // armoury preview's reveal cue, where no skill selection exists.
    internal static int ArmouryFocus { get; set; } = -1;

    private readonly Dictionary<IntPtr, RevealState> _states = new();
    private readonly List<IntPtr> _dead = new();
    private object _loop;

    public override void OnInit()
    {
        Instance = this;
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        _states.Clear();
        // Stopped explicitly: mod coroutines run on MelonLoader and SURVIVE
        // scene loads, so nulling the handle alone left an orphan loop
        // ticking beside the next mission's fresh one.
        if (_loop != null)
            try { Context.Coroutines.Stop(_loop); } catch { }
        _loop = null;
        ArmouryFocus = -1;
    }

    // Called by BayMountSystem after it stores a concealed mount set, so
    // registration never races the two systems' patch ordering.
    internal static void OnBayMounted(IntPtr elementPtr, Element element, bool tactical)
        => Instance?.Register(elementPtr, element, tactical);

    private void Register(IntPtr elementPtr, Element element, bool tactical)
    {
        try
        {
            var state = new RevealState
            {
                Tactical = tactical,
                ArmScales = new float[Bay.SlotCount],
                ArmVelocities = new float[Bay.SlotCount],
                WeaponScales = new float[Bay.SlotCount],
                WeaponVelocities = new float[Bay.SlotCount],
            };
            for (var i = 0; i < Bay.SlotCount; i++)
                state.ArmScales[i] = HiddenArmScale;
            state.Element = element;
            _states[elementPtr] = state;
            if (_loop == null)
                _loop = Context.Coroutines.Start(RevealLoop());
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay reveal: register failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private IEnumerator RevealLoop()
    {
        while (true)
        {
            var selectedPtr = IntPtr.Zero;
            try
            {
                selectedPtr = TacticalState.Get()?.GetSelectedSkill()?.Pointer ?? IntPtr.Zero;
            }
            catch
            {
                // between missions there is nothing selected
            }
            _dead.Clear();
            foreach (var (elementPtr, state) in _states)
            {
                try
                {
                    if (!BayMountSystem.Mounts.TryGetValue(elementPtr, out var set) || !set.Concealed)
                    {
                        _dead.Add(elementPtr);
                        continue;
                    }
                    int mask;
                    if (state.Tactical)
                    {
                        if (state.Actor == null && state.Element != null && !state.Element.WasCollected)
                            state.Actor = (state.Element.GetEntity() as Il2CppObjectBase)?.TryCast<Actor>();
                        mask = SlotMaskOf(selectedPtr, elementPtr);
                        // Execution keeps the arm out after the selection clears.
                        if (mask == 0 && state.Actor != null)
                            mask = SlotMaskOf(state.Actor.GetActiveSkill()?.Pointer ?? IntPtr.Zero, elementPtr);
                    }
                    else
                    {
                        // Armoury preview: the equip UI's open tile is the cue.
                        mask = ArmouryFocus >= 0 ? 1 << ArmouryFocus : 0;
                    }
                    Animate(set, state, mask);
                }
                catch
                {
                    _dead.Add(elementPtr); // element died: its transforms are gone
                }
            }
            foreach (var elementPtr in _dead)
                _states.Remove(elementPtr);
            if (_states.Count == 0)
            {
                _loop = null;
                yield break;
            }
            yield return null;
        }
    }

    // A MASK rather than one slot: a linked group fires from all its arms at
    // once, so every one has to be out for the shot. One slot could only ever
    // reveal part of a linked volley.
    private static void Animate(BayMountSet set, RevealState state, int mask)
    {
        for (var j = 0; j < Bay.SlotCount; j++)
        {
            var record = set.Slots[j];
            if (record?.Scapula == null)
                continue;
            var outNow = (mask & (1 << j)) != 0 && record.Occupied;
            var armTarget = outNow ? 1f : HiddenArmScale;
            state.ArmScales[j] = Mathf.SmoothDamp(state.ArmScales[j], armTarget, ref state.ArmVelocities[j], ArmSmoothTime);
            record.Scapula.localScale = Vector3.one * state.ArmScales[j];
            if (record.Mount == null)
                continue;
            var weaponTarget = outNow && state.ArmScales[j] >= WeaponRevealAt ? record.InvHandScale : 0f;
            state.WeaponScales[j] = Mathf.SmoothDamp(state.WeaponScales[j], weaponTarget, ref state.WeaponVelocities[j], WeaponSmoothTime);
            record.Mount.localScale = Vector3.one * state.WeaponScales[j];
        }
    }

    // Which arms this skill fires from, as a slot bitmask. A single grant is
    // its own arm; a linked skill is EVERY arm of its group, which is what
    // puts the other arms out for a linked shot.
    private static int SlotMaskOf(IntPtr skillPtr, IntPtr elementPtr)
    {
        if (skillPtr == IntPtr.Zero)
            return 0;
        if (BaySkillSystem.TryGetGranted(skillPtr, out var granted))
            return granted.ElementPtr == elementPtr ? 1 << granted.Slot : 0;
        if (BaySkillSystem.TryGetLinked(skillPtr, out var pair, out _))
        {
            var mask = 0;
            foreach (var slot in BayLink.Groups[pair])
                mask |= 1 << slot;
            return mask;
        }
        return 0;
    }
}
