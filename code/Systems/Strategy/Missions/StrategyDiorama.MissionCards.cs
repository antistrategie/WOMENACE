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
    private const float CardWorldWidth = 2.72f;
    private const float CardFarScale = 1.5f;
    private const float CardIntensity = 2f;
    private const float CardHoverIntensity = 2.8f;
    // How dark the room is made behind a card, so its text reads over the land's glow.
    private const float CardPlate = 0.94f;
    private static readonly Color CardTint = new(0.85f, 1f, 1f);
    private static readonly Color CardSelectedTint = new(1f, 0.93f, 0.72f);


    // The panel that draws vanilla's board into the cards' texture, made on first use. Its root
    // repeats the names, classes and stylesheets of the board's ancestors on the screen, so the
    // screen's rules still reach the markers.
    private VisualElement CardsSlot(VisualElement board)
    {
        if (_missions.CardsSlot != null && _missions.CardsSlot.panel != null)
            return _missions.CardsSlot;
        if (!TexturePanel("WOMENACE_MissionCards", board, CardsTextureWidth, CardsTextureHeight, CardsScale,
            out _missions.CardsHost, out _missions.CardsSettings, out _missions.CardsTexture, out var slot))
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
        _missions.CardsSlot = slot;
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
            places.Add((poi, _missions.MarkerBoard[poi.Pointer]));
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
        var card = Quad(template, "WOMENACE_MissionCard", _missions.CardsTexture);
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

    private void ReleaseCards()
    {
        if (_missions.CardsHost != null)
            UnityEngine.Object.Destroy(_missions.CardsHost);
        if (_missions.CardsSettings != null)
            UnityEngine.Object.Destroy(_missions.CardsSettings);
        if (_missions.CardsTexture != null)
        {
            _missions.CardsTexture.Release();
            UnityEngine.Object.Destroy(_missions.CardsTexture);
        }
        (_missions.CardsHost, _missions.CardsSettings, _missions.CardsTexture, _missions.CardsSlot) = (null, null, null, null);
        ReleaseLand();
    }
}
