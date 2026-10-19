using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MotionForge.Model;
using MotionForge.Services;
using MotionForge.Tools;
using RenderOptions = MotionForge.Services.RenderOptions;

namespace MotionForge.Views;

/* ===================================================================== */
/*  Общие операции с цветом выделения                                     */
/* ===================================================================== */

public static class PaintOps
{
    /// <summary>Заливка: сразу применяется к выделенным фигурам и тексту, иначе — к параметрам инструмента.</summary>
    public static void SetFill(Store store, string hex)
    {
        store.Opts.Fill = hex;
        var selected = store.SelectionElements();
        if (selected.Count == 0)
        {
            store.Report("Цвет заливки: " + hex);
            return;
        }
        foreach (var e in selected)
        {
            if (e is ShapeElement shape) { shape.Fill = hex; shape.Filled = true; }
            else if (e is TextElement text) text.Color = hex;
        }
        store.Commit("Цвет заливки");
        store.RaiseAll();
    }

    public static void SetStroke(Store store, string hex)
    {
        store.Opts.Stroke = hex;
        var selected = store.SelectionElements();
        if (selected.Count == 0) return;
        foreach (var e in selected)
            if (e is ShapeElement shape) { shape.Stroke = hex; shape.Stroked = true; }
        store.Commit("Цвет обводки");
        store.RaiseAll();
    }

    public static void SetStrokeWidth(Store store, double width)
    {
        store.Opts.StrokeWidth = width;
        var selected = store.SelectionElements();
        if (selected.Count == 0) return;
        foreach (var e in selected)
            if (e is ShapeElement shape) shape.StrokeWidth = width;
        store.Commit("Толщина обводки");
        store.RaiseAll();
    }
}

/* ===================================================================== */
/*  Панель «Инструменты»                                                  */
/* ===================================================================== */

public class ToolsPanel : StackPanel
{
    public Store Store;
    public readonly FillStrokeWidget Colors = new FillStrokeWidget();

    private readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>();
    private readonly List<(Button button, Func<bool> get)> _viewToggles = new List<(Button, Func<bool>)>();

    public ToolsPanel()
    {
        Orientation = Orientation.Vertical;

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(5, 6, 5, 4) };
        foreach (Cmd command in Commands.All.Where(c => c.Group == "Инструменты"))
        {
            string iconId = command.Id.StartsWith("tool.") ? command.Id.Substring(5) : command.Id;
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["ToolBtn"],
                Width = 28,
                Height = 28,
                Tag = "",
                ToolTip = command.Title + (string.IsNullOrEmpty(command.Keys) ? "" : "   (" + command.Keys + ")"),
            };
            var path = Icons.CreatePath(iconId, UiKit.Res("Txt"), 1.5);
            path.Width = 17;
            path.Height = 17;
            button.Content = path;
            Cmd captured = command;
            button.Click += (s, e) => { RunCommand(captured); };
            grid.Children.Add(button);
            _buttons[command.Id] = button;
        }
        Children.Add(grid);
        Children.Add(new Border { Height = 1, Background = UiKit.Res("Line"), Margin = new Thickness(0, 2, 0, 4) });

        // образцы заливки и обводки
        var colorBox = new Border
        {
            Background = UiKit.Res("Bg2"),
            BorderBrush = UiKit.Res("Line"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(4, 5, 4, 5),
            Child = Colors,
        };
        Children.Add(colorBox);
        Colors.Width = 60;
        Colors.Height = 46;
        Colors.FillChanged = hex => PaintOps.SetFill(Store, hex);
        Colors.StrokeChanged = hex => PaintOps.SetStroke(Store, hex);
        Colors.SwapRequested += () =>
        {
            Colors.Swap();
            Store.Opts.Fill = Colors.Fill;
            Store.Opts.Stroke = Colors.Stroke;
        };
        Colors.DefaultsRequested += () =>
        {
            Colors.Fill = "#FFFFFF";
            Colors.Stroke = "#000000";
            Store.Opts.Fill = Colors.Fill;
            Store.Opts.Stroke = Colors.Stroke;
        };

        // переключатели вида — маленькие кнопки, как блок «Параметры» в панели инструментов Animate
        var options = new StackPanel { Margin = new Thickness(5, 6, 5, 8) };
        options.Children.Add(new TextBlock
        {
            Text = UiKit.T("ВИД"),
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = UiKit.Res("TxtDim"),
            Margin = new Thickness(2, 0, 0, 4),
        });
        var toggles = new UniformGrid { Columns = 2 };
        toggles.Children.Add(ViewToggle("grid", "Показать сетку (Ctrl+')",
            () => Store != null && Store.GridOn, v => Store.GridOn = v));
        toggles.Children.Add(ViewToggle("outline", "Показать только контуры (Ctrl+Shift+O)",
            () => Store != null && Store.OutlineAll, v => Store.OutlineAll = v));
        toggles.Children.Add(ViewToggle("onion", "Соседние кадры полупрозрачно (Alt+O)",
            () => Store != null && Store.OnionOn, v => Store.OnionOn = v));
        options.Children.Add(toggles);
        Children.Add(options);
    }

    private Button ViewToggle(string iconId, string tooltip, Func<bool> get, Action<bool> set)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["ToolBtn"],
            Width = 28,
            Height = 26,
            Margin = new Thickness(1),
            ToolTip = tooltip,
            Content = Icons.CreatePath(iconId, UiKit.Res("Txt"), 1.4),
        };
        button.Click += (s, e) => { set(!get()); Refresh(); };
        _viewToggles.Add((button, get));
        return button;
    }

    public void Refresh()
    {
        if (Store == null) return;
        foreach (var pair in _buttons)
            pair.Value.Tag = pair.Key == "tool." + Store.ToolId ? "on" : "";

        Colors.Fill = Store.Opts.Fill;
        Colors.Stroke = Store.Opts.Stroke;

        foreach (var (button, get) in _viewToggles) button.Tag = get() ? "on" : "";
    }

    private void RunCommand(Cmd command)
    {
        if (command?.Run == null) return;
        var shell = Window.GetWindow(this) as IAppShell;
        if (shell != null && command.IsEnabled(shell)) command.Run(shell);
        Refresh();
    }
}

/* ===================================================================== */
/*  Панель «Свойства»: параметры инструмента, сцены и объекта             */
/* ===================================================================== */

public class PropertiesPanel : StackPanel
{
    public Store Store;

    public PropertiesPanel()
    {
        Orientation = Orientation.Vertical;
    }

    public void Refresh()
    {
        if (UiKit.IsEditingText(this)) return;
        Children.Clear();
        if (Store == null) return;

        Children.Add(UiKit.SectionHeader("ИНСТРУМЕНТ"));
        Children.Add(Pad(ToolSection()));

        Children.Add(UiKit.SectionHeader("СЦЕНА"));
        Children.Add(Pad(StageSection()));

        var selected = Store.SelectionElements();
        if (selected.Count == 0)
        {
            Children.Add(UiKit.SectionHeader("КАДР И СЛОЙ"));
            Children.Add(Pad(FrameLayerSection()));
            return;
        }

        Children.Add(UiKit.SectionHeader("ПОЛОЖЕНИЕ И РАЗМЕР"));
        Children.Add(Pad(TransformSection(selected)));

        if (selected.Count == 1)
        {
            Children.Add(UiKit.SectionHeader("ОБЪЕКТ"));
            Children.Add(Pad(ObjectSection(selected[0])));
        }
        else
        {
            Children.Add(UiKit.SectionHeader("ВЫРАВНИВАНИЕ"));
            Children.Add(Pad(MultiSection()));
        }

        Keyframe keyframe = Store.ExactKeyframeAt(Store.Playhead);
        if (keyframe != null && keyframe.Tween != null && keyframe.Tween.Type != "none")
        {
            Children.Add(UiKit.SectionHeader("ПЕРЕХОД"));
            Children.Add(Pad(TweenSection(keyframe)));
        }
    }

    private static UIElement Pad(UIElement child, double top = 7, double bottom = 8)
    {
        return new Border { Padding = new Thickness(9, top, 9, bottom), Child = child };
    }

    /// <summary>Первое предложение подсказки — чтобы панель не разрасталась.</summary>
    private static string FirstSentence(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        int index = text.IndexOf('.');
        return index > 0 ? text.Substring(0, index + 1) : text;
    }

    /* ------------------------------ инструмент ------------------------ */

    private UIElement ToolSection()
    {
        var tool = ToolRegistry.Get(Store.ToolId);
        var opts = Store.Opts;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = UiKit.T(tool.Title).ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = UiKit.Res("Accent2"),
            Margin = new Thickness(0, 0, 0, 5),
        });
        if (!string.IsNullOrEmpty(tool.Hint)) panel.Children.Add(UiKit.Hint(FirstSentence(tool.Hint)));

        switch (Store.ToolId)
        {
            case "rect":
            case "ellipse":
            case "pen":
                panel.Children.Add(UiKit.PropRow("Заливка", 74, FillButton()));
                panel.Children.Add(UiKit.PropRow("Обводка", 74, StrokeButton()));
                panel.Children.Add(UiKit.PropRow("Толщина", 74,
                    UiKit.Number(opts.StrokeWidth, v => PaintOps.SetStrokeWidth(Store, v), 0.5, 56, 0, 200)));
                panel.Children.Add(UiKit.Row(
                    UiKit.Check("Заливка", opts.Filled, v => { opts.Filled = v; ApplySelection(e => { if (e is ShapeElement s) s.Filled = v; }, "Заливка объектов", true); }),
                    UiKit.Check("Обводка", opts.Stroked, v => { opts.Stroked = v; ApplySelection(e => { if (e is ShapeElement s) s.Stroked = v; }, "Обводка объектов", true); })));
                panel.Children.Add(UiKit.Row(
                    UiKit.Check("Сглаживание", opts.Smooth, v => { opts.Smooth = v; ApplySelection(e => { if (e is ShapeElement s) s.Smooth = v; }, "Сглаживание", true); })));
                break;

            case "line":
            case "pencil":
                panel.Children.Add(UiKit.PropRow("Цвет", 74, StrokeButton()));
                panel.Children.Add(UiKit.PropRow("Толщина", 74,
                    UiKit.Number(opts.StrokeWidth, v => PaintOps.SetStrokeWidth(Store, v), 0.5, 56, 0, 200)));
                panel.Children.Add(UiKit.Row(
                    UiKit.Check("Сглаживание", opts.Smooth, v => { opts.Smooth = v; ApplySelection(e => { if (e is ShapeElement s) s.Smooth = v; }, "Сглаживание", true); })));
                break;

            case "brush":
                panel.Children.Add(UiKit.PropRow("Цвет", 74, FillButton()));
                panel.Children.Add(UiKit.PropRow("Размер", 74, UiKit.Number(opts.BrushSize, v => opts.BrushSize = v, 1, 56, 1, 200)));
                break;

            case "text":
                panel.Children.Add(UiKit.PropRow("Шрифт", 74, UiKit.Combo(new[]
                {
                    ("Segoe UI", "Segoe UI"), ("Arial", "Arial"), ("Georgia", "Georgia"),
                    ("Consolas", "Consolas"), ("Times New Roman", "Times New Roman"), ("Verdana", "Verdana"),
                }, opts.FontFamily, v => { opts.FontFamily = v; ApplyText(t => t.FontFamily = v, "Шрифт"); }, 150)));
                panel.Children.Add(UiKit.PropRow("Размер", 74,
                    UiKit.Number(opts.FontSize, v => { opts.FontSize = v; ApplyText(t => t.FontSize = v, "Размер текста"); }, 2, 56, 4, 600),
                    UiKit.Check("Ж", opts.Bold, v => { opts.Bold = v; ApplyText(t => t.Bold = v, "Жирный"); }, 6),
                    UiKit.Check("К", opts.Italic, v => { opts.Italic = v; ApplyText(t => t.Italic = v, "Курсив"); }, 6)));
                panel.Children.Add(UiKit.PropRow("Выравн.", 74, UiKit.Combo(new[]
                {
                    ("left", "по левому"), ("center", "по центру"), ("right", "по правому"),
                }, opts.TextAlign, v => { opts.TextAlign = v; ApplyText(t => t.Align = v, "Выравнивание текста"); }, 130)));
                panel.Children.Add(UiKit.PropRow("Цвет", 74, TextColorButton()));
                break;

            case "bucket":
                panel.Children.Add(UiKit.PropRow("Заливка", 74, FillButton()));
                panel.Children.Add(UiKit.PropRow("Обводка", 74, StrokeButton()));
                panel.Children.Add(UiKit.PropRow("Толщина", 74,
                    UiKit.Number(opts.StrokeWidth, v => PaintOps.SetStrokeWidth(Store, v), 0.5, 56, 0, 200)));
                break;
        }
        return panel;
    }

    private ColorButton FillButton()
    {
        var button = new ColorButton(Store.Opts.Fill);
        button.ColorChanged = hex => PaintOps.SetFill(Store, hex);
        return button;
    }

    private ColorButton StrokeButton()
    {
        var button = new ColorButton(Store.Opts.Stroke);
        button.ColorChanged = hex => PaintOps.SetStroke(Store, hex);
        return button;
    }

    private ColorButton TextColorButton()
    {
        var button = new ColorButton(Store.Opts.Stroke);
        button.ColorChanged = hex =>
        {
            Store.Opts.Stroke = hex;
            ApplyText(t => t.Color = hex, "Цвет текста");
        };
        return button;
    }

    private void ApplyText(Action<TextElement> action, string label)
    {
        ApplySelection(e => { if (e is TextElement t) action(t); }, label, true);
    }

    private void ApplySelection(Action<Element> action, string label, bool onlyIfSelected)
    {
        var selected = Store.SelectionElements();
        if (selected.Count == 0)
        {
            if (onlyIfSelected) return;
            return;
        }
        foreach (var e in selected) action(e);
        Store.Commit(label);
        Store.RaiseAll();
    }

    /* ------------------------------ сцена ----------------------------- */

    private UIElement StageSection()
    {
        var stage = Store.Project.Stage;
        var panel = new StackPanel();

        panel.Children.Add(UiKit.PropRow("Размер", 88,
            UiKit.Number(stage.Width, v => { stage.Width = v; StageChanged("Ширина сцены"); }, 10, 58, 16, 8000),
            new TextBlock { Text = UiKit.T("×"), Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.Res("TxtDim") },
            UiKit.Number(stage.Height, v => { stage.Height = v; StageChanged("Высота сцены"); }, 10, 58, 16, 8000)));

        var bg = new ColorButton(stage.Background);
        bg.ColorChanged = hex => { stage.Background = hex; StageChanged("Фон сцены"); };

        panel.Children.Add(UiKit.PropRow("Фон", 88, bg,
            UiKit.Label("Кадры/с", 54),
            UiKit.Number(stage.Fps, v =>
            {
                stage.Fps = (int)Math.Max(1, Math.Min(120, v));
                Store.Commit("Частота кадров");
                Store.RaiseAll();
            }, 1, 46, 1, 120)));

        panel.Children.Add(UiKit.PropRow("Длительность", 88, new TextBlock
        {
            Text = Store.SceneLength + UiKit.T(" кадр.  ·  ") + Num.F(Store.SceneLength / (double)Math.Max(1, stage.Fps), 2) + UiKit.T(" с"),
            Foreground = UiKit.Res("TxtDim"),
            VerticalAlignment = VerticalAlignment.Center,
        }));

        panel.Children.Add(UiKit.Row(UiKit.SmallButton("Настройки сцены…", "Размер, фон, частота кадров и готовые пресеты", () =>
        {
            var shell = Window.GetWindow(this) as IAppShell;
            shell?.ShowStageSettings();
        })));
        return panel;
    }

    private void StageChanged(string label)
    {
        Store.Commit(label);
        Store.RaiseAll();
    }

    /* ------------------------------ кадр и слой ----------------------- */

    private UIElement FrameLayerSection()
    {
        var panel = new StackPanel();
        Layer layer = Store.ActiveLayer;
        Keyframe keyframe = Store.KeyframeAt(Store.Playhead);
        Keyframe exact = Store.ExactKeyframeAt(Store.Playhead);

        panel.Children.Add(UiKit.PropRow("Слой", 74,
            UiKit.Text(layer.Name, v =>
            {
                if (!string.IsNullOrWhiteSpace(v)) Store.RenameLayer(layer.Id, v.Trim());
            }, 140),
            new Border
            {
                Width = 20,
                Height = 20,
                Background = UiKit.Brush(layer.Color),
                BorderBrush = UiKit.Res("Line2"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = UiKit.T("Цвет слоя"),
                Margin = new Thickness(4, 0, 0, 0),
            }.WithClick(element =>
            {
                ColorPicker.Open(element, layer.Color, hex => Store.SetLayerColor(layer.Id, hex), PlacementMode.Left);
            })));

        panel.Children.Add(UiKit.Row(
            UiKit.Check("Видимый", layer.Visible, v => Store.SetLayerFlag(layer.Id, "visible", v)),
            UiKit.Check("Заблокирован", layer.Locked, v => Store.SetLayerFlag(layer.Id, "locked", v))));

        panel.Children.Add(UiKit.PropRow("Кадр", 74, new TextBlock
        {
            Text = (Store.Playhead + 1).ToString(),
            Foreground = UiKit.Res("Txt"),
            VerticalAlignment = VerticalAlignment.Center,
        }, UiKit.Label(keyframe == null
            ? "вне диапазона слоя"
            : (exact != null ? "ключевой кадр" : "продолжение кадра " + (keyframe.Index + 1)), 200)));

        if (keyframe != null)
        {
            panel.Children.Add(UiKit.PropRow("Диапазон", 74, new TextBlock
            {
                Text = (keyframe.Index + 1) + " – " + (keyframe.EndIndex + 1) + UiKit.T("   ·   объектов: ") + keyframe.Elements.Count,
                Foreground = UiKit.Res("TxtDim"),
                VerticalAlignment = VerticalAlignment.Center,
            }));
        }

        panel.Children.Add(UiKit.Row(
            UiKit.SmallButton("F5 кадр", "Вставить кадр", () => Run("frame.insert")),
            UiKit.SmallButton("F6 ключ", "Ключевой кадр", () => Run("frame.key")),
            UiKit.SmallButton("Твин", "Создать классический переход (F9)", () => Run("frame.tween"))));
        return panel;
    }

    /* ------------------------------ трансформации --------------------- */

    private UIElement TransformSection(List<Element> selected)
    {
        var panel = new StackPanel();
        RectD bounds = Store.SelectionBounds();

        if (selected.Count == 1)
        {
            var element = selected[0];
            panel.Children.Add(UiKit.PropRow("", 0,
                UiKit.Label("X", 14), UiKit.Number(element.X, v => { element.X = v; Commit("Положение X"); }, 1, 72),
                UiKit.Label("Y", 14), UiKit.Number(element.Y, v => { element.Y = v; Commit("Положение Y"); }, 1, 72)));
            panel.Children.Add(UiKit.PropRow("", 0,
                UiKit.Label("Ш", 14), UiKit.Number(bounds.W, v => ResizeSelection(v, bounds.H, true), 1, 72, 0.5, 20000),
                UiKit.Label("В", 14), UiKit.Number(bounds.H, v => ResizeSelection(bounds.W, v, false), 1, 72, 0.5, 20000)));
            panel.Children.Add(UiKit.PropRow("", 0,
                UiKit.Label("Поворот", 52), UiKit.Number(element.Rotation, v => { element.Rotation = v; Commit("Поворот"); }, 1, 60, -3600, 3600, "°"),
                UiKit.Label("Прозр.", 40), UiKit.Number(element.Alpha * 100, v => { element.Alpha = Math.Max(0, Math.Min(1, v / 100)); Commit("Прозрачность"); }, 1, 60, 0, 100, "%")));
            panel.Children.Add(UiKit.PropRow("", 0,
                UiKit.Label("Масштаб", 52), UiKit.Number(element.ScaleX * 100, v => { element.ScaleX = v / 100; Commit("Масштаб X"); }, 1, 60, -1000, 1000, "%"),
                UiKit.Number(element.ScaleY * 100, v => { element.ScaleY = v / 100; Commit("Масштаб Y"); }, 1, 60, -1000, 1000, "%")));
        }
        else
        {
            panel.Children.Add(UiKit.PropRow("Центр", 74, new TextBlock
            {
                Text = Num.F(bounds.Center.X, 1) + " ; " + Num.F(bounds.Center.Y, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = UiKit.Res("TxtDim"),
            }));
            panel.Children.Add(UiKit.PropRow("Размер", 74,
                UiKit.Number(bounds.W, v => ResizeSelection(v, bounds.H, true), 1, 72, 0.5, 20000),
                UiKit.Number(bounds.H, v => ResizeSelection(bounds.W, v, false), 1, 72, 0.5, 20000)));
        }

        // кнопки выравнивания — как в панели «Выравнивание» Animate
        panel.Children.Add(UiKit.Row(
            UiKit.SmallButton("\u2551\u25C0", "Выровнять по левому краю", () => Run("mod.alignLeft")),
            UiKit.SmallButton("\u25C0\u2551", "Центр по горизонтали", () => Run("mod.alignHCenter")),
            UiKit.SmallButton("\u25B6\u2551", "Выровнять по правому краю", () => Run("mod.alignRight")),
            UiKit.SmallButton("\u2550\u25B2", "Выровнять по верху", () => Run("mod.alignTop")),
            UiKit.SmallButton("\u2550\u25A0", "Центр по вертикали", () => Run("mod.alignVCenter")),
            UiKit.SmallButton("\u2550\u25BC", "Выровнять по низу", () => Run("mod.alignBottom"))));
        return panel;
    }

    private void ResizeSelection(double targetWidth, double targetHeight, bool horizontal)
    {
        RectD bounds = Store.SelectionBounds();
        if (bounds.IsEmpty) return;
        if (horizontal && bounds.W > 0.001)
        {
            double k = targetWidth / bounds.W;
            foreach (var element in Store.SelectionElements())
            {
                element.ScaleX *= k;
                Store.OffsetElement(element, (element.X - bounds.Center.X) * (k - 1), 0);
            }
        }
        else if (!horizontal && bounds.H > 0.001)
        {
            double k = targetHeight / bounds.H;
            foreach (var element in Store.SelectionElements())
            {
                element.ScaleY *= k;
                Store.OffsetElement(element, 0, (element.Y - bounds.Center.Y) * (k - 1));
            }
        }
        Commit("Размер объекта");
    }

    private UIElement TweenSection(Keyframe keyframe)
    {
        var panel = new StackPanel();
        panel.Children.Add(UiKit.PropRow("Тип", 74, new TextBlock
        {
            Text = keyframe.Tween.Type == "classic" ? "Классический переход" : keyframe.Tween.Type,
            Foreground = UiKit.Res("Txt"),
            VerticalAlignment = VerticalAlignment.Center,
        }));
        var slider = new Slider
        {
            Minimum = -100,
            Maximum = 100,
            Value = keyframe.Tween.Ease * 100,
            Width = 130,
            VerticalAlignment = VerticalAlignment.Center,
            IsMoveToPointEnabled = true,
            ToolTip = UiKit.T("Замедление перехода"),
        };
        slider.ValueChanged += (s, e) =>
        {
            keyframe.Tween.Ease = e.NewValue / 100.0;
            Store.Commit("Замедление перехода");
            Store.RaiseAll();
        };
        panel.Children.Add(UiKit.PropRow("Замедление", 74, slider));
        panel.Children.Add(UiKit.Row(UiKit.SmallButton("Убрать переход", "Удалить переход на этом кадре", () => Run("frame.tweenNone"))));
        return panel;
    }

    /* ------------------------------ объект ---------------------------- */

    private UIElement ObjectSection(Element element)
    {
        var panel = new StackPanel();
        panel.Children.Add(UiKit.PropRow("Имя", 74,
            UiKit.Text(element.Name, v => { element.Name = v; Commit("Имя объекта"); }, 150)));

        switch (element)
        {
            case ShapeElement shape:
                panel.Children.Add(new TextBlock
                {
                    Text = UiKit.T("ФИГУРА"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = UiKit.Res("TxtDim"),
                    Margin = new Thickness(0, 4, 0, 5),
                });
                panel.Children.Add(UiKit.PropRow("Заливка", 74, ShapeFill(shape)));
                panel.Children.Add(UiKit.PropRow("Обводка", 74, ShapeStroke(shape)));
                panel.Children.Add(UiKit.PropRow("Толщина", 74,
                    UiKit.Number(shape.StrokeWidth, v => { shape.StrokeWidth = v; Commit("Толщина"); }, 0.5, 56, 0, 200)));
                panel.Children.Add(UiKit.Row(
                    UiKit.Check("Заливка", shape.Filled, v => { shape.Filled = v; Commit("Заливка"); }),
                    UiKit.Check("Обводка", shape.Stroked, v => { shape.Stroked = v; Commit("Обводка"); })));
                panel.Children.Add(UiKit.Row(
                    UiKit.Check("Замкнутая", shape.Closed, v => { shape.Closed = v; Commit("Замкнутость"); }),
                    UiKit.Check("Сглаживание", shape.Smooth, v => { shape.Smooth = v; Commit("Сглаживание"); })));
                panel.Children.Add(new TextBlock
                {
                    Text = UiKit.T("Точек: ") + shape.Points.Count,
                    Foreground = UiKit.Res("TxtDim"),
                    Margin = new Thickness(0, 2, 0, 0),
                    FontSize = 10.5,
                });
                break;

            case TextElement text:
                panel.Children.Add(new TextBlock
                {
                    Text = UiKit.T("ТЕКСТ"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = UiKit.Res("TxtDim"),
                    Margin = new Thickness(0, 4, 0, 5),
                });
                var content = new TextBox
                {
                    Text = text.Text,
                    AcceptsReturn = true,
                    MinHeight = 52,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(0, 0, 0, 5),
                };
                content.LostFocus += (s, e) => { text.Text = content.Text; Commit("Текст"); };
                panel.Children.Add(content);
                panel.Children.Add(UiKit.PropRow("Шрифт", 74, UiKit.Combo(new[]
                {
                    ("Segoe UI", "Segoe UI"), ("Arial", "Arial"), ("Georgia", "Georgia"),
                    ("Consolas", "Consolas"), ("Times New Roman", "Times New Roman"), ("Verdana", "Verdana"),
                }, text.FontFamily, v => { text.FontFamily = v; Commit("Шрифт"); }, 140)));
                panel.Children.Add(UiKit.PropRow("Размер", 74,
                    UiKit.Number(text.FontSize, v => { text.FontSize = v; Commit("Размер текста"); }, 2, 56, 4, 600),
                    UiKit.Check("Ж", text.Bold, v => { text.Bold = v; Commit("Жирный"); }, 6),
                    UiKit.Check("К", text.Italic, v => { text.Italic = v; Commit("Курсив"); }, 6)));
                var color = new ColorButton(text.Color);
                color.ColorChanged = hex => { text.Color = hex; Commit("Цвет текста"); };
                panel.Children.Add(UiKit.PropRow("Цвет", 74, color,
                    UiKit.Combo(new[]
                    {
                        ("left", "по левому"), ("center", "по центру"), ("right", "по правому"),
                    }, text.Align, v => { text.Align = v; Commit("Выравнивание"); }, 120)));
                panel.Children.Add(UiKit.Row(UiKit.SmallButton("Править на сцене", "Редактирование прямо на сцене (двойной щелчок)", () =>
                {
                    if (Window.GetWindow(this) is MainWindow main) main.BeginTextEdit(text);
                })));
                break;

            case InstanceElement instance:
                panel.Children.Add(new TextBlock
                {
                    Text = UiKit.T("СИМВОЛ"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = UiKit.Res("TxtDim"),
                    Margin = new Thickness(0, 4, 0, 5),
                });
                panel.Children.Add(UiKit.PropRow("Экземпляр", 74, new TextBlock
                {
                    Text = instance.SymbolName + "   (" + (Store.Project.Library.TryGetValue(instance.SymbolId, out Symbol s) ? s.Kind.ToString() : "нет") + ")",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = UiKit.Res("TxtDim"),
                }));
                panel.Children.Add(UiKit.PropRow("Режим", 74, UiKit.Combo(new[]
                {
                    ("Loop", "цикл"), ("PlayOnce", "один раз"), ("SingleFrame", "один кадр"),
                }, instance.Loop.ToString(), v => { instance.Loop = (LoopMode)Enum.Parse(typeof(LoopMode), v); Commit("Режим символа"); }, 110)));
                panel.Children.Add(UiKit.PropRow("Первый кадр", 74,
                    UiKit.Number(instance.FirstFrame + 1, v => { instance.FirstFrame = (int)Math.Max(0, v - 1); Commit("Первый кадр символа"); }, 1, 56, 1, 5000)));
                panel.Children.Add(UiKit.Row(
                    UiKit.SmallButton("Открыть символ", "Редактировать содержимое символа (двойной щелчок по объекту)", () => Store.EnterSymbol(instance.SymbolId)),
                    UiKit.SmallButton("Разобрать", "Вставить содержимое символа прямо в сцену", () => ExpandInstance(instance))));
                break;

            case GroupElement group:
                panel.Children.Add(new TextBlock
                {
                    Text = UiKit.T("ГРУППА"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = UiKit.Res("TxtDim"),
                    Margin = new Thickness(0, 4, 0, 5),
                });
                panel.Children.Add(UiKit.PropRow("Объектов", 74, new TextBlock
                {
                    Text = group.Children.Count.ToString(),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = UiKit.Res("TxtDim"),
                }, UiKit.SmallButton("Разгруппировать", "Ctrl+Shift+G", () => Run("mod.ungroup"))));
                break;

            case ImageElement image:
                panel.Children.Add(new TextBlock
                {
                    Text = UiKit.T("ИЗОБРАЖЕНИЕ"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = UiKit.Res("TxtDim"),
                    Margin = new Thickness(0, 4, 0, 5),
                });
                panel.Children.Add(UiKit.PropRow("Файл", 74, new TextBlock
                {
                    Text = image.FileName + "   (" + image.PixelWidth.ToString("0") + "×" + image.PixelHeight.ToString("0") + ")",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = UiKit.Res("TxtDim"),
                }));
                break;
        }

        panel.Children.Add(UiKit.Row(
            UiKit.SmallButton("Вперёд", "На передний план (Ctrl+Shift+↑)", () => Run("mod.front")),
            UiKit.SmallButton("Назад", "На задний план (Ctrl+Shift+↓)", () => Run("mod.back")),
            UiKit.SmallButton("Дублировать", "Ctrl+D", () => Run("edit.duplicate")),
            UiKit.SmallButton("Удалить", "Delete", () => Run("edit.delete"))));
        return panel;
    }

    private ColorButton ShapeFill(ShapeElement shape)
    {
        var button = new ColorButton(shape.Fill);
        button.ColorChanged = hex => { shape.Fill = hex; shape.Filled = true; Commit("Цвет заливки"); };
        return button;
    }

    private ColorButton ShapeStroke(ShapeElement shape)
    {
        var button = new ColorButton(shape.Stroke);
        button.ColorChanged = hex => { shape.Stroke = hex; shape.Stroked = true; Commit("Цвет обводки"); };
        return button;
    }

    private void ExpandInstance(InstanceElement instance)
    {
        if (!Store.Project.Library.TryGetValue(instance.SymbolId, out Symbol symbol)) return;
        var (_, _, keyframe) = Store.FindItem(instance.Id);
        if (keyframe == null) return;
        int index = keyframe.Elements.IndexOf(instance);
        keyframe.Elements.Remove(instance);
        Mat matrix = instance.LocalMatrix;
        var added = new List<Element>();
        foreach (var ev in Anim.EvaluateScene(symbol.Timeline, Math.Max(0, instance.FirstFrame)))
        {
            var copy = ev.Src.Clone();
            Mat combined = matrix.Multiply(copy.LocalMatrix);
            combined.Decompose(out double x, out double y, out double rot, out double sx, out double sy);
            copy.X = x; copy.Y = y; copy.Rotation = rot; copy.ScaleX = sx; copy.ScaleY = sy;
            copy.Alpha *= instance.Alpha;
            keyframe.Elements.Insert(Math.Max(0, index), copy);
            added.Add(copy);
        }
        Store.SetSelection(added.Select(a => a.Id));
        Store.Commit("Разгруппировать символ");
        Store.RaiseAll();
    }

    private UIElement MultiSection()
    {
        var panel = new StackPanel();
        panel.Children.Add(UiKit.Row(
            UiKit.SmallButton("Сгруппировать", "Ctrl+G", () => Run("mod.group")),
            UiKit.SmallButton("Разгруппировать", "Ctrl+Shift+G", () => Run("mod.ungroup"))));
        panel.Children.Add(UiKit.Row(
            UiKit.SmallButton("Распределить ↔", "Равные промежутки по горизонтали", () => Run("mod.distributeH")),
            UiKit.SmallButton("Распределить ↕", "Равные промежутки по вертикали", () => Run("mod.distributeV"))));
        panel.Children.Add(UiKit.Row(
            UiKit.SmallButton("Отразить ↔", "Отразить по горизонтали", () => Run("mod.flipH")),
            UiKit.SmallButton("Отразить ↕", "Отразить по вертикали", () => Run("mod.flipV")),
            UiKit.SmallButton("⟲ 90°", "Повернуть влево", () => Run("mod.rotCCW")),
            UiKit.SmallButton("⟳ 90°", "Повернуть вправо", () => Run("mod.rotCW"))));
        panel.Children.Add(UiKit.Row(
            UiKit.SmallButton("На передний план", "Ctrl+Shift+↑", () => Run("mod.front")),
            UiKit.SmallButton("На задний план", "Ctrl+Shift+↓", () => Run("mod.back"))));
        return panel;
    }

    private void Run(string commandId)
    {
        Cmd command = Commands.Find(commandId);
        if (command?.Run == null) return;
        var shell = Window.GetWindow(this) as IAppShell;
        if (shell != null && command.IsEnabled(shell)) command.Run(shell);
        Refresh();
    }

    private void Commit(string label)
    {
        Store.Commit(label);
        Store.RaiseAll();
    }
}

/* ===================================================================== */
/*  Панель «Библиотека»                                                   */
/* ===================================================================== */

public class LibraryPanel : DockPanel
{
    public Store Store;
    public string SelectedSymbolId;

    private readonly Border _preview;
    private readonly Image _previewImage;
    private readonly TextBlock _previewName;
    private readonly StackPanel _list;
    private readonly TextBlock _emptyHint;

    public LibraryPanel()
    {
        LastChildFill = true;

        // область предпросмотра
        _previewName = new TextBlock
        {
            FontSize = 10.5,
            Foreground = UiKit.Res("TxtDim"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 3, 4, 4),
        };
        Grid.SetRow(_previewName, 1);

        _previewImage = new Image
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(4, 4, 4, 0),
        };
        Grid.SetRow(_previewImage, 0);

        var previewGrid = new Grid();
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewGrid.Children.Add(_previewImage);
        previewGrid.Children.Add(_previewName);

        _preview = new Border
        {
            Height = 92,
            Background = UiKit.Res("DarkInset"),
            BorderBrush = UiKit.Res("Line"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = previewGrid,
        };
        DockPanel.SetDock(_preview, Dock.Top);
        Children.Add(_preview);

        // кнопки внизу — как в панели «Библиотека» у Animate
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = UiKit.Res("Panel"),
            Margin = new Thickness(0, 1, 0, 0),
        };
        Grid.SetRow(buttons, 0);
        var buttonBar = new Border
        {
            Background = UiKit.Res("Panel"),
            BorderBrush = UiKit.Res("Line"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(4, 4, 4, 4),
            Child = buttons,
        };
        buttons.Children.Add(UiKit.GlyphButton("\uE710", "Преобразовать выделенное в символ (F8)", () =>
        {
            (Window.GetWindow(this) as IAppShell)?.CreateSymbolFromSelection();
        }, 24));
        buttons.Children.Add(UiKit.GlyphButton("\uE7C3", "Поставить экземпляр на сцену", () =>
        {
            if (string.IsNullOrEmpty(SelectedSymbolId))
            {
                Store?.Report("Сначала выберите символ в библиотеке");
                return;
            }
            Store.AddInstance(SelectedSymbolId);
        }, 24));
        buttons.Children.Add(UiKit.GlyphButton("\uE713", "Свойства символа: переименовать", () =>
        {
            var symbol = SelectedSymbol();
            if (symbol != null) RenameSymbol(symbol);
        }, 24));
        buttons.Children.Add(UiKit.GlyphButton("\uE74D", "Удалить символ из библиотеки", () =>
        {
            var symbol = SelectedSymbol();
            if (symbol != null) Store.DeleteSymbol(symbol.Id);
        }, 24));
        DockPanel.SetDock(buttonBar, Dock.Bottom);
        Children.Add(buttonBar);

        // список с колонками
        var header = new Border
        {
            Background = UiKit.Res("Panel"),
            BorderBrush = UiKit.Res("Line"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(20) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(66) },
                    new ColumnDefinition { Width = new GridLength(40) },
                },
                Children =
                {
                    HeaderCell("", 0),
                    HeaderCell("Имя", 1),
                    HeaderCell("Тип", 2),
                    HeaderCell("Исп.", 3),
                },
            },
        };
        DockPanel.SetDock(header, Dock.Top);
        Children.Add(header);

        _list = new StackPanel();
        _emptyHint = new TextBlock
        {
            Text = UiKit.T("Библиотека пуста.\nВыделите рисунок на сцене и нажмите «Преобразовать в символ» (F8)."),
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiKit.Res("TxtDim"),
            Margin = new Thickness(8),
            FontSize = 10.5,
        };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = UiKit.Res("Bg2"),
            Content = _list,
        };
        Children.Add(scroll);
    }

    private static UIElement HeaderCell(string text, int column)
    {
        var block = new TextBlock
        {
            Text = UiKit.T(text),
            FontSize = 10,
            Foreground = UiKit.Res("TxtDim"),
        };
        Grid.SetColumn(block, column);
        return block;
    }

    private Symbol SelectedSymbol()
    {
        if (Store == null || string.IsNullOrEmpty(SelectedSymbolId)) return null;
        return Store.Project.Library.TryGetValue(SelectedSymbolId, out Symbol s) ? s : null;
    }

    public void Refresh()
    {
        _list.Children.Clear();
        if (Store == null) return;

        var symbols = Store.Project.Library.Values.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Symbol selected = SelectedSymbol();

        if (symbols.Count == 0)
        {
            _list.Children.Add(_emptyHint);
            SetPreview(null);
            return;
        }
        if (selected == null) selected = symbols[0];

        foreach (Symbol symbol in symbols) _list.Children.Add(BuildRow(symbol, symbol.Id == selected.Id));
        SetPreview(selected);
    }

    private void SetPreview(Symbol symbol)
    {
        if (symbol != null)
        {
            _previewImage.Source = CreateThumbnail(symbol, Store.Project, 170, 62);
            _previewName.Text = symbol.Name + "   ·   " + KindName(symbol.Kind);
        }
        else
        {
            _previewImage.Source = null;
            _previewName.Text = UiKit.T("нет выбранного символа");
        }
    }

    private UIElement BuildRow(Symbol symbol, bool selected)
    {
        var grid = new Grid { Height = 22 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var icon = Icons.CreatePath(symbol.Kind == SymbolKind.MovieClip ? "symbol" : "image", UiKit.Res(selected ? "Txt" : "TxtDim"), 1.3);
        icon.Width = 13;
        icon.Height = 13;
        icon.Margin = new Thickness(4, 0, 3, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var name = new TextBlock
        {
            Text = symbol.Name,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = UiKit.Res(selected ? "Txt" : "TxtDim"),
        };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var type = new TextBlock
        {
            Text = KindName(symbol.Kind),
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = UiKit.Res("TxtDim"),
        };
        Grid.SetColumn(type, 2);
        grid.Children.Add(type);

        var uses = new TextBlock
        {
            Text = CountUses(symbol.Id).ToString(),
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = UiKit.Res("TxtDim"),
        };
        Grid.SetColumn(uses, 3);
        grid.Children.Add(uses);

        var row = new Border
        {
            Background = selected ? UiKit.Res("Accent") : Brushes.Transparent,
            Padding = new Thickness(3, 0, 3, 0),
            Child = grid,
            Cursor = Cursors.Hand,
            ToolTip = symbol.Name + "   ·   двойной щелчок — открыть символ",
        };
        row.MouseLeftButtonDown += (s, e) =>
        {
            SelectedSymbolId = symbol.Id;
            if (e.ClickCount == 2) Store.EnterSymbol(symbol.Id);
            else Refresh();
        };

        var menu = new ContextMenu();
        menu.Items.Add(UiKit.Menu("Поставить на сцену", () => Store.AddInstance(symbol.Id)));
        menu.Items.Add(UiKit.Menu("Заменить у выделенного", () => Store.SwapSymbol(symbol.Id)));
        menu.Items.Add(UiKit.Menu("Открыть для правки", () => Store.EnterSymbol(symbol.Id)));
        menu.Items.Add(UiKit.Menu("Переименовать…", () => RenameSymbol(symbol)));
        menu.Items.Add(UiKit.Menu("Дублировать", () => Store.DuplicateSymbol(symbol.Id)));
        menu.Items.Add(new Separator());
        menu.Items.Add(UiKit.Menu("Удалить из библиотеки", () => Store.DeleteSymbol(symbol.Id)));
        row.ContextMenu = menu;
        return row;
    }

    private static string KindName(SymbolKind kind)
    {
        switch (kind)
        {
            case SymbolKind.MovieClip: return L10n.T("Клип");
            case SymbolKind.Graphic: return L10n.T("Графика");
            default: return L10n.T("Кнопка");
        }
    }

    private int CountUses(string symbolId)
    {
        int count = CountInScene(Store.Project.Timeline, symbolId);
        foreach (Symbol symbol in Store.Project.Library.Values) count += CountInScene(symbol.Timeline, symbolId);
        return count;
    }

    private static int CountInScene(Scene scene, string symbolId)
    {
        int count = 0;
        if (scene == null) return 0;
        foreach (Layer layer in scene.Layers)
            foreach (Keyframe keyframe in layer.Frames)
                count += CountInList(keyframe.Elements, symbolId);
        return count;
    }

    private static int CountInList(List<Element> elements, string symbolId)
    {
        int count = 0;
        if (elements == null) return 0;
        foreach (Element element in elements)
        {
            if (element is InstanceElement instance && instance.SymbolId == symbolId) count++;
            else if (element is GroupElement group) count += CountInList(group.Children, symbolId);
        }
        return count;
    }

    private void RenameSymbol(Symbol symbol)
    {
        var dialog = new TextInputDialog("Имя символа", symbol.Name);
        if (dialog.ShowDialog() == true && dialog.Value.Trim().Length > 0)
        {
            Store.RenameSymbol(symbol.Id, dialog.Value.Trim());
            SelectedSymbolId = symbol.Id;
            Refresh();
        }
    }

    public static BitmapSource CreateThumbnail(Symbol symbol, Project project, int width, int height)
    {
        RectD bounds = symbol.Bounds;
        if (bounds.W <= 0 || bounds.H <= 0) bounds = new RectD(-50, -50, 100, 100);

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(UiKit.Res("DarkInset"), null, new Rect(0, 0, width, height));
            double scale = Math.Min(width / (bounds.W * 1.25), height / (bounds.H * 1.25));
            double ox = width / 2.0 - bounds.Center.X * scale;
            double oy = height / 2.0 - bounds.Center.Y * scale;
            dc.PushTransform(new TranslateTransform(ox, oy));
            dc.PushTransform(new ScaleTransform(scale, scale));
            var options = new RenderOptions
            {
                Project = project,
                PixelsPerDip = 1,
                CurrentFrame = 0,
            };
            Renderer.DrawScene(dc, project, symbol.Timeline, 0, options);
            dc.Pop();
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}

/* ===================================================================== */
/*  Панель «Цвет» и «Образцы»                                             */
/* ===================================================================== */

public class SwatchesPanel : StackPanel
{
    public Store Store;

    public SwatchesPanel()
    {
        Orientation = Orientation.Vertical;
        var grid = ColorPicker.SwatchGrid(20, hex => PaintOps.SetFill(Store, hex), 14);
        grid.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(grid);
    }

    public void Refresh()
    {
    }
}

/* ===================================================================== */
/*  Панель слоёв (колонка слева от кадров)                                */
/* ===================================================================== */

public class LayersPanel : StackPanel
{
    public Store Store;
    public double RowHeight = 24;
    public double RulerHeight = 20;

    private bool _renaming;

    public LayersPanel()
    {
        Orientation = Orientation.Vertical;
    }

    public void Refresh()
    {
        if (_renaming) return;
        Children.Clear();
        if (Store == null) return;

        Children.Add(BuildHeader());
        var scene = Store.ActiveScene;
        for (int i = 0; i < scene.Layers.Count; i++) Children.Add(BuildRow(scene.Layers[i], i));
    }

    private UIElement BuildHeader()
    {
        var grid = new Grid { Height = RulerHeight, Background = UiKit.Res("Panel2") };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var eye = HeaderButton(EyeDot(true), "Показать/скрыть все слои", () =>
        {
            var scene = Store.ActiveScene;
            bool any = scene.Layers.Any(l => l.Visible);
            foreach (var layer in scene.Layers) Store.SetLayerFlag(layer.Id, "visible", !any);
        });
        Grid.SetColumn(eye, 0);
        grid.Children.Add(eye);

        var padlock = HeaderButton(LockGlyph(false), "Заблокировать/разблокировать все слои", () =>
        {
            var scene = Store.ActiveScene;
            bool any = scene.Layers.Any(l => !l.Locked);
            foreach (var layer in scene.Layers) Store.SetLayerFlag(layer.Id, "locked", any);
        });
        Grid.SetColumn(padlock, 1);
        grid.Children.Add(padlock);

        var title = new TextBlock
        {
            Text = UiKit.T("СЛОИ"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = UiKit.Res("TxtDim"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(title, 2);
        grid.Children.Add(title);

        return new Border
        {
            Height = RulerHeight,
            Background = UiKit.Res("Panel2"),
            BorderBrush = UiKit.Res("Line"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
    }

    private static Button HeaderButton(UIElement content, string tooltip, Action onClick)
    {
        var button = new Button
        {
            Content = content,
            ToolTip = tooltip,
            Style = (Style)Application.Current.Resources["IconBtn"],
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += (s, e) => onClick();
        return button;
    }

    private UIElement BuildRow(Layer layer, int index)
    {
        bool active = layer.Id == Store.ActiveLayerId;
        var grid = new Grid { Height = RowHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var eye = new Button
        {
            Content = EyeDot(layer.Visible),
            ToolTip = layer.Visible ? "Слой виден — щёлкните, чтобы скрыть" : "Слой скрыт — щёлкните, чтобы показать",
            Style = (Style)Application.Current.Resources["IconBtn"],
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Focusable = false,
        };
        eye.Click += (s, e) => Store.SetLayerFlag(layer.Id, "visible", !layer.Visible);
        Grid.SetColumn(eye, 0);
        grid.Children.Add(eye);

        var padlock = new Button
        {
            Content = LockGlyph(layer.Locked),
            ToolTip = layer.Locked ? "Слой заблокирован — щёлкните, чтобы снять" : "Слой доступен для правки",
            Style = (Style)Application.Current.Resources["IconBtn"],
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Focusable = false,
        };
        padlock.Click += (s, e) => Store.SetLayerFlag(layer.Id, "locked", !layer.Locked);
        Grid.SetColumn(padlock, 1);
        grid.Children.Add(padlock);

        var chip = new Border
        {
            Background = UiKit.Brush(layer.Color),
            Width = 9,
            Height = 9,
            BorderBrush = UiKit.Res("Line"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = UiKit.T("Цвет слоя"),
        };
        chip.MouseLeftButtonUp += (s, e) =>
        {
            ColorPicker.Open(chip, layer.Color, hex => Store.SetLayerColor(layer.Id, hex), PlacementMode.Right);
            e.Handled = true;
        };
        Grid.SetColumn(chip, 2);
        grid.Children.Add(chip);

        var name = new TextBlock
        {
            Text = layer.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 11,
            Margin = new Thickness(4, 0, 4, 0),
            Foreground = UiKit.Res(active ? "Txt" : "TxtDim"),
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
        };
        Grid.SetColumn(name, 3);
        grid.Children.Add(name);

        var row = new Border
        {
            Height = RowHeight,
            Background = active ? UiKit.Res("RowActive") : Brushes.Transparent,
            BorderBrush = UiKit.Res("Line"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = grid,
            Cursor = Cursors.Hand,
        };
        row.MouseLeftButtonDown += (s, e) =>
        {
            Store.ActiveLayerId = layer.Id;
            Store.ClearSelection();
            Store.RaiseAll();
        };
        name.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                BeginRename(layer, name);
                e.Handled = true;
            }
        };

        var menu = new ContextMenu();
        menu.Items.Add(UiKit.Menu("Переименовать…", () => BeginRename(layer, name)));
        menu.Items.Add(UiKit.Menu("Цвет слоя…", () => ColorPicker.Open(chip, layer.Color, hex => Store.SetLayerColor(layer.Id, hex), PlacementMode.Right)));
        menu.Items.Add(new Separator());
        menu.Items.Add(UiKit.Menu("Дублировать слой", () => Store.DuplicateLayer(layer.Id)));
        menu.Items.Add(UiKit.Menu("Удалить слой", () => Store.DeleteLayer(layer.Id)));
        menu.Items.Add(UiKit.Menu("Выше", () => Store.MoveLayer(layer.Id, -1)));
        menu.Items.Add(UiKit.Menu("Ниже", () => Store.MoveLayer(layer.Id, 1)));
        row.ContextMenu = menu;
        return row;
    }

    private void BeginRename(Layer layer, TextBlock label)
    {
        _renaming = true;
        var editor = new TextBox
        {
            Text = layer.Name,
            FontSize = 11,
            Height = RowHeight - 3,
            Padding = new Thickness(3, 0, 3, 0),
            Margin = new Thickness(3, 0, 3, 0),
        };
        var parent = (Grid)label.Parent;
        int index = parent.Children.IndexOf(label);
        parent.Children.Remove(label);
        Grid.SetColumn(editor, 3);
        parent.Children.Insert(index, editor);
        editor.Focus();
        editor.SelectAll();
        bool done = false;
        void Finish(bool save)
        {
            if (done) return;
            done = true;
            _renaming = false;
            if (save && editor.Text.Trim().Length > 0) Store.RenameLayer(layer.Id, editor.Text.Trim());
            Refresh();
        }
        editor.LostFocus += (s, e) => Finish(true);
        editor.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { Finish(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { Finish(false); e.Handled = true; }
        };
    }

    private static UIElement EyeDot(bool visible)
    {
        var canvas = new Canvas { Width = 16, Height = 16 };
        var dot = new Ellipse
        {
            Width = visible ? 8 : 8,
            Height = visible ? 8 : 8,
            Fill = visible ? UiKit.Res("Txt") : Brushes.Transparent,
            Stroke = visible ? null : UiKit.Res("TxtDim"),
            StrokeThickness = visible ? 0 : 1.2,
        };
        Canvas.SetLeft(dot, 4);
        Canvas.SetTop(dot, 4);
        canvas.Children.Add(dot);
        return canvas;
    }

    private static UIElement LockGlyph(bool locked)
    {
        var canvas = new Canvas { Width = 16, Height = 16 };
        if (locked)
        {
            var body = new Rectangle
            {
                Width = 9,
                Height = 7,
                RadiusX = 1,
                RadiusY = 1,
                Fill = UiKit.Res("Warn"),
            };
            Canvas.SetLeft(body, 3.5);
            Canvas.SetTop(body, 7);
            canvas.Children.Add(body);
            var arch = new Path
            {
                Data = Geometry.Parse("M5.5 7 V5.5 A2.5 2.5 0 0 1 10.5 5.5 V7"),
                Stroke = UiKit.Res("Warn"),
                StrokeThickness = 1.5,
            };
            canvas.Children.Add(arch);
        }
        else
        {
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.Transparent,
                Stroke = UiKit.Res("Line2"),
                StrokeThickness = 1.2,
            };
            Canvas.SetLeft(dot, 4);
            Canvas.SetTop(dot, 4);
            canvas.Children.Add(dot);
        }
        return canvas;
    }
}

/* ===================================================================== */
/*  Панель таймлайна: воспроизведение, кадры и слои                       */
/* ===================================================================== */

public class TimelineToolbar : StackPanel
{
    public Store Store;
    public event Action PlayToggled;

    private readonly TextBlock _playButton;
    private readonly TextBlock _frameInfo;

    public TimelineToolbar()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;

        _playButton = new TextBlock
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _frameInfo = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 10.5,
            Foreground = UiKit.Res("TxtDim"),
        };

        Children.Add(Button("В начало (Home)", "first", () => Run("view.first")));
        Children.Add(Button("Предыдущий кадр (,)", "prev", () => Run("view.prev")));
        Children.Add(PlayButton());
        Children.Add(Button("Следующий кадр (.)", "next", () => Run("view.next")));
        Children.Add(Button("В конец (End)", "last", () => Run("view.last")));
        Children.Add(_frameInfo);

        Children.Add(Divider());
        Children.Add(TextButton("F5", "Вставить кадр", "frame.insert"));
        Children.Add(TextButton("F6", "Ключевой кадр", "frame.key"));
        Children.Add(TextButton("F7", "Пустой ключевой кадр", "frame.blankKey"));
        Children.Add(TextButton("⇧F5", "Удалить кадр", "frame.remove"));
        Children.Add(TextButton("⇧F6", "Удалить ключевой кадр", "frame.clearKey"));
        Children.Add(TextButton("Твин", "Классический переход (F9)", "frame.tween"));
        Children.Add(TextButton("Без твина", "Убрать переход", "frame.tweenNone"));

        Children.Add(Divider());
        Children.Add(Button("Новый слой", "add", () => Run("layer.add")));
        Children.Add(Button("Дублировать слой", "copy", () => Run("layer.duplicate")));
        Children.Add(Button("Удалить слой", "trash", () => Run("layer.delete")));
        Children.Add(Button("Слой выше", "up", () => Run("layer.up")));
        Children.Add(Button("Слой ниже", "down", () => Run("layer.down")));
    }

    private Button Button(string tooltip, string icon, Action action)
    {
        var button = UiKit.IconButton(icon, tooltip, action, 22);
        button.Margin = new Thickness(0, 0, 2, 0);
        return button;
    }

    private Button PlayButton()
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["IconBtn"],
            Width = 26,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 2, 0),
            Content = _playButton,
            ToolTip = UiKit.T("Воспроизведение / пауза (Пробел)"),
        };
        button.Click += (s, e) => PlayToggled?.Invoke();
        return button;
    }

    private static UIElement Divider()
    {
        return new Border
        {
            Width = 1,
            Background = UiKit.Res("Line"),
            Margin = new Thickness(6, 4, 6, 4),
        };
    }

    private Button TextButton(string text, string tooltip, string commandId)
    {
        var button = UiKit.SmallButton(text, tooltip, () => Run(commandId));
        button.Margin = new Thickness(0, 0, 3, 0);
        button.FontSize = 10.5;
        return button;
    }

    private void Run(string commandId)
    {
        Cmd command = Commands.Find(commandId);
        if (command?.Run == null) return;
        if (Window.GetWindow(this) is IAppShell shell && command.IsEnabled(shell)) command.Run(shell);
    }

    public void Refresh()
    {
        if (Store == null) return;
        _playButton.Text = Store.Playing ? "\uE769" : "\uE768";
        _playButton.Foreground = Store.Playing ? UiKit.Res("Ok") : UiKit.Res("Txt");
        _frameInfo.Text = UiKit.T("кадр ") + (Store.Playhead + 1) + "/" + Store.SceneLength +
                          "   ·   " + Store.Project.Stage.Fps + " к/с" +
                          (Store.ActiveSymbol != null ? "   ·   " + Store.ActiveSymbol.Name : "");
    }
}

/// <summary>Небольшое расширение для создания щелчков по обычным рамкам.</summary>
internal static class BorderExtensions
{
    public static T WithClick<T>(this T element, Action<T> onClick) where T : UIElement
    {
        element.MouseLeftButtonUp += (s, e) => { onClick(element); e.Handled = true; };
        return element;
    }
}
