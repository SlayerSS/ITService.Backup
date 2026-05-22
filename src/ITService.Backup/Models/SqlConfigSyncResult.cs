namespace ITService.Backup.Models;

public sealed class SqlConfigSyncResult
{
    public bool Success { get; init; }
    public bool ConfigChanged { get; init; }
    public bool InstanceAutoDetected { get; init; }
    public int DatabaseCount { get; init; }
    public int NewDatabases { get; init; }
    public string Message { get; init; } = "";
}
