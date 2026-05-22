namespace ITService.Backup.Services;

public sealed class BackupWriteContext
{
    public required string ApprovedTargetBase { get; init; }
    public required HashSet<string> ForbiddenDriveRoots { get; init; }
    public required HashSet<string> ForbiddenSourcePaths { get; init; }
    public bool UseUsbTarget { get; init; }
}
