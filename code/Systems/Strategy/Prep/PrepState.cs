using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

public sealed partial class StrategyDiorama
{
    // Mission preparation's state for one scene: the hologram map, its tokens, the camera round
    // it and the flat view. The scene owns it (DioramaScene), so a new scene starts it afresh.
    private sealed class PrepState
    {
        public string ChipsOf;
        public int Tick;
        public bool SuppliesPlaced;
        public float SuppliesWaitFrom = -1f;
        public bool Flat;
        // 0 while the hologram shows, 1 while the flat map does.
        public float FlatAmount;
        public bool FlatVisit, FlatPlaced;
        // How much of their size the map's tokens stand at: none over the flat map.
        public float TokensUp = 1f;
        // The lens's scale at mission preparation's field of view (FitPrepView reads it).
        public float LensScale;
        public Sprite FlatPicture;
        // Set when vanilla has made its map's parts, which it does in its own colours.
        public bool FlatUndressed = true;
        // The same for the deployment zone alone, which may be every frame of a drag.
        public bool FlatZonesUndressed;
        public int FlatZoneFrames;
        // The hologram's picture the flat map's was made from.
        public IntPtr FlatPictureOf;
        // The pointer on the panel, and the tile it is over (a tile's middle is half past its number).
        public Vector2 PointerPanel;
        public Vector2? PointerTile;
        // How far the pointer was from a chibi's tile when it came onto her, in tiles.
        public Vector2 Grab;
        public bool Dragging, Moved;
        public Vector2 PressedAt;
        public float Yaw, Pitch = PrepOrbitPitch, Distance = PrepOrbitDistance;
        public float YawTarget, PitchTarget = PrepOrbitPitch, DistanceTarget = PrepOrbitDistance;
        public bool Orbiting;
        public bool OrbitOn;
        // The view a visit starts from: at the usual tilt, the distance and the lens's shift at which
        // the whole map lies in the frame's free part, between the frame's left and the roster and
        // between the chips and the foot of the panels, or as near that as the room's ceiling lets
        // the camera stand. It is worked out from the map's own shape, which is as long as it is wide
        // for one mission and not for the next, whatever its size in tiles.
        public float FitDistance = PrepOrbitDistance, FitShift = PrepFraming.Shift;
        public string SectionsOf;
        public GameObject Map;
        public Texture2D MapTexture;
        public IntPtr MapOf;
        public DeploymentZoneScale MapScale;
        public string MapAreas;
        public Mesh MapMesh;
        public Vector3[] MapFull;
        public Il2CppStructArray<Vector3> MapShown;
        public Il2CppStructArray<Color> MapLit;
        public float[] MapStarts;
        public float MapBornAt;
        public bool MapBuilding;
        // A new mission's map whose build has not been heard yet.
        public bool MapUnheard;
        public int MapSizeX, MapSizeZ;
        public float MapTile;
        public float[,] MapGround;
        // The heights the ground stands at, highest first.
        public float[] MapLevels = { 0f };
        public readonly Dictionary<IntPtr, Token> Tokens = new();
        public readonly Dictionary<string, Texture2D> Chibis = new();
        public readonly HashSet<int> ContactSlots = new();
        public readonly HashSet<int> ObjectiveSlots = new();
        public GameObject TokensHost;
        public PanelSettings TokensSettings;
        public RenderTexture TokensTexture;
        public VisualElement TokensPanel;
        public Texture2D TokenRing;
        public int TokenTick;
        public readonly GameObject[] DragMarks = new GameObject[2];
        // Whether a squad was being dragged a frame ago, and whether it was over a place it may stand.
        public bool? DragHeard;
        public (int X, int Z, float Aspect) FitFor;
        // When each kind of token was last heard, so a crowd of them sounds once.
        public readonly float[] TokenHeardAt = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
        // Where the camera looks down from in the flat view, and with what field of view.
        public (Vector3 Eye, float Fov)? FlatShot;
        // The hologram map's material, texture and mesh, destroyed together when it is built again.
        public readonly Owned MapMade = new();
    }

    private PrepState _prep => _scene.Prep;
}
