using UnityEngine;

namespace WOMENACE.Code;

public sealed partial class StrategyDiorama
{
    // What the diorama holds for one scene: the rooms, their camera, and how the screens arrived in
    // them. Remove() tears down what the scene made and puts a fresh one in its place, so nothing
    // carries over to the next scene unless it is reset by hand.
    private sealed class DioramaScene
    {
        // The ops room, the strategy layer's own set, and the room camera.
        public GameObject Room, Camera;
        public Transform Anchor;

        public readonly RoomSet Ops = new() { Asset = "environment/ops_room/main", Effects = "environment/ops_room_vfx/main", Origin = RoomOrigin, Look = OpsLook, MirrorFloor = -0.97f };
        public readonly RoomSet Refit = new() { Asset = "environment/refit_room/main", Effects = "environment/refit_room_vfx/main", Origin = RoomOrigin + new Vector3(0f, 0f, 100f), Look = RefitLook, MirrorFloor = 0f };
        // GFL2's weapon room stands on its crew deck, well off its scene's origin: the set's origin
        // brings the middle of its floor to a hundred metres along x from the ops room's.
        public readonly RoomSet Weapon = new() { Asset = "environment/weapon_room/main", Effects = "environment/weapon_room_vfx/main", Origin = RoomOrigin + new Vector3(133.5f, -18.55f, 96.5f), Look = WeaponLook, MirrorFloor = 18.55f };
        public RoomSet Active;
        public RoomSet[] Sets => _sets ??= new[] { Ops, Refit, Weapon };
        private RoomSet[] _sets;

        // The room the last shell was in, and whether the bars were shut to leave it. The first
        // shell of a scene finds neither, and opens through the bars.
        public string ShellRoom;
        public bool ArrivedThroughBars;
        // A cut or a mission launch under way: another is refused until it is through.
        public bool Cutting;
        // The view a home screen opens on when the shell's Operations button sent the player there.
        public bool ArriveOnOperations;
        // The station asked for from another screen, and until when: the Workshop takes it up once
        // its panels are built, which is some frames after it is the screen.
        public int? ArriveAtStation;
        public float ArriveAtStationUntil;

        public readonly PrepState Prep = new();
    }

    private DioramaScene _scene = new();

    // The scene's parts under the names the partials have always used.
    private GameObject _room { get => _scene.Room; set => _scene.Room = value; }
    private GameObject _camera { get => _scene.Camera; set => _scene.Camera = value; }
    private Transform _anchor { get => _scene.Anchor; set => _scene.Anchor = value; }
    private RoomSet _ops => _scene.Ops;
    private RoomSet _refitSet => _scene.Refit;
    private RoomSet _weaponSet => _scene.Weapon;
    private RoomSet[] Sets => _scene.Sets;
    private RoomSet _active { get => _scene.Active; set => _scene.Active = value; }
    private string _shellRoom { get => _scene.ShellRoom; set => _scene.ShellRoom = value; }
    private bool _arrivedThroughBars { get => _scene.ArrivedThroughBars; set => _scene.ArrivedThroughBars = value; }
    private bool _cutting { get => _scene.Cutting; set => _scene.Cutting = value; }
    private bool _arriveOnOperations { get => _scene.ArriveOnOperations; set => _scene.ArriveOnOperations = value; }
    private int? _arriveAtStation { get => _scene.ArriveAtStation; set => _scene.ArriveAtStation = value; }
    private float _arriveAtStationUntil { get => _scene.ArriveAtStationUntil; set => _scene.ArriveAtStationUntil = value; }
}
