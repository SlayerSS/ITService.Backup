using ITService.Backup.Models;

namespace ITService.Backup.Services;

/// <summary>Защита дисков и путей источников от перезаписи при бэкапе.</summary>
public sealed class SourceDriveGuard
{
    private readonly SqlBackupService _sql = new();

    public async Task<HashSet<string>> CollectForbiddenDriveRootsAsync(
        AppConfig config,
        IReadOnlyList<string> selectedDatabases,
        IReadOnlyList<string> selectedFolderIds,
        IReadOnlyList<string> selectedFileIds,
        CancellationToken ct = default)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in selectedFolderIds)
        {
            var folder = config.Folders.FirstOrDefault(f => f.Id == id);
            if (folder == null || string.IsNullOrWhiteSpace(folder.Path))
                continue;
            AddDriveRoot(roots, folder.Path);
        }

        foreach (var id in selectedFileIds)
        {
            var file = config.Files.FirstOrDefault(f => f.Id == id);
            if (file == null || string.IsNullOrWhiteSpace(file.Path))
                continue;
            try
            {
                var source = FileBackupService.ResolveSourcePath(file);
                AddDriveRoot(roots, source);
            }
            catch
            {
                AddDriveRoot(roots, file.Path);
            }
        }

        foreach (var db in selectedDatabases)
        {
            var dbRoots = await _sql.GetDatabaseFileDriveRootsAsync(config.SqlServer, db, ct);
            foreach (var r in dbRoots)
                roots.Add(r);
        }

        return roots;
    }

    public async Task<HashSet<string>> CollectForbiddenSourcePathsAsync(
        AppConfig config,
        IReadOnlyList<string> selectedDatabases,
        IReadOnlyList<string> selectedFolderIds,
        IReadOnlyList<string> selectedFileIds,
        CancellationToken ct = default)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in selectedFolderIds)
        {
            var folder = config.Folders.FirstOrDefault(f => f.Id == id);
            if (folder == null || string.IsNullOrWhiteSpace(folder.Path))
                continue;
            AddSourcePath(paths, folder.Path, isFile: false);
        }

        foreach (var id in selectedFileIds)
        {
            var file = config.Files.FirstOrDefault(f => f.Id == id);
            if (file == null || string.IsNullOrWhiteSpace(file.Path))
                continue;
            try
            {
                var source = FileBackupService.ResolveSourcePath(file);
                AddSourcePath(paths, source, isFile: true);
            }
            catch
            {
                AddSourcePath(paths, file.Path, isFile: true);
            }
        }

        foreach (var db in selectedDatabases)
        {
            var dbPaths = await _sql.GetDatabasePhysicalPathsAsync(config.SqlServer, db, ct);
            foreach (var p in dbPaths)
                AddSourcePath(paths, p, isFile: true);
        }

        return paths;
    }

    public static void EnsureWriteAllowed(string writePath, BackupWriteContext ctx)
    {
        var full = Path.GetFullPath(writePath);
        BackupSafety.EnsureUnderTargetBase(full, ctx.ApprovedTargetBase);

        if (ctx.UseUsbTarget)
        {
            var writeRoot = Path.GetFullPath(Path.GetPathRoot(full) ?? "");
            if (BackupSafety.IsSystemDrive(writeRoot))
                throw new InvalidOperationException("Запись на системный диск C: запрещена.");

            foreach (var forbidden in ctx.ForbiddenDriveRoots)
            {
                if (string.IsNullOrWhiteSpace(forbidden))
                    continue;

                if (writeRoot.Equals(forbidden, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Запись на диск {GetDriveLetter(writeRoot)}: запрещена — на нём расположены исходные данные. " +
                        "Используйте съёмный USB или укажите папку на другом диске в настройках.");
            }
        }
        else
        {
            foreach (var source in ctx.ForbiddenSourcePaths)
            {
                if (string.IsNullOrWhiteSpace(source))
                    continue;

                if (PathsOverlap(source, full))
                    throw new InvalidOperationException(
                        "Папка архива не может находиться внутри исходных данных или совпадать с ними. " +
                        "Выберите другую папку для резервных копий.");
            }
        }
    }

    private static void AddDriveRoot(HashSet<string> roots, string path)
    {
        var root = Path.GetFullPath(Path.GetPathRoot(path) ?? "");
        if (!string.IsNullOrEmpty(root))
            roots.Add(root);
    }

    private static void AddSourcePath(HashSet<string> paths, string path, bool isFile)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (isFile && File.Exists(full))
                paths.Add(full);
            else if (Directory.Exists(full))
                paths.Add(full.TrimEnd('\\') + "\\");
            else if (isFile)
            {
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    paths.Add(Path.GetFullPath(dir).TrimEnd('\\') + "\\");
            }
            else
                paths.Add(full.TrimEnd('\\') + "\\");
        }
        catch
        {
            // ignore invalid paths
        }
    }

    private static bool PathsOverlap(string a, string b)
    {
        a = NormalizeDir(a);
        b = NormalizeDir(b);
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase)
               || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDir(string path)
    {
        path = Path.GetFullPath(path);
        if (File.Exists(path))
            path = Path.GetDirectoryName(path) ?? path;
        if (!path.EndsWith(Path.DirectorySeparatorChar))
            path += Path.DirectorySeparatorChar;
        return path;
    }

    private static string GetDriveLetter(string root) =>
        root.TrimEnd('\\').TrimEnd(':');
}
