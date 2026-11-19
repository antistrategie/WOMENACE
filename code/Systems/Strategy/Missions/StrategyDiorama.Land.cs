using UnityEngine;

namespace WOMENACE.Code;

// GFL2's land hologram on the ops room's holotable, which belongs to the operation running.
public sealed partial class StrategyDiorama
{
    // GFL2's land hologram on the holotable is several layers (the island's fill, its coastline
    // and its outlying islets, each a node of the table's effect). They are enlarged together
    // about the island's middle, past the table's edges, and lifted off it,
    // so the island reads as projected up from the table and gives the missions' cards room. It
    // passes through whoever stands at the table, as a hologram does.
    private const string LandNode = "map";
    // GFL2's curtain of dots: one card that travels up the table from its foot, dies and starts
    // again. It is widened to the island's width and stood on the island, so it sweeps the map.
    private const string CurtainNode = "Particle System";
    private static readonly Vector3 CurtainScale = new(1.5f, 1f, 1f);
    private const float LandScale = 1.6f;
    private const float LandLift = 0.2f;
    // The island as GFL2 draws it, per unit of the layers' scale, in room space: how far its
    // middle lies from the layers' shared pivot, and its half length along the table and half
    // width across it. Measured from a capture at a known scale, through the camera's projection.
    private static readonly Vector3 LandFromPivot = new(0.30f, 0f, 0.23f);
    private const float LandHalfLength = 1.47f;
    private const float LandHalfWidth = 0.70f;
    // Where the enlarged island's middle is put: over the middle of the table, at the layers' height.
    private static readonly Vector3 LandCentre = new(-14.4f, -0.14f, 0f);
    // The island's fill (its coastline and islets are other layers): dimmed while it stands behind
    // the missions' cards, which are hard to read over it at GFL2's strength.
    private const string LandFillNode = "map_1";
    private const float LandFillBehindCards = 0.35f;

    private const float LandSeconds = 0.9f;

    // The island belongs to an operation: it shows while one runs (mission select), on the table in
    // the Home view and risen over it in the Operations view, and moves between the two with the
    // camera. With no operation running there is no area to show, and it is put away.
    private void DriveLand()
    {
        if (_scene.Room == null)
            return;
        if (_missions.LandLayers.Count == 0 || _missions.LandLayers[0].Layer == null)
        {
            _missions.LandLayers.Clear();
            _missions.Curtain = default;
            _missions.LandShown = null;
            _missions.LandFill = null;
            // The room's whole hierarchy is walked once for the table's nodes, and again only for
            // another room or once one of them is gone. Until GFL2 switches a layer on, only the
            // nodes found are looked at, each frame.
            if (_missions.LandNodesOf != _scene.Room.Pointer || _missions.LandNodes.Any(node => node == null))
            {
                _missions.LandNodes.Clear();
                foreach (var t in _scene.Room.GetComponentsInChildren<Transform>(true))
                    if (t.parent != null && t.parent.name == TableEffects
                        && (t.name == LandNode || t.name.StartsWith(LandNode + "_", StringComparison.Ordinal) || t.name == CurtainNode))
                        _missions.LandNodes.Add(t);
                _missions.LandNodesOf = _scene.Room.Pointer;
            }
            foreach (var t in _missions.LandNodes)
            {
                if (t.name == LandNode || t.name.StartsWith(LandNode + "_", StringComparison.Ordinal))
                {
                    // Only the layers GFL2 has switched on. The rest stay off.
                    if (t.gameObject.activeSelf)
                    {
                        _missions.LandLayers.Add((t, _scene.Room.transform.InverseTransformPoint(t.position), t.localScale));
                        if (t.name == LandFillNode && _missions.LandFill == null)
                        {
                            _missions.LandFill = t.GetComponent<Renderer>()?.material;
                            if (_missions.LandFill != null)
                                _missions.LandFillBrightness = _missions.LandFill.GetFloat("_Brightness");
                        }
                    }
                }
                else if (t.name == CurtainNode && t.gameObject.activeInHierarchy && _missions.Curtain.Node == null)
                    _missions.Curtain = (t, _scene.Room.transform.InverseTransformPoint(t.position), t.localScale);
            }
            ApplyLand(Ease.InOutCubic(_missions.LandAmount));
        }
        var shown = _policyScreen == VanillaUi.Screens.MissionSelect;
        if (shown != _missions.LandShown)
        {
            _missions.LandShown = shown;
            foreach (var (layer, _, _) in _missions.LandLayers)
                if (layer != null)
                    layer.gameObject.SetActive(shown);
        }
        var target = shown && _homeView == HomeView.Operations ? 1f : 0f;
        if (Mathf.Approximately(_missions.LandAmount, target))
            return;
        _missions.LandAmount = Mathf.MoveTowards(_missions.LandAmount, target, Time.unscaledDeltaTime / LandSeconds);
        ApplyLand(Ease.InOutCubic(_missions.LandAmount));
    }

    // Each layer is a particle system, which takes only its own transform's scale, so each is
    // scaled itself. They share one pivot.
    private void ApplyLand(float amount)
    {
        var grow = Mathf.Lerp(1f, LandScale, amount);
        var lift = LandLift * amount;
        if (_missions.LandFill != null)
            _missions.LandFill.SetFloat("_Brightness", _missions.LandFillBrightness * Mathf.Lerp(1f, LandFillBehindCards, amount));
        if (_missions.Curtain.Node != null)
        {
            _missions.Curtain.Node.localScale = Vector3.Scale(_missions.Curtain.Scale, Vector3.Lerp(Vector3.one, CurtainScale, amount));
            _missions.Curtain.Node.position = _scene.Room.transform.TransformPoint(_missions.Curtain.Place + Vector3.up * lift);
        }
        foreach (var (layer, place, scale) in _missions.LandLayers)
        {
            if (layer == null)
                continue;
            // Scaled about its pivot the island's middle moves off: every layer is moved by the
            // same amount, which keeps them in register, so the grown island's middle ends over
            // the table's.
            layer.localScale = scale * grow;
            var shift = (LandCentre - (place + LandFromPivot * grow)) * amount;
            shift.y = lift;
            layer.position = _scene.Room.transform.TransformPoint(place + shift);
        }
    }

    private void ReleaseLand()
    {
        _missions.LandLayers.Clear();
        _missions.LandNodes.Clear();
        _missions.LandNodesOf = IntPtr.Zero;
    }
}
