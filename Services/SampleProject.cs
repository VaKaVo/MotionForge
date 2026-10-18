using System;
using System.Collections.Generic;
using MotionForge.Model;

namespace MotionForge.Services;

/// <summary>Демонстрационный проект: классические твины, символ с анимацией и текст.</summary>
public static class SampleProject
{
    private static bool LocalizeNames = true;

    /// <summary>Имя объекта демо-проекта: локализуется вместе с интерфейсом
    /// (автотесты вызывают Create(false) и сверяются с исходными именами).</summary>
    private static string N(string source) => LocalizeNames ? L10n.T(source) : source;

    /// <summary>Демонстрационный проект: классические твины, символ с анимацией и текст.</summary>
    public static Project Create(bool localizeNames = true)
    {
        LocalizeNames = localizeNames;

        var project = new Project { Name = N("Демо: отскок мяча") };
        project.Stage = new StageInfo { Width = 960, Height = 600, Background = "#101828", Fps = 24 };
        project.Timeline.Name = N("Сцена 1");

        var symbolId = AddStarSymbol(project);

        // Порядок слоёв: сверху вниз.
        project.Timeline.Layers.Add(TextLayer());
        project.Timeline.Layers.Add(StarLayer(symbolId));
        project.Timeline.Layers.Add(BallLayer());
        project.Timeline.Layers.Add(BackLayer());

        Store.NormalizeTimeline(project.Timeline);
        return project;
    }

    private static Layer TextLayer()
    {
        var layer = new Layer { Name = N("Заголовок"), Color = "#4A90D9" };
        var keyframe = new Keyframe { Index = 0, Duration = 48 };
        keyframe.Elements.Add(new TextElement
        {
            Name = N("Заголовок"),
            Text = "MotionForge",
            X = 480,
            Y = 70,
            FontSize = 72,
            Bold = true,
            Color = "#E6E9F2",
            Align = "center",
        });
        keyframe.Elements.Add(new TextElement
        {
            Name = N("Подзаголовок"),
            Text = N("классический твин · символы · библиотека"),
            X = 480,
            Y = 160,
            FontSize = 24,
            Bold = false,
            Color = "#98A0B8",
            Align = "center",
        });
        layer.Frames.Add(keyframe);
        return layer;
    }

    private static Layer StarLayer(string symbolId)
    {
        var layer = new Layer { Name = N("Звезда (символ)"), Color = "#FFB545" };
        var keyframe = new Keyframe { Index = 0, Duration = 48 };
        keyframe.Elements.Add(new InstanceElement
        {
            Name = N("Звезда"),
            SymbolId = symbolId,
            SymbolName = N("Звезда"),
            X = 855,
            Y = 320,
            ScaleX = 0.85,
            ScaleY = 0.85,
        });
        layer.Frames.Add(keyframe);
        return layer;
    }

    private static Layer BallLayer()
    {
        var layer = new Layer { Name = N("Мяч"), Color = "#35D39A" };
        var ball = MakeBall();
        ball.X = 150;
        ball.Y = 430;
        ball.ScaleX = 1.15;
        ball.ScaleY = 0.85;

        var up = ball.Clone();
        up.X = 480;
        up.Y = 170;
        up.ScaleX = 0.92;
        up.ScaleY = 1.08;

        var down = ball.Clone();
        down.X = 810;
        down.Y = 430;

        layer.Frames.Add(new Keyframe { Index = 0, Duration = 12, Elements = { ball } });
        layer.Frames.Add(new Keyframe
        {
            Index = 12,
            Duration = 12,
            Elements = { up },
            Tween = new Tween { Type = "classic", Ease = 0.45 },
        });
        layer.Frames.Add(new Keyframe
        {
            Index = 24,
            Duration = 24,
            Elements = { down },
            Tween = new Tween { Type = "classic", Ease = -0.45 },
        });
        return layer;
    }

    private static Layer BackLayer()
    {
        var layer = new Layer { Name = N("Фон"), Color = "#B8A33C" };
        var keyframe = new Keyframe { Index = 0, Duration = 48 };
        keyframe.Elements.Add(new ShapeElement
        {
            Name = N("Земля"),
            Points = new List<Vec>
            {
                new Vec(-480, 0), new Vec(480, 0), new Vec(480, 130), new Vec(-480, 130),
            },
            Closed = true,
            Smooth = false,
            Filled = true,
            Stroked = false,
            Fill = "#1B2440",
            X = 480,
            Y = 535,
        });
        keyframe.Elements.Add(new ShapeElement
        {
            Name = N("Пятно тени"),
            Points = Geo.EllipsePoints(120, 18, 40),
            Closed = true,
            Fill = "#0B0F1C",
            Stroked = false,
            X = 480,
            Y = 470,
        });
        layer.Frames.Add(keyframe);
        return layer;
    }

    private static ShapeElement MakeBall()
    {
        return new ShapeElement
        {
            Name = N("Мяч"),
            Points = Geo.EllipsePoints(48, 48, 48),
            Closed = true,
            Fill = "#FFB545",
            Stroke = "#141826",
            StrokeWidth = 6,
            Filled = true,
            Stroked = true,
            Smooth = true,
        };
    }

    private static string AddStarSymbol(Project project)
    {
        var star = new ShapeElement
        {
            Name = N("Звезда"),
            Points = StarPoints(46, 20, 5),
            Closed = true,
            Fill = "#FFD166",
            Stroke = "#141826",
            StrokeWidth = 5,
            Filled = true,
            Stroked = true,
            Smooth = false,
        };
        var rotated = star.Clone();
        rotated.Rotation = 360;

        var keyframeA = new Keyframe { Index = 0, Duration = 24, Elements = { star } };
        var keyframeB = new Keyframe { Index = 24, Duration = 1, Elements = { rotated }, Tween = new Tween { Type = "classic" } };
        var layer = new Layer { Name = N("Слой 1"), Color = "#FFB545", Frames = { keyframeA, keyframeB } };

        var symbol = new Symbol
        {
            Name = N("Звезда"),
            Kind = SymbolKind.MovieClip,
            Bounds = new RectD(-46, -46, 92, 92),
        };
        symbol.Timeline = new Scene { Name = N("Звезда"), Layers = { layer } };
        project.Library[symbol.Id] = symbol;
        return symbol.Id;
    }

    private static List<Vec> StarPoints(double outer, double inner, int spikes)
    {
        var points = new List<Vec>();
        for (int i = 0; i < spikes * 2; i++)
        {
            double radius = i % 2 == 0 ? outer : inner;
            double angle = -Math.PI / 2 + i * Math.PI / spikes;
            points.Add(new Vec(Math.Cos(angle) * radius, Math.Sin(angle) * radius));
        }
        return points;
    }
}