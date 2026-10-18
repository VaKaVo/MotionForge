using System;
using System.IO;
using System.Text;

namespace MotionForge.Model;

/// <summary>
/// Пользовательские настройки приложения. Хранятся в JSON:
/// %APPDATA%\MotionForge\settings.json (рядом с автосохранением).
/// </summary>
public class AppSettings
{
    /* --- общие --- */
    public string Language = "ru";            // код языка: ru, en, uk, de, fr, es, it, pt, pl, tr, zh, ja
    public bool ShowStatusHints = true;       // подсказки в строке состояния
    public bool ConfirmOnExit = true;         // спрашивать про несохранённые изменения

    /* --- сцена --- */
    public bool GridOn = false;
    public double GridStep = 40;              // шаг сетки в единицах сцены
    public bool SnapToGrid = false;           // привязывать новые фигуры к сетке
    public bool OutlineAll = false;           // показывать контуры всех объектов

    /* --- луковая кожа --- */
    public bool OnionOn = false;
    public int OnionBefore = 1;
    public int OnionAfter = 1;

    /* --- рисование --- */
    public double StrokeWidth = 1;
    public double BrushSize = 16;
    public bool SmoothStrokes = false;        // сглаживание линий по умолчанию

    /* --- планшет --- */
    public bool TabletEnabled = true;         // обрабатывать перо отдельно от мыши
    public bool TabletPressure = true;        // сила нажатия → толщина линии
    public bool TabletIgnoreTouch = true;     // игнорировать касания пальцем (защита от ладони)

    /* --- автосохранение --- */
    public int AutosaveMinutes = 1;           // 0 — выключено

    /// <summary>Текущие настройки приложения (загружаются один раз при запуске).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public static AppSettings Current = new AppSettings();

    [System.Text.Json.Serialization.JsonIgnore]
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MotionForge");

    [System.Text.Json.Serialization.JsonIgnore]
    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = Json.LoadObject<AppSettings>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (loaded != null) return loaded.Normalized();
            }
        }
        catch { /* повреждённый файл настроек не должен мешать запуску */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, Json.SaveObject(this), new UTF8Encoding(false));
        }
        catch { /* настройки — не повод падать */ }
    }

    /// <summary>Приводит значения к допустимым (на случай ручной правки файла).</summary>
    public AppSettings Normalized()
    {
        if (string.IsNullOrWhiteSpace(Language)) Language = "ru";
        GridStep = Math.Max(2, Math.Min(400, GridStep));
        OnionBefore = Math.Max(0, Math.Min(5, OnionBefore));
        OnionAfter = Math.Max(0, Math.Min(5, OnionAfter));
        StrokeWidth = Math.Max(0.1, Math.Min(200, StrokeWidth));
        BrushSize = Math.Max(1, Math.Min(400, BrushSize));
        AutosaveMinutes = Math.Max(0, Math.Min(60, AutosaveMinutes));
        return this;
    }

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        return copy;
    }
}
