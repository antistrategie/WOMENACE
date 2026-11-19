using UnityEngine;

namespace WOMENACE.Code;

public sealed partial class StrategyDiorama
{
    // The weapon room's state for one scene: what each station shows and the doll seated in the
    // lounge for calibration. The scene owns it (DioramaScene), so a new scene starts it afresh.
    private sealed class WeaponRoomState
    {
        // What is shown at the station, the template it is of, and the station it stands at.
        public GameObject WorkedOn;
        public IntPtr WorkedOnTemplate;
        public bool WorkedOnNothing;
        public int WorkedOnStation = -1;
        public readonly List<Material> WorkedOnHolograms = new();
        public GameObject CalibrationDoll;
        public string CalibrationDollTag;
        public IntPtr CalibrationLeader;
        public string CalibrationTag;
        public string CalibrationWeapon;
    }

    private WeaponRoomState _weaponRoom => _scene.WeaponRoom;
}
