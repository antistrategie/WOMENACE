using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Womenace.EditorTools
{
    // Batchmode utility: the umbrella carrier's humanoid controller, and its ride into the
    // game. UmbrellaCarrySystem swaps it onto the carrier's Animator while the umbrella is out
    // (Reach, Swing, Open, Raise, then Hold looping until UmbrellaOpen drops; Fold, Stow) and hands
    // the vanilla controller back after.
    //
    // The controller ships inside the umbrella prefab: a disabled child "carrier_controller"
    // holds an Animator that references it, so the weapon's bundle carries it and the runtime
    // finds it on the umbrella it already has. The bake rebuilds the prefab from raw.glb and
    // drops the child, so shade_weapons.py runs this after every umbrella bake (listed in
    // Authored/weapon/welrod_umbrella/post_bake.txt).
    //
    //   Unity -batchmode -nographics -quit -projectPath unity/ \
    //     -executeMethod Womenace.EditorTools.BuildUmbrellaCarrier.Run -logFile carrier.log
    public static class BuildUmbrellaCarrier
    {
        private const string Clips = "Assets/Authored/weapon/welrod_umbrella/carrier";
        private const string ControllerPath = "Assets/Prefabs/weapon/welrod_umbrella/carrier.controller";
        private const string PrefabPath = "Assets/Prefabs/weapon/welrod_umbrella/main.prefab";
        private const string ChildName = "carrier_controller";
        private const string OpenParam = "UmbrellaOpen";
        // Raise is GFL2's held thrust stretched: slowed so the thrust lingers and the arm
        // comes up unhurried.
        private const float RaiseSpeed = 0.6f;

        public static void Run()
        {
            // rebuilt in place when it exists, so its GUID (and the prefab's reference to it)
            // stays put across bakes
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (var p in ac.parameters)
                ac.RemoveParameter(p);
            ac.AddParameter(OpenParam, AnimatorControllerParameterType.Bool);
            var sm = ac.layers[0].stateMachine;
            foreach (var existing in sm.states)
                sm.RemoveState(existing.state);
            foreach (var t in sm.anyStateTransitions)
                sm.RemoveAnyStateTransition(t);
            AnimatorState State(string name)
            {
                var s = sm.AddState(name);
                s.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Clips}/{name}.anim");
                if (s.motion == null)
                    throw new System.Exception($"no clip {Clips}/{name}.anim");
                return s;
            }
            var reach = State("Reach");
            var swing = State("Swing");
            var open = State("Open");
            var raise = State("Raise");
            raise.speed = RaiseSpeed;
            var hold = State("Hold");
            var fold = State("Fold");
            var stow = State("Stow");
            sm.defaultState = reach;
            void Chain(AnimatorState a, AnimatorState b, float blend)
            {
                var t = a.AddTransition(b);
                t.hasExitTime = true;
                t.exitTime = 1f;
                t.duration = blend;
            }
            // Reach and Swing are cut either side of the take's flourish, so they blend.
            Chain(reach, swing, 0.12f);
            Chain(swing, open, 0.03f);
            Chain(open, raise, 0.03f);
            Chain(raise, hold, 0.03f);
            // closing folds from any stage of the draw, not only once it is held
            foreach (var from in new[] { reach, swing, open, raise, hold })
            {
                var close = from.AddTransition(fold);
                close.hasExitTime = false;
                close.duration = 0.1f;
                close.AddCondition(AnimatorConditionMode.IfNot, 0, OpenParam);
            }
            Chain(fold, stow, 0.03f);
            AssetDatabase.SaveAssets();

            var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            var old = prefab.transform.Find(ChildName);
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var child = new GameObject(ChildName);
            child.transform.SetParent(prefab.transform, false);
            var animator = child.AddComponent<Animator>();
            animator.runtimeAnimatorController = ac;
            animator.enabled = false;
            child.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            PrefabUtility.UnloadPrefabContents(prefab);
            Debug.Log($"BuildUmbrellaCarrier: {ControllerPath} built and attached to {PrefabPath}/{ChildName}.");
        }
    }
}
