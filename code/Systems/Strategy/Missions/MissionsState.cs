using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;
using NVector3 = System.Numerics.Vector3;

namespace WOMENACE.Code;

public sealed partial class StrategyDiorama
{
    // The operation's missions for one scene: the cards on the table, the land under them, the
    // planet on mission select with its cards and camera, and the star map over the table. The
    // scene owns it (DioramaScene), so a new scene starts it afresh.
    private sealed class MissionsState
    {
        public readonly List<Marker> Markers = new();
        public readonly Dictionary<IntPtr, Vector2> MarkerBoard = new();
        public float MarkerFade = 1f;
        // When the cards last began to arrive, and how each scans in: one after another down the table.
        public float MarkersArrive = float.MinValue;
        public GameObject Chibi;
        public Material ChibiMaterial;
        public Vector3 ChibiAt;
        public bool ChibiPlaced;
        public bool ChibiFacesLeft;
        public float ChibiClock;
        // On a planet: her place on the unit globe and her height.
        public NVector3 ChibiSpot;
        public float ChibiTall;
        public Transform ChibiTransform;
        // Which of the two she was last placed by, the land or a planet: the other places her afresh.
        public bool ChibiOnPlanet;
        // The selected mission's marker, as the cards were last looked over.
        public Marker SelectedMarker;
        // The selection the pick sound was last decided for.
        public IntPtr SelectHeard;
        public GameObject CardsHost;
        public PanelSettings CardsSettings;
        public RenderTexture CardsTexture;
        public VisualElement CardsSlot;
        public readonly List<(Transform Layer, Vector3 Place, Vector3 Scale)> LandLayers = new();
        public (Transform Node, Vector3 Place, Vector3 Scale) Curtain;
        public Material LandFill;
        public float LandFillBrightness = 1f;
        // How far the island has risen off the table and grown, 0 (on the table, as GFL2 draws it)
        // to 1 (enlarged over it), where it is heading, and whether it shows at all.
        public float LandAmount;
        public bool? LandShown;
        // The planet of the operation last asked about, and the planet the parts were built for.
        public IntPtr PlanetOperation;
        public PlanetShape PlanetShape;
        public (string Id, IntPtr Clip) PlanetBuilt;
        public GameObject Planet;
        public GameObject PlanetEdge;
        public VideoPlayer PlanetPlayer;
        public RenderTexture PlanetTexture;
        public bool PlanetHidLand;
        public (float Along, float Across) PlanetLaidFor;
        public float PlanetLay;
        public float PlanetLaidAt = float.MinValue;
        // The section's points as the globe has them, the same stood upright (the lean taken out)
        // for unrolling, and the points being written while it unrolls.
        public Vector3[] PlanetRest;
        public NVector3[] PlanetUpright;
        public Il2CppStructArray<Vector3> PlanetLaid;
        public float PlanetMoment;
        // The moment the parts were last placed for. They are placed again only when it changes.
        public float PlanetPlacedAt = -1f;
        public GameObject PlanetGlobe;
        public Mesh PlanetMesh, GlobeMesh;
        public Material PlanetMaterial;
        // Each of the wireframe's points: how far round the globe it is from the section's middle
        // (an angle), how far up the globe from its lower pole (0 to 1), and how far across the
        // planet's picture (0 to 1). Its shade is worked out from these every frame it arrives.
        public NVector3[] GlobePoints;
        public float[] GlobeRound, GlobeUp, GlobeAcross;
        public Il2CppStructArray<Color> GlobeShades;
        // The section and the lines that go with it: the bright line round its edge, shown once
        // the picture is laid, and the wireframe globe until then.
        public bool PlanetShown, PlanetEdgeShown, PlanetGlobeShown;
        public float PlanetYaw, PlanetTilt, PlanetDistance;
        public float PlanetYawTarget, PlanetTiltTarget, PlanetDistanceTarget;
        public bool PlanetOrbiting;
        public bool PlanetOrbitOn;
        // The bearing, tilt and distance the shot was last worked out for.
        public (float Yaw, float Tilt, float Distance) PlanetShotFor = (float.NaN, float.NaN, float.NaN);
        public bool[,,] RoomSolid;
        public IntPtr RoomMapped;
        // The planet the cards stand on, or null while they stand on the land.
        public PlanetShape MarkersPlanet;
        // Where the camera was and how far the planet had arrived when the cards were last placed.
        // They are placed again only when one has changed.
        public (NVector3 Eye, float Moment) MarkersPlacedFor;
        public int PlanetPlateFrames;
        public GameObject StarMap;
        public GameObject StarMapSource;
        public readonly List<(Material Material, int Queue)> StarMapQueues = new();
        public readonly List<(MeshRenderer Source, GameObject Copy, bool Marker)> StarMapParts = new();
        public readonly List<bool> StarOffTable = new();
        public Renderer StarBriefingPlanet;
        public Camera StarMapCamera;
        public Vector3 StarMapCameraPosition;
        public Quaternion StarMapCameraRotation;
        public float StarMapCameraFov;
        public readonly List<(VisualElement Hud, float Left, float Top, Vector3 Point, float Scale)> StarMapNames = new();
        public Vector3 StarFromPosition = StarMapOrigin, StarToPosition = StarMapOrigin, StarPosition = StarMapOrigin;
        public float StarFromScale = StarMapScale, StarToScale = StarMapScale, StarScale = StarMapScale;
        public float StarZoomTime = StarZoomSeconds;
        public float StarHiddenAt = float.NegativeInfinity;
    }

    private MissionsState _missions => _scene.Missions;
}
