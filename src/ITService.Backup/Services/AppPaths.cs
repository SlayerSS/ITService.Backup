namespace ITService.Backup.Services;

public static class AppPaths
{
    private static readonly string LegacyDataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ITService", "Backup");

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ITBackup");

    public static string ConfigFile => Path.Combine(DataDirectory, "config.json");
    public static string HistoryFile => Path.Combine(DataDirectory, "history.json");
    public static string SelectionFile => Path.Combine(DataDirectory, "selection.json");
    public static string LogFile => Path.Combine(DataDirectory, "backup.log");

    /// <summary>DLL AlphaVSS (распаковка/обновление при старте приложения).</summary>
    public static string VssRuntimeDirectory => Path.Combine(DataDirectory, "vss-runtime");

    public static void EnsureDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
        MigrateAndRemoveLegacy();
    }

    private static void MigrateAndRemoveLegacy()
    {
        if (!Directory.Exists(LegacyDataDirectory))
            return;

        foreach (var name in new[] { "config.json", "history.json", "selection.json", "backup.log" })
        {
            var src = Path.Combine(LegacyDataDirectory, name);
            var dst = Path.Combine(DataDirectory, name);
            try
            {
                if (File.Exists(src) && !File.Exists(dst))
                    File.Copy(src, dst);
            }
            catch
            {
                // ignore — файл может быть занят
            }
        }

        TryDeleteLegacyDirectory();
    }

    private static void TryDeleteLegacyDirectory()
    {
        try
        {
            if (Directory.Exists(LegacyDataDirectory))
                Directory.Delete(LegacyDataDirectory, recursive: true);

            var itServiceDir = Path.GetDirectoryName(LegacyDataDirectory);
            if (!string.IsNullOrEmpty(itServiceDir)
                && Directory.Exists(itServiceDir)
                && !Directory.EnumerateFileSystemEntries(itServiceDir).Any())
                Directory.Delete(itServiceDir);
        }
        catch
        {
            // нет прав или файлы заняты — при следующем запуске повторим
        }
    }
}
