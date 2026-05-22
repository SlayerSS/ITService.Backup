using ITService.Backup.Helpers;
using ITService.Backup.Models;

namespace ITService.Backup.Services;

public sealed class UsbService
{
    /// <summary>
    /// Авто: только диски Windows с типом «Съёмный» (Removable).
    /// Вручную: буква из настроек (для конкретного клиента), без привязки к E:.
    /// </summary>
    public UsbDriveStatus GetDriveStatus(UsbSettings usb, bool removableOnly) =>
        UsbSettingsHelper.ExpectsSpecificDriveLetter(usb)
            ? GetManualLetterStatus(usb, removableOnly)
            : GetAutoDetectedStatus(usb, removableOnly);

    public UsbDriveStatus GetDriveStatus(UsbSettings usb) =>
        GetDriveStatus(usb, removableOnly: true);

    public bool IsDriveReady(UsbSettings usb, out string message)
    {
        var status = GetDriveStatus(usb);
        message = status.Message;
        return status.IsReady;
    }

    public string GetDriveRoot(UsbSettings usb)
    {
        var status = GetDriveStatus(usb);
        return status.IsReady ? status.RootPath : GetDriveRootForLetter(usb.DriveLetter);
    }

    public long GetFreeBytes(UsbSettings usb) => GetDriveStatus(usb).FreeBytes;

    public static List<DriveInfo> DetectRemovableDrives() =>
        DetectBackupDrives(removableOnly: true);

    public static List<DriveInfo> DetectBackupDrives(bool removableOnly)
    {
        var result = new List<DriveInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is DriveType.CDRom or DriveType.Unknown)
                    continue;

                if (removableOnly && drive.DriveType != DriveType.Removable)
                    continue;

                if (!TryProbeDrive(drive, out _, out _, out _, out var totalBytes, out var rootPath))
                    continue;

                if (totalBytes <= 0 || BackupSafety.IsSystemDrive(rootPath))
                    continue;

                result.Add(drive);
            }
            catch
            {
                // skip
            }
        }

        return result.OrderBy(d => DriveLetterHelper.Normalize(d.Name), StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Безопасно читает метку и размер — диск мог отключиться между проверками.</summary>
    public static bool TryProbeDrive(
        DriveInfo drive,
        out string letter,
        out string? volumeLabel,
        out long freeBytes,
        out long totalBytes,
        out string rootPath)
    {
        letter = "";
        volumeLabel = null;
        freeBytes = 0;
        totalBytes = 0;
        rootPath = "";

        try
        {
            if (!drive.IsReady)
                return false;

            letter = DriveLetterHelper.Normalize(drive.Name);
            if (string.IsNullOrEmpty(letter))
                return false;

            rootPath = Path.GetFullPath(drive.Name);
            volumeLabel = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel.Trim();
            freeBytes = drive.AvailableFreeSpace;
            totalBytes = drive.TotalSize;
            return true;
        }
        catch (Exception ex) when (IsDriveAccessException(ex))
        {
            return false;
        }
    }

    public static string FormatDriveComboLabel(DriveInfo drive)
    {
        if (!TryProbeDrive(drive, out var letter, out var volumeLabel, out _, out _, out _))
            return "";

        var type = DescribeDriveType(drive.DriveType);
        return string.IsNullOrWhiteSpace(volumeLabel)
            ? $"{letter}: ({type})"
            : $"{letter}: {volumeLabel} ({type})";
    }

    private static string DescribeDriveType(DriveType type) => type switch
    {
        DriveType.Removable => "съёмный",
        DriveType.Fixed => "локальный",
        DriveType.Network => "сеть",
        _ => "диск"
    };

    private static bool IsDriveAccessException(Exception ex) =>
        ex is DriveNotFoundException or IOException or UnauthorizedAccessException;

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "—";
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        double size = bytes;
        var i = 0;
        while (size >= 1024 && i < units.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return i == 0 ? $"{bytes:0} {units[i]}" : $"{size:0.##} {units[i]}";
    }

    private static UsbDriveStatus GetAutoDetectedStatus(UsbSettings usb, bool removableOnly)
    {
        var detected = DetectBackupDrives(removableOnly);
        if (detected.Count == 0)
        {
            return new UsbDriveStatus
            {
                IsReady = false,
                Message = removableOnly
                    ? "Съёмный диск не найден. Вставьте USB-накопитель."
                    : "Готовый диск не найден. Подключите накопитель или укажите папку."
            };
        }

        // Авто: буква из конфига — приоритет при переключении нескольких флешек на главном экране.
        var preferred = DriveLetterHelper.Normalize(usb.DriveLetter);
        foreach (var drive in OrderDrivesForSelection(detected, preferred))
        {
            if (TryBuildReadyStatus(drive, autoDetected: true, isManualSelection: false, out var status))
                return status;
        }

        return new UsbDriveStatus
        {
            IsReady = false,
            Message = removableOnly
                ? "Съёмный диск не найден. Вставьте USB-накопитель."
                : "Готовый диск не найден. Подключите накопитель или укажите папку."
        };
    }

    private static UsbDriveStatus GetManualLetterStatus(UsbSettings usb, bool removableOnly)
    {
        var letter = DriveLetterHelper.Normalize(usb.DriveLetter);
        if (string.IsNullOrEmpty(letter))
        {
            return new UsbDriveStatus
            {
                IsReady = false,
                IsManualSelection = true,
                Message = "Выберите диск в настройках (вкладка «Носитель»)."
            };
        }

        // Ручной выбор: ждём только эту букву, даже если Windows видит диск как «локальный».
        var drive = DriveLetterHelper.TryGetDrive(letter, requireRemovable: false);
        if (drive == null)
        {
            return ManualNotReady(letter, disconnected: true);
        }

        var root = Path.GetFullPath(drive.Name);
        if (BackupSafety.IsSystemDrive(root))
        {
            return new UsbDriveStatus
            {
                IsReady = false,
                DriveLetter = letter,
                IsManualSelection = true,
                Message = "Запись на диск C: запрещена."
            };
        }

        if (!TryBuildReadyStatus(drive, autoDetected: false, isManualSelection: true, out var ready))
            return ManualNotReady(letter, disconnected: true);

        return ready;
    }

    private static UsbDriveStatus ManualNotReady(string letter, bool disconnected)
    {
        var detail = disconnected
            ? "не подключён"
            : "недоступен";
        return new UsbDriveStatus
        {
            IsReady = false,
            DriveLetter = letter,
            IsManualSelection = true,
            Message = $"Диск {letter}: {detail}. Ожидается только диск, выбранный в настройках."
        };
    }

    private static IEnumerable<DriveInfo> OrderDrivesForSelection(List<DriveInfo> drives, string preferred)
    {
        return drives
            .OrderByDescending(d =>
                !string.IsNullOrEmpty(preferred)
                && string.Equals(DriveLetterHelper.Normalize(d.Name), preferred, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(d => TryProbeDrive(d, out _, out _, out var free, out _, out _) ? free : 0);
    }

    private static bool TryBuildReadyStatus(
        DriveInfo drive,
        bool autoDetected,
        bool isManualSelection,
        out UsbDriveStatus status)
    {
        if (!TryProbeDrive(drive, out var letter, out var label, out var freeBytes, out var totalBytes, out var rootPath))
        {
            status = default!;
            return false;
        }

        var kind = drive.DriveType == DriveType.Removable ? "Съёмный диск" : "Диск";
        var readyNote = isManualSelection
            ? " — выбран в настройках, подключён"
            : " — подключён";

        status = new UsbDriveStatus
        {
            IsReady = true,
            DriveLetter = letter,
            RootPath = rootPath,
            VolumeLabel = label,
            FreeBytes = freeBytes,
            TotalBytes = totalBytes,
            AutoDetected = autoDetected,
            IsManualSelection = isManualSelection,
            Message = string.IsNullOrWhiteSpace(label)
                ? $"{kind} {letter}:{readyNote}"
                : $"{kind} {letter}: «{label}»{readyNote}"
        };
        return true;
    }

    private static string GetDriveRootForLetter(string letter) =>
        DriveLetterHelper.ToRootPath(letter);
}
