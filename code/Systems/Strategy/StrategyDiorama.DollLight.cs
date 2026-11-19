using UnityEngine;

namespace WOMENACE.Code;

// The key on a doll in the ops room. GFL2 stands its characters round the table, under two lamps,
// and nobody on the walkways: its probes carry a flat ambient and no sun anywhere a doll stands,
// so GFL2's shading gives them no key at all. On a lamp-lit doll that leaves her body lit by the
// lamps and her face, which only the key shades, on the skin ramp's red dark end. Away from the
// lamps it leaves her dull against the bright windows beside her. So a doll under the lamps takes
// their light as her key, from where they are and in their colour, and any other doll takes the
// room's sun as her key, whole, from where the window's light comes. Lights of her own in its
// place read as pasted on.
public sealed partial class StrategyDiorama
{
    // The lamps' light on a doll at and over which she is theirs, as a lamp's intensity over
    // the square of its distance, inside its cone and range.
    private const float LampsReachLevel = 3f;
    private const float ChestHeight = 1.1f;

    // How far her key leans towards the way she faces, and how far up, against the way its light
    // comes from.
    private const float KeyTowardsFace = 0.75f, KeyUp = 0.6f;

    // Lights a doll standing at a place in the room's space.
    private void LightRoomDoll(GameObject doll, Vector3 feet)
    {
        var lamps = Lamps(feet + Vector3.up * ChestHeight);
        if (lamps.Reach >= LampsReachLevel)
            ProbeLight(doll, _ops, 1f, Leant(doll, lamps.From), LampKeyColour(lamps.Colour));
        else
            ProbeLight(doll, _ops, 1f, Leant(doll, WindowLightFrom(feet)));
    }

    // The lamps' colour at the sun's key's brightness, so a doll under them is as well lit as one
    // in the window's light, in their colour. The key is the ops room's own sun as the shadow light
    // takes it, read from the sun itself: the room's dolls are first lit before the shadow light
    // is pointed along it.
    private Color LampKeyColour(Color lamps)
    {
        var sun = _ops.Sun != null && LightsCharacters(_ops.Sun) ? _ops.Sun.color.linear * (_ops.Sun.intensity * Mathf.PI) : Color.black;
        var bright = Luminance(sun);
        var level = Luminance(lamps);
        return level > 1e-6f ? lamps * (bright / level) : sun;

        static float Luminance(Color c) => c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
    }

    // A key's way leant towards the way the doll faces, and up. A key along the light's own way
    // left a doll facing away from it with her face in shadow under a lit body. Leant towards her
    // face, the light still comes from its side and her face takes some of it wherever she looks.
    private static Vector3 Leant(GameObject doll, Vector3 from)
    {
        var faces = doll.transform.forward;
        faces.y = 0f;
        return (from + faces.normalized * KeyTowardsFace + Vector3.up * KeyUp).normalized;
    }

    // The way the window's light comes from where a doll stands, level, in the world: the way the
    // probes say their light comes from. The sun's own way fits the room's sunlit places and no
    // other.
    private Vector3 WindowLightFrom(Vector3 feet)
    {
        var (sh, _, total) = BlendProbes(_ops, feet + Vector3.up * ChestHeight);
        // The probes' first band points the way their light comes from, a colour at a time.
        var from = (new Vector3(sh[0].x, sh[0].y, sh[0].z) * 0.3f + new Vector3(sh[1].x, sh[1].y, sh[1].z) * 0.59f + new Vector3(sh[2].x, sh[2].y, sh[2].z) * 0.11f) / total;
        from = _room.transform.TransformDirection(from);
        from.y = 0f;
        return from.sqrMagnitude > 1e-8f ? from.normalized : Vector3.zero;
    }

    // How strongly the room's lamps that light characters reach a place in the room's space, the
    // way they reach it from (level, in the world) and their colour, each lamp weighed by its reach.
    private (float Reach, Vector3 From, Color Colour) Lamps(Vector3 chest)
    {
        var reach = 0f;
        var from = Vector3.zero;
        var colour = Color.black;
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
            var r = light.intensity / Mathf.Max(distance * distance, 0.01f) * window * window;
            reach += r;
            from -= new Vector3(to.x, 0f, to.z).normalized * r;
            colour += light.color.linear * r;
        }
        return (reach, from.sqrMagnitude > 1e-8f ? from.normalized : Vector3.zero, reach > 0f ? colour / reach : Color.black);
    }

    private float LampsReach(Vector3 chest) => Lamps(chest).Reach;
}
