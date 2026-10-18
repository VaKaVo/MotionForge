using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MotionForge.Model;

namespace MotionForge.Services;

public class RenderOptions
{
    public Project Project;
    /// <summary>Общая прозрачность всего кадра (используется для «луковой кожи»).</summary>
    public double Alpha = 1;
    public bool OutlineAll;
    public int Depth;
    public int CurrentFrame;
    public double PixelsPerDip = 1;

    public RenderOptions Child()
    {
        return new RenderOptions
        {
            Project = Project,
            Alpha = 1,
            OutlineAll = OutlineAll,
            Depth = Depth + 1,
            CurrentFrame = CurrentFrame,
            PixelsPerDip = PixelsPerDip,
        };
    }
}

/// <summary>Отрисовка сцены на DrawingContext: один код для экрана, предпросмотра и экспорта.</summary>
public static class Renderer
{
    private static readonly Dictionary<string, Brush> BrushCache = new Dictionary<string, Brush>();
    private static readonly Dictionary<string, Pen> PenCache = new Dictionary<string, Pen>();
    private static readonly Dictionary<string, BitmapSource> ImageCache = new Dictionary<string, BitmapSource>();

    public static void ClearCaches()
    {
        BrushCache.Clear();
        PenCache.Clear();
        ImageCache.Clear();
    }

    public static Matrix ToWpf(Mat m) => new Matrix(m.A, m.B, m.C, m.D, m.E, m.F);

    public static Brush GetBrush(string hex, double alpha)
    {
        hex = ColorUtil.Normalize(hex);
        double a = 1;
        if (hex.Length == 9)
        {
            ColorUtil.Parse(hex, out byte aa, out _, out _, out _);
            a = aa / 255.0;
            hex = "#" + hex.Substring(3);
        }
        a *= Math.Max(0, Math.Min(1, alpha));
        if (a <= 0.002) return null;

        string key = hex + "|" + a.ToString("0.###", CultureInfo.InvariantCulture);
        if (BrushCache.TryGetValue(key, out Brush cached)) return cached;
        var color = (Color)ColorConverter.ConvertFromString(hex);
        color.A = (byte)Math.Round(255 * a);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        BrushCache[key] = brush;
        return brush;
    }

    public static Pen GetPen(string hex, double width, double alpha)
    {
        if (width <= 0) return null;
        string key = ColorUtil.Normalize(hex) + "|" + width.ToString("0.##", CultureInfo.InvariantCulture)
                     + "|" + alpha.ToString("0.###", CultureInfo.InvariantCulture);
        if (PenCache.TryGetValue(key, out Pen cached)) return cached;
        Brush brush = GetBrush(hex, alpha);
        if (brush == null) return null;
        var pen = new Pen(brush, width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        PenCache[key] = pen;
        return pen;
    }

    public static PathGeometry BuildGeometry(IList<Vec> points, bool closed, bool smooth)
    {
        var geo = new PathGeometry { FillRule = FillRule.Nonzero };
        var segments = Geo.Segments(points, closed, smooth);
        if (segments.Count == 0) return geo;
        var figure = new PathFigure { StartPoint = new Point(segments[0].P0.X, segments[0].P0.Y), IsClosed = closed };
        foreach (var seg in segments)
        {
            figure.Segments.Add(new BezierSegment(
                new Point(seg.C1.X, seg.C1.Y), new Point(seg.C2.X, seg.C2.Y), new Point(seg.P1.X, seg.P1.Y), true));
        }
        geo.Figures.Add(figure);
        geo.Freeze();
        return geo;
    }

    public static FormattedText BuildText(TextElement t, double pixelsPerDip, Brush brush)
    {
        var typeface = new Typeface(
            new FontFamily(string.IsNullOrWhiteSpace(t.FontFamily) ? "Segoe UI" : t.FontFamily),
            t.Italic ? FontStyles.Italic : FontStyles.Normal,
            t.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        var formatted = new FormattedText(
            t.Text ?? "",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            Math.Max(1, t.FontSize),
            brush,
            pixelsPerDip);
        formatted.LineHeight = Math.Max(1, t.FontSize * t.LineHeight);
        return formatted;
    }

    public static BitmapSource LoadImage(ImageElement img)
    {
        if (string.IsNullOrEmpty(img.Source)) return null;
        string key = img.Id + "|" + img.Source.Length + "|" + img.Source.GetHashCode().ToString("x");
        if (ImageCache.TryGetValue(key, out BitmapSource cached)) return cached;
        try
        {
            byte[] data = Convert.FromBase64String(img.Source);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(data);
            bmp.EndInit();
            bmp.Freeze();
            ImageCache[key] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public static void DrawScene(DrawingContext dc, Project project, Scene scene, int frame, RenderOptions options)
    {
        if (scene == null || options.Depth > 10) return;
        bool pushed = false;
        if (options.Alpha < 0.999)
        {
            dc.PushOpacity(Math.Max(0, options.Alpha));
            pushed = true;
        }
        var child = options.Child();
        child.CurrentFrame = frame;
        foreach (var ev in Anim.EvaluateScene(scene, frame))
            DrawElement(dc, ev, child);
        if (pushed) dc.Pop();
    }

    public static void DrawElement(DrawingContext dc, EvalElement ev, RenderOptions options)
    {
        if (ev == null || !ev.Src.Visible) return;
        if (ev.Alpha <= 0.002) return;

        dc.PushTransform(new MatrixTransform(ToWpf(ev.Matrix)));
        dc.PushOpacity(Math.Max(0, Math.Min(1, ev.Alpha)));

        switch (ev.Src)
        {
            case ShapeElement shape:
                DrawShape(dc, shape, ev, options);
                break;
            case TextElement text:
                DrawText(dc, text, ev, options);
                break;
            case ImageElement image:
                DrawImage(dc, image, options);
                break;
            case GroupElement group:
            {
                var child = options.Child();
                foreach (var item in group.Children)
                    DrawElement(dc, Anim.Build(item, ev.Layer), child);
                break;
            }
            case InstanceElement instance:
                DrawInstance(dc, instance, options);
                break;
        }

        dc.Pop();
        dc.Pop();
    }

    private static void DrawShape(DrawingContext dc, ShapeElement shape, EvalElement ev, RenderOptions options)
    {
        var points = ev.Points ?? shape.Points;
        if (points == null || points.Count < 2) return;

        if (options.OutlineAll || (ev.Layer != null && ev.Layer.Outline))
        {
            var outlinePen = GetPen(ev.Layer != null ? ev.Layer.Color : "#4A90D9", 1.5, 1);
            var outlineGeometry = BuildGeometry(points, shape.Closed, shape.Smooth);
            if (outlinePen != null) dc.DrawGeometry(null, outlinePen, outlineGeometry);
            return;
        }

        // Линия с профилем толщины (рисование пером с нажатием) — рисуем «лентой».
        if (shape.Widths != null && shape.Widths.Count == points.Count && !shape.Closed && shape.StrokeWidth > 0)
        {
            double scale = Math.Max(0.02, ev.StrokeWidth / shape.StrokeWidth);
            var ribbonBrush = GetBrush(ev.Stroke ?? shape.Stroke, 1);
            if (ribbonBrush != null)
            {
                DrawRibbon(dc, points, shape.Widths, ribbonBrush, scale);
                return;
            }
        }

        var geometry = BuildGeometry(points, shape.Closed, shape.Smooth);
        Brush fill = shape.Filled && shape.Closed ? GetBrush(ev.Fill ?? shape.Fill, 1) : null;
        Pen pen = shape.Stroked ? GetPen(ev.Stroke ?? shape.Stroke, Math.Max(0.1, ev.StrokeWidth), 1) : null;
        dc.DrawGeometry(fill, pen, geometry);
    }

    /// <summary>
    /// Рисует ломаную переменной толщины: смещаем точки по нормали на половину ширины,
    /// соединяем в замкнутый контур и заливаем. Торцы скругляются.
    /// </summary>
    public static void DrawRibbon(DrawingContext dc, IList<Vec> points, IList<double> widths, Brush brush, double scale)
    {
        if (points == null || widths == null || brush == null) return;
        int n = Math.Min(points.Count, widths.Count);
        if (n < 2) return;

        var left = new List<Vec>(n);
        var right = new List<Vec>(n);
        for (int i = 0; i < n; i++)
        {
            Vec prev = points[Math.Max(0, i - 1)];
            Vec next = points[Math.Min(n - 1, i + 1)];
            double dx = next.X - prev.X;
            double dy = next.Y - prev.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) { dx = 1; dy = 0; len = 1; }
            double nx = -dy / len;
            double ny = dx / len;
            double half = Math.Max(0.15, widths[i] * scale) / 2;
            left.Add(new Vec(points[i].X + nx * half, points[i].Y + ny * half));
            right.Add(new Vec(points[i].X - nx * half, points[i].Y - ny * half));
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(left[0].X, left[0].Y), true, true);
            for (int i = 1; i < n; i++) ctx.LineTo(new Point(left[i].X, left[i].Y), true, false);
            AddCap(ctx, points[n - 1], left[n - 1], right[n - 1], Math.Max(0.15, widths[n - 1] * scale) / 2);
            for (int i = n - 1; i >= 0; i--) ctx.LineTo(new Point(right[i].X, right[i].Y), true, false);
            AddCap(ctx, points[0], right[0], left[0], Math.Max(0.15, widths[0] * scale) / 2);
        }
        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private static void AddCap(StreamGeometryContext ctx, Vec center, Vec from, Vec to, double radius)
    {
        double a0 = Math.Atan2(from.Y - center.Y, from.X - center.X);
        double a1 = Math.Atan2(to.Y - center.Y, to.X - center.X);
        double delta = a1 - a0;
        while (delta > Math.PI) delta -= 2 * Math.PI;
        while (delta < -Math.PI) delta += 2 * Math.PI;
        const int steps = 6;
        for (int i = 1; i <= steps; i++)
        {
            double a = a0 + delta * i / steps;
            ctx.LineTo(new Point(center.X + Math.Cos(a) * radius, center.Y + Math.Sin(a) * radius), true, false);
        }
    }

    private static void DrawText(DrawingContext dc, TextElement text, EvalElement ev, RenderOptions options)
    {
        string color = options.OutlineAll
            ? (ev.Layer != null ? ev.Layer.Color : "#4A90D9")
            : (ev.Color ?? text.Color);
        var brush = GetBrush(color, 1);
        if (brush == null) return;
        var formatted = BuildText(text, options.PixelsPerDip, brush);
        dc.DrawText(formatted, new Point(Anim.AlignOffset(text.Align, formatted.Width), 0));
    }

    private static void DrawImage(DrawingContext dc, ImageElement image, RenderOptions options)
    {
        var bmp = LoadImage(image);
        if (bmp == null) return;
        var rect = new Rect(0, 0, Math.Max(1, image.PixelWidth), Math.Max(1, image.PixelHeight));
        dc.DrawImage(bmp, rect);
        if (options.OutlineAll)
        {
            var pen = GetPen("#4A90D9", 1, 1);
            if (pen != null) dc.DrawRectangle(null, pen, rect);
        }
    }

    private static void DrawInstance(DrawingContext dc, InstanceElement instance, RenderOptions options)
    {
        if (options.Project == null) return;
        if (!options.Project.Library.TryGetValue(instance.SymbolId, out Symbol symbol)) return;

        int length = Math.Max(1, Anim.SceneLength(symbol.Timeline));
        int first = Math.Max(0, Math.Min(instance.FirstFrame, length - 1));
        int local;
        switch (instance.Loop)
        {
            case LoopMode.SingleFrame:
                local = first;
                break;
            case LoopMode.PlayOnce:
                local = Math.Min(length - 1, first + Math.Max(0, options.CurrentFrame));
                break;
            default:
            {
                int span = Math.Max(1, length - first);
                int offset = ((options.CurrentFrame % span) + span) % span;
                local = first + offset;
                break;
            }
        }

        var child = options.Child();
        DrawScene(dc, options.Project, symbol.Timeline, local, child);
    }
}