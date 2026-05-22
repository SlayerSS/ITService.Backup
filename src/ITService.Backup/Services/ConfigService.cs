using System.Text.Json;
using System.Text.Json.Serialization;
using ITService.Backup.Helpers;
using ITService.Backup.Models;

namespace ITService.Backup.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AppConfig Load()
    {
        AppPaths.EnsureDataDirectory();
        if (!File.Exists(AppPaths.ConfigFile))
        {
            var defaults = CreateDefault();
            Save(defaults);
            return defaults;
        }

        var json = File.ReadAllText(AppPaths.ConfigFile);
        var config = Normalize(JsonSerializer.Deserialize<AppConfig>(json, JsonOptions));
        if (!json.Contains("autoAddOneCDatabases", StringComparison.OrdinalIgnoreCase))
            config.Ui.AutoAddOneCDatabases = true;
        if (MigrateLegacyInitialDiscovery(config))
            Save(config);
        return config;
    }

    public void Save(AppConfig config)
    {
        EnsureConfiguredItemsEnabled(Normalize(config));
        AppPaths.EnsureDataDirectory();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(AppPaths.ConfigFile, json);
    }

    public UserSelection LoadSelection()
    {
        AppPaths.EnsureDataDirectory();
        if (!File.Exists(AppPaths.SelectionFile))
            return new UserSelection();

        var json = File.ReadAllText(AppPaths.SelectionFile);
        return JsonSerializer.Deserialize<UserSelection>(json, JsonOptions) ?? new UserSelection();
    }

    public void SaveSelection(UserSelection selection)
    {
        AppPaths.EnsureDataDirectory();
        var json = JsonSerializer.Serialize(selection, JsonOptions);
        File.WriteAllText(AppPaths.SelectionFile, json);
    }

    private static AppConfig CreateDefault() => Normalize(new AppConfig
    {
        SqlServer = new SqlServerSettings
        {
            UseLocalServer = true,
            Instance = "",
            UseWindowsAuth = true,
            ExcludeTransactionLogs = true,
            UseCompression = true
        },
        Usb = new UsbSettings { DriveLetter = "", RootFolder = "Backups" }
    });

    private static AppConfig Normalize(AppConfig? config)
    {
        config ??= new AppConfig();
        config.BackupTarget ??= new BackupTargetSettings();
        if (!config.BackupTarget.UseUsbTarget)
            config.BackupTarget.RemovableOnly = false;
        var localPath = config.BackupTarget.LocalFolderPath?.Trim() ?? "";
        if (!config.BackupTarget.RemovableOnly
            && !string.IsNullOrEmpty(localPath)
            && config.BackupTarget.UseUsbTarget)
        {
            config.BackupTarget.UseUsbTarget = false;
            config.BackupTarget.LocalFolderPath = localPath;
            config.Usb.DriveLetter = "";
        }
        config.Usb ??= new UsbSettings();
        config.Usb.DriveLetter = DriveLetterHelper.Normalize(config.Usb.DriveLetter);
        if (!string.IsNullOrWhiteSpace(config.Usb.DriveLetter) && !config.Usb.PickDriveManually)
            config.Usb.PickDriveManually = !config.Usb.AutoDetectRemovable;
        config.SqlServer ??= new SqlServerSettings();
        if (!config.SqlServer.UseLocalServer && string.IsNullOrWhiteSpace(config.SqlServer.Instance))
            config.SqlServer.UseLocalServer = true;
        config.Ui ??= new UiSettings();
        config.Folders ??= [];
        config.Files ??= [];
        EnsureConfiguredItemsEnabled(config);
        return config;
    }

    /// <summary>В настройках только список источников; выбор для копии — на главном экране.</summary>
    public static void EnsureConfiguredItemsEnabled(AppConfig config)
    {
        foreach (var folder in config.Folders)
            folder.Enabled = true;
        foreach (var db in config.SqlServer.Databases)
            db.Enabled = true;
        foreach (var file in config.Files)
            file.Enabled = true;
    }

    /// <summary>Старые config.json без InitialDiscoveryCompleted.</summary>
    private static bool MigrateLegacyInitialDiscovery(AppConfig config)
    {
        if (config.Ui.InitialDiscoveryCompleted)
            return false;

        var hasItems = config.Folders.Count > 0 || config.SqlServer.Databases.Count > 0;
        if (!hasItems)
            return false;

        config.Ui.InitialDiscoveryCompleted = true;
        return true;
    }
}
