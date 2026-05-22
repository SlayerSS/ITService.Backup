using Microsoft.Win32;

namespace ITService.Backup.Helpers;

public static class OneCV8iFileDialog
{
    public static bool TryPick(out string? filePath)
    {
        filePath = null;
        var dlg = new OpenFileDialog
        {
            Title = "Файл списка баз 1С",
            Filter = "Список баз 1С (*.v8i)|*.v8i|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };

        var initialDir = Services.OneCPathImportService.GetDefaultV8iBrowseFolder();
        if (!string.IsNullOrEmpty(initialDir))
            dlg.InitialDirectory = initialDir;

        if (dlg.ShowDialog() != true)
            return false;

        filePath = dlg.FileName;
        return true;
    }
}
