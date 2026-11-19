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
    private const float PlanetCardWidth = 1.47f;
    private const float PlanetCardDistance = 5.3f;
    private const float PlanetCardNearScale = 0.9f;
    private const float PlanetCardFarScale = 1.7f;
    // The room kept between two cards' boxes as the view starts, in screen heights, and between a
    // box and the edge of the planet's picture, as a part of the picture.
    private const float CardBoxGap = 0.035f;
    private const float CardBoxEdge = 0.02f;
    // How far a box that stands out past the planet is moved in at a time, and how often.
    private const float CardBoxStep = 0.01f;
    private const int CardBoxSteps = 60;
    // A card stands on the surface with the ring under its icon on its place: the ring's
    // middle is this far up the marker's box.
    private const float CardRingUp = 8.4f;
    // The chibi stands at the card's left, this much of her height clear of its icon.
    private const float ChibiBeside = 0.1f;
    // How far a card's box keeps from the side panels, in the shell's units.
    private const float CardPanelClear = 8f;

    // A mission's place in the planet's picture (across, up) from its place on vanilla's board.
    private static Vector2 PlanetSlot(Vector2 board)
    {
        var across = Mathf.Lerp(820f / PlanetFrameWidth, 1265f / PlanetFrameWidth, Mathf.InverseLerp(BoardMin.x, BoardMax.x, board.x));
        var up = 1f - Mathf.Lerp(232f / PlanetFrameHeight, 688f / PlanetFrameHeight, Mathf.InverseLerp(BoardMin.y, BoardMax.y, board.y));
        return new Vector2(0.5f + (across - PlanetSlotMiddle.x) * PlanetSlotSpread, 0.5f + (up - PlanetSlotMiddle.y) * PlanetSlotSpread);
    }

    // Where a place in the room falls on the screen as the view starts (PlanetFraming), in screen
    // heights from the view's middle, and how many screen heights a metre covers there. The
    // lens's shift moves the whole picture and is left out: only how places stand to each other
    // is asked.
    private static (float X, float Y, float PerMetre) SeenAtStart(Vector3 place)
    {
        var forward = (PlanetFraming.Target - PlanetFraming.Eye).normalized;
        var right = Vector3.Cross(Vector3.up, forward).normalized;
        var up = Vector3.Cross(forward, right);
        var from = place - PlanetFraming.Eye;
        var perMetre = 1f / (Mathf.Max(Vector3.Dot(from, forward), 0.01f) * 2f * Mathf.Tan(PlanetFraming.Fov * 0.5f * Mathf.Deg2Rad));
        return (Vector3.Dot(from, right) * perMetre, Vector3.Dot(from, up) * perMetre, perMetre);
    }

    // Where the side panels' inner edges fall as the view starts, in SeenAtStart's units: the
    // operation's column at the left and the selected mission's at the right. The lens is read as
    // the room camera draws with it at the planet's view, its shift and its fit to the screen
    // included, and then given back.
    private (float Left, float Right) PlanetFreeAcross()
    {
        var camera = _scene.Camera != null ? _scene.Camera.GetComponent<Camera>() : null;
        if (camera == null)
            return (float.NegativeInfinity, float.PositiveInfinity);
        var (fov, shift, lift) = (camera.fieldOfView, _lensShift, _lensLift);
        SetLens(camera, PlanetFraming.Fov, PlanetFraming.Shift, PlanetFraming.Lift);
        var lens = camera.projectionMatrix;
        SetLens(camera, fov, shift, lift);
        // Across the picture (-1 to 1) a place is m00 * (across / depth) - m02, and SeenAtStart
        // counts across / depth in the view's heights.
        var perSeen = lens.m00 * 2f * Mathf.Tan(PlanetFraming.Fov * 0.5f * Mathf.Deg2Rad);
        if (Mathf.Abs(perSeen) < 0.0001f)
            return (float.NegativeInfinity, float.PositiveInfinity);
        // The shell is the middle 16:9 of a wider screen.
        var block = Mathf.Min(1f, 16f / 9f / camera.aspect);
        float Seen(float panel) => ((0.5f + (panel / 1280f - 0.5f) * block) * 2f - 1f + lens.m02) / perSeen;
        return (Seen(OpsColumnLeft + OpsColumnWidth + CardPanelClear), Seen(1280f - MissionColumnRight - MissionColumnWidth - CardPanelClear));
    }

    // Stands a card on the planet for each mission shown: upright on its place on the surface,
    // which the ring under its icon marks. No card may cover another or stand under a side panel,
    // so each card's box on the screen as the view starts (the card, and the room at its left for
    // the chibi) is kept between the panels and moved off the boxes it meets, with room between
    // them, until none meet. The boxes are measured on
    // the screen and not in the picture: the picture is on a globe, where equal parts of it are
    // not equal lengths on the screen, and a card is larger the further off it stands.
    private void ShowPlanetMarkers(IReadOnlyList<MissionPoi> pois, GameObject template, VisualElement layer, PlanetShape planet)
    {
        var sectionWide = (planet.OnSphere(1f, 0.5f, out _) - planet.OnSphere(0f, 0.5f, out _)).magnitude * PlanetRadius;
        (float X, float Y, float PerMetre) Seen(float across, float up) => SeenAtStart(planet.InRoom(planet.OnSphere(across, up, out _)));
        // How far a part of the picture goes across the screen, and up it, about its middle.
        var acrossSeen = (Seen(0.6f, 0.5f).X - Seen(0.4f, 0.5f).X) / 0.2f;
        var upSeen = (Seen(0.5f, 0.6f).Y - Seen(0.5f, 0.4f).Y) / 0.2f;
        if (Mathf.Abs(acrossSeen) < 0.001f || Mathf.Abs(upSeen) < 0.001f)
            (acrossSeen, upSeen) = (1f, 1f);
        // How much of the chibi's room at a card's left its box keeps: all of it, and less where
        // the boxes cannot all be kept apart over the planet with it.
        var chibiRoom = 1f;
        var (eye, centre) = (PlanetFraming.Eye, planet.InRoom(Vector3.zero));
        var forward = (PlanetFraming.Target - eye).normalized;
        var right = Vector3.Cross(Vector3.up, forward).normalized;
        var unturn = Quaternion.Inverse(planet.Turn);
        var aspect = PlanetFrameHeight / PlanetFrameWidth;
        var free = PlanetFreeAcross();
        // Whether a place in the room is seen over the planet's picture as the view starts: the
        // line of sight to it meets the globe on the part the picture covers.
        bool OverPlanet(Vector3 place)
        {
            var (along, from) = ((place - eye).normalized, eye - centre);
            var half = Vector3.Dot(from, along);
            var inside = half * half - (from.sqrMagnitude - PlanetRadius * PlanetRadius);
            if (inside < 0f)
                return false;
            var spot = unturn * (eye + along * (-half - Mathf.Sqrt(inside)) - centre) / PlanetRadius;
            var (across, up) = (spot.x * planet.Rim + planet.CentreX, 1f - (planet.CentreY * aspect - spot.y * planet.Rim) / aspect);
            return spot.z < 0f && across > CardBoxEdge && across < 1f - CardBoxEdge && up > CardBoxEdge && up < 1f - CardBoxEdge;
        }
        // Whether a card's whole box, standing upright on its place and square to the view, is
        // seen over the planet and between the side panels.
        bool Contained(Vector2 size, Vector2 at)
        {
            var surface = planet.InRoom(planet.OnSphere(at.x, at.y, out var onPlanet));
            if (!onPlanet)
                return false;
            var seen = Measure(size, at).Box;
            if (seen.xMin < free.Left || seen.xMax > free.Right)
                return false;
            var width = PlanetCardWidth * size.x / CardWidth * Mathf.Clamp(Vector3.Distance(surface, eye) / PlanetCardDistance, PlanetCardNearScale, PlanetCardFarScale);
            var height = width * size.y / size.x;
            var (left, far) = (height * ChibiToCard * (1f + ChibiBeside) * chibiRoom + width * CardIconCentre / size.x, width * (1f - CardIconCentre / size.x));
            var (below, above) = (height * CardRingUp / size.y, Mathf.Max(height * (1f - CardRingUp / size.y), height * ChibiToCard * chibiRoom));
            return OverPlanet(surface - right * left - Vector3.up * below) && OverPlanet(surface - right * left + Vector3.up * above)
                && OverPlanet(surface + right * far - Vector3.up * below) && OverPlanet(surface + right * far + Vector3.up * above);
        }
        // A box that stands out past the planet or under a panel is brought in: across the picture
        // first, so the cards keep the heights their missions gave them, and then up or down it.
        Vector2 Contain(Vector2 size, Vector2 at)
        {
            at = new Vector2(Mathf.Clamp01(at.x), Mathf.Clamp01(at.y));
            for (var step = 0; step < CardBoxSteps && at.x != 0.5f && !Contained(size, at); step++)
                at.x = Mathf.MoveTowards(at.x, 0.5f, CardBoxStep);
            for (var step = 0; step < CardBoxSteps && at.y != 0.5f && !Contained(size, at); step++)
                at.y = Mathf.MoveTowards(at.y, 0.5f, CardBoxStep);
            return at;
        }
        // A card's width on its place, and its box on the screen with that place in it.
        (float Width, Rect Box, Vector2 Place) Measure(Vector2 size, Vector2 at)
        {
            var surface = planet.InRoom(planet.OnSphere(at.x, at.y, out _));
            var far = Vector3.Distance(surface, PlanetFraming.Eye) / PlanetCardDistance;
            var width = PlanetCardWidth * size.x / CardWidth * Mathf.Clamp(far, PlanetCardNearScale, PlanetCardFarScale);
            var height = width * size.y / size.x;
            var (x, y, perMetre) = SeenAtStart(surface);
            return (width, Rect.MinMaxRect(
                x - (height * ChibiToCard * (1f + ChibiBeside) * chibiRoom + width * CardIconCentre / size.x) * perMetre,
                y - height * CardRingUp / size.y * perMetre,
                x + width * (1f - CardIconCentre / size.x) * perMetre,
                y + Mathf.Max(height * (1f - CardRingUp / size.y), height * ChibiToCard * chibiRoom) * perMetre), new Vector2(x, y));
        }
        var asked = new List<(int Index, Vector2 Size, Vector2 At)>();
        for (var i = 0; i < pois.Count; i++)
        {
            if (!_missions.MarkerBoard.TryGetValue(pois[i].Pointer, out var board))
                continue;
            var size = new Vector2(pois[i].layout.width, pois[i].layout.height);
            if (!(size.x > 1f) || !(size.y > 1f))
                size = new Vector2(CardWidth, CardHeight);
            asked.Add((i, size, PlanetSlot(board)));
        }
        var slots = asked;
        foreach (var room in new[] { 1f, 0.5f, 0f })
        {
            chibiRoom = room;
            slots = new List<(int Index, Vector2 Size, Vector2 At)>(asked);
            var seen = new Rect[slots.Count];
            // A box brought in across the picture stands against the planet's edge, and has no more
            // room that way.
            var held = new bool[slots.Count];
            var met = false;
            for (var pass = 0; pass < 300; pass++)
            {
                // Each box keeps over the planet.
                for (var i = 0; i < slots.Count; i++)
                {
                    var (index, size, at) = slots[i];
                    var across = at.x;
                    at = Contain(size, at);
                    held[i] = Mathf.Abs(at.x - across) > 0.0001f;
                    slots[i] = (index, size, at);
                    seen[i] = Measure(size, at).Box;
                }
                met = false;
                for (var i = 0; i < slots.Count; i++)
                    for (var j = i + 1; j < slots.Count; j++)
                    {
                        var (a, b) = (seen[i], seen[j]);
                        var lapAcross = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) + CardBoxGap;
                        var lapUp = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) + CardBoxGap;
                        if (lapAcross <= 0f || lapUp <= 0f)
                            continue;
                        met = true;
                        var (first, second) = (slots[i], slots[j]);
                        // Apart along whichever way needs the smaller move for the boxes' shape, half
                        // each. Across, one against the planet's edge stays and the other takes the
                        // whole move, and two against it part up and down.
                        if (lapAcross / a.width < lapUp / a.height && !(held[i] && held[j]))
                        {
                            var push = (a.center.x <= b.center.x ? -1f : 1f) * (lapAcross + 0.002f) / acrossSeen;
                            var share = held[i] ? 0f : held[j] ? 1f : 0.5f;
                            (first.At.x, second.At.x) = (first.At.x + push * share, second.At.x - push * (1f - share));
                        }
                        else
                        {
                            var push = (a.center.y <= b.center.y ? -1f : 1f) * (lapUp / 2f + 0.001f) / upSeen;
                            (first.At.y, second.At.y) = (first.At.y + push, second.At.y - push);
                        }
                        (slots[i], slots[j]) = (first, second);
                    }
                if (!met)
                    break;
            }
            if (!met)
                break;
        }
        // Where there is not room for every box, a card keeps over the planet before it keeps
        // clear of another: the order they are drawn in settles two that meet.
        var boxes = new List<(int Index, Vector2 Size, float Width, float Left, float Lead, float Foot)>();
        foreach (var (index, size, slot) in slots)
        {
            var at = Contain(size, slot);
            boxes.Add((index, size, Measure(size, at).Width, at.x, 0f, at.y));
        }
        foreach (var box in boxes)
        {
            var spot = planet.OnSphere(box.Left + box.Lead, box.Foot, out _);
            var surface = planet.InRoom(spot);
            // The chibi's own place on the surface: at the left of the card's icon as the
            // view starts, clear of it by a part of her height. It is a place on the globe, so
            // she keeps to the ground as the camera goes round and the card turns to face it.
            var height = box.Width * box.Size.y / box.Size.x;
            var chibiAcross = box.Left + box.Lead - (box.Width * CardIconCentre / box.Size.x + height * ChibiToCard * (0.5f + ChibiBeside)) / sectionWide;
            var marker = new Marker
            {
                Poi = pois[box.Index],
                Spot = N(spot),
                ChibiSpot = N(planet.OnSphere(chibiAcross, box.Foot, out _)),
                Surface = N(surface),
                Across = box.Left + box.Lead,
                Width = box.Width,
                Height = box.Width * box.Size.y / box.Size.x,
                Box = box.Size,
                Card = Card(template, box.Index, surface, 0f, box.Size),
            };
            marker.CardTransform = marker.Card.transform;
            var renderer = marker.Card.GetComponent<MeshRenderer>();
            renderer.sortingOrder = PlanetCardOrder;
            marker.Renderer = renderer;
            marker.Material = renderer.material;
            marker.Target = ClickTarget(layer, marker);
            marker.Target.name = "wm-marker-" + _missions.Markers.Count;
            _missions.Markers.Add(marker);
        }
        _missions.MarkersPlanet = planet;
        _missions.MarkersPlacedFor = (new NVector3(float.NaN), -1f);
        PlacePlanetMarkers();
    }

    // Where two meet on the screen as the camera goes round: the chibi is drawn over every card,
    // and the selected mission's card over the others.
    private const int PlanetCardOrder = PlanetOrder + 2;
    private const int PlanetSelectedOrder = PlanetOrder + 3;
    private const int PlanetChibiOrder = PlanetOrder + 4;


    // The camera's place in room space.
    private NVector3 PlanetEye() => _scene.Camera != null ? N(_scene.Room.transform.InverseTransformPoint(_scene.Camera.transform.position)) : N(PlanetFraming.Eye);

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
        if (_missions.MarkersPlacedFor == (eye, _missions.PlanetMoment))
            return;
        _missions.MarkersPlacedFor = (eye, _missions.PlanetMoment);
        foreach (var marker in _missions.Markers)
        {
            if (marker.Card == null)
                continue;
            var (turn, cos, sin) = FacingFrom(eye, marker.Surface);
            // Arriving, the card opens sideways from its icon as the picture's scan line passes
            // its place.
            var passed = PlanetScanFrom + (PlanetScanTo - PlanetScanFrom) * marker.Across;
            marker.Open = Ease.SmoothStep(passed, passed + CardOpen, _missions.PlanetMoment);
            if (marker.Open > 0f != marker.CardShown)
                marker.Card.SetActive(marker.CardShown = marker.Open > 0f);
            // From its place: its icon's middle over it, the ring under the icon on it.
            var (across, up) = (marker.Width * (0.5f - CardIconCentre / marker.Box.x) * marker.Open, marker.Height * (0.5f - CardRingUp / marker.Box.y));
            marker.CardTransform.localRotation = turn;
            marker.CardTransform.localScale = new Vector3(marker.Width * MathF.Max(marker.Open, 0.001f), marker.Height, 1f);
            marker.CardTransform.localPosition = new Vector3(marker.Surface.X + across * cos, marker.Surface.Y + up, marker.Surface.Z - across * sin);
        }
    }

    // The chibi on the planet: on her place beside the selected mission's card with her feet on
    // the surface, walking the shortest way round the globe to a newly selected one. Solid, so a
    // bright planet does not wash her out.
    private void MovePlanetChibi(PlanetShape planet, Marker selected)
    {
        // She comes with her card.
        if (!_missions.ChibiPlaced || selected.Open < 1f)
            (_missions.ChibiSpot, _missions.ChibiTall, _missions.ChibiPlaced) = (selected.ChibiSpot, selected.ChibiTall, true);
        if (selected.Open < 1f)
        {
            _missions.Chibi.SetActive(false);
            return;
        }
        // Her way left, as the straight line between the two places: an angle between two
        // directions this close is not measured finely enough to tell when she has arrived.
        var left = NVector3.Distance(_missions.ChibiSpot, selected.ChibiSpot) * PlanetRadius;
        var walking = left > 0.01f;
        var before = planet.InRoom(_missions.ChibiSpot);
        _missions.ChibiSpot = walking ? RoundGlobe(_missions.ChibiSpot, selected.ChibiSpot, MathF.Min(1f, ChibiSpeed * PlanetStep / left)) : selected.ChibiSpot;
        var grows = PlanetStep * ChibiGrowth;
        _missions.ChibiTall += Math.Clamp(selected.ChibiTall - _missions.ChibiTall, -grows, grows);
        var surface = planet.InRoom(_missions.ChibiSpot);
        var (turn, cos, sin) = FacingFrom(_missions.MarkersPlacedFor.Eye, surface);
        if (walking)
        {
            // How far she has gone to the view's right this frame.
            var across = (surface.X - before.X) * cos - (surface.Z - before.Z) * sin;
            if (MathF.Abs(across) > 0.0005f)
                _missions.ChibiFacesLeft = across < 0f;
        }
        // On her place, a touch nearer the camera than the surface.
        _missions.ChibiTransform.localRotation = turn;
        _missions.ChibiTransform.localScale = new Vector3(_missions.ChibiTall, _missions.ChibiTall, 1f);
        _missions.ChibiTransform.localPosition = new Vector3(surface.X - ChibiNearer * sin, surface.Y + _missions.ChibiTall / 2f, surface.Z - ChibiNearer * cos);
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
