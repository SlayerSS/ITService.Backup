using System.Windows;
using System.Windows.Threading;
using ITService.Backup.Services;

namespace ITService.Backup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        _ = AlphaVssRuntimeBootstrap.EnsureReady();
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.Error($"UI crash: {e.Exception}");
        e.Handled = true;

        try
        {
            Helpers.AppDialog.Error(
                Current.MainWindow,
                $"Необработанная ошибка:\n{e.Exception.Message}",
                AppPaths.DataDirectory,
                "ITBackup");
        }
        catch
        {
            MessageBox.Show(
                $"Необработанная ошибка:\n{e.Exception.Message}",
                "ITBackup",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogService.Error($"Domain crash: {ex}");
    }
}
