using System;
using System.Linq;
using System.Windows;
using MotionForge.Model;
using MotionForge.Services;
using MotionForge.Views;

namespace MotionForge;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Текстовые метрики для модели: в WPF считаем по-настоящему (FormattedText),
        // в headless-тестах используется встроенная аппроксимация.
        Anim.TextMeasurer = new WpfTextMeasurer();

        // Пользовательские настройки: язык интерфейса, сетка, планшет, автосохранение.
        // При самом первом запуске (файла настроек ещё нет) подхватываем язык системы,
        // если для него есть перевод: иначе англоязычный пользователь видит русский UI.
        bool firstRun = !System.IO.File.Exists(AppSettings.FilePath);
        AppSettings.Current = AppSettings.Load().Normalized();
        if (firstRun) AppSettings.Current.Language = L10n.SystemLanguage(AppSettings.Current.Language);
        L10n.SetLanguage(AppSettings.Current.Language);

        if (e.Args.Any(a => string.Equals(a, "--lang-template", StringComparison.OrdinalIgnoreCase)))
            L10n.TemplateRequested = true;

        if (e.Args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = SelfTest.Run();
            Shutdown(Environment.ExitCode);
            return;
        }

        // Служебный режим: собрать иконку и превью знака (используется при сборке релиза).
        int iconIndex = Array.FindIndex(e.Args, a => string.Equals(a, "--export-icon", StringComparison.OrdinalIgnoreCase));
        if (iconIndex >= 0)
        {
            string target = iconIndex + 1 < e.Args.Length ? e.Args[iconIndex + 1]
                : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            Branding.ExportIco(target);
            Branding.SavePng(Branding.Render(256), System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target), "preview-256.png"));
            Branding.SavePng(Branding.Render(32), System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target), "preview-32.png"));
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            MessageBox.Show(args.Exception.Message, "MotionForge — ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            // Заставка: показывает знак приложения, пока готовится главное окно.
            SplashWindow splash = null;
            if (AppSettings.Current.ShowSplash && e.Args.Length == 0)
            {
                splash = new SplashWindow();
                splash.Show();
            }

            var window = new MainWindow();
            MainWindow = window;
            window.Show();

            if (splash != null)
            {
                // Держим заставку не меньше секунды, чтобы она не «мигала»,
                // и закрываем, когда главное окно уже нарисовано.
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1100),
                };
                timer.Tick += (_, __) =>
                {
                    timer.Stop();
                    try { splash.Close(); } catch { /* окно могло быть закрыто пользователем */ }
                };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            LogError(ex);
            throw;
        }
    }

    /// <summary>Журнал сбоев: помогает разобраться, если окно не открылось (путь в сообщении об ошибке).</summary>
    private static void LogError(Exception exception)
    {
        try
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "motionforge-error.txt");
            System.IO.File.AppendAllText(path,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + exception + Environment.NewLine +
                new string('-', 70) + Environment.NewLine);
        }
        catch
        {
            // журнал не должен мешать обработке ошибки
        }
    }
}
