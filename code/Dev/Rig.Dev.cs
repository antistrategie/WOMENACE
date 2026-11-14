using Il2CppMenace.Tactical;
using Jiangyu.Game.Tactical;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Live rig inspection for the hand and arm bugs the offline asset checks cannot see.
// Dump reports, for every element on the field, where the hand bones, the weapon
// socket, the mounted weapon and its weapon_hand_l IK empty actually ARE, so a broken
// doll and a healthy unit in the same mission can be compared number by number.
// Every stage that can come up empty reports itself instead of silently dropping the row.
//
//   scripts/bridge.py verb Rig.Dump               every element on the field
//   scripts/bridge.py verb Rig.Dump '["klukai"]'  only actors whose template id contains the filter
[DevVerb]
public static class Rig
{
    // Every SkinnedMeshRenderer in the scene with arm elevations measured from ITS OWN
    // bones array: the renderer's rig is by construction the one the player sees, wherever
    // the body instance is parented. The element-based Dump measures whatever rig hangs
    // under Element.gameObject, which is not always the rendered one.
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
            rows.Add($"{path}\n  enabled={smr.enabled} visible={smr.isVisible} layers={smr.renderingLayerMask} bones={(bones == null ? 0 : bones.Length)}"
                + $" armL={Elev(uL, lL)} armR={Elev(uR, lR)}"
                + (hR != null ? $" handR@{Fmt(hR.position)}" : ""));
        }
        return string.Join("\n", rows);
    }

    // Shoots a close-up of one bone of the nth skinned mesh whose path contains the filter. A clone
    // of the main camera (keeping its HDRP data and volume, free of whatever drives the original)
    // renders the shot live for a few frames with the UI hidden, the screen is captured, then the
    // clone is destroyed. An offscreen Render of the armoury camera comes back black. The offset
    // is in the character's frame (her right, up, her facing), taken from her shoulders because
    // body roots do not share one facing axis.
    public static string Closeup(string filter, int nth, string bone, float right, float up, float forward, float fov, string path, bool hideUi = true)
    {
        // The strategy room's camera is not tagged MainCamera, so fall back to the deepest
        // enabled camera, which is the one drawing the screen.
        var camera = Camera.main;
        if (camera == null)
            foreach (var candidate in Camera.allCameras)
                if (candidate.enabled && (camera == null || candidate.depth > camera.depth))
                    camera = candidate;
        if (camera == null)
            return "no main camera";
        Transform target = null, shoulderL = null, shoulderR = null;
        var seen = 0;
        foreach (var smr in UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>())
        {
            var top = smr.transform;
            var name = smr.name;
            while (top.parent != null) { top = top.parent; name = top.name + "/" + name; }
            if (!name.ToLowerInvariant().Contains(filter.ToLowerInvariant()) || smr.bones == null)
                continue;
            Transform found = null, l = null, r = null;
            foreach (var b in smr.bones)
            {
                if (b == null) continue;
                if (b.name == bone) found = b;
                if (b.name == "UpperArm_L") l = b;
                if (b.name == "UpperArm_R") r = b;
            }
            if (found == null || l == null || r == null || seen++ < nth)
                continue;
            (target, shoulderL, shoulderR) = (found, l, r);
            break;
        }
        if (target == null)
            return $"no {bone} on a mesh matching {filter} (#{nth})";
        var her = shoulderR.position - shoulderL.position;
        her.y = 0f;
        her.Normalize();
        var facing = Vector3.Cross(her, Vector3.up);
        var context = StrategyDiorama.SystemContext;
        if (context == null)
            return "no context";
        var position = target.position + her * right + Vector3.up * up + facing * forward;
        context.Coroutines.Start(Shoot(camera, position, target.position, fov, path, hideUi));
        return $"shooting {path} at {Fmt(target.position)} facing {Fmt(facing)}";
    }

    // The strategy room from GFL2's own camera anchor, UI and the Dinergate hidden, for comparing
    // against a GFL2 capture of the same view.
    public static string Anchor(string path)
    {
        var room = StrategyDiorama.Room;
        var source = StrategyDiorama.RoomCamera;
        var anchor = room != null ? room.transform.Find("_CameraAnchor") : null;
        var context = StrategyDiorama.SystemContext;
        if (anchor == null || source == null || context == null)
            return "no room camera";
        var dinergate = GameObject.Find("WOMENACE_Dinergate");
        if (dinergate != null)
            dinergate.SetActive(false);
        source.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
        source.fieldOfView = StrategyDiorama.RoomFov;
        context.Coroutines.Start(ShootThenShow(source, anchor, path, dinergate));
        return $"shooting {path} from the room anchor";
    }

    private static System.Collections.IEnumerator ShootThenShow(Camera source, Transform anchor, string path, GameObject hidden)
    {
        yield return Shoot(source, anchor.position, anchor.position + anchor.forward, StrategyDiorama.RoomFov, path, true, false);
        if (hidden != null)
            hidden.SetActive(true);
    }

    private static System.Collections.IEnumerator Shoot(Camera source, Vector3 position, Vector3 lookAt, float fov, string path, bool hideUi, bool closeup = true)
    {
        var clone = UnityEngine.Object.Instantiate(source.gameObject);
        clone.name = "WOMENACE_CloseupCamera";
        var listener = clone.GetComponent<AudioListener>();
        if (listener != null)
            UnityEngine.Object.Destroy(listener);
        // Only the camera and HDRP's data and volume stay: a copied camera controller (the tactical
        // one) runs on the clone and throws every frame.
        foreach (var behaviour in clone.GetComponents<MonoBehaviour>())
        {
            var type = behaviour.GetIl2CppType().Name;
            if (type != "HDAdditionalCameraData" && type != "Volume")
                UnityEngine.Object.DestroyImmediate(behaviour);
        }
        var camera = clone.GetComponent<Camera>();
        camera.depth = source.depth + 50f;
        // A close-up drops the physical camera (its sensor overrides the field of view) and pulls the
        // near plane in, since strategy cameras clip anything close. A view shot keeps the source's
        // camera whole, whose physical settings also set its exposure.
        if (closeup)
        {
            camera.usePhysicalProperties = false;
            camera.fieldOfView = fov;
            camera.nearClipPlane = 0.05f;
            camera.useOcclusionCulling = false;
        }
        clone.transform.position = position;
        clone.transform.LookAt(lookAt);
        var hidden = hideUi ? Diorama.HideUi() : null;
        for (var i = 0; i < 15; i++)
            yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        UnityEngine.Object.Destroy(clone);
        if (hidden != null)
            Diorama.ShowUi(hidden);
        if (tex == null)
            yield break;
        System.IO.File.WriteAllBytes(path, (byte[])ImageConversion.EncodeToPNG(tex));
        UnityEngine.Object.Destroy(tex);
    }

    // Writes how every matching element stands right now, for replaying the hold outside the
    // game: each transform under the element (path, place and rotation in the element's own
    // frame, scale), each Animator's parameters and playing clips, and the weapon in its hand.
    // One file an element, named <template id>_<index>.txt, in the folder given.
    public static object Record(string folder, string filter = null)
    {
        System.IO.Directory.CreateDirectory(folder);
        var written = new List<string>();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var actor in Mission.Actors())
        {
            var id = actor?.GetTemplate()?.GetID() ?? "?";
            if (actor == null || (filter != null && !id.Contains(filter)))
                continue;
            var elements = actor.GetElements();
            for (var k = 0; elements != null && k < elements.Count; k++)
            {
                var element = elements[k];
                var root = element?.gameObject;
                if (root == null)
                    continue;
                var text = new System.Text.StringBuilder();
                text.Append("ELEMENT\t").Append(id).Append('\t').Append(k).Append('\n');
                GameObject weapon = null;
                try { element.GetAttachments()?.TryGetFirstAttachmentInSlot(Il2CppMenace.Items.VisualAlterationSlot.Hand_R, out weapon); } catch { }
                text.Append("WEAPON\t").Append(weapon != null ? PathFrom(root.transform, weapon.transform) : "none").Append('\n');
                foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                {
                    text.Append("ANIMATOR\t").Append(PathFrom(root.transform, animator.transform)).Append('\t').Append(animator.isHuman ? "human" : "generic")
                        .Append('\t').Append(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "none").Append('\n');
                    try
                    {
                        for (var index = 0; index < animator.parameterCount; index++)
                        {
                            var parameter = animator.GetParameter(index);
                            var value = parameter.type switch
                            {
                                AnimatorControllerParameterType.Float => animator.GetFloat(parameter.name).ToString("0.####", inv),
                                AnimatorControllerParameterType.Int => animator.GetInteger(parameter.name).ToString(inv),
                                _ => animator.GetBool(parameter.name) ? "1" : "0",
                            };
                            text.Append("PARAM\t").Append(parameter.name).Append('\t').Append(parameter.type.ToString()).Append('\t').Append(value).Append('\n');
                        }
                        for (var layer = 0; layer < animator.layerCount; layer++)
                        {
                            var infos = animator.GetCurrentAnimatorClipInfo(layer);
                            var clips = new List<string>();
                            for (var c = 0; infos != null && c < infos.Length; c++)
                                if (infos[c].clip != null)
                                    clips.Add($"{infos[c].clip.name}:{infos[c].weight.ToString("0.###", inv)}");
                            text.Append("LAYER\t").Append(layer).Append('\t').Append(animator.GetLayerName(layer)).Append('\t')
                                .Append(animator.GetLayerWeight(layer).ToString("0.###", inv)).Append('\t').Append(string.Join(",", clips)).Append('\n');
                        }
                    }
                    catch (Exception ex)
                    {
                        text.Append("ANIMATORERROR\t").Append(ex.GetType().Name).Append('\n');
                    }
                }
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var skinned = renderer.TryCast<SkinnedMeshRenderer>();
                    var mesh = skinned != null ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    var material = renderer.sharedMaterial;
                    text.Append("MESH\t").Append(PathFrom(root.transform, renderer.transform)).Append('\t').Append(mesh != null ? $"{mesh.name}:{mesh.vertexCount}" : "none")
                        .Append('\t').Append(material != null ? $"{material.name}:{material.shader?.name}" : "none").Append('\t').Append(renderer.enabled ? 1 : 0).Append('\n');
                }
                var back = Quaternion.Inverse(root.transform.rotation);
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var place = root.transform.InverseTransformPoint(t.position);
                    var turn = back * t.rotation;
                    var scale = t.lossyScale;
                    text.Append("T\t").Append(PathFrom(root.transform, t)).Append('\t')
                        .Append(string.Format(inv, "{0:0.#####} {1:0.#####} {2:0.#####}\t{3:0.######} {4:0.######} {5:0.######} {6:0.######}\t{7:0.####} {8:0.####} {9:0.####}\t{10}\n",
                            place.x, place.y, place.z, turn.x, turn.y, turn.z, turn.w, scale.x, scale.y, scale.z, t.gameObject.activeInHierarchy ? 1 : 0));
                }
                var file = System.IO.Path.Combine(folder, $"{id}_{k}.txt");
                System.IO.File.WriteAllText(file, text.ToString());
                written.Add($"{id}_{k}");
            }
        }
        return $"wrote {written.Count}: {string.Join(", ", written)}";
    }

    private static string PathFrom(Transform root, Transform t)
    {
        var path = t.name;
        for (var up = t.parent; up != null && up != root; up = up.parent)
            path = up.name + "/" + path;
        return t == root ? "." : path;
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
