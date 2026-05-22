using System.Diagnostics;
using System.Windows.Navigation;

namespace ITService.Backup.Helpers;

public static class UriHelper
{
    public static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialog.Warning(null, $"Не удалось открыть ссылку:\n{url}\n\n{ex.Message}");
        }
    }

    public static void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri != null)
            OpenInBrowser(e.Uri.AbsoluteUri);
        e.Handled = true;
    }
}
