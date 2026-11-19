using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace WOMENACE.Code;

// GFL2's tonemapping and colour lookup. GFL2 (URP) tonemaps with UE4's filmic curve (its
// Tonemapping mode 3: slope, toe, shoulder, black and white clip, through ACES's glow and red
// modifiers in AP1), then blends its ColorLookup strip in on the display-encoded colour. HDRP
// applies a lookup before its tonemapper, in LogC, so neither step can be handed over as it is. The
// room camera instead tonemaps through HDRP's external LUT mode, with a LUT built here per room
// that does both: for each LogC input it tonemaps with GFL2's curve, encodes to sRGB, blends the
// room's lookup in at its contribution, and decodes back to the linear display value HDRP writes
// out. The strip ships uncompressed (Womenace.EditorTools.BuildRoomGrade).
public sealed partial class StrategyDiorama
{
    private const string GradeAsset = "environment/ops_room_grade/main";

    private Color32[] _gfl2Lut;
    private int _gfl2LutWidth, _gfl2LutHeight;

    private Texture3D BuildGradeLut(RoomLook look)
    {
        try
        {
            if (look.LookupContribution > 0f && _gfl2Lut == null)
            {
                var prefab = Context.Assets.Load<GameObject>(GradeAsset);
                var strip = prefab?.GetComponent<MeshRenderer>()?.sharedMaterial?.mainTexture?.TryCast<Texture2D>();
                if (strip == null)
                {
                    Context.Log.Warn($"strategy diorama: '{GradeAsset}' carries no lookup strip, tonemapping only");
                }
                else
                {
                    _gfl2Lut = strip.GetPixels32();
                    (_gfl2LutWidth, _gfl2LutHeight) = (strip.width, strip.height);
                }
            }
            var size = HDRenderPipeline.currentAsset?.currentPlatformRenderPipelineSettings.postProcessSettings.lutSize ?? 32;
            var lut = new Texture3D(size, size, size, TextureFormat.RGBAHalf, false)
            {
                name = "WOMENACE_RoomGrade",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            lut.SetPixels(BuildGrade(size, look));
            lut.Apply(false, false);
            return lut;
        }
        catch (System.Exception ex)
        {
            Context.Log.Warn($"strategy diorama: grade failed, keeping ACES: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private Color[] BuildGrade(int size, RoomLook look)
    {
        var contribution = _gfl2Lut != null ? look.LookupContribution : 0.0;
        var pixels = new Color[size * size * size];
        var step = 1.0 / (size - 1);
        var linear = new double[3];
        for (var b = 0; b < size; b++)
            for (var g = 0; g < size; g++)
                for (var r = 0; r < size; r++)
                {
                    linear[0] = LogCToLinear(r * step);
                    linear[1] = LogCToLinear(g * step);
                    linear[2] = LogCToLinear(b * step);
                    var toned = FilmToneMap(linear, look);
                    var rgb = new double[3];
                    for (var i = 0; i < 3; i++)
                        rgb[i] = LinearToSrgb(System.Math.Clamp(toned[i], 0.0, 1.0));
                    if (contribution > 0.0)
                    {
                        var graded = SampleGfl2(rgb[0], rgb[1], rgb[2]);
                        for (var i = 0; i < 3; i++)
                            rgb[i] += (graded[i] - rgb[i]) * contribution;
                    }
                    pixels[r + g * size + b * size * size] = new Color((float)SrgbToLinear(rgb[0]), (float)SrgbToLinear(rgb[1]), (float)SrgbToLinear(rgb[2]), 1f);
                }
        return pixels;
    }

    // UE4's FilmToneMap (TonemapCommon.ush) on linear sRGB, returning linear sRGB.
    private static readonly double[,] SrgbToAp1 = { { 0.613097, 0.339523, 0.047379 }, { 0.070194, 0.916354, 0.013452 }, { 0.020616, 0.109570, 0.869815 } };
    private static readonly double[,] Ap1ToSrgb = { { 1.704859, -0.621715, -0.083299 }, { -0.130078, 1.140734, -0.010560 }, { -0.023964, -0.128975, 1.153013 } };
    private static readonly double[,] Ap1ToAp0 = { { 0.6954522414, 0.1406786965, 0.1638690622 }, { 0.0447945634, 0.8596711185, 0.0955343182 }, { -0.0055258826, 0.0040252103, 1.0015006723 } };
    private static readonly double[,] Ap0ToAp1 = { { 1.4514393161, -0.2365107469, -0.2149285693 }, { -0.0765537734, 1.1762296998, -0.0996759264 }, { 0.0083161484, -0.0060324498, 0.9977163014 } };
    private static readonly double[] Ap1Luma = { 0.2722287168, 0.6740817658, 0.0536895174 };

    private static double[] Mul(double[,] m, double[] v) => new[]
    {
        m[0, 0] * v[0] + m[0, 1] * v[1] + m[0, 2] * v[2],
        m[1, 0] * v[0] + m[1, 1] * v[1] + m[1, 2] * v[2],
        m[2, 0] * v[0] + m[2, 1] * v[1] + m[2, 2] * v[2],
    };

    private static double[] FilmToneMap(double[] linearSrgb, RoomLook look)
    {
        var ap0 = Mul(Ap1ToAp0, Mul(SrgbToAp1, linearSrgb));

        // ACES's glow module and red modifier.
        var max = System.Math.Max(ap0[0], System.Math.Max(ap0[1], ap0[2]));
        var min = System.Math.Min(ap0[0], System.Math.Min(ap0[1], ap0[2]));
        var saturation = (System.Math.Max(max, 1e-10) - System.Math.Max(min, 1e-10)) / System.Math.Max(max, 1e-2);
        var chroma = System.Math.Sqrt(System.Math.Max(0.0, ap0[2] * (ap0[2] - ap0[1]) + ap0[1] * (ap0[1] - ap0[0]) + ap0[0] * (ap0[0] - ap0[2])));
        var yc = (ap0[0] + ap0[1] + ap0[2] + 1.75 * chroma) / 3.0;
        var x = (saturation - 0.4) / 0.2;
        var t = System.Math.Max(1.0 - System.Math.Abs(x / 2.0), 0.0);
        var s = (1.0 + System.Math.Sign(x) * (1.0 - t * t)) / 2.0;
        var gain = 0.05 * s;
        var glow = yc <= 2.0 / 3.0 * 0.08 ? gain : yc >= 2.0 * 0.08 ? 0.0 : gain * (0.08 / yc - 0.5);
        for (var i = 0; i < 3; i++)
            ap0[i] *= 1.0 + glow;
        var hue = ap0[0] == ap0[1] && ap0[1] == ap0[2] ? 0.0
            : System.Math.Atan2(System.Math.Sqrt(3.0) * (ap0[1] - ap0[2]), 2.0 * ap0[0] - ap0[1] - ap0[2]) * 180.0 / System.Math.PI;
        if (hue < 0.0)
            hue += 360.0;
        var centred = hue > 180.0 ? hue - 360.0 : hue;
        var hueWeight = SmoothStep(1.0 - System.Math.Abs(2.0 * centred / 135.0));
        hueWeight *= hueWeight;
        ap0[0] += hueWeight * saturation * (0.03 - ap0[0]) * (1.0 - 0.82);

        var working = Mul(Ap0ToAp1, ap0);
        for (var i = 0; i < 3; i++)
            working[i] = System.Math.Max(0.0, working[i]);
        var luma = Dot(working, Ap1Luma);
        for (var i = 0; i < 3; i++)
            working[i] = luma + (working[i] - luma) * 0.96;

        double slope = look.FilmSlope, toe = look.FilmToe, shoulder = look.FilmShoulder, black = look.FilmBlackClip, white = look.FilmWhiteClip;
        var toeScale = 1.0 + black - toe;
        var shoulderScale = 1.0 + white - shoulder;
        const double inMatch = 0.18, outMatch = 0.18;
        double toeMatch;
        if (toe > 0.8)
        {
            toeMatch = (1.0 - toe - outMatch) / slope + System.Math.Log10(inMatch);
        }
        else
        {
            var bt = (outMatch + black) / toeScale - 1.0;
            toeMatch = System.Math.Log10(inMatch) - 0.5 * System.Math.Log((1.0 + bt) / (1.0 - bt)) * (toeScale / slope);
        }
        var straightMatch = (1.0 - toe) / slope - toeMatch;
        var shoulderMatch = shoulder / slope - straightMatch;

        var tone = new double[3];
        for (var i = 0; i < 3; i++)
        {
            var log = System.Math.Log10(System.Math.Max(working[i], 1e-10));
            var straight = slope * (log + straightMatch);
            var toeColour = -black + 2.0 * toeScale / (1.0 + System.Math.Exp(-2.0 * slope / toeScale * (log - toeMatch)));
            var shoulderColour = 1.0 + white - 2.0 * shoulderScale / (1.0 + System.Math.Exp(2.0 * slope / shoulderScale * (log - shoulderMatch)));
            toeColour = log < toeMatch ? toeColour : straight;
            shoulderColour = log > shoulderMatch ? shoulderColour : straight;
            var blend = System.Math.Clamp((log - toeMatch) / (shoulderMatch - toeMatch), 0.0, 1.0);
            if (shoulderMatch < toeMatch)
                blend = 1.0 - blend;
            blend = (3.0 - 2.0 * blend) * blend * blend;
            tone[i] = toeColour + (shoulderColour - toeColour) * blend;
        }
        var toneLuma = Dot(tone, Ap1Luma);
        for (var i = 0; i < 3; i++)
            tone[i] = System.Math.Max(0.0, toneLuma + (tone[i] - toneLuma) * 0.93);
        return Mul(Ap1ToSrgb, tone);
    }

    private static double SmoothStep(double x)
    {
        x = System.Math.Clamp(x, 0.0, 1.0);
        return x * x * (3.0 - 2.0 * x);
    }

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    // GFL2's strip: slices of side x side along x by blue, red across a slice, green up the rows.
    private double[] SampleGfl2(double r, double g, double b)
    {
        var side = _gfl2LutHeight;
        var max = side - 1;
        double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        var (x, y, z) = (Clamp(r) * max, Clamp(g) * max, Clamp(b) * max);
        int x0 = (int)x, y0 = (int)y, z0 = (int)z;
        int x1 = System.Math.Min(x0 + 1, max), y1 = System.Math.Min(y0 + 1, max), z1 = System.Math.Min(z0 + 1, max);
        double fx = x - x0, fy = y - y0, fz = z - z0;
        var result = new double[3];
        for (var i = 0; i < 3; i++)
        {
            double Texel(int xi, int yi, int zi)
            {
                var c = _gfl2Lut[(zi * side + xi) + yi * _gfl2LutWidth];
                return (i == 0 ? c.r : i == 1 ? c.g : c.b) / 255.0;
            }
            double Lerp(double a, double c, double t) => a + (c - a) * t;
            var c00 = Lerp(Texel(x0, y0, z0), Texel(x1, y0, z0), fx);
            var c10 = Lerp(Texel(x0, y1, z0), Texel(x1, y1, z0), fx);
            var c01 = Lerp(Texel(x0, y0, z1), Texel(x1, y0, z1), fx);
            var c11 = Lerp(Texel(x0, y1, z1), Texel(x1, y1, z1), fx);
            result[i] = Lerp(Lerp(c00, c10, fy), Lerp(c01, c11, fy), fz);
        }
        return result;
    }

    // HDRP's LogC (Alexa El 1000), as Color.hlsl defines it.
    private static double LogCToLinear(double x)
    {
        const double cut = 0.011361, a = 5.555556, b = 0.047996, c = 0.244161, d = 0.386036, e = 5.301883, f = 0.092819;
        return x > e * cut + f ? (System.Math.Pow(10.0, (x - d) / c) - b) / a : (x - f) / e;
    }

    private static double LinearToSrgb(double c) =>
        c <= 0.0031308 ? c * 12.92 : 1.055 * System.Math.Pow(c, 1.0 / 2.4) - 0.055;

    private static double SrgbToLinear(double c) =>
        c <= 0.04045 ? c / 12.92 : System.Math.Pow((c + 0.055) / 1.055, 2.4);
}
