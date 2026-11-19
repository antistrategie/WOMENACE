using System;
using System.Collections;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// A Doll carrying a tripod weapon in a mission holds it beside her head, as vanilla's soldier
// does, and her head is larger than his, so the weapon passes through it. Her right hand is
// held out clear of it by the armoury's solve (StrategyDiorama.ArmoryCarry.cs), which measures
// how far once per Doll, outfit and weapon. While the weapon is set up on its tripod it is out
// of her hand, and the hand is left to the animation.
//
// The carrier is always element 0, and element creation is where her weapon's model is mounted.
public sealed class TripodCarrySystem : JiangyuSystem
{
    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Tactical.Element", "CreateAttachments", OnCreateAttachments);
    }

    private void OnCreateAttachments(PatchInfo info)
    {
        try
        {
            var element = (info.Instance as Il2CppObjectBase)?.TryCast<Element>();
            if (element == null || info.Args == null || info.Args.Count < 2 || info.Args[0] is not int index || index != 0)
                return;
            // Attachments made again on a live element (a weapon changed in the mission) replace
            // the weapon her hand was held out with.
            StrategyDiorama.ReleaseInMission(element.transform);
            var items = (info.Args[1] as Il2CppObjectBase)?.TryCast<ItemContainer>();
            var id = items?.GetItemAtSlot(ItemSlot.InfantrySpecial)?.GetTemplate()?.GetID();
            if (id == null || !id.Contains(StrategyDiorama.CarryTripodId, StringComparison.Ordinal))
                return;
            var attachments = element.GetAttachments();
            if (attachments == null || !attachments.TryGetFirstAttachmentInSlot(VisualAlterationSlot.Hand_R_Special, out var weapon) || weapon == null)
            {
                Context.Log.Debug($"tripod carry: {id} carrier has no weapon in her hand");
                return;
            }
            Context.Coroutines.Start(HoldOnceKnown(element, weapon.transform, id));
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"tripod carry: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // A unit gone from the mission throws, and holds nothing.
    private static bool IsCarried(Actor actor)
    {
        try { return actor == null || actor.GetStance() != ActorStance.Deployed; }
        catch { return false; }
    }

    // Who carries the weapon is read from her entity's speaker, which the game may give it after
    // her attachments are made, so she is asked again a frame at a time for a while.
    private const int IdentifyFrames = 120;

    private IEnumerator HoldOnceKnown(Element element, Transform weapon, string id)
    {
        for (var frame = 0; frame < IdentifyFrames; frame++)
        {
            yield return null;
            string tag;
            Entity entity;
            try
            {
                if (element == null || element.WasCollected || weapon == null)
                    yield break;
                entity = element.GetEntity();
                tag = entity != null ? Affinity.CharacterTag(entity) : null;
            }
            catch (Exception ex)
            {
                Context.Log.Warn($"tripod carry: {ex.GetType().Name}: {ex.Message}");
                yield break;
            }
            if (entity == null)
                continue;
            // A carrier with no speaker of ours once her entity is known is not a Doll.
            if (tag == null)
            {
                Context.Log.Debug($"tripod carry: {id} carrier is not a Doll");
                yield break;
            }
            try
            {
                Animator body = null;
                foreach (var animator in element.GetComponentsInChildren<Animator>())
                    if (animator != null && animator.isHuman)
                    {
                        body = animator;
                        break;
                    }
                var actor = (entity as Il2CppObjectBase)?.TryCast<Actor>();
                StrategyDiorama.HoldInMission(body, weapon, $"{tag}|{Transmog.SelectionFor(Context, tag)}|{id}", () => IsCarried(actor));
                Context.Log.Debug($"tripod carry: holding {tag}'s {id} clear of her head (body {(body != null ? "found" : "missing")})");
            }
            catch (Exception ex)
            {
                Context.Log.Warn($"tripod carry: {ex.GetType().Name}: {ex.Message}");
            }
            yield break;
        }
        Context.Log.Debug($"tripod carry: {id} carrier never joined an entity");
    }
}
