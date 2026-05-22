using ITService.Backup.Models;

namespace ITService.Backup.Services;

public sealed class BackupSizeEstimateService
{
    private readonly SqlBackupService _sql = new();
    private readonly FolderBackupService _folders = new();
    private readonly FileBackupService _files = new();

    public async Task<BackupSizeEstimate> EstimateAsync(
        AppConfig config,
        IReadOnlyList<string> selectedDatabases,
        IReadOnlyList<string> selectedFolderIds,
        IReadOnlyList<string> selectedFileIds,
        CancellationToken ct = default)
    {
        long sqlBytes = 0;
        foreach (var db in selectedDatabases)
        {
            ct.ThrowIfCancellationRequested();
            var raw = await _sql.EstimateDatabaseSizeAsync(config.SqlServer, db, ct);
            sqlBytes += ApplySqlBakFactor(raw, config.SqlServer.UseCompression);
        }

        long folderBytes = 0;
        foreach (var id in selectedFolderIds)
        {
            var folder = config.Folders.FirstOrDefault(f => f.Id == id);
            if (folder != null)
                folderBytes += _folders.EstimateFolderSize(folder);
        }

        long fileBytes = 0;
        foreach (var id in selectedFileIds)
        {
            var file = config.Files.FirstOrDefault(f => f.Id == id);
            if (file != null)
            {
                try
                {
                    fileBytes += _files.EstimateFileSize(file);
                }
                catch
                {
                    // пропускаем недоступные файлы в оценке
                }
            }
        }

        var total = sqlBytes + folderBytes + fileBytes;
        return new BackupSizeEstimate
        {
            SqlBytes = sqlBytes,
            FolderBytes = folderBytes,
            FileBytes = fileBytes,
            TotalBytes = total,
            RequiredBytes = BackupSpaceHelper.ApplySafetyMargin(total),
            SqlCount = selectedDatabases.Count,
            FolderCount = selectedFolderIds.Count,
            FileCount = selectedFileIds.Count
        };
    }

    public static long ApplySqlBakFactor(long catalogBytes, bool useCompression) =>
        useCompression
            ? (long)Math.Ceiling(catalogBytes * BackupSpaceHelper.SqlCompressedBakFactor)
            : catalogBytes;

    /// <summary>Вес шага для прогресс-бара (как при планировании).</summary>
    public async Task<long> GetStepWeightAsync(
        AppConfig config,
        string kind,
        string idOrName,
        CancellationToken ct = default) => kind switch
    {
        "sql" => ApplySqlBakFactor(
            await _sql.EstimateDatabaseSizeAsync(config.SqlServer, idOrName, ct),
            config.SqlServer.UseCompression),
        "folder" => _folders.EstimateFolderSize(config.Folders.First(f => f.Id == idOrName)),
        "file" => _files.EstimateFileSize(config.Files.First(f => f.Id == idOrName)),
        _ => 1
    };
}
