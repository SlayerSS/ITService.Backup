namespace ITService.Backup.Models;

/// <summary>Оценка объёма будущего архива до и во время бэкапа.</summary>
public sealed class BackupSizeEstimate
{
    public long SqlBytes { get; init; }
    public long FolderBytes { get; init; }
    public long FileBytes { get; init; }
    public long TotalBytes { get; init; }
    /// <summary>С запасом (× <see cref="BackupSpaceHelper.SafetyMargin"/>).</summary>
    public long RequiredBytes { get; init; }
    public int SqlCount { get; init; }
    public int FolderCount { get; init; }
    public int FileCount { get; init; }

    public bool HasSelection => SqlCount + FolderCount + FileCount > 0;
}
