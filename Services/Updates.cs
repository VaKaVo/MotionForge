using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MotionForge.Model;

namespace MotionForge.Services;

/// <summary>Сведения о доступной версии на GitHub.</summary>
public class UpdateInfo
{
    public string Version;      // 1.1.0
    public string Tag;          // v1.1.0
    public string Notes;        // описание релиза (Markdown)
    public string PageUrl;      // страница релиза
    public string AssetUrl;     // zip для Windows x64
    public string AssetName;
    public long AssetSize;
}

/// <summary>
/// Проверка и установка обновлений через GitHub Releases.
/// Без сторонних библиотек: HttpClient, System.Text.Json, ZipFile.
/// </summary>
public static class Updates
{
    public const string Repository = "VaKaVo/MotionForge";

    private const string LatestReleaseUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";

    private static readonly Lazy<HttpClient> Client = new Lazy<HttpClient>(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MotionForge/" + VersionText);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    });

    /// <summary>Версия запущенного приложения (из сборки).</summary>
    public static Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    /// <summary>Версия без хвоста «.0»: «1.1.0».</summary>
    public static string VersionText => CurrentVersion.ToString(3);

    /// <summary>Папка для скачанных обновлений.</summary>
    public static string TempFolder => Path.Combine(Path.GetTempPath(), "MotionForge", "update");

    /// <summary>Разбирает тег вида «v1.2.3» или «1.2.3-beta» в версию.</summary>
    public static Version ParseVersion(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        tag = tag.Trim();
        if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tag = tag.Substring(1);
        int dash = tag.IndexOf('-');
        if (dash > 0) tag = tag.Substring(0, dash);
        if (!Version.TryParse(tag, out Version parsed)) return null;
        // Сравниваем только major.minor.build, чтобы «1.0.1.0» и «1.0.1» были равны.
        return new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
    }

    /// <summary>Новее ли тег, чем установленная версия.</summary>
    public static bool IsNewer(string tag, Version current)
    {
        Version parsed = ParseVersion(tag);
        if (parsed == null) return false;
        Version installed = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        return parsed > installed;
    }

    /// <summary>
    /// Спрашивает у GitHub последний релиз. Возвращает null, если версия актуальна
    /// или проверить не удалось (нет сети, лимит запросов и т.п.).
    /// </summary>
    public static async Task<UpdateInfo> CheckAsync()
    {
        using HttpResponseMessage response = await Client.Value
            .GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseContentRead)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        ReleaseDto release = Json.LoadObject<ReleaseDto>(json);
        if (release == null || !IsNewer(release.TagName, CurrentVersion)) return null;

        ReleaseAssetDto asset = release.Assets?.FirstOrDefault(a =>
            a.Name != null && a.Name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase));

        return new UpdateInfo
        {
            Version = ParseVersion(release.TagName).ToString(3),
            Tag = release.TagName,
            Notes = release.Body ?? "",
            PageUrl = release.HtmlUrl,
            AssetUrl = asset?.BrowserDownloadUrl,
            AssetName = asset?.Name,
            AssetSize = asset?.Size ?? 0,
        };
    }

    /// <summary>Скачивает zip обновления и возвращает путь к нему (прогресс 0..1).</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double> progress = null,
        CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(info?.AssetUrl))
            throw new InvalidOperationException(L10n.T("В этом релизе нет сборки для Windows — скачайте её со страницы релиза."));

        using HttpResponseMessage response = await Client.Value
            .GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellation)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? info.AssetSize;
        string folder = Path.Combine(TempFolder, info.Version);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, string.IsNullOrWhiteSpace(info.AssetName) ? "MotionForge.zip" : info.AssetName);

        using (Stream source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
        using (FileStream target = File.Create(path))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false);
                done += read;
                if (total > 0) progress?.Report(Math.Min(1, (double)done / total));
            }
        }

        return path;
    }

    /// <summary>
    /// Готовит установку: распаковывает zip во временную папку и запускает скрипт,
    /// который дождётся закрытия приложения, заменит файлы и снова его запустит.
    /// </summary>
    public static bool TryInstall(string zipPath, out string error)
    {
        error = null;
        try
        {
            string appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string exe = Path.Combine(appDir, "MotionForge.exe");
            if (!File.Exists(exe))
            {
                error = L10n.T("Приложение запущено не из папки сборки — обновите его вручную.");
                return false;
            }

            // Права на запись проверяем заранее, чтобы не оставить папку в половинчатом состоянии.
            try
            {
                string probe = Path.Combine(appDir, ".motionforge-update-probe");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
            }
            catch
            {
                error = L10n.T("Папка программы недоступна для записи — скачайте обновление вручную.");
                return false;
            }

            string stage = Path.Combine(TempFolder, "stage");
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            ZipFile.ExtractToDirectory(zipPath, stage);

            string script = Path.Combine(TempFolder, "apply-update.ps1");
            // UTF-8 с BOM: PowerShell 5.1 иначе неверно читает кириллицу в путях и комментариях.
            File.WriteAllText(script, ApplyScript, new UTF8Encoding(true));

            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = TempFolder,
            };
            foreach (string argument in new[]
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                "-ProcId", Environment.ProcessId.ToString(),
                "-AppDir", appDir,
                "-Exe", exe,
                "-Stage", stage,
            })
            {
                start.ArgumentList.Add(argument);
            }

            Process.Start(start);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Открывает страницу релиза в браузере.</summary>
    public static void OpenPage(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* нет браузера — не критично */ }
    }

    /// <summary>Скрипт-установщик: ждёт выхода, копирует файлы с повторами и запускает приложение.</summary>
    private const string ApplyScript = @"param(
    [int]$ProcId,
    [string]$AppDir,
    [string]$Exe,
    [string]$Stage
)

$log = Join-Path ([System.IO.Path]::GetTempPath()) 'MotionForge\update\apply.log'
function Write-Log([string]$text) {
    try { Add-Content -LiteralPath $log -Value ('[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $text) } catch { }
}

Write-Log ('Ожидание закрытия приложения, PID {0}' -f $ProcId)
for ($i = 0; $i -lt 240; $i++) {
    if (-not (Get-Process -Id $ProcId -ErrorAction SilentlyContinue)) { break }
    Start-Sleep -Milliseconds 250
}
Start-Sleep -Milliseconds 400

$ok = $false
for ($i = 0; $i -lt 20; $i++) {
    try {
        Copy-Item -Path (Join-Path $Stage '*') -Destination $AppDir -Recurse -Force -ErrorAction Stop
        $ok = $true
        break
    } catch {
        Write-Log ('Попытка {0}: {1}' -f ($i + 1), $_.Exception.Message)
        Start-Sleep -Milliseconds 500
    }
}

if ($ok) {
    Write-Log 'Файлы обновлены'
    Remove-Item -LiteralPath $Stage -Recurse -Force -ErrorAction SilentlyContinue
} else {
    Write-Log 'Не удалось заменить файлы — запускаю установленную версию'
}

Start-Process -FilePath $Exe
";

    /* ------------------------------ DTO ------------------------------ */

    public sealed class ReleaseDto
    {
        public ReleaseDto() { }
        [JsonPropertyName("tag_name")] public string TagName { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("body")] public string Body { get; set; }
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; }
        [JsonPropertyName("assets")] public List<ReleaseAssetDto> Assets { get; set; }
    }

    public sealed class ReleaseAssetDto
    {
        public ReleaseAssetDto() { }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
