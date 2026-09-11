using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

internal enum ShopSurface { Panel, Dialog, Button, Card, Tab }

internal static class ShopVisuals
{
    internal static readonly Color Gold = new(.84f, .72f, .44f);
    private static readonly Il2CppStructArray<ushort> QuadIndices = new(new ushort[] { 0, 1, 2, 0, 2, 3 });
    private static readonly Il2CppStructArray<ushort> ShadowIndices = new(new ushort[]
    {
        0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2,
        2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0,
    });
    private static readonly Il2CppStructArray<ushort> SheenIndices = new(new ushort[]
    {
        0, 2, 3, 0, 3, 1, 2, 4, 5, 2, 5, 3,
        4, 6, 7, 4, 7, 5, 6, 8, 9, 6, 9, 7,
    });
    private static readonly Il2CppStructArray<ushort> SmallGlowIndices = GlowIndices(12, 3);
    private static readonly Il2CppStructArray<ushort> LargeGlowIndices = GlowIndices(64, 8);

    internal static VisualElement Element(string classes, VisualElement parent = null, PickingMode picking = PickingMode.Position)
    {
        var element = new VisualElement { pickingMode = picking };
        Classes(element, classes);
        parent?.Add(element);
        return element;
    }

    internal static void Classes(VisualElement element, string classes)
    {
        foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            element.AddToClassList(name);
    }

    // USS has no gradients or box shadows. Paint them below the content so every
    // native shop surface keeps the same lighting without covering its labels.
    internal static VisualElement Surface(VisualElement root, ShopSurface kind, Color? tone = null)
    {
        var accent = tone ?? Gold;
        var hovered = false;
        var primary = root.ClassListContains("wm-shop-primary");
        var drawing = Draw(root, context =>
        {
            var p = context.painter2D;
            var rect = context.visualElement.contentRect;
            var w = rect.width;
            var h = rect.height;
            if (w <= 0 || h <= 0)
                return;
            var active = hovered && root.enabledInHierarchy;
            var claimed = root.ClassListContains("wm-proc-claimed");
            if (kind is ShopSurface.Panel or ShopSurface.Dialog)
            {
                Gradient(context, rect, kind == ShopSurface.Dialog ? new Color(.075f, .105f, .086f) : new Color(.031f, .043f, .039f),
                    kind == ShopSurface.Dialog ? new Color(.047f, .075f, .063f) : new Color(.025f, .035f, .03f));
                Glow(context, new Vector2(w * .86f, 0), w * .44f, Alpha(accent, .075f));
                Line(p, Alpha(Color.white, .06f), 1, new(1, 1), new(w - 1, 1));
                if (kind == ShopSurface.Dialog)
                {
                    Line(p, Alpha(accent, .9f), 2, stackalloc Vector2[] { new(1, 22), new(1, 1), new(22, 1) });
                    Line(p, Alpha(accent, .6f), 2, stackalloc Vector2[] { new(w - 22, h - 1), new(w - 1, h - 1), new(w - 1, h - 22) });
                }
                else
                    Gradient(context, new Rect(16, 47, w - 32, 1), Alpha(accent, .35f), Alpha(accent, .035f));
            }
            else if (kind == ShopSurface.Tab)
            {
                var selected = root.ClassListContains("wm-shop-tab-active") || root.ClassListContains("wm-proc-selected");
                if (selected || active)
                    Gradient(context, rect, Alpha(accent, selected ? .16f : .08f), Alpha(accent, .015f));
                if (selected && root.ClassListContains("wm-proc-pool-tab"))
                {
                    Fill(p, new Rect(0, 10, 2, h - 20), accent);
                    Gradient(context, new Rect(2, 10, 12, h - 20), Alpha(accent, .1f), Alpha(accent, 0));
                }
                else if (selected)
                    Fill(p, new Rect(0, h - 2, w, 2), Alpha(accent, .9f));
            }
            else if (kind == ShopSurface.Card)
            {
                var cardAccent = claimed ? new Color(.55f, .55f, .55f) : accent;
                Gradient(context, rect, Alpha(cardAccent, claimed ? .025f : active ? .16f : .075f), Alpha(cardAccent, .008f));
                Line(p, Alpha(cardAccent, active ? .6f : .22f), 1, stackalloc Vector2[] { new(w - 10, 5), new(w - 5, 5), new(w - 5, 10) });
                Line(p, Alpha(Color.white, .035f), 1, new(1, 1), new(w - 1, 1));
                if (active)
                    Outline(p, rect, Alpha(cardAccent, .42f));
            }
            else
            {
                if (primary)
                {
                    Gradient(context, rect, active ? new Color(.93f, .88f, .65f) : new Color(.85f, .74f, .455f),
                        active ? new Color(.80f, .67f, .39f) : new Color(.74f, .61f, .33f));
                    Line(p, Alpha(Color.white, .25f), 1, new(1, 1), new(w - 1, 1));
                    if (root.enabledInHierarchy)
                        Sheen(context, rect, Mathf.Repeat(Time.unscaledTime, 9f) / 1.3f);
                }
                else
                    Gradient(context, rect, Alpha(accent, active ? .12f : .025f), Alpha(accent, active ? .05f : .08f), true);
            }
        });
        drawing.name = "wm-shop-surface";
        drawing.style.overflow = Overflow.Hidden;
        if (kind is ShopSurface.Dialog or ShopSurface.Panel)
            Draw(root, context => Shadow(context, context.visualElement.contentRect, kind == ShopSurface.Dialog ? 28f : 12f));
        if (kind is ShopSurface.Button or ShopSurface.Card or ShopSurface.Tab)
        {
            root.RegisterCallback<PointerEnterEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerEnterEvent>>(
                (Action<PointerEnterEvent>)(_ => { hovered = true; drawing.MarkDirtyRepaint(); })));
            root.RegisterCallback<PointerLeaveEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerLeaveEvent>>(
                (Action<PointerLeaveEvent>)(_ => { hovered = false; drawing.MarkDirtyRepaint(); })));
        }
        if (kind == ShopSurface.Button && primary)
        {
            var shining = false;
            drawing.schedule.Execute(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
            {
                if (!Displayed(root) || !root.enabledInHierarchy)
                    return;
                var next = Mathf.Repeat(Time.unscaledTime, 9f) < 1.3f;
                if (next || shining)
                    drawing.MarkDirtyRepaint();
                shining = next;
            })).Every(33);
        }
        return drawing;
    }

    internal static VisualElement Draw(VisualElement parent, Action<MeshGenerationContext> paint, bool behind = true)
    {
        var drawing = new VisualElement { pickingMode = PickingMode.Ignore };
        drawing.style.position = Position.Absolute;
        drawing.style.left = drawing.style.right = drawing.style.top = drawing.style.bottom = 0;
        drawing.generateVisualContent += DelegateSupport.ConvertDelegate<Il2CppSystem.Action<MeshGenerationContext>>(paint);
        if (behind)
            parent.Insert(0, drawing);
        else
            parent.Add(drawing);
        return drawing;
    }

    internal static void Refresh(VisualElement root)
    {
        for (var i = 0; i < root.childCount; i++)
            if (root[i].name == "wm-shop-surface")
                root[i].MarkDirtyRepaint();
    }

    internal static bool Displayed(VisualElement element)
    {
        if (element.panel == null)
            return false;
        // IsVisible only checks the element itself. Scheduled cosmetics must also
        // stop repainting when a containing tab, modal or screen is hidden.
        for (var current = element; current != null; current = current.parent)
            if (current.resolvedStyle.display == DisplayStyle.None)
                return false;
        return true;
    }

    internal static void Enter(VisualElement element, float distance = 7f, long delay = 0)
    {
        var start = Time.unscaledTime + delay / 1000f;
        element.style.opacity = 0;
        IVisualElementScheduledItem timer = null;
        timer = element.schedule.Execute(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
        {
            var progress = Mathf.Clamp01((Time.unscaledTime - start) / .24f);
            var eased = 1f - Mathf.Pow(1f - progress, 3f);
            element.style.opacity = eased;
            element.style.translate = new StyleTranslate(new Translate(0, distance * (1f - eased)));
            if (progress >= 1f)
                timer.Pause();
        })).Every(16);
    }

    internal static void Glint(VisualElement element)
    {
        var start = Time.unscaledTime;
        var drawing = Draw(element, context =>
            Sheen(context, context.visualElement.contentRect, (Time.unscaledTime - start) / .45f), false);
        IVisualElementScheduledItem timer = null;
        timer = drawing.schedule.Execute(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
        {
            if (Time.unscaledTime - start >= .45f)
            {
                timer.Pause();
                drawing.RemoveFromHierarchy();
            }
            else
                drawing.MarkDirtyRepaint();
        })).Every(16);
    }

    internal static Color Alpha(Color colour, float alpha) => new(colour.r, colour.g, colour.b, alpha);

    internal static void Gradient(MeshGenerationContext context, Rect rect, Color start, Color end, bool vertical = false)
    {
        if (!(rect.width > 0 && rect.height > 0))
            return;
        // Vertex interpolation keeps translucent highlights smooth. Adjacent painted
        // strips expose antialiasing seams against the dark shop background.
        var mesh = Allocate(context, 4, QuadIndices);
        Vertex(mesh, rect.xMin, rect.yMin, start);
        Vertex(mesh, rect.xMax, rect.yMin, vertical ? start : end);
        Vertex(mesh, rect.xMax, rect.yMax, end);
        Vertex(mesh, rect.xMin, rect.yMax, vertical ? end : start);
    }

    private static void Vertex(MeshWriteData mesh, float x, float y, Color colour)
        => mesh.SetNextVertex(new Vertex { position = new Vector3(x, y, UnityEngine.UIElements.Vertex.nearZ), tint = colour });

    private static MeshWriteData Allocate(MeshGenerationContext context, int vertices, Il2CppStructArray<ushort> indices)
    {
        // MENACE strips SetNextIndex. Its generated wrapper throws after allocation,
        // leaving uninitialised triangles on screen. The bulk array overload has a
        // native implementation. Shared topology also avoids per-frame array allocations.
        var mesh = context.Allocate(vertices, indices.Length, null);
        mesh.SetAllIndices(indices);
        return mesh;
    }

    internal static void Fill(Painter2D p, Rect rect, Color colour)
    {
        p.BeginPath();
        p.MoveTo(new(rect.xMin, rect.yMin));
        p.LineTo(new(rect.xMax, rect.yMin));
        p.LineTo(new(rect.xMax, rect.yMax));
        p.LineTo(new(rect.xMin, rect.yMax));
        p.ClosePath();
        p.fillColor = colour;
        p.Fill(FillRule.NonZero);
    }

    internal static void Line(Painter2D p, Color colour, float width, Vector2 start, Vector2 end)
    {
        p.strokeColor = colour;
        p.lineWidth = width;
        p.BeginPath();
        p.MoveTo(start);
        p.LineTo(end);
        p.Stroke();
    }

    internal static void Line(Painter2D p, Color colour, float width, ReadOnlySpan<Vector2> points)
    {
        p.strokeColor = colour;
        p.lineWidth = width;
        p.BeginPath();
        p.MoveTo(points[0]);
        for (var i = 1; i < points.Length; i++)
            p.LineTo(points[i]);
        p.Stroke();
    }

    internal static void Arc(Painter2D p, Vector2 centre, float radius, float start, float end, Color colour, float width)
    {
        p.BeginPath();
        p.Arc(centre, radius, new Angle(start, AngleUnit.Degree), new Angle(end, AngleUnit.Degree), ArcDirection.Clockwise);
        p.strokeColor = colour;
        p.lineWidth = width;
        p.Stroke();
    }

    internal static void Disc(Painter2D p, Vector2 centre, float radius, Color colour)
    {
        p.BeginPath();
        p.Arc(centre, radius, new Angle(0, AngleUnit.Degree), new Angle(360, AngleUnit.Degree), ArcDirection.Clockwise);
        p.ClosePath();
        p.fillColor = colour;
        p.Fill(FillRule.NonZero);
    }

    internal static void Glow(MeshGenerationContext context, Vector2 centre, float radius, Color colour)
    {
        var segments = radius < 15f ? 12 : 64;
        var rings = radius < 15f ? 3 : 8;
        var mesh = Allocate(context, 1 + rings * segments, radius < 15f ? SmallGlowIndices : LargeGlowIndices);
        Vertex(mesh, centre.x, centre.y, colour);
        for (var ring = 1; ring <= rings; ring++)
        {
            var t = ring / (float)rings;
            var alpha = colour.a * Mathf.Max(0, (Mathf.Exp(-4f * t * t) - .01831564f) / .98168436f);
            for (var i = 0; i < segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                Vertex(mesh, centre.x + Mathf.Cos(angle) * radius * t, centre.y + Mathf.Sin(angle) * radius * t, Alpha(colour, alpha));
            }
        }
    }

    private static Il2CppStructArray<ushort> GlowIndices(int segments, int rings)
    {
        var indices = new List<ushort>(segments * 3 + (rings - 1) * segments * 6);
        void Triangle(int a, int b, int c)
        {
            indices.Add((ushort)a);
            indices.Add((ushort)b);
            indices.Add((ushort)c);
        }
        for (var i = 0; i < segments; i++)
            Triangle(0, 1 + i, 1 + (i + 1) % segments);
        for (var ring = 0; ring < rings - 1; ring++)
            for (var i = 0; i < segments; i++)
            {
                var a = 1 + ring * segments + i;
                var b = 1 + ring * segments + (i + 1) % segments;
                Triangle(a, a + segments, b + segments);
                Triangle(a, b + segments, b);
            }
        return new Il2CppStructArray<ushort>(indices.ToArray());
    }

    private static void Outline(Painter2D p, Rect rect, Color colour)
        => Line(p, colour, 1, stackalloc Vector2[] { new(rect.xMin, rect.yMin), new(rect.xMax, rect.yMin),
            new(rect.xMax, rect.yMax), new(rect.xMin, rect.yMax), new(rect.xMin, rect.yMin) });

    private static void Shadow(MeshGenerationContext context, Rect rect, float radius)
    {
        var mesh = Allocate(context, 8, ShadowIndices);
        var inner = new Color(0, 0, 0, .45f);
        var outer = Color.clear;
        Vertex(mesh, rect.xMin, rect.yMin + 6, inner);
        Vertex(mesh, rect.xMax, rect.yMin + 6, inner);
        Vertex(mesh, rect.xMax, rect.yMax + 6, inner);
        Vertex(mesh, rect.xMin, rect.yMax + 6, inner);
        Vertex(mesh, rect.xMin - radius, rect.yMin - radius * .4f, outer);
        Vertex(mesh, rect.xMax + radius, rect.yMin - radius * .4f, outer);
        Vertex(mesh, rect.xMax + radius, rect.yMax + radius, outer);
        Vertex(mesh, rect.xMin - radius, rect.yMax + radius, outer);
    }

    private static void Sheen(MeshGenerationContext context, Rect rect, float progress)
    {
        if (progress < 0 || progress > 1)
            return;
        var centre = Mathf.Lerp(-rect.width * .4f, rect.width * 1.4f, progress);
        var span = rect.width * .17f;
        var mesh = Allocate(context, 10, SheenIndices);
        for (var i = 0; i < 5; i++)
        {
            var t = i / 4f;
            var x = centre + span * (t * 2f - 1f);
            var lean = rect.height * .2f;
            var colour = new Color(1, .96f, .83f, Mathf.Sin(t * Mathf.PI) * .16f);
            Vertex(mesh, Mathf.Clamp(x + lean, 0, rect.width), 0, colour);
            Vertex(mesh, Mathf.Clamp(x - lean, 0, rect.width), rect.height, colour);
        }
    }
}
