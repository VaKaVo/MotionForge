using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MotionForge.Model;

namespace MotionForge.Views;

/// <summary>Общая палитра цветов (используется образцами, панелью «Цвет» и панелью «Образцы»).</summary>
public static class ColorPicker
{
    /// <summary>Разворачивает палитру рядом с элементом и отдаёт выбранный цвет.</summary>
    public static void Open(FrameworkElement anchor, string current, Action<string> onPick,
        PlacementMode placement = PlacementMode.Bottom)
    {
        if (anchor == null || onPick == null) return;
        string normalized = ColorUtil.Normalize(current);
        ColorUtil.Parse(normalized, out byte alpha, out byte startR, out byte startG, out byte startB);

        Popup popup = null;
        bool dragging = false;   // тянем ползунок — цвет отдаём только по отпусканию
        bool syncing = false;    // защита от рекурсии при программной установке значений

        // Цвет храним в HSV: так круг и ползунок яркости не «теряют» оттенок у серых тонов.
        ColorUtil.RgbToHsv(startR, startG, startB, out double hue, out double sat, out double val);
        byte r = startR, g = startG, b = startB;

        var preview = new Border
        {
            Width = 44,
            Height = 26,
            CornerRadius = new CornerRadius(3),
            Background = UiKit.Brush(normalized),
            BorderBrush = (Brush)Application.Current.Resources["Line"],
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 0),
        };
        var custom = new TextBox
        {
            Text = normalized,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var apply = new Button
        {
            Content = UiKit.T("Применить"),
            Style = (Style)Application.Current.Resources["BtnSmall"],
        };

        Slider MakeSlider(byte value) => new Slider
        {
            Minimum = 0,
            Maximum = 255,
            Value = value,
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            Style = (Style)Application.Current.Resources["ColorSlider"],
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        TextBox MakeBox() => new TextBox
        {
            Width = 42,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
        };

        var vSlider = MakeSlider((byte)Math.Round(val * 255));
        var rSlider = MakeSlider(startR);
        var gSlider = MakeSlider(startG);
        var bSlider = MakeSlider(startB);
        var vBox = MakeBox();
        var rBox = MakeBox();
        var gBox = MakeBox();
        var bBox = MakeBox();

        var wheel = new ColorWheel(150)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 9, 0, 4),
        };

        string CurrentHex() => alpha >= 255
            ? ColorUtil.ToHex(r, g, b)
            : ColorUtil.Argb(alpha, r, g, b);

        void Pick() => onPick(CurrentHex());

        // Перерисовывает все элементы по текущим r/g/b и HSV.
        void RefreshVisuals()
        {
            string hex = CurrentHex();
            preview.Background = UiKit.Brush(hex);
            preview.ToolTip = hex;
            if (!custom.IsKeyboardFocusWithin) custom.Text = hex;

            syncing = true;
            vSlider.Value = Math.Round(val * 255);
            rSlider.Value = r;
            gSlider.Value = g;
            bSlider.Value = b;
            syncing = false;

            if (!vBox.IsKeyboardFocusWithin) vBox.Text = ((int)Math.Round(val * 255)).ToString();
            if (!rBox.IsKeyboardFocusWithin) rBox.Text = r.ToString();
            if (!gBox.IsKeyboardFocusWithin) gBox.Text = g.ToString();
            if (!bBox.IsKeyboardFocusWithin) bBox.Text = b.ToString();

            // Дорожки показывают, как меняется цвет вдоль своего канала.
            ColorUtil.HsvToRgb(hue, sat, 1, out byte fr, out byte fg, out byte fb);
            vSlider.Background = Ramp(Colors.Black, Color.FromRgb(fr, fg, fb));
            rSlider.Background = Ramp(Color.FromRgb(0, g, b), Color.FromRgb(255, g, b));
            gSlider.Background = Ramp(Color.FromRgb(r, 0, b), Color.FromRgb(r, 255, b));
            bSlider.Background = Ramp(Color.FromRgb(r, g, 0), Color.FromRgb(r, g, 255));

            wheel.SetHsv(hue, sat);
        }

        // Цвет задан в RGB (ползунки каналов, HEX, образец).
        void ApplyRgb(byte nr, byte ng, byte nb, bool pick)
        {
            r = nr;
            g = ng;
            b = nb;
            ColorUtil.RgbToHsv(r, g, b, out double h, out double s, out double v);
            if (s > 0.0001) hue = h;   // у серого оттенок не меняется
            sat = s;
            val = v;
            RefreshVisuals();
            if (pick) Pick();
        }

        // Цвет задан в HSV (круг, ползунок яркости).
        void ApplyHsv(double h, double s, double v, bool pick)
        {
            hue = h;
            sat = s;
            val = v;
            ColorUtil.HsvToRgb(hue, sat, val, out r, out g, out b);
            RefreshVisuals();
            if (pick) Pick();
        }

        void HookSlider(Slider slider, TextBox box, Action apply)
        {
            slider.ValueChanged += (s, e) =>
            {
                if (syncing) return;
                apply();
            };
            slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((s, e) => dragging = true));
            slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((s, e) =>
            {
                dragging = false;
                apply();
            }));

            void CommitBox()
            {
                if (double.TryParse(box.Text.Trim(), out double value))
                {
                    syncing = true;
                    slider.Value = Math.Max(0, Math.Min(255, Math.Round(value)));
                    syncing = false;
                    dragging = false;
                    apply();
                }
                else
                {
                    box.Text = ((int)Math.Round(slider.Value)).ToString();
                }
            }

            box.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                CommitBox();
                Keyboard.ClearFocus();
                e.Handled = true;
            };
            box.LostFocus += (s, e) => CommitBox();
        }

        HookSlider(vSlider, vBox, () => ApplyHsv(hue, sat, vSlider.Value / 255.0, !dragging));
        HookSlider(rSlider, rBox, () => ApplyRgb(
            (byte)Math.Round(rSlider.Value), (byte)Math.Round(gSlider.Value), (byte)Math.Round(bSlider.Value), !dragging));
        HookSlider(gSlider, gBox, () => ApplyRgb(
            (byte)Math.Round(rSlider.Value), (byte)Math.Round(gSlider.Value), (byte)Math.Round(bSlider.Value), !dragging));
        HookSlider(bSlider, bBox, () => ApplyRgb(
            (byte)Math.Round(rSlider.Value), (byte)Math.Round(gSlider.Value), (byte)Math.Round(bSlider.Value), !dragging));

        wheel.Picked = (h, s) => ApplyHsv(h, s, val < 0.02 ? 1 : val, false);
        wheel.Committed = () => Pick();

        void ApplyHex()
        {
            string text = (custom.Text ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(text, "^#?([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$"))
            {
                custom.Text = CurrentHex();
                return;
            }
            ColorUtil.Parse(text, out byte a2, out byte r2, out byte g2, out byte b2);
            alpha = a2;
            ApplyRgb(r2, g2, b2, true);
        }

        apply.Click += (s, e) =>
        {
            ApplyHex();
            if (popup != null) popup.IsOpen = false;
        };
        custom.KeyDown += (s, e) =>
        {
            if (e.Key != Key.Enter) return;
            ApplyHex();
            if (popup != null) popup.IsOpen = false;
            e.Handled = true;
        };

        var grid = new UniformGrid { Columns = 7 };
        foreach (string hex in ColorUtil.DefaultPalette())
        {
            string captured = hex;
            grid.Children.Add(Swatch(captured, () =>
            {
                ColorUtil.Parse(captured, out byte a3, out byte r3, out byte g3, out byte b3);
                alpha = a3;
                ApplyRgb(r3, g3, b3, true);
                if (popup != null) popup.IsOpen = false;
            }));
        }

        Grid ChannelRow(string name, Slider slider, TextBox box)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Text = name,
                Foreground = (Brush)Application.Current.Resources["TxtDim"],
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
            };
            Grid.SetColumn(label, 0);
            Grid.SetColumn(slider, 1);
            slider.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(box, 2);
            row.Children.Add(label);
            row.Children.Add(slider);
            row.Children.Add(box);
            return row;
        }

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(preview, 0);
        Grid.SetColumn(custom, 1);
        Grid.SetColumn(apply, 2);
        header.Children.Add(preview);
        header.Children.Add(custom);
        header.Children.Add(apply);

        var body = new StackPanel { Margin = new Thickness(10), Width = 240 };
        body.Children.Add(header);
        body.Children.Add(wheel);
        body.Children.Add(ChannelRow("V", vSlider, vBox));
        body.Children.Add(ChannelRow("R", rSlider, rBox));
        body.Children.Add(ChannelRow("G", gSlider, gBox));
        body.Children.Add(ChannelRow("B", bSlider, bBox));
        body.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.Resources["Line"], Margin = new Thickness(0, 8, 0, 7) });
        body.Children.Add(new TextBlock
        {
            Text = UiKit.T("ОБРАЗЦЫ"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
            Margin = new Thickness(0, 0, 0, 5),
        });
        body.Children.Add(grid);

        RefreshVisuals();

        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["Panel"],
            BorderBrush = (Brush)Application.Current.Resources["Line2"],
            BorderThickness = new Thickness(1),
            Child = body,
        };

        popup = new Popup
        {
            Child = border,
            PlacementTarget = anchor,
            Placement = placement,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.None,
        };
        popup.IsOpen = true;
    }

    private static Brush Ramp(Color from, Color to)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
        };
        brush.GradientStops.Add(new GradientStop(from, 0));
        brush.GradientStops.Add(new GradientStop(to, 1));
        brush.Freeze();
        return brush;
    }

    private static Button Swatch(string hex, Action pick)
    {
        var button = new Button
        {
            Width = 22,
            Height = 20,
            Margin = new Thickness(1),
            Background = UiKit.Brush(hex),
            ToolTip = hex.ToUpperInvariant(),
            Style = (Style)Application.Current.Resources["SwatchBtn"],
        };
        button.Click += (s, e) => pick();
        return button;
    }

    /// <summary>Сетка образцов без рамки: используется панелью «Образцы».</summary>
    public static UniformGrid SwatchGrid(int columns, Action<string> pick, double cell = 12)
    {
        var grid = new UniformGrid { Columns = columns, Margin = new Thickness(4) };
        foreach (string hex in WebPalette())
        {
            var button = new Button
            {
                Width = cell,
                Height = cell,
                Margin = new Thickness(0),
                Background = UiKit.Brush(hex),
                BorderThickness = new Thickness(1),
                BorderBrush = UiKit.Res("Line"),
                ToolTip = hex.ToUpperInvariant(),
                Style = (Style)Application.Current.Resources["SwatchBtn"],
            };
            string captured = hex;
            button.Click += (s, e) => pick?.Invoke(captured);
            grid.Children.Add(button);
        }
        return grid;
    }

    /// <summary>Классическая 216-цветная «безопасная» палитра — как в панели «Образцы» у Animate.</summary>
    public static IEnumerable<string> WebPalette()
    {
        int[] steps = { 0, 51, 102, 153, 204, 255 };
        for (int r = 0; r < 6; r++)
            for (int g = 0; g < 6; g++)
                for (int b = 0; b < 6; b++)
                    yield return ColorUtil.ToHex((byte)steps[r], (byte)steps[g], (byte)steps[b]);
    }
}

/// <summary>
/// Цветовой круг: оттенок — угол, насыщенность — радиус, яркость затемняет круг.
/// </summary>
public class ColorWheel : Grid
{
    private static readonly Dictionary<int, BitmapSource> Cache = new Dictionary<int, BitmapSource>();
    private readonly double _size;
    private readonly Grid _marker;
    private bool _drag;

    /// <summary>Живое изменение оттенка и насыщенности (0..360 и 0..1).</summary>
    public Action<double, double> Picked;

    /// <summary>Пользователь отпустил мышь — можно применить цвет.</summary>
    public Action Committed;

    public ColorWheel(double size = 150)
    {
        _size = size;
        Width = size;
        Height = size;
        Background = Brushes.Transparent;
        Cursor = Cursors.Cross;
        ToolTip = UiKit.T("Цветовой круг");

        Children.Add(new Image
        {
            Source = WheelBitmap((int)Math.Round(size)),
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        });

        _marker = new Grid
        {
            Width = 13,
            Height = 13,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        _marker.Children.Add(new Ellipse
        {
            Stroke = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            StrokeThickness = 3.6,
        });
        _marker.Children.Add(new Ellipse
        {
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
        });
        Children.Add(_marker);

        MouseLeftButtonDown += (s, e) =>
        {
            _drag = true;
            CaptureMouse();
            PickAt(e.GetPosition(this));
            e.Handled = true;
        };
        MouseMove += (s, e) =>
        {
            if (!_drag) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndDrag();
                return;
            }
            PickAt(e.GetPosition(this));
        };
        MouseLeftButtonUp += (s, e) =>
        {
            if (!_drag) return;
            PickAt(e.GetPosition(this));
            EndDrag();
            e.Handled = true;
        };
    }

    /// <summary>Ставит маркер по оттенку и насыщенности.</summary>
    public void SetHsv(double hue, double sat)
    {
        double radius = _size / 2 - 2;
        double angle = hue * Math.PI / 180;
        double x = _size / 2 + Math.Cos(angle) * sat * radius;
        double y = _size / 2 + Math.Sin(angle) * sat * radius;
        _marker.Margin = new Thickness(x - 6.5, y - 6.5, 0, 0);
    }

    private void EndDrag()
    {
        _drag = false;
        ReleaseMouseCapture();
        Committed?.Invoke();
    }

    private void PickAt(Point p)
    {
        double half = _size / 2;
        double radius = half - 2;
        double dx = p.X - half;
        double dy = p.Y - half;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double sat = radius <= 0 ? 0 : Math.Min(1, distance / radius);
        double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        Picked?.Invoke(hue, sat);
    }

    /// <summary>Растровая картинка круга при яркости 100% (кэшируется по размеру).</summary>
    private static BitmapSource WheelBitmap(int size)
    {
        if (Cache.TryGetValue(size, out BitmapSource cached)) return cached;

        int side = Math.Max(24, size);
        double radius = side / 2.0;
        var pixels = new byte[side * side * 4];
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                double dx = x + 0.5 - radius;
                double dy = y + 0.5 - radius;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                double edge = radius - 1 - distance;
                if (edge <= 0) continue;

                double sat = Math.Min(1, distance / (radius - 1));
                double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                ColorUtil.HsvToRgb(hue, sat, 1, out byte rr, out byte gg, out byte bb);
                int i = (y * side + x) * 4;
                pixels[i] = bb;
                pixels[i + 1] = gg;
                pixels[i + 2] = rr;
                pixels[i + 3] = (byte)(Math.Min(1, edge) * 255);
            }
        }

        var bitmap = BitmapSource.Create(side, side, 96, 96, PixelFormats.Bgra32, null, pixels, side * 4);
        bitmap.Freeze();
        Cache[side] = bitmap;
        return bitmap;
    }
}

/// <summary>
/// Блок заливки и обводки как в Animate: два перекрывающихся образца,
/// стрелка обмена цветов и значок цветов по умолчанию.
/// </summary>
public class FillStrokeWidget : Canvas
{
    private readonly Border _strokeOuter;
    private readonly Border _strokeInner;
    private readonly Border _fillOuter;
    private readonly Border _fillInner;
    private string _fill = "#000000";
    private string _stroke = "#000000";

    public Action<string> FillChanged;
    public Action<string> StrokeChanged;

    /// <summary>Обменять заливку и обводку местами (кнопка со стрелками).</summary>
    public event Action SwapRequested;
    /// <summary>Вернуть чёрно-белые цвета (значок по умолчанию).</summary>
    public event Action DefaultsRequested;
    /// <summary>Щёлкнули по образцу обводки (обычно нужно переключить активный образец).</summary>
    public event Action StrokeFocused;

    public FillStrokeWidget()
    {
        Width = 60;
        Height = 46;
        Background = Brushes.Transparent;

        // обводка (кольцо)
        _strokeOuter = new Border
        {
            Width = 22,
            Height = 22,
            BorderBrush = (Brush)Application.Current.Resources["Line"],
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = UiKit.T("Цвет обводки"),
        };
        _strokeInner = new Border { Width = 12, Height = 12, Cursor = Cursors.Hand };
        _strokeOuter.Child = _strokeInner;
        SetLeft(_strokeOuter, 0);
        SetTop(_strokeOuter, 0);
        Children.Add(_strokeOuter);

        // заливка (сплошной квадрат)
        _fillOuter = new Border
        {
            Width = 22,
            Height = 22,
            BorderBrush = (Brush)Application.Current.Resources["Line"],
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = UiKit.T("Цвет заливки"),
        };
        _fillInner = new Border { Cursor = Cursors.Hand };
        _fillOuter.Child = _fillInner;
        SetLeft(_fillOuter, 18);
        SetTop(_fillOuter, 18);
        Children.Add(_fillOuter);

        var swap = new Path
        {
            Data = Geometry.Parse("M0 4 H13 M10 1 L13 4 L10 7 M13 13 H0 M3 10 L0 13 L3 16"),
            Stroke = (Brush)Application.Current.Resources["Txt"],
            StrokeThickness = 1.3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Cursor = Cursors.Hand,
            ToolTip = UiKit.T("Обменять заливку и обводку"),
        };
        SetLeft(swap, 42);
        SetTop(swap, 2);
        swap.MouseLeftButtonUp += (s, e) => { SwapRequested?.Invoke(); e.Handled = true; };
        Children.Add(swap);

        var defaults = new Grid { Width = 18, Height = 18, Cursor = Cursors.Hand, ToolTip = UiKit.T("Чёрная обводка, белая заливка") };
        defaults.Children.Add(new Border
        {
            Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Black,
            BorderBrush = (Brush)Application.Current.Resources["Line"], BorderThickness = new Thickness(1),
        });
        defaults.Children.Add(new Border
        {
            Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.White,
            BorderBrush = (Brush)Application.Current.Resources["Line"], BorderThickness = new Thickness(1),
        });
        SetLeft(defaults, 40);
        SetTop(defaults, 26);
        defaults.MouseLeftButtonUp += (s, e) => { DefaultsRequested?.Invoke(); e.Handled = true; };
        Children.Add(defaults);

        _fillOuter.MouseLeftButtonUp += (s, e) =>
        {
            ColorPicker.Open(_fillOuter, _fill, hex =>
            {
                _fill = hex;
                Apply();
                FillChanged?.Invoke(hex);
            }, PlacementMode.Right);
            e.Handled = true;
        };
        _strokeOuter.MouseLeftButtonUp += (s, e) =>
        {
            StrokeFocused?.Invoke();
            ColorPicker.Open(_strokeOuter, _stroke, hex =>
            {
                _stroke = hex;
                Apply();
                StrokeChanged?.Invoke(hex);
            }, PlacementMode.Right);
            e.Handled = true;
        };
    }

    public string Fill
    {
        get => _fill;
        set { _fill = ColorUtil.Normalize(value); Apply(); }
    }

    public string Stroke
    {
        get => _stroke;
        set { _stroke = ColorUtil.Normalize(value); Apply(); }
    }

    public void Swap()
    {
        string tmp = _fill;
        _fill = _stroke;
        _stroke = tmp;
        Apply();
        FillChanged?.Invoke(_fill);
        StrokeChanged?.Invoke(_stroke);
    }

    private void Apply()
    {
        _fillInner.Background = UiKit.Brush(_fill);
        _strokeOuter.Background = UiKit.Brush(_stroke);
        _strokeInner.Background = (Brush)Application.Current.Resources["Panel"];
    }
}
