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
        // A doll whose body could not be made, not tried again until another is picked.
        public string CalibrationDollFailed;
        // The Workshop's parts its looks read, and the shell root they were found under.
        public UnityEngine.UIElements.VisualElement PartsOf;
        public Dictionary<string, UnityEngine.UIElements.VisualElement> Parts = new();
    }

    private WeaponRoomState _weaponRoom => _scene.WeaponRoom;
}
