using Il2CppMenace.UI.Strategy;
using UnityEngine;
using UnityEngine.UIElements;
using NVector3 = System.Numerics.Vector3;

namespace WOMENACE.Code;

// The missions' cards and the chibi on mission select's planet (StrategyDiorama.Planet): each
// card stands upright on its mission's place on the globe's surface, square to the camera, and
// the chibi stands at the selected card's left and walks the surface to a newly selected one.
public sealed partial class StrategyDiorama
{
    // The missions' places on the planet. Vanilla's board slots fall in a small part of its
    // picture (the marker icons run from pixel 820 to 1265 across and 232 to 688 down, of 1600
    // by 900), and are spread apart about the picture's middle so the cards have room.
    private const float PlanetSlotSpread = 1.7f;
    private static readonly Vector2 PlanetSlotMiddle = new(0.65f, 0.49f);
    // A card's width on the planet for a marker of vanilla's usual width, in metres, at the
    // distance the view's own place is from the planet's middle. Further cards are larger.
    private const float PlanetCardWidth = 1.1f;
    private const float PlanetCardDistance = 6.2f;
    private const float PlanetCardNearScale = 0.9f;
    private const float PlanetCardFarScale = 1.7f;
    // The room kept between two cards' boxes and between a box and the picture's edge, as parts
    // of the picture.
    private const float CardBoxGap = 0.015f;
    private const float CardBoxEdge = 0.05f;
    // A card stands on the surface with the ring under its icon on its place: the ring's
    // middle is this far up the marker's box.
    private const float CardRingUp = 8.4f;
    // The chibi stands at the card's left, this much of her height clear of its icon.
    private const float ChibiBeside = 0.1f;

    // A mission's place in the planet's picture (across, up) from its place on vanilla's board.
    private static Vector2 PlanetSlot(Vector2 board)
    {
        var across = Mathf.Lerp(820f / PlanetFrameWidth, 1265f / PlanetFrameWidth, Mathf.InverseLerp(BoardMin.x, BoardMax.x, board.x));
        var up = 1f - Mathf.Lerp(232f / PlanetFrameHeight, 688f / PlanetFrameHeight, Mathf.InverseLerp(BoardMin.y, BoardMax.y, board.y));
        return new Vector2(0.5f + (across - PlanetSlotMiddle.x) * PlanetSlotSpread, 0.5f + (up - PlanetSlotMiddle.y) * PlanetSlotSpread);
    }

    // Stands a card on the planet for each mission shown: upright on its place on the surface,
    // which the ring under its icon marks. No card may cover another, so each card's box in the
    // picture (the card, and the room at its left for the chibi) is moved off the boxes it
    // meets until none meet.
    private void ShowPlanetMarkers(IReadOnlyList<MissionPoi> pois, GameObject template, VisualElement layer, PlanetShape planet)
    {
        var (sectionWide, sectionTall) = (
            (planet.OnSphere(1f, 0.5f, out _) - planet.OnSphere(0f, 0.5f, out _)).magnitude * PlanetRadius,
            (planet.OnSphere(0.5f, 1f, out _) - planet.OnSphere(0.5f, 0f, out _)).magnitude * PlanetRadius);
        var boxes = new List<(int Index, Vector2 Size, float Width, float Lead, float Left, float Foot, float Wide, float Tall)>();
        for (var i = 0; i < pois.Count; i++)
        {
            if (!_markerBoard.TryGetValue(pois[i].Pointer, out var board))
                continue;
            var slot = PlanetSlot(board);
            var size = new Vector2(pois[i].layout.width, pois[i].layout.height);
            if (!(size.x > 1f) || !(size.y > 1f))
                size = new Vector2(CardWidth, CardHeight);
            var far = Vector3.Distance(planet.InRoom(planet.OnSphere(slot.x, slot.y, out _)), PlanetFraming.Eye) / PlanetCardDistance;
            var width = PlanetCardWidth * size.x / CardWidth * Mathf.Clamp(far, PlanetCardNearScale, PlanetCardFarScale);
            var height = width * size.y / size.x;
            // The box starts where the chibi would stand, at the left of the card's icon.
            var lead = (height * ChibiToCard * (1f + ChibiBeside) + width * CardIconCentre / size.x) / sectionWide;
            boxes.Add((i, size, width, lead, slot.x - lead, slot.y, lead + width / sectionWide + CardBoxGap, height * Mathf.Max(1f, ChibiToCard) / sectionTall + CardBoxGap));
        }
        for (var pass = 0; pass < 200; pass++)
        {
            var met = false;
            for (var i = 0; i < boxes.Count; i++)
            {
                var a = boxes[i];
                a.Left = Mathf.Clamp(a.Left, CardBoxEdge, 1f - CardBoxEdge - a.Wide);
                a.Foot = Mathf.Clamp(a.Foot, CardBoxEdge, 1f - CardBoxEdge - a.Tall);
                boxes[i] = a;
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    var b = boxes[j];
                    var lapAcross = Mathf.Min(a.Left + a.Wide, b.Left + b.Wide) - Mathf.Max(a.Left, b.Left);
                    var lapUp = Mathf.Min(a.Foot + a.Tall, b.Foot + b.Tall) - Mathf.Max(a.Foot, b.Foot);
                    if (lapAcross <= 0f || lapUp <= 0f)
                        continue;
                    met = true;
                    // Apart along whichever way needs the smaller move, half each.
                    if (lapAcross / a.Wide < lapUp / a.Tall)
                    {
                        var push = (a.Left <= b.Left ? -1f : 1f) * (lapAcross / 2f + 0.001f);
                        (a.Left, b.Left) = (a.Left + push, b.Left - push);
                    }
                    else
                    {
                        var push = (a.Foot <= b.Foot ? -1f : 1f) * (lapUp / 2f + 0.001f);
                        (a.Foot, b.Foot) = (a.Foot + push, b.Foot - push);
                    }
                    (boxes[i], boxes[j]) = (a, b);
                }
            }
            if (!met)
                break;
        }
        foreach (var box in boxes)
        {
            var spot = planet.OnSphere(box.Left + box.Lead, box.Foot, out _);
            var surface = planet.InRoom(spot);
            var marker = new Marker
            {
                Poi = pois[box.Index],
                Spot = N(spot),
                Surface = N(surface),
                Across = box.Left + box.Lead,
                Width = box.Width,
                Height = box.Width * box.Size.y / box.Size.x,
                Box = box.Size,
                Card = Card(template, box.Index, surface, 0f, box.Size),
            };
            marker.CardTransform = marker.Card.transform;
            var renderer = marker.Card.GetComponent<MeshRenderer>();
            renderer.sortingOrder = PlanetOrder + 2;
            marker.Material = renderer.material;
            marker.Target = ClickTarget(layer, marker);
            marker.Target.name = "wm-marker-" + _markers.Count;
            _markers.Add(marker);
        }
        _markersPlanet = planet;
        _markersPlacedFor = (new NVector3(float.NaN), -1f);
        PlacePlanetMarkers();
    }

    // The planet the cards stand on, or null while they stand on the land.
    private PlanetShape _markersPlanet;
    // Where the camera was and how far the planet had arrived when the cards were last placed.
    // They are placed again only when one has changed.
    private (NVector3 Eye, float Moment) _markersPlacedFor;

    // The camera's place in room space.
    private NVector3 PlanetEye() => _camera != null ? N(_room.transform.InverseTransformPoint(_camera.transform.position)) : N(PlanetFraming.Eye);

    // Upright and square to the camera from a place in the room: the turn about the upright,
    // and that turn's cosine and sine for placing things beside it.
    private static (Quaternion Turn, float Cos, float Sin) FacingFrom(NVector3 eye, NVector3 place)
    {
        var (dx, dz) = (place.X - eye.X, place.Z - eye.Z);
        if (dx * dx + dz * dz <= 0.0001f)
            return (Quaternion.identity, 1f, 0f);
        var bearing = MathF.Atan2(dx, dz);
        return (new Quaternion(0f, MathF.Sin(bearing / 2f), 0f, MathF.Cos(bearing / 2f)), MathF.Cos(bearing), MathF.Sin(bearing));
    }

    // The cards keep square to the camera as it moves.
    private void PlacePlanetMarkers()
    {
        var eye = PlanetEye();
        if (_markersPlacedFor == (eye, _planetMoment))
            return;
        _markersPlacedFor = (eye, _planetMoment);
        foreach (var marker in _markers)
        {
            if (marker.Card == null)
                continue;
            var (turn, cos, sin) = FacingFrom(eye, marker.Surface);
            // Arriving, the card opens sideways from its icon as the picture's scan line passes
            // its place.
            var passed = PlanetScanFrom + (PlanetScanTo - PlanetScanFrom) * marker.Across;
            marker.Open = Ease.SmoothStep(passed, passed + CardOpen, _planetMoment);
            if (marker.Open > 0f != marker.CardShown)
                marker.Card.SetActive(marker.CardShown = marker.Open > 0f);
            // From its place: its icon's middle over it, the ring under the icon on it.
            var (across, up) = (marker.Width * (0.5f - CardIconCentre / marker.Box.x) * marker.Open, marker.Height * (0.5f - CardRingUp / marker.Box.y));
            marker.CardTransform.localRotation = turn;
            marker.CardTransform.localScale = new Vector3(marker.Width * MathF.Max(marker.Open, 0.001f), marker.Height, 1f);
            marker.CardTransform.localPosition = new Vector3(marker.Surface.X + across * cos, marker.Surface.Y + up, marker.Surface.Z - across * sin);
        }
    }

    // The chibi on the planet: at the left of the selected mission's card with her feet on the
    // surface, walking the shortest way round the globe to a newly selected one. Solid, so a
    // bright planet does not wash her out.
    private void MovePlanetChibi(PlanetShape planet, Marker selected)
    {
        // She comes with her card.
        if (!_chibiPlaced || selected.Open < 1f)
            (_chibiSpot, _chibiTall, _chibiPlaced) = (selected.Spot, selected.ChibiTall, true);
        if (selected.Open < 1f)
        {
            _chibi.SetActive(false);
            return;
        }
        // Her way left, as the straight line between the two places: an angle between two
        // directions this close is not measured finely enough to tell when she has arrived.
        var left = NVector3.Distance(_chibiSpot, selected.Spot) * PlanetRadius;
        var walking = left > 0.01f;
        var before = planet.InRoom(_chibiSpot);
        _chibiSpot = walking ? RoundGlobe(_chibiSpot, selected.Spot, MathF.Min(1f, ChibiSpeed * PlanetStep / left)) : selected.Spot;
        var grows = Time.unscaledDeltaTime * ChibiGrowth;
        _chibiTall += Math.Clamp(selected.ChibiTall - _chibiTall, -grows, grows);
        var surface = planet.InRoom(_chibiSpot);
        var (turn, cos, sin) = FacingFrom(_markersPlacedFor.Eye, surface);
        if (walking)
        {
            // How far she has gone to the view's right this frame.
            var across = (surface.X - before.X) * cos - (surface.Z - before.Z) * sin;
            if (MathF.Abs(across) > 0.0005f)
                _chibiFacesLeft = across < 0f;
        }
        // At the left of the card's icon, a touch nearer the camera than the surface.
        var beside = -selected.Width * CardIconCentre / selected.Box.x - _chibiTall * (0.5f + ChibiBeside);
        _chibiTransform.localRotation = turn;
        _chibiTransform.localScale = new Vector3(_chibiTall, _chibiTall, 1f);
        _chibiTransform.localPosition = new Vector3(surface.X + beside * cos - ChibiNearer * sin, surface.Y + _chibiTall / 2f, surface.Z - beside * sin - ChibiNearer * cos);
        ShowChibiFrame(walking, plate: 1f);
    }

    // How fast her height follows the card she stands by, in metres a second, and how far she
    // stands off the surface towards the camera.
    private const float ChibiGrowth = 0.2f;
    private const float ChibiNearer = 0.02f;

    // A part of the way from one place on the unit globe to another, round the globe.
    private static NVector3 RoundGlobe(NVector3 from, NVector3 to, float part)
    {
        var angle = MathF.Acos(Math.Clamp(NVector3.Dot(from, to), -1f, 1f));
        if (angle < 0.0001f)
            return to;
        var sine = MathF.Sin(angle);
        return NVector3.Normalize(from * (MathF.Sin((1f - part) * angle) / sine) + to * (MathF.Sin(part * angle) / sine));
    }
}
