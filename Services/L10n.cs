using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace MotionForge.Services;

/// <summary>
/// Локализация интерфейса. Строкой-ключом служит русский текст из кода,
/// переводы лежат в папке Localization рядом с исполняемым файлом:
/// Localization/&lt;код&gt;.json = { "Файл": "File", ... }.
/// Русский встроен (файл не нужен), при отсутствии перевода берётся английский,
/// затем исходная строка. Новый язык добавляется простым копированием файла.
/// </summary>
public static class L10n
{
    public const string Fallback = "en";

    /// <summary>Служебный ключ в файле языка: как называется язык в списке настроек.</summary>
    public const string NameKey = "@name";

    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new();
    private static readonly Dictionary<string, string> Names = new();
    private static readonly Dictionary<string, string> NativeNames = new()
    {
        { "ru", "Русский" },
        { "en", "English" },
        { "uk", "Українська" },
        { "be", "Беларуская" },
        { "de", "Deutsch" },
        { "fr", "Français" },
        { "es", "Español" },
        { "it", "Italiano" },
        { "pt", "Português" },
        { "pl", "Polski" },
        { "cs", "Čeština" },
        { "tr", "Türkçe" },
        { "zh", "中文" },
        { "ja", "日本語" },
        { "ko", "한국어" },
        { "ar", "العربية" },
    };

    /// <summary>Собранные строки — для генерации шаблона нового языка (см. --lang-template).</summary>
    public static readonly HashSet<string> Requested = new();

    /// <summary>Запуск с ключом --lang-template: выгрузить собранные строки и выйти.</summary>
    public static bool TemplateRequested;

    public static string Current { get; private set; } = "ru";
    public static event Action Changed;

    public static string LocalizationFolder => Path.Combine(AppContext.BaseDirectory, "Localization");

    /// <summary>Доступные языки: русский + все найденные файлы Localization/*.json.
    /// Название берётся из ключа "@name" внутри файла, затем из встроенного списка.</summary>
    public static List<(string Code, string Name)> Languages()
    {
        var list = new List<(string, string)> { ("ru", NativeNames["ru"]) };
        try
        {
            if (Directory.Exists(LocalizationFolder))
            {
                foreach (string file in Directory.GetFiles(LocalizationFolder, "*.json"))
                {
                    string code = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    if (code == "ru") continue;
                    string name = PeekName(code, file);
                    if (string.IsNullOrWhiteSpace(name)) name = code;
                    list.Add((code, name));
                }
            }
        }
        catch { }
        return list.OrderBy(l => l.Item1 == "ru" ? 0 : 1).ThenBy(l => l.Item2, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>Название языка: сначала "@name" из файла, затем встроенный список, иначе код.</summary>
    private static string PeekName(string code, string file)
    {
        if (Names.TryGetValue(code, out string cached)) return cached;
        string name = null;
        try
        {
            using (var doc = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8)))
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty(NameKey, out var value) && value.ValueKind == JsonValueKind.String)
                    name = value.GetString();
            }
        }
        catch { /* битый файл — покажем хотя бы код */ }
        if (string.IsNullOrWhiteSpace(name) && NativeNames.TryGetValue(code, out string known)) name = known;
        Names[code] = name ?? code;
        return Names[code];
    }

    public static void SetLanguage(string code)
    {
        code = string.IsNullOrWhiteSpace(code) ? "ru" : code.Trim().ToLowerInvariant();
        // Английский — резерв для всех остальных языков: держим его загруженным всегда.
        Load(Fallback);
        Load(code);
        if (code == Current) return;
        Current = code;
        Changed?.Invoke();
    }

    /// <summary>Загружена ли таблица перевода для этого кода (для диагностики и настроек).</summary>
    public static bool IsLoaded(string code) => Tables.ContainsKey(code);

    /// <summary>Перевод строки. Ключ — русский текст; отсутствующий перевод возвращает исходный текст.</summary>
    public static string T(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;
        Requested.Add(source);
        if (Current == "ru") return source;
        if (Tables.TryGetValue(Current, out var table) && table.TryGetValue(source, out string translated) && !string.IsNullOrEmpty(translated))
            return translated;
        if (Tables.TryGetValue(Fallback, out var fallback) && fallback.TryGetValue(source, out string en) && !string.IsNullOrEmpty(en))
            return en;
        return source;
    }

    /// <summary>Записать собранные строки как шаблон языка (code=ru — все ключи с пустыми значениями).</summary>
    public static string DumpTemplate(string code)
    {
        var table = new SortedDictionary<string, string>(StringComparer.CurrentCulture);
        table[NameKey] = "";                      // название языка для списка в настройках
        foreach (string key in Requested) table[key] = "";
        string path = Path.Combine(Path.GetTempPath(), "motionforge-lang-" + code + ".json");
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(path, JsonSerializer.Serialize(table, options), new UTF8Encoding(false));
        return path;
    }

    private static void Load(string code)
    {
        if (code == "ru" || Tables.ContainsKey(code)) return;
        string path = Path.Combine(LocalizationFolder, code + ".json");
        try
        {
            if (File.Exists(path))
            {
                var table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8));
                if (table != null) Tables[code] = table;
            }
        }
        catch { /* битый файл перевода игнорируем: останутся исходные строки */ }
    }
}
