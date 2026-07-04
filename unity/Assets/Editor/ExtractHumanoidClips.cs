using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Batchmode utility: import an animation-donor FBX as Humanoid, log the
    // avatar's human-bone mapping for verification, and extract every take as
    // a standalone muscle-clip .anim asset. Muscle clips are rig-independent,
    // so the extracted assets play on any humanoid avatar (the baked doll
    // prefabs) and the donor FBX itself never ships.
    //
    // Invoke:
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.ExtractHumanoidClips.Run \
    //     -fbx Assets/Authored/sextans/_donor/SextansSSR01_clean.fbx \
    //     -outDir Assets/Authored/sextans/clips \
    //     -stripPrefix c_SextansSSR0101_slg_
    public static class ExtractHumanoidClips
    {
        // Clips whose short name (after stripPrefix) matches one of these are
        // imported looping. Everything else is a one-shot.
        private static readonly string[] LoopNames =
        {
            "Idle", "Walk", "Run", "SupressedLoop",
            "HighCoverIdle_L", "HighCoverIdle_R",
            "LowCoverIdle_L", "LowCoverIdle_R",
        };

        // Rotate the upper arm so shoulder->wrist runs horizontal, keeping its
        // horizontal heading. The minimal lift: no assumptions about which
        // world axis is "out" for this rig, just remove the vertical droop.
        private static void LiftArmToHorizontal(Transform root, string upperArmName, string wristName)
        {
            var upper = FindDeep(root, upperArmName);
            var wrist = FindDeep(root, wristName);
            if (upper == null || wrist == null)
            {
                Debug.LogWarning($"ExtractHumanoidClips: T-pose lift skipped, missing {upperArmName}/{wristName}.");
                return;
            }
            var dir = wrist.position - upper.position;
            var flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 1e-10f || dir.sqrMagnitude < 1e-10f)
                return;
            var lift = Quaternion.FromToRotation(dir.normalized, flat.normalized);
            upper.rotation = lift * upper.rotation;
            Debug.Log($"ExtractHumanoidClips: lifted {upperArmName} by {Quaternion.Angle(Quaternion.identity, lift):0.0} degrees to horizontal.");
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
                return root;
            foreach (Transform child in root)
            {
                var hit = FindDeep(child, name);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            string Arg(string name, string fallback)
            {
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == name) return args[i + 1];
                return fallback;
            }

            var fbxPath = Arg("-fbx", null);
            var outDir = Arg("-outDir", null);
            var stripPrefix = Arg("-stripPrefix", string.Empty);
            var humanMapPath = Arg("-humanMap", null);
            if (string.IsNullOrEmpty(fbxPath) || string.IsNullOrEmpty(outDir))
            {
                Debug.LogError("ExtractHumanoidClips: -fbx and -outDir are required.");
                EditorApplication.Exit(1);
                return;
            }

            AssetDatabase.Refresh();
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"ExtractHumanoidClips: no model at '{fbxPath}'.");
                EditorApplication.Exit(1);
                return;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;

            // Unity's humanoid auto-mapper trusts bone NAMES over topology, and
            // GFL2 names its upper arm "Shoulder_L", so the whole arm chain
            // auto-maps shifted one joint (clavicle=upper arm, upper=forearm,
            // lower=a twist bone). An explicit "Human=Bone" mapping file
            // overrides the auto-map.
            if (!string.IsNullOrEmpty(humanMapPath))
            {
                var human = new List<HumanBone>();
                foreach (var raw in File.ReadAllLines(humanMapPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || !line.Contains('='))
                        continue;
                    var parts = line.Split('=', 2);
                    human.Add(new HumanBone
                    {
                        humanName = parts[0].Trim(),
                        boneName = parts[1].Trim(),
                        limit = new HumanLimit { useDefaultValues = true },
                    });
                }

                // A custom humanDescription is only honoured when it carries a
                // full skeleton array. With skeleton empty the importer runs
                // its auto-mapper and quietly discards the custom human list.
                // The skeleton is walked from the already-imported hierarchy's
                // default pose, with one adjustment: the upper arms are lifted
                // to horizontal (a synthetic T-pose). GFL2 binds in an A-pose,
                // and muscle calibration against an A-pose skeleton offsets
                // the arms in every converted clip.
                var modelRoot = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                var pose = Object.Instantiate(modelRoot);
                SkeletonBone[] skeleton;
                try
                {
                    pose.name = modelRoot.name;
                    LiftArmToHorizontal(pose.transform, "Shoulder_L", "Wrist_L");
                    LiftArmToHorizontal(pose.transform, "Shoulder_R", "Wrist_R");
                    var bones = new List<SkeletonBone>();
                    void Walk(Transform t)
                    {
                        bones.Add(new SkeletonBone
                        {
                            name = t.name,
                            position = t.localPosition,
                            rotation = t.localRotation,
                            scale = t.localScale,
                        });
                        foreach (Transform child in t)
                            Walk(child);
                    }
                    Walk(pose.transform);
                    skeleton = bones.ToArray();
                }
                finally
                {
                    Object.DestroyImmediate(pose);
                }

                importer.humanDescription = new HumanDescription
                {
                    human = human.ToArray(),
                    skeleton = skeleton,
                    upperArmTwist = 0.5f,
                    lowerArmTwist = 0.5f,
                    upperLegTwist = 0.5f,
                    lowerLegTwist = 0.5f,
                    armStretch = 0.05f,
                    legStretch = 0.05f,
                    feetSpacing = 0f,
                };
                Debug.Log($"ExtractHumanoidClips: explicit human map with {human.Count} bone(s), skeleton {skeleton.Length} node(s) from {humanMapPath}.");
            }

            // Per-take clip settings: name (prefix stripped), loop flag.
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                var shortName = clip.takeName.StartsWith(stripPrefix)
                    ? clip.takeName.Substring(stripPrefix.Length)
                    : clip.takeName;
                clip.name = shortName;
                clip.loopTime = LoopNames.Contains(shortName);
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            // Log the humanoid mapping the importer settled on so a mismap
            // (GFL2 names its upper arm "Shoulder_L", MENACE's clavicle name)
            // is visible in the bake log instead of surfacing as bent clips.
            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null)
            {
                Debug.LogError("ExtractHumanoidClips: import produced no Avatar.");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log($"ExtractHumanoidClips: avatar isHuman={avatar.isHuman} isValid={avatar.isValid}");
            foreach (var hb in avatar.humanDescription.human)
                Debug.Log($"ExtractHumanoidClips: MAP {hb.humanName} = {hb.boneName}");

            if (!Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            int extracted = 0;
            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath))
            {
                if (asset is not AnimationClip clip || clip.name.StartsWith("__preview__"))
                    continue;
                var copy = Object.Instantiate(clip);
                copy.name = clip.name;
                var assetPath = $"{outDir}/{clip.name}.anim";
                AssetDatabase.CreateAsset(copy, assetPath);
                extracted++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"ExtractHumanoidClips: extracted {extracted} clip(s) to {outDir}.");

            // Retarget spot-check: sample the extracted Idle muscle clip on the
            // baked doll prefab and report hand heights. A standing idle puts
            // hands near thigh height close to the body. Hands out at shoulder
            // height mean the donor avatar calibrated off an A-pose skeleton
            // and every clip will play with the arms offset.
            var validatePrefab = Arg("-validatePrefab", null);
            if (!string.IsNullOrEmpty(validatePrefab))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(validatePrefab);
                var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{outDir}/Idle.anim");
                if (prefab == null || idle == null)
                {
                    Debug.LogWarning("ExtractHumanoidClips: validation skipped (prefab or Idle.anim missing).");
                }
                else
                {
                    var instance = Object.Instantiate(prefab);
                    try
                    {
                        var animator = instance.GetComponentInChildren<Animator>();
                        idle.SampleAnimation(animator != null ? animator.gameObject : instance, 0.1f);
                        foreach (var name in new[] { "Hand_L", "Hand_R", "Head", "Hips" })
                        {
                            var t = FindDeep(instance.transform, name);
                            if (t != null)
                                Debug.Log($"ExtractHumanoidClips: VALIDATE {name} world=({t.position.x:+0.000;-0.000},{t.position.y:+0.000;-0.000},{t.position.z:+0.000;-0.000})");
                        }
                    }
                    finally
                    {
                        Object.DestroyImmediate(instance);
                    }
                }
            }
            EditorApplication.Exit(0);
        }
    }
}
