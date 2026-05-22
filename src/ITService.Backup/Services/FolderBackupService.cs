using System.IO.Compression;
using ITService.Backup.Models;

namespace ITService.Backup.Services;

public sealed class FolderBackupService
{
    public long EstimateFolderSize(FolderEntry folder)
    {
        long total = 0;
        foreach (var file in GetSourceFiles(folder))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // пропускаем недоступные файлы
            }
        }

        // ZIP обычно меньше сырого объёма; для проверки места берём полный размер (консервативно)
        return total;
    }

    public async Task BackupFolderAsync(
        FolderEntry folder,
        string destinationRoot,
        BackupWriteContext writeContext,
        ShadowCopyService? vss,
        BackupProgressTracker tracker,
        int stepIndex,
        IProgress<BackupProgressReport>? progress,
        CancellationToken ct = default)
    {
        BackupSafety.ValidateFolderSource(folder.Path);
        SourceDriveGuard.EnsureWriteAllowed(destinationRoot, writeContext);

        var safeName = SanitizeName(folder.DisplayName);
        var label = folder.Has1CDatabase ? $"Папка 1С: {folder.DisplayName}" : $"Папка: {folder.DisplayName}";
        tracker.BeginStep(stepIndex, label);

        if (folder.Archive)
        {
            var zipPath = Path.Combine(destinationRoot, safeName + ".zip");
            SourceDriveGuard.EnsureWriteAllowed(zipPath, writeContext);
            BackupSafety.ValidateFolderDestination(zipPath, destinationRoot, folder.Path, writeContext.ApprovedTargetBase);
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            await Task.Run(() => CreateZipWithProgress(folder, zipPath, vss, tracker, progress, ct), ct);
            LogService.Info($"Folder zipped: {folder.Path} -> {zipPath}");
        }
        else
        {
            var destDir = Path.Combine(destinationRoot, safeName);
            SourceDriveGuard.EnsureWriteAllowed(destDir, writeContext);
            BackupSafety.ValidateFolderDestination(destDir, destinationRoot, folder.Path, writeContext.ApprovedTargetBase);
            if (Directory.Exists(destDir))
                Directory.Delete(destDir, true);

            await Task.Run(() => CopyWithProgress(folder, destDir, vss, tracker, progress, ct), ct);
            LogService.Info($"Folder copied: {folder.Path} -> {destDir}");
        }

        tracker.EndStep();
        progress?.Report(tracker.Snapshot());
    }

    private static IReadOnlyList<string> GetSourceFiles(FolderEntry folder)
    {
        if (folder.Has1CDatabase && folder.CopyOnly1cdBody)
            return [FileBackupService.Resolve1cdBodyPath(folder.Path)];

        if (!Directory.Exists(folder.Path))
            return [];

        return Directory.EnumerateFiles(folder.Path, "*", SearchOption.AllDirectories).ToList();
    }

    private static void CreateZipWithProgress(
        FolderEntry folder,
        string zipPath,
        ShadowCopyService? vss,
        BackupProgressTracker tracker,
        IProgress<BackupProgressReport>? progress,
        CancellationToken ct)
    {
        var files = GetSourceFiles(folder);
        var total = Math.Max(files.Count, 1);
        var basePath = folder.Has1CDatabase && folder.CopyOnly1cdBody
            ? Path.GetDirectoryName(files[0]) ?? folder.Path
            : folder.Path;

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            var relative = folder.Has1CDatabase && folder.CopyOnly1cdBody
                ? Path.GetFileName(file)
                : Path.GetRelativePath(basePath, file);
            var entry = zip.CreateEntry(relative.Replace('\\', '/'), MapCompression(folder.CompressionLevel));

            using var entryStream = entry.Open();
            CopyFileToStream(file, entryStream, vss);

            var pct = (i + 1) * 100 / total;
            tracker.SetSubProgress(pct, $"Архив ZIP: {folder.DisplayName} ({pct}%)");
            progress?.Report(tracker.Snapshot());
        }
    }

    private static void CopyWithProgress(
        FolderEntry folder,
        string destination,
        ShadowCopyService? vss,
        BackupProgressTracker tracker,
        IProgress<BackupProgressReport>? progress,
        CancellationToken ct)
    {
        var files = GetSourceFiles(folder);
        var total = Math.Max(files.Count, 1);

        if (folder.Has1CDatabase && folder.CopyOnly1cdBody && files.Count == 1)
        {
            Directory.CreateDirectory(destination);
            var destFile = Path.Combine(destination, Path.GetFileName(files[0]));
            LockedFileCopier.CopyFile(files[0], destFile, vss);
            tracker.SetSubProgress(100, "Копирование .1CD — готово");
            progress?.Report(tracker.Snapshot());
            return;
        }

        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            var relative = Path.GetRelativePath(folder.Path, file);
            var dest = Path.Combine(destination, relative);
            LockedFileCopier.CopyFile(file, dest, vss);

            var pct = (i + 1) * 100 / total;
            tracker.SetSubProgress(pct, $"Копирование файлов ({pct}%)");
            progress?.Report(tracker.Snapshot());
        }
    }

    private static void CopyFileToStream(string sourceFile, Stream destination, ShadowCopyService? vss)
    {
        if (TryStreamFile(sourceFile, destination))
            return;

        if (vss != null && vss.TryCreateForPath(sourceFile))
        {
            var shadowPath = vss.ResolveReadPath(sourceFile);
            if (TryStreamFile(shadowPath, destination))
                return;
        }

        throw new IOException($"Не удалось прочитать файл: {sourceFile}");
    }

    private static bool TryStreamFile(string path, Stream destination)
    {
        if (!File.Exists(path))
            return false;
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            input.CopyTo(destination);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static CompressionLevel MapCompression(string level) => level.ToLowerInvariant() switch
    {
        "fast" or "fastest" => CompressionLevel.Fastest,
        "smallest" or "max" => CompressionLevel.SmallestSize,
        _ => CompressionLevel.Optimal
    };

    private static string SanitizeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "folder" : name.Trim();
    }
}
