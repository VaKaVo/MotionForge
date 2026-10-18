using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MotionForge.Model;

public enum ElementKind { Shape, Text, Group, Image, Instance }

/// <summary>Базовый элемент сцены: трансформация + отрисовка собственным типом.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ShapeElement), "shape")]
[JsonDerivedType(typeof(TextElement), "text")]
[JsonDerivedType(typeof(GroupElement), "group")]
[JsonDerivedType(typeof(ImageElement), "image")]
[JsonDerivedType(typeof(InstanceElement), "instance")]
public abstract class Element
{
    public string Id { get; set; } = Ids.New("el");
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Rotation { get; set; }
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public double Alpha { get; set; } = 1;
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }

    [JsonIgnore] public abstract ElementKind Kind { get; }
    [JsonIgnore] public abstract string TypeName { get; }
    [JsonIgnore] public Mat LocalMatrix => Mat.FromTransform(X, Y, Rotation, ScaleX, ScaleY);

    public abstract Element Clone();

    /// <summary>Переносит общие свойства текущего элемента в копию (используется в Clone каждого типа).</summary>
    protected T CopyInto<T>(T target) where T : Element
    {
        target.Id = Id;
        target.Name = Name;
        target.X = X; target.Y = Y; target.Rotation = Rotation;
        target.ScaleX = ScaleX; target.ScaleY = ScaleY; target.Alpha = Alpha;
        target.Visible = Visible; target.Locked = Locked;
        return target;
    }
}

public class ShapeElement : Element
{
    public List<Vec> Points { get; set; } = new List<Vec>();
    public bool Closed { get; set; }
    public string Fill { get; set; } = "#000000";
    public string Stroke { get; set; } = "#141826";
    public double StrokeWidth { get; set; } = 4;
    public bool Smooth { get; set; } = true;
    public bool Filled { get; set; } = true;
    public bool Stroked { get; set; } = true;

    public override ElementKind Kind => ElementKind.Shape;
    public override string TypeName => "Фигура";

    public override Element Clone() => CopyInto(new ShapeElement
    {
        Points = new List<Vec>(Points),
        Closed = Closed, Fill = Fill, Stroke = Stroke, StrokeWidth = StrokeWidth,
        Smooth = Smooth, Filled = Filled, Stroked = Stroked,
    });

    /// <summary>Ширина/высота фигуры в её собственных координатах.</summary>
    public RectD LocalBox() => RectD.FromPoints(Points, Stroked ? StrokeWidth / 2 : 0);
}

public class TextElement : Element
{
    public string Text { get; set; } = "Текст";
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 72;
    public bool Bold { get; set; } = true;
    public bool Italic { get; set; }
    public string Color { get; set; } = "#141826";
    /// <summary>left | center | right</summary>
    public string Align { get; set; } = "left";
    public double LineHeight { get; set; } = 1.2;

    public override ElementKind Kind => ElementKind.Text;
    public override string TypeName => "Текст";

    public override Element Clone() => CopyInto(new TextElement
    {
        Text = Text, FontFamily = FontFamily, FontSize = FontSize, Bold = Bold, Italic = Italic,
        Color = Color, Align = Align, LineHeight = LineHeight,
    });
}

public class GroupElement : Element
{
    public List<Element> Children { get; set; } = new List<Element>();

    public override ElementKind Kind => ElementKind.Group;
    public override string TypeName => "Группа";

    public override Element Clone()
    {
        var g = CopyInto(new GroupElement());
        foreach (var c in Children) g.Children.Add(c.Clone());
        return g;
    }
}

public class ImageElement : Element
{
    /// <summary>PNG/JPEG в base64 — проект остаётся одним самостоятельным файлом.</summary>
    public string Source { get; set; } = "";
    public string FileName { get; set; } = "";
    public double PixelWidth { get; set; } = 100;
    public double PixelHeight { get; set; } = 100;

    public override ElementKind Kind => ElementKind.Image;
    public override string TypeName => "Изображение";

    public override Element Clone() => CopyInto(new ImageElement
    {
        Source = Source, FileName = FileName, PixelWidth = PixelWidth, PixelHeight = PixelHeight,
    });
}

public class InstanceElement : Element
{
    public string SymbolId { get; set; } = "";
    public string SymbolName { get; set; } = "";
    public LoopMode Loop { get; set; } = LoopMode.Loop;
    public int FirstFrame { get; set; }

    public override ElementKind Kind => ElementKind.Instance;
    public override string TypeName => "Символ";

    public override Element Clone() => CopyInto(new InstanceElement
    {
        SymbolId = SymbolId, SymbolName = SymbolName, Loop = Loop, FirstFrame = FirstFrame,
    });
}