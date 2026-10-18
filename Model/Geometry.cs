using System;
using System.Collections.Generic;

namespace MotionForge.Model;

/// <summary>Двумерная точка (не зависит от WPF — модель чистая и тестируемая).</summary>
public struct Vec
{
    public double X { get; set; }
    public double Y { get; set; }

    public Vec(double x, double y) { X = x; Y = y; }

    public static readonly Vec Zero = new Vec(0, 0);

    public static Vec operator +(Vec a, Vec b) => new Vec(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new Vec(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double k) => new Vec(a.X * k, a.Y * k);
    public static Vec operator /(Vec a, double k) => new Vec(a.X / k, a.Y / k);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public override string ToString() => X.ToString("0.###") + ";" + Y.ToString("0.###");
}

/// <summary>Прямоугольник (в мировых координатах или локальных — зависит от контекста).</summary>
public struct RectD
{
    // Свойства (а не поля) — чтобы прямоугольник корректно сохранялся в JSON.
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }

    public RectD(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }

    public static readonly RectD Empty = new RectD(double.NaN, double.NaN, 0, 0);

    public bool IsEmpty => double.IsNaN(X) || double.IsNaN(Y) || double.IsNaN(W) || double.IsNaN(H) || (W <= 0 && H <= 0);

    public double Left => X;
    public double Top => Y;
    public double Right => X + W;
    public double Bottom => Y + H;
    public Vec Center => new Vec(X + W / 2, Y + H / 2);
    public Vec TopLeft => new Vec(X, Y);
    public Vec TopRight => new Vec(Right, Y);
    public Vec BottomLeft => new Vec(X, Bottom);
    public Vec BottomRight => new Vec(Right, Bottom);

    public RectD Inflate(double d) => new RectD(X - d, Y - d, W + d * 2, H + d * 2);

    public bool Contains(Vec p) => p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;
    public bool Intersects(RectD o) => !(o.Left > Right || o.Right < Left || o.Top > Bottom || o.Bottom < Top);

    public static RectD FromPoints(IEnumerable<Vec> pts, double pad = 0)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;
        foreach (var p in pts)
        {
            any = true;
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        if (!any) return Empty;
        return new RectD(minX - pad, minY - pad, maxX - minX + pad * 2, maxY - minY + pad * 2);
    }

    public static RectD Union(RectD a, RectD b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        double x = Math.Min(a.Left, b.Left), y = Math.Min(a.Top, b.Top);
        double r = Math.Max(a.Right, b.Right), bt = Math.Max(a.Bottom, b.Bottom);
        return new RectD(x, y, r - x, bt - y);
    }

    public override string ToString() => $"({X:0.#},{Y:0.#} {W:0.#}x{H:0.#})";
}

/// <summary>Аффинное преобразование 2×3: [A C E; B D F].</summary>
public struct Mat
{
    public double A, B, C, D, E, F;

    public Mat(double a, double b, double c, double d, double e, double f)
    { A = a; B = b; C = c; D = d; E = e; F = f; }

    public static readonly Mat Identity = new Mat(1, 0, 0, 1, 0, 0);

    public static Mat FromTransform(double x, double y, double rotationDeg, double scaleX, double scaleY)
    {
        double r = rotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(r), sin = Math.Sin(r);
        return new Mat(cos * scaleX, sin * scaleX, -sin * scaleY, cos * scaleY, x, y);
    }

    public static Mat Translate(double x, double y) => new Mat(1, 0, 0, 1, x, y);
    public static Mat Scale(double sx, double sy) => new Mat(sx, 0, 0, sy, 0, 0);
    public static Mat RotateDeg(double deg)
    {
        double r = deg * Math.PI / 180.0;
        return new Mat(Math.Cos(r), Math.Sin(r), -Math.Sin(r), Math.Cos(r), 0, 0);
    }

    /// <summary>this * m — сначала применяется m, затем this.</summary>
    public Mat Multiply(Mat m) => new Mat(
        A * m.A + C * m.B,
        B * m.A + D * m.B,
        A * m.C + C * m.D,
        B * m.C + D * m.D,
        A * m.E + C * m.F + E,
        B * m.E + D * m.F + F);

    public double Determinant => A * D - B * C;

    public Vec Apply(Vec p) => new Vec(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);

    public Vec ApplyVector(Vec v) => new Vec(A * v.X + C * v.Y, B * v.X + D * v.Y);

    public bool TryInvert(out Mat inv)
    {
        double det = Determinant;
        if (Math.Abs(det) < 1e-12) { inv = Identity; return false; }
        double id = 1.0 / det;
        inv = new Mat(D * id, -B * id, -C * id, A * id, (C * F - D * E) * id, (B * E - A * F) * id);
        return true;
    }

    /// <summary>Разложение в перенос/поворот/масштаб (без скоса).</summary>
    public void Decompose(out double x, out double y, out double rotationDeg, out double scaleX, out double scaleY)
    {
        x = E; y = F;
        rotationDeg = Math.Atan2(B, A) * 180.0 / Math.PI;
        scaleX = Math.Sqrt(A * A + B * B);
        scaleY = (scaleX < 1e-12) ? Math.Sqrt(C * C + D * D) : (A * D - B * C) / scaleX;
    }
}

/// <summary>Кубическая кривая Безье.</summary>
public struct BezierSeg
{
    public Vec P0, C1, C2, P1;
    public BezierSeg(Vec p0, Vec c1, Vec c2, Vec p1) { P0 = p0; C1 = c1; C2 = c2; P1 = p1; }
}

public static class Geo
{
    public static double DistToSegment(Vec p, Vec a, Vec b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-12) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
        t = Math.Max(0, Math.Min(1, t));
        double px = a.X + t * dx, py = a.Y + t * dy;
        return Math.Sqrt((p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py));
    }

    public static bool PointInPolygon(Vec p, IList<Vec> poly)
    {
        if (poly == null || poly.Count < 3) return false;
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Vec a = poly[i], b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y + 1e-18) + a.X)
                inside = !inside;
        }
        return inside;
    }

    public static List<Vec> RectPoints(double w, double h)
    {
        return new List<Vec> { new Vec(0, 0), new Vec(w, 0), new Vec(w, h), new Vec(0, h) };
    }

    public static List<Vec> EllipsePoints(double rx, double ry, int segments = 48)
    {
        var pts = new List<Vec>(segments);
        for (int i = 0; i < segments; i++)
        {
            double a = i / (double)segments * Math.PI * 2;
            pts.Add(new Vec(Math.Cos(a) * rx, Math.Sin(a) * ry));
        }
        return pts;
    }

    /// <summary>Ломанная или сглаженная (Catmull-Rom) кривая по точкам.</summary>
    public static List<BezierSeg> Segments(IList<Vec> pts, bool closed, bool smooth)
    {
        var result = new List<BezierSeg>();
        if (pts == null || pts.Count < 2) return result;

        int n = pts.Count;
        int segCount = closed ? n : n - 1;

        for (int i = 0; i < segCount; i++)
        {
            Vec p0 = pts[i];
            Vec p1 = pts[(i + 1) % n];
            if (!smooth)
            {
                result.Add(new BezierSeg(p0, p0, p1, p1));
                continue;
            }
            Vec prev, next;
            if (closed)
            {
                prev = pts[(i - 1 + n) % n];
                next = pts[(i + 2) % n];
            }
            else
            {
                prev = pts[Math.Max(i - 1, 0)];
                next = pts[Math.Min(i + 2, n - 1)];
            }
            Vec c1 = p0 + (p1 - prev) / 6.0;
            Vec c2 = p1 - (next - p0) / 6.0;
            result.Add(new BezierSeg(p0, c1, c2, p1));
        }
        return result;
    }

    /// <summary>Упрощение линии (алгоритм Дугласа-Пекера) — чтобы росчерк не весил мегабайты.</summary>
    public static List<Vec> Simplify(IList<Vec> pts, double tolerance)
    {
        if (pts == null || pts.Count < 3) return pts == null ? new List<Vec>() : new List<Vec>(pts);
        var keep = new bool[pts.Count];
        keep[0] = keep[pts.Count - 1] = true;
        var stack = new Stack<int[]>();
        stack.Push(new[] { 0, pts.Count - 1 });
        while (stack.Count > 0)
        {
            int[] range = stack.Pop();
            int first = range[0], last = range[1];
            double maxDist = 0;
            int index = -1;
            for (int i = first + 1; i < last; i++)
            {
                double d = DistToSegment(pts[i], pts[first], pts[last]);
                if (d > maxDist) { maxDist = d; index = i; }
            }
            if (maxDist > tolerance && index > 0)
            {
                keep[index] = true;
                stack.Push(new[] { first, index });
                stack.Push(new[] { index, last });
            }
        }
        var outPts = new List<Vec>();
        for (int i = 0; i < pts.Count; i++) if (keep[i]) outPts.Add(pts[i]);
        return outPts;
    }

    public static RectD TransformBounds(Mat m, RectD local)
    {
        if (local.IsEmpty) return RectD.Empty;
        var corners = new[]
        {
            m.Apply(local.TopLeft), m.Apply(local.TopRight),
            m.Apply(local.BottomLeft), m.Apply(local.BottomRight),
        };
        return RectD.FromPoints(corners);
    }

    public static double AngleDeg(Vec center, Vec from, Vec to)
    {
        double a1 = Math.Atan2(from.Y - center.Y, from.X - center.X);
        double a2 = Math.Atan2(to.Y - center.Y, to.X - center.X);
        return (a2 - a1) * 180.0 / Math.PI;
    }

    public static double Snap(double value, double step)
    {
        if (step <= 0) return value;
        return Math.Round(value / step) * step;
    }
}