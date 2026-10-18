using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotionForge.Model;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Save(Project project) => JsonSerializer.Serialize(project, Options);

    public static Project Load(string text) => JsonSerializer.Deserialize<Project>(text, Options);

    /// <summary>Сериализация любого объекта теми же правилами (настройки, шаблоны и т.п.).</summary>
    public static string SaveObject<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T LoadObject<T>(string text) where T : class => JsonSerializer.Deserialize<T>(text, Options);

    /// <summary>Глубокое копирование (используется для снимков отмены и копирования кадров).</summary>
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options);
}

/// <summary>Элемент с уже вычисленными (в том числе проинтерполированными твином) свойствами.</summary>
public class EvalElement
{
    public Element Src;
    public Layer Layer;
    public double X, Y, Rotation, ScaleX, ScaleY, Alpha;
    public string Fill, Stroke, Color;
    public double StrokeWidth;
    public List<Vec> Points;

    public ElementKind Kind => Src.Kind;
    public Mat Matrix => Mat.FromTransform(X, Y, Rotation, ScaleX, ScaleY);
    public bool Interactive => Src.Visible && !Src.Locked;
}

/// <summary>Вычисление состояния сцены на заданном кадре, твины и интерполяция.</summary>
public static class Anim
{
    public static ITextMeasurer TextMeasurer = new ApproxTextMeasurer();

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    /// <summary>Кривая ускорения: ease &lt; 0 — медленный старт, ease &gt; 0 — плавное торможение.</summary>
    public static double EaseT(double t, double ease)
    {
        t = Clamp01(t);
        if (Math.Abs(ease) < 1e-6) return t;
        double power = 1 + Math.Abs(ease) * 2.2;
        return ease < 0 ? Math.Pow(t, power) : 1 - Math.Pow(1 - t, power);
    }

    public static List<Keyframe> Sorted(Layer layer)
    {
        var list = new List<Keyframe>(layer.Frames);
        list.Sort((a, b) => a.Index.CompareTo(b.Index));
        return list;
    }

    public static Keyframe Exact(Layer layer, int frame)
    {
        foreach (var kf in layer.Frames) if (kf.Index == frame) return kf;
        return null;
    }

    /// <summary>Ключевой кадр, который «держит» указанный кадр (включая растянутый диапазон).</summary>
    public static Keyframe Covering(Layer layer, int frame)
    {
        Keyframe result = null;
        foreach (var kf in layer.Frames)
            if (kf.Covers(frame) && (result == null || kf.Index > result.Index)) result = kf;
        return result;
    }

    public static Keyframe Previous(Layer layer, int frame)
    {
        Keyframe result = null;
        foreach (var kf in layer.Frames)
            if (kf.Index < frame && (result == null || kf.Index > result.Index)) result = kf;
        return result;
    }

    public static Keyframe Next(Layer layer, int frame)
    {
        Keyframe result = null;
        foreach (var kf in layer.Frames)
            if (kf.Index > frame && (result == null || kf.Index < result.Index)) result = kf;
        return result;
    }

    public static int LayerLength(Layer layer)
    {
        int length = 1;
        foreach (var kf in layer.Frames) length = Math.Max(length, kf.EndIndex + 1);
        return length;
    }

    public static int SceneLength(Scene scene)
    {
        int length = 1;
        foreach (var layer in scene.Layers) length = Math.Max(length, LayerLength(layer));
        return length;
    }

    /// <summary>Габариты элемента в его собственных координатах (без учёта X/Y/поворота).</summary>
    public static RectD LocalBounds(Element e, Project project)
    {
        switch (e)
        {
            case ShapeElement s:
                return s.LocalBox();
            case TextElement t:
            {
                TextMeasurer.Measure(t, out double w, out double h);
                return new RectD(AlignOffset(t.Align, w), 0, w, h);
            }
            case ImageElement img:
                return new RectD(0, 0, Math.Max(1, img.PixelWidth), Math.Max(1, img.PixelHeight));
            case GroupElement g:
            {
                RectD box = RectD.Empty;
                foreach (var child in g.Children) box = RectD.Union(box, WorldBounds(child, project));
                return box;
            }
            case InstanceElement inst:
            {
                if (project != null && project.Library.TryGetValue(inst.SymbolId, out Symbol sym))
                    return sym.Bounds;
                return new RectD(-40, -40, 80, 80);
            }
        }
        return RectD.Empty;
    }

    public static double AlignOffset(string align, double width)
        => align == "center" ? -width / 2 : (align == "right" ? -width : 0);

    public static RectD WorldBounds(Element e, Project project)
    {
        RectD local = LocalBounds(e, project);
        return local.IsEmpty ? RectD.Empty : Geo.TransformBounds(e.LocalMatrix, local);
    }

    /// <summary>Габариты элемента кадра с учётом твина (для выделения и прямоугольников).</summary>
    public static RectD EvalBounds(EvalElement ev, Project project)
    {
        RectD local;
        switch (ev.Src)
        {
            case ShapeElement s:
            {
                double pad = s.Stroked ? ev.StrokeWidth / 2 : 0;
                local = RectD.FromPoints(ev.Points ?? s.Points, pad);
                break;
            }
            case TextElement t:
            {
                TextMeasurer.Measure(t, out double w, out double h);
                local = new RectD(AlignOffset(t.Align, w), 0, w, h);
                break;
            }
            default:
                local = LocalBounds(ev.Src, project);
                break;
        }
        if (local.IsEmpty) return RectD.Empty;
        return Geo.TransformBounds(ev.Matrix, local);
    }

    public static EvalElement Build(Element e, Layer layer)
    {
        var ev = new EvalElement
        {
            Src = e, Layer = layer,
            X = e.X, Y = e.Y, Rotation = e.Rotation,
            ScaleX = e.ScaleX, ScaleY = e.ScaleY, Alpha = e.Alpha,
        };
        switch (e)
        {
            case ShapeElement s:
                ev.Fill = s.Fill; ev.Stroke = s.Stroke; ev.StrokeWidth = s.StrokeWidth;
                ev.Points = new List<Vec>(s.Points);
                break;
            case TextElement t:
                ev.Color = t.Color;
                break;
        }
        return ev;
    }

    public static EvalElement Interpolate(Element from, Element to, Layer layer, double t)
    {
        double Lerp(double a, double b) => a + (b - a) * t;
        var ev = new EvalElement
        {
            Src = to, Layer = layer,
            X = Lerp(from.X, to.X),
            Y = Lerp(from.Y, to.Y),
            Rotation = Lerp(from.Rotation, to.Rotation),
            ScaleX = Lerp(from.ScaleX, to.ScaleX),
            ScaleY = Lerp(from.ScaleY, to.ScaleY),
            Alpha = Lerp(from.Alpha, to.Alpha),
        };

        var sFrom = from as ShapeElement;
        var sTo = to as ShapeElement;
        if (sFrom != null && sTo != null)
        {
            ev.Fill = ColorUtil.Mix(sFrom.Fill, sTo.Fill, t);
            ev.Stroke = ColorUtil.Mix(sFrom.Stroke, sTo.Stroke, t);
            ev.StrokeWidth = Lerp(sFrom.StrokeWidth, sTo.StrokeWidth);
            if (sFrom.Points.Count == sTo.Points.Count)
            {
                var pts = new List<Vec>(sTo.Points.Count);
                for (int i = 0; i < sTo.Points.Count; i++)
                    pts.Add(new Vec(Lerp(sFrom.Points[i].X, sTo.Points[i].X), Lerp(sFrom.Points[i].Y, sTo.Points[i].Y)));
                ev.Points = pts;
            }
            else ev.Points = new List<Vec>(sTo.Points);
        }
        else if (to is TextElement tTo && from is TextElement tFrom)
        {
            ev.Color = ColorUtil.Mix(tFrom.Color, tTo.Color, t);
        }
        return ev;
    }

    /// <summary>
    /// Все элементы слоя на кадре. Твин помечается на ключевом кадре и анимирует отрезок
    /// от предыдущего ключевого кадра до него самого (как в Adobe Animate).
    /// </summary>
    public static List<EvalElement> EvaluateLayer(Layer layer, int frame)
    {
        var result = new List<EvalElement>();
        if (!layer.Visible) return result;

        Keyframe cover = Covering(layer, frame);
        if (cover == null) return result;

        Keyframe target = null;
        if (frame > cover.Index)
        {
            Keyframe next = Next(layer, cover.Index);
            if (next != null && next.Tween != null && next.Tween.Type != "none") target = next;
        }

        if (target != null)
        {
            double span = target.Index - cover.Index;
            double t = span <= 0 ? 1 : (frame - cover.Index) / span;
            t = EaseT(t, target.Tween.Ease);

            foreach (var e in target.Elements)
            {
                if (!e.Visible) continue;
                Element from = FindById(cover.Elements, e.Id);
                result.Add(from != null ? Interpolate(from, e, layer, t) : Build(e, layer));
            }
            // Объекты, которых нет в конечном ключе, остаются на своих местах.
            foreach (var e in cover.Elements)
            {
                if (!e.Visible) continue;
                if (FindById(target.Elements, e.Id) == null) result.Add(Build(e, layer));
            }
            return result;
        }

        foreach (var e in cover.Elements)
            if (e.Visible) result.Add(Build(e, layer));
        return result;
    }

    public static Element FindById(List<Element> list, string id)
    {
        if (list == null) return null;
        foreach (var e in list) if (e.Id == id) return e;
        return null;
    }

    /// <summary>Сцена целиком в порядке отрисовки: сначала нижние слои, затем верхние.</summary>
    public static List<EvalElement> EvaluateScene(Scene scene, int frame)
    {
        var result = new List<EvalElement>();
        for (int i = scene.Layers.Count - 1; i >= 0; i--)
            result.AddRange(EvaluateLayer(scene.Layers[i], frame));
        return result;
    }

    public static RectD ContentBounds(Scene scene, int frame, Project project)
    {
        RectD box = RectD.Empty;
        foreach (var layer in scene.Layers)
        {
            if (!layer.Visible) continue;
            foreach (var ev in EvaluateLayer(layer, frame))
                box = RectD.Union(box, EvalBounds(ev, project));
        }
        return box;
    }

    /// <summary>Объединение габаритов содержимого по всем кадрам (используется для рамки символа).</summary>
    public static RectD ContentBoundsAll(Scene scene, Project project)
    {
        RectD box = RectD.Empty;
        int length = Math.Max(1, SceneLength(scene));
        for (int frame = 0; frame < length; frame++)
            box = RectD.Union(box, ContentBounds(scene, frame, project));
        return box;
    }
}