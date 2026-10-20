using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MotionForge.Model;

namespace MotionForge.Services;

/// <summary>
/// Описание плагина из файла plugin.json.
/// Плагин — это внешняя программа (обычно скрипт Python), которая получает
/// файл проекта, меняет его и записывает результат в другой файл.
/// </summary>
public class PluginInfo
{
    public string Id = "";
    public string Name = "";
    /// <summary>Название по-английски (необязательно): показывается, когда интерфейс не русский.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("name_en")]
    public string NameEn = "";
    public string Version = "1.0";
    public string Author = "";
    public string Description = "";
    /// <summary>Тип плагина: python (скрипт) или command (любая программа).</summary>
    public string Type = "python";
    /// <summary>Команда/скрипт для запуска (относительно папки плагина).</summary>
    public string Command = "";
    /// <summary>Дополнительные аргументы командной строки.</summary>
    public string[] Arguments = new string[0];
    /// <summary>Ограничение времени работы, секунды.</summary>
    public int Timeout = 60;

    [System.Text.Json.Serialization.JsonIgnore] public string Folder = "";
    [System.Text.Json.Serialization.JsonIgnore] public string Error = "";

    public string ManifestPath => Path.Combine(Folder, "plugin.json");
    public string CommandPath => string.IsNullOrEmpty(Command) ? "" : Path.Combine(Folder, Command);
    public bool Ready => Error.Length == 0;

    /// <summary>Пункт меню: русское название, а для других языков — английское, если оно задано.</summary>
    public string Title
    {
        get
        {
            string name = string.IsNullOrEmpty(Name) ? Id : Name;
            bool russian = string.Equals(L10n.Current, "ru", StringComparison.OrdinalIgnoreCase);
            return !russian && !string.IsNullOrEmpty(NameEn) ? NameEn : name;
        }
    }
}

/// <summary>
/// Плагины: список папок рядом с программой (Plugins\&lt;имя&gt;\plugin.json).
/// Протокол: программа запускается с аргументами
/// <c>--input in.json --output out.json --frame N</c>, читает in.json (проект)
/// и пишет изменённый проект в out.json. Если out.json не создан — изменений нет.
/// </summary>
public static class Plugins
{
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Plugins");

    /// <summary>Имя внешней программы Python (берётся из настроек).</summary>
    public static string PythonCommand = "python";

    public static List<PluginInfo> Cache = new List<PluginInfo>();

    /// <summary>Читает папку плагинов. Ошибки разбора попадают в поле Error.</summary>
    public static List<PluginInfo> Load()
    {
        var result = new List<PluginInfo>();
        Cache = result;
        try
        {
            if (!Directory.Exists(Folder))
            {
                Directory.CreateDirectory(Folder);
                return result;
            }

            foreach (string directory in Directory.GetDirectories(Folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                // Папки, начинающиеся с «_», — шаблоны и примеры: они не загружаются,
                // чтобы их можно было копировать и переименовывать.
                string name = Path.GetFileName(directory);
                if (name.StartsWith("_") || name.StartsWith(".")) continue;

                string manifest = Path.Combine(directory, "plugin.json");
                if (!File.Exists(manifest)) continue;

                var info = new PluginInfo { Folder = directory };
                try
                {
                    var parsed = Json.LoadObject<PluginInfo>(File.ReadAllText(manifest, Encoding.UTF8));
                    if (parsed != null)
                    {
                        info.Id = parsed.Id;
                        info.Name = parsed.Name;
                        info.NameEn = parsed.NameEn;
                        info.Version = string.IsNullOrEmpty(parsed.Version) ? "1.0" : parsed.Version;
                        info.Author = parsed.Author;
                        info.Description = parsed.Description;
                        info.Type = string.IsNullOrEmpty(parsed.Type) ? "python" : parsed.Type;
                        info.Command = parsed.Command;
                        info.Arguments = parsed.Arguments ?? new string[0];
                        info.Timeout = parsed.Timeout <= 0 ? 60 : parsed.Timeout;
                    }
                }
                catch (Exception ex)
                {
                    info.Error = "неверный plugin.json: " + ex.Message;
                }

                if (info.Id.Length == 0) info.Id = Path.GetFileName(directory);
                if (info.Error.Length == 0)
                {
                    if (info.Command.Length == 0) info.Error = "в plugin.json не указано поле command";
                    else if (!File.Exists(info.CommandPath)) info.Error = "файл не найден: " + info.Command;
                }
                result.Add(info);
            }
        }
        catch (Exception ex)
        {
            var broken = new PluginInfo { Id = "folder", Name = "Папка плагинов", Folder = Folder };
            broken.Error = ex.Message;
            result.Add(broken);
        }
        return result;
    }

    /// <summary>
    /// Запускает плагин над проектом. Возвращает изменённый проект или null.
    /// <paramref name="log"/> — вывод плагина и диагностика.
    /// </summary>
    public static Project Run(PluginInfo plugin, Project project, int frame, out string log)
    {
        log = "";
        if (plugin == null) { log = "Плагин не выбран."; return null; }
        if (!plugin.Ready) { log = plugin.Error; return null; }
        if (project == null) { log = "Нет открытого проекта."; return null; }

        string root = Path.Combine(Path.GetTempPath(), "MotionForge", "plugins");
        try { Directory.CreateDirectory(root); }
        catch (Exception ex) { log = "Не удалось создать временную папку: " + ex.Message; return null; }

        string safeId = Sanitize(plugin.Id);
        string input = Path.Combine(root, safeId + "-in.json");
        string output = Path.Combine(root, safeId + "-out.json");
        try
        {
            File.WriteAllText(input, Json.Save(project), new UTF8Encoding(false));
            if (File.Exists(output)) File.Delete(output);
        }
        catch (Exception ex) { log = "Не удалось записать входной файл: " + ex.Message; return null; }

        var start = new ProcessStartInfo
        {
            FileName = CommandFor(plugin),
            WorkingDirectory = plugin.Folder,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (plugin.Command.Length > 0) start.ArgumentList.Add(plugin.CommandPath);
        foreach (string argument in plugin.Arguments) start.ArgumentList.Add(argument);
        start.ArgumentList.Add("--input");
        start.ArgumentList.Add(input);
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add(output);
        start.ArgumentList.Add("--frame");
        start.ArgumentList.Add(frame.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var outputText = new StringBuilder();
        var errorText = new StringBuilder();
        // Python и большинство консольных программ под Windows пишут в кодировке
        // системы; принудительно просим UTF-8, чтобы русский текст не превращался в «кракозябры».
        start.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        start.EnvironmentVariables["PYTHONUTF8"] = "1";
        try
        {
            using (var process = new Process { StartInfo = start })
            {
                process.OutputDataReceived += (_, e) => { if (e.Data != null) outputText.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorText.AppendLine(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(Math.Max(2, plugin.Timeout) * 1000))
                {
                    try { process.Kill(true); } catch { /* уже завершился */ }
                    log = "Плагин «" + plugin.Title + "» не успел завершиться за " + plugin.Timeout + " с и был остановлен.";
                    return null;
                }
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    log = "Плагин «" + plugin.Title + "» завершился с ошибкой (код " + process.ExitCode + ")." + Environment.NewLine;
                    log += Join(outputText, errorText);
                    return null;
                }
            }
        }
        catch (Exception ex)
        {
            log = "Не удалось запустить плагин: " + ex.Message + Environment.NewLine +
                  "Для плагинов на Python нужен установленный Python 3 (команда «" + PythonCommand + "»).";
            return null;
        }

        if (!File.Exists(output))
        {
            log = "Плагин ничего не изменил (файл результата не создан)." + Environment.NewLine + Join(outputText, errorText);
            return null;
        }

        try
        {
            string text = File.ReadAllText(output, Encoding.UTF8);
            var changed = Json.Load(text);
            if (changed == null) { log = "Плагин записал пустой файл проекта."; return null; }
            log = Join(outputText, errorText);
            return changed;
        }
        catch (Exception ex)
        {
            log = "Файл результата не удалось прочитать: " + ex.Message;
            return null;
        }
    }

    /// <summary>Имя исполняемого файла: для python-плагинов — настроенный Python.</summary>
    private static string CommandFor(PluginInfo plugin)
    {
        if (!string.Equals(plugin.Type, "python", StringComparison.OrdinalIgnoreCase)) return plugin.CommandPath;
        string python = string.IsNullOrWhiteSpace(PythonCommand) ? "python" : PythonCommand.Trim();
        return python;
    }

    private static string Join(StringBuilder output, StringBuilder error)
    {
        var text = new StringBuilder();
        if (output.Length > 0) text.Append(output.ToString().TrimEnd());
        if (error.Length > 0)
        {
            if (text.Length > 0) text.AppendLine();
            text.Append("Ошибки:");
            text.AppendLine();
            text.Append(error.ToString().TrimEnd());
        }
        return text.ToString();
    }

    private static string Sanitize(string id)
    {
        var text = new StringBuilder();
        foreach (char c in id)
            text.Append(char.IsLetterOrDigit(c) ? c : '_');
        return text.Length == 0 ? "plugin" : text.ToString();
    }

    /// <summary>Есть ли Python в системе (для подсказок в окне плагинов).</summary>
    public static bool PythonAvailable(out string version)
    {
        version = "";
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(PythonCommand) ? "python" : PythonCommand.Trim(),
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("--version");
            using (var process = Process.Start(start))
            {
                if (process == null) return false;
                string text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                if (!process.WaitForExit(5000)) { try { process.Kill(true); } catch { } return false; }
                version = text.Trim();
                return process.ExitCode == 0 || version.Length > 0;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Открывает папку плагинов в проводнике.</summary>
    public static void OpenFolder()
    {
        try
        {
            if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
            Process.Start(new ProcessStartInfo { FileName = Folder, UseShellExecute = true });
        }
        catch { /* проводник недоступен — не критично */ }
    }
}
