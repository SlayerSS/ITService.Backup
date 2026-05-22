using System.Text.Json;
using ITService.Backup.Models;

namespace ITService.Backup.Services;

public sealed class BackupOrchestrator
{
    private readonly ConfigService _configService = new();
    private readonly HistoryService _historyService = new();
    private readonly BackupDestinationService _destinationService = new();
    private readonly ArchiveFolderService _archiveFolder = new();
    private readonly SqlBackupService _sqlService = new();
    private readonly FolderBackupService _folderService = new();
    private readonly FileBackupService _fileService = new();
    private readonly SourceDriveGuard _sourceGuard = new();
    private readonly BackupSizeEstimateService _sizeEstimate = new();

    public async Task<BackupResult> RunAsync(
        AppConfig config,
        IReadOnlyList<string> selectedDatabases,
        IReadOnlyList<string> selectedFolderIds,
        IReadOnlyList<string> selectedFileIds,
        IProgress<BackupProgressReport>? progress = null,
        string? archivePathOverride = null,
        CancellationToken ct = default)
    {
        var items = BuildItemLabels(config, selectedDatabases, selectedFolderIds, selectedFileIds);
        if (items.Count == 0)
            return BackupResult.Fail("Выберите хотя бы один элемент для копирования.");

        var destStatus = _destinationService.GetStatus(config);
        if (!destStatus.IsReady)
        {
            _historyService.RecordFailure(destStatus.Message, items);
            return BackupResult.Fail(destStatus.Message);
        }

        var backupBasePath = destStatus.BackupBasePath;
        var tracker = new BackupProgressTracker();
        using var vss = new ShadowCopyService();
        string? archivePath = null;
        var archiveCommitted = false;
        BackupSizeEstimate? estimate = null;

        try
        {
            progress?.Report(tracker.Snapshot());
            tracker.SetSubProgress(0, "Оценка размера и проверка места...");
            progress?.Report(tracker.Snapshot());

            var forbiddenDriveRoots = destStatus.UseUsbTarget
                ? await _sourceGuard.CollectForbiddenDriveRootsAsync(
                    config, selectedDatabases, selectedFolderIds, selectedFileIds, ct)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var forbiddenSourcePaths = destStatus.UseUsbTarget
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : await _sourceGuard.CollectForbiddenSourcePathsAsync(
                    config, selectedDatabases, selectedFolderIds, selectedFileIds, ct);

            var writeContext = new BackupWriteContext
            {
                ApprovedTargetBase = backupBasePath,
                ForbiddenDriveRoots = forbiddenDriveRoots,
                ForbiddenSourcePaths = forbiddenSourcePaths,
                UseUsbTarget = destStatus.UseUsbTarget
            };

            SourceDriveGuard.EnsureWriteAllowed(backupBasePath, writeContext);

            estimate = await _sizeEstimate.EstimateAsync(
                config, selectedDatabases, selectedFolderIds, selectedFileIds, ct);

            foreach (var db in selectedDatabases)
            {
                var w = await _sizeEstimate.GetStepWeightAsync(config, "sql", db, ct);
                tracker.PlanStep($"SQL: {db}", w);
            }

            foreach (var id in selectedFolderIds)
            {
                var folder = config.Folders.First(f => f.Id == id);
                tracker.PlanStep($"Папка: {folder.DisplayName}", _folderService.EstimateFolderSize(folder));
            }

            foreach (var id in selectedFileIds)
            {
                var file = config.Files.First(f => f.Id == id);
                tracker.PlanStep($"Файл: {file.DisplayName}", _fileService.EstimateFileSize(file));
            }

            var spaceError = TryGetInsufficientSpaceMessage(destStatus, estimate.RequiredBytes);
            if (spaceError != null)
            {
                _historyService.RecordFailure(spaceError, items);
                progress?.Report(tracker.Finished(false, spaceError));
                return BackupResult.Fail(spaceError);
            }

            tracker.SetSubProgress(0, "Проверка дисков источников...");
            progress?.Report(tracker.Snapshot());

            if (!string.IsNullOrWhiteSpace(archivePathOverride))
            {
                archivePath = _archiveFolder.CreateStructure(archivePathOverride);
            }
            else
            {
                var plan = _archiveFolder.PlanFromBasePath(backupBasePath, config.ArchiveNameFormat);
                if (plan.AlreadyExists)
                {
                    var msg =
                        $"Папка архива уже существует:\n{plan.Path}\n\n" +
                        "Бэкап остановлен, чтобы не перезаписать существующие данные.";
                    _historyService.RecordFailure(msg, items);
                    tracker.SetSubProgress(0, msg);
                    progress?.Report(tracker.Snapshot());
                    return BackupResult.FailConflict(msg, plan.AlternatePathWithTimestamp);
                }

                Directory.CreateDirectory(backupBasePath);
                archivePath = _archiveFolder.CreateStructure(plan.Path);
            }

            LogService.BeginArchiveSession(archivePath);
            BackupSafety.ValidateArchiveRoot(archivePath, backupBasePath);
            SourceDriveGuard.EnsureWriteAllowed(archivePath, writeContext);

            var manifest = new BackupManifest
            {
                CreatedAt = DateTime.Now,
                Server = config.SqlServer.Instance,
                AppVersion = "1.0.0"
            };

            var stepIndex = 0;
            if (selectedDatabases.Count > 0)
                ArchiveSessionPaths.EnsureSqlDir(archivePath);

            foreach (var dbName in selectedDatabases)
            {
                ct.ThrowIfCancellationRequested();
                var stepBytes = await _sizeEstimate.GetStepWeightAsync(config, "sql", dbName, ct);
                EnsureSpaceForStep(destStatus, backupBasePath, stepBytes);

                var sqlDir = ArchiveSessionPaths.SqlDir(archivePath);
                var bakPath = Path.Combine(sqlDir, dbName + ".bak");
                await _sqlService.BackupDatabaseAsync(
                    config.SqlServer, dbName, bakPath, sqlDir, archivePath, writeContext,
                    tracker, stepIndex++, progress, ct);
                manifest.Databases.Add(new ManifestDatabase
                {
                    Name = dbName,
                    File = Path.Combine("SQL", dbName + ".bak"),
                    SizeBytes = new FileInfo(bakPath).Length
                });
            }

            if (selectedFolderIds.Count > 0)
                ArchiveSessionPaths.EnsureFoldersDir(archivePath);

            foreach (var folderId in selectedFolderIds)
            {
                ct.ThrowIfCancellationRequested();
                var folder = config.Folders.First(f => f.Id == folderId);
                var stepBytes = _folderService.EstimateFolderSize(folder);
                EnsureSpaceForStep(destStatus, backupBasePath, stepBytes);

                var foldersDir = ArchiveSessionPaths.FoldersDir(archivePath);
                vss.TryCreateForPath(folder.Path);
                await _folderService.BackupFolderAsync(
                    folder, foldersDir, writeContext, vss,
                    tracker, stepIndex++, progress, ct);

                string fileOrPath;
                long size;
                if (folder.Archive)
                {
                    var zip = Path.Combine(foldersDir, Sanitize(folder.DisplayName) + ".zip");
                    fileOrPath = Path.Combine("Folders", Path.GetFileName(zip));
                    size = new FileInfo(zip).Length;
                }
                else
                {
                    var dir = Path.Combine(foldersDir, Sanitize(folder.DisplayName));
                    fileOrPath = Path.Combine("Folders", Path.GetFileName(dir));
                    size = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                }

                manifest.Folders.Add(new ManifestFolder
                {
                    Name = folder.DisplayName,
                    Mode = folder.Archive ? "zip" : "copy",
                    FileOrPath = fileOrPath,
                    Has1CDatabase = folder.Has1CDatabase,
                    CopyOnly1cdBody = folder.CopyOnly1cdBody,
                    SizeBytes = size
                });
            }

            foreach (var fileId in selectedFileIds)
            {
                ct.ThrowIfCancellationRequested();
                var file = config.Files.First(f => f.Id == fileId);
                var stepBytes = _fileService.EstimateFileSize(file);
                EnsureSpaceForStep(destStatus, backupBasePath, stepBytes);

                var source = FileBackupService.ResolveSourcePath(file);
                vss.TryCreateForPath(source);
                await _fileService.BackupFileAsync(
                    file, archivePath, writeContext, vss,
                    tracker, stepIndex++, progress, ct);

                var destName = Sanitize(file.DisplayName) + Path.GetExtension(source);
                var destPath = Path.Combine(ArchiveSessionPaths.FilesDir(archivePath), destName);
                manifest.Files.Add(new ManifestFile
                {
                    Name = file.DisplayName,
                    SourcePath = source,
                    File = Path.Combine("Files", destName),
                    Is1CDatabase = file.Is1CDatabase,
                    CopyOnly1cdBody = file.CopyOnly1cdBody,
                    SizeBytes = new FileInfo(destPath).Length
                });
            }

            tracker.SetSubProgress(100, "Сохранение manifest.json...");
            progress?.Report(tracker.Snapshot());

            var manifestPath = Path.Combine(archivePath, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), ct);

            ArchiveSessionPaths.TryRemoveTempDirectory(archivePath);

            var totalSize = Directory.EnumerateFiles(archivePath, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            _historyService.RecordSuccess(archivePath, totalSize, items);
            archiveCommitted = true;

            if (config.Ui.RememberLastSelection)
            {
                _configService.SaveSelection(new UserSelection
                {
                    Sql = selectedDatabases.ToList(),
                    Folders = selectedFolderIds.ToList(),
                    Files = selectedFileIds.ToList()
                });
            }

            progress?.Report(tracker.Finished(true, "Резервная копия создана"));
            return BackupResult.Ok(archivePath);
        }
        catch (OperationCanceledException)
        {
            return HandleUserCancellation(archivePath, archiveCommitted, items, tracker, progress);
        }
        catch (Exception ex) when (BackupOrchestrator.IsUserCancellation(ex))
        {
            return HandleUserCancellation(archivePath, archiveCommitted, items, tracker, progress);
        }
        catch (Exception ex)
        {
            LogService.Error(ex.ToString());
            var freeNow = BackupSpaceHelper.GetAvailableBytes(backupBasePath);
            var diskFull = BackupSpaceHelper.IsDiskFullException(ex);

            if (!archiveCommitted && archivePath != null)
            {
                LogService.EndArchiveSession();
                BackupSpaceHelper.TryRemoveIncompleteArchive(archivePath);
            }

            var userMessage = diskFull
                ? BackupSpaceHelper.BuildInsufficientSpaceMessage(
                    estimate?.RequiredBytes ?? BackupSpaceHelper.ApplySafetyMargin(1),
                    freeNow,
                    destStatus.UseUsbTarget,
                    duringBackup: true) +
                  "\n\nНеполная папка архива удалена (если удалось)."
                : ex.Message;

            _historyService.RecordFailure(userMessage, items);
            progress?.Report(tracker.Finished(false, userMessage));
            var logDir = LogService.SessionLogDirectory
                         ?? (archivePath != null && Directory.Exists(ArchiveSessionPaths.LogsDir(archivePath))
                             ? ArchiveSessionPaths.LogsDir(archivePath)
                             : AppPaths.DataDirectory);
            return BackupResult.Fail(
                $"{userMessage}\n\nПодробности в папке логов:\n{logDir}",
                logDir);
        }
        finally
        {
            ArchiveSessionPaths.TryRemoveTempDirectory(archivePath);
            LogService.EndArchiveSession();
        }
    }

    public Task<BackupSizeEstimate> EstimateSelectionAsync(
        AppConfig config,
        IReadOnlyList<string> selectedDatabases,
        IReadOnlyList<string> selectedFolderIds,
        IReadOnlyList<string> selectedFileIds,
        CancellationToken ct = default) =>
        _sizeEstimate.EstimateAsync(config, selectedDatabases, selectedFolderIds, selectedFileIds, ct);

    public static string? TryGetInsufficientSpaceMessage(BackupDestinationStatus dest, long requiredBytes)
    {
        if (requiredBytes <= 0)
            return null;

        var free = dest.FreeBytes > 0
            ? dest.FreeBytes
            : BackupSpaceHelper.GetAvailableBytes(dest.BackupBasePath);

        if (free <= 0)
            return null;

        if (free < requiredBytes)
            return BackupSpaceHelper.BuildInsufficientSpaceMessage(requiredBytes, free, dest.UseUsbTarget);

        return null;
    }

    private static void EnsureSpaceForStep(BackupDestinationStatus dest, string volumePath, long stepEstimatedBytes)
    {
        if (stepEstimatedBytes <= 0)
            return;

        var free = BackupSpaceHelper.GetAvailableBytes(volumePath);
        if (free <= 0)
            return;

        var need = BackupSpaceHelper.ApplySafetyMargin(stepEstimatedBytes);
        if (free < need)
            throw new IOException(
                BackupSpaceHelper.BuildInsufficientSpaceMessage(need, free, dest.UseUsbTarget, duringBackup: true));
    }

    private static List<string> BuildItemLabels(
        AppConfig config,
        IReadOnlyList<string> selectedDatabases,
        IReadOnlyList<string> selectedFolderIds,
        IReadOnlyList<string> selectedFileIds)
    {
        var items = new List<string>();
        foreach (var db in selectedDatabases)
            items.Add($"SQL:{db}");
        foreach (var id in selectedFolderIds)
        {
            var folder = config.Folders.FirstOrDefault(f => f.Id == id);
            if (folder != null)
                items.Add($"Folder:{folder.DisplayName}");
        }
        foreach (var id in selectedFileIds)
        {
            var file = config.Files.FirstOrDefault(f => f.Id == id);
            if (file != null)
                items.Add($"File:{file.DisplayName}");
        }
        return items;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "folder" : name.Trim();
    }

    private BackupResult HandleUserCancellation(
        string? archivePath,
        bool archiveCommitted,
        IReadOnlyList<string> items,
        BackupProgressTracker tracker,
        IProgress<BackupProgressReport>? progress)
    {
        try
        {
            ArchiveSessionPaths.TryRemoveTempDirectory(archivePath);

            if (!archiveCommitted && archivePath != null && Directory.Exists(archivePath))
                LogService.Info("Резервное копирование отменено пользователем. Папка архива сохранена.");
            else
                LogService.InfoGlobal("Резервное копирование отменено пользователем.");
        }
        catch (Exception logEx)
        {
            LogService.WarnGlobal($"Отмена: {logEx.Message}");
        }
        finally
        {
            LogService.EndArchiveSession();
        }

        var userMessage = BuildCancelledMessage(archivePath, archiveCommitted);
        _historyService.RecordFailure(userMessage, items);
        progress?.Report(tracker.Finished(false, userMessage));
        var logDir = archivePath != null && Directory.Exists(archivePath)
            ? ArchiveSessionPaths.LogsDir(archivePath)
            : AppPaths.DataDirectory;
        return BackupResult.Cancelled(userMessage, logDir);
    }

    private static string BuildCancelledMessage(string? archivePath, bool archiveCommitted)
    {
        if (archiveCommitted || string.IsNullOrWhiteSpace(archivePath) || !Directory.Exists(archivePath))
            return "Резервное копирование отменено.";

        return "Резервное копирование отменено.\n\n" +
               "Уже скопированные файлы оставлены в папке:\n" +
               archivePath;
    }

    private static bool IsUserCancellation(Exception ex)
    {
        if (ex is OperationCanceledException)
            return true;

        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is Microsoft.Data.SqlClient.SqlException sql && IsSqlUserCancel(sql))
                return true;
        }

        return false;
    }

    private static bool IsSqlUserCancel(Microsoft.Data.SqlClient.SqlException ex) =>
        ex.Number == 3204
        || ex.Message.Contains("отменена пользователем", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("cancelled by user", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("canceled by user", StringComparison.OrdinalIgnoreCase);
}

public sealed class BackupResult
{
    public bool IsSuccess { get; init; }
    public bool IsCancelled { get; init; }
    public bool IsFolderConflict { get; init; }
    public string? AlternateArchivePath { get; init; }
    public string? LogFolderPath { get; init; }
    public string Message { get; init; } = "";

    public static BackupResult Ok(string path) => new() { IsSuccess = true, Message = path };

    public static BackupResult Fail(string message, string? logFolder = null) => new()
    {
        IsSuccess = false,
        Message = message,
        LogFolderPath = logFolder ?? AppPaths.DataDirectory
    };

    public static BackupResult Cancelled(string message, string? logFolder = null) => new()
    {
        IsSuccess = false,
        IsCancelled = true,
        Message = message,
        LogFolderPath = logFolder ?? AppPaths.DataDirectory
    };

    public static BackupResult FailConflict(string message, string? alternatePath) => new()
    {
        IsSuccess = false,
        IsFolderConflict = true,
        Message = message,
        AlternateArchivePath = alternatePath,
        LogFolderPath = AppPaths.DataDirectory
    };
}
