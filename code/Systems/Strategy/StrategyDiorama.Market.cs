using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Audio;
using Jiangyu.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// The black market in the ops room: the barter laid out on the holotable, seen from the table's
// side. What the player gives is a block of hologram cubes on a square at the left, what they
// get is one at the right, and the difference between the two is a number lying on the table
// between them. Vanilla's lists stand at the sides, and what is on offer in a strip
// under the table with the Trade button over it.
public sealed partial class StrategyDiorama
{
    private const string MarketClass = "wm-market";
    private const string MarketFoldName = "wm-market-fold";

    // The squares' side and their middles along the table, in the room's space: the player's is
    // at the left of the view, which looks along -z with +x at its left.
    private const float MarketSquare = 1.2f;
    private const float MarketTableY = -0.12f;
    private const float MarketTableZ = 0f;
    private static readonly float[] MarketSquareX = { -13.3f, -15.5f };
    private const float MarketMiddleX = -14.4f;
    // A cube is this much trade value, a block is this many cubes to an edge at first, and at
    // most this many: past that a cube is worth more.
    private const int MarketCubeWorth = 10;
    private const int MarketEdge = 5;
    private const int MarketMostEdge = 8;
    private const int MarketMostCubes = MarketMostEdge * MarketMostEdge * MarketMostEdge;
    // How much of its cell a cube fills, how far over the table it comes in from, how long the
    // next one waits, and how fast it falls.
    private const float MarketCubeFill = 0.84f;
    private const float MarketDropHeight = 1.6f;
    private const float MarketDropStagger = 0.018f;
    private const float MarketGravity = 48f;
    private const float MarketDropSpeed = 2.5f;
    private const float MarketIntensity = 1f;
    private const int MarketFaceTexture = 64;
    private const int MarketFaceLine = 4;
    private const byte MarketFaceFill = 14;
    // The number's quad on the table, and its texture.
    private const float MarketNumberWidth = 0.9f;
    private const int MarketNumberTextureWidth = 256;
    private const int MarketNumberTextureHeight = 128;
    private const float MarketNumberIntensity = 1.8f;

    // The panels, in the shell's units.
    private const float MarketPanelWidth = 276f;
    private const float MarketStripTop = 498f;
    private const float MarketStripGap = 8f;
    private const float MarketButtonHeight = 28f;
    // How far inside the strip's top the holder of its parts starts.
    private const float MarketStripInset = 3f;
    private const float MarketStripSeconds = 0.22f;

    private sealed class MarketCube
    {
        public float X, Z, Drop, Speed, Size, Wait;
        public bool Placed, Shown, Leaving;
    }

    private GameObject _marketCubes, _marketNumber, _marketNumberHost;
    private Mesh _marketMesh;
    private Texture2D _marketFace;
    private RenderTexture _marketNumberTexture;
    private PanelSettings _marketNumberSettings;
    private Label _marketNumberLabel;
    private Il2CppStructArray<Vector3> _marketVertices;
    private Il2CppStructArray<Color32> _marketColours;
    private Color32 _marketGold, _marketCyan, _marketOver, _marketShort;
    private readonly List<MarketCube>[] _marketPiles = { new(), new() };
    private readonly int[] _marketCounts = new int[2];
    private readonly int[] _marketWritten = new int[2];
    private int _marketGive = -1, _marketGet = -1, _marketEdge = MarketEdge;
    private bool _marketOn, _marketMoving;
    // Whether the strip of what is on offer is opened to its full height, how far open it stands,
    // and the strip with its chevron.
    private bool _marketStripOpen;
    private float _marketStripAt;
    private VisualElement _marketStrip, _marketFold;

    // Each face's corners, as the corner's bits: x, y, z.
    private static readonly int[] MarketFaces =
    {
        0, 1, 3, 2, 4, 5, 7, 6, 0, 1, 5, 4, 2, 3, 7, 6, 0, 2, 6, 4, 1, 3, 7, 5,
    };

    // The cubes' mesh and the number's quad, made once for the room. False where the room has
    // no hologram quad to copy.
    private bool MarketPicture()
    {
        if (_marketCubes != null)
            return true;
        var template = MarkerTemplate();
        if (template == null)
            return false;

        // A cube's face: a bright line round a faint fill.
        _marketFace = new Texture2D(MarketFaceTexture, MarketFaceTexture, TextureFormat.RGBA32, true) { name = "WOMENACE_MarketCube" };
        var pixels = new Il2CppStructArray<Color32>(MarketFaceTexture * MarketFaceTexture);
        var (line, fill) = (new Color32(255, 255, 255, 255), new Color32(MarketFaceFill, MarketFaceFill, MarketFaceFill, 255));
        for (var y = 0; y < MarketFaceTexture; y++)
            for (var x = 0; x < MarketFaceTexture; x++)
            {
                var edge = Math.Min(Math.Min(x, y), Math.Min(MarketFaceTexture - 1 - x, MarketFaceTexture - 1 - y));
                pixels[y * MarketFaceTexture + x] = edge < MarketFaceLine ? line : fill;
            }
        _marketFace.SetPixels32(pixels);
        _marketFace.Apply(true, true);

        var corners = MarketMostCubes * 2 * 24;
        _marketVertices = new Il2CppStructArray<Vector3>(corners);
        _marketColours = new Il2CppStructArray<Color32>(corners);
        var uv = new Il2CppStructArray<Vector2>(corners);
        var triangles = new Il2CppStructArray<int>(MarketMostCubes * 2 * 36);
        for (var face = 0; face < MarketMostCubes * 2 * 6; face++)
        {
            var (v, t) = (face * 4, face * 6);
            (uv[v], uv[v + 1], uv[v + 2], uv[v + 3]) = (new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            (triangles[t], triangles[t + 1], triangles[t + 2], triangles[t + 3], triangles[t + 4], triangles[t + 5]) = (v, v + 1, v + 2, v, v + 2, v + 3);
        }
        _marketMesh = new Mesh { name = "WOMENACE_MarketCubes" };
        _marketMesh.MarkDynamic();
        _marketMesh.vertices = _marketVertices;
        _marketMesh.uv = uv;
        _marketMesh.colors32 = _marketColours;
        _marketMesh.triangles = triangles;
        // Never culled: the cubes are always over the table the camera looks at.
        _marketMesh.bounds = new Bounds(new Vector3(MarketMiddleX, MarketTableY, MarketTableZ), Vector3.one * 8f);
        (_marketGold, _marketCyan) = (Wui.Gold, new Color32(79, 220, 234, 255));
        (_marketOver, _marketShort) = (new Color32(243, 234, 208, 255), new Color32(255, 123, 108, 255));

        _marketCubes = Quad(template, "WOMENACE_MarketCubes", _marketFace);
        _marketCubes.transform.localPosition = Vector3.zero;
        _marketCubes.transform.localRotation = Quaternion.identity;
        _marketCubes.transform.localScale = Vector3.one;
        _marketCubes.GetComponent<MeshFilter>().sharedMesh = _marketMesh;
        PlainHologram(_marketCubes, MarketIntensity);

        MarketNumber(template);
        return true;
    }

    // The number between the squares: a label drawn into a texture by a panel of its own, on a
    // quad lying on the table with its foot towards the camera.
    private void MarketNumber(GameObject template)
    {
        if (!TexturePanel("WOMENACE_MarketNumber", _shellRoot, MarketNumberTextureWidth, MarketNumberTextureHeight, 1f,
            out _marketNumberHost, out _marketNumberSettings, out _marketNumberTexture, out var root))
            return;
        _marketNumberLabel = new Label { pickingMode = PickingMode.Ignore };
        var style = _marketNumberLabel.style;
        style.position = Position.Absolute;
        style.left = 0;
        style.top = 0;
        style.right = 0;
        style.bottom = 0;
        style.fontSize = 84;
        style.unityFontStyleAndWeight = FontStyle.Bold;
        style.unityTextAlign = TextAnchor.MiddleCenter;
        style.marginLeft = style.marginRight = style.marginTop = style.marginBottom = 0;
        style.paddingLeft = style.paddingRight = style.paddingTop = style.paddingBottom = 0;
        root.Add(_marketNumberLabel);

        _marketNumber = Quad(template, "WOMENACE_MarketNumber", _marketNumberTexture);
        _marketNumber.transform.localPosition = new Vector3(MarketMiddleX, MarketTableY + 0.005f, MarketTableZ);
        // Its right is the view's right and its top is away from the camera.
        _marketNumber.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.back);
        _marketNumber.transform.localScale = new Vector3(MarketNumberWidth, MarketNumberWidth * MarketNumberTextureHeight / MarketNumberTextureWidth, 1f);
        PlainHologram(_marketNumber, MarketNumberIntensity);
    }

    // A panel of its own that draws into a texture, for what is shown on a quad in the room: it
    // takes the theme and text settings of the panel an element is in, and draws on nothing at
    // a fixed scale.
    private static bool TexturePanel(string name, VisualElement like, int width, int height, float scale,
        out GameObject host, out PanelSettings settings, out RenderTexture texture, out VisualElement root)
    {
        (host, settings, texture, root) = (null, null, null, null);
        UIDocument source = null;
        if (like?.panel != null)
            foreach (var document in UnityEngine.Object.FindObjectsOfType<UIDocument>())
                if (document.rootVisualElement?.panel == like.panel)
                    source = document;
        if (source?.panelSettings == null)
            return false;
        texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = name };
        settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.themeStyleSheet = source.panelSettings.themeStyleSheet;
        settings.textSettings = source.panelSettings.textSettings;
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = scale;
        settings.clearColor = true;
        settings.colorClearValue = new Color(0f, 0f, 0f, 0f);
        settings.targetTexture = texture;
        host = new GameObject(name);
        var panel = host.AddComponent<UIDocument>();
        panel.panelSettings = settings;
        root = panel.rootVisualElement;
        return root != null;
    }

    // A hologram quad that shows its texture as it is: no tint, scan lines, plate or faded edge.
    private static void PlainHologram(GameObject quad, float intensity)
    {
        var material = quad.GetComponent<MeshRenderer>().material;
        material.SetColor("_Tint", Color.white);
        material.SetFloat(IntensityId, intensity);
        material.SetFloat("_ScanDepth", 0f);
        material.SetFloat("_Plate", 0f);
        material.SetFloat("_EdgeFade", 0f);
    }

    // What the market showed goes with its room, and is made again when the room is.
    private void ClearMarket()
    {
        foreach (var quad in new[] { _marketCubes, _marketNumber })
            if (quad != null)
            {
                UnityEngine.Object.Destroy(quad.GetComponent<MeshRenderer>()?.sharedMaterial);
                UnityEngine.Object.Destroy(quad);
            }
        foreach (var made in new UnityEngine.Object[] { _marketMesh, _marketFace, _marketNumberHost, _marketNumberSettings })
            if (made != null)
                UnityEngine.Object.Destroy(made);
        ReleaseTexture(ref _marketNumberTexture);
        (_marketCubes, _marketNumber, _marketNumberHost, _marketMesh, _marketFace, _marketNumberSettings, _marketNumberLabel) = (null, null, null, null, null, null, null);
        (_marketVertices, _marketColours, _marketOn) = (null, null, false);
        (_marketStrip, _marketFold) = (null, null);
        (_marketWritten[0], _marketWritten[1]) = (0, 0);
        foreach (var pile in _marketPiles)
            pile.Clear();
    }

    private void DriveMarket()
    {
        var on = _policyScreen == VanillaUi.Screens.BlackMarket && _room != null && _camera != null && _active == _ops
            && _shellRoot?.panel != null;
        if (!on || !MarketPicture())
        {
            if (_marketOn)
                ShowMarket(false);
            return;
        }
        if (!_marketOn)
        {
            // Each visit starts with a bare table: what is on offer drops in again.
            ShowMarket(true);
            foreach (var pile in _marketPiles)
                pile.Clear();
            (_marketGive, _marketGet, _marketMoving) = (-1, -1, true);
            (_marketStripOpen, _marketStripAt) = (false, 0f);
            PlaceMarketStrip();
        }
        // The strip goes between its two heights over a moment.
        var stripTo = _marketStripOpen ? 1f : 0f;
        if (_marketStripAt != stripTo)
        {
            var step = HitchStep(ShotHitchSeconds, ShotHitchStep) / MarketStripSeconds;
            _marketStripAt = _marketStripOpen ? MathF.Min(1f, _marketStripAt + step) : MathF.Max(0f, _marketStripAt - step);
            PlaceMarketStrip();
        }
        if (UnityEngine.Time.frameCount % 6 == 0
            && Il2CppMenace.UI.UIManager.Get()?.GetActiveScreen()?.TryCast<BlackMarketUIScreen>() is { } screen)
        {
            var (give, get) = (screen.CalcItemsSellValue(), screen.CalcItemsBuyValue());
            if (give != _marketGive || get != _marketGet)
                SetMarket(give, get);
        }
        if (_marketMoving)
            MoveMarketCubes(HitchStep(ShotHitchSeconds, ShotHitchStep));
    }

    // The strip's top, and its chevron's, for how far open it is: opened, it takes the whole
    // height under its buttons, over the table.
    private void PlaceMarketStrip()
    {
        if (_marketStrip?.panel == null || _marketFold == null)
            return;
        var top = MarketStripTop + (SquadsTop + MarketButtonHeight + MarketStripGap - MarketStripTop) * Ease.InOutCubic(_marketStripAt);
        _marketStrip.style.top = top;
        _marketStrip.style.height = SquadsBottom - top;
        _marketFold.style.top = top - MarketButtonHeight - MarketStripGap;
    }

    private void ShowMarket(bool on)
    {
        if (on != _marketOn)
            Cue(on ? TableCue.Appear : TableCue.Vanish);
        _marketOn = on;
        _marketCubes?.SetActive(on);
        _marketNumber?.SetActive(on);
    }

    // The two sides' worth, as cubes: both blocks share the cube's worth and size, which the
    // bigger side sets.
    private void SetMarket(int give, int get)
    {
        // The first worth of a visit arrives with the table. A change after it is counted out.
        if (_marketGive >= 0)
            TableSounds.Play(TableSounds.Count);
        (_marketGive, _marketGet) = (give, get);
        var worth = MarketCubeWorth;
        while ((Math.Max(give, get) + worth - 1) / worth > MarketMostCubes)
            worth *= 2;
        (_marketCounts[0], _marketCounts[1]) = ((give + worth - 1) / worth, (get + worth - 1) / worth);
        var most = Math.Max(_marketCounts[0], _marketCounts[1]);
        _marketEdge = MarketEdge;
        while (_marketEdge * _marketEdge * _marketEdge < most)
            _marketEdge++;
        for (var side = 0; side < 2; side++)
        {
            var pile = _marketPiles[side];
            var staying = pile.Count(c => !c.Leaving);
            if (_marketCounts[side] > staying)
            {
                // Cubes on their way out stand at the block's end, and go before any is added.
                pile.RemoveAll(c => c.Leaving);
                for (var i = staying; i < _marketCounts[side]; i++)
                    pile.Add(new MarketCube { Drop = MarketDropHeight, Speed = -MarketDropSpeed, Wait = (i - staying) * MarketDropStagger });
            }
            else
                for (int i = pile.Count - 1, going = staying - _marketCounts[side]; i >= 0 && going > 0; i--)
                    if (!pile[i].Leaving)
                    {
                        pile[i].Leaving = true;
                        going--;
                    }
        }
        if (_marketNumberLabel != null)
        {
            var balance = give - get;
            // Nothing on either side is no trade, which has no balance to show.
            _marketNumberLabel.text = give + get == 0 ? "" : balance > 0 ? $"+{balance}" : balance.ToString();
            _marketNumberLabel.style.color = balance < 0 ? new Color(1f, 0.48f, 0.42f) : give + get > 0 ? new Color(0.33f, 0.79f, 0.61f) : Color.white;
        }
        _marketMoving = true;
    }

    // Every cube goes towards its cell, falls if it is in the air, and is written into the mesh.
    private void MoveMarketCubes(float seconds)
    {
        var ease = 1f - MathF.Exp(-seconds * 9f);
        var cell = MarketSquare / _marketEdge;
        var level = Math.Min(_marketCounts[0], _marketCounts[1]);
        var moving = false;
        for (var side = 0; side < 2; side++)
        {
            var pile = _marketPiles[side];
            var (shown, index) = (0, 0);
            var floor = _marketEdge * _marketEdge;
            for (var i = 0; i < pile.Count; i++)
            {
                var cube = pile[i];
                var (layer, row, column) = (index / floor, index % floor / _marketEdge, index % _marketEdge);
                // Filled from the view's left and from the far row, a floor at a time.
                var toX = MarketSquareX[side] + MarketSquare / 2f - (column + 0.5f) * cell;
                var toZ = MarketTableZ - MarketSquare / 2f + (row + 0.5f) * cell;
                if (!cube.Placed)
                    (cube.X, cube.Z, cube.Placed) = (toX, toZ, true);
                cube.X += (toX - cube.X) * ease;
                cube.Z += (toZ - cube.Z) * ease;
                var size = cell * MarketCubeFill;
                if (cube.Leaving)
                {
                    // Taken off the table by shrinking away where it stands.
                    cube.Size -= cube.Size * ease;
                    if (cube.Size < 0.003f)
                    {
                        pile.RemoveAt(i--);
                        continue;
                    }
                }
                else if (cube.Wait > 0f)
                    cube.Wait -= seconds;
                else
                {
                    // A cube comes in whole. Its size changes only with the block's cells.
                    cube.Size = cube.Shown ? cube.Size + (size - cube.Size) * ease : size;
                    cube.Shown = true;
                    if (cube.Drop > 0f || cube.Speed != 0f)
                    {
                        cube.Speed -= MarketGravity * seconds;
                        cube.Drop += cube.Speed * seconds;
                        if (cube.Drop <= 0f)
                            (cube.Drop, cube.Speed) = (0f, MathF.Abs(cube.Speed) > 3f ? -cube.Speed * 0.14f : 0f);
                    }
                }
                moving |= cube.Leaving || cube.Wait > 0f || cube.Drop > 0f || cube.Speed != 0f || MathF.Abs(size - cube.Size) > 0.0005f
                    || MathF.Abs(toX - cube.X) > 0.0005f || MathF.Abs(toZ - cube.Z) > 0.0005f;
                var colour = cube.Leaving || index < level ? side == 0 ? _marketGold : _marketCyan : side == 0 ? _marketOver : _marketShort;
                if (shown < MarketMostCubes)
                    WriteMarketCube(side * MarketMostCubes + shown++, cube.X, MarketTableY + (layer + 0.5f) * cell + cube.Drop, cube.Z, cube.Size / 2f, colour);
                if (!cube.Leaving)
                    index++;
            }
            // Places a block has given up are drawn as nothing.
            for (var gone = shown; gone < _marketWritten[side]; gone++)
                WriteMarketCube(side * MarketMostCubes + gone, 0f, 0f, 0f, 0f, _marketGold);
            _marketWritten[side] = shown;
        }
        _marketMesh.vertices = _marketVertices;
        _marketMesh.colors32 = _marketColours;
        _marketMoving = moving;
    }

    private void WriteMarketCube(int place, float x, float y, float z, float half, Color32 colour)
    {
        var first = place * 24;
        var corner = default(Vector3);
        for (var i = 0; i < 24; i++)
        {
            var bits = MarketFaces[i];
            corner.x = x + ((bits & 1) != 0 ? half : -half);
            corner.y = y + ((bits & 2) != 0 ? half : -half);
            corner.z = z + ((bits & 4) != 0 ? half : -half);
            _marketVertices[first + i] = corner;
            _marketColours[first + i] = colour;
        }
    }

    // Vanilla's three panels: the player's items at the left, the market's at the right, and
    // what is on offer in a strip under the table, with the Trade button and the chevron that
    // opens the strip over it. The balance is the number on the table.
    private void LayOutMarket(VisualElement root)
    {
        var panels = UI.Find(root, UiSelector.Name(VanillaUi.Elements.Panels));
        if (panels == null || _active != _ops)
            return;
        if (!root.ClassListContains(MarketClass))
        {
            root.AddToClassList(MarketClass);
            _shellUndo.Add(() => root.RemoveFromClassList(MarketClass));
        }
        // Vanilla's shadow is as wide as the root, which on a screen wider than 16:9 leaves the
        // room lit at its sides: it is drawn out to the screen's edges.
        var shadow = UI.Find(root, UiSelector.Name(VanillaUi.Elements.MarketShadow));
        if (shadow != null && shadow.parent == root && _shellInset is { } inset)
            Inline(shadow, e => { e.style.left = -inset.Left; e.style.right = -inset.Right; e.style.width = StyleKeyword.Auto; },
                e => { e.style.left = StyleKeyword.Null; e.style.right = StyleKeyword.Null; e.style.width = StyleKeyword.Null; });
        var (right, tall) = (1280f - RosterLeft, SquadsBottom - SquadsTop);
        Dock(panels, 0f, 0f, 1280f, 720f);
        Dock(UI.Find(panels, UiSelector.Name(VanillaUi.Elements.MarketPlayerPanel)), RosterLeft, SquadsTop, MarketPanelWidth, tall);
        Dock(UI.Find(panels, UiSelector.Name(VanillaUi.Elements.MarketMerchantPanel)), right - MarketPanelWidth, SquadsTop, MarketPanelWidth, tall);
        var stripLeft = RosterLeft + MarketPanelWidth + MarketStripGap;
        var trade = UI.Find(panels, UiSelector.Name(VanillaUi.Elements.MarketTradePanel));
        var stripRight = right - MarketPanelWidth - MarketStripGap;
        Dock(trade, stripLeft, MarketStripTop, stripRight - stripLeft, SquadsBottom - MarketStripTop);
        var fold = UI.Find(panels, UiSelector.Name(MarketFoldName));
        if (fold == null)
        {
            // A chevron: up to open the strip, down to shut it.
            fold = new VisualElement { name = MarketFoldName };
            fold.AddToClassList("wui-mini");
            fold.AddToClassList("wm-market-fold");
            var mark = new VisualElement { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("wm-market-fold__mark");
            fold.Add(mark);
            fold.RegisterCallback((EventCallback<ClickEvent>)(_ =>
            {
                Sound.Click();
                _marketStripOpen = !_marketStripOpen;
                fold.EnableInClassList("wm-market-fold--open", _marketStripOpen);
            }));
            panels.Add(fold);
            _shellUndo.Add(fold.RemoveFromHierarchy);
        }
        fold.EnableInClassList("wm-market-fold--open", _marketStripOpen);
        fold.style.right = 1280f - stripRight;
        (_marketStrip, _marketFold) = (trade, fold);
        PlaceMarketStrip();
        // The lists scroll on the shell's slim bar.
        foreach (var scroll in UI.FindAll(panels, UiSelector.TypeName("ScrollView")))
            if (!scroll.ClassListContains(SlimScrollClass))
            {
                scroll.AddToClassList(SlimScrollClass);
                _shellUndo.Add(() => scroll.RemoveFromClassList(SlimScrollClass));
            }
        if (trade == null)
            return;
        // The table's number is the balance, so vanilla's scales and its number take no room.
        foreach (var name in new[] { VanillaUi.Elements.MarketBalanceIcon, VanillaUi.Elements.MarketBalanceValue })
            Inline(UI.Find(trade, UiSelector.Name(name)), e => e.style.display = DisplayStyle.None, e => e.style.display = StyleKeyword.Null);
        // The offers fill the strip, and the button stands over its middle, outside the panel.
        var button = UI.Find(trade, UiSelector.Name(VanillaUi.Elements.MarketTradeButton));
        var offers = UI.Find(trade, UiSelector.Name(VanillaUi.Elements.MarketPlayerOffer))?.parent;
        Inline(offers, e =>
        {
            e.style.flexGrow = 1;
            e.style.flexShrink = 1;
            e.style.marginTop = 0;
            e.style.marginBottom = 0;
        }, e =>
        {
            e.style.flexGrow = StyleKeyword.Null;
            e.style.flexShrink = StyleKeyword.Null;
            e.style.marginTop = StyleKeyword.Null;
            e.style.marginBottom = StyleKeyword.Null;
        });
        Inline(button, e =>
        {
            e.style.position = Position.Absolute;
            e.style.left = Length.Percent(50f);
            e.style.translate = new Translate(Length.Percent(-50f), 0f);
            e.style.top = -(MarketButtonHeight + MarketStripGap + MarketStripInset);
            e.style.height = MarketButtonHeight;
            e.style.marginTop = 0;
            e.style.marginBottom = 0;
        }, e =>
        {
            e.style.position = StyleKeyword.Null;
            e.style.left = StyleKeyword.Null;
            e.style.translate = StyleKeyword.Null;
            e.style.top = StyleKeyword.Null;
            e.style.height = StyleKeyword.Null;
            e.style.marginTop = StyleKeyword.Null;
            e.style.marginBottom = StyleKeyword.Null;
        });
    }
}
