using System.Windows;
using System.Windows.Media;
using MotionForge.Model;

namespace MotionForge.Services;

/// <summary>Точное измерение текста средствами WPF — используется в интерфейсе.</summary>
public class WpfTextMeasurer : ITextMeasurer
{
    private static readonly Brush Probe = Brushes.Black;

    public void Measure(TextElement t, out double width, out double height)
    {
        try
        {
            var formatted = Renderer.BuildText(t, 1.0, Probe);
            width = formatted.Width;
            height = formatted.Height;
            if (double.IsNaN(width) || width <= 0) width = t.FontSize * 0.6;
            if (double.IsNaN(height) || height <= 0) height = t.FontSize * t.LineHeight;
        }
        catch
        {
            new ApproxTextMeasurer().Measure(t, out width, out height);
        }
    }
}