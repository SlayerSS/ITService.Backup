namespace ITService.Backup.Services;

public sealed class BackupDestinationStatus
{
    public bool IsReady { get; init; }
    public bool UseUsbTarget { get; init; }
    public string Message { get; init; } = "";
    /// <summary>Базовый каталог архивов с подпапкой из настроек (например E:\Backups).</summary>
    public string BackupBasePath { get; init; } = "";
    public long FreeBytes { get; init; }
    public string FreeSpaceText { get; init; } = "";
    public string TotalSpaceText { get; init; } = "";
}
