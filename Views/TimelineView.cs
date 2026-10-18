using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MotionForge.Model;
using MotionForge.Services;

namespace MotionForge.Views;

/// <summary>
/// Кадровая сетка таймлайна в стиле Animate: линейка с номерами, заполненные кадры,
/// ключевые кадры-точки, перехода-переходы со стрелкой и играющая головка.
/// </summary>
public class TimelineView : FrameworkElement
{
    private Store _store;
    private bool _scrubbing;

    public double FrameWidth = 14;
    public double RowHeight = 24;
    public double RulerHeight = 20;
    public int ExtraFrames = 40;

    /// <summary>Щелчок правой кнопкой: (индекс слоя, кадр, точка на экране).</summary>
    public event Action<int, int, Point> RightClicked;
    /// <summary>Пользователь выбрал слой или кадр в таймлайне.</summary>
    public event Action LayersChangedByUser;
    /// <summary>Пользователь изменил масштаб кадров колесом мыши.</summary>
    public event Action FrameWidthChanged;

    public Store Store
    {
        get => _store;
        set
        {
            if (_store != null)
            {
                _store.DocumentChanged -= OnStoreChanged;
                _store.FrameChanged -= OnStoreChanged;
                _store.SelectionChanged -= OnStoreChanged;
            }
            _store = value;
            if (_store != null)
            {
                _store.DocumentChanged += OnStoreChanged;
                _store.FrameChanged += OnStoreChanged;
                _store.SelectionChanged += OnStoreChanged;
            }
            Refresh();
        }
    }

    private void OnStoreChanged()
    {
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.BeginInvoke(new Action(Refresh));
    }

    public void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    /* ------------------------------------------------------------------ */
    /*  Палитра кадров (нейтральная, как в тёмной теме Animate)            */
    /* ------------------------------------------------------------------ */

    private static readonly Brush Backdrop = Frozen("#232323");
    private static readonly Brush RowA = Frozen("#2A2A2A");
    private static readonly Brush RowB = Frozen("#2E2E2E");
    private static readonly Brush RowActive = Frozen("#405060");
    private static readonly Brush FrameBody = Frozen("#585858");
    private static readonly Brush FrameEdge = Frozen("#6E6E6E");
    private static readonly Brush FrameLine = Frozen("#4A4A4A");
    private static readonly Brush TweenBody = Frozen("#7E8FA8");
    private static readonly Brush TweenArrow = Frozen("#E8EDF5");
    private static readonly Brush Dot = Frozen("#F0F0F0");
    private static readonly Brush DotEmpty = Frozen("#B0B0B0");
    private static readonly Brush Ruler = Frozen("#303030");
    private static readonly Brush RulerLine = Frozen("#4A4A4A");
    private static readonly Brush RulerTick = Frozen("#6A6A6A");
    private static readonly Brush RulerText = Frozen("#B5B5B5");
    private static readonly Brush PlayheadBrush = Frozen("#E04B4B");
    private static readonly Brush ColumnHi = Frozen("#1AFFFFFF");
    private static readonly Brush ColumnHiActive = Frozen("#26FFFFFF");
    private static readonly Brush SceneEnd = Frozen("#7A7A7A");
    private static readonly Brush OnionMark = Frozen("#8AB4E8");

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = availableSize.Width;
        double height = availableSize.Height;
        if (Store != null)
        {
            int length = Store.SceneLength + ExtraFrames;
            width = Math.Max(120, length * FrameWidth);
            height = Math.Max(80, RulerHeight + Store.ActiveScene.Layers.Count * RowHeight + 2);
        }
        if (double.IsInfinity(width)) width = 800;
        if (double.IsInfinity(height)) height = 200;
        return new Size(width, height);
    }

    public int FrameAtX(double x) => Math.Max(0, (int)Math.Floor(x / Math.Max(1, FrameWidth)));

    public int LayerAtY(double y) => (int)Math.Floor((y - RulerHeight) / RowHeight);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Store == null) return;

        double width = ActualWidth;
        double height = ActualHeight;
        dc.DrawRectangle(Backdrop, null, new Rect(0, 0, width, height));

        Scene scene = Store.ActiveScene;
        int frameCount = (int)Math.Ceiling(width / Math.Max(1, FrameWidth)) + 1;
        int length = Store.SceneLength;

        // сетка каждые 5 кадров
        var gridPen = new Pen(Frozen("#343434"), 1);
        for (int f = 0; f <= frameCount; f += 5)
        {
            double x = f * FrameWidth;
            dc.DrawLine(gridPen, new Point(Math.Floor(x) + 0.5, RulerHeight), new Point(Math.Floor(x) + 0.5, height));
        }

        // строки слоёв
        for (int i = 0; i < scene.Layers.Count; i++)
        {
            Layer layer = scene.Layers[i];
            double y = RulerHeight + i * RowHeight;
            bool active = layer.Id == Store.ActiveLayerId;
            dc.DrawRectangle(active ? RowActive : (i % 2 == 0 ? RowA : RowB), null, new Rect(0, y, width, RowHeight));
            if (!layer.Visible)
                dc.DrawRectangle(Frozen("#40000000"), null, new Rect(0, y, width, RowHeight));

            foreach (var kf in Anim.Sorted(layer))
            {
                double kx = kf.Index * FrameWidth;
                Keyframe previous = kf.Tween != null ? Anim.Previous(layer, kf.Index) : null;

                if (previous != null)
                {
                    // переход: светлая полоса и стрелка направления
                    double tx = previous.Index * FrameWidth;
                    double tw = Math.Max(FrameWidth, kx - tx);
                    dc.DrawRectangle(TweenBody, null, new Rect(tx + 1, y + 5, tw - 1, RowHeight - 10));
                    double ay = y + RowHeight / 2;
                    var arrowPen = new Pen(TweenArrow, 1.1);
                    dc.DrawLine(arrowPen, new Point(tx + 4, ay), new Point(kx - 6, ay));
                    var arrow = new StreamGeometry();
                    using (StreamGeometryContext ctx = arrow.Open())
                    {
                        ctx.BeginFigure(new Point(kx - 2, ay), true, true);
                        ctx.LineTo(new Point(kx - 8, ay - 3.5), true, false);
                        ctx.LineTo(new Point(kx - 8, ay + 3.5), true, false);
                    }
                    arrow.Freeze();
                    dc.DrawGeometry(TweenArrow, null, arrow);
                }

                // заполненный отрезок кадров
                double spanWidth = Math.Max(FrameWidth - 1, kf.Duration * FrameWidth - 1);
                var body = kf.IsEmpty ? Frozen("#3C3C3C") : FrameBody;
                dc.DrawRectangle(body, null, new Rect(kx + 0.5, y + 4, spanWidth, RowHeight - 8));
                dc.DrawRectangle(FrameEdge, null, new Rect(kx + 0.5, y + 4, spanWidth, 1));

                // разделители отдельных кадров внутри отрезка
                var linePen = new Pen(FrameLine, 1);
                for (int f = kf.Index + 1; f <= kf.EndIndex; f++)
                {
                    double lx = Math.Floor(f * FrameWidth) + 0.5;
                    dc.DrawLine(linePen, new Point(lx, y + 5), new Point(lx, y + RowHeight - 5));
                }

                // точка ключевого кадра
                double cx = kx + 5;
                double cy = y + RowHeight - 6;
                if (kf.IsEmpty)
                    dc.DrawEllipse(null, new Pen(DotEmpty, 1.2), new Point(cx, cy), 2.8, 2.8);
                else
                    dc.DrawEllipse(Dot, null, new Point(cx, cy), 3.0, 3.0);
            }

            // конец отрезка — светлая метка, как в Animate
            var last = LastKeyframe(layer);
            if (last != null)
            {
                double ex = (last.EndIndex + 1) * FrameWidth;
                dc.DrawRectangle(Frozen("#D8D8D8"), null, new Rect(ex - 1.5, y + 4, 1.5, RowHeight - 8));
            }
        }

        // подсветка текущего кадра
        double playheadX = Store.Playhead * FrameWidth;
        dc.DrawRectangle(ColumnHi, null, new Rect(playheadX, RulerHeight, FrameWidth, height - RulerHeight));
        int activeIndex = scene.Layers.FindIndex(l => l.Id == Store.ActiveLayerId);
        if (activeIndex >= 0)
            dc.DrawRectangle(ColumnHiActive, null,
                new Rect(playheadX, RulerHeight + activeIndex * RowHeight, FrameWidth, RowHeight));

        // линейка кадров
        dc.DrawRectangle(Ruler, null, new Rect(0, 0, width, RulerHeight));
        dc.DrawLine(new Pen(Frozen("#1A1A1A"), 1), new Point(0, RulerHeight - 0.5), new Point(width, RulerHeight - 0.5));
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var tickPen = new Pen(RulerTick, 1);
        for (int f = 0; f <= frameCount; f++)
        {
            double x = Math.Floor(f * FrameWidth) + 0.5;
            bool major = f % 5 == 0;
            dc.DrawLine(tickPen, new Point(x, major ? RulerHeight - 8 : RulerHeight - 4), new Point(x, RulerHeight));
            if (!major) continue;
            var label = new FormattedText((f + 1).ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9.5, RulerText, 1.0);
            dc.DrawText(label, new Point(x + 3, 3));
        }

        // маркеры луковой кожи
        if (Store.OnionOn)
        {
            var onionPen = new Pen(OnionMark, 1);
            for (int i = 1; i <= Math.Max(0, Store.OnionBefore); i++)
            {
                int f = Store.Playhead - i;
                if (f < 0) continue;
                dc.DrawLine(onionPen, new Point(f * FrameWidth + 1, 2), new Point(f * FrameWidth + 1, RulerHeight - 3));
            }
            for (int i = 1; i <= Math.Max(0, Store.OnionAfter); i++)
            {
                int f = Store.Playhead + i;
                dc.DrawLine(onionPen, new Point(f * FrameWidth + 1, 2), new Point(f * FrameWidth + 1, RulerHeight - 3));
            }
        }

        // играющая головка
        dc.DrawLine(new Pen(PlayheadBrush, 1.4), new Point(playheadX + 0.5, 0), new Point(playheadX + 0.5, height));
        var marker = new StreamGeometry();
        using (StreamGeometryContext ctx = marker.Open())
        {
            ctx.BeginFigure(new Point(playheadX - 6, 0), true, true);
            ctx.LineTo(new Point(playheadX + 7, 0), true, false);
            ctx.LineTo(new Point(playheadX + 7, 8), true, false);
            ctx.LineTo(new Point(playheadX + 0.5, 13), true, false);
            ctx.LineTo(new Point(playheadX - 6, 8), true, false);
        }
        marker.Freeze();
        dc.DrawGeometry(PlayheadBrush, null, marker);

        // граница сцены (документа)
        dc.DrawLine(new Pen(SceneEnd, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) },
            new Point(Math.Floor(length * FrameWidth) + 0.5, 0), new Point(Math.Floor(length * FrameWidth) + 0.5, height));
    }

    private static Keyframe LastKeyframe(Layer layer)
    {
        Keyframe result = null;
        foreach (Keyframe kf in layer.Frames)
            if (result == null || kf.EndIndex > result.EndIndex) result = kf;
        return result;
    }

    /* ------------------------------------------------------------------ */
    /*  Ввод                                                               */
    /* ------------------------------------------------------------------ */

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (Store == null) return;
        Focus();
        Point pos = e.GetPosition(this);
        int layerIndex = LayerAtY(pos.Y);

        if (e.ChangedButton == MouseButton.Right)
        {
            RightClicked?.Invoke(layerIndex, FrameAtX(pos.X), pos);
            e.Handled = true;
            return;
        }

        if (layerIndex >= 0 && layerIndex < Store.ActiveScene.Layers.Count)
        {
            Store.ActiveLayerId = Store.ActiveScene.Layers[layerIndex].Id;
            LayersChangedByUser?.Invoke();
        }

        _scrubbing = true;
        CaptureMouse();
        Store.SetPlayhead(FrameAtX(pos.X));
        Refresh();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Store == null || !_scrubbing) return;
        Store.SetPlayhead(FrameAtX(e.GetPosition(this).X));
        Refresh();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        _scrubbing = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetFrameWidthInternal(FrameWidth * (e.Delta > 0 ? 1.15 : 1 / 1.15));
        e.Handled = true;
    }

    public void SetFrameWidthInternal(double width)
    {
        FrameWidth = Math.Max(6, Math.Min(48, width));
        FrameWidthChanged?.Invoke();
        Refresh();
    }
}
