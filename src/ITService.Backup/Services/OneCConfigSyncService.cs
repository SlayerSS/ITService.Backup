using ITService.Backup.Models;

namespace ITService.Backup.Services;

/// <summary>Автодобавление файловых путей 1С из ibases.v8i / реестра в конфиг.</summary>
public sealed class OneCConfigSyncService
{
    private readonly OneCPathImportService _import = new();

    public OneCConfigSyncResult Sync(AppConfig config) =>
        Sync(config, new MachineDiscoveryOptions());

    public OneCConfigSyncResult Sync(AppConfig config, MachineDiscoveryOptions options)
    {
        if (!config.Ui.AutoAddOneCDatabases)
        {
            return new OneCConfigSyncResult
            {
                Message = "1С: поиск путей при запуске отключён в настройках"
            };
        }

        var discovered = _import.DiscoverForCurrentUser();
        var filePaths = discovered.Where(p => p.Kind == OneCPathKind.File).ToList();
        var newFolderIds = new List<string>();
        var newCount = OneCPathImportService.MergeFileFolders(
            config.Folders, discovered, newFolderIds: newFolderIds);
        var listCount = config.Folders.Count(f => f.Has1CDatabase);

        var parts = new List<string>();
        if (filePaths.Count > 0)
            parts.Add($"путей 1С (файловые): {filePaths.Count}");
        if (newCount > 0)
            parts.Add($"добавлено: {newCount}");
        if (listCount > 0 && newCount == 0 && filePaths.Count > 0)
            parts.Add($"в списке: {listCount}");

        var message = parts.Count > 0
            ? "1С: " + string.Join(", ", parts)
            : "1С: файловые базы в списке пользователя не найдены";

        return new OneCConfigSyncResult
        {
            ConfigChanged = newCount > 0,
            NewFolders = newCount,
            NewFolderIds = newFolderIds,
            FilePathCount = filePaths.Count,
            Message = message
        };
    }
}
