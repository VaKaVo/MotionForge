using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        _format.Items.Add(new ComboBoxItem { Content = UiKit.T("Спрайт-лист (PNG + XML)"), Tag = "sheet" });
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

        string sheetXml = null;

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
                    sheetXml = Exporter.ExportSpriteSheet(_store.Project, path, _scale, _columns, _transparent, progress);
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
                if (sheetXml != null) _doneText.Text += "  +  " + Path.GetFileName(sheetXml);
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
            Text = string.Format(UiKit.T("Версия {0}  ·  .NET 10 / WPF  ·  {1}"), Updates.VersionText,
                Environment.OSVersion.VersionString),
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
        });

        AddButtons(Secondary("Закрыть"));
    }
}

/* ===================================================================== */
/*  Обновление приложения                                                 */
/* ===================================================================== */

/// <summary>Окно «Помощь → Проверить обновления…»: описание релиза, загрузка и установка.</summary>
public class UpdateWindow : DialogBase
{
    private readonly UpdateInfo _info;
    private readonly Grid _bar;
    private readonly Border _barTrack;
    private readonly Border _barFill;
    private readonly TextBlock _status;
    private readonly Button _install;
    private double _fraction;
    private bool _indeterminate;

    public UpdateWindow(UpdateInfo info) : base("Обновление MotionForge", 560)
    {
        _info = info;

        Body.Children.Add(Heading(string.Format(L10n.T("Доступна версия {0}"), info.Version)));
        Body.Children.Add(Paragraph(string.Format(
            L10n.T("Сейчас установлена версия {0}. Обновление скачается с GitHub и установится автоматически — приложение перезапустится."),
            Updates.VersionText)));

        Body.Children.Add(new TextBlock
        {
            Text = UiKit.T("Что нового"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 5),
        });

        Body.Children.Add(new TextBox
        {
            Text = PlainNotes(info.Notes),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 230,
            Padding = new Thickness(8, 6, 8, 6),
            Background = (Brush)Application.Current.Resources["DarkInset"],
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
            BorderBrush = (Brush)Application.Current.Resources["Line2"],
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11.5,
        });

        _bar = new Grid { Height = 6, Margin = new Thickness(0, 12, 0, 6), Visibility = Visibility.Collapsed };
        _barTrack = new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = (Brush)Application.Current.Resources["DarkInset"],
        };
        _barFill = new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = (Brush)Application.Current.Resources["Accent2"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
        };
        _bar.Children.Add(_barTrack);
        _bar.Children.Add(_barFill);
        _barTrack.SizeChanged += (s, e) => UpdateBar();
        Body.Children.Add(_bar);

        _status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
            MinHeight = 18,
        };
        Body.Children.Add(_status);

        _install = Primary("Обновить сейчас", Install, false);
        if (string.IsNullOrEmpty(info.AssetUrl))
        {
            _install.IsEnabled = false;
            _status.Text = L10n.T("В этом релизе нет сборки для Windows — скачайте её со страницы релиза.");
        }

        AddButtons(
            Secondary("Страница релиза", () => Updates.OpenPage(_info.PageUrl)),
            Secondary("Позже"),
            Secondary("Пропустить версию", Skip),
            _install);
    }

    private void UpdateBar()
    {
        _barFill.Width = Math.Max(0, _barTrack.ActualWidth * (_indeterminate ? 1 : _fraction));
    }

    private void SetProgress(double fraction)
    {
        _fraction = Math.Max(0, Math.Min(1, fraction));
        UpdateBar();
    }

    private async void Install()
    {
        if (string.IsNullOrEmpty(_info.AssetUrl)) return;
        _install.IsEnabled = false;
        _bar.Visibility = Visibility.Visible;
        _indeterminate = false;
        SetProgress(0);
        _status.Text = L10n.T("Загрузка обновления…");

        try
        {
            var progress = new Progress<double>(value =>
            {
                SetProgress(value);
                _status.Text = string.Format(L10n.T("Загрузка обновления… {0}%"), (int)Math.Round(value * 100));
            });

            string zip = await Updates.DownloadAsync(_info, progress);

            _indeterminate = true;
            _status.Text = L10n.T("Установка…");
            UpdateBar();
            await Task.Delay(150);

            if (!Updates.TryInstall(zip, out string error))
            {
                _indeterminate = false;
                _bar.Visibility = Visibility.Collapsed;
                _status.Text = error;
                _install.IsEnabled = true;
                return;
            }

            _status.Text = L10n.T("Готово. Приложение перезапустится автоматически.");
            await Task.Delay(800);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _indeterminate = false;
            _bar.Visibility = Visibility.Collapsed;
            _status.Text = string.Format(L10n.T("Не удалось скачать обновление: {0}"), ex.Message);
            _install.IsEnabled = true;
        }
    }

    private void Skip()
    {
        AppSettings.Current.SkippedVersion = _info.Version;
        AppSettings.Current.Save();
        Close();
    }

    /// <summary>Убирает разметку Markdown, чтобы описание читалось в обычном текстовом поле.</summary>
    private static string PlainNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return L10n.T("Описание изменений не указано.");

        string text = notes.Replace("\r\n", "\n").Replace("**", "").Replace("__", "").Replace("`", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, "(?m)^\\s{0,3}#{1,6}\\s*", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, "(?m)^\\s*[-*]\\s+", "• ");
        text = System.Text.RegularExpressions.Regex.Replace(text, "(?m)^\\s*---\\s*$", "────────────");
        return text.Trim();
    }
}

/* ===================================================================== */
/*  Настройки                                                             */
/* ===================================================================== */

/// <summary>Окно «Правка → Настройки…»: разделы и поиск слева, содержимое справа,
/// кнопки ОК / Отмена / Применить — как в «Параметрах» Windows.</summary>
public class SettingsDialog : DialogBase
{
    private sealed class Section
    {
        public string Title;
        public string About;
        public Action<StackPanel> Fill;
        public Action<AppSettings> Reset;
        public Func<string> Words;
    }

    private readonly List<Section> _sections = new List<Section>();
    private readonly StackPanel _nav = new StackPanel();
    private readonly StackPanel _content = new StackPanel();
    private readonly TextBox _search = new TextBox
    {
        Padding = new Thickness(31, 5, 9, 5),
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _hint = new TextBlock
    {
        Foreground = (Brush)Application.Current.Resources["Accent2"],
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        FontSize = 11,
        Margin = new Thickness(0, 0, 12, 0),
    };

    private AppSettings _draft;
    private int _current;

    public AppSettings Result { get; private set; }

    /// <summary>«Применить»: оболочка сразу переносит настройки в сессию (язык — только по «ОК»).</summary>
    public Action<AppSettings> OnApply { get; set; }

    public SettingsDialog(AppSettings current) : base("Настройки", 760)
    {
        _draft = (current ?? new AppSettings()).Clone();
        Width = 760;
        Height = 620;
        SizeToContent = SizeToContent.Manual;
        BuildSections();
        BuildShell();
        RebuildNav();
        Loaded += (s, e) => _search.Focus();   // курсор сразу в поиске, как в «Параметрах»
    }

    /* ----------------------------- каркас ----------------------------- */

    private void BuildShell()
    {
        var header = new Border
        {
            Background = (Brush)Application.Current.Resources["Bg2"],
            Padding = new Thickness(20, 13, 20, 12),
            Child = new TextBlock
            {
                Text = L10n.T("Настройки"),
                FontSize = 16.5,
                FontWeight = FontWeights.SemiBold,
            },
        };
        DockPanel.SetDock(header, Dock.Top);

        /* поиск по настройкам */
        var search = new Grid
        {
            Margin = new Thickness(18, 12, 18, 6),
        };
        search.Children.Add(_search);

        var glass = Icons.CreatePath("search", (Brush)Application.Current.Resources["TxtDim"], 1.3);
        glass.Width = 14;
        glass.Height = 14;
        glass.HorizontalAlignment = HorizontalAlignment.Left;
        glass.VerticalAlignment = VerticalAlignment.Center;
        glass.Margin = new Thickness(11, 0, 0, 0);
        glass.IsHitTestVisible = false;
        search.Children.Add(glass);

        var placeholder = new TextBlock
        {
            Text = L10n.T("Поиск настроек…"),
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(32, 0, 0, 0),
            IsHitTestVisible = false,
        };
        search.Children.Add(placeholder);
        _search.TextChanged += (s, e) =>
        {
            placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RebuildNav();
        };
        DockPanel.SetDock(search, Dock.Top);

        /* список разделов и содержимое */
        var split = new Grid { Margin = new Thickness(18, 4, 18, 8) };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(198) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var navScroll = new ScrollViewer
        {
            Content = _nav,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(navScroll, 0);
        split.Children.Add(navScroll);

        var divider = new Border
        {
            Width = 1,
            Background = (Brush)Application.Current.Resources["Line2"],
            Opacity = 0.55,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(6, 2, 6, 2),
        };
        Grid.SetColumn(divider, 1);
        split.Children.Add(divider);

        var contentScroll = new ScrollViewer
        {
            Content = _content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(15, 2, 4, 2),
        };
        Grid.SetColumn(contentScroll, 2);
        split.Children.Add(contentScroll);

        /* кнопки */
        var bar = new Grid { Margin = new Thickness(18, 6, 18, 14) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(_hint);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var ok = Primary("ОК", () => { Result = _draft.Normalized(); DialogResult = true; });
        var cancel = Secondary("Отмена");
        var apply = Secondary("Применить", ApplyDraft);
        foreach (Button button in new[] { ok, cancel, apply })
        {
            button.MinWidth = 92;
            button.Margin = new Thickness(8, 0, 0, 0);
            button.Padding = new Thickness(14, 5, 14, 5);
            buttons.Children.Add(button);
        }
        Grid.SetColumn(buttons, 1);
        bar.Children.Add(buttons);
        DockPanel.SetDock(bar, Dock.Bottom);

        var root = new DockPanel { LastChildFill = true };
        root.Children.Add(header);
        root.Children.Add(search);
        root.Children.Add(bar);
        root.Children.Add(split);
        Content = root;
    }

    /* ---------------------------- разделы ----------------------------- */

    private void BuildSections()
    {
        _sections.Add(new Section
        {
            Title = "Общие",
            About = "Язык интерфейса, подсказки в строке состояния, поведение при закрытии и проверка обновлений.",
            Words = () => Join("Язык интерфейса", "Подсказки в строке состояния",
                               "Спрашивать при выходе с несохранёнными изменениями",
                               "Проверять обновления при запуске", "Настройки"),
            Fill = host =>
            {
                var language = UiKit.Combo(
                    L10n.Languages().Select(l => (l.Code, l.Name)).ToList(),
                    _draft.Language,
                    value => { _draft.Language = value; UpdateHint(); },
                    240);
                language.MaxDropDownHeight = 380;      // весь список языков без прокрутки
                Row(host, "Язык интерфейса", language);
                Check(host, "Подсказки в строке состояния", _draft.ShowStatusHints, v => _draft.ShowStatusHints = v);
                Check(host, "Спрашивать при выходе с несохранёнными изменениями", _draft.ConfirmOnExit, v => _draft.ConfirmOnExit = v);
                Check(host, "Проверять обновления при запуске", _draft.CheckUpdates, v => _draft.CheckUpdates = v);
                Check(host, "Показывать заставку при запуске", _draft.ShowSplash, v => _draft.ShowSplash = v);
                Note(host, "Файл настроек: %APPDATA%\\MotionForge\\settings.json");
            },
            Reset = s =>
            {
                s.Language = "ru";
                s.ShowStatusHints = true;
                s.ConfirmOnExit = true;
                s.CheckUpdates = true;
                s.ShowSplash = true;
            },
        });

        _sections.Add(new Section
        {
            Title = "Сцена",
            About = "Сетка на рабочей области и то, как показывать объекты.",
            Words = () => Join("Показывать сетку", "Шаг сетки", "Привязывать новые фигуры к сетке",
                               "Показывать только контуры объектов", "Сцена"),
            Fill = host =>
            {
                Check(host, "Показывать сетку", _draft.GridOn, v => _draft.GridOn = v);
                Row(host, "Шаг сетки", UiKit.Number(_draft.GridStep, v => _draft.GridStep = v, 5, 78, 2, 400));
                Check(host, "Привязывать новые фигуры к сетке", _draft.SnapToGrid, v => _draft.SnapToGrid = v);
                Check(host, "Показывать только контуры объектов", _draft.OutlineAll, v => _draft.OutlineAll = v);
            },
            Reset = s =>
            {
                s.GridOn = false;
                s.GridStep = 40;
                s.SnapToGrid = false;
                s.OutlineAll = false;
            },
        });

        _sections.Add(new Section
        {
            Title = "Луковая кожа",
            About = "Полупрозрачные кадры до и после текущего: так проще рисовать движение.",
            Words = () => Join("Показывать соседние кадры", "Кадров до", "Кадров после", "Луковая кожа"),
            Fill = host =>
            {
                Check(host, "Показывать соседние кадры", _draft.OnionOn, v => _draft.OnionOn = v);
                Row(host, "Кадров до", UiKit.Number(_draft.OnionBefore, v => _draft.OnionBefore = (int)Math.Round(v), 1, 60, 0, 5));
                Row(host, "Кадров после", UiKit.Number(_draft.OnionAfter, v => _draft.OnionAfter = (int)Math.Round(v), 1, 60, 0, 5));
            },
            Reset = s =>
            {
                s.OnionOn = false;
                s.OnionBefore = 1;
                s.OnionAfter = 1;
            },
        });

        _sections.Add(new Section
        {
            Title = "Рисование",
            About = "Инструменты рисования: толщина линий, кисти, ластик, сглаживание и глубина отмены.",
            Words = () => Join("Толщина линии", "Размер кисти", "Сглаживать линии карандаша и кисти", "Рисование",
                               "Стиль кисти", "Размер ластика", "Сглаживание штриха", "Глубина отмены", "Отмена"),
            Fill = host =>
            {
                Row(host, "Толщина линии", UiKit.Number(_draft.StrokeWidth, v => _draft.StrokeWidth = v, 0.5, 78, 0.1, 200));
                Row(host, "Размер кисти", UiKit.Number(_draft.BrushSize, v => _draft.BrushSize = v, 1, 78, 1, 400));
                Row(host, "Стиль кисти", UiKit.Combo(
                    new List<(string, string)>
                    {
                        ("pencil", L10n.T("Карандаш")),
                        ("brush", L10n.T("Кисть")),
                        ("marker", L10n.T("Маркер")),
                        ("calligraphy", L10n.T("Каллиграфия")),
                        ("airbrush", L10n.T("Аэрограф")),
                    },
                    string.IsNullOrEmpty(_draft.BrushPreset) ? "brush" : _draft.BrushPreset,
                    v => _draft.BrushPreset = v,
                    200));
                Row(host, "Размер ластика", UiKit.Number(_draft.EraserSize, v => _draft.EraserSize = v, 1, 78, 4, 400));
                Row(host, "Сглаживание штриха", UiKit.Number(_draft.Smoothing, v => _draft.Smoothing = v, 1, 78, 0, 100));
                Row(host, "Глубина отмены", UiKit.Number(_draft.UndoLimit, v => _draft.UndoLimit = (int)Math.Round(v), 1, 78, 10, 500));
                Check(host, "Сглаживать линии карандаша и кисти", _draft.SmoothStrokes, v => _draft.SmoothStrokes = v);
                Note(host, "100 шагов отмены требуют больше памяти, но позволяют вернуться дальше назад.");
            },
            Reset = s =>
            {
                s.StrokeWidth = 1;
                s.BrushSize = 16;
                s.BrushPreset = "brush";
                s.EraserSize = 28;
                s.Smoothing = 50;
                s.UndoLimit = 90;
                s.SmoothStrokes = true;
            },
        });

        _sections.Add(new Section
        {
            Title = "Графический планшет",
            About = "Перо обрабатывается отдельно от мыши и может менять толщину от силы нажатия.",
            Words = () => Join("Обрабатывать перо отдельно от мыши", "Учитывать силу нажатия пера",
                               "Игнорировать касания пальцем", "Графический планшет", "Перо", "Стилус"),
            Fill = host =>
            {
                Check(host, "Обрабатывать перо отдельно от мыши", _draft.TabletEnabled, v => _draft.TabletEnabled = v);
                Check(host, "Учитывать силу нажатия пера (толщина линии)", _draft.TabletPressure, v => _draft.TabletPressure = v);
                Check(host, "Игнорировать касания пальцем (защита от ладони)", _draft.TabletIgnoreTouch, v => _draft.TabletIgnoreTouch = v);
                Note(host, TabletInfo());
            },
            Reset = s =>
            {
                s.TabletEnabled = true;
                s.TabletPressure = true;
                s.TabletIgnoreTouch = true;
            },
        });

        _sections.Add(new Section
        {
            Title = "Автосохранение",
            About = "Резервная копия проекта на случай сбоя.",
            Words = () => Join("Интервал в минутах", "Автосохранение", "Резервная копия"),
            Fill = host =>
            {
                Row(host, "Интервал в минутах", UiKit.Number(_draft.AutosaveMinutes, v => _draft.AutosaveMinutes = (int)Math.Round(v), 1, 68, 0, 60));
                Note(host, "0 — автосохранение выключено. Копия лежит в %APPDATA%\\MotionForge\\autosave.mforge.");
            },
            Reset = s => s.AutosaveMinutes = 1,
        });

        _sections.Add(new Section
        {
            Title = "Плагины",
            About = "Плагины расширяют программу: добавляют эффекты, генераторы, инструменты и экспорт. " +
                    "Каждый плагин — папка в каталоге Plugins рядом с программой.",
            Words = () => Join("Плагины", "Python", "Команда Python", "Открыть папку плагинов",
                               "Обновить список плагинов", "Расширения"),
            Fill = host =>
            {
                Row(host, "Команда Python", UiKit.Text(_draft.PythonPath, v => _draft.PythonPath = v, 240));
                Note(host, "По умолчанию используется команда python из системы. Если Python установлен в другом месте, " +
                           "укажите полный путь к python.exe.");

                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
                var openFolder = UiKit.SmallButton("Открыть папку плагинов", "Папка рядом с программой", () => Plugins.OpenFolder());
                var reload = UiKit.SmallButton("Проверить плагины", "Перечитать список плагинов", () =>
                {
                    int count = Commands.LoadPluginCommands();
                    string info = string.Format(L10n.T("Найдено плагинов: {0}"), count);
                    if (Plugins.PythonAvailable(out string version)) info += " · " + version;
                    else info += " · " + L10n.T("Python не найден");
                    MessageBox.Show(info, "MotionForge", MessageBoxButton.OK, MessageBoxImage.Information);
                });
                reload.Margin = new Thickness(8, 0, 0, 0);
                buttons.Children.Add(openFolder);
                buttons.Children.Add(reload);
                host.Children.Add(buttons);

                Note(host, "Подробное руководство и примеры плагинов — в файле PLUGINS.md рядом с программой " +
                           "(и в репозитории проекта).");
            },
            Reset = s => s.PythonPath = "python",
        });
    }

    /* ---------------------------- навигация --------------------------- */

    private void RebuildNav()
    {
        _nav.Children.Clear();
        string query = _search.Text.Trim();
        var matches = new List<int>();
        for (int i = 0; i < _sections.Count; i++)
            if (query.Length == 0 || Matches(i, query)) matches.Add(i);

        if (matches.Count == 0)
        {
            _nav.Children.Add(new TextBlock
            {
                Text = L10n.T("Ничего не найдено"),
                Foreground = (Brush)Application.Current.Resources["TxtDim"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 10, 10, 0),
            });
            _content.Children.Clear();
            return;
        }

        if (!matches.Contains(_current)) _current = matches[0];
        foreach (int index in matches) _nav.Children.Add(NavItem(index));
        FillContent();
    }

    private bool Matches(int index, string query)
    {
        Section section = _sections[index];
        var haystack = new List<string> { section.Title, L10n.T(section.Title) };
        if (!string.IsNullOrEmpty(section.About)) haystack.Add(L10n.T(section.About));
        if (section.Words != null) haystack.Add(section.Words());
        return haystack.Any(text => !string.IsNullOrEmpty(text) &&
                                    text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0);
    }

    private Border NavItem(int index)
    {
        bool selected = index == _current;
        var item = new Border
        {
            Padding = new Thickness(10, 6, 8, 6),
            Margin = new Thickness(0, 1, 0, 1),
            CornerRadius = new CornerRadius(3),
            Background = selected ? (Brush)Application.Current.Resources["Panel2"] : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = L10n.T(_sections[index].Title),
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources[selected ? "Accent2" : "Txt"],
            },
        };
        if (!selected)
        {
            item.MouseEnter += (s, e) => item.Background = (Brush)Application.Current.Resources["Panel"];
            item.MouseLeave += (s, e) => item.Background = Brushes.Transparent;
        }
        item.MouseLeftButtonUp += (s, e) =>
        {
            if (_current == index) return;
            _current = index;
            RebuildNav();
        };
        return item;
    }

    private void FillContent()
    {
        _content.Children.Clear();
        if (_current < 0 || _current >= _sections.Count) return;
        Section section = _sections[_current];

        _content.Children.Add(new TextBlock
        {
            Text = L10n.T(section.Title),
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3),
        });
        if (!string.IsNullOrEmpty(section.About))
            _content.Children.Add(new TextBlock
            {
                Text = L10n.T(section.About),
                Foreground = (Brush)Application.Current.Resources["TxtDim"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            });

        var host = new StackPanel();
        section.Fill(host);
        _content.Children.Add(host);

        var reset = new Button
        {
            Content = L10n.T("Сбросить раздел"),
            Style = (Style)Application.Current.Resources["Btn"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 18, 0, 4),
        };
        reset.Click += (s, e) =>
        {
            section.Reset(_draft);
            FillContent();
            UpdateHint();
        };
        _content.Children.Add(reset);
    }

    /* ----------------------------- строки ----------------------------- */

    private static void Row(StackPanel host, string label, params UIElement[] fields)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var caption = new TextBlock
        {
            Text = L10n.T(label),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["Txt"],
            Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(caption, 0);
        row.Children.Add(caption);

        var values = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (UIElement field in fields)
        {
            if (field is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 6, 0);
            values.Children.Add(field);
        }
        Grid.SetColumn(values, 1);
        row.Children.Add(values);
        host.Children.Add(row);
    }

    private static void Check(StackPanel host, string label, bool value, Action<bool> onChange)
    {
        var check = UiKit.Check(label, value, onChange);
        check.Margin = new Thickness(0, 5, 0, 0);
        host.Children.Add(check);
    }

    private static void Note(StackPanel host, string text)
    {
        host.Children.Add(new TextBlock
        {
            Text = L10n.T(text),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 0),
        });
    }

    private static string Join(params string[] parts) => string.Join(" ", parts);

    /* ----------------------------- кнопки ----------------------------- */

    private void ApplyDraft()
    {
        Result = _draft.Normalized();
        OnApply?.Invoke(Result);
        UpdateHint(L10n.T("Настройки применены"));
    }

    private void UpdateHint(string text = null)
    {
        bool pending = !string.Equals(_draft.Language, L10n.Current, StringComparison.OrdinalIgnoreCase);
        _hint.Text = pending ? L10n.T("Язык применится после нажатия «ОК»") : (text ?? "");
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

/* ===================================================================== */
/*  Новый документ                                                        */
/* ===================================================================== */

/// <summary>Параметры, выбранные в окне «Новый документ».</summary>
public class NewDocumentOptions
{
    public string Name = "Без названия";
    public double Width = 960;
    public double Height = 600;
    public int Fps = 24;
    public string Background = "#FFFFFF";
}

/// <summary>Окно создания документа: готовые размеры слева, параметры справа.</summary>
public class NewDocumentDialog : DialogBase
{
    private sealed class Preset
    {
        public string Category;
        public string Title;
        public double Width;
        public double Height;
        public int Fps;
        public string Background = "#FFFFFF";
    }

    private static readonly Preset[] Presets =
    {
        new Preset { Category = "Кино и видео", Title = "HD 1080p", Width = 1920, Height = 1080, Fps = 24 },
        new Preset { Category = "Кино и видео", Title = "HD 720p", Width = 1280, Height = 720, Fps = 24 },
        new Preset { Category = "Кино и видео", Title = "Презентация 4:3", Width = 1024, Height = 768, Fps = 24 },
        new Preset { Category = "Кино и видео", Title = "Сцена по умолчанию", Width = 960, Height = 600, Fps = 24 },

        new Preset { Category = "Web", Title = "Баннер", Width = 728, Height = 90, Fps = 12 },
        new Preset { Category = "Web", Title = "Обложка", Width = 1200, Height = 630, Fps = 30 },
        new Preset { Category = "Web", Title = "Постер", Width = 1080, Height = 1350, Fps = 24 },

        new Preset { Category = "Мобильные", Title = "Вертикаль", Width = 1080, Height = 1920, Fps = 30 },
        new Preset { Category = "Мобильные", Title = "История", Width = 1080, Height = 1920, Fps = 15 },
        new Preset { Category = "Мобильные", Title = "iPhone", Width = 390, Height = 844, Fps = 30 },

        new Preset { Category = "Квадрат", Title = "Квадрат 1080", Width = 1080, Height = 1080, Fps = 30 },
        new Preset { Category = "Квадрат", Title = "Квадрат 720", Width = 720, Height = 720, Fps = 24 },
        new Preset { Category = "Квадрат", Title = "Мини", Width = 512, Height = 512, Fps = 12 },
    };

    private static readonly string[] Categories = { "Кино и видео", "Web", "Мобильные", "Квадрат" };

    private readonly NewDocumentOptions _draft = new NewDocumentOptions();
    private readonly WrapPanel _cards = new WrapPanel();
    private readonly StackPanel _tabs = new StackPanel { Orientation = Orientation.Horizontal };
    private readonly StackPanel _form = new StackPanel();

    private Preset _selected;
    private string _category = "Кино и видео";
    private TextBox _nameBox;
    private TextBox _widthBox;
    private TextBox _heightBox;
    private ComboBox _fpsBox;
    private ColorButton _color;

    public NewDocumentOptions Result { get; private set; }

    public NewDocumentDialog() : base("Новый документ", 880)
    {
        Width = 880;
        Height = 600;
        SizeToContent = SizeToContent.Manual;

        _selected = Presets[3];                       // сцена по умолчанию — 960 × 600
        _draft.Name = L10n.T("Без названия");
        _draft.Width = _selected.Width;
        _draft.Height = _selected.Height;
        _draft.Fps = _selected.Fps;
        _draft.Background = _selected.Background;

        BuildShell();
        RebuildTabs();
        RebuildCards();
        UiKit.SetCombo(_fpsBox, _draft.Fps.ToString());
        _nameBox.Focus();
        _nameBox.SelectAll();
    }

    /* ----------------------------- каркас ----------------------------- */

    private void BuildShell()
    {
        var head = new StackPanel();
        head.Children.Add(new TextBlock
        {
            Text = L10n.T("Новый документ"),
            FontSize = 16.5,
            FontWeight = FontWeights.SemiBold,
        });
        head.Children.Add(new TextBlock
        {
            Text = L10n.T("Выберите готовый размер или задайте свои параметры."),
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
            Margin = new Thickness(0, 3, 0, 0),
        });
        var header = new Border
        {
            Background = (Brush)Application.Current.Resources["Bg2"],
            Padding = new Thickness(20, 12, 20, 11),
            Child = head,
        };
        DockPanel.SetDock(header, Dock.Top);

        /* пресеты */
        var left = new DockPanel { LastChildFill = true, Margin = new Thickness(18, 10, 12, 8) };
        var tabsHost = new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["Line"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, 0, 0, 9),
            Child = _tabs,
        };
        DockPanel.SetDock(tabsHost, Dock.Top);
        left.Children.Add(tabsHost);
        left.Children.Add(new ScrollViewer
        {
            Content = _cards,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        /* параметры */
        BuildForm();
        var right = new Border
        {
            Background = (Brush)Application.Current.Resources["Bg2"],
            BorderBrush = (Brush)Application.Current.Resources["Line"],
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(16, 14, 16, 12),
            Child = _form,
        };

        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(296) });
        Grid.SetColumn(left, 0);
        main.Children.Add(left);
        Grid.SetColumn(right, 1);
        main.Children.Add(right);

        /* кнопки */
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var close = Secondary("Закрыть");
        var create = Primary("Создать", CreateProject);
        foreach (Button button in new[] { close, create })
        {
            button.MinWidth = 104;
            button.Padding = new Thickness(14, 5, 14, 5);
            button.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(button);
        }
        var bar = new Grid { Margin = new Thickness(18, 6, 18, 14) };
        bar.Children.Add(buttons);
        DockPanel.SetDock(bar, Dock.Bottom);

        var root = new DockPanel { LastChildFill = true };
        root.Children.Add(header);
        root.Children.Add(bar);
        root.Children.Add(main);
        Content = root;
    }

    private void BuildForm()
    {
        _form.Children.Add(new TextBlock
        {
            Text = L10n.T("Параметры"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        });

        _form.Children.Add(FieldLabel("Имя"));
        _nameBox = UiKit.Text(_draft.Name, v => _draft.Name = v);
        _form.Children.Add(_nameBox);

        _form.Children.Add(FieldLabel("Размер, пиксели"));
        _widthBox = UiKit.Number(_draft.Width, v => _draft.Width = Math.Max(16, Math.Round(v)), 10, 108, 16, 8000);
        _heightBox = UiKit.Number(_draft.Height, v => _draft.Height = Math.Max(16, Math.Round(v)), 10, 108, 16, 8000);
        _form.Children.Add(UiKit.Row(_widthBox, new TextBlock
        {
            Text = "×",
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
        }, _heightBox));

        _form.Children.Add(FieldLabel("Частота кадров, к/с"));
        _fpsBox = UiKit.Combo(new[]
        {
            ("12", "12"), ("15", "15"), ("24", "24"), ("25", "25"), ("30", "30"), ("60", "60"),
        }, _draft.Fps.ToString(), v =>
        {
            if (int.TryParse(v, out int fps)) _draft.Fps = fps;
        }, 108);
        _form.Children.Add(_fpsBox);

        _form.Children.Add(FieldLabel("Фон сцены"));
        _color = new ColorButton(_draft.Background, 64);
        _color.ColorChanged = hex => _draft.Background = hex;
        _form.Children.Add(UiKit.Row(_color, new TextBlock
        {
            Text = L10n.T("Прозрачный фон задаётся при экспорте"),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 170,
            Margin = new Thickness(8, 0, 0, 0),
        }));

        _form.Children.Add(new TextBlock
        {
            Text = L10n.T("Размер, частоту кадров и фон можно изменить позже в свойствах сцены."),
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 22, 0, 0),
        });
    }

    /* --------------------------- пресеты ------------------------------ */

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        foreach (string category in Categories) _tabs.Children.Add(Tab(category));
    }

    private Border Tab(string category)
    {
        bool active = category == _category;
        var item = new Border
        {
            Padding = new Thickness(9, 7, 9, 6),
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = active ? (Brush)Application.Current.Resources["Accent2"] : Brushes.Transparent,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = L10n.T(category),
                Foreground = (Brush)Application.Current.Resources[active ? "Accent2" : "TxtDim"],
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            },
        };
        if (!active)
        {
            item.MouseEnter += (s, e) => item.Background = (Brush)Application.Current.Resources["Panel"];
            item.MouseLeave += (s, e) => item.Background = Brushes.Transparent;
        }
        item.MouseLeftButtonUp += (s, e) =>
        {
            if (_category == category) return;
            _category = category;
            if (_selected == null || _selected.Category != category)
            {
                _selected = Presets.First(p => p.Category == category);
                ApplyPreset(_selected);
            }
            RebuildTabs();
            RebuildCards();
        };
        return item;
    }

    private void RebuildCards()
    {
        _cards.Children.Clear();
        foreach (Preset preset in Presets)
            if (preset.Category == _category) _cards.Children.Add(Card(preset));
    }

    private Border Card(Preset preset)
    {
        bool selected = ReferenceEquals(preset, _selected);

        /* миниатюра в реальных пропорциях */
        double scale = Math.Min(30.0 / preset.Width, 30.0 / preset.Height);
        var thumb = new Grid { Width = 38, Height = 38, VerticalAlignment = VerticalAlignment.Center };
        thumb.Children.Add(new Border
        {
            Width = Math.Max(5, preset.Width * scale),
            Height = Math.Max(5, preset.Height * scale),
            Background = UiKit.Brush(preset.Background),
            BorderBrush = (Brush)Application.Current.Resources["Line2"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
        info.Children.Add(new TextBlock { Text = L10n.T(preset.Title) });
        info.Children.Add(new TextBlock
        {
            Text = preset.Width.ToString("0") + " × " + preset.Height.ToString("0") + " · " +
                   preset.Fps + " " + L10n.T("к/с"),
            Foreground = (Brush)Application.Current.Resources["TxtDim"],
            FontSize = 10.5,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(thumb);
        content.Children.Add(info);

        var card = new Border
        {
            Width = 246,
            Height = 62,
            Padding = new Thickness(9, 6, 8, 6),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources[selected ? "Accent2" : "Line"],
            Background = (Brush)Application.Current.Resources[selected ? "Panel2" : "Panel"],
            Cursor = Cursors.Hand,
            Child = content,
        };
        if (!selected)
        {
            card.MouseEnter += (s, e) => card.BorderBrush = (Brush)Application.Current.Resources["Line2"];
            card.MouseLeave += (s, e) => card.BorderBrush = (Brush)Application.Current.Resources["Line"];
        }
        card.MouseLeftButtonUp += (s, e) =>
        {
            _selected = preset;
            ApplyPreset(preset);
            RebuildCards();
        };
        return card;
    }

    private void ApplyPreset(Preset preset)
    {
        _draft.Width = preset.Width;
        _draft.Height = preset.Height;
        _draft.Fps = preset.Fps;
        _draft.Background = preset.Background;

        UiKit.SetNumber(_widthBox, preset.Width);
        UiKit.SetNumber(_heightBox, preset.Height);
        UiKit.SetCombo(_fpsBox, preset.Fps.ToString());
        _color.Color = preset.Background;
    }

    /* ---------------------------- создание ---------------------------- */

    private void CreateProject()
    {
        Result = new NewDocumentOptions
        {
            Name = string.IsNullOrWhiteSpace(_draft.Name) ? L10n.T("Без названия") : _draft.Name.Trim(),
            Width = Math.Max(16, Math.Round(_draft.Width)),
            Height = Math.Max(16, Math.Round(_draft.Height)),
            Fps = Math.Max(1, Math.Min(120, _draft.Fps)),
            Background = _draft.Background,
        };
        DialogResult = true;
    }
}

/* ===================================================================== */
/*  Плагины                                                              */
/* ===================================================================== */

/// <summary>Список найденных плагинов, состояние Python и кнопки работы с папкой.</summary>
public class PluginsWindow : DialogBase
{
    private readonly StackPanel _list = new StackPanel();
    private readonly TextBlock _pythonText;

    public PluginsWindow() : base("Плагины", 600)
    {
        Body.Children.Add(Heading("Плагины"));
        Body.Children.Add(Paragraph(
            "Плагин — это папка в каталоге Plugins рядом с программой. Внутри неё лежит plugin.json с описанием " +
            "и скрипт (обычно на Python). Плагин получает файл проекта, изменяет его и возвращает результат обратно."));

        _pythonText = Paragraph("");
        Body.Children.Add(_pythonText);
        Body.Children.Add(_list);

        var runButton = Secondary("Открыть папку плагинов", () => Plugins.OpenFolder());
        var reloadButton = Secondary("Обновить список", Reload);
        AddButtons(runButton, reloadButton, Primary("Закрыть", () => Close()));
        Reload();
    }

    /// <summary>Перечитывает папку плагинов и перестраивает список.</summary>
    public void Reload()
    {
        Plugins.Load();
        _list.Children.Clear();

        bool hasPython = Plugins.PythonAvailable(out string version);
        _pythonText.Text = hasPython
            ? string.Format(L10n.T("Python найден: {0}"), version)
            : L10n.T("Python не найден. Плагины на Python не смогут запуститься — установите Python 3 " +
                     "или укажите команду в настройках (раздел «Общие»).");

        if (Plugins.Cache.Count == 0)
        {
            _list.Children.Add(Paragraph("Плагинов не найдено. Скопируйте папку плагина в каталог Plugins и нажмите «Обновить список»."));
            return;
        }

        foreach (PluginInfo plugin in Plugins.Cache)
        {
            var title = new TextBlock
            {
                Text = plugin.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
            };
            var details = new TextBlock
            {
                Text = plugin.Ready
                    ? string.Format(L10n.T("версия {0}{1} · файл: {2}"),
                        plugin.Version,
                        string.IsNullOrEmpty(plugin.Author) ? "" : " · " + plugin.Author,
                        Path.GetFileName(plugin.Command))
                    : L10n.T("не загрузился: ") + plugin.Error,
                Foreground = (Brush)Application.Current.Resources[plugin.Ready ? "TxtDim" : "Err"],
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5,
            };
            var card = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            card.Children.Add(title);
            if (!string.IsNullOrWhiteSpace(plugin.Description))
            {
                card.Children.Add(new TextBlock
                {
                    Text = plugin.Description,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0),
                    Foreground = (Brush)Application.Current.Resources["Txt"],
                });
            }
            card.Children.Add(details);
            card.Children.Add(new TextBlock
            {
                Text = plugin.Folder,
                FontSize = 10.5,
                Foreground = (Brush)Application.Current.Resources["TxtDim"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
            var frame = new Border
            {
                Background = (Brush)Application.Current.Resources["Panel"],
                BorderBrush = (Brush)Application.Current.Resources["Line"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 6, 0, 0),
                Child = card,
            };
            if (!plugin.Ready) frame.BorderBrush = (Brush)Application.Current.Resources["Err"];
            _list.Children.Add(frame);
        }
    }
}

/// <summary>Окно с отчетом о работе плагина (вывод скрипта, ошибки).</summary>
public class LogWindow : DialogBase
{
    public LogWindow(string title, string text, bool error) : base(title, 640)
    {
        Body.Children.Add(Heading(title));
        if (error)
            Body.Children.Add(Paragraph("Плагин не смог выполнить работу. Ниже — подробности от программы."));

        var box = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(text) ? L10n.T("Готово.") : text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
            FontSize = 11.5,
            Background = (Brush)Application.Current.Resources["DarkInset"],
            Foreground = (Brush)Application.Current.Resources[error ? "Err" : "Txt"],
            BorderBrush = (Brush)Application.Current.Resources["Line"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            MinHeight = 120,
            MaxHeight = 360,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Body.Children.Add(box);
        AddButtons(Primary("Закрыть", () => Close()));
    }
}
