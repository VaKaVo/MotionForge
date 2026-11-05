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

        // Следы ластика: вырезаем области прямо из геометрии — объект остаётся целым,
        // а «дырка» появляется ровно там, где провели ластиком.
        Geometry erase = shape.HasErase ? BuildEraseGeometry(shape.Erase) : null;

        // Штрих кистью: профиль толщины + стиль кисти (маркер, каллиграфия, аэрограф).
        string preset = shape.Brush ?? "";
        bool hasWidths = shape.Widths != null && shape.Widths.Count == points.Count;
        if ((hasWidths || preset.Length > 0) && !shape.Closed && shape.StrokeWidth > 0)
        {
            double strokeScale = Math.Max(0.02, ev.StrokeWidth / shape.StrokeWidth);
            string color = ev.Stroke ?? shape.Stroke;
            var brush = GetBrush(color, 1);
            if (brush != null)
            {
                List<double> widths = hasWidths
                    ? new List<double>(shape.Widths)
                    : Repeat(shape.StrokeWidth, points.Count);
                widths = ApplyPreset(preset, points, widths, shape.BrushAngle);

                if (preset == "airbrush")
                {
                    var haloWidths = new List<double>(widths.Count);
                    foreach (double w in widths) haloWidths.Add(w * 2.6);
                    var halo = GetBrush(color, 0.14);
                    if (halo != null) DrawRibbon(dc, points, haloWidths, halo, strokeScale, shape.Smooth, false, erase);
                    DrawRibbon(dc, points, widths, GetBrush(color, 0.62) ?? brush, strokeScale, shape.Smooth, false, erase);
                    return;
                }

                var ink = preset == "marker" ? (GetBrush(color, 0.85) ?? brush) : brush;
                DrawRibbon(dc, points, widths, ink, strokeScale, shape.Smooth, preset == "marker", erase);
                return;
            }
        }

        var geometry = BuildGeometry(points, shape.Closed, shape.Smooth);
        Brush fill = shape.Filled && shape.Closed ? GetBrush(ev.Fill ?? shape.Fill, 1) : null;
        Pen pen = shape.Stroked ? GetPen(ev.Stroke ?? shape.Stroke, Math.Max(0.1, ev.StrokeWidth), 1) : null;

        if (erase == null)
        {
            dc.DrawGeometry(fill, pen, geometry);
            return;
        }

        // Заливка и обводка режутся отдельно: у них могут быть разные цвета.
        if (fill != null) dc.DrawGeometry(fill, null, CutOut(geometry, erase));
        if (pen != null)
        {
            Geometry outline = geometry.GetWidenedPathGeometry(pen);
            dc.DrawGeometry(pen.Brush, null, CutOut(outline, erase));
        }
    }

    /// <summary>Вычитает из геометрии область ластика (null — если стирать нечего).</summary>
    public static Geometry CutOut(Geometry shape, Geometry erase)
    {
        if (shape == null) return null;
        if (erase == null) return shape;
        var result = Geometry.Combine(shape, erase, GeometryCombineMode.Exclude, null);
        result.Freeze();
        return result;
    }

    /// <summary>Геометрия следов ластика: путь, «раздутый» до диаметра ластика.</summary>
    public static Geometry BuildEraseGeometry(IList<ErasePath> paths)
    {
        if (paths == null || paths.Count == 0) return null;
        Geometry result = null;
        foreach (var path in paths)
        {
            if (path == null || path.Points == null || path.Points.Count == 0) continue;
            double radius = Math.Max(0.4, path.Radius);
            Geometry part;
            if (path.Points.Count == 1)
            {
                part = new EllipseGeometry(new Point(path.Points[0].X, path.Points[0].Y), radius, radius);
            }
            else
            {
                var line = new PathGeometry { FillRule = FillRule.Nonzero };
                var figure = new PathFigure
                {
                    StartPoint = new Point(path.Points[0].X, path.Points[0].Y),
                    IsClosed = false,
                };
                for (int i = 1; i < path.Points.Count; i++)
                    figure.Segments.Add(new LineSegment(new Point(path.Points[i].X, path.Points[i].Y), true));
                line.Figures.Add(figure);
                var pen = new Pen(Brushes.Black, radius * 2)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round,
                };
                part = line.GetWidenedPathGeometry(pen);
            }
            part.Freeze();
            result = result == null ? part : Geometry.Combine(result, part, GeometryCombineMode.Union, null);
        }
        if (result != null) result.Freeze();
        return result;
    }

    /// <summary>
    /// Рисует ломаную переменной толщины: смещаем точки по нормали на половину ширины,
    /// соединяем в замкнутый контур и заливаем. Торцы скругляются.
    /// </summary>
    public static void DrawRibbon(DrawingContext dc, IList<Vec> points, IList<double> widths, Brush brush, double scale,
        bool smooth = false, bool flatCaps = false, Geometry erase = null)
    {
        var geometry = BuildRibbonGeometry(points, widths, scale, smooth, flatCaps);
        if (geometry == null || brush == null) return;
        dc.DrawGeometry(brush, null, CutOut(geometry, erase));
    }

    /// <summary>
    /// Геометрия «ленты» переменной толщины. Точки пересэмплируются с постоянным шагом,
    /// а смещение ограничено miter-лимитом: на резких поворотах лента не складывается
    /// сама в себя и не даёт заломов.
    /// </summary>
    public static Geometry BuildRibbonGeometry(IList<Vec> points, IList<double> widths, double scale,
        bool smooth, bool flatCaps)
    {
        if (points == null || widths == null) return null;
        int n = Math.Min(points.Count, widths.Count);
        if (n < 2) return null;

        List<Vec> pts;
        List<double> ws;
        if (smooth && n >= 3)
        {
            pts = new List<Vec>();
            ws = new List<double>();
            Geo.SmoothPolyline(points, widths, 0, pts, ws);
            if (pts.Count < 2) return null;
        }
        else
        {
            pts = new List<Vec>(n);
            ws = new List<double>(n);
            for (int i = 0; i < n; i++) { pts.Add(points[i]); ws.Add(widths[i]); }
        }

        // Постоянный шаг: чем толще штрих, тем реже можно ставить точки.
        double average = 0;
        for (int i = 0; i < ws.Count; i++) average += ws[i];
        average = Math.Max(0.5, average / Math.Max(1, ws.Count) * scale);
        double spacing = Math.Max(0.6, Math.Min(6, average * 0.45));

        var resampledPoints = new List<Vec>();
        var resampledWidths = new List<double>();
        Geo.Resample(pts, ws, spacing, resampledPoints, resampledWidths);
        if (resampledPoints.Count < 2) return null;
        pts = resampledPoints;
        ws = resampledWidths;
        int m = pts.Count;

        var left = new List<Vec>(m);
        var right = new List<Vec>(m);
        for (int i = 0; i < m; i++)
        {
            Vec prev = pts[Math.Max(0, i - 1)];
            Vec next = pts[Math.Min(m - 1, i + 1)];
            Vec d1 = Normalize(pts[i] - prev);
            Vec d2 = Normalize(next - pts[i]);
            if (d1.X == 0 && d1.Y == 0) d1 = d2;
            if (d2.X == 0 && d2.Y == 0) d2 = d1;

            Vec bisector = d1 + d2;
            double length = Math.Sqrt(bisector.X * bisector.X + bisector.Y * bisector.Y);
            if (length < 0.15)
            {
                // Разворот почти на 180°: берём нормаль к текущему сегменту.
                bisector = d2;
                length = 1;
            }
            bisector = new Vec(bisector.X / length, bisector.Y / length);

            // 1 / cos(θ/2), ограниченный, чтобы угол не превращался в длинный шип.
            double miter = Math.Min(2.0, 1.0 / Math.Max(0.5, length));
            double half = Math.Max(0.15, ws[i] * scale) / 2 * miter;

            var normal = new Vec(-bisector.Y, bisector.X);
            left.Add(pts[i] + normal * half);
            right.Add(pts[i] - normal * half);
        }

        // Nonzero (а не EvenOdd): самопересечения росчерка не вырезают дырки.
        // Круглые торцы добавляем в ту же геометрию и с той же намоткой — иначе
        // в месте стыка торца и штриха контуры «гасят» друг друга и появляется кольцо.
        var ribbon = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = ribbon.Open())
        {
            ctx.BeginFigure(new Point(left[0].X, left[0].Y), true, true);
            for (int i = 1; i < m; i++) ctx.LineTo(new Point(left[i].X, left[i].Y), true, false);
            for (int i = m - 1; i >= 0; i--) ctx.LineTo(new Point(right[i].X, right[i].Y), true, false);

            if (!flatCaps)
            {
                var loop = new List<Vec>(m * 2);
                loop.AddRange(left);
                for (int i = m - 1; i >= 0; i--) loop.Add(right[i]);
                double direction = SignedArea(loop) >= 0 ? 1 : -1;

                double r0 = Math.Max(0.15, ws[0] * scale) / 2;
                double rn = Math.Max(0.15, ws[m - 1] * scale) / 2;
                AddCircle(ctx, pts[0], r0, direction);
                AddCircle(ctx, pts[m - 1], rn, direction);
            }
        }
        ribbon.Freeze();
        return ribbon;
    }

    /// <summary>Площадь замкнутого контура со знаком (для согласованной намотки фигур).</summary>
    private static double SignedArea(IList<Vec> points)
    {
        double sum = 0;
        for (int i = 0; i < points.Count; i++)
        {
            Vec a = points[i], b = points[(i + 1) % points.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum * 0.5;
    }

    private static void AddCircle(StreamGeometryContext ctx, Vec center, double radius, double direction)
    {
        const int steps = 18;
        ctx.BeginFigure(new Point(center.X + radius, center.Y), true, true);
        for (int i = 1; i <= steps; i++)
        {
            double angle = direction * 2 * Math.PI * i / steps;
            ctx.LineTo(new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)), false, false);
        }
    }

    private static Vec Normalize(Vec v)
    {
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        if (len < 1e-9) return new Vec(0, 0);
        return new Vec(v.X / len, v.Y / len);
    }

    /// <summary>Толщина штриха с учётом стиля кисти.</summary>
    private static List<double> ApplyPreset(string preset, IList<Vec> points, List<double> widths, double angle)
    {
        if (preset != "calligraphy" || points == null || widths.Count < 2) return widths;

        // Каллиграфия: ширина зависит от направления движения относительно пера.
        double nib = angle * Math.PI / 180.0;
        var result = new List<double>(widths.Count);
        for (int i = 0; i < widths.Count; i++)
        {
            Vec prev = points[Math.Max(0, i - 1)];
            Vec next = points[Math.Min(points.Count - 1, i + 1)];
            double dx = next.X - prev.X, dy = next.Y - prev.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double direction = length < 1e-9 ? nib : Math.Atan2(dy, dx);
            double factor = Math.Abs(Math.Sin(direction - nib));
            result.Add(widths[i] * (0.18 + 0.82 * factor));
        }
        return result;
    }

    private static List<double> Repeat(double value, int count)
    {
        var list = new List<double>(count);
        for (int i = 0; i < count; i++) list.Add(value);
        return list;
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