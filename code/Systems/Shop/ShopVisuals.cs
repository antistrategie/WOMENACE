using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

internal static class ShopVisuals
{
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

    // What the stylesheet cannot draw of the shop's surfaces is painted below their content.

    // A dialog's corner brackets, and the shadow under it.
    internal static void Brackets(VisualElement root)
    {
        Painted(root, (context, w, h) =>
        {
            var p = context.painter2D;
            Line(p, Alpha(Wui.Gold, .9f), 2, stackalloc Vector2[] { new(1, 22), new(1, 1), new(22, 1) });
            Line(p, Alpha(Wui.Gold, .6f), 2, stackalloc Vector2[] { new(w - 22, h - 1), new(w - 1, h - 1), new(w - 1, h - 22) });
        });
        Draw(root, context => Shadow(context, context.visualElement.contentRect, 28f));
    }

    // The chosen reward pool tab's mark: its tier's colour down its edge.
    internal static void TabEdge(VisualElement root, Color tone)
    {
        Painted(root, (context, w, h) =>
        {
            Fill(context.painter2D, new Rect(0, 10, 2, h - 20), tone);
            Gradient(context, new Rect(2, 10, 12, h - 20), Alpha(tone, .1f), Alpha(tone, 0));
        });
    }

    // A card's corner tick, its outline under the pointer, and its tier's tint when it has a
    // tier. A claimed card is grey.
    internal static void Card(VisualElement root, Color? tone = null)
    {
        var hovered = false;
        var drawing = Painted(root, (context, w, h) =>
        {
            var p = context.painter2D;
            var rect = context.visualElement.contentRect;
            var active = hovered && root.enabledInHierarchy;
            var claimed = root.ClassListContains("wm-proc-claimed");
            var accent = claimed ? new Color(.55f, .55f, .55f) : tone ?? Wui.Gold;
            if (tone.HasValue)
                Gradient(context, rect, Alpha(accent, claimed ? .025f : active ? .16f : .075f), Alpha(accent, .008f));
            Line(p, Alpha(accent, active ? .6f : .22f), 1, stackalloc Vector2[] { new(w - 10, 5), new(w - 5, 5), new(w - 5, 10) });
            if (active)
                Outline(p, rect, Alpha(accent, .42f));
        });
        Hovered(root, drawing, over => hovered = over);
    }

    // A button's wash under the pointer, and the gold of a primary one while it can be pressed.
    internal static void Button(VisualElement root)
    {
        var hovered = false;
        var primary = root.ClassListContains("wm-shop-primary");
        var drawing = Painted(root, (context, w, h) =>
        {
            var p = context.painter2D;
            var rect = context.visualElement.contentRect;
            var active = hovered && root.enabledInHierarchy;
            if (primary && root.enabledInHierarchy)
            {
                Gradient(context, rect, active ? new Color(.93f, .88f, .65f) : new Color(.85f, .74f, .455f),
                    active ? new Color(.80f, .67f, .39f) : new Color(.74f, .61f, .33f));
                Line(p, Alpha(Color.white, .25f), 1, new(1, 1), new(w - 1, 1));
                Sheen(context, rect, Mathf.Repeat(Time.unscaledTime, 9f) / 1.3f);
            }
            else if (active)
                Fill(p, rect, Alpha(Wui.Gold, .14f));
        });
        Hovered(root, drawing, over => hovered = over);
        if (!primary)
            return;
        var shining = false;
        var enabled = root.enabledInHierarchy;
        drawing.schedule.Execute(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
        {
            // Most ticks have nothing to repaint, which is known before the walk up the
            // tree that says whether the button is shown at all.
            var now = root.enabledInHierarchy;
            var next = now && Mathf.Repeat(Time.unscaledTime, 9f) < 1.3f;
            if (now == enabled && !next && !shining)
                return;
            if (!Displayed(root))
                return;
            // Its gold comes and goes with whether it can be pressed.
            if (now != enabled)
            {
                enabled = now;
                drawing.MarkDirtyRepaint();
            }
            if (!now)
                return;
            if (next || shining)
                drawing.MarkDirtyRepaint();
            shining = next;
        })).Every(33);
    }

    // A drawing below a surface's content, given the surface's size and never asked to draw
    // one that has none.
    private static VisualElement Painted(VisualElement root, Action<MeshGenerationContext, float, float> paint)
    {
        var drawing = Draw(root, context =>
        {
            var rect = context.visualElement.contentRect;
            if (rect.width > 0 && rect.height > 0)
                paint(context, rect.width, rect.height);
        });
        drawing.name = "wm-shop-surface";
        drawing.style.overflow = Overflow.Hidden;
        return drawing;
    }

    // Tells a surface whether the pointer is over it, and has its drawing painted again.
    private static void Hovered(VisualElement root, VisualElement drawing, Action<bool> set)
    {
        root.RegisterCallback<PointerEnterEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerEnterEvent>>(
            (Action<PointerEnterEvent>)(_ => { set(true); drawing.MarkDirtyRepaint(); })));
        root.RegisterCallback<PointerLeaveEvent>(DelegateSupport.ConvertDelegate<EventCallback<PointerLeaveEvent>>(
            (Action<PointerLeaveEvent>)(_ => { set(false); drawing.MarkDirtyRepaint(); })));
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
