using Il2CppMenace.Strategy;
using Il2CppMenace.UI.Strategy;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

public sealed partial class StrategyDiorama
{
    // The refitting room's state for one scene: the squad staged on its floor, the camera round it
    // and the hall it stands in, the Squads screen's panels, and the recruit's hologram. The scene
    // owns it (DioramaScene), so a new scene starts it afresh.
    private sealed class StagingState
    {
        public StageShot CurrentStageShot = SquadShot;
        // Asked for several times a frame while the camera orbits, and measured once.
        public int StagePanFrame = -1;
        public float CurrentStagePan;
        public (Vector3 Position, Quaternion Rotation) StageFrom;
        public float StageTravel = 1f;
        public readonly List<StagedRoot> StagedRoots = new();
        // The units under the staged roots, by pointer and active state, so a respawned squad is lit
        // and gripped on the frame it appears.
        public long SquadSignature;
        // The units' renderers, by pointer, read again every few frames and whenever the units
        // change: a body or a weapon that changes in place leaves the units as they were.
        public long SquadShallow, SquadDeep;
        public int SquadDeepFrame;
        // The room camera's lens, looked up once for the orbit's every frame.
        public Camera Lens;
        // Each unit renderer the room's probes light, with its own property block (null when it had
        // none), given back with the units.
        public readonly Dictionary<IntPtr, (Renderer Renderer, MaterialPropertyBlock Block)> ProbeLitUnits = new();
        public readonly List<Renderer> ArmoryHidden = new();
        public Camera ArmoryCamera;
        public GameObject ArmoryLabels;
        public GameObject ArmoryVanilla;
        public Transform Stage;
        public float OrbitYaw, OrbitPitch, OrbitDistance;
        public float OrbitYawTarget, OrbitPitchTarget, OrbitDistanceTarget;
        public object OrbitRoutine;
        // Whether a staged unit is shaded by the room itself: a doll's shaders take the room's light
        // probes and its sun, and the rig on top of them read as a spotlight on her. MENACE's own
        // soldiers and vehicles take neither, and keep the rig.
        // The answer is kept for a short while only: a doll's outfit body loads after she is staged,
        // and vanilla makes its units anew on each selection.
        public readonly Dictionary<IntPtr, (bool Lit, int Frame)> RoomLitUnits = new();
        // How far back the shot stands for a footprint of this radius: the default distance, or enough
        // for the footprint to fill the frame's height with a margin. The zoom-out limit follows it.
        public float OrbitDistanceLimit = OrbitDistanceMax;
        public float FramedRadius;
        public bool[,,] HallSolid;
        // The room's floor plan, by the same cells across and deep.
        public bool[,] HallFloor;
        public IntPtr HallMapped;
        public bool RosterDeceased;
        public int SquadTab;
        public bool DressLinks;
        public IntPtr SquadRestores;
        public VisualElement StatsWindow, StatsRaised, CurrentStatsColumn;
        public bool PickerScrollPress;
        public VisualElement SquadWindow;
        public bool SquadPanelInPrep;
        public int SquadPickerTick;
        // Whether the equipment picker is open over the room, as last looked.
        public bool SquadPickerOpen;
        public Action<VisualElement> SquadAffinityChanged;
        // The way the squad faces, towards the room camera's starting shot, flat on the floor.
        public Vector3 SquadFacing = Vector3.back;
        public GameObject NamePlateHost;
        public PanelSettings NamePlateSettings;
        public RenderTexture NamePlateTexture;
        public readonly Label[] NamePlateLabels = new Label[NamePlateRows];
        public readonly GameObject[] NamePlates = new GameObject[NamePlateRows];
        // Each plate's renderer, which the mirror turns off and on around every render it makes.
        public readonly MeshRenderer[] NamePlateRenderers = new MeshRenderer[NamePlateRows];
        public GameObject RecruitStand;
        public Transform RecruitSlot;
        public GameObject Candidate;
        public IntPtr CandidateTemplate;
        public UnitLeaderTemplate CandidateLeader;
        public bool CandidateReal;
        public float CandidateDrawn;
        public bool RecruitRefreshing;
        public bool CurrentHiring;
        public Material Hologram;
        public readonly List<(Renderer Renderer, Material[] Own)> CandidateOwn = new();
        public readonly List<Material> CandidateHolograms = new();
    }

    private StagingState _staging => _scene.Staging;
}
