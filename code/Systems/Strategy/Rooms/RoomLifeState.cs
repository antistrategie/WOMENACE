using Il2CppStem;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

public sealed partial class StrategyDiorama
{
    // The rooms' own life for one scene: the floor mirror, the shadow light, the holotable's
    // parts, the Dinergate and its HUD, the dolls round the ops room and the room's sounds. The
    // scene owns it (DioramaScene), so a new scene starts it afresh.
    private sealed class RoomLifeState
    {
        public GameObject Mirror;
        public Camera MirrorCamera;
        public Camera RoomCamera;
        public RenderTexture MirrorTexture;
        public object MirrorRoutine;
        // The frame the screen last changed. The mirror sits that frame out: vanilla switches its
        // background scenes after the screen policy has run, so for the rest of the frame the last
        // screen's sun can still be on beside the room's, and HDRP logs an error for a render with two
        // shadow-casting suns (seen going from O.C.I. to the armoury).
        public int MirrorHeldFrame = -1;
        public Light ShadowLight;
        // The shadow maps follow the player's shadow quality as MENACE's own do: the characters' sun at
        // 1024, 2048 and, at the top, GFL2's 4096, the room's live spots and points at half that (they
        // share HDRP's punctual atlas). Rechecked every frame, so a change in the options applies at once.
        public int ShadowQuality = -1;
        // Each lit unit's rig, by the unit.
        public readonly Dictionary<IntPtr, Light[]> DollRigs = new();
        public readonly List<Renderer> SparkRenderers = new();
        public bool TableSparksShown = true;
        public readonly List<(Material Material, float UnderMap)> TableTopMaterials = new();
        public float TableTopDimmed;
        public float RoomShade;
        public bool DinergateHover;
        public bool DinergateProjecting;
        public int DinergateFlight;
        public float DinergateYaw;
        public GameObject Dinergate;
        public Animator DinergateAnimator;
        public bool DinergateFlying;
        public bool TableHover;
        // The holotable's tag in the shell, found when the shell is made.
        public VisualElement TableTag;
        public Vector2 DinergatePointer = new(float.MinValue, float.MinValue);
        public HudVariant HudRest;
        public HudVariant HudNoticeVariant;
        public HudVariant HudShown;
        public HudFade HudFading;
        public float HudFadeTime;
        public float HudLoopTime;
        public float HintTimer;
        public bool DinergateNotice;
        public MaterialPropertyBlock HudBlock;
        public readonly List<GameObject> Crew = new();
        public readonly Dictionary<GameObject, DollSpot> CrewSpots = new();
        public string CrewWears, CrewWornOn;
        public float CrewCheckedAt;
        public Animator PoseProbe;
        // Each standing doll's idle and fidget loops, and when she next fidgets.
        public readonly Dictionary<GameObject, (int Idle, int Fidget, float At)> CrewFidgets = new();
        public readonly Dictionary<GameObject, Vector3> CrewSeatShifts = new();
        public TableCue CurrentTableCue;
        // The ops room's bed, looped for as long as a room is showing.
        public SoundInstance RoomBed;
        // When the bed was last asked for. A bed that will not play is asked for again a second on,
        // never every frame.
        public float RoomBedAskedAt = float.NegativeInfinity;
    }

    private RoomLifeState _life => _scene.Life;
}
