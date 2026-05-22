using ITService.Backup.Models;

namespace ITService.Backup.Helpers;

public static class UsbSettingsHelper
{
    /// <summary>Ждём только букву из настроек, без авто-переключения на другие диски.</summary>
    public static bool ExpectsSpecificDriveLetter(UsbSettings? usb) =>
        usb is { PickDriveManually: true } or { AutoDetectRemovable: false };
}
