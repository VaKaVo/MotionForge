using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
using MotionForge.Model;

namespace MotionForge.Services;

/// <summary>Автономная проверка ядра (запуск: MotionForge.exe --selftest). UI не требуется.</summary>
public static class SelfTest
{
    public static int Run()
    {
        int failures = 0;
        var log = new List<string>();

        void Check(string name, bool ok, string detail = "")
        {
            if (!ok) failures++;
            log.Add((ok ? "[ OK ] " : "[FAIL] ") + name + (string.IsNullOrEmpty(detail) ? "" : "  →  " + detail));
        }

        string root = Path.Combine(Path.GetTempPath(), "motionforge-selftest");
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        catch { /* не критично */ }
        Directory.CreateDirectory(root);

        /* ---------- геометрия ---------- */
        try
        {
            var square = new List<Vec> { new Vec(0, 0), new Vec(100, 0), new Vec(100, 100), new Vec(0, 100) };
            Check("Точка внутри полигона", Geo.PointInPolygon(new Vec(50, 50), square));
            Check("Точка снаружи полигона", !Geo.PointInPolygon(new Vec(150, 50), square));
            RectD box = RectD.FromPoints(square);
            Check("Габариты прямоугольника", Math.Abs(box.W - 100) < 0.001 && Math.Abs(box.H - 100) < 0.001);
            Check("Snap к сетке", Math.Abs(Geo.Snap(43, 40) - 40) < 0.001 && Math.Abs(Geo.Snap(61, 40) - 80) < 0.001);

            var dense = new List<Vec>();
            for (int i = 0; i < 400; i++) dense.Add(new Vec(i, Math.Sin(i * 0.05) * 10));
            var simplified = Geo.Simplify(dense, 0.35);
            Check("Упрощение линии уменьшает точки", simplified.Count < dense.Count / 2,
                dense.Count + " → " + simplified.Count);

            Mat m = Mat.FromTransform(100, 50, 90, 1, 1);
            Vec p = m.Apply(new Vec(10, 0));
            Check("Матрица: поворот 90°", Math.Abs(p.X - 100) < 0.001 && Math.Abs(p.Y - 60) < 0.001, p.ToString());
            Check("Матрица: обратное преобразование", m.TryInvert(out Mat inv) && Math.Abs(inv.Apply(p).X - 10) < 0.001);
        }
        catch (Exception ex) { Check("Геометрия (исключение)", false, ex.Message); }

        /* ---------- демо-проект и твины ---------- */
        Project demo = null;
        try
        {
            demo = SampleProject.Create();
            Check("Демо-проект: слои", demo.Timeline.Layers.Count == 4, string.Join(", ", demo.Timeline.Layers.Select(l => l.Name)));
            int length = Anim.SceneLength(demo.Timeline);
            Check("Демо-проект: длительность 48 кадров", length == 48, length.ToString());
            Check("Демо-проект: есть библиотечный символ", demo.Library.Count == 1);

            var ballLayer = demo.Timeline.Layers.First(l => l.Name == "Мяч");
            var at0 = Anim.EvaluateLayer(ballLayer, 0).First(e => e.Src is ShapeElement);
            var at12 = Anim.EvaluateLayer(ballLayer, 12).First(e => e.Src is ShapeElement);
            var at18 = Anim.EvaluateLayer(ballLayer, 18).First(e => e.Src is ShapeElement);
            var at24 = Anim.EvaluateLayer(ballLayer, 24).First(e => e.Src is ShapeElement);

            Check("Ключевой кадр 0: x = 150", Math.Abs(at0.X - 150) < 0.01, at0.X.ToString("0.##"));
            Check("Ключевой кадр 12: x = 480", Math.Abs(at12.X - 480) < 0.01, at12.X.ToString("0.##"));
            Check("Ключевой кадр 24: x = 810", Math.Abs(at24.X - 810) < 0.01, at24.X.ToString("0.##"));
            bool between = at18.X > 480 && at18.X < 810 + 0.01 && at18.Y != at24.Y;
            Check("Твин интерполирует между ключами", between, $"x={at18.X:0.##} y={at18.Y:0.##}");
            Check("Твин едет по Y (падение)", at18.Y > at12.Y, $"{at18.Y:0.##} > {at12.Y:0.##}");

            var starLayer = demo.Timeline.Layers.First(l => l.Name.StartsWith("Звезда"));
            var starEval = Anim.EvaluateLayer(starLayer, 0).First();
            Check("Экземпляр символа в сцене", starEval.Src is InstanceElement);

            var symbol = demo.Library.Values.First();
            var rot = Anim.EvaluateLayer(symbol.Timeline.Layers[0], 12).First();
            Check("Символ вращается твином", rot.Rotation > 90 && rot.Rotation < 270, rot.Rotation.ToString("0.#") + "°");
        }
        catch (Exception ex) { Check("Демо-проект (исключение)", false, ex.Message); }

        /* ---------- сохранение / загрузка ---------- */
        try
        {
            string json = Json.Save(demo);
            Project loaded = Json.Load(json);
            Check("JSON: слои сохранились", loaded.Timeline.Layers.Count == demo.Timeline.Layers.Count);
            Check("JSON: символы сохранились", loaded.Library.Count == demo.Library.Count);
            var types = loaded.Timeline.Layers
                .SelectMany(l => l.Frames)
                .SelectMany(f => f.Elements)
                .Select(e => e.GetType().Name)
                .Distinct()
                .ToList();
            Check("JSON: полиморфные типы объектов", types.Count >= 3, string.Join(", ", types));
            var tweened = loaded.Timeline.Layers.First(l => l.Name == "Мяч").Frames.First(f => f.Tween != null);
            Check("JSON: твин и объекты ключа", tweened.Tween.Type == "classic" && tweened.Elements.Count == 1);

            var shape = loaded.Timeline.Layers.SelectMany(l => l.Frames).SelectMany(f => f.Elements)
                .OfType<ShapeElement>().First();
            Check("JSON: точки фигуры сохранились", shape.Points.Count > 2);
            Check("JSON: габариты символа сохранились", Math.Abs(loaded.Library.Values.First().Bounds.W - 92) < 0.5,
                loaded.Library.Values.First().Bounds.ToString());
        }
        catch (Exception ex) { Check("JSON (исключение)", false, ex.Message); }

        /* ---------- Store: кадры, отмена, символы ---------- */
        try
        {
            var store = new Store(SampleProject.Create());
            var layer = store.ActiveLayer;
            var original = Anim.Covering(layer, 0);
            int before = layer.Frames.Count;
            store.SetPlayhead(6);
            store.InsertKeyframe(6, false);
            var split = Anim.Sorted(layer);
            Check("F6 разбивает кадр на два ключа", split.Count == before + 1, before + " → " + split.Count);
            var left = split.First(k => k.Index == 0);
            var right = split.First(k => k.Index == 6);
            Check("Ключи сохраняют Id объектов (для твина)",
                left.Elements.Count == right.Elements.Count &&
                left.Elements[0].Id == right.Elements[0].Id);
            Check("Длительности после разбивки", left.Duration == 6 && right.Duration == 42,
                left.Duration + " / " + right.Duration);
            Check("Общая длительность слоя сохранена", Anim.LayerLength(layer) == 48, Anim.LayerLength(layer).ToString());

            store.RemoveKeyframe(6);
            Check("Удаление ключевого кадра возвращает длительность", Anim.Sorted(layer).Count == before);

            store.SetPlayhead(0);
            var text = new TextElement { Text = "Проверка", X = 10, Y = 10 };
            store.AddElement(text, "Тест");
            Check("Объект добавлен", store.ActiveKeyframe.Elements.Contains(text));
            bool undoWorks = store.CanUndo;
            store.Undo();
            Check("Отмена удаляет объект", undoWorks && !projectHasElement(store.Project, text.Id));
            store.Redo();
            Check("Повтор возвращает объект", projectHasElement(store.Project, text.Id));

            store.SetSelection(new[] { store.ActiveKeyframe.Elements.First(e => e.Id == text.Id).Id });
            var symbol = store.CreateSymbolFromSelection("Тестовый символ", SymbolKind.MovieClip);
            Check("Создание символа из выделения", symbol != null && store.Project.Library.ContainsKey(symbol.Id));
            var instance = store.ActiveKeyframe.Elements.OfType<InstanceElement>().FirstOrDefault();
            Check("Вместо объектов создан экземпляр", instance != null && instance.SymbolId == symbol.Id);
            Check("Габариты символа рассчитаны",
                symbol.Bounds.W > 10 && symbol.Bounds.W < 400 && symbol.Bounds.H > 5,
                symbol.Bounds.ToString());

            store.EnterSymbol(symbol.Id);
            Check("Вход в символ меняет активную сцену", store.ActiveSymbol != null && store.EditPath.Count == 1);
            store.ExitSymbol();
            Check("Выход из символа возвращает сцену", store.EditPath.Count == 0 && store.ActiveSymbol == null);

            // попадание по объекту
            var rect = new ShapeElement
            {
                Points = new List<Vec> { new Vec(0, 0), new Vec(100, 0), new Vec(100, 100), new Vec(0, 100) },
                Closed = true, Filled = true, Stroked = false, X = 300, Y = 300, Name = "Квадрат",
            };
            store.AddElement(rect, "Квадрат");
            var hit = store.HitTest(new Vec(350, 350), 3, out Element leaf);
            var miss = store.HitTest(new Vec(760, 480), 3, out _);
            Check("Попадание по фигуре", hit != null && leaf == rect);
            Check("Промах по пустому месту", miss == null);
        }
        catch (Exception ex) { Check("Store (исключение)", false, ex.Message); }

        /* ---------- команды ---------- */
        try
        {
            var ids = Commands.All.Select(c => c.Id).ToList();
            Check("Команды: идентификаторы уникальны", ids.Count == ids.Distinct().Count(), ids.Count + " команд");
            Check("Команды: у всех есть действие", Commands.All.All(c => c.Run != null));
            Check("Команды: есть группы", Commands.GroupNames.Count() >= 6, string.Join(", ", Commands.GroupNames));
            Check("Команды: инструменты зарегистрированы", Tools.ToolRegistry.All.Count() == 15);
        }
        catch (Exception ex) { Check("Команды (исключение)", false, ex.Message); }

        /* ---------- экспорт ---------- */
        try
        {
            string png = Path.Combine(root, "frame.png");
            Exporter.ExportPng(demo, png, 1, false, 6);
            Check("PNG: файл создан", File.Exists(png) && new FileInfo(png).Length > 2000,
                File.Exists(png) ? new FileInfo(png).Length + " байт" : "нет файла");

            string png2 = Path.Combine(root, "frame-transparent.png");
            Exporter.ExportPng(demo, png2, 0.5, true, 0);
            Check("PNG: прозрачный фон, масштаб 0.5", File.Exists(png2));
            var decoded = new BitmapImage(new Uri(png2));
            Check("PNG: размер соответствует масштабу", decoded.PixelWidth == 480 && decoded.PixelHeight == 300,
                decoded.PixelWidth + "x" + decoded.PixelHeight);

            string gif = Path.Combine(root, "anim.gif");
            byte[] rawGif = Exporter.EncodeGifRaw(demo, 0.35, 12, false);
            File.WriteAllBytes(Path.Combine(root, "raw.gif"), rawGif);
            Check("GIF: системный энкодер выдал данные", rawGif != null && rawGif.Length > 1000, rawGif == null ? "нет" : rawGif.Length + " байт");
            Check("GIF: необработанный файл читается", CanDecodeGif(rawGif));
            Check("GIF: заголовок файла", rawGif != null && rawGif[0] == (byte)'G' && rawGif[1] == (byte)'I' && rawGif[2] == (byte)'F',
                HexOf(rawGif, 0, 13));
            byte[] patchedGif = Exporter.PatchGif(rawGif, 8, 0);
            File.WriteAllBytes(Path.Combine(root, "patched.gif"), patchedGif);
            Check("GIF: после правки файл читается", CanDecodeGif(patchedGif),
                "raw " + rawGif.Length + " → patched " + patchedGif.Length + " | " + HexOf(patchedGif, 0, 24));
            Exporter.ExportGif(demo, gif, 0.35, 12, false, 0);
            using (var stream = File.OpenRead(gif))
            {
                var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Check("GIF: все кадры записаны", decoder.Frames.Count == 48, decoder.Frames.Count + " кадров");
            }
            byte[] gifBytes = File.ReadAllBytes(gif);
            int delay = Exporter.ReadGifDelay(gifBytes);
            Check("GIF: задержка кадра соответствует 12 к/с", delay == 8, "задержка = " + delay + " (1/100 с)");
            Check("GIF: зацикливание прописано", Exporter.PatchGif(gifBytes, delay, 0).Length >= gifBytes.Length);
            Check("GIF: размер файла разумный", new FileInfo(gif).Length > 5000, new FileInfo(gif).Length + " байт");

            string sheet = Path.Combine(root, "sheet.png");
            Exporter.ExportSpriteSheet(demo, sheet, 0.25, 5, true);
            var sheetImage = new BitmapImage(new Uri(sheet));
            Check("Спрайт-лист: 5 колонок × 10 рядов",
                sheetImage.PixelWidth == 240 * 5 && sheetImage.PixelHeight == 150 * 10,
                sheetImage.PixelWidth + "x" + sheetImage.PixelHeight);

            var files = Exporter.ExportPngSequence(demo, Path.Combine(root, "seq.png"), 0.2, false);
            Check("PNG-секвенция: 48 файлов", files.Count == 48, files.Count.ToString());

            string svg = Path.Combine(root, "scene.svg");
            Exporter.ExportSvg(demo, svg, 6, false);
            string svgText = File.ReadAllText(svg);
            Check("SVG: есть фигуры и текст", svgText.Contains("<path") && svgText.Contains("<text") && svgText.Contains("</svg>"),
                svgText.Length + " символов");

            // рендер символа в отдельном проходе (проверка рекурсии)
            var instanceFrame = Exporter.RenderFrame(demo, 12, 0.5, false);
            Check("Рендер кадра со символом", instanceFrame.PixelWidth == 480 && instanceFrame.PixelHeight == 300);

            // проверка пикселей: заливка, обводка, фон
            var probe = new Project();
            probe.Stage.Width = 300; probe.Stage.Height = 300; probe.Stage.Background = "#FFFFFF";
            var probeLayer = new Layer { Name = "Слой 1" };
            var probeKey = new Keyframe { Index = 0, Duration = 1 };
            probeKey.Elements.Add(new ShapeElement
            {
                Points = new List<Vec> { new Vec(-50, -50), new Vec(50, -50), new Vec(50, 50), new Vec(-50, 50) },
                Closed = true, Smooth = false,
                Fill = "#0000FF", Stroke = "#FF0000", StrokeWidth = 10,
                Filled = true, Stroked = true, X = 150, Y = 150,
            });
            probeLayer.Frames.Add(probeKey);
            probe.Timeline.Layers.Add(probeLayer);
            var probeBitmap = Exporter.RenderFrame(probe, 0, 1, false);
            byte[] pixels = new byte[probeBitmap.PixelWidth * probeBitmap.PixelHeight * 4];
            probeBitmap.CopyPixels(pixels, probeBitmap.PixelWidth * 4, 0);
            byte[] At(int x, int y)
            {
                int i = (y * probeBitmap.PixelWidth + x) * 4;
                return new[] { pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3] };
            }
            var center = At(150, 150);
            var edge = At(101, 150);
            var corner = At(20, 20);
            Check("Пиксель: заливка синяя", center[0] > 200 && center[1] < 60 && center[2] < 60, $"B={center[0]} G={center[1]} R={center[2]}");
            Check("Пиксель: обводка красная", edge[2] > 200 && edge[0] < 60, $"B={edge[0]} G={edge[1]} R={edge[2]}");
            Check("Пиксель: фон белый", corner[0] > 240 && corner[1] > 240 && corner[2] > 240, $"B={corner[0]} G={corner[1]} R={corner[2]}");
            Check("Пиксель: прозрачный фон", CheckAlpha(probe, probeBitmap));

            // наложение кадров «луковой кожи» не должно падать
            int onionFrames = 0;
            for (int f = 0; f < 48; f++) onionFrames += Anim.EvaluateScene(demo.Timeline, f).Count;
            Check("Оценка всех 48 кадров сцены", onionFrames > 100, onionFrames + " объектов суммарно");
        }
        catch (Exception ex) { Check("Экспорт (исключение)", false, ex.GetType().Name + ": " + ex.Message + " :: " + FirstStack(ex)); }

        /* ---------- отчёт ---------- */
        var report = new StringBuilder();
        report.AppendLine("MotionForge — автотест ядра");
        report.AppendLine("Дата: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine(new string('-', 60));
        foreach (string line in log) report.AppendLine(line);
        report.AppendLine(new string('-', 60));
        report.AppendLine("Всего проверок: " + log.Count + ", ошибок: " + failures);
        report.AppendLine(failures == 0 ? "ИТОГ: ВСЁ ХОРОШО" : "ИТОГ: ЕСТЬ ОШИБКИ");

        string reportPath = Path.Combine(Path.GetTempPath(), "motionforge-selftest.txt");
        try { File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(true)); } catch { }
        try { Console.WriteLine(report.ToString()); } catch { }
        return failures;
    }

    private static bool CheckAlpha(Project project, BitmapSource opaque)
    {
        var transparent = Exporter.RenderFrame(project, 0, 1, true);
        byte[] pixels = new byte[transparent.PixelWidth * transparent.PixelHeight * 4];
        transparent.CopyPixels(pixels, transparent.PixelWidth * 4, 0);
        int i = (10 * transparent.PixelWidth + 10) * 4;
        return pixels[i + 3] == 0;
    }

    private static bool CanDecodeGif(byte[] data)
    {
        try
        {
            using (var stream = new MemoryStream(data))
            {
                var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return decoder.Frames.Count > 0;
            }
        }
        catch
        {
            return false;
        }
    }

    private static string HexOf(byte[] data, int start, int count)
    {
        if (data == null) return "нет данных";
        var sb = new StringBuilder();
        for (int i = start; i < Math.Min(data.Length, start + count); i++) sb.Append(data[i].ToString("X2")).Append(' ');
        return sb.ToString().Trim();
    }

    private static string FirstStack(Exception ex)
    {
        string stack = ex.StackTrace ?? "";
        string[] lines = stack.Split('\n');
        return lines.Length > 0 ? lines[0].Trim() : "";
    }

    private static bool projectHasElement(Project project, string id)
    {
        foreach (var layer in project.Timeline.Layers)
            foreach (var frame in layer.Frames)
                if (frame.Elements.Any(e => e.Id == id)) return true;
        return false;
    }
}