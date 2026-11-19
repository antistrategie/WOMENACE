using UnityEngine;

namespace WOMENACE.Code;

// The ops room's holotable and the room round it, as the screens over the table want them: the
// table's sparks, its grid and glow, and the room's shade while the map or a briefing shows.
public sealed partial class StrategyDiorama
{
    // The sparks' objects under the table's effects. Its grid and glow are particle systems too.
    private const string TableSparks = "Particle System";

    // The table's rising lines of sparks stand along its far side, and vanilla's planets are not
    // solid, so they would show through a briefing's planet as dotted lines across it.
    private void ShowTableSparks(bool show)
    {
        if (show == _life.TableSparksShown || _scene.Room == null)
            return;
        _life.TableSparksShown = show;
        _life.SparkRenderers.RemoveAll(r => r == null);
        if (_life.SparkRenderers.Count == 0)
            foreach (var t in _scene.Room.GetComponentsInChildren<Transform>(true))
                if (t.name == TableEffects)
                    foreach (var sparks in t.GetComponentsInChildren<ParticleSystemRenderer>(true))
                        if (sparks.name.StartsWith(TableSparks, StringComparison.Ordinal))
                            _life.SparkRenderers.Add(sparks);
        foreach (var sparks in _life.SparkRenderers)
            sparks.enabled = show;
    }

    // The table's top is lit by GFL2 with a grid of lines over a sheet of glow, and curtains of
    // dots travel across it. Mission preparation's map stands on the table, and its ground,
    // roads and zones are hard to tell from a bright grid and a glow of the map's own colour
    // under them, so there both show faintly. The lines are added to the picture at some thirty times what the screen can
    // show, so they look no different until they are down to a few hundredths.
    private static readonly (string Node, float UnderMap)[] TableTopParts = { ("line (1)", 0.04f), ("line (2)", 0.04f), ("glow_an", 0.15f), ("Particle System", 0.2f), ("Particle System (1)", 0.2f) };
    private const float TableTopSeconds = 0.4f;
    private static readonly int ShownId = Shader.PropertyToID("_WmShown");

    private void DriveTableGrid()
    {
        if (_scene.Room == null)
            return;
        if (_life.TableTopMaterials.Count == 0 || _life.TableTopMaterials[0].Material == null)
        {
            _life.TableTopMaterials.Clear();
            _life.TableTopDimmed = 0f;
            foreach (var t in _scene.Room.GetComponentsInChildren<Transform>(true))
            {
                if (t.parent == null || t.parent.name != TableEffects || t.GetComponent<Renderer>() is not { } part)
                    continue;
                foreach (var (node, underMap) in TableTopParts)
                    if (t.name == node)
                        // Its own copy: other parts of the table are drawn with the same materials.
                        _life.TableTopMaterials.Add((part.material, underMap));
            }
            if (_life.TableTopMaterials.Count == 0)
                return;
        }
        var target = _policyScreen == VanillaUi.Screens.MissionPrep ? 1f : 0f;
        if (_life.TableTopDimmed == target)
            return;
        var step = HitchStep(StageLongestStep) / TableTopSeconds;
        _life.TableTopDimmed = Towards(_life.TableTopDimmed, target, step);
        foreach (var (material, underMap) in _life.TableTopMaterials)
            material.SetFloat(ShownId, 1f + (underMap - 1f) * _life.TableTopDimmed);
    }

    // The room is bright where its windows are, and the map's names and markers are thin. While
    // the map or a briefing shows, everything Womenace/BakedScene draws is darkened by this share:
    // the room and the view through its windows, and not the holograms or the dolls.
    private const float RoomShadeUnderMap = 0.55f;
    private const float RoomShadeSeconds = 0.5f;
    private static readonly int RoomShadeId = Shader.PropertyToID("_WmRoomShade");

    private void DriveRoomShade()
    {
        // The map's screen is also the home view's, where the camera is not at the table.
        var atTable = _policyScreen == VanillaUi.Screens.OperationSelect
            || (_policyScreen == VanillaUi.Screens.SystemMap && _homeView == HomeView.Operations);
        var target = atTable ? RoomShadeUnderMap : 0f;
        if (_life.RoomShade == target)
            return;
        var step = RoomShadeUnderMap * Time.unscaledDeltaTime / RoomShadeSeconds;
        _life.RoomShade = Towards(_life.RoomShade, target, step);
        Shader.SetGlobalFloat(RoomShadeId, _life.RoomShade);
    }

    // The table's own material copies go with the room. The next room's are found afresh.
    private void ClearTable()
    {
        foreach (var (material, _) in _life.TableTopMaterials)
            if (material != null)
                UnityEngine.Object.Destroy(material);
    }
}
