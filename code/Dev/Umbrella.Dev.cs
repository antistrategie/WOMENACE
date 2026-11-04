using System.Collections.Generic;
using Il2CppMenace.Items;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Dev verb for the umbrella carry system, {verb: "Umbrella.Probe"}: everything mounted on
// each carrier's hands and back, which copies are visible, where the engine resolves the
// muzzle and the off hand, the umbrella Animator's state and which controller her body runs.
[DevVerb]
public static class Umbrella
{
    public static object Probe()
    {
        var system = UmbrellaCarrySystem.Instance;
        if (system == null)
            return new { error = "umbrella carry system not initialised" };
        var carriers = new List<object>();
        foreach (var (_, c) in system.Carriers)
        {
            object Node(Transform t) => t == null ? null : new Dictionary<string, object>
            {
                ["name"] = t.name,
                ["path"] = Path(t),
                ["active"] = t.gameObject.activeInHierarchy,
                ["scale"] = t.lossyScale.ToString("F2"),
            };
            var hand = c.Umbrella?.transform.parent;
            var handChildren = new List<object>();
            for (var i = 0; i < (hand?.childCount ?? 0); i++)
                handChildren.Add(Node(hand.GetChild(i)));
            var renderers = new List<object>();
            var root = c.Body != null ? c.Body.transform : c.Umbrella?.transform.root;
            if (root != null)
                foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                    if (r.enabled && r.transform.lossyScale.sqrMagnitude > 1e-6f && Path(r.transform).Contains("Hand_R"))
                        renderers.Add(Path(r.transform));
            Animator animator = null;
            try { animator = c.Umbrella?.GetComponentInChildren<Animator>(true); } catch { }
            object open = null, state = null;
            try { open = animator?.GetBool(Animator.StringToHash("UmbrellaOpen")); } catch (System.Exception ex) { open = ex.GetType().Name; }
            try { state = animator?.GetCurrentAnimatorStateInfo(0).shortNameHash; } catch (System.Exception ex) { state = ex.GetType().Name; }
            carriers.Add(new Dictionary<string, object>
            {
                ["open"] = c.Open,
                ["umbrella"] = Node(c.Umbrella?.transform),
                ["back"] = Node(c.Back?.transform),
                ["rifle"] = Node(c.Rifle?.transform),
                ["hand_children"] = handChildren,
                ["visible_hand_renderers"] = renderers,
                ["resolved_muzzle"] = Node(c.Element?.GetSpawnpoint("muzzle", true, false)),
                ["resolved_hand_l"] = Node(c.Element?.GetSpawnpoint("weapon_hand_l", true, false)),
                ["animator_open"] = open,
                ["animator_state"] = state,
                ["body_controller"] = c.Body?.runtimeAnimatorController?.name,
            });
        }
        // the bridge serialiser only renders one level, so everything goes out as JSON text
        return System.Text.Json.JsonSerializer.Serialize(carriers, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private static string Path(Transform t)
    {
        var parts = new List<string>();
        for (var x = t; x != null && parts.Count < 12; x = x.parent)
            parts.Insert(0, x.name);
        return string.Join("/", parts);
    }
}
