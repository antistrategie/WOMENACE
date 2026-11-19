using Il2CppMenace.UI.Strategy;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

public sealed partial class StrategyDiorama
{
    // O.C.I.'s state for one scene: the ship's hologram, the camera round it and the module groups'
    // lines. The scene owns it (DioramaScene), so a new scene starts it afresh.
    private sealed class OciState
    {
        public GameObject Ship;
        public readonly List<Material> Holograms = new();
        public float Reveal;
        public bool On, Orbiting;
        // The view each visit opens on, round the ship from it, and the view the shot was last made for.
        public (float Yaw, float Tilt, float Distance) Start;
        public float Yaw, Distance, YawTarget, DistanceTarget;
        public (float Yaw, float Distance) ShotFor;
        // A group with the parts of its line, and the group the pointer is over.
        public readonly List<(VisualElement Group, Vector3 Anchor, VisualElement Stem, VisualElement Line, VisualElement Ring)> Groups = new();
        public int Picked = -1;
        // Each slot's group, by the slot, the slots' holder and how many elements it had when laid
        // out, and whether a slot was left out of the layout because vanilla had not yet set it up.
        public readonly Dictionary<IntPtr, int> SlotGroups = new();
        public VisualElement SlotsHolder;
        public int SlotCount;
        public bool Unplaced;
        // The slots whose kind is not known yet, asked again every few frames until one is.
        public readonly List<ShipSlot> Unresolved = new();
        // Frames the lines are still placed for after anything they follow last moved.
        public int Settling;
        // While vanilla is given its panel back, its refresh is left alone.
        public bool InfoRestoring;
    }

    private OciState _oci => _scene.Oci;
}
