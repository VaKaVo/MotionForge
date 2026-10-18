using System;
using System.Collections.Generic;
using System.Globalization;

namespace MotionForge.Model;

/// <summary>Работа с цветом в формате #RRGGBB / #AARRGGBB без зависимости от WPF.</summary>
public static class ColorUtil
{
    public static string Normalize(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "#000000";
        hex = hex.Trim();
        if (!hex.StartsWith("#")) hex = "#" + hex;
        if (hex.Length == 4)
            return "#" + new string(hex[1], 2) + new string(hex[2], 2) + new string(hex[3], 2);
        if (hex.Length == 7) return hex.ToUpperInvariant();
        if (hex.Length == 9) return hex.ToUpperInvariant();
        return "#000000";
    }

    public static void Parse(string hex, out byte a, out byte r, out byte g, out byte b)
    {
        hex = Normalize(hex);
        a = 255;
        if (hex.Length == 9)
        {
            a = byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            r = byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            g = byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            b = byte.Parse(hex.Substring(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return;
        }
        r = byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        g = byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        b = byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    public static string Argb(byte a, byte r, byte g, byte b) => $"#{a:X2}{r:X2}{g:X2}{b:X2}";

    /// <summary>Опорная смена прозрачности цвета (сохраняя исходную альфу).</summary>
    public static string WithAlpha(string hex, double alpha)
    {
        Parse(hex, out byte a, out byte r, out byte g, out byte b);
        byte na = (byte)Math.Max(0, Math.Min(255, Math.Round(a * alpha)));
        return Argb(na, r, g, b);
    }

    public static string Mix(string from, string to, double t)
    {
        t = Math.Max(0, Math.Min(1, t));
        Parse(from, out byte a1, out byte r1, out byte g1, out byte b1);
        Parse(to, out byte a2, out byte r2, out byte g2, out byte b2);
        byte a = (byte)Math.Round(a1 + (a2 - a1) * t);
        byte r = (byte)Math.Round(r1 + (r2 - r1) * t);
        byte g = (byte)Math.Round(g1 + (g2 - g1) * t);
        byte b = (byte)Math.Round(b1 + (b2 - b1) * t);
        return Argb(a, r, g, b);
    }

    public static string Lighten(string hex, double amount) => Mix(hex, "#FFFFFF", amount);
    public static string Darken(string hex, double amount) => Mix(hex, "#000000", amount);

    /// <summary>Палитра образцов: та же логика, что в Animate — чёрный, серые, базовые и «web-safe» цвета.</summary>
    public static IEnumerable<string> DefaultPalette()
    {
        return new[]
        {
            "#000000", "#333333", "#666666", "#999999", "#CCCCCC", "#FFFFFF",
            "#7F0000", "#B21F1F", "#E04B4B", "#FF8A8A", "#7F3F00", "#C76A1E",
            "#ED9A3C", "#FFCF8A", "#7F6A00", "#B8A33C", "#EDE04B", "#FFF9A0",
            "#1F5C1F", "#3F8A3F", "#5CB85C", "#9BD89B", "#0F5A5A", "#3EAFB0",
            "#6FD6D6", "#B8EFEF", "#1F3F7F", "#2A62AC", "#4A90D9", "#9CC4ED",
            "#3B1F7F", "#6A45C8", "#9B6BD6", "#C9AEED", "#7F1F5C", "#C86BAA",
            "#ED9ED2", "#FFD6EE", "#5A3A1F", "#8D6A45", "#C4A275", "#E8D5BB",
        };
    }
}