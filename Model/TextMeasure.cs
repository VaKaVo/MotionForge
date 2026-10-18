using System;

namespace MotionForge.Model;

/// <summary>Измерение текста. По умолчанию — быстрая аппроксимация, в WPF подставляется точная реализация.</summary>
public interface ITextMeasurer
{
    void Measure(TextElement text, out double width, out double height);
}

public class ApproxTextMeasurer : ITextMeasurer
{
    public void Measure(TextElement t, out double width, out double height)
    {
        string content = t.Text ?? "";
        string[] lines = content.Split('\n');
        double w = 0;
        foreach (string line in lines)
        {
            double lineWidth = 0;
            foreach (char c in line) lineWidth += char.IsWhiteSpace(c) ? t.FontSize * 0.3 : t.FontSize * 0.58;
            w = Math.Max(w, lineWidth);
        }
        if (w <= 0.5) w = t.FontSize * 0.6;
        height = Math.Max(1, lines.Length) * t.FontSize * t.LineHeight;
        width = w;
    }
}