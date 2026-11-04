using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Batchmode utility for the umbrella's carrier clips (Welrod's GFL2 ultimate, cut by
    // ExtractHumanoidClips -ranges).
    //
    // 1. Grip: where the umbrella has to sit in a doll's right-hand socket for the hold to
    //    match GFL2. Samples the hold on the raw GFL2 rig (a Generic import of the donor, so
    //    Weapon_02 is posed by the take) and the extracted muscle clip on a baked doll, then
    //    compares the umbrella against each body's own frame (built from shoulders and hips),
    //    which cancels the rigs' differing world axes. Prints the umbrella prefab root's
    //    local position and rotation under Hand_R_Socket.
    // 2. Retarget check: bone positions through every clip on two dolls, written as JSON
    //    for a stick-figure filmstrip (a check on the donor's own avatar proves nothing).
    //
    // Invoke:
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.MeasureUmbrellaGrip.Run -logFile grip.log
    public static class MeasureUmbrellaGrip
    {
        private const string Donor = "Assets/Authored/welrod/_donor/WelrodSSR01_combat.fbx";
        private const string Generic = "Assets/Authored/welrod/_donor/WelrodSSR01_generic.fbx";
        private const string Clips = "Assets/Authored/weapon/welrod_umbrella/carrier";
        private const string HoldTake = "c_WelrodSSR01_slg_UltraSkill_God_Main";
        private const float HoldTakeFrame = 40f;
        private const int HoldClipFirst = 22;
        private const string Umbrella = "Assets/Prefabs/weapon/welrod_umbrella/main.prefab";
        private static readonly string[] Dolls =
        {
            "Assets/Prefabs/welrod/default/main.prefab",
            "Assets/Prefabs/robella/default/main.prefab",
        };

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

        // Right, up, forward from the shoulders and hips.
        private static Quaternion BodyFrame(Vector3 lShoulder, Vector3 rShoulder, Vector3 lHip, Vector3 rHip)
        {
            var right = ((rShoulder - lShoulder) + (rHip - lHip)).normalized;
            var up = ((lShoulder + rShoulder) - (lHip + rHip)).normalized;
            up = (up - Vector3.Dot(up, right) * right).normalized;
            var forward = Vector3.Cross(right, up);
            return Quaternion.LookRotation(forward, up);
        }

        public static void Run()
        {
            if (!File.Exists(Generic))
                File.Copy(Donor, Generic);
            AssetDatabase.ImportAsset(Generic);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Generic);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.SaveAndReimport();

            // GFL2 rig at the hold
            var gflAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Generic);
            var take = AssetDatabase.LoadAllAssetRepresentationsAtPath(Generic).OfType<AnimationClip>()
                .First(c => c.name.Contains("UltraSkill_God_Main"));
            var gfl = Object.Instantiate(gflAsset);
            take.SampleAnimation(gfl, HoldTakeFrame / take.frameRate);
            var t = gfl.transform;
            var gWrist = Find(t, "Wrist_R");
            var gUmb = Find(t, "Weapon_02");
            var gBody = BodyFrame(Find(t, "Shoulder_L").position, Find(t, "Shoulder_R").position,
                Find(t, "Hip_L").position, Find(t, "Hip_R").position);
            var gHeight = Vector3.Distance(Find(t, "Root_M").position, Find(t, "Head_M").position);
            var umbInBody = Quaternion.Inverse(gBody) * gUmb.rotation;
            var umbOffsetInBody = Quaternion.Inverse(gBody) * (gUmb.position - gWrist.position) / gHeight;
            Debug.Log($"MeasureUmbrellaGrip: GFL2 take '{take.name}' at frame {HoldTakeFrame}, hips-head {gHeight:0.000}");

            var hold = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Clips}/Hold.anim");
            var holdTime = (HoldTakeFrame - HoldClipFirst) / take.frameRate;
            var report = new StringBuilder("{");
            foreach (var dollPath in Dolls)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(dollPath);
                if (asset == null)
                {
                    Debug.LogWarning($"MeasureUmbrellaGrip: no doll at {dollPath}");
                    continue;
                }
                var doll = Object.Instantiate(asset);
                var animator = doll.GetComponentInChildren<Animator>();
                hold.SampleAnimation(animator.gameObject, holdTime);
                Vector3 B(HumanBodyBones b) => animator.GetBoneTransform(b).position;
                var dBody = BodyFrame(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.RightUpperArm),
                    B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.RightUpperLeg));
                var dHeight = Vector3.Distance(B(HumanBodyBones.Hips), B(HumanBodyBones.Head));
                var socket = Find(doll.transform, "Hand_R_Socket");

                if (dollPath.Contains("/welrod/"))
                {
                    // where the GFL2 umbrella would be, in the doll's world
                    var wantRot = dBody * umbInBody;
                    var wantPos = animator.GetBoneTransform(HumanBodyBones.RightHand).position + dBody * umbOffsetInBody * dHeight;
                    // the umbrella prefab under the socket: solve its root so its Weapon_02 lands there
                    var umb = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Umbrella), socket);
                    umb.transform.localPosition = Vector3.zero;
                    umb.transform.localRotation = Quaternion.identity;
                    var w02 = Find(umb.transform, "Weapon_02");
                    var rootToW02 = umb.transform.worldToLocalMatrix * w02.localToWorldMatrix;
                    var want = Matrix4x4.TRS(wantPos, wantRot, Vector3.one);
                    var rootWorld = want * rootToW02.inverse;
                    var local = socket.worldToLocalMatrix * rootWorld;
                    var lp = (Vector3)local.GetColumn(3);
                    var lr = local.rotation;
                    Debug.Log($"MeasureUmbrellaGrip: GRIP localPosition=({lp.x:0.0000}f, {lp.y:0.0000}f, {lp.z:0.0000}f) "
                        + $"localRotation=({lr.x:0.000000}f, {lr.y:0.000000}f, {lr.z:0.000000}f, {lr.w:0.000000}f) "
                        + $"(doll hips-head {dHeight:0.000}, socket scale {socket.lossyScale.x:0.000})");
                    Object.DestroyImmediate(umb);
                }

                // retarget filmstrip data
                report.Append($"\"{Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(dollPath)))}\":{{");
                var first = true;
                foreach (var name in new[] { "Reach", "Swing", "Open", "Hold", "Fold", "Stow" })
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Clips}/{name}.anim");
                    if (!first) report.Append(',');
                    first = false;
                    report.Append($"\"{name}\":[");
                    for (var i = 0; i < 4; i++)
                    {
                        clip.SampleAnimation(animator.gameObject, clip.length * i / 3f);
                        report.Append(i == 0 ? "{" : ",{");
                        var bones = new[] { HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head,
                            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };
                        report.Append(string.Join(",", bones.Select(b =>
                        {
                            var p = animator.GetBoneTransform(b).position;
                            return $"\"{b}\":[{p.x:0.0000},{p.y:0.0000},{p.z:0.0000}]";
                        })));
                        report.Append('}');
                    }
                    report.Append(']');
                }
                report.Append("},");
                Object.DestroyImmediate(doll);
            }
            report.Length--;
            report.Append('}');
            var outPath = Path.Combine(Path.GetTempPath(), "umbrella_retarget.json");
            File.WriteAllText(outPath, report.ToString());
            Object.DestroyImmediate(gfl);
            Debug.Log($"MeasureUmbrellaGrip: wrote {outPath}");
        }
    }
}
