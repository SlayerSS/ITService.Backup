using ITService.Backup.Models;

namespace ITService.Backup.Services;

public sealed class BackupDestinationService
{
    private readonly UsbService _usbService = new();

    public BackupDestinationStatus GetStatus(AppConfig config)
    {
        config.BackupTarget ??= new BackupTargetSettings();
        config.Usb ??= new UsbSettings();
        var subfolder = NormalizeSubfolder(config.Usb.RootFolder);
        if (config.BackupTarget.UseUsbTarget)
            return GetUsbStatus(config, subfolder);

        return GetLocalStatus(config, subfolder);
    }

    private BackupDestinationStatus GetUsbStatus(AppConfig config, string subfolder)
    {
        var removableOnly = config.BackupTarget.RemovableOnly;
        var drive = _usbService.GetDriveStatus(config.Usb, removableOnly);
        if (!drive.IsReady)
        {
            return new BackupDestinationStatus
            {
                UseUsbTarget = true,
                IsReady = false,
                Message = drive.Message
            };
        }

        var backupBase = Path.GetFullPath(Path.Combine(drive.RootPath, subfolder));
        return new BackupDestinationStatus
        {
            UseUsbTarget = true,
            IsReady = true,
            Message = drive.Message,
            BackupBasePath = backupBase,
            FreeBytes = drive.FreeBytes,
            FreeSpaceText = drive.FreeSpaceText,
            TotalSpaceText = drive.TotalSpaceText
        };
    }

    private static BackupDestinationStatus GetLocalStatus(AppConfig config, string subfolder)
    {
        var local = config.BackupTarget.LocalFolderPath?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(local))
        {
            return new BackupDestinationStatus
            {
                UseUsbTarget = false,
                IsReady = false,
                Message = "Укажите папку для резервных копий в настройках."
            };
        }

        try
        {
            local = Path.GetFullPath(local);
        }
        catch (Exception ex)
        {
            return new BackupDestinationStatus
            {
                UseUsbTarget = false,
                IsReady = false,
                Message = $"Некорректный путь: {ex.Message}"
            };
        }

        if (!Directory.Exists(local))
        {
            return new BackupDestinationStatus
            {
                UseUsbTarget = false,
                IsReady = false,
                Message = $"Папка не найдена:\n{local}"
            };
        }

        var backupBase = Path.GetFullPath(Path.Combine(local, subfolder));
        long free = 0, total = 0;
        try
        {
            var root = Path.GetPathRoot(backupBase) ?? "";
            var drive = new DriveInfo(root);
            if (drive.IsReady)
            {
                free = drive.AvailableFreeSpace;
                total = drive.TotalSize;
            }
        }
        catch
        {
            // ignore
        }

        return new BackupDestinationStatus
        {
            UseUsbTarget = false,
            IsReady = true,
            Message = $"Папка на этом ПК:\n{backupBase}",
            BackupBasePath = backupBase,
            FreeBytes = free,
            FreeSpaceText = UsbService.FormatBytes(free),
            TotalSpaceText = UsbService.FormatBytes(total)
        };
    }

    private static string NormalizeSubfolder(string? rootFolder) =>
        string.IsNullOrWhiteSpace(rootFolder) ? "Backups" : rootFolder.Trim();
}
