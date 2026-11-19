using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

public sealed partial class StrategyDiorama
{
    // The shops' state for one scene: the black market's barter on the table, the capsule
    // machine and its pull, and Kalina's basket. The scene owns it (DioramaScene), so a new scene
    // starts it afresh.
    private sealed class ShopState
    {
        public GameObject MarketCubes, MarketNumber, MarketNumberHost;
        public Mesh MarketMesh;
        public Texture2D MarketFace;
        public RenderTexture MarketNumberTexture;
        public PanelSettings MarketNumberSettings;
        public Label MarketNumberLabel;
        public Il2CppStructArray<Vector3> MarketVertices;
        public Il2CppStructArray<Color32> MarketColours;
        public Color32 MarketGold, MarketCyan, MarketOver, MarketShort;
        public readonly List<MarketCube>[] MarketPiles = { new(), new() };
        public readonly int[] MarketCounts = new int[2];
        public readonly int[] MarketWritten = new int[2];
        public int MarketGive = -1, MarketGet = -1, CubeEdge = MarketEdge;
        public bool MarketOn, MarketMoving;
        // Whether the strip of what is on offer is opened to its full height, how far open it stands,
        // and the strip with its chevron.
        public bool MarketStripOpen;
        public float MarketStripAt;
        public VisualElement MarketStrip, MarketFold;
        public bool PullOn, Procurement, MachineForShop;
        public Action PullDone;
        public object PullRoutine;
        public readonly List<SkitPicture> Skit = new();
        public GameObject Machine;
        public readonly List<Il2CppStem.SoundInstance> MachineSounds = new();
        public readonly List<Material> MachineMaterials = new();
        public object MachineCameraRoutine, MachineEffectsRoutine;
        public readonly Dictionary<string, GameObject> MachineEffects = new();
        public Transform MachineCameraNode, MachineCameraEffects;
        public readonly List<GameObject> Cards = new();
        public readonly List<Material> CardMaterials = new();
        public readonly List<(Il2CppMenace.Items.BaseItemTemplate Template, int Count)> Basket = new();
        public readonly List<UnityEngine.UIElements.Label> Counts = new();
        public RenderTexture CountTexture;
        public UnityEngine.UIElements.PanelSettings CountSettings;
        public GameObject CountHost;
        public long CartFor;
        public long CartCounts;
        public readonly List<(Material Material, float Started)> Arriving = new();
        public readonly HashSet<IntPtr> Standing = new();
    }

    private ShopState _shop => _scene.Shop;
}
