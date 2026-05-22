namespace ITService.Backup.Models;

public sealed class OneCConfigSyncResult
{
    public bool ConfigChanged { get; init; }
    public int NewFolders { get; init; }
    public IReadOnlyList<string> NewFolderIds { get; init; } = [];
    public int FilePathCount { get; init; }
    public string Message { get; init; } = "";
}
