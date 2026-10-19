using System;
using System.Collections.Generic;
using System.Linq;
using MotionForge.Services;

namespace MotionForge.Model;

public class ToolOptions
{
    public string Fill = "#000000";
    public string Stroke = "#000000";
    public double StrokeWidth = 1;
    public double BrushSize = 16;
    public bool Filled = true;
    public bool Stroked = true;
    public bool Smooth = false;
    /// <summary>Сглаживать новые росчерки кисти и карандаша (настройка «Рисование»).</summary>
    public bool FreehandSmooth = true;
    public string FontFamily = "Segoe UI";
    public double FontSize = 72;
    public bool Bold = true;
    public bool Italic;
    public string TextAlign = "left";
}

public class EditPathEntry
{
    public string SymbolId;
    public string Name;
}

/// <summary>Состояние приложения: документ, выделение, история и все правки документа.</summary>
public class Store
{
    private readonly List<string> _history = new List<string>();
    private readonly List<string> _labels = new List<string>();
    private int _index = -1;
    private bool _loadingState;

    public Project Project { get; private set; }
    public string FilePath;
    public bool Dirty;
    public bool SuspendCommit;

    public int Playhead;
    public string ActiveLayerId;
    public readonly List<string> Selection = new List<string>();
    public readonly List<EditPathEntry> EditPath = new List<EditPathEntry>();
    public string ToolId = "select";
    public ToolOptions Opts = new ToolOptions();

    public double Zoom = 1;
    public double PanX;
    public double PanY;

    public bool OnionOn;
    public int OnionBefore = 1;
    public int OnionAfter = 1;
    public bool GridOn;
    public bool SnapToGrid;
    public bool OutlineAll;
    public bool Playing;
    public bool Loop = true;
    public double Speed = 1;

    public event Action DocumentChanged;
    public event Action FrameChanged;
    public event Action SelectionChanged;
    public event Action ToolChanged;
    public event Action HistoryChanged;
    public event Action<string> Status;

    public Store(Project project)
    {
        Project = project;
        EnsureStructure();
        ResetHistory();
    }

    public static double GridSize = 40;

    /* ------------------------------------------------------------------ */
    /*  Доступ к активной сцене                                            */
    /* ------------------------------------------------------------------ */

    public Symbol ActiveSymbol
    {
        get
        {
            if (EditPath.Count == 0) return null;
            return Project.Library.TryGetValue(EditPath[EditPath.Count - 1].SymbolId, out Symbol s) ? s : null;
        }
    }

    public Scene ActiveScene => ActiveSymbol != null ? ActiveSymbol.Timeline : Project.Timeline;

    public Layer ActiveLayer
    {
        get
        {
            var scene = ActiveScene;
            if (scene.Layers.Count == 0) scene.Layers.Add(new Layer { Name = "Слой 1", Color = LayerPalette.Pick(0) });
            Layer layer = scene.Layers.FirstOrDefault(l => l.Id == ActiveLayerId);
            if (layer == null)
            {
                layer = scene.Layers[0];
                ActiveLayerId = layer.Id;
            }
            return layer;
        }
        set { ActiveLayerId = value == null ? null : value.Id; }
    }

    public Keyframe KeyframeAt(int frame) => Anim.Covering(ActiveLayer, frame);

    public Keyframe ActiveKeyframe => KeyframeAt(Playhead);

    public Keyframe ExactKeyframeAt(int frame) => Anim.Exact(ActiveLayer, frame);

    public bool IsEditableFrame => ExactKeyframeAt(Playhead) != null;

    public int SceneLength => Anim.SceneLength(ActiveScene);

    private void EnsureStructure()
    {
        if (Project.Timeline == null) Project.Timeline = new Scene();
        if (Project.Timeline.Layers == null) Project.Timeline.Layers = new List<Layer>();
        if (Project.Timeline.Layers.Count == 0)
            Project.Timeline.Layers.Add(new Layer { Name = "Слой 1", Color = LayerPalette.Pick(0) });
        if (Project.Library == null) Project.Library = new Dictionary<string, Symbol>();
        if (Project.Stage == null) Project.Stage = new StageInfo();
        ActiveLayerId = Project.Timeline.Layers[0].Id;
        Playhead = 0;
        NormalizeTimeline(Project.Timeline);
    }

    /// <summary>Приводит кадры слоя к корректному виду (первый ключ на 0, без наложений).</summary>
    public static void NormalizeTimeline(Scene scene)
    {
        foreach (var layer in scene.Layers)
        {
            layer.Frames = Anim.Sorted(layer);
            if (layer.Frames.Count == 0)
                layer.Frames.Add(new Keyframe { Index = 0, Duration = 1 });
            if (layer.Frames[0].Index != 0)
                layer.Frames.Insert(0, new Keyframe { Index = 0, Duration = Math.Max(1, layer.Frames[0].Index) });
            for (int i = 0; i < layer.Frames.Count; i++)
            {
                if (layer.Frames[i].Duration < 1) layer.Frames[i].Duration = 1;
                if (i > 0 && layer.Frames[i].Index <= layer.Frames[i - 1].Index)
                    layer.Frames[i].Index = layer.Frames[i - 1].Index + layer.Frames[i - 1].Duration;
                if (i > 0)
                {
                    int maxDuration = layer.Frames[i].Index - layer.Frames[i - 1].Index;
                    if (layer.Frames[i - 1].Duration > maxDuration)
                        layer.Frames[i - 1].Duration = Math.Max(1, maxDuration);
                }
            }
        }
    }

    /* ------------------------------------------------------------------ */
    /*  История (отмена/повтор)                                            */
    /* ------------------------------------------------------------------ */

    public bool CanUndo => _index > 0;
    public bool CanRedo => _index >= 0 && _index < _history.Count - 1;
    public int HistoryCount => _history.Count;
    public string UndoLabel => _index > 0 ? _labels[_index] : "";

    public void ResetHistory()
    {
        _history.Clear();
        _labels.Clear();
        _history.Add(Json.Save(Project));
        _labels.Add("Исходное состояние");
        _index = 0;
        Dirty = false;
        HistoryChanged?.Invoke();
    }

    /// <summary>Фиксирует текущее состояние в истории. Вызывается ПОСЛЕ правки документа.</summary>
    public void Commit(string label)
    {
        if (_loadingState) return;
        if (SuspendCommit) { Dirty = true; return; }

        string snapshot = Json.Save(Project);
        if (_index >= 0 && _index < _history.Count && _history[_index] == snapshot)
        {
            Dirty = true;
            return;
        }
        if (_index < _history.Count - 1)
        {
            _history.RemoveRange(_index + 1, _history.Count - _index - 1);
            _labels.RemoveRange(_index + 1, _labels.Count - _index - 1);
        }
        _history.Add(snapshot);
        _labels.Add(string.IsNullOrEmpty(label) ? "Правка" : label);
        _index = _history.Count - 1;
        while (_history.Count > 90)
        {
            _history.RemoveAt(0);
            _labels.RemoveAt(0);
            _index--;
        }
        Dirty = true;
        HistoryChanged?.Invoke();
        Status?.Invoke(_labels[_index]);
    }

    public void Undo()
    {
        if (!CanUndo) { Status?.Invoke("Отменять больше нечего"); return; }
        _index--;
        Restore(_history[_index]);
        Status?.Invoke("Отменено: " + _labels[_index + 1]);
    }

    public void Redo()
    {
        if (!CanRedo) { Status?.Invoke("Повторять нечего"); return; }
        _index++;
        Restore(_history[_index]);
        Status?.Invoke("Повторено: " + _labels[_index]);
    }

    private void Restore(string state)
    {
        _loadingState = true;
        try
        {
            Project = Json.Load(state);
            if (Project.Library == null) Project.Library = new Dictionary<string, Symbol>();
            if (Project.Timeline == null) Project.Timeline = new Scene();
            if (Project.Stage == null) Project.Stage = new StageInfo();
            EditPath.RemoveAll(p => !Project.Library.ContainsKey(p.SymbolId));
            NormalizeTimeline(Project.Timeline);
            foreach (var sym in Project.Library.Values) NormalizeTimeline(sym.Timeline);
            if (ActiveScene.Layers.Count == 0)
                ActiveScene.Layers.Add(new Layer { Name = "Слой 1", Color = LayerPalette.Pick(0) });
            if (!ActiveScene.Layers.Any(l => l.Id == ActiveLayerId)) ActiveLayerId = ActiveScene.Layers[0].Id;
            Playhead = Math.Max(0, Math.Min(Playhead, SceneLength - 1));
            Selection.RemoveAll(id => FindItem(id).element == null);
        }
        finally
        {
            _loadingState = false;
        }
        Renderer.ClearCaches();
        RaiseAll();
        HistoryChanged?.Invoke();
    }

    public void ReplaceProject(Project project, string filePath)
    {
        Project = project ?? new Project();
        FilePath = filePath;
        EditPath.Clear();
        Selection.Clear();
        Playhead = 0;
        ActiveLayerId = null;
        EnsureStructure();
        NormalizeTimeline(Project.Timeline);
        foreach (var symbol in Project.Library.Values) NormalizeTimeline(symbol.Timeline);
        Renderer.ClearCaches();
        ResetHistory();
        RaiseAll();
    }

    public void MarkDirty() { Dirty = true; }

    public void RaiseAll()
    {
        DocumentChanged?.Invoke();
        FrameChanged?.Invoke();
        SelectionChanged?.Invoke();
        HistoryChanged?.Invoke();
    }

    public void RaiseDocument() => DocumentChanged?.Invoke();
    public void RaiseFrame() => FrameChanged?.Invoke();
    public void RaiseSelection() => SelectionChanged?.Invoke();
    public void RaiseToolChanged() => ToolChanged?.Invoke();
    public void Report(string message) => Status?.Invoke(message);

    /* ------------------------------------------------------------------ */
    /*  Навигация                                                          */
    /* ------------------------------------------------------------------ */

    public void SetPlayhead(int frame)
    {
        frame = Math.Max(0, frame);
        if (frame == Playhead) return;
        Playhead = frame;
        FrameChanged?.Invoke();
    }

    public void SetTool(string toolId)
    {
        if (ToolId == toolId) return;
        ToolId = toolId;
        ToolChanged?.Invoke();
    }

    public void SetZoom(double zoom, double? anchorX = null, double? anchorY = null)
    {
        double old = Zoom;
        double next = Math.Max(0.05, Math.Min(16, zoom));
        if (anchorX.HasValue && anchorY.HasValue && old > 0)
        {
            double k = next / old;
            PanX = anchorX.Value - (anchorX.Value - PanX) * k;
            PanY = anchorY.Value - (anchorY.Value - PanY) * k;
        }
        Zoom = next;
        FrameChanged?.Invoke();
    }

    public void SetPan(double x, double y)
    {
        PanX = x; PanY = y;
        FrameChanged?.Invoke();
    }

    /* ------------------------------------------------------------------ */
    /*  Поиск и выделение                                                  */
    /* ------------------------------------------------------------------ */

    public (Element element, Layer layer, Keyframe frame) FindItem(string id)
    {
        var scene = ActiveScene;
        // Сначала ищем в кадре, который сейчас виден (там и редактируем),
        // потому что при копировании ключевых кадров Id объектов повторяются.
        foreach (var layer in scene.Layers)
        {
            var kf = Anim.Covering(layer, Playhead);
            if (kf == null) continue;
            var found = FindInList(kf.Elements, id);
            if (found != null) return (found, layer, kf);
        }
        foreach (var layer in scene.Layers)
        {
            foreach (var kf in layer.Frames)
            {
                var found = FindInList(kf.Elements, id);
                if (found != null) return (found, layer, kf);
            }
        }
        return (null, null, null);
    }

    private static Element FindInList(List<Element> list, string id)
    {
        foreach (var e in list)
        {
            if (e.Id == id) return e;
            if (e is GroupElement g)
            {
                var nested = FindInList(g.Children, id);
                if (nested != null) return nested;
            }
        }
        return null;
    }

    /// <summary>Матрица элемента в мировых координатах с учётом вложенных групп.</summary>
    public Mat WorldMatrix(Element target)
    {
        var (_, _, kf) = FindItem(target.Id);
        if (kf == null) return target.LocalMatrix;
        var chain = new List<Element>();
        if (FindChain(kf.Elements, target.Id, chain))
        {
            Mat m = Mat.Identity;
            for (int i = chain.Count - 1; i >= 0; i--) m = m.Multiply(chain[i].LocalMatrix);
            return m;
        }
        return target.LocalMatrix;
    }

    /// <summary>Матрица родительской группы (единичная, если элемент на верхнем уровне).</summary>
    public Mat ParentMatrix(Element target)
    {
        var (_, _, kf) = FindItem(target.Id);
        if (kf == null) return Mat.Identity;
        var chain = new List<Element>();
        if (!FindChain(kf.Elements, target.Id, chain)) return Mat.Identity;
        chain.RemoveAt(chain.Count - 1);
        Mat m = Mat.Identity;
        for (int i = chain.Count - 1; i >= 0; i--) m = m.Multiply(chain[i].LocalMatrix);
        return m;
    }

    private static bool FindChain(List<Element> list, string id, List<Element> chain)
    {
        foreach (var e in list)
        {
            chain.Add(e);
            if (e.Id == id) return true;
            if (e is GroupElement g && FindChain(g.Children, id, chain)) return true;
            chain.RemoveAt(chain.Count - 1);
        }
        return false;
    }

    public List<Element> SelectionElements()
    {
        var result = new List<Element>();
        foreach (string id in Selection)
        {
            var (element, _, _) = FindItem(id);
            if (element != null) result.Add(element);
        }
        return result;
    }

    public List<EvalElement> EvalSelection()
    {
        var result = new List<EvalElement>();
        foreach (var ev in Anim.EvaluateScene(ActiveScene, Playhead))
            if (Selection.Contains(ev.Src.Id)) result.Add(ev);
        return result;
    }

    public void ClearSelection()
    {
        if (Selection.Count == 0) return;
        Selection.Clear();
        SelectionChanged?.Invoke();
    }

    public void SetSelection(IEnumerable<string> ids)
    {
        Selection.Clear();
        if (ids != null) Selection.AddRange(ids.Distinct());
        SelectionChanged?.Invoke();
    }

    public void ToggleSelection(string id)
    {
        if (Selection.Contains(id)) Selection.Remove(id);
        else Selection.Add(id);
        SelectionChanged?.Invoke();
    }

    public RectD SelectionBounds()
    {
        RectD box = RectD.Empty;
        foreach (var ev in EvalSelection()) box = RectD.Union(box, Anim.EvalBounds(ev, Project));
        if (box.IsEmpty)
            foreach (var e in SelectionElements()) box = RectD.Union(box, Anim.WorldBounds(e, Project));
        return box;
    }

    public EvalElement HitTest(Vec worldPoint, double toleranceWorld, out Element leaf)
    {
        leaf = null;
        var list = Anim.EvaluateScene(ActiveScene, Playhead);
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var ev = list[i];
            if (!ev.Src.Visible || ev.Src.Locked) continue;
            if (ev.Layer != null && (!ev.Layer.Visible || ev.Layer.Locked)) continue;
            if (HitsElement(Project, ev.Src, ev.Matrix, worldPoint, toleranceWorld, out leaf)) return ev;
        }
        return null;
    }

    public static bool HitsElement(Project project, Element element, Mat matrix, Vec point, double tolerance, out Element leaf)
    {
        leaf = null;
        if (!matrix.TryInvert(out Mat inv)) return false;
        Vec local = inv.Apply(point);
        double scale = Math.Max(0.05, (Math.Abs(matrix.A) + Math.Abs(matrix.D)) / 2);
        double localTolerance = tolerance / scale;

        switch (element)
        {
            case ShapeElement shape:
            {
                if (shape.Stroked)
                {
                    for (int i = 0; i + 1 < shape.Points.Count; i++)
                        if (Geo.DistToSegment(local, shape.Points[i], shape.Points[i + 1]) <= shape.StrokeWidth / 2 + localTolerance)
                        { leaf = shape; return true; }
                    if (shape.Closed && shape.Points.Count > 2 &&
                        Geo.DistToSegment(local, shape.Points[shape.Points.Count - 1], shape.Points[0]) <= shape.StrokeWidth / 2 + localTolerance)
                    { leaf = shape; return true; }
                }
                if (shape.Filled && shape.Closed && Geo.PointInPolygon(local, shape.Points))
                { leaf = shape; return true; }
                return false;
            }
            case TextElement text:
            {
                RectD box = Anim.LocalBounds(text, project);
                if (!box.IsEmpty && box.Inflate(localTolerance).Contains(local)) { leaf = text; return true; }
                return false;
            }
            case ImageElement image:
            {
                var box = new RectD(0, 0, Math.Max(1, image.PixelWidth), Math.Max(1, image.PixelHeight));
                if (box.Contains(local)) { leaf = image; return true; }
                return false;
            }
            case GroupElement group:
            {
                foreach (var child in group.Children)
                {
                    if (!child.Visible) continue;
                    Mat childMatrix = matrix.Multiply(child.LocalMatrix);
                    if (HitsElement(project, child, childMatrix, point, tolerance, out leaf)) return true;
                }
                return false;
            }
            case InstanceElement instance:
            {
                RectD box = project != null && project.Library.TryGetValue(instance.SymbolId, out Symbol symbol)
                    ? symbol.Bounds
                    : new RectD(-40, -40, 80, 80);
                // Пустой символ всё равно должен выделяться: даём минимальную рамку.
                if (box.W < 6 || box.H < 6) box = new RectD(box.Center.X - 20, box.Center.Y - 20, 40, 40);
                if (box.Contains(local)) { leaf = instance; return true; }
                return false;
            }
        }
        return false;
    }

    /* ------------------------------------------------------------------ */
    /*  Кадры                                                              */
    /* ------------------------------------------------------------------ */

    public Keyframe InsertKeyframe(int frame, bool blank)
    {
        var layer = ActiveLayer;
        NormalizeTimeline(ActiveScene);
        Keyframe covering = Anim.Covering(layer, frame);

        if (covering == null)
        {
            Keyframe previous = Anim.Previous(layer, frame);
            if (previous != null) previous.Duration = Math.Max(previous.Duration, frame - previous.Index);
            var created = new Keyframe
            {
                Index = frame,
                Duration = 1,
                Elements = blank || previous == null ? new List<Element>() : Keyframe.CloneElements(previous.Elements),
            };
            layer.Frames.Add(created);
            layer.Frames = Anim.Sorted(layer);
            return created;
        }

        if (covering.Index == frame)
        {
            if (blank)
            {
                covering.Elements.Clear();
                covering.Tween = null;
            }
            return covering;
        }

        var left = new Keyframe
        {
            Index = covering.Index,
            Duration = frame - covering.Index,
            Elements = Keyframe.CloneElements(covering.Elements),
            Tween = covering.Tween == null ? null : new Tween { Type = covering.Tween.Type, Ease = covering.Tween.Ease },
        };
        var right = new Keyframe
        {
            Index = frame,
            Duration = covering.EndIndex - frame + 1,
            Elements = blank ? new List<Element>() : Keyframe.CloneElements(covering.Elements),
            Tween = null,
        };
        int at = layer.Frames.IndexOf(covering);
        layer.Frames[at] = left;
        layer.Frames.Insert(at + 1, right);
        return right;
    }

    public void DoInsertKeyframe(bool blank)
    {
        InsertKeyframe(Playhead, blank);
        Commit(blank ? "Пустой ключевой кадр" : "Ключевой кадр");
        RaiseAll();
    }

    /// <summary>Удаляет ключевой кадр, содержимое достаётся предыдущему.</summary>
    public void RemoveKeyframe(int frame)
    {
        var layer = ActiveLayer;
        Keyframe exact = Anim.Exact(layer, frame);
        if (exact == null) { Report("Здесь нет ключевого кадра"); return; }
        Keyframe previous = Anim.Previous(layer, frame);
        if (previous == null)
        {
            exact.Elements.Clear();
            exact.Tween = null;
        }
        else
        {
            previous.Duration += exact.Duration;
            layer.Frames.Remove(exact);
        }
        layer.Frames = Anim.Sorted(layer);
        NormalizeTimeline(ActiveScene);
        Selection.RemoveAll(id => FindItem(id).element == null);
        Commit("Удалить ключевой кадр");
        RaiseAll();
    }

    public void InsertFrames(int frame, int count)
    {
        if (count <= 0) return;
        var layer = ActiveLayer;
        Keyframe covering = Anim.Covering(layer, frame);
        if (covering != null) covering.Duration += count;
        foreach (var kf in layer.Frames)
            if (kf != covering && kf.Index > frame) kf.Index += count;
        layer.Frames = Anim.Sorted(layer);
        Commit(count > 1 ? $"Вставить {count} кадров" : "Вставить кадр");
        RaiseAll();
    }

    public void RemoveFrames(int frame, int count)
    {
        if (count <= 0) return;
        var layer = ActiveLayer;
        for (int i = 0; i < count; i++)
        {
            Keyframe covering = Anim.Covering(layer, frame);
            if (covering == null) break;
            if (covering.Duration > 1)
            {
                covering.Duration--;
            }
            else if (layer.Frames.Count > 1)
            {
                layer.Frames.Remove(covering);
            }
            else
            {
                covering.Elements.Clear();
            }
            foreach (var kf in layer.Frames)
                if (kf.Index > frame) kf.Index--;
        }
        layer.Frames = Anim.Sorted(layer);
        NormalizeTimeline(ActiveScene);
        Playhead = Math.Max(0, Math.Min(Playhead, SceneLength - 1));
        Commit(count > 1 ? $"Удалить {count} кадров" : "Удалить кадр");
        RaiseAll();
    }

    public void SetTween(string type, double ease = 0)
    {
        var layer = ActiveLayer;
        Keyframe exact = Anim.Exact(layer, Playhead);
        if (exact == null)
        {
            Report("Твин ставится на ключевой кадр: нажмите F6 и повторите");
            return;
        }
        if (Anim.Previous(layer, exact.Index) == null)
        {
            Report("Нужны два ключевых кадра с одинаковыми объектами");
            return;
        }
        if (type == "none") exact.Tween = null;
        else exact.Tween = new Tween { Type = type, Ease = ease };
        Commit(type == "none" ? "Убрать твин" : "Классический твин");
        RaiseAll();
    }

    public void SetTweenEase(double ease)
    {
        var exact = Anim.Exact(ActiveLayer, Playhead);
        if (exact == null || exact.Tween == null) return;
        exact.Tween.Ease = ease;
        Commit("Плавность твина");
        RaiseAll();
    }

    public void SetFrameDuration(int frame, int duration)
    {
        Keyframe covering = Anim.Covering(ActiveLayer, frame);
        if (covering == null) return;
        covering.Duration = Math.Max(1, duration);
        NormalizeTimeline(ActiveScene);
        Commit("Длительность кадра");
        RaiseAll();
    }

    /* ------------------------------------------------------------------ */
    /*  Слои                                                              */
    /* ------------------------------------------------------------------ */

    public Layer AddLayer(bool above)
    {
        var scene = ActiveScene;
        var layer = new Layer { Name = "Слой " + (scene.Layers.Count + 1), Color = LayerPalette.Pick(scene.Layers.Count) };
        int index = above ? Math.Max(0, scene.Layers.IndexOf(ActiveLayer)) : scene.Layers.Count;
        scene.Layers.Insert(index, layer);
        ActiveLayerId = layer.Id;
        Selection.Clear();
        Commit("Новый слой");
        RaiseAll();
        return layer;
    }

    public void DeleteLayer(string layerId)
    {
        var scene = ActiveScene;
        if (scene.Layers.Count <= 1) { Report("Нельзя удалить единственный слой"); return; }
        var layer = scene.Layers.FirstOrDefault(l => l.Id == layerId);
        if (layer == null) return;
        int index = scene.Layers.IndexOf(layer);
        scene.Layers.Remove(layer);
        ActiveLayerId = scene.Layers[Math.Min(index, scene.Layers.Count - 1)].Id;
        Selection.Clear();
        Commit("Удалить слой");
        RaiseAll();
    }

    public void DuplicateLayer(string layerId)
    {
        var scene = ActiveScene;
        var layer = scene.Layers.FirstOrDefault(l => l.Id == layerId);
        if (layer == null) return;
        var copy = new Layer
        {
            Name = layer.Name + " копия",
            Color = layer.Color,
            Visible = layer.Visible,
            Locked = layer.Locked,
            Outline = layer.Outline,
        };
        foreach (var kf in layer.Frames) copy.Frames.Add(kf.Clone());
        scene.Layers.Insert(Math.Max(0, scene.Layers.IndexOf(layer)), copy);
        ActiveLayerId = copy.Id;
        Commit("Дублировать слой");
        RaiseAll();
    }

    public void RenameLayer(string layerId, string name)
    {
        var layer = ActiveScene.Layers.FirstOrDefault(l => l.Id == layerId);
        if (layer == null || layer.Name == name) return;
        layer.Name = name;
        Commit("Переименовать слой");
        RaiseDocument();
    }

    public void MoveLayer(string layerId, int delta)
    {
        var scene = ActiveScene;
        var layer = scene.Layers.FirstOrDefault(l => l.Id == layerId);
        if (layer == null) return;
        int index = scene.Layers.IndexOf(layer);
        int target = index + delta;
        if (target < 0 || target >= scene.Layers.Count) return;
        scene.Layers.RemoveAt(index);
        scene.Layers.Insert(target, layer);
        Commit("Порядок слоёв");
        RaiseAll();
    }

    public void SetLayerFlag(string layerId, string flag, bool value)
    {
        var layer = ActiveScene.Layers.FirstOrDefault(l => l.Id == layerId);
        if (layer == null) return;
        switch (flag)
        {
            case "visible": layer.Visible = value; break;
            case "locked": layer.Locked = value; break;
            case "outline": layer.Outline = value; break;
        }
        Commit("Свойства слоя");
        RaiseDocument();
    }

    public void SetLayerColor(string layerId, string color)
    {
        var layer = ActiveScene.Layers.FirstOrDefault(l => l.Id == layerId);
        if (layer == null) return;
        layer.Color = color;
        Commit("Цвет слоя");
        RaiseDocument();
    }

    /* ------------------------------------------------------------------ */
    /*  Объекты                                                            */
    /* ------------------------------------------------------------------ */

    public Keyframe EnsureKeyframeForEdit()
    {
        return InsertKeyframe(Playhead, false);
    }

    public Element AddElement(Element element, string label = "Добавить объект")
    {
        Keyframe kf = EnsureKeyframeForEdit();
        kf.Elements.Add(element);
        Selection.Clear();
        Selection.Add(element.Id);
        Commit(label);
        RaiseAll();
        return element;
    }

    public void AddElementsToCurrentKeyframe(IEnumerable<Element> elements, string label)
    {
        Keyframe kf = EnsureKeyframeForEdit();
        var ids = new List<string>();
        foreach (var e in elements) { kf.Elements.Add(e); ids.Add(e.Id); }
        SetSelection(ids);
        Commit(label);
        RaiseAll();
    }

    public void DeleteSelection()
    {
        if (Selection.Count == 0) return;
        foreach (string id in Selection.ToList())
        {
            var (element, _, kf) = FindItem(id);
            if (element == null || kf == null) continue;
            if (kf.Elements.Remove(element)) continue;
            var parent = FindGroupParent(kf.Elements, id);
            parent?.Children.Remove(element);
        }
        Selection.Clear();
        Commit("Удалить");
        RaiseAll();
    }

    private static GroupElement FindGroupParent(List<Element> list, string id)
    {
        foreach (var e in list)
        {
            if (e is GroupElement g)
            {
                foreach (var c in g.Children) if (c.Id == id) return g;
                var nested = FindGroupParent(g.Children, id);
                if (nested != null) return nested;
            }
        }
        return null;
    }

    public void ApplyToSelection(Action<Element> action, string label)
    {
        if (Selection.Count == 0) return;
        foreach (var element in SelectionElements()) action(element);
        Commit(label);
        RaiseAll();
    }

    public void ApplyToSelectionEval(Action<Element, EvalElement> action, string label)
    {
        if (Selection.Count == 0) return;
        var eval = EvalSelection().ToDictionary(e => e.Src.Id);
        foreach (var element in SelectionElements())
        {
            EvalElement ev;
            if (eval.TryGetValue(element.Id, out ev)) action(element, ev);
            else action(element, Anim.Build(element, ActiveLayer));
        }
        Commit(label);
        RaiseAll();
    }

    public void MoveSelection(double dx, double dy)
    {
        if (Selection.Count == 0) return;
        foreach (var element in SelectionElements()) OffsetElement(element, dx, dy);
        Commit("Перемещение");
        RaiseAll();
    }

    /// <summary>Сдвиг элемента в мировых координатах с пересчётом в систему его родителя.</summary>
    public void OffsetElement(Element element, double worldDx, double worldDy)
    {
        Mat parent = ParentMatrix(element);
        if (parent.TryInvert(out Mat inv))
        {
            Vec d = inv.ApplyVector(new Vec(worldDx, worldDy));
            element.X += d.X;
            element.Y += d.Y;
        }
        else
        {
            element.X += worldDx;
            element.Y += worldDy;
        }
    }

    /// <summary>Поворот элемента вокруг мировой точки.</summary>
    public void RotateElement(Element element, double deltaDeg, Vec worldPivot)
    {
        Mat parent = ParentMatrix(element);
        Vec origin = parent.Apply(new Vec(element.X, element.Y));
        element.Rotation += deltaDeg;
        Vec pivoted = RotatePoint(origin, worldPivot, deltaDeg);
        OffsetElement(element, pivoted.X - origin.X, pivoted.Y - origin.Y);
    }

    public static Vec RotatePoint(Vec p, Vec pivot, double deg)
    {
        double r = deg * Math.PI / 180.0;
        double cos = Math.Cos(r), sin = Math.Sin(r);
        double dx = p.X - pivot.X, dy = p.Y - pivot.Y;
        return new Vec(pivot.X + dx * cos - dy * sin, pivot.Y + dx * sin + dy * cos);
    }

    public void DuplicateSelection(double dx = 16, double dy = 16)
    {
        if (Selection.Count == 0) return;
        var copies = new List<Element>();
        foreach (var element in SelectionElements())
        {
            var copy = element.Clone();
            copy.Id = Ids.New("el");
            copy.X += dx;
            copy.Y += dy;
            copies.Add(copy);
        }
        Keyframe kf = EnsureKeyframeForEdit();
        foreach (var c in copies) kf.Elements.Add(c);
        SetSelection(copies.Select(c => c.Id));
        Commit("Дублировать объект");
        RaiseAll();
    }

    public void BringToFront(bool front)
    {
        if (Selection.Count == 0) return;
        var (_, _, kf) = FindItem(Selection[0]);
        if (kf == null) return;
        foreach (var element in SelectionElements())
        {
            var list = kf.Elements.Contains(element) ? kf.Elements : FindGroupParent(kf.Elements, element.Id)?.Children;
            if (list == null) continue;
            list.Remove(element);
            if (front) list.Add(element);
            else list.Insert(0, element);
        }
        Commit(front ? "На передний план" : "На задний план");
        RaiseAll();
    }

    public void MoveSelectionOrder(int delta)
    {
        if (Selection.Count == 0) return;
        var (_, _, kf) = FindItem(Selection[0]);
        if (kf == null) return;
        foreach (var element in SelectionElements())
        {
            List<Element> list = kf.Elements.Contains(element) ? kf.Elements : FindGroupParent(kf.Elements, element.Id)?.Children;
            if (list == null) continue;
            int i = list.IndexOf(element);
            int target = Math.Max(0, Math.Min(list.Count - 1, i + delta));
            if (i == target) continue;
            list.RemoveAt(i);
            list.Insert(target, element);
        }
        Commit("Порядок объектов");
        RaiseAll();
    }

    public void GroupSelection()
    {
        var selected = SelectionElements().Where(e => !(e is GroupElement)).ToList();
        if (selected.Count < 2) { Report("Нужно минимум два объекта"); return; }
        var (_, _, kf) = FindItem(selected[0].Id);
        if (kf == null) return;
        int index = selected.Max(e => kf.Elements.IndexOf(e));
        foreach (var e in selected) kf.Elements.Remove(e);
        var group = new GroupElement { Name = "Группа" };
        foreach (var e in selected) group.Children.Add(e);
        kf.Elements.Insert(Math.Max(0, index), group);
        SetSelection(new[] { group.Id });
        Commit("Сгруппировать");
        RaiseAll();
    }

    public void UngroupSelection()
    {
        var groups = SelectionElements().OfType<GroupElement>().ToList();
        if (groups.Count == 0) { Report("Выделите группу"); return; }
        var newSelection = new List<string>();
        foreach (var group in groups)
        {
            var (_, _, kf) = FindItem(group.Id);
            if (kf == null) continue;
            List<Element> list = kf.Elements.Contains(group) ? kf.Elements : FindGroupParent(kf.Elements, group.Id)?.Children;
            if (list == null) continue;
            int index = list.IndexOf(group);
            list.Remove(group);
            Mat gm = group.LocalMatrix;
            var children = new List<Element>(group.Children);
            int offset = 0;
            foreach (var child in children)
            {
                Mat combined = gm.Multiply(child.LocalMatrix);
                combined.Decompose(out double x, out double y, out double rot, out double sx, out double sy);
                child.X = x; child.Y = y; child.Rotation = rot; child.ScaleX = sx; child.ScaleY = sy;
                list.Insert(Math.Min(list.Count, index + offset), child);
                offset++;
                newSelection.Add(child.Id);
            }
        }
        SetSelection(newSelection);
        Commit("Разгруппировать");
        RaiseAll();
    }

    /* ------------------------------------------------------------------ */
    /*  Символы                                                            */
    /* ------------------------------------------------------------------ */

    public Symbol CreateSymbolFromSelection(string name, SymbolKind kind)
    {
        var selected = SelectionElements();
        if (selected.Count == 0) { Report("Сначала выделите объекты"); return null; }

        RectD box = RectD.Empty;
        foreach (var e in selected) box = RectD.Union(box, Anim.WorldBounds(e, Project));
        if (box.IsEmpty) box = new RectD(0, 0, 100, 100);
        Vec center = box.Center;

        var (_, _, hostKeyframe) = FindItem(selected[0].Id);
        if (hostKeyframe == null) hostKeyframe = EnsureKeyframeForEdit();

        foreach (var e in selected)
        {
            var (_, _, owner) = FindItem(e.Id);
            if (owner == null) continue;
            if (owner.Elements.Remove(e)) continue;
            var parent = FindGroupParent(owner.Elements, e.Id);
            parent?.Children.Remove(e);
        }

        var symbolKeyframe = new Keyframe { Index = 0, Duration = 1 };
        foreach (var e in selected)
        {
            var copy = e.Clone();
            copy.X -= center.X;
            copy.Y -= center.Y;
            symbolKeyframe.Elements.Add(copy);
        }

        var symbol = new Symbol
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Символ" : name,
            Kind = kind,
            Bounds = new RectD(-box.W / 2, -box.H / 2, Math.Max(1, box.W), Math.Max(1, box.H)),
        };
        symbol.Timeline = new Scene
        {
            Name = symbol.Name,
            Layers = { new Layer { Name = "Слой 1", Color = LayerPalette.Pick(0), Frames = { symbolKeyframe } } },
        };
        Project.Library[symbol.Id] = symbol;

        var instance = new InstanceElement
        {
            SymbolId = symbol.Id,
            SymbolName = symbol.Name,
            X = center.X,
            Y = center.Y,
            Name = symbol.Name,
        };
        if (hostKeyframe != null) hostKeyframe.Elements.Add(instance);
        SetSelection(new[] { instance.Id });
        Commit("Создать символ");
        RaiseAll();
        return symbol;
    }

    public void RefreshSymbolBounds(string symbolId)
    {
        if (!Project.Library.TryGetValue(symbolId, out Symbol symbol)) return;
        RectD box = Anim.ContentBoundsAll(symbol.Timeline, Project);
        if (!box.IsEmpty) symbol.Bounds = box;
    }

    public void RefreshAllSymbolBounds()
    {
        foreach (string id in Project.Library.Keys.ToList()) RefreshSymbolBounds(id);
    }

    public void EnterSymbol(string symbolId)
    {
        if (!Project.Library.TryGetValue(symbolId, out Symbol symbol)) return;
        EditPath.Add(new EditPathEntry { SymbolId = symbolId, Name = symbol.Name });
        Selection.Clear();
        Playhead = 0;
        ActiveLayerId = symbol.Timeline.Layers.Count > 0 ? symbol.Timeline.Layers[0].Id : null;
        RaiseAll();
    }

    public void ExitSymbol()
    {
        if (EditPath.Count == 0) return;
        var leaving = EditPath[EditPath.Count - 1];
        EditPath.RemoveAt(EditPath.Count - 1);
        RefreshSymbolBounds(leaving.SymbolId);
        Selection.Clear();
        Playhead = 0;
        ActiveLayerId = ActiveScene.Layers.Count > 0 ? ActiveScene.Layers[0].Id : null;
        RaiseAll();
    }

    public void RenameSymbol(string symbolId, string name)
    {
        if (!Project.Library.TryGetValue(symbolId, out Symbol symbol)) return;
        symbol.Name = name;
        Commit("Переименовать символ");
        RaiseDocument();
    }

    public Symbol DuplicateSymbol(string symbolId)
    {
        if (!Project.Library.TryGetValue(symbolId, out Symbol symbol)) return null;
        var copy = Json.Clone(symbol);
        copy.Id = Ids.New("sym");
        copy.Name = symbol.Name + " копия";
        Project.Library[copy.Id] = copy;
        Commit("Дублировать символ");
        RaiseAll();
        return copy;
    }

    public void DeleteSymbol(string symbolId)
    {
        if (!Project.Library.ContainsKey(symbolId)) return;
        Project.Library.Remove(symbolId);
        Commit("Удалить символ");
        RaiseAll();
    }

    /// <summary>Ставит экземпляр символа в центр сцены.</summary>
    public InstanceElement AddInstance(string symbolId)
    {
        if (!Project.Library.TryGetValue(symbolId, out Symbol symbol)) return null;
        var instance = new InstanceElement
        {
            SymbolId = symbolId,
            SymbolName = symbol.Name,
            X = Project.Stage.Width / 2 + symbol.Bounds.Center.X,
            Y = Project.Stage.Height / 2 + symbol.Bounds.Center.Y,
            Name = symbol.Name,
        };
        AddElement(instance, "Экземпляр символа");
        return instance;
    }

    public void SwapSymbol(string symbolId)
    {
        var instances = SelectionElements().OfType<InstanceElement>().ToList();
        if (instances.Count == 0 || !Project.Library.TryGetValue(symbolId, out Symbol symbol)) return;
        foreach (var instance in instances)
        {
            instance.SymbolId = symbolId;
            instance.SymbolName = symbol.Name;
        }
        Commit("Сменить символ");
        RaiseAll();
    }
}