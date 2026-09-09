using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace BackupProjekt.App;

public partial class App : Application
{
    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backup_explorer.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        Log("==========================================");
        Log($"Backup Explorer started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Log($"OS: {Environment.OSVersion}, .NET: {Environment.Version}");
        Log($"Base Directory: {AppDomain.CurrentDomain.BaseDirectory}");
        Log("==========================================");

        try
        {
            Log("Creating MainWindow...");
            var mainWindow = new MainWindow();
            Log("Showing MainWindow...");
            mainWindow.Show();
            Log("MainWindow displayed successfully.");
        }
        catch (Exception ex)
        {
            LogException("Startup Failure in OnStartup", ex);
            MessageBox.Show(
                $"Failed to launch Backup Explorer:\n\n{ex.Message}\n\nDetailed logs written to:\n{LogFilePath}",
                "Backup Explorer - Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("DispatcherUnhandledException", e.Exception);
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nCheck backup_explorer.log for details.",
            "Backup Explorer Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException("CurrentDomain_UnhandledException", ex);
        }
        else
        {
            Log($"[CRITICAL] CurrentDomain_UnhandledException: {e.ExceptionObject}");
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("TaskScheduler_UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    public static void Log(string message)
    {
        try
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Console.WriteLine(line);
            File.AppendAllText(LogFilePath, line + Environment.NewLine);
        }
        catch { }
    }

    public static void LogException(string context, Exception ex)
    {
        try
        {
            string details = $"[ERROR] [{DateTime.Now:HH:mm:ss.fff}] {context}:\n{ex}";
            Console.Error.WriteLine(details);
            File.AppendAllText(LogFilePath, details + Environment.NewLine);
        }
        catch { }
    }
}
