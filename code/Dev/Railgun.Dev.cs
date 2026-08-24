using System.Collections.Generic;
using Il2CppMenace.Tactical;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Dev verb for the railgun carry system, invoked over the dev-loader bridge as
// {verb: "Railgun.Probe"}. The carry swap is spread across sockets, renames and
// renderer toggles that are invisible in logs, so the probe reports the whole live
// state of every registered carrier.
[DevVerb]
public static class Railgun
{
    private static object NoSystem => new { error = "railgun carry system not initialised" };

    public static object Probe()
    {
        var system = RailgunCarrySystem.Instance;
        if (system == null)
            return NoSystem;
        var carriers = new List<object>();
        foreach (var (_, c) in system.Carriers)
        {
            // dictionaries, not anonymous types: the bridge serialiser falls back to
            // ToString on nested anonymous objects
            object Node(Transform t) => t == null ? null : new Dictionary<string, object>
            {
                ["name"] = t.name,
                ["path"] = Path(t),
                ["active"] = t.gameObject.activeInHierarchy,
                ["world"] = Vec(t.position),
                ["local"] = Vec(t.localPosition),
            };
            Transform back = null;
            GameObject backGo = null;
            c.Element?.GetAttachments()?.TryGetFirstAttachmentInSlot(
                Il2CppMenace.Items.VisualAlterationSlot.Back_Special, out backGo);
            back = backGo?.transform;
            var animator = c.HandGun?.GetComponentInChildren<Animator>(true)
                ?? c.HandGun?.GetComponent<Animator>();
            var spawn = c.Element?.GetSpawnpoint("muzzle", true, false);
            var handL = c.Element?.GetSpawnpoint("weapon_hand_l", true, false);
            carriers.Add(new Dictionary<string, object>
            {
                ["pending_deployed"] = c.PendingDeployed,
                ["hand_gun"] = Node(c.HandGun?.transform),
                ["hand_gun_scale"] = c.HandGun == null ? null : Vec(c.HandGun.transform.localScale),
                ["stowed"] = Node(c.Stowed?.transform),
                ["rifle"] = Node(c.Rifle?.transform),
                ["back_attachment"] = Node(back),
                ["railgun_muzzle"] = Node(c.RailgunMuzzle),
                ["rifle_muzzle"] = Node(c.RifleMuzzle),
                ["railgun_hand_l"] = Node(c.RailgunHandL),
                ["rifle_hand_l"] = Node(c.RifleHandL),
                ["resolved_muzzle"] = Node(spawn),
                ["resolved_hand_l"] = Node(handL),
                ["animator"] = animator == null ? null : Describe(animator),
            });
        }
        return new Dictionary<string, object> { ["count"] = carriers.Count, ["carriers"] = carriers };
    }

    // string-keyed animator getters marshal through a span API the interop build
    // lacks, so everything goes through precomputed hashes and each read is fenced
    private static object Describe(Animator animator)
    {
        object Guard(System.Func<object> read)
        {
            try { return read(); }
            catch (System.Exception ex) { return ex.GetType().Name; }
        }
        var stanceHash = Animator.StringToHash("Stance");
        return new Dictionary<string, object>
        {
            ["on"] = Guard(() => animator.gameObject.name),
            ["active"] = Guard(() => animator.isActiveAndEnabled),
            ["stance"] = Guard(() => animator.GetInteger(stanceHash)),
            ["state_hash"] = Guard(() => animator.GetCurrentAnimatorStateInfo(0).shortNameHash),
            ["controller"] = Guard(() => animator.runtimeAnimatorController?.name),
        };
    }

    private static string Path(Transform t)
    {
        var parts = new List<string>();
        for (var cursor = t; cursor != null && parts.Count < 12; cursor = cursor.parent)
            parts.Insert(0, cursor.name);
        return string.Join("/", parts);
    }

    private static float[] Vec(Vector3 v) => new[] { v.x, v.y, v.z };
}
