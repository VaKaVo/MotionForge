using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MotionForge.Services;

/// <summary>
/// Знак приложения: градиентный фон и белый орёл. Рисуется вектором, поэтому
/// одинаково выглядит и в заставке, и в иконке любого размера.
/// </summary>
public static class Branding
{
    public static readonly Color BackgroundTop = (Color)ColorConverter.ConvertFromString("#171F45");
    public static readonly Color BackgroundMiddle = (Color)ColorConverter.ConvertFromString("#4B2F9E");
    public static readonly Color BackgroundBottom = (Color)ColorConverter.ConvertFromString("#8A45D6");

    /// <summary>Рисует знак в квадрате со стороной <paramref name="size"/>.</summary>
    public static void DrawLogo(DrawingContext dc, double size, bool background = true, bool rounded = true)
    {
        if (size <= 0) return;

        if (background)
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0.1, 0),
                EndPoint = new Point(0.9, 1),
            };
            gradient.GradientStops.Add(new GradientStop(BackgroundTop, 0));
            gradient.GradientStops.Add(new GradientStop(BackgroundMiddle, 0.5));
            gradient.GradientStops.Add(new GradientStop(BackgroundBottom, 1));
            gradient.Freeze();

            double radius = rounded ? size * 0.23 : 0;
            dc.DrawRoundedRectangle(gradient, null, new Rect(0, 0, size, size), radius, radius);
        }

        var white = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        white.Freeze();

        dc.PushTransform(new ScaleTransform(size, size));
        DrawEagle(dc, white);
        dc.Pop();
    }

    /// <summary>Готовое изображение знака (для заставки, окна «О программе», иконки).</summary>
    public static BitmapSource Render(double size, bool background = true, bool rounded = true)
    {
        int pixels = Math.Max(1, (int)Math.Round(size));
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            DrawLogo(dc, pixels, background, rounded);
        }
        var bitmap = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Орёл в единичном квадрате (0…1) — так он одинаково тянется под любой размер.</summary>
    private static void DrawEagle(DrawingContext dc, Brush brush)
    {
        // Расправленные крылья: сплошной силуэт с зубцами на нижней кромке.
        dc.DrawGeometry(brush, null, Wing(false));
        dc.DrawGeometry(brush, null, Wing(true));

        // Корпус и грудь
        dc.DrawGeometry(brush, null, Body());

        // Хвост
        dc.DrawGeometry(brush, null, Tail());

        // Голова и клюв
        dc.DrawEllipse(brush, null, new Point(0.5, 0.238), 0.087, 0.087);
        dc.DrawGeometry(brush, null, Beak());
    }

    private static Geometry Wing(bool mirror)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext c = geometry.Open())
        {
            c.BeginFigure(P(0.455, 0.385, mirror), true, true);
            // верхняя кромка крыла к кончику
            c.BezierTo(P(0.395, 0.245, mirror), P(0.265, 0.115, mirror), P(0.062, 0.112, mirror), true, false);
            // зубчатая нижняя кромка (перья)
            c.LineTo(P(0.155, 0.205, mirror), true, false);
            c.LineTo(P(0.112, 0.246, mirror), true, false);
            c.LineTo(P(0.215, 0.315, mirror), true, false);
            c.LineTo(P(0.172, 0.352, mirror), true, false);
            c.LineTo(P(0.275, 0.412, mirror), true, false);
            c.LineTo(P(0.232, 0.446, mirror), true, false);
            c.LineTo(P(0.345, 0.492, mirror), true, false);
            c.LineTo(P(0.402, 0.522, mirror), true, false);
            c.LineTo(P(0.455, 0.538, mirror), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry Body()
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext c = geometry.Open())
        {
            c.BeginFigure(new Point(0.5, 0.315), true, true);
            c.BezierTo(new Point(0.575, 0.375), new Point(0.585, 0.52), new Point(0.545, 0.625), true, false);
            c.BezierTo(new Point(0.53, 0.70), new Point(0.515, 0.74), new Point(0.5, 0.775), true, false);
            c.BezierTo(new Point(0.485, 0.74), new Point(0.47, 0.70), new Point(0.455, 0.625), true, false);
            c.BezierTo(new Point(0.415, 0.52), new Point(0.425, 0.375), new Point(0.5, 0.315), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry Tail()
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext c = geometry.Open())
        {
            c.BeginFigure(new Point(0.455, 0.70), true, true);
            c.LineTo(new Point(0.372, 0.902), true, false);
            c.LineTo(new Point(0.5, 0.842), true, false);
            c.LineTo(new Point(0.628, 0.902), true, false);
            c.LineTo(new Point(0.545, 0.70), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry Beak()
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext c = geometry.Open())
        {
            c.BeginFigure(new Point(0.562, 0.205), true, true);
            c.LineTo(new Point(0.678, 0.245), true, false);
            c.LineTo(new Point(0.562, 0.278), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Точка в координатах знака; при <paramref name="mirror"/> отражается по горизонтали.</summary>
    private static Point P(double x, double y, bool mirror) => mirror ? new Point(1 - x, y) : new Point(x, y);

    /// <summary>Сохраняет изображение в PNG (превью знака, отладка иконки).</summary>
    public static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
        using (FileStream stream = File.Create(path)) encoder.Save(stream);
    }

    /// <summary>Записывает многоразмерный .ico (PNG-кадры внутри контейнера).</summary>
    public static string ExportIco(string path, params int[] sizes)
    {
        if (sizes == null || sizes.Length == 0) sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };

        var images = new List<byte[]>();
        foreach (int size in sizes)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Render(size)));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                images.Add(stream.ToArray());
            }
        }

        string folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);

        using (FileStream file = File.Create(path))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0);                 // reserved
            writer.Write((ushort)1);                 // тип: иконка
            writer.Write((ushort)images.Count);

            int offset = 6 + 16 * images.Count;
            for (int i = 0; i < images.Count; i++)
            {
                int size = sizes[i];
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);               // цветов в палитре
                writer.Write((byte)0);               // зарезервировано
                writer.Write((ushort)1);             // плоскостей
                writer.Write((ushort)32);            // бит на пиксель
                writer.Write(images[i].Length);
                writer.Write(offset);
                offset += images[i].Length;
            }

            foreach (byte[] data in images) writer.Write(data);
        }
        return path;
    }
}
