using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MotionForge.Model;
using MotionForge.Services;
using MotionForge.Tools;
using MotionForge.Views;

namespace MotionForge;

/// <summary>Главное окно: меню, панель инструментов, сцена, свойства, библиотека и временная шкала.</summary>
public partial class MainWindow : Window, IAppShell
{
    private Store _store;

    private readonly ToolsPanel _tools = new ToolsPanel();
    private readonly LayersPanel _layers = new LayersPanel();
    private readonly LibraryPanel _library = new LibraryPanel();
    private readonly PropertiesPanel _properties = new PropertiesPanel();
    private readonly SwatchesPanel _swatches = new SwatchesPanel();
    private readonly TimelineToolbar _timelineToolbar = new TimelineToolbar();

    private readonly Dictionary<string, Cmd> _hotkeys = new Dictionary<string, Cmd>();
    private readonly List<Button> _footerToggles = new List<Button>();

    private readonly DispatcherTimer _playTimer = new DispatcherTimer();
    private readonly DispatcherTimer _autosaveTimer = new DispatcherTimer();

    public Store Store => _store;

    /* --- порядок меню: как в Animate (без группы «Инструменты», она в панели) --- */
    private static readonly string[] MenuOrder =
    {
        "Файл", "Правка", "Вид", "Вставка", "Изменение", "Выравнивание",
        "Слои", "Воспроизведение", "Окно", "Плагины", "Помощь",
    };

    private static readonly HashSet<string> SeparatorBefore = new HashSet<string>
    {
        "file.import", "edit.copy", "edit.duplicate", "edit.selectAll", "frame.key", "frame.tween",
        "mod.symbol", "mod.front", "mod.flipH", "mod.alignLeft", "mod.distributeH",
        "layer.toggleVisible", "view.zoomIn", "view.play", "win.tools", "help.palette", "plugin.list",
    };

    private static readonly HashSet<string> AllowedWhileTyping = new HashSet<string>
    {
        "file.new", "file.open", "file.save", "file.saveAs", "file.export", "file.import", "help.palette",
    };

    private static readonly string[] StageContextMenu =
    {
        "edit.cut", "edit.copy", "edit.paste", "edit.duplicate", "edit.delete",
        "mod.group", "mod.ungroup", "mod.symbol", "mod.enterSymbol",
        "mod.front", "mod.back", "mod.forward", "mod.backward",
        "file.stage", "help.palette",
    };

    private static readonly string[] TimelineContextMenu =
    {
        "frame.key", "frame.blankKey", "frame.insert", "frame.insertMany", "frame.remove", "frame.clearKey",
        "frame.tween", "frame.tweenNone",
        "layer.add", "layer.duplicate", "layer.delete", "layer.up", "layer.down",
    };

    private static readonly string[] TimelineMenu =
    {
        "view.first", "view.prev", "view.play", "view.next", "view.last",
        "frame.insert", "frame.key", "frame.blankKey", "frame.remove", "frame.clearKey",
        "frame.tween", "frame.tweenNone",
        "layer.add", "layer.duplicate", "layer.delete", "layer.up", "layer.down",
        "win.timeline",
    };

    /* ================================================================== */
    /*  Сборка интерфейса                                                  */
    /* ================================================================== */

    public MainWindow()
    {
        InitializeComponent();

        BuildMenu();
        BuildDocks();
        BuildTimelineFooter();
        BuildStageBar();

        _timelineToolbar.PlayToggled += TogglePlay;
        TimelineToolbarHost.Children.Add(_timelineToolbar);

        Stage.RightClicked += OnStageRightClick;
        Stage.WorldMouseMoved += OnStageMouseMoved;
        Timeline.RightClicked += OnTimelineRightClick;
        Timeline.LayersChangedByUser += () =>
        {
            _layers.Refresh();
            _properties.Refresh();
            _timelineToolbar.Refresh();
        };
        Timeline.FrameWidthChanged += () => { FollowPlayhead(); StatusZoom_Update(); };
        StatusZoom.MouseLeftButtonDown += (s, e) => ZoomTo(1);

        LayersScroll.ScrollChanged += (s, e) => TimelineScroll.ScrollToVerticalOffset(e.VerticalOffset);
        TimelineScroll.ScrollChanged += (s, e) => LayersScroll.ScrollToVerticalOffset(e.VerticalOffset);

        BuildHotkeys();
        _playTimer.Tick += OnPlayTick;
        _autosaveTimer.Interval = TimeSpan.FromMinutes(1);
        _autosaveTimer.Tick += (s, e) => Autosave();

        AttachStore(new Store(SampleProject.Create()));
        ApplySettings(AppSettings.Current);
        RefreshAll();
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _autosaveTimer.Start();
        FitStage();
        Stage.Focus();
        Dispatcher.BeginInvoke(new Action(TryRestoreAutosave), DispatcherPriority.ApplicationIdle);

        // Служебный режим: выгрузить собранные строки интерфейса как шаблон перевода.
        if (L10n.TemplateRequested)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string path = L10n.DumpTemplate("template");
                Close();
            }), DispatcherPriority.ApplicationIdle);

        string[] args = Environment.GetCommandLineArgs();
        string flag = args.Length > 1 ? args[1] : "";

        if (string.Equals(flag, "--prefs", StringComparison.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(new Action(ShowSettings), DispatcherPriority.ApplicationIdle);
        else if (string.Equals(flag, "--newdoc", StringComparison.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(new Action(NewProject), DispatcherPriority.ApplicationIdle);

        if (AppSettings.Current.CheckUpdates)
            Dispatcher.BeginInvoke(new Action(AutoCheckUpdates), DispatcherPriority.ApplicationIdle);
    }

    /* ================================================================== */
    /*  Настройки                                                          */
    /* ================================================================== */

    /// <summary>Переносит настройки в текущую сессию: параметры инструментов, вид сцены, автосохранение.</summary>
    public void ApplySettings(AppSettings settings)
    {
        if (settings == null || _store == null) return;

        _store.Opts.StrokeWidth = settings.StrokeWidth;
        _store.Opts.BrushSize = settings.BrushSize;
        _store.Opts.EraserSize = Math.Max(4, settings.EraserSize);
        _store.Opts.FreehandSmooth = settings.SmoothStrokes;
        _store.Opts.BrushPreset = string.IsNullOrEmpty(settings.BrushPreset) ? "brush" : settings.BrushPreset;
        _store.Opts.Smoothing = Math.Max(0, Math.Min(100, settings.Smoothing));
        Store.UndoLimit = Math.Max(10, Math.Min(500, settings.UndoLimit));
        Plugins.PythonCommand = string.IsNullOrWhiteSpace(settings.PythonPath) ? "python" : settings.PythonPath.Trim();

        _store.OnionOn = settings.OnionOn;
        _store.OnionBefore = settings.OnionBefore;
        _store.OnionAfter = settings.OnionAfter;
        _store.GridOn = settings.GridOn;
        if (settings.GridStep > 1) Store.GridSize = settings.GridStep;
        _store.OutlineAll = settings.OutlineAll;
        _store.SnapToGrid = settings.SnapToGrid;

        if (settings.AutosaveMinutes <= 0) _autosaveTimer.Stop();
        else
        {
            _autosaveTimer.Interval = TimeSpan.FromMinutes(settings.AutosaveMinutes);
            if (IsLoaded) _autosaveTimer.Start();
        }
    }

    /// <summary>Пересобирает текстовые части интерфейса (вызывается после смены языка).</summary>
    public void RebuildUi()
    {
        MenuBar.Children.Clear();
        StageBarRight.Children.Clear();
        TimelineFooter.Children.Clear();
        TimelineToolbarHost.Children.Clear();
        _footerToggles.Clear();

        BuildMenu();
        BuildDocks();
        BuildStageBar();
        BuildTimelineFooter();
        TimelineToolbarHost.Children.Add(_timelineToolbar);
        RefreshAll();
    }

    /// <summary>Окно «Правка → Настройки…» (Ctrl+,).</summary>
    public void ShowSettings()
    {
        var dialog = new SettingsDialog(AppSettings.Current)
        {
            Owner = this,
            // «Применить»: настройки вступают в силу сразу, язык — после закрытия окна,
            // чтобы не перестраивать интерфейс под открытым модальным диалогом.
            OnApply = draft => ApplySettingsDraft(draft, false),
        };

        bool? result = dialog.ShowDialog();
        Stage.Focus();

        if (dialog.Result == null) return;                      // «Отмена» без «Применить»

        ApplySettingsDraft(dialog.Result, result == true);      // язык — только по «ОК»
        Store.Report(L10n.T(result == true ? "Настройки сохранены" : "Настройки применены"));
    }

    /// <summary>Переносит черновик настроек в сессию. Язык меняется только по «ОК»:
    /// перестраивать интерфейс под открытым модальным диалогом нельзя.</summary>
    private void ApplySettingsDraft(AppSettings draft, bool closeWithOk)
    {
        AppSettings.Current = draft.Clone();
        if (!closeWithOk) AppSettings.Current.Language = L10n.Current;   // язык ещё не применён
        AppSettings.Current.Save();

        bool languageChanged = closeWithOk &&
            !string.Equals(AppSettings.Current.Language, L10n.Current, StringComparison.OrdinalIgnoreCase);

        ApplySettings(AppSettings.Current);
        if (languageChanged)
        {
            L10n.SetLanguage(AppSettings.Current.Language);
            RebuildUi();
            UpdateTitle();
        }
        else RefreshAll();
    }

    private void StatusZoom_Update()
    {
        if (_store != null) StatusZoom.Text = Math.Round(_store.Zoom * 100) + "%";
    }

    /* ------------------------------ меню ------------------------------ */

    private void BuildMenu()
    {
        // Названия плагинов зависят от языка интерфейса — перечитываем список при сборке меню.
        Commands.LoadPluginCommands();

        var groups = new List<string>();
        foreach (string group in MenuOrder)
            if (Commands.All.Any(c => c.Group == group)) groups.Add(group);
        foreach (string group in Commands.GroupNames)
            if (group != "Инструменты" && !groups.Contains(group)) groups.Add(group);

        foreach (string group in groups)
        {
            var commands = Commands.All.Where(c => c.Group == group).ToList();
            if (commands.Count == 0) continue;

            ContextMenu menu = BuildCommandMenu(commands);
            var button = new Button
            {
                Content = L10n.T(group),
                Style = (Style)Application.Current.Resources["MenuBtn"],
                ContextMenu = menu,
            };
            button.PreviewMouseLeftButtonDown += (s, e) =>
            {
                menu.PlacementTarget = button;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
                e.Handled = true;
            };
            MenuBar.Children.Add(button);
        }
    }

    private ContextMenu BuildCommandMenu(IEnumerable<Cmd> commands)
    {
        var menu = new ContextMenu();
        foreach (Cmd command in commands)
        {
            if (SeparatorBefore.Contains(command.Id) && menu.Items.Count > 0 &&
                !(menu.Items[menu.Items.Count - 1] is Separator))
                menu.Items.Add(new Separator());

            var item = new MenuItem
            {
                Header = L10n.T(command.Title),
                InputGestureText = command.Keys,
                Tag = command,
            };
            item.Click += (s, e) => RunCommand(command);
            menu.Items.Add(item);
        }
        while (menu.Items.Count > 0 && menu.Items[menu.Items.Count - 1] is Separator)
            menu.Items.RemoveAt(menu.Items.Count - 1);
        menu.Opened += (s, e) => RefreshMenuState(menu);
        return menu;
    }

    private ContextMenu BuildCommandMenu(params string[] ids)
    {
        var menu = new ContextMenu();
        foreach (string id in ids)
        {
            if (id == "—")
            {
                if (menu.Items.Count > 0 && !(menu.Items[menu.Items.Count - 1] is Separator))
                    menu.Items.Add(new Separator());
                continue;
            }
            Cmd command = Commands.Find(id);
            if (command == null) continue;
            var item = new MenuItem { Header = L10n.T(command.Title), InputGestureText = command.Keys, Tag = command };
            item.Click += (s, e) => RunCommand(command);
            menu.Items.Add(item);
        }
        menu.Opened += (s, e) => RefreshMenuState(menu);
        return menu;
    }

    /* ------------------------------ доки ------------------------------ */

    private void BuildDocks()
    {
        // Инструменты
        ToolsHost.Content = _tools;
        ToolsHeaderHost.Content = UiKit.PanelHeader("", BuildCommandMenu(Commands.All.Where(c => c.Group == "Инструменты").ToArray()));

        // Слои (колонка таймлайна)
        LayersHost.Content = _layers;

        // Свойства
        var propsMenu = new ContextMenu();
        propsMenu.Items.Add(UiKit.Menu("Свойства сцены…", () => ShowStageSettings()));
        propsMenu.Items.Add(UiKit.Menu("Вписать сцену (Ctrl+0)", FitStage));
        propsMenu.Items.Add(new Separator());
        propsMenu.Items.Add(UiKit.Menu("Закрыть панель", () => SetPanelVisible("props", false)));
        PropertiesHeaderHost.Content = UiKit.PanelHeader("СВОЙСТВА", propsMenu);
        PropertiesBodyHost.Content = _properties;

        // Библиотека
        var libraryMenu = new ContextMenu();
        libraryMenu.Items.Add(UiKit.Menu("Преобразовать в символ (F8)", CreateSymbolFromSelection));
        libraryMenu.Items.Add(UiKit.Menu("Обновить список", () => _library.Refresh()));
        libraryMenu.Items.Add(new Separator());
        libraryMenu.Items.Add(UiKit.Menu("Закрыть панель", () => SetPanelVisible("library", false)));
        LibraryHeaderHost.Content = UiKit.PanelHeader("БИБЛИОТЕКА", libraryMenu);
        LibraryBodyHost.Content = _library;

        // Образцы
        var swatchMenu = new ContextMenu();
        swatchMenu.Items.Add(UiKit.Menu("Закрыть панель", () => SetPanelVisible("swatches", false)));
        SwatchesHeaderHost.Content = UiKit.PanelHeader("ОБРАЗЦЫ", swatchMenu);
        SwatchesHost.Content = _swatches;
    }

    private void BuildStageBar()
    {
        StageBarRight.Children.Add(UiKit.IconButton("zoomOut", "Уменьшить (Ctrl+-)", () => ZoomBy(1 / 1.25), 22));
        StageBarRight.Children.Add(UiKit.IconButton("zoomIn", "Увеличить (Ctrl++)", () => ZoomBy(1.25), 22));
        StageBarRight.Children.Add(UiKit.IconButton("zoomFit", "Вписать сцену (Ctrl+0)", FitStage, 22));
    }

    private void BuildTimelineFooter()
    {
        TimelineFooter.Children.Add(FooterToggle("onion", "Луковая кожа",
            "Показывать соседние кадры полупрозрачно (Alt+O)",
            () => _store.OnionOn, v => _store.OnionOn = v));

        TimelineFooter.Children.Add(new Border
        {
            Width = 1,
            Background = UiKit.Res("Line"),
            Margin = new Thickness(6, 3, 6, 3),
        });

        TimelineFooter.Children.Add(new TextBlock
        {
            Text = UiKit.T("Кадры"),
            FontSize = 10,
            Foreground = UiKit.Res("TxtDim"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        });
        TimelineFooter.Children.Add(UiKit.SmallButton("−", "Уже кадры", () =>
        {
            Timeline.SetFrameWidthInternal(Timeline.FrameWidth - 2);
            StatusZoom_Update();
        }));
        TimelineFooter.Children.Add(UiKit.SmallButton("＋", "Шире кадры", () =>
        {
            Timeline.SetFrameWidthInternal(Timeline.FrameWidth + 2);
            StatusZoom_Update();
        }));

        TimelineFooter.Children.Add(new Border
        {
            Width = 1,
            Background = UiKit.Res("Line"),
            Margin = new Thickness(6, 3, 6, 3),
        });
        TimelineFooter.Children.Add(FooterToggle("grid", "Сетка", "Показать сетку сцены (Ctrl+')",
            () => _store.GridOn, v => _store.GridOn = v));
        TimelineFooter.Children.Add(FooterToggle("outline", "Контуры", "Показать только контуры объектов (Ctrl+Shift+O)",
            () => _store.OutlineAll, v => _store.OutlineAll = v));
    }

    private Button FooterToggle(string iconId, string text, string tooltip, Func<bool> get, Action<bool> set)
    {
        text = UiKit.T(text);
        tooltip = UiKit.T(tooltip);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var path = Icons.CreatePath(iconId, UiKit.Res("Txt"), 1.4);
        path.Width = 13;
        path.Height = 13;
        path.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(path);
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var button = new Button
        {
            Style = (Style)Application.Current.Resources["ToolBtn"],
            Width = double.NaN,
            Height = 18,
            Padding = new Thickness(5, 0, 5, 0),
            Margin = new Thickness(0, 0, 2, 0),
            Content = content,
            ToolTip = tooltip,
        };
        button.Click += (s, e) => { set(!get()); RefreshAll(); };
        _footerToggles.Add(button);
        return button;
    }

    /* ================================================================== */
    /*  Состояние документа                                               */
    /* ================================================================== */

    private void AttachStore(Store store)
    {
        if (_store != null)
        {
            _store.DocumentChanged -= OnDocumentChanged;
            _store.FrameChanged -= OnFrameChanged;
            _store.SelectionChanged -= OnSelectionChanged;
            _store.ToolChanged -= OnToolChanged;
            _store.Status -= OnStatus;
        }

        _store = store;
        Stage.Store = store;
        Timeline.Store = store;
        _layers.Store = store;
        _layers.RowHeight = Timeline.RowHeight;
        _layers.RulerHeight = Timeline.RulerHeight;
        _library.Store = store;
        _properties.Store = store;
        _swatches.Store = store;
        _tools.Store = store;
        _timelineToolbar.Store = store;

        store.DocumentChanged += OnDocumentChanged;
        store.FrameChanged += OnFrameChanged;
        store.SelectionChanged += OnSelectionChanged;
        store.ToolChanged += OnToolChanged;
        store.Status += OnStatus;

        _playTimer.Stop();
        _library.SelectedSymbolId = null;
        RefreshAll();
    }

    private void OnDocumentChanged() => RefreshAll();

    private void OnFrameChanged()
    {
        Stage.InvalidateVisual();
        Timeline.Refresh();
        _timelineToolbar.Refresh();
        FollowPlayhead();
        UpdateStatus();
    }

    private void OnSelectionChanged()
    {
        _properties.Refresh();
        Stage.InvalidateVisual();
    }

    private void OnToolChanged()
    {
        _properties.Refresh();
        _tools.Refresh();
        UpdateStatus();
        Stage.InvalidateVisual();
    }

    private void OnStatus(string message) => StatusMessage.Text = message;

    public void RefreshAll()
    {
        if (_store == null) return;
        Stage.InvalidateVisual();
        Timeline.Refresh();
        _timelineToolbar.Refresh();
        _layers.Refresh();
        _library.Refresh();
        _properties.Refresh();
        _tools.Refresh();
        if (_footerToggles.Count >= 3)
        {
            _footerToggles[0].Tag = _store.OnionOn ? "on" : "";
            _footerToggles[1].Tag = _store.GridOn ? "on" : "";
            _footerToggles[2].Tag = _store.OutlineAll ? "on" : "";
        }
        BuildBreadcrumb();
        UpdateStatus();
        UpdateTitle();
    }

    private void FollowPlayhead()
    {
        if (!_store.Playing) return;
        double x = _store.Playhead * Timeline.FrameWidth;
        if (x < TimelineScroll.HorizontalOffset || x > TimelineScroll.HorizontalOffset + TimelineScroll.ViewportWidth - 48)
            TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, x - TimelineScroll.ViewportWidth / 2));
    }

    private void BuildBreadcrumb()
    {
        BreadcrumbHost.Children.Clear();
        string sceneName = string.IsNullOrWhiteSpace(_store.Project.Name) ? UiKit.T("Сцена 1") : _store.Project.Name;
        var scene = new Button
        {
            Content = sceneName,
            Style = (Style)Application.Current.Resources["MenuBtn"],
            FontWeight = _store.EditPath.Count == 0 ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = UiKit.Res(_store.EditPath.Count == 0 ? "Txt" : "TxtDim"),
            ToolTip = UiKit.T("Вернуться на сцену (Esc)"),
        };
        scene.Click += (s, e) => ExitToSymbol(null);
        BreadcrumbHost.Children.Add(scene);

        foreach (EditPathEntry entry in _store.EditPath.ToList())
        {
            BreadcrumbHost.Children.Add(new TextBlock
            {
                Text = UiKit.T("\u203A"),
                Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = UiKit.Res("TxtDim"),
            });
            string id = entry.SymbolId;
            bool current = _store.EditPath.Count > 0 && _store.EditPath[_store.EditPath.Count - 1].SymbolId == id;
            var button = new Button
            {
                Content = entry.Name,
                Style = (Style)Application.Current.Resources["MenuBtn"],
                Foreground = UiKit.Res(current ? "Txt" : "TxtDim"),
                FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
                ToolTip = UiKit.T("Редактировать символ «") + entry.Name + "»",
            };
            button.Click += (s, e) => ExitToSymbol(id);
            BreadcrumbHost.Children.Add(button);
        }
    }

    private void BuildTabs()
    {
        TabStrip.Children.Clear();
        string docName = string.IsNullOrWhiteSpace(_store.Project.Name) ? L10n.T("Без названия") : _store.Project.Name;
        bool atScene = _store.EditPath.Count == 0;
        TabStrip.Children.Add(DocTab(docName, atScene, () => ExitToSymbol(null), "Документ: сцена"));
        var path = _store.EditPath.ToList();
        for (int i = 0; i < path.Count; i++)
        {
            string id = path[i].SymbolId;
            bool active = i == path.Count - 1;
            TabStrip.Children.Add(DocTab(path[i].Name, active, () => ExitToSymbol(id), "Символ: " + path[i].Name));
        }
    }

    private static Button DocTab(string text, bool active, Action onClick, string tooltip)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)Application.Current.Resources["DocTab"],
            Tag = active ? "on" : "",
            ToolTip = tooltip,
        };
        button.Click += (s, e) => onClick();
        return button;
    }

    private void ExitToSymbol(string symbolId)
    {
        while (_store.EditPath.Count > 0)
        {
            if (symbolId != null && _store.EditPath[_store.EditPath.Count - 1].SymbolId == symbolId) break;
            _store.ExitSymbol();
        }
        RefreshAll();
    }

    private void UpdateStatus()
    {
        if (_store == null) return;
        var tool = ToolRegistry.Get(_store.ToolId);
        StatusHint.Text = L10n.T(tool.Title) + (string.IsNullOrEmpty(tool.Hint) ? "" : "   ·   " + L10n.T(tool.Hint));
        StatusZoom.Text = Math.Round(_store.Zoom * 100) + "%";
        StatusDirty.Text = L10n.T(_store.Dirty ? "● не сохранено" : "✓ сохранено");
        StatusDirty.Foreground = _store.Dirty ? UiKit.Res("Warn") : UiKit.Res("Ok");
        TimelineSummary.Text = _store.SceneLength + L10n.T(" кадр.   ·   ") + _store.Project.Stage.Fps + L10n.T(" к/с") +
                               (_store.Playing ? "   ·   " + L10n.T("воспроизведение") : "") +
                               (_store.ActiveSymbol != null ? "   ·   " + L10n.T("символ: ") + _store.ActiveSymbol.Name : "");
    }

    private void UpdateTitle()
    {
        string name = string.IsNullOrWhiteSpace(_store.Project.Name) ? L10n.T("Без названия") : _store.Project.Name;
        string symbol = _store.ActiveSymbol != null ? "  ›  " + _store.ActiveSymbol.Name : "";
        string dirty = _store.Dirty ? "•  " : "";
        Title = dirty + name + symbol + " — MotionForge";
        DocTitle.Text = _store.FilePath ?? L10n.T("не сохранён на диск");
        BuildTabs();
    }

    /* ================================================================== */
    /*  Панели: показ и скрытие                                            */
    /* ================================================================== */

    public bool IsPanelVisible(string id)
    {
        switch (id)
        {
            case "tools": return ToolsColumn.Width.Value > 1;
            case "props": return PropertiesRow.Height.Value > 1;
            case "library": return LibraryRow.Height.Value > 1;
            case "swatches": return SwatchesRow.Height.Value > 1;
            case "timeline": return TimelineRow.Height.Value > 1;
        }
        return false;
    }

    public void TogglePanel(string id) => SetPanelVisible(id, !IsPanelVisible(id));

    public void SetPanelVisible(string id, bool visible)
    {
        switch (id)
        {
            case "tools":
                ToolsColumn.Width = new GridLength(visible ? 70 : 0);
                ToolsDock.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                break;
            case "props":
                Collapse(PropertiesRow, PropertiesDock, visible, 150, new GridLength(1, GridUnitType.Star));
                break;
            case "library":
                Collapse(LibraryRow, LibraryDock, visible, 86, new GridLength(240));
                break;
            case "swatches":
                Collapse(SwatchesRow, SwatchesDock, visible, 56, new GridLength(150));
                break;
            case "timeline":
                TimelineRow.Height = visible ? new GridLength(256) : new GridLength(0);
                TimelineRow.MinHeight = visible ? 118 : 0;
                break;
        }
        RefreshAll();
    }

    private static void Collapse(RowDefinition row, UIElement panel, bool visible, double minHeight, GridLength open)
    {
        row.MinHeight = visible ? minHeight : 0;
        row.Height = visible ? open : new GridLength(0);
        panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /* ================================================================== */
    /*  Команды и горячие клавиши                                          */
    /* ================================================================== */

    private void RunCommand(Cmd command)
    {
        if (command == null || command.Run == null) return;
        if (!command.IsEnabled(this)) return;
        command.Run(this);
    }

    private void RefreshMenuState(ContextMenu menu)
    {
        foreach (object entry in menu.Items)
        {
            if (!(entry is MenuItem item) || !(item.Tag is Cmd command)) continue;
            item.IsEnabled = command.IsEnabled(this);
            item.Icon = command.IsChecked(this)
                ? new TextBlock { Text = UiKit.T("✓"), Foreground = UiKit.Res("Accent2") }
                : null;
        }
    }

    private void BuildHotkeys()
    {
        foreach (Cmd command in Commands.All)
        {
            if (string.IsNullOrWhiteSpace(command.Keys)) continue;
            string id = Hotkeys.IdOf(command.Keys);
            if (id == null || _hotkeys.ContainsKey(id)) continue;
            _hotkeys[id] = command;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_store == null) return;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys mods = Keyboard.Modifiers;

        if (IsTextInputFocused())
        {
            string onlyId = Hotkeys.Id(mods, key);
            if (onlyId == null || !_hotkeys.TryGetValue(onlyId, out Cmd allowed) || !AllowedWhileTyping.Contains(allowed.Id))
                return;
            RunCommand(allowed);
            e.Handled = true;
            return;
        }

        // Пробел на сцене — временная «рука» (панорама), а не воспроизведение.
        if (key == Key.Space && mods == ModifierKeys.None && Stage.IsKeyboardFocusWithin) return;

        string id = Hotkeys.Id(mods, key);
        if (id == null) return;
        if (_hotkeys.TryGetValue(id, out Cmd command))
        {
            RunCommand(command);
            e.Handled = true;
        }
    }

    private static bool IsTextInputFocused()
    {
        object focused = Keyboard.FocusedElement;
        return focused is TextBox || focused is ComboBox || focused is PasswordBox;
    }

    /* ================================================================== */
    /*  IAppShell: файл                                                    */
    /* ================================================================== */

    public void NewProject()
    {
        if (!ConfirmDiscard("Создать новый проект?")) return;

        var dialog = new NewDocumentDialog { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null) return;

        _playTimer.Stop();
        var project = new Project
        {
            Name = dialog.Result.Name,
            Stage = new StageInfo
            {
                Width = dialog.Result.Width,
                Height = dialog.Result.Height,
                Fps = dialog.Result.Fps,
                Background = dialog.Result.Background,
            },
        };
        AttachStore(new Store(project));
        FitStage();
        Store.Report(L10n.T("Новый документ создан"));
    }

    public void OpenProject()
    {
        if (!ConfirmDiscard("Открыть другой проект?")) return;
        var dialog = new OpenFileDialog
        {
            Filter = "Проект MotionForge (*.mforge;*.json)|*.mforge;*.json|Все файлы (*.*)|*.*",
            Title = "Открыть проект",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            string text = File.ReadAllText(dialog.FileName, Encoding.UTF8);
            Project project = Json.Load(text);
            if (project == null) throw new InvalidDataException("файл пуст или повреждён");
            string path = dialog.FileName;
            _playTimer.Stop();
            AttachStore(new Store(project));
            _store.FilePath = path;
            _store.Project.Name = Path.GetFileNameWithoutExtension(path);
            _store.Dirty = false;
            FitStage();
            Store.Report("Открыт проект: " + path);
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось открыть файл:\n" + ex.Message, "MotionForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void SaveProject(bool saveAs)
    {
        if (_store == null) return;
        string path = _store.FilePath;
        if (saveAs || string.IsNullOrEmpty(path))
        {
            var dialog = new SaveFileDialog
            {
                FileName = SanitizeName(_store.Project.Name) + ".mforge",
                Filter = "Проект MotionForge (*.mforge)|*.mforge|JSON (*.json)|*.json",
                DefaultExt = "mforge",
                OverwritePrompt = true,
                Title = "Сохранить проект",
            };
            if (!string.IsNullOrEmpty(path)) dialog.InitialDirectory = Path.GetDirectoryName(path);
            if (dialog.ShowDialog(this) != true) return;
            path = dialog.FileName;
        }

        try
        {
            File.WriteAllText(path, Json.Save(_store.Project), new UTF8Encoding(false));
            _store.FilePath = path;
            _store.Project.Name = Path.GetFileNameWithoutExtension(path);
            _store.Dirty = false;
            Store.Report("Сохранено: " + path);
            RemoveAutosave();
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось сохранить файл:\n" + ex.Message, "MotionForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "animation";
        foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        return name;
    }

    private bool ConfirmDiscard(string question)
    {
        if (_store == null || !_store.Dirty) return true;
        MessageBoxResult result = MessageBox.Show(this,
            question + "\nВ проекте «" + _store.Project.Name + "» есть несохранённые изменения.\nСохранить их?",
            "MotionForge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.Yes)
        {
            SaveProject(false);
            return !_store.Dirty;
        }
        return true;
    }

    public void ImportImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Все файлы (*.*)|*.*",
            Title = "Импортировать изображение",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            byte[] data = File.ReadAllBytes(dialog.FileName);
            var element = BuildImageElement(data, Path.GetFileName(dialog.FileName), Path.GetFileNameWithoutExtension(dialog.FileName));
            _store.AddElement(element, "Импорт изображения");
            Store.Report("Изображение добавлено: " + element.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось прочитать изображение:\n" + ex.Message, "MotionForge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Вставка картинки из буфера обмена (Ctrl+V): скриншот или файл изображения.</summary>
    public bool PasteImage(byte[] data, string fileName)
    {
        try
        {
            string name = string.IsNullOrWhiteSpace(fileName) ? "Изображение из буфера" : Path.GetFileNameWithoutExtension(fileName);
            var element = BuildImageElement(data, string.IsNullOrWhiteSpace(fileName) ? "clipboard.png" : fileName, name);

            // Не кладём новую картинку ровно поверх уже существующей — сдвигаем каскадом.
            Keyframe keyframe = _store.EnsureKeyframeForEdit();
            double x = element.X, y = element.Y;
            for (int step = 0; step < 12; step++)
            {
                bool taken = false;
                foreach (Element other in keyframe.Elements)
                {
                    if (Math.Abs(other.X - x) < 1 && Math.Abs(other.Y - y) < 1) { taken = true; break; }
                }
                if (!taken) break;
                x += 24;
                y += 24;
            }
            element.X = x;
            element.Y = y;

            _store.AddElement(element, "Вставка изображения");
            Store.Report("Изображение вставлено: " + element.Name);
            return true;
        }
        catch (Exception ex)
        {
            Store.Report("Не удалось вставить изображение: " + ex.Message);
            return false;
        }
    }

    /// <summary>Создаёт элемент изображения из байтов, вписывая его в сцену по центру.</summary>
    private ImageElement BuildImageElement(byte[] data, string fileName, string name)
    {
        var probe = new BitmapImage();
        probe.BeginInit();
        probe.CacheOption = BitmapCacheOption.OnLoad;
        probe.StreamSource = new MemoryStream(data);
        probe.EndInit();
        probe.Freeze();

        double stageWidth = _store.Project.Stage.Width;
        double stageHeight = _store.Project.Stage.Height;
        double scale = Math.Min(1, Math.Min(stageWidth * 0.82 / Math.Max(1, probe.PixelWidth),
                                            stageHeight * 0.82 / Math.Max(1, probe.PixelHeight)));
        return new ImageElement
        {
            Source = Convert.ToBase64String(data),
            FileName = fileName,
            PixelWidth = probe.PixelWidth,
            PixelHeight = probe.PixelHeight,
            Name = string.IsNullOrWhiteSpace(name) ? "Изображение" : name,
            ScaleX = scale,
            ScaleY = scale,
            X = stageWidth / 2 - probe.PixelWidth * scale / 2,
            Y = stageHeight / 2 - probe.PixelHeight * scale / 2,
        };
    }

    public void ShowExportDialog()
    {
        var dialog = new ExportDialog(_store) { Owner = this };
        dialog.ShowDialog();
    }

    public void ShowStageSettings()
    {
        var dialog = new StageSettingsDialog(_store) { Owner = this };
        if (dialog.ShowDialog() == true) FitStage();
    }

    /* ================================================================== */
    /*  IAppShell: прочее                                                  */
    /* ================================================================== */

    public void ShowCommandPalette()
    {
        var palette = new CommandPaletteWindow(this) { Owner = this };
        if (palette.ShowDialog() == true && palette.Chosen != null) RunCommand(palette.Chosen);
        Stage.Focus();
    }

    public void ShowHelp()
    {
        new HelpWindow(this) { Owner = this }.ShowDialog();
        Stage.Focus();
    }

    public void ShowAbout()
    {
        new AboutWindow { Owner = this }.ShowDialog();
        Stage.Focus();
    }

    /* --------------------------- плагины ------------------------------ */

    /// <summary>Запуск плагина из меню «Плагины».</summary>
    public void RunPlugin(string id)
    {
        PluginInfo plugin = Plugins.Cache.FirstOrDefault(p => p.Id == id) ?? Plugins.Load().FirstOrDefault(p => p.Id == id);
        if (plugin == null)
        {
            Store.Report(L10n.T("Плагин не найден"));
            return;
        }
        if (!plugin.Ready)
        {
            new LogWindow(plugin.Title, plugin.Error, true) { Owner = this }.ShowDialog();
            return;
        }

        Project changed = Plugins.Run(plugin, _store.Project, _store.Playhead, out string log);
        if (changed != null)
        {
            _store.ApplyProject(changed, "Плагин: " + plugin.Title);
            RefreshAll();
            Store.Report(string.Format(L10n.T("Плагин «{0}» выполнен"), plugin.Title));
        }

        // Показываем отчёт, если есть что показать (ошибка или вывод скрипта).
        if (changed == null || !string.IsNullOrWhiteSpace(log))
            new LogWindow(plugin.Title, log, changed == null) { Owner = this }.ShowDialog();
        Stage.Focus();
    }

    public void ShowPlugins()
    {
        new PluginsWindow { Owner = this }.ShowDialog();
        Stage.Focus();
    }

    /* --------------------------- обновления --------------------------- */

    /// <summary>Ручная проверка из меню «Помощь»: всегда показывает результат.</summary>
    public void CheckUpdates() => _ = CheckForUpdatesAsync(false);

    private async void AutoCheckUpdates()
    {
        // Не мешаем восстановлению автосохранения и первому кадру.
        await Task.Delay(3500);
        if (!IsLoaded) return;
        if ((DateTime.UtcNow - AppSettings.Current.LastUpdateCheck).TotalHours < 12) return;
        await CheckForUpdatesAsync(true);
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (!silent) Store.Report(L10n.T("Проверка обновлений…"));
        try
        {
            UpdateInfo info = await Updates.CheckAsync();

            AppSettings.Current.LastUpdateCheck = DateTime.UtcNow;
            AppSettings.Current.Save();

            if (info == null)
            {
                if (!silent)
                    MessageBox.Show(this, L10n.T("У вас последняя версия MotionForge."), "MotionForge",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Эту версию пользователь попросил больше не предлагать при запуске.
            if (silent && AppSettings.Current.SkippedVersion == info.Version) return;
            if (!IsLoaded) return;

            new UpdateWindow(info) { Owner = this }.ShowDialog();
            Stage.Focus();
        }
        catch
        {
            if (!silent)
                MessageBox.Show(this, L10n.T("Не удалось проверить обновления. Проверьте подключение к сети."), "MotionForge",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void CreateSymbolFromSelection()
    {
        if (_store.Selection.Count == 0)
        {
            Store.Report("Сначала выделите объекты, которые нужно превратить в символ");
            return;
        }
        var dialog = new SymbolDialog(SuggestSymbolName()) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Symbol symbol = _store.CreateSymbolFromSelection(dialog.SymbolName, dialog.Kind);
        if (symbol != null)
        {
            _library.SelectedSymbolId = symbol.Id;
            Store.Report("Символ «" + symbol.Name + "» создан и добавлен в библиотеку");
        }
        RefreshAll();
    }

    private string SuggestSymbolName()
    {
        var element = _store.SelectionElements().FirstOrDefault();
        string hint = element is TextElement text && !string.IsNullOrWhiteSpace(text.Text) ? text.Text.Trim() : null;
        if (!string.IsNullOrEmpty(hint))
            return hint.Length > 24 ? hint.Substring(0, 24) : hint;
        int index = _store.Project.Library.Count + 1;
        return "Символ " + index;
    }

    public void ZoomBy(double factor)
    {
        if (_store == null) return;
        Stage.ZoomAt(factor, Stage.ViewportCenterScreen);
        UpdateStatus();
    }

    public void ZoomTo(double zoom)
    {
        if (_store == null) return;
        Point center = Stage.ViewportCenterScreen;
        _store.SetZoom(zoom, center.X, center.Y);
        Stage.InvalidateVisual();
        UpdateStatus();
    }

    public void FitStage()
    {
        Stage.FitStage();
        UpdateStatus();
    }

    public void TogglePlay()
    {
        if (_store == null) return;
        if (_store.SceneLength <= 1)
        {
            Store.Report("В проекте один кадр — добавьте кадры клавишей F5");
            return;
        }
        _store.Playing = !_store.Playing;
        if (_store.Playing)
        {
            if (_store.Playhead >= _store.SceneLength - 1) _store.SetPlayhead(0);
            _playTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _store.Project.Stage.Fps));
            _playTimer.Start();
        }
        else
        {
            _playTimer.Stop();
        }
        _timelineToolbar.Refresh();
        UpdateStatus();
    }

    private void OnPlayTick(object sender, EventArgs e)
    {
        if (_store == null || !_store.Playing) return;
        int next = _store.Playhead + 1;
        if (next >= _store.SceneLength)
        {
            if (_store.Loop)
            {
                next = 0;
            }
            else
            {
                _playTimer.Stop();
                _store.Playing = false;
                _timelineToolbar.Refresh();
                return;
            }
        }
        _store.SetPlayhead(next);
    }

    public void StepFrames(int delta)
    {
        if (_store == null) return;
        int frame = Math.Max(0, Math.Min(Math.Max(0, _store.SceneLength - 1), _store.Playhead + delta));
        _store.SetPlayhead(frame);
        UpdateStatus();
    }

    public void GotoStart()
    {
        if (_store == null) return;
        _store.SetPlayhead(0);
    }

    public void GotoEnd()
    {
        if (_store == null) return;
        _store.SetPlayhead(Math.Max(0, _store.SceneLength - 1));
    }

    public void FocusStage() => Stage.Focus();

    /// <summary>Правка текста прямо на сцене (вызывается из панели свойств).</summary>
    public void BeginTextEdit(TextElement element)
    {
        Stage.Focus();
        Stage.BeginTextEdit(element);
    }

    /* ================================================================== */
    /*  Контекстные меню сцены и таймлайна                                 */
    /* ================================================================== */

    private void OnStageRightClick(Vec world, Point screen)
    {
        if (_store.Selection.Count == 0)
        {
            var hit = _store.HitTest(world, 5 * Stage.Px, out Element leaf);
            Element target = leaf ?? hit?.Src;
            if (target != null) _store.SetSelection(new[] { target.Id });
        }
        ContextMenu menu = BuildCommandMenu(StageContextMenu);
        menu.PlacementTarget = Stage;
        menu.Placement = PlacementMode.AbsolutePoint;
        Point point = Stage.PointToScreen(screen);
        menu.HorizontalOffset = point.X;
        menu.VerticalOffset = point.Y;
        menu.IsOpen = true;
    }

    private void OnTimelineRightClick(int layerIndex, int frame, Point position)
    {
        if (layerIndex >= 0 && layerIndex < _store.ActiveScene.Layers.Count)
            _store.ActiveLayerId = _store.ActiveScene.Layers[layerIndex].Id;
        _store.SetPlayhead(frame);
        _layers.Refresh();
        RefreshAll();

        ContextMenu menu = BuildCommandMenu(TimelineContextMenu);
        menu.PlacementTarget = Timeline;
        menu.Placement = PlacementMode.AbsolutePoint;
        Point point = Timeline.PointToScreen(position);
        menu.HorizontalOffset = point.X;
        menu.VerticalOffset = point.Y;
        menu.IsOpen = true;
    }

    private void OnStageMouseMoved(Vec world, Point screen)
    {
        StatusCoords.Text = UiKit.T("X ") + Num.F(world.X, 1) + "   Y " + Num.F(world.Y, 1);
    }

    /* ================================================================== */
    /*  Автосохранение и закрытие                                          */
    /* ================================================================== */

    private string AutosavePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MotionForge", "autosave.mforge");

    private void Autosave()
    {
        if (_store == null || !_store.Dirty) return;
        try
        {
            string folder = Path.GetDirectoryName(AutosavePath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(AutosavePath, Json.Save(_store.Project), new UTF8Encoding(false));
        }
        catch
        {
            // автосохранение не должно мешать работе
        }
    }

    private void RemoveAutosave()
    {
        try
        {
            if (File.Exists(AutosavePath)) File.Delete(AutosavePath);
        }
        catch
        {
            // файл мог быть занят — не критично
        }
    }

    private void TryRestoreAutosave()
    {
        try
        {
            if (!File.Exists(AutosavePath)) return;
            MessageBoxResult result = MessageBox.Show(this,
                "Найдена несохранённая работа с прошлого запуска.\nВосстановить её?",
                "MotionForge — восстановление", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                RemoveAutosave();
                return;
            }
            Project project = Json.Load(File.ReadAllText(AutosavePath, Encoding.UTF8));
            if (project == null) return;
            AttachStore(new Store(project));
            _store.Dirty = true;
            FitStage();
            Store.Report("Восстановлена несохранённая работа — не забудьте сохранить файл (Ctrl+S)");
        }
        catch
        {
            RemoveAutosave();
        }
    }

    private void OnWindowClosing(object sender, CancelEventArgs e)
    {
        _playTimer.Stop();
        if (_store != null && _store.Dirty && AppSettings.Current.ConfirmOnExit)
        {
            MessageBoxResult result = MessageBox.Show(this,
                "Сохранить изменения в «" + _store.Project.Name + "» перед закрытием?",
                "MotionForge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (result == MessageBoxResult.Yes)
            {
                SaveProject(false);
                if (_store.Dirty)
                {
                    e.Cancel = true;
                    return;
                }
            }
        }
        RemoveAutosave();
    }
}
