using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Batchmode utility: turns the umbrella upright in the carrier clips. GFL2 holds it out
    // sideways at the end of the thrust; here she holds the thrust, then Raise brings her arm
    // up so the shaft stands up (leaning HoldLean forward, canopy over her) for Hold.
    //
    // The pose is found on a baked doll with the umbrella in her grip (UmbrellaCarrySystem's
    // GripPosition/GripRotation): a search over the right arm, elbow and wrist muscles for
    // the pose that points the shaft (Weapon_02 to the runner) at the target, each muscle
    // penalised for straying from GFL2's pose so no single joint is maxed out. Hold gets
    // those muscles constant; Raise keeps GFL2's thrust for RaiseHoldShare of its length and
    // eases into them over the rest.
    //
    // Run after ExtractHumanoidClips -ranges and before BuildUmbrellaCarrier:
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.TuneUmbrellaHold.Run -logFile hold.log
    public static class TuneUmbrellaHold
    {
        private const string Clips = "Assets/Authored/weapon/welrod_umbrella/carrier";
        private const string Doll = "Assets/Prefabs/welrod/default/main.prefab";
        private const string Umbrella = "Assets/Prefabs/weapon/welrod_umbrella/main.prefab";
        private static readonly Vector3 GripPosition = new(0.0514f, 0.0643f, 0.0463f);
        private static readonly Quaternion GripRotation = new(0.193306f, -0.448133f, -0.567993f, 0.662717f);
        private const float HoldLean = 15f;
        // Raise holds the thrust for this share of its length, then eases the arm up over the
        // rest (its state also plays slowed, see BuildUmbrellaCarrier).
        private const float RaiseHoldShare = 0.4f;
        private static readonly string[] Muscles =
        {
            "Right Arm Down-Up", "Right Arm Front-Back", "Right Arm Twist In-Out", "Right Forearm Stretch",
            "Right Forearm Twist In-Out", "Right Hand Down-Up", "Right Hand In-Out",
        };
        // Degrees of shaft error each muscle may trade for moving one unit (its full range)
        // off GFL2's pose, squared. The wrist is stiffest: an umbrella held up is held by the
        // arm, elbow bent and forearm rising, not by a wrist bent back as far as it goes.
        private static readonly float[] Stiffness = { 6f, 6f, 8f, 4f, 10f, 30f, 30f };

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name)
                return root;
            foreach (Transform c in root)
            {
                var hit = Find(c, name);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        public static void Run()
        {
            var doll = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Doll));
            var animator = doll.GetComponentInChildren<Animator>();
            var socket = Find(doll.transform, "Hand_R_Socket");
            var umb = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Umbrella), socket);
            umb.transform.localPosition = GripPosition;
            umb.transform.localRotation = GripRotation;
            var shaftBase = Find(umb.transform, "Weapon_02");
            var runner = Find(umb.transform, "weapon_16");

            var hold = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Clips}/Hold.anim");
            var raise = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Clips}/Raise.anim");
            hold.SampleAnimation(animator.gameObject, 0f);

            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            var names = HumanTrait.MuscleName;
            var idx = Muscles.Select(m => System.Array.IndexOf(names, m)).ToArray();
            if (idx.Any(i => i < 0))
                throw new System.Exception("muscle name not found: " + string.Join(", ", Muscles));

            // the body's frame, for "up, leaning forward"
            Vector3 B(HumanBodyBones b) => animator.GetBoneTransform(b).position;
            var right = ((B(HumanBodyBones.RightUpperArm) - B(HumanBodyBones.LeftUpperArm))
                + (B(HumanBodyBones.RightUpperLeg) - B(HumanBodyBones.LeftUpperLeg))).normalized;
            var up = Vector3.up;
            var forward = Vector3.Cross(right, up).normalized;
            var target = Quaternion.AngleAxis(HoldLean, right) * up;
            if (Vector3.Dot(target, forward) < 0)
                target = Quaternion.AngleAxis(-HoldLean, right) * up;

            float[] start = null;
            float Angle(float[] m)
            {
                var p = pose;
                p.muscles = (float[])pose.muscles.Clone();
                for (var k = 0; k < idx.Length; k++)
                    p.muscles[idx[k]] = m[k];
                handler.SetHumanPose(ref p);
                return Vector3.Angle((runner.position - shaftBase.position).normalized, target);
            }
            float Score(float[] m)
            {
                var p = pose;
                p.muscles = (float[])pose.muscles.Clone();
                for (var k = 0; k < idx.Length; k++)
                    p.muscles[idx[k]] = m[k];
                handler.SetHumanPose(ref p);
                var shaft = (runner.position - shaftBase.position).normalized;
                var cost = Vector3.Angle(shaft, target);
                for (var k = 0; k < idx.Length; k++)
                    cost += Stiffness[k] * (m[k] - start[k]) * (m[k] - start[k]);
                return cost;
            }

            start = idx.Select(i => pose.muscles[i]).ToArray();
            var before = Score(start);
            var best = (float[])start.Clone();
            var bestScore = before;
            // coordinate descent: each muscle in turn, shrinking steps
            for (var step = 0.3f; step >= 0.005f; step *= 0.6f)
            {
                var improved = true;
                while (improved)
                {
                    improved = false;
                    for (var k = 0; k < best.Length; k++)
                        foreach (var dir in new[] { -1f, 1f })
                        {
                            var m = (float[])best.Clone();
                            m[k] = Mathf.Clamp(m[k] + dir * step, -1.5f, 1.5f);
                            var sc = Score(m);
                            if (sc < bestScore - 1e-4f)
                            {
                                bestScore = sc;
                                best = m;
                                improved = true;
                            }
                        }
                }
            }
            Debug.Log($"TuneUmbrellaHold: shaft off target {Angle(start):0.0} -> {Angle(best):0.0} deg; muscles "
                + string.Join(", ", Muscles.Select((n, k) => $"{n} {start[k]:0.000}->{best[k]:0.000}")));

            // Hold: those muscles constant. Raise: GFL2's thrust held, then eased up into them.
            foreach (var clip in new[] { hold, raise })
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var k = System.Array.IndexOf(Muscles, binding.propertyName);
                    if (k < 0)
                        continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    var keys = curve.keys;
                    if (clip == hold)
                    {
                        curve = AnimationCurve.Constant(0f, clip.length, best[k]);
                    }
                    else
                    {
                        var blendStart = clip.length * RaiseHoldShare;
                        var fresh = new AnimationCurve();
                        foreach (var key in keys)
                        {
                            if (key.time <= blendStart)
                                fresh.AddKey(key.time, key.value);
                        }
                        var from = curve.Evaluate(blendStart);
                        var steps = Mathf.Max(2, Mathf.RoundToInt((clip.length - blendStart) * clip.frameRate));
                        for (var f = 1; f <= steps; f++)
                        {
                            var u = f / (float)steps;
                            var eased = u * u * (3f - 2f * u);
                            fresh.AddKey(blendStart + (clip.length - blendStart) * u, Mathf.Lerp(from, best[k], eased));
                        }
                        curve = fresh;
                    }
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
                EditorUtility.SetDirty(clip);
            }
            AssetDatabase.SaveAssets();
            Object.DestroyImmediate(doll);
        }
    }
}
