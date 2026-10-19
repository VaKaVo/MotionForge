using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace MotionForge.Services;

/// <summary>Векторные иконки инструментов (SVG-подобные path-данные, 24×24).</summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Paths = new Dictionary<string, string>
    {
        ["select"] = "M5 3 L5 19 L9.5 15 L12.5 21 L15 20 L12 14 L18 13.5 Z",
        ["subselect"] = "M4 4 L4 18 L8 14.5 L10.5 20 L12.5 19 L10 13.5 L15.5 13 Z",
        ["transform"] = "M6 6 H11 V11 H6 Z M14 14 H19 V19 H14 Z M12 6 V4 M6 12 H4",
        ["line"] = "M4 20 L20 4",
        ["rect"] = "M4 6 H20 V18 H4 Z",
        ["ellipse"] = "M12 6 C16.5 6 20 8.7 20 12 C20 15.3 16.5 18 12 18 C7.5 18 4 15.3 4 12 C4 8.7 7.5 6 12 6 Z",
        ["pencil"] = "M4 20 L4 16 L15 5 L19 9 L8 20 Z M14 6 L18 10",
        ["brush"] = "M15 4 L20 9 L11 18 C9 20 5 20 4 19 C6 18 6 16 7 14 Z M6.5 17 A1.6 1.6 0 1 0 6.5 17.2 Z",
        ["pen"] = "M12 3 L17 12 L12 21 L7 12 Z M12 10 V14",
        ["text"] = "M5 6 H19 M12 6 V19 M9 19 H15",
        ["bucket"] = "M5 11 L11 5 L18 12 L12 18 Z M11 3 V6 M19 14 C21 17 21 19 19 19 C17 19 17 17 19 14 Z",
        ["eraser"] = "M4 16 L11 9 L17 15 L13 19 H8 Z M11 9 L15 5 L20 10 L17 13",
        ["eyedropper"] = "M16 4 L20 8 L11 17 L7 13 Z M15 5 L19 9 M7 13 L4 20 L11 17",
        ["hand"] = "M8 13 V7 A1.4 1.4 0 0 1 11 7 V6 A1.4 1.4 0 0 1 14 6 V8 A1.4 1.4 0 0 1 17 8 V13 C17 18 14 20 11 20 C8 20 6 18 6 14 V11 A1.4 1.4 0 0 1 9 11",
        ["zoom"] = "M11 5 A6 6 0 1 1 10.9 5 Z M15.5 15.5 L20 20 M8 11 H14 M11 8 V14",
        ["play"] = "M7 4 L19 12 L7 20 Z",
        ["stop"] = "M6 6 H18 V18 H6 Z",
        ["prev"] = "M15 4 L7 12 L15 20 Z",
        ["next"] = "M9 4 L17 12 L9 20 Z",
        ["first"] = "M6 4 V20 M18 4 L8 12 L18 20 Z",
        ["last"] = "M18 4 V20 M6 4 L16 12 L6 20 Z",
        ["keyframe"] = "M12 6 L18 12 L12 18 L6 12 Z",
        ["tween"] = "M4 12 H20 M16 8 L20 12 L16 16",
        ["onion"] = "M12 4 C16 4 19 7.6 19 12 C19 16.4 16 20 12 20 C8 20 5 16.4 5 12 C5 7.6 8 4 12 4 Z M12 8 C14.2 8 16 9.8 16 12 C16 14.2 14.2 16 12 16",
        ["grid"] = "M4 4 H20 V20 H4 Z M4 10 H20 M4 16 H20 M10 4 V20 M16 4 V20",
        ["outline"] = "M4 7 H20 M4 12 H14 M4 17 H20",
        ["zoomIn"] = "M11 5 A6 6 0 1 1 10.9 5 Z M15.5 15.5 L20 20 M8 11 H14 M11 8 V14",
        ["zoomOut"] = "M11 5 A6 6 0 1 1 10.9 5 Z M15.5 15.5 L20 20 M8 11 H14",
        ["zoomFit"] = "M4 9 V4 H9 M15 4 H20 V9 M20 15 V20 H15 M9 20 H4 V15",
        ["add"] = "M12 5 V19 M5 12 H19",
        ["remove"] = "M5 12 H19",
        ["up"] = "M12 19 V6 M6 12 L12 6 L18 12",
        ["down"] = "M12 5 V18 M6 12 L12 18 L18 12",
        ["copy"] = "M8 8 H19 V19 H8 Z M5 16 V5 H16",
        ["symbol"] = "M12 4 L20 12 L12 20 L4 12 Z M9 12 L11 14 L15 10",
        ["library"] = "M4 5 H20 V19 H4 Z M4 9 H20 M9 9 V19",
        ["layers"] = "M12 4 L20 9 L12 14 L4 9 Z M4 13 L12 18 L20 13",
        ["folder"] = "M4 7 H10 L12 9 H20 V19 H4 Z",
        ["image"] = "M4 6 H20 V18 H4 Z M4 15 L9 10 L13 14 L16 11 L20 15",
        ["export"] = "M12 4 V15 M7 10 L12 15 L17 10 M5 19 H19",
        ["undo"] = "M9 7 H14 A4 4 0 0 1 14 15 H7 M9 7 L6 4 M9 7 L6 10",
        ["redo"] = "M15 7 H10 A4 4 0 0 0 10 15 H17 M15 7 L18 4 M15 7 L18 10",
        ["trash"] = "M5 7 H19 M9 7 V5 H15 V7 M7 7 L8 20 H16 L17 7",
        ["group"] = "M4 4 H10 V10 H4 Z M14 4 H20 V10 H14 Z M4 14 H10 V20 H4 Z M14 14 H20 V20 H14 Z",
        ["align"] = "M4 4 V20 M7 8 H16 M7 16 H20",
        ["magnet"] = "M6 4 V12 A6 6 0 0 0 18 12 V4 H14 V12 A2 2 0 0 1 10 12 V4 Z",
        ["search"] = "M10 4 A6 6 0 1 1 9.9 4 Z M14.4 14.4 L20 20",
        ["document"] = "M6 3 H14 L19 8 V21 H6 Z M14 3 V8 H19",
    };

    public static string PathData(string id)
    {
        return Paths.TryGetValue(id, out string data) ? data : Paths["rect"];
    }

    private static readonly Dictionary<string, Geometry> Cache = new Dictionary<string, Geometry>();

    public static Geometry Get(string id)
    {
        if (Cache.TryGetValue(id, out Geometry cached)) return cached;
        var geometry = Geometry.Parse(PathData(id));
        geometry.Freeze();
        Cache[id] = geometry;
        return geometry;
    }

    /// <summary>Готовый Path для панели инструментов.</summary>
    public static System.Windows.Shapes.Path CreatePath(string id, Brush stroke, double thickness = 1.5)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Get(id),
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = null,
            Width = 22,
            Height = 22,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = false,
        };
        return path;
    }
}