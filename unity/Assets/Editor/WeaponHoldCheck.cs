using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Batchmode utility: how a doll holds a weapon, without the game. Each line of the list
    // names a doll prefab, a weapon prefab and the driver parameters of a stance. The doll is
    // posed by her own controller, the weapon is mounted in her right-hand socket as the game
    // mounts it, and her left hand is brought to the weapon's weapon_hand_l empty by the
    // two-bone solve the armoury uses. The posed doll and the weapon are written as one OBJ
    // (objects "body" and "weapon") with a JSON of the hands, fingers and empties beside it,
    // for a renderer and a measuring script outside Unity: HDRP does not draw in batchmode here.
    //
    // A stance is better taken from the game: the controller alone does not give the pose the
    // game's own driver and IK leave a unit in. A line's last word may name a recording written
    // by the Rig.Record dev verb in place of parameters, and the doll then stands as that unit
    // stood: bone for bone where the recording is of the same doll, and by rotation alone
    // (her own bone lengths kept) where it is of another.
    //
    // List line: <name> <doll prefab> <weapon prefab> [Param=value,... | <recording>.txt] [move=x,y,z]
    //
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.WeaponHoldCheck.Run -list holds.txt -out /tmp/holds
    public static class WeaponHoldCheck
    {
        private const string Socket = "Hand_R_Socket";
        private const string LeftGrip = "weapon_hand_l";
        private const string Muzzle = "muzzle";
        // The game brings the left hand to the empty and turns it to the empty's rotation with
        // this turn after it, the same for every unit and weapon recorded.
        private static readonly Quaternion GripTurn = new Quaternion(0.4912f, 0.5087f, 0.5087f, 0.4912f).normalized;

        private static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
            HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
            HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
        };

        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == name) return args[i + 1];
                return null;
            }
            var list = Arg("-list");
            var outDir = Arg("-out");
            if (list == null || outDir == null)
            {
                Debug.LogError("WeaponHoldCheck: -list and -out required.");
                EditorApplication.Exit(1);
                return;
            }
            Directory.CreateDirectory(outDir);
            var failed = 0;
            foreach (var line in File.ReadAllLines(list))
            {
                var parts = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3 || line.StartsWith("#"))
                    continue;
                try
                {
                    Pose(parts[0], parts[1], parts[2], parts.Length > 3 ? parts[3] : "", parts.Length > 4 ? parts[4] : "", outDir);
                    Debug.Log($"WeaponHoldCheck: wrote {parts[0]}");
                }
                catch (System.Exception ex)
                {
                    failed++;
                    Debug.LogError($"WeaponHoldCheck: {parts[0]} failed: {ex.Message}");
                }
            }
            EditorApplication.Exit(failed > 0 ? 2 : 0);
        }

        private static void Pose(string name, string dollPath, string weaponPath, string parameters, string move, string outDir)
        {
            var dollPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(dollPath);
            var weaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(weaponPath);
            if (dollPrefab == null || weaponPrefab == null)
                throw new System.Exception($"missing prefab ({dollPath}, {weaponPath})");
            var doll = Object.Instantiate(dollPrefab);
            try
            {
                var socket = Find(doll.transform, Socket);
                var animator = doll.GetComponentInChildren<Animator>();
                if (socket == null || animator == null)
                    throw new System.Exception("no socket or animator");
                var weapon = Object.Instantiate(weaponPrefab, socket, false);
                weapon.transform.localPosition = Vector3.zero;
                weapon.transform.localRotation = Quaternion.identity;
                // A trial placement: the weapon's children (its mesh and its empties alike) moved
                // in the weapon's own frame, as moving them in its source file would.
                if (move.StartsWith("move="))
                {
                    var by = move.Substring(5).Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                    foreach (Transform child in weapon.transform)
                        child.localPosition += new Vector3(by[0], by[1], by[2]);
                }

                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var recorded = parameters.EndsWith(".txt");
                if (recorded)
                {
                    animator.enabled = false;
                    Stand(doll, parameters);
                }
                foreach (var pair in recorded ? new string[0] : parameters.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split('=');
                    var declared = animator.parameters.FirstOrDefault(p => p.name == kv[0]);
                    if (declared == null)
                        throw new System.Exception($"no parameter {kv[0]}");
                    var value = float.Parse(kv[1], CultureInfo.InvariantCulture);
                    switch (declared.type)
                    {
                        case AnimatorControllerParameterType.Float: animator.SetFloat(kv[0], value); break;
                        case AnimatorControllerParameterType.Int: animator.SetInteger(kv[0], (int)value); break;
                        case AnimatorControllerParameterType.Bool: animator.SetBool(kv[0], value != 0f); break;
                        case AnimatorControllerParameterType.Trigger: animator.SetTrigger(kv[0]); break;
                    }
                }
                for (var step = 0; step < (recorded ? 0 : 120); step++)
                    animator.Update(1f / 60f);

                var states = new List<string>();
                for (var layer = 0; layer < animator.layerCount; layer++)
                {
                    var infos = animator.GetCurrentAnimatorClipInfo(layer);
                    if (animator.GetLayerWeight(layer) > 0.001f && infos != null && infos.Length > 0 && infos[0].clip != null)
                        states.Add($"{animator.GetLayerName(layer)}:{infos[0].clip.name}");
                }

                var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                var hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                var grip = Find(weapon.transform, LeftGrip);
                var before = hand != null ? hand.position : Vector3.zero;
                var gameGap = grip != null && hand != null ? Vector3.Distance(hand.position, grip.position) : -1f;
                var gameTurn = grip != null && hand != null ? Quaternion.Angle(hand.rotation, grip.rotation * GripTurn) : -1f;
                var reach = 0f;
                var arm = 0f;
                if (grip != null && upper != null && lower != null && hand != null)
                {
                    arm = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
                    reach = Vector3.Distance(upper.position, grip.position);
                    Solve(upper, lower, hand, grip);
                }

                var json = new StringBuilder("{\n");
                json.Append($"  \"name\": \"{name}\",\n  \"states\": [{string.Join(", ", states.Select(s => $"\"{s}\""))}],\n");
                json.Append($"  \"armLength\": {F(arm)}, \"reach\": {F(reach)}, \"leftHandBefore\": {V(before)},\n");
                json.Append($"  \"handToGripBefore\": {F(gameGap)}, \"handTurnBefore\": {F(gameTurn)},\n");
                json.Append($"  \"hasLeftGrip\": {(grip != null ? "true" : "false")},\n  \"nodes\": {{\n");
                var nodes = new List<string>();
                void Node(string label, Transform t)
                {
                    if (t != null)
                        nodes.Add($"    \"{label}\": {{\"p\": {V(t.position)}, \"x\": {V(t.right)}, \"y\": {V(t.up)}, \"z\": {V(t.forward)}}}");
                }
                foreach (var bone in Bones)
                    Node(bone.ToString(), animator.GetBoneTransform(bone));
                Node("socket", socket);
                Node("weapon", weapon.transform);
                Node("grip", grip);
                Node("muzzle", Find(weapon.transform, Muzzle));
                json.Append(string.Join(",\n", nodes)).Append("\n  }\n}\n");
                File.WriteAllText(Path.Combine(outDir, name + ".json"), json.ToString());

                var obj = new StringBuilder();
                var offset = 1;
                WriteObject(obj, "body", Posed(doll, weapon.transform, false), ref offset);
                WriteObject(obj, "weapon", Posed(weapon, null, true), ref offset);
                WriteObject(obj, "hand_r", Posed(doll, weapon.transform, false, "Hand_R"), ref offset);
                WriteObject(obj, "hand_l", Posed(doll, weapon.transform, false, "Hand_L"), ref offset);
                File.WriteAllText(Path.Combine(outDir, name + ".obj"), obj.ToString());
            }
            finally
            {
                Object.DestroyImmediate(doll);
            }
        }

        // Puts the doll's transforms where a recording has them, by their paths under the root.
        // The recording names the unit's body by its first level of detail: where the doll has
        // the same, the recording is her own and every bone is placed. Another doll's bones are
        // only turned, and her hips set at the recorded height.
        private static void Stand(GameObject doll, string recording)
        {
            var inv = CultureInfo.InvariantCulture;
            var rows = File.ReadAllLines(recording).Select(l => l.Split('\t')).Where(r => r[0] == "T").ToArray();
            var body = rows.Select(r => r[1]).FirstOrDefault(path => path.EndsWith("_LOD0") && !path.Contains("/"));
            var own = body != null && doll.transform.Find(body) != null;
            foreach (var row in rows)
            {
                if (!row[1].StartsWith("Root"))
                    continue;
                var t = doll.transform.Find(row[1]);
                if (t == null)
                    continue;
                var p = row[2].Split(' ').Select(v => float.Parse(v, inv)).ToArray();
                var q = row[3].Split(' ').Select(v => float.Parse(v, inv)).ToArray();
                t.rotation = new Quaternion(q[0], q[1], q[2], q[3]);
                if (own || row[1] == "Root/Hips")
                    t.position = new Vector3(p[0], p[1], p[2]);
            }
            // The game's driver moves the weapon socket off the hand bone. On another doll it
            // is put where the recording has it in its own hand's frame.
            if (!own)
            {
                var hand = rows.FirstOrDefault(r => r[1].EndsWith("/Hand_R"));
                var socket = rows.FirstOrDefault(r => r[1].EndsWith("/Hand_R/" + Socket));
                var mine = Find(doll.transform, Socket);
                if (hand != null && socket != null && mine != null)
                {
                    Vector3 P(string[] r) { var v = r[2].Split(' ').Select(x => float.Parse(x, inv)).ToArray(); return new Vector3(v[0], v[1], v[2]); }
                    Quaternion Q(string[] r) { var v = r[3].Split(' ').Select(x => float.Parse(x, inv)).ToArray(); return new Quaternion(v[0], v[1], v[2], v[3]); }
                    mine.localPosition = Quaternion.Inverse(Q(hand)) * (P(socket) - P(hand));
                    mine.localRotation = Quaternion.Inverse(Q(hand)) * Q(socket);
                }
            }
            Debug.Log($"WeaponHoldCheck: {doll.name} stands as {Path.GetFileName(recording)} ({(own ? "her own recording" : "another doll's, by rotation")})");
        }

        // The first level of detail's meshes as they stand in the world, as vertices and triangles.
        // With a bone named, only the triangles whose corners all follow that bone most: a
        // doll's fingers have no bones of their own, so her hand is what follows her hand bone.
        private static (List<Vector3> Vertices, List<int> Triangles) Posed(GameObject root, Transform skip, bool all, string bone = null)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var group = root.GetComponent<LODGroup>() ?? root.GetComponentInChildren<LODGroup>();
            var renderers = group != null && group.GetLODs().Length > 0 && (!all || group.transform == root.transform || group.transform.IsChildOf(root.transform))
                ? group.GetLODs()[0].renderers
                : root.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || (skip != null && renderer.transform.IsChildOf(skip)))
                    continue;
                Mesh mesh = null;
                var owned = false;
                var matrix = renderer.transform.localToWorldMatrix;
                if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                {
                    mesh = new Mesh();
                    owned = true;
                    skinned.BakeMesh(mesh, true);
                }
                else if (renderer.TryGetComponent<MeshFilter>(out var filter))
                    mesh = filter.sharedMesh;
                if (mesh == null)
                    continue;
                var first = vertices.Count;
                foreach (var v in mesh.vertices)
                    vertices.Add(matrix.MultiplyPoint3x4(v));
                bool[] follows = null;
                if (bone != null)
                {
                    var rigged = renderer as SkinnedMeshRenderer;
                    var which = rigged != null ? System.Array.FindIndex(rigged.bones, b => b != null && b.name == bone) : -1;
                    var weights = rigged != null ? rigged.sharedMesh.boneWeights : null;
                    follows = new bool[mesh.vertexCount];
                    for (var i = 0; which >= 0 && weights != null && i < weights.Length && i < follows.Length; i++)
                        follows[i] = weights[i].boneIndex0 == which;
                }
                var corners = mesh.triangles;
                for (var i = 0; i + 2 < corners.Length; i += 3)
                {
                    if (follows != null && !(follows[corners[i]] && follows[corners[i + 1]] && follows[corners[i + 2]]))
                        continue;
                    triangles.Add(first + corners[i]);
                    triangles.Add(first + corners[i + 1]);
                    triangles.Add(first + corners[i + 2]);
                }
                if (owned)
                    Object.DestroyImmediate(mesh);
            }
            return (vertices, triangles);
        }

        // Unity is left-handed: x is negated and each triangle turned round for the OBJ.
        private static void WriteObject(StringBuilder obj, string name, (List<Vector3> Vertices, List<int> Triangles) mesh, ref int offset)
        {
            obj.Append("o ").Append(name).Append('\n');
            foreach (var v in mesh.Vertices)
                obj.Append("v ").Append(F(-v.x)).Append(' ').Append(F(v.y)).Append(' ').Append(F(v.z)).Append('\n');
            for (var i = 0; i + 2 < mesh.Triangles.Count; i += 3)
                obj.Append("f ").Append(mesh.Triangles[i] + offset).Append(' ').Append(mesh.Triangles[i + 2] + offset).Append(' ').Append(mesh.Triangles[i + 1] + offset).Append('\n');
            offset += mesh.Vertices.Count;
        }

        // The armoury's grip (StrategyDiorama.ArmoryGrip): the elbow opens or closes about the
        // axis the animation bends it round, the shoulder swings the hand onto the empty and
        // rolls the elbow back to the side it was on, and the hand takes the empty's rotation as the
        // game gives it.
        private static void Solve(Transform upper, Transform lower, Transform hand, Transform target)
        {
            Vector3 s = upper.position, e = lower.position, h = hand.position, t = target.position;
            float a = Vector3.Distance(s, e), b = Vector3.Distance(e, h);
            if (a < 1e-4f || b < 1e-4f || (s - t).sqrMagnitude < 1e-8f)
                return;
            var c = Mathf.Clamp(Vector3.Distance(s, t), Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
            var want = Mathf.Acos(Mathf.Clamp((a * a + b * b - c * c) / (2f * a * b), -1f, 1f)) * Mathf.Rad2Deg;
            var have = Vector3.Angle(s - e, h - e);
            var axis = Vector3.Cross(s - e, h - e);
            if (axis.sqrMagnitude < 1e-8f)
                axis = Vector3.Cross(s - e, Vector3.down);
            var turn = axis.sqrMagnitude < 1e-8f ? Quaternion.identity : Quaternion.AngleAxis(want - have, axis.normalized);
            h = e + turn * (h - e);
            var swing = Quaternion.FromToRotation(h - s, t - s);
            var swungElbow = s + swing * (e - s);
            var line = (t - s).normalized;
            var was = Vector3.ProjectOnPlane(e - s, line);
            var now = Vector3.ProjectOnPlane(swungElbow - s, line);
            var roll = was.sqrMagnitude > 1e-8f && now.sqrMagnitude > 1e-8f ? Quaternion.AngleAxis(Vector3.SignedAngle(now, was, line), line) : Quaternion.identity;
            lower.rotation = turn * lower.rotation;
            upper.rotation = roll * swing * upper.rotation;
            hand.rotation = target.rotation * GripTurn;
        }

        private static string F(float value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

        // Written in the OBJ's handedness, as the vertices are.
        private static string V(Vector3 v) => $"[{F(-v.x)}, {F(v.y)}, {F(v.z)}]";

        private static Transform Find(Transform node, string name)
        {
            if (node.name == name)
                return node;
            for (int i = 0; i < node.childCount; i++)
            {
                var hit = Find(node.GetChild(i), name);
                if (hit != null)
                    return hit;
            }
            return null;
        }
    }
}
