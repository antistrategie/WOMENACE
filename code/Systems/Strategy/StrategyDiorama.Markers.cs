using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.UIElements;
using NVector3 = System.Numerics.Vector3;

namespace WOMENACE.Code;

// An operation's missions standing on the holotable: for each mission a card in the room showing
// vanilla's own marker for it (StrategyDiorama.MissionCards), upright on the table where vanilla's
// board places it, so the dolls round the table hide and are hidden by it. The shell adds only an
// unseen click target over each card: a click or hover there is passed to vanilla's marker.
public sealed partial class StrategyDiorama
{
    // Vanilla's board in panel pixels from the markers' container: the corners of the box its
    // marker slots fall in. The board is spread over the land hologram's inner part, so every
    // mission stands on land.
    private static readonly Vector2 BoardMin = new(27f, 30f);
    private static readonly Vector2 BoardMax = new(384f, 392f);
    private const float LandInner = 0.78f;

    private sealed class Marker
    {
        public MissionPoi Poi;
        public GameObject Card;
        public Material Material;
        public VisualElement Target;
        public bool Selected;
        // The card's renderer on a planet, whose place in the draw order follows the selection.
        public MeshRenderer Renderer;
        public bool Hover;
        // The part of the marker under the pointer that has a tooltip of its own (its reward),
        // when the pointer came onto it, and whether the tooltip is up.
        public Il2CppMenace.UI.InteractiveElement Part;
        public float PartSince;
        public bool PartShown;
        // On a planet: its place on the unit globe and in the room, how far across the planet's
        // picture that is, its card's width and height in metres, and its marker's box.
        public NVector3 Spot;
        public NVector3 Surface;
        // Where the chibi stands beside it, on the unit globe.
        public NVector3 ChibiSpot;
        public float Across;
        public float Width;
        public float Height;
        public Vector2 Box;
        // How far its card has opened as the planet arrives, and whether it shows yet.
        public float Open = 1f;
        public bool CardShown = true;
        public Transform CardTransform;
        // The height the chibi stands at beside it.
        public float ChibiTall => Height * ChibiToCard;
    }

    // The hologram shader's properties that are set every frame.
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int PlateId = Shader.PropertyToID("_Plate");
    private static readonly int RevealId = Shader.PropertyToID("_Reveal");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int UvRectId = Shader.PropertyToID("_UvRect");
    private static readonly int CutByAlphaId = Shader.PropertyToID("_CutByAlpha");

    private readonly List<Marker> _markers = new();
    private readonly Dictionary<IntPtr, Vector2> _markerBoard = new();
    private float _markerFade = 1f;
    // When the cards last began to arrive, and how each scans in: one after another down the table.
    private float _markersArrive = float.MinValue;
    private const float CardScanSeconds = 0.32f;
    private const float CardScanStagger = 0.07f;

    // Vanilla lays its board out again whenever the screen opens or a mission is played, which
    // undoes the Operations view's own layout of it. The view is laid out again after it.
    private void HookMissionBoard()
    {
        var target = VanillaUi.Targets.MissionBoardInit;
        Context.Patches.Postfix(target.Type, target.Method, _ =>
        {
            if (_shell != null && _policyScreen == VanillaUi.Screens.MissionSelect)
                ApplyHome(_homeScreen, _homeRoomShown);
        });
    }

    // Where vanilla placed a mission marker on its board, read before the board leaves the screen.
    private void RememberBoardPlace(VisualElement poi)
    {
        if (!_markerBoard.ContainsKey(poi.Pointer))
            _markerBoard[poi.Pointer] = new Vector2(poi.resolvedStyle.left, poi.resolvedStyle.top);
    }

    // Stands a card on the table for each mission shown. A mission's card shows the row of the
    // cards' texture its marker was given.
    private void ShowMarkers(IReadOnlyList<MissionPoi> pois)
    {
        ClearMarkers();
        var template = MarkerTemplate();
        var layer = _shell != null ? UI.Find(_shell, UiSelector.Name("wm-markers")) : null;
        if (template == null || layer == null || _room == null || _cardsTexture == null)
            return;
        // Where the operation's planet is known the cards stand on it, in place of the land.
        if (CurrentPlanet() is { } planet)
        {
            ShowPlanetMarkers(pois, template, layer, planet);
            ShowChibi(template, PlanetChibiOrder);
            RefreshMarkers(true);
            SetMarkersFade(_markerFade);
            return;
        }
        // Down the table the cards keep vanilla's order from far to near but stand evenly spaced:
        // a card is wider than the land, so two at one depth would run into each other.
        var depth = pois.Select((poi, i) => (i, Y: _markerBoard.TryGetValue(poi.Pointer, out var place) ? place.y : 0f))
            .OrderBy(p => p.Y).Select((p, rank) => (p.i, rank)).ToDictionary(p => p.i, p => p.rank);
        for (var i = 0; i < pois.Count; i++)
        {
            var poi = pois[i];
            if (!_markerBoard.TryGetValue(poi.Pointer, out var board))
                continue;
            var u = Mathf.Clamp01(Mathf.InverseLerp(BoardMin.x, BoardMax.x, board.x));
            var v = pois.Count > 1 ? depth[i] / (pois.Count - 1f) : 0.5f;
            // The board's top is the table's far end, its left the view's left.
            var (length, width) = (LandHalfLength * LandScale * LandInner, LandHalfWidth * LandScale * LandInner);
            var foot = new Vector3(LandCentre.x + Mathf.Lerp(-length, length, v), LandCentre.y + LandLift, LandCentre.z + Mathf.Lerp(-width, width, u));
            var marker = new Marker { Poi = poi, Card = Card(template, i, foot, 1f - v, new Vector2(poi.layout.width, poi.layout.height)) };
            marker.Material = marker.Card.GetComponent<MeshRenderer>()?.material;
            marker.Target = ClickTarget(layer, marker);
            _markers.Add(marker);
        }
        ShowChibi(template);
        RefreshMarkers(true);
        SetMarkersFade(_markerFade);
    }

    // The chibi of the mission board, in the room: an upright hologram of her own frames, standing
    // beside the selected mission's card and walking over the land to a newly selected one.
    // Her height against the card she stands by.
    private const float ChibiToCard = 1.15f;
    // Negative: her frame has empty space round the figure, so it laps into the card's edge.
    private const float ChibiGap = -0.2f;
    private const float ChibiSpeed = 1.5f;
    private const float ChibiIntensity = 1.5f;
    private GameObject _chibi;
    private Material _chibiMaterial;
    private Vector3 _chibiAt;
    private bool _chibiPlaced;
    private bool _chibiFacesLeft;
    private float _chibiClock;
    // On a planet: her place on the unit globe and her height.
    private NVector3 _chibiSpot;
    private float _chibiTall;
    private Transform _chibiTransform;
    // Which of the two she was last placed by, the land or a planet: the other places her afresh.
    private bool _chibiOnPlanet;

    private void ShowChibi(GameObject template, int order = 0)
    {
        if (CampaignMapSystem.WaitFrames == null || CampaignMapSystem.MoveFrames == null)
            return;
        _chibi = Quad(template, "WOMENACE_MissionChibi", CampaignMapSystem.WaitFrames[0]);
        _chibiTransform = _chibi.transform;
        _chibiTransform.localRotation = MarkerFacing;
        var renderer = _chibi.GetComponent<MeshRenderer>();
        renderer.sortingOrder = order;
        _chibiMaterial = renderer.material;
        _chibiMaterial.SetFloat("_ScanDepth", 0.1f);
        _chibiMaterial.SetFloat("_ScanLines", 60f);
        _chibiMaterial.SetColor("_Tint", Color.white);
        _chibi.SetActive(false);
    }

    // Walks the chibi towards her place beside the selected card and plays her frames.
    private void MoveChibi()
    {
        if (_chibi == null)
            return;
        var selected = _selectedMarker;
        if (selected == null)
        {
            _chibi.SetActive(false);
            return;
        }
        var onPlanet = _markersPlanet != null;
        if (onPlanet != _chibiOnPlanet)
            (_chibiOnPlanet, _chibiPlaced) = (onPlanet, false);
        if (onPlanet)
        {
            MovePlanetChibi(_markersPlanet, selected);
            return;
        }
        // To the left of the card as the view sees it, a gap between them, her top level with its.
        var card = selected.Card.transform;
        var (cardWidth, cardHeight) = (card.localScale.x, card.localScale.y);
        var size = cardHeight * ChibiToCard;
        _chibi.transform.localScale = new Vector3(size, size, 1f);
        var stand = card.localPosition + new Vector3(0f, cardHeight / 2f - size / 2f, -cardWidth / 2f - ChibiGap * size - size / 2f);
        if (!_chibiPlaced)
        {
            (_chibiAt, _chibiPlaced) = (stand, true);
        }
        var to = stand - _chibiAt;
        var walking = to.magnitude > 0.01f;
        if (walking)
        {
            _chibiAt = Vector3.MoveTowards(_chibiAt, stand, ChibiSpeed * Time.unscaledDeltaTime);
            if (Mathf.Abs(to.z) > 0.02f)
                _chibiFacesLeft = to.z < 0f;
        }
        _chibi.transform.localPosition = _chibiAt;
        ShowChibiFrame(walking);
    }

    // Plays the chibi's frames, waiting or walking. A plate makes her solid over what is behind.
    private void ShowChibiFrame(bool walking, float? plate = null)
    {
        _chibiClock += Time.unscaledDeltaTime;
        var frames = walking ? CampaignMapSystem.MoveFrames : CampaignMapSystem.WaitFrames;
        var rate = walking ? CampaignMapSystem.MoveFramesPerSecond : CampaignMapSystem.WaitFramesPerSecond;
        var frame = frames[(int)(_chibiClock * rate) % frames.Length];
        _chibi.SetActive(true);
        var material = _chibiMaterial;
        if (material == null || frame == null)
            return;
        material.SetTexture(MainTexId, frame);
        // Her frames face right. Walking left, the texture is mirrored.
        material.SetVector(UvRectId, _chibiFacesLeft ? new Vector4(-1f, 1f, 1f, 0f) : new Vector4(1f, 1f, 0f, 0f));
        material.SetFloat(IntensityId, ChibiIntensity * _markerFade);
        if (plate is { } backing)
        {
            material.SetFloat(PlateId, backing * _markerFade);
            material.SetFloat(CutByAlphaId, 1f);
        }
    }

    // The room's hologram quad, which the cards are copies of.
    private GameObject MarkerTemplate()
    {
        var found = _room != null ? _room.transform.Find("_HoloScreen") : null;
        return found != null ? found.gameObject : null;
    }

    // A hologram quad in the room: the holotable's own quad and shader, showing a texture.
    // The part of a sheet one of its cells is, as _UvRect takes it: cells are counted from the
    // top left, across and then down.
    private static Vector4 SheetCell(int index, int columns, int rows) =>
        new(1f / columns, 1f / rows, index % columns / (float)columns, 1f - (index / columns + 1f) / rows);

    private GameObject Quad(GameObject template, string name, Texture texture)
    {
        var quad = UnityEngine.Object.Instantiate(template, _room.transform, false);
        quad.name = name;
        quad.SetActive(true);
        var material = quad.GetComponent<MeshRenderer>()?.material;
        if (material != null)
        {
            material.SetTexture("_MainTex", texture);
            material.SetFloat("_Backdrop", 0f);
            // Depth tested: a doll in front of a card hides it.
            material.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
        }
        return quad;
    }

    private static VisualElement ClickTarget(VisualElement layer, Marker marker)
    {
        var target = new VisualElement();
        target.AddToClassList("wui-marker");
        // The marker itself shows it is under the pointer, as vanilla draws it.
        target.RegisterCallback((EventCallback<PointerEnterEvent>)(_ =>
        {
            marker.Hover = true;
            if (marker.Poi?.panel != null)
                marker.Poi.OnHoverStart();
        }));
        target.RegisterCallback((EventCallback<PointerLeaveEvent>)(_ =>
        {
            marker.Hover = false;
            if (marker.Poi?.panel != null)
                marker.Poi.OnHoverEnd();
            HoverPart(marker, null);
        }));
        // A part of the marker with a tooltip of its own shows it, as on vanilla's board. The
        // card is the marker's whole box, so a place on the one is the same place on the other.
        target.RegisterCallback((EventCallback<PointerMoveEvent>)(moved =>
        {
            var box = target.layout;
            if (box.width > 0f && box.height > 0f)
                HoverPart(marker, new Vector2(moved.localPosition.x / box.width, moved.localPosition.y / box.height));
        }));
        target.RegisterCallback((EventCallback<ClickEvent>)(_ =>
        {
            if (marker.Poi?.panel != null && marker.Poi.IsInteractable())
                marker.Poi.OnClicked(MouseButton.LeftMouse);
        }));
        layer.Add(target);
        return target;
    }

    // How long the pointer rests on a part before its tooltip shows.
    private const float PartTooltipSeconds = 0.35f;

    // Notes the part of a marker under a place on its card (fractions of its width and height),
    // or none. The marker is drawn in a panel of its own, where vanilla never finds the pointer
    // over it, so its parts' tooltips are put up from here.
    private static void HoverPart(Marker marker, Vector2? at)
    {
        Il2CppMenace.UI.InteractiveElement part = null;
        var poi = marker.Poi;
        if (at is { } place && poi?.panel != null)
        {
            var box = poi.worldBound;
            var point = new Vector2(box.x + place.x * box.width, box.y + place.y * box.height);
            var open = new Stack<VisualElement>();
            open.Push(poi);
            while (open.Count > 0)
            {
                var element = open.Pop();
                for (var i = 0; i < element.childCount; i++)
                    open.Push(element.ElementAt(i));
                if (element.Pointer == poi.Pointer || !element.worldBound.Contains(point))
                    continue;
                var candidate = element.TryCast<Il2CppMenace.UI.InteractiveElement>();
                if (candidate != null && candidate.HasTooltip())
                    part = candidate;
            }
        }
        if ((part != null ? part.Pointer : IntPtr.Zero) == (marker.Part != null ? marker.Part.Pointer : IntPtr.Zero))
            return;
        if (marker.PartShown)
            UIManager.Get()?.RemoveActiveTooltip();
        (marker.Part, marker.PartSince, marker.PartShown) = (part, Time.unscaledTime, false);
    }

    // Puts up the tooltip of the part the pointer has rested on.
    private static void ShowPartTooltip(Marker marker)
    {
        if (marker.Part == null || marker.PartShown || Time.unscaledTime - marker.PartSince < PartTooltipSeconds)
            return;
        marker.PartShown = true;
        if (marker.Part.panel == null || marker.Part.GetTooltipData() is not { } tooltip)
            return;
        UIManager.Get()?.AddTooltip(tooltip, marker.Part, false, true, false);
    }

    // A quad's corners in its own space.
    private static readonly Vector3[] MarkerCorners = { new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(-0.5f, 0.5f, 0f), new(0.5f, 0.5f, 0f) };

    // The cards stand upright on the table and face down its length, towards its foot, as signs
    // planted on it would. They do not turn to the camera.
    private static readonly Quaternion MarkerFacing = Quaternion.LookRotation(Vector3.left, Vector3.up);

    // The selected mission's card warms, the one under the pointer brightens, and the shell's click
    // targets follow the cards on screen.
    private void RefreshMarkers(bool force)
    {
        if (_markers.Count == 0)
            return;
        var camera = RoomCamera;
        var selected = CampaignMapSystem.SelectedPoi;
        // A mission picked after another. The one a visit opens on is not a pick.
        if (selected != _selectHeard && _selectHeard != IntPtr.Zero && selected != IntPtr.Zero)
            TableSounds.Play(TableSounds.Select);
        _selectHeard = selected;
        if (_markersPlanet != null)
            PlacePlanetMarkers();
        Marker chosen = null;
        foreach (var marker in _markers)
        {
            var material = marker.Card != null ? marker.Material : null;
            var on = marker.Poi != null && marker.Poi.Pointer == selected;
            if (on)
                chosen = marker;
            if (on != marker.Selected || force)
            {
                marker.Selected = on;
                material?.SetColor("_Tint", on ? CardSelectedTint : CardTint);
                if (marker.Renderer != null)
                    marker.Renderer.sortingOrder = on ? PlanetSelectedOrder : PlanetCardOrder;
            }
            ShowPartTooltip(marker);
            // The dark backing arrives with the picture, never ahead of it as a black card.
            material?.SetFloat(IntensityId, (marker.Hover ? CardHoverIntensity : CardIntensity) * _markerFade);
            material?.SetFloat(PlateId, CardPlate * _markerFade);
            // On a planet a card opens from its icon as the planet arrives. On the land it scans in.
            var scan = _markersPlanet != null ? 1f : (Time.unscaledTime - _markersArrive - _markers.IndexOf(marker) * CardScanStagger) / CardScanSeconds;
            material?.SetFloat(RevealId, Math.Clamp(scan, 0f, 1f));
            if (camera == null || marker.Target?.panel == null || marker.Card == null)
                continue;
            // The click target is the box round the card on screen.
            var panel = marker.Target.panel;
            var (min, max, ahead) = (new Vector2(float.MaxValue, float.MaxValue), new Vector2(float.MinValue, float.MinValue), true);
            foreach (var corner in MarkerCorners)
            {
                var screen = camera.WorldToScreenPoint(marker.Card.transform.TransformPoint(corner));
                ahead &= screen.z > 0f;
                var point = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
                (min, max) = (Vector2.Min(min, point), Vector2.Max(max, point));
            }
            marker.Target.style.left = min.x;
            marker.Target.style.top = min.y;
            marker.Target.style.width = max.x - min.x;
            marker.Target.style.height = max.y - min.y;
            marker.Target.style.display = ahead && marker.Open >= 1f ? DisplayStyle.Flex : DisplayStyle.None;
        }
        _selectedMarker = chosen;
        MoveChibi();
    }

    // The selected mission's marker, as the cards were last looked over.
    private Marker _selectedMarker;
    // The selection the pick sound was last decided for.
    private IntPtr _selectHeard;

    // How far the cards have arrived with the Operations view, 0 to 1.
    private void SetMarkersFade(float k)
    {
        // Arriving: the cards scan in as the view's fade begins.
        if (k > 0f && _markerFade <= 0f)
        {
            _markersArrive = Time.unscaledTime;
            if (_markers.Count > 0)
                TableSounds.Play(TableSounds.Cards);
        }
        _markerFade = k;
        foreach (var marker in _markers)
        {
            marker.Material?.SetFloat("_Intensity", CardIntensity * k);
            marker.Material?.SetFloat("_Plate", CardPlate * k);
        }
        _chibiMaterial?.SetFloat("_Intensity", ChibiIntensity * k);
    }

    private void ClearMarkers()
    {
        foreach (var marker in _markers)
        {
            // A card taken away under the pointer is never told the pointer has left it, so
            // the tooltip it put up goes with it.
            if (marker.PartShown)
                UIManager.Get()?.RemoveActiveTooltip();
            // The card's material is its own copy, which outlives the card.
            if (marker.Material != null)
                UnityEngine.Object.Destroy(marker.Material);
            if (marker.Card != null)
                UnityEngine.Object.Destroy(marker.Card);
            try { marker.Target?.RemoveFromHierarchy(); }
            catch { }
        }
        _markers.Clear();
        (_markersPlanet, _selectedMarker) = (null, null);
        if (_chibiMaterial != null)
            UnityEngine.Object.Destroy(_chibiMaterial);
        if (_chibi != null)
            UnityEngine.Object.Destroy(_chibi);
        (_chibi, _chibiMaterial, _chibiTransform) = (null, null, null);
    }
}
