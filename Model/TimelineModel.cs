using System;
using System.Collections.Generic;

namespace MotionForge.Model;

public enum SymbolKind { Graphic, MovieClip, Button }

public enum LoopMode { Loop, PlayOnce, SingleFrame }

public class StageInfo
{
    public double Width { get; set; } = 960;
    public double Height { get; set; } = 600;
    public string Background { get; set; } = "#FFFFFF";
    public int Fps { get; set; } = 24;
}

public class Tween
{
    public string Type { get; set; } = "classic";
    /// <summary>От -1 (плавный разгон) до 1 (плавное торможение).</summary>
    public double Ease { get; set; }
}

public class Keyframe
{
    public string Id { get; set; } = Ids.New("kf");
    public int Index { get; set; }
    /// <summary>Сколько кадров держится этот ключевой кадр.</summary>
    public int Duration { get; set; } = 1;
    public Tween Tween { get; set; }
    public List<Element> Elements { get; set; } = new List<Element>();

    public int EndIndex => Index + Duration - 1;
    public bool Covers(int frame) => frame >= Index && frame <= EndIndex;
    public bool IsEmpty => Elements == null || Elements.Count == 0;

    public Keyframe Clone() => new Keyframe
    {
        Id = Ids.New("kf"), Index = Index, Duration = Duration,
        Tween = Tween == null ? null : new Tween { Type = Tween.Type, Ease = Tween.Ease },
        Elements = CloneElements(Elements),
    };

    public static List<Element> CloneElements(List<Element> src)
    {
        var list = new List<Element>();
        if (src != null) foreach (var e in src) list.Add(e.Clone());
        return list;
    }
}

public class Layer
{
    public string Id { get; set; } = Ids.New("lyr");
    public string Name { get; set; } = "Слой";
    public string Color { get; set; } = "#4A90D9";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public bool Outline { get; set; }
    public List<Keyframe> Frames { get; set; } = new List<Keyframe>();
}

public class Scene
{
    public string Id { get; set; } = Ids.New("scn");
    public string Name { get; set; } = "Сцена";
    public string Background { get; set; } = "";
    public List<Layer> Layers { get; set; } = new List<Layer>();
}

public class Symbol
{
    public string Id { get; set; } = Ids.New("sym");
    public string Name { get; set; } = "Символ";
    public SymbolKind Kind { get; set; } = SymbolKind.MovieClip;
    public Scene Timeline { get; set; } = new Scene();
    /// <summary>Габарит содержимого символа (обновляется при редактировании) — нужен для рамки и попадания клика.</summary>
    public RectD Bounds { get; set; } = new RectD(-50, -50, 100, 100);
}

public class Project
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = Ids.New("prj");
    public string Name { get; set; } = "Без названия";
    public StageInfo Stage { get; set; } = new StageInfo();
    public Scene Timeline { get; set; } = new Scene();
    public Dictionary<string, Symbol> Library { get; set; } = new Dictionary<string, Symbol>();
}

public static class LayerPalette
{
    private static readonly string[] Colors =
    {
        "#4A90D9", "#5CB85C", "#9B6BD6", "#E08A3C", "#D1535B", "#C86BAA", "#3EAFB0", "#B8A33C",
    };

    public static string Pick(int index) => Colors[((index % Colors.Length) + Colors.Length) % Colors.Length];
}