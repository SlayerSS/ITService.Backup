namespace ITService.Backup.Services;

public static class BackupSpaceHelper
{
    /// <summary>Запас к оценке (15%): свободного места должно быть ≥ оценка × 1.15.</summary>
    public const double SafetyMargin = 1.15;

    /// <summary>Коэффициент для .bak при сжатии SQL (оценка по размеру файлов БД).</summary>
    public const double SqlCompressedBakFactor = 0.85;

    public static long ApplySafetyMargin(long estimatedBytes) =>
        estimatedBytes <= 0 ? 0 : (long)Math.Ceiling(estimatedBytes * SafetyMargin);

    public static long GetAvailableBytes(string? pathOnVolume)
    {
        if (string.IsNullOrWhiteSpace(pathOnVolume))
            return 0;

        try
        {
            var full = Path.GetFullPath(pathOnVolume);
            var root = Path.GetPathRoot(full) ?? "";
            if (string.IsNullOrEmpty(root))
                return 0;

            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static string FormatSizeLine(long requiredBytes, long freeBytes, bool useUsbTarget)
    {
        var need = FormatBytes(requiredBytes);
        if (freeBytes <= 0)
            return $"Нужно примерно {need} (свободное место не определено)";

        var free = FormatBytes(freeBytes);
        var target = useUsbTarget ? "на USB" : "в папке";
        var ok = freeBytes >= requiredBytes;
        return ok
            ? $"Нужно ~{need} · свободно {free} {target}"
            : $"Нужно ~{need} · свободно только {free} {target} — мало места";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
            bytes = 0;
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        double size = bytes;
        var i = 0;
        while (size >= 1024 && i < units.Length - 1)
        {
            size /= 1024;
            i++;
        }

        return i switch
        {
            0 => $"{size:0} {units[i]}",
            1 => $"{size:0} {units[i]}",
            _ => $"{size:0.##} {units[i]}"
        };
    }

    public static bool IsDiskFullException(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is IOException io && (
                    io.HResult == -2147024784 /* ERROR_DISK_FULL */ ||
                    io.Message.Contains("disk space", StringComparison.OrdinalIgnoreCase) ||
                    io.Message.Contains("недостаточно места", StringComparison.OrdinalIgnoreCase) ||
                    io.Message.Contains("not enough space", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (e.Message.Contains("disk full", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("недостаточно места на диске", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static void TryRemoveIncompleteArchive(string? archivePath)
    {
        LogService.EndArchiveSession();

        if (string.IsNullOrWhiteSpace(archivePath) || !Directory.Exists(archivePath))
            return;

        try
        {
            Directory.Delete(archivePath, recursive: true);
            LogService.InfoGlobal($"Удалена неполная папка архива: {archivePath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogService.WarnGlobal($"Не удалось удалить неполный архив {archivePath}: {ex.Message}");
        }
    }

    public static string BuildInsufficientSpaceMessage(
        long requiredBytes,
        long freeBytes,
        bool useUsbTarget,
        bool duringBackup = false)
    {
        var target = useUsbTarget ? "На USB" : "В папке назначения";
        var phase = duringBackup ? "Во время копирования" : "Перед началом";
        return $"{phase}: {target} недостаточно места.\n" +
               $"Нужно примерно {FormatBytes(requiredBytes)} (с запасом 15%).\n" +
               $"Свободно: {(freeBytes > 0 ? FormatBytes(freeBytes) : "не удалось определить")}.";
    }
}
