using UnityEngine;

namespace WOMENACE.Code;

// The sun on a doll who stands where the ops room's own lamps do not reach. GFL2 stands its
// characters round the table, under two lamps, and nobody on the walkways: there its light probes
// carry a flat ambient and no sun, which leaves a doll dull against the bright windows beside
// her. Where the baked light is bright she takes the room's sun as her key, as GFL2's own shading
// has it, in the share that light is bright. Lights of her own in its place read as pasted on.
public sealed partial class StrategyDiorama
{
    // The lamps' light on a doll at and over which she is theirs, as a lamp's intensity over
    // the square of its distance, inside its cone and range.
    private const float LampsReachLevel = 3f;
    private const float ChestHeight = 1.1f;
    // The baked light's brightness at which the sun's key starts, and at which it is whole.
    private const float WindowDim = 0.08f, WindowBright = 0.2f;

    // How bright the baked light is at a place, as the probes' flat ambient.
    private float BakedBrightness(Vector3 feet)
    {
        if (_ops.LightProbes.Length == 0)
            return 0f;
        var (sh, _, total) = BlendProbes(_ops, feet + Vector3.up * ChestHeight);
        return (sh[0].w * 0.3f + sh[1].w * 0.59f + sh[2].w * 0.11f) / total;
    }

    // The share of the sun's key a doll takes from standing in bright baked light, out of the
    // lamps' reach.
    private float WindowShare(Vector3 feet) =>
        LampsReach(feet + Vector3.up * ChestHeight) >= LampsReachLevel ? 0f : Mathf.InverseLerp(WindowDim, WindowBright, BakedBrightness(feet));

    // How far her key leans towards the way she faces, and how far up, against the way the
    // baked light comes from across the floor.
    private const float KeyTowardsFace = 0.75f, KeyUp = 0.6f;

    // The way a doll's key comes from where it is the window's light: the way the probes say the
    // baked light comes from where she stands, leant towards the way she faces and up. The sun's
    // own way fits the room's sunlit places and no other, and left a doll facing away from it
    // with her face in shadow under a lit body. Leant towards her face, the light still comes
    // from the window's side and her face takes some of it wherever she looks.
    private Vector3 WindowLightFrom(GameObject doll, Vector3 feet)
    {
        var (sh, _, total) = BlendProbes(_ops, feet + Vector3.up * ChestHeight);
        // The probes' first band points the way their light comes from, a colour at a time.
        var from = (new Vector3(sh[0].x, sh[0].y, sh[0].z) * 0.3f + new Vector3(sh[1].x, sh[1].y, sh[1].z) * 0.59f + new Vector3(sh[2].x, sh[2].y, sh[2].z) * 0.11f) / total;
        from = _room.transform.TransformDirection(from);
        from.y = 0f;
        from = from.sqrMagnitude > 1e-8f ? from.normalized : Vector3.zero;
        var faces = doll.transform.forward;
        faces.y = 0f;
        return (from + faces.normalized * KeyTowardsFace + Vector3.up * KeyUp).normalized;
    }

    // How strongly the room's lamps that light characters reach a place, in the room's space.
    private float LampsReach(Vector3 chest)
    {
        var reach = 0f;
        var at = _room.transform.TransformPoint(chest);
        foreach (var light in _ops.LiveLights)
        {
            if (light == null || !LightsCharacters(light))
                continue;
            var to = at - light.transform.position;
            var distance = to.magnitude;
            if (distance >= light.range)
                continue;
            if (light.type == LightType.Spot && Vector3.Angle(light.transform.forward, to) > light.spotAngle * 0.5f)
                continue;
            // Fading to nothing at the lamp's range, as its own falloff does.
            var window = 1f - Mathf.Pow(distance / light.range, 4f);
            reach += light.intensity / Mathf.Max(distance * distance, 0.01f) * window * window;
        }
        return reach;
    }
}
