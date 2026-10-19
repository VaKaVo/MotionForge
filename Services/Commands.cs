using System;
using System.Collections.Generic;
using System.Linq;
using MotionForge.Model;

namespace MotionForge.Services;

/// <summary>То, что команды могут попросить у главного окна.</summary>
public interface IAppShell
{
    Store Store { get; }
    void NewProject();
    void OpenProject();
    void SaveProject(bool saveAs);
    void ImportImage();
    /// <summary>Вставляет изображение из буфера обмена (скриншот, картинка или файл).</summary>
    bool PasteImage(byte[] data, string fileName);
    void ShowExportDialog();
    void ShowStageSettings();
    void ShowSettings();
    void ShowCommandPalette();
    void ShowHelp();
    void ShowAbout();
    void CreateSymbolFromSelection();
    void ZoomBy(double factor);
    void ZoomTo(double zoom);
    void FitStage();
    void TogglePlay();
    void StepFrames(int delta);
    void GotoStart();
    void GotoEnd();
    void RefreshAll();
    void FocusStage();
    void TogglePanel(string id);
    void SetPanelVisible(string id, bool visible);
    bool IsPanelVisible(string id);
}

public class Cmd
{
    public string Id;
    public string Group;
    public string Title;
    public string Keys;
    public Action<IAppShell> Run;
    public Func<IAppShell, bool> Enabled;
    public Func<IAppShell, bool> Checked;

    public bool IsEnabled(IAppShell shell) => Enabled == null || Enabled(shell);
    public bool IsChecked(IAppShell shell) => Checked != null && Checked(shell);
}

/// <summary>Единый реестр команд: меню, палитра команд и горячие клавиши берут данные отсюда.</summary>
public static class Commands
{
    public static readonly List<Cmd> All = new List<Cmd>();
    public static readonly List<Element> Clipboard = new List<Element>();

    static Commands()
    {
        BuildTools();
        BuildFile();
        BuildEdit();
        BuildFrames();
        BuildModify();
        BuildLayers();
        BuildView();
        BuildWindow();
        BuildHelp();
    }

    public static Cmd Find(string id) => All.FirstOrDefault(c => c.Id == id);
    public static IEnumerable<string> GroupNames => All.Select(c => c.Group).Distinct();

    private static Cmd Add(string id, string group, string title, string keys, Action<IAppShell> run,
        Func<IAppShell, bool> enabled = null, Func<IAppShell, bool> check = null)
    {
        var cmd = new Cmd { Id = id, Group = group, Title = title, Keys = keys, Run = run, Enabled = enabled, Checked = check };
        All.Add(cmd);
        return cmd;
    }

    private static void BuildTools()
    {
        Add("tool.select", "Инструменты", "Выделение", "V", s => s.Store.SetTool("select"), null, s => s.Store.ToolId == "select");
        Add("tool.subselect", "Инструменты", "Частичное выделение (точки)", "A", s => s.Store.SetTool("subselect"), null, s => s.Store.ToolId == "subselect");
        Add("tool.transform", "Инструменты", "Свободное трансформирование", "Q", s => s.Store.SetTool("transform"), null, s => s.Store.ToolId == "transform");
        Add("tool.line", "Инструменты", "Линия", "N", s => s.Store.SetTool("line"), null, s => s.Store.ToolId == "line");
        Add("tool.rect", "Инструменты", "Прямоугольник", "R", s => s.Store.SetTool("rect"), null, s => s.Store.ToolId == "rect");
        Add("tool.ellipse", "Инструменты", "Овал", "O", s => s.Store.SetTool("ellipse"), null, s => s.Store.ToolId == "ellipse");
        Add("tool.pencil", "Инструменты", "Карандаш", "Y", s => s.Store.SetTool("pencil"), null, s => s.Store.ToolId == "pencil");
        Add("tool.brush", "Инструменты", "Кисть", "B", s => s.Store.SetTool("brush"), null, s => s.Store.ToolId == "brush");
        Add("tool.pen", "Инструменты", "Перо (кривые)", "P", s => s.Store.SetTool("pen"), null, s => s.Store.ToolId == "pen");
        Add("tool.text", "Инструменты", "Текст", "T", s => s.Store.SetTool("text"), null, s => s.Store.ToolId == "text");
        Add("tool.bucket", "Инструменты", "Заливка", "K", s => s.Store.SetTool("bucket"), null, s => s.Store.ToolId == "bucket");
        Add("tool.eraser", "Инструменты", "Ластик", "E", s => s.Store.SetTool("eraser"), null, s => s.Store.ToolId == "eraser");
        Add("tool.eyedropper", "Инструменты", "Пипетка", "I", s => s.Store.SetTool("eyedropper"), null, s => s.Store.ToolId == "eyedropper");
        Add("tool.hand", "Инструменты", "Рука (панорама)", "H", s => s.Store.SetTool("hand"), null, s => s.Store.ToolId == "hand");
        Add("tool.zoom", "Инструменты", "Масштаб", "Z", s => s.Store.SetTool("zoom"), null, s => s.Store.ToolId == "zoom");
    }

    private static void BuildFile()
    {
        Add("file.new", "Файл", "Новый документ…", "Ctrl+N", s => s.NewProject());
        Add("file.open", "Файл", "Открыть…", "Ctrl+O", s => s.OpenProject());
        Add("file.save", "Файл", "Сохранить", "Ctrl+S", s => s.SaveProject(false));
        Add("file.saveAs", "Файл", "Сохранить как…", "Ctrl+Shift+S", s => s.SaveProject(true));
        Add("file.import", "Файл", "Импортировать изображение…", "Ctrl+I", s => s.ImportImage());
        Add("file.export", "Файл", "Экспорт…", "Ctrl+E", s => s.ShowExportDialog());
        Add("file.stage", "Файл", "Свойства сцены…", "", s => s.ShowStageSettings());
    }

    private static void BuildEdit()
    {
        Add("edit.undo", "Правка", "Отменить", "Ctrl+Z", s => { s.Store.Undo(); s.RefreshAll(); }, s => s.Store.CanUndo);
        Add("edit.redo", "Правка", "Повторить", "Ctrl+Y", s => { s.Store.Redo(); s.RefreshAll(); }, s => s.Store.CanRedo);
        Add("edit.copy", "Правка", "Копировать", "Ctrl+C", DoCopy, s => s.Store.Selection.Count > 0);
        Add("edit.cut", "Правка", "Вырезать", "Ctrl+X", s => { DoCopy(s); s.Store.DeleteSelection(); }, s => s.Store.Selection.Count > 0);
        Add("edit.paste", "Правка", "Вставить", "Ctrl+V", DoPaste, s => Clipboard.Count > 0 || HasClipboardImage());
        Add("edit.duplicate", "Правка", "Дублировать", "Ctrl+D", s => s.Store.DuplicateSelection(), s => s.Store.Selection.Count > 0);
        Add("edit.delete", "Правка", "Удалить", "Delete", s => s.Store.DeleteSelection(), s => s.Store.Selection.Count > 0);
        Add("edit.selectAll", "Правка", "Выделить всё", "Ctrl+A", s =>
        {
            var ids = new List<string>();
            foreach (var layer in s.Store.ActiveScene.Layers)
            {
                if (!layer.Visible || layer.Locked) continue;
                var kf = Anim.Covering(layer, s.Store.Playhead);
                if (kf == null) continue;
                foreach (var e in kf.Elements) ids.Add(e.Id);
            }
            s.Store.SetSelection(ids);
        });
        Add("edit.deselect", "Правка", "Снять выделение", "Ctrl+Shift+A", s => s.Store.ClearSelection(), s => s.Store.Selection.Count > 0);
        Add("edit.settings", "Правка", "Настройки…", "Ctrl+OemComma", s => s.ShowSettings());
    }

    private static void BuildFrames()
    {
        Add("frame.insert", "Вставка", "Вставить кадр", "F5", s => s.Store.InsertFrames(s.Store.Playhead, 1));
        Add("frame.insertMany", "Вставка", "Вставить 5 кадров", "", s => s.Store.InsertFrames(s.Store.Playhead, 5));
        Add("frame.remove", "Вставка", "Удалить кадр", "Shift+F5", s => s.Store.RemoveFrames(s.Store.Playhead, 1));
        Add("frame.key", "Вставка", "Ключевой кадр", "F6", s => s.Store.DoInsertKeyframe(false));
        Add("frame.blankKey", "Вставка", "Пустой ключевой кадр", "F7", s => s.Store.DoInsertKeyframe(true));
        Add("frame.clearKey", "Вставка", "Удалить ключевой кадр", "Shift+F6", s => s.Store.RemoveKeyframe(s.Store.Playhead));
        Add("frame.tween", "Вставка", "Создать классический переход", "F9", s => s.Store.SetTween("classic", 0.3));
        Add("frame.tweenNone", "Вставка", "Убрать переход", "", s => s.Store.SetTween("none"), null, s =>
        {
            var kf = Anim.Exact(s.Store.ActiveLayer, s.Store.Playhead);
            return kf != null && kf.Tween != null;
        });
    }

    private static void BuildModify()
    {
        Add("mod.group", "Изменение", "Сгруппировать", "Ctrl+G", s => s.Store.GroupSelection(), s => s.Store.Selection.Count > 1);
        Add("mod.ungroup", "Изменение", "Разгруппировать", "Ctrl+Shift+G", s => s.Store.UngroupSelection(), s => s.Store.Selection.Count > 0);
        Add("mod.symbol", "Изменение", "Преобразовать в символ…", "F8", s => s.CreateSymbolFromSelection(), s => s.Store.Selection.Count > 0);
        Add("mod.enterSymbol", "Изменение", "Войти в символ", "", s =>
        {
            var inst = s.Store.SelectionElements().OfType<InstanceElement>().FirstOrDefault();
            if (inst != null) s.Store.EnterSymbol(inst.SymbolId);
        }, s => s.Store.SelectionElements().OfType<InstanceElement>().Any());
        Add("mod.exitSymbol", "Изменение", "Выйти из символа", "Ctrl+Up", s => s.Store.ExitSymbol(), s => s.Store.EditPath.Count > 0);
        Add("mod.front", "Изменение", "На передний план", "Ctrl+Shift+Up", s => s.Store.BringToFront(true));
        Add("mod.back", "Изменение", "На задний план", "Ctrl+Shift+Down", s => s.Store.BringToFront(false));
        Add("mod.forward", "Изменение", "Выше на один", "Alt+Up", s => s.Store.MoveSelectionOrder(1));
        Add("mod.backward", "Изменение", "Ниже на один", "Alt+Down", s => s.Store.MoveSelectionOrder(-1));
        Add("mod.flipH", "Изменение", "Отразить по горизонтали", "", s => Flip(s, true));
        Add("mod.flipV", "Изменение", "Отразить по вертикали", "", s => Flip(s, false));
        Add("mod.rotCW", "Изменение", "Повернуть на 90° вправо", "", s => Rotate(s, 90));
        Add("mod.rotCCW", "Изменение", "Повернуть на 90° влево", "", s => Rotate(s, -90));
        Add("mod.alignLeft", "Выравнивание", "Выровнять по левому краю", "", s => Align(s, "left"));
        Add("mod.alignHCenter", "Выравнивание", "Выровнять по центру (гориз.)", "", s => Align(s, "hcenter"));
        Add("mod.alignRight", "Выравнивание", "Выровнять по правому краю", "", s => Align(s, "right"));
        Add("mod.alignTop", "Выравнивание", "Выровнять по верху", "", s => Align(s, "top"));
        Add("mod.alignVCenter", "Выравнивание", "Выровнять по центру (верт.)", "", s => Align(s, "vcenter"));
        Add("mod.alignBottom", "Выравнивание", "Выровнять по низу", "", s => Align(s, "bottom"));
        Add("mod.distributeH", "Выравнивание", "Распределить по горизонтали", "", s => Distribute(s, true));
        Add("mod.distributeV", "Выравнивание", "Распределить по вертикали", "", s => Distribute(s, false));
    }

    private static void BuildLayers()
    {
        Add("layer.add", "Слои", "Новый слой", "Ctrl+Shift+N", s => s.Store.AddLayer(true));
        Add("layer.duplicate", "Слои", "Дублировать слой", "", s => s.Store.DuplicateLayer(s.Store.ActiveLayer.Id));
        Add("layer.delete", "Слои", "Удалить слой", "", s => s.Store.DeleteLayer(s.Store.ActiveLayer.Id), s => s.Store.ActiveScene.Layers.Count > 1);
        Add("layer.up", "Слои", "Слой выше", "Ctrl+Alt+Up", s => s.Store.MoveLayer(s.Store.ActiveLayer.Id, -1));
        Add("layer.down", "Слои", "Слой ниже", "Ctrl+Alt+Down", s => s.Store.MoveLayer(s.Store.ActiveLayer.Id, 1));
        Add("layer.toggleVisible", "Слои", "Показать/скрыть слой", "", s =>
        {
            var l = s.Store.ActiveLayer;
            s.Store.SetLayerFlag(l.Id, "visible", !l.Visible);
        });
        Add("layer.toggleLock", "Слои", "Заблокировать слой", "", s =>
        {
            var l = s.Store.ActiveLayer;
            s.Store.SetLayerFlag(l.Id, "locked", !l.Locked);
        });
    }

    private static void BuildView()
    {
        Add("view.zoomIn", "Вид", "Увеличить", "Ctrl+OemPlus", s => s.ZoomBy(1.25));
        Add("view.zoomOut", "Вид", "Уменьшить", "Ctrl+OemMinus", s => s.ZoomBy(1 / 1.25));
        Add("view.zoom100", "Вид", "Масштаб 100%", "Ctrl+D1", s => s.ZoomTo(1));
        Add("view.fit", "Вид", "Вписать сцену", "Ctrl+D0", s => s.FitStage());
        Add("view.grid", "Вид", "Сетка", "Ctrl+OemQuotes", s =>
        {
            s.Store.GridOn = !s.Store.GridOn;
            s.RefreshAll();
        }, null, s => s.Store.GridOn);
        Add("view.onion", "Вид", "Луковая кожа (соседние кадры)", "Alt+O", s =>
        {
            s.Store.OnionOn = !s.Store.OnionOn;
            s.RefreshAll();
        }, null, s => s.Store.OnionOn);
        Add("view.outline", "Вид", "Показать контуры", "Ctrl+Shift+O", s =>
        {
            s.Store.OutlineAll = !s.Store.OutlineAll;
            s.RefreshAll();
        }, null, s => s.Store.OutlineAll);
        Add("view.play", "Воспроизведение", "Играть / Пауза", "Space", s => s.TogglePlay());
        Add("view.first", "Воспроизведение", "В начало", "Home", s => s.GotoStart());
        Add("view.last", "Воспроизведение", "В конец", "End", s => s.GotoEnd());
        Add("view.prev", "Воспроизведение", "Предыдущий кадр", "OemComma", s => s.StepFrames(-1));
        Add("view.next", "Воспроизведение", "Следующий кадр", "OemPeriod", s => s.StepFrames(1));
    }

    private static void BuildWindow()
    {
        Add("win.tools", "Окно", "Инструменты", "Ctrl+F2", s => s.TogglePanel("tools"), null, s => s.IsPanelVisible("tools"));
        Add("win.props", "Окно", "Свойства", "Ctrl+F3", s => s.TogglePanel("props"), null, s => s.IsPanelVisible("props"));
        Add("win.library", "Окно", "Библиотека", "Ctrl+L", s => s.TogglePanel("library"), null, s => s.IsPanelVisible("library"));
        Add("win.swatches", "Окно", "Образцы", "", s => s.TogglePanel("swatches"), null, s => s.IsPanelVisible("swatches"));
        Add("win.timeline", "Окно", "Временная шкала", "", s => s.TogglePanel("timeline"), null, s => s.IsPanelVisible("timeline"));
    }

    private static void BuildHelp()    {
        Add("help.palette", "Помощь", "Палитра команд", "Ctrl+K", s => s.ShowCommandPalette());
        Add("help.keys", "Помощь", "Горячие клавиши", "F1", s => s.ShowHelp());
        Add("help.about", "Помощь", "О программе", "", s => s.ShowAbout());
    }

    /* ------------------------- реализация команд ------------------------- */

    private static void DoCopy(IAppShell shell)
    {
        Clipboard.Clear();
        foreach (var e in shell.Store.SelectionElements()) Clipboard.Add(e.Clone());
    }

    private static void DoPaste(IAppShell shell)
    {
        if (Clipboard.Count > 0)
        {
            var copies = new List<Element>();
            foreach (var e in Clipboard)
            {
                var copy = e.Clone();
                copy.Id = Ids.New("el");
                copy.X += 24;
                copy.Y += 24;
                copies.Add(copy);
            }
            shell.Store.AddElementsToCurrentKeyframe(copies, "Вставить");
            return;
        }

        PasteImageFromClipboard(shell);
    }

    /// <summary>Есть ли в системном буфере картинка (растровая или файл изображения).</summary>
    private static bool HasClipboardImage()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsImage()) return true;
            if (System.Windows.Clipboard.ContainsFileDropList())
            {
                foreach (string file in System.Windows.Clipboard.GetFileDropList())
                    if (IsImageFile(file)) return true;
            }
        }
        catch { /* буфер может быть занят другим приложением */ }
        return false;
    }

    /// <summary>Ctrl+V для картинки: скриншот из буфера или скопированный файл изображения.</summary>
    private static void PasteImageFromClipboard(IAppShell shell)
    {
        try
        {
            if (System.Windows.Clipboard.ContainsImage())
            {
                System.Windows.Media.Imaging.BitmapSource image = System.Windows.Clipboard.GetImage();
                if (image != null)
                {
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                    using (var stream = new System.IO.MemoryStream())
                    {
                        encoder.Save(stream);
                        if (shell.PasteImage(stream.ToArray(), "clipboard.png")) return;
                    }
                }
            }

            if (System.Windows.Clipboard.ContainsFileDropList())
            {
                foreach (string file in System.Windows.Clipboard.GetFileDropList())
                {
                    if (!IsImageFile(file)) continue;
                    if (shell.PasteImage(System.IO.File.ReadAllBytes(file), System.IO.Path.GetFileName(file))) return;
                }
            }
        }
        catch { }
    }

    private static bool IsImageFile(string path)
    {
        string ext = System.IO.Path.GetExtension(path ?? "").ToLowerInvariant();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif";
    }

    private static void Flip(IAppShell shell, bool horizontal)
    {
        var store = shell.Store;
        var selection = store.SelectionElements();
        if (selection.Count == 0) return;
        RectD box = store.SelectionBounds();
        foreach (var e in selection)
        {
            RectD bounds = Anim.WorldBounds(e, store.Project);
            if (bounds.IsEmpty) continue;
            if (horizontal)
            {
                e.ScaleX = -e.ScaleX;
                store.OffsetElement(e, 2 * (box.Center.X - bounds.Center.X), 0);
            }
            else
            {
                e.ScaleY = -e.ScaleY;
                store.OffsetElement(e, 0, 2 * (box.Center.Y - bounds.Center.Y));
            }
        }
        store.Commit(horizontal ? "Отразить по горизонтали" : "Отразить по вертикали");
        store.RaiseAll();
    }

    private static void Rotate(IAppShell shell, double degrees)
    {
        var store = shell.Store;
        var selection = store.SelectionElements();
        if (selection.Count == 0) return;
        Vec pivot = store.SelectionBounds().Center;
        foreach (var e in selection) store.RotateElement(e, degrees, pivot);
        store.Commit("Поворот");
        store.RaiseAll();
    }

    private static void Align(IAppShell shell, string mode)
    {
        var store = shell.Store;
        var selection = store.SelectionElements();
        if (selection.Count == 0) return;
        RectD box = store.SelectionBounds();
        foreach (var e in selection)
        {
            RectD bounds = Anim.WorldBounds(e, store.Project);
            if (bounds.IsEmpty) continue;
            double dx = 0, dy = 0;
            switch (mode)
            {
                case "left": dx = box.Left - bounds.Left; break;
                case "hcenter": dx = box.Center.X - bounds.Center.X; break;
                case "right": dx = box.Right - bounds.Right; break;
                case "top": dy = box.Top - bounds.Top; break;
                case "vcenter": dy = box.Center.Y - bounds.Center.Y; break;
                case "bottom": dy = box.Bottom - bounds.Bottom; break;
            }
            store.OffsetElement(e, dx, dy);
        }
        store.Commit("Выравнивание");
        store.RaiseAll();
    }

    private static void Distribute(IAppShell shell, bool horizontal)
    {
        var store = shell.Store;
        var selection = store.SelectionElements();
        if (selection.Count < 3) { store.Report("Для распределения нужно 3+ объекта"); return; }

        var items = selection
            .Select(e => new { Element = e, Bounds = Anim.WorldBounds(e, store.Project) })
            .Where(i => !i.Bounds.IsEmpty)
            .OrderBy(i => horizontal ? i.Bounds.Center.X : i.Bounds.Center.Y)
            .ToList();

        double first = horizontal ? items[0].Bounds.Center.X : items[0].Bounds.Center.Y;
        double last = horizontal ? items[items.Count - 1].Bounds.Center.X : items[items.Count - 1].Bounds.Center.Y;
        double step = (last - first) / (items.Count - 1);

        for (int i = 1; i < items.Count - 1; i++)
        {
            double target = first + step * i;
            double current = horizontal ? items[i].Bounds.Center.X : items[i].Bounds.Center.Y;
            double delta = target - current;
            store.OffsetElement(items[i].Element, horizontal ? delta : 0, horizontal ? 0 : delta);
        }
        store.Commit("Распределение");
        store.RaiseAll();
    }
}