namespace ITService.Backup.Services;

public static class ArchiveSessionPaths
{
    public const string TempFolderName = "_temp";
    public const string LogsFolderName = "logs";
    public const string FilesFolderName = "Files";
    public const string SqlFolderName = "SQL";
    public const string FoldersFolderName = "Folders";

    public static string TempDir(string archivePath) =>
        Path.Combine(archivePath, TempFolderName);

    public static string LogsDir(string archivePath) =>
        Path.Combine(archivePath, LogsFolderName);

    public static string SessionLogFile(string archivePath) =>
        Path.Combine(LogsDir(archivePath), "backup.log");

    public static string FilesDir(string archivePath) =>
        Path.Combine(archivePath, FilesFolderName);

    public static string SqlDir(string archivePath) =>
        Path.Combine(archivePath, SqlFolderName);

    public static string FoldersDir(string archivePath) =>
        Path.Combine(archivePath, FoldersFolderName);

    public static void EnsureLogsDir(string archivePath) =>
        Directory.CreateDirectory(LogsDir(archivePath));

    public static void EnsureSqlDir(string archivePath) =>
        Directory.CreateDirectory(SqlDir(archivePath));

    public static void EnsureFoldersDir(string archivePath) =>
        Directory.CreateDirectory(FoldersDir(archivePath));

    public static void EnsureFilesDir(string archivePath) =>
        Directory.CreateDirectory(FilesDir(archivePath));

    public static void EnsureTempDir(string archivePath) =>
        Directory.CreateDirectory(TempDir(archivePath));

    public static void TryRemoveTempDirectory(string? archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            return;

        var temp = TempDir(archivePath);
        try
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
        catch
        {
            // занято или нет прав — не критично
        }
    }
}
