using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using ITService.Backup.Models;

namespace ITService.Backup.Services;

public sealed class SqlBackupService
{
    private static readonly Regex PercentRegex = new(@"(\d+)\s*percent", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string BuildConnectionString(SqlServerSettings settings, string? initialCatalog = "master")
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = settings.Instance,
            InitialCatalog = initialCatalog ?? "master",
            TrustServerCertificate = true,
            ConnectTimeout = 30
        };

        if (settings.UseWindowsAuth)
            builder.IntegratedSecurity = true;
        else
        {
            builder.IntegratedSecurity = false;
            builder.UserID = settings.UserName ?? "";
            builder.Password = settings.Password ?? "";
        }

        return builder.ConnectionString;
    }

    public async Task TestConnectionAsync(SqlServerSettings settings, CancellationToken ct = default)
    {
        if (!await TryConnectAsync(settings, connectTimeoutSeconds: 30, ct))
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(settings.Instance)
                    ? "Укажите имя сервера SQL (как в SSMS) или дождитесь автоопределения на этом сервере."
                    : $"Не удалось подключиться к «{settings.Instance.Trim()}».");
    }

    public async Task<bool> TryConnectAsync(
        SqlServerSettings settings,
        int connectTimeoutSeconds = 8,
        CancellationToken ct = default)
    {
        var instance = settings.Instance?.Trim();
        if (string.IsNullOrEmpty(instance))
            return false;

        try
        {
            var builder = new SqlConnectionStringBuilder(BuildConnectionString(settings))
            {
                ConnectTimeout = connectTimeoutSeconds
            };
            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<string>> ListUserDatabasesAsync(SqlServerSettings settings, CancellationToken ct = default)
    {
        const string sql = """
            SELECT name
            FROM sys.databases
            WHERE database_id > 4
              AND state_desc = 'ONLINE'
              AND name NOT IN ('ReportServer', 'ReportServerTempDB')
            ORDER BY name
            """;

        var result = new List<string>();
        await using var conn = new SqlConnection(BuildConnectionString(settings));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0));
        return result;
    }

    public async Task<List<string>> GetDataFileLogicalNamesAsync(
        SqlServerSettings settings,
        string databaseName,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT name
            FROM sys.master_files
            WHERE database_id = DB_ID(@db) AND type = 0
            ORDER BY file_id
            """;

        var names = new List<string>();
        await using var conn = new SqlConnection(BuildConnectionString(settings));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@db", databaseName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            names.Add(reader.GetString(0));

        if (names.Count == 0)
            throw new InvalidOperationException($"У базы «{databaseName}» не найдены файлы данных.");

        return names;
    }

    public async Task<HashSet<string>> GetDatabaseFileDriveRootsAsync(
        SqlServerSettings settings,
        string databaseName,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT physical_name
            FROM sys.master_files
            WHERE database_id = DB_ID(@db)
            """;

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var conn = new SqlConnection(BuildConnectionString(settings));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@db", databaseName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var path = reader.GetString(0);
            var root = Path.GetFullPath(Path.GetPathRoot(path) ?? "");
            if (!string.IsNullOrEmpty(root))
                roots.Add(root);
        }
        return roots;
    }

    public async Task<HashSet<string>> GetDatabasePhysicalPathsAsync(
        SqlServerSettings settings,
        string databaseName,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT physical_name
            FROM sys.master_files
            WHERE database_id = DB_ID(@db)
            """;

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var conn = new SqlConnection(BuildConnectionString(settings));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@db", databaseName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var path = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(path))
                paths.Add(path);
        }
        return paths;
    }

    public async Task BackupDatabaseAsync(
        SqlServerSettings settings,
        string databaseName,
        string backupFilePath,
        string sqlOutputDirectory,
        string archivePath,
        BackupWriteContext writeContext,
        BackupProgressTracker tracker,
        int stepIndex,
        IProgress<BackupProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        BackupSafety.ValidateDatabaseName(databaseName);
        SourceDriveGuard.EnsureWriteAllowed(backupFilePath, writeContext);
        BackupSafety.ValidateSqlBackupTarget(backupFilePath, sqlOutputDirectory, writeContext.ApprovedTargetBase);

        ArchiveSessionPaths.EnsureTempDir(archivePath);
        var tempDir = ArchiveSessionPaths.TempDir(archivePath);
        var tempBak = Path.Combine(tempDir, databaseName + "_" + Guid.NewGuid().ToString("N") + ".bak");
        SourceDriveGuard.EnsureWriteAllowed(tempBak, writeContext);

        tracker.BeginStep(stepIndex, $"SQL: {databaseName} — 0%");
        progress?.Report(tracker.Snapshot());

        var sqlDir = Path.GetDirectoryName(backupFilePath);
        if (!string.IsNullOrEmpty(sqlDir))
            Directory.CreateDirectory(sqlDir);

        try
        {
            var escapedTemp = tempBak.Replace("'", "''");
            var scopeClause = await BuildBackupScopeClauseAsync(settings, databaseName, ct);
            var options = BuildBackupOptions(settings);
            var dbEscaped = databaseName.Replace("]", "]]");

            await using var conn = new SqlConnection(BuildConnectionString(settings, databaseName));
            conn.InfoMessage += (_, e) =>
            {
                var m = PercentRegex.Match(e.Message);
                if (!m.Success || !int.TryParse(m.Groups[1].Value, out var pct))
                    return;
                tracker.SetSubProgress(pct, $"SQL: {databaseName} — {pct}%");
                progress?.Report(tracker.Snapshot());
            };

            await conn.OpenAsync(ct);
            await ExecuteBackupAsync(conn, settings, dbEscaped, scopeClause, escapedTemp, options, ct);

            if (File.Exists(backupFilePath))
                File.Delete(backupFilePath);
            File.Move(tempBak, backupFilePath);
        }
        finally
        {
            try
            {
                if (File.Exists(tempBak))
                    File.Delete(tempBak);
            }
            catch
            {
                // ignore
            }
        }

        tracker.SetSubProgress(100, $"SQL: {databaseName} — готово");
        tracker.EndStep();
        progress?.Report(tracker.Snapshot());
        LogService.Info($"SQL backup completed: {databaseName} -> {backupFilePath}");
    }

    public async Task<long> EstimateDatabaseSizeAsync(SqlServerSettings settings, string databaseName, CancellationToken ct = default)
    {
        var sql = settings.ExcludeTransactionLogs
            ? """
              SELECT SUM(CAST(size AS bigint) * 8192)
              FROM sys.master_files
              WHERE database_id = DB_ID(@db) AND type = 0
              """
            : """
              SELECT SUM(CAST(size AS bigint) * 8192)
              FROM sys.master_files
              WHERE database_id = DB_ID(@db) AND type IN (0, 1)
              """;

        await using var conn = new SqlConnection(BuildConnectionString(settings, databaseName));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@db", databaseName);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is long l ? l : 0;
    }

    private async Task<string> GetRecoveryModelAsync(
        SqlServerSettings settings,
        string databaseName,
        CancellationToken ct)
    {
        const string sql = "SELECT recovery_model_desc FROM sys.databases WHERE name = @db";
        await using var conn = new SqlConnection(BuildConnectionString(settings));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@db", databaseName);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value?.ToString()?.Trim() ?? "FULL";
    }

    /// <summary>Строка между именем базы и TO DISK (FILE=… или READ_WRITE_FILEGROUPS).</summary>
    private async Task<string> BuildBackupScopeClauseAsync(
        SqlServerSettings settings,
        string databaseName,
        CancellationToken ct)
    {
        if (!settings.ExcludeTransactionLogs)
            return "";

        var recovery = await GetRecoveryModelAsync(settings, databaseName, ct);
        if (recovery.Equals("SIMPLE", StringComparison.OrdinalIgnoreCase))
        {
            LogService.Info($"SQL {databaseName}: модель SIMPLE — частичный бэкап READ_WRITE_FILEGROUPS (без отдельного FILE=)");
            return "READ_WRITE_FILEGROUPS";
        }

        var files = await GetDataFileLogicalNamesAsync(settings, databaseName, ct);
        var parts = files.Select(f => $"FILE = N'{f.Replace("'", "''")}'");
        return string.Join(",\n", parts);
    }

    private static string BuildBackupSql(string dbEscaped, string scopeClause, string diskPath, string options)
    {
        var scopeLine = string.IsNullOrWhiteSpace(scopeClause) ? "" : scopeClause + "\n";
        return $"""
            BACKUP DATABASE [{dbEscaped}]
            {scopeLine}TO DISK = N'{diskPath}'
            WITH {options}
            """;
    }

    private static async Task ExecuteBackupAsync(
        SqlConnection conn,
        SqlServerSettings settings,
        string dbEscaped,
        string scopeClause,
        string diskPath,
        string options,
        CancellationToken ct)
    {
        var sql = BuildBackupSql(dbEscaped, scopeClause, diskPath, options);
        try
        {
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 0 };
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException ex) when (IsSimpleRecoveryFileBackupError(ex) && scopeClause.Contains("FILE =", StringComparison.OrdinalIgnoreCase))
        {
            LogService.Warn($"SQL {dbEscaped}: FILE= не поддерживается для SIMPLE, повтор с READ_WRITE_FILEGROUPS");
            var fallbackSql = BuildBackupSql(dbEscaped, "READ_WRITE_FILEGROUPS", diskPath, options);
            await using var cmd2 = new SqlCommand(fallbackSql, conn) { CommandTimeout = 0 };
            await cmd2.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException ex) when (settings.UseCompression && options.Contains("COMPRESSION", StringComparison.OrdinalIgnoreCase)
            && ex.Message.Contains("compression", StringComparison.OrdinalIgnoreCase))
        {
            LogService.Warn($"Сжатие недоступно для {dbEscaped}, повтор без COMPRESSION");
            var noComp = BuildBackupOptions(settings, forceNoCompression: true);
            var retrySql = BuildBackupSql(dbEscaped, scopeClause, diskPath, noComp);
            await using var cmdRetry = new SqlCommand(retrySql, conn) { CommandTimeout = 0 };
            await cmdRetry.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException ex) when (IsSqlUserCancel(ex))
        {
            throw new OperationCanceledException("Резервное копирование SQL отменено.", ex);
        }
    }

    private static bool IsSqlUserCancel(SqlException ex) =>
        ex.Number == 3204
        || ex.Message.Contains("отменена пользователем", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("cancelled by user", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("canceled by user", StringComparison.OrdinalIgnoreCase);

    private static bool IsSimpleRecoveryFileBackupError(SqlException ex) =>
        ex.Number is 3119 or 4217
        || ex.Message.Contains("SIMPLE", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("модель восстановления SIMPLE", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("READ_WRITE_FILEGROUPS", StringComparison.OrdinalIgnoreCase);

    private static string BuildBackupOptions(SqlServerSettings settings, bool forceNoCompression = false)
    {
        var list = new List<string> { "COPY_ONLY", "CHECKSUM", "INIT", "STATS = 5" };
        if (settings.UseCompression && !forceNoCompression)
            list.Insert(0, "COMPRESSION");
        else
            list.Insert(0, "NO_COMPRESSION");
        return string.Join(", ", list);
    }
}
