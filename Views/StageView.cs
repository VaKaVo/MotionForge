using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MotionForge.Model;
using MotionForge.Services;
using MotionForge.Tools;
using RenderOptions = MotionForge.Services.RenderOptions;

namespace MotionForge.Views;

/// <summary>Рабочая область сцены: отрисовка кадров, сетка, «луковая кожа», выделение и ввод инструментов.</summary>
public class StageView : Canvas
{
    public Store Store;

    private readonly ToolContext _ctx = new ToolContext();
    private bool _panning;
    private Point _panStartScreen;
    private double _panStartX, _panStartY;
    private Point _mouseScreen;
    private bool _hasMouse;
    private bool _spaceDown;
    private TextBox _textEditor;
    private TextElement _editingText;

    public event Action<Vec, Point> WorldMouseMoved;
    public event Action<Vec, Point> RightClicked;

    public StageView()
    {
        Focusable = true;
        ClipToBounds = true;
        Background = new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x18));
        SnapsToDevicePixels = false;
        ToolRegistry.Ensure();
    }

    public Tool CurrentTool => ToolRegistry.Get(Store == null ? "select" : Store.ToolId);

    public double Px => 1.0 / Math.Max(0.02, Store?.Zoom ?? 1);

    public Vec ToWorld(Point screen)
    {
        double zoom = Store?.Zoom ?? 1;
        return new Vec((screen.X - Store.PanX) / zoom, (screen.Y - Store.PanY) / zoom);
    }

    public Point ToScreen(Vec world)
    {
        double zoom = Store?.Zoom ?? 1;
        return new Point(world.X * zoom + Store.PanX, world.Y * zoom + Store.PanY);
    }

    public Point ViewportCenterScreen => new Point(ActualWidth / 2, ActualHeight / 2);

    public void FitStage()
    {
        if (Store == null || ActualWidth < 40 || ActualHeight < 40) return;
        StageInfo stage = Store.Project.Stage;
        double margin = 70;
        double zoom = Math.Min((ActualWidth - margin * 2) / Math.Max(1, stage.Width),
                               (ActualHeight - margin * 2) / Math.Max(1, stage.Height));
        zoom = Math.Max(0.05, Math.Min(8, zoom));
        Store.Zoom = zoom;
        Store.PanX = (ActualWidth - stage.Width * zoom) / 2;
        Store.PanY = (ActualHeight - stage.Height * zoom) / 2;
        InvalidateVisual();
    }

    public void ZoomAt(double factor, Point anchorScreen)
    {
        Store.SetZoom(Store.Zoom * factor, anchorScreen.X, anchorScreen.Y);
        InvalidateVisual();
    }

    /* ------------------------------------------------------------------ */
    /*  Отрисовка                                                          */
    /* ------------------------------------------------------------------ */

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Store == null) return;

        dc.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var stage = Store.Project.Stage;
        Point topLeft = ToScreen(Vec.Zero);
        double zoom = Store.Zoom;
        var stageRect = new Rect(topLeft.X, topLeft.Y, stage.Width * zoom, stage.Height * zoom);

        // тень сцены
        var shadow = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
        dc.DrawRectangle(shadow, null, new Rect(stageRect.X + 6, stageRect.Y + 8, stageRect.Width, stageRect.Height));

        // фон сцены
        var stageBrush = Renderer.GetBrush(stage.Background, 1);
        dc.DrawRectangle(stageBrush, new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x42, 0x60)), 1), stageRect);

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        dc.PushTransform(new TranslateTransform(Store.PanX, Store.PanY));
        dc.PushTransform(new ScaleTransform(zoom, zoom));

        var options = new RenderOptions
        {
            Project = Store.Project,
            PixelsPerDip = pixelsPerDip,
            OutlineAll = Store.OutlineAll,
            CurrentFrame = Store.Playhead,
        };

        Scene scene = Store.ActiveScene;

        // «Луковая кожа»: соседние кадры полупрозрачно
        if (Store.OnionOn)
        {
            for (int i = Store.OnionBefore; i >= 1; i--)
            {
                int frame = Store.Playhead - i;
                if (frame < 0) continue;
                var ghost = new RenderOptions
                {
                    Project = Store.Project,
                    PixelsPerDip = pixelsPerDip,
                    Alpha = 0.30,
                    CurrentFrame = frame,
                };
                Renderer.DrawScene(dc, Store.Project, scene, frame, ghost);
            }
            for (int i = 1; i <= Store.OnionAfter; i++)
            {
                int frame = Store.Playhead + i;
                if (frame >= Store.SceneLength) continue;
                var ghost = new RenderOptions
                {
                    Project = Store.Project,
                    PixelsPerDip = pixelsPerDip,
                    Alpha = 0.22,
                    CurrentFrame = frame,
                };
                Renderer.DrawScene(dc, Store.Project, scene, frame, ghost);
            }
        }

        // сетка
        if (Store.GridOn) DrawGrid(dc, stage);

        // содержимое кадра
        Renderer.DrawScene(dc, Store.Project, scene, Store.Playhead, options);

        // границы символа при редактировании
        if (Store.ActiveSymbol != null)
        {
            RectD bounds = Store.ActiveSymbol.Bounds;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 0x22, 0xD3, 0xEE)), Px * 1) 
            { DashStyle = new DashStyle(new double[] { 5, 4 }, 0) };
            dc.DrawRectangle(null, pen, new Rect(bounds.X, bounds.Y, Math.Max(1, bounds.W), Math.Max(1, bounds.H)));
        }

        // оверлей инструмента
        _ctx.Store = Store;
        _ctx.Px = Px;
        _ctx.Invalidate = InvalidateVisual;
        try { CurrentTool.DrawOverlay(dc, _ctx); } catch { /* оверлей не должен ломать кадр */ }

        // курсор инструмента (ластик/кисть)
        if (_hasMouse && !_panning)
        {
            Vec world = ToWorld(_mouseScreen);
            double radius = 0;
            if (Store.ToolId == "eraser") radius = 14 * Px;
            else if (Store.ToolId == "brush") radius = Math.Max(2, Store.Opts.BrushSize / 2);
            if (radius > 0)
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(180, 0xE6, 0xE9, 0xF2)), Px), new Point(world.X, world.Y), radius, radius);
        }

        dc.Pop();
        dc.Pop();
    }

    private void DrawGrid(DrawingContext dc, StageInfo stage)
    {
        var brush = new SolidColorBrush(Color.FromArgb(40, 0x7C, 0x5C, 0xFF));
        var pen = new Pen(brush, Px);
        double step = Store.GridSize;
        for (double x = 0; x <= stage.Width; x += step)
            dc.DrawLine(pen, new Point(x, 0), new Point(x, stage.Height));
        for (double y = 0; y <= stage.Height; y += step)
            dc.DrawLine(pen, new Point(0, y), new Point(stage.Width, y));
    }

    /* ------------------------------------------------------------------ */
    /*  Ввод                                                               */
    /* ------------------------------------------------------------------ */

    private void SyncContext(ModifierKeys mods)
    {
        _ctx.Store = Store;
        _ctx.Px = Px;
        _ctx.Mods = mods;
        _ctx.Invalidate = InvalidateVisual;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (Store == null) return;
        if (_textEditor != null && e.OriginalSource is TextBox) return;

        Focus();
        SyncContext(Keyboard.Modifiers);
        _mouseScreen = e.GetPosition(this);
        Vec world = ToWorld(_mouseScreen);

        if (_spaceDown || e.ChangedButton == MouseButton.Middle)
        {
            BeginPan(_mouseScreen);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            RightClicked?.Invoke(world, _mouseScreen);
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            CommitTextEdit();
            CurrentTool.DoubleClick(_ctx, world);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        CommitTextEdit();
        CaptureMouse();
        CurrentTool.Down(_ctx, world, false);
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Store == null) return;
        _mouseScreen = e.GetPosition(this);
        _hasMouse = true;
        Vec world = ToWorld(_mouseScreen);
        WorldMouseMoved?.Invoke(world, _mouseScreen);

        if (_panning)
        {
            double dx = _mouseScreen.X - _panStartScreen.X;
            double dy = _mouseScreen.Y - _panStartScreen.Y;
            Store.SetPan(_panStartX + dx, _panStartY + dy);
            InvalidateVisual();
            return;
        }

        SyncContext(Keyboard.Modifiers);
        CurrentTool.Move(_ctx, world);
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (Store == null) return;

        if (_panning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left))
        {
            EndPan();
            return;
        }

        ReleaseMouseCapture();
        SyncContext(Keyboard.Modifiers);
        CurrentTool.Up(_ctx, ToWorld(e.GetPosition(this)));
        InvalidateVisual();
    }

    private void BeginPan(Point screen)
    {
        _panning = true;
        _panStartScreen = screen;
        _panStartX = Store.PanX;
        _panStartY = Store.PanY;
        Cursor = Cursors.SizeAll;
        CaptureMouse();
    }

    private void EndPan()
    {
        _panning = false;
        Cursor = _spaceDown ? Cursors.SizeAll : null;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Store == null) return;
        Point pos = e.GetPosition(this);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ZoomAt(e.Delta > 0 ? 1.15 : 1 / 1.15, pos);
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            Store.SetPan(Store.PanX + e.Delta * 0.8, Store.PanY);
        }
        else
        {
            Store.SetPan(Store.PanX, Store.PanY + e.Delta * 0.8);
        }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hasMouse = false;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Store == null) return;
        SyncContext(Keyboard.Modifiers);

        if (e.Key == Key.Space)
        {
            _spaceDown = true;
            Cursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CurrentTool.Cancel(_ctx);
            CurrentTool.OnKey(_ctx, Key.Escape);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (CurrentTool.OnKey(_ctx, e.Key))
        {
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        // Точное перемещение стрелками
        if (Store.Selection.Count > 0)
        {
            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            double dx = 0, dy = 0;
            switch (e.Key)
            {
                case Key.Left: dx = -step; break;
                case Key.Right: dx = step; break;
                case Key.Up: dy = -step; break;
                case Key.Down: dy = step; break;
            }
            if (dx != 0 || dy != 0)
            {
                Store.MoveSelection(dx, dy);
                InvalidateVisual();
                e.Handled = true;
            }
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
        {
            _spaceDown = false;
            if (!_panning) Cursor = null;
        }
    }

    /* ------------------------------------------------------------------ */
    /*  Правка текста прямо на сцене                                       */
    /* ------------------------------------------------------------------ */

    public void BeginTextEdit(TextElement element)
    {
        CommitTextEdit();
        if (element == null || Store == null) return;
        _editingText = element;

        double boxWidth = 420;
        Point screen = ToScreen(new Vec(element.X, element.Y));
        var editor = new TextBox
        {
            Text = element.Text,
            Width = boxWidth,
            AcceptsReturn = true,
            FontSize = Math.Max(11, Math.Min(42, element.FontSize * Math.Min(2, Store.Zoom))),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(230, 0x19, 0x1D, 0x29)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x5C, 0xFF)),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(4, 2, 4, 2),
            TextWrapping = TextWrapping.NoWrap,
        };
        editor.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CommitTextEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                CommitTextEdit();
                e.Handled = true;
            }
        };
        editor.LostFocus += (s, e) => CommitTextEdit();

        SetLeft(editor, Math.Max(4, screen.X - 10));
        SetTop(editor, Math.Max(4, screen.Y - 6));
        Children.Add(editor);
        _textEditor = editor;
        editor.Focus();
        editor.SelectAll();
    }

    public void CommitTextEdit()
    {
        if (_textEditor == null) return;
        var editor = _textEditor;
        _textEditor = null;
        if (_editingText != null)
        {
            string text = editor.Text ?? "";
            if (text != _editingText.Text)
            {
                _editingText.Text = text;
                Store.Commit("Текст");
                Store.RaiseAll();
            }
        }
        _editingText = null;
        Children.Remove(editor);
        Focus();
        InvalidateVisual();
    }

    public bool IsEditingText => _textEditor != null;
}