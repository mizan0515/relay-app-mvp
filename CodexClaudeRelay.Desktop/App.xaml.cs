using System.IO;
using System.Windows;
using System.Windows.Threading;
using CodexClaudeRelay.Core.Runtime;

namespace CodexClaudeRelay.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string RotationSmokeSwitch = "--rotation-smoke";

    private static readonly string CrashDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexClaudeRelayMvp");

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Any(a => string.Equals(a, RotationSmokeSwitch, StringComparison.Ordinal)))
        {
            var result = RotationSmokeRunner.Run();
            WriteSmokeResult(result);
            Shutdown(result.ExitCode);
            return;
        }

        base.OnStartup(e);
    }

    private static void WriteSmokeResult(RotationSmokeRunner.Result result)
    {
        try
        {
            Directory.CreateDirectory(CrashDirectory);
            var path = Path.Combine(CrashDirectory, "rotation-smoke.log");
            File.WriteAllText(path, result.Summary);
        }
        catch
        {
            // Log write is best-effort; exit code still communicates pass/fail.
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportCrash("DispatcherUnhandledException", e.Exception);
        e.Handled = true;
        Shutdown(-1);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        ReportCrash("AppDomainUnhandledException", e.ExceptionObject as Exception);
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ReportCrash("TaskSchedulerUnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void ReportCrash(string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(CrashDirectory);

            var path = Path.Combine(
                CrashDirectory,
                $"relayapp-crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            var body =
                $"Source: {source}{Environment.NewLine}" +
                $"Time: {DateTime.Now:O}{Environment.NewLine}" +
                $"Exception: {exception}{Environment.NewLine}";

            File.WriteAllText(path, body);

            MessageBox.Show(
                $"Relay App failed during startup or runtime.{Environment.NewLine}{Environment.NewLine}" +
                $"Source: {source}{Environment.NewLine}" +
                $"Log: {path}{Environment.NewLine}{Environment.NewLine}" +
                $"{exception?.Message}",
                "Relay App Crash",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Avoid secondary crash loops while reporting the original failure.
        }
    }
}
