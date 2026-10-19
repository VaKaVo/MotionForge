using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MotionForge.Model;
using MotionForge.Services;

namespace MotionForge.Views;

/// <summary>Мелкие строительные блоки интерфейса, чтобы панели читались как текст.</summary>
public static class UiKit
{
    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>Короткий псевдоним локализации для панелей и диалогов.</summary>
    public static string T(string source) => L10n.T(source);

    /// <summary>Идёт редактирование текста внутри указанной панели — пересобирать её содержимое нельзя.</summary>
    public static bool IsEditingText(DependencyObject scope)
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (!(focused is TextBox || focused is PasswordBox || focused is ComboBox)) return false;
        if (scope == null) return true;
        while (focused != null)
        {
            if (ReferenceEquals(focused, scope)) return true;
            focused = VisualTreeHelper.GetParent(focused);
        }
        return false;
    }

    public static Brush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorUtil.Normalize(hex)));
        brush.Freeze();
        return brush;
    }

    /* ------------------------------------------------------------------ */
    /*  Заголовки                                                          */
    /* ------------------------------------------------------------------ */

    /// <summary>Заголовок панели-дока: название, кнопка-гамбургер с меню и, если нужно, крестик.</summary>
    public static Border PanelHeader(string title, ContextMenu menu = null, Action onClose = null,
        params UIElement[] extraButtons)
    {
        title = L10n.T(title);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (!string.IsNullOrEmpty(title))
        {
            var caption = new TextBlock
            {
                Text = title,
                Style = (Style)Application.Current.Resources["PanelTitle"],
                Margin = new Thickness(9, 0, 0, 0),
            };
            Grid.SetColumn(caption, 0);
            grid.Children.Add(caption);
        }

        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 3, 0) };
        foreach (UIElement element in extraButtons) right.Children.Add(element);
        if (menu != null)
        {
            var burger = new TextBlock
            {
                Text = UiKit.T("\uE700"),
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = Res("HeaderTxt"),
                Margin = new Thickness(4, 0, 4, 0),
                Cursor = Cursors.Hand,
                ToolTip = UiKit.T("Меню панели"),
            };
            burger.MouseLeftButtonDown += (s, e) =>
            {
                menu.PlacementTarget = burger;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
                e.Handled = true;
            };
            right.Children.Add(burger);
        }
        if (onClose != null)
        {
            var close = new TextBlock
            {
                Text = UiKit.T("\uE711"),
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = Res("HeaderTxt"),
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                ToolTip = UiKit.T("Закрыть панель"),
            };
            close.MouseLeftButtonDown += (s, e) => { onClose(); e.Handled = true; };
            right.Children.Add(close);
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        return new Border
        {
            Style = (Style)Application.Current.Resources["PanelHeader"],
            Child = grid,
        };
    }

    /// <summary>Заголовок раздела внутри панели — как в «Свойствах» у Animate: треугольник и жирный текст.</summary>
    public static Border SectionHeader(string text, bool expandable = false)
    {
        text = L10n.T(text);
        var arrow = new Path
        {
            Data = Geometry.Parse("M0 0 L8 0 L4 4 Z"),
            Fill = expandable ? Res("Accent2") : Res("TxtDim"),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 0, 9, 0) };
        row.Children.Add(arrow);
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = Res("Txt"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var border = new Border
        {
            Background = Res("Panel2"),
            BorderBrush = Res("Line"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(0, 4, 0, 4),
            Child = row,
        };
        return border;
    }

    public static TextBlock Caption(string text)
    {
        text = L10n.T(text);
        return new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("TxtDim"),
            Margin = new Thickness(0, 0, 0, 4),
        };
    }

    public static TextBlock Hint(string text, double width = double.NaN)
    {
        text = L10n.T(text);
        var block = new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            Foreground = Res("TxtDim"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        };
        if (!double.IsNaN(width)) block.Width = width;
        return block;
    }

    public static TextBlock Label(string text, double width = 78)
    {
        text = L10n.T(text);
        return new TextBlock
        {
            Text = text,
            Width = width,
            FontSize = 11,
            Foreground = Res("TxtDim"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
    }

    public static Border FieldLabel(string text)
    {
        text = L10n.T(text);
        return new Border
        {
            Background = Res("Panel2"),
            BorderBrush = Res("Line"),
            BorderThickness = new Thickness(0, 0, 0, 0),
            Padding = new Thickness(5, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = Res("TxtDim"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
    }

    public static StackPanel Row(params UIElement[] elements)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 5),
        };
        foreach (var e in elements) panel.Children.Add(e);
        return panel;
    }

    /// <summary>Полоса «подпись — поле» в стиле панели «Свойства».</summary>
    public static Grid PropRow(string label, double labelWidth, params UIElement[] fields)
    {
        label = L10n.T(label);
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var caption = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = Res("TxtDim"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(caption, 0);
        grid.Children.Add(caption);

        var host = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var field in fields)
        {
            if (field is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 4, 0);
            host.Children.Add(field);
        }
        Grid.SetColumn(host, 1);
        grid.Children.Add(host);
        return grid;
    }

    /* ------------------------------------------------------------------ */
    /*  Поля ввода                                                         */
    /* ------------------------------------------------------------------ */

    public static TextBox Text(string value, Action<string> onCommit, double width = double.NaN)
    {
        var box = new TextBox { Text = value ?? "", MinWidth = 40 };
        if (!double.IsNaN(width)) box.Width = width;
        void Commit() => onCommit?.Invoke(box.Text);
        box.LostFocus += (s, e) => Commit();
        box.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); Keyboard.ClearFocus(); e.Handled = true; }
        };
        return box;
    }

    /// <summary>Числовое поле: ввод с клавиатуры + протяжка мышью (как в Animate).</summary>
    public static TextBox Number(double value, Action<double> onCommit, double step = 1, double width = 62,
        double min = -100000, double max = 100000, string suffix = "")
    {
        var box = new TextBox
        {
            Text = Num.F(value, 2) + suffix,
            Width = width,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            FontFamily = new FontFamily("Consolas"),
            Cursor = Cursors.SizeWE,
        };
        box.Tag = new double[] { value };

        bool dragging = false;
        double startX = 0;
        double startValue = value;

        void Apply(double v)
        {
            v = Math.Max(min, Math.Min(max, v));
            box.Text = Num.F(v, 2) + suffix;
            onCommit?.Invoke(v);
        }

        box.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (box.IsKeyboardFocusWithin) return;
            dragging = true;
            startX = e.GetPosition(box).X;
            startValue = value;
            box.CaptureMouse();
            e.Handled = true;
            box.Focus();
        };
        box.PreviewMouseMove += (s, e) =>
        {
            if (!dragging) return;
            double dx = e.GetPosition(box).X - startX;
            var mods = Keyboard.Modifiers;
            double k = step;
            if ((mods & ModifierKeys.Shift) != 0) k = step * 10;
            if ((mods & ModifierKeys.Alt) != 0) k = step * 0.1;
            Apply(startValue + dx * k);
        };
        box.PreviewMouseLeftButtonUp += (s, e) =>
        {
            if (!dragging) return;
            dragging = false;
            box.ReleaseMouseCapture();
            e.Handled = true;
        };
        box.LostFocus += (s, e) =>
        {
            if (Num.TryParse(Strip(box.Text, suffix), out double parsed)) { value = parsed; Apply(parsed); }
            else Apply(startValue);
        };
        box.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                if (Num.TryParse(Strip(box.Text, suffix), out double parsed)) { value = parsed; Apply(parsed); }
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Up) { value += step * ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1); Apply(value); e.Handled = true; }
            else if (e.Key == Key.Down) { value -= step * ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1); Apply(value); e.Handled = true; }
        };
        return box;
    }

    private static string Strip(string text, string suffix)
    {
        if (string.IsNullOrEmpty(text)) return "0";
        if (!string.IsNullOrEmpty(suffix) && text.EndsWith(suffix)) text = text.Substring(0, text.Length - suffix.Length);
        return text.Replace("°", "").Replace("%", "").Trim();
    }

    public static void SetNumber(TextBox box, double value, string suffix = "")
    {
        box.Text = Num.F(value, 2) + suffix;
    }

    public static CheckBox Check(string label, bool value, Action<bool> onChange, double marginRight = 12)
    {
        label = L10n.T(label);
        var check = new CheckBox
        {
            Content = label,
            IsChecked = value,
            Margin = new Thickness(0, 0, marginRight, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        check.Checked += (s, e) => onChange?.Invoke(true);
        check.Unchecked += (s, e) => onChange?.Invoke(false);
        return check;
    }

    public static ComboBox Combo(IEnumerable<(string value, string label)> items, string current, Action<string> onChange,
        double width = 120)
    {
        var combo = new ComboBox { Width = width, VerticalAlignment = VerticalAlignment.Center };
        string selectedValue = current;
        foreach (var (value, label) in items)
        {
            var item = new ComboBoxItem { Content = L10n.T(label), Tag = value };
            combo.Items.Add(item);
            if (value == current) combo.SelectedItem = item;
        }
        if (combo.SelectedItem == null && combo.Items.Count > 0) combo.SelectedIndex = 0;
        combo.SelectionChanged += (s, e) =>
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is string value && value != selectedValue)
            {
                selectedValue = value;
                onChange?.Invoke(value);
            }
        };
        return combo;
    }

    /// <summary>Выбирает в комбобоксе пункт с заданным значением (без вызова onCommit).</summary>
    public static void SetCombo(ComboBox combo, string value)
    {
        if (combo == null) return;
        foreach (object entry in combo.Items)
            if (entry is ComboBoxItem item && item.Tag is string tag && tag == value)
            {
                combo.SelectedItem = item;
                return;
            }
    }

    public static Button SmallButton(string text, string tooltip, Action onClick)
    {
        text = L10n.T(text);
        tooltip = L10n.T(tooltip);
        var button = new Button
        {
            Content = text,
            ToolTip = tooltip,
            Style = (Style)Application.Current.Resources["BtnSmall"],
            MinWidth = 26,
            Margin = new Thickness(0, 0, 4, 0),
        };
        button.Click += (s, e) => onClick?.Invoke();
        return button;
    }

    public static Button IconButton(string iconId, string tooltip, Action onClick, double size = 26)
    {
        tooltip = L10n.T(tooltip);
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["IconBtn"],
            ToolTip = tooltip,
            Width = size,
            Height = size,
            Padding = new Thickness(0),
        };
        var path = Services.Icons.CreatePath(iconId, Res("Txt"), 1.5);
        path.Width = size - 8;
        path.Height = size - 8;
        button.Content = path;
        button.Click += (s, e) => onClick?.Invoke();
        return button;
    }

    /// <summary>Кнопка-иконка из шрифта Segoe MDL2 (плоская, без рамки).</summary>
    public static Button GlyphButton(string glyph, string tooltip, Action onClick, double size = 22)
    {
        tooltip = L10n.T(tooltip);
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["IconBtn"],
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = size * 0.45,
                Foreground = Res("Txt"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            ToolTip = tooltip,
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 1, 0),
        };
        button.Click += (s, e) => onClick?.Invoke();
        return button;
    }

    public static MenuItem Menu(string header, Action action)
    {
        header = L10n.T(header);
        var item = new MenuItem { Header = header };
        item.Click += (s, e) => action?.Invoke();
        return item;
    }
}

/// <summary>Кнопка-образец цвета: открывает палитру и ввод HEX.</summary>
public class ColorButton : Button
{
    private readonly Border _swatch;
    private string _color;

    public Action<string> ColorChanged;

    public string Color
    {
        get => _color;
        set
        {
            _color = ColorUtil.Normalize(value);
            _swatch.Background = UiKit.Brush(_color);
        }
    }

    public ColorButton(string color, double width = 34)
    {
        _swatch = new Border
        {
            Background = UiKit.Brush(ColorUtil.Normalize(color)),
            BorderBrush = (Brush)Application.Current.Resources["Line"],
            BorderThickness = new Thickness(1),
            Width = width,
            Height = 20,
        };
        _color = ColorUtil.Normalize(color);
        Style = (Style)Application.Current.Resources["Btn"];
        Padding = new Thickness(2);
        Content = _swatch;
        ToolTip = UiKit.T("Цвет: нажмите, чтобы выбрать");
        Click += (s, e) => ColorPicker.Open(this, _color, hex =>
        {
            Color = hex;
            ColorChanged?.Invoke(hex);
        });
    }
}
