using Il2CppMenace.Tactical;
using Jiangyu.Game.Tactical;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Live rig inspection for the hand and arm bugs the offline asset checks cannot see.
// Dump reports, for every element on the field, where the hand bones, the weapon
// socket, the mounted weapon and its weapon_hand_l IK empty actually ARE, so a broken
// doll and a healthy unit in the same mission can be compared number by number.
// Every stage that can come up empty reports itself instead of silently dropping the
// row, because iterating on this verb costs the user a game restart per attempt.
//
//   scripts/bridge.py verb Rig.Dump               every element on the field
//   scripts/bridge.py verb Rig.Dump '["klukai"]'  only actors whose template id contains the filter
[DevVerb]
public static class Rig
{
    // Every SkinnedMeshRenderer in the scene with arm elevations measured from ITS OWN
    // bones array: the renderer's rig is by construction the one the player sees, wherever
    // the body instance is parented. The element-based Dump measures whatever rig hangs
    // under Element.gameObject, which live evidence says is NOT always the rendered one.
    public static object Smrs(string filter = null)
    {
        var rows = new List<string>();
        foreach (var smr in UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>())
        {
            var top = smr.transform;
            var path = smr.name;
            while (top.parent != null)
            {
                top = top.parent;
                path = top.name + "/" + path;
            }
            if (filter != null && !path.ToLowerInvariant().Contains(filter.ToLowerInvariant()))
                continue;
            Transform uL = null, lL = null, uR = null, lR = null, hR = null;
            var bones = smr.bones;
            for (var i = 0; bones != null && i < bones.Length; i++)
            {
                var b = bones[i];
                if (b == null) continue;
                switch (b.name)
                {
                    case "UpperArm_L": uL = b; break;
                    case "LowerArm_L": lL = b; break;
                    case "UpperArm_R": uR = b; break;
                    case "LowerArm_R": lR = b; break;
                    case "Hand_R": hR = b; break;
                }
            }
            rows.Add($"{path}\n  enabled={smr.enabled} visible={smr.isVisible} bones={(bones == null ? 0 : bones.Length)}"
                + $" armL={Elev(uL, lL)} armR={Elev(uR, lR)}"
                + (hR != null ? $" handR@{Fmt(hR.position)}" : ""));
        }
        return string.Join("\n", rows);
    }

    public static object Dump(string filter = null)
    {
        var rows = new List<string>();
        var actors = Mission.Actors();
        var seen = 0;
        foreach (var actor in actors)
        {
            if (actor == null)
                continue;
            var id = actor.GetTemplate()?.GetID() ?? "?";
            if (filter != null && !id.Contains(filter))
                continue;
            seen++;
            var elements = actor.GetElements();
            var firstGo = elements != null && elements.Count > 0 && elements[0] != null ? elements[0].gameObject : null;
            if (firstGo != null)
            {
                var top = firstGo.transform; var path = firstGo.name;
                while (top.parent != null) { top = top.parent; path = top.name + "/" + path; }
                rows.Add($"{id} element0 path: {path}");
            }
            if (elements == null || elements.Count == 0)
            {
                var unit = actor.TryCast<UnitActor>();
                elements = unit?.GetElements();
            }
            if (elements == null || elements.Count == 0)
            {
                rows.Add($"{id}: no elements");
                continue;
            }
            for (var k = 0; k < elements.Count; k++)
            {
                var element = elements[k];
                // Element is a MonoBehaviour: its own GameObject roots the rig. The
                // m_Mesh field reads null over interop at this lifecycle.
                var root = element?.gameObject;
                if (root == null)
                {
                    rows.Add($"{id}[{k}]: element null");
                    continue;
                }
                rows.Add(Describe(id, k, root, element));
            }
        }
        return $"actors={actors.Count} matched={seen}\n" + string.Join("\n", rows);
    }

    private static string Describe(string id, int index, GameObject root, Element element)
    {
        // Transmog gives a doll element TWO rigs carrying the same bone names: the
        // vanilla carrier skeleton that animates, and the attached doll body that
        // renders. Report every match separately, tagged by its top-level ancestor
        // under the element, so the two can be compared.
        var extras = new List<string>();
        foreach (var name in new[] { "Hand_L", "Hand_R", "UpperArm_L", "UpperArm_R", "Hand_R_Socket" })
        {
            var all = FindAll(root, name);
            if (all.Count > 1)
                for (var i = 0; i < all.Count; i++)
                    extras.Add($"    {name}#{i} under [{TopAncestor(root, all[i])}] {Basis(all[i])} pos {Fmt(all[i].position)}");
        }
        var handL = Find(root, "Hand_L");
        var handR = Find(root, "Hand_R");
        var socket = Find(root, "Hand_R_Socket");
        var upperL = Find(root, "UpperArm_L");
        var lowerL = Find(root, "LowerArm_L");
        var upperR = Find(root, "UpperArm_R");
        var lowerR = Find(root, "LowerArm_R");

        GameObject weapon = null;
        try
        {
            element.GetAttachments()?.TryGetFirstAttachmentInSlot(
                Il2CppMenace.Items.VisualAlterationSlot.Hand_R, out weapon);
        }
        catch (Exception ex)
        {
            return $"{id}[{index}]: attachments threw {ex.GetType().Name}";
        }
        var weaponHandL = weapon != null ? Find(weapon, "weapon_hand_l") : null;

        var ik = handL != null && weaponHandL != null
            ? Quaternion.Angle(handL.rotation, weaponHandL.rotation).ToString("F1")
            : "n/a";
        return $"{id}[{index}] armL={Elev(upperL, lowerL)} armR={Elev(upperR, lowerR)}"
            + $"\n  handL {Basis(handL)}\n  handR {Basis(handR)}\n  socket {Basis(socket)}"
            + $"\n  weapon {weapon?.name ?? "none"} local="
            + (weapon == null ? "n/a" : $"pos {Fmt(weapon.transform.localPosition)} rot {Fmt(weapon.transform.localRotation.eulerAngles)}")
            + $"\n  ikEmpty {Basis(weaponHandL)}  handLvsEmpty={ik}"
            + (extras.Count > 0 ? "\n  DUAL-RIG:\n" + string.Join("\n", extras) : "");
    }

    private static List<Transform> FindAll(GameObject root, string name)
    {
        var found = new List<Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
            if (t.name == name)
                found.Add(t);
        return found;
    }

    private static string TopAncestor(GameObject root, Transform t)
    {
        var cur = t;
        while (cur.parent != null && cur.parent.gameObject != root)
            cur = cur.parent;
        return cur.name;
    }

    // The bone's world axes, which carry the palm and thumb: palm is +X local on the
    // right hand and -X local on the left, fingers are +Y.
    private static string Basis(Transform t)
    {
        if (t == null)
            return null;
        return $"X>{Fmt(t.right)} Y>{Fmt(t.up)} Z>{Fmt(t.forward)}";
    }

    private static string Elev(Transform upper, Transform lower)
    {
        if (upper == null || lower == null)
            return null;
        var v = (lower.position - upper.position).normalized;
        return Mathf.Round(Mathf.Asin(Mathf.Clamp(v.y, -1f, 1f)) * Mathf.Rad2Deg).ToString();
    }

    private static string Fmt(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";

    private static Transform Find(GameObject root, string name)
    {
        if (root == null)
            return null;
        foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
            if (t.name == name)
                return t;
        return null;
    }
}
