namespace ITService.Backup.Services;

public sealed class UsbDriveStatus
{
    public bool IsReady { get; init; }
    public string DriveLetter { get; init; } = "";
    public string RootPath { get; init; } = "";
    public string? VolumeLabel { get; init; }
    public long FreeBytes { get; init; }
    public long TotalBytes { get; init; }
    public bool AutoDetected { get; init; }
    /// <summary>Буква зафиксирована в настройках — не переключаться на другие диски.</summary>
    public bool IsManualSelection { get; init; }
    public string Message { get; init; } = "";

    public string FreeSpaceText => UsbService.FormatBytes(FreeBytes);
    public string TotalSpaceText => UsbService.FormatBytes(TotalBytes);
}
