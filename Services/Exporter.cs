using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Xml;
using System.Xml.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MotionForge.Model;

namespace MotionForge.Services;

public class ExportSettings
{
    public string Format = "png";     // png | gif | sheet | seq | svg
    public double Scale = 1;
    public bool Transparent;
    public int Fps = 24;
    public int Columns = 5;
    public int Frame = 0;
    public bool Ffmpeg;
}

/// <summary>Выгрузка результата: кадр PNG, анимированный GIF, спрайт-лист, PNG-секвенция, SVG.</summary>
public static class Exporter
{
    public static BitmapSource RenderFrame(Project project, int frame, double scale, bool transparent)
    {
        StageInfo stage = project.Stage;
        scale = Math.Max(0.05, Math.Min(6, scale));
        int pixelWidth = Math.Max(1, (int)Math.Round(stage.Width * scale));
        int pixelHeight = Math.Max(1, (int)Math.Round(stage.Height * scale));

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            if (!transparent)
                dc.DrawRectangle(Renderer.GetBrush(stage.Background, 1), null, new Rect(0, 0, stage.Width, stage.Height));

            var options = new RenderOptions
            {
                Project = project,
                PixelsPerDip = scale,
                CurrentFrame = frame,
            };
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, stage.Width, stage.Height)));
            Renderer.DrawScene(dc, project, project.Timeline, frame, options);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
    }

    public static void ExportPng(Project project, string path, double scale, bool transparent, int frame)
    {
        SavePng(RenderFrame(project, frame, scale, transparent), path);
    }

    /// <summary>Анимированный GIF: покадровая запись с задержкой и бесконечным циклом.</summary>
    public static void ExportGif(Project project, string path, double scale, int fps, bool transparent, int loop,
        Action<int, int> progress = null, Func<bool> cancelled = null)
    {
        int delay = Math.Max(2, (int)Math.Round(100.0 / Math.Max(1, fps)));
        byte[] raw = EncodeGifRaw(project, scale, fps, transparent, progress, cancelled);
        if (raw == null) return;
        File.WriteAllBytes(path, PatchGif(raw, delay, loop));
    }

    /// <summary>Необработанный поток GIF от системного энкодера.</summary>
    public static byte[] EncodeGifRaw(Project project, double scale, int fps, bool transparent,
        Action<int, int> progress = null, Func<bool> cancelled = null)
    {
        int length = Math.Max(1, Anim.SceneLength(project.Timeline));
        int delay = Math.Max(2, (int)Math.Round(100.0 / Math.Max(1, fps)));

        var encoder = new GifBitmapEncoder();
        for (int frame = 0; frame < length; frame++)
        {
            if (cancelled != null && cancelled()) return null;
            progress?.Invoke(frame + 1, length);

            BitmapSource bitmap = ConvertForGif(RenderFrame(project, frame, scale, transparent), transparent);
            var metadata = new BitmapMetadata("gif");
            try
            {
                metadata.SetQuery("/grctlext/Delay", (ushort)delay);
                metadata.SetQuery("/grctlext/Disposal", (byte)2);
            }
            catch
            {
                // часть метаданных системный энкодер может не принять — задержку поправим после
            }
            encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        }

        using (var buffer = new MemoryStream())
        {
            encoder.Save(buffer);
            return buffer.ToArray();
        }
    }

    /// <summary>
    /// WPF-энкодер игнорирует часть метаданных GIF, поэтому задержку кадров и зацикливание
    /// прописываем в готовый файл сами.
    /// </summary>
    public static byte[] PatchGif(byte[] data, int delayCentiseconds, int loop)
    {
        if (data == null || data.Length < 14) return data;
        if (data[0] != (byte)'G' || data[1] != (byte)'I' || data[2] != (byte)'F') return data;

        var result = new List<byte>(data.Length + 32);
        result.AddRange(Slice(data, 0, 13));
        int pos = 13;
        byte packed = data[10];
        if ((packed & 0x80) != 0)
        {
            int size = 3 * (1 << ((packed & 0x07) + 1));
            result.AddRange(Slice(data, pos, size));
            pos += size;
        }

        if (loop >= 0 && !HasLoopExtension(data))
        {
            result.Add(0x21);
            result.Add(0xFF);
            result.Add(0x0B);
            result.AddRange(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            result.Add(0x03);
            result.Add(0x01);
            result.Add((byte)(loop & 0xFF));
            result.Add((byte)((loop >> 8) & 0xFF));
            result.Add(0x00);
        }

        while (pos < data.Length)
        {
            byte block = data[pos];

            if (block == 0x21 && pos + 1 < data.Length && data[pos + 1] == 0xF9 && pos + 7 < data.Length)
            {
                // Графическое управление: 21 F9 04 | packed | delay(2) | индекс | 00
                result.AddRange(Slice(data, pos, 3));
                result.Add(data[pos + 3]);
                result.Add((byte)(delayCentiseconds & 0xFF));
                result.Add((byte)((delayCentiseconds >> 8) & 0xFF));
                result.Add(data[pos + 6]);
                result.Add(data[pos + 7]);
                pos += 8;
                continue;
            }

            if (block == 0x21 && pos + 1 < data.Length)
            {
                result.Add(data[pos]);
                result.Add(data[pos + 1]);
                pos = CopySubBlocks(data, pos + 2, result);
                continue;
            }

            if (block == 0x2C)
            {
                result.AddRange(Slice(data, pos, 10));   // 2C + left/top/width/height + packed
                byte imagePacked = data[pos + 9];
                pos += 10;
                if ((imagePacked & 0x80) != 0)
                {
                    int size = 3 * (1 << ((imagePacked & 0x07) + 1));
                    result.AddRange(Slice(data, pos, size));
                    pos += size;
                }
                if (pos < data.Length)
                {
                    result.Add(data[pos]);
                    pos++;
                    pos = CopySubBlocks(data, pos, result);
                }
                continue;
            }

            if (block == 0x3B)
            {
                result.Add(0x3B);
                break;
            }
            break;
        }

        return result.ToArray();
    }

    private static int CopySubBlocks(byte[] data, int pos, List<byte> target)
    {
        while (pos < data.Length)
        {
            int size = data[pos];
            target.Add(data[pos]);
            pos++;
            if (size == 0) break;
            target.AddRange(Slice(data, pos, size));
            pos += size;
        }
        return pos;
    }

    private static byte[] Slice(byte[] data, int start, int length)
    {
        if (start >= data.Length) return new byte[0];
        int count = Math.Min(length, data.Length - start);
        var slice = new byte[count];
        Array.Copy(data, start, slice, 0, count);
        return slice;
    }

    private static bool HasLoopExtension(byte[] data)
    {
        byte[] needle = Encoding.ASCII.GetBytes("NETSCAPE2.0");
        for (int i = 0; i + needle.Length <= data.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (data[i + j] != needle[j]) { match = false; break; }
            if (match) return true;
        }
        return false;
    }

    /// <summary>Читает задержку первого кадра прямо из байтов GIF (используется в проверках).</summary>
    public static int ReadGifDelay(byte[] data)
    {
        for (int i = 0; i + 8 < data.Length; i++)
        {
            if (data[i] == 0x21 && data[i + 1] == 0xF9 && data[i + 2] == 0x04)
                return data[i + 4] | (data[i + 5] << 8);
        }
        return -1;
    }

    /// <summary>GIF плохо работает с полупрозрачностью — подкладываем фон при необходимости.</summary>
    private static BitmapSource ConvertForGif(BitmapSource source, bool transparent)
    {
        if (!transparent) return source;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
        }
        var target = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// <summary>
    /// Спрайт-лист: все кадры одной сеткой. Рядом с PNG пишется XML в формате
    /// Starling/Sparrow (TextureAtlas) — его читают Phaser, PixiJS, Unity-плагины и т. п.
    /// Возвращает путь к XML.
    /// </summary>
    public static string ExportSpriteSheet(Project project, string path, double scale, int columns, bool transparent,
        Action<int, int> progress = null)
    {
        int length = Math.Max(1, Anim.SceneLength(project.Timeline));
        columns = Math.Max(1, columns);
        int rows = (int)Math.Ceiling(length / (double)columns);

        BitmapSource first = RenderFrame(project, 0, scale, transparent);
        int cellW = first.PixelWidth, cellH = first.PixelHeight;
        int sheetW = cellW * columns, sheetH = cellH * rows;

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, sheetW, sheetH));
            for (int frame = 0; frame < length; frame++)
            {
                BitmapSource bitmap = frame == 0 ? first : RenderFrame(project, frame, scale, transparent);
                int col = frame % columns, row = frame / columns;
                dc.DrawImage(bitmap, new Rect(col * cellW, row * cellH, cellW, cellH));
                dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), 1),
                    new Rect(col * cellW, row * cellH, cellW, cellH));
                progress?.Invoke(frame + 1, length);
            }
        }

        var sheet = new RenderTargetBitmap(sheetW, sheetH, 96, 96, PixelFormats.Pbgra32);
        sheet.Render(visual);
        sheet.Freeze();
        SavePng(sheet, path);
        return WriteSpriteSheetXml(project, path, cellW, cellH, columns, length);
    }

    /// <summary>XML-описание спрайт-листа (формат Starling/Sparrow).</summary>
    private static string WriteSpriteSheetXml(Project project, string sheetPath, int cellW, int cellH, int columns, int length)
    {
        string prefix = Path.GetFileNameWithoutExtension(sheetPath);
        if (prefix.EndsWith("_sheet", StringComparison.OrdinalIgnoreCase))
            prefix = prefix.Substring(0, prefix.Length - "_sheet".Length);
        if (string.IsNullOrWhiteSpace(prefix)) prefix = "frame";

        int rows = (int)Math.Ceiling(length / (double)columns);
        var root = new XElement("TextureAtlas",
            new XAttribute("imagePath", Path.GetFileName(sheetPath)),
            new XAttribute("width", cellW * columns),
            new XAttribute("height", cellH * rows),
            new XAttribute("fps", project.Stage.Fps));

        for (int frame = 0; frame < length; frame++)
        {
            int col = frame % columns, row = frame / columns;
            root.Add(new XElement("SubTexture",
                new XAttribute("name", prefix + "_" + frame.ToString("0000", CultureInfo.InvariantCulture)),
                new XAttribute("x", col * cellW),
                new XAttribute("y", row * cellH),
                new XAttribute("width", cellW),
                new XAttribute("height", cellH)));
        }

        string xmlPath = Path.ChangeExtension(sheetPath, ".xml");
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(false),
        };
        using (XmlWriter writer = XmlWriter.Create(xmlPath, settings))
            document.Save(writer);
        return xmlPath;
    }

    /// <summary>Последовательность кадров: frame_0001.png … (для сборки видео в стороннем редакторе или ffmpeg).</summary>
    public static List<string> ExportPngSequence(Project project, string basePath, double scale, bool transparent,
        Action<int, int> progress = null, Func<bool> cancelled = null)
    {
        int length = Math.Max(1, Anim.SceneLength(project.Timeline));
        string directory = Path.GetDirectoryName(basePath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(basePath);
        if (string.IsNullOrWhiteSpace(name)) name = "frame";
        var files = new List<string>();
        for (int frame = 0; frame < length; frame++)
        {
            if (cancelled != null && cancelled()) break;
            string file = Path.Combine(directory, name + "_" + (frame + 1).ToString("0000", CultureInfo.InvariantCulture) + ".png");
            SavePng(RenderFrame(project, frame, scale, transparent), file);
            files.Add(file);
            progress?.Invoke(frame + 1, length);
        }
        return files;
    }

    /// <summary>Пытается собрать видео через ffmpeg, если он установлен (необязательная возможность).</summary>
    public static bool TryMakeVideo(string firstFramePath, int fps, string outputPath, out string log)
    {
        log = "";
        try
        {
            string pattern = firstFramePath.Replace("%", "%%");
            int index = pattern.LastIndexOf('_');
            if (index > 0) pattern = pattern.Substring(0, index + 1) + "%04d.png";
            var info = new System.Diagnostics.ProcessStartInfo("ffmpeg",
                "-y -framerate " + fps + " -i \"" + pattern + "\" -pix_fmt yuv420p \"" + outputPath + "\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var process = System.Diagnostics.Process.Start(info))
            {
                log = process.StandardError.ReadToEnd();
                process.WaitForExit(120000);
                return process.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            log = ex.Message;
            return false;
        }
    }

    /* ----------------------------- SVG ----------------------------- */

    public static string BuildSvg(Project project, int frame, bool transparent)
    {
        StageInfo stage = project.Stage;
        var sb = new StringBuilder();
        string bg = transparent ? "none" : stage.Background;
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"{0}\" height=\"{1}\" viewBox=\"0 0 {0} {1}\">\n",
            Num.F(stage.Width, 2), Num.F(stage.Height, 2));
        sb.AppendFormat(CultureInfo.InvariantCulture, "  <rect width=\"{0}\" height=\"{1}\" fill=\"{2}\"/>\n",
            Num.F(stage.Width, 2), Num.F(stage.Height, 2), bg);

        foreach (var ev in Anim.EvaluateScene(project.Timeline, frame))
        {
            if (!ev.Src.Visible || ev.Alpha <= 0.01) continue;
            sb.Append("  ");
            WriteSvgElement(sb, ev, project);
            sb.Append('\n');
        }

        sb.Append("</svg>\n");
        return sb.ToString();
    }

    public static void ExportSvg(Project project, string path, int frame, bool transparent)
    {
        File.WriteAllText(path, BuildSvg(project, frame, transparent), new UTF8Encoding(false));
    }

    private static void WriteSvgElement(StringBuilder sb, EvalElement ev, Project project)
    {
        Mat m = ev.Matrix;
        string transform = string.Format(CultureInfo.InvariantCulture,
            " transform=\"matrix({0},{1},{2},{3},{4},{5})\"",
            Num.F(m.A, 4), Num.F(m.B, 4), Num.F(m.C, 4), Num.F(m.D, 4), Num.F(m.E, 3), Num.F(m.F, 3));
        string opacity = ev.Alpha < 0.999
            ? " opacity=\"" + Num.F(ev.Alpha, 3) + "\""
            : "";

        switch (ev.Src)
        {
            case ShapeElement shape:
            {
                var points = ev.Points ?? shape.Points;
                var segments = Geo.Segments(points, shape.Closed, shape.Smooth);
                if (segments.Count == 0) return;
                var d = new StringBuilder();
                d.AppendFormat(CultureInfo.InvariantCulture, "M {0} {1}", Num.F(points[0].X, 3), Num.F(points[0].Y, 3));
                foreach (var seg in segments)
                    d.AppendFormat(CultureInfo.InvariantCulture, " C {0} {1} {2} {3} {4} {5}",
                        Num.F(seg.C1.X, 3), Num.F(seg.C1.Y, 3), Num.F(seg.C2.X, 3), Num.F(seg.C2.Y, 3),
                        Num.F(seg.P1.X, 3), Num.F(seg.P1.Y, 3));
                if (shape.Closed) d.Append(" Z");
                string fill = shape.Filled && shape.Closed ? (ev.Fill ?? shape.Fill) : "none";
                string stroke = shape.Stroked ? (ev.Stroke ?? shape.Stroke) : "none";
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<path d=\"{0}\" fill=\"{1}\" stroke=\"{2}\" stroke-width=\"{3}\" stroke-linejoin=\"round\" stroke-linecap=\"round\"{4}{5}/>",
                    d, fill, stroke, Num.F(Math.Max(0.1, ev.StrokeWidth), 2), transform, opacity);
                return;
            }
            case TextElement text:
            {
                var lines = (text.Text ?? "").Split('\n');
                double lineHeight = text.FontSize * text.LineHeight;
                double dx = 0; // выравнивание уже учтено положением
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<text x=\"{0}\" y=\"{1}\" font-family=\"{2}\" font-size=\"{3}\" font-weight=\"{4}\" fill=\"{5}\"{6}{7}>",
                    Num.F(dx, 2), Num.F(text.FontSize * 0.85, 2), Escape(text.FontFamily),
                    Num.F(text.FontSize, 2), text.Bold ? "bold" : "normal", ev.Color ?? text.Color, transform, opacity);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0)
                        sb.AppendFormat(CultureInfo.InvariantCulture, "<tspan x=\"{0}\" dy=\"{1}\">", Num.F(dx, 2), Num.F(lineHeight, 2));
                    sb.Append(Escape(lines[i]));
                    if (i > 0) sb.Append("</tspan>");
                }
                sb.Append("</text>");
                return;
            }
            case ImageElement image:
            {
                if (string.IsNullOrEmpty(image.Source)) return;
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<image x=\"0\" y=\"0\" width=\"{0}\" height=\"{1}\" xlink:href=\"data:image/png;base64,{2}\"{3}{4}/>",
                    Num.F(image.PixelWidth, 2), Num.F(image.PixelHeight, 2), image.Source, transform, opacity);
                return;
            }
            case GroupElement group:
            {
                sb.AppendFormat("<g{0}{1}>", transform, opacity);
                foreach (var child in group.Children)
                    WriteSvgElement(sb, Anim.Build(child, ev.Layer), project);
                sb.Append("</g>");
                return;
            }
            case InstanceElement instance:
            {
                if (project.Library.TryGetValue(instance.SymbolId, out Symbol symbol))
                {
                    sb.AppendFormat("<g{0}{1}>", transform, opacity);
                    foreach (var child in Anim.EvaluateScene(symbol.Timeline, 0))
                        WriteSvgElement(sb, child, project);
                    sb.Append("</g>");
                }
                return;
            }
        }
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}