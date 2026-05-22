namespace ITService.Backup.Services;

/// <summary>
/// Проверки: программа не изменяет и не удаляет исходные базы и файлы.
/// Запись только в папку архива на выбранном носителе (USB или локальная папка).
/// </summary>
public static class BackupSafety
{
    public const string UsbSafetyNotice =
        "Только чтение исходных данных. Запись на съёмный USB, диски с базами не изменяются.";

    public const string LocalSafetyNotice =
        "Только чтение исходных данных. Запись в выбранную папку; не класть архив в каталоги источников.";

    public const string NonRemovableDriveWarning =
        "Выбранный диск не съёмный. Рекомендуется копировать на USB-флешку и после завершения отключить носитель от компьютера.";

    public const string LocalBackupRiskWarning =
        "Хранение резервных копий на том же компьютере, где работают оригинальные базы, опасно: " +
        "при сбое диска, вирусе или ошибке администратора можно потерять и данные, и копии. " +
        "Рекомендуется съёмный USB или другой сервер.";
    private static readonly HashSet<string> ForbiddenDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "master", "model", "msdb", "tempdb", "distribution"
    };

    public static string SafetyNotice(bool useUsbTarget, bool removableOnly = true)
    {
        if (!useUsbTarget)
            return LocalSafetyNotice;
        return removableOnly
            ? UsbSafetyNotice
            : "Только чтение исходных данных. Запись на выбранный диск; для переноса лучше съёмный USB.";
    }

    public static void ValidateDatabaseName(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new InvalidOperationException("Имя базы не задано.");

        if (ForbiddenDatabases.Contains(databaseName.Trim()))
            throw new InvalidOperationException($"Резервное копирование системной базы «{databaseName}» запрещено.");

        if (databaseName.Contains(']') || databaseName.Contains(';') || databaseName.Contains('\''))
            throw new InvalidOperationException($"Недопустимое имя базы: {databaseName}");
    }

    public static void ValidateSqlBackupTarget(string backupFilePath, string sqlOutputDirectory, string approvedTargetBase)
    {
        var fullPath = Path.GetFullPath(backupFilePath);
        var sqlDir = Path.GetFullPath(sqlOutputDirectory);

        if (!fullPath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Файл резервной копии SQL должен иметь расширение .bak");

        EnsureUnderDirectory(fullPath, sqlDir);
        EnsureUnderTargetBase(fullPath, approvedTargetBase);
    }

    public static void ValidateFileSource(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new InvalidOperationException("Путь к файлу не задан.");

        var full = Path.GetFullPath(sourcePath);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Исходный файл не найден: {full}");
    }

    public static void ValidateFolderSource(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new InvalidOperationException("Путь к исходной папке не задан.");

        var full = Path.GetFullPath(sourcePath);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Исходная папка не найдена: {full}");
    }

    public static void ValidateFolderDestination(string destinationPath, string foldersOutputDirectory, string sourcePath, string approvedTargetBase)
    {
        var destFull = Path.GetFullPath(destinationPath);
        var foldersDir = Path.GetFullPath(foldersOutputDirectory);
        var sourceFull = Path.GetFullPath(sourcePath);

        EnsureUnderDirectory(destFull, foldersDir);
        EnsureUnderTargetBase(destFull, approvedTargetBase);

        if (PathsOverlap(sourceFull, destFull))
            throw new InvalidOperationException("Папка назначения не может совпадать с исходной или находиться внутри неё.");
    }

    public static void ValidateArchiveRoot(string archivePath, string backupBasePath)
    {
        var archive = Path.GetFullPath(archivePath);
        var basePath = Path.GetFullPath(backupBasePath);
        EnsureUnderDirectory(archive, basePath);
        EnsureUnderTargetBase(archive, basePath);
    }

    public static void EnsureUnderDirectory(string fullPath, string directoryPath)
    {
        var dir = Path.GetFullPath(directoryPath);
        if (!dir.EndsWith(Path.DirectorySeparatorChar))
            dir += Path.DirectorySeparatorChar;

        var file = Path.GetFullPath(fullPath);
        if (!file.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Путь записи находится вне разрешённой папки архива.");
    }

    public static void EnsureUnderTargetBase(string fullPath, string approvedTargetBase)
    {
        var file = NormalizePath(fullPath);
        var dir = NormalizePath(approvedTargetBase);

        if (file.Equals(dir, StringComparison.OrdinalIgnoreCase))
            return;

        var dirPrefix = dir + Path.DirectorySeparatorChar;
        if (!file.StartsWith(dirPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Запись разрешена только в выбранную папку для резервных копий.");
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static bool IsSystemDrive(string driveRoot) =>
        driveRoot.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase);

    private static bool PathsOverlap(string a, string b)
    {
        if (!a.EndsWith(Path.DirectorySeparatorChar))
            a += Path.DirectorySeparatorChar;
        if (!b.EndsWith(Path.DirectorySeparatorChar))
            b += Path.DirectorySeparatorChar;

        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase)
               || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }
}
