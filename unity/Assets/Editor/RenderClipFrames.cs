using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Womenace.EditorTools
{
    // Offline retarget-quality harness: instantiate a character prefab,
    // sample an AnimationClip at chosen normalised times, render each frame
    // to PNG, and report the lowest skinned vertex per frame (ground
    // penetration). Runs in batchmode WITH graphics (no -nographics).
    //
    //   Unity -batchmode -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.RenderClipFrames.Run \
    //     -prefab Assets/Prefabs/sextans/default/main.prefab \
    //     -clip Assets/Authored/sextans/clips/Run.anim \
    //     -times 0.0,0.25,0.5,0.75 -lod 0 -out /tmp/frames
    public static class RenderClipFrames
    {
        private static bool _camPlaced;

        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            string Arg(string name, string fallback)
            {
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == name) return args[i + 1];
                return fallback;
            }

            var prefabPath = Arg("-prefab", null);
            var clipPath = Arg("-clip", null);
            var outDir = Arg("-out", null);
            var lod = int.Parse(Arg("-lod", "0"));
            var speed = float.Parse(Arg("-speed", "-1"));
            var times = Arg("-times", "0.0,0.5").Split(',').Select(float.Parse).ToArray();
            if (prefabPath == null || outDir == null || (clipPath == null && speed < 0))
            {
                Debug.LogError("RenderClipFrames: -prefab, -out and (-clip or -speed) required.");
                EditorApplication.Exit(1);
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var clip = clipPath != null ? AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath) : null;
            if (prefab == null || (clipPath != null && clip == null))
            {
                Debug.LogError($"RenderClipFrames: prefab or clip missing ({prefabPath}, {clipPath}).");
                EditorApplication.Exit(1);
                return;
            }
            System.IO.Directory.CreateDirectory(outDir);

            var instance = Object.Instantiate(prefab);
            var lodGroup = instance.GetComponentInChildren<LODGroup>();
            if (lodGroup != null)
                lodGroup.ForceLOD(lod);

            // -attach Assets/.../weapon.prefab:Hand_R mounts a prefab at a
            // named bone with identity local TRS, mirroring how the game
            // parents an equipped weapon model, so grip alignment can be
            // checked offline against the character's actual hand pose.
            var attachSpec = Arg("-attach", null);
            Transform socket = null;
            if (attachSpec != null)
            {
                var sep = attachSpec.LastIndexOf(':');
                var attachPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(attachSpec.Substring(0, sep));
                socket = FindChildDeep(instance.transform, attachSpec.Substring(sep + 1));
                if (attachPrefab == null || socket == null)
                {
                    Debug.LogError($"RenderClipFrames: bad -attach '{attachSpec}' (prefab={attachPrefab}, socket={socket}).");
                    EditorApplication.Exit(1);
                    return;
                }
                var attached = Object.Instantiate(attachPrefab, socket, false);
                attached.transform.localPosition = Vector3.zero;
                attached.transform.localRotation = Quaternion.identity;
                // -attachRot x,y,z: local euler offset for the attachment, to
                // trial mounting orientations before baking one into the GLB
                var attachRot = Arg("-attachRot", null);
                if (attachRot != null)
                {
                    var e = attachRot.Split(',').Select(float.Parse).ToArray();
                    attached.transform.localRotation = Quaternion.Euler(e[0], e[1], e[2]);
                }
                // flat-lit batchmode renders read as silhouettes, so tint the
                // attachment to keep it legible against the character
                var tint = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.9f, 0.2f, 0.2f) };
                foreach (var r in attached.GetComponentsInChildren<Renderer>())
                    r.sharedMaterial = tint;
            }

            var light = new GameObject("KeyLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);
            cam.fieldOfView = 35f;

            var rt = new RenderTexture(700, 1000, 24);
            cam.targetTexture = rt;

            // Sample through the Animator via a PlayableGraph. Raw
            // AnimationClip.SampleAnimation applies humanoid curves without
            // the Animator's retarget/root handling, which offsets the hips
            // baseline (every character "sinks" ~1m) and makes ground
            // numbers meaningless.
            var animator = instance.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogError("RenderClipFrames: prefab has no Animator.");
                EditorApplication.Exit(1);
                return;
            }
            // the prefab ships CullUpdateTransforms: an offscreen batchmode
            // animator skips pose application entirely (the in-game armoury
            // sets this too before animating its preview)
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // Two modes. Clip mode: play one clip through the Animator via a
            // PlayableGraph. Controller mode (-speed >= 0): run the prefab's
            // OWN AnimatorController with the driver's Speed parameter set,
            // stepping real frames, so the full layer stack (additive aim /
            // shooting layers included) evaluates exactly as it would in-game
            // minus MENACE's IK components. Discriminates controller-stack
            // defects from runtime-IK defects.
            string clipName;
            var graph = default(PlayableGraph);
            var playable = default(AnimationClipPlayable);
            bool controllerMode = speed >= 0;
            if (controllerMode)
            {
                clipName = $"controller_speed{speed:0.0}";
                // -zeroLayers 1: force every non-base layer to weight 0, to
                // bisect which layer poisons the humanoid pose evaluation
                if (Arg("-zeroLayers", "0") == "1")
                {
                    for (int li = 1; li < animator.layerCount; li++)
                        animator.SetLayerWeight(li, 0f);
                    clipName += "_zeroLayers";
                }
                animator.SetFloat("Speed", speed);
                animator.SetBool("IsMoving", speed > 0.01f);
                // -params "Name=1,Other=0.5" sets extra driver parameters by
                // declared type, so stance/aim paths can be exercised offline.
                var extraParams = Arg("-params", "");
                foreach (var pair in extraParams.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split('=');
                    var declared = animator.parameters.FirstOrDefault(p => p.name == kv[0]);
                    if (declared == null)
                    {
                        Debug.LogWarning($"RenderClipFrames: no parameter '{kv[0]}'.");
                        continue;
                    }
                    switch (declared.type)
                    {
                        case AnimatorControllerParameterType.Float: animator.SetFloat(kv[0], float.Parse(kv[1])); break;
                        case AnimatorControllerParameterType.Int: animator.SetInteger(kv[0], int.Parse(kv[1])); break;
                        case AnimatorControllerParameterType.Bool: animator.SetBool(kv[0], float.Parse(kv[1]) != 0f); break;
                        case AnimatorControllerParameterType.Trigger: animator.SetTrigger(kv[0]); break;
                    }
                    clipName += $"_{kv[0]}{kv[1]}";
                }
                animator.Update(0.5f); // settle into the target state
            }
            else
            {
                clipName = System.IO.Path.GetFileNameWithoutExtension(clipPath);
                graph = PlayableGraph.Create("RenderClipFrames");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playableOutput = AnimationPlayableOutput.Create(graph, "out", animator);
                playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                playableOutput.SetSourcePlayable(playable);
                // the graph does not write its output on the first
                // evaluation cycle (single-sample renders came out in rest
                // pose): warm it up with real ticks before sampling
                graph.Play();
                for (int warm = 0; warm < 4; warm++)
                    graph.Evaluate(1f / 60f);
            }

            float prevT = 0f;
            foreach (var t in times)
            {
                if (controllerMode)
                {
                    // advance in real steps so transitions/blends progress
                    float delta = Mathf.Max(0.001f, (t - prevT) * 2f);
                    for (float stepped = 0; stepped < delta; stepped += 1f / 60f)
                        animator.Update(1f / 60f);
                    prevT = t;
                }
                else
                {
                    // the first Evaluate after graph creation does not write
                    // the output (single-sample renders came out in rest
                    // pose), so evaluate twice: idempotent and reliable
                    playable.SetTime(Mathf.Clamp01(t) * clip.length);
                    graph.Evaluate(0f);
                    graph.Evaluate(0f);
                }

                // frame on the skinned bounds
                var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                var bounds = new Bounds(instance.transform.position + Vector3.up, Vector3.one * 0.5f);
                float lowestY = float.MaxValue;
                bool first = true;
                foreach (var r in renderers)
                {
                    var baked = new Mesh();
                    r.BakeMesh(baked);
                    var verts = baked.vertices;
                    foreach (var v in verts)
                    {
                        var w = r.transform.TransformPoint(v);
                        if (w.y < lowestY) lowestY = w.y;
                        if (first) { bounds = new Bounds(w, Vector3.zero); first = false; }
                        else bounds.Encapsulate(w);
                    }
                    Object.DestroyImmediate(baked);
                }
                if (controllerMode)
                {
                    for (int li = 0; li < animator.layerCount; li++)
                    {
                        var w = animator.GetLayerWeight(li);
                        var infos = animator.GetCurrentAnimatorClipInfo(li);
                        var playing = infos != null && infos.Length > 0 && infos[0].clip != null ? infos[0].clip.name : "-";
                        if (w > 0.001f || playing != "-")
                            Debug.Log($"RenderClipFrames: LAYER {li} '{animator.GetLayerName(li)}' weight={w:0.00} clip={playing}");
                    }
                }
                if (socket != null)
                {
                    // socket basis in world space: what the attachment's
                    // local axes resolve to under an identity local TRS
                    Debug.Log($"RenderClipFrames: SOCKET '{socket.name}' t={t:0.00} pos={socket.position:F3} "
                        + $"X->{socket.right:F3} Y->{socket.up:F3} Z->{socket.forward:F3} charFwd={instance.transform.forward:F3}");
                }
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var footL = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Debug.Log($"RenderClipFrames: {clipName} t={t:0.00} lowestVertexY={lowestY:+0.000;-0.000} boundsY=[{bounds.min.y:0.00},{bounds.max.y:0.00}] hipsY={(hips ? hips.position.y : -99f):0.000} footLY={(footL ? footL.position.y : -99f):0.000} rootY={animator.transform.position.y:0.000} bodyY={animator.bodyPosition.y:0.000}");

                var centre = bounds.center;
                var size = Mathf.Max(bounds.size.y, bounds.size.x) * 1.15f;
                var dist = size / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                // -camYaw rotates the view direction around the character
                // (degrees, 0 = the default three-quarter angle). -fixedCam 1
                // frames the first sample and holds, so frame sequences can
                // be assembled into a steady animation.
                var camYaw = float.Parse(Arg("-camYaw", "0"));
                if (Arg("-fixedCam", "0") != "1" || !_camPlaced)
                {
                    var viewDir = Quaternion.Euler(0f, camYaw, 0f) * new Vector3(0.4f, 0.05f, -1f).normalized;
                    cam.transform.position = centre + viewDir * (dist * (Arg("-fixedCam", "0") == "1" ? 1.4f : 1f));
                    cam.transform.LookAt(centre);
                    _camPlaced = true;
                }

                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                var png = tex.EncodeToPNG();
                var path = $"{outDir}/{clipName}_lod{lod}_t{t:0.00}.png";
                System.IO.File.WriteAllBytes(path, png);
                Object.DestroyImmediate(tex);
                Debug.Log($"RenderClipFrames: wrote {path}");
            }
            if (!controllerMode)
                graph.Destroy();

            EditorApplication.Exit(0);
        }

        private static Transform FindChildDeep(Transform node, string name)
        {
            if (node.name == name)
                return node;
            for (int i = 0; i < node.childCount; i++)
            {
                var hit = FindChildDeep(node.GetChild(i), name);
                if (hit != null)
                    return hit;
            }
            return null;
        }
    }
}
