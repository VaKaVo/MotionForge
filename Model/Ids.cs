using System;
using System.Globalization;

namespace MotionForge.Model;

public static class Ids
{
    private static int _counter;

    public static string New(string prefix)
    {
        _counter++;
        return prefix + "_" + DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture)
               + "_" + _counter.ToString(CultureInfo.InvariantCulture);
    }
}

public static class Num
{
    public static string F(double v, int decimals = 1)
        => v.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture);

    public static bool TryParse(string text, out double value)
    {
        text = (text ?? "").Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}