using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// What stands on mission preparation's hologram map: each squad placed as its doll's chibi, and
// each objective and enemy contact as vanilla's own icon over a ring, an objective with its name
// on a plate beside it. They are cards in the room, upright on their tiles as a mission's card
// stands on the table in mission select, and they turn with the camera. A contact's words show
// beside it while the pointer is on it. All of it is read
// from vanilla's flat map every few frames (its markers' places, icons, words and colours), so
// the hologram shows what that map shows at the mission's intelligence level and no more.
//
// A chibi's card shows her picture. The others show a part of one texture, which a panel of the
// shell's own draws: a cell for each objective and each contact.
public sealed partial class StrategyDiorama
{
    private const string PrepTokensName = "wm-prep-tokens";

    // The cells' panel, in its own units, drawn at this many texels each.
    private const int TokenPanelSize = 512;
    private const float TokenPanelScale = 2f;
    // Contacts are square cells in rows from the top, objectives wide cells in two columns below.
    private const float ContactCell = 40f;
    private const int ContactColumns = 12;
    private const int ContactRows = 6;
    private const float ObjectiveCellWidth = 256f;
    private const float ObjectiveCellHeight = 40f;
    private const int ObjectiveRows = 6;
    // Where an objective's icon stands along its cell, which is the point on the map it marks.
    private const float ObjectiveIconCentre = 14f;
    private const float ObjectiveIconWidth = 22f;

    // Sizes in tiles: a chibi's height, a badge's where a squad has no chibi, a cell unit, and
    // the ring under a squad.
    private const float ChibiTiles = 3.5f;
    // A chibi's picture is a sheet of her waiting loop: frames in a grid, read left to right and
    // top to bottom, each wider than it is tall, her feet this far up it (scripts/doll/chibi_frames.py).
    private const int ChibiFrames = 40;
    private const int ChibiColumns = 6;
    private const int ChibiRows = 7;
    private const float ChibiFramesPerSecond = 10f;
    private const float ChibiFrameAspect = 1.5f;
    // The part of a frame's width she stands in, for the pointer.
    private const float ChibiBodyWidth = 0.45f;
    private const float BadgeTiles = 2.2f;
    private const float CellUnitTiles = 0.075f;
    private const float RingTiles = 2f;
    private const float ChibiFeet = 0.06f;
    // The steepest angle a card is seen from, in degrees above its own level.
    private const float TokenSeenFrom = 22f;
    private const float TokenIntensity = 1.6f;
    private const float TokenPlate = 0.92f;
    // Drawn after the map, whose light would otherwise be added over them. The pipeline draws
    // no transparent thing past this queue.
    private const int TokenQueue = 3100;
    private static readonly Color TokenRing = new(0.62f, 0.85f, 0.94f);
    private static readonly Color TokenRingOpen = new(0.84f, 0.75f, 0.45f);
    private static readonly Color TokenInvalid = new(1f, 0.26f, 0.26f);
    // How near the pointer a contact shows its words, in the panel's units.
    private const float ContactReach = 14f;
    private static readonly Color TokenPlateFill = new(8f / 255f, 18f / 255f, 38f / 255f, 0.9f);

    private enum TokenKind { Squad, Objective, Contact }

    private sealed class Token
    {
        public TokenKind Kind;
        // Whether its coming up out of the map has been heard, or was not to be.
        public bool Heard;
        public GameObject Card;
        public GameObject Ring;
        public VisualElement Cell;
        public VisualElement Icon;
        public VisualElement Under;
        public Label Name;
        public Label Words;
        public int Slot = -1;
        public Vector2 Tile;
        public Vector2 Size;
        public float FootAlong = 0.5f;
        public float FootUp;
        // The card on the panel, as the camera last saw it.
        public Rect Seen;
        // A chibi's loop: whether the card plays one, where in it she started, and the frame shown.
        public bool Plays;
        public float Phase;
        public int Frame = -1;
    }

    private readonly Dictionary<IntPtr, Token> _prepTokens = new();
    private readonly Dictionary<string, Texture2D> _chibis = new();
    private readonly HashSet<int> _contactSlots = new();
    private readonly HashSet<int> _objectiveSlots = new();
    private GameObject _tokensHost;
    private PanelSettings _tokensSettings;
    private RenderTexture _tokensTexture;
    private VisualElement _tokensPanel;
    private Texture2D _tokenRing;
    private StyleFontDefinition _tokenFont;
    private int _prepTokenTick;
    private bool _prepChibisOff;
    private readonly GameObject[] _dragMarks = new GameObject[2];

    // Whether a squad was being dragged a frame ago, and whether it was over a place it may stand.
    private bool? _dragHeard;

    private void DrivePrepTokens()
    {
        var layer = _shellRoot != null && _policyScreen == VanillaUi.Screens.MissionPrep
            ? UI.Find(_shellRoot, UiSelector.Name(PrepTokensName)) : null;
        if (layer == null || _prepMap == null || _camera == null || layer.panel == null)
        {
            if (_prepTokens.Count > 0)
                ClearPrepTokens();
            _dragHeard = null;
            return;
        }
        try
        {
            // A dragged squad's place changes every frame.
            var drag = PrepDrag();
            // A squad lifted, and put down where it may stand or where it may not.
            if (drag != null && _dragHeard == null)
                TableSounds.Play(TableSounds.TokenLift);
            else if (drag == null && _dragHeard is { } valid)
                TableSounds.Play(valid ? TableSounds.TokenPlace : TableSounds.TokenRefused);
            _dragHeard = drag?.Valid;
            if (_prepTokenTick++ % 6 == 0 || drag != null)
                ReadPrepTokens(layer);
            PlacePrepTokens(layer, drag);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"strategy diorama: mission map tokens failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ClearPrepTokens()
    {
        foreach (var token in _prepTokens.Values)
            DropToken(token);
        _prepTokens.Clear();
    }

    private void DropToken(Token token)
    {
        foreach (var part in new[] { token.Card, token.Ring })
        {
            if (part == null)
                continue;
            var material = part.GetComponent<MeshRenderer>()?.material;
            if (material != null)
                UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(part);
        }
        token.Cell?.RemoveFromHierarchy();
        token.Words?.RemoveFromHierarchy();
        if (token.Slot >= 0)
            (token.Kind == TokenKind.Contact ? _contactSlots : _objectiveSlots).Remove(token.Slot);
    }

    // The cells' panel and its texture go with the room.
    private void ReleasePrepTokens()
    {
        ClearPrepTokens();
        if (_tokensHost != null)
            UnityEngine.Object.Destroy(_tokensHost);
        if (_tokensSettings != null)
            UnityEngine.Object.Destroy(_tokensSettings);
        if (_tokensTexture != null)
        {
            _tokensTexture.Release();
            UnityEngine.Object.Destroy(_tokensTexture);
        }
        if (_tokenRing != null)
            UnityEngine.Object.Destroy(_tokenRing);
        for (var i = 0; i < _dragMarks.Length; i++)
        {
            if (_dragMarks[i] != null)
                UnityEngine.Object.Destroy(_dragMarks[i]);
            _dragMarks[i] = null;
        }
        (_tokensHost, _tokensSettings, _tokensTexture, _tokensPanel, _tokenRing) = (null, null, null, null, null);
    }

    // The shell's layer over the map, for a contact's words while the pointer is on it. Under the roster, the objectives and the squad panel.
    private void LayOutPrepTokens(VisualElement row)
    {
        if (UI.Find(row, UiSelector.Name(PrepTokensName)) is { } there)
        {
            there.SendToBack();
            return;
        }
        var layer = new VisualElement { name = PrepTokensName, pickingMode = PickingMode.Ignore };
        layer.AddToClassList("wm-prep__tokens");
        row.Insert(0, layer);
        _shellUndo.Add(() =>
        {
            ClearPrepTokens();
            layer.RemoveFromHierarchy();
        });
    }

    // The panel that draws the cells into their texture, made on first use with the shell's own
    // theme and text settings.
    private VisualElement TokensPanel(VisualElement shell)
    {
        if (_tokensPanel != null && _tokensPanel.panel != null)
            return _tokensPanel;
        var texels = (int)(TokenPanelSize * TokenPanelScale);
        if (!TexturePanel("WOMENACE_PrepTokens", shell, texels, texels, TokenPanelScale,
            out _tokensHost, out _tokensSettings, out _tokensTexture, out _tokensPanel))
            return null;
        // The cells' words are in the shell's face, which that panel has no rule for.
        if (UI.Find(_shellRoot, UiSelector.Name("wm-shell-title")) is { } title)
            _tokenFont = title.resolvedStyle.unityFontDefinition;
        return _tokensPanel;
    }

    // Reads vanilla's markers: makes a token for each one shown, drops the tokens of those gone,
    // and takes each one's place on the map, picture and words.
    private void ReadPrepTokens(VisualElement layer)
    {
        var seen = new HashSet<IntPtr>();
        var open = _squadWindow != null ? Affinity.LeaderOf(_squadWindow) : null;
        foreach (var (kind, holderName) in new[]
        {
            (TokenKind.Contact, VanillaUi.Elements.PrepUnitPois),
            (TokenKind.Objective, VanillaUi.Elements.PrepObjectivePois),
            (TokenKind.Squad, VanillaUi.Elements.PrepDeployed),
        })
        {
            var holder = UI.Find(_shellRoot, UiSelector.Name(holderName));
            if (holder == null || holder.layout.width <= 0f)
                continue;
            var (tileX, tileZ) = (holder.layout.width / _prepMapSizeX, holder.layout.height / _prepMapSizeZ);
            for (var i = 0; i < holder.childCount; i++)
            {
                var source = holder.ElementAt(i);
                if (source.resolvedStyle.display == DisplayStyle.None || source.resolvedStyle.visibility == Visibility.Hidden)
                    continue;
                if (!_prepTokens.TryGetValue(source.Pointer, out var token))
                {
                    token = MakeToken(kind, layer);
                    if (token == null)
                        continue;
                    _prepTokens[source.Pointer] = token;
                }
                seen.Add(source.Pointer);
                // A marker's own box is empty or small: its middle is its place on the map, whose
                // far side is the element's top.
                var at = source.layout.center;
                token.Tile = new Vector2(at.x / tileX, _prepMapSizeZ - at.y / tileZ);
                switch (kind)
                {
                    case TokenKind.Squad:
                        var leader = source.TryCast<MissionPrepDeployedEntity>()?.m_Leader;
                        var material = token.Card.GetComponent<MeshRenderer>()?.material;
                        // A squad with no chibi of ours stands as its unit's own badge, or failing
                        // that as the small one vanilla's map draws for it.
                        var chibi = _prepChibisOff ? null : Chibi(Affinity.CharacterTag(leader));
                        var badge = chibi != null ? null
                            : leader?.GetTemplate()?.Badge
                                ?? UI.Find(source, UiSelector.Name(VanillaUi.Elements.PrepDeployedImage))?.resolvedStyle.backgroundImage.sprite;
                        var picture = chibi != null ? chibi : badge != null ? badge.texture : null;
                        if (material != null && picture != null && material.GetTexture("_MainTex") != picture)
                        {
                            material.SetTexture("_MainTex", picture);
                            // A badge may be one of many on a shared sheet: only its own part is shown.
                            var part = badge != null ? badge.textureRect : new Rect(0f, 0f, picture.width, picture.height);
                            material.SetVector("_UvRect", new Vector4(part.width / picture.width, part.height / picture.height, part.x / picture.width, part.y / picture.height));
                            token.FootUp = chibi != null ? ChibiFeet : 0f;
                            token.Size = chibi != null ? new Vector2(ChibiTiles * ChibiFrameAspect, ChibiTiles) : Vector2.one * BadgeTiles;
                            (token.Plays, token.Frame) = (chibi != null, -1);
                            // Squads do not blink together.
                            token.Phase = (source.Pointer.ToInt64() >> 4) % 97 / 97f * ChibiFrames;
                        }
                        var isOpen = leader != null && open != null && leader.Pointer == open.Pointer;
                        token.Ring.GetComponent<MeshRenderer>()?.material?.SetColor("_Tint", isOpen ? TokenRingOpen : TokenRing);
                        break;
                    case TokenKind.Objective:
                        token.Icon.style.backgroundImage = Picture(UI.Find(source, UiSelector.Name(VanillaUi.Elements.PrepObjectiveIcon)));
                        // Vanilla keeps four copies of the label, one per side it can stand on, and shows one.
                        foreach (var label in UI.FindAll(source, UiSelector.Name(VanillaUi.Elements.PrepObjectiveLabel)))
                            if (label.layout.width > 0f && label.TryCast<Label>() is { } shown && token.Name.text != Spaced(shown.text))
                                token.Name.text = Spaced(shown.text);
                        break;
                    case TokenKind.Contact:
                        var icon = UI.Find(source, UiSelector.Name(VanillaUi.Elements.PrepUnitIcon));
                        token.Icon.style.backgroundImage = Picture(icon);
                        if (UI.Find(source, UiSelector.Name(VanillaUi.Elements.PrepUnitText))?.TryCast<Label>() is { } words)
                        {
                            // Vanilla colours a contact by what is known of it: its words carry that colour.
                            var ink = words.resolvedStyle.color;
                            if (icon != null)
                                token.Icon.style.unityBackgroundImageTintColor = icon.resolvedStyle.unityBackgroundImageTintColor;
                            Border(token.Under.style, 1.2f, ink);
                            token.Words.text = words.text;
                            token.Words.style.color = ink;
                        }
                        break;
                }
            }
        }
        foreach (var gone in _prepTokens.Keys.Where(key => !seen.Contains(key)).ToList())
        {
            DropToken(_prepTokens[gone]);
            _prepTokens.Remove(gone);
        }
    }

    private static StyleBackground Picture(VisualElement from) =>
        from != null ? new StyleBackground(from.resolvedStyle.backgroundImage) : new StyleBackground(StyleKeyword.None);

    // Vanilla spaces an objective's name out letter by letter. Here it is the word again, spaced
    // by the label's own letter spacing.
    private static string Spaced(string text)
    {
        var words = (text ?? "").Split("  ", StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(word => word.Replace(" ", ""))).Trim();
    }

    private Texture2D Chibi(string characterTag)
    {
        if (string.IsNullOrEmpty(characterTag))
            return null;
        if (!_chibis.TryGetValue(characterTag, out var texture))
        {
            var doll = characterTag.StartsWith(Affinity.Tag + "_", StringComparison.Ordinal) ? characterTag[(Affinity.Tag.Length + 1)..] : characterTag;
            _chibis[characterTag] = texture = Context.Assets.Load<Texture2D>($"{doll}__chibi_wait");
        }
        return texture;
    }

    // A soft ring with a dot at its middle, for the ground under a chibi.
    private Texture2D RingTexture()
    {
        if (_tokenRing != null)
            return _tokenRing;
        const int size = 64;
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var r = Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f)) / 31.5f;
                var line = Mathf.Clamp01(1f - Mathf.Abs(r - 0.88f) / 0.07f);
                var fill = r < 0.88f ? 0.16f : 0f;
                // The dot at its middle is the tile the squad stands on.
                var dot = Mathf.Clamp01((0.17f - r) / 0.04f);
                var v = (byte)(255f * Mathf.Max(Mathf.Max(line, fill), dot));
                pixels[y * size + x] = new Color32(v, v, v, v);
            }
        }
        _tokenRing = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        _tokenRing.SetPixels32(pixels);
        _tokenRing.Apply(false, true);
        return _tokenRing;
    }

    // A hologram's material with nothing of the screen's about it: no scan lines, no faded
    // edge, the whole texture, all of it shown.
    private static void PlainHologram(Material material, float plate, float intensity, Color tint)
    {
        material.SetFloat("_ScanDepth", 0f);
        material.SetFloat("_EdgeFade", 0f);
        material.SetFloat("_Reveal", 1f);
        material.SetFloat("_Plate", plate);
        material.SetFloat("_Intensity", intensity);
        material.SetColor("_Tint", tint);
        material.SetVector("_UvRect", new Vector4(1f, 1f, 0f, 0f));
    }

    private GameObject TokenQuad(GameObject template, string name, Texture texture, float plate)
    {
        var quad = Quad(template, name, texture);
        var material = quad.GetComponent<MeshRenderer>()?.material;
        if (material != null)
        {
            PlainHologram(material, plate, TokenIntensity, Color.white);
            material.renderQueue = TokenQueue;
        }
        return quad;
    }

    private Token MakeToken(TokenKind kind, VisualElement layer)
    {
        var template = MarkerTemplate();
        var panel = TokensPanel(layer);
        if (template == null || panel == null)
            return null;
        var token = new Token { Kind = kind };
        if (kind == TokenKind.Squad)
        {
            token.Card = TokenQuad(template, "WOMENACE_PrepSquad", Texture2D.blackTexture, 1f);
            // A chibi or a badge is a picture with clear parts, which must add no light.
            token.Card.GetComponent<MeshRenderer>()?.material?.SetFloat("_CutByAlpha", 1f);
            token.Ring = TokenQuad(template, "WOMENACE_PrepSquadRing", RingTexture(), 0f);
            token.Ring.GetComponent<MeshRenderer>().material.renderQueue = TokenQueue - 1;
            token.Size = Vector2.one * BadgeTiles;
            return token;
        }

        // A free cell of the texture, or no token: the map has more markers than the texture holds.
        var slots = kind == TokenKind.Contact ? _contactSlots : _objectiveSlots;
        var room = kind == TokenKind.Contact ? ContactColumns * ContactRows : 2 * ObjectiveRows;
        var slot = Enumerable.Range(0, room).FirstOrDefault(i => !slots.Contains(i), -1);
        if (slot < 0)
            return null;
        slots.Add(slot);
        token.Slot = slot;
        var cell = kind == TokenKind.Contact
            ? new Rect(slot % ContactColumns * ContactCell, slot / ContactColumns * ContactCell, ContactCell, ContactCell)
            : new Rect(slot % 2 * ObjectiveCellWidth, ContactRows * ContactCell + slot / 2 * ObjectiveCellHeight, ObjectiveCellWidth, ObjectiveCellHeight);
        token.Cell = new VisualElement { pickingMode = PickingMode.Ignore };
        token.Cell.style.position = Position.Absolute;
        token.Cell.style.left = cell.x;
        token.Cell.style.top = cell.y;
        token.Cell.style.width = cell.width;
        token.Cell.style.height = cell.height;
        token.Cell.style.overflow = Overflow.Hidden;
        panel.Add(token.Cell);

        // The ring the icon stands over, at the cell's foot, and the icon.
        var iconWidth = kind == TokenKind.Contact ? ContactCell - 8f : ObjectiveIconWidth;
        var iconHeight = kind == TokenKind.Contact ? ContactCell - 8f : ObjectiveCellHeight - 4f;
        var centre = kind == TokenKind.Contact ? ContactCell / 2f : ObjectiveIconCentre;
        token.Under = Part(token.Cell, centre - 11f, cell.height - 8f, 22f, 7f);
        token.Under.style.borderTopLeftRadius = token.Under.style.borderTopRightRadius = Length.Percent(50);
        token.Under.style.borderBottomLeftRadius = token.Under.style.borderBottomRightRadius = Length.Percent(50);
        Border(token.Under.style, 1.2f, Color.white);
        token.Icon = Part(token.Cell, centre - iconWidth / 2f, cell.height - 4f - iconHeight, iconWidth, iconHeight);
        if (kind == TokenKind.Objective)
        {
            token.Name = new Label { pickingMode = PickingMode.Ignore };
            var s = token.Name.style;
            s.position = Position.Absolute;
            s.left = ObjectiveIconCentre * 2f + 2f;
            s.top = 8f;
            s.height = 20f;
            s.paddingLeft = s.paddingRight = 9f;
            s.paddingTop = s.paddingBottom = 0f;
            s.marginLeft = s.marginRight = s.marginTop = s.marginBottom = 0f;
            s.backgroundColor = TokenPlateFill;
            s.color = Color.white;
            s.fontSize = 11f;
            s.letterSpacing = 1f;
            s.unityTextAlign = TextAnchor.MiddleLeft;
            s.unityFontDefinition = _tokenFont;
            token.Cell.Add(token.Name);
        }

        token.Card = TokenQuad(template, kind == TokenKind.Contact ? "WOMENACE_PrepContact" : "WOMENACE_PrepObjective", _tokensTexture, TokenPlate);
        token.Card.GetComponent<MeshRenderer>()?.material?.SetVector("_UvRect", new Vector4(cell.width / TokenPanelSize, cell.height / TokenPanelSize,
            cell.x / TokenPanelSize, 1f - cell.yMax / TokenPanelSize));
        token.Size = new Vector2(cell.width, cell.height) * CellUnitTiles;
        token.FootAlong = centre / cell.width;
        token.FootUp = 4.5f / cell.height;

        if (kind == TokenKind.Contact)
        {
            // Its words, shown beside it while the pointer is on it.
            token.Words = new Label { pickingMode = PickingMode.Ignore };
            token.Words.AddToClassList("wm-prep-token__words");
            layer.Add(token.Words);
        }
        return token;
    }

    private static VisualElement Part(VisualElement cell, float left, float top, float width, float height)
    {
        var part = new VisualElement { pickingMode = PickingMode.Ignore };
        part.style.position = Position.Absolute;
        part.style.left = left;
        part.style.top = top;
        part.style.width = width;
        part.style.height = height;
        cell.Add(part);
        return part;
    }

    // Stands each card on its tile and keeps each contact's target over it on the screen.
    // A token coming up as the build's front reaches its tile. Only while the map is being
    // built: one made later is already standing. Tokens of a kind that come up together are
    // heard as one.
    private void HearToken(TokenKind kind)
    {
        if (!_prepMapBuilding || Time.unscaledTime - _tokenHeardAt[(int)kind] < TokenHeardApart)
            return;
        _tokenHeardAt[(int)kind] = Time.unscaledTime;
        TableSounds.Play(kind == TokenKind.Objective ? TableSounds.MapObjective : kind == TokenKind.Contact ? TableSounds.MapContact : TableSounds.TokenPlace);
    }

    private readonly float[] _tokenHeardAt = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
    private const float TokenHeardApart = 0.12f;

    private void PlacePrepTokens(VisualElement layer, (IntPtr Squad, Vector2Int Now, Vector2Int From, bool Valid)? drag)
    {
        var camera = _camera.GetComponent<Camera>();
        if (camera == null)
            return;
        // Every card is square to the view, not turned towards the eye: turned, a card off to one
        // side shows its foot as a slope. A card also leans back as the camera rises over the map,
        // so it is seen from no steeper than a set angle and does not thin to a line from above.
        var ahead = camera.transform.forward;
        var pitch = Mathf.Asin(Mathf.Clamp(-ahead.y, -1f, 1f)) * Mathf.Rad2Deg;
        ahead.y = 0f;
        if (ahead.sqrMagnitude < 1e-6f)
            ahead = camera.transform.up;
        ahead.y = 0f;
        var facing = Quaternion.LookRotation(ahead.normalized, Vector3.up) * Quaternion.Euler(Mathf.Max(0f, pitch - TokenSeenFrom), 0f, 0f);
        foreach (var token in _prepTokens.Values)
        {
            var foot = PrepMapPoint(token.Tile.x, token.Tile.y, 0.05f);
            // A token grows out of its tile as the map is built.
            var up = PrepBuilt(token.Tile.x, token.Tile.y);
            if (!token.Heard && up > 0f)
            {
                token.Heard = true;
                HearToken(token.Kind);
            }
            var (width, height) = (token.Size.x * _prepMapTile * up, token.Size.y * _prepMapTile * up);
            var card = token.Card.transform;
            card.rotation = facing;
            card.localScale = new Vector3(width, height, 1f);
            card.position = foot + facing * new Vector3(width * (0.5f - token.FootAlong), height * (0.5f - token.FootUp), 0f);
            if (token.Ring != null)
            {
                var ring = token.Ring.transform;
                ring.rotation = Quaternion.Euler(90f, 0f, 0f);
                ring.localScale = new Vector3(RingTiles * _prepMapTile * up, RingTiles * _prepMapTile * up, 1f);
                ring.position = foot;
            }
            if (token.Kind == TokenKind.Squad)
            {
                if (token.Plays)
                {
                    var frame = (int)(Time.unscaledTime * ChibiFramesPerSecond + token.Phase) % ChibiFrames;
                    if (frame != token.Frame)
                    {
                        token.Frame = frame;
                        token.Card.GetComponent<MeshRenderer>()?.material?.SetVector("_UvRect", SheetCell(frame, ChibiColumns, ChibiRows));
                    }
                }
                var body = token.Plays ? ChibiBodyWidth : 1f;
                var (low, high) = (new Vector2(float.MaxValue, float.MaxValue), new Vector2(float.MinValue, float.MinValue));
                foreach (var corner in MarkerCorners)
                {
                    var seen = camera.WorldToScreenPoint(card.TransformPoint(new Vector3(corner.x * body, corner.y, corner.z)));
                    var point = RuntimePanelUtils.ScreenToPanel(layer.panel, new Vector2(seen.x, Screen.height - seen.y));
                    (low, high) = (Vector2.Min(low, point), Vector2.Max(high, point));
                }
                token.Seen = Rect.MinMaxRect(low.x, low.y, high.x, high.y);
            }
            if (token.Words == null)
                continue;
            var screen = camera.WorldToScreenPoint(card.position);
            var at = RuntimePanelUtils.ScreenToPanel(layer.panel, new Vector2(screen.x, Screen.height - screen.y));
            var near = screen.z > 0f && Vector2.Distance(at, _prepPointerPanel) < ContactReach;
            token.Words.style.display = near ? DisplayStyle.Flex : DisplayStyle.None;
            token.Words.style.left = at.x + 12f;
            token.Words.style.top = at.y - 12f;
        }
        PlaceDragMarks(drag);
    }

    // While vanilla drags a squad: the tile it would stand on, lit gold where it may stand and
    // red where it may not, and the tile it was picked up from.
    private void PlaceDragMarks((IntPtr Squad, Vector2Int Now, Vector2Int From, bool Valid)? drag)
    {
        if (drag is not { } d)
        {
            foreach (var mark in _dragMarks)
                if (mark != null)
                    mark.SetActive(false);
            return;
        }
        var template = MarkerTemplate();
        if (template == null)
            return;
        for (var i = 0; i < _dragMarks.Length; i++)
        {
            if (_dragMarks[i] == null)
            {
                _dragMarks[i] = TokenQuad(template, "WOMENACE_PrepDragMark", Texture2D.whiteTexture, 0f);
                _dragMarks[i].GetComponent<MeshRenderer>().material.renderQueue = TokenQueue - 1;
            }
            var tile = i == 0 ? d.Now : d.From;
            var mark = _dragMarks[i].transform;
            _dragMarks[i].SetActive(true);
            mark.rotation = Quaternion.Euler(90f, 0f, 0f);
            mark.localScale = new Vector3(_prepMapTile, _prepMapTile, 1f);
            mark.position = PrepMapPoint(tile.x + 0.5f, tile.y + 0.5f, 0.03f);
            var colour = i == 0 ? (d.Valid ? TokenRingOpen : TokenInvalid) : TokenRing * 0.5f;
            _dragMarks[i].GetComponent<MeshRenderer>().material.SetColor("_Tint", colour);
        }
        // The dragged squad's ring shows the same.
        foreach (var (source, token) in _prepTokens)
            if (source == d.Squad && token.Ring != null)
                token.Ring.GetComponent<MeshRenderer>()?.material?.SetColor("_Tint", d.Valid ? TokenRingOpen : TokenInvalid);
    }
}
