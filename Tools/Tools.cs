using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MotionForge.Model;
using MotionForge.Services;

namespace MotionForge.Tools;

public enum HandleKind { None, Body, NW, N, NE, E, SE, S, SW, W, Rotate }

/// <summary>Контекст, который StageView передаёт инструменту: мировые координаты + модификаторы.</summary>
public class ToolContext
{
    public Store Store;
    public double Px = 1;                // размер экранного пикселя в мировых единицах
    public ModifierKeys Mods;
    public Action Invalidate;

    /// <summary>Ввод от пера планшета (а не от мыши).</summary>
    public bool FromStylus;

    /// <summary>Сила нажатия пера: 0.05…1 (для мыши всегда 1).</summary>
    public double Pressure = 1;

    /// <summary>Есть ли смысл учитывать нажатие (перо и нажатие реально меняется).</summary>
    public bool PressureActive => FromStylus && Pressure < 0.999 && Pressure > 0.02;

    public bool Shift => (Mods & ModifierKeys.Shift) != 0;
    public bool Alt => (Mods & ModifierKeys.Alt) != 0;
    public bool Ctrl => (Mods & ModifierKeys.Control) != 0;

    /// <summary>Множитель толщины для текущего ввода (1 — без нажатия).</summary>
    public double PressureScale => PressureActive ? Math.Max(0.08, Pressure) : 1;

    public void Redraw() => Invalidate?.Invoke();
}

public static class Handles
{
    public const double ScreenSize = 8;

    public static (Vec pos, HandleKind kind)[] Positions(RectD box, double px)
    {
        double cx = box.Center.X, cy = box.Center.Y;
        double off = 22 * px;
        return new[]
        {
            (new Vec(box.Left, box.Top), HandleKind.NW),
            (new Vec(cx, box.Top), HandleKind.N),
            (new Vec(box.Right, box.Top), HandleKind.NE),
            (new Vec(box.Right, cy), HandleKind.E),
            (new Vec(box.Right, box.Bottom), HandleKind.SE),
            (new Vec(cx, box.Bottom), HandleKind.S),
            (new Vec(box.Left, box.Bottom), HandleKind.SW),
            (new Vec(box.Left, cy), HandleKind.W),
            (new Vec(cx, box.Top - off), HandleKind.Rotate),
        };
    }

    public static HandleKind Hit(RectD box, Vec p, double px)
    {
        if (box.IsEmpty) return HandleKind.None;
        double r = ScreenSize * px;
        foreach (var (pos, kind) in Positions(box, px))
            if (Math.Abs(pos.X - p.X) <= r && Math.Abs(pos.Y - p.Y) <= r) return kind;
        return HandleKind.None;
    }

    /// <summary>Точка-«якорь», относительно которой масштабируется прямоугольник.</summary>
    public static Vec Anchor(RectD box, HandleKind kind)
    {
        switch (kind)
        {
            case HandleKind.NW: return box.BottomRight;
            case HandleKind.N: return new Vec(box.Center.X, box.Bottom);
            case HandleKind.NE: return box.BottomLeft;
            case HandleKind.E: return new Vec(box.Left, box.Center.Y);
            case HandleKind.SE: return box.TopLeft;
            case HandleKind.S: return new Vec(box.Center.X, box.Top);
            case HandleKind.SW: return box.TopRight;
            case HandleKind.W: return new Vec(box.Right, box.Center.Y);
            default: return box.Center;
        }
    }
}

public abstract class Tool
{
    public abstract string Id { get; }
    public virtual string Title => Id;
    public virtual string Hint => "";
    public bool Dragging;

    public virtual void Down(ToolContext ctx, Vec p, bool rightButton) { }
    public virtual void Move(ToolContext ctx, Vec p) { }
    public virtual void Up(ToolContext ctx, Vec p) { }
    public virtual void DoubleClick(ToolContext ctx, Vec p) { }
    public virtual void Cancel(ToolContext ctx) { Dragging = false; }
    public virtual void DrawOverlay(DrawingContext dc, ToolContext ctx) { }
    public virtual bool OnKey(ToolContext ctx, Key key) => false;

    /* --- помощники отрисовки оверлея (в мировых координатах) --- */

    protected static void DrawLine(DrawingContext dc, Vec a, Vec b, Brush brush, double px, double thickness = 1)
    {
        dc.DrawLine(new Pen(brush, thickness * px), new Point(a.X, a.Y), new Point(b.X, b.Y));
    }

    protected static void DrawDot(DrawingContext dc, Vec p, Brush brush, double px, double size = 7)
    {
        double s = size * px;
        dc.DrawEllipse(brush, null, new Point(p.X, p.Y), s, s);
    }

    protected static void DrawFrame(DrawingContext dc, RectD box, Brush brush, double px, double dash = 4)
    {
        if (box.IsEmpty) return;
        var pen = new Pen(brush, 1 * px) { DashStyle = new DashStyle(new double[] { dash, dash }, 0) };
        dc.DrawRectangle(null, pen, new Rect(box.X, box.Y, Math.Max(0.1, box.W), Math.Max(0.1, box.H)));
    }

    protected static readonly Brush AccentBrush = Frozen("#4A90D9");
    protected static readonly Brush Accent2Brush = Frozen("#7FB8E8");
    protected static readonly Brush WhiteBrush = Frozen("#FFFFFF");
    protected static readonly Brush DarkBrush = Frozen("#141414");
    protected static readonly Brush WarnBrush = Frozen("#E0A23C");

    protected static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/* ===================================================================== */
/*  Выделение / трансформирование                                        */
/* ===================================================================== */

public class SelectTool : Tool
{
    private readonly bool _alwaysHandles;

    public SelectTool(bool alwaysHandles = false) { _alwaysHandles = alwaysHandles; }

    public override string Id => _alwaysHandles ? "transform" : "select";
    public override string Title => _alwaysHandles ? "Трансформирование" : "Выделение";
    public override string Hint => "Тяните объект, чтобы переместить. Уголки — масштаб, верхний кружок — поворот. Shift — добавить к выделению.";

    private Vec _start;
    private Vec _applied;
    private HandleKind _handle = HandleKind.None;
    private RectD _startBox;
    private Vec _pivot;
    private double _startAngle;
    private bool _marquee;
    private readonly List<(Element element, Vec world, double sx, double sy)> _records = new List<(Element, Vec, double, double)>();
    private RectD _marqueeBox;
    private bool _moved;

    public override void Down(ToolContext ctx, Vec p, bool rightButton)
    {
        var store = ctx.Store;
        _start = p;
        _applied = p;
        _moved = false;
        _marquee = false;
        _records.Clear();

        if (!_alwaysHandles && store.Selection.Count > 0)
        {
            RectD box = store.SelectionBounds();
            HandleKind hit = Handles.Hit(box, p, ctx.Px);
            if (hit == HandleKind.Rotate)
            {
                _handle = hit;
                _startBox = box;
                _pivot = box.Center;
                _startAngle = Math.Atan2(p.Y - _pivot.Y, p.X - _pivot.X) * 180 / Math.PI;
                Record(store);
                Dragging = true;
                return;
            }
            if (hit != HandleKind.None)
            {
                _handle = hit;
                _startBox = box;
                _pivot = Handles.Anchor(box, hit);
                Record(store);
                Dragging = true;
                return;
            }
        }

        double tolerance = 5 * ctx.Px;
        var hitElement = store.HitTest(p, tolerance, out Element leaf);
        Element target = leaf ?? hitElement?.Src;

        if (target != null)
        {
            if (ctx.Shift) store.ToggleSelection(target.Id);
            else if (!store.Selection.Contains(target.Id)) store.SetSelection(new[] { target.Id });
            _handle = HandleKind.Body;
            Record(store);
            Dragging = true;
        }
        else
        {
            if (!ctx.Shift) store.ClearSelection();
            _marquee = true;
            _marqueeBox = new RectD(p.X, p.Y, 0, 0);
            Dragging = true;
        }
        ctx.Redraw();
    }

    private void Record(Store store)
    {
        _records.Clear();
        foreach (var element in store.SelectionElements())
        {
            Vec world = store.ParentMatrix(element).Apply(new Vec(element.X, element.Y));
            _records.Add((element, world, element.ScaleX, element.ScaleY));
        }
    }

    public override void Move(ToolContext ctx, Vec p)
    {
        if (!Dragging) return;
        var store = ctx.Store;

        if (_marquee)
        {
            _marqueeBox = new RectD(Math.Min(_start.X, p.X), Math.Min(_start.Y, p.Y), Math.Abs(p.X - _start.X), Math.Abs(p.Y - _start.Y));
            ctx.Redraw();
            return;
        }

        if (_records.Count == 0) return;

        switch (_handle)
        {
            case HandleKind.Body:
            {
                Vec delta = new Vec(p.X - _applied.X, p.Y - _applied.Y);
                if (ctx.Shift)
                {
                    if (Math.Abs(delta.X) > Math.Abs(delta.Y)) delta.Y = 0; else delta.X = 0;
                }
                if (Math.Abs(delta.X) > 0.001 || Math.Abs(delta.Y) > 0.001)
                {
                    foreach (var rec in _records) store.OffsetElement(rec.element, delta.X, delta.Y);
                    _applied = p;
                    _moved = true;
                }
                break;
            }
            case HandleKind.Rotate:
            {
                double angle = Math.Atan2(p.Y - _pivot.Y, p.X - _pivot.X) * 180 / Math.PI;
                double delta = angle - _startAngle;
                if (ctx.Shift) delta = Math.Round(delta / 15.0) * 15.0;
                double step = delta - (_appliedAngle);
                foreach (var rec in _records.ToList())
                {
                    store.RotateElement(rec.element, step, _pivot);
                }
                _appliedAngle = delta;
                _moved = true;
                break;
            }
            default:
            {
                double sx = 1, sy = 1;
                if (_startBox.W > 0.01) sx = (p.X - _pivot.X) / (_startBox_anchorX() - _pivot.X + 1e-9);
                if (_startBox.H > 0.01) sy = (p.Y - _pivot.Y) / (_startBox_anchorY() - _pivot.Y + 1e-9);
                if (ctx.Shift)
                {
                    double m = Math.Max(Math.Abs(sx), Math.Abs(sy));
                    sx = Math.Sign(sx == 0 ? 1 : sx) * m;
                    sy = Math.Sign(sy == 0 ? 1 : sy) * m;
                }
                ApplyScale(store, sx, sy);
                _moved = true;
                break;
            }
        }
        ctx.Redraw();
    }

    private double _appliedAngle;

    private double _startBox_anchorX()
    {
        // Точка, от которой считаем относительный масштаб по X.
        switch (_handle)
        {
            case HandleKind.NW: return _startBox.Right;
            case HandleKind.NE: return _startBox.Left;
            case HandleKind.SE: return _startBox.Left;
            case HandleKind.SW: return _startBox.Right;
            case HandleKind.N: return _startBox.Center.X;
            case HandleKind.S: return _startBox.Center.X;
            case HandleKind.E: return _startBox.Left;
            case HandleKind.W: return _startBox.Right;
        }
        return _startBox.Center.X;
    }

    private double _startBox_anchorY()
    {
        switch (_handle)
        {
            case HandleKind.NW: return _startBox.Bottom;
            case HandleKind.NE: return _startBox.Bottom;
            case HandleKind.SE: return _startBox.Top;
            case HandleKind.SW: return _startBox.Top;
            case HandleKind.N: return _startBox.Bottom;
            case HandleKind.S: return _startBox.Top;
            case HandleKind.E: return _startBox.Center.Y;
            case HandleKind.W: return _startBox.Center.Y;
        }
        return _startBox.Center.Y;
    }

    private void ApplyScale(Store store, double sx, double sy)
    {
        Vec ratio = new Vec(
            _startBox_anchorX() - _pivot.X == 0 ? 1 : sx,
            _startBox_anchorY() - _pivot.Y == 0 ? 1 : sy);

        foreach (var rec in _records)
        {
            var element = rec.element;
            element.ScaleX = rec.sx * (double.IsFinite(ratio.X) ? ratio.X : 1);
            element.ScaleY = rec.sy * (double.IsFinite(ratio.Y) ? ratio.Y : 1);
            Vec target = new Vec(
                _pivot.X + (rec.world.X - _pivot.X) * ratio.X,
                _pivot.Y + (rec.world.Y - _pivot.Y) * ratio.Y);
            Vec current = store.ParentMatrix(element).Apply(new Vec(element.X, element.Y));
            store.OffsetElement(element, target.X - current.X, target.Y - current.Y);
        }
    }

    public override void Up(ToolContext ctx, Vec p)
    {
        var store = ctx.Store;
        if (_marquee)
        {
            var ids = new List<string>();
            RectD area = _marqueeBox;
            foreach (var ev in Anim.EvaluateScene(store.ActiveScene, store.Playhead))
            {
                if (!ev.Src.Visible || ev.Src.Locked) continue;
                if (ev.Layer != null && (!ev.Layer.Visible || ev.Layer.Locked)) continue;
                RectD bounds = Anim.EvalBounds(ev, store.Project);
                if (bounds.Intersects(area)) ids.Add(ev.Src.Id);
            }
            if (ctx.Shift)
            {
                foreach (string id in ids) if (!store.Selection.Contains(id)) store.Selection.Add(id);
                store.RaiseSelection();
            }
            else store.SetSelection(ids);
        }
        else if (_moved)
        {
            string label = _handle == HandleKind.Body ? "Перемещение"
                : _handle == HandleKind.Rotate ? "Поворот" : "Масштаб";
            store.Commit(label);
            store.RaiseAll();
        }

        Dragging = false;
        _marquee = false;
        _handle = HandleKind.None;
        _appliedAngle = 0;
        _records.Clear();
        ctx.Redraw();
    }

    public override void DoubleClick(ToolContext ctx, Vec p)
    {
        var store = ctx.Store;
        var hit = store.HitTest(p, 5 * ctx.Px, out Element leaf);
        if (hit == null) return;
        if (hit.Src is InstanceElement instance)
        {
            store.EnterSymbol(instance.SymbolId);
            return;
        }
        if (hit.Src is GroupElement && leaf != null && leaf != hit.Src)
        {
            store.SetSelection(new[] { leaf.Id });
            store.Report("Выбран объект внутри группы. Esc — вернуться к группе.");
        }
    }

    public override bool OnKey(ToolContext ctx, Key key)
    {
        if (key == Key.Escape && ctx.Store.Selection.Count > 0)
        {
            // Выход из группы наружу
            var element = ctx.Store.SelectionElements().FirstOrDefault();
            if (element != null)
            {
                var (_, _, kf) = ctx.Store.FindItem(element.Id);
                var parent = FindParent(kf?.Elements, element.Id);
                if (parent != null)
                {
                    ctx.Store.SetSelection(new[] { parent.Id });
                    return true;
                }
            }
            ctx.Store.ClearSelection();
            return true;
        }
        return false;
    }

    private static GroupElement FindParent(List<Element> list, string id)
    {
        if (list == null) return null;
        foreach (var e in list)
        {
            if (e is GroupElement g)
            {
                foreach (var c in g.Children) if (c.Id == id) return g;
                var nested = FindParent(g.Children, id);
                if (nested != null) return nested;
            }
        }
        return null;
    }

    public override void DrawOverlay(DrawingContext dc, ToolContext ctx)
    {
        var store = ctx.Store;
        if (_marquee)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 74, 144, 217)), new Pen(Accent2Brush, 1 * ctx.Px), ToRect(_marqueeBox));
        }

        if (store.Selection.Count == 0) return;
        RectD box = store.SelectionBounds();
        if (box.IsEmpty) return;

        var pen = new Pen(AccentBrush, 1 * ctx.Px) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
        dc.DrawRectangle(null, pen, ToRect(box));

        double s = Handles.ScreenSize * ctx.Px / 2;
        foreach (var (pos, kind) in Handles.Positions(box, ctx.Px))
        {
            if (kind == HandleKind.Rotate)
            {
                DrawLine(dc, new Vec(box.Center.X, box.Top), pos, AccentBrush, ctx.Px, 1);
                dc.DrawEllipse(WhiteBrush, new Pen(AccentBrush, 1.5 * ctx.Px), new Point(pos.X, pos.Y), s * 0.9, s * 0.9);
            }
            else
            {
                dc.DrawRectangle(WhiteBrush, new Pen(AccentBrush, 1.2 * ctx.Px),
                    new Rect(pos.X - s, pos.Y - s, s * 2, s * 2));
            }
        }
    }

    public static Rect ToRect(RectD box) => new Rect(box.X, box.Y, Math.Max(0.01, box.W), Math.Max(0.01, box.H));
}

/* ===================================================================== */
/*  Частичное выделение (точки фигуры)                                    */
/* ===================================================================== */

public class SubSelectTool : Tool
{
    public override string Id => "subselect";
    public override string Title => "Частичное выделение";
    public override string Hint => "Выберите фигуру и тяните её точки. Двойной щелчок по отрезку добавит точку.";

    private ShapeElement _shape;
    private int _pointIndex = -1;
    private Vec _last;
    private bool _moved;

    private ShapeElement CurrentShape(ToolContext ctx)
    {
        if (_shape != null && ctx.Store.FindItem(_shape.Id).element == _shape) return _shape;
        var first = ctx.Store.SelectionElements().OfType<ShapeElement>().FirstOrDefault();
        _shape = first;
        return _shape;
    }

    public override void Down(ToolContext ctx, Vec p, bool rightButton)
    {
        var store = ctx.Store;
        var shape = CurrentShape(ctx);
        double tolerance = 6 * ctx.Px;

        if (shape != null)
        {
            var matrix = store.WorldMatrix(shape);
            if (matrix.TryInvert(out Mat inv))
            {
                Vec local = inv.Apply(p);
                for (int i = 0; i < shape.Points.Count; i++)
                {
                    double d = Math.Sqrt(Math.Pow(shape.Points[i].X - local.X, 2) + Math.Pow(shape.Points[i].Y - local.Y, 2));
                    if (d <= tolerance * 1.5)
                    {
                        _pointIndex = i;
                        _last = local;
                        Dragging = true;
                        ctx.Redraw();
                        return;
                    }
                }
            }
        }

        var hit = store.HitTest(p, tolerance, out Element leaf);
        if (hit != null)
        {
            store.SetSelection(new[] { (leaf ?? hit.Src).Id });
            _shape = (leaf ?? hit.Src) as ShapeElement;
        }
        else
        {
            store.ClearSelection();
            _shape = null;
        }
        _pointIndex = -1;
        ctx.Redraw();
    }

    public override void Move(ToolContext ctx, Vec p)
    {
        if (!Dragging || _shape == null || _pointIndex < 0) return;
        var store = ctx.Store;
        Mat matrix = store.WorldMatrix(_shape);
        if (!matrix.TryInvert(out Mat inv)) return;
        Vec local = inv.Apply(p);
        if (ctx.Shift)
        {
            local = new Vec(Math.Round(local.X), Math.Round(local.Y));
        }
        _shape.Points[_pointIndex] = local;
        _moved = true;
        ctx.Redraw();
    }

    public override void Up(ToolContext ctx, Vec p)
    {
        if (_moved)
        {
            ctx.Store.Commit("Правка точек");
            ctx.Store.RaiseAll();
        }
        _moved = false;
        Dragging = false;
    }

    public override void DrawOverlay(DrawingContext dc, ToolContext ctx)
    {
        var shape = CurrentShape(ctx);
        if (shape == null) return;
        var matrix = ctx.Store.WorldMatrix(shape);
        double s = 4.5 * ctx.Px;
        for (int i = 0; i < shape.Points.Count; i++)
        {
            Vec world = matrix.Apply(shape.Points[i]);
            bool active = i == _pointIndex;
            dc.DrawRectangle(active ? Accent2Brush : WhiteBrush, new Pen(AccentBrush, 1.2 * ctx.Px),
                new Rect(world.X - s, world.Y - s, s * 2, s * 2));
        }
        RectD box = Anim.WorldBounds(shape, ctx.Store.Project);
        DrawFrame(dc, box, AccentBrush, ctx.Px, 3);
    }
}

/* ===================================================================== */
/*  Примитивы: линия, прямоугольник, овал                                 */
/* ===================================================================== */

public abstract class ShapeTool : Tool
{
    protected Vec Start;
    protected Vec End;
    protected bool Committed;

    public override void Down(ToolContext ctx, Vec p, bool rightButton)
    {
        Start = p;
        End = p;
        Committed = false;
        Dragging = true;
        ctx.Redraw();
    }

    public override void Move(ToolContext ctx, Vec p)
    {
        if (!Dragging) return;
        End = p;
        ctx.Redraw();
    }

    public override void Up(ToolContext ctx, Vec p)
    {
        if (!Dragging) return;
        End = p;
        Dragging = false;
        Create(ctx);
    }

    protected Vec[] Corners(ToolContext ctx, bool constrain)
    {
        double x1 = Start.X, y1 = Start.Y, x2 = End.X, y2 = End.Y;
        if (constrain)
        {
            double size = Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1));
            x2 = x1 + Math.Sign(x2 - x1 == 0 ? 1 : x2 - x1) * size;
            y2 = y1 + Math.Sign(y2 - y1 == 0 ? 1 : y2 - y1) * size;
        }
        if ((ctx.Store.SnapToGrid || ctx.Store.GridOn) && !ctx.Alt)
        {
            x1 = Geo.Snap(x1, Store.GridSize); y1 = Geo.Snap(y1, Store.GridSize);
            x2 = Geo.Snap(x2, Store.GridSize); y2 = Geo.Snap(y2, Store.GridSize);
        }
        return new[] { new Vec(x1, y1), new Vec(x2, y2) };
    }

    protected ShapeElement NewShape(ToolContext ctx, List<Vec> points, Vec origin, bool closed)
    {
        var opts = ctx.Store.Opts;
        return new ShapeElement
        {
            Points = points,
            Closed = closed,
            X = origin.X,
            Y = origin.Y,
            Fill = opts.Fill,
            Stroke = opts.Stroke,
            StrokeWidth = opts.StrokeWidth,
            Filled = closed && opts.Filled,
            Stroked = opts.Stroked,
            Smooth = opts.Smooth,
        };
    }

    protected abstract void Create(ToolContext ctx);
}

public class RectTool : ShapeTool
{
    public override string Id => "rect";
    public override string Title => "Прямоугольник";
    public override string Hint => "Shift — квадрат, Alt — игнорировать сетку.";

    protected override void Create(ToolContext ctx)
    {
        var c = Corners(ctx, ctx.Shift);
        double w = Math.Abs(c[1].X - c[0].X), h = Math.Abs(c[1].Y - c[0].Y);
        if (w < 2 || h < 2) { Dragging = false; return; }
        var origin = new Vec((c[0].X + c[1].X) / 2, (c[0].Y + c[1].Y) / 2);
        var pts = new List<Vec>
        {
            new Vec(-w / 2, -h / 2), new Vec(w / 2, -h / 2), new Vec(w / 2, h / 2), new Vec(-w / 2, h / 2),
        };
        ctx.Store.AddElement(NewShape(ctx, pts, origin, true), "Прямоугольник");
        Dragging = false;
    }

    public override void DrawOverlay(DrawingContext dc, ToolContext ctx)
    {
        if (!Dragging) return;
        var c = Corners(ctx, ctx.Shift);
        DrawFrame(dc, new RectD(Math.Min(c[0].X, c[1].X), Math.Min(c[0].Y, c[1].Y), Math.Abs(c[1].X - c[0].X), Math.Abs(c[1].Y - c[0].Y)),
            Accent2Brush, ctx.Px, 4);
    }
}

public class EllipseTool : ShapeTool
{
    public override string Id => "ellipse";
    public override string Title => "Овал";
    public override string Hint => "Shift — круг.";

    protected override void Create(ToolContext ctx)
    {
        var c = Corners(ctx, ctx.Shift);
        double w = Math.Abs(c[1].X - c[0].X), h = Math.Abs(c[1].Y - c[0].Y);
        if (w < 2 || h < 2) { Dragging = false; return; }
        var origin = new Vec((c[0].X + c[1].X) / 2, (c[0].Y + c[1].Y) / 2);
        var pts = Geo.EllipsePoints(w / 2, h / 2, 56);
        ctx.Store.AddElement(NewShape(ctx, pts, origin, true), "Овал");
        Dragging = false;
    }

    public override void DrawOverlay(DrawingContext dc, ToolContext ctx)
    {
        if (!Dragging) return;
        var c = Corners(ctx, ctx.Shift);
        DrawFrame(dc, new RectD(Math.Min(c[0].X, c[1].X), Math.Min(c[0].Y, c[1].Y), Math.Abs(c[1].X - c[0].X), Math.Abs(c[1].Y - c[0].Y)),
            Accent2Brush, ctx.Px, 4);
    }
}

public class LineTool : ShapeTool
{
    public override string Id => "line";
    public override string Title => "Линия";
    public override string Hint => "Shift — под углом 45°.";

    protected override void Create(ToolContext ctx)
    {
        Vec a = Start, b = End;
        if (ctx.Shift)
        {
            double angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
            double snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
            double len = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
            b = new Vec(a.X + Math.Cos(snapped) * len, a.Y + Math.Sin(snapped) * len);
        }
        if ((ctx.Store.SnapToGrid || ctx.Store.GridOn) && !ctx.Alt)
        {
            a = new Vec(Geo.Snap(a.X, Store.GridSize), Geo.Snap(a.Y, Store.GridSize));
            b = new Vec(Geo.Snap(b.X, Store.GridSize), Geo.Snap(b.Y, Store.GridSize));
        }
        double dx = b.X - a.X, dy = b.Y - a.Y;
        if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1) { Dragging = false; return; }
        var origin = new Vec((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var pts = new List<Vec> { new Vec(-dx / 2, -dy / 2), new Vec(dx / 2, dy / 2) };
        var shape = NewShape(ctx, pts, origin, false);
        shape.Filled = false;
        shape.Stroked = true;
        shape.Smooth = false;
        ctx.Store.AddElement(shape, "Линия");
        Dragging = false;
    }

    public override void DrawOverlay(DrawingContext dc, ToolContext ctx)
    {
        if (!Dragging) return;
        DrawLine(dc, Start, End, Accent2Brush, ctx.Px, 2);
    }
}

/* ===================================================================== */
/*  Карандаш и кисть                                                      */
/* ===================================================================== */

public class FreehandTool : Tool
{
    private readonly bool _brush;
    private readonly List<Vec> _points = new List<Vec>();
    private readonly List<double> _widths = new List<double>();
    private double _baseWidth = 1;

    public FreehandTool(bool brush) { _brush = brush; }

    public override string Id => _brush ? "brush" : "pencil";
    public override string Title => _brush ? "Кисть" : "Карандаш";
    public override string Hint => "Рисуйте прямо на сцене. Толщина — в параметрах инструмента, нажатие пера меняет нажим.";

    /// <summary>Толщина без учёта нажатия (как задано в параметрах инструмента).</summary>
    private double BaseWidth(Store store) =>
        _brush ? Math.Max(1, store.Opts.BrushSize) : Math.Max(0.5, store.Opts.StrokeWidth);

    public override void Down(ToolContext ctx, Vec p, bool rightButton)
    {
        _points.Clear();
        _widths.Clear();
        _baseWidth = BaseWidth(ctx.Store);
        _points.Add(p);
        _widths.Add(_baseWidth * ctx.PressureScale);
        Dragging = true;
        ctx.Redraw();
    }

    public override void Move(ToolContext ctx, Vec p)
    {
        if (!Dragging) return;
        Vec last = _points[_points.Count - 1];
        if (Math.Sqrt(Math.Pow(p.X - last.X, 2) + Math.Pow(p.Y - last.Y, 2)) < 1.2 * ctx.Px) return;
        _points.Add(p);
        _widths.Add(_baseWidth * ctx.PressureScale);
        ctx.Redraw();
    }

    public override void Up(ToolContext ctx, Vec p)
    {
        if (!Dragging) return;
        Dragging = false;
        if (_points.Count < 2) { _points.Clear(); _widths.Clear(); ctx.Redraw(); return; }

        var pts = Geo.Simplify(_points, 0.9 * ctx.Px);
        if (pts.Count < 2) pts = new List<Vec>(_points);

        RectD box = RectD.FromPoints(pts);
        var origin = box.Center;
        var local = new List<Vec>(pts.Count);
        foreach (var pt in pts) local.Add(new Vec(pt.X - origin.X, pt.Y - origin.Y));

        var opts = ctx.Store.Opts;
        var shape = new ShapeElement
        {
            Points = local,
            Closed = false,
            X = origin.X,
            Y = origin.Y,
            Fill = opts.Fill,
            Stroke = _brush ? opts.Fill : opts.Stroke,
            StrokeWidth = _baseWidth,
            Filled = false,
            Stroked = true,
            Smooth = opts.Smooth,
        };

        // Нажим пера: если толщина заметно менялась — сохраняем профиль по точкам.
        if (UsesPressure())
        {
            var widths = Geo.WidthsAlong(_points, _widths, pts);
            bool varies = false;
            foreach (double w in widths)
            {
                if (Math.Abs(w - _baseWidth) > 0.05) { varies = true; break; }
            }
            if (varies) shape.Widths = widths;
        }

        _points.Clear();
        _widths.Clear();
        ctx.Store.AddElement(shape, _brush ? "Мазок кисти" : "Линия карандаша");
    }

    private bool UsesPressure()
    {
        if (_widths.Count != _points.Count) return false;
        foreach (double w in _widths)
        {
            if (Math.Abs(w - _baseWidth) > 0.05) return true;
        }
        return false;
    }

    public override void DrawOverlay(DrawingContext dc, ToolContext ctx)
    {
        if (!Dragging || _points.Count < 2) return;

        if (ctx.PressureActive && UsesPressure())
        {
            var brush = new SolidColorBrush(Color.FromArgb(160, 111, 174, 219));
            Renderer.DrawRibbon(dc, _points, _widths, brush, 1);
            return;
        }

        var geometry = Renderer.BuildGeometry(_points, false, ctx.Store.Opts.Smooth);
        var pen = new Pen(Accent2Brush, (_brush ? ctx.Store.Opts.BrushSize : Math.Max(1, ctx.Store.Opts.StrokeWidth)) * 1);
        pen.StartLineCap = PenLineCap.Round;
        pen.EndLineCap = PenLineCap.Round;
        pen.Brush = new SolidColorBrush(Color.FromArgb(150, 111, 174, 219));
        dc.DrawGeometry(null, pen, geometry);
    }
}

/* ===================================================================== */
/*  Перо (построение кривой по точкам)                                    */
/* ===================================================================== */

public class PenTool : Tool
{
    private readonly List<Vec> _points = new List<Vec>();
    private Vec _cursor;

    public override string Id => "pen";
    public override string Title => "Перо";
    public override string Hint => "Щелчок — точка, щелчок по первой точке — замкнуть, Enter — закончить, Esc — отменить.";

    public override void Down(ToolContext ctx, Vec p, bool rightButton)
    {
        if (rightButton || _points.Count > 2 && Near(p, _points[0], 8 * ctx.Px))
        {
            Finish(ctx, true);
            return;
        }
        _points.Add(p);
        _cursor = p;
        Dragging = true;
        ctx.Redraw();
    }

    public override void Move(ToolContext ctx, Vec p)
    {
        _cursor = p;
        if (_points.Count > 0) ctx.Redraw();
    }

    public override void Up(ToolContext ctx, Vec p)
    {
        Dragging = false;
    }

    private static bool Near(Vec a, Vec b, double r)
    {
        return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)) <= r;
    }

    private void Finish(ToolContext ctx, bool closed)
    {
        if (_points.Count >= 2)
        {
            var pts = new List<Vec>(_points);
            if (ctx.Store.GridOn)
                for (int i = 0; i < pts.Count; i++) pts[i] = new Vec(Geo.Snap(pts[i].X, Store.GridSize), Geo.Snap(pts[i].Y, Store.GridSize));

            RectD box = RectD.FromPoints(pts);
            var origin = box.Center;
            var local = new List<Vec>(pts.Count);
            foreach (var pt in pts) local.Add(new Vec(pt.X - origin.X, pt.Y - origin.Y));

            var opts = ctx.Store.Opts;
            var shape = new ShapeElement
            {
                Points = local,
                Closed = closed,
                X = origin.X,
                Y = origin.Y,
                Fill = opts.Fill,
                Stroke = opts.Stroke,
                StrokeWidth = opts.StrokeWidth,
                Filled = closed && opts.Filled,
                Stroked = opts.Stroked,
                Smooth = true,
            };
            ctx.Store.AddElement(shape, closed ? "Фигура пера" : "Кривая пера");
        }
        _points.Clear();
        ctx.Redraw();
    }

    public override bool OnKey(ToolContext ctx, Key key)
    {
        if (_points.Count == 0) return false;
        if (key == Key.Enter) { Finish(ctx, false); return true; }
        if (key == Key.Escape) { _points.Clear(); ctx.Redraw(); return true; }
        if (key == Key.Back) { _points.RemoveAt(_points.Count - 1); ctx.Redraw(); return true; }
        return false;
    }

    public override void DrawOverlay(DrawingContext dc, ToolContext ctx)
    {
        if (_points.Count == 0) return;
        var preview = new List<Vec>(_points) { _cursor };
        var geometry = Renderer.BuildGeometry(preview, false, true);
        dc.DrawGeometry(null, new Pen(AccentBrush, 1.2 * ctx.Px), geometry);
        foreach (var pt in _points) DrawDot(dc, pt, WhiteBrush, ctx.Px, 3.5);
        DrawDot(dc, _points[0], Accent2Brush, ctx.Px, 5);
    }
}

/* ===================================================================== */
/*  Текст                                                                 */
/* ===================================================================== */

public class TextTool : Tool
{
    public override string Id => "text";
    public override string Title => "Текст";
    public override string Hint => "Щелкните по сцене и введите текст. Двойной щелчок по тексту выделенного объекта — правка.";

    public override void Down(ToolContext ctx, Vec p, bool rightButton)
    {
        var opts = ctx.Store.Opts;
        var text = new TextElement
        {
            Text = L10n.T("Текст"),
            X = p.X,
            Y = p.Y,
            FontFamily = opts.FontFamily,
            FontSize = opts.FontSize,
            Bold = opts.Bold,
            Italic = opts.Italic,
            Color = opts.Stroke == "#141826" ? "#141826" : opts.Stroke,
            Align = opts.TextAlign,
        };
        ctx.Store.AddElement(text, "Текст");
        ctx.Store.SetTool("select");
        ctx.Redraw();
    }

    public override void DrawOverlay(DrawingContext dc, ToolContext ctx)
    {
        DrawLine(dc, new Vec(_last.X - 6 * ctx.Px, _last.Y), new Vec(_last.X + 6 * ctx.Px, _last.Y), Accent2Brush, ctx.Px, 1.5);
    }

    private Vec _last;

    public override void Move(ToolContext ctx, Vec p) { _last = p; }
}

/* ===================================================================== */
/*  Заливка, ластик, пипетка                                              */
/* ===================================================================== */

public class BucketTool : Tool
{
    public override string Id => "bucket";
    public override string Title => "Заливка";
    public override string Hint => "Щелчок по замкнутой фигуре перекрашивает её. Alt — только обводка.";

    public override void Down(ToolContext ctx, Vec p, bool rightButton)
    {
        var store = ctx.Store;
        var hit = store.HitTest(p, 4 * ctx.Px, out Element leaf);
        var shape = (leaf ?? hit?.Src) as ShapeElement;
        if (shape == null)
        {
            var first = store.SelectionElements().OfType<ShapeElement>().FirstOrDefault();
            shape = first;
        }
        if (shape == null) { store.Report("Фигура не найдена"); return; }

        if (ctx.Alt)
        {
            shape.Stroke = store.Opts.Stroke;
            shape.StrokeWidth = store.Opts.StrokeWidth;
            shape.Stroked = true;
        }
        else
        {
            shape.Fill = store.Opts.Fill;
            shape.Filled = true;
            if (!shape.Closed) shape.Closed = true;
        }
        store.Commit("Заливка");
        store.RaiseAll();
    }
}

public class EraserTool : Tool
{
    private readonly List<(ShapeElement shape, List<Vec> original, Keyframe kf)> _touched =
        new List<(ShapeElement, List<Vec>, Keyframe)>();

    public override string Id => "eraser";
    public override string Title => "Ластик";
    public override string Hint => "Стирайте части фигур движением мыши. Alt — удалить объект целиком.";

    public override void Down(ToolContext ctx, Vec p, bool rightButton)
    {
        _touched.Clear();
        Dragging = true;
        Erase(ctx, p);
    }

    public override void Move(ToolContext ctx, Vec p)
    {
        if (Dragging) Erase(ctx, p);
    }

    public override void Up(ToolContext ctx, Vec p)
    {
        Dragging = false;
        if (_touched.Count > 0)
        {
            ctx.Store.Commit("Ластик");
            ctx.Store.RaiseAll();
        }
        _touched.Clear();
    }

    private void Erase(ToolContext ctx, Vec p)
    {
        var store = ctx.Store;
        double radius = 14 * ctx.Px;

        if (ctx.Alt)
        {
            var hit = store.HitTest(p, 4 * ctx.Px, out Element leaf);
            if (hit != null)
            {
                store.SetSelection(new[] { (leaf ?? hit.Src).Id });
                store.DeleteSelection();
            }
            return;
        }

        var kf = store.ActiveKeyframe;
        if (kf == null) return;
        bool changed = false;

        foreach (var element in kf.Elements.ToList())
        {
            if (!(element is ShapeElement shape)) continue;
            Mat matrix = store.WorldMatrix(shape);
            if (!matrix.TryInvert(out Mat inv)) continue;
            Vec local = inv.Apply(p);
            double localRadius = radius / Math.Max(0.05, Math.Sqrt(Math.Abs(matrix.A * matrix.D - matrix.B * matrix.C)));

            var record = _touched.FirstOrDefault(t => t.shape == shape);
            if (record.shape == null)
            {
                _touched.Add((shape, new List<Vec>(shape.Points), kf));
            }

            var kept = new List<Vec>();
            foreach (var pt in shape.Points)
            {
                double d = Math.Sqrt(Math.Pow(pt.X - local.X, 2) + Math.Pow(pt.Y - local.Y, 2));
                if (d > localRadius) kept.Add(pt);
                else changed = true;
            }
            if (kept.Count < 2)
            {
                kf.Elements.Remove(shape);
                changed = true;
            }
            else if (kept.Count != shape.Points.Count)
            {
                shape.Points = kept;
                changed = true;
            }
        }
        if (changed) ctx.Redraw();
    }

    public override void DrawOverlay(DrawingContext dc, ToolContext ctx)
    {
        // Курсор ластика рисует StageView (нужна позиция мыши).
    }
}

public class EyedropperTool : Tool
{
    public override string Id => "eyedropper";
    public override string Title => "Пипетка";
    public override string Hint => "Щелчок по объекту — взять его цвет.";

    public override void Down(ToolContext ctx, Vec p, bool rightButton)
    {
        var store = ctx.Store;
        var hit = store.HitTest(p, 4 * ctx.Px, out Element leaf);
        var target = leaf ?? hit?.Src;
        switch (target)
        {
            case ShapeElement shape:
                store.Opts.Fill = shape.Fill;
                store.Opts.Stroke = shape.Stroke;
                store.Opts.StrokeWidth = shape.StrokeWidth;
                store.Report("Цвет взят: " + shape.Fill);
                break;
            case TextElement text:
                store.Opts.Fill = text.Color;
                store.Report("Цвет текста взят: " + text.Color);
                break;
        }
        store.RaiseToolChanged();
        ctx.Redraw();
    }
}

/* ===================================================================== */
/*  Рука и масштаб                                                        */
/* ===================================================================== */

public class HandTool : Tool
{
    private Vec _start;
    private double _panX, _panY;

    public override string Id => "hand";
    public override string Title => "Рука";
    public override string Hint => "Тяните сцену. Пробел — временная рука, колесо — прокрутка, Ctrl+колесо — масштаб.";

    public override void Down(ToolContext ctx, Vec p, bool rightButton)
    {
        _start = p;
        _panX = ctx.Store.PanX;
        _panY = ctx.Store.PanY;
        Dragging = true;
        ctx.Redraw();
    }

    public override void Move(ToolContext ctx, Vec p)
    {
        if (!Dragging) return;
        double dx = (p.X - _start.X) * ctx.Store.Zoom;
        double dy = (p.Y - _start.Y) * ctx.Store.Zoom;
        ctx.Store.SetPan(_panX + dx, _panY + dy);
    }

    public override void Up(ToolContext ctx, Vec p) { Dragging = false; }
}

public class ZoomTool : Tool
{
    public override string Id => "zoom";
    public override string Title => "Масштаб";
    public override string Hint => "Щелчок — увеличить, Alt+щелчок — уменьшить.";

    public override void Down(ToolContext ctx, Vec p, bool rightButton)
    {
        double factor = (ctx.Alt || rightButton) ? 1 / 1.4 : 1.4;
        ctx.Store.SetZoom(ctx.Store.Zoom * factor);
        ctx.Redraw();
    }
}

/// <summary>Реестр инструментов по идентификатору.</summary>
public static class ToolRegistry
{
    private static readonly Dictionary<string, Tool> Map = new Dictionary<string, Tool>();
    private static bool _built;

    public static void Ensure()
    {
        if (_built) return;
        _built = true;
        Add(new SelectTool(false));
        Add(new SelectTool(true));
        Add(new SubSelectTool());
        Add(new LineTool());
        Add(new RectTool());
        Add(new EllipseTool());
        Add(new FreehandTool(false));
        Add(new FreehandTool(true));
        Add(new PenTool());
        Add(new TextTool());
        Add(new BucketTool());
        Add(new EraserTool());
        Add(new EyedropperTool());
        Add(new HandTool());
        Add(new ZoomTool());
    }

    private static void Add(Tool tool) => Map[tool.Id] = tool;

    public static Tool Get(string id)
    {
        Ensure();
        if (Map.TryGetValue(id, out Tool tool)) return tool;
        return Map["select"];
    }

    public static IEnumerable<Tool> All { get { Ensure(); return Map.Values; } }
}