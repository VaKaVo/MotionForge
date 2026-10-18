using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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

        var grid = new UniformGrid { Columns = 7 };
        Popup popup = null;

        foreach (string hex in ColorUtil.DefaultPalette())
        {
            grid.Children.Add(Swatch(hex, () =>
            {
                onPick(hex);
                if (popup != null) popup.IsOpen = false;
            }));
        }

        var custom = new TextBox { Text = normalized, Width = 92, Margin = new Thickness(0, 0, 4, 0) };
        var apply = new Button { Content = "Применить", Style = (Style)Application.Current.Resources["BtnSmall"] };
        apply.Click += (s, e) =>
        {
            onPick(ColorUtil.Normalize(custom.Text));
            if (popup != null) popup.IsOpen = false;
        };
        custom.KeyDown += (s, e) =>
        {
            if (e.Key != Key.Enter) return;
            onPick(ColorUtil.Normalize(custom.Text));
            if (popup != null) popup.IsOpen = false;
            e.Handled = true;
        };

        var body = new StackPanel { Margin = new Thickness(8) };
        body.Children.Add(new TextBlock
        {
            Text = "ОБРАЗЦЫ",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
            Margin = new Thickness(0, 0, 0, 5),
        });
        body.Children.Add(grid);
        body.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.Resources["Line"], Margin = new Thickness(0, 7, 0, 7) });
        body.Children.Add(UiKit.Row(UiKit.Label("HEX", 30), custom, apply));

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
            ToolTip = "Цвет обводки",
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
            ToolTip = "Цвет заливки",
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
            ToolTip = "Обменять заливку и обводку",
        };
        SetLeft(swap, 42);
        SetTop(swap, 2);
        swap.MouseLeftButtonUp += (s, e) => { SwapRequested?.Invoke(); e.Handled = true; };
        Children.Add(swap);

        var defaults = new Grid { Width = 18, Height = 18, Cursor = Cursors.Hand, ToolTip = "Чёрная обводка, белая заливка" };
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
