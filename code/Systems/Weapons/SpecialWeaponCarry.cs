using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppStem;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// What the carry systems for doll special weapons share (RailgunCarrySystem,
// UmbrellaCarrySystem). Each keeps the special on its back while the squad fights and puts
// the squad weapon in the carrier's hands, then swaps when the special comes out.
//
// The squad weapon in her hands is our own copy of its model on the special's hand socket.
// Element.AttachPrefab is no use for it: with a special equipped the carrier has no
// main-weapon attachment point to find the engine's copy on, and it cannot be found by
// name either (weapon and doll body prefabs are both named "main", so "main(Clone)" from
// the character's root finds her body). Left alone, that copy stayed visible and took
// her shots.
//
// Vanilla volleys leave element 0 out whenever the special slot is filled
// (ExecutingElement AllButSpecial); a carrier holding the squad weapon is put back in.
internal static class SpecialWeaponCarry
{
    internal const string MuzzleName = "muzzle";
    internal const string HandLName = "weapon_hand_l";

    // The squad weapon's model as worn, skin included.
    internal static GameObject SquadWeaponModel(ItemContainer items)
    {
        var template = (items?.GetItemAtSlot(ItemSlot.InfantryWeapon)?.GetTemplate()
            as Il2CppObjectBase)?.TryCast<WeaponTemplate>();
        return WeaponSkinSystem.ModelFor(items, ItemSlot.InfantryWeapon, template);
    }

    // Our copy of the squad weapon on a hand socket, named so it can be found again.
    internal static GameObject MountSquadWeapon(ItemContainer items, Transform socket, string nodeName)
    {
        var model = SquadWeaponModel(items);
        if (model == null || socket == null)
            return null;
        var copy = UnityEngine.Object.Instantiate(model, socket, false);
        copy.name = nodeName;
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        return copy;
    }

    // The armoury and squad viewer build their own preview attachments outside the tactical
    // path. The preview shows the standing look: the special's hand model (found by its rig)
    // hidden, the leader's squad weapon in her hands. Refresh calls find it already dressed.
    internal static void DressPreview(PatchInfo info, string handRigName, string nodeName)
    {
        var element = (info.Instance as Il2CppObjectBase)?.TryCast<Il2CppUI.PrefabControllers.ArmoryElement>();
        if (element == null)
            return;
        var hand = SceneQuery.FindNamed(element.gameObject, handRigName)?.parent;
        if (hand == null)
            return;
        hand.localScale = Vector3.zero;
        if (SceneQuery.FindNamed(element.gameObject, nodeName) != null)
            return;
        var leader = info.Args != null && info.Args.Count > 0
            ? (info.Args[0] as Il2CppObjectBase)?.TryCast<Il2CppMenace.Strategy.BaseUnitLeader>()
            : null;
        MountSquadWeapon(leader?.GetItems(), hand.parent, nodeName);
    }

    // Postfix body for Skill.GetExecutingElements: puts element 0 back into a squad-weapon
    // volley when joins(actor, element 0) says so. Random-pick filters are left alone: their
    // result is an already-chosen element, not a roster.
    internal static void JoinVolley(PatchInfo info, Func<Actor, Element, bool> joins)
    {
        if (info.Args == null || info.Args.Count < 1)
            return;
        var type = info.Args[0] switch
        {
            ExecutingElementType t => t,
            int i => (ExecutingElementType)i,
            _ => ExecutingElementType.All,
        };
        if (type != ExecutingElementType.AllButSpecial)
            return;
        var actor = (info.Instance as Il2CppObjectBase)?.TryCast<Skill>()?.GetActor();
        var element = actor?.GetElement(0);
        if (element == null || !element.IsAlive() || !joins(actor, element))
            return;
        if (info.Result is Il2CppSystem.Collections.Generic.List<int> result && !result.Contains(0))
            result.Add(0);
    }

    // Wrappers outlive their mission; entries whose element died with its scene go.
    internal static void Prune<T>(Dictionary<IntPtr, T> carriers, Func<T, Element> element)
    {
        List<IntPtr> stale = null;
        foreach (var (key, carrier) in carriers)
        {
            bool dead;
            try
            {
                var e = element(carrier);
                dead = e == null || e.WasCollected;
            }
            catch
            {
                dead = true;
            }
            if (dead)
                (stale ??= new List<IntPtr>()).Add(key);
        }
        if (stale != null)
            foreach (var key in stale)
                carriers.Remove(key);
    }

    internal static IEnumerator Wait(float seconds)
    {
        for (var waited = 0f; waited < seconds; waited += Time.deltaTime)
            yield return null;
    }

    // A sound from a KDL bank, at a weapon's position (m_Mesh is null on transmogged doll
    // bodies, so the weapon is the anchor). Ids resolve the way the loader resolves the KDL's.
    internal static void PlaySound(string bankId, string soundId, Transform at, Action<string> warn)
    {
        try
        {
            var sound = SoundManager.GetSoundInstance(new ID(SoundIds.FromName(bankId), SoundIds.FromName(soundId)));
            if (sound == null || at == null)
            {
                warn($"{soundId} not played (sound={(sound != null)}, anchor={(at != null)})");
                return;
            }
            sound.Play3D(at.position);
        }
        catch (Exception ex)
        {
            warn($"{soundId} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
