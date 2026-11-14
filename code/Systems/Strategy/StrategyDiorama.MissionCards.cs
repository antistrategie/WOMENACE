using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// Vanilla's mission markers as holograms on the holotable. Vanilla's board (its markers, each an
// icon with the mission's name, difficulty and reward) is moved off the screen into a panel of its
// own that draws into a texture, and each marker's part of that texture is shown on a card standing
// on the table, through the hologram shader. Vanilla still owns the markers: it fills and restyles
// them as before, and a click or hover on a card is passed to the marker it shows.
public sealed partial class StrategyDiorama
{
    // The cards' texture: panel pixels drawn at this many texels each.
    private const float CardsScale = 1.6f;
    private const int CardsTextureWidth = 512;
    private const int CardsTextureHeight = 1024;
    // A marker's usual box in panel pixels, for one that has not laid out, and the rows' pitch down
    // the texture. This is the marker's full size: vanilla shows its board at four fifths.
    private const float CardWidth = 269f;
    private const float CardHeight = 55f;
    private const float CardPitch = 62f;
    private const float CardPad = 2f;
    // How far along the card the marker's icon stands, which is the point on the table it marks.
    private const float CardIconCentre = 20f;

    // A card's width in the room.
    private const float CardWorldWidth = 2.05f;
    private const float CardFarScale = 1.5f;
    private const float CardIntensity = 2f;
    private const float CardHoverIntensity = 2.8f;
    // How dark the room is made behind a card, so its text reads over the land's glow.
    private const float CardPlate = 0.94f;
    private static readonly Color CardTint = new(0.85f, 1f, 1f);
    private static readonly Color CardSelectedTint = new(1f, 0.93f, 0.72f);

    private GameObject _cardsHost;
    private PanelSettings _cardsSettings;
    private RenderTexture _cardsTexture;
    private VisualElement _cardsSlot;

    // The panel that draws vanilla's board into the cards' texture, made on first use. Its root
    // repeats the names, classes and stylesheets of the board's ancestors on the screen, so the
    // screen's rules still reach the markers.
    private VisualElement CardsSlot(VisualElement board)
    {
        if (_cardsSlot != null && _cardsSlot.panel != null)
            return _cardsSlot;
        if (!TexturePanel("WOMENACE_MissionCards", board, CardsTextureWidth, CardsTextureHeight, CardsScale,
            out _cardsHost, out _cardsSettings, out _cardsTexture, out var slot))
            return null;

        var ancestors = new List<VisualElement>();
        for (var e = board.parent; e != null; e = e.parent)
            ancestors.Insert(0, e);
        foreach (var ancestor in ancestors)
        {
            var copy = new VisualElement { name = ancestor.name, pickingMode = PickingMode.Ignore };
            var classes = ancestor.classList;
            for (var i = 0; classes != null && i < classes.Count; i++)
                copy.AddToClassList(classes[i]);
            var sheets = ancestor.styleSheetList;
            if (sheets != null && sheets.Count > 0)
            {
                var list = new Il2CppSystem.Collections.Generic.List<StyleSheet>();
                foreach (var sheet in sheets)
                    if (sheet != null)
                        list.Add(sheet);
                copy.styleSheetList = list;
                copy.IncrementVersion(VersionChangeType.StyleSheet);
            }
            copy.style.position = Position.Absolute;
            copy.style.left = 0;
            copy.style.top = 0;
            copy.style.right = 0;
            copy.style.bottom = 0;
            slot.Add(copy);
            slot = copy;
        }
        _cardsSlot = slot;
        return slot;
    }

    // Moves vanilla's board into the cards' panel and lays its shown markers out down the texture,
    // one to a row. Returns the action that puts it back.
    private Action TakeBoardOffScreen(VisualElement board, IReadOnlyList<MissionPoi> shown)
    {
        var slot = CardsSlot(board);
        var home = board.parent;
        if (slot == null || home == null)
            return null;
        var index = home.IndexOf(board);
        var positionWas = board.resolvedStyle.position;
        slot.Add(board);
        board.style.position = Position.Absolute;
        board.style.left = 0;
        board.style.top = 0;
        board.style.right = StyleKeyword.Auto;
        board.style.marginLeft = 0;
        board.style.scale = new Scale(Vector3.one);
        // The Operations view fades its panels in on the screen, and the board may be taken
        // mid-fade. In the cards' panel it is always drawn in full: the cards fade themselves.
        board.style.opacity = 1f;
        // The chibi walks vanilla's board, which is not shown as a board here.
        var chibi = UI.Find(board, UiSelector.Name("wm-chibi"));
        if (chibi != null)
            chibi.style.display = DisplayStyle.None;
        var places = new List<(MissionPoi Poi, Vector2 Place)>();
        for (var i = 0; i < shown.Count; i++)
        {
            var poi = shown[i];
            places.Add((poi, _markerBoard[poi.Pointer]));
            poi.style.left = CardPad;
            poi.style.top = CardPad + i * CardPitch;
        }
        return () =>
        {
            foreach (var (poi, place) in places)
            {
                poi.style.left = place.x;
                poi.style.top = place.y;
            }
            if (chibi != null)
                chibi.style.display = StyleKeyword.Null;
            board.style.position = positionWas;
            board.style.left = StyleKeyword.Null;
            board.style.top = StyleKeyword.Null;
            board.style.right = StyleKeyword.Null;
            board.style.marginLeft = StyleKeyword.Null;
            board.style.scale = StyleKeyword.Null;
            board.style.opacity = StyleKeyword.Null;
            if (home.panel != null)
                home.Insert(Math.Min(index, home.childCount), board);
        };
    }

    // A card for the marker in a row of the texture, standing upright on the table with its icon
    // over the marked point.
    private GameObject Card(GameObject template, int row, Vector3 foot, float far, Vector2 box)
    {
        var card = Quad(template, "WOMENACE_MissionCard", _cardsTexture);
        // The card is exactly the marker's own box, so nothing shows round it. Cards at the
        // table's far end are projected larger, so their titles still read.
        if (!(box.x > 1f) || !(box.y > 1f))
            box = new Vector2(CardWidth, CardHeight);
        var width = CardWorldWidth * box.x / CardWidth * Mathf.Lerp(1f, CardFarScale, far);
        var height = width * box.y / box.x;
        var rotation = MarkerFacing;
        card.transform.localRotation = rotation;
        card.transform.localScale = new Vector3(width, height, 1f);
        card.transform.localPosition = foot + rotation * new Vector3(width * (0.5f - CardIconCentre / box.x), height / 2f, 0f);
        var material = card.GetComponent<MeshRenderer>()?.material;
        if (material != null)
        {
            var (w, h) = (CardsTextureWidth / CardsScale, CardsTextureHeight / CardsScale);
            // The panel draws from the texture's top, and a texture's rows count from its bottom.
            material.SetVector("_UvRect", new Vector4(box.x / w, box.y / h, CardPad / w, 1f - (CardPad + row * CardPitch + box.y) / h));
            material.SetFloat("_ScanDepth", 0.08f);
            material.SetFloat("_ScanLines", 34f);
            material.SetFloat("_Plate", CardPlate);
            material.SetFloat("_EdgeFade", 0f);
        }
        return card;
    }

    // GFL2's land hologram on the holotable is several layers (the island's fill, its coastline
    // and its outlying islets, each a node of the table's effect). They are enlarged together
    // about the island's middle, past the table's edges, and lifted off it,
    // so the island reads as projected up from the table and gives the missions' cards room. It
    // passes through whoever stands at the table, as a hologram does.
    private const string LandEffect = "s_Aimo_table_02";
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
    private readonly List<(Transform Layer, Vector3 Place, Vector3 Scale)> _landLayers = new();
    private (Transform Node, Vector3 Place, Vector3 Scale) _curtain;
    // The island's fill (its coastline and islets are other layers): dimmed while it stands behind
    // the missions' cards, which are hard to read over it at GFL2's strength.
    private const string LandFillNode = "map_1";
    private const float LandFillBehindCards = 0.35f;
    private Material _landFill;
    private float _landFillBrightness = 1f;

    // How far the island has risen off the table and grown, 0 (on the table, as GFL2 draws it)
    // to 1 (enlarged over it), where it is heading, and whether it shows at all.
    private float _landAmount;
    private bool? _landShown;
    private const float LandSeconds = 0.9f;

    // The island belongs to an operation: it shows while one runs (mission select), on the table in
    // the Home view and risen over it in the Operations view, and moves between the two with the
    // camera. With no operation running there is no area to show, and it is put away.
    private void DriveLand()
    {
        if (_room == null)
            return;
        if (_landLayers.Count == 0 || _landLayers[0].Layer == null)
        {
            _landLayers.Clear();
            _curtain = default;
            _landShown = null;
            _landFill = null;
            foreach (var t in _room.GetComponentsInChildren<Transform>(true))
            {
                if (t.parent == null || t.parent.name != LandEffect)
                    continue;
                if (t.name == LandNode || t.name.StartsWith(LandNode + "_", StringComparison.Ordinal))
                {
                    // Only the layers GFL2 has switched on. The rest stay off.
                    if (t.gameObject.activeSelf)
                    {
                        _landLayers.Add((t, _room.transform.InverseTransformPoint(t.position), t.localScale));
                        if (t.name == LandFillNode && _landFill == null)
                        {
                            _landFill = t.GetComponent<Renderer>()?.material;
                            if (_landFill != null)
                                _landFillBrightness = _landFill.GetFloat("_Brightness");
                        }
                    }
                }
                else if (t.name == CurtainNode && t.gameObject.activeInHierarchy && _curtain.Node == null)
                    _curtain = (t, _room.transform.InverseTransformPoint(t.position), t.localScale);
            }
            ApplyLand(Ease.InOutCubic(_landAmount));
        }
        var shown = _policyScreen == VanillaUi.Screens.MissionSelect;
        if (shown != _landShown)
        {
            _landShown = shown;
            foreach (var (layer, _, _) in _landLayers)
                if (layer != null)
                    layer.gameObject.SetActive(shown);
        }
        var target = shown && _homeView == HomeView.Operations ? 1f : 0f;
        if (Mathf.Approximately(_landAmount, target))
            return;
        _landAmount = Mathf.MoveTowards(_landAmount, target, Time.unscaledDeltaTime / LandSeconds);
        ApplyLand(Ease.InOutCubic(_landAmount));
    }

    // Each layer is a particle system, which takes only its own transform's scale, so each is
    // scaled itself. They share one pivot.
    private void ApplyLand(float amount)
    {
        var grow = Mathf.Lerp(1f, LandScale, amount);
        var lift = LandLift * amount;
        if (_landFill != null)
            _landFill.SetFloat("_Brightness", _landFillBrightness * Mathf.Lerp(1f, LandFillBehindCards, amount));
        if (_curtain.Node != null)
        {
            _curtain.Node.localScale = Vector3.Scale(_curtain.Scale, Vector3.Lerp(Vector3.one, CurtainScale, amount));
            _curtain.Node.position = _room.transform.TransformPoint(_curtain.Place + Vector3.up * lift);
        }
        foreach (var (layer, place, scale) in _landLayers)
        {
            if (layer == null)
                continue;
            // Scaled about its pivot the island's middle moves off: every layer is moved by the
            // same amount, which keeps them in register, so the grown island's middle ends over
            // the table's.
            layer.localScale = scale * grow;
            var shift = (LandCentre - (place + LandFromPivot * grow)) * amount;
            shift.y = lift;
            layer.position = _room.transform.TransformPoint(place + shift);
        }
    }

    private void ReleaseCards()
    {
        if (_cardsHost != null)
            UnityEngine.Object.Destroy(_cardsHost);
        if (_cardsSettings != null)
            UnityEngine.Object.Destroy(_cardsSettings);
        if (_cardsTexture != null)
        {
            _cardsTexture.Release();
            UnityEngine.Object.Destroy(_cardsTexture);
        }
        (_cardsHost, _cardsSettings, _cardsTexture, _cardsSlot) = (null, null, null, null);
        _landLayers.Clear();
    }
}
