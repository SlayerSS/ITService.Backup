using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ITService.Backup.Helpers;
using ITService.Backup.Services;

namespace ITService.Backup;

public partial class AppDialogWindow : Window
{
    private readonly AppDialogKind _kind;
    private readonly string? _logFolder;
    private readonly bool _showLogButton;

    public AppDialogWindow(
        AppDialogKind kind,
        string title,
        string message,
        string? logFolder,
        string primaryText,
        string secondaryText,
        bool showSecondary)
    {
        InitializeComponent();
        _kind = kind;
        _logFolder = logFolder;
        _showLogButton = kind == AppDialogKind.Error && !string.IsNullOrWhiteSpace(logFolder);

        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;

        if (showSecondary)
        {
            SecondaryButton.Visibility = Visibility.Visible;
            SecondaryButton.Content = secondaryText;
        }

        if (_showLogButton)
        {
            LogPathPanel.Visibility = Visibility.Visible;
            LogPathText.Text = logFolder;
        }

        try
        {
            ApplyTheme(kind);
        }
        catch (Exception ex)
        {
            LogService.Warn($"AppDialog theme: {ex.Message}");
        }

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                    // ignore drag errors on transparent window
                }
            }
        };
    }

    private void ApplyTheme(AppDialogKind kind)
    {
        var (accent, badgeBg, icon) = kind switch
        {
            AppDialogKind.Success => ("#22C55E", "#E8F5E9", "✓"),
            AppDialogKind.Warning => ("#F59E0B", "#FFF8E1", "!"),
            AppDialogKind.Error => ("#EF4444", "#FFEBEE", "✕"),
            AppDialogKind.Question => ("#3B82F6", "#E3F2FD", "?"),
            _ => ("#1565C0", "#E3F2FD", "i")
        };

        AccentBar.Background = CreateBrush(accent);
        IconBadge.Background = CreateBrush(badgeBg);
        IconText.Text = icon;
        IconText.Foreground = CreateBrush(accent);

        if (kind == AppDialogKind.Error)
            RootBorder.BorderBrush = CreateBrush("#C62828");
        else if (kind == AppDialogKind.Question)
            RootBorder.BorderBrush = CreateBrush("#1565C0");
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        if (_kind == AppDialogKind.Question)
        {
            DialogResult = false;
            Close();
            return;
        }

        if (_showLogButton)
            OpenLogFolder();
    }

    private void OpenLogFolder()
    {
        if (string.IsNullOrWhiteSpace(_logFolder))
            return;

        try
        {
            Directory.CreateDirectory(_logFolder);
            Process.Start(new ProcessStartInfo { FileName = _logFolder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось открыть папку:\n{ex.Message}", "ITBackup",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
