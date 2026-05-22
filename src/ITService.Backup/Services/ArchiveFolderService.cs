using ITService.Backup.Models;

namespace ITService.Backup.Services;

public sealed class ArchiveFolderPlan
{
    public required string Path { get; init; }
    public bool AlreadyExists { get; init; }
    public bool HasExistingData { get; init; }
    public string? AlternatePathWithTimestamp { get; init; }
}

public sealed class ArchiveFolderService
{
    public ArchiveFolderPlan Plan(UsbDriveStatus drive, UsbSettings usb, string archiveNameFormat) =>
        PlanFromBasePath(Path.Combine(drive.RootPath, usb.RootFolder), archiveNameFormat);

    public ArchiveFolderPlan PlanFromBasePath(string backupBasePath, string archiveNameFormat)
    {
        var basePath = Path.GetFullPath(backupBasePath);
        var name = DateTime.Now.ToString(archiveNameFormat);
        var path = Path.GetFullPath(Path.Combine(basePath, name));
        var exists = Directory.Exists(path);
        var hasData = exists && DirectoryHasBackupData(path);

        string? alternate = null;
        if (exists)
        {
            alternate = Path.GetFullPath(Path.Combine(
                basePath,
                DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")));
        }

        return new ArchiveFolderPlan
        {
            Path = path,
            AlreadyExists = exists,
            HasExistingData = hasData,
            AlternatePathWithTimestamp = alternate
        };
    }

    public string CreateStructure(string archivePath)
    {
        archivePath = Path.GetFullPath(archivePath);
        if (Directory.Exists(archivePath))
            throw new InvalidOperationException(
                $"Папка «{archivePath}» уже существует. Бэкап не выполнен, чтобы не перезаписать данные.");

        Directory.CreateDirectory(archivePath);
        return archivePath;
    }

    private static bool DirectoryHasBackupData(string path)
    {
        try
        {
            if (File.Exists(Path.Combine(path, "manifest.json")))
                return true;

            foreach (var dir in new[] { "SQL", "Folders", "Files", "_temp", "logs" })
            {
                var sub = Path.Combine(path, dir);
                if (!Directory.Exists(sub))
                    continue;
                if (Directory.EnumerateFileSystemEntries(sub).Any())
                    return true;
            }

            return Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch
        {
            return true;
        }
    }
}
