using System;
using System.Linq;
using System.Windows;
using MotionForge.Model;
using MotionForge.Services;

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
        AppSettings.Current = AppSettings.Load().Normalized();
        L10n.SetLanguage(AppSettings.Current.Language);

        if (e.Args.Any(a => string.Equals(a, "--lang-template", StringComparison.OrdinalIgnoreCase)))
            L10n.TemplateRequested = true;

        if (e.Args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = SelfTest.Run();
            Shutdown(Environment.ExitCode);
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
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
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
