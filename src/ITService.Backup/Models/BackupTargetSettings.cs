namespace ITService.Backup.Models;

public sealed class BackupTargetSettings
{
    /// <summary>Только съёмные USB. Если false — можно выбрать другой диск или папку.</summary>
    public bool RemovableOnly { get; set; } = true;

    /// <summary>Запись на диск (USB/локальный). Если false — <see cref="LocalFolderPath"/>.</summary>
    public bool UseUsbTarget { get; set; } = true;

    /// <summary>Корневая папка для архивов на этом ПК (подпапка <see cref="UsbSettings.RootFolder"/> добавляется автоматически).</summary>
    public string LocalFolderPath { get; set; } = "";
}
