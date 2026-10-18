using System.Windows.Input;

namespace MotionForge.Services;

/// <summary>
/// Разбор строк вида «Ctrl+Shift+S», «F5», «Delete» в пару «модификаторы + клавиша».
/// ВАЖНО: буквы переводятся в <see cref="Key"/> через <c>Key.A + смещение</c>, а не приведением
/// кода символа — иначе значение получается сдвинутым (Value = ASCII-код) и вся таблица
/// горячих клавиш ломается.
/// </summary>
public static class Hotkeys
{
    public static (ModifierKeys Mods, Key Key) Parse(string spec)
    {
        ModifierKeys mods = ModifierKeys.None;
        Key key = Key.None;
        if (string.IsNullOrWhiteSpace(spec)) return (mods, key);
        foreach (string raw in spec.Split('+'))
        {
            string part = raw.Trim();
            switch (part)
            {
                case "Ctrl": mods |= ModifierKeys.Control; break;
                case "Shift": mods |= ModifierKeys.Shift; break;
                case "Alt": mods |= ModifierKeys.Alt; break;
                case "Win": mods |= ModifierKeys.Windows; break;
                default: key = ParseKey(part); break;
            }
        }
        return (mods, key);
    }

    public static Key ParseKey(string name)
    {
        if (string.IsNullOrEmpty(name)) return Key.None;
        switch (name)
        {
            case "Delete": return Key.Delete;
            case "Back": return Key.Back;
            case "Space": return Key.Space;
            case "Home": return Key.Home;
            case "End": return Key.End;
            case "Up": return Key.Up;
            case "Down": return Key.Down;
            case "Left": return Key.Left;
            case "Right": return Key.Right;
            case "Enter": return Key.Enter;
            case "Tab": return Key.Tab;
            case "Escape": return Key.Escape;
            case "Insert": return Key.Insert;
            case "PageUp": return Key.PageUp;
            case "PageDown": return Key.PageDown;
            case "OemPlus": return Key.OemPlus;
            case "OemMinus": return Key.OemMinus;
            case "OemComma": return Key.OemComma;
            case "OemPeriod": return Key.OemPeriod;
            case "OemQuotes": return Key.OemQuotes;
        }
        if (name.Length == 1)
        {
            char c = name[0];
            if (c >= 'A' && c <= 'Z') return Key.A + (c - 'A');
            if (c >= 'a' && c <= 'z') return Key.A + (c - 'a');
            if (c >= '0' && c <= '9') return Key.D0 + (c - '0');
        }
        if (name.Length == 2 && name[0] == 'F' && char.IsDigit(name[1]))
        {
            int n = name[1] - '1';
            if (n < 0 || n > 23) return Key.None;
            return Key.F1 + n;
        }
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
            return Key.D0 + (name[1] - '0');
        return Key.None;
    }

    /// <summary>Устойчивый идентификатор сочетания для таблицы горячих клавиш.</summary>
    public static string Id(ModifierKeys mods, Key key)
    {
        if (key == Key.None) return null;
        return (int)mods + ":" + (int)key;
    }

    public static string IdOf(string spec)
    {
        var (mods, key) = Parse(spec);
        return Id(mods, key);
    }

    /// <summary>Человекочитаемое имя сочетания (для справки и подсказок).</summary>
    public static string Describe(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return "";
        var (mods, key) = Parse(spec);
        if (key == Key.None) return spec;
        string text = "";
        if ((mods & ModifierKeys.Control) != 0) text += "Ctrl+";
        if ((mods & ModifierKeys.Shift) != 0) text += "Shift+";
        if ((mods & ModifierKeys.Alt) != 0) text += "Alt+";
        return text + KeyName(key);
    }

    private static string KeyName(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return ((char)('A' + (key - Key.A))).ToString();
        if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key >= Key.F1 && key <= Key.F24) return "F" + (1 + (key - Key.F1));
        return key.ToString();
    }
}
