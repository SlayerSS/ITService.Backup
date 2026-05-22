using ITService.Backup.Models;

namespace ITService.Backup.Services;

public sealed class FileBackupService
{
    public long EstimateFileSize(FileEntry file)
    {
        var source = ResolveSourcePath(file);
        return new FileInfo(source).Length;
    }

    public async Task BackupFileAsync(
        FileEntry file,
        string archivePath,
        BackupWriteContext writeContext,
        ShadowCopyService? vss,
        BackupProgressTracker tracker,
        int stepIndex,
        IProgress<BackupProgressReport>? progress,
        CancellationToken ct = default)
    {
        var source = ResolveSourcePath(file);
        BackupSafety.ValidateFileSource(source);
        SourceDriveGuard.EnsureWriteAllowed(archivePath, writeContext);

        ArchiveSessionPaths.EnsureFilesDir(archivePath);
        ArchiveSessionPaths.EnsureTempDir(archivePath);
        var filesDir = ArchiveSessionPaths.FilesDir(archivePath);
        var tempDir = ArchiveSessionPaths.TempDir(archivePath);

        var fileName = Path.GetFileName(source);
        var destPath = Path.Combine(filesDir, SanitizeName(file.DisplayName) + Path.GetExtension(fileName));
        var tempPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + Path.GetExtension(fileName) + ".part");

        SourceDriveGuard.EnsureWriteAllowed(destPath, writeContext);
        SourceDriveGuard.EnsureWriteAllowed(tempPath, writeContext);

        tracker.BeginStep(stepIndex, $"Файл: {file.DisplayName}");
        progress?.Report(tracker.Snapshot());

        await Task.Run(() =>
        {
            if (File.Exists(destPath))
                File.Delete(destPath);
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            CopyWithProgress(source, tempPath, destPath, vss, file.DisplayName, tracker, progress);
        }, ct);

        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            // ignore cleanup
        }

        tracker.EndStep();
        progress?.Report(tracker.Snapshot());
        LogService.Info($"File backup: {source} -> {destPath}");
    }

    public static string ResolveSourcePath(FileEntry file)
    {
        if (string.IsNullOrWhiteSpace(file.Path))
            throw new InvalidOperationException("Не указан путь к файлу.");

        var path = Path.GetFullPath(file.Path.Trim());

        if (file.Is1CDatabase && file.CopyOnly1cdBody)
            return Resolve1cdBodyPath(path);

        if (File.Exists(path))
            return path;

        throw new FileNotFoundException($"Файл не найден: {path}");
    }

    public static string Resolve1cdBodyPath(string path)
    {
        if (File.Exists(path))
        {
            if (!path.EndsWith(".1cd", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Для режима «только .1CD» укажите файл .1CD или каталог с ним.");
            return path;
        }

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Путь не найден: {path}");

        var candidates = Directory.GetFiles(path, "*.1cd", SearchOption.TopDirectoryOnly);
        if (candidates.Length == 0)
            throw new FileNotFoundException($"В каталоге нет файла .1CD: {path}");

        if (candidates.Length == 1)
            return candidates[0];

        return candidates
            .OrderByDescending(f => new FileInfo(f).Length)
            .First();
    }

    private static void CopyWithProgress(
        string source,
        string tempPath,
        string destPath,
        ShadowCopyService? vss,
        string displayName,
        BackupProgressTracker tracker,
        IProgress<BackupProgressReport>? progress)
    {
        var total = new FileInfo(source).Length;
        if (total <= 0)
            total = 1;

        using var input = OpenReadStream(source, vss);
        using var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[1024 * 1024];
        long copied = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            copied += read;
            var pct = (int)Math.Min(99, copied * 100 / total);
            tracker.SetSubProgress(pct, $"Файл: {displayName} — {pct}%");
            progress?.Report(tracker.Snapshot());
        }

        output.Flush();
        output.Close();
        input.Close();

        if (File.Exists(destPath))
            File.Delete(destPath);
        File.Move(tempPath, destPath);

        tracker.SetSubProgress(100, $"Файл: {displayName} — готово");
        progress?.Report(tracker.Snapshot());
    }

    private static Stream OpenReadStream(string source, ShadowCopyService? vss)
    {
        try
        {
            return new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch when (vss != null)
        {
            if (!vss.TryCreateForPath(source))
                throw;
            var shadow = vss.ResolveReadPath(source);
            return new FileStream(shadow, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
    }

    private static string SanitizeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "file" : name.Trim();
    }
}
