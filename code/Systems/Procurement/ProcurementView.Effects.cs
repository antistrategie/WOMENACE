using Il2CppInterop.Runtime;
using Jiangyu.Game.Ui;
using UnityEngine;
using UnityEngine.UIElements;
using static WOMENACE.Code.ShopVisuals;

namespace WOMENACE.Code;

internal sealed partial class ProcurementView
{
    private static readonly float[] CargoLatches = [124f, 279f];
    private IVisualElementScheduledItem _ambientTimer;
    private VisualElement _ambientOrbit, _ambientParticles;
    private float _cargoProgress;

    private static VisualElement Drawing(string classes, VisualElement parent, Action<MeshGenerationContext> paint)
    {
        var element = Element(classes, parent, PickingMode.Ignore);
        element.generateVisualContent += DelegateSupport.ConvertDelegate<Il2CppSystem.Action<MeshGenerationContext>>(paint);
        return element;
    }

    private void BuildAmbientBackdrop(VisualElement hero)
    {
        Drawing("wm-proc-ambient", hero, context =>
        {
            var p = context.painter2D;
            var width = context.visualElement.contentRect.width;
            var centre = new Vector2(width * .70f, 166f);
            Glow(context, new Vector2(width * .74f, 65), 260f, new Color(.65f, .46f, .18f, .24f));
            var grid = new Color(.64f, .63f, .46f, .045f);
            for (var x = 0f; x < width; x += 32f)
                Line(p, grid, 1f, new Vector2(x, 0), new Vector2(x, 204));
            for (var y = 12f; y < 204f; y += 32f)
                Line(p, grid, 1f, new Vector2(0, y), new Vector2(width, y));
            Arc(p, centre, 182f, 0, 360, new Color(.78f, .67f, .35f, .085f), 1f);
        });
        _ambientOrbit = Drawing("wm-proc-orbit", hero, context =>
        {
            for (var i = 0; i < 100; i++)
                Arc(context.painter2D, new Vector2(226, 226), 225, i * 3.6f, i * 3.6f + 1.3f,
                    new Color(.78f, .67f, .35f, .22f), 1f);
        });
    }

    private void BuildAmbientParticles(VisualElement hero)
    {
        var particles = Embers(28);
        _ambientParticles = Drawing("wm-proc-ambient", hero, context => PaintEmbers(context, particles));
        _ambientParticles.name = "wm-procurement-embers";
        _ambientTimer = hero.schedule.Execute(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
        {
            if (!_visible || _busy || _pool?.IsVisible() == true || _exchange?.IsVisible() == true)
                return;
            // Rotate the cached dotted ring instead of tessellating a hundred arcs
            // every frame. Only the changing particle positions need fresh geometry.
            _ambientOrbit.style.rotate = new StyleRotate(new Rotate(new Angle(Time.unscaledTime * 3.6f, AngleUnit.Degree)));
            _ambientParticles.MarkDirtyRepaint();
        })).Every(33);
        _ambientTimer.Pause();
    }

    private readonly record struct Ember(float Phase, float Duration, float X, float Drift, float Radius, float Alpha, Color Colour, bool Halo);

    private static Ember[] Embers(int count) => Enumerable.Range(0, count).Select(i =>
        new Ember(Noise(i, 1), 4.5f + Noise(i, 2) * 9f, .06f + Noise(i, 3) * .88f,
            5f + Noise(i, 4) * 35f, .45f + Mathf.Pow(Noise(i, 5), 2) * 1.3f,
            .2f + Noise(i, 6) * .65f, Color.Lerp(new Color(.88f, .65f, .32f), new Color(1f, .91f, .65f), Noise(i, 7)), i % 6 == 0)).ToArray();

    private static float Noise(int index, uint salt)
    {
        // Cosmetic variation has its own deterministic hash and never consumes pull RNG.
        var value = unchecked((uint)index * 747796405u + salt * 2891336453u);
        value = unchecked((value ^ (value >> 16)) * 2246822519u);
        return (value & 0x00ffffff) / 16777216f;
    }

    private static void PaintEmbers(MeshGenerationContext context, Ember[] particles)
    {
        var p = context.painter2D;
        var rect = context.visualElement.contentRect;
        var time = Time.unscaledTime;
        foreach (var ember in particles)
        {
            var travel = Mathf.Repeat(time / ember.Duration + ember.Phase, 1f);
            var wave = Mathf.Sin(travel * 4f + ember.Phase * 6f);
            var point = new Vector2(rect.width * ember.X + wave * ember.Drift, rect.height * (1.08f - travel * 1.16f));
            var alpha = Mathf.Sin(travel * Mathf.PI) * ember.Alpha * (.85f + .15f * Mathf.Sin(time * 2f + ember.Phase * 10f));
            if (ember.Radius > 1f || ember.Halo)
                Glow(context, point, ember.Radius * 5f, Alpha(ember.Colour, alpha * .28f));
            Disc(p, point, ember.Radius, Alpha(ember.Colour, alpha));
        }
    }

    private static VisualElement AnimatedDrawing(VisualElement parent, Action<MeshGenerationContext> paint, bool behind = true)
    {
        var drawing = Draw(parent, paint, behind);
        drawing.schedule.Execute(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
        {
            if (Displayed(drawing))
                drawing.MarkDirtyRepaint();
        })).Every(33);
        return drawing;
    }

    private static void Sparks(VisualElement parent, int count)
    {
        var particles = Embers(count);
        AnimatedDrawing(parent, context => PaintEmbers(context, particles), false);
    }

    private void BuildTransferBackdrop(bool rare)
    {
        AnimatedDrawing(_transfer, context =>
        {
            var p = context.painter2D;
            var rect = context.visualElement.contentRect;
            var tone = rare ? Gold : new Color(.61f, .73f, .59f);
            Gradient(context, rect, new Color(.07f, .11f, .09f), new Color(.025f, .045f, .04f));
            Glow(context, new Vector2(rect.width * .5f, rect.height * .42f), 360f, Alpha(tone, .18f));
            PaintStageGrid(p, rect, Time.unscaledTime, tone);
        });
    }

    private void BuildUnlockBackdrop()
    {
        AnimatedDrawing(_reveal, context =>
        {
            var p = context.painter2D;
            var rect = context.visualElement.contentRect;
            var time = Time.unscaledTime;
            var centre = new Vector2(350, 336);
            Gradient(context, rect, new Color(.086f, .114f, .098f), new Color(.039f, .063f, .051f));
            Glow(context, new Vector2(rect.width * .36f, rect.height * .44f), 420, new Color(.63f, .46f, .21f, .36f));
            PaintStageGrid(p, rect, time * .4f, Gold);
            for (var i = 0; i < 20; i++)
            {
                var angle = (i * 18f + time * 3.6f) * Mathf.Deg2Rad;
                var start = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var end = new Vector2(Mathf.Cos(angle + .035f), Mathf.Sin(angle + .035f));
                for (var band = 0; band < 4; band++)
                {
                    var inner = band * 130f;
                    var outer = inner + 130f;
                    Polygon(p, new Color(.9f, .78f, .54f, .024f * (1f - band / 4f)), Color.clear, centre + start * inner, centre + start * outer, centre + end * outer, centre + end * inner);
                }
            }
            Arc(p, centre, 310, 0, 360, new Color(.78f, .68f, .38f, .24f), 1f);
            Arc(p, centre, 343, 0, 360, new Color(.78f, .68f, .38f, .08f), 1f);
            for (var i = 0; i < 24; i++)
            {
                var y = rect.height * i / 24f;
                var alpha = Mathf.Sin(i / 24f * Mathf.PI) * .3f;
                Line(p, Alpha(Gold, alpha), 1,
                    new(680 - y * .24f, y), new(680 - (y + rect.height / 24f) * .24f, y + rect.height / 24f));
            }
        });
    }

    private static void PaintStageGrid(Painter2D p, Rect rect, float time, Color tone)
    {
        var drift = Mathf.Repeat(time * 2f, 60f);
        for (var x = -rect.height; x < rect.width + rect.height; x += 60f)
            Line(p, Alpha(tone, .026f), 1, new(x + drift, 0), new(x + drift - rect.height * .2f, rect.height));
        for (var y = -rect.width; y < rect.height + rect.width; y += 60f)
            Line(p, Alpha(tone, .034f), 1, new(0, y + drift), new(rect.width, y + drift - rect.width * .16f));
    }

    private VisualElement BuildCargo(bool rare)
    {
        _cargoProgress = 0;
        var cargo = Drawing("wm-proc-cargo", _transfer, context => PaintCargo(context, rare));
        cargo.name = "wm-procurement-cargo";
        return cargo;
    }

    private void PaintCargo(MeshGenerationContext context, bool rare)
    {
        var p = context.painter2D;
        var progress = _cargoProgress;
        var centre = new Vector2(210, 177);
        var accent = rare ? new Color(.93f, .74f, .36f, 1f) : new Color(.63f, .79f, .69f, 1f);
        var dim = new Color(accent.r, accent.g, accent.b, .22f);
        var opened = Mathf.SmoothStep(0, 1, Mathf.Clamp01((progress - .79f) / .2f));
        Glow(context, centre, 173f, new Color(accent.r, accent.g, accent.b, .10f + opened * .16f));
        for (var i = 0; i < 4; i++)
        {
            var angle = i * 90f + progress * 65f;
            Arc(p, centre, 154, angle, angle + 52f, dim, 1f);
            Arc(p, centre, 140, -angle + 20, -angle + 33, accent, 1.5f);
        }
        for (var i = 0; i < 48; i++)
        {
            var angle = i * Mathf.PI / 24f;
            var ray = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Line(p, dim, 1f, centre + ray * 164, centre + ray * (i % 4 == 0 ? 171 : 167));
        }
        var body = new Color(.09f, .13f, .12f, 1f);
        var edge = new Color(.33f, .39f, .32f, 1f);
        var bevel = new Color(.17f, .21f, .18f, 1f);
        var lift = opened * 35f;
        Polygon(p, new Color(.035f, .065f, .06f, 1f), edge, stackalloc Vector2[] { new(100, 145), new(320, 145), new(320, 234), new(303, 251), new(117, 251), new(100, 234) });
        Polygon(p, body, edge, stackalloc Vector2[] { new(114, 148), new(306, 148), new(306, 226), new(294, 238), new(126, 238), new(114, 226) });
        // Bevelled side rails and recessed plates give the container depth without a model.
        Polygon(p, bevel, edge, stackalloc Vector2[] { new(100, 145), new(114, 153), new(114, 226), new(126, 238), new(117, 251), new(100, 234) });
        Polygon(p, bevel, edge, stackalloc Vector2[] { new(320, 145), new(306, 153), new(306, 226), new(294, 238), new(303, 251), new(320, 234) });
        Polygon(p, new Color(.065f, .10f, .095f, 1f), dim, stackalloc Vector2[] { new(137, 161), new(283, 161), new(291, 169), new(291, 211), new(281, 222), new(137, 222), new(129, 214), new(129, 169) });
        for (var i = 0; i < 4; i++)
        {
            Line(p, edge, 2f, new Vector2(143, 179 + i * 7), new Vector2(161, 179 + i * 7));
            Line(p, edge, 2f, new Vector2(259, 179 + i * 7), new Vector2(277, 179 + i * 7));
        }
        Glow(context, new Vector2(210, 146), 76f, new Color(accent.r, accent.g, accent.b, opened * .42f));
        Line(p, new Color(accent.r, accent.g, accent.b, .4f + opened * .6f), 2f,
            new Vector2(116, 146), new Vector2(304, 146));
        Polygon(p, new Color(.18f, .23f, .20f, 1f), edge, stackalloc Vector2[] { new(100, 139 - lift), new(118, 104 - lift), new(302, 104 - lift), new(320, 139 - lift), new(304, 150 - lift), new(116, 150 - lift) });
        Polygon(p, new Color(.24f, .28f, .22f, 1f), edge, new(118, 104 - lift), new(302, 104 - lift), new(311, 128 - lift), new(109, 128 - lift));
        Line(p, new Color(.61f, .64f, .46f, .48f), 1, stackalloc Vector2[] { new(118, 104 - lift), new(302, 104 - lift), new(311, 128 - lift) });
        Line(p, new Color(.7f, .73f, .52f, .15f), 1, stackalloc Vector2[] { new(100, 145), new(100, 234), new(117, 251) });
        Line(p, dim, 1f, new Vector2(134, 113 - lift), new Vector2(286, 113 - lift));
        foreach (var x in CargoLatches)
        {
            Polygon(p, body, edge, new(x, 112 - lift), new(x + 17, 112 - lift), new(x + 17, 159 - lift), new(x, 159 - lift));
            Line(p, accent, 2f, new Vector2(x + 4, 140 - lift), new Vector2(x + 13, 140 - lift));
        }
        Polygon(p, bevel, accent, stackalloc Vector2[] { new(192, 173), new(228, 173), new(235, 180), new(235, 204), new(228, 211), new(192, 211), new(185, 204), new(185, 180) });
        Line(p, accent, 2f, new Vector2(203, 183), new Vector2(203, 201));
        Line(p, accent, 2f, new Vector2(210, 183), new Vector2(210, 201));
        Line(p, accent, 2f, new Vector2(217, 183), new Vector2(217, 201));
        for (var i = 0; i < 12; i++)
            Line(p, dim, i % 3 == 0 ? 2f : 1f, new Vector2(184 + i * 4, 231), new Vector2(184 + i * 4, 235));
        // The scan finishes before the locks release, so its sweep never crosses the open lid.
        if (progress < .78f)
        {
            var y = 104 + Mathf.Repeat(progress * 2.5f, 1f) * 135f;
            Line(p, new Color(accent.r, accent.g, accent.b, .06f), 14f, new Vector2(91, y), new Vector2(329, y));
            Line(p, new Color(accent.r, accent.g, accent.b, .16f), 5f, new Vector2(91, y), new Vector2(329, y));
            Line(p, new Color(accent.r, accent.g, accent.b, .8f), 1f, new Vector2(91, y), new Vector2(329, y));
        }
        for (var i = 0; i < 12; i++)
        {
            var travel = Mathf.Repeat(progress * 1.1f + i * .173f, 1f);
            var point = new Vector2(65 + i * 27f, 290 - travel * 234);
            Disc(p, point, .8f, new Color(accent.r, accent.g, accent.b, Mathf.Sin(travel * Mathf.PI) * .65f));
        }
    }

    private static void Polygon(Painter2D p, Color fill, Color stroke, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        => Polygon(p, fill, stroke, stackalloc Vector2[] { a, b, c, d });

    private static void Polygon(Painter2D p, Color fill, Color stroke, ReadOnlySpan<Vector2> points)
    {
        p.BeginPath();
        p.MoveTo(points[0]);
        for (var i = 1; i < points.Length; i++)
            p.LineTo(points[i]);
        p.ClosePath();
        p.fillColor = fill;
        p.Fill(FillRule.NonZero);
        if (stroke.a > 0)
        {
            p.strokeColor = stroke;
            p.lineWidth = 1f;
            p.Stroke();
        }
    }

}
