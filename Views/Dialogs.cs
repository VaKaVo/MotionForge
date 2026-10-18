using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MotionForge.Model;
using MotionForge.Services;

namespace MotionForge.Views;

/* ===================================================================== */
/*  Общая основа диалогов                                                 */
/* ===================================================================== */

/// <summary>Общий каркас диалогового окна: заголовок, содержимое, полоса кнопок.</summary>
public abstract class DialogBase : Window
{
    protected readonly StackPanel Body = new StackPanel();
    protected readonly Grid Footer = new Grid { Margin = new Thickness(18, 4, 18, 16) };

    protected DialogBase(string title, double width = 440)
    {
        Title = "MotionForge — " + L10n.T(title);
        Width = width;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        // Неявный стиль Window из темы не применяется к наследникам,
        // поэтому тёмный фон задаём окну явно (иначе диалог был бы белым).
        Background = (Brush)Application.Current.Resources["Bg"];
        Foreground = (Brush)Application.Current.Resources["Txt"];
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 11.5;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        UseLayoutRounding = true;

        Body.Margin = new Thickness(18, 18, 18, 6);
        var scroll = new ScrollViewer
        {
            Content = Body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 640,
        };
        DockPanel.SetDock(Footer, Dock.Bottom);
        var root = new DockPanel { LastChildFill = true };
        root.Children.Add(Footer);
        root.Children.Add(scroll);
        Content = root;
    }

    protected static TextBlock Heading(string text)
    {
        return new TextBlock
        {
            Text = L10n.T(text),
            FontSize = 16.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        };
    }

    protected static TextBlock Paragraph(string text)
    {
        return new TextBlock
        {
            Text = L10n.T(text),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
            Margin = new Thickness(0, 0, 0, 8),
        };
    }

    protected static TextBlock FieldLabel(string text)
    {
        return new TextBlock { Text = L10n.T(text), Margin = new Thickness(0, 8, 0, 3), Foreground = (Brush)Application.Current.Resources["TxtDim"] };
    }

    protected void AddButtons(params UIElement[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in buttons)
        {
            if (button is FrameworkElement fe) fe.Margin = new Thickness(8, 0, 0, 0);
            panel.Children.Add(button);
        }
        Footer.Children.Add(panel);
    }

    protected Button Primary(string text, Action onClick, bool isDefault = true)
    {
        var button = new Button
        {
            Content = L10n.T(text),
            Style = (Style)Application.Current.Resources["BtnPrimary"],
            MinWidth = 96,
            IsDefault = isDefault,
        };
        button.Click += (s, e) => onClick();
        return button;
    }

    protected Button Secondary(string text, Action onClick = null)
    {
        var button = new Button
        {
            Content = L10n.T(text),
            Style = (Style)Application.Current.Resources["Btn"],
            MinWidth = 96,
            IsCancel = onClick == null,
        };
        if (onClick != null) button.Click += (s, e) => onClick();
        return button;
    }
}

/* ===================================================================== */
/*  Ввод строки                                                           */
/* ===================================================================== */

public class TextInputDialog : DialogBase
{
    private readonly TextBox _box;

    public string Value => _box.Text;

    public TextInputDialog(string title, string initial, string hint = null) : base(title, 400)
    {
        Body.Children.Add(Heading(title));
        if (!string.IsNullOrEmpty(hint)) Body.Children.Add(Paragraph(hint));
        _box = new TextBox { Text = initial ?? "", Padding = new Thickness(8, 6, 8, 6) };
        _box.Loaded += (s, e) => { _box.Focus(); _box.SelectAll(); };
        _box.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
        };
        Body.Children.Add(_box);
        AddButtons(Secondary("Отмена"), Primary("ОК", () => DialogResult = true));
    }

    /// <summary>Быстрый вызов: возвращает введённый текст или null при отмене.</summary>
    public static string Ask(Window owner, string title, string initial, string hint = null)
    {
        var dialog = new TextInputDialog(title, initial, hint) { Owner = owner };
        return dialog.ShowDialog() == true && dialog.Value.Trim().Length > 0 ? dialog.Value.Trim() : null;
    }
}

/* ===================================================================== */
/*  Новый символ                                                          */
/* ===================================================================== */

public class SymbolDialog : DialogBase
{
    private readonly TextBox _name;
    private readonly ComboBox _kind;

    public string SymbolName => _name.Text;
    public SymbolKind Kind
    {
        get
        {
            if (_kind.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                return (SymbolKind)Enum.Parse(typeof(SymbolKind), tag);
            return SymbolKind.MovieClip;
        }
    }

    public SymbolDialog(string suggestedName) : base("Преобразовать в символ", 420)
    {
        Body.Children.Add(Heading("Новый символ"));
        Body.Children.Add(Paragraph("Выделенные объекты переедут в библиотеку, а на их месте появится экземпляр символа."));

        Body.Children.Add(FieldLabel("Имя символа"));
        _name = new TextBox { Text = suggestedName ?? "Символ" };
        _name.Loaded += (s, e) => { _name.Focus(); _name.SelectAll(); };
        Body.Children.Add(_name);

        Body.Children.Add(FieldLabel("Тип"));
        _kind = new ComboBox { Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
        _kind.Items.Add(new ComboBoxItem { Content = UiKit.T("Графический символ — кадры задаёт родитель"), Tag = "Graphic" });
        _kind.Items.Add(new ComboBoxItem { Content = UiKit.T("Клип — своя временная шкала (по умолчанию)"), Tag = "MovieClip" });
        _kind.Items.Add(new ComboBoxItem { Content = UiKit.T("Кнопка — для интерактивных элементов"), Tag = "Button" });
        _kind.SelectedIndex = 1;
        Body.Children.Add(_kind);

        AddButtons(Secondary("Отмена"), Primary("Создать", () => DialogResult = true));
    }
}

/* ===================================================================== */
/*  Свойства сцены                                                        */
/* ===================================================================== */

public class StageSettingsDialog : DialogBase
{
    public StageSettingsDialog(Store store) : base("Свойства сцены", 460)
    {
        StageInfo stage = store.Project.Stage;
        Body.Children.Add(Heading("Параметры документа"));
        Body.Children.Add(Paragraph("Размер рабочей области, частота кадров и цвет фона. Фон можно сделать прозрачным при экспорте."));

        var width = UiKit.Number(stage.Width, v => stage.Width = Math.Max(16, Math.Round(v)), 10, 80, 16, 8000);
        Body.Children.Add(FieldLabel("Ширина сцены, пиксели"));
        Body.Children.Add(width);

        var height = UiKit.Number(stage.Height, v => stage.Height = Math.Max(16, Math.Round(v)), 10, 80, 16, 8000);
        Body.Children.Add(FieldLabel("Высота сцены, пиксели"));
        Body.Children.Add(height);

        var fps = UiKit.Number(stage.Fps, v => stage.Fps = (int)Math.Max(1, Math.Min(120, Math.Round(v))), 1, 80, 1, 120);
        Body.Children.Add(FieldLabel("Частота кадров, кадров в секунду"));
        Body.Children.Add(fps);

        var background = new ColorButton(stage.Background, 60);
        background.ColorChanged = hex => stage.Background = hex;
        Body.Children.Add(FieldLabel("Цвет фона сцены"));
        Body.Children.Add(UiKit.Row(background, new TextBlock
        {
            Text = UiKit.T("Прозрачный фон задаётся при экспорте"),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
            Margin = new Thickness(8, 0, 0, 0),
        }));

        var presets = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        presets.Children.Add(Preset("1920 × 1080 (Full HD)", 1920, 1080, width, height));
        presets.Children.Add(Preset("1280 × 720 (HD)", 1280, 720, width, height));
        presets.Children.Add(Preset("960 × 600", 960, 600, width, height));
        presets.Children.Add(Preset("1080 × 1080 (квадрат)", 1080, 1080, width, height));
        Body.Children.Add(FieldLabel("Быстрые пресеты"));
        Body.Children.Add(presets);

        AddButtons(Secondary("Отмена"), Primary("Применить", () =>
        {
            store.Commit("Свойства сцены");
            store.RaiseAll();
            DialogResult = true;
        }));
    }

    private static Button Preset(string title, double w, double h, TextBox widthBox, TextBox heightBox)
    {
        var button = UiKit.SmallButton(title, "Подставить размер " + w + "×" + h, () =>
        {
            UiKit.SetNumber(widthBox, w);
            UiKit.SetNumber(heightBox, h);
        });
        button.Margin = new Thickness(0, 0, 6, 0);
        return button;
    }
}

/* ===================================================================== */
/*  Экспорт                                                               */
/* ===================================================================== */

public class ExportDialog : DialogBase
{
    private readonly Store _store;
    private readonly ComboBox _format;
    private readonly Image _preview = new Image { Stretch = Stretch.Uniform, Width = 280, Height = 158 };
    private readonly ProgressBar _progress = new ProgressBar { Height = 6, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _progressText = new TextBlock
    {
        Foreground = (Brush)Application.Current.Resources["TxtDim"],
        Margin = new Thickness(0, 4, 0, 0),
    };
    private readonly TextBlock _doneText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _export;

    private double _scale = 1;
    private int _fps;
    private int _columns = 5;
    private int _frame;
    private bool _transparent;
    private bool _makeVideo;
    private bool _cancelled;

    public ExportDialog(Store store) : base("Экспорт", 660)
    {
        _store = store;
        StageInfo stage = store.Project.Stage;
        _fps = Math.Max(1, stage.Fps);
        _frame = store.Playhead;

        Body.Children.Add(Heading("Экспорт результата"));
        Body.Children.Add(Paragraph("Сцена выгружается с учётом текущего документа: кадр PNG, анимированный GIF, спрайт-лист, " +
                                     "последовательность PNG или векторный SVG. Внешние библиотеки не нужны."));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

        /* ---- левая колонка: настройки ---- */
        var left = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };

        left.Children.Add(FieldLabel("Формат"));
        _format = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        _format.Items.Add(new ComboBoxItem { Content = UiKit.T("PNG — текущий кадр"), Tag = "png" });
        _format.Items.Add(new ComboBoxItem { Content = UiKit.T("GIF — анимация со всеми кадрами"), Tag = "gif" });
        _format.Items.Add(new ComboBoxItem { Content = UiKit.T("Спрайт-лист (одна картинка)"), Tag = "sheet" });
        _format.Items.Add(new ComboBoxItem { Content = UiKit.T("PNG-последовательность"), Tag = "seq" });
        _format.Items.Add(new ComboBoxItem { Content = UiKit.T("SVG — векторный кадр"), Tag = "svg" });
        _format.SelectedIndex = 1;
        _format.SelectionChanged += (s, e) => FormatChanged();
        left.Children.Add(_format);

        left.Children.Add(FieldLabel("Масштаб, %"));
        var scale = UiKit.Number(_scale * 100, v =>
        {
            _scale = Math.Max(5, Math.Min(600, v)) / 100.0;
            UpdatePreview();
        }, 5, 90, 5, 600, "%");
        left.Children.Add(scale);

        left.Children.Add(FieldLabel("Частота кадров GIF"));
        var fps = UiKit.Number(_fps, v => _fps = (int)Math.Max(1, Math.Min(60, v)), 1, 90, 1, 60);
        left.Children.Add(fps);

        left.Children.Add(FieldLabel("Колонок в спрайт-листе"));
        var columns = UiKit.Number(_columns, v => _columns = (int)Math.Max(1, Math.Min(64, v)), 1, 90, 1, 64);
        left.Children.Add(columns);

        left.Children.Add(FieldLabel("Кадр для PNG и SVG"));
        var frame = UiKit.Number(_frame + 1, v =>
        {
            _frame = (int)Math.Max(1, v) - 1;
            UpdatePreview();
        }, 1, 90, 1, 100000);
        left.Children.Add(frame);

        left.Children.Add(UiKit.Check("Прозрачный фон", _transparent, v =>
        {
            _transparent = v;
            UpdatePreview();
        }));
        left.Children.Add(UiKit.Check("Собрать видео из PNG через ffmpeg (если установлен)", _makeVideo, v => _makeVideo = v));

        _progress.Visibility = Visibility.Collapsed;
        _progressText.Visibility = Visibility.Collapsed;
        left.Children.Add(_progress);
        left.Children.Add(_progressText);
        left.Children.Add(_doneText);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        /* ---- правая колонка: предпросмотр ---- */
        var right = new StackPanel();
        right.Children.Add(FieldLabel("Предпросмотр кадра"));
        right.Children.Add(new Border
        {
            Background = (Brush)Application.Current.Resources["DarkInset"],
            BorderBrush = (Brush)Application.Current.Resources["Line"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            Child = _preview,
        });
        var openFolder = UiKit.SmallButton("Открыть папку файлов", "Открыть проводник рядом с проектом", OpenFolder);
        openFolder.Margin = new Thickness(0, 8, 0, 0);
        right.Children.Add(openFolder);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        Body.Children.Add(grid);

        _export = Primary("Экспортировать…", DoExport);
        AddButtons(Secondary("Отмена", () => { _cancelled = true; Close(); }), _export);

        FormatChanged();
    }

    private string Format
    {
        get
        {
            if (_format.SelectedItem is ComboBoxItem item && item.Tag is string tag) return tag;
            return "png";
        }
    }

    private void FormatChanged()
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        try
        {
            StageInfo stage = _store.Project.Stage;
            double fit = Math.Min(280 / Math.Max(1, stage.Width), 158 / Math.Max(1, stage.Height));
            _preview.Source = Exporter.RenderFrame(_store.Project, _frame, Math.Max(0.05, fit), _transparent);
        }
        catch
        {
            _preview.Source = null;
        }
    }

    private void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private void DoExport()
    {
        string path = ChoosePath();
        if (path == null) return;

        _doneText.Text = UiKit.T("");
        _progress.Value = 0;
        _progress.Visibility = Visibility.Visible;
        _progressText.Visibility = Visibility.Visible;
        _export.IsEnabled = false;
        _cancelled = false;

        Action<int, int> progress = (done, total) =>
        {
            _progress.Value = total <= 0 ? 0 : done * 100.0 / total;
            _progressText.Text = UiKit.T("Кадр ") + done + " из " + total;
            Pump();
        };
        Func<bool> cancelled = () => _cancelled;

        try
        {
            switch (Format)
            {
                case "png":
                    Exporter.ExportPng(_store.Project, path, _scale, _transparent, _frame);
                    break;
                case "gif":
                    Exporter.ExportGif(_store.Project, path, _scale, _fps, _transparent, 0, progress, cancelled);
                    break;
                case "sheet":
                    Exporter.ExportSpriteSheet(_store.Project, path, _scale, _columns, _transparent, progress);
                    break;
                case "svg":
                    Exporter.ExportSvg(_store.Project, path, _frame, _transparent);
                    break;
                default:
                    var files = Exporter.ExportPngSequence(_store.Project, path, _scale, _transparent, progress, cancelled);
                    if (_makeVideo && files.Count > 0)
                    {
                        string video = Path.ChangeExtension(path, ".mp4");
                        _progressText.Text = UiKit.T("Сборка видео через ffmpeg…");
                        Pump();
                        if (Exporter.TryMakeVideo(files[0], _fps, video, out string log))
                            _doneText.Text = UiKit.T("Видео готово: ") + video;
                        else
                            _doneText.Text = UiKit.T("ffmpeg недоступен, кадры сохранены. ") + Shorten(log);
                    }
                    else
                    {
                        _doneText.Text = UiKit.T("Сохранено кадров: ") + files.Count;
                    }
                    break;
            }

            if (_cancelled)
            {
                _doneText.Text = UiKit.T("Экспорт отменён.");
            }
            else
            {
                _doneText.Foreground = (Brush)Application.Current.Resources["Ok"];
                _doneText.Text += (string.IsNullOrEmpty(_doneText.Text) ? "Готово: " : "  ") + path;
                _lastFolder = Path.GetDirectoryName(path);
            }
        }
        catch (Exception ex)
        {
            _doneText.Foreground = (Brush)Application.Current.Resources["Err"];
            _doneText.Text = UiKit.T("Ошибка экспорта: ") + ex.Message;
        }
        finally
        {
            _progress.Value = 100;
            _export.IsEnabled = true;
        }
    }

    private string _lastFolder;

    private static string Shorten(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = text.Trim();
        return text.Length > 160 ? text.Substring(0, 160) + "…" : text;
    }

    private string ChoosePath()
    {
        var dialog = new SaveFileDialog
        {
            FileName = (string.IsNullOrWhiteSpace(_store.Project.Name) ? "animation" : _store.Project.Name) + FrameSuffix(),
            Filter = FilterFor(Format),
            OverwritePrompt = true,
        };
        if (!string.IsNullOrEmpty(_store.FilePath))
            dialog.InitialDirectory = Path.GetDirectoryName(_store.FilePath);
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string FrameSuffix()
    {
        switch (Format)
        {
            case "gif": return ".gif";
            case "sheet": return "_sheet.png";
            case "seq": return "_frame.png";
            case "svg": return ".svg";
            default: return ".png";
        }
    }

    private static string FilterFor(string format)
    {
        switch (format)
        {
            case "gif": return "Анимация GIF (*.gif)|*.gif";
            case "svg": return "Векторный рисунок (*.svg)|*.svg";
            default: return "Изображение PNG (*.png)|*.png";
        }
    }

    private void OpenFolder()
    {
        try
        {
            string folder = _lastFolder;
            if (string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(_store.FilePath))
                folder = Path.GetDirectoryName(_store.FilePath);
            if (string.IsNullOrEmpty(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
        }
        catch
        {
            // папка могла исчезнуть — не мешаем работе
        }
    }
}

/* ===================================================================== */
/*  Палитра команд                                                        */
/* ===================================================================== */

public class CommandPaletteWindow : DialogBase
{
    private readonly TextBox _search;
    private readonly ListBox _list = new ListBox { Margin = new Thickness(0, 10, 0, 0), MaxHeight = 380 };

    /// <summary>Команда, которую нужно выполнить после закрытия.</summary>
    public Cmd Chosen { get; private set; }

    public CommandPaletteWindow(IAppShell shell) : base("Палитра команд", 560)
    {
        Body.Children.Add(Heading("Палитра команд"));
        Body.Children.Add(Paragraph("Введите часть названия и нажмите Enter. Ctrl+K открывает эту палитру в любой момент."));

        _search = new TextBox { Padding = new Thickness(9, 7, 9, 7) };
        _search.TextChanged += (s, e) => Fill(_search.Text);
        _search.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Down && _list.Items.Count > 0)
            {
                _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && _list.Items.Count > 0)
            {
                _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Accept();
                e.Handled = true;
            }
        };
        Body.Children.Add(_search);

        _list.MouseDoubleClick += (s, e) => Accept();
        Body.Children.Add(_list);

        AddButtons(Secondary("Закрыть"));
        Fill("");
        Loaded += (s, e) => _search.Focus();
    }

    private void Fill(string query)
    {
        _list.Items.Clear();
        query = (query ?? "").Trim();
        foreach (Cmd command in Commands.All)
        {
            string haystack = (command.Group + " " + command.Title + " " + command.Id).ToLowerInvariant();
            if (query.Length > 0 && !haystack.Contains(query.ToLowerInvariant().Replace('ё', 'е'))) continue;

            var grid = new Grid { Width = 480 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock { Text = L10n.T(command.Title) };
            var group = new TextBlock
            {
                Text = L10n.T(command.Group) + (string.IsNullOrEmpty(command.Keys) ? "" : "   " + command.Keys),
                Foreground = (Brush)Application.Current.Resources["TxtDim"],
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            Grid.SetColumn(group, 1);
            grid.Children.Add(title);
            grid.Children.Add(group);

            var item = new ListBoxItem { Content = grid, Tag = command };
            _list.Items.Add(item);
        }
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    private void Accept()
    {
        if (_list.SelectedItem is ListBoxItem item && item.Tag is Cmd command)
        {
            Chosen = command;
            DialogResult = true;
        }
    }
}

/* ===================================================================== */
/*  Помощь                                                                */
/* ===================================================================== */

public class HelpWindow : DialogBase
{
    public HelpWindow(IAppShell shell) : base("Справка и горячие клавиши", 640)
    {
        Body.Children.Add(Heading("Горячие клавиши"));
        Body.Children.Add(Paragraph("Полный список команд смотрите в меню: каждая строка подписана своим сочетанием. " +
                                     "Ctrl+K открывает палитру команд — там можно найти любую функцию по названию."));

        foreach (string group in Commands.GroupNames)
        {
            var commands = Commands.All.Where(c => c.Group == group).ToList();
            if (commands.Count == 0) continue;

            Body.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["Panel"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Child = BuildGroup(group, commands),
            });
        }

        Body.Children.Add(new TextBlock
        {
            Text = UiKit.T("Мышь: колесо — прокрутка, Ctrl+колесо — масштаб, средняя кнопка или пробел — панорама. ") +
                   "Протяжка по числовому полю меняет значение (Shift — быстрее, Alt — точнее).",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
        });

        AddButtons(Secondary("Закрыть"));
    }

    private static UIElement BuildGroup(string group, List<Cmd> commands)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = group,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5),
        });
        foreach (Cmd command in commands)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock { Text = L10n.T(command.Title), TextWrapping = TextWrapping.Wrap };
            var keys = new TextBlock
            {
                Text = command.Keys ?? "",
                Foreground = (Brush)Application.Current.Resources["Accent2"],
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(keys, 1);
            row.Children.Add(title);
            row.Children.Add(keys);
            panel.Children.Add(row);
        }
        return panel;
    }
}

public class AboutWindow : DialogBase
{
    public AboutWindow() : base("О программе", 520)
    {
        Body.Children.Add(Heading("MotionForge"));
        Body.Children.Add(Paragraph("Домашняя студия двумерной анимации для Windows. Кадры, ключевые кадры, " +
                                     "классические твины, символы и библиотека — привычный рабочий процесс, собранный заново " +
                                     "на C# и WPF без единой сторонней библиотеки."));

        Body.Children.Add(new TextBlock
        {
            Text = UiKit.T("Возможности\n") +
                   "  ·  Слои, кадры, ключевые кадры и классические твины с плавностью\n" +
                   "  ·  Символы (графические, клипы, кнопки) и библиотека с предпросмотром\n" +
                   "  ·  Рисование: карандаш, кисть, перо, фигуры, заливка, ластик, пипетка\n" +
                   "  ·  Текст с правкой прямо на сцене\n" +
                   "  ·  Луковая кожа, сетка, контуры, привязка, панорама и масштаб\n" +
                   "  ·  Отмена и повтор на 90 шагов, автосохранение и восстановление\n" +
                   "  ·  Экспорт: PNG, анимированный GIF, спрайт-лист, PNG-секвенция, SVG\n" +
                   "  ·  Горячие клавиши, палитра команд (Ctrl+K), тёмная тема",
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19,
            Margin = new Thickness(0, 0, 0, 10),
        });

        Body.Children.Add(Paragraph("Лицензия MIT: код можно свободно использовать, изменять и распространять. " +
                                     "Название и интерфейс — оригинальные, ничего чужого в проекте нет."));

        Body.Children.Add(new TextBlock
        {
            Text = UiKit.T("Версия 1.0  ·  .NET 10 / WPF  ·  ") + Environment.OSVersion.VersionString,
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
        });

        AddButtons(Secondary("Закрыть"));
    }
}

/* ===================================================================== */
/*  Настройки                                                             */
/* ===================================================================== */

/// <summary>Единое окно настроек: язык, сцена, луковая кожа, рисование, планшет, автосохранение.</summary>
public class SettingsDialog : DialogBase
{
    private AppSettings _draft;
    private readonly Action<AppSettings> _preview;

    public AppSettings Result { get; private set; }

    public SettingsDialog(AppSettings current, Action<AppSettings> preview = null)
        : base("Настройки", 560)
    {
        _draft = (current ?? new AppSettings()).Clone();
        _preview = preview;
        Build();
    }

    private void Build()
    {
        Body.Children.Clear();
        Body.Children.Add(Heading("Настройки"));
        Body.Children.Add(Paragraph("Настройки хранятся в файле %APPDATA%\\MotionForge\\settings.json " +
                                     "и применяются сразу после нажатия «Готово»."));

        /* ---------------------------- общие ---------------------------- */
        AddSection("Общие");
        AddRow("Язык интерфейса", UiKit.Combo(
            L10n.Languages().Select(l => (l.Code, l.Name)).ToList(),
            _draft.Language,
            value => { _draft.Language = value; if (_preview != null) { _preview(_draft); Build(); } },
            210));
        AddCheck("Подсказки в строке состояния", _draft.ShowStatusHints, v => _draft.ShowStatusHints = v);
        AddCheck("Спрашивать при выходе с несохранёнными изменениями", _draft.ConfirmOnExit, v => _draft.ConfirmOnExit = v);

        /* ---------------------------- сцена ---------------------------- */
        AddSection("Сцена");
        AddCheck("Показывать сетку", _draft.GridOn, v => _draft.GridOn = v);
        AddRow("Шаг сетки", UiKit.Number(_draft.GridStep, v => _draft.GridStep = v, 5, 78, 2, 400));
        AddCheck("Привязывать новые фигуры к сетке", _draft.SnapToGrid, v => _draft.SnapToGrid = v);
        AddCheck("Показывать только контуры объектов", _draft.OutlineAll, v => _draft.OutlineAll = v);

        /* ------------------------ луковая кожа ------------------------- */
        AddSection("Луковая кожа");
        AddCheck("Показывать соседние кадры", _draft.OnionOn, v => _draft.OnionOn = v);
        AddRow("Кадров до и после",
            UiKit.Number(_draft.OnionBefore, v => _draft.OnionBefore = (int)Math.Round(v), 1, 60, 0, 5),
            UiKit.Number(_draft.OnionAfter, v => _draft.OnionAfter = (int)Math.Round(v), 1, 60, 0, 5));

        /* --------------------------- рисование ------------------------- */
        AddSection("Рисование");
        AddRow("Толщина линии", UiKit.Number(_draft.StrokeWidth, v => _draft.StrokeWidth = v, 0.5, 78, 0.1, 200));
        AddRow("Размер кисти", UiKit.Number(_draft.BrushSize, v => _draft.BrushSize = v, 1, 78, 1, 400));
        AddCheck("Сглаживать линии карандаша и кисти", _draft.SmoothStrokes, v => _draft.SmoothStrokes = v);

        /* ---------------------------- планшет -------------------------- */
        AddSection("Графический планшет");
        AddCheck("Обрабатывать перо отдельно от мыши", _draft.TabletEnabled, v => _draft.TabletEnabled = v);
        AddCheck("Учитывать силу нажатия пера (толщина линии)", _draft.TabletPressure, v => _draft.TabletPressure = v);
        AddCheck("Игнорировать касания пальцем (защита от ладони)", _draft.TabletIgnoreTouch, v => _draft.TabletIgnoreTouch = v);
        Body.Children.Add(new TextBlock
        {
            Text = TabletInfo(),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
            Margin = new Thickness(0, 6, 0, 2),
        });

        /* ------------------------- автосохранение ---------------------- */
        AddSection("Автосохранение");
        AddRow("Интервал в минутах (0 — выключено)",
            UiKit.Number(_draft.AutosaveMinutes, v => _draft.AutosaveMinutes = (int)Math.Round(v), 1, 60, 0, 60));

        AddButtons(
            Primary("Готово", () =>
            {
                Result = _draft.Normalized();
                DialogResult = true;
            }),
            Secondary("Отмена"),
            Secondary("По умолчанию", () =>
            {
                _draft = new AppSettings { Language = _draft.Language };
                Build();
            }));
    }

    private void AddSection(string title)
    {
        Body.Children.Add(new TextBlock
        {
            Text = L10n.T(title).ToUpperInvariant(),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["Accent2"],
            Margin = new Thickness(0, 14, 0, 2),
        });
        Body.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["Line"],
            Margin = new Thickness(0, 0, 0, 4),
        });
    }

    private void AddCheck(string label, bool value, Action<bool> onChange)
    {
        var check = UiKit.Check(label, value, onChange);
        check.Margin = new Thickness(0, 5, 0, 0);
        Body.Children.Add(check);
    }

    private void AddRow(string label, params UIElement[] fields)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock
        {
            Text = L10n.T(label),
            Width = 250,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["Txt"],
        });
        foreach (var field in fields)
        {
            if (field is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(field);
        }
        Body.Children.Add(row);
    }

    /// <summary>Какие перьевые устройства видит система (подсказка в разделе «Планшет»).</summary>
    private static string TabletInfo()
    {
        try
        {
            var devices = Tablet.TabletDevices;
            if (devices == null || devices.Count == 0)
                return L10n.T("Перьевые устройства не обнаружены: работают мышь и тачпад. Подключите планшет — и перо начнёт работать сразу.");

            var names = new List<string>();
            foreach (TabletDevice device in devices)
            {
                string kind = device.Type == TabletDeviceType.Touch ? L10n.T("сенсорный ввод") : L10n.T("перо");
                names.Add(device.Name + " (" + kind + ")");
            }
            return L10n.T("Найдено устройств: ") + devices.Count + " — " + string.Join("; ", names) + ".";
        }
        catch
        {
            return L10n.T("Сведения об устройствах ввода недоступны.");
        }
    }
}
