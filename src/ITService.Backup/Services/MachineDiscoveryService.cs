using ITService.Backup.Models;

namespace ITService.Backup.Services;

/// <summary>Автоопределение SQL и 1С (параллельно). Стартовый поиск — один раз за сессию приложения.</summary>
public sealed class MachineDiscoveryService
{
    private static bool s_startupDiscoveryCompleted;

    public static MachineDiscoveryOptions GetStartupOptions(AppConfig config) => new()
    {
        EnableNewSqlDatabases = !config.Ui.InitialDiscoveryCompleted,
        LocalProbeTimeoutSeconds = 2,
        ConfiguredConnectTimeoutSeconds = 3
    };

    private readonly SqlConfigSyncService _sqlSync = new();
    private readonly OneCConfigSyncService _oneCSync = new();

    public static bool IsStartupDiscoveryCompleted => s_startupDiscoveryCompleted;

    public static void MarkStartupDiscoveryCompleted() => s_startupDiscoveryCompleted = true;

    public async Task<(SqlConfigSyncResult Sql, OneCConfigSyncResult OneC)> DiscoverAsync(
        AppConfig config,
        MachineDiscoveryOptions options,
        CancellationToken ct = default)
    {
        var sqlTask = _sqlSync.SyncAsync(config.SqlServer, options, ct);
        var oneCTask = Task.Run(() => _oneCSync.Sync(config, options), ct);
        await Task.WhenAll(sqlTask, oneCTask).ConfigureAwait(false);
        return (await sqlTask.ConfigureAwait(false), await oneCTask.ConfigureAwait(false));
    }
}
