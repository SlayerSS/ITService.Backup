namespace ITService.Backup.Services;

public static class LogService
{
    private static readonly object Lock = new();
    private static string? _sessionLogFile;
    private static string? _sessionLogDirectory;

    public static string? SessionLogDirectory => _sessionLogDirectory;

    public static void BeginArchiveSession(string archivePath)
    {
        ArchiveSessionPaths.EnsureLogsDir(archivePath);
        _sessionLogDirectory = ArchiveSessionPaths.LogsDir(archivePath);
        _sessionLogFile = ArchiveSessionPaths.SessionLogFile(archivePath);
        Write("INFO", $"Сессия бэкапа: {archivePath}");
    }

    public static void EndArchiveSession()
    {
        _sessionLogFile = null;
        _sessionLogDirectory = null;
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    /// <summary>Только общий лог (после удаления папки архива / EndArchiveSession).</summary>
    public static void InfoGlobal(string message) => Write("INFO", message, session: false);

    public static void WarnGlobal(string message) => Write("WARN", message, session: false);

    private static void Write(string level, string message, bool session = true)
    {
        AppPaths.EnsureDataDirectory();
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        lock (Lock)
        {
            try
            {
                File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
            }
            catch
            {
                // Не прерываем бэкап/отмену из-за сбоя общего лога.
            }

            if (session)
                TryAppendSessionLog(line);
        }
    }

    private static void TryAppendSessionLog(string line)
    {
        if (_sessionLogFile == null)
            return;

        try
        {
            var dir = Path.GetDirectoryName(_sessionLogFile);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return;

            File.AppendAllText(_sessionLogFile, line + Environment.NewLine);
        }
        catch
        {
            // Папка архива уже удалена (отмена бэкапа и т.п.) — остаётся только общий лог.
        }
    }
}
