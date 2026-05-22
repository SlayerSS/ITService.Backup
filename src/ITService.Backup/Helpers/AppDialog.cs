using System.Windows;
using ITService.Backup.Services;

namespace ITService.Backup.Helpers;

public enum AppDialogKind
{
    Info,
    Success,
    Warning,
    Error,
    Question
}

public static class AppDialog
{
    public static void Info(Window? owner, string message, string? title = null) =>
        Show(owner, AppDialogKind.Info, message, title);

    public static void Success(Window? owner, string message, string? title = null) =>
        Show(owner, AppDialogKind.Success, message, title);

    public static void Warning(Window? owner, string message, string? title = null) =>
        Show(owner, AppDialogKind.Warning, message, title);

    public static void Error(Window? owner, string message, string? logFolder = null, string? title = null) =>
        Show(owner, AppDialogKind.Error, message, title, logFolder);

    public static bool Question(Window? owner, string message, string? title = null, string yesText = "Да", string noText = "Нет") =>
        Show(owner, AppDialogKind.Question, message, title, null, yesText, noText);

    private static bool Show(
        Window? owner,
        AppDialogKind kind,
        string message,
        string? title,
        string? logFolder = null,
        string yesText = "ОК",
        string noText = "Отмена")
    {
        var safeOwner = ResolveOwner(owner);
        var (primary, secondary, showSecondary) = kind switch
        {
            AppDialogKind.Question => (yesText, noText, true),
            AppDialogKind.Error when !string.IsNullOrWhiteSpace(logFolder) => ("Закрыть", "Открыть логи", true),
            _ => ("ОК", "", false)
        };

        try
        {
            var dlg = new AppDialogWindow(
                kind,
                title ?? DefaultTitle(kind),
                message,
                logFolder,
                primary,
                secondary,
                showSecondary)
            {
                Owner = safeOwner,
                WindowStartupLocation = safeOwner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen
            };
            return dlg.ShowDialog() == true;
        }
        catch (Exception ex)
        {
            LogService.Error($"AppDialog failed ({kind}): {ex}");
            return ShowFallback(kind, title ?? DefaultTitle(kind), message, primary, noText, showSecondary);
        }
    }

    private static Window? ResolveOwner(Window? owner)
    {
        if (owner == null)
            return null;

        try
        {
            if (!owner.IsLoaded || !owner.IsVisible)
                return Application.Current?.MainWindow;
        }
        catch
        {
            return Application.Current?.MainWindow;
        }

        return owner;
    }

    private static bool ShowFallback(
        AppDialogKind kind,
        string title,
        string message,
        string primary,
        string secondary,
        bool showSecondary)
    {
        if (kind == AppDialogKind.Question && showSecondary)
        {
            var result = System.Windows.MessageBox.Show(
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }

        System.Windows.MessageBox.Show(
            message,
            title,
            MessageBoxButton.OK,
            kind switch
            {
                AppDialogKind.Error => MessageBoxImage.Error,
                AppDialogKind.Warning => MessageBoxImage.Warning,
                AppDialogKind.Success => MessageBoxImage.Information,
                _ => MessageBoxImage.Information
            });
        return kind != AppDialogKind.Question;
    }

    private static string DefaultTitle(AppDialogKind kind) => kind switch
    {
        AppDialogKind.Success => "Готово",
        AppDialogKind.Warning => "Внимание",
        AppDialogKind.Error => "Ошибка",
        AppDialogKind.Question => "Подтверждение",
        _ => "ITBackup"
    };
}
