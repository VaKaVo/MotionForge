using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
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

            var smoothPts = new List<Vec>();
            var smoothWidths = new List<double>();
            Geo.SmoothPolyline(new List<Vec> { new Vec(0, 0), new Vec(50, 0), new Vec(100, 20) },
                new List<double> { 4, 10, 16 }, 4, smoothPts, smoothWidths);
            Check("Сглаживание ленты: концы на месте",
                smoothPts.Count > 1 && Math.Abs(smoothPts[0].X) < 1e-9 && Math.Abs(smoothPts[0].Y) < 1e-9 &&
                Math.Abs(smoothPts[smoothPts.Count - 1].X - 100) < 1e-9 &&
                Math.Abs(smoothPts[smoothPts.Count - 1].Y - 20) < 1e-9,
                smoothPts.Count + " точек");
            Check("Сглаживание ленты: плотность и толщина",
                smoothPts.Count == 9 && smoothWidths.Count == 9 &&
                Math.Abs(smoothWidths[0] - 4) < 1e-6 && Math.Abs(smoothWidths[8] - 16) < 1e-6,
                smoothPts.Count + " точек, " + smoothWidths[0].ToString("0.##") + "…" +
                smoothWidths[smoothWidths.Count - 1].ToString("0.##"));

            Mat m = Mat.FromTransform(100, 50, 90, 1, 1);
            Vec p = m.Apply(new Vec(10, 0));
            Check("Матрица: поворот 90°", Math.Abs(p.X - 100) < 0.001 && Math.Abs(p.Y - 60) < 0.001, p.ToString());
            Check("Матрица: обратное преобразование", m.TryInvert(out Mat inv) && Math.Abs(inv.Apply(p).X - 10) < 0.001);
        }
        catch (Exception ex) { Check("Геометрия (исключение)", false, ex.Message); }

        /* ---------- цвет ---------- */
        try
        {
            ColorUtil.HsvToRgb(0, 1, 1, out byte red, out byte green, out byte blue);
            Check("HSV: красный", red == 255 && green == 0 && blue == 0, ColorUtil.ToHex(red, green, blue));
            ColorUtil.HsvToRgb(120, 1, 1, out red, out green, out blue);
            Check("HSV: зелёный", red == 0 && green == 255 && blue == 0, ColorUtil.ToHex(red, green, blue));
            ColorUtil.HsvToRgb(240, 1, 1, out red, out green, out blue);
            Check("HSV: синий", red == 0 && green == 0 && blue == 255, ColorUtil.ToHex(red, green, blue));
            ColorUtil.HsvToRgb(0, 1, 0, out red, out green, out blue);
            Check("HSV: нулевая яркость — чёрный", red == 0 && green == 0 && blue == 0, ColorUtil.ToHex(red, green, blue));

            ColorUtil.RgbToHsv(255, 0, 0, out double h1, out double s1, out double v1);
            Check("RGB → HSV: красный", Math.Abs(h1) < 1e-6 && Math.Abs(s1 - 1) < 1e-6 && Math.Abs(v1 - 1) < 1e-6,
                $"h={h1:0.##} s={s1:0.##} v={v1:0.##}");
            ColorUtil.HsvToRgb(210, 0.6, 0.8, out red, out green, out blue);
            ColorUtil.RgbToHsv(red, green, blue, out double h2, out double s2, out double v2);
            Check("HSV → RGB → HSV: без потерь",
                Math.Abs(h2 - 210) < 1 && Math.Abs(s2 - 0.6) < 0.01 && Math.Abs(v2 - 0.8) < 0.01,
                $"h={h2:0.##} s={s2:0.##} v={v2:0.##}");
            ColorUtil.RgbToHsv(128, 128, 128, out _, out double s3, out double v3);
            Check("Серый: насыщенность 0", Math.Abs(s3) < 1e-6 && Math.Abs(v3 - 0.502) < 0.01);
        }
        catch (Exception ex) { Check("Цвет (исключение)", false, ex.Message); }

        /* ---------- демо-проект и твины ---------- */
        Project demo = null;
        try
        {
            demo = SampleProject.Create(false);
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
            var store = new Store(SampleProject.Create(false));
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
            string sheetXml = Exporter.ExportSpriteSheet(demo, sheet, 0.25, 5, true);
            var sheetImage = new BitmapImage(new Uri(sheet));
            Check("Спрайт-лист: 5 колонок × 10 рядов",
                sheetImage.PixelWidth == 240 * 5 && sheetImage.PixelHeight == 150 * 10,
                sheetImage.PixelWidth + "x" + sheetImage.PixelHeight);

            Check("Спрайт-лист: XML рядом с PNG", File.Exists(sheetXml) && sheetXml.EndsWith(".xml", StringComparison.OrdinalIgnoreCase),
                sheetXml);
            var atlas = XDocument.Load(sheetXml);
            var subTextures = atlas.Root != null ? atlas.Root.Elements("SubTexture").ToList() : new List<XElement>();
            Check("Спрайт-лист: XML описывает все 48 кадров", subTextures.Count == 48, subTextures.Count + " кадров");
            Check("Спрайт-лист: XML — координаты и имя кадра 7",
                subTextures.Count > 6 && (int)subTextures[6].Attribute("x") == 240 &&
                (int)subTextures[6].Attribute("y") == 150 &&
                (string)subTextures[6].Attribute("name") == "sheet_0006",
                subTextures.Count > 6 ? subTextures[6].ToString() : "нет кадра");
            Check("Спрайт-лист: XML — размер кадра",
                subTextures.Count > 0 && (int)subTextures[0].Attribute("width") == 240 &&
                (int)subTextures[0].Attribute("height") == 150,
                subTextures.Count > 0 ? subTextures[0].ToString() : "нет кадров");

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

            // «Лента» с нажимом: самопересечение заливается целиком (Nonzero), а не вырезается дыркой.
            var ribbon = new Project();
            ribbon.Stage.Width = 200; ribbon.Stage.Height = 200; ribbon.Stage.Background = "#FFFFFF";
            var ribbonLayer = new Layer { Name = "Слой 1" };
            var ribbonKey = new Keyframe { Index = 0, Duration = 1 };
            ribbonKey.Elements.Add(new ShapeElement
            {
                Points = new List<Vec> { new Vec(-40, -40), new Vec(40, 40), new Vec(40, -40), new Vec(-40, 40) },
                Widths = new List<double> { 20, 20, 20, 20 },
                Closed = false, Smooth = false,
                Stroke = "#000000", StrokeWidth = 20,
                Filled = false, Stroked = true, X = 100, Y = 100,
            });
            ribbonLayer.Frames.Add(ribbonKey);
            ribbon.Timeline.Layers.Add(ribbonLayer);
            var ribbonBitmap = Exporter.RenderFrame(ribbon, 0, 1, false);
            var ribbonEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            ribbonEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(ribbonBitmap));
            using (var ribbonFile = File.Create(Path.Combine(root, "ribbon.png"))) ribbonEncoder.Save(ribbonFile);
            ((ShapeElement)ribbonKey.Elements[0]).Smooth = true;
            var ribbonSmoothBitmap = Exporter.RenderFrame(ribbon, 0, 1, false);
            var ribbonSmoothEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            ribbonSmoothEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(ribbonSmoothBitmap));
            using (var ribbonSmoothFile = File.Create(Path.Combine(root, "ribbon-smooth.png"))) ribbonSmoothEncoder.Save(ribbonSmoothFile);
            ((ShapeElement)ribbonKey.Elements[0]).Smooth = false;
            byte[] ribbonPixels = new byte[ribbonBitmap.PixelWidth * ribbonBitmap.PixelHeight * 4];
            ribbonBitmap.CopyPixels(ribbonPixels, ribbonBitmap.PixelWidth * 4, 0);
            byte[] RibbonAt(int x, int y)
            {
                int i = (y * ribbonBitmap.PixelWidth + x) * 4;
                return new[] { ribbonPixels[i], ribbonPixels[i + 1], ribbonPixels[i + 2], ribbonPixels[i + 3] };
            }
            var cross = RibbonAt(100, 100);
            Check("Лента: самопересечение залито", cross[0] < 40 && cross[1] < 40 && cross[2] < 40,
                "B=" + cross[0] + " G=" + cross[1] + " R=" + cross[2]);
            var cap = RibbonAt(60, 52);
            Check("Лента: круглый торец", cap[0] < 60 && cap[1] < 60 && cap[2] < 60,
                "B=" + cap[0] + " G=" + cap[1] + " R=" + cap[2]);

            // наложение кадров «луковой кожи» не должно падать
            int onionFrames = 0;
            for (int f = 0; f < 48; f++) onionFrames += Anim.EvaluateScene(demo.Timeline, f).Count;
            Check("Оценка всех 48 кадров сцены", onionFrames > 100, onionFrames + " объектов суммарно");
        }
        catch (Exception ex) { Check("Экспорт (исключение)", false, ex.GetType().Name + ": " + ex.Message + " :: " + FirstStack(ex)); }

        /* ---------- горячие клавиши ---------- */
        try
        {
            Check("Буква A разбирается в Key.A", Hotkeys.ParseKey("A") == System.Windows.Input.Key.A);
            Check("Буква Z разбирается в Key.Z (69)", (int)Hotkeys.ParseKey("Z") == 69, ((int)Hotkeys.ParseKey("Z")).ToString());
            Check("Разные буквы дают разные клавиши", Hotkeys.ParseKey("E") != Hotkeys.ParseKey("Z")
                && Hotkeys.ParseKey("V") != Hotkeys.ParseKey("A"));
            Check("Ctrl+D1 разбирается (масштаб 100%)", Hotkeys.IdOf("Ctrl+D1") != null, Hotkeys.IdOf("Ctrl+D1") ?? "null");

            var table = new Dictionary<string, Cmd>();
            var missing = new List<string>();
            var collisions = new List<string>();
            foreach (Cmd command in Commands.All)
            {
                if (string.IsNullOrWhiteSpace(command.Keys)) continue;
                string id = Hotkeys.IdOf(command.Keys);
                if (id == null) { missing.Add(command.Id + " (" + command.Keys + ")"); continue; }
                if (table.ContainsKey(id)) { collisions.Add(table[id].Id + " / " + command.Id + " (" + command.Keys + ")"); continue; }
                table[id] = command;
            }
            Check("Каждое сочетание распознаётся", missing.Count == 0, string.Join(", ", missing));
            Check("Нет конфликтов сочетаний", collisions.Count == 0, string.Join(", ", collisions));

            string Ctrl(System.Windows.Input.Key k) => Hotkeys.Id(System.Windows.Input.ModifierKeys.Control, k);
            string Plain(System.Windows.Input.Key k) => Hotkeys.Id(System.Windows.Input.ModifierKeys.None, k);

            bool Runs(string id, string expected)
            {
                return table.TryGetValue(id, out Cmd c) && c.Id == expected;
            }
            Check("Ctrl+Z выполняет «Отменить»", Runs(Ctrl(System.Windows.Input.Key.Z), "edit.undo"),
                table.TryGetValue(Ctrl(System.Windows.Input.Key.Z), out Cmd u) ? u.Id : "нет");
            Check("Ctrl+S выполняет «Сохранить»", Runs(Ctrl(System.Windows.Input.Key.S), "file.save"));
            Check("Ctrl+C выполняет «Копировать»", Runs(Ctrl(System.Windows.Input.Key.C), "edit.copy"));
            Check("Ctrl+K открывает палитру команд", Runs(Ctrl(System.Windows.Input.Key.K), "help.palette"));
            Check("Удаление по клавише Delete", Runs(Plain(System.Windows.Input.Key.Delete), "edit.delete"));
            Check("Инструмент «прямоугольник» на R", Runs(Plain(System.Windows.Input.Key.R), "tool.rect"));
            Check("Инструмент «масштаб» на Z", Runs(Plain(System.Windows.Input.Key.Z), "tool.zoom"));
            Check("Ctrl+, открывает «Настройки»", Runs(Ctrl(System.Windows.Input.Key.OemComma), "edit.settings"));

            int letters = 0;
            foreach (string spec in new[] { "A", "B", "C", "D", "V", "Y", "Z" })
            {
                int vk = (int)Hotkeys.ParseKey(spec);
                if (vk >= 44 && vk <= 69) letters++;
            }
            Check("Все буквенные сочетания в диапазоне 44…69", letters == 7, letters + "/7");
        }
        catch (Exception ex) { Check("Горячие клавиши (исключение)", false, ex.GetType().Name + ": " + ex.Message); }

        /* ---------- локализация ---------- */
        try
        {
            string ruPath = Path.Combine(L10n.LocalizationFolder, "en.json");
            Check("Файл английского перевода на месте", File.Exists(ruPath), L10n.LocalizationFolder);

            var enTable = File.Exists(ruPath)
                ? Json.LoadObject<Dictionary<string, string>>(File.ReadAllText(ruPath, Encoding.UTF8))
                : null;
            Check("Английский перевод читается", enTable != null && enTable.Count > 300,
                enTable == null ? "таблица не прочитана" : enTable.Count + " строк");

            string previous = L10n.Current;
            L10n.SetLanguage("ru");
            Check("Русский возвращает исходную строку", L10n.T("Файл") == "Файл");
            L10n.SetLanguage("de");
            Check("Резервная английская таблица загружается всегда", L10n.IsLoaded("en"));
            Check("Непереведённая строка берётся из английского", L10n.T("Фон сцены") == "Stage background",
                L10n.T("Фон сцены"));
            L10n.SetLanguage("en");
            Check("Английский переводит строку", L10n.T("Файл") == "File", L10n.T("Файл"));
            Check("Неизвестная строка не теряется", L10n.T("Нет такой строки — 42") == "Нет такой строки — 42");
            L10n.SetLanguage("xx");
            Check("Незнакомый язык падает на английский", L10n.T("Файл") == "File", L10n.T("Файл"));
            L10n.SetLanguage(previous);

            var languages = L10n.Languages();
            Check("Список языков начинается с русского", languages.Count > 0 && languages[0].Code == "ru");
            Check("Все файлы переводов имеют код и название",
                languages.TrueForAll(l => !string.IsNullOrWhiteSpace(l.Code) && !string.IsNullOrWhiteSpace(l.Name)),
                languages.Count + " языков");

            // Каждый файл перевода должен читаться тем же способом, что и в приложении.
            var broken = new List<string>();
            var thin = new List<string>();
            int totalKeys = 0;
            foreach (string file in Directory.GetFiles(L10n.LocalizationFolder, "*.json"))
            {
                string code = Path.GetFileNameWithoutExtension(file);
                try
                {
                    var table = Json.LoadObject<Dictionary<string, string>>(File.ReadAllText(file, Encoding.UTF8));
                    if (table == null) { broken.Add(code + " (пусто)"); continue; }
                    totalKeys += table.Count;
                    foreach (var pair in table)
                        if (string.IsNullOrWhiteSpace(pair.Value)) broken.Add(code + ": " + pair.Key);
                    if (table.Count < 40) thin.Add(code + " (" + table.Count + ")");
                }
                catch (Exception ex) { broken.Add(code + " (" + ex.GetType().Name + ")"); }
            }
            Check("Все файлы переводов читаются, пустых строк нет", broken.Count == 0,
                broken.Count == 0 ? null : string.Join(", ", broken));
            Check("В каждом языке не меньше 40 строк", thin.Count == 0,
                thin.Count == 0 ? languages.Count + " языков, " + totalKeys + " строк" : string.Join(", ", thin));

            // Свой перевод добавляется простым копированием файла — проверим на живом примере.
            string dropIn = Path.Combine(L10n.LocalizationFolder, "zz.json");
            try
            {
                File.WriteAllText(dropIn, "{\"@name\":\"Testlang\",\"Файл\":\"Zz\"}", new UTF8Encoding(false));
                var withNew = L10n.Languages();
                var found = withNew.Find(l => l.Code == "zz");
                Check("Новый файл перевода появляется в списке языков",
                    found.Code == "zz" && found.Name == "Testlang", found.Code + " / " + found.Name);

                L10n.SetLanguage("zz");
                Check("Новый язык переводит свои строки, остальное берёт из английского",
                    L10n.T("Файл") == "Zz" && L10n.T("Фон сцены") == "Stage background",
                    L10n.T("Файл") + " / " + L10n.T("Фон сцены"));
                L10n.SetLanguage("ru");
            }
            finally { try { File.Delete(dropIn); } catch { } }

            var list = L10n.Languages();
            if (list.Count > 1)
            {
                string sample = list[1].Code;
                L10n.SetLanguage(sample);
                string probe = null;
                foreach (string key in new[] { "Помощь", "Вид", "Настройки…", "Файл" })
                    if (L10n.T(key) != key) { probe = key + " → " + L10n.T(key); break; }
                Check("Второй язык в списке переводит меню", probe != null, sample + ": " + (probe ?? "ничего не переведено"));
                L10n.SetLanguage("ru");
            }
        }
        catch (Exception ex) { Check("Локализация (исключение)", false, ex.GetType().Name + ": " + ex.Message); }

        /* ---------- настройки ---------- */
        try
        {
            var defaults = new AppSettings();
            Check("Планшет включён по умолчанию", defaults.TabletEnabled && defaults.TabletPressure);
            Check("Сетка по умолчанию выключена", !defaults.GridOn && !defaults.SnapToGrid);

            var copy = defaults.Clone();
            copy.GridStep = 77;
            copy.Language = "de";
            Check("Копия настроек не меняет оригинал", defaults.GridStep == 40 && defaults.Language == "ru");

            var wild = new AppSettings { GridStep = 100000, OnionBefore = -9, OnionAfter = 99, BrushSize = 0, AutosaveMinutes = 999 };
            wild.Normalized();
            Check("Недопустимые значения приводятся к норме",
                wild.GridStep == 400 && wild.OnionBefore == 0 && wild.OnionAfter == 5 && wild.BrushSize == 1 && wild.AutosaveMinutes == 60,
                wild.GridStep + " / " + wild.OnionBefore + " / " + wild.OnionAfter + " / " + wild.BrushSize + " / " + wild.AutosaveMinutes);
            Check("Пустой язык заменяется русским", new AppSettings { Language = "  " }.Normalized().Language == "ru");

            var round = Json.LoadObject<AppSettings>(Json.SaveObject(new AppSettings
            {
                Language = "ja",
                GridOn = true,
                GridStep = 25,
                SnapToGrid = true,
                StrokeWidth = 3.5,
                BrushSize = 42,
                TabletIgnoreTouch = false,
                AutosaveMinutes = 7,
            }));
            Check("Настройки переживают сохранение и чтение",
                round != null && round.Language == "ja" && round.GridOn && round.SnapToGrid &&
                Math.Abs(round.GridStep - 25) < 1e-9 && Math.Abs(round.StrokeWidth - 3.5) < 1e-9 &&
                round.BrushSize == 42 && !round.TabletIgnoreTouch && round.AutosaveMinutes == 7);
        }
        catch (Exception ex) { Check("Настройки (исключение)", false, ex.GetType().Name + ": " + ex.Message); }

        /* ---------- толщина линии (перо) ---------- */
        try
        {
            var line = new List<Vec>();
            for (int i = 0; i <= 99; i++) line.Add(new Vec(i * 4, 0));
            var widths = new List<double>();
            for (int i = 0; i <= 99; i++) widths.Add(1 + i * 0.5);   // 1 → 50.5

            var thin = Geo.WidthsAlong(line, widths, new List<Vec> { new Vec(0, 0), new Vec(200, 0), new Vec(396, 0) });
            Check("Профиль толщины переносится на упрощённую линию", thin.Count == 3,
                thin.Count + " точек");
            Check("Толщина растёт по ходу линии",
                thin.Count == 3 && thin[0] < thin[1] && thin[1] < thin[2],
                thin.Count == 3 ? string.Join(" / ", thin.ConvertAll(v => Math.Round(v, 1))) : "нет данных");
            Check("Начало и конец профиля совпадают с исходными",
                thin.Count == 3 && Math.Abs(thin[0] - 1) < 1e-6 && Math.Abs(thin[2] - 50.5) < 1e-6);

            Check("Несогласованный профиль игнорируется",
                Geo.WidthsAlong(line, new List<double> { 5 }, new List<Vec> { new Vec(0, 0) }).Count == 0);

            var element = new ShapeElement { Widths = new List<double> { 2, 6, 10 } };
            var clone = (ShapeElement)element.Clone();
            Check("Профиль толщины копируется вместе с объектом",
                clone.Widths != null && clone.Widths.Count == 3 && Math.Abs(clone.Widths[2] - 10) < 1e-9);
        }
        catch (Exception ex) { Check("Толщина линии (исключение)", false, ex.GetType().Name + ": " + ex.Message); }

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